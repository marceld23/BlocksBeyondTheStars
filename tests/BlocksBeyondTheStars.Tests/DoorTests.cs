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
/// Doors: a settlement's doorways get real doors — sci-fi sliders for towns/cities, hinged doors for
/// villages/hamlets. Slide doors are server-automatic (open when a player is near, auto-close after a
/// short delay); hinge doors are manual (toggle on E). State is server-authoritative; the client renders
/// the panels + a collider that blocks passage while closed.
/// </summary>
public sealed class DoorTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public DoorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_door_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "door_" + seed));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "door_" + seed, Seed = seed, StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false,
            PlaceSettlements = true, PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Finds an inhabited settlement that has at least one door of the given kind ("slide"/"hinge").</summary>
    private SvGameServer StartedWithDoor(string kind, out SqliteWorldRepository repo,
        out (int Id, string Kind, Vector3f Pos, bool Open) door)
    {
        for (long seed = 1; seed <= 140; seed++)
        {
            var server = Start(seed, out repo);
            var hit = server.DoorSnapshots.FirstOrDefault(d => d.Kind == kind);
            if (hit.Id != 0)
            {
                door = hit;
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException($"No settlement with a '{kind}' door found across 140 seeds.");
    }

    private static bool IsOpen(SvGameServer server, int doorId)
        => server.DoorSnapshots.First(d => d.Id == doorId).Open;

    [Fact]
    public void InhabitedSettlement_RegistersDoors_AtItsDoorways()
    {
        var server = StartedWithDoor("hinge", out var repo, out _);
        using (repo)
        {
            Assert.True(server.DoorCount > 0);

            foreach (var d in server.DoorSnapshots)
            {
                Assert.Contains(d.Kind, new[] { "slide", "hinge" });
                Assert.False(d.Open);               // doors start closed
                Assert.Contains(server.SettlementMarkers, // each door sits at a real door marker
                    m => (m.Type == "door_slide" || m.Type == "door_hinge")
                         && System.Math.Abs(m.Pos.X - d.Pos.X) < 2f
                         && System.Math.Abs(m.Pos.Z - d.Pos.Z) < 2f);
            }
        }
    }

    [Fact]
    public void SlideDoor_AutoOpens_WhenPlayerApproaches_AndClosesAfterTheyLeave()
    {
        var server = StartedWithDoor("slide", out var repo, out var door);
        using (repo)
        {
            var p = server.AddLocalPlayer("Visitor");

            // Standing well clear: the door stays shut.
            p.State.Position = new Vector3f(door.Pos.X + 50f, door.Pos.Y, door.Pos.Z + 50f);
            server.TickForTest(0.5);
            Assert.False(IsOpen(server, door.Id));

            // Step into the doorway: it slides open.
            p.State.Position = door.Pos;
            server.TickForTest(0.2);
            Assert.True(IsOpen(server, door.Id));

            // Walk away: it auto-closes once the delay elapses.
            p.State.Position = new Vector3f(door.Pos.X + 50f, door.Pos.Y, door.Pos.Z + 50f);
            for (int i = 0; i < 6; i++)
            {
                server.TickForTest(0.5); // > the auto-close delay
            }

            Assert.False(IsOpen(server, door.Id));
        }
    }

    [Fact]
    public void HingeDoor_Toggles_OnInteract_ButNotFromAfar()
    {
        var server = StartedWithDoor("hinge", out var repo, out var door);
        using (repo)
        {
            var p = server.AddLocalPlayer("Visitor");
            p.State.Position = door.Pos;

            Assert.False(IsOpen(server, door.Id));   // starts closed
            server.InteractDoorForTest(p, door.Id);
            Assert.True(IsOpen(server, door.Id));    // E opens it
            server.InteractDoorForTest(p, door.Id);
            Assert.False(IsOpen(server, door.Id));   // E again shuts it

            // Out of reach, the latch can't be touched.
            p.State.Position = new Vector3f(door.Pos.X + 50f, door.Pos.Y, door.Pos.Z + 50f);
            server.InteractDoorForTest(p, door.Id);
            Assert.False(IsOpen(server, door.Id));
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
