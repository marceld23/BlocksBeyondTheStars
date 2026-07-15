// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The browser singleplayer's persistence path: the REAL server + REAL client run on the fully
/// managed <see cref="MemoryWorldRepository"/> (no SQLite — the WebGL host can't load native code),
/// and the whole world round-trips through the gzip'd JSON snapshot blob that becomes the
/// IndexedDB/Glitch-cloud save payload.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class BrowserSingleplayerPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_sp_" + Guid.NewGuid().ToString("N"));

    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private MemoryWorldRepository NewRepo(string world = "browser_sp")
        => new(new SaveGamePaths(_root, world));

    [Fact]
    public void FullStack_MineOnMemoryRepo_BlobReload_RestoresEditAndInventory()
    {
        var content = LoadContent();
        byte[] blob;
        Vector3i target = default;
        string? dropItem = null;

        // Session 1: play on the memory repo — join, mine one block, stop (= synchronous SaveAll).
        var repoA = NewRepo();
        using (var h = new ClientServerHarness(content, repository: repoA))
        {
            h.Join("Solo");
            Assert.True(h.PumpUntil(() => h.Chunks.Count > 0, maxTicks: 60), "Chunks should stream after join.");

            var session = h.Server.Sessions[1];
            int px = (int)Math.Floor(session.State.Position.X);
            int pz = (int)Math.Floor(session.State.Position.Z);
            int topY = (int)Math.Ceiling(session.State.Position.Y);
            for (int y = topY; y > topY - 12; y--)
            {
                var pos = new Vector3i(px, y, pz);
                var b = h.Server.World.GetBlock(pos);
                if (!b.IsAir && h.Server.World.Definition(b) is { } def && def.Drops.Count > 0)
                {
                    target = pos;
                    dropItem = def.Drops[0].Item;
                    break;
                }
            }

            Assert.NotNull(dropItem);
            for (int hit = 0; hit < 15 && !h.Server.World.GetBlock(target).IsAir; hit++)
            {
                h.Client.SendMine(target.X, target.Y, target.Z);
                h.Tick(0.1);
            }

            Assert.True(h.Server.World.GetBlock(target).IsAir, "Server should have removed the mined block.");
            h.Server.Stop(); // no run loop → saves synchronously into the memory repo

            blob = repoA.ExportSnapshotBlob();
            Assert.True(blob.Length > 0);
        }

        // Session 2: fresh repo hydrated from the blob (same seed/config) — the edit and the drop persist.
        var repoB = NewRepo();
        repoB.ImportSnapshotBlob(blob);
        using (var h2 = new ClientServerHarness(content, repository: repoB))
        {
            h2.Join("Solo");
            var tc = WorldConstants.WorldToChunk(target);
            Assert.True(h2.PumpUntil(() => h2.Chunks.ContainsKey((tc.X, tc.Y, tc.Z)), maxTicks: 60),
                "The edited chunk should stream on the reloaded world.");

            Assert.True(h2.Server.World.GetBlock(target).IsAir,
                "The mined block must stay gone after the blob round-trip.");
            Assert.NotNull(h2.LastInventory);
            Assert.Contains(h2.LastInventory!.Personal, s => s.Item == dropItem && s.Count >= 1);
        }
    }

    [Fact]
    public void SnapshotBlob_RoundTripsEveryTable()
    {
        var repo = NewRepo();
        repo.Initialize();

        repo.EnsureBlockPalette(new Dictionary<ushort, string> { [1] = "stone", [2] = "dirt" });
        repo.SaveMetadata(new WorldMetadata { WorldName = "Blob World", Seed = 42 });
        repo.SetBlock("rocky", new Vector3i(10, 20, 30), 2, tint: 3, glow: 4, shape: 5);
        repo.SaveFloraRegrow("rocky", new Vector3i(1, 2, 3), 7, 12.5);
        repo.SavePlayer(StateMapper.FromSnapshot(new PlayerSnapshot { Id = "p1", Name = "Solo", KnowledgePoints = 9 }));
        repo.SaveShip("ship:p1", StateMapper.FromSnapshot(new ShipSnapshot { ShipType = "starter", Hull = 77f }));
        repo.SaveContainer(new StoredContainer { Id = "c1", Planet = "rocky", Position = new Vector3i(4, 5, 6) });
        repo.SaveDoor(new StoredDoor { Planet = "rocky", X = 1, Y = 1, Z = 1, Kind = "slide" });
        repo.SaveBeacon(new StoredBeacon { Planet = "rocky", X = 2, Y = 2, Z = 2, Label = "Home", OwnerId = "p1" });
        repo.SaveBeam(new StoredBeam { Planet = "rocky", X = 3, Y = 3, Z = 3, Name = "Pad", OwnerId = "p1" });
        repo.SaveBase(new StoredBase { Planet = "rocky", X = 4, Y = 4, Z = 4, Name = "Base", OwnerId = "p1" });
        repo.SaveAlliance(new StoredAlliance { PlayerA = "a", PlayerB = "b", FormedUtc = "2026-07-15T00:00:00Z" });
        repo.SaveStoryState(new StoredStoryState { StoryId = "s1", FragmentsFound = 3 });
        repo.SaveSpaceStructure(new StoredSpaceStructure { Id = "st1", OwnerId = "p1", Name = "Station", Location = "sys0-p1", Blocks = "0:0:0:2" });
        repo.SetStructureBlock("ship:p1", new Vector3i(9, 9, 9), 2);
        repo.SetLocationStatus("sys0-p1", "generated");

        var fresh = NewRepo("blob_copy");
        fresh.ImportSnapshotBlob(repo.ExportSnapshotBlob());

        Assert.Equal("Blob World", fresh.LoadMetadata()!.WorldName);
        var edit = Assert.Single(fresh.LoadChunkEdits("rocky", WorldConstants.WorldToChunk(new Vector3i(10, 20, 30))));
        Assert.Equal(2, edit.Block);
        Assert.Equal(3, edit.Tint);
        Assert.Equal(4, edit.Glow);
        Assert.Equal(5, edit.Shape);
        Assert.Equal(12.5, Assert.Single(fresh.ListFloraRegrow("rocky")).Timer);
        Assert.Equal(9, fresh.LoadPlayer("p1")!.KnowledgePoints);
        Assert.Equal(new[] { "p1" }, fresh.ListPlayerIds());
        Assert.Equal(77f, fresh.LoadShip("ship:p1")!.Hull);
        Assert.Equal("c1", Assert.Single(fresh.ListContainers("rocky")).Id);
        Assert.Equal("slide", Assert.Single(fresh.ListDoors("rocky")).Kind);
        Assert.Equal("Home", Assert.Single(fresh.ListBeacons("rocky")).Label);
        Assert.Equal("Pad", Assert.Single(fresh.ListBeams("rocky")).Name);
        Assert.Equal("Base", Assert.Single(fresh.ListAllBases()).Name);
        Assert.Equal("a", Assert.Single(fresh.ListAlliances()).PlayerA);
        Assert.Equal(3, Assert.Single(fresh.ListStoryStates()).FragmentsFound);
        Assert.Equal("Station", Assert.Single(fresh.ListSpaceStructures()).Name);
        Assert.Equal(2, Assert.Single(fresh.LoadStructureEdits("ship:p1")).Block);
        Assert.Equal("generated", fresh.LoadLocationStatuses()["sys0-p1"]);
    }

    [Fact]
    public void EnsureBlockPalette_RemapsStoredIds_WhenContentShifts()
    {
        var repo = NewRepo();
        repo.EnsureBlockPalette(new Dictionary<ushort, string> { [1] = "stone", [2] = "dirt" });
        repo.SetBlock("rocky", new Vector3i(0, 0, 0), 2); // dirt under the old assignment

        // Content update swapped the ids: dirt is now 1, stone is now 2.
        repo.EnsureBlockPalette(new Dictionary<ushort, string> { [1] = "dirt", [2] = "stone" });

        var edit = Assert.Single(repo.LoadChunkEdits("rocky", WorldConstants.WorldToChunk(new Vector3i(0, 0, 0))));
        Assert.Equal(1, edit.Block); // still dirt, under its NEW id
    }

    [Fact]
    public void Flush_RaisesFlushed_SoTheHostCanPersistTheBlob()
    {
        var repo = NewRepo();
        int flushes = 0;
        repo.Flushed += () => flushes++;

        repo.Flush();
        repo.Flush();

        Assert.Equal(2, flushes);
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
            // best effort — temp cleanup only
        }
    }
}
