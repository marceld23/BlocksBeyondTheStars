// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// P0 — the story engine wired into the server: a fresh world starts the default pack, gameplay events
/// advance the shared per-save arc by the threshold score, fragments dedupe, combat is capped, the "none"
/// sandbox disables it, and progress survives a relaunch.
/// </summary>
public sealed class GameServerStoryTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GameServerStoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_gsstory_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private void Run(string world, Action<SvGameServer> body)
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 5,
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

    [Fact]
    public void Fresh_world_defaults_to_vega_protocol_and_reveals_the_first_beat()
    {
        Run("fresh", server =>
        {
            var snap = server.StorySnapshot;
            Assert.Equal("vega_protocol", snap.StoryId);
            Assert.Equal(1, snap.BeatsRevealed); // B0 ("Systems online") at threshold 0
            Assert.False(snap.Defeated);
        });
    }

    [Fact]
    public void Fragments_advance_the_beat_arc_by_the_progress_score()
    {
        Run("frag", server =>
        {
            for (int i = 0; i < 5; i++)
            {
                server.RecordStoryFragmentForTest("frag_" + i); // 5*3 = 15 progress
            }

            var snap = server.StorySnapshot;
            Assert.Equal(5, snap.Fragments);
            Assert.Equal(3, snap.BeatsRevealed); // thresholds 0,6,14 crossed; 24 not yet
        });
    }

    [Fact]
    public void Duplicate_fragment_keys_are_not_counted_twice()
    {
        Run("dup", server =>
        {
            server.RecordStoryFragmentForTest("same");
            server.RecordStoryFragmentForTest("same");
            Assert.Equal(1, server.StorySnapshot.Fragments);
        });
    }

    [Fact]
    public void Machine_kills_advance_the_story_but_stay_capped()
    {
        Run("kills", server =>
        {
            for (int i = 0; i < 100; i++)
            {
                server.RecordStoryMachineKillForTest();
            }

            var snap = server.StorySnapshot;
            Assert.Equal(100, snap.Kills);
            // capped at 40 -> progress 40 -> beats at thresholds 0,6,14,24,36 (B5=50 unreached) = 5
            Assert.Equal(5, snap.BeatsRevealed);
        });
    }

    [Fact]
    public void Selecting_none_disables_the_story()
    {
        Run("none", server =>
        {
            server.SetActiveStoryForTest("none");
            server.RecordStoryFragmentForTest("ignored");
            var snap = server.StorySnapshot;
            Assert.Equal("none", snap.StoryId);
            Assert.Equal(0, snap.Fragments);
            Assert.Equal(0, snap.BeatsRevealed);
        });
    }

    [Fact]
    public void Switching_pack_resets_progress_and_reveals_the_first_beat()
    {
        Run("switch", server =>
        {
            for (int i = 0; i < 6; i++)
            {
                server.RecordStoryFragmentForTest("f" + i);
            }

            server.SetActiveStoryForTest("vega_protocol"); // re-select resets
            var snap = server.StorySnapshot;
            Assert.Equal("vega_protocol", snap.StoryId);
            Assert.Equal(0, snap.Fragments);
            Assert.Equal(1, snap.BeatsRevealed); // back to just B0
        });
    }

    [Fact]
    public void Story_progress_persists_across_a_relaunch()
    {
        Run("persist", server =>
        {
            for (int i = 0; i < 4; i++)
            {
                server.RecordStoryFragmentForTest("f" + i);
            }

            server.RecordStoryMachineKillForTest();
        });

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        Run("persist", server =>
        {
            var snap = server.StorySnapshot;
            Assert.Equal("vega_protocol", snap.StoryId);
            Assert.Equal(4, snap.Fragments);
            Assert.Equal(1, snap.Kills);
        });
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
