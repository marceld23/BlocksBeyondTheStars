// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Generation 17, Arena Nigra (#2073, Theo's idea): the once-per-galaxy black-sand landmark — a sea of black sand over basalt
/// that never floods with the lava filling the lowest ground, no plants, rare ores, a red sky — and the Ignivermis, the
/// authored sandworm that takes the world's giant slot (#2075), plus the pure rules of the hunting worms (#2076) and the
/// sound-device sources (#2077). The pure half: content, the galaxy roll, worldgen, rosters, rules.
/// </summary>
public sealed class ArenaNigraWorldTests
{
    private const string Key = "arena_nigra";
    private const int Gen = WorldDescription.ArenaNigraGeneration;
    private const long Seed = 20260927;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static PlanetType Arena => Content.GetPlanet(Key)!;

    private static WorldGenerator Generator(int generation = Gen)
    {
        var gen = new WorldGenerator(Seed, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    /// <summary>Reads generated blocks by world position, one cached chunk per coordinate.</summary>
    private sealed class Probe
    {
        private readonly WorldGenerator _gen;
        private readonly PlanetType _planet;
        private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new();

        public Probe(WorldGenerator gen, PlanetType planet)
        {
            _gen = gen;
            _planet = planet;
        }

        public BlockId At(int x, int y, int z)
        {
            var coord = WorldConstants.WorldToChunk(new Vector3i(x, y, z));
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                chunk = _gen.Generate(_planet, coord);
                _chunks[coord] = chunk;
            }

            var origin = WorldConstants.ChunkOrigin(coord);
            return chunk.Get(x - origin.X, y - origin.Y, z - origin.Z);
        }

        /// <summary>The column's ground cell by the generator's own surface height, or -1 where the ground is gone.</summary>
        public int Surface(int x, int z)
        {
            int sy = _gen.SurfaceHeight(_planet, x, z);
            return sy is >= 1 and <= 150 && !At(x, sy, z).IsAir ? sy : -1;
        }
    }

    private static BlockId Block(string key) => Content.GetBlock(key)!.NumericId;

    private static IEnumerable<(int X, int Z)> Columns(int step, int span)
    {
        for (int x = 0; x < span; x += step)
            for (int z = -span / 2; z < span / 2; z += step)
                yield return (x, z);
    }

    // ---------------- the content ----------------

    [Fact]
    public void ArenaNigra_IsDataComplete_AndGatedToGenerationSeventeen()
    {
        Assert.Equal(17, Gen);
        Assert.True(WorldDescription.CurrentTerrainGeneration >= Gen);
        var p = Arena;
        Assert.Equal(Gen, p.MinTerrainGeneration);
        Assert.True(p.Exotic);
        Assert.True(p.OncePerGalaxy);
        Assert.Equal("Arena Nigra", p.FixedName);
        Assert.Equal(1, p.SpawnWeight);
        Assert.Equal("breathable", p.Atmosphere);
        Assert.True(p.BaseTemperature >= 55, "storms fall as ash from 55 °C");
        Assert.Equal("ash", p.Precipitation);
        Assert.True(p.SkyColor > 0, "a red sky is the type's own");
        Assert.True(p.CloudColor < 0x404040, "black clouds");
        Assert.Equal("black_sand", p.SurfaceBlock);
        Assert.Equal("basalt", p.SubSurfaceBlock);
        Assert.Equal("basalt", p.DeepBlock);
        Assert.True(p.SandSeaShare > 0.5, "mostly sea");
        Assert.Equal(24, p.SandSeaDepth);
        Assert.Equal(3, p.SandwormCount);
        Assert.Equal(0.0, p.WaterAbundance);
        Assert.True(p.LavaAbundance > 0.0, "lava is the sea fluid");
        Assert.Equal(0.0, p.FloraDensity);
        Assert.Equal(0.0, p.TreeDensity);
        Assert.Equal("few", p.CreatureAbundance);
        Assert.Equal(0.0, p.SettlementsBias);
        Assert.Contains("ignivermis", p.AuthoredCreatures);
        Assert.Contains("spires", p.TerrainStyles);
        Assert.Contains("volcanic", p.TerrainTags);
        Assert.Contains(p.Biomes, b => b.SandSea && b.SurfaceBlock == "black_sand");
        Assert.Equal("black_sand", GiantRules.SeaSandBlock(p));
        Assert.True(GiantRules.HostsSandworms(p, Gen));

        // Rare ores: every vein a real block, every rarity low, copper (the cable chain) and obsidian among them.
        Assert.All(p.Ores, o => Assert.NotNull(Content.GetBlock(o.Block)));
        Assert.All(p.Ores, o => Assert.True(o.Rarity <= 0.03, $"{o.Block} is not rare ({o.Rarity})"));
        Assert.Contains(p.Ores, o => o.Block == "copper_ore");
        Assert.Contains(p.Ores, o => o.Block == "obsidian");
        Assert.All(p.Biomes, b => Assert.NotNull(Content.GetBlock(b.SurfaceBlock)));

        // The black sand: a granular terrain block that drops itself, its item places it, both named in EN + DE.
        var sand = Content.GetBlock("black_sand");
        Assert.NotNull(sand);
        Assert.True(sand!.Granular);
        Assert.Contains(sand.Drops, d => d.Item == "black_sand");
        Assert.Equal("black_sand", Content.GetItem("black_sand")!.PlacesBlock);
        var en = Content.CreateLocalizer(GameLocale.English);
        var de = Content.CreateLocalizer(GameLocale.German);
        Assert.True(en.Has("block.black_sand.name") && de.Has("block.black_sand.name"));
        Assert.True(en.Has("item.black_sand.name") && de.Has("item.black_sand.name"));

        foreach (var file in Directory.GetFiles(Path.Combine(TestPaths.DataDir(), "locales"), "*.json"))
        {
            Assert.Contains("\"planet.arena_nigra.name\": \"Arena Nigra\"", File.ReadAllText(file)); // never translated
        }

        foreach (var code in new[] { "en", "de" })
        {
            string text = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json"));
            Assert.Contains("\"planet.arena_nigra.desc\"", text);
            Assert.Contains("\"vega.hint.world.arena_nigra\"", text);
        }

        Assert.Contains("Theo", en.Get("planet.arena_nigra.desc"));
        Assert.Contains("Theo", de.Get("planet.arena_nigra.desc"));

        // Every other type keeps the generation-17 no-op default.
        foreach (var other in Content.Planets.Values.Where(t => t.Key != Key))
        {
            Assert.Equal(0, other.SandwormCount);
        }
    }

    // ---------------- the galaxy ----------------

    [Fact]
    public void ArenaNigra_AppearsAtMostOncePerGalaxy_NeverInTheStartSystem_AndIsNamed()
    {
        var older = new WorldDescription { StarSystemCount = 12, TerrainGeneration = Gen - 1 };
        var current = new WorldDescription { StarSystemCount = 12, TerrainGeneration = Gen };
        older.PlanetTypeFrequencies[Key] = Frequency.Frequent;
        current.PlanetTypeFrequencies[Key] = Frequency.Frequent; // deliberately rare — weighted up so 40 seeds are enough
        int galaxiesWithIt = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            Assert.DoesNotContain(new UniverseGenerator(seed, older, Content).Generate().Systems.SelectMany(s => s.Bodies), b => b.PlanetType == Key);

            var galaxy = new UniverseGenerator(seed, current, Content).Generate();
            UniverseGenerator.ApplyFixedNames(galaxy, Content);
            var found = galaxy.Systems.SelectMany((s, i) => s.Bodies.Select(b => (System: i, Body: b))).Where(e => e.Body.PlanetType == Key).ToList();
            Assert.True(found.Count <= 1, $"seed {seed}: {found.Count} bodies of type {Key}");
            if (found.Count == 1)
            {
                galaxiesWithIt++;
                Assert.NotEqual(0, found[0].System);
                Assert.Equal(CelestialKind.Planet, found[0].Body.Kind);
                Assert.Equal("Arena Nigra", found[0].Body.Name);
            }
        }

        Assert.True(galaxiesWithIt > 0, "no generation-17 galaxy rolled Arena Nigra in 40 seeds");
    }

