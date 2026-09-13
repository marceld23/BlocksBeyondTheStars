// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The gravity volume of a player-built station (#1485, decision Marcel 2026-09-03: option b). A boarder who
/// stepped over the edge of the deck used to fall for ever — the station world has no floor, the void rescue
/// skips boarded players, and only <c>U</c> (leave station) brought him back. Now the station's stamped box
/// plus a margin (the same reach the sealed-air pocket uses, #1473) IS the gravity: inside it you walk, outside
/// it the suit floats the way it does above a planet's atmosphere (item 10 — the client's on-foot zero-g,
/// driven by <see cref="Shared.State.PlayerState.AboveAtmosphere"/>), so you drift back or build the outer
/// hull from outside. <c>U</c> stays the anchor, and a boarder who drifts far beyond the volume is pulled back
/// to the spawn pad instead of being lost. NPC / template stations keep their decks as they are: their
/// interiors are closed and the world's gravity applies throughout.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Blocks beyond the stamped box that still count as station gravity — the air margin, so the two
    /// volumes share one boundary.</summary>
    private const int StationGravityMargin = StationAirMargin;

    /// <summary>Extra blocks a floating boarder keeps walking gravity for once inside, so the boundary never
    /// flickers underfoot.</summary>
    private const int StationGravityHysteresis = 2;

    /// <summary>Blocks beyond the gravity volume at which a drifting boarder is pulled back to the pad.</summary>
    private const int StationDriftRescueDistance = 64;

    /// <summary>Boarders who have already been told about the float this boarding (station id per player).</summary>
    private readonly Dictionary<string, string> _stationFloatHinted = new();

    /// <summary>Seconds after zero-g construction mode (#1842) is switched off during which a reported landing
    /// is not a fall: the player was hovering when the gravity came back.</summary>
    private const double StationZeroGFallGraceSeconds = 3.0;

    /// <summary>Whether a boarded player stands outside their player station's gravity volume (#1485) — or has
    /// chosen zero-g construction mode for the whole station (#1842). Always false on NPC stations and before
    /// the interior is stamped.</summary>
    private bool OutsideStationGravity(PlayerSession session)
    {
        var p = session.State;
        if (!TryGetBoardedPlayerStation(p.PlayerId, out var station))
        {
            return false;
        }

        if (session.StationZeroG)
        {
            return true; // the float is chosen, so the "drifted outside" hint would be wrong here
        }

        int margin = StationGravityMargin + (p.AboveAtmosphere ? 0 : StationGravityHysteresis);
        bool outside = BeyondStationBox(station, p.Position, margin);
        if (outside && !p.AboveAtmosphere
            && (!_stationFloatHinted.TryGetValue(p.PlayerId, out var hintedFor) || hintedFor != station.Id))
        {
            _stationFloatHinted[p.PlayerId] = station.Id;
            Send(session, new ServerMessage { Text = "@srv.station.zero_g" }); // once per boarding
        }

        return outside;
    }

    /// <summary>The void-rescue branch for boarded players (#1485): a boarder more than
    /// <see cref="StationDriftRescueDistance"/> blocks beyond the gravity volume is set back on the pad. Returns
    /// true when the player was moved.</summary>
    private bool RescueDriftingBoarder(PlayerSession session)
    {
        var p = session.State;
        if (!TryGetBoardedPlayerStation(p.PlayerId, out var station)
            || !BeyondStationBox(station, p.Position, StationGravityMargin + StationDriftRescueDistance))
        {
            return false;
        }

        p.Position = station.Spawn;
        p.AboveAtmosphere = false;
        session.AwaitingSpawnAdopt = true; // the client's stale stream must not drag them back out (#865)
        _log.Warn($"Player '{p.Name}' drifted away from station '{station.Name}'; pulled back to the pad.");
        Send(session, new RespawnNotice { X = station.Spawn.X, Y = station.Spawn.Y, Z = station.Spawn.Z, Reason = "@srv.station.drifted_back" });
        Send(session, new ServerMessage { Text = Localize(session.Locale, "srv.station.drifted_back") }); // readable in chat too (#1318)
        SendPlayerState(session);
        return true;
    }

    private bool TryGetBoardedPlayerStation(string playerId, out BoardableStation station)
    {
        station = null!;
        return _boardedStation.TryGetValue(playerId, out var stationId)
            && IsPlayerStationId(stationId)
            && _stationsById.TryGetValue(stationId, out station!)
            && station.Stamped;
    }

    /// <summary>Whether a position lies more than <paramref name="margin"/> blocks outside the station's stamped box.</summary>
    private static bool BeyondStationBox(BoardableStation station, Vector3f pos, int margin)
        => pos.X < station.BoundsMin.X - margin || pos.X > station.BoundsMax.X + 1 + margin
        || pos.Y < station.BoundsMin.Y - margin || pos.Y > station.BoundsMax.Y + 1 + margin
        || pos.Z < station.BoundsMin.Z - margin || pos.Z > station.BoundsMax.Z + 1 + margin;

    /// <summary>Zero-g construction mode (#1842): a boarder of a player-built station switches the float on or off
    /// for themselves. Only honoured while boarded on a stamped player station — on an NPC station, on a planet or
    /// aboard the ship the intent is dropped without a word. The atmosphere flag is re-evaluated at once, so the
    /// suit lifts off (or the deck catches them) on the very next client frame instead of the next tick.</summary>
    private void HandleSetStationZeroG(PlayerSession session, SetStationZeroGIntent intent)
    {
        if (!TryGetBoardedPlayerStation(session.State.PlayerId, out _))
        {
            return;
        }

        if (session.StationZeroG == intent.Enabled)
        {
            return; // nothing to flip — no repeated toast for a doubled key press
        }

        session.StationZeroG = intent.Enabled;
        if (!intent.Enabled)
        {
            session.StationZeroGOffAt = _uptime;
        }

        bool floatedBefore = session.State.AboveAtmosphere;
        UpdateAboveAtmosphere(session); // sends the state itself when the float flips
        if (session.State.AboveAtmosphere == floatedBefore)
        {
            SendPlayerState(session); // the mode flag changed even though the float did not (e.g. switched on while standing)
        }

        Send(session, new ServerMessage { Text = intent.Enabled ? "@srv.station.zero_g_on" : "@srv.station.zero_g_off" });
    }

    /// <summary>Drops zero-g construction mode (#1842) — on leaving the station, on any world change, on a
    /// respawn away from the station and on disconnect. The mode is session-only by decision, so nothing
    /// persists and a fresh boarding always starts walking.</summary>
    private static void ClearStationZeroG(PlayerSession session)
    {
        if (session.StationZeroG)
        {
            // The float was the mode's doing: drop it with the mode, so the state update the caller sends
            // (leave / board / respawn) already says "walking" instead of a one-tick stale float.
            session.State.AboveAtmosphere = false;
        }

        session.StationZeroG = false;
        session.StationZeroGOffAt = double.NegativeInfinity;
    }

    /// <summary>Whether a reported landing falls into the grace window right after zero-g construction mode
    /// was switched off (#1842) — the drop back to the deck is the mode's doing, not a fall the player took.</summary>
    private bool InStationZeroGFallGrace(PlayerSession session)
        => _uptime - session.StationZeroGOffAt < StationZeroGFallGraceSeconds;

    /// <summary>Test seam (#1485): whether the player currently floats outside their station's gravity volume.</summary>
    public bool FloatingOutsideStationForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s && s.State.AboveAtmosphere && InStation(playerId);

    /// <summary>Test seam (#1842): the client asked to switch zero-g construction mode on or off.</summary>
    public void SetStationZeroGForTest(string playerId, bool enabled)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleSetStationZeroG(session, new SetStationZeroGIntent { Enabled = enabled });
        }
    }

    /// <summary>Test seam (#1842): whether zero-g construction mode is on for the player's session.</summary>
    public bool StationZeroGForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s && s.StationZeroG;
}
