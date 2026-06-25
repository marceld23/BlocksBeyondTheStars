// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
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

        // Buried vault ruins (W-R3): the surface pillar rings show on the map as discovery targets.
        for (int i = 0; i < _vaultEntrances.Count; i++)
        {
            pois.Add(new NetPoi
            {
                Type = "vault_ruin",
                Name = "Ruin " + (char)('A' + i),
                X = _vaultEntrances[i].X,
                Z = _vaultEntrances[i].Z,
            });
        }

        // Finale (P6 Stage 2): the Guardian-core aperture is the navigation target on the finale body.
        if (_worlds.Active.HasCoreChamber)
        {
            var c = _worlds.Active.CoreChamberCenter;
            pois.Add(new NetPoi { Type = "guardian_core", Name = "Guardian Core", X = c.X, Z = c.Z });
        }

        Send(session, new PlanetPoiList { Pois = pois.ToArray() });
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
    }

    private void StampSettlement()
    {
        var planet = _world.Planet;

        // Deterministic per planet + seed.
        long sSeed = _meta.Seed ^ WorldGenerator.StableHash("settlement:" + planet.Key);
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
            if (!TryPlaceSettlement(structure, ir, reserved, wantIsland, out var origin, out int groundY, out bool onIsland))
            {
                continue; // no room on this world for this one — skip (reported below)
            }

            string name = UniqueName(SettlementDisplayName(tier, ruined, ir), usedNames);
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
            });
            reserved.Add((origin.X + structure.Width / 2, origin.Z + structure.Length / 2,
                structure.Width / 2 + 1, structure.Length / 2 + 1));
        }

        if (placed.Count == 0)
        {
            _log.Info($"No room to place any of {requested} requested settlement(s) on '{_world.LocationId}'.");
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
    /// Must run inside a repo transaction (called once per settlement from the batched stamp).</summary>
    private void StampSettlementBlocks(PlacedSettlement p, string surface)
    {
        var s = p.Structure;
        int gy = p.GroundY;
        var origin = p.Origin;
        var foundationId = _content.GetBlock(surface)?.NumericId ?? BlockId.Air;

        // 1) Clear any terrain occupying the build volume above the foundation, so a hill never buries the
        //    buildings (the structure's own air cells are otherwise left as whatever was there).
        for (int x = 0; x < s.Width; x++)
            for (int z = 0; z < s.Length; z++)
                for (int y = 1; y < s.Height; y++)
                {
                    _world.SetBlock(new Vector3i(origin.X + x, gy + y, origin.Z + z), BlockId.Air);
                }

        // 2) Flatten a foundation slab at ground level across the footprint (so it sits flush).
        if (!foundationId.IsAir)
        {
            for (int x = 0; x < s.Width; x++)
                for (int z = 0; z < s.Length; z++)
                {
                    _world.SetBlock(new Vector3i(origin.X + x, gy, origin.Z + z), foundationId);
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
    /// island deck that covers the whole footprint. Returns false if no candidate fits.</summary>
    private bool TryPlaceSettlement(SettlementStructure s, System.Random rng,
        List<(int Cx, int Cz, int Hw, int Hl)> reserved, bool wantIsland,
        out Vector3i origin, out int groundY, out bool onIsland)
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

        for (int attempt = 0; attempt < 64; attempt++)
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

            if (wantIsland)
            {
                if (TryIslandFootprint(planet, ox, oz, w, l, out int itop))
                {
                    origin = new Vector3i(ox, itop, oz);
                    groundY = itop;
                    onIsland = true;
                    return true;
                }

                continue; // wanted a sky island here but the footprint isn't fully on one
            }

            if (FootprintWet(planet, ox, oz, w, l) || FootprintSpread(planet, ox, oz, w, l) > 8)
            {
                continue; // in water/lava, or on terrain too uneven to seat the build
            }

            int gy = _generator.SurfaceHeight(planet, cx, cz);
            origin = new Vector3i(ox, gy, oz);
            groundY = gy;
            onIsland = false;
            return true;
        }

        return false;
    }

    /// <summary>The nine footprint sample columns (corners, edge mid-points, centre) in world coords.</summary>
    private static IEnumerable<(int X, int Z)> FootprintSamples(int ox, int oz, int w, int l)
    {
        int x0 = ox, x1 = ox + w / 2, x2 = ox + w - 1;
        int z0 = oz, z1 = oz + l / 2, z2 = oz + l - 1;
        yield return (x0, z0); yield return (x1, z0); yield return (x2, z0);
        yield return (x0, z1); yield return (x1, z1); yield return (x2, z1);
        yield return (x0, z2); yield return (x1, z2); yield return (x2, z2);
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

            if (pos.Y >= s.Min.Y && pos.Y <= s.Max.Y && pos.Z >= s.Min.Z && pos.Z <= s.Max.Z)
            {
                return true;
            }
        }

        return false;
    }
}
