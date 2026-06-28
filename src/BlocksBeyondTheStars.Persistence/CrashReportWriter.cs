// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Diagnostics;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>
/// Writes one self-contained crash report per unexpected server exception to a local <c>crashreports/</c>
/// folder, as the durable, endpoint-independent record of a fault. It is deliberately the source of truth
/// for the (future) uploader: a report is ALWAYS written to disk first — that works even when the process
/// is terminating or offline, when an HTTP POST cannot — and the sender later marks it sent. Until a sender
/// exists, the files simply accumulate so a player can attach them to a bug report by hand.
///
/// Every method is best-effort and NEVER throws: a crash reporter that itself crashes would be worse than
/// useless. Fed from <c>GameServer.RecordTickFault</c> (contained tick faults) and the process-wide
/// <c>AppDomain.UnhandledException</c> / <c>TaskScheduler.UnobservedTaskException</c> handlers in the host.
/// </summary>
public sealed class CrashReportWriter
{
    /// <summary>On-disk schema version, so a later reader/uploader can migrate older files.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Description cap matching the website endpoint (it rejects longer); the full stack rides in
    /// reportJson instead, so nothing is lost.</summary>
    public const int MaxDescriptionLength = 5000;

    /// <summary>Prefix every report file shares; also the glob used to find unsent reports.</summary>
    private const string FilePrefix = "crash_";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly string _worldName;
    private readonly string _version;
    private readonly object _gate = new();
    private int _seq;

    /// <param name="directory">Folder reports are written to (created on demand). Empty disables writing.</param>
    /// <param name="worldName">World tag baked into the file name + payload (kept stable across a run).</param>
    /// <param name="version">Server build/version string, recorded so a report identifies its binary.</param>
    public CrashReportWriter(string directory, string worldName, string version)
    {
        _directory = directory ?? string.Empty;
        _worldName = string.IsNullOrWhiteSpace(worldName) ? "world" : worldName;
        _version = version ?? string.Empty;
    }

    /// <summary>The folder reports land in (for a startup hint that points the player at unsent files).</summary>
    public string DirectoryPath => _directory;

    /// <summary>Writes one crash report. Returns the file path on success, or null when nothing was written
    /// (no directory configured, a null exception, or a write failure — all swallowed). Never throws.</summary>
    /// <param name="kind">Coarse origin: <c>tick-fault</c>, <c>unhandled-exception</c>, <c>unobserved-task</c>.</param>
    /// <param name="system">The tick subsystem that threw (e.g. <c>TickCreatures</c>), or null off the tick path.</param>
    /// <param name="ex">The exception to capture (type, message, full stack).</param>
    /// <param name="uptimeSeconds">Server uptime when it happened, when known.</param>
    public string? Write(string kind, string? system, Exception ex, double? uptimeSeconds = null)
    {
        if (ex is null || string.IsNullOrEmpty(_directory))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_directory);

            int n;
            lock (_gate)
            {
                n = ++_seq;
            }

            string stem = $"{FilePrefix}{Sanitize(_worldName)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{n:D3}";

            // Shaped to match the website's bug-report endpoint (post_bugreport) so server crashes flow through
            // the SAME function + collection as player feedback and client crashes — no separate endpoint. The
            // crash-native detail (system, exception type, full stack) rides in reportJson, which the backend
            // stores verbatim; the full stack lives there because the endpoint caps `description` at 5000 chars.
            string exceptionType = ex.GetType().FullName ?? ex.GetType().Name;
            // Scrub PII (OS user name in paths, e-mails) out of the free text before it touches disk or the wire.
            string message = CrashPiiScrubber.Scrub(ex.Message);
            string stack = CrashPiiScrubber.Scrub(ex.ToString());
            var report = new
            {
                title = Truncate($"Server crash [{kind}]{(string.IsNullOrEmpty(system) ? string.Empty : " " + system)}: {ex.GetType().Name}", 110),
                description = Truncate($"{exceptionType}: {message}\n\n{stack}", MaxDescriptionLength),
                email = string.Empty,
                gameVersion = _version,
                buildNumber = string.Empty,
                playerId = string.Empty,
                playerName = string.Empty,
                sessionId = string.Empty,
                platform = "server",
                clientTimestamp = DateTime.UtcNow.ToString("o"),
                reportJson = new
                {
                    schemaVersion = SchemaVersion,
                    kind,
                    source = "server",
                    system,
                    world = _worldName,
                    serverVersion = _version,
                    uptimeSeconds,
                    exceptionType,
                    message,
                    stackTrace = stack,
                },
            };

            string file = Path.Combine(_directory, stem + ".json");
            File.WriteAllText(file, JsonSerializer.Serialize(report, JsonOptions));
            return file;
        }
        catch
        {
            return null; // a crash reporter must never throw
        }
    }

    /// <summary>The report files currently on disk awaiting upload (best-effort; empty on any error). The
    /// uploader will remove/relocate a file once it is accepted, so what remains here is the retry queue.</summary>
    public IReadOnlyList<string> ListPending()
    {
        if (string.IsNullOrEmpty(_directory) || !Directory.Exists(_directory))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.GetFiles(_directory, FilePrefix + "*.json");
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>How many unsent reports are queued on disk (for a startup hint). Never throws.</summary>
    public int CountPending() => ListPending().Count;

    /// <summary>Sends queued reports through <paramref name="sink"/> and relocates each accepted file into a
    /// <c>sent/</c> subfolder (so it is never re-sent but stays for the dev to inspect). Stops at the first
    /// failure — a down/offline endpoint means the rest stay queued for the next attempt — and at
    /// <paramref name="maxPerFlush"/> so one flush can't block on a huge backlog. Returns the number sent.
    /// Best-effort and never throws; safe to call on a background thread while new reports are being written
    /// (each file is independent and accepted files are moved, not deleted out from under a writer).</summary>
    public int FlushPending(ICrashReportSink sink, int maxPerFlush = 20)
    {
        if (sink is null || !sink.IsConfigured)
        {
            return 0;
        }

        int sent = 0;
        foreach (var path in ListPending())
        {
            if (sent >= maxPerFlush)
            {
                break;
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch
            {
                continue; // mid-write or locked — skip it this round, retry next flush
            }

            if (!sink.Send(json))
            {
                break; // endpoint rejected/unreachable — leave this and the rest queued
            }

            MarkSent(path);
            sent++;
        }

        return sent;
    }

    /// <summary>Moves an accepted report into the <c>sent/</c> subfolder so <see cref="ListPending"/> (which
    /// only scans the top level) no longer returns it. Best-effort; never throws.</summary>
    private void MarkSent(string path)
    {
        try
        {
            string sentDir = Path.Combine(_directory, "sent");
            Directory.CreateDirectory(sentDir);
            File.Move(path, Path.Combine(sentDir, Path.GetFileName(path)), overwrite: true);
        }
        catch
        {
            // best-effort: if the move fails the file stays pending and is retried (harmless re-send)
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max);

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }
}
