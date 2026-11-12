// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3514: one footprint registry for every stamper. On a terrain-generation-38 world every stamped structure registers its
/// ground box (the pads and the crash site too) and every keep-clear check asks the registry — so no two structures' boxes
/// overlap, monuments, ruins, camps, factories and labs included (before, the vaults, cubes, chests, unique sites and the wreck
/// asked only the settlement list and could land in them). A reload registers the same boxes again. Generation 37 is pinned
/// unchanged by <see cref="StructurePlacementGoldenTests"/>.
/// </summary>
public sealed class StructureFootprintTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_footprints_" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public StructureFootprintTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    // ---------------------------------------------------------------- the registry itself

    [Fact]
    public void Overlaps_WrapsAroundTheSeam_AndKeepsTheLargerMargin()
    {
        var reg = new StructureFootprints { Circumference = 1000 };
        reg.Add("monument", (995, 0, 4, 4), margin: 6);

        Assert.True(reg.Overlaps(6, 0, 2, 0)); // across the seam: |dx| = 11 < 2 + 4 + the entry's own margin 6
        Assert.False(reg.Overlaps(10, 0, 2, 0)); // |dx| = 15
        Assert.True(reg.Overlaps(10, 0, 2, 10)); // a wider asking margin reaches further: 15 < 2 + 4 + 10
        Assert.False(reg.Overlaps(6, 0, 2, 0, exceptKind: "monument"));
    }

    /// <summary>The shared searches take the boxes as a list checked with their own margin: every box grown by what its margin
    /// exceeds the search's answers exactly like the registry's own query.</summary>
    [Fact]
    public void Reserved_AnswersLikeOverlaps_ForTheSearchMargin()
    {
        var reg = new StructureFootprints { Circumference = 4000 };
        var rng = new Random(3514);
        for (int i = 0; i < 40; i++)
        {
            reg.Add(i % 2 == 0 ? StructureTypeRules.Settlement : StructureTypeRules.Ruin,
                (rng.Next(4000), rng.Next(-300, 300), 2 + rng.Next(20), 2 + rng.Next(20)), margin: rng.Next(3) * 6);
        }

        var lodge = StructureTypeRules.Defaults.Get(StructureTypeRules.Lodge);
        Func<string, int, int> margins = (kind, own) => lodge.MarginFrom(kind, own);
        var reserved = reg.Reserved(margins, searchMargin: 6);
        for (int probe = 0; probe < 2000; probe++)
        {
            int x = rng.Next(4000), z = rng.Next(-320, 320), hw = 1 + rng.Next(12), hl = 1 + rng.Next(12);
            bool viaList = reserved.Any(r => Math.Abs(WorldConstants.WrapDeltaX(x - r.Cx, 4000)) < hw + r.Hw + 6 && Math.Abs(z - r.Cz) < hl + r.Hl + 6);
            Assert.Equal(reg.Overlaps(x, z, hw, hl, margins), viaList);
        }
    }

    [Fact]
    public void Collisions_IgnoreTheBoxesOfOneStructure()
    {
        var reg = new StructureFootprints { Circumference = 1000 };
        reg.Add("intercity", (100, 0, 10, 3), margin: 6, owner: "line");
        reg.Add("intercity", (110, 0, 10, 3), margin: 6, owner: "line");
        reg.Add("vault", (200, 0, 6, 6), margin: 6);

        Assert.Empty(reg.Collisions());
        reg.Add("chest", (204, 3, 1, 1), margin: 6);
        Assert.Single(reg.Collisions());
    }

    // ---------------------------------------------------------------- generation 38 worlds

    /// <summary>Fresh generation-38 start worlds with the intercity line, the abandoned station and the landmark at chance 1:
    /// no two structures' boxes overlap — but for the overlaps that are the design (see <see cref="ByDesign"/>) — and a reload
    /// of the save registers exactly the same boxes. Between them the seeds stamp every kind: towns, the line, ruins, the
    /// station, camps, huts, outposts, SPS labs, monuments, factories, the wreck, vaults, cubes, chests, a unique site, Soaniri's
    /// sites, the city world and (below) a landmark — a sweep of forty generation-38 worlds found no other overlap.</summary>
    [Theory]
    [InlineData("meadowlands", 4L, new[] { "intercity", "ruin", "lodge", "outpost", "factory", "monument", "wreck", "vault", "chest" })]
    [InlineData("jungle", 9L, new[] { "banditcamp", "intercity", "ruin", "datacube", "rail_ruin" })]
    [InlineData("scrapyard", 2L, new[] { "unique_site", "factory", "monument", "vault", "chest" })]
    [InlineData("titas", 20261112L, new[] { "spslab" })]
    [InlineData("sounder_world", 20261112L, new[] { "sounder_site", "monument" })]
    [InlineData("gds_desert", 20261107L, new[] { "settlement", "underground", "banditcamp", "monument" })]
    public void AGeneration38World_KeepsEveryFootprintClearOfEveryOther(string planet, long seed, string[] kinds)
        => AssertClearAndReplayed(planet, seed, Content, kinds);

    /// <summary>The same with a landmark (the shipped samples made rollable, as the placement golden does).</summary>
    [Fact]
    public void AGeneration38LandmarkWorld_KeepsEveryFootprintClearOfEveryOther()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var samples = content.Landmarks.ToList();
        foreach (var t in samples)
        {
            t.PinOnly = false;
        }

        content.SetLandmarks(samples);
        AssertClearAndReplayed("meadowlands", 20261111L, content, new[] { StructureTypeRules.Landmark });
    }

    private void AssertClearAndReplayed(string planet, long seed, GameContent content, string[] kinds)
    {
        string name = $"{planet}_{seed}";
        var first = Boot(name, planet, seed, content, out var collisions);
        var second = Boot(name, planet, seed, content, out _);

        Assert.All(kinds, kind => Assert.Contains(first, f => f.Kind == kind)); // the seed still stamps what the case is there for
        _output.WriteLine($"{name}: {first.Count} boxes — " + string.Join(", ", first.GroupBy(f => f.Kind).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}×{g.Count()}")));
        var unexpected = collisions.Where(c => !ByDesign(planet, c.A, c.B)).ToList();
        Assert.True(unexpected.Count == 0, $"{name}: footprints overlap:\n" + string.Join("\n", unexpected.Select(c => $"  {Line(c.A)}  ×  {Line(c.B)}")));
        Assert.Equal(first.Select(Line), second.Select(Line));
    }

    /// <summary>The overlaps the registry holds on purpose: the intercity line's stations and end spans belong to the towns they
    /// join; the city world's one city is built around its pads and the crash site and hides its G.D.S. ruin deep underneath
    /// (under the pads too — its hatch keeps clear of them).</summary>
    private static bool ByDesign(string planet, StructureFootprints.Footprint a, StructureFootprints.Footprint b)
    {
        bool Pair(string x, string y) => (a.Kind == x && b.Kind == y) || (a.Kind == y && b.Kind == x);
        if (Pair(StructureTypeRules.Intercity, StructureTypeRules.Settlement))
        {
            return true;
        }

        bool cityWorld = Content.GetPlanet(planet)?.CityWorld.Length > 0;
        return cityWorld && (Pair(StructureTypeRules.Settlement, StructureTypeRules.Pad) || Pair(StructureTypeRules.Settlement, StructureTypeRules.WreckSite)
                             || Pair(StructureTypeRules.Settlement, StructureTypeRules.Wreck) || Pair(StructureTypeRules.Settlement, StructureTypeRules.Underground)
                             || Pair(StructureTypeRules.Underground, StructureTypeRules.Pad));
    }

    private static string Line(StructureFootprints.Footprint f) => $"{f.Kind}@{f.Cx},{f.Cz}±{f.Hw},{f.Hl}+{f.Margin}";

    private IReadOnlyList<StructureFootprints.Footprint> Boot(string name, string planet, long seed, GameContent content,
        out List<(StructureFootprints.Footprint A, StructureFootprints.Footprint B)> collisions)
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            IntercityRailChance = 1.0,
            RailRuinChance = 1.0,
            LandmarkChance = 1.0,
        };
        config.World.TerrainGeneration = WorldDescription.StructurePipelineGeneration;
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        var boxes = server.FootprintsForTest;
        collisions = server.FootprintCollisionsForTest();
        server.Stop();
        return boxes;
    }
}
