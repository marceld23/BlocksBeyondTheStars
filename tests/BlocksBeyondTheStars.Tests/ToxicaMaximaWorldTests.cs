// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
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
/// Generation 15, Toxica-Maxima (#2062, Justus' idea): the once-per-galaxy poisoned landmark — tainted ground and shallow tainted
/// ores, dead snags of tainted wood, needles and caves, a contaminated green-eyed roster — and the eye colour every generation-15
/// species rolls (#2069). The pure half: content, the galaxy roll, worldgen, rosters.
/// </summary>
public sealed class ToxicaMaximaWorldTests
{
    private const string Key = "toxica_maxima";
    private const int Gen = WorldDescription.ToxicaMaximaGeneration;
    private const long Seed = 20260927;
    private const int GreenEyes = 0x39FF14;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static PlanetType Toxica => Content.GetPlanet(Key)!;

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

        /// <summary>The column's ground cell: the generator's own surface height (no chunk scan — the CI runner pays for every
        /// chunk this probe touches, #2068), or -1 where a cave mouth, hole or lake has taken the ground away.</summary>
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
    public void Toxica_IsDataComplete_AndGatedToGenerationFifteen()
    {
        Assert.Equal(15, Gen);
        Assert.True(WorldDescription.CurrentTerrainGeneration >= Gen);
        var p = Toxica;
        Assert.Equal(Gen, p.MinTerrainGeneration);
        Assert.True(p.Exotic);
        Assert.True(p.OncePerGalaxy);
        Assert.Equal("Toxica-Maxima", p.FixedName);
        Assert.Equal(1, p.SpawnWeight);
        Assert.Equal("toxic", p.Atmosphere);
        Assert.Equal(1.0, p.CorrosiveAirChance); // always, no roll
        Assert.Equal(1.0, p.WaterDamageChance);
        Assert.True(p.AirDamagePerSecond > 0 && p.WaterDamagePerSecond > 0);
        Assert.Equal("stormy", p.Weather);
        Assert.Equal("acid", p.Precipitation);
        Assert.True(p.SkyColor > 0);
        Assert.True(p.DeadForests);
        Assert.Equal("tainted_log", p.DeadTreeBlock);
        Assert.True(p.ContaminatedFauna);
        Assert.Equal(GreenEyes, p.EyeColor);
        Assert.Equal("few", p.CreatureAbundance);
        Assert.Equal(0.0, p.SettlementsBias);
        Assert.True(p.RuinedSettlementsOnly);
        Assert.Equal(new[] { 6, 10 }, p.FactoryCount);
        Assert.Equal(4, p.FactoryRecipes.Count);
        Assert.Equal("tainted_soil", p.SurfaceBlock);
        Assert.Equal("tainted_subsoil", p.SubSurfaceBlock);
        Assert.Equal("tainted_stone", p.DeepBlock);
        Assert.Contains("spires", p.TerrainStyles);
        Assert.Contains("stone-forest", p.TerrainStyles);
        Assert.True(p.CaveThreshold is > 0 and <= 0.5, "the maximum carve share");
        Assert.True(p.WaterAbundance > 0.3, "cenotes and sinkholes need water above 0.3");

        // Every ore within the top 8 blocks, the tainted veins first, and every block real.
        Assert.All(p.Ores, o => Assert.True(o.MinDepth <= 8, $"{o.Block} starts {o.MinDepth} deep"));
        Assert.All(p.Ores.Take(4), o => Assert.StartsWith("tainted_", o.Block));
        Assert.All(p.Ores, o => Assert.NotNull(Content.GetBlock(o.Block)));
        Assert.All(p.Biomes, b => Assert.NotNull(Content.GetBlock(b.SurfaceBlock)));
        Assert.Contains(p.Ores, o => o.Block == "carbon"); // the wash needs carbon on site

        foreach (var file in Directory.GetFiles(Path.Combine(TestPaths.DataDir(), "locales"), "*.json"))
        {
            Assert.Contains("\"planet.toxica_maxima.name\": \"Toxica-Maxima\"", File.ReadAllText(file)); // never translated
        }

        foreach (var code in new[] { "en", "de" })
        {
            string text = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json"));
            Assert.Contains("\"planet.toxica_maxima.desc\"", text);
            Assert.Contains("\"vega.hint.world.toxica_maxima\"", text);
            Assert.Contains("\"weather.toxic_storm\"", text);
            Assert.Contains("\"ui.scan.threat.contaminated\"", text);
            Assert.Contains("\"ui.hud.factory_air\"", text);
        }

        // Every classic type keeps the generation-15 no-op defaults. Arena Nigra (#2073, generation 16) is the one other type
        // that names its sky and its precipitation — and has its own tests.
        foreach (var other in Content.Planets.Values.Where(t => t.Key != Key && t.Key != "arena_nigra"))
        {
            Assert.Equal(0, other.SkyColor);
            Assert.Equal(string.Empty, other.Precipitation);
            Assert.Equal(string.Empty, other.DeadTreeBlock);
            Assert.False(other.ContaminatedFauna);
            Assert.Equal(0, other.EyeColor);
            Assert.Empty(other.FactoryCount);
            Assert.Empty(other.FactoryRecipes);
            Assert.NotEqual("stormy", other.Weather);
        }
    }

