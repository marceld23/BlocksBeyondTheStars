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

    /// <summary>Whether a boarded player stands outside their player station's gravity volume (#1485). Always
    /// false on NPC stations and before the interior is stamped.</summary>
    private bool OutsideStationGravity(PlayerSession session)
    {
        var p = session.State;
        if (!TryGetBoardedPlayerStation(p.PlayerId, out var station))
        {
            return false;
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

    /// <summary>Test seam (#1485): whether the player currently floats outside their station's gravity volume.</summary>
    public bool FloatingOutsideStationForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s && s.State.AboveAtmosphere && InStation(playerId);
}
