// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
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
/// The world notices your base (#1120, stage 2): a settler NPC moves in once a base carries enough
/// machines — known to the owner from day one — and never before. No NPC ever damages a block (the settler
/// only exists; there is no block-touching code path at all).
/// </summary>
public sealed class BaseLifeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BaseLifeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_baselife_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Start(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "baselife"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = "baselife",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        }, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void ASettlerMovesIn_OnceTheBaseHasMachines_AndIsKnownToTheOwner()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Homesteader");
            var feet = owner.State.Position;
            var core = new Vector3i((int)Math.Floor(feet.X) + 3, (int)Math.Floor(feet.Y) + 4, (int)Math.Floor(feet.Z));
            server.PlaceBaseForTest(owner, core);
            int baseId = server.BaseSnapshots.Single(b => b.OwnerId == owner.State.PlayerId).Id;

            // A bare claim attracts nobody.
            server.ScanBaseLifeForTest();
            Assert.Null(server.BaseSettlerForTest(baseId));
            Assert.DoesNotContain(server.NpcSnapshots, n => n.Role == "settler");

            // Three machines make it a home — the settler moves in, known to the owner from day one.
            var workbench = _content.GetBlock("workbench")!.NumericId;
            server.World.SetBlock(new Vector3i(core.X + 1, core.Y, core.Z), workbench, 0, 0, 0, "Homesteader");
            server.World.SetBlock(new Vector3i(core.X + 2, core.Y, core.Z), workbench, 0, 0, 0, "Homesteader");
            server.World.SetBlock(new Vector3i(core.X + 1, core.Y, core.Z + 1), _content.GetBlock("forge")!.NumericId, 0, 0, 0, "Homesteader");

            server.ScanBaseLifeForTest();
            int? settlerId = server.BaseSettlerForTest(baseId);
            Assert.NotNull(settlerId);
            Assert.Contains(server.NpcSnapshots, n => n.Id == settlerId!.Value && n.Role == "settler");
            Assert.Contains(owner.State.NpcMemory.Values, r => r.Role == "settler" && r.Value >= 10);

            // Running the scan again never doubles the settler.
            server.ScanBaseLifeForTest();
            Assert.Equal(1, server.NpcSnapshots.Count(n => n.Role == "settler"));
        }
    }

    /// <summary>A second landable planet in the SAME system (quick travel without a jump generator).</summary>
    private Shared.World.CelestialBody OtherPlanet(SvGameServer server)
    {
        string sys = server.Galaxy.FindBody(server.ActiveLocationId)!.SystemId;
        return server.Galaxy.AllBodies().First(b =>
            b.Kind is Shared.World.CelestialKind.Planet or Shared.World.CelestialKind.Moon or Shared.World.CelestialKind.AsteroidField
            && b.SystemId == sys
            && !string.IsNullOrEmpty(b.PlanetType)
            && _content.GetPlanet(b.PlanetType!) is not null
            && b.Id != server.ActiveLocationId);
    }

    /// <summary>Places a base with enough machines that the scan moves a settler in, returns its id.</summary>
    private int FoundHomeWithSettler(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession owner, out Vector3i core)
    {
        var feet = owner.State.Position;
        core = new Vector3i((int)Math.Floor(feet.X) + 3, (int)Math.Floor(feet.Y) + 4, (int)Math.Floor(feet.Z));
        server.PlaceBaseForTest(owner, core);
        int baseId = server.BaseSnapshots.Single(b => b.OwnerId == owner.State.PlayerId).Id;
        var workbench = _content.GetBlock("workbench")!.NumericId;
        server.World.SetBlock(new Vector3i(core.X + 1, core.Y, core.Z), workbench, 0, 0, 0, owner.State.Name);
        server.World.SetBlock(new Vector3i(core.X + 2, core.Y, core.Z), workbench, 0, 0, 0, owner.State.Name);
        server.World.SetBlock(new Vector3i(core.X + 1, core.Y, core.Z + 1), workbench, 0, 0, 0, owner.State.Name);
        server.ScanBaseLifeForTest();
        Assert.NotNull(server.BaseSettlerForTest(baseId));
        return baseId;
    }

    [Fact]
    public void RenamingTheBase_KeepsOneSettler_AndOneRosterEntry()
    {
        // The settler was keyed by a hash of the base NAME: after a rename the scan saw "no settler" and
        // spawned another under the new hash — one extra NPC + roster entry per rename (#1262).
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Renamer");
            int baseId = FoundHomeWithSettler(server, owner, out _);
            string settlerName = owner.State.NpcMemory.Values.Single(r => r.Role == "settler").Name;

            server.RenameBaseForTest(owner, "Erste Heimat");
            server.ScanBaseLifeForTest();
            server.RenameBaseForTest(owner, "Zweite Heimat");
            server.ScanBaseLifeForTest();
            server.ScanBaseLifeForTest();

            Assert.Equal(1, server.NpcSnapshots.Count(n => n.Role == "settler"));
            Assert.NotNull(server.BaseSettlerForTest(baseId));
            var entries = owner.State.NpcMemory.Where(kv => kv.Value.Role == "settler").ToList();
            Assert.Single(entries);
            Assert.Equal(settlerName, entries[0].Value.Name);
            Assert.Equal("Zweite Heimat", entries[0].Value.Place); // the roster follows the new name
            Assert.StartsWith("base_", entries[0].Key); // keyed by base id, not by the name hash
        }
    }

    [Fact]
    public void PreExistingRenameDuplicates_CollapseIntoOneEntry()
    {
        // A save from before #1262 carries one name-keyed settler entry per rename, all with the same coined
        // name. The join-time migration moves the current one onto the base-id key and drops the rest.
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Legacy");
            int baseId = FoundHomeWithSettler(server, owner, out _);
            var live = owner.State.NpcMemory.Single(kv => kv.Value.Role == "settler");
            string baseName = server.BaseSnapshots.Single(b => b.Id == baseId).Name;

            // Rebuild the pre-fix state: the entry keyed by the current name plus two stale renames.
            owner.State.NpcMemory.Remove(live.Key);
            owner.State.NpcMemory["settle_11111:settler"] = new Shared.State.NpcRelationship { Name = live.Value.Name, Role = "settler", Place = "Alt 1", Value = 10 };
            owner.State.NpcMemory["settle_22222:settler"] = new Shared.State.NpcRelationship { Name = live.Value.Name, Role = "settler", Place = "Alt 2", Value = 12 };
            owner.State.NpcMemory[$"settle_{(uint)BlocksBeyondTheStars.WorldGeneration.WorldGenerator.StableHash(baseName) % 100000u}:settler"] =
                new Shared.State.NpcRelationship { Name = live.Value.Name, Role = "settler", Place = baseName, Value = 15 };

            server.MigrateBaseSettlerMemoryForTest(owner);

            var entries = owner.State.NpcMemory.Where(kv => kv.Value.Role == "settler").ToList();
            Assert.Single(entries);
            Assert.Equal(live.Key, entries[0].Key);
            Assert.Equal(15, entries[0].Value.Value); // the current entry carried over, standing intact
            Assert.Equal(baseName, entries[0].Value.Place);
        }
    }

    [Fact]
    public void TheSettler_ComesBackAfterAWorldRoundTrip()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Wanderer");
            int baseId = FoundHomeWithSettler(server, owner, out _);

            // Travelling away clears the world's NPC list; coming home must respawn the settler — a stale
            // id mapping must not block the scan (#1152).
            server.SetInstantTravelForTest(true);
            string home = owner.CurrentLocationId;
            Assert.True(server.QuickTravelForTest("Wanderer", OtherPlanet(server).Id));
            Assert.True(server.QuickTravelForTest("Wanderer", home));

            server.ScanBaseLifeForTest();
            int? settlerId = server.BaseSettlerForTest(baseId);
            Assert.NotNull(settlerId);
            Assert.Contains(server.NpcSnapshots, n => n.Id == settlerId!.Value && n.Role == "settler");
            Assert.Equal(1, server.NpcSnapshots.Count(n => n.Role == "settler"));
        }
    }

    [Fact]
    public void DissolvingABase_WhileAnotherWorldIsActive_NeverTouchesThatWorldsNpcs()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Mover");
            int baseId = FoundHomeWithSettler(server, owner, out var core);
            string home = owner.CurrentLocationId;

            // Dissolve the base (mine its core — a drill-tier block) and switch the active world BEFORE
            // the next sweep runs — the exact ordering that used to delete a same-numbered NPC on the
            // OTHER world (#1152: NPC ids restart at 1 per world).
            owner.State.AboardShip = false;
            owner.State.Inventory.SetSlot(0, new Shared.State.ItemStack("basic_drill", 1));
            owner.State.Position = new Vector3f(core.X + 1.2f, core.Y + 0.5f, core.Z + 0.5f); // within mining reach
            server.World.SetBlock(core, _content.GetBlock("base_core")!.NumericId, 0, 0, 0, "Mover"); // the seam founds only the entity
            server.MineBlock("Mover", core.X, core.Y, core.Z);
            Assert.DoesNotContain(server.BaseSnapshots, b => b.Id == baseId);
            server.SetInstantTravelForTest(true);
            Assert.True(server.QuickTravelForTest("Mover", OtherPlanet(server).Id));

            var npcsBefore = server.NpcSnapshots.Select(n => n.Id).OrderBy(i => i).ToList();
            server.ScanBaseLifeForTest();
            Assert.Equal(npcsBefore, server.NpcSnapshots.Select(n => n.Id).OrderBy(i => i).ToList());
            Assert.NotNull(server.BaseSettlerForTest(baseId)); // deferred, not deleted blindly

            // Back home, the sweep cleans up on the settler's own world.
            Assert.True(server.QuickTravelForTest("Mover", home));
            server.ScanBaseLifeForTest();
            Assert.Null(server.BaseSettlerForTest(baseId));
            Assert.DoesNotContain(server.NpcSnapshots, n => n.Role == "settler");
        }
    }

    // ---------------- #1248: the settler never stands inside a wall ----------------

    /// <summary>Feet cell has air at feet + head and a floor below — the settler can actually stand there.</summary>
    private static void AssertStandable(SvGameServer server, Vector3f feet)
    {
        var c = new Vector3i((int)Math.Floor(feet.X), (int)Math.Floor(feet.Y), (int)Math.Floor(feet.Z));
        Assert.True(server.World.GetBlock(c).IsAir, $"feet cell {c} is not air");
        Assert.True(server.World.GetBlock(new Vector3i(c.X, c.Y + 1, c.Z)).IsAir, $"head cell above {c} is not air");
        Assert.False(server.World.GetBlock(new Vector3i(c.X, c.Y - 1, c.Z)).IsAir, $"no floor under {c}");
    }

    [Fact]
    public void TheSettler_NeverMovesIntoAWall_AndMovesOutWhenBuiltOver()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Homesteader");
            var feet = owner.State.Position;
            // A base on the ground, the way players build them: core at feet level, machines beside it.
            var core = new Vector3i((int)Math.Floor(feet.X) + 3, (int)Math.Floor(feet.Y), (int)Math.Floor(feet.Z));
            server.PlaceBaseForTest(owner, core);
            int baseId = server.BaseSnapshots.Single(b => b.OwnerId == owner.State.PlayerId).Id;
            var workbench = _content.GetBlock("workbench")!.NumericId;
            server.World.SetBlock(new Vector3i(core.X + 1, core.Y, core.Z), workbench, 0, 0, 0, "Homesteader");
            server.World.SetBlock(new Vector3i(core.X - 1, core.Y, core.Z), workbench, 0, 0, 0, "Homesteader");
            server.World.SetBlock(new Vector3i(core.X, core.Y, core.Z - 1), workbench, 0, 0, 0, "Homesteader");

            // Lyxette's case: the owner built exactly where the settler used to be dropped (core + (2, *, 2)).
            var wall = _content.GetBlock("iron_wall")!.NumericId;
            for (int y = -1; y <= 3; y++)
            {
                server.World.SetBlock(new Vector3i(core.X + 2, core.Y + y, core.Z + 2), wall, 0, 0, 0, "Homesteader");
            }

            server.ScanBaseLifeForTest();
            int settlerId = server.BaseSettlerForTest(baseId)!.Value;
            var settler = server.NpcSnapshots.Single(n => n.Id == settlerId && n.Role == "settler");
            AssertStandable(server, settler.Home);
            Assert.NotEqual(new Vector3f(core.X + 2.5f, core.Y + 1f, core.Z + 2.5f), settler.Home);

            // Building over the home AFTER the settler moved in re-homes them on the next scan instead of
            // leaving the leash walking them into the new wall.
            var h = new Vector3i((int)Math.Floor(settler.Home.X), (int)Math.Floor(settler.Home.Y), (int)Math.Floor(settler.Home.Z));
            server.World.SetBlock(h, wall, 0, 0, 0, "Homesteader");
            server.World.SetBlock(new Vector3i(h.X, h.Y + 1, h.Z), wall, 0, 0, 0, "Homesteader");

            server.ScanBaseLifeForTest();
            var moved = server.NpcSnapshots.Single(n => n.Id == settlerId && n.Role == "settler");
            Assert.NotEqual(settler.Home, moved.Home);
            AssertStandable(server, moved.Home);
            Assert.Equal(1, server.NpcSnapshots.Count(n => n.Role == "settler"));
        }
    }
}
