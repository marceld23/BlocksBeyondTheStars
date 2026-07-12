// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Maintenance announcements: operator/admin messages ("server restarts in 10 minutes") broadcast to all
/// players as a prominent client banner, plus an optional restart countdown that ends in the same graceful
/// <see cref="RequestStop"/> drain the idle shutdown uses. Announcements arrive from two directions — the
/// in-game admin commands (tick thread) and the instance HTTP gateway's <c>POST /announce</c> (accept-loop
/// thread) — so intake goes through a lock-guarded pending slot that the tick applies.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Countdown re-broadcast marks (seconds remaining). Each crossing re-sends the notice with the
    /// authoritative remaining time so late joiners and drifted client timers re-sync.</summary>
    private static readonly int[] MaintenanceThresholds = { 600, 300, 120, 60, 30, 10 };

    private const int MaxMaintenanceTextLength = 200;
    private const int MaxMaintenanceRestartMinutes = 180;

    /// <summary>Grace between the final "restarting now" broadcast and the actual stop so the packet still
    /// leaves the transport before it shuts down.</summary>
    private const double MaintenanceStopFlushSeconds = 2.0;

    private readonly Lock _maintenanceGate = new();
    private MaintenanceNotice? _maintenancePending;

    private double _maintenanceRemaining = -1; // < 0 = no countdown active
    private string _maintenanceText = string.Empty;
    private int _maintenanceThresholdIndex;
    private bool _maintenanceFinalSent;
    private double _maintenanceFlushRemaining;

    /// <summary>True once a maintenance countdown reached zero and the server is draining to stop.
    /// Latched, like <see cref="IdleShutdownTriggered"/>.</summary>
    public bool MaintenanceStopTriggered { get; private set; }

    /// <summary>Seconds left on the active restart countdown, or -1 when none is running.</summary>
    public int MaintenanceSecondsRemaining
        => _maintenanceRemaining < 0 ? -1 : (int)System.Math.Ceiling(_maintenanceRemaining);

    /// <summary>
    /// Queues a maintenance announcement for the next tick. Safe to call from any thread (the HTTP gateway's
    /// accept loop uses it). Returns false when the request is invalid; nothing is queued then.
    /// Kinds: <see cref="MaintenanceNotice.KindInfo"/> broadcasts a one-off message,
    /// <see cref="MaintenanceNotice.KindRestartCountdown"/> starts/replaces a countdown of
    /// <paramref name="seconds"/>, <see cref="MaintenanceNotice.KindCancelled"/> clears a running countdown.
    /// </summary>
    public bool EnqueueMaintenance(byte kind, string? text, int seconds)
    {
        string clean = StripControlChars(text).Trim();
        if (clean.Length > MaxMaintenanceTextLength)
        {
            clean = clean[..MaxMaintenanceTextLength];
        }

        switch (kind)
        {
            case MaintenanceNotice.KindInfo when clean.Length == 0:
                return false;
            case MaintenanceNotice.KindRestartCountdown
                when seconds < 1 || seconds > MaxMaintenanceRestartMinutes * 60:
                return false;
            case MaintenanceNotice.KindInfo:
            case MaintenanceNotice.KindRestartCountdown:
            case MaintenanceNotice.KindCancelled:
                break;
            default:
                return false;
        }

        lock (_maintenanceGate)
        {
            _maintenancePending = new MaintenanceNotice { Kind = kind, Text = clean, SecondsRemaining = seconds };
        }

        return true;
    }

    /// <summary>The notice a mid-countdown joiner must receive so their banner starts in sync, or null.</summary>
    internal MaintenanceNotice? BuildActiveMaintenanceNotice()
    {
        if (_maintenanceRemaining < 0)
        {
            return null;
        }

        return new MaintenanceNotice
        {
            Kind = MaintenanceNotice.KindRestartCountdown,
            MessageKey = "ui.maint.restart_in",
            Text = _maintenanceText,
            SecondsRemaining = System.Math.Max(0, MaintenanceSecondsRemaining),
        };
    }

    /// <summary>Per-tick maintenance bookkeeping: applies queued announcements, re-broadcasts the countdown
    /// at the threshold marks, and turns a finished countdown into a graceful stop.</summary>
    private void TickMaintenance(double deltaSeconds)
    {
        MaintenanceNotice? pending;
        lock (_maintenanceGate)
        {
            pending = _maintenancePending;
            _maintenancePending = null;
        }

        if (pending is not null)
        {
            ApplyMaintenance(pending);
        }

        // Flush phase: the final "restarting now" is out (remaining has gone negative — this MUST be
        // checked before the inactive early-out below or the stop would never fire); give the packet a
        // moment to leave the transport, then stop.
        if (_maintenanceFinalSent)
        {
            _maintenanceFlushRemaining -= deltaSeconds;
            if (_maintenanceFlushRemaining <= 0 && !MaintenanceStopTriggered)
            {
                MaintenanceStopTriggered = true;
                _log.Info("Maintenance countdown finished — stopping (world is saved on the way down).");
                RequestStop();
            }

            return;
        }

        if (_maintenanceRemaining < 0)
        {
            return;
        }

        _maintenanceRemaining -= deltaSeconds;

        if (_maintenanceRemaining <= 0)
        {
            _maintenanceFinalSent = true;
            _maintenanceFlushRemaining = MaintenanceStopFlushSeconds;
            Broadcast(new MaintenanceNotice
            {
                Kind = MaintenanceNotice.KindRestartCountdown,
                MessageKey = "ui.maint.restarting_now",
                Text = _maintenanceText,
                SecondsRemaining = 0,
            });
            return;
        }

        if (_maintenanceThresholdIndex < MaintenanceThresholds.Length
            && _maintenanceRemaining <= MaintenanceThresholds[_maintenanceThresholdIndex])
        {
            // Skip every mark the countdown already passed (short countdowns start below the top marks).
            while (_maintenanceThresholdIndex < MaintenanceThresholds.Length
                && _maintenanceRemaining <= MaintenanceThresholds[_maintenanceThresholdIndex])
            {
                _maintenanceThresholdIndex++;
            }

            Broadcast(BuildActiveMaintenanceNotice()!);
        }
    }

    private void ApplyMaintenance(MaintenanceNotice pending)
    {
        switch (pending.Kind)
        {
            case MaintenanceNotice.KindInfo:
                _log.Info($"Maintenance announcement: {pending.Text}");
                Broadcast(new MaintenanceNotice
                {
                    Kind = MaintenanceNotice.KindInfo,
                    Text = pending.Text,
                    SecondsRemaining = -1,
                });
                break;

            case MaintenanceNotice.KindRestartCountdown:
                _maintenanceRemaining = pending.SecondsRemaining;
                _maintenanceText = pending.Text;
                _maintenanceFinalSent = false;
                _maintenanceThresholdIndex = 0;
                while (_maintenanceThresholdIndex < MaintenanceThresholds.Length
                    && MaintenanceThresholds[_maintenanceThresholdIndex] >= pending.SecondsRemaining)
                {
                    _maintenanceThresholdIndex++;
                }

                _log.Info($"Maintenance restart scheduled in {pending.SecondsRemaining} s. {pending.Text}");
                Broadcast(BuildActiveMaintenanceNotice()!);
                break;

            case MaintenanceNotice.KindCancelled:
                if (_maintenanceRemaining >= 0 && !_maintenanceFinalSent)
                {
                    _maintenanceRemaining = -1;
                    _maintenanceText = string.Empty;
                    _log.Info("Maintenance restart cancelled.");
                    Broadcast(new MaintenanceNotice
                    {
                        Kind = MaintenanceNotice.KindCancelled,
                        MessageKey = "ui.maint.cancelled",
                        SecondsRemaining = -1,
                    });
                }

                break;
        }
    }
}
