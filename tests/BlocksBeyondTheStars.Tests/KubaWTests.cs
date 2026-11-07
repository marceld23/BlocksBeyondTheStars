// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Generation 33, KuBa-W (#3306, Jafar's idea from the school club): the asteroid type (#3307), exactly one in every galaxy
/// with its fixed name (#3308), two landing pads (#3309), the two stone text tablets that read "KuBa West Trier" and the
/// club's initials in front of their pads (#3310), indestructible (#3311) and scannable (#3312), plus the exotic-slider
/// rounding fix found on the way (#3313).
/// </summary>
public sealed class KubaWTests : IDisposable
{
    private const string Key = "asteroid_kuba_w";
    private const int Gen = WorldDescription.KubaWGeneration;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_kubaw_" + Guid.NewGuid().ToString("N"));
    private int _worlds;

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
            // a SQLite handle may linger on Windows; the temp folder is cleaned by the OS
        }
    }

    // ---------------- the type (#3307) ----------------

    [Fact]
    public void TheType_IsABreathableGreenAsteroid_WithTwoPadsTwoTabletsAndNothingElse()
    {
        var p = Content.GetPlanet(Key);
        Assert.NotNull(p);
        Assert.True(WorldConstants.IsAsteroidType(Key)); // the asteroid size class, wrap and gravity band
        Assert.Equal(Gen, p!.MinTerrainGeneration);
        Assert.Equal(0, p.SpawnWeight); // the family roll never draws it
        Assert.False(p.Selectable);
        Assert.True(p.GuaranteedOnce);
        Assert.Equal("KuBa-W", p.FixedName);
        Assert.Equal("breathable", p.Atmosphere);
        Assert.False(p.SpaceSky);
        Assert.False(p.Cratered);
        Assert.NotEqual(0, p.SkyColor);
        int r = (p.SkyColor >> 16) & 0xFF, g = (p.SkyColor >> 8) & 0xFF, b = p.SkyColor & 0xFF;
        Assert.True(g > r && g > b, "the sky is greenish");
        Assert.Equal(0.0, p.TreeDensity); // nothing tall between a pad and its tablet
        Assert.NotEqual("none", p.CreatureAbundance);
        Assert.Equal(2, p.LandingPadCount);
        Assert.True(p.RestrictStructures);
        Assert.Equal(new[] { "net_fragments" }, p.AllowedStructures);
        Assert.NotNull(p.WeatherEvents);
        Assert.Equal(0.0, p.WeatherEvents!.TryGetValue("meteor_shower", out double meteors) ? meteors : -1.0);
        Assert.Equal(new[] { "kuba_school", "kuba_initials" }, p.TextMonuments.Select(t => t.Key));
        Assert.Equal(new[] { "KuBa", "West", "Trier" }, p.TextMonuments[0].Lines);
        Assert.Equal(new[] { 0, 1 }, p.TextMonuments.Select(t => t.Pad));
    }

    [Fact]
    public void TheInitials_AreEveryChildAlphabetically_ThenTheLeadsInTheAccent()
    {
        var initials = Content.GetPlanet(Key)!.TextMonuments[1];
        string children = string.Concat("Ben Daimien Jafar Justus Lena Marie Nikita Noah Paul Sophie Theo".Split(' ').OrderBy(n => n, StringComparer.Ordinal).Select(n => n[0]));
        string all = string.Concat(initials.Lines).Replace(" ", string.Empty, StringComparison.Ordinal);
        Assert.Equal(children + "MV", all);
        Assert.Equal(new[] { 1, initials.Lines[1].IndexOf("MV", StringComparison.Ordinal) }, initials.AccentFrom);
    }

    [Fact]
    public void TheLocales_NameAndDescribeIt_InGermanAndEnglish()
    {
        foreach (string locale in new[] { "en", "de" })
        {
            string text = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", locale + ".json"));
            foreach (string key in new[]
                     {
                         "\"planet.asteroid_kuba_w.name\": \"KuBa-W\"", "\"planet.asteroid_kuba_w.desc\"", "\"vega.hint.world.kuba_w\"",
                         "\"ui.scan.subject.monument_kuba_school\"", "\"ui.scan.subject.monument_kuba_initials\"",
                         "\"ui.scan.monument.kuba_school\"", "\"ui.scan.monument.kuba_initials\"", "\"srv.protect.monument\"",
                     })
            {
                Assert.Contains(key, text);
            }
        }
    }

    // ---------------- the galaxy (#3308) ----------------

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(32)]
    public void EveryGalaxy_HasExactlyOneKubaW_AnAsteroidOutsideTheFirstSystem_NamedKuBaW(int systems)
    {
        for (long seed = 1; seed <= 30; seed++)
        {
            var desc = new WorldDescription { StarSystemCount = systems, SystemVariance = true, AsteroidBelts = true, TerrainGeneration = Gen };
            var galaxy = new UniverseGenerator(seed, desc, Content, "varied").Generate();
            UniverseGenerator.ApplyFixedNames(galaxy, Content);
            var found = galaxy.Systems.SelectMany((s, i) => s.Bodies.Select(b => (System: i, Body: b))).Where(e => e.Body.PlanetType == Key).ToList();
            var single = Assert.Single(found);
            Assert.Equal(CelestialKind.AsteroidField, single.Body.Kind);
            Assert.NotEqual(0, single.System);
            Assert.Equal("KuBa-W", single.Body.Name);
        }
    }

    [Fact]
    public void KubaW_IgnoresTheExoticSlider_StaysPutWhenTheGalaxyGrows_AndIsAbsentBeforeGeneration33()
    {
        for (long seed = 1; seed <= 20; seed++)
        {
            var off = new WorldDescription { StarSystemCount = 12, SystemVariance = true, AsteroidBelts = true, TerrainGeneration = Gen, ExoticWorlds = Frequency.Off };
            var gen = new UniverseGenerator(seed, off, Content, "varied");
            string id = Assert.Single(gen.Generate().AllBodies(), b => b.PlanetType == Key).Id;
            Assert.Equal(id, Assert.Single(gen.Generate(16).AllBodies(), b => b.PlanetType == Key).Id); // appended systems never take it

            var older = new WorldDescription { StarSystemCount = 12, SystemVariance = true, AsteroidBelts = true, TerrainGeneration = Gen - 1 };
            Assert.DoesNotContain(new UniverseGenerator(seed, older, Content, "varied").Generate().AllBodies(), b => b.PlanetType == Key);
        }
    }

    [Fact]
    public void TheClassicAsteroidRoll_NeverDrawsKubaW_AndNoPlanetEverBecomesIt()
    {
        for (long seed = 1; seed <= 20; seed++)
        {
            var desc = new WorldDescription { StarSystemCount = 20, SystemVariance = true, AsteroidBelts = true, TerrainGeneration = Gen, ExoticWorlds = Frequency.Frequent };
            var bodies = new UniverseGenerator(seed, desc, Content, "varied").Generate().AllBodies().ToList();
            Assert.DoesNotContain(bodies, b => b.Kind != CelestialKind.AsteroidField && b.PlanetType == Key);
            Assert.Equal(1, bodies.Count(b => b.PlanetType == Key));
        }
    }

    // ---------------- the exotic slider (#3313) ----------------

    [Fact]
    public void TheExoticSlider_NoLongerRoundsAWeightOneLandmarkAway()
    {
        Assert.Equal(0, FrequencyExtensions.ExoticWeight(1, Frequency.VeryRare, Gen - 1)); // the old arithmetic, kept for old saves
        Assert.Equal(2, FrequencyExtensions.ExoticWeight(1, Frequency.Frequent, Gen - 1));
        Assert.Equal(1, FrequencyExtensions.ExoticWeight(1, Frequency.VeryRare, Gen));
        Assert.Equal(1, FrequencyExtensions.ExoticWeight(1, Frequency.Rare, Gen));
        Assert.Equal(1, FrequencyExtensions.ExoticWeight(1, Frequency.Normal, Gen));
        Assert.Equal(3, FrequencyExtensions.ExoticWeight(1, Frequency.Frequent, Gen));
        Assert.Equal(0, FrequencyExtensions.ExoticWeight(1, Frequency.Off, Gen));
        Assert.Equal(0, FrequencyExtensions.ExoticWeight(0, Frequency.Frequent, Gen)); // the gas giant's 0 stays 0
        foreach (int w in new[] { 1, 2, 3, 5, 6 })
        {
            Assert.Equal(w, FrequencyExtensions.ExoticWeight(w, Frequency.Normal, Gen)); // a default galaxy rolls the same
            Assert.Equal(FrequencyExtensions.ExoticWeight(w, Frequency.Normal, Gen - 1), FrequencyExtensions.ExoticWeight(w, Frequency.Normal, Gen));
        }
    }

    [Fact]
    public void OnVeryRare_TheNamedLandmarksStillAppearSomewhere()
    {
        int landmarks = 0;
        for (long seed = 1; seed <= 120 && landmarks == 0; seed++)
        {
            var desc = new WorldDescription { StarSystemCount = 32, SystemVariance = true, AsteroidBelts = true, TerrainGeneration = Gen, ExoticWorlds = Frequency.VeryRare };
            landmarks += new UniverseGenerator(seed, desc, Content, "varied").Generate().AllBodies()
                .Count(b => b.PlanetType is "titas" or "toxica_maxima" or "arena_nigra");
        }

        Assert.True(landmarks > 0, "with 'Very rare' exotic worlds no landmark rolled in 120 huge galaxies — the weight rounded to 0 again");
    }

    // ---------------- the tablet generator (#3310) ----------------

    [Fact]
    public void TheTablet_SpellsItsLines_InTheFont_AndHasTheAgreedSize()
    {
        var school = TextMonumentGenerator.Build(new[] { "KuBa", "West", "Trier" }, 1, 2, 3);
        Assert.Equal(62, school.Width);
        Assert.Equal(54, school.Height);
        Assert.Equal(new[] { "KuBa", "West", "Trier" }, ReadTablet(school.Lines, (x, y) => school.Get(x, y, 0), 2, 3));

        var initials = TextMonumentGenerator.Build(new[] { "BDJJLMN", "NPST MV" }, 1, 2, 3, accentLine: 1, accentFrom: 5);
        Assert.Equal(86, initials.Width);
        Assert.Equal(36, initials.Height);
        Assert.Equal(new[] { "BDJJLMN", "NPST MV" }, ReadTablet(initials.Lines, (x, y) => initials.Get(x, y, 0), 2, 3));
        int left = TextMonumentGenerator.GlyphLeft("NPST MV", 5, MonumentFont.LineWidth("BDJJLMN"));
        int top = TextMonumentGenerator.GlyphTop(initials.Height, 1);
        Assert.Equal(3, initials.Get(left, top, 0)); // the M's top-left pixel is the accent block
        Assert.True(Enumerable.Range(0, initials.Width).Count(x => initials.IsLamp(x, TextMonumentGenerator.Border - 1, 0)) is > 0 and < 20);
    }

    // ---------------- the server: pads, tablets, protection, scan (#3309–#3312) ----------------

    [Fact]
    public void AKubaWWorld_HasTwoPads_AndATabletInFullViewOfEach_ThatReadsItsText()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Assert.Equal(2, server.LandingPadCount);
            var tablets = server.TextMonumentsForTest();
            Assert.Equal(new[] { "kuba_school", "kuba_initials" }, tablets.Select(t => t.Key));
            var specs = Content.GetPlanet(Key)!.TextMonuments;
            ushort letter = Content.GetBlock("concrete")!.NumericId.Value, accent = Content.GetBlock("gold_block")!.NumericId.Value;
            for (int i = 0; i < tablets.Count; i++)
            {
                var t = tablets[i];
                var pad = server.LandingPadForTest(specs[i].Pad);
                var read = ReadTablet(specs[i].Lines, (x, y) => server.World.GetBlock(server.TextMonumentCellForTest(t.Key, x, y, 0)).Value, letter, accent);
                Assert.Equal(specs[i].Lines, read); // the stamped blocks spell the text, left to right for the reader

                // The reader's frame: the face (z = 0) is the side toward the pad, x runs to the right of someone looking from the pad.
                var a = server.TextMonumentCellForTest(t.Key, 0, 0, 0);
                var back = server.TextMonumentCellForTest(t.Key, 0, 0, t.Depth - 1);
                Assert.True(Dist(pad, a) < Dist(pad, back), $"{t.Key}: the face must look at its pad");
                var next = server.TextMonumentCellForTest(t.Key, 1, 0, 0);
                Assert.Equal((t.Fz, -t.Fx), (WorldConstants.WrapDeltaX(next.X - a.X, server.World.Circumference), next.Z - a.Z));

                // The whole tablet lies inside the browser's view radius around the pad.
                foreach (var (x, z) in new[] { (0, 0), (t.Width - 1, 0), (0, t.Depth - 1), (t.Width - 1, t.Depth - 1) })
                {
                    Assert.True(Dist(pad, server.TextMonumentCellForTest(t.Key, x, 0, z)) <= SvGameServer.TextMonumentViewRadius + 1, $"{t.Key} corner too far");
                }

                AssertClearView(server, pad, t);
            }
        }
    }

    [Fact]
    public void TheTablets_AreIndestructible_ButTheGroundBesideThemIsNot()
    {
        var server = Started(out var repo);
        using (repo)
        {
            foreach (var t in server.TextMonumentsForTest())
            {
                var letterCell = server.TextMonumentCellForTest(t.Key, t.Width / 2, t.Height / 2, 1);
                var foot = server.TextMonumentCellForTest(t.Key, 0, -1, 0);
                Assert.True(server.TextMonumentProtectedForTest(letterCell));
                Assert.True(server.TextMonumentProtectedForTest(foot), "the plinth is part of it");
                Assert.True(server.BreakProtectedForTest(letterCell, "Visitor"));
                Assert.True(server.BreakProtectedForTest(letterCell, "Admin", isAdmin: true));

                var beside = server.TextMonumentCellForTest(t.Key, -3, 0, 0);
                Assert.False(server.TextMonumentProtectedForTest(beside));
                var front = server.TextMonumentCellForTest(t.Key, t.Width / 2, 0, -3);
                Assert.False(server.TextMonumentProtectedForTest(front));
            }
        }
    }

    [Fact]
    public void ScanningATablet_ReadsTheMonument_OncePerTablet()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Reader");
            foreach (var t in server.TextMonumentsForTest())
            {
                var cell = server.TextMonumentCellForTest(t.Key, t.Width / 2, 1, 1);
                var first = server.ScanSubject("Reader", "block", "basalt", cell: cell);
                Assert.Equal("monument", first.Kind);
                Assert.Equal("monument_" + t.Key, first.SubjectKey);
                Assert.Equal("ui.scan.monument." + t.Key, first.InfoKey);
                Assert.True(first.FirstTime);
                Assert.False(server.ScanSubject("Reader", "block", "concrete", cell: server.TextMonumentCellForTest(t.Key, 4, 4, 1)).FirstTime);
            }

            var elsewhere = server.ScanSubject("Reader", "block", "grass", cell: new Vector3i(9999, 40, 0));
            Assert.NotEqual("monument", elsewhere.Kind);
        }
    }

    [Fact]
    public void AReload_RederivesTheTablets_FromTheirRecords_WithoutStampingAgain()
    {
        string world = "kubaw_reload";
        IReadOnlyList<(string Key, Vector3i Anchor, int Fx, int Fz, int Width, int Height, int Depth)> before;
        var first = Started(out var repo, world);
        using (repo)
        {
            before = first.TextMonumentsForTest();
            first.SaveNow();
            first.Stop();
        }

        var second = Started(out var repo2, world);
        using (repo2)
        {
            Assert.Equal(before, second.TextMonumentsForTest());
            Assert.Equal(2, second.Metadata.StampedFeatures.Count(f => f.Contains("|textmonument@", StringComparison.Ordinal)));
        }
    }

    // ---------------- helpers ----------------

    private SvGameServer Started(out SqliteWorldRepository repo, string? world = null, long seed = 3306)
    {
        world ??= "kubaw_" + (++_worlds);
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = Key, // the server makes the galaxy's first planet a KuBa-W for the start — a test world
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = Gen },
        };
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        Assert.Equal(Key, server.World.Planet.Key);
        return server;
    }

    private static double Dist((int X, int Y, int Z) pad, Vector3i cell)
        => Math.Sqrt(Math.Pow(pad.X - cell.X, 2) + Math.Pow(pad.Z - cell.Z, 2));

    /// <summary>Reads a tablet face back into text: every glyph cell is matched against the font, pixel by pixel.</summary>
    private static string[] ReadTablet(IReadOnlyList<string> lines, Func<int, int, ushort> face, ushort letter, ushort accent)
    {
        const string Alphabet = " ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        int maxPixels = lines.Max(MonumentFont.LineWidth);
        int height = (TextMonumentGenerator.Border * 2) + (((lines.Count * MonumentFont.GlyphHeight) + ((lines.Count - 1) * TextMonumentGenerator.LineGapPixels)) * TextMonumentGenerator.PixelScale);
        var result = new string[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            var sb = new StringBuilder();
            int top = TextMonumentGenerator.GlyphTop(height, i);
            for (int k = 0; k < lines[i].Length; k++)
            {
                int left = TextMonumentGenerator.GlyphLeft(lines[i], k, maxPixels);
                bool Ink(int col, int row)
                {
                    ushort b = face(left + (col * TextMonumentGenerator.PixelScale), top - (row * TextMonumentGenerator.PixelScale));
                    return b == letter || b == accent;
                }

                char match = Alphabet.FirstOrDefault(c => Enumerable.Range(0, MonumentFont.GlyphHeight)
                    .All(row => Enumerable.Range(0, MonumentFont.GlyphWidth).All(col => MonumentFont.IsInk(c, col, row) == Ink(col, row))));
                sb.Append(match == '\0' ? '?' : match);
            }

            result[i] = sb.ToString();
        }

        return result;
    }

    /// <summary>No solid block stands on the lines of sight from eye height at the pad's rim to the foot of the text (its
    /// left end, middle and right end).</summary>
    private static void AssertClearView(SvGameServer server, (int X, int Y, int Z) pad,
        (string Key, Vector3i Anchor, int Fx, int Fz, int Width, int Height, int Depth) t)
    {
        double eyeY = pad.Y + 2.6;
        int circ = server.World.Circumference;
        foreach (int x in new[] { 0, t.Width / 2, t.Width - 1 })
        {
            var target = server.TextMonumentCellForTest(t.Key, x, TextMonumentGenerator.Border, 0);
            double tx = pad.X + WorldConstants.WrapDeltaX(target.X - pad.X, circ), tz = target.Z;
            double sx = pad.X + ((tx - pad.X) * 0.2), sz = pad.Z + ((tz - pad.Z) * 0.2);
            int steps = (int)Math.Ceiling(Math.Sqrt(((tx - sx) * (tx - sx)) + ((tz - sz) * (tz - sz))));
            for (int i = 1; i < steps - 1; i++)
            {
                double s = i / (double)steps;
                var cell = new Vector3i(WorldConstants.WrapX((int)Math.Round(sx + ((tx - sx) * s)), circ), (int)Math.Floor(eyeY + ((target.Y + 0.5 - eyeY) * s)), (int)Math.Round(sz + ((tz - sz) * s)));
                var block = Content.BlockById(server.World.GetBlock(cell));
                Assert.False(block is { Solid: true } && !server.World.GetBlock(cell).IsAir, $"{t.Key}: a solid block at {cell} blocks the view from pad ({pad.X},{pad.Z})");
            }
        }
    }
}
