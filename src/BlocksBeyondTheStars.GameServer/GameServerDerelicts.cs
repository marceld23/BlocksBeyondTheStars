// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Derelict stations (#2632, #2634): broken old boxy stations nobody lives on — one guaranteed in every world's start
/// system, otherwise rarely one to three in a system (<see cref="DerelictPlacer"/>, world option
/// <see cref="WorldDescription.Derelicts"/>). A derelict rides the PLAYER-STATION machinery rather than the NPC one:
/// <list type="bullet">
/// <item>its build is a <see cref="SpaceStructure"/> in <see cref="_playerStationCells"/> (ownerless, so anyone may board
/// it), seeded from <see cref="DerelictGenerator"/> — boarding stamps it into its own void world
/// <c>station:derelict:…</c> once and then keeps the world (the stamp anchor is persisted);</item>
/// <item>interior edits are written back into the build and the build is mirrored to the hull seen from space
/// (<see cref="MirrorStationCell"/> / <see cref="MirrorStationCellOutside"/>), so the more you mine, the more broken it
/// looks from outside;</item>
/// <item>it breathes like a player station — only its sealed pockets hold air (<see cref="UsesSealedPocketAir"/>) — and
/// keeps a gravity volume with a pull-back rescue (<see cref="TryGetBoardedPlayerStation"/>);</item>
/// <item>nobody lives there: no crew, no working trading post, no mission board, no visiting traders, no home spawn, and every
/// trading-station rule (the start station, trader traffic, the dock lesson, VEGA's station tip) skips it.</item>
/// </list>
/// Persistence = seed + deltas: an untouched derelict stores nothing; the first boarding or edit writes its build as a
/// <see cref="StoredSpaceStructure"/> row (id <c>derelict:…</c>, kept apart from the player stations at load) plus its door
/// records as structure edits. See docs/developer/DERELICT_STATIONS.md.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far beyond its gravity volume a boarder of The Long Quiet may drift before the suit brings them back —
    /// closer than a player station's (<see cref="StationDriftRescueDistance"/>): out here nobody builds from outside. A
    /// generated derelict's boarder steps out into a real spacewalk much sooner instead (#2636,
    /// <see cref="DerelictSpacewalkMargin"/>).</summary>
    private const int DerelictDriftRescueDistance = 24;

    /// <summary>Range at which VEGA first remarks on a derelict in flight.</summary>
    private const float DerelictApproachRange = 110f;

    /// <summary>A placed derelict of this galaxy and — once built (<see cref="EnsureDerelict"/>) — the layout its build was
    /// seeded from.</summary>
    private sealed class DerelictSite
    {
        public CelestialBody Body { get; init; } = null!;
        public string Id => Body.Id;
        public string SystemId { get; init; } = string.Empty;
        public int Index { get; init; }

        /// <summary>The start system's guaranteed one: always a module station with a sealed room to arrive in.</summary>
        public bool Guaranteed { get; init; }

        /// <summary>Null until the derelict is first needed (its system's flight, a boarding, a menu travel).</summary>
        public DerelictLayout? Layout { get; set; }
    }

    private readonly Dictionary<string, DerelictSite> _derelicts = new(System.StringComparer.Ordinal);

    /// <summary>Persisted derelict rows (#2634), read by <see cref="LoadPlayerStations"/> and kept out of the player
    /// stations; <see cref="RegisterDerelicts"/> seeds a derelict's build from its row when it has one.</summary>
    private readonly Dictionary<string, StoredSpaceStructure> _storedDerelictRows = new(System.StringComparer.Ordinal);

    /// <summary>True for any derelict station id — the generated ones and "The Long Quiet".</summary>
    internal static bool IsDerelictId(string? id) => DerelictPlacer.IsDerelictId(id);

    /// <summary>True for a derelict placed by <see cref="DerelictPlacer"/> (minable, airless where breached, persisted) —
    /// not the story's Long Quiet, which keeps its own rules.</summary>
    internal static bool IsGeneratedDerelictId(string? id) => IsDerelictId(id) && id != DerelictStationId;

    /// <summary>True for the interior world of a generated derelict (<c>station:derelict:…</c>).</summary>
    private static bool IsGeneratedDerelictWorld(string locationId)
        => locationId.StartsWith(StationLocationIdPrefix, System.StringComparison.Ordinal)
           && IsGeneratedDerelictId(locationId.Substring(StationLocationIdPrefix.Length));

    // ---------------- placement + registration ----------------

    /// <summary>The galaxy pass (end of <see cref="BuildGalaxy"/>): adds the derelict bodies and gives them the status the
    /// save remembers (Visited once someone boarded one).</summary>
    private void PlaceDerelicts()
    {
        string? startSystem = _galaxy.FindBody(_meta.ActiveLocationId)?.SystemId;
        int placed = DerelictPlacer.Place(_galaxy.Systems, _meta.Seed, _meta.Description.Derelicts,
            _meta.Description.StarSystemCount, startSystem);
        if (placed == 0)
        {
            return;
        }

        var stored = _repo.LoadLocationStatuses();
        foreach (var body in _galaxy.AllBodies())
        {
            if (IsGeneratedDerelictId(body.Id) && stored.TryGetValue(body.Id, out var s)
                && System.Enum.TryParse<GenerationStatus>(s, out var status))
            {
                body.Status = status;
            }
        }

        _log.Info($"{placed} derelict station(s) drift in this galaxy.");
    }

    /// <summary>Registers every placed derelict (registries boot stage, after <see cref="LoadPlayerStations"/>) — only the
    /// site; the build itself is made on first need (<see cref="EnsureDerelict"/>), so a server that never flies there
    /// never pays for it.</summary>
    private void RegisterDerelicts()
    {
        _derelicts.Clear();
        string? startSystem = _galaxy.FindBody(_meta.ActiveLocationId)?.SystemId;
        foreach (var sys in _galaxy.Systems)
        {
            foreach (var body in sys.Bodies.Where(b => IsGeneratedDerelictId(b.Id)).ToList())
            {
                int index = DerelictIndex(body.Id);
                _derelicts[body.Id] = new DerelictSite
                {
                    Body = body,
                    SystemId = sys.Id,
                    Index = index,
                    Guaranteed = sys.Id == startSystem && index == 0,
                };
            }
        }
    }

    /// <summary>Builds a derelict the first time it is needed — its layout (seed-pure), its build (from its persisted row
    /// when it has one, else fresh from the layout), the boardable record and its flight position — and returns the
    /// boardable record. Null for an id that is no generated derelict of this galaxy. Called from
    /// <see cref="GetOrCreateStation"/>, so every path that finds a station (the flight contacts, a menu travel, a rejoin)
    /// builds it on the way.</summary>
    private BoardableStation? EnsureDerelict(string id)
    {
        if (!_derelicts.TryGetValue(id, out var site))
        {
            return null;
        }

        if (site.Layout is null)
        {
            long seed = _meta.Seed ^ WorldGenerator.StableHash("derelict:" + site.Id);
            site.Layout = DerelictGenerator.Generate(seed, _content, proceduralOnly: site.Guaranteed);
            RegisterDerelict(site.Body, site.Index, site.Layout);
        }

        return _stationsById.TryGetValue(id, out var station) ? station : null;
    }

    private void RegisterDerelict(CelestialBody body, int index, DerelictLayout layout)
    {
        var s = new SpaceStructure
        {
            Id = body.Id,
            Kind = "station",
            OwnerId = string.Empty, // nobody's — anyone may board, mine and patch it
            Name = body.Name,
            Boardable = true,
        };

        bool restored = _storedDerelictRows.TryGetValue(body.Id, out var row);
        if (restored)
        {
            DeserializeCells(row!.Blocks, s);
            foreach (var cell in s.Cells.Keys.ToList())
            {
                // The row stores block ids only: an unchanged cell takes its dye and form back from the layout.
                if (layout.Get(cell.X, cell.Y, cell.Z) == s.Cells[cell].Value)
                {
                    var (tint, glow) = layout.GetModifier(cell.X, cell.Y, cell.Z);
                    s.Set(cell, s.Cells[cell], tint, glow, layout.GetShape(cell.X, cell.Y, cell.Z));
                }
            }
        }
        else
        {
            for (int x = 0; x < layout.Width; x++)
                for (int y = 0; y < layout.Height; y++)
                    for (int z = 0; z < layout.Length; z++)
                    {
                        ushort b = layout.Get(x, y, z);
                        if (b != 0)
                        {
                            var (tint, glow) = layout.GetModifier(x, y, z);
                            s.Set(new Vector3i(x, y, z), new BlockId(b), tint, glow, layout.GetShape(x, y, z));
                        }
                    }
        }

        // The doors: what the save says once anything was stored, else the layout's.
        if (restored || _repo.LoadStructureEdits(body.Id).Count > 0)
        {
            ApplyPersistedStationDoors(s);
        }
        else
        {
            foreach (var d in layout.Doors)
            {
                AddPlacedShipDoor(s, d.Cell, DoorBlocks.Slide, d.AxisX);
            }
        }

        // The flight position: seed-pure from the ORIGINAL layout, so mining an edge never moves the hull.
        var pos = DerelictSpacePosition(index, layout);
        s.Position = pos;
        var (min, max) = CellBox(s);
        s.Width = max.X - min.X + 1;
        s.Height = max.Y - min.Y + 1;
        s.Length = max.Z - min.Z + 1;

        _playerStationCells[s.Id] = s;
        _stationsById[s.Id] = new BoardableStation
        {
            Id = s.Id,
            Name = s.Name,
            SizeTier = layout.SizeTier,
            SpacePosition = pos,
            Origin = StationStampOrigin,
            Materialised = row?.Stamped ?? false, // #1481: the interior world already holds a stamp at this anchor
            StampMin = row is null ? default : new Vector3i(row.StampMinX, row.StampMinY, row.StampMinZ),
        };
    }

    /// <summary>The <c>n</c> of a derelict body id <c>derelict:&lt;sys&gt;-&lt;n&gt;</c> (0 when unreadable).</summary>
    private static int DerelictIndex(string id)
    {
        int dash = id.LastIndexOf('-');
        return dash >= 0 && int.TryParse(id.AsSpan(dash + 1), out int n) ? n : 0;
    }

    /// <summary>Where a derelict floats in every orbit instance of its system — behind the launch point (the NPC
    /// stations' lanes lie ahead of it), above the flight plane so no planet sphere ever reaches it, the three of a
    /// system side by side.</summary>
    internal static Vector3f DerelictSpacePosition(int index, DerelictLayout layout)
    {
        float halfY = layout.Height / 2f, halfZ = layout.Length / 2f;
        float x = (index % 3 - 1) * 95f;
        float y = StationHullFloorY + halfY + 6f + (index * 7 % 12);
        float z = -(110f + halfZ + (index / 3) * 70f);
        return new Vector3f(x, y, z);
    }

    /// <summary>Puts the hulls of the instance system's derelicts into a freshly created space instance (their dock
    /// contacts come with the station contacts — a derelict is a station body without a host, seen from every orbit).</summary>
    private void AddDerelictHulls(SpaceInstance instance, CelestialBody? anchor)
    {
        if (anchor is null || _derelicts.Count == 0)
        {
            return;
        }

        foreach (var site in _derelicts.Values)
        {
            if (site.SystemId == anchor.SystemId && EnsureDerelict(site.Id) is not null
                && _playerStationCells.TryGetValue(site.Id, out var s))
            {
                instance.Structures[s.Id] = s;
            }
        }
    }

    // ---------------- persistence ----------------

    /// <summary>Writes a generated derelict's build (#2634): the row (cells + stamp anchor) and its door records. The row's
    /// location is the derelict's system — it has no host body, and the player-station loader never reads it.</summary>
    private void PersistDerelict(SpaceStructure s)
    {
        var stamped = _stationsById.TryGetValue(s.Id, out var boardable) && boardable.Materialised ? boardable : null;
        var row = new StoredSpaceStructure
        {
            Id = s.Id,
            OwnerId = string.Empty,
            Name = s.Name,
            Location = _derelicts.TryGetValue(s.Id, out var site) ? site.SystemId : string.Empty,
            PosX = s.Position.X,
            PosY = s.Position.Y,
            PosZ = s.Position.Z,
            Boardable = true,
            Blocks = SerializeCells(s.Cells),
            Stamped = stamped is not null,
            StampMinX = stamped?.StampMin.X ?? 0,
            StampMinY = stamped?.StampMin.Y ?? 0,
            StampMinZ = stamped?.StampMin.Z ?? 0,
        };
        _repo.SaveSpaceStructure(row);
        _storedDerelictRows[s.Id] = row;
        PersistStationDoors(s);
    }

    /// <summary>Persists the build when <paramref name="s"/> is a generated derelict; false for every other structure, so
    /// the caller runs its own (player-station) persistence.</summary>
    private bool PersistIfDerelict(SpaceStructure s)
    {
        if (!IsGeneratedDerelictId(s.Id))
        {
            return false;
        }

        PersistDerelict(s);
        return true;
    }

    // ---------------- boarding ----------------

    /// <summary>The boarding half of a derelict (after the stamp): VEGA's line the first time this player steps aboard
    /// this one. The crates (#2633) and the guardian spawner (#2635) hook in here — their spots are on the layout
    /// (<see cref="DerelictCrateSpotsWorld"/>, <see cref="DerelictSpawnerSpotWorld"/>).</summary>
    private void OnDerelictBoarded(PlayerSession session, BoardableStation station)
    {
        if (!IsGeneratedDerelictId(station.Id))
        {
            return;
        }

        SpawnDerelictCrates(station); // #2633: the old supply crates, filled once ever (GameServerDerelictCrates.cs)

        if (session.State.Milestones.Add("derelict:boarded:" + station.Id))
        {
            SendVegaLine(session, "vega.derelict.boarded", 2, station.Name);
            _repo.SavePlayer(session.State);
        }
    }

    /// <summary>The interior-world cell a generated derelict's boarder is set down near: the layout's arrival (the sealed
    /// command room). Null for anything else.</summary>
    private Vector3i? DerelictArrivalHint(BoardableStation station)
        => _derelicts.TryGetValue(station.Id, out var site) && site.Layout is { } layout ? StationCellToWorld(station, layout.Arrival) : null;

    /// <summary>#2633 hook: the interior-world feet cells of a derelict's old supply crates (empty deck cells), once its
    /// interior is stamped — empty before.</summary>
    internal IReadOnlyList<Vector3i> DerelictCrateSpotsWorld(string stationId)
        => _derelicts.TryGetValue(stationId, out var site) && site.Layout is { } layout
           && _stationsById.TryGetValue(stationId, out var st) && st.Materialised
            ? layout.CrateSpots.Select(c => StationCellToWorld(st, c)).ToList()
            : new List<Vector3i>();

    /// <summary>#2635 hook: the interior-world feet cell for the guardian spawner (the middle of the largest room but
    /// the arrival's), once the interior is stamped — null before or when the layout has none.</summary>
    internal Vector3i? DerelictSpawnerSpotWorld(string stationId)
        => _derelicts.TryGetValue(stationId, out var site) && site.Layout?.SpawnerSpot is { } spot
           && _stationsById.TryGetValue(stationId, out var st) && st.Materialised
            ? StationCellToWorld(st, spot)
            : null;

    /// <summary>A boarder far beyond a derelict's gravity volume (#2632): the suit's thrusters bring them back aboard. Since
    /// #2636 only The Long Quiet (a tiny story site with blanket air) and the fallback of a generated derelict's spacewalk
    /// when no flight can open (free flight off, a ship that can't fly — <see cref="StartDerelictSpacewalk"/>) end here;
    /// a generated derelict's boarder otherwise steps out into a real spacewalk.</summary>
    private void ReturnDriftingDerelictBoarder(PlayerSession session, BoardableStation station)
    {
        var p = session.State;
        p.Position = station.Spawn;
        p.AboveAtmosphere = false;
        session.AwaitingSpawnAdopt = true; // the client's stale stream must not drag them back out (#865)
        _log.Info($"Player '{p.Name}' drifted out of derelict '{station.Name}'; brought back aboard.");
        Send(session, new RespawnNotice { X = station.Spawn.X, Y = station.Spawn.Y, Z = station.Spawn.Z, Reason = "@srv.derelict.drifted_back" });
        Send(session, new ServerMessage { Text = Localize(session.Locale, "srv.derelict.drifted_back") }); // readable in chat too (#1318)
        SendPlayerState(session);
    }

    // ---------------- flight ----------------

    /// <summary>VEGA's first word about a derelict in flight (called with the wreck approach on every ship pose): once per
    /// player and derelict, inside <see cref="DerelictApproachRange"/> of its centre (a plain distance — this runs on
    /// every pose).</summary>
    private void CheckDerelictApproach(PlayerSession session, CombatEntity e, Vector3f pos)
    {
        if (e.Kind != CombatEntityKind.SpaceStation || !IsGeneratedDerelictId(e.Id)
            || e.Position.DistanceSquared(pos) > DerelictApproachRange * DerelictApproachRange)
        {
            return;
        }

        if (session.State.Milestones.Add("derelict:seen:" + e.Id))
        {
            SendVegaLine(session, "vega.derelict.approach", 1, e.Name);
            _repo.SavePlayer(session.State);
        }
    }

    // ---------------- test seams ----------------

    /// <summary>Test seam: the generated derelicts of this galaxy (body ids).</summary>
    public IReadOnlyList<string> DerelictIdsForTest => _derelicts.Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToList();

    /// <summary>Test seam: the layout a derelict was seeded from.</summary>
    public DerelictLayout? DerelictLayoutForTest(string id) => EnsureDerelict(id) is not null && _derelicts.TryGetValue(id, out var site) ? site.Layout : null;

    /// <summary>Test seam: a layout cell of a derelict in its interior world (after the first boarding).</summary>
    public Vector3i DerelictCellToWorldForTest(string id, Vector3i cell)
        => _stationsById.TryGetValue(id, out var st) ? StationCellToWorld(st, cell) : cell;

    /// <summary>Test seam: the interior-world crate spots / spawner spot of a boarded derelict.</summary>
    public IReadOnlyList<Vector3i> DerelictCrateSpotsForTest(string id) => DerelictCrateSpotsWorld(id);

    public Vector3i? DerelictSpawnerSpotForTest(string id) => DerelictSpawnerSpotWorld(id);
}
