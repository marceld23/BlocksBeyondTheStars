// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Stamps 0..N procedural settlements (see <see cref="SettlementGenerator"/>) onto a planet/moon surface,
/// away from the landing pads and the crashed-ship wreck zone. How many a world gets — and how many are
/// ruins — is derived deterministically from the world seed and scales with the world's <b>hospitability</b>
/// (only worlds with an atmosphere; harsher worlds get fewer and more ruins) and its <b>size</b> (a big
/// planet holds more than a small moon), with a per-world "character" roll for high variance (some worlds
/// are crowded, some empty). Each settlement is placed on a dry, reasonably flat spot (or — on floating-island
/// worlds — on a sky island); the footprint is carved clear and given a flat foundation so it sits cleanly.
/// Intact settlements are mining-protected; ruins are left scavengeable. Interactive markers
/// (vendor/mission_board/npc/loot) become interaction points.
/// </summary>
public sealed partial class GameServer
{
    // --- count model knobs ---
    private const double SettlementRefCirc = 8000.0; // mid-planet circumference → sizeFactor 1.0
    private const double SettlementBaseDensity = 3.0; // expected settlements at H=1, size 1.0, Normal frequency, character 1.0
    private const int SettlementHardCap = 8;          // backstop only — placement/space usually caps lower
    private const int SettlementCollisionMargin = 6;  // blocks of clearance kept around pads/wreck/other settlements

    private List<SettlementInstance> _settlements => _worlds.Active.Settlements;

    /// <summary>The union of every settlement's interaction/spawn markers (vendor/mission_board/npc/loot/door)
    /// in world space — used for door registration and proximity checks.</summary>
    private List<(string Type, Vector3f Pos)> _settlementMarkers => _worlds.Active.SettlementMarkers;

    /// <summary>Interaction/spawn points across ALL stamped settlements (vendor/mission_board/npc/loot).</summary>
    public IReadOnlyList<(string Type, Vector3f Pos)> SettlementMarkers => _settlementMarkers;

    /// <summary>The "primary" settlement for single-settlement callers: the first inhabited one (or the first of
    /// any if all are ruins). Null when the world has none.</summary>
    private SettlementInstance? PrimarySettlement
        => _settlements.FirstOrDefault(s => !s.Ruined) ?? _settlements.FirstOrDefault();

    /// <summary>Name of the primary settlement (empty if none) — back-compat shim for single-settlement callers.</summary>
    public string SettlementName => PrimarySettlement?.Name ?? string.Empty;

    /// <summary>Whether at least one settlement was stamped on this world.</summary>
    public bool HasSettlement => _settlements.Count > 0;

    /// <summary>Number of settlements stamped on this world.</summary>
    public int SettlementCount => _settlements.Count;

    /// <summary>Number of inhabited (non-ruin) settlements on this world.</summary>
    public int InhabitedSettlementCount => _settlements.Count(s => !s.Ruined);

    /// <summary>Per-settlement world-space bounds + flags — test seam for placement/collision checks.</summary>
    public IReadOnlyList<(int MinX, int MinZ, int MaxX, int MaxZ, bool Ruined, bool OnIsland)> SettlementsForTest
        => _settlements.Select(s => (s.Min.X, s.Min.Z, s.Max.X, s.Max.Z, s.Ruined, s.OnIsland)).ToList();

    /// <summary>True when the world has settlements but ALL of them are ruins — back-compat shim (the primary
    /// settlement is a ruin only when there is no inhabited one).</summary>
    public bool SettlementRuined => PrimarySettlement?.Ruined ?? false;

    /// <summary>Back-compat shim for single-settlement callers (the first settlement's name).</summary>
    private string _settlementName => SettlementName;

    /// <summary>Whether any settlement is stamped — back-compat shim.</summary>
    private bool _settlementStamped => _settlements.Count > 0;

    /// <summary>Whether the first settlement is a ruin — back-compat shim.</summary>
    private bool _settlementRuined => SettlementRuined;

    /// <summary>Sends the planet's points of interest (settlements + ruins) for the world map.</summary>
    private void SendPlanetPois(PlayerSession session)
        => Send(session, new PlanetPoiList { Pois = BuildPlanetPois(session).ToArray() });

    /// <summary>Re-sends the POI list to everyone on the world (per session — chest names are localized).</summary>
    private void BroadcastPlanetPois()
    {
        foreach (var s in JoinedInActiveWorld())
        {
            SendPlanetPois(s);
        }
    }

    /// <summary>Test seam: the POI list exactly as <see cref="SendPlanetPois"/> would send it to this player.</summary>
    public IReadOnlyList<NetPoi> PlanetPoisForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s ? BuildPlanetPois(s) : new List<NetPoi>();

    /// <summary>Builds the planet's POI list for one player (world map markers + info panel).</summary>
    private List<NetPoi> BuildPlanetPois(PlayerSession session)
    {
        var pois = new List<NetPoi>();
        foreach (var s in _settlements)
        {
            pois.Add(new NetPoi
            {
                Type = s.Ruined ? "settlement_ruin" : "settlement",
                Name = s.Name,
                X = (s.Min.X + s.Max.X) * 0.5f,
                Z = (s.Min.Z + s.Max.Z) * 0.5f,
            });
        }

        // Factories: industrial halls are landmarks you go back to (the production terminal is a workstation),
        // so unlike bandit camps and monuments they belong on the map rather than staying discovery content.
        foreach (var f in _factories)
        {
            pois.Add(new NetPoi
            {
                Type = "factory",
                Name = f.Name,
                X = f.TerminalPos.X,
                Z = f.TerminalPos.Z,
            });
        }

        // Buried vault ruins (W-R3): the surface pillar rings show on the map as discovery targets.
        for (int i = 0; i < _vaultEntrances.Count; i++)
        {
            pois.Add(new NetPoi
            {
                Type = "vault_ruin",
                Name = string.Format(Localize(session.Locale, "poi.ruin"), (char)('A' + i)),
                X = _vaultEntrances[i].X,
                Z = _vaultEntrances[i].Z,
            });
        }

        // Finale (P6 Stage 2): the Guardian-core aperture is the navigation target on the finale body.
        if (_worlds.Active.HasCoreChamber)
        {
            var c = _worlds.Active.CoreChamberCenter;
            pois.Add(new NetPoi { Type = "guardian_core", Name = Localize(session.Locale, "poi.guardian_core"), X = c.X, Z = c.Z });
        }

        // NPC-hint reveals: the wreck + treasure chests stay OFF the map until a villager shares them
        // (TryEmitHint). A claimed wreck is the player's ship now; a looted chest has no container left —
        // both drop out of the list on their own.
        if (_wreckStamped && !_wreckClaimed && _meta.RevealedPois.Contains(_world.LocationId + "|wreck"))
        {
            var (wx, wz) = WreckPoiCenter();
            pois.Add(new NetPoi { Type = "wreck", Name = _wreckName, X = wx, Z = wz });
        }

        foreach (var cont in _containers)
        {
            if (cont.Id.StartsWith(ChestContainerIdPrefix, System.StringComparison.Ordinal)
                && _meta.RevealedPois.Contains(ChestRevealKey(cont)))
            {
                pois.Add(new NetPoi
                {
                    Type = "treasure",
                    Name = Localize(session.Locale, "poi.treasure"),
                    X = cont.Position.X + 0.5f,
                    Z = cont.Position.Z + 0.5f,
                });
            }
        }

        return pois;
    }

