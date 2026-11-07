// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Microsoft.Data.Sqlite;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Companion selection (#3344): the player picks who follows. A planet pet left behind stays home — it appears at its home
/// spot, potters about there and is leashed to home, not to its owner; a travelling companion left behind waits on board and
/// appears nowhere. The follow payoffs come only from followers; the choice and the home spot survive a restart; the
/// Companions tab's intent gets through while the Tab menu holds a singleplayer world.
/// </summary>
public sealed class CompanionSelectionTests : IDisposable
{
    private static readonly Vector3i Centre = new(300, 150, 300);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_select_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer Server(string name, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = -12; x <= 12; x++)
        {
            for (int z = -12; z <= 12; z++)
            {
                server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y - 1, Centre.Z + z), stone);
                for (int y = 0; y < 3; y++)
                {
                    server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y + y, Centre.Z + z), BlockId.Air);
                }
            }
        }

        // The pets of these tests walk on the ground, awake around the clock.
        var sp = server.SpeciesRoster.First();
        sp.Habitat = CreatureHabitat.Land;
        sp.HasWings = false;
        sp.HasGasSac = false;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.Temperament = CreatureTemperament.Passive;
        return server;
    }

    private (SvGameServer Server, RecordingTransport Transport, PlayerSession Owner) Started(string name)
    {
        var transport = new RecordingTransport();
        var server = Server(name, transport);
        var owner = server.AddLocalPlayer("Ada");
        owner.State.AboardShip = false;
        owner.State.Position = At(0, 0);
        return (server, transport, owner);
    }

    private static Vector3f At(float dx, float dz) => new(Centre.X + 0.5f + dx, Centre.Y, Centre.Z + 0.5f + dz);

    private static float Flat(Vector3f a, Vector3f b) => MathF.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    private static TamedCreature Pet(SvGameServer server, string id, bool following = true, Vector3f? home = null)
    {
        var species = server.SpeciesRoster.First();
        var tc = new TamedCreature
        {
            Id = id,
            HomeBodyId = server.ActiveLocationId,
            Name = "Flocke " + id,
            SpeciesId = species.Id,
            Species = species,
            SizeScale = 1f,
            Bond = 80,
            Following = following,
        };
        if (home is { } h)
        {
            tc.HasHomeSpot = true;
            tc.HomeX = h.X;
            tc.HomeY = h.Y;
            tc.HomeZ = h.Z;
        }

        return tc;
    }

    private static CombatEntity EntityOf(SvGameServer server, string companionId)
        => server.CompanionEntitiesForTest("Ada").Single(c => c.CompanionId == companionId);

    [Fact]
    public void APetLeftAtHome_AppearsAtHome_PottersAboutThere_AndIsLeashedToHome()
    {
        var (server, _, owner) = Started("select_home");
        var home = At(-6, -6);
        owner.State.TamedCreatures.Add(Pet(server, "homebody", following: false, home: home));
        owner.State.TamedCreatures.Add(Pet(server, "follower"));
        owner.State.Position = At(8, 9);

        server.ReconcileCompanionsForTest();
        var homebody = EntityOf(server, "homebody");
        var follower = EntityOf(server, "follower");
        Assert.True(homebody.StaysHome);
        Assert.False(follower.StaysHome);
        Assert.True(Flat(homebody.Position, home) <= 4f, "a pet at home appears at its home spot, not beside its owner");

        // Both end up far from home but near the owner: the follower stays with its owner, the homebody goes home.
        homebody.Position = At(8, 8);
        follower.Position = At(8, 8);
        server.MoveCompanionsForTest(0.1);
        Assert.True(Flat(homebody.Position, home) <= 4f, "past the home leash a pet at home is put back home");
        Assert.True(Flat(follower.Position, owner.State.Position) <= 3f, "a follower is not pulled home");

        for (int i = 0; i < 80; i++)
        {
            server.MoveCompanionsForTest(0.1);
            Assert.True(Flat(homebody.Position, home) <= SvGameServer.CompanionHomeWanderRange + 2f, $"step {i}: it potters about home");
        }
    }

    [Fact]
    public void LeavingAFollowerAtHome_MakesTheSpotItStandsOnItsHome_AndTakingItAlongAgainBringsItBack()
    {
        var (server, transport, owner) = Started("select_toggle");
        owner.State.TamedCreatures.Add(Pet(server, "flocke")); // a record from before #3344: no home spot
        server.ReconcileCompanionsForTest();
        var pet = EntityOf(server, "flocke");
        var where = pet.Position;

        server.SetCompanionFollowForTest("Ada", "flocke", following: false);

        var tc = owner.State.TamedCreatures.Single();
        Assert.False(tc.Following);
        Assert.True(tc.HasHomeSpot);
        Assert.Equal((where.X, where.Z), (tc.HomeX, tc.HomeZ));
        Assert.True(pet.StaysHome);
        Assert.Contains("@srv.companion.stays_home:Flocke flocke", transport.TextsTo(owner));
        var listed = transport.To<CompanionList>(owner).Last().Companions.Single();
        Assert.False(listed.Following);
        Assert.False(listed.Roams);

        server.SetCompanionFollowForTest("Ada", "flocke", following: true);

        Assert.True(tc.Following);
        Assert.False(pet.StaysHome);
        Assert.Contains("@srv.companion.follows:Flocke flocke", transport.TextsTo(owner));
    }

    [Fact]
    public void ATravellingCompanionLeftBehind_WaitsOnBoard_AndAppearsNowhere_UntilItComesAlongAgain()
    {
        var (server, transport, owner) = Started("select_onboard");
        owner.State.Inventory.Add(SvGameServer.StarBellItem, 1, 1);
        Assert.True(server.DeployReindeerForTest("Ada"));
        string id = owner.State.TamedCreatures.Single().Id;

        server.SetCompanionFollowForTest("Ada", id, following: false);
        server.ReconcileCompanionsForTest();

        Assert.Null(server.ReindeerEntityForTest("Ada"));
        Assert.Contains("@srv.companion.on_board:Rudolpho", transport.TextsTo(owner));
        var listed = transport.To<CompanionList>(owner).Last().Companions.Single();
        Assert.False(listed.Following);
        Assert.True(listed.Roams);
        Assert.Equal(TamedCreature.ReindeerKind, listed.Kind);
        Assert.False(listed.Present);

        owner.State.TamedCreatures.Single().HomeBodyId = "some-other-body"; // on board he travels anywhere
        server.SetCompanionFollowForTest("Ada", id, following: true);

        Assert.NotNull(server.ReindeerEntityForTest("Ada"));
    }

    [Fact]
    public void TakeAllAndLeaveAll_ChangeEveryCompanion()
    {
        var (server, transport, owner) = Started("select_all");
        owner.State.TamedCreatures.Add(Pet(server, "a"));
        owner.State.TamedCreatures.Add(Pet(server, "b"));
        owner.State.Inventory.Add(SvGameServer.StarBellItem, 1, 1);
        Assert.True(server.DeployReindeerForTest("Ada"));
        server.ReconcileCompanionsForTest();

        server.SetCompanionFollowForTest("Ada", string.Empty, following: false, all: true);

        Assert.All(owner.State.TamedCreatures, t => Assert.False(t.Following));
        Assert.Null(server.ReindeerEntityForTest("Ada"));
        Assert.True(EntityOf(server, "a").StaysHome);
        Assert.True(EntityOf(server, "b").StaysHome);
        Assert.Contains("@srv.companion.all_stay", transport.TextsTo(owner));

        server.SetCompanionFollowForTest("Ada", string.Empty, following: true, all: true);

        Assert.All(owner.State.TamedCreatures, t => Assert.True(t.Following));
        Assert.NotNull(server.ReindeerEntityForTest("Ada"));
        Assert.False(EntityOf(server, "a").StaysHome);
        Assert.Contains("@srv.companion.all_follow", transport.TextsTo(owner));
    }

    [Fact]
    public void TheFollowPayoffs_ComeOnlyFromFollowers()
    {
        var (server, _, owner) = Started("select_payoff");
        owner.State.TamedCreatures.Add(Pet(server, "flocke"));
        server.ReconcileCompanionsForTest();
        var pet = EntityOf(server, "flocke");
        pet.Position = At(2, 0);

        Assert.True(server.PlayerWardedByCompanionForTest("Ada"));
        Assert.True(server.BanditWardedByCompanionForTest("Ada"));
        Assert.True(server.CompanionFetchesForTest("Ada", pet.Position));

        server.SetCompanionFollowForTest("Ada", "flocke", following: false);
        pet.Position = At(2, 0); // at home right beside its owner — and still it does none of it

        Assert.False(server.PlayerWardedByCompanionForTest("Ada"));
        Assert.False(server.BanditWardedByCompanionForTest("Ada"));
        Assert.False(server.CompanionFetchesForTest("Ada", pet.Position));
    }

    [Fact]
    public void SetHomeHere_MovesTheHome_OnThePetsOwnWorldOnly()
    {
        var (server, transport, owner) = Started("select_sethome");
        owner.State.TamedCreatures.Add(Pet(server, "flocke", following: false, home: At(-6, -6)));
        owner.State.TamedCreatures.Add(Pet(server, "faraway", following: false, home: At(0, 0)));
        owner.State.TamedCreatures.Last().HomeBodyId = "some-other-body";
        server.ReconcileCompanionsForTest();
        owner.State.Position = At(5, -3);

        server.SetCompanionHomeHereForTest("Ada", "flocke");
        server.SetCompanionHomeHereForTest("Ada", "faraway");

        var tc = owner.State.TamedCreatures.First();
        Assert.Equal((owner.State.Position.X, owner.State.Position.Z), (tc.HomeX, tc.HomeZ));
        Assert.Equal(owner.State.Position, EntityOf(server, "flocke").HomeSpot);
        Assert.Contains("@srv.companion.home_set:Flocke flocke", transport.TextsTo(owner));
        Assert.Contains("@srv.companion.home_away:Flocke faraway", transport.To<ActionRejected>(owner).Select(r => r.Reason));
        Assert.Equal(At(0, 0).X, owner.State.TamedCreatures.Last().HomeX); // the faraway pet keeps its home
    }

    [Fact]
    public void APetAtHome_KeepsItsSlot_TheCapIsUnchanged()
    {
        var (server, _, owner) = Started("select_cap");
        for (int i = 0; i < 7; i++)
        {
            owner.State.TamedCreatures.Add(Pet(server, "pet" + i, following: i % 2 == 0, home: At(-4, -4)));
        }

        server.ReconcileCompanionsForTest();

        Assert.Equal(6, server.CompanionEntitiesForTest("Ada").Count); // six planet pets per world, at home or not
    }

    [Fact]
    public void TheFollowIntent_IsServedWhileTheSingleplayerPauseHolds()
    {
        var (server, _, owner) = Started("select_paused");
        owner.State.TamedCreatures.Add(Pet(server, "flocke"));
        server.PauseForTest(owner, true);
        Assert.True(server.IsPaused);

        server.HandlePayloadForTest(owner.ConnectionId, NetCodec.Encode(new SetCompanionFollowIntent { CompanionId = "flocke", Following = false }));

        Assert.True(server.IsPaused);
        Assert.False(owner.State.TamedCreatures.Single().Following);
    }

    /// <summary>The choice and the home spot through the real save: after a restart the pet still stays home, at home.</summary>
    [Fact]
    public void AfterARestart_APetLeftAtHome_IsStillAtHome()
    {
        var (server, _, owner) = Started("select_restart");
        owner.State.TamedCreatures.Add(Pet(server, "flocke", home: At(-6, -6)));
        server.SetCompanionFollowForTest("Ada", "flocke", following: false); // saves the player
        server.Stop();
        _repos[0].Dispose();
        _repos.Clear();
        SqliteConnection.ClearAllPools();

        var again = Server("select_restart", new RecordingTransport());
        again.HandlePayloadForTest(91, NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Ada" }));

        var tc = Assert.Single(again.Sessions[91].State.TamedCreatures);
        Assert.False(tc.Following);
        Assert.True(tc.HasHomeSpot);
        Assert.Equal((At(-6, -6).X, At(-6, -6).Z), (tc.HomeX, tc.HomeZ));
    }
}