    // ---------------- the terrain ----------------

    [Fact]
    public void ArenaNigra_Sea_IsDeepBlackSand_NeverFloods_AndTheLowGroundIsLava()
    {
        var gen = Generator();
        var p = Arena;
        var probe = new Probe(gen, p);
        var blackSand = Block("black_sand");
        var lava = Block("lava");
        var basalt = Block("basalt");
        Assert.NotEqual(int.MinValue, gen.SeaLevel(p)); // a sea exists — and with no water it is lava

        int seaColumns = 0, landColumns = 0, lavaTops = 0, basaltTops = 0, flora = 0, seaLava = 0;
        var offenders = new List<string>();
        var seaTops = new Dictionary<string, int>();
        var floraKeys = new Dictionary<string, int>();
        foreach (var (x, z) in Columns(step: 19, span: 1200))
        {
            int sy = probe.Surface(x, z);
            if (sy < 0)
            {
                continue;
            }

            var top = probe.At(x, sy, z);
            if (gen.IsSandSeaAt(p, x, z))
            {
                seaColumns++;
                // The sea's skin is black sand — except where a gen-3 obsidian field, a crystal or a geyser vent (paints and
                // props the volcanic tag brings to every sand sea) sits ON it; the sand is always right underneath.
                if (top == lava)
                {
                    seaLava++; // a lava-flow pocket on the sea (the volcanic tag's tongues), never the lava sea itself
                }

                if (top != blackSand && probe.At(x, sy - 1, z) != blackSand)
                {
                    string key = Content.BlockById(top)?.Key ?? top.Value.ToString();
                    seaTops[key] = seaTops.GetValueOrDefault(key) + 1;
                    offenders.Add($"({x},{z}) sea top is {key} over {Content.BlockById(probe.At(x, sy - 1, z))?.Key}");
                }

                // Deep sand with nothing carved under it: the sea's depth of black sand, no air pocket in the band.
                for (int d = 1; d <= p.SandSeaDepth; d++)
                {
                    var b = probe.At(x, sy - d, z);
                    if (b.IsAir || b == lava)
                    {
                        offenders.Add($"({x},{z}) {Content.BlockById(b)?.Key ?? "air"} {d} under the sea");
                        break;
                    }
                }
            }
            else
            {
                landColumns++;
                if (top == lava)
                {
                    lavaTops++;
                }
                else if (top == basalt)
                {
                    basaltTops++;
                }
            }

            // No plants anywhere: nothing of the flora catalogue and no leaves above the ground (a ruin's timber is not a tree).
            for (int dy = 1; dy <= 6; dy++)
            {
                string key = Content.BlockById(probe.At(x, sy + dy, z))?.Key ?? string.Empty;
                if (key.StartsWith("flora_", System.StringComparison.Ordinal) || key.Contains("leaves"))
                {
                    flora++;
                    floraKeys[key] = floraKeys.GetValueOrDefault(key) + 1;
                }
            }
        }

        Assert.True(seaColumns > landColumns, $"mostly sea: {seaColumns} sea vs {landColumns} land columns");
        // The sea's skin is not sand everywhere: the gen-1 slope paints (scree, bare rock on steep dune faces and island
        // feet), the gen-3 obsidian fields (90-block glass pans the volcanic tag lays on any dry world) and a few crystal and
        // lava-flow pockets cover about a tenth of the cells on this seed, exactly as on the classic sand sea — the band
        // under them is still shielded sand, and the worm is simply deaf on those spots like on any rock. Never more than
        // a seventh, and lava on the sea is a pocket here and there, never the lava sea flooding it.
        string histogram = string.Join(", ", seaTops.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}×{kv.Value}"));
        Assert.True(offenders.Count <= seaColumns / 7, $"{offenders.Count} of {seaColumns} sea columns ({histogram}):\n" + string.Join("\n", offenders.Take(12)));
        Assert.True(seaLava <= seaColumns / 100, $"lava on {seaLava} of {seaColumns} sea columns — the sea flooded");
        Assert.True(lavaTops > 0, "no lava surfaced in the low ground");
        Assert.True(basaltTops > 0, "no basalt rock country around the sea");
        Assert.True(flora == 0, "plants on a world without plants: " + string.Join(", ", floraKeys.Select(kv => $"{kv.Key}×{kv.Value}")));
    }

