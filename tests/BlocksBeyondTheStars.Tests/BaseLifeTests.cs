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
}