    /// <summary>A settlement chosen for placement, with everything needed to stamp + record it.</summary>
    private sealed class PlacedSettlement
    {
        public required SettlementStructure Structure;
        public required Vector3i Origin; // world cell of structure-local (0,0,0); Y = ground/island top
        public required int GroundY;
        public required string Tier;
        public required bool Ruined;
        public required bool OnIsland;
        public required string Name;
        public required System.Random Rng; // per-instance deterministic rng (loot + names)

        /// <summary>Seat style (#586): how the stamper couples this structure to the terrain —
        /// "legacy"/"flat"/"slope" (foundation + stepped skirt), "shelf" (cut &amp; fill at median height),
        /// "stilts" (platform over water on pile columns), "lava" (basalt plinth), "island" (sky deck).</summary>
        public string Seat = "legacy";
    }

    private void StampSettlement()
    {
        var planet = _world.Planet;

        // Deterministic per BODY + seed (#478): keyed by the location id, not the planet type — every rocky
        // world used to draw the same tier/ruin/character sequence, layouts and NAMES ("Karth Village"
        // everywhere), and identically-named settlements even shared NPC memory and mission-id prefixes.
        long sSeed = _meta.Seed ^ WorldGenerator.StableHash("settlement:" + _world.LocationId);
        var rng = new System.Random(unchecked((int)(sSeed ^ (sSeed >> 32))));

        // World options: the chosen settlement frequency scales the density (Off ⇒ none).
        double factor = _meta.Description.Settlements.StructureFactor();
        double h = Hospitability(planet);
        if (factor <= 0 || h <= 0)
        {
            return; // airless / no atmosphere, or settlements switched off ⇒ none
        }

        // Count = hospitability × world size × base density × frequency × a per-world "character" multiplier
        // (the character roll is the variance source: some worlds are empty, some crowded). High variance,
        // scales with how big and how liveable the world is.
        double sizeFactor = _world.Circumference / SettlementRefCirc;
        double character = RollWorldCharacter(rng);
        double lambda = h * sizeFactor * SettlementBaseDensity * factor * character;
        int requested = DrawCount(rng, lambda, SettlementHardCap);
        if (requested <= 0)
        {
            return;
        }

        var surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;

        // Reserved footprints the settlements must avoid: every landing pad (+ each player's ship sits on one)
        // and the crashed-ship wreck zone (a fixed offset from pad 0 — reserved up-front so a settlement never
        // lands where the wreck will later stamp, regardless of stamping order).
        var reserved = new List<(int Cx, int Cz, int Hw, int Hl)>();
        foreach (var pad in _landingPads)
        {
            reserved.Add((pad.CenterX, pad.CenterZ, LandingPadRadius + 2, LandingPadRadius + 2));
        }

        int pad0X = _landingPads.Count > 0 ? _landingPads[0].CenterX : 0;
        int pad0Z = _landingPads.Count > 0 ? _landingPads[0].CenterZ : 0;
        reserved.Add((pad0X - 56, pad0Z + 56, 14, 14)); // wreck zone (see GameServerWrecks.StampWreck)

        // Phase A — decide each settlement's design + a collision-free, dry/flat (or sky-island) spot.
        var placed = new List<PlacedSettlement>();
        var usedNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < requested; i++)
        {
            long instSeed = sSeed ^ unchecked((long)(i + 1) * (long)0x9E3779B97F4A7C15);
            var ir = new System.Random(unchecked((int)(instSeed ^ (instSeed >> 32))));

            string tier = RollTier(ir, h);
            bool ruined;
            SettlementStructure structure;

            var template = ir.NextDouble() < _meta.Description.SettlementTemplateUse.Probability()
                ? _content.PickSettlementTemplate(tier, _meta.Description.EnabledStructurePacks, ir)
                : null;

            if (template != null)
            {
                tier = template.Tier;
                ruined = false;
                structure = SettlementGenerator.FromTemplate(template, _content);
            }
            else
            {
                ruined = ir.NextDouble() < RuinChance(h);
                structure = SettlementGenerator.Generate(tier, ruined, instSeed, surface, _content);
            }

            bool wantIsland = planet.FloatingIslands && ir.NextDouble() < 0.5;

            // #586: pinned record → legacy re-derive → guaranteed search (fresh worlds only). The record is
            // written on whichever path runs first, so the search algorithm can evolve without moving
            // structures under existing worlds.
            Vector3i origin;
            int groundY;
            bool onIsland;
            string seat;
            string name;
            var rec = FindPlacementRecord("settlement", i);
            if (rec is not null)
            {
                if (!rec.Placed)
                {
                    continue; // decided before: this instance has no spot on this world — forever
                }

                origin = new Vector3i(rec.X, rec.GroundY, rec.Z);
                groundY = rec.GroundY;
                onIsland = rec.OnIsland;
                seat = rec.Seat;
                name = rec.Name;
                usedNames.Add(name);
            }
            else if (!_worlds.Active.VirginAtLoad)
            {
                // Legacy world (stamped before the record registry existed): the FROZEN first-fit search
                // reproduces the positions its blocks were stamped at; the outcome is recorded so future
                // loads replay instead of re-deriving.
                if (!TryPlaceSettlement(structure, ir, reserved, wantIsland, out origin, out groundY, out onIsland))
                {
                    RecordPlacementSkip("settlement", i);
                    continue;
                }

                seat = "legacy";
                name = UniqueName(SettlementDisplayName(tier, ruined, ir), usedNames);
                RecordPlacement("settlement", i, origin, groundY, onIsland, seat, name);
            }
            else
            {
                // Fresh world: the escalating search guarantees a spot (terrain adapts via the seat style).
                // It draws from its own rng lane so the shared per-instance stream stays search-independent;
                // the name comes from a dedicated lane for the same reason.
                if (!TryPlaceStructureGuaranteed(structure, RngFor(instSeed, "search"), reserved, wantIsland,
                        ruined ? SeatPolicy.Ruin : SeatPolicy.Inhabited, avoidPlayerEdits: false,
                        out origin, out groundY, out onIsland, out seat))
                {
                    RecordPlacementSkip("settlement", i); // all-lava carve-out — see TryPlaceStructureGuaranteed
                    continue;
                }

                name = UniqueName(SettlementDisplayName(tier, ruined, RngFor(instSeed, "name")), usedNames);
                RecordPlacement("settlement", i, origin, groundY, onIsland, seat, name);
            }

            placed.Add(new PlacedSettlement
            {
                Structure = structure,
                Origin = origin,
                GroundY = groundY,
                Tier = tier,
                Ruined = ruined,
                OnIsland = onIsland,
                Name = name,
                Rng = ir,
                Seat = seat,
            });
            reserved.Add((origin.X + structure.Width / 2, origin.Z + structure.Length / 2,
                structure.Width / 2 + 1, structure.Length / 2 + 1));
        }

        SavePlacementRecords();
        ReportStamp("settlement", requested, placed.Count);
        if (placed.Count == 0)
        {
            return;
        }

        // Phase B — stamp every settlement's voxels in ONE transaction (hundreds–thousands of cells each).
        _repo.RunInTransaction(() =>
        {
            foreach (var p in placed)
            {
                StampSettlementBlocks(p, surface);
            }
        });