    // ---------------- the roster and the giant ----------------

    [Fact]
    public void Roster_IsProcedural_AndTheIgnivermisIsTheWorldsWorm_NotARosterSpecies()
    {
        var p = Arena;
        var authored = Content.AuthoredCreaturesFor(p);
        Assert.Contains(authored, a => a.Key == "ignivermis" && a.BodyPlan == CreatureBodyPlan.Sandworm);

        var roster = CreatureGenerator.GenerateRoster(p, Seed, Gen, authored);
        Assert.Equal(5, roster.Count); // "few", and the authored giant is NOT appended
        Assert.DoesNotContain(roster, s => s.IsGiant);
        Assert.DoesNotContain(roster, s => s.Id == "au_ignivermis");

        var worm = CreatureGenerator.GenerateAuthoredGiant(p, Seed, authored, CreatureBodyPlan.Sandworm, Gen);
        Assert.NotNull(worm);
        Assert.Equal("au_ignivermis", worm!.Id);
        Assert.StartsWith("Ignivermis", worm.Name);
        Assert.Equal(CreatureBodyPlan.Sandworm, worm.BodyPlan);
        Assert.True(worm.IsGiant);
        Assert.Equal(0xB01818, worm.ColorRgb);
        Assert.Equal(0x141414, worm.BellyRgb);
        Assert.Equal(1.3f, worm.Hearing, 3);
        Assert.Equal(55f, worm.GiantHeight, 3);
        Assert.Equal(5.5f, worm.Size, 3);
        Assert.Equal(5, worm.Mandibles);
        Assert.Equal("plated", worm.Hide);
        Assert.True(worm.Glows);
        Assert.True(worm.SwallowsCreatures);
        Assert.Null(CreatureGenerator.GenerateAuthoredGiant(p, Seed, authored, CreatureBodyPlan.Colossus, Gen));
        Assert.Null(CreatureGenerator.GenerateAuthoredGiant(Content.GetPlanet("sand_sea")!, Seed, Content.AuthoredCreaturesFor(Content.GetPlanet("sand_sea")!), CreatureBodyPlan.Sandworm, Gen));

        // Same seed, same worm — the authored giant is a pure function of the type and the world.
        var again = CreatureGenerator.GenerateAuthoredGiant(p, Seed, authored, CreatureBodyPlan.Sandworm, Gen)!;
        Assert.Equal(worm.Name, again.Name);
        Assert.Equal(worm.VoiceSeed, again.VoiceSeed);
    }

