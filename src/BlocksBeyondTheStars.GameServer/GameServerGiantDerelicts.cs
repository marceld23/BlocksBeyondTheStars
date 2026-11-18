// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// GIANT SHIP WRECKS in space (#3614): huge authored ships (<c>data/giant_ships/*.json</c>, <see cref="GiantShipRules"/>) that
/// nobody can build, drifting abandoned — everyone got away. One guaranteed in the start system, rare elsewhere
/// (<see cref="GiantWreckPlacer"/>, world option <see cref="WorldDescription.GiantWrecks"/>; the host's
/// <see cref="Shared.Configuration.ServerConfig.PlaceGiantWrecks"/> off keeps them out of every world). A giant wreck is a
/// derelict of its own family (<c>derelict:giant-&lt;sys&gt;-&lt;n&gt;</c>), so every derelict rule holds for it by its id:
/// boardable by anyone, no air aboard (dead life support), nobody home, no trade, no home spawn, falling out is a spacewalk and a
/// breach lets the suit back in.
/// <list type="bullet">
/// <item><b>Which ship</b>: a weighted pick by a hash of the id among the designs no other wreck of the world is yet (#3764,
/// <see cref="GiantWreckPlacer.PickDesign"/> over <see cref="GiantWreckPlacer.Untaken"/>), pinned the first time
/// the wreck is built — GameServerGiantDerelictPins.cs: the ship, the ruin and decay revisions and a hash of the base build;
/// a wreck whose base would no longer come out as stored stays away while the save holds anything of it.</item>
/// <item><b>The build</b>: <see cref="GiantShipGenerator.BuildSpace"/> (ruin revision 3, profile <c>hull</c>) → the derelict decay in
/// ship mode (<see cref="DerelictGenerator.GenerateShip"/>: rooms from the doorways, breaches, doors, emergency and dead lights,
/// one guardian bay) → a DERIVED station structure (<see cref="RegisterDerivedStation"/>: the shell only to the pilots, the
/// players' changes as one delta blob, the interior one structure overlay). The heavy part runs ahead on a background thread
/// where the server has one (GameServerGiantDerelictBuilds.cs).</item>
/// <item><b>In flight</b> it is a contact of every orbit of its system, behind the launch point, past the derelicts and the
/// player stations, its floor above every planet sphere — placed by its real size (<see cref="GiantWreckFlightPosition"/>) the
/// first time and pinned there.</item>
/// </list>
/// Life aboard (chests, sounds, VEGA, the scanner): GameServerGiantDerelictLife.cs. See docs/developer/DERELICT_STATIONS.md.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The nearest a giant wreck's box comes to the launch point along Z (the derelicts' row starts there too).</summary>
    private const float GiantWreckNearZ = -110f;

    /// <summary>Clear space between a giant wreck's box and the box of every derelict, player station or earlier giant.</summary>
    private const float GiantWreckGap = 40f;

    /// <summary>A giant wreck's lowest block floats this high over the flight plane — clear of every planet sphere.</summary>
    private const float GiantWreckFloorY = StationHullFloorY + 6f;

    /// <summary>A placed giant wreck of this galaxy, its ship, and — once built — what its markers became.</summary>
    private sealed class GiantDerelictSite
    {
        public CelestialBody Body { get; init; } = null!;

        public string Id => Body.Id;

        public string SystemId { get; init; } = string.Empty;

        public int Index { get; init; }

        /// <summary>The ship it is (pinned at the first build).</summary>
        public StructureTemplate Template { get; set; } = null!;

        public bool Built { get; set; }

        /// <summary>Its base no longer comes out as the save stored it: it stays away this run (its data kept).</summary>
        public bool Retired { get; set; }

        /// <summary>Half the base build's cell box (the flight frame's size of the hull).</summary>
        public Vector3f Half { get; set; }

        /// <summary>The boarding arrival (the template's first entrance), in build cells.</summary>
        public Vector3i Arrival { get; set; }

        public IReadOnlyList<SettlementMarker> Chests { get; set; } = System.Array.Empty<SettlementMarker>();

        public IReadOnlyList<SettlementMarker> Sounds { get; set; } = System.Array.Empty<SettlementMarker>();

        public Vector3i? SpawnerSpot { get; set; }

        public int DoorCount { get; set; }

        /// <summary>The build running on a background thread (GameServerGiantDerelictBuilds.cs), until it is taken.</summary>
        public GiantBuildJob? Job { get; set; }
    }

    private readonly Dictionary<string, GiantDerelictSite> _giantDerelicts = new(System.StringComparer.Ordinal);

    /// <summary>Test seam (#3614): the ship every giant wreck is built from — also a pin-only sample — set before
    /// <see cref="Start"/>. Null = the weighted pick over the shipped pool.</summary>
    public string? GiantWreckDesignForTest { get; set; }

    // ---------------- placement + registration ----------------

    /// <summary>The galaxy pass right after the derelict stations (#3614): adds the giant wreck bodies — only where a ship can
    /// be built for one (or one is pinned already) — and gives them the status the save remembers. The host's switch
    /// (<see cref="Shared.Configuration.ServerConfig.PlaceGiantWrecks"/> off) keeps every one out, of an existing world too:
    /// nothing stored for them is touched, they simply are not there.</summary>
    private void PlaceGiantDerelicts()
    {
        if (!_config.PlaceGiantWrecks)
        {
            return;
        }

        string? startSystem = _galaxy.FindBody(_meta.ActiveLocationId)?.SystemId;
        bool anyDesign = GiantWreckCandidates().Count > 0;
        int placed = GiantWreckPlacer.Place(_galaxy.Systems, _meta.Seed, _meta.Description.GiantWrecks,
            _meta.Description.StarSystemCount, startSystem, id => anyDesign || _meta.GiantWreckDesigns.ContainsKey(id));
        if (placed == 0)
        {
            return;
        }

        var stored = _repo.LoadLocationStatuses();
        foreach (var body in _galaxy.AllBodies())
        {
            if (GiantShipRules.IsGiantWreckId(body.Id) && stored.TryGetValue(body.Id, out var s)
                && System.Enum.TryParse<GenerationStatus>(s, out var status))
            {
                body.Status = status;
            }
        }

        _log.Info($"{placed} giant ship wreck(s) drift in this galaxy.");
    }

    /// <summary>The ships a new giant wreck may be: the shipped pool of the world's packs (never a pin-only sample), or the
    /// test seam's one.</summary>
    private IReadOnlyList<StructureTemplate> GiantWreckCandidates()
    {
        if (GiantWreckDesignForTest is { } key)
        {
            return _content.GetGiantShip(key) is { } t ? new[] { t } : System.Array.Empty<StructureTemplate>();
        }

        return _content.GiantShipsFor(_meta.Description.EnabledStructurePacks, planetTypeKey: null);
    }

    /// <summary>Registers every placed giant wreck (from <see cref="RegisterDerelicts"/>) with the ship it is, then gets the start
    /// system's ready: on a background thread where the server has one, else built right here — during the boot, which the
    /// loading screen covers, instead of at the first flight. A wreck whose pinned ship cannot be rebuilt while the save holds
    /// its changes leaves the star map.</summary>
    private void RegisterGiantDerelicts()
    {
        RegisterGiantDerelictSites();
        string? start = _galaxy.FindBody(_meta.ActiveLocationId)?.SystemId;
        if (_config.ChunkGenWorkers > 0)
        {
            PrebuildGiantDerelicts(start);
            return;
        }

        foreach (var site in _giantDerelicts.Values.Where(s => s.SystemId == start).OrderBy(s => s.Index).ToList())
        {
            EnsureGiantDerelict(site.Id);
        }
    }

    private void RegisterGiantDerelictSites()
    {
        _giantDerelicts.Clear();
        // #3764: the wrecks of one world are different ships while a design is left. The pinned ones first (a wreck built
        // once keeps its ship), then every other wreck in the systems' order picks among the designs still free.
        var taken = PinnedGiantDesignKeys();
        foreach (var sys in _galaxy.Systems)
        {
            foreach (var body in sys.Bodies.Where(b => GiantShipRules.IsGiantWreckId(b.Id)).ToList())
            {
                var template = ResolveGiantDesign(body.Id, taken);
                if (template is null)
                {
                    sys.Bodies.Remove(body);
                    continue;
                }

                taken.Add(template.Key);

                _giantDerelicts[body.Id] = new GiantDerelictSite
                {
                    Body = body,
                    SystemId = sys.Id,
                    Index = DerelictIndex(body.Id),
                    Template = template,
                };
            }
        }
    }

    // ---------------- the build ----------------

    /// <summary>Builds a giant wreck the first time it is needed (from <see cref="EnsureDerelict"/>, so the flight contacts, a
    /// menu travel and a rejoin all build it on the way) and returns its boardable record; null for an id that is no giant
    /// wreck of this galaxy, or one that had to stay away (<see cref="GiantDerelictSite.Retired"/>).</summary>
    private BoardableStation? EnsureGiantDerelict(string id)
    {
        if (!_giantDerelicts.TryGetValue(id, out var site) || site.Retired)
        {
            return null;
        }

        if (!site.Built)
        {
            BuildGiantDerelict(site);
        }

        return site.Retired ? null : _stationsById.TryGetValue(id, out var station) ? station : null;
    }

    /// <summary>Whether a station id is a giant wreck that is not there (anymore) this run — the paths that find a station by
    /// a body (contacts, a menu travel, a rejoin) skip it instead of making it a plain station.</summary>
    private bool GiantWreckMissing(string id) => GiantShipRules.IsGiantWreckId(id) && EnsureGiantDerelict(id) is null;

    /// <summary>Template + seed → the ruined wreck → the ship-mode decay → the base build (ahead on a background thread where
    /// there is one) → checked against the pin → a derived station at its pinned flight spot.</summary>
    private void BuildGiantDerelict(GiantDerelictSite site)
    {
        site.Built = true;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var parts = TakeGiantBuild(site, out string how);
        var (wreck, build) = (parts.Wreck, parts.Build);
        long taken = watch.ElapsedMilliseconds;
        if (!GiantBaseStillMatches(site, wreck.BaseHash))
        {
            RetireGiantDerelict(site);
            return;
        }

        bool rebased = PinGiantDesign(site, wreck.BaseHash, out bool dirty);
        var (min, max) = build.CellBox();
        site.Half = new Vector3f((max.X - min.X + 1) / 2f, (max.Y - min.Y + 1) / 2f, (max.Z - min.Z + 1) / 2f);
        site.Arrival = wreck.Layout.Arrival;
        site.Chests = wreck.Chests;
        site.Sounds = wreck.Sounds;
        site.SpawnerSpot = wreck.Layout.SpawnerSpot;
        site.DoorCount = wreck.Layout.Doors.Count;
        build.Position = GiantWreckSpot(site, rebased, ref dirty);
        if (dirty)
        {
            _repo.SaveMetadata(_meta); // the pin and the spot: written once per wreck (again only when its base changed)
        }

        RegisterDerivedStation(site.Id, build, wreck.Layout.Arrival, DerelictGenerator.GiantSizeTier, wreck.BaseHash);
        AdoptGiantShell(site.Id, build, parts.Shell);
        _log.Info($"Giant wreck '{site.Body.Name}' ({site.Template.Key}): {build.Cells.Count} blocks, {site.DoorCount} door(s), " +
                  $"{site.Chests.Count} chest(s), {wreck.Layout.SealedRoomCount} closed / {wreck.Layout.BreachedRoomCount} torn room(s); " +
                  $"{how} ({taken} ms), registered in {watch.ElapsedMilliseconds - taken} ms.");
    }

    /// <summary>The decayed layout as a station build: its cells with their dye, glow and shape, and its intact doors.</summary>
    private static SpaceStructure GiantBaseBuild(string name, DerelictLayout layout)
    {
        var s = new SpaceStructure { Name = name, OwnerId = string.Empty };
        int count = 0;
        for (int x = 0; x < layout.Width; x++)
            for (int y = 0; y < layout.Height; y++)
                for (int z = 0; z < layout.Length; z++)
                {
                    count += layout.Get(x, y, z) != 0 ? 1 : 0;
                }

        s.Cells.EnsureCapacity(count); // one table of the right size instead of a chain of doubling copies
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

        foreach (var d in layout.Doors)
        {
            AddPlacedShipDoor(s, d.Cell, DoorBlocks.Slide, d.AxisX);
        }

        s.CellBox(); // counts the box slices here (on the builder's thread), not on the tick
        return s;
    }

    // ---------------- in flight ----------------

    /// <summary>Where a giant wreck floats in every orbit instance of its system when it is first placed: past the system's
    /// derelicts (their ORIGINAL layouts), its player stations (their hull boxes as they stand) and the earlier giants, by its
    /// own size. The caller pins the result (<see cref="GiantWreckSpot"/>), so a station built later never moves it.</summary>
    private Vector3f GiantWreckSpacePosition(GiantDerelictSite site)
    {
        var obstacles = new List<(Vector3f Centre, Vector3f Half)>();
        foreach (var d in _derelicts.Values.Where(d => d.SystemId == site.SystemId).OrderBy(d => d.Id, System.StringComparer.Ordinal).ToList())
        {
            if (EnsureDerelict(d.Id) is not null && d.Layout is { } l)
            {
                obstacles.Add((DerelictSpacePosition(d.Index, l), new Vector3f(l.Width / 2f, l.Height / 2f, l.Length / 2f)));
            }
        }

        obstacles.AddRange(PlayerStationBoxesIn(site.SystemId));
        foreach (var g in _giantDerelicts.Values.Where(g => g.SystemId == site.SystemId && g.Index < site.Index).OrderBy(g => g.Index).ToList())
        {
            if (EnsureGiantDerelict(g.Id) is not null && _playerStationCells.TryGetValue(g.Id, out var other))
            {
                obstacles.Add((other.Position, g.Half));
            }
        }

        return GiantWreckFlightPosition(site.Half, obstacles);
    }

    /// <summary>The hull boxes of the player stations of a system (each floats in its own body's orbit; the giant in every one).</summary>
    private List<(Vector3f Centre, Vector3f Half)> PlayerStationBoxesIn(string systemId)
    {
        var boxes = new List<(Vector3f Centre, Vector3f Half)>();
        foreach (var (id, s) in _playerStationCells.OrderBy(kv => kv.Key, System.StringComparer.Ordinal))
        {
            if (IsPlayerStationId(id) && s.Cells.Count > 0 && _stationHostBody.TryGetValue(id, out var host)
                && _galaxy.FindBody(host)?.SystemId == systemId)
            {
                var (min, max) = s.CellBox();
                boxes.Add((s.Position, new Vector3f((max.X - min.X + 1) / 2f, (max.Y - min.Y + 1) / 2f, (max.Z - min.Z + 1) / 2f)));
            }
        }

        return boxes;
    }

    /// <summary>
    /// The flight spot of a giant wreck's hull (its cell box's centre) with half size <paramref name="half"/>: behind the launch
    /// point (the NPC stations' lanes lie ahead of it, at positive Z), its near face <see cref="GiantWreckGap"/> past the far
    /// face of every obstacle box (the system's derelicts, player stations, earlier giants) and never nearer than
    /// <see cref="GiantWreckNearZ"/>; its lowest block <see cref="GiantWreckFloorY"/> over the flight plane, clear of every
    /// planet sphere, the space wreck and The Long Quiet down there. The client's flight clamp grows with every station hull, so
    /// the spot is always reachable.
    /// </summary>
    internal static Vector3f GiantWreckFlightPosition(Vector3f half, IReadOnlyList<(Vector3f Centre, Vector3f Half)> obstacles)
    {
        float near = GiantWreckNearZ;
        foreach (var (centre, h) in obstacles)
        {
            near = System.MathF.Min(near, centre.Z - h.Z - GiantWreckGap);
        }

        return new Vector3f(0f, GiantWreckFloorY + half.Y, near - half.Z);
    }

    /// <summary>Puts the hulls of the anchor system's giant wrecks into a fresh space instance (their contacts come with the
    /// station contacts, like a derelict's).</summary>
    private void AddGiantDerelictHulls(SpaceInstance instance, CelestialBody anchor)
    {
        foreach (var site in _giantDerelicts.Values.Where(s => s.SystemId == anchor.SystemId).OrderBy(s => s.Index).ToList())
        {
            if (EnsureGiantDerelict(site.Id) is not null && _playerStationCells.TryGetValue(site.Id, out var s))
            {
                instance.Structures[s.Id] = s;
            }
        }
    }

    /// <summary>A flight position's distance to a structure's hull box (its cell box, centred on its position as the flight
    /// view draws it) — 0 inside it.</summary>
    private static float HullBoxDistance(SpaceStructure s, Vector3f p)
    {
        var (min, max) = s.CellBox();
        float hx = (max.X - min.X + 1) / 2f, hy = (max.Y - min.Y + 1) / 2f, hz = (max.Z - min.Z + 1) / 2f;
        float dx = System.MathF.Max(0f, System.MathF.Abs(p.X - s.Position.X) - hx);
        float dy = System.MathF.Max(0f, System.MathF.Abs(p.Y - s.Position.Y) - hy);
        float dz = System.MathF.Max(0f, System.MathF.Abs(p.Z - s.Position.Z) - hz);
        return System.MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
