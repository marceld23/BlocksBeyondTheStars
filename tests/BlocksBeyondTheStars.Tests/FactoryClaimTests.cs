// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Claiming a structure with an access (SPS) code: a protected factory becomes the player's editable base.
/// One code claims one factory; the owner (and their allies) may then rebuild it, others still can't, and the
/// claim persists across a reload.
/// </summary>
public sealed class FactoryClaimTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public FactoryClaimTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_claim_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(long seed, string world, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceRuins = false,
            PlaceChests = false,
            PlaceWrecks = false,
            PlaceVaults = false,
            PlaceFactories = true,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private long FirstFactorySeed()
    {
        for (long seed = 1; seed <= 80; seed++)
        {
            var s = Start(seed, "probe" + seed, out var repo);
            using (repo)
            {
                if (s.FactoryCount > 0)
                {
                    return seed;
                }
            }
        }

        throw new Xunit.Sdk.XunitException("No factory across 80 seeds.");
    }

    [Fact]
    public void Claim_ConsumesAccessCode_GrantsOwnerEdit_DeniesOthers_AndPersists()
    {
        long seed = FirstFactorySeed();

        var server = Start(seed, "claimworld", out var repo);
        var fac = server.FactoriesForTest[0];
        var wallCell = new Vector3i((int)System.Math.Floor(fac.Terminal.X), (int)System.Math.Floor(fac.Terminal.Y), (int)System.Math.Floor(fac.Terminal.Z));

        var owner = server.AddLocalPlayer("Owner");
        owner.State.AboardShip = false;
        owner.State.Position = fac.Terminal;

        // Without a code: claim is refused and the factory stays protected against the owner.
        server.ClaimFactory("Owner", fac.Id);
        Assert.True(server.FactoryProtectedForTest(wallCell, "Owner"));
        Assert.Equal(string.Empty, server.FactoriesForTest[0].OwnerId);

        // With a code: claim succeeds, the code is spent, and the owner may now edit.
        owner.State.Inventory.Add("access_code", 1, 10);
        server.ClaimFactory("Owner", fac.Id);

        Assert.Equal(0, owner.State.Inventory.CountOf("access_code")); // spent
        Assert.Equal("Owner", server.FactoriesForTest[0].OwnerId);     // claimed
        Assert.False(server.FactoryProtectedForTest(wallCell, "Owner")); // owner can build
        Assert.True(server.FactoryProtectedForTest(wallCell, "Stranger")); // others still can't

        repo.Dispose();

        // Reload the same world — the claim persists (re-applied from metadata onto the re-derived factory).
        var reloaded = Start(seed, "claimworld", out var repo2);
        using (repo2)
        {
            var f2 = reloaded.FactoriesForTest[0];
            Assert.Equal("Owner", f2.OwnerId);
            Assert.False(reloaded.FactoryProtectedForTest(wallCell, "Owner"));
        }
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
