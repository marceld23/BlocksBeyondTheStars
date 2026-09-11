// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Cell budget per fill; a pocket larger than this is treated as open. Was 12 000 — a "Superfabrik"
    /// hall of a few hundred square blocks silently counted as breached (#1559); the fill is a plain BFS over
    /// resident chunks, so a budget four times that of a base still costs milliseconds every 1.5 s.</summary>
    private const int MaxSealedCellsPerStation = 64000;

    /// <summary>Why a pocket is not sealed (#1559): the warning names the cause, because "a hole in the hull" was
    /// wrong for a closed hall that was merely bigger than the budget.</summary>
    private enum StationLeak
    {
        None,
        Hull,       // the fill reached the void beyond the box + margin through an opening
        TooLarge,   // the pocket exceeded the cell budget
    }

    /// <summary>Minimum seconds between recomputes for one station's air.</summary>
    private const double StationAirRecomputeInterval = 1.5;

    private sealed class StationAirVolume
    {
        public string Body = string.Empty;                 // station world the fill was computed on
        public HashSet<Vector3i> Sealed = new();           // cells known to sit in a sealed pocket
        public HashSet<Vector3i> Leaking = new();          // cells known to sit in a pocket open to the void
        public StationLeak LastLeak = StationLeak.None;    // why the most recent leaking fill failed (#1559)
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
                // #1559: say WHY — the one-size warning blamed a hole in the hull for a closed hall that was only
                // bigger than the budget, and the player went looking for a leak that was not there.
                var leak = _stationAir.TryGetValue(stationId, out var vol) ? vol.LastLeak : StationLeak.Hull;
                Send(session, new Networking.Messages.ServerMessage { Text = leak == StationLeak.TooLarge ? "@station_air_too_large" : "@station_air_lost" });
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

        var pocket = FillStationPocket(station, cell, out bool sealedPocket, out var leak);
        (sealedPocket ? vol.Sealed : vol.Leaking).UnionWith(pocket);
        if (!sealedPocket)
        {
            vol.LastLeak = leak;
        }

        return sealedPocket;
    }

    /// <summary>Test seam (#1559): the sealed cells of the pocket around <paramref name="cell"/> in the active
    /// station world (empty when it leaks), so a test can size a large hall's pocket.</summary>
    public int StationPocketSizeForTest(string stationId, Vector3i cell)
    {
        if (!_stationsById.TryGetValue(stationId, out var station))
        {
            return 0;
        }

        var pocket = FillStationPocket(station, cell, out bool sealedPocket, out _);
        return sealedPocket ? pocket.Count : 0;
    }

    /// <summary>Flood-fills the air pocket containing <paramref name="start"/> inside the station's reach box.
    /// Sealed = never stepped outside the box and stayed within the cell budget; <paramref name="leak"/> says
    /// which of the three failed (#1559).</summary>
    private HashSet<Vector3i> FillStationPocket(BoardableStation station, Vector3i start, out bool sealedPocket, out StationLeak leak)
    {
        var cells = new HashSet<Vector3i>();
        sealedPocket = false;
        leak = StationLeak.Hull;
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
                    leaked = true; // reached the void beyond the hull box → this pocket is open (a hole in the hull)
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
        if (sealedPocket)
        {
            leak = StationLeak.None;
        }
        else if (!leaked)
        {
            leak = StationLeak.TooLarge;
        }

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

    // ---------------- #1775: the crew stays inside the hull ----------------

    /// <summary>Cells the current spawn pass already handed to a crew member, so two settlers don't share one.</summary>
    private readonly HashSet<Vector3i> _stationCrewSpotsTaken = new();

    /// <summary>Where a crew member stands for a marker (#1775). The old rule homed the filler crew at the marker
    /// plus a blind ±2 jitter and the post keeper on the marker's floor cell — on a player station that is the
    /// vendor block itself, and a post two blocks from the hull put a settler inside the wall or in the vacuum
    /// beyond it ("Hier läuft einer außerhalb der Eisenmauer herum"). Now a spot must be standable (a floor
    /// under two free cells, judged the way the NPC walks) and, on a player station, lie in the sealed pocket
    /// of its post. Random cells within <paramref name="jitter"/> are tried first (a crowd, not a queue), then
    /// the rings around the marker on the marker's floor, then one deck down and up; the legacy spot is the
    /// last resort so an odd procedural layout keeps its crew exactly where it always stood.</summary>
    private Vector3f StationCrewSpot(BoardableStation station, Vector3f marker, System.Random rng, int jitter)
    {
        int mx = (int)System.Math.Floor(marker.X), my = (int)System.Math.Floor(marker.Y), mz = (int)System.Math.Floor(marker.Z);
        bool playerStation = IsPlayerStationId(station.Id);
        var legacy = jitter > 0
            ? new Vector3f(marker.X + (float)(rng.NextDouble() * 2 * jitter - jitter), my, marker.Z + (float)(rng.NextDouble() * 2 * jitter - jitter))
            : new Vector3f(marker.X, my, marker.Z);

        for (int i = 0; i < (jitter > 0 ? 8 : 0); i++)
        {
            if (TryCrewCell(station, mx + rng.Next(-jitter, jitter + 1), my, mz + rng.Next(-jitter, jitter + 1), playerStation, out var spot))
            {
                return spot;
            }
        }

        foreach (int y in new[] { my, my - 1, my + 1 })
        {
            for (int r = 0; r <= jitter + 1; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) != r)
                        {
                            continue; // ring r only
                        }

                        if (TryCrewCell(station, mx + dx, y, mz + dz, playerStation, out var spot))
                        {
                            return spot;
                        }
                    }
        }

        return legacy;
    }

    private bool TryCrewCell(BoardableStation station, int x, int y, int z, bool playerStation, out Vector3f spot)
    {
        spot = default;
        var cell = new Vector3i(x, y, z);
        if (_stationCrewSpotsTaken.Contains(cell) || StandableSpot(x, y, z) is not { } s)
        {
            return false;
        }

        if (playerStation && !InSealedStationPocket(station, cell))
        {
            return false;
        }

        _stationCrewSpotsTaken.Add(cell);
        spot = s;
        return true;
    }

    /// <summary>The player station whose interior world is active, once stamped — the crew's containment volume
    /// is its sealed pocket (#1775); null on planets, NPC stations and before the stamp.</summary>
    private BoardableStation? ActivePlayerStation()
        => IsPlayerStationWorld(_world.LocationId)
            && _stationsById.TryGetValue(_world.LocationId.Substring("station:".Length), out var station)
            && station.Stamped
            ? station
            : null;

    /// <summary>Whether a crew member's feet cell lies outside the sealed pocket its home sits in. False while the
    /// home itself is not in a sealed pocket (a breached room follows the plain walking rules — the re-staffing
    /// takes the crew away shortly anyway).</summary>
    private bool OutsideCrewPocket(BoardableStation station, Vector3f home, Vector3f pos)
    {
        var homeCell = new Vector3i((int)System.Math.Floor(home.X), (int)System.Math.Floor(home.Y), (int)System.Math.Floor(home.Z));
        if (!InSealedStationPocket(station, homeCell))
        {
            return false;
        }

        var cell = new Vector3i((int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Y), (int)System.Math.Floor(pos.Z));
        return !InSealedStationPocket(station, cell);
    }

    /// <summary>Test seam (#1775): puts an NPC somewhere, as if it had wandered there.</summary>
    public void MoveNpcForTest(int id, Vector3f pos)
    {
        if (_npcs.FirstOrDefault(n => n.Id == id) is { } npc)
        {
            npc.Pos = pos;
        }
    }

    /// <summary>Whether a player-built door entity occupies the cell (its ~3-tall opening column). The door is
    /// stored canonical (x in [0, circ)), the fill walks the unwrapped space around the origin (#1558), so the
    /// column is compared the short way round the seam (#1773) — otherwise no door west of x 0 ever seals.</summary>
    private bool PlayerDoorFillsCell(Vector3i c)
    {
        int circ = _world.Circumference;
        foreach (var d in _doors)
        {
            int dx = (int)System.Math.Floor(d.Pos.X) - c.X;
            if (d.PlayerBuilt
                && (circ > 0 ? WorldConstants.WrapDeltaX(dx, circ) == 0 : dx == 0) && (int)System.Math.Floor(d.Pos.Z) == c.Z
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
