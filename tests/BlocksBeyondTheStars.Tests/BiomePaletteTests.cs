// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3289: rune stones and ancient bricks take the colours of the biome they stand in. Every biome surface a planet can
/// show names a palette, every look block ships one opaque tile per palette (and no look key is a block), the content
/// rejects an unknown palette, and a monument's runes glow in the palette colour of their own column — stamped once,
/// with the monument's layout and its re-derived position unchanged.
/// </summary>
public sealed class BiomePaletteTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly string TexturesDir = Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "Resources", "textures");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_biomelook_" + Guid.NewGuid().ToString("N"));

    /// <summary>The biome surface keys <c>WorldGenerator.BiomeSurfaceKeyAt</c> can answer on a planet: its biomes' surfaces,
    /// or its own surface block when it lists no biomes.</summary>
    private static IEnumerable<string> BiomeSurfaces(PlanetType planet)
        => planet.Biomes.Count > 0 ? planet.Biomes.Select(b => b.SurfaceBlock) : new[] { planet.SurfaceBlock };

    [Fact]
    public void EveryBiomeSurface_OnEveryPlanetType_HasAPalette()
    {
        var missing = new List<string>();
        int surfaces = 0;
        foreach (var planet in Content.Planets.Values.Where(p => !p.Void))
        {
            foreach (string surface in BiomeSurfaces(planet))
            {
                surfaces++;
                if (BiomePalettes.OfSurface(Content, surface) < 0)
                {
                    missing.Add($"{planet.Key}: '{surface}'");
                }
            }
        }

        Assert.True(surfaces > 50, $"only {surfaces} biome surfaces found");
        Assert.True(missing.Count == 0, "biome surfaces without a biomePalette in data/blocks.json:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void ThePalettes_AreDistinct_AndEachHasItsOwnGlow()
    {
        Assert.Equal(8, BiomePalettes.Count);
        Assert.Equal(BiomePalettes.Count, BiomePalettes.Names.Distinct().Count());
        var glows = Enumerable.Range(0, BiomePalettes.Count).Select(BiomePalettes.GlowOf).ToList();
        Assert.DoesNotContain(0, glows);
        Assert.Equal(glows.Count, glows.Distinct().Count());
        Assert.Equal(-1, BiomePalettes.IndexOf("nowhere"));
        Assert.Equal(-1, BiomePalettes.OfSurface(Content, "iron_wall")); // a building block names no palette
        Assert.Equal(BiomePalettes.IndexOf("desert"), BiomePalettes.OfSurface(Content, "sand"));
    }

    [Fact]
    public void EveryBiomeLook_IsABundledOpaqueTile_AndNoBlockKey()
    {
        var lookBlocks = Content.Blocks.Values.Where(b => b.BiomeLooks).Select(b => b.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "ancient_brick", "rune_stone" }, lookBlocks);
        var textureOnly = BlockFaceTextures.TextureOnlyKeys(Content.Blocks.Values, key => Content.GetBlock(key) != null);
        foreach (var block in Content.Blocks.Values.Where(b => b.BiomeLooks))
        {
            var looks = BiomePalettes.LookKeysOf(block).ToList();
            Assert.Equal(BiomePalettes.Count, looks.Count);
            for (int p = 0; p < looks.Count; p++)
            {
                string look = looks[p];
                Assert.Null(Content.GetBlock(look)); // a look key is never a block
                Assert.Contains(look, textureOnly); // the atlas deals it a slot in the extras band
                Assert.True(BiomePalettes.TryParseLookKey(Content, look, out var parsed, out int palette), look);
                Assert.Same(block, parsed);
                Assert.Equal(p, palette);

                string path = Path.Combine(TexturesDir, look + ".bytes");
                Assert.True(File.Exists(path), $"biome look '{look}' is not bundled ({path})");
                byte[] raw = File.ReadAllBytes(path);
                Assert.Equal(64 * 64 * 4, raw.Length);
                for (int i = 3; i < raw.Length; i += 4)
                {
                    Assert.True(raw[i] == 255, $"biome look '{look}' has a see-through pixel — block tiles ship opaque");
                }
            }
        }

        Assert.False(BiomePalettes.TryParseLookKey(Content, "stone_desert", out _, out _)); // stone wears no looks
        Assert.False(BiomePalettes.TryParseLookKey(Content, "rune_stone_nowhere", out _, out _));
    }

    [Fact]
    public void Validation_RejectsAnUnknownPalette_AndALookBlockThatIsNotSolid()
    {
        var ex = Assert.Throws<ContentValidationException>(() =>
        {
            var content = new GameContent(
                blocks: new[]
                {
                    new BlockDefinition { Key = "stone", BiomePalette = "lunar" },
                    new BlockDefinition { Key = "rune_stone", BiomeLooks = true, Solid = false },
                    new BlockDefinition { Key = "rune_stone_frost" },
                },
                items: Array.Empty<ItemDefinition>(),
                recipes: Array.Empty<RecipeDefinition>(),
                blueprints: Array.Empty<BlueprintDefinition>(),
                shipModules: Array.Empty<ShipModuleDefinition>(),
                locales: new Dictionary<GameLocale, Dictionary<string, string>>());
            content.Validate();
        });

        Assert.Contains(ex.Problems, p => p.Contains("lunar", StringComparison.Ordinal));
        Assert.Contains(ex.Problems, p => p.Contains("not solid", StringComparison.Ordinal));
        Assert.Contains(ex.Problems, p => p.Contains("rune_stone_frost", StringComparison.Ordinal));
    }

    [Fact]
    public void MonumentRunes_GlowInTheirColumnsPalette_AndTheMonumentsStayWhereTheyWere()
    {
        ushort rune = Content.GetBlock("rune_stone")!.NumericId.Value;
        ushort brick = Content.GetBlock("ancient_brick")!.NumericId.Value;
        for (long seed = 1; seed <= 40; seed++)
        {
            string world = "glow" + seed;
            var server = Start(world, seed, out var repo);
            List<(Vector3f Center, string Archetype)> before;
            int checkedCells = 0;
            var palettes = new HashSet<int>();
            using (repo)
            {
                before = server.MonumentsForTest().ToList();
                var planet = server.World.Planet;
                foreach (var (center, _) in before)
                {
                    for (int x = (int)center.X - 12; x <= (int)center.X + 12; x++)
                        for (int z = (int)center.Z - 12; z <= (int)center.Z + 12; z++)
                        {
                            int palette = BiomePalettes.OfSurface(Content, server.World.Generator.BiomeSurfaceKeyAt(planet, x, z));
                            for (int y = (int)center.Y - 12; y <= (int)center.Y + 24; y++)
                            {
                                var pos = new Vector3i(x, y, z);
                                ushort b = server.World.GetBlock(pos).Value;
                                int glow = server.World.GetModifier(pos).Glow;
                                if ((b != rune && b != brick) || glow == 0)
                                {
                                    continue;
                                }

                                Assert.True(palette >= 0, $"monument column ({x}, {z}) has no palette");
                                Assert.Equal(BiomePalettes.GlowOf(palette), glow);
                                palettes.Add(palette);
                                checkedCells++;
                            }
                        }
                }
            }

            if (checkedCells == 0)
            {
                continue; // no monument (or none with a glowing rune) on this seed's world
            }

            // The layout is the generator's, untouched: a reload re-derives every monument where it was stamped.
            var again = Start(world, seed, out var repo2);
            using (repo2)
            {
                var after = again.MonumentsForTest().ToList();
                Assert.Equal(before.Count, after.Count);
                for (int i = 0; i < before.Count; i++)
                {
                    Assert.Equal(before[i].Archetype, after[i].Archetype);
                    Assert.Equal(before[i].Center.X, after[i].Center.X, 3);
                    Assert.Equal(before[i].Center.Y, after[i].Center.Y, 3);
                    Assert.Equal(before[i].Center.Z, after[i].Center.Z, 3);
                }
            }

            Assert.NotEmpty(palettes);
            return;
        }

        Assert.Fail("No monument with a glowing rune was placed across 40 seeds.");
    }

    private SvGameServer Start(string world, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = "jungle", // several biomes, so the runes meet more than one palette
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceRuins = false,
            PlaceRailRuins = false,
            PlaceChests = false,
            PlaceWrecks = false,
            PlaceVaults = false,
            PlaceDataCubes = false,
            PlaceBanditCamps = false,
        };
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A repo file may still be locked on Windows — the temp dir is disposable either way.
        }
    }
}