    [Fact]
    public void WormCount_IsTheTypesOwn_OnGenerationSeventeen_AndTheClassicRuleBefore()
    {
        var p = Arena;
        var sandSea = Content.GetPlanet("sand_sea")!;
        Assert.Equal(3, GiantRules.SandwormCount(p, 5000, Gen));
        Assert.Equal(1, GiantRules.SandwormCount(p, 5000, Gen - 1)); // an older save keeps the circumference rule
        Assert.Equal(1, GiantRules.SandwormCount(sandSea, 5000, Gen));
        Assert.Equal(2, GiantRules.SandwormCount(sandSea, 7000, Gen));
        Assert.Equal(GiantRules.SandwormCount(7000), GiantRules.SandwormCount(sandSea, 7000, Gen));
    }

    [Fact]
    public void RolledWorms_Hunt_FromGenerationSeventeen_AndNoOtherTraitMoves()
    {
        for (int i = 0; i < 12; i++)
        {
            var older = CreatureGenerator.GenerateSandworm(500 + i, "sys3-p1", Gen - 1);
            var current = CreatureGenerator.GenerateSandworm(500 + i, "sys3-p1", Gen);
            Assert.False(older.SwallowsCreatures);
            Assert.True(current.SwallowsCreatures);
            Assert.Equal(older.Name, current.Name);
            Assert.Equal(older.ColorRgb, current.ColorRgb);
            Assert.Equal(older.Hearing, current.Hearing);
            Assert.Equal(older.MaxHealth, current.MaxHealth);
            Assert.Equal(older.WormLength, current.WormLength);
        }

        Assert.False(CreatureGenerator.GenerateSandworm(7, "sys1-p1").SwallowsCreatures); // the classic call: generation 0
        Assert.False(GiantRules.RolledWormsHunt(WorldDescription.GiantsGeneration));
        Assert.True(GiantRules.RolledWormsHunt(Gen));
    }

    // ---------------- the vibration rules ----------------

    [Fact]
    public void SoundDevices_CarryBetweenTheSpeederCrashAndTheThumper_AndAnimalsShakeTheSandTooWhenTheyMove()
    {
        Assert.True(GiantRules.Reach(VibrationSource.Horn) > GiantRules.Reach(VibrationSource.SpeederCrash));
        Assert.True(GiantRules.Reach(VibrationSource.Siren) > GiantRules.Reach(VibrationSource.Horn));
        Assert.True(GiantRules.Reach(VibrationSource.Siren) < GiantRules.Reach(VibrationSource.Thumper));
        Assert.True(GiantRules.Weight(VibrationSource.Siren) > GiantRules.Weight(VibrationSource.Horn));
        Assert.True(GiantRules.Weight(VibrationSource.Horn) > GiantRules.Weight(VibrationSource.Step));
        Assert.True(GiantRules.Hears(VibrationSource.Horn, 1f, 150f));
        Assert.False(GiantRules.Hears(VibrationSource.Horn, 1f, 170f));

        Assert.False(GiantRules.CreatureShakes(0f));
        Assert.False(GiantRules.CreatureShakes(0.5f));
        Assert.True(GiantRules.CreatureShakes(2.4f)); // a grazer's amble, slower than a player's sneak, is heard
        Assert.True(GiantRules.CreatureStepSpeed < GiantRules.SneakSpeed);

        for (int i = 0; i < 20; i++)
        {
            Assert.InRange(GiantRules.SwallowMeat("cr_" + i), 1, 2);
        }

        Assert.Equal("sand", GiantRules.SeaSandBlock(Content.GetPlanet("sand_sea")!));
        Assert.Equal("sand", GiantRules.SeaSandBlock(Content.GetPlanet("desert")!)); // no sea biome: the classic key
        Assert.Equal("sand", GiantRules.SeaSandBlock(null));
    }
}