    [Fact]
    public void TaintedMaterials_AreBlocksAndItems_ThatDropThemselves_AndWashClean()
    {
        var tainted = new[]
        {
            ("tainted_soil", "dirt", "clean_soil"), ("tainted_subsoil", "dirt", "clean_subsoil"), ("tainted_stone", "stone", "clean_stone"),
            ("tainted_log", "wood_log", "clean_wood"), ("tainted_iron_ore", "iron_ore", "clean_iron_ore"),
            ("tainted_copper_ore", "copper_ore", "clean_copper_ore"), ("tainted_titanium_ore", "titanium_ore", "clean_titanium_ore"),
            ("tainted_diamond_ore", "diamond_ore", "clean_diamond_ore"),
        };
        var en = Content.CreateLocalizer(GameLocale.English);
        var de = Content.CreateLocalizer(GameLocale.German);
        foreach (var (key, clean, recipeKey) in tainted)
        {
            var block = Content.GetBlock(key);
            Assert.NotNull(block);
            Assert.Contains(block!.Drops, d => d.Item == key);
            Assert.NotNull(Content.GetItem(key));
            Assert.True(en.Has($"block.{key}.name") && de.Has($"block.{key}.name"), key);
            Assert.True(en.Has($"item.{key}.name") && de.Has($"item.{key}.name"), key);

            // The clean twin's hardness and tool tier; nothing but the decontaminator accepts the tainted item.
            var twin = Content.GetBlock(clean)!;
            Assert.Equal(twin.MinToolTier, block.MinToolTier);
            var wash = Content.Recipes[recipeKey];
            Assert.Equal(CraftingStation.Decontaminator, wash.Station);
            Assert.Equal("decontaminator", wash.RequiredBlueprint);
            Assert.Equal(2, wash.Inputs.Single(i => i.Item == key).Count);
            Assert.Equal(1, wash.Inputs.Single(i => i.Item == "carbon").Count);
            Assert.Equal((clean, 2), (wash.Outputs.Single().Item, wash.Outputs.Single().Count));
            Assert.DoesNotContain(Content.Recipes.Values, r => r.Station != CraftingStation.Decontaminator && r.Station != CraftingStation.Factory
                && r.Inputs.Any(i => i.Item == key));
        }

        // Contaminated meat is poison and washes into safe meat.
        var meat = Content.GetItem("toxic_meat")!;
        Assert.True(meat.ConsumeHealth < 0);
        Assert.Equal("creature_meat", Content.Recipes["wash_meat"].Outputs.Single().Item);

        // The decontaminator: block, item, ship module, blueprint after the detoxifier, built at the workshop.
        Assert.NotNull(Content.GetBlock("decontaminator"));
        Assert.Equal("decontaminator", Content.GetItem("decontaminator")!.PlacesBlock);
        Assert.Contains(Content.ShipModules.Values, m => m.Key == "decontaminator" && m.RequiredBlueprint == "decontaminator");
        Assert.Contains("detoxifier", Content.Blueprints["decontaminator"].Prerequisites);
        Assert.Equal(CraftingStation.Workshop, Content.Recipes["decontaminator"].Station);

        // The factory washes stay out of every seeded roster and are the type's fixed roster.
        foreach (var key in Toxica.FactoryRecipes)
        {
            var r = Content.Recipes[key];
            Assert.Equal(CraftingStation.Factory, r.Station);
            Assert.False(r.FactoryPool);
            Assert.Equal(6, r.Inputs.Single().Count);
            Assert.Equal(4, r.Outputs.Single().Count);
        }

        Assert.All(Content.Recipes.Values.Where(r => r.Station == CraftingStation.Factory && !Toxica.FactoryRecipes.Contains(r.Key)),
            r => Assert.True(r.FactoryPool));
    }