        // Phase C — record instances, markers (world space), missions + ruin loot.
        _settlements.Clear();
        _settlementMarkers.Clear();
        foreach (var p in placed)
        {
            var inst = new SettlementInstance
            {
                Min = p.Origin,
                Max = new Vector3i(p.Origin.X + p.Structure.Width - 1, p.GroundY + p.Structure.Height - 1, p.Origin.Z + p.Structure.Length - 1),
                Ruined = p.Ruined,
                Tier = p.Tier,
                Name = p.Name,
                Inhabitant = p.Structure.Inhabitant,
                OnIsland = p.OnIsland,
            };

            foreach (var m in p.Structure.Markers)
            {
                var pos = new Vector3f(p.Origin.X + m.LocalPos.X + 0.5f, p.GroundY + m.LocalPos.Y + 0.5f, p.Origin.Z + m.LocalPos.Z + 0.5f);
                inst.Markers.Add((m.Type, pos));
                _settlementMarkers.Add((m.Type, pos));

                if (m.Type == "loot")
                {
                    SpawnStructureLoot("settlement", m.Type, pos, p.Rng); // ruins: scavengeable loot caches
                }
            }

            // An inhabited settlement's mission board offers an endless rolling set of gather missions: seed the
            // first window now; the per-player mission-giver window then slides it so it never runs dry.
            if (!p.Ruined && inst.Markers.Any(m => m.Type == "mission_board"))
            {
                string prefix = $"settle_{(uint)WorldGenerator.StableHash(p.Name) % 100000u}_";
                StockBoard(prefix, p.Name, inst.MissionIds, CoinGiverName(p.Name));
            }

            _settlements.Add(inst);
        }

        // Phase D — populate inhabited settlements with NPCs and hang real doors in the doorways.
        SpawnSettlementNpcs(rng);
        RegisterDoors();

