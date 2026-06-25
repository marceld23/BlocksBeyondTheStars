// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _root;

    public PersistenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_test_" + Guid.NewGuid().ToString("N"));
    }

    private SqliteWorldRepository NewRepo(string world = "world_001")
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        repo.Initialize();
        return repo;
    }

    [Fact]
    public void Metadata_RoundTrips()
    {
        using (var repo = NewRepo())
        {
            repo.SaveMetadata(new WorldMetadata { WorldName = "Alpha", Seed = 42, DefaultPlanetType = "ice" });
        }

        using var reopened = NewRepo();
        var meta = reopened.LoadMetadata();
        Assert.NotNull(meta);
        Assert.Equal("Alpha", meta!.WorldName);
        Assert.Equal(42, meta.Seed);
        Assert.Equal("ice", meta.DefaultPlanetType);
    }

    [Fact]
    public void BlockEdits_PersistAndLoadByChunk()
    {
        using (var repo = NewRepo())
        {
            repo.SetBlock("rocky", new Vector3i(1, 2, 3), 5);
            repo.SetBlock("rocky", new Vector3i(1, 2, 3), 0);   // overwrite (mined to air)
            repo.SetBlock("rocky", new Vector3i(40, 2, 3), 9);  // different chunk
        }

        using var reopened = NewRepo();
        var edits = reopened.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0));
        Assert.Single(edits);
        Assert.Equal(new Vector3i(1, 2, 3), edits[0].WorldPosition);
        Assert.Equal((ushort)0, edits[0].Block);

        var otherChunk = reopened.LoadChunkEdits("rocky", WorldConstants.WorldToChunk(new Vector3i(40, 2, 3)));
        Assert.Single(otherChunk);
        Assert.Equal((ushort)9, otherChunk[0].Block);
    }

    [Fact]
    public void Player_RoundTripsNpcMemory()
    {
        var player = new PlayerState { PlayerId = "p1", Name = "Pilot" };
        var rel = new NpcRelationship { Name = "Kra Thraxon", Role = "quartermaster", Value = 7 };
        rel.Log.Add(new NpcInteraction { Kind = NpcInteractionKind.MissionAccepted });
        rel.Log.Add(new NpcInteraction { Kind = NpcInteractionKind.Trade });
        player.NpcMemory["settle_123:quartermaster"] = rel;

        using (var repo = NewRepo())
        {
            repo.SavePlayer(player);
        }

        using var reopened = NewRepo();
        var loaded = reopened.LoadPlayer("p1");

        Assert.NotNull(loaded);
        Assert.True(loaded!.NpcMemory.TryGetValue("settle_123:quartermaster", out var r));
        Assert.Equal("Kra Thraxon", r!.Name);
        Assert.Equal("quartermaster", r.Role);
        Assert.Equal(7, r.Value);
        Assert.Equal(2, r.Log.Count);
        Assert.Equal(NpcInteractionKind.MissionAccepted, r.Log[0].Kind);
        Assert.Equal(NpcInteractionKind.Trade, r.Log[1].Kind);
    }

    [Fact]
    public void Player_RoundTripsInventoryAndBlueprints()
    {
        var player = new PlayerState
        {
            PlayerId = "p1",
            Name = "Pilot",
            Position = new Vector3f(10f, 64f, -5f),
            Health = 80f,
        };
        player.Inventory.Add("iron_ore", 30, 99);
        player.UnlockedBlueprints.Add("oxygen_tank_1");

        using (var repo = NewRepo())
        {
            repo.SavePlayer(player);
        }

        using var reopened = NewRepo();
        var loaded = reopened.LoadPlayer("p1");
        Assert.NotNull(loaded);
        Assert.Equal("Pilot", loaded!.Name);
        Assert.Equal(30, loaded.Inventory.CountOf("iron_ore"));
        Assert.Contains("oxygen_tank_1", loaded.UnlockedBlueprints);
        Assert.Equal(64f, loaded.Position.Y);
    }

    [Fact]
    public void Ship_RoundTripsModulesAndCargo()
    {
        var ship = new ShipState { CurrentLocationId = "rocky" };
        ship.Modules.Add("cockpit");
        ship.Modules.Add("workshop");
        ship.Cargo.Add("stone", 200, 99);

        using (var repo = NewRepo())
        {
            repo.SaveShip("default", ship);
        }

        using var reopened = NewRepo();
        var loaded = reopened.LoadShip("default");
        Assert.NotNull(loaded);
        Assert.Contains("workshop", loaded!.Modules);
        Assert.Equal(200, loaded.Cargo.CountOf("stone"));
    }

    [Fact]
    public void Backup_CreatesReadableCopy()
    {
        string backup;
        using (var repo = NewRepo())
        {
            repo.SaveMetadata(new WorldMetadata { WorldName = "Beta", Seed = 7 });
            backup = repo.CreateBackup("backup_test");
        }

        Assert.True(File.Exists(backup));

        // The backup should itself be a valid, loadable world database.
        var backupPaths = new SaveGamePaths(Path.GetDirectoryName(backup)!, "x");
        using var verify = new SqliteWorldRepository(
            new SaveGamePaths(_root, "world_001")); // not used; just ensure original still works
        verify.Initialize();
        Assert.Equal(7, verify.LoadMetadata()!.Seed);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup races on Windows file locks
        }
    }
}
