// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Sealed-volume air for PLAYER-BUILT stations (#1473, decision Marcel 2026-09-03: option b — like planet
/// bases); a generated derelict (#2632) has the pockets but no air at all since #2675 (its life support is dead). A
/// boarder breathes only inside an air pocket that is completely enclosed by
/// airtight full cubes (glass counts, so windows are fine), door blocks and doors built into a doorway (an airlock is an
/// airlock, open or closed) and the station core — a pocket that reaches the void leaks, and the suit tank takes over
/// until the hole is patched. The already-airtight <c>force_field</c> block is the intended plug for openings. NPC /
/// template stations keep the blanket air: they are authored sealed and their crews have no oxygen model.
/// <para>
/// Since #2652 the pockets come from the shared air engine (<see cref="AirFill"/>, #2650) — the same rule and the same
/// engine as a base, with the void beyond the station's build box plus <see cref="StationAirMargin"/> as the outside.
/// There is no cell budget: a sealed hall of any size breathes (only the engine's emergency brake stops a pathological
/// fill, and it never takes air away). A room is filled the first time anyone asks about it — at once, so a boarder's
/// arrival, a crew spot or a path step is answered correctly — and the rooms already known are refilled time-sliced
/// when a block inside the station changed, a door changed or the backstop elapsed, while the last result keeps
/// answering. Doors are bulkheads: a breach empties only its own room, so only the posts in that room lose their keeper
/// (#1487) and the hull warning goes only to the players standing in it, naming the opening (#2654).
/// </para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far beyond the stamped build box a pocket may extend before it counts as leaked — room for
    /// hull work done while boarded without making leaky fills expensive.</summary>
    private const int StationAirMargin = 8;

    /// <summary>Minimum seconds between the starts of two refills of one station's air.</summary>
    private const double StationAirRecomputeInterval = 1.5;

    /// <summary>A station's air: the known pockets (sealed and leaky) and a refill in progress.</summary>
    private sealed class StationAirVolume
    {
        public string Body = string.Empty;                 // station world the pockets were filled on
        public AirFill? Committed;                         // what every query answers from; grows with each room first asked about
        public StationAirSpace? Space;                     // the reader Committed was filled with
        public AirFill? Job;                               // a refill of the known sealed rooms in progress
        public StationAirSpace? JobSpace;
        public Vector3i BoundsMin, BoundsMax;              // the box the fills used — a grown box starts over
        public bool Dirty;                                 // a block set inside the station since the last refill started
        public int DoorsVersion;                           // _baseAirDoorsVersion at the last refill
        public double StartedAt = double.NegativeInfinity; // when Committed (or the pending refill) started
        public bool JobForChange;                          // the pending refill answers a block set or a door change
        public bool BrakeLogged;
    }

    private readonly Dictionary<string, StationAirVolume> _stationAir = new();

    /// <summary>Player id → station id the "hull is open" warning was last sent for; cleared once the player's
    /// pocket seals again, so a fresh breach warns again but a persistent one does not spam.</summary>
    private readonly Dictionary<string, string> _stationAirWarnedFor = new();

    private HashSet<ushort>? _stationAirDoorBlocks;
    private ushort _stationCoreId;

    private static bool IsPlayerStationId(string stationId)
        => stationId.StartsWith("pstation:", System.StringComparison.Ordinal);

    /// <summary>True for the interior world of a player-built station (<c>station:pstation:…</c>).</summary>
    private static bool IsPlayerStationWorld(string locationId)
        => locationId.StartsWith("station:pstation:", System.StringComparison.Ordinal);

    /// <summary>Whether a station has no blanket air — it breathes only inside its sealed pockets: a player-built one
    /// (#1473); and a generated derelict (#2632), whose pockets since #2675 breathe not at all (dead life support,
    /// <see cref="StationLifeSupport"/>) — the pockets still tell a closed room from a breached one (where a boarder
    /// through a door arrives). NPC stations and The Long Quiet keep the blanket air.</summary>
    private static bool UsesSealedPocketAir(string stationId) => IsPlayerStationId(stationId) || IsGeneratedDerelictId(stationId);

    /// <summary>The interior world of a station whose air comes from sealed pockets only (<see cref="UsesSealedPocketAir"/>):
    /// its deck is no free air — the world-level "breathable" flag is off there.</summary>
    private static bool IsSealedAirStationWorld(string locationId)
        => IsPlayerStationWorld(locationId) || IsGeneratedDerelictWorld(locationId);

    /// <summary>Whether the boarded station keeps this player breathing at <paramref name="cell"/>: always on an
    /// NPC station, only inside a sealed pocket on a player-built one, never on a generated derelict (#2675: its life
    /// support is dead — a pocket the player seals breathes no more than a breached one). Sends the one-shot hull-open
    /// warning on the sealed → leaking transition — for the pocket the player stands in, naming its opening (#2654); on a
    /// derelict the "no air aboard, your suit tank is your air" line once per boarding instead.</summary>
    private bool StationLifeSupport(PlayerState p, Vector3i cell)
    {
        if (!_boardedStation.TryGetValue(p.PlayerId, out var stationId))
        {
            return false;
        }

        if (IsGeneratedDerelictId(stationId))
        {
            if (Rules.OxygenEnabledFor(p.ModeOverride)
                && (!_stationAirWarnedFor.TryGetValue(p.PlayerId, out var told) || told != stationId))
            {
                _stationAirWarnedFor[p.PlayerId] = stationId; // re-armed on every boarding (OnDerelictBoarded)
                if (FindSessionByPlayerId(p.PlayerId) is { } derelictSession)
                {
                    Send(derelictSession, new Networking.Messages.ServerMessage { Text = "@srv.derelict.no_air" });
                }
            }

            return false;
        }

        if (!UsesSealedPocketAir(stationId) || !_stationsById.TryGetValue(stationId, out var station) || !station.Stamped)
        {
            return true; // NPC / template stations are authored sealed
        }

        bool sealedHere = StationPocketFor(station, cell, out var pocket);
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
                // #2654: name the hole — where this room meets the void, at the opening's height. Without a pocket (standing
                // inside a wall cell) the general hull warning stands in.
                string text = pocket is { Leaky: true, Opening: { } opening }
                    ? BaseAirLeakText(session.Locale, "srv.base.air_leak_at", "srv.base.air_leak_open_at", opening,
                        _world.GetBlockIfLoaded(opening), cell)
                    : "@station_air_lost";
                Send(session, new Networking.Messages.ServerMessage { Text = text });
            }
        }

        return sealedHere;
    }

    /// <summary>Whether a cell of the active world holds breathable air of its OWN making — a founded base's
    /// supply cube or sealed room, or a player station's sealed pocket — regardless of the world's atmosphere
    /// (#1483). What a flame needs on an airless body; the world-level atmosphere is checked by the caller.</summary>
    private bool BreathableAirAt(Vector3i cell)
    {
        if (InAnyBaseZone(cell) || InSealedBaseRoom(cell)
            || InFactoryAir(new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f))) // #2070: a factory hall breathes
        {
            return true;
        }

        if (IsSealedAirStationWorld(_world.LocationId)) // a player station's sealed rooms — never a derelict's (#2675)
        {
            string stationId = _world.LocationId.Substring("station:".Length);
            return !IsGeneratedDerelictId(stationId)
                && _stationsById.TryGetValue(stationId, out var station) && station.Stamped && InSealedStationPocket(station, cell);
        }

        return false;
    }

    /// <summary>Test/inspection: whether the given cell of the active station world sits in a sealed pocket — a due
    /// refill (a block set, a door changed) run to the end first.</summary>
    public bool StationCellSealedForTest(string stationId, Vector3i cell)
    {
        if (!_stationsById.TryGetValue(stationId, out var station))
        {
            return false;
        }

        StationAirFor(station, complete: true);
        return InSealedStationPocket(station, cell);
    }

    /// <summary>Whether the cell of the active station world sits in a sealed pocket of the station.</summary>
    private bool InSealedStationPocket(BoardableStation station, Vector3i cell) => StationPocketFor(station, cell, out _);

    /// <summary>
    /// The pocket around <paramref name="cell"/> and whether it is sealed. #1836: a door cell (built or stamped) and a
    /// fluid cell count as walls for the fill, but the player DOES stand in them — in the doorway between two rooms, in the
    /// pond of the arboretum. Reading that as "inside a wall → hull open" fired the breach warning on every door transit,
    /// so such a cell is judged by the air beside it: the head cell or a side, whichever holds a sealed pocket.
    /// </summary>
    private bool StationPocketFor(BoardableStation station, Vector3i cell, out AirPocket? pocket)
    {
        var vol = StationAirFor(station, complete: false);
        var space = vol.Space!;
        if (vol.Committed!.PocketAt(cell) is { } known && (known.Complete || known.Braked))
        {
            // A known room — also one carried over while its chunks are unloaded, or one the brake stopped (unknown: no air
            // to give that was never known, and no second fill of it on every question).
            pocket = known;
            return known.Sealed;
        }

        if (space.Classify(cell.X, cell.Y, cell.Z) == AirCell.Open)
        {
            pocket = StationPocketAt(vol, cell);
            return pocket is { Sealed: true };
        }

        pocket = null;
        if (!StartCellIsPassable(cell))
        {
            return false; // standing inside a wall cell: no pocket to breathe from
        }

        foreach (var alt in StartCellAlternatives(cell))
        {
            if (space.Classify(alt.X, alt.Y, alt.Z) != AirCell.Open)
            {
                continue;
            }

            var found = StationPocketAt(vol, alt);
            if (found is { Sealed: true })
            {
                pocket = found;
                return true;
            }

            pocket ??= found;
        }

        return false;
    }

    /// <summary>The known pocket holding an open cell, or — the first time anyone asks about that room — the pocket filled
    /// from it right now (and queued for the pending refill too, so the next result knows it). A room the per-room brake
    /// stops is logged once and stays known as unknown (#2649 review).</summary>
    private AirPocket? StationPocketAt(StationAirVolume vol, Vector3i cell)
    {
        var fill = vol.Committed!;
        var known = fill.PocketAt(cell);
        if (known is not null && (known.Complete || known.Braked))
        {
            return known;
        }

        int before = fill.Pockets.Count;
        int brakedBefore = fill.BrakedPockets;
        fill.AddSeed(cell);
        fill.Step(-1);
        vol.Job?.AddSeed(cell);
        if (fill.BrakedPockets > brakedBefore || fill.Braked)
        {
            LogStationAirBrake(vol, fill);
        }

        return fill.PocketAt(cell) ?? (fill.Pockets.Count > before ? fill.Pockets[before] : null);
    }

    /// <summary>Tells the log once per station that a room passed the emergency brake (#2650).</summary>
    private void LogStationAirBrake(StationAirVolume vol, AirFill fill)
    {
        if (vol.BrakeLogged)
        {
            return;
        }

        vol.BrakeLogged = true;
        _log.Warn($"Station air: a room on {vol.Body} passed the emergency brake ({fill.SpanCount} spans, {fill.CellCount} cells) " +
                  "— it keeps its last known air (none, if it was never known).");
    }

    /// <summary>Test seam (#2649 review): whether a station's air has met the emergency brake (logged).</summary>
    internal bool StationAirBrakedForTest(string stationId) => _stationAir.TryGetValue(stationId, out var vol) && vol.BrakeLogged;

    /// <summary>The station's air state on the active world: started over when the world or the box changed, a refill of
    /// the known sealed rooms started when stale and worked on within this tick's air budget (at least the per-tick minimum
    /// by the query that starts it, #2870; to the end with <paramref name="complete"/>, which also skips the minimum
    /// interval — the test seams).</summary>
    private StationAirVolume StationAirFor(BoardableStation station, bool complete)
    {
        string body = _world.LocationId;
        if (!_stationAir.TryGetValue(station.Id, out var vol))
        {
            _stationAir[station.Id] = vol = new StationAirVolume();
        }

        if (vol.Committed is null || vol.Body != body || vol.BoundsMin != station.BoundsMin || vol.BoundsMax != station.BoundsMax)
        {
            vol.Body = body;
            vol.BoundsMin = station.BoundsMin;
            vol.BoundsMax = station.BoundsMax;
            vol.Committed = NewStationFill(station, out var space);
            vol.Space = space;
            vol.Job = null;
            vol.JobSpace = null;
            vol.Dirty = false;
            vol.DoorsVersion = _baseAirDoorsVersion;
            vol.StartedAt = _uptime;
            return vol;
        }

        bool changed = vol.Dirty || vol.DoorsVersion != _baseAirDoorsVersion;
        bool stale = changed || _uptime - vol.StartedAt >= BaseAirBackstopInterval;
        bool started = false;
        if (vol.Job is null && stale && (complete || _uptime - vol.StartedAt >= StationAirRecomputeInterval))
        {
            started = true;
            var job = NewStationFill(station, out var jobSpace);
            foreach (var seed in vol.Committed.SealedSpanBottoms())
            {
                job.AddSeed(seed); // every span of a known sealed room: a room a new wall split is found from both halves
            }

            vol.Job = job;
            vol.JobSpace = jobSpace;
            vol.JobForChange = changed;
            vol.Dirty = false;
            vol.DoorsVersion = _baseAirDoorsVersion;
            vol.StartedAt = _uptime;
        }

        if (vol.Job is { } pending)
        {
            // The query that starts the refill always works a fixed minimum of it (#2870), as on a base.
            StepAirFill(pending, guaranteeProgress: started, complete);
            if (pending.Done)
            {
                CommitStationAir(vol);
            }
        }

        return vol;
    }

    /// <summary>Swaps a finished refill in — unless the whole run braked, which keeps the last result (#2650). Rooms the
    /// refill could not look at keep their last state (room by room, #2649 review): a room behind unloaded chunks, a room
    /// the per-room brake stopped (its last sealed state is kept and logged once) — and, on a refill no change asked for,
    /// a room already known as braked, so the backstop does not fill a pathological room again every few seconds.</summary>
    private void CommitStationAir(StationAirVolume vol)
    {
        var job = vol.Job!;
        var space = vol.JobSpace!;
        vol.Job = null;
        vol.JobSpace = null;
        if (job.Braked || job.BrakedPockets > 0)
        {
            LogStationAirBrake(vol, job);
            if (job.Braked)
            {
                return;
            }
        }

        if (vol.Committed is not null)
        {
            job.AdoptSealedFrom(vol.Committed, carryBraked: !vol.JobForChange);
        }

        vol.Committed = job;
        vol.Space = space;
    }

    /// <summary>A fresh fill over the station's interior as it stands now (no seeds yet).</summary>
    private AirFill NewStationFill(BoardableStation station, out StationAirSpace space)
    {
        int circ = _world.Circumference;
        var doorCells = new HashSet<Vector3i>();
        foreach (var d in _doors)
        {
            if (!d.PlayerBuilt)
            {
                continue;
            }

            // The door entity's ~3-tall opening column, keyed like the reader's canonical cells (see PlayerDoorFillsCell).
            int x = WorldConstants.WrapX((int)System.Math.Floor(d.Pos.X), circ);
            int z = WorldConstants.WrapZ((int)System.Math.Floor(d.Pos.Z), circ);
            int y0 = (int)System.Math.Floor(d.Pos.Y);
            for (int dy = 0; dy <= 2; dy++)
            {
                doorCells.Add(new Vector3i(x, y0 + dy, z));
            }
        }

        space = new StationAirSpace(_world, AirtightTable(), StationAirDoorBlocks(), _stationCoreId, doorCells,
            station.BoundsMin, station.BoundsMax, StationAirMargin);
        return new AirFill(space, _airLimits, chainDoors: false, station.BoundsMin);
    }

    /// <summary>Test seam (#1559): the sealed cells of the pocket around <paramref name="cell"/> in the active
    /// station world (0 when it leaks), filled afresh, so a test can size a large hall's pocket.</summary>
    public int StationPocketSizeForTest(string stationId, Vector3i cell)
    {
        if (!_stationsById.TryGetValue(stationId, out var station))
        {
            return 0;
        }

        _stationAir.Remove(stationId);
        return StationPocketFor(station, cell, out var pocket) && pocket is not null
            ? (int)System.Math.Min(int.MaxValue, pocket.Cells)
            : 0;
    }

    /// <summary>A cell the fill treats as a wall although a player can occupy it: a door (built into an air cell or a
    /// stamped door block) or a fluid (#1836).</summary>
    private bool StartCellIsPassable(Vector3i c)
    {
        var id = _world.GetBlockIfLoaded(c);
        if (id.IsAir)
        {
            return PlayerDoorFillsCell(c);
        }

        if (IsFluid(id.Value))
        {
            return true;
        }

        return _content.BlockById(id)?.Key.StartsWith("door_", System.StringComparison.Ordinal) == true;
    }

    /// <summary>Where to look for the pocket around an occupied wall-like cell: the head first (a pond is one deep, a
    /// doorway is walked through upright), then the four sides.</summary>
    private static IEnumerable<Vector3i> StartCellAlternatives(Vector3i c)
    {
        yield return new Vector3i(c.X, c.Y + 1, c.Z);
        yield return new Vector3i(c.X + 1, c.Y, c.Z);
        yield return new Vector3i(c.X - 1, c.Y, c.Z);
        yield return new Vector3i(c.X, c.Y, c.Z + 1);
        yield return new Vector3i(c.X, c.Y, c.Z - 1);
    }

    /// <summary>The door blocks that seal a station room (an airlock is an airlock); also reads the station core's id.</summary>
    private HashSet<ushort> StationAirDoorBlocks()
    {
        if (_stationAirDoorBlocks is null)
        {
            var set = new HashSet<ushort>();
            foreach (var key in new[] { "door_slide", "door_hinge", "door_energy" })
            {
                var id = _content.GetBlock(key)?.NumericId ?? BlockId.Air;
                if (!id.IsAir)
                {
                    set.Add(id.Value);
                }
            }

            _stationCoreId = _content.GetBlock(StationCoreBlock)?.NumericId.Value ?? 0;
            _stationAirDoorBlocks = set;
        }

        return _stationAirDoorBlocks;
    }

    // ---------------- #1487: crew only staffs posts that hold air ----------------

    private const double StationStaffInterval = 3.0; // seconds between re-checks of a boarded player station's posts
    private double _stationStaffTimer // #2235: per world — each boarded station keeps its own 3-s rhythm
    {
        get => _worlds.Active.SinceStationStaffCheck;
        set => _worlds.Active.SinceStationStaffCheck = value;
    }
    private readonly Dictionary<string, int> _stationStaffSig = new(); // station id → bitmask of staffable posts at the last (re)staffing

    /// <summary>Whether a station post may be staffed: always on NPC stations and for non-post markers; on a
    /// player-built station only while the air cell above the post sits in a sealed pocket.</summary>
    private bool StationMarkerStaffable(BoardableStation station, string type, Vector3f pos)
    {
        if (!IsPlayerStationId(station.Id) || !NpcProfessions.IsStaffedPostMarker(type))
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
            if (NpcProfessions.IsStaffedPostMarker(type) && StationMarkerStaffable(station, type, pos))
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
            if (NpcProfessions.IsStaffedPostMarker(type) && !StationMarkerStaffable(station, type, pos))
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
        var homeCell = home.ToBlock();
        if (!InSealedStationPocket(station, homeCell))
        {
            return false;
        }

        var cell = pos.ToBlock();
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
}
