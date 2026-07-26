// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Hosted-worlds lifecycle: idle shutdown and the live status snapshot. A control plane that runs one
/// server per world needs two things from the instance itself — "stop yourself when nobody plays" (so
/// sleeping worlds cost nothing) and "tell me who is on" (the admin API only sees persisted rows, not live
/// sessions). Both are inert unless configured/wired: idle shutdown requires
/// <see cref="Shared.Configuration.ServerConfig.IdleShutdownMinutes"/> &gt; 0, and the snapshot is only
/// served when the host exposes <see cref="StatusJson"/> (the WebSocket gateway's <c>/status</c>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Rebuilding the snapshot every tick would be wasted JSON churn at 15 Hz; once a second is
    /// plenty for a control plane polling on the order of tens of seconds.</summary>
    private const double StatusPublishIntervalSeconds = 1.0;

    private long _lastActiveUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private double _idleSeconds;
    private double _sinceStatusPublish = double.MaxValue; // publish on the first tick
    private volatile string? _statusJson;

    /// <summary>True once the idle timeout has fired and the server is draining to stop. Latched (never
    /// resets) — by the time it is set the shutdown is already underway.</summary>
    public bool IdleShutdownTriggered { get; private set; }

    /// <summary>Current status snapshot as JSON — built on the tick thread, safe to read from any thread
    /// (the WebSocket gateway serves it on its accept loop). "{}" until the first tick publishes.</summary>
    public string StatusJson => _statusJson ?? "{}";

    /// <summary>Per-tick lifecycle bookkeeping: tracks idle time, fires the idle shutdown, and republishes
    /// the status snapshot. Runs under a Guard like every other tick system.</summary>
    private void TickHostedLifecycle(double deltaSeconds)
    {
        // Two different counts on purpose (issue #487). The REPORTED number is real players only — an operator
        // watching a world is not "someone playing", and the fleet panel would otherwise show a busy world that
        // nobody is in. The IDLE timer, however, counts any live session: shutting the world down under the
        // admin who is currently walking through it would be its own kind of bug.
        int joined = JoinedPlayerCount();
        bool anySession = false;
        foreach (var s in _sessions.Values)
        {
            if (s.Joined)
            {
                anySession = true;
                break;
            }
        }

        if (anySession)
        {
            _idleSeconds = 0;
            _lastActiveUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        else
        {
            _idleSeconds += deltaSeconds;
        }

        if (_config.IdleShutdownMinutes > 0 && !anySession && !IdleShutdownTriggered
            && _idleSeconds >= _config.IdleShutdownMinutes * 60.0)
        {
            IdleShutdownTriggered = true;
            _log.Info($"No player for {_config.IdleShutdownMinutes} min — idle shutdown (world is saved on the way down).");
            RequestStop();
        }

        _sinceStatusPublish += deltaSeconds;
        if (_sinceStatusPublish >= StatusPublishIntervalSeconds)
        {
            _sinceStatusPublish = 0;
            _statusJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                serverName = _config.ServerName,
                worldName = _config.WorldName,
                joinedPlayers = joined,
                maxPlayers = _config.MaxPlayers,
                protocolVersion = Networking.Protocol.Version,
                serverVersion = ServerVersionString,
                uptimeSeconds = (long)_uptime, // the shared monotonic tick clock (see GameServerBump.SampleHistories)
                lastActiveUnixSeconds = _lastActiveUnixSeconds,
                idleSeconds = (long)_idleSeconds,
                idleShutdownMinutes = _config.IdleShutdownMinutes,
            });
        }
    }
}