    // ---------------- the galaxy ----------------

    [Fact]
    public void Toxica_AppearsAtMostOncePerGalaxy_NeverInTheStartSystem_AndIsNamed()
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
                Assert.Equal("Toxica-Maxima", found[0].Body.Name);
            }
        }

        Assert.True(galaxiesWithIt > 0, "no generation-15 galaxy rolled Toxica-Maxima in 40 seeds");
    }

    // ---------------- the terrain ----------------

    [Fact]
    public void Toxica_Ground_IsTainted_AndTaintedOreLiesWithinTheTopEightBlocks()
    {
        var probe = new Probe(Generator(), Toxica);
        var taintedGround = new HashSet<BlockId> { Block("tainted_soil"), Block("tainted_subsoil"), Block("tainted_stone"), Block("tainted_log") };
        var frozen = new HashSet<BlockId> { Block("ice"), Block("snow") }; // the needles reach the cold: upland ponds freeze
        var taintedOres = new HashSet<BlockId>
        {
            Block("tainted_iron_ore"), Block("tainted_copper_ore"), Block("tainted_titanium_ore"), Block("tainted_diamond_ore"),
        };
        var water = Block("water");
        var cleanGround = new HashSet<BlockId> { Block("dirt"), Block("stone"), Block("grass") };

        int columns = 0, taintedTop = 0, oreShallow = 0, cleanSeen = 0, wet = 0, bottomless = 0;
        var offenders = new List<string>();
        var tops = new Dictionary<string, int>();
        foreach (var (x, z) in Columns(step: 17, span: 800))
        {
            int sy = probe.Surface(x, z);
            if (sy < 0)
            {
                bottomless++;
                continue;
            }

            var top = probe.At(x, sy, z);
            if (top == water || frozen.Contains(top))
            {
                wet++;
                continue; // the lakes, frozen or not
            }

            columns++;
            if (taintedGround.Contains(top) || taintedOres.Contains(top))
            {
                taintedTop++;
            }
            else
            {
                string key = Content.BlockById(top)?.Key ?? top.Value.ToString();
                tops[key] = tops.GetValueOrDefault(key) + 1;
            }

            for (int d = 1; d <= 8; d++)
            {
                var b = probe.At(x, sy - d, z);
                if (taintedOres.Contains(b))
                {
                    oreShallow++;
                }

                if (cleanGround.Contains(b))
                {
                    cleanSeen++;
                    if (offenders.Count < 5)
                    {
                        offenders.Add($"({x},{sy - d},{z}) {Content.BlockById(b)?.Key}");
                    }
                }
            }
        }

        Assert.True(columns > 400, $"only {columns} dry columns sampled ({wet} under water, {bottomless} with no ground at the surface height)");
        string others = string.Join(", ", tops.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}×{kv.Value}"));
        Assert.True(taintedTop >= columns * 0.9, $"{taintedTop} of {columns} dry columns wear a tainted surface; the others: {others}");
        Assert.True(oreShallow > 0, "no tainted ore within the top eight blocks of any sampled column");
        Assert.True(cleanSeen == 0, "clean dirt / stone / grass in the contaminated layers: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Toxica_DeadSnags_AreTaintedLogs_WithoutLeaves_AndTheCavesAreCarved()
    {
        var probe = new Probe(Generator(), Toxica);
        var taintedLog = Block("tainted_log");
        var woodLog = Block("wood_log");
        var leaves = Block("tree_leaves");
        int snags = 0, caveAir = 0;
        foreach (var (x, z) in Columns(step: 3, span: 480))
        {
            int sy = probe.Surface(x, z);
            if (sy < 0)
            {
                continue;
            }

            // A snag's trunk stands on the ground cell; nothing else grows here. Below the ground, air is a cave.
            var above = probe.At(x, sy + 1, z);
            if (above == taintedLog)
            {
                snags++;
            }

            Assert.NotEqual(woodLog, above);
            Assert.NotEqual(leaves, above);
            for (int y = sy - 4; y > sy - 12 && y > 2; y--)
            {
                if (probe.At(x, y, z).IsAir)
                {
                    caveAir++;
                    break;
                }
            }
        }

        Assert.True(snags > 0, "no dead snag of tainted wood in the sampled area");
        Assert.True(caveAir > 0, "no cave air under the surface in the sampled area");
    }

    // ---------------- the roster ----------------

    [Fact]
    public void Roster_IsContaminated_OnGenerationFifteen_AndClassicOnFourteen()
    {
        var roster = CreatureGenerator.GenerateRoster(Toxica, Seed, Gen);
        Assert.InRange(roster.Count, 4, 6); // "few"
        Assert.All(roster, sp =>
        {
            Assert.True(sp.Hostile, sp.Name);
            Assert.Equal(CreatureActivity.Cathemeral, sp.Activity);
            Assert.True(sp.AttackDamage >= 2f, sp.Name);
            Assert.Equal(GreenEyes, sp.EyeRgb);
            Assert.NotEqual("creature_meat", sp.DropItem);
        });
        Assert.Contains(roster, sp => sp.Temperament == CreatureTemperament.PackHunter);

        // The same type on a generation-14 world: no pass ran (the type never reaches such a galaxy, but the rule is the gate).
        var classic = CreatureGenerator.GenerateRoster(Toxica, Seed, Gen - 1);
        Assert.Equal(classic.Count, roster.Count);
        Assert.All(classic, sp => Assert.Equal(0, sp.EyeRgb));
        Assert.Contains(classic, sp => !sp.Hostile || sp.Activity != CreatureActivity.Cathemeral);
        for (int i = 0; i < classic.Count; i++)
        {
            // The pass only swaps meat for contaminated meat; every other drop (fibre, a gland) stays what it was.
            bool meat = classic[i].DropItem == "creature_meat" || classic[i].DropKind == CreatureDropKind.Food;
            Assert.Equal(meat ? "toxic_meat" : classic[i].DropItem, roster[i].DropItem);
            Assert.Equal(meat ? CreatureDropKind.Poison : classic[i].DropKind, roster[i].DropKind);
        }
    }

    [Fact]
    public void EyeColour_IsTheFinalDraw_OnlyOnGenerationFifteen_AndMovesNoOlderTrait()
    {
        var jungle = Content.GetPlanet("jungle")!;
        int coloured = 0;
        foreach (long seed in new long[] { 11, 4242, 20260927 })
        {
            var before = CreatureGenerator.GenerateRoster(jungle, seed, Gen - 1);
            var after = CreatureGenerator.GenerateRoster(jungle, seed, Gen);
            Assert.Equal(before.Count, after.Count);
            for (int i = 0; i < before.Count; i++)
            {
                var a = before[i];
                var b = after[i];
                Assert.Equal(0, a.EyeRgb);
                Assert.Equal((a.Name, a.Habitat, a.Activity, a.Temperament, a.Size, a.Speed, a.MaxHealth, a.AttackDamage),
                    (b.Name, b.Habitat, b.Activity, b.Temperament, b.Size, b.Speed, b.MaxHealth, b.AttackDamage));
                Assert.Equal((a.Legs, a.Eyes, a.ColorRgb, a.BellyRgb, a.Glows, a.BodyPlan, a.SocialGroupSize, a.DropItem),
                    (b.Legs, b.Eyes, b.ColorRgb, b.BellyRgb, b.Glows, b.BodyPlan, b.SocialGroupSize, b.DropItem));
                if (b.EyeRgb != 0)
                {
                    coloured++;
                }
            }
        }

        Assert.True(coloured > 0, "no generation-15 species rolled an iris colour in three rosters");
    }
}
