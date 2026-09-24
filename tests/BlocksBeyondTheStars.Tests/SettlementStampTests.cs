// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Stamping settlements into the world: a lush planet gets a named settlement on its surface with
/// interaction markers; an airless world never does; intact settlements are mining-protected while
/// ruins stay scavengeable. Deterministic from the seed.
/// </summary>
public sealed class SettlementStampTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SettlementStampTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_settle_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string planet, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, planet + seed));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = planet + seed,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Finds a seed that yields a settlement on the given planet (lush worlds almost always do).</summary>
    private SvGameServer StartedWithSettlement(string planet, out SqliteWorldRepository repo)
    {
        for (long seed = 1; seed <= 40; seed++)
        {
            var server = Started(planet, seed, out repo);
            if (server.HasSettlement)
            {
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException($"No settlement found on '{planet}' across 40 seeds.");
    }

    [Fact]
    public void LushPlanet_GetsANamedSettlement_WithMarkers()
    {
        var server = StartedWithSettlement("jungle", out var repo);
        using (repo)
        {
            Assert.True(server.HasSettlement);
            Assert.False(string.IsNullOrWhiteSpace(server.SettlementName));
            Assert.NotEmpty(server.SettlementMarkers);
        }
    }

    [Fact]
    public void AirlessPlanet_NeverGetsASettlement()
    {
        // The asteroid body is airless → uninhabited, regardless of seed.
        for (long seed = 1; seed <= 10; seed++)
        {
            var server = Started("asteroid", seed, out var repo);
            using (repo)
            {
                Assert.False(server.HasSettlement, $"Airless/lifeless asteroid should have no settlement (seed {seed}).");
            }
        }
    }

    [Fact]
    public void Stamp_IsDeterministic_ForSameSeed()
    {
        var a = Started("jungle", 7, out var repoA);
        bool hadA = a.HasSettlement;
        string nameA = a.SettlementName;
        repoA.Dispose();

        var b = Started("jungle", 7, out var repoB);
        using (repoB)
        {
            Assert.Equal(hadA, b.HasSettlement);
            Assert.Equal(nameA, b.SettlementName);
        }
    }

    [Fact]
    public void IntactSettlement_StampsRealBlocksIntoTheWorld()
    {
        var server = StartedWithSettlement("jungle", out var repo);
        using (repo)
        {
            // At least one marker cell area should be inside the stamped footprint; sample some
            // blocks around the first marker and confirm solid structure exists nearby.
            var marker = server.SettlementMarkers.First();
            var basePos = new Vector3i((int)marker.Pos.X, (int)marker.Pos.Y, (int)marker.Pos.Z);

            bool solidNearby = false;
            for (int dx = -4; dx <= 4 && !solidNearby; dx++)
                for (int dy = -1; dy <= 4 && !solidNearby; dy++)
                    for (int dz = -4; dz <= 4 && !solidNearby; dz++)
                    {
                        if (!server.World.GetBlock(new Vector3i(basePos.X + dx, basePos.Y + dy, basePos.Z + dz)).IsAir)
                        {
                            solidNearby = true;
                        }
                    }

            Assert.True(solidNearby, "A stamped settlement should place solid blocks in the world.");
        }
    }

    /// <summary>
    /// #1990: a settlement's blocks are stamped into the world ONCE. Until now every server start wrote the
    /// whole structure again, so a wall a player had mined stood there again after a restart — and a 256×256
    /// city re-wrote 405 332 cells per load. A vault, a monument, a ruin and a bandit camp have always been
    /// stamped once; the buildings now work the same way.
    /// </summary>
    [Fact]
    public void AWallAPlayerMined_StaysMined_WhenTheWorldIsLoadedAgain()
    {
        for (long seed = 1; seed <= 40; seed++)
        {
            var server = Started("jungle", seed, out var repo);
            if (!server.HasSettlement)
            {
                server.Stop();
                repo.Dispose();
                continue;
            }

            // A solid cell of the stamped settlement, mined away by a player.
            var marker = server.SettlementMarkers.First();
            var basePos = new Vector3i((int)marker.Pos.X, (int)marker.Pos.Y, (int)marker.Pos.Z);
            Vector3i? wall = null;
            for (int dx = -4; dx <= 4 && wall is null; dx++)
                for (int dy = -1; dy <= 4 && wall is null; dy++)
                    for (int dz = -4; dz <= 4 && wall is null; dz++)
                    {
                        var cell = new Vector3i(basePos.X + dx, basePos.Y + dy, basePos.Z + dz);
                        if (!server.World.GetBlock(cell).IsAir)
                        {
                            wall = cell;
                        }
                    }

            Assert.True(wall is not null, "sanity: the settlement stamped solid blocks");
            server.World.SetBlock(wall!.Value, BlockId.Air, owner: "Marcel");
            Assert.True(server.World.GetBlock(wall.Value).IsAir, "sanity: the cell is mined");
            server.Stop();
            repo.Dispose();

            // Load the same world again: the settlement must not rebuild itself over the hole.
            var again = Started("jungle", seed, out var repo2);
            using (repo2)
            {
                Assert.True(again.World.GetBlock(wall.Value).IsAir,
                    "a mined settlement wall must stay mined when the world is loaded again (#1990)");
                again.Stop();
            }

            return;
        }

        throw new Xunit.Sdk.XunitException("No settlement found on 'jungle' across 40 seeds.");
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
