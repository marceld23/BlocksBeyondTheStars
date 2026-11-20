// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, a merchant's ship at a station (docs/developer/LIVELY_PLACES.md): an NPC trader that docks no longer
/// vanishes in a warp flash. Its ship lies moored beside the station's hangar for as long as its pilot trades aboard
/// (<see cref="NpcTraderPhase.Docked"/>, reported like a pilot's parked ship: engines off, "parked" on its plate), then casts
/// off and flies out to the system's edge where everybody can watch it go.
/// <list type="bullet">
/// <item><b>One record, two views.</b> The merchant aboard is the <c>VisitingTrader</c> record of the station; it now knows
/// the ship it came in. A flight scene that shows the station and finds no moored ship for the record makes one
/// (<see cref="EnsureMooredTraders"/>) — so a pilot who enters space later sees it too.</item>
/// <item><b>The pilot comes and goes with the ship.</b> Aboard, the station's own 1 Hz pass
/// (<see cref="SyncVisitingPilot"/>) stands the merchant at the trade post when a ship docked while somebody was already
/// there, and takes it away when the ship has left — both were missing before.</item>
/// <item><b>One berth.</b> A station that has a visitor is no destination for the next trader.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The berth: this far out of the hangar's mouth and this far to its side (flight units = blocks).</summary>
    private const float TraderBerthOut = 10f;
    private const float TraderBerthSide = 16f;

    /// <summary>Whether a merchant is aboard a station right now (its ship lies at the berth).</summary>
    private bool StationHasVisitor(string stationId)
        => _visitingTraders.TryGetValue(stationId, out var vt) && vt.ExpiresAt > _uptime;

    /// <summary>Where a moored trader lies: beside the hangar's mouth (never in the way of a docking pilot), or a little
    /// off the middle of a station whose hull is not known yet.</summary>
    private Vector3f TraderBerth(string stationId)
    {
        if (!_stationsById.TryGetValue(stationId, out var station) || station.Hull == null)
        {
            var middle = StationDockPoint(stationId, 0f);
            return new Vector3f(middle.X + TraderBerthSide, middle.Y, middle.Z);
        }

        var mouth = StationDockPoint(stationId, TraderBerthOut);
        return new Vector3f(mouth.X - (station.DockOutZ * TraderBerthSide), mouth.Y, mouth.Z + (station.DockOutX * TraderBerthSide));
    }

    /// <summary>Lays a trader at its station's berth until <paramref name="until"/>, its bow turned the way out.</summary>
    private void MoorTrader(NpcTrader t, double until)
    {
        t.Phase = NpcTraderPhase.Docked;
        t.Pos = TraderBerth(t.DestStationId);
        t.Target = t.Pos;
        t.DockedUntil = until;
        if (_stationsById.TryGetValue(t.DestStationId, out var station) && station.Hull != null)
        {
            t.Yaw = (float)System.Math.Atan2(station.DockOutX, station.DockOutZ) * Rad2Deg;
        }
    }

    /// <summary>Whether a moored trader must leave: its stay is over, or the station's record is another merchant's (or gone).</summary>
    private bool DockedTraderIsDue(NpcTrader t)
        => _uptime >= t.DockedUntil || !_visitingTraders.TryGetValue(t.DestStationId, out var vt) || vt.TraderId != t.Id;

    /// <summary>The merchant leaves: the station's record goes (the pass aboard takes the pilot away), everybody aboard is
    /// told, and the ship heads for the system's edge.</summary>
    private void CastOffTrader(NpcTrader t)
    {
        if (_visitingTraders.TryGetValue(t.DestStationId, out var vt) && vt.TraderId == t.Id)
        {
            _visitingTraders.Remove(t.DestStationId);
            AnnounceAboardStation(t.DestStationId, "trader_left", t.Name);
        }

        SetExitTarget(t);
        FaceTarget(t);
        t.Phase = NpcTraderPhase.Departing;
    }

    /// <summary>Makes the moored ship of every merchant aboard a station of this flight scene, where the scene has none.</summary>
    private void EnsureMooredTraders(SpaceInstance instance)
    {
        if (_visitingTraders.Count == 0)
        {
            return;
        }

        foreach (var e in instance.Entities)
        {
            if (e.Kind != CombatEntityKind.SpaceStation || !_visitingTraders.TryGetValue(e.Id, out var vt) || vt.ExpiresAt <= _uptime
                || vt.ShipType.Length == 0 || instance.Traders.Any(t => t.Id == vt.TraderId))
            {
                continue;
            }

            var trader = new NpcTrader { Id = vt.TraderId, ShipType = vt.ShipType, Name = vt.Name, HullRgb = vt.HullRgb, DestStationId = e.Id };
            MoorTrader(trader, vt.ExpiresAt);
            instance.Traders.Add(trader);
            ShowTraderHull(instance, trader);
            BroadcastSpaceState(instance);
        }
    }

    /// <summary>Caches a trader's voxel hull in the flight scene and hands it to everybody in it (a pilot who enters later
    /// gets it through the scene's structures).</summary>
    private void ShowTraderHull(SpaceInstance instance, NpcTrader trader)
    {
        var design = BuildNpcShipStructure(trader.StructureId, trader.ShipType);
        design.Position = trader.Pos;
        instance.Structures[design.Id] = design;
        foreach (var pid in instance.Players)
        {
            if (FindSessionByPlayerId(pid) is { } s)
            {
                SendShipDesign(s, design, "ship_remote");
            }
        }
    }

    /// <summary>Aboard a station, once a second: the visiting merchant stands at the trade post exactly while its ship
    /// lies at the berth.</summary>
    private void SyncVisitingPilot(BoardableStation station)
    {
        bool due = StationHasVisitor(station.Id);
        var pilot = _npcs.FirstOrDefault(n => n.VisitingPilot);
        if (due && pilot is null)
        {
            MaybeSpawnVisitingTrader(station);
            BroadcastNpcs();
        }
        else if (!due && pilot is not null)
        {
            _npcs.Remove(pilot);
            BroadcastNpcs();
        }

        if (!due && _visitingTraders.TryGetValue(station.Id, out var gone))
        {
            // The stay ran out while nobody watched from space: the record goes here, and the station says goodbye.
            _visitingTraders.Remove(station.Id);
            AnnounceAboardStation(station.Id, "trader_left", gone.Name);
        }
    }

    /// <summary>Test seam: the phase and the place of the traders in a pilot's flight scene.</summary>
    internal System.Collections.Generic.IReadOnlyList<(string Id, string Name, string Phase, Vector3f Pos, string Station)> TradersInSceneForTest(string playerId)
        => _playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var inst)
            ? inst.Traders.Select(t => (t.Id, t.Name, t.Phase.ToString(), t.Pos, t.DestStationId)).ToList()
            : new System.Collections.Generic.List<(string, string, string, Vector3f, string)>();

    /// <summary>Test seam: ends a station's merchant's stay now.</summary>
    internal void ExpireVisitingTraderForTest(string stationId)
    {
        if (_visitingTraders.TryGetValue(stationId, out var vt))
        {
            vt.ExpiresAt = _uptime;
        }

        foreach (var instance in _spaceInstances.Values)
        {
            foreach (var t in instance.Traders)
            {
                if (t.DestStationId == stationId && t.Phase == NpcTraderPhase.Docked)
                {
                    t.DockedUntil = _uptime;
                }
            }
        }
    }
}
