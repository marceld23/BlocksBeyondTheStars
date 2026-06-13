using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Planet bases (the "Grundstein") + the space-station travel-screen features: founding/renaming/persisting a
/// surface base, renaming a commissioned station, the menu "board a visited station" gate, and that the
/// landed-body discovery history now survives a reload (which the visited gate relies on).
/// </summary>
public sealed class PlanetBaseAndStationMapTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PlanetBaseAndStationMapTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_basemap_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo, string name = "basemap")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static PlayerSession Builder(SvGameServer server, string name = "Builder")
    {
        var p = server.AddLocalPlayer(name);
        p.State.Position = new Vector3f(0, 200, 0); // up in the air → the target cell is empty + within reach
        p.State.Inventory.Add("base_core", 4, 16);
        return p;
    }

    private static bool Landable(SvGameServer server, string bodyId)
    {
        var b = server.Galaxy.FindBody(bodyId);
        return b is not null && (b.Kind == CelestialKind.Planet || b.Kind == CelestialKind.Moon || b.Kind == CelestialKind.AsteroidField);
    }

    /// <summary>Deploys + commissions a minimal player station (core + 11-wall hull + an airlock). Returns its id.
    /// The player is left on an EVA in space next to the station.</summary>
    private static string CommissionStation(SvGameServer server, PlayerSession pilot)
    {
        string playerId = pilot.State.PlayerId;
        server.EnterSpace(playerId);
        pilot.State.InEva = true;
        pilot.State.InstantBuild = true; // free build for the test

        server.DeployStationCoreForTest(playerId);
        string id = server.OwnedStationIdForTest(playerId)!;

        for (int i = 1; i <= 11; i++)
        {
            server.HandleStructureEditForTest(playerId,
                new StructureEditIntent { StructureId = id, X = i, Y = 0, Z = 0, Mine = false, ItemKey = "iron_wall" });
        }

        server.HandleStructureEditForTest(playerId,
            new StructureEditIntent { StructureId = id, X = 0, Y = 1, Z = 0, Mine = false, ItemKey = "door_slide" });

        Assert.True(server.StationIsBoardableForTest(id));
        return id;
    }

    // ---------------- Content ----------------

    [Fact]
    public void BaseCore_Block_Item_Recipe_AllLoadAndResolve()
    {
        Assert.NotNull(_content.GetBlock("base_core"));
        var item = _content.GetItem("base_core");
        Assert.NotNull(item);
        Assert.Equal("base_core", item!.PlacesBlock);
        var recipe = _content.GetRecipe("base_core");
        Assert.NotNull(recipe);
        Assert.Null(recipe!.RequiredBlueprint); // craftable from the start (Q4)
        foreach (var ia in recipe.Inputs.Concat(recipe.Outputs))
        {
            Assert.NotNull(_content.GetItem(ia.Item));
        }
    }

    // ---------------- Planet bases ----------------

    [Fact]
    public void PlaceBaseCore_FoundsANamedBase_OwnedByThePlayer()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server);
            Assert.True(Landable(server, server.ActiveLocationId)); // the home body is a surface

            server.PlaceBlock("Builder", 1, 200, 0, "base_core");

            Assert.False(server.World.GetBlock(new Vector3i(1, 200, 0)).IsAir); // a real solid block
            var b = server.BaseSnapshots.Single();
            Assert.Equal("Builder", b.OwnerId);
            Assert.Equal(server.ActiveLocationId, b.Body);
            Assert.Contains("Builder", b.Name);                       // default "Builder's Base"
            Assert.Equal(3, p.State.Inventory.CountOf("base_core"));  // one was consumed
            Assert.Single(repo.ListAllBases());                       // persisted by its cell
        }
    }

    [Fact]
    public void MiningTheBaseCore_RemovesTheBase_AndReturnsTheItem()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server);
            server.PlaceBlock("Builder", 1, 200, 0, "base_core");
            Assert.Single(server.BaseSnapshots);

            server.MineBlock("Builder", 1, 200, 0);

            Assert.Empty(server.BaseSnapshots);
            Assert.True(server.World.GetBlock(new Vector3i(1, 200, 0)).IsAir);
            Assert.Equal(4, p.State.Inventory.CountOf("base_core")); // 3 left + 1 returned
            Assert.Empty(repo.ListAllBases());
        }
    }

    [Fact]
    public void OnlyOneBasePerBody_SecondFoundingIsRefused()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Builder(server);
            server.PlaceBlock("Builder", 1, 200, 0, "base_core");
            Assert.Single(server.BaseSnapshots);

            // A second base core on the SAME body is refused (one base per body per player).
            server.PlaceBlock("Builder", 3, 200, 0, "base_core");
            Assert.Single(server.BaseSnapshots);
            Assert.True(server.World.GetBlock(new Vector3i(3, 200, 0)).IsAir); // nothing placed
        }
    }

    [Fact]
    public void OnlyTheOwnerCanRenameABase_AndTheNamePersists()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var owner = Builder(server);
            var other = server.AddLocalPlayer("Other");
            string body = server.ActiveLocationId;

            server.PlaceBlock("Builder", 1, 200, 0, "base_core");

            server.SetBaseNameForTest(other, body, "Hijacked"); // not the owner → no base of theirs here → refused
            Assert.Contains("Builder", server.BaseSnapshots.Single().Name);

            server.SetBaseNameForTest(owner, body, "Fort Iron");
            Assert.Equal("Fort Iron", server.BaseSnapshots.Single().Name);
            Assert.Equal("Fort Iron", repo.ListAllBases().Single().Name); // persisted
        }
    }

    [Fact]
    public void Base_AndItsName_SurviveAReload()
    {
        string body;
        var server = NewServer(out var repo1, "base_persist");
        using (repo1)
        {
            var owner = Builder(server);
            body = server.ActiveLocationId;
            server.PlaceBlock("Builder", 1, 200, 0, "base_core");
            server.SetBaseNameForTest(owner, body, "Home One");
            Assert.Equal("Home One", server.BaseSnapshots.Single().Name);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var server2 = NewServer(out var repo2, "base_persist");
        using (repo2)
        {
            var b = server2.BaseSnapshots.Single();
            Assert.Equal("Home One", b.Name);
            Assert.Equal("Builder", b.OwnerId);
            Assert.Equal(body, b.Body);
        }
    }

    // ---------------- Station rename ----------------

    [Fact]
    public void OnlyTheOwnerCanRenameTheirStation_AndTheNamePersists()
    {
        var server = NewServer(out var repo, "station_rename");
        using (repo)
        {
            var owner = server.AddLocalPlayer("Owner");
            var other = server.AddLocalPlayer("Other");
            string id = CommissionStation(server, owner);

            server.SetStationNameForTest(other, id, "Hijacked"); // not the owner → refused
            Assert.NotEqual("Hijacked", server.Galaxy.FindBody(id)!.Name);

            server.SetStationNameForTest(owner, id, "Fort Alpha");
            Assert.Equal("Fort Alpha", server.Galaxy.FindBody(id)!.Name);           // the star-map entry
            Assert.Equal("Fort Alpha", repo.ListSpaceStructures().Single(s => s.Id == id).Name); // persisted
        }
    }

    // ---------------- Menu "board a visited station" gate (Q1) ----------------

    [Fact]
    public void TravelToStationFromMenu_IsGatedByHavingVisitedIt()
    {
        var server = NewServer(out var repo, "station_travel");
        using (repo)
        {
            var owner = server.AddLocalPlayer("Owner");
            string id = CommissionStation(server, owner);

            // Never boarded → the travel-screen quick-board is refused (Instant Travel off).
            server.QuickTravelForTest("Owner", id);
            Assert.False(server.InStation("Owner"));

            // Board it directly once (stands in for flying there + docking) → marks it visited.
            server.Travel("Owner", id);
            Assert.True(server.InStation("Owner"));
            server.LeaveStation("Owner");
            Assert.False(server.InStation("Owner"));

            // Now visited → the travel-screen quick-board boards it.
            server.QuickTravelForTest("Owner", id);
            Assert.True(server.InStation("Owner"));
        }
    }

    // ---------------- Discovery persistence (the gate relies on it) ----------------

    [Fact]
    public void LandedBodyHistory_SurvivesAReload()
    {
        var config = new ServerConfig { WorldName = "landed", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;

        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "landed"));
        using (repo)
        {
            string firstHopId;
            var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
            server.Start();
            var session = server.AddLocalPlayer("Pilot");
            server.Ship.Modules.Add("jump_generator");

            var others = server.Galaxy.AllBodies().Where(b =>
                b.Kind == CelestialKind.Planet && !string.IsNullOrEmpty(b.PlanetType)
                && _content.GetPlanet(b.PlanetType!) is not null
                && b.Id != server.ActiveLocationId).Take(2).ToList();
            Assert.True(others.Count >= 2, "seed needs at least two other planets for this test");

            server.Travel("Pilot", others[0].Id); // visit A
            firstHopId = others[0].Id;
            server.Travel("Pilot", others[1].Id); // then B (now standing on B)
            Assert.Contains(firstHopId, session.State.LandedBodies);
            repo.SavePlayer(session.State);

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // Reload: the player returns to B; A is still in the landed history only if it was persisted
            // (on reload only the current body is auto-marked), so this proves the discovery set was saved.
            var server2 = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
            server2.Start();
            var session2 = server2.AddLocalPlayer("Pilot");
            Assert.Contains(firstHopId, session2.State.LandedBodies);
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
