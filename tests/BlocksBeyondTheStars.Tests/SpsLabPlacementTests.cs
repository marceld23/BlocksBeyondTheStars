// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2696: the SPS labs on Titas stand on dry land with their pads under the open sky. The frozen sea's thick ice read as
/// land to the placement's wet gate, so many labs were seated on the seabed — the ship pad under ice and water, counted
/// as roofed (no air, −90 °C), the compound hard to reach. The search now refuses such seats; a pinned placement record
/// keeps its lab wherever it stands.
/// </summary>
public sealed class SpsLabPlacementTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SpsLabPlacementTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_spslab_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(string world, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = "titas",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void OpenColumns_StayRoofless_InEveryLayout()
    {
        // The placement trusts the generator's list: every listed column carries only its ground or pad row.
        for (long seed = 0; seed < 64; seed++)
        {
            var s = SpsLabGenerator.Generate(seed * 7919 + 13, "snow", _content);
            foreach (var (x, z) in SpsLabGenerator.OpenColumns)
            {
                Assert.NotEqual(0, s.Get(x, 0, z));
                for (int y = 1; y < s.Height; y++)
                {
                    Assert.True(s.Get(x, y, z) == 0, $"layout {seed}: open column ({x},{z}) has a block at y {y}");
                }
            }
        }
    }

    // Titas worlds on which, before #2696, one to three labs stood below the frozen sea with their pad roofed. Four seeds
    // keep the CI budget; the full sweep over seeds 70–100 (134 of 134 labs placed, 25 roofed pads → 0) was run once by
    // hand when the fix landed (see docs/developer/WORLD_GENERATION.md, SPS labs).
    [Theory]
    [InlineData(82L)]
    [InlineData(85L)]
    [InlineData(93L)]
    [InlineData(97L)]
    public void FreshTitas_EveryLabStandsUnderTheOpenSky(long seed)
    {
        var server = Start("open_" + seed, seed, out var repo);
        using (repo)
        {
            var gen = server.World.Generator;
            var planet = server.World.Planet;
            int sea = gen.SeaLevel(planet);
            var labs = server.SpsLabsForTest();
            var (_, requested, placed) = server.StampReportForTest.Single(r => r.Kind == "spslab");
            Assert.InRange(requested, 3, 6);
            Assert.Equal(requested, placed); // these worlds have dry land enough for every rolled lab
            Assert.Equal(placed, labs.Count);

            var water = _content.GetBlock("water")!.NumericId;
            var ice = _content.GetBlock("ice")!.NumericId;
            foreach (var lab in labs)
            {
                Assert.True(lab.Min.Y >= sea, $"seed {seed}: lab at {lab.Min} has its floor below the sea ({sea})");
                foreach (var (lx, lz) in SpsLabGenerator.OpenColumns)
                {
                    int x = lab.Min.X + lx, z = lab.Min.Z + lz;
                    var floor = server.World.GetBlock(new Vector3i(x, lab.Min.Y, z));
                    Assert.True(floor != water && floor != ice, $"seed {seed}: lab at {lab.Min}, open column ({lx},{lz}) stands in water or ice");

                    // Nothing over the open ground as far as the roof probe looks — no ice, no water, no terrain.
                    for (int y = lab.Min.Y + 1; y <= lab.Min.Y + 56; y++)
                    {
                        var b = server.World.GetBlock(new Vector3i(x, y, z));
                        Assert.True(b.IsAir, $"seed {seed}: lab at {lab.Min}, open column ({lx},{lz}) is covered at y {y} by block {b.Value}");
                    }

                    Assert.False(server.InSpsLabForTest(new Vector3f(x + 0.5f, lab.Min.Y + 1f, z + 0.5f)),
                        $"seed {seed}: lab at {lab.Min}, open column ({lx},{lz}) counts as inside");
                }
            }
        }
    }

    [Fact]
    public void PinnedRecord_KeepsItsLab_EvenUnderTheIce()
    {
        // A save from before #2696 may hold a lab on the seabed. Its record wins over the new rule: the re-derived lab
        // stays exactly there, and the record is not rewritten.
        const string world = "pinned";
        Vector3i sunken;
        var server = Start(world, 93, out var repo);
        using (repo)
        {
            var gen = server.World.Generator;
            var planet = server.World.Planet;
            int sea = gen.SeaLevel(planet);
            var first = server.SpsLabsForTest()[0];
            int found = int.MinValue;
            for (int dx = 0; dx < 8192 && found == int.MinValue; dx += 16)
            {
                int x = first.Min.X + dx;
                if (gen.SurfaceIceThickness(planet, x, first.Min.Z) > 0 && gen.SurfaceHeight(planet, x, first.Min.Z) < sea)
                {
                    found = x;
                }
            }

            Assert.NotEqual(int.MinValue, found);
            sunken = new Vector3i(found, gen.SurfaceHeight(planet, found, first.Min.Z), first.Min.Z);
        }

        EditMetadata(world, meta =>
        {
            var rec = meta.Placements.Single(r => r.Kind == "spslab" && r.Index == 0);
            rec.X = sunken.X;
            rec.GroundY = sunken.Y;
            rec.Z = sunken.Z;
            rec.Seat = "flat";
        });

        var reloaded = Start(world, 93, out var repo2);
        using (repo2)
        {
            Assert.Equal(sunken, reloaded.SpsLabsForTest()[0].Min);
            var rec = reloaded.PlacementRecordsForTest.Single(r => r.Kind == "spslab" && r.Index == 0);
            Assert.Equal((sunken.X, sunken.Y, sunken.Z), (rec.X, rec.GroundY, rec.Z));
        }
    }

    /// <summary>Rewrites the persisted metadata of a world between two loads — the way an older save reaches the loader.</summary>
    private void EditMetadata(string world, Action<WorldMetadata> edit)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        repo.Initialize();
        var meta = repo.LoadMetadata() ?? throw new Xunit.Sdk.XunitException("The first load saved no metadata.");
        edit(meta);
        repo.SaveMetadata(meta);
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
