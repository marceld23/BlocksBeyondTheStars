// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// "Back to my ship" (#2286) — the pause menu's way out for a player on foot who is stuck: lost, trapped in a cave,
/// stranded on a summit above the atmosphere. Server-authoritative and free of any device or energy, gated instead by
/// the situation (<see cref="ReturnToShipRules"/>): the world rule <c>GameRules.ReturnToShip</c> must be on, the player
/// must be on foot, their own ship must be landed on the body they stand on, they must not be in a fight or falling,
/// and one use arms a three-minute cooldown. The arrival is the ship's heal tank — the same spot the suit teleporter
/// and a respawn use — delivered on the <see cref="RespawnNotice"/> snap channel (a plain state position would be
/// overwritten by the client's next move report, #414 N17).
/// <para>
/// "In a fight" is the closest existing notion the server has: the last blow the player was part of — a hit they
/// landed on a creature, a machine or a bandit, or damage one of those did to them — lies less than
/// <see cref="ReturnToShipRules.CombatGraceSeconds"/> back, or a bandit's hold-up is still waiting for their answer.
/// "Falling" is read from the player's own position reports (the client owns on-foot movement): the vertical speed
/// between the last two reports in different ticks is below −<see cref="ReturnToShipRules.FallingSpeed"/>.
/// </para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>playerId → server uptime at which "Back to my ship" may be used again. Not persisted: like the suit
    /// teleporter's cooldown it lives for the server run, which is what every other per-player timer here does.</summary>
    private readonly Dictionary<string, double> _returnToShipReadyAt = new();

    /// <summary>Puts the player back aboard their ship if every gate holds (public entry for tests and tools).</summary>
    public void ReturnToShip(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            Serve(session);
            HandleReturnToShip(session);
        }
    }

    private void HandleReturnToShip(PlayerSession session)
    {
        if (ReturnToShipRefusal(session) is { } reason)
        {
            Reject(session, "return_ship", reason);
            return;
        }

        var p = session.State;
        _returnToShipReadyAt[p.PlayerId] = _uptime + ReturnToShipRules.CooldownSeconds;
        p.Position = _healTank; // the heal tank of the ship parked on this body — where a respawn and the teleporter land too
        p.AboardShip = true;
        session.AwaitingSpawnAdopt = true; // #865: the client still streams its stuck-spot pose for a beat — it must not drag the player back outside
        session.VerticalSpeed = 0f; // set down, not falling

        Send(session, new RespawnNotice
        {
            X = p.Position.X,
            Y = p.Position.Y,
            Z = p.Position.Z,
            Reason = "@srv.return_ship.done",
        });
        SendPlayerState(session);
        _log.Info($"'{p.Name}' used Back to my ship on '{session.CurrentLocationId}'.");
    }

    /// <summary>Why "Back to my ship" is refused right now — the reject token the client localizes — or null when it
    /// may go ahead. One place for every gate, so the pause menu's refusal and the test seam read the same rule.</summary>
    private string? ReturnToShipRefusal(PlayerSession session)
    {
        var p = session.State;
        if (!Rules.ReturnToShip)
        {
            return "@srv.return_ship.disabled";
        }

        if (p.AboardShip)
        {
            return "@srv.return_ship.aboard";
        }

        if (session.RespawnChoiceDeadline > 0 || session.Spectating || InSpace(p.PlayerId) || p.InEva
            || p.InSpeeder.Length > 0 || p.InTrain.Length > 0)
        {
            return "@srv.return_ship.on_foot_only";
        }

        // The own ship has to stand on the very body the player is on (#2286, decided: no free cross-planet teleport).
        // _shipPlaced is exactly that: this player's landed-ship record in the world they are served in. On a space
        // station, with the ship in flight (launching unparks it) or parked on another body there is nothing here.
        if (InStation(p.PlayerId) || !_shipPlaced)
        {
            return "@srv.return_ship.no_ship_here";
        }

        if (InCombat(session))
        {
            return "@srv.return_ship.in_combat";
        }

        if (IsFalling(session))
        {
            return "@srv.return_ship.falling";
        }

        if (_returnToShipReadyAt.TryGetValue(p.PlayerId, out var readyAt) && _uptime < readyAt)
        {
            return "@srv.return_ship.cooldown:" + ReturnToShipRules.FormatRemaining(readyAt - _uptime);
        }

        return null;
    }

    /// <summary>The player was part of a blow less than the grace ago, or a robber is still waiting for their answer.</summary>
    private bool InCombat(PlayerSession session)
        => _uptime - session.LastCombatAt < ReturnToShipRules.CombatGraceSeconds || session.BanditDemandId != 0;

    /// <summary>The player's last two position reports say they are dropping faster than a hop — and the reading is fresh.</summary>
    private bool IsFalling(PlayerSession session)
        => session.LastMoveAt > 0
           && _uptime - session.LastMoveAt <= ReturnToShipRules.FallSampleMaxAgeSeconds
           && session.VerticalSpeed < -ReturnToShipRules.FallingSpeed;

    /// <summary>Stamps the player as being in a fight right now — called wherever a blow lands on foot (#2286).</summary>
    private void NoteCombat(PlayerSession session) => session.LastCombatAt = _uptime;

    /// <summary>Seconds until this player may use "Back to my ship" again (0 = ready) — for the state update.</summary>
    private float ReturnToShipCooldownLeft(PlayerSession session)
        => _returnToShipReadyAt.TryGetValue(session.State.PlayerId, out var readyAt) && readyAt > _uptime
            ? (float)(readyAt - _uptime)
            : 0f;

    /// <summary>Reads the vertical speed out of two consecutive position reports (#2286). Reports inside one tick share
    /// the tick's uptime, so only the first report of a new tick measures; a report after a long silence measures
    /// nothing (the player stood still, or was somewhere else) and resets the speed.</summary>
    private void TrackVerticalSpeed(PlayerSession session, float fromY, float toY)
    {
        if (session.LastMoveAt <= 0)
        {
            session.LastMoveAt = _uptime > 0 ? _uptime : double.Epsilon;
            session.VerticalSpeed = 0f;
            return;
        }

        double dt = _uptime - session.LastMoveAt;
        if (dt <= 0)
        {
            return; // same tick as the report that measured it
        }

        session.VerticalSpeed = dt > ReturnToShipRules.FallSampleMaxAgeSeconds ? 0f : (float)((toY - fromY) / dt);
        session.LastMoveAt = _uptime;
    }

    /// <summary>Test seam (#2286): the refusal token "Back to my ship" would answer this player with right now, or null
    /// when it would go ahead — without using it (no cooldown is armed).</summary>
    public string? ReturnToShipRefusalForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return null;
        }

        Serve(session);
        return ReturnToShipRefusal(session);
    }

    /// <summary>Test seam (#2286): takes this player's landed ship out of the world they are on — what a launch does —
    /// so a test can stand a player on a body their ship is not parked on.</summary>
    public void UnparkShipForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            Serve(session);
            RemoveLandedShip(session);
        }
    }
}
