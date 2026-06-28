// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Persistence;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Tick resilience: the authoritative loop is single-threaded, so before this an unexpected exception in
/// ANY simulation system (creatures, fluids, weather, AI, …) propagated straight out of <c>Run()</c>,
/// skipped the shutdown save and crashed the whole dedicated server — dropping every connected player and
/// losing unsaved progress. Message handlers were already contained per-message (see <c>OnPayload</c>); the
/// simulation systems were not. <see cref="Guard(string, Action)"/> closes that gap: a throwing system is
/// logged (throttled per system) and skipped for this tick, and the server keeps simulating.
///
/// This is also the single choke point a future crash-report uploader / local crash-dump writer hooks into
/// (see <see cref="RecordTickFault"/>) — one place that learns about every contained server fault.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A system that throws every tick is logged at most once per this window (with an occurrence
    /// count), so a persistent fault cannot flood the log thousands of times a minute. The first failure of
    /// each distinct system is always logged immediately.</summary>
    private const double TickFaultLogThrottleSeconds = 30.0;

    private readonly Dictionary<string, TickFaultState> _tickFaults = new();

    /// <summary>Per-system rolling fault counter used to throttle the error log.</summary>
    private sealed class TickFaultState
    {
        /// <summary>Failures accumulated since the last log line for this system.</summary>
        public int Count;

        /// <summary>Server uptime (seconds) at or after which this system may log again.</summary>
        public double NextLogAt;
    }

    /// <summary>Runs one no-argument tick subsystem and contains any exception it throws. Returns true when
    /// the body completed without throwing. Pass a method group (e.g. <c>Guard("StreamChunks", StreamChunks)</c>)
    /// so the delegate is cached and the guard allocates nothing on the hot path.</summary>
    private bool Guard(string system, Action body)
    {
        try
        {
            body();
            return true;
        }
        catch (Exception ex)
        {
            RecordTickFault(system, ex);
            return false;
        }
    }

    /// <summary>Runs one delta-time tick subsystem and contains any exception it throws. Returns true when the
    /// body completed without throwing. Pass a method group (e.g. <c>Guard("TickFluids", dt, TickFluids)</c>):
    /// the method-group conversion to <see cref="Action{Double}"/> is cached by the compiler, so no delegate
    /// is allocated per tick.</summary>
    private bool Guard(string system, double dt, Action<double> body)
    {
        try
        {
            body(dt);
            return true;
        }
        catch (Exception ex)
        {
            RecordTickFault(system, ex);
            return false;
        }
    }

    /// <summary>Records a contained tick fault: logs the full exception (first occurrence, then at most once
    /// per <see cref="TickFaultLogThrottleSeconds"/> with the count of suppressed repeats). Uses the existing
    /// <see cref="_uptime"/> clock so it needs no wall-clock access. The future crash reporter extends THIS
    /// method (forward to the website and/or write a local crash file) — keep it the only fault sink.</summary>
    private void RecordTickFault(string system, Exception ex)
    {
        if (!_tickFaults.TryGetValue(system, out var state))
        {
            state = new TickFaultState();
            _tickFaults[system] = state;
        }

        state.Count++;

        if (_uptime < state.NextLogAt)
        {
            return; // within the throttle window — keep counting, stay quiet
        }

        if (state.Count == 1)
        {
            _log.Error($"Tick system '{system}' threw and was contained: {ex}");
        }
        else
        {
            _log.Error($"Tick system '{system}' threw {state.Count}× in the last {TickFaultLogThrottleSeconds:0}s; most recent: {ex}");
        }

        // Persist a durable report alongside the log line (same throttle gate, so a per-tick fault can't fill
        // the disk). This is the endpoint-independent record the future uploader will pick up.
        TryWriteCrashReport("tick-fault", system, ex);

        state.Count = 0;
        state.NextLogAt = _uptime + TickFaultLogThrottleSeconds;
    }

    /// <summary>How often (seconds of sim time) the tick kicks a background upload of queued crash reports.
    /// Coarse on purpose — sending is best-effort catch-up, not real-time.</summary>
    private const double CrashFlushIntervalSeconds = 60.0;

    private double _sinceCrashFlush;
    private int _crashFlushRunning; // 0/1 guard so only one background flush runs at a time (Interlocked)

    /// <summary>The optional uploader for automatic crash sending, set by the host once it has read config.
    /// Null / not-configured ⇒ reports just accumulate on disk (the source of truth) for a manual send.</summary>
    internal ICrashReportSink? CrashUploader { get; set; }

    /// <summary>Once per <see cref="CrashFlushIntervalSeconds"/>, hands the on-disk queue to a background task
    /// so live faults reach the website within the same session — WITHOUT blocking the single-threaded tick on
    /// network I/O. A single flush runs at a time; the writer relocates accepted files so the tick can keep
    /// writing new ones concurrently. No-op until an uploader is configured.</summary>
    private void MaybeFlushCrashReports(double dt)
    {
        var sink = CrashUploader;
        var writer = _crashWriter;
        if (sink is null || !sink.IsConfigured || writer is null)
        {
            return;
        }

        _sinceCrashFlush += dt;
        if (_sinceCrashFlush < CrashFlushIntervalSeconds)
        {
            return;
        }

        _sinceCrashFlush = 0;
        if (Interlocked.CompareExchange(ref _crashFlushRunning, 1, 0) != 0)
        {
            return; // a previous flush is still in flight
        }

        _ = Task.Run(() =>
        {
            try
            {
                writer.FlushPending(sink);
            }
            catch
            {
                // best-effort: never let a background send fault the server
            }
            finally
            {
                Volatile.Write(ref _crashFlushRunning, 0);
            }
        });
    }

    /// <summary>Writes a crash report for a contained fault, swallowing anything that goes wrong (including a
    /// throwing <see cref="CrashWriter"/> lookup) — the fault sink itself must never throw.</summary>
    private void TryWriteCrashReport(string kind, string? system, Exception ex)
    {
        try
        {
            CrashWriter.Write(kind, system, ex, _uptime);
        }
        catch
        {
            // best-effort: a failure to record a fault must not turn into a second fault
        }
    }
}