        int ruins = _settlements.Count(s => s.Ruined);
        _log.Info($"Stamped {placed.Count}/{requested} settlement(s) on '{_world.LocationId}' " +
                  $"({_settlements.Count - ruins} inhabited, {ruins} ruined; H={h:F2}, size={sizeFactor:F2}, char={character:F1}).");
    }

    /// <summary>Carves the footprint clear of terrain, lays a flat foundation, then stamps the structure's blocks.
    /// The seat style (#586) picks how the foundation couples to the terrain: legacy/flat/slope keep the classic
    /// foundation + stepped skirt, shelf cuts into rugged relief and fills with stone, stilts raise a platform
    /// over water on pile columns, lava raises a basalt plinth above a lava sheet, island keeps the floating
    /// deck. Must run inside a repo transaction (called once per settlement from the batched stamp).</summary>
    private void StampSettlementBlocks(PlacedSettlement p, string surface)
    {
        var s = p.Structure;
        int gy = p.GroundY;
        var origin = p.Origin;
        bool shelf = p.Seat == "shelf";
        bool stilts = p.Seat == "stilts";
        bool lavaSeat = p.Seat == "lava";
        var foundationId = _content.GetBlock(surface)?.NumericId ?? BlockId.Air;

        // Seat materials. The shelf fills with the biome's sub-surface block so the cut reads as bedrock, a
        // lava plinth is basalt, stilt piles are logs for inhabited builds and weathered stone for ruins.
        var planet = _world.Planet;
        string sub = planet.Biomes.Count > 0 ? planet.Biomes[0].SubSurfaceBlock : planet.SubSurfaceBlock;
        var shelfFillId = _content.GetBlock(sub)?.NumericId ?? foundationId;
        var basaltId = (_content.GetBlock("basalt") ?? _content.GetBlock("stone"))?.NumericId ?? foundationId;
        var pileId = p.Ruined
            ? (_content.GetBlock("stone")?.NumericId ?? foundationId)
            : (_content.GetBlock("wood_log") ?? _content.GetBlock("stone"))?.NumericId ?? foundationId;
        var floorId = lavaSeat ? basaltId : stilts && !p.Ruined ? pileId : foundationId;

        // Carve only as high as terrain actually rises above the foundation. The classic gates guarantee a
        // low spread, so clearing the structure's FULL height (up to 128 for a hand-authored tower) would be
        // millions of pointless air writes; a SHELF seat sits in genuinely rugged relief, so its cut may run
        // higher than the buildings to open the mountainside above them. Sample the footprint coarsely to
        // find the intrusion height. (On a sky island the ground is far below, so maxSurf - gy goes negative
        // and the carve collapses to the minimum.)
        int maxSurf = gy;
        for (int x = 0; x < s.Width; x += 8)
            for (int z = 0; z < s.Length; z += 8)
            {
                maxSurf = System.Math.Max(maxSurf, _generator.SurfaceHeight(planet, origin.X + x, origin.Z + z));
            }

        int clearH = System.Math.Clamp(maxSurf - gy + 3, 2, shelf ? System.Math.Max(s.Height, 96) : s.Height);

        // 1) Clear any terrain occupying the build volume above the foundation, so a hill never buries the
        //    buildings (the structure's own air cells are otherwise left as whatever was there).
        for (int x = 0; x < s.Width; x++)
            for (int z = 0; z < s.Length; z++)
                for (int y = 1; y < clearH; y++)
                {
                    _world.SetBlock(new Vector3i(origin.X + x, gy + y, origin.Z + z), BlockId.Air);
                }

        // 2) Foundation row + support skirt. The buildings are authored on one flat plane, so the floor at gy
        //    must stay level — but on a slope a single flat slab would hang in mid-air on the downhill side. So
        //    each column also gets a stepped plinth: solid fill from gy down to the natural surface, deep on the
        //    downhill side, shallow uphill. The result is a real multi-level foundation that meets the ground
        //    all the way round instead of a flat platform floating over a dip. (Skipped on sky islands, whose
        //    deck is meant to float over the void.) Depth is capped so a missed crevasse can't fill a chasm —
        //    a SHELF seat, cut into rugged relief on purpose, gets double the cap. A STILTS seat swaps the
        //    solid fill for pile columns on a sparse grid over the wet cells (a platform, not a dam).
        if (!floorId.IsAir)
        {
            int maxSkirt = shelf ? 96 : 48;
            var skirtId = shelf ? shelfFillId : lavaSeat ? basaltId : foundationId;
            for (int x = 0; x < s.Width; x++)
                for (int z = 0; z < s.Length; z++)
                {
                    int wx = origin.X + x, wz = origin.Z + z;
                    _world.SetBlock(new Vector3i(wx, gy, wz), floorId); // flat floor row

                    if (p.OnIsland)
                    {
                        continue;
                    }

                    if (stilts && _generator.TryGetWaterSurface(planet, wx, wz, out _, out int seabedY))
                    {
                        // Wet column: no solid fill — a pile column every few cells plus the footprint rim
                        // carries the platform down to the seabed; the water flows on beneath the deck.
                        bool pile = (x % 3 == 0 && z % 3 == 0)
                            || ((x == 0 || x == s.Width - 1 || z == 0 || z == s.Length - 1) && (x + z) % 3 == 0);
                        if (pile)
                        {
                            int pileFloor = System.Math.Max(seabedY + 1, gy - maxSkirt);
                            for (int y = gy - 1; y >= pileFloor; y--)
                            {
                                _world.SetBlock(new Vector3i(wx, y, wz), pileId);
                            }
                        }

                        continue;
                    }

                    int colSurf = _generator.SurfaceHeight(planet, wx, wz);
                    int floorY = System.Math.Max(colSurf + 1, gy - maxSkirt);
                    for (int y = gy - 1; y >= floorY; y--) // fill the gap down to the natural ground
                    {
                        _world.SetBlock(new Vector3i(wx, y, wz), skirtId);
                    }
                }

            // Slope/shelf seats get a stepped apron around the plinth so no sheer foundation wall meets the
            // ground (#586) — one terrace ring per step, only where the plinth actually stands proud.
            if (p.Seat is "slope" or "shelf")
            {
                StampFoundationApron(p, skirtId);
            }
        }

        // 3) Stamp the structure above the foundation (y=0 of the structure is the foundation row).
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    ushort b = s.Get(x, y, z);
                    if (b != 0)
                    {
                        var (tint, glow) = s.GetModifier(x, y, z);
                        _world.SetBlock(new Vector3i(origin.X + x, gy + y, origin.Z + z),
                            new BlockId(b), tint, glow, s.GetShape(x, y, z));
                    }
                }
    }

    // --- placement allocator -------------------------------------------------------------------------------

    /// <summary>Finds a collision-free spot for a settlement: a ring of deterministic candidates around the home
    /// landing pad, each accepted only if it clears every reserved footprint (pads/wreck/other settlements) and
    /// — for a ground settlement — is dry and reasonably flat, or — for a sky settlement — sits on a floating
    /// island deck that covers the whole footprint. Returns false if no candidate fits.
    /// <paramref name="avoidPlayerEdits"/> additionally rejects a footprint that already holds player-built
    /// blocks (#527): a feature added in a later release stamps into worlds people have long since settled,
    /// and the reserved rects know nothing about their bases.</summary>
    private bool TryPlaceSettlement(SettlementStructure s, System.Random rng,
        List<(int Cx, int Cz, int Hw, int Hl)> reserved, bool wantIsland,
        out Vector3i origin, out int groundY, out bool onIsland, bool avoidPlayerEdits = false)
    {
        origin = default;
        groundY = 0;
        onIsland = false;

        var planet = _world.Planet;
        int circ = _world.Circumference;
        int latP = WorldConstants.LatitudePeriodFor(circ);
        int w = s.Width, l = s.Length;
        int hw = w / 2 + 1, hl = l / 2 + 1;
        int latBand = System.Math.Max(8, latP / 2 - System.Math.Max(w, l) / 2 - 16);
        int pad0X = _landingPads.Count > 0 ? _landingPads[0].CenterX : 0;
        int pad0Z = _landingPads.Count > 0 ? _landingPads[0].CenterZ : 0;
        int maxDist = System.Math.Max(80, (int)(circ * 0.4));

        // A bigger build seats on a bigger, carved + foundationed footprint, so it tolerates more terrain
        // relief than a small hut; and a large footprint can't fully cover a (small) floating island, so big
        // builds always go on the ground. Both gates scale with the footprint so hand-authored 128² structures
        // can actually find a home, while small settlements keep their tight, must-be-flat seating.
        int maxSpread = System.Math.Clamp(System.Math.Max(w, l) / 8, 8, 24);
        bool canIsland = wantIsland && System.Math.Max(w, l) <= 40;

        // Larger footprints are harder to fit, so give the search more candidate spots before giving up.
        int attempts = System.Math.Max(w, l) > 48 ? 160 : 64;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            double ang = rng.NextDouble() * System.Math.PI * 2.0;
            int dist = 40 + rng.Next(0, maxDist);
            int cx = pad0X + (int)System.Math.Round(System.Math.Cos(ang) * dist);
            int cz = System.Math.Clamp(pad0Z + (int)System.Math.Round(System.Math.Sin(ang) * dist), -latBand, latBand);

            if (OverlapsFootprint(cx, cz, hw, hl, reserved, SettlementCollisionMargin))
            {
                continue;
            }

            int ox = cx - w / 2, oz = cz - l / 2;

            if (canIsland)
            {
                if (TryIslandFootprint(planet, ox, oz, w, l, out int itop)
                    && !(avoidPlayerEdits && FootprintHasPlayerEdits(ox, oz, itop, w, s.Height, l)))
                {
                    origin = new Vector3i(ox, itop, oz);
                    groundY = itop;
                    onIsland = true;
                    return true;
                }

                continue; // wanted a sky island here but the footprint isn't fully on one
            }

            if (FootprintWet(planet, ox, oz, w, l) || FootprintSpread(planet, ox, oz, w, l) > maxSpread)
            {
                continue; // in water/lava, or on terrain too uneven to seat the build
            }

            int gy = _generator.SurfaceHeight(planet, cx, cz);
            if (avoidPlayerEdits && FootprintHasPlayerEdits(ox, oz, gy, w, s.Height, l))
            {
                continue; // somebody already built here — pick another spot rather than bulldoze it
            }

            origin = new Vector3i(ox, gy, oz);
            groundY = gy;
            onIsland = false;
            return true;
        }

        return false;
    }

    // --- guaranteed placement (#586) ------------------------------------------------------------------------

    /// <summary>Which seatings a structure kind tolerates: inhabited builds may stand on stilts but never in
    /// lava; ruins and monuments may do both (a drowned ruin / a dead relic in a lava plain); factories and
    /// camps stay on dry land, and camps prefer rugged ground when the search has to escalate (they hide).</summary>
    private readonly record struct SeatPolicy(bool AllowStilts, bool AllowLava, bool PreferRugged)
    {
        public static SeatPolicy Inhabited => new(true, false, false);
        public static SeatPolicy Ruin => new(true, true, false);
        public static SeatPolicy Factory => new(false, false, false);
        public static SeatPolicy Camp => new(false, false, true);
        public static SeatPolicy Monument => new(false, true, false);
    }

    /// <summary>Footprint statistics for one candidate spot, gathered over the sample columns.</summary>
    private sealed class SeatCandidate
    {
        public int Cx, Cz;              // footprint centre (world)
        public int Spread;              // max-min ground height over the samples
        public int MedianY;             // median ground height (shelf seat level)
        public int MaxY;                // highest ground sample (lava plinth clears the sheet from here)
        public int WetSamples;          // water-covered sample columns
        public int LavaSamples;         // lava-covered sample columns
        public int Samples;             // total sample columns
        public int WaterTop = int.MinValue; // highest water surface over the wet samples
    }

    /// <summary>A deterministic rng lane per instance seed, so the guaranteed search and the display name
    /// never share draws with the legacy per-instance stream — the search can then grow or shrink its
    /// consumption freely without shifting any other roll (#586).</summary>
    private static System.Random RngFor(long instSeed, string lane)
    {
        long s = instSeed ^ WorldGenerator.StableHash(lane);
        return new System.Random(unchecked((int)(s ^ (s >> 32))));
    }

    /// <summary>The pinned placement record for a structure instance on the active world, or null.</summary>
    private StructurePlacementRecord? FindPlacementRecord(string kind, int index)
        => _meta.Placements.Find(r => r.LocationId == _world.LocationId && r.Kind == kind && r.Index == index);

    private bool _placementRecordsDirty;

    /// <summary>Pins where a structure instance landed (#586). Batched — call
    /// <see cref="SavePlacementRecords"/> once per stamper after its loop.</summary>
    private void RecordPlacement(string kind, int index, Vector3i origin, int groundY, bool onIsland,
        string seat, string name)
    {
        var rec = FindPlacementRecord(kind, index);
        if (rec is null)
        {
            rec = new StructurePlacementRecord { LocationId = _world.LocationId, Kind = kind, Index = index };
            _meta.Placements.Add(rec);
        }

        rec.Placed = true;
        rec.X = origin.X;
        rec.GroundY = groundY;
        rec.Z = origin.Z;
        rec.OnIsland = onIsland;
        rec.Seat = seat;
        rec.Name = name;
        _placementRecordsDirty = true;
    }

    /// <summary>Pins the decision that an instance found NO spot (legacy worlds / the all-lava carve-out),
    /// so later loads don't re-roll it.</summary>
    private void RecordPlacementSkip(string kind, int index)
    {
        if (FindPlacementRecord(kind, index) is not null)
        {
            return;
        }

        _meta.Placements.Add(new StructurePlacementRecord
        {
            LocationId = _world.LocationId,
            Kind = kind,
            Index = index,
            Placed = false,
        });
        _placementRecordsDirty = true;
    }

    private void SavePlacementRecords()
    {
        if (_placementRecordsDirty)
        {
            _placementRecordsDirty = false;
            _repo.SaveMetadata(_meta);
        }
    }

    /// <summary>Per-kind requested/placed telemetry: a drop is a WARN — with the guaranteed search it should
    /// only ever fire on legacy worlds and the documented all-lava carve-out.</summary>
    private void ReportStamp(string kind, int requested, int placedCount)
    {
        _worlds.Active.StampReport.Add((kind, requested, placedCount));
        if (placedCount < requested)
        {
            _log.Warn($"Placed only {placedCount}/{requested} {kind}(s) on '{_world.LocationId}'.");
        }
    }

    /// <summary>Test seam: this load's per-kind requested/placed stamp report.</summary>
    public IReadOnlyList<(string Kind, int Requested, int Placed)> StampReportForTest
        => _worlds.Active.StampReport;

    /// <summary>Test seam: the pinned placement records of the active world.</summary>
    public IReadOnlyList<StructurePlacementRecord> PlacementRecordsForTest
        => _meta.Placements.Where(r => r.LocationId == _world.LocationId).ToList();

    /// <summary>The guaranteed placement search (#586): first the classic gates (ring 1 — first-fit on dry,
    /// flat ground, no visual change where they succeed), then widening best-fit rings that rank every seen
    /// candidate (dry lowest-spread first, then water for stilt-capable kinds, lava last for lava-capable
    /// kinds), then a terminal pass with the collision margin relaxed 6 → 2, and finally a deterministic
    /// longitude sweep. The chosen spot is classified into a seat style; the stamper adapts the terrain to
    /// it. Returns false only when policy forbids every reachable column (in practice: an all-lava ring for
    /// a kind that may not stand in lava) — the one documented carve-out from the guarantee. Player-edit
    /// avoidance and pad/settlement reservations are never relaxed beyond margin 2.</summary>
    private bool TryPlaceStructureGuaranteed(SettlementStructure s, System.Random rng,
        List<(int Cx, int Cz, int Hw, int Hl)> reserved, bool wantIsland, SeatPolicy policy,
        bool avoidPlayerEdits, out Vector3i origin, out int groundY, out bool onIsland, out string seat)
    {
        origin = default;
        groundY = 0;
        onIsland = false;
        seat = "flat";

        var planet = _world.Planet;
        int circ = _world.Circumference;
        int latP = WorldConstants.LatitudePeriodFor(circ);
        int w = s.Width, l = s.Length;
        int hw = w / 2 + 1, hl = l / 2 + 1;
        int latBand = System.Math.Max(8, latP / 2 - System.Math.Max(w, l) / 2 - 16);
        int pad0X = _landingPads.Count > 0 ? _landingPads[0].CenterX : 0;
        int pad0Z = _landingPads.Count > 0 ? _landingPads[0].CenterZ : 0;
        int baseDist = System.Math.Max(80, (int)(circ * 0.4));
        int maxSpread = System.Math.Clamp(System.Math.Max(w, l) / 8, 8, 24);
        bool canIsland = wantIsland && System.Math.Max(w, l) <= 40;

        bool Blocked(int cx, int cz, int margin)
            => OverlapsFootprint(cx, cz, hw, hl, reserved, margin);

        bool EditGate(int ox, int oz, int gy)
            => avoidPlayerEdits && FootprintHasPlayerEdits(ox, oz, gy, w, s.Height, l);

        // Ring 1 — the classic gates, first-fit. Where they succeed nothing changes visually.
        int attempts = System.Math.Max(w, l) > 48 ? 160 : 64;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            double ang = rng.NextDouble() * System.Math.PI * 2.0;
            int dist = 40 + rng.Next(0, baseDist);
            int cx = pad0X + (int)System.Math.Round(System.Math.Cos(ang) * dist);
            int cz = System.Math.Clamp(pad0Z + (int)System.Math.Round(System.Math.Sin(ang) * dist), -latBand, latBand);
            if (Blocked(cx, cz, SettlementCollisionMargin))
            {
                continue;
            }

            int ox = cx - w / 2, oz = cz - l / 2;
            if (canIsland)
            {
                if (TryIslandFootprint(planet, ox, oz, w, l, out int itop) && !EditGate(ox, oz, itop))
                {
                    origin = new Vector3i(ox, itop, oz);
                    groundY = itop;
                    onIsland = true;
                    seat = "island";
                    return true;
                }

                continue;
            }

            if (FootprintWet(planet, ox, oz, w, l) || FootprintSpread(planet, ox, oz, w, l) > maxSpread)
            {
                continue;
            }

            int gy = _generator.SurfaceHeight(planet, cx, cz);
            if (EditGate(ox, oz, gy))
            {
                continue;
            }

            origin = new Vector3i(ox, gy, oz);
            groundY = gy;
            seat = FootprintSpread(planet, ox, oz, w, l) <= 4 ? "flat" : "slope";
            return true;
        }

        // Rings 2+ — collect + rank. Widening rings, then a terminal margin-2 ring; the best candidate seen
        // anywhere wins. Ranking tiers: dry (spread asc — desc for rugged-preferring kinds), then water for
        // stilt-capable kinds (least wet first), then water for everyone (function over matrix — better a
        // dry-land kind on stilts than a world missing its rolled structure), then lava if allowed.
        SeatCandidate? best = null;
        int bestTier = int.MaxValue, bestScore = int.MaxValue;

        void Consider(SeatCandidate c)
        {
            int tier;
            int score;
            if (c.LavaSamples > 0)
            {
                if (!policy.AllowLava)
                {
                    return; // lava is the one absolute no-go for kinds that may not stand in it
                }

                tier = 3;
                score = c.LavaSamples * 1000 / System.Math.Max(1, c.Samples);
            }
            else if (c.WetSamples > 0)
            {
                tier = policy.AllowStilts ? 1 : 2;
                score = c.WetSamples * 1000 / System.Math.Max(1, c.Samples);
            }
            else
            {
                tier = 0;
                score = policy.PreferRugged ? -c.Spread : c.Spread;
            }

            if (tier < bestTier || (tier == bestTier && score < bestScore))
            {
                best = c;
                bestTier = tier;
                bestScore = score;
            }
        }

        foreach (var (scale, margin, tries) in new[] { (1.0, 6, attempts), (1.5, 6, attempts), (2.0, 6, attempts), (2.0, 2, attempts * 2) })
        {
            int maxDist = System.Math.Min(circ / 2, (int)(baseDist * scale));
            for (int attempt = 0; attempt < tries; attempt++)
            {
                double ang = rng.NextDouble() * System.Math.PI * 2.0;
                int dist = 40 + rng.Next(0, maxDist);
                int cx = pad0X + (int)System.Math.Round(System.Math.Cos(ang) * dist);
                int cz = System.Math.Clamp(pad0Z + (int)System.Math.Round(System.Math.Sin(ang) * dist), -latBand, latBand);
                if (Blocked(cx, cz, margin))
                {
                    continue;
                }

                var c = EvaluateFootprint(planet, cx - w / 2, cz - l / 2, w, l);
                c.Cx = cx;
                c.Cz = cz;
                if (EditGate(cx - w / 2, cz - l / 2, c.MedianY))
                {
                    continue;
                }

                Consider(c);
            }

            if (best is not null && bestTier == 0)
            {
                break; // a dry spot in a tighter ring beats walking further out
            }
        }

        // Terminal sweep — deterministic longitude walk on three latitude lines. Only reachable when even
        // the widened rings found nothing rankable (tiny bodies saturated with reservations).
        if (best is null)
        {
            foreach (int cz in new[] { 0, latBand / 2, -latBand / 2 })
            {
                for (int cx = pad0X + 40; cx < pad0X + circ - 40 && best is null; cx += 16)
                {
                    int wxc = WorldConstants.WrapX(cx, circ);
                    if (Blocked(wxc, cz, 2))
                    {
                        continue;
                    }

                    var c = EvaluateFootprint(planet, wxc - w / 2, cz - l / 2, w, l);
                    c.Cx = wxc;
                    c.Cz = cz;
                    if (EditGate(wxc - w / 2, cz - l / 2, c.MedianY))
                    {
                        continue;
                    }

                    Consider(c);
                }
            }
        }

        if (best is null)
        {
            return false; // the documented carve-out (e.g. an all-lava surface for a no-lava kind)
        }

        // Classify the winning footprint into a seat style + its ground level.
        var bc = best;
        int bx = bc.Cx - w / 2, bz = bc.Cz - l / 2;
        if (bc.LavaSamples > 0)
        {
            seat = "lava";
            groundY = bc.MaxY + 2; // the basalt plinth clears the lava sheet
        }
        else if (bc.WetSamples > 0)
        {
            seat = "stilts";
            groundY = bc.WaterTop + 1; // platform just above the water line
        }
        else if (bc.Spread <= 4)
        {
            seat = "flat";
            groundY = _generator.SurfaceHeight(planet, bc.Cx, bc.Cz);
        }
        else if (bc.Spread <= maxSpread)
        {
            seat = "slope";
            groundY = _generator.SurfaceHeight(planet, bc.Cx, bc.Cz);
        }
        else
        {
            seat = "shelf";
            groundY = bc.MedianY; // cut & fill around the median so the shelf splits the relief evenly
        }

        origin = new Vector3i(bx, groundY, bz);
        return true;
    }

    /// <summary>Gathers footprint statistics (spread/median over ground heights, wet/lava sample counts,
    /// highest water surface) for the seat classification — one pass over the standard sample columns.</summary>
    private SeatCandidate EvaluateFootprint(PlanetType planet, int ox, int oz, int w, int l)
    {
        var c = new SeatCandidate();
        var heights = new List<int>();
        foreach (var (x, z) in FootprintSamples(ox, oz, w, l))
        {
            c.Samples++;
            if (_generator.IsSurfaceLava(planet, x, z))
            {
                c.LavaSamples++;
            }
            else if (_generator.TryGetWaterSurface(planet, x, z, out int waterTop, out _))
            {
                c.WetSamples++;
                c.WaterTop = System.Math.Max(c.WaterTop, waterTop);
            }

            heights.Add(_generator.SurfaceHeight(planet, x, z));
        }

        heights.Sort();
        c.MedianY = heights[heights.Count / 2];
        c.MaxY = heights[^1];
        c.Spread = heights[^1] - heights[0];
        return c;
    }

    /// <summary>Stamps a stepped apron around a slope/shelf plinth (#586): terrace rings just outside the
    /// footprint, one step lower per ring, filled a few blocks down to the natural ground — so the foundation
    /// meets the terrain as steps instead of a sheer wall.</summary>
    private void StampFoundationApron(PlacedSettlement p, BlockId fill)
    {
        if (fill.IsAir)
        {
            return;
        }

        var planet = _world.Planet;
        var s = p.Structure;
        int gy = p.GroundY;
        for (int ring = 1; ring <= 2; ring++)
        {
            int stepY = gy - ring;
            int x0 = p.Origin.X - ring, x1 = p.Origin.X + s.Width - 1 + ring;
            int z0 = p.Origin.Z - ring, z1 = p.Origin.Z + s.Length - 1 + ring;
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    if (x != x0 && x != x1 && z != z0 && z != z1)
                    {
                        continue; // ring perimeter only
                    }

                    int colSurf = _generator.SurfaceHeight(planet, x, z);
                    if (colSurf >= stepY || _generator.IsSurfaceWater(planet, x, z) || _generator.IsSurfaceLava(planet, x, z))
                    {
                        continue; // uphill side / wet ground — no terrace needed or wanted
                    }

                    int floorY = System.Math.Max(colSurf + 1, stepY - 4); // short footing, not another wall
                    for (int y = stepY; y >= floorY; y--)
                    {
                        _world.SetBlock(new Vector3i(x, y, z), fill);
                    }
                }
        }
    }

    /// <summary>True if any cell in the build volume carries a player-authored block edit (#527). Worldgen
    /// stamps, fluid flow, fire and flora regrowth all write with an empty owner, so only real player builds
    /// (and player mining) match — which is exactly the property that must not be bulldozed by a feature
    /// added after the world was settled. One bounded repo query per candidate (two across the seam), run
    /// only after the cheap terrain gates already passed.</summary>
    private bool FootprintHasPlayerEdits(int ox, int oz, int gy, int w, int h, int l)
    {
        const int Margin = 2; // a relic must not crowd a build either
        int circ = _world.Circumference;
        int minY = gy - 2, maxY = gy + h;
        int minZ = oz - Margin, maxZ = oz + l + Margin;
        int x0 = WorldConstants.WrapX(ox - Margin, circ);
        int span = w + 2 * Margin;

        // Stored X is canonical [0, circ), so a footprint straddling the seam needs the two halves queried
        // separately rather than one range that wraps past the end.
        if (x0 + span < circ)
        {
            return _repo.HasPlayerBlockEdits(_world.LocationId,
                new Vector3i(x0, minY, minZ), new Vector3i(x0 + span, maxY, maxZ));
        }

        return _repo.HasPlayerBlockEdits(_world.LocationId,
                   new Vector3i(x0, minY, minZ), new Vector3i(circ - 1, maxY, maxZ))
               || _repo.HasPlayerBlockEdits(_world.LocationId,
                   new Vector3i(0, minY, minZ), new Vector3i(x0 + span - circ, maxY, maxZ));
    }

    /// <summary>Footprint sample columns (world coords) used by the wet + flatness gates. A small footprint
    /// samples a fixed 3×3 (corners, edge mid-points, centre); a large one samples a denser grid (≈ every 16
    /// blocks) so a lake or a steep slope buried in the middle of a 128² footprint can't slip past the gates.</summary>
    private static IEnumerable<(int X, int Z)> FootprintSamples(int ox, int oz, int w, int l)
    {
        int nx = System.Math.Clamp(w / 16 + 1, 2, 9);
        int nz = System.Math.Clamp(l / 16 + 1, 2, 9);
        for (int ix = 0; ix <= nx; ix++)
        {
            int x = ox + (int)((long)(w - 1) * ix / nx);
            for (int iz = 0; iz <= nz; iz++)
            {
                int z = oz + (int)((long)(l - 1) * iz / nz);
                yield return (x, z);
            }
        }
    }

    private bool FootprintWet(PlanetType planet, int ox, int oz, int w, int l)
    {
        foreach (var (x, z) in FootprintSamples(ox, oz, w, l))
        {
            if (_generator.IsSurfaceWater(planet, x, z) || _generator.IsSurfaceLava(planet, x, z))
            {
                return true;
            }
        }

        return false;
    }

    private int FootprintSpread(PlanetType planet, int ox, int oz, int w, int l)
    {
        int min = int.MaxValue, max = int.MinValue;
        foreach (var (x, z) in FootprintSamples(ox, oz, w, l))
        {
            int y = _generator.SurfaceHeight(planet, x, z);
            min = System.Math.Min(min, y);
            max = System.Math.Max(max, y);
        }

        return max - min;
    }

    /// <summary>True if a floating sky island covers the WHOLE footprint with a near-level deck; outputs the
    /// (min) deck top to seat the settlement on.</summary>
    private bool TryIslandFootprint(PlanetType planet, int ox, int oz, int w, int l, out int top)
    {
        top = int.MinValue;
        int min = int.MaxValue, max = int.MinValue;
        foreach (var (x, z) in FootprintSamples(ox, oz, w, l))
        {
            int t = _generator.FloatingIslandTop(planet, x, z);
            if (t == int.MinValue)
            {
                return false; // a gap — the island doesn't cover the whole footprint
            }

            min = System.Math.Min(min, t);
            max = System.Math.Max(max, t);
        }

        if (max - min > 2)
        {
            return false; // deck too uneven to seat a build
        }

        top = min;
        return true;
    }

    /// <summary>True if a candidate footprint (centre + half-extents) overlaps any reserved footprint within a
    /// margin, wrapping on longitude.</summary>
    private bool OverlapsFootprint(int cx, int cz, int hw, int hl, List<(int Cx, int Cz, int Hw, int Hl)> rects, int margin)
    {
        int circ = _world.Circumference;
        foreach (var r in rects)
        {
            int dx = System.Math.Abs(WorldConstants.WrapDeltaX(cx - r.Cx, circ));
            int dz = System.Math.Abs(cz - r.Cz);
            if (dx < hw + r.Hw + margin && dz < hl + r.Hl + margin)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if a point (or a small area around it) lies inside any stamped settlement's footprint —
    /// used by other surface stampers (wrecks/vaults/data cubes) to keep clear of settlements.</summary>
    public bool OverlapsAnySettlement(int x, int z, int halfExtent = 0)
    {
        int circ = _world.Circumference;
        foreach (var s in _settlements)
        {
            int scx = (s.Min.X + s.Max.X) / 2, scz = (s.Min.Z + s.Max.Z) / 2;
            int shw = (s.Max.X - s.Min.X) / 2 + 1, shl = (s.Max.Z - s.Min.Z) / 2 + 1;
            int dx = System.Math.Abs(WorldConstants.WrapDeltaX(x - scx, circ));
            int dz = System.Math.Abs(z - scz);
            if (dx < shw + halfExtent + SettlementCollisionMargin && dz < shl + halfExtent + SettlementCollisionMargin)
            {
                return true;
            }
        }

        return false;
    }

    // --- count + balance model ----------------------------------------------------------------------------

    /// <summary>How liveable a world is, 0..1 — airless worlds 0 (no atmosphere ⇒ no settlements). Drives both
    /// how many settlements a world gets and how likely each is a ruin (harsher ⇒ fewer + more ruins).</summary>
    private static double Hospitability(PlanetType p)
    {
        if (p.IsAirless)
        {
            return 0.0; // no atmosphere — uninhabited (airless moons/asteroids/etc.)
        }

        double atm = string.Equals(p.Atmosphere, "breathable", System.StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.45;
        double fauna = (p.CreatureAbundance ?? "few").ToLowerInvariant() switch
        {
            "many" => 1.0,
            "none" => 0.15,
            _ => 0.5,
        };
        double climate = 1.0 - System.Math.Min(1.0, System.Math.Abs(p.BaseTemperature - 15.0) / 60.0);
        double water = (p.WaterAbundance ?? 0.0) > 0.1 ? 0.1 : 0.0;
        return System.Math.Clamp(atm * 0.5 + fauna * 0.3 + climate * 0.2 + water, 0.0, 1.0);
    }

    /// <summary>Per-world "character" multiplier — a weighted mixture that makes worlds differ a lot: some are
    /// empty (×0), most ordinary, a few boom towns. This overdispersion is the variance source.</summary>
    private static double RollWorldCharacter(System.Random rng)
    {
        double r = rng.NextDouble();
        if (r < 0.12) return 0.0;  // lonely — no settlements at all
        if (r < 0.32) return 0.4;  // sparse
        if (r < 0.72) return 1.0;  // normal
        if (r < 0.92) return 1.7;  // busy
        return 2.6;                 // boom
    }

    /// <summary>Draws an integer settlement count around an expected value with a natural spread (a sum of
    /// Bernoulli slots), clamped to a hard cap.</summary>
    private static int DrawCount(System.Random rng, double lambda, int hardCap)
    {
        if (lambda <= 0)
        {
            return 0;
        }

        const int slots = 12;
        double pp = System.Math.Min(0.95, lambda / slots);
        int n = 0;
        for (int i = 0; i < slots; i++)
        {
            if (rng.NextDouble() < pp)
            {
                n++;
            }
        }

        return System.Math.Min(hardCap, n);
    }

    /// <summary>Per-settlement ruin probability — harsher worlds are mostly ruins, liveable ones mostly inhabited.</summary>
    private static double RuinChance(double hospitability)
        => System.Math.Clamp(0.15 + (1.0 - hospitability) * 0.7, 0.05, 0.9);

    /// <summary>Picks a settlement size tier weighted by hospitability: liveable worlds skew toward towns/cities,
    /// harsh worlds toward hamlets/villages.</summary>
    private static string RollTier(System.Random rng, double h)
    {
        double city = 0.10 + h * 0.20;     // 0.10 .. 0.30
        double town = 0.30 + h * 0.10;     // 0.30 .. 0.40
        double hamlet = System.Math.Max(0.05, 0.15 - h * 0.08); // more hamlets on harsh worlds
        double village = System.Math.Max(0.05, 1.0 - city - town - hamlet);

        double r = rng.NextDouble() * (city + town + hamlet + village);
        if (r < city) return "city";
        r -= city;
        if (r < town) return "town";
        r -= town;
        if (r < hamlet) return "hamlet";
        return "village";
    }

    /// <summary>Ensures a settlement's display name is unique on this world (so mission boards + NPC memory keys
    /// don't collide); appends a Roman numeral on a clash.</summary>
    private static string UniqueName(string name, HashSet<string> used)
    {
        if (used.Add(name))
        {
            return name;
        }

        for (int n = 2; n < 50; n++)
        {
            string candidate = name + " " + Roman(n);
            if (used.Add(candidate))
            {
                return candidate;
            }
        }

        return name; // give up gracefully (extremely unlikely)
    }

    private static string Roman(int n) => n switch
    {
        2 => "II",
        3 => "III",
        4 => "IV",
        5 => "V",
        6 => "VI",
        7 => "VII",
        8 => "VIII",
        9 => "IX",
        _ => n.ToString(),
    };

    /// <summary>The settlement a board mission id belongs to (its <c>settle_&lt;hash&gt;_</c> prefix), or null.</summary>
    private SettlementInstance? SettlementForBoardMission(string missionId)
    {
        foreach (var s in _settlements)
        {
            if (string.IsNullOrEmpty(s.Name))
            {
                continue;
            }

            if (missionId.StartsWith($"settle_{(uint)WorldGenerator.StableHash(s.Name) % 100000u}_", System.StringComparison.Ordinal))
            {
                return s;
            }
        }

        return null;
    }

    private static string SettlementDisplayName(string tier, bool ruined, System.Random rng)
    {
        string[] roots = { "Karth", "Vega", "Mira", "Dorn", "Ysel", "Tarn", "Olun", "Reth", "Sabik", "Cael" };
        string[] citySuffix = { " City", " Metropolis", " Prime", " Central" };
        string[] townSuffix = { " Town", " Colony", " Outpost", " Heights" };
        string[] hamletSuffix = { " Hamlet", " Cross", " Camp", " Rest" };
        string[] villageSuffix = { " Village", " Hollow", " Glen", " Stead", " End" };
        string[] suffixes = tier switch
        {
            "city" => citySuffix,
            "town" => townSuffix,
            "hamlet" => hamletSuffix,
            _ => villageSuffix,
        };
        string root = roots[rng.Next(roots.Length)];
        string suffix = suffixes[rng.Next(suffixes.Length)];
        return ruined ? $"Ruins of {root}{suffix}" : $"{root}{suffix}";
    }

    private const float SettlementVendorReach = 4f;
    private const float SettlementBoardReach = 4f;

    /// <summary>The four settlement trade professions. A settlement's profession (derived deterministically from
    /// its name) themes its NPCs and decides which market goods its vendor posts.</summary>
    private static readonly string[] SettlementTrades = { "miners", "traders", "researchers", "settlers" };

    /// <summary>Deterministic trade profession for a settlement (stable from its name), so a mining village and a
    /// trade city always offer their own distinct barter — and both the NPC theme and the market filter agree.</summary>
    private static string SettlementTradeFor(string name)
        => string.IsNullOrEmpty(name)
            ? "settlers"
            : SettlementTrades[(uint)WorldGenerator.StableHash(name) % (uint)SettlementTrades.Length];

    /// <summary>The trade profession for one vendor among several at a location (B55): the first vendor keeps the
    /// location's own theme (so the place keeps its identity), and each additional vendor gets its own
    /// deterministic profession — so a station/settlement with multiple vendors offers several distinct barters
    /// (and visibly distinct crew) instead of every vendor selling the same goods.</summary>
    private static string VendorThemeFor(string locationName, int vendorIndex, string baseTheme)
        => vendorIndex <= 0
            ? baseTheme
            : SettlementTrades[(uint)WorldGenerator.StableHash(locationName + ":vendor:" + vendorIndex) % (uint)SettlementTrades.Length];

    /// <summary>Test seam for the per-vendor theme derivation (B55).</summary>
    public static string VendorThemeForTest(string locationName, int vendorIndex, string baseTheme)
        => VendorThemeFor(locationName, vendorIndex, baseTheme);

    /// <summary>The trade theme of the vendor the player is standing at (settlement or boarded station), or empty
    /// when none is in reach (B55). Drives which themed market goods the server accepts — per actual vendor, not
    /// one theme per location — so different vendors at one place trade different goods.</summary>
    private string VendorThemeAt(Shared.State.PlayerState player)
        => (NearSettlementVendor(player) || NearSpaceStationVendor(player) || NearLandedTraderPilot(player))
           && NearestNpc(player, "vendor") is { } v
            ? v.Theme
            : string.Empty;

    /// <summary>True if the player is standing next to a settlement vendor (enables market barter there).</summary>
    public bool NearSettlementVendor(Shared.State.PlayerState player)
        => NearMarker(player, "vendor", SettlementVendorReach);

    /// <summary>True if the player is standing next to a settlement's mission board.</summary>
    public bool NearSettlementMissionBoard(Shared.State.PlayerState player)
        => NearMarker(player, "mission_board", SettlementBoardReach);

    /// <summary>True if the player stands within a settlement's footprint (a small margin out), used to scope a
    /// settlement's board missions to "you are in this settlement".</summary>
    private bool PlayerInSettlement(Shared.State.PlayerState player, SettlementInstance s)
    {
        const int margin = 6;
        int circ = _world.Circumference;
        int lx = WorldConstants.WrapDeltaX((int)System.Math.Floor(player.Position.X) - s.Min.X, circ);
        if (lx < -margin || lx > (s.Max.X - s.Min.X) + margin)
        {
            return false;
        }

        int z = (int)System.Math.Floor(player.Position.Z);
        return z >= s.Min.Z - margin && z <= s.Max.Z + margin;
    }

    private bool NearMarker(Shared.State.PlayerState player, string type, float reach)
    {
        foreach (var (markerType, pos) in _settlementMarkers)
        {
            if (markerType == type && WrapDistSq(player.Position, pos) <= reach * reach)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if a mission is one offered by any settlement board on this world (board-gated accept/turn-in).</summary>
    public bool IsSettlementMission(string missionId)
    {
        foreach (var s in _settlements)
        {
            if (s.MissionIds.Contains(missionId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Mission ids offered by every settlement board (test/inspection).</summary>
    public IReadOnlyCollection<string> SettlementMissionIds
        => _settlements.SelectMany(s => s.MissionIds).ToHashSet();

    /// <summary>True if the block belongs to an intact (protected) settlement — ruins are scavengeable.</summary>
    public bool IsSettlementBlock(Vector3i pos)
    {
        int circ = _world.Circumference;
        foreach (var s in _settlements)
        {
            if (s.Ruined)
            {
                continue;
            }

            int lx = WorldConstants.WrapDeltaX(pos.X - s.Min.X, circ);
            if (lx < 0 || lx > s.Max.X - s.Min.X)
            {
                continue;
            }

            // #480 (was ST-7): the protected volume reaches BELOW Min.Y too — the foundation plinth is
            // filled from the foundation row down to the natural surface (up to 48 on sloped ground), and
            // leaving it mineable let a "protected" village be undermined from underneath.
            if (pos.Y >= s.Min.Y - 48 && pos.Y <= s.Max.Y && pos.Z >= s.Min.Z && pos.Z <= s.Max.Z)
            {
                return true;
            }
        }

        return false;
    }
}
