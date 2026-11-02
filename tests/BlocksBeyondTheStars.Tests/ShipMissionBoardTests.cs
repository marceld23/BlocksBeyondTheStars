// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3087 — the mission board built into the player's own ship (Justus 2026-11-01: "I can't interact with the mission board in
/// the spaceship", Marcel: the ship board should work). It needs no quartermaster: at the board the player takes the ship's
/// cargo jobs and hands them in; away from it the server refuses with the ship's own lines. #3090: the stand-alone board
/// jobs offered at "any" board are taken there too. A trading post built into a ship stays decoration and says so.
/// </summary>
public sealed class ShipMissionBoardTests : IDisposable
{
    private const string Pilot = "Builder";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_shipboard_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort temp cleanup */ }
    }

    private sealed class ShipWorld : IDisposable
    {
        public required SvGameServer Server { get; init; }
        public required SqliteWorldRepository Repo { get; init; }
        public required RecordingTransport Transport { get; init; }
        public required BlocksBeyondTheStars.GameServer.PlayerSession Player { get; init; }
        public Vector3i Origin { get; init; }

        public void Dispose() => Repo.Dispose();
    }

    private ShipWorld StartAboard(string world)
    {
        var transport = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        server.AddLocalPlayer("Host");
        var player = server.AddLocalPlayer(Pilot);
        player.State.InstantBuild = true; // no materials needed for the test placements
        var (origin, _) = server.LandedShipBoundsForTest(Pilot);
        return new ShipWorld { Server = server, Repo = repo, Transport = transport, Player = player, Origin = origin };
    }

    /// <summary>Builds <paramref name="item"/> into a free cabin cell standing on the deck; returns that cell (structure-local).</summary>
    private Vector3i BuildIntoCabin(ShipWorld w, string item)
    {
        var placed = _content.GetBlock(_content.GetItem(item)!.PlacesBlock!)!.NumericId;
        var s = w.Server.BuildShipStructureForTest(Pilot);
        for (int y = 1; y < s.Height - 1; y++)
        {
            for (int x = 1; x < s.Width - 1; x++)
            {
                for (int z = 1; z < s.Length - 1; z++)
                {
                    var c = new Vector3i(x, y, z);
                    if (!s.Get(c).IsAir || s.Get(new Vector3i(x, y - 1, z)).IsAir || !s.Get(new Vector3i(x, y + 1, z)).IsAir
                        || s.DoorCells.Contains(c))
                    {
                        continue;
                    }

                    StandAt(w, c, 1.5f);
                    w.Server.HandleStructureEditForTest(Pilot, new StructureEditIntent
                    {
                        StructureId = "ship:" + Pilot,
                        X = x,
                        Y = y,
                        Z = z,
                        Mine = false,
                        ItemKey = item,
                    });
                    if (w.Server.BuildShipStructureForTest(Pilot).Get(c) == placed)
                    {
                        return c;
                    }
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"no free cabin cell took a {item}");
    }

    /// <summary>Puts the player <paramref name="offset"/> metres east of a structure-local cell, on its floor.</summary>
    private static void StandAt(ShipWorld w, Vector3i cell, float offset)
        => w.Player.State.Position = new Vector3f(w.Origin.X + cell.X + 0.5f + offset, w.Origin.Y + cell.Y, w.Origin.Z + cell.Z + 0.5f);

    private static IEnumerable<string> Messages(ShipWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<ServerMessage>().Select(m => m.Text);

    private static IEnumerable<string> MissionRefusals(ShipWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<MissionResult>()
            .Where(r => !r.Success).Select(r => r.Reason ?? string.Empty);

    private static MissionProgress? Held(ShipWorld w, string id) => w.Player.State.Missions.FirstOrDefault(m => m.MissionId == id);

    [Fact]
    public void ABoardInTheOwnShip_OffersCargoJobs_TakenAndHandedInAtTheBoard()
    {
        using var w = StartAboard("shipboard_jobs");
        var board = BuildIntoCabin(w, "mission_board");
        Assert.Contains("@srv.ship.board_ready", Messages(w)); // the builder hears that it works

        StandAt(w, board, 1.5f);
        var jobs = w.Server.ShipBoardMissionsForTest(Pilot);
        Assert.Equal(3, jobs.Count);
        Assert.All(jobs, id => Assert.StartsWith("shipboard_", id, StringComparison.Ordinal));
        Assert.Equal("mission.ship.gather.title", w.Server.MissionDefForTest(jobs[0])!.NameKey);
        Assert.Equal(string.Empty, w.Server.MissionGiverName(jobs[0])); // no quartermaster: the ship's own terminal

        // Away from the board (off the ship): refused with the ship's line, nothing taken.
        StandAt(w, board, 40f);
        w.Server.AcceptMission(Pilot, jobs[0]);
        Assert.Null(Held(w, jobs[0]));
        Assert.Contains("@srv.mission.board_ship", MissionRefusals(w));
        Assert.Empty(w.Server.ShipBoardMissionsForTest(Pilot)); // nor listed out there

        StandAt(w, board, 1.5f);
        w.Server.AcceptMission(Pilot, jobs[0]);
        Assert.Equal(MissionStatus.Active, Held(w, jobs[0])?.Status);
        Assert.Empty(w.Player.State.NpcMemory.Where(kv => kv.Key.StartsWith("shipboard_", StringComparison.Ordinal))); // nobody to remember it

        var need = w.Server.FirstObjectiveForTest(jobs[0])!;
        w.Player.State.Inventory.Add(need.Target, need.Required, _content.MaxStackOf(need.Target));

        StandAt(w, board, 40f);
        w.Server.TurnInMission(Pilot, jobs[0]);
        Assert.Equal(MissionStatus.Active, Held(w, jobs[0])?.Status);
        Assert.Contains("@srv.mission.return_ship", MissionRefusals(w));

        StandAt(w, board, 1.5f);
        w.Server.TurnInMission(Pilot, jobs[0]);
        Assert.Equal(MissionStatus.TurnedIn, Held(w, jobs[0])?.Status);
    }

    [Fact]
    public void TheStandAloneBoardJob_IsTakenAtTheShipBoard_NotInTheWild()
    {
        using var w = StartAboard("shipboard_standalone");
        var board = BuildIntoCabin(w, "mission_board");

        StandAt(w, board, 40f);
        Assert.DoesNotContain("supply_cables", w.Server.OfferedMissionIdsForTest(Pilot));
        w.Server.AcceptMission(Pilot, "supply_cables");
        Assert.Null(Held(w, "supply_cables"));

        StandAt(w, board, 1.5f);
        Assert.Contains("supply_cables", w.Server.OfferedMissionIdsForTest(Pilot)); // #3090: the ship's board is a board
        w.Server.AcceptMission(Pilot, "supply_cables");
        Assert.NotNull(Held(w, "supply_cables"));
    }

    [Fact]
    public void ATradingPostInAShip_IsDecoration_AndSaysSo()
    {
        using var w = StartAboard("shipboard_vendor");
        BuildIntoCabin(w, "station_vendor");
        Assert.Contains("@srv.ship.post_decor", Messages(w));
        Assert.DoesNotContain("@srv.ship.board_ready", Messages(w));
    }

    [Fact]
    public void AHeldCargoJob_IsCoinedBack_AfterARestart()
    {
        string job;
        MissionObjective need;
        using (var w = StartAboard("shipboard_restart"))
        {
            var board = BuildIntoCabin(w, "mission_board");
            StandAt(w, board, 1.5f);
            job = w.Server.ShipBoardMissionsForTest(Pilot)[0];
            w.Server.AcceptMission(Pilot, job);
            need = w.Server.FirstObjectiveForTest(job)!;
            w.Server.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "shipboard_restart"));
        var config = new ServerConfig { WorldName = "shipboard_restart", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true };
        var server = new SvGameServer(config, _content, new RecordingTransport(), repo);
        server.Start();
        server.AddLocalPlayer("Host");
        var player = server.AddLocalPlayer(Pilot); // the world entry coins the held board jobs back, wherever the player stands

        Assert.Contains(player.State.Missions, m => m.MissionId == job && m.Status == MissionStatus.Active);
        var again = server.FirstObjectiveForTest(job);
        Assert.NotNull(again); // the job still counts and can be handed in
        Assert.Equal((need.Target, need.Required), (again!.Target, again.Required)); // the same roll
        server.Stop();
    }
}
