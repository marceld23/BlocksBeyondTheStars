// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldHost;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The operator's world-detail page reads a real save through <see cref="WorldInspector"/> (issue #1063).
///
/// These tests write the save with the game's own <see cref="SqliteWorldRepository"/> so the schema is the one
/// production produces — the bug they pin down (an aggregate inside a correlated sub-select) only fires on the
/// attributed schema every save has had since #490, and no test exercised that path before.
/// </summary>
public sealed class WorldInspectorTests : IDisposable
{
    private const string WorldId = "577a035779b4";
    private const string Planet = "sys0-p1";

    private readonly string _root;
    private readonly WorldHostConfig _config;

    public WorldInspectorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_inspector_" + Guid.NewGuid().ToString("N"));
        _config = new WorldHostConfig { WorldsDir = _root };
    }

    [Fact]
    public void AttributedSave_ReportsHotspotWithMostRecentEditor_AndNoProblem()
    {
        // Two builders in one 32-block bucket. "bob" gets the higher player_ref id (registered second) but
        // "a17" made the most recent edit — "last editor" has to be a17, not the highest id.
        using (var repo = OpenRepository())
        {
            for (int i = 0; i < 20; i++)
            {
                repo.SetBlock(Planet, new Vector3i(i, 64, 3), 5, owner: "a17");
            }

            for (int i = 0; i < 10; i++)
            {
                repo.SetBlock(Planet, new Vector3i(i, 65, 3), 5, owner: "bob");
            }

            repo.SavePlayer(new PlayerState { PlayerId = "a17", Name = "a17", CurrentLocationId = Planet });
            repo.Flush();
        }

        // Stamp the timestamps explicitly: SetBlock uses wall-clock seconds, and thirty edits in one test tick
        // would all tie. bob's are older, a17's newest edit is the maximum in the bucket.
        StampEdits(owner: "bob", unix: 1_000);
        StampEdits(owner: "a17", unix: 2_000);

        var insight = WorldInspector.Read(_config, WorldId);

        Assert.Null(insight.Problem);
        Assert.Null(insight.HotspotsProblem);
        Assert.Null(insight.PlayersProblem);
        Assert.Null(insight.BuildsProblem);

        var hotspot = Assert.Single(insight.Hotspots);
        Assert.Equal(Planet, hotspot.Body);
        Assert.Equal(30, hotspot.Edits);
        Assert.Equal("a17", hotspot.LastEditor);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2_000).UtcDateTime, hotspot.LastEditUtc);

        var player = Assert.Single(insight.Players);
        Assert.Equal("a17", player.Name);
    }

    [Fact]
    public void LegacySaveWithoutAttribution_StillListsHotspots()
    {
        using (var repo = OpenRepository())
        {
            for (int i = 0; i < 30; i++)
            {
                repo.SetBlock(Planet, new Vector3i(i, 64, 3), 5, owner: "a17");
            }

            repo.Flush();
        }

        // Rebuild block_edit without the #490 columns, as a save from an older build would have it.
        Execute(
            "CREATE TABLE legacy AS SELECT planet, x, y, z, block, tint, glow, shape FROM block_edit;" +
            "DROP TABLE block_edit; ALTER TABLE legacy RENAME TO block_edit;");

        var insight = WorldInspector.Read(_config, WorldId);

        Assert.Null(insight.Problem);
        Assert.Null(insight.HotspotsProblem);
        var hotspot = Assert.Single(insight.Hotspots);
        Assert.Equal(30, hotspot.Edits);
        Assert.Equal(string.Empty, hotspot.LastEditor);
        Assert.Null(hotspot.LastEditUtc);
    }

    [Fact]
    public void BrokenSection_DoesNotBlankTheOtherCards()
    {
        using (var repo = OpenRepository())
        {
            repo.SavePlayer(new PlayerState { PlayerId = "a17", Name = "a17", CurrentLocationId = Planet });
            repo.Flush();
        }

        // Sabotage only the hotspot source: the players card must survive with its rows and the failure has
        // to be reported on the hotspot card, not as a page-wide "could not read the world save".
        Execute("DROP TABLE block_edit; CREATE TABLE block_edit (planet TEXT NOT NULL);");

        var insight = WorldInspector.Read(_config, WorldId);

        Assert.Null(insight.Problem);
        Assert.NotNull(insight.HotspotsProblem);
        Assert.Contains("build hotspots", insight.HotspotsProblem, StringComparison.Ordinal);
        Assert.Equal("a17", Assert.Single(insight.Players).Name);
    }

    [Fact]
    public void MissingSave_IsAnEmptyStateNotAnError()
    {
        var insight = WorldInspector.Read(_config, WorldId);

        Assert.NotNull(insight.Problem);
        Assert.Contains("never been started", insight.Problem, StringComparison.Ordinal);
        Assert.Empty(insight.Players);
    }

    private SqliteWorldRepository OpenRepository()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(SavePaths.HostSavesDir(_config, WorldId), WorldId));
        repo.Initialize();
        return repo;
    }

    private void StampEdits(string owner, long unix)
        => Execute("UPDATE block_edit SET edited_unix = " + unix +
                   " WHERE owner_id = (SELECT id FROM player_ref WHERE name = '" + owner + "');");

    private void Execute(string sql)
    {
        using var con = new SqliteConnection("Data Source=" + SavePaths.WorldDbPath(_config, WorldId));
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
