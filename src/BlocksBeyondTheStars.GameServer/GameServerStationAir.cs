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

    /// <summary>Whether a cell of the active world holds breathable air of its OWN making — a founded base's
    /// supply cube or sealed room, or a player station's sealed pocket — regardless of the world's atmosphere
    /// (#1483). What a flame needs on an airless body; the world-level atmosphere is checked by the caller.</summary>
    private bool BreathableAirAt(Vector3i cell)
    {
        if (InAnyBaseZone(cell) || InSealedBaseRoom(cell))
        {
            return true;
        }

        if (IsPlayerStationWorld(_world.LocationId))
        {
            string stationId = _world.LocationId.Substring("station:".Length);
            return _stationsById.TryGetValue(stationId, out var station) && station.Stamped && InSealedStationPocket(station, cell);
        }

        return false;
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
    /// (the airlock the commission rule already demands), a door BUILT inside (a door entity fills its air cells —
    /// #1481, an airlock is an airlock whichever way it was placed) or the station core itself.</summary>
    private bool IsStationAirtightCell(Vector3i c)
    {
        var id = _world.GetBlockIfLoaded(c);
        if (id.IsAir)
        {
            return PlayerDoorFillsCell(c);
        }

        _stationAirtightExtras ??= BuildStationAirtightExtras();
        if (_stationAirtightExtras.Contains(id.Value))
        {
            return true;
        }

        var def = _content.BlockById(id);
        return def is { Airtight: true } && ShapeCode.IsCube(_world.GetShape(c)); // shaped cells leak
    }

    // ---------------- #1487: crew only staffs posts that hold air ----------------

    private const double StationStaffInterval = 3.0; // seconds between re-checks of a boarded player station's posts
    private double _stationStaffTimer;
    private readonly Dictionary<string, int> _stationStaffSig = new(); // station id → bitmask of staffable posts at the last (re)staffing

    /// <summary>Whether a station post may be staffed: always on NPC stations and for non-post markers; on a
    /// player-built station only while the air cell above the post sits in a sealed pocket.</summary>
    private bool StationMarkerStaffable(BoardableStation station, string type, Vector3f pos)
    {
        if (!IsPlayerStationId(station.Id) || type is not ("vendor" or "mission_board"))
        {
            return true;
        }

        var head = new Vector3i((int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Y) + 1, (int)System.Math.Floor(pos.Z));
        return InSealedStationPocket(station, head);
    }

    /// <summary>Bitmask of the station's posts that are staffable right now (marker order is stable per stamp).</summary>
    private int StationStaffSignature(BoardableStation station)
    {
        int sig = 0;
        for (int i = 0; i < station.Markers.Count && i < 31; i++)
        {
            var (type, pos) = station.Markers[i];
            if (type is "vendor" or "mission_board" && StationMarkerStaffable(station, type, pos))
            {
                sig |= 1 << i;
            }
        }

        return sig;
    }

    /// <summary>True when a player station has a trading post or mission board standing in an unsealed room.</summary>
    private bool StationHasUnstaffedPost(BoardableStation station)
    {
        foreach (var (type, pos) in station.Markers)
        {
            if (type is "vendor" or "mission_board" && !StationMarkerStaffable(station, type, pos))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Re-staffs a boarded player station when a post's room seals or opens (#1487): the crew arrives
    /// once the hull around the post is airtight and leaves — with a word to the boarders — when it is breached.
    /// Cheap: one signature per few seconds, a respawn only on a change.</summary>
    private void TickStationStaffing(double dt)
    {
        if (!IsPlayerStationWorld(_world.LocationId))
        {
            return;
        }

        _stationStaffTimer += dt;
        if (_stationStaffTimer < StationStaffInterval)
        {
            return;
        }

        _stationStaffTimer = 0;
        string stationId = _world.LocationId.Substring("station:".Length);
        if (!_stationsById.TryGetValue(stationId, out var station) || !station.Stamped)
        {
            return;
        }

        int sig = StationStaffSignature(station);
        if (!_stationStaffSig.TryGetValue(stationId, out var last) || last == sig)
        {
            _stationStaffSig[stationId] = sig;
            return;
        }

        _npcs.Clear();
        SpawnStationNpcs(station); // deterministic from the station seed: the same faces come back; a newly open post tells the boarders
        BroadcastNpcs();
    }

    /// <summary>Whether a player-built door entity occupies the cell (its ~3-tall opening column).</summary>
    private bool PlayerDoorFillsCell(Vector3i c)
    {
        foreach (var d in _doors)
        {
            if (d.PlayerBuilt
                && (int)System.Math.Floor(d.Pos.X) == c.X && (int)System.Math.Floor(d.Pos.Z) == c.Z
                && c.Y >= (int)System.Math.Floor(d.Pos.Y) && c.Y <= (int)System.Math.Floor(d.Pos.Y) + 2)
            {
                return true;
            }
        }

        return false;
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
