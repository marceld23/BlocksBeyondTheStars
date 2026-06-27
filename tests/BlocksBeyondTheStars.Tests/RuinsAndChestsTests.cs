// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Ruins (fallen-city decay) and standalone treasure chests. Ruins are a real collapse — height-graded,
/// non-empty, varying per seed — and are NOT protected (they ride the settlement list with Ruined=true,
/// which the protection check skips). Chests are rare standalone lootable caches scattered on the surface.
/// </summary>
public sealed class RuinsAndChestsTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public RuinsAndChestsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_ruins_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private static int CountSolids(SettlementStructure s, int yLo, int yHi)
    {
        int n = 0;
        for (int x = 0; x < s.Width; x++)
            for (int y = yLo; y < yHi; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    if (s.Get(x, y, z) != 0) n++;
                }

        return n;
    }

    [Fact]
    public void RuinDecay_IsHeightGraded_NonEmpty_AndDeterministic()
    {
        var intact = SettlementGenerator.Generate("town", ruined: false, 4242, "stone", _content);
        var ruin = SettlementGenerator.Generate("town", ruined: true, 4242, "stone", _content);
        var ruin2 = SettlementGenerator.Generate("town", ruined: true, 4242, "stone", _content);

        int ruinTotal = CountSolids(ruin, 0, ruin.Height);
        int intactTotal = CountSolids(intact, 0, intact.Height);
        int bottom = CountSolids(ruin, 0, ruin.Height / 2);
        int top = CountSolids(ruin, ruin.Height / 2, ruin.Height);

        Assert.True(ruinTotal > 0, "A ruin must still have standing blocks.");
        Assert.True(ruinTotal < intactTotal, "A ruin must have collapsed (fewer blocks than the intact build).");
        Assert.True(bottom > top, "Collapse is height-graded — the lower half keeps more standing than the upper half.");

        // Deterministic from the seed.
        for (int x = 0; x < ruin.Width; x++)
            for (int y = 0; y < ruin.Height; y++)
                for (int z = 0; z < ruin.Length; z++)
                {
                    Assert.Equal(ruin.Get(x, y, z), ruin2.Get(x, y, z));
                }
    }

    private SvGameServer Start(long seed, out SqliteWorldRepository repo, bool settlements, bool ruins, bool chests)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "w" + seed));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "w" + seed,
            Seed = seed,
            StartPlanet = "jungle", // a hospitable world so structures actually appear
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = settlements,
            PlaceRuins = ruins,
            PlaceChests = chests,
            PlaceWrecks = false,
            PlaceVaults = false,
            PlaceDataCubes = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void Ruins_Spawn_AsUnprotectedTerrain_NotAsStructures()
    {
        // Settlements OFF, ruins ON ⇒ any ruin loot cache must come from StampRuins. Ruins are NOT registered
        // as settlements/structures (so they aren't protected) — they're just terrain + scavengeable caches.
        bool foundRuin = false;
        for (long seed = 1; seed <= 60 && !foundRuin; seed++)
        {
            var server = Start(seed, out var repo, settlements: false, ruins: true, chests: false);
            using (repo)
            {
                if (server.Containers.Any(c => c.Id.StartsWith("loot_ruin", StringComparison.Ordinal)))
                {
                    foundRuin = true;
                    Assert.Equal(0, server.SettlementCount); // ruins are terrain, never a protected structure
                }
            }
        }

        Assert.True(foundRuin, "StampRuins should place at least one ruin (with a loot cache) across 60 seeds.");
    }

    [Fact]
    public void Chests_Spawn_AndAreLootable()
    {
        bool found = false;
        for (long seed = 1; seed <= 60 && !found; seed++)
        {
            var server = Start(seed, out var repo, settlements: false, ruins: false, chests: true);
            using (repo)
            {
                var chest = server.Containers.FirstOrDefault(c => c.Id.StartsWith("loot_chest", StringComparison.Ordinal));
                if (chest is null)
                {
                    continue;
                }

                found = true;
                Assert.NotEmpty(chest.Items); // a chest carries loot

                var p = server.AddLocalPlayer("Looter");
                p.State.AboardShip = false;
                p.State.Position = new Vector3f(chest.Position.X + 0.5f, chest.Position.Y + 0.5f, chest.Position.Z + 0.5f);

                server.LootContainer("Looter", chest.Id);

                Assert.True(p.State.Inventory.SlotCount > 0 && p.State.Inventory.Slots.Any(s => s is { IsEmpty: false }),
                    "Looting a chest should put items into the player's inventory.");
                Assert.DoesNotContain(server.Containers, c => c.Id == chest.Id); // emptied chest despawns
            }
        }

        Assert.True(found, "StampChests should scatter at least one lootable chest across 60 seeds.");
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
