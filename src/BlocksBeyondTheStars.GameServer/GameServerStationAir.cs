// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Sealed-volume air for PLAYER-BUILT stations (#1473, decision Marcel 2026-09-03: option b — like planet
/// bases). Until now a boarded station kept everyone breathing no matter how many holes the hull had: the
/// station world was "breathable" and life support was simply "is boarded". Now a boarder on a
/// <c>pstation:</c> station breathes only inside an air pocket that is completely enclosed by airtight full
/// cubes (glass counts, so windows are fine), door blocks (an airlock is an airlock) and the station core —
/// a pocket that reaches the void leaks, and the suit tank takes over until the hole is patched. The
/// already-airtight <c>force_field</c> block is the intended plug for openings ("a field that holds the
/// air", the reporter's own suggestion). NPC / template stations keep the blanket air: they are authored
/// sealed and their crews have no oxygen model.
/// <para>
/// The fill mirrors <see cref="GameServerBaseAir"/>'s pocket model, bounded by the station's stamped cell
/// box plus a margin (the void outside is pure air, so any pocket that escapes the box has leaked) and a
/// cell budget. Results are cached per station for a short interval, separately for sealed and leaking
/// cells, so several boarders in different rooms each get a correct answer without refilling every tick.
/// </para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far beyond the stamped build box a pocket may extend before it counts as leaked — room for
    /// hull work done while boarded without making leaky fills expensive.</summary>
    private const int StationAirMargin = 8;

    /// <summary>Cell budget per fill; a pocket larger than this is treated as open (player stations are small).</summary>
    private const int MaxSealedCellsPerStation = 12000;

    /// <summary>Minimum seconds between recomputes for one station's air.</summary>
    private const double StationAirRecomputeInterval = 1.5;

    private sealed class StationAirVolume
    {
        public string Body = string.Empty;                 // station world the fill was computed on
        public HashSet<Vector3i> Sealed = new();           // cells known to sit in a sealed pocket
        public HashSet<Vector3i> Leaking = new();          // cells known to sit in a pocket open to the void
        public double ComputedAt = double.NegativeInfinity;
    }

    private readonly Dictionary<string, StationAirVolume> _stationAir = new();

    /// <summary>Player id → station id the "hull is open" warning was last sent for; cleared once the player's
    /// pocket seals again, so a fresh breach warns again but a persistent one does not spam.</summary>
    private readonly Dictionary<string, string> _stationAirWarnedFor = new();

    private HashSet<ushort>? _stationAirtightExtras;

    private static bool IsPlayerStationId(string stationId)
        => stationId.StartsWith("pstation:", System.StringComparison.Ordinal);

    /// <summary>True for the interior world of a player-built station (<c>station:pstation:…</c>).</summary>
    private static bool IsPlayerStationWorld(string locationId)
        => locationId.StartsWith("station:pstation:", System.StringComparison.Ordinal);

    /// <summary>Whether the boarded station keeps this player breathing at <paramref name="cell"/>: always on an
    /// NPC station, only inside a sealed pocket on a player-built one. Sends the one-shot hull-open warning on the
    /// sealed → leaking transition.</summary>
    private bool StationLifeSupport(PlayerState p, Vector3i cell)
    {
        if (!_boardedStation.TryGetValue(p.PlayerId, out var stationId))
        {
            return false;
        }

        if (!IsPlayerStationId(stationId) || !_stationsById.TryGetValue(stationId, out var station) || !station.Stamped)
        {
            return true; // NPC / template stations are authored sealed
        }

        bool sealedHere = InSealedStationPocket(station, cell);
        if (sealedHere)
        {
            _stationAirWarnedFor.Remove(p.PlayerId);
        }
        else if (Rules.OxygenEnabledFor(p.ModeOverride)
            && (!_stationAirWarnedFor.TryGetValue(p.PlayerId, out var warnedFor) || warnedFor != stationId))
        {
            _stationAirWarnedFor[p.PlayerId] = stationId;
            if (FindSessionByPlayerId(p.PlayerId) is { } session)
            {
                Send(session, new Networking.Messages.ServerMessage { Text = "@station_air_lost" });
            }
        }

        return sealedHere;
    }

    /// <summary>Test/inspection: whether the given cell of the active station world sits in a sealed pocket.</summary>
    public bool StationCellSealedForTest(string stationId, Vector3i cell)
        => _stationsById.TryGetValue(stationId, out var station) && InSealedStationPocket(station, cell);

    private bool InSealedStationPocket(BoardableStation station, Vector3i cell)
    {
        string body = _world.LocationId;
        if (!_stationAir.TryGetValue(station.Id, out var vol))
        {
            _stationAir[station.Id] = vol = new StationAirVolume();
        }

        if (vol.Body != body || _uptime - vol.ComputedAt >= StationAirRecomputeInterval)
        {
            vol.Body = body;
            vol.ComputedAt = _uptime;
            vol.Sealed.Clear();
            vol.Leaking.Clear();
        }

        if (vol.Sealed.Contains(cell))
        {
            return true;
        }

        if (vol.Leaking.Contains(cell))
        {
            return false;
        }

        var pocket = FillStationPocket(station, cell, out bool sealedPocket);
        (sealedPocket ? vol.Sealed : vol.Leaking).UnionWith(pocket);
        return sealedPocket;
    }

    /// <summary>Flood-fills the air pocket containing <paramref name="start"/> inside the station's reach box.
    /// Sealed = never stepped outside the box and stayed within the cell budget.</summary>
    private HashSet<Vector3i> FillStationPocket(BoardableStation station, Vector3i start, out bool sealedPocket)
    {
        var cells = new HashSet<Vector3i>();
        sealedPocket = false;
        if (IsStationAirtightCell(start))
        {
            cells.Add(start);
            return cells; // standing inside a wall cell: no pocket to breathe from
        }

        int minX = station.BoundsMin.X - StationAirMargin, minY = station.BoundsMin.Y - StationAirMargin, minZ = station.BoundsMin.Z - StationAirMargin;
        int maxX = station.BoundsMax.X + StationAirMargin, maxY = station.BoundsMax.Y + StationAirMargin, maxZ = station.BoundsMax.Z + StationAirMargin;

        var frontier = new Queue<Vector3i>();
        cells.Add(start);
        frontier.Enqueue(start);
        bool leaked = false;
        var neighbours = new Vector3i[6];
        while (frontier.Count > 0 && !leaked && cells.Count <= MaxSealedCellsPerStation)
        {
            var c = frontier.Dequeue();
            neighbours[0] = new Vector3i(c.X + 1, c.Y, c.Z);
            neighbours[1] = new Vector3i(c.X - 1, c.Y, c.Z);
            neighbours[2] = new Vector3i(c.X, c.Y + 1, c.Z);
            neighbours[3] = new Vector3i(c.X, c.Y - 1, c.Z);
            neighbours[4] = new Vector3i(c.X, c.Y, c.Z + 1);
            neighbours[5] = new Vector3i(c.X, c.Y, c.Z - 1);
            for (int i = 0; i < 6; i++)
            {
                var n = neighbours[i];
                if (cells.Contains(n))
                {
                    continue;
                }

                if (n.X < minX || n.X > maxX || n.Y < minY || n.Y > maxY || n.Z < minZ || n.Z > maxZ)
                {
                    leaked = true; // reached the void beyond the hull box → this pocket is open
                    break;
                }

                if (IsStationAirtightCell(n))
                {
                    continue;
                }

                cells.Add(n);
                frontier.Enqueue(n);
            }
        }

        sealedPocket = !leaked && cells.Count <= MaxSealedCellsPerStation;
        return cells;
    }

    /// <summary>Airtight for station purposes: an airtight full cube (walls, glass, force field), any door block
    /// (the airlock the commission rule already demands) or the station core itself.</summary>
    private bool IsStationAirtightCell(Vector3i c)
    {
        var id = _world.GetBlockIfLoaded(c);
        if (id.IsAir)
        {
            return false;
        }

        _stationAirtightExtras ??= BuildStationAirtightExtras();
        if (_stationAirtightExtras.Contains(id.Value))
        {
            return true;
        }

        var def = _content.BlockById(id);
        return def is { Airtight: true } && ShapeCode.IsCube(_world.GetShape(c)); // shaped cells leak
    }

    private HashSet<ushort> BuildStationAirtightExtras()
    {
        var set = new HashSet<ushort>();
        foreach (var key in new[] { "door_slide", "door_hinge", "door_energy", "station_core" })
        {
            var id = _content.GetBlock(key)?.NumericId ?? BlockId.Air;
            if (!id.IsAir)
            {
                set.Add(id.Value);
            }
        }

        return set;
    }
}
