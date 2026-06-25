// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// P2 — net fragments scattered on planet surfaces (datacube-style): deterministic per seed, drawn from the
/// active pack's still-needed pool, picked up to advance the shared story, and never re-offered once found.
/// </summary>
public sealed class GameServerNetFragmentTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GameServerNetFragmentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_netfrag_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private void Run(string world, long seed, Action<SvGameServer> body)
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        try
        {
            body(server);
        }
        finally
        {
            server.Stop();
        }
    }

    private static List<(string Key, string Cat, float X, float Y, float Z)> Shape(SvGameServer s)
        => s.NetFragmentSnapshots.Select(f => (f.Key, f.Category, f.Pos.X, f.Pos.Y, f.Pos.Z)).ToList();

    [Fact]
    public void Placement_is_deterministic_per_seed()
    {
        List<(string, string, float, float, float)> a = null!, b = null!;
        Run("det_a", 7, s => a = Shape(s));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Run("det_b", 7, s => b = Shape(s));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Placed_fragments_are_valid_pack_keys_and_unique()
    {
        var packKeys = new HashSet<string>(_content.Stories["vega_protocol"].Fragments.Select(f => f.Key));
        Run("valid", 5, s =>
        {
            var keys = s.NetFragmentSnapshots.Select(f => f.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count()); // no duplicate fragment on one world
            foreach (var f in s.NetFragmentSnapshots)
            {
                Assert.Contains(f.Key, packKeys);
                Assert.False(string.IsNullOrEmpty(f.Category));
            }
        });
    }

    [Fact]
    public void Picking_up_a_fragment_advances_the_story_and_removes_it()
    {
        bool tested = false;
        for (long seed = 1; seed <= 50 && !tested; seed++)
        {
            Run("pick_" + seed, seed, s =>
            {
                if (s.NetFragmentCount == 0)
                {
                    return;
                }

                var frag = s.NetFragmentSnapshots[0];
                int before = s.StorySnapshot.Fragments;
                Assert.True(s.PickUpNetFragmentForTest(frag.Id));
                Assert.Equal(before + 1, s.StorySnapshot.Fragments);
                Assert.DoesNotContain(s.NetFragmentSnapshots, f => f.Id == frag.Id);
                tested = true;
            });
        }

        Assert.True(tested, "no seed in 1..50 placed a net fragment on the start world");
    }

    [Fact]
    public void A_found_fragment_is_not_placed_again_after_relaunch()
    {
        bool tested = false;
        for (long seed = 1; seed <= 50 && !tested; seed++)
        {
            string world = "redo_" + seed;
            string? foundKey = null;

            Run(world, seed, s =>
            {
                if (s.NetFragmentCount == 0)
                {
                    return;
                }

                var frag = s.NetFragmentSnapshots[0];
                foundKey = frag.Key;
                s.PickUpNetFragmentForTest(frag.Id);
            });

            if (foundKey is null)
            {
                continue;
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Run(world, seed, s =>
            {
                Assert.DoesNotContain(s.NetFragmentSnapshots, f => f.Key == foundKey);
                Assert.True(s.StorySnapshot.Fragments >= 1); // the earlier find persisted
            });
            tested = true;
        }

        Assert.True(tested, "no seed in 1..50 placed a net fragment on the start world");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
