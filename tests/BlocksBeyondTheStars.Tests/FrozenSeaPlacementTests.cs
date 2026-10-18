// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2738: on cold worlds structures were seated on or under the sea ice. The surface-water query reads a thick sheet (or a
/// body frozen through) as land — a ship may land on a frozen sea — and the structure searches used it: a first-fit seat
/// stood at the SEABED height under the ice, a ranked stilts seat inside the sheet. From terrain generation 27 every
/// structure search counts sea ice as wet: dry land wins, and a seat that has to go onto the ice stands on stilts above
/// it. A generation-26 world keeps every placement bit for bit, and a warm world places exactly as before.
/// </summary>
public sealed class FrozenSeaPlacementTests : IDisposable
{
    private const int Gen = WorldDescription.SeaIceWetGeneration;
    private const int OldGen = Gen - 1;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_frozensea_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private SvGameServer Start(string planet, long seed, int generation, out SqliteWorldRepository repo)
    {
        string name = planet + seed + "_g" + generation;
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        config.World.TerrainGeneration = generation;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    // Frozen worlds on which, at generation 26, settlements, bandit camps, lodges, pioneer outposts, factories, rail ruins
    // and a unique site stood under the sea ice (a sweep over six seeds each of ice, tundra, glacier, frozen_ocean, icecap
    // and boreal found structures seated below the ice on 26 of 36 worlds — see docs/developer/WORLD_GENERATION.md §44).
    // Four seeds keep the CI budget while covering every kind the sweep caught.
    [Theory]
    [InlineData("frozen_ocean", 1L)]
    [InlineData("frozen_ocean", 2L)]
    [InlineData("tundra", 5L)]
    [InlineData("glacier", 6L)]
    public void FreshFrozenWorld_SeatsNothingUnderTheIce(string planet, long seed)
    {
        var server = Start(planet, seed, Gen, out var repo);
        using (repo)
        {
            var gen = server.World.Generator;
            var body = server.World.Planet;
            int sea = gen.SeaLevel(body);
            string world = $"{planet}/{seed}";

            foreach (var (kind, requested, placed) in server.StampReportForTest)
            {
                Assert.True(placed == requested, $"{world}: {kind} placed {placed}/{requested} — the guarantee must not drop instances");
            }

            // Vaults are buried by design; the starter wreck has its own rule (below); everything else stands in the open.
            foreach (var wreck in server.PlacementRecordsForTest.Where(r => r.Placed && r.Seat == "crash"))
            {
                AssertTheWreckIsNotUnderTheIce(server, wreck, world);
            }

            var records = server.PlacementRecordsForTest
                .Where(r => r.Placed && !r.OnIsland && r.Seat is not ("buried" or "wellhead" or "crash"))
                .ToList();
            Assert.NotEmpty(records);
            int stilts = 0;
            foreach (var r in records)
            {
                string at = $"{world}: {r.Kind}#{r.Index} ({r.Seat}) at ({r.X},{r.GroundY},{r.Z})";
                Assert.True(r.GroundY >= sea, $"{at} is seated below the sea level ({sea})");
                if (r.Seat == "stilts")
                {
                    stilts++;
                    if (gen.TryGetIceSurface(body, r.X, r.Z, out int iceTop))
                    {
                        Assert.True(r.GroundY > iceTop, $"{at}: the deck is not above the ice top ({iceTop})");
                    }

                    if (gen.TryGetWaterSurface(body, r.X, r.Z, out int waterTop, out _))
                    {
                        Assert.True(r.GroundY > waterTop, $"{at}: the deck is not above the water ({waterTop})");
                    }
                }
                else
                {
                    Assert.True(gen.SurfaceIceThickness(body, r.X, r.Z) == 0, $"{at} stands on sea ice without stilts");
                }
            }

            Assert.True(stilts < records.Count, $"{world}: every structure went onto the ice — dry land should win");
        }
    }

    /// <summary>#2756: the starter wreck's crash site lies on dry land (its ground at or above the sea) or rests on the ice
    /// top / the water line — never on the seabed below them.</summary>
    private static void AssertTheWreckIsNotUnderTheIce(SvGameServer server, StructurePlacementRecord wreck, string world)
    {
        var gen = server.World.Generator;
        var body = server.World.Planet;
        string at = $"{world}: the wreck at ({wreck.X},{wreck.GroundY},{wreck.Z})";
        Assert.Equal(new Vector3i(wreck.X, wreck.GroundY - 1, wreck.Z), server.WreckOriginForTest);
        if (gen.TryGetIceSurface(body, wreck.X, wreck.Z, out int iceTop))
        {
            Assert.True(wreck.GroundY >= iceTop, $"{at} lies below the ice top ({iceTop})");
        }
        else if (gen.TryGetWaterSurface(body, wreck.X, wreck.Z, out int waterTop, out _))
        {
            Assert.True(wreck.GroundY >= waterTop, $"{at} lies below the water line ({waterTop})");
        }
        else
        {
            Assert.True(wreck.GroundY >= gen.SeaLevel(body), $"{at} lies below the sea level");
        }
    }

    [Fact]
    public void FreshFrozenWorld_TheStarterWreckLiesOnDryLandOrOnTheIce()
    {
        // frozen_ocean/4 (and glacier/6 in the theory above): at generation 26 the pad-relative crash site lay on the
        // seabed under the frozen sea — (-56, 59, 56) under ice up to 61.
        var server = Start("frozen_ocean", 4, Gen, out var repo);
        using (repo)
        {
            Assert.True(server.HasWreck);
            var wreck = server.PlacementRecordsForTest.Single(r => r.Kind == "wreck" && r.Placed);
            Assert.NotEqual((-56, 59, 56), (wreck.X, wreck.GroundY, wreck.Z));
            AssertTheWreckIsNotUnderTheIce(server, wreck, "frozen_ocean/4");
        }
    }

    [Fact]
    public void Generation26_KeepsItsWreck_BitForBit()
    {
        // Pinned from the code before #2756: the generation-26 wreck of frozen_ocean/4 stays on the seabed where it lies.
        var server = Start("frozen_ocean", 4, OldGen, out var repo);
        using (repo)
        {
            var wreck = server.PlacementRecordsForTest.Single(r => r.Kind == "wreck" && r.Placed);
            Assert.Equal((-56, 59, 56, "crash"), (wreck.X, wreck.GroundY, wreck.Z, wreck.Seat));
            Assert.Equal(new Vector3i(-56, 58, 56), server.WreckOriginForTest);
        }
    }

    [Fact]
    public void Generation26_KeepsItsPlacements_BitForBit()
    {
        // Pinned from the code before #2738 (frozen_ocean, seed 1): two settlements, the bandit camp and the lodge sat on
        // the seabed under the frozen sea (sea level 59). An existing world never moves a structure.
        var server = Start("frozen_ocean", 1, OldGen, out var repo);
        using (repo)
        {
            var records = server.PlacementRecordsForTest;
            Assert.Contains(records, r => (r.Kind, r.Index, r.X, r.GroundY, r.Z, r.Seat) == ("settlement", 1, 774, 8, -2063, "flat"));
            Assert.Contains(records, r => (r.Kind, r.Index, r.X, r.GroundY, r.Z, r.Seat) == ("settlement", 2, 1718, 14, 1714, "flat"));
            Assert.Contains(records, r => (r.Kind, r.Index, r.X, r.GroundY, r.Z, r.Seat) == ("banditcamp", 0, -583, 10, -672, "flat"));
            Assert.Contains(records, r => (r.Kind, r.Index, r.X, r.GroundY, r.Z, r.Seat) == ("lodge", 0, 106, 9, 49, "flat"));
        }
    }

    // A warm sea world and a swamp: a few frozen ponds high up (the coarse sweep found 113 and 76 iced columns on an
    // 8-block grid), none under a candidate footprint. Even "warm" worlds can freeze a mountain pond — on ocean/71 a
    // shelf seat over one became a stilts seat above its ice — so these two are pinned rather than any warm world.
    [Theory]
    [InlineData("coral_sea", 1L)]
    [InlineData("swamp", 67L)]
    public void AWarmWorld_PlacesExactlyAsBefore(string planet, long seed)
    {
        // No candidate touches ice: the gate never fires, and the search draws its candidates before it, so generation 27
        // places every structure where generation 26 did. The starter wreck is the exception by design (#2756): a crash
        // site in open water moves to dry land or rests on the water line too — the swamp's lay on the seabed.
        List<string> Placements(int generation)
        {
            var server = Start(planet, seed, generation, out var repo);
            using (repo)
            {
                if (generation >= Gen)
                {
                    foreach (var wreck in server.PlacementRecordsForTest.Where(r => r.Placed && r.Seat == "crash"))
                    {
                        AssertTheWreckIsNotUnderTheIce(server, wreck, $"{planet}/{seed}");
                    }
                }

                return server.PlacementRecordsForTest
                    .Where(r => r.Kind != "wreck")
                    .Select(r => $"{r.Kind}#{r.Index} {r.Placed} ({r.X},{r.GroundY},{r.Z}) {r.Seat} {r.OnIsland} {r.Name} {r.Template}")
                    .ToList();
            }
        }

        var old = Placements(OldGen);
        Assert.NotEmpty(old);
        Assert.Equal(old, Placements(Gen));
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
