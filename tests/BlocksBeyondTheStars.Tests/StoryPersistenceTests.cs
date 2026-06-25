// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// P0 — the per-save story-state persistence (server-wide, like the alliance graph): a JSON-blob row keyed by
/// story id, surviving a relaunch.
/// </summary>
public sealed class StoryPersistenceTests : IDisposable
{
    private readonly string _root;

    public StoryPersistenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_story_" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void StoryState_round_trips_through_sqlite_across_a_relaunch()
    {
        var paths = new SaveGamePaths(_root, "story");
        var state = new StoredStoryState
        {
            StoryId = "vega_protocol",
            FragmentsFound = 5,
            MachineKills = 12,
            Milestones = 3,
            BeatsRevealed = 7,
            GuardianSystemRevealed = true,
            GuardianDefeated = false,
            FoundFragmentKeys = new() { "frag_a", "frag_b" },
        };

        using (var repo = new SqliteWorldRepository(paths))
        {
            repo.Initialize();
            repo.SaveStoryState(state);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var repo2 = new SqliteWorldRepository(paths))
        {
            repo2.Initialize();
            var s = Assert.Single(repo2.ListStoryStates());
            Assert.Equal("vega_protocol", s.StoryId);
            Assert.Equal(5, s.FragmentsFound);
            Assert.Equal(12, s.MachineKills);
            Assert.Equal(3, s.Milestones);
            Assert.Equal(7, s.BeatsRevealed);
            Assert.True(s.GuardianSystemRevealed);
            Assert.False(s.GuardianDefeated);
            Assert.Equal(new[] { "frag_a", "frag_b" }, s.FoundFragmentKeys.OrderBy(x => x).ToArray());
        }
    }

    [Fact]
    public void SaveStoryState_upserts_by_story_id()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "story2"));
        repo.Initialize();
        repo.SaveStoryState(new StoredStoryState { StoryId = "vega_protocol", FragmentsFound = 1 });
        repo.SaveStoryState(new StoredStoryState { StoryId = "vega_protocol", FragmentsFound = 9 });
        var s = Assert.Single(repo.ListStoryStates());
        Assert.Equal(9, s.FragmentsFound); // replaced in place, not duplicated
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
