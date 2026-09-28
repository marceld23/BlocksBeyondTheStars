// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
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
/// One seat, one sitter (#2122, "I can sit down on a chair that is occupied by a player or an entity!"): a player is
/// refused a seat an NPC or another player sits on, an NPC does not sit into a seated player (it rests beside the chair
/// and sits once he got up), and the spawners never hand one seat to two residents.
/// </summary>
public sealed class SeatOccupancyTests : IDisposable
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_seats_" + Guid.NewGuid().ToString("N"));

    private static ActionRejected? LastRejection(NpcLifeWorld w, BlocksBeyondTheStars.GameServer.PlayerSession who)
        => w.Transport.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().LastOrDefault();

    private static void StandBeside(BlocksBeyondTheStars.GameServer.PlayerSession who, Vector3i seat, int feet)
        => who.State.Position = new Vector3f(seat.X + 1.5f, feet, seat.Z + 0.5f);

    [Fact]
    public void APlayer_IsRefusedTheChairASettlerSitsOn()
    {
        using var w = new NpcLifeWorld(_content, "seat_npc_first");
        int baseId = w.FoundBase();
        var chair = w.Chair(NpcLifeWorld.Cx - 1, NpcLifeWorld.Cz + 5);
        w.Server.ScanBaseLifeForTest();
        var settler = w.Server.BaseResidentsForTest(baseId).Single(r => r.Slot == 0);
        Assert.Equal(chair, settler.Seat);

        w.TickAt(0.72, 60, until: () => w.Server.NpcRoutineForTest(settler.NpcId).Pose == 1);
        Assert.Equal(1, w.Server.NpcRoutineForTest(settler.NpcId).Pose);

        StandBeside(w.Player, chair, w.Feet);
        w.Server.SetSeatedForTest(w.Player.State.PlayerId, true, chair);

        Assert.False(w.Player.State.Seated);
        Assert.Null(w.Player.State.SeatCell);
        var rejected = LastRejection(w, w.Player);
        Assert.NotNull(rejected);
        Assert.Equal("seat", rejected!.Action);
        Assert.Equal("@srv.seat.taken", rejected.Reason);
        Assert.Equal(1, w.Server.NpcRoutineForTest(settler.NpcId).Pose); // the settler keeps its chair
    }

    [Fact]
    public void ASettler_DoesNotSitIntoASeatedPlayer_AndSitsDownOnceHeGotUp()
    {
        using var w = new NpcLifeWorld(_content, "seat_player_first");
        int baseId = w.FoundBase();
        var chair = w.Chair(NpcLifeWorld.Cx - 1, NpcLifeWorld.Cz + 5);
        w.Server.ScanBaseLifeForTest();
        var settler = w.Server.BaseResidentsForTest(baseId).Single(r => r.Slot == 0);
        Assert.Equal(chair, settler.Seat);

        w.Player.State.Position = new Vector3f(chair.X + 0.5f, w.Feet, chair.Z + 0.5f);
        w.Server.SetSeatedForTest(w.Player.State.PlayerId, true, chair);
        Assert.True(w.Player.State.Seated);
        Assert.Equal(chair, w.Player.State.SeatCell);

        // The evening: the settler walks over but never sits down into the player — not on arrival, and not at the
        // routine looks after it (every 2 s).
        bool everSat = false, walked = false;
        w.TickAt(0.72, 60,
            until: () => walked && !w.Server.NpcHasGoalForTest(settler.NpcId),
            every: () =>
            {
                everSat |= w.Server.NpcRoutineForTest(settler.NpcId).Pose == 1;
                walked |= w.Server.NpcHasGoalForTest(settler.NpcId);
            });
        w.TickAt(0.72, 6, every: () => everSat |= w.Server.NpcRoutineForTest(settler.NpcId).Pose == 1);
        Assert.True(walked, "the settler never set off for its chair");
        Assert.False(everSat, "the settler sat down into the seated player");
        var waiting = w.Server.NpcRoutineForTest(settler.NpcId);
        Assert.False(w.Server.NpcHasGoalForTest(settler.NpcId), $"never arrived at the chair (at {waiting.Pos})");
        Assert.Equal("npc.activity.resting", waiting.Activity);
        double dx = waiting.Pos.X - (chair.X + 0.5), dz = waiting.Pos.Z - (chair.Z + 0.5);
        Assert.True(dx * dx + dz * dz < 3.0 * 3.0, $"rests at {waiting.Pos}, not beside the chair {chair}");

        // The player gets up: the settler takes the chair at its next look.
        w.Server.SetSeatedForTest(w.Player.State.PlayerId, false);
        Assert.Null(w.Player.State.SeatCell);
        w.TickAt(0.72, 10, until: () => w.Server.NpcRoutineForTest(settler.NpcId).Pose == 1);
        var sitting = w.Server.NpcRoutineForTest(settler.NpcId);
        Assert.Equal(1, sitting.Pose);
        Assert.True(Math.Abs(sitting.Pos.X - (chair.X + 0.5f)) < 0.01f && Math.Abs(sitting.Pos.Z - (chair.Z + 0.5f)) < 0.01f,
            $"sits at {sitting.Pos}, not on the chair {chair}");
    }

    [Fact]
    public void TwoPlayers_OneChair_TheSecondIsRefused_UntilTheFirstGetsUp()
    {
        using var w = new NpcLifeWorld(_content, "seat_two_players");
        var chair = w.Chair(NpcLifeWorld.Cx + 3, NpcLifeWorld.Cz + 3);
        var guest = w.Server.AddLocalPlayer("Guest");
        guest.State.AboardShip = false;

        StandBeside(w.Player, chair, w.Feet);
        StandBeside(guest, chair, w.Feet);
        w.Server.SetSeatedForTest(w.Player.State.PlayerId, true, chair);
        Assert.True(w.Player.State.Seated);

        w.Server.SetSeatedForTest(guest.State.PlayerId, true, chair);
        Assert.False(guest.State.Seated);
        Assert.Equal("@srv.seat.taken", LastRejection(w, guest)?.Reason);

        // Re-sending the own seat is no conflict with oneself.
        w.Server.SetSeatedForTest(w.Player.State.PlayerId, true, chair);
        Assert.True(w.Player.State.Seated);

        w.Server.SetSeatedForTest(w.Player.State.PlayerId, false);
        w.Server.SetSeatedForTest(guest.State.PlayerId, true, chair);
        Assert.True(guest.State.Seated);
        Assert.Equal(chair, guest.State.SeatCell);
    }

    [Fact]
    public void ASeat_MustBeThere_AndInReach_AndAnOlderClientsSeatIsFoundAroundIt()
    {
        using var w = new NpcLifeWorld(_content, "seat_checks");
        var chair = w.Chair(NpcLifeWorld.Cx + 3, NpcLifeWorld.Cz + 3);
        string id = w.Player.State.PlayerId;

        // No seat in that cell (plain stone floor beside the chair is air above the pad).
        StandBeside(w.Player, chair, w.Feet);
        w.Server.SetSeatedForTest(id, true, new Vector3i(chair.X + 1, chair.Y, chair.Z));
        Assert.False(w.Player.State.Seated);
        Assert.Equal("@srv.seat.none", LastRejection(w, w.Player)?.Reason);

        // Too far away.
        w.Player.State.Position = new Vector3f(chair.X + 15.5f, w.Feet, chair.Z + 0.5f);
        w.Server.SetSeatedForTest(id, true, chair);
        Assert.False(w.Player.State.Seated);
        Assert.Equal("@srv.seat.too_far", LastRejection(w, w.Player)?.Reason);

        // An older client sends no cell: the seat right around its position is the one it sits on.
        w.Player.State.Position = new Vector3f(chair.X + 0.5f, w.Feet, chair.Z + 0.5f);
        w.Server.SetSeatedForTest(id, true);
        Assert.True(w.Player.State.Seated);
        Assert.Equal(WorldConstants.CanonicalBlock(chair, w.Server.World.Circumference), w.Player.State.SeatCell);

        // Standing up frees it.
        w.Server.SetSeatedForTest(id, false);
        Assert.False(w.Player.State.Seated);
        Assert.Null(w.Player.State.SeatCell);
    }

    [Fact]
    public void SettlementResidents_NeverShareASeat_EvenWithMoreResidentsThanTavernChairs()
    {
        bool crowded = false;
        int checkedWorlds = 0;
        for (long seed = 1; seed <= 40 && !crowded; seed++)
        {
            var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "seats_" + seed));
            using (repo)
            {
                var config = new ServerConfig
                {
                    WorldName = "seats_" + seed,
                    Seed = seed,
                    StartPlanet = "jungle",
                    AutoSaveIntervalMinutes = 9999,
                    PlaceStarterShip = false,
                    PlaceSettlements = true,
                    PlaceWrecks = false,
                };
                var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
                server.Start();
                int circ = server.World.Circumference;

                var claims = server.NpcSeatsForTest.Select(s => WorldConstants.CanonicalBlock(s.Seat, circ)).ToList();
                Assert.Equal(claims.Count, claims.Distinct().Count());
                checkedWorlds++;

                foreach (var (name, tavernSeats) in server.TavernSeatsForTest)
                {
                    int residents = server.SettlementResidentsForTest.Single(s => s.Name == name).Residents.Count;
                    if (tavernSeats > 0 && residents > tavernSeats)
                    {
                        crowded = true; // the case the wrapping cursor double-booked
                    }
                }

                server.Stop();
            }
        }

        Assert.True(checkedWorlds > 0);
        Assert.True(crowded, "no settlement with more residents than tavern seats in the searched seeds");
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
        catch (IOException)
        {
            // a locked temp file must not fail the run
        }
    }
}
