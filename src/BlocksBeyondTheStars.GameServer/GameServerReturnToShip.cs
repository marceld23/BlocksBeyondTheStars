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
/// and one use arms a three-minute cooldown (not for a builder, #2969: see <see cref="ReturnToShipWithoutCooldown"/>). The arrival is the ship's heal tank — the same spot the suit teleporter
/// and a respawn use — delivered on the <see cref="RespawnNotice"/> snap channel (a plain state position would be
/// overwritten by the client's next move report, #414 N17).
/// <para>
/// "In a fight" is the closest existing notion the server has: the last blow the player was part of — a hit they
/// landed on a creature, a machine or a bandit, or damage one of those did to them — lies less than
/// <see cref="ReturnToShipRules.CombatGraceSeconds"/> back, or a bandit's hold-up is still waiting for their answer.
/// "Falling" is read from the player's own position reports (the client owns on-foot movement): the vertical speed
/// between the last two reports in different ticks is below −<see cref="ReturnToShipRules.FallingSpeed"/>.
/// </para>
/// <para>
/// Both of those gates age on <see cref="GateClock"/>, which keeps running while the pause menu holds the world (#2558);
/// only the cooldown runs on the server uptime, which stands still in a held world.
/// </para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>playerId → server uptime at which "Back to my ship" may be used again. Not persisted: like the suit
    /// teleporter's cooldown it lives for the server run, which is what every other per-player timer here does.</summary>
    private readonly Dictionary<string, double> _returnToShipReadyAt = new();

    /// <summary>Seconds the world has stood held by the pause menu over this server run (#2558) — summed up by the held
    /// branch of <see cref="Tick"/>, the one place the hold's time passes without advancing the uptime.</summary>
    private double _heldSeconds;

    /// <summary>The clock the fight grace and the fall reading age on (#2558): the server uptime plus the time the world
    /// stood held. The uptime alone stands still behind the singleplayer pause menu — the very menu the button lives
    /// in — so a menu opened within the grace of a blow refused "Back to my ship" on every click for as long as it
    /// stayed open. A held world stops every attacker, so a grace that runs out behind the menu is no way around a
    /// fight. The three-minute cooldown stays on the uptime.</summary>
    private double GateClock => _uptime + _heldSeconds;

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
        if (!ReturnToShipWithoutCooldown(session))
        {
            _returnToShipReadyAt[p.PlayerId] = _uptime + ReturnToShipRules.CooldownSeconds;
        }

        p.Position = _healTank; // the heal tank of the ship parked on this body — where a respawn and the teleporter land too
        p.AboardShip = true;
        session.AwaitingSpawnAdopt = true; // #865: the client still streams its stuck-spot pose for a beat — it must not drag the player back outside
        session.VerticalSpeed = 0f; // set down, not falling

        Send(session, new RespawnNotice
        {
            X = p.Position.X,
            Y = p.Position.Y,
            Z = p.Position.Z,
            Reason = ReturnToShipRules.DoneToken, // #2559: the client's pause menu closes on exactly this
        });
        SendPlayerState(session);
        RaiseGameEvent(session, Shared.Story.GameEventKeys.ShipReturned, "menu"); // #2469: the tutorial's way-home lesson
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

        if (session.Respawn.RespawnChoiceDeadline > 0 || session.Spectating || InSpace(p.PlayerId) || p.InEva
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
            // #2559: a blow's grace names its seconds in the token's argument (a client reads them from the "_left" key; the
            // plain key has no slot, so an older client reads its sentence unchanged). A waiting robber has no clock to
            // name — the hold-up ends with the player's answer — so that refusal is the bare token.
            return session.Bandit.BanditDemandId != 0
                ? ReturnToShipRules.InCombatToken
                : ReturnToShipRules.InCombatToken + ":" + ReturnToShipRules.FormatFightSeconds(ReturnToShipFightLeft(session));
        }

        if (IsFalling(session))
        {
            return "@srv.return_ship.falling";
        }

        if (!ReturnToShipWithoutCooldown(session) && _returnToShipReadyAt.TryGetValue(p.PlayerId, out var readyAt) && _uptime < readyAt)
        {
            return "@srv.return_ship.cooldown:" + ReturnToShipRules.FormatRemaining(readyAt - _uptime);
        }

        return null;
    }

    /// <summary>#2969: a player who builds rather than survives — a Sandbox world, a Creative world, or a per-player Creative
    /// override — uses "Back to my ship" without the three-minute cooldown. Nothing there is worth waiting out: the
    /// cooldown keeps a survival world's walk home meaningful, which a world with free flight or no needs does not have.
    /// The other gates (on foot, own ship landed here, not in a fight or falling) stay.</summary>
    private bool ReturnToShipWithoutCooldown(PlayerSession session)
        => Rules.ModeFor(session.State.ModeOverride) == Shared.Configuration.GameMode.Creative
            || CurrentWorldMode() != WorldModeExplorer;

    /// <summary>The player was part of a blow less than the grace ago, or a robber is still waiting for their answer.
    /// The grace runs on <see cref="GateClock"/>, so it also runs out behind the pause menu (#2558).</summary>
    private bool InCombat(PlayerSession session)
        => GateClock - session.LastCombatAt < ReturnToShipRules.CombatGraceSeconds || session.Bandit.BanditDemandId != 0;

    /// <summary>Seconds the fight still blocks "Back to my ship" (#2559), 0 when it does not: what is left of the grace
    /// after the last blow. While a robber waits for an answer it is <see cref="ReturnToShipRules.RobberWaitingFightSeconds"/>
    /// — no countdown: the hold-up ends with the player's answer (and its deadline runs on <c>_uptime</c>, which stands
    /// still behind the pause menu, so any number here would count down to a refusal). For the state update, whose
    /// pause-menu row counts it down, and for the refusal's text.</summary>
    private double ReturnToShipFightLeft(PlayerSession session)
    {
        if (session.Bandit.BanditDemandId != 0)
        {
            return ReturnToShipRules.RobberWaitingFightSeconds;
        }

        double left = ReturnToShipRules.CombatGraceSeconds - (GateClock - session.LastCombatAt);
        return left > 0 ? left : 0;
    }

    /// <summary>The player's last two position reports say they are dropping faster than a hop — and the reading is
    /// fresh on <see cref="GateClock"/>, so it goes stale behind the pause menu too (#2558).</summary>
    private bool IsFalling(PlayerSession session)
        => session.LastMoveAt > 0
           && GateClock - session.LastMoveAt <= ReturnToShipRules.FallSampleMaxAgeSeconds
           && session.VerticalSpeed < -ReturnToShipRules.FallingSpeed;

    /// <summary>Stamps the player as being in a fight right now — called wherever a blow lands on foot (#2286) — and
    /// queues a state update, so the pause menu's row learns about the fight (#2559): a blow the player lands sends
    /// none of its own.</summary>
    private void NoteCombat(PlayerSession session)
    {
        session.LastCombatAt = GateClock;
        if (session.State.Health > 0f)
        {
            MarkPlayerStateDirty(session); // a lethal blow is the caller's to send (the death path sends at once)
        }
    }

    /// <summary>Seconds until this player may use "Back to my ship" again (0 = ready) — for the state update.</summary>
    private float ReturnToShipCooldownLeft(PlayerSession session)
        => !ReturnToShipWithoutCooldown(session)
            && _returnToShipReadyAt.TryGetValue(session.State.PlayerId, out var readyAt) && readyAt > _uptime
            ? (float)(readyAt - _uptime)
            : 0f;

    /// <summary>Reads the vertical speed out of two consecutive position reports (#2286). Reports inside one tick share
    /// the tick's clock, so only the first report of a new tick measures; a report after a long silence measures
    /// nothing (the player stood still, or was somewhere else) and resets the speed. Stamped on <see cref="GateClock"/>
    /// (#2558), so a pause between two reports counts as the silence it is.</summary>
    private void TrackVerticalSpeed(PlayerSession session, float fromY, float toY)
    {
        double now = GateClock;
        if (session.LastMoveAt <= 0)
        {
            session.LastMoveAt = now > 0 ? now : double.Epsilon;
            session.VerticalSpeed = 0f;
            return;
        }

        double dt = now - session.LastMoveAt;
        if (dt <= 0)
        {
            return; // same tick as the report that measured it
        }

        session.VerticalSpeed = dt > ReturnToShipRules.FallSampleMaxAgeSeconds ? 0f : (float)((toY - fromY) / dt);
        session.LastMoveAt = now;
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
