// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Base landing pads (#3069–#3073, epic #3074): a pad stands near one of the player's own bases, at most four per base, on a
/// clear 17 × 17 square that then stays clear and is protected like the base; who may land is the owner's setting; a landing
/// on it holds it like a communal pad; a landing nobody picked by hand prefers the player's own pad; mining the core makes it
/// inactive; a Market base's pads on "Me + allies" and "Everyone" receive the travelling merchants (#3175); pads survive a restart.
/// </summary>
public sealed class BasePadTests : IDisposable
{
    private static readonly Vector3i Core = new(100, 200, 100);
    private static readonly Vector3i PadEast = new(120, 200, 100);
    private static readonly Vector3i PadWest = new(80, 200, 100);
    private static readonly Vector3i PadNorth = new(100, 200, 120);
    private static readonly Vector3i PadSouth = new(100, 200, 80);

    private readonly string _root;
    private readonly GameContent _content;

    public BasePadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_basepads_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string tag = "pads", bool ship = false, string startPlanet = "rocky")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = tag, Seed = 1, StartPlanet = startPlanet, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = ship };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static PlayerSession Player(SvGameServer server, string name)
    {
        var p = server.AddLocalPlayer(name);
        p.State.Position = new Vector3f(Core.X, Core.Y + 1, Core.Z);
        return p;
    }

    private static void Ally(SvGameServer server, string a, string b)
    {
        server.RequestAlliance(a, b);
        server.RespondAlliance(b, a, accept: true);
        Assert.True(server.AreAllied(a, b));
    }

    private void FoundBase(SvGameServer server, PlayerSession owner, Vector3i core)
    {
        server.PlaceBaseForTest(owner, core);
        server.World.SetBlock(core, _content.GetBlock("base_core")!.NumericId);
    }

    [Fact]
    public void APad_StandsOnlyNearAnOwnBase_ApartFromOtherPads_AndAtMostFourPerBase()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var owner = Player(server, "Owner");
            Assert.Equal("@srv.basepad.no_base", server.PlaceBasePadForTest(owner, PadEast));

            FoundBase(server, owner, Core);
            Assert.Null(server.PlaceBasePadForTest(owner, PadEast));
            Assert.Equal("@srv.basepad.overlap", server.PlaceBasePadForTest(owner, new Vector3i(PadEast.X + 10, PadEast.Y, PadEast.Z)));
            Assert.Equal("@srv.basepad.no_base", server.PlaceBasePadForTest(owner, new Vector3i(Core.X + BasePadRules.ReachXZ + 5, Core.Y, Core.Z)));
            Assert.Null(server.PlaceBasePadForTest(owner, PadWest));
            Assert.Null(server.PlaceBasePadForTest(owner, PadNorth));
            Assert.Null(server.PlaceBasePadForTest(owner, PadSouth));

            Assert.Equal("@srv.basepad.limit:4", server.PlaceBasePadForTest(owner, new Vector3i(120, 200, 120)));
            Assert.Equal(4, server.BasePadSnapshots.Count);
            Assert.All(server.BasePadSnapshots, p => Assert.Equal(BasePadAccess.Allies, p.Access));
            Assert.Equal(4, repo.ListAllBasePads().Count);
        }
    }

    [Fact]
    public void TheLandingSquare_MustBeClear_ThenNobodyBuildsInIt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var owner = Player(server, "Owner");
            FoundBase(server, owner, Core);
            var stone = _content.GetBlock("stone")!.NumericId;

            server.World.SetBlock(new Vector3i(PadEast.X + 3, PadEast.Y + 5, PadEast.Z - 2), stone);
            Assert.Equal("@srv.basepad.blocked:1", server.PlaceBasePadForTest(owner, PadEast));

            server.World.SetBlock(new Vector3i(PadEast.X + 3, PadEast.Y + 5, PadEast.Z - 2), BlockId.Air);
            Assert.Null(server.PlaceBasePadForTest(owner, PadEast));

            // The volume above the pad is kept clear for everyone — the owner too; the ground layer itself is not.
            Assert.True(server.BasePadVolumeForTest(new Vector3i(PadEast.X + 8, PadEast.Y + 1, PadEast.Z + 8)));
            Assert.True(server.BasePadVolumeForTest(new Vector3i(PadEast.X, PadEast.Y + BasePadRules.Clearance, PadEast.Z)));
            Assert.False(server.BasePadVolumeForTest(new Vector3i(PadEast.X + 3, PadEast.Y, PadEast.Z)));
            Assert.False(server.BasePadVolumeForTest(new Vector3i(PadEast.X + 9, PadEast.Y + 1, PadEast.Z)));
            Assert.False(server.BasePadVolumeForTest(new Vector3i(PadEast.X, PadEast.Y + BasePadRules.Clearance + 1, PadEast.Z)));
        }
    }

    [Fact]
    public void TheSquare_IsProtectedLikeTheBase()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var owner = Player(server, "Owner");
            Player(server, "Ally");
            Player(server, "Stranger");
            Ally(server, "Owner", "Ally");
            FoundBase(server, owner, Core);
            Assert.Null(server.PlaceBasePadForTest(owner, PadEast));
            var ground = new Vector3i(PadEast.X + 5, PadEast.Y, PadEast.Z - 5);

            Assert.True(server.BasePadProtectedForTest(ground, "Stranger"));
            Assert.True(server.BasePadProtectedForTest(PadEast, "Stranger"));
            Assert.False(server.BasePadProtectedForTest(ground, "Owner"));
            Assert.False(server.BasePadProtectedForTest(PadEast, "Owner"));
            Assert.False(server.BasePadProtectedForTest(ground, "Ally"));
            Assert.True(server.BasePadProtectedForTest(PadEast, "Ally")); // the pad block itself is the owner's
            Assert.False(server.BasePadProtectedForTest(PadEast, "Stranger", isAdmin: true));
            Assert.False(server.BasePadProtectedForTest(new Vector3i(PadEast.X + 12, PadEast.Y, PadEast.Z), "Stranger"));
        }
    }

    [Fact]
    public void TheAccess_DecidesWhoMayLand_AndAStrangerSeesAFoundPadLocked()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var owner = Player(server, "Owner");
            var ally = Player(server, "Ally");
            var stranger = Player(server, "Stranger");
            Ally(server, "Owner", "Ally");
            FoundBase(server, owner, Core);
            Assert.Null(server.PlaceBasePadForTest(owner, PadEast));
            string body = owner.CurrentLocationId;

            NetLandingPad Seen(PlayerSession p) => Assert.Single(server.BasePadListForTest(p, body));

            Assert.False(Seen(owner).Locked);
            Assert.Equal(NetBase.RelationOwn, Seen(owner).Relation);
            Assert.False(Seen(ally).Locked);
            Assert.Equal(NetBase.RelationAlly, Seen(ally).Relation);
            Assert.Empty(server.BasePadListForTest(stranger, body)); // an undiscovered stranger's base: nothing at all

            stranger.State.DiscoveredBases.Add(body + "|" + Core.X + "|" + Core.Y + "|" + Core.Z);
            Assert.True(Seen(stranger).Locked);
            Assert.Equal(new[] { PadEast.X, PadEast.Y, PadEast.Z, Core.X, Core.Y, Core.Z },
                new[] { Seen(stranger).CellX, Seen(stranger).CellY, Seen(stranger).CellZ, Seen(stranger).BaseX, Seen(stranger).BaseY, Seen(stranger).BaseZ });

            server.SetBasePadForTest(owner, PadEast, "North Field", BasePadAccess.Everyone);
            Assert.False(Seen(stranger).Locked);
            Assert.Equal("North Field", Seen(stranger).Name);

            server.SetBasePadForTest(owner, PadEast, "North Field", BasePadAccess.Owner);
            Assert.True(Seen(ally).Locked);

            server.SetBasePadForTest(ally, PadEast, "Mine now", BasePadAccess.Everyone); // not the owner: refused
            Assert.Equal(BasePadAccess.Owner, server.BasePadSnapshots.Single().Access);
            Assert.Equal("North Field", repo.ListAllBasePads().Single().Name);
        }
    }

    [Fact]
    public void LandingOnTheBasePad_HoldsIt_SetsThePilotDownThere_AndKeepsTheBlockFromBeingMined()
    {
        var server = Started(out var repo, "land", ship: true);
        using (repo)
        {
            var owner = Player(server, "Owner");
            var ally = Player(server, "Ally");
            Ally(server, "Owner", "Ally");
            FoundBase(server, owner, Core);
            Assert.Null(server.PlaceBasePadForTest(owner, PadEast));

            server.EnterSpace("Owner");
            server.LandOnBasePadForTest(owner, PadEast);

            Assert.Equal(PadEast, server.HeldBasePadForTest(owner));
            Assert.Equal(-1, owner.AssignedPadIndex);
            Assert.InRange(owner.State.Position.X, PadEast.X - BasePadRules.HalfSize, PadEast.X + BasePadRules.HalfSize + 1);
            Assert.InRange(owner.State.Position.Z, PadEast.Z - BasePadRules.HalfSize, PadEast.Z + BasePadRules.HalfSize + 1);
            Assert.True(owner.State.Position.Y > PadEast.Y);
            Assert.True(Assert.Single(server.BasePadListForTest(owner, owner.CurrentLocationId)).Mine);
            Assert.True(Assert.Single(server.BasePadListForTest(ally, owner.CurrentLocationId)).Occupied);

            server.EnterSpace("Ally");
            server.LandOnBasePadForTest(ally, PadEast); // taken: the ally stays up there
            Assert.Null(server.HeldBasePadForTest(ally));

            owner.State.Position = new Vector3f(PadEast.X + 2, PadEast.Y + 1, PadEast.Z);
            server.MineBlock("Owner", PadEast.X, PadEast.Y, PadEast.Z);
            Assert.Single(server.BasePadSnapshots); // a ship holds it: the pad stays
        }
    }

    [Fact]
    public void APad_MayNotStandAboveOrBelowAnotherPad()
    {
        var server = Started(out var repo, "stacked");
        using (repo)
        {
            var owner = Player(server, "Owner");
            FoundBase(server, owner, Core);
            Assert.Null(server.PlaceBasePadForTest(owner, PadEast));

            // 25 blocks up: clear of the landing volume, but one column holds one pad (the parking height is read by column).
            Assert.Equal("@srv.basepad.overlap", server.PlaceBasePadForTest(owner, new Vector3i(PadEast.X, PadEast.Y + 25, PadEast.Z)));
        }
    }

    [Fact]
    public void WalkingTheShipInOrbit_KeepsTheBasePadItStandsOn()
    {
        var server = Started(out var repo, "interior", ship: true);
        using (repo)
        {
            var owner = Player(server, "Owner");
            FoundBase(server, owner, Core);
            Assert.Null(server.PlaceBasePadForTest(owner, PadEast));
            server.EnterSpace("Owner");
            server.LandOnBasePadForTest(owner, PadEast);
            Assert.Equal(PadEast, server.HeldBasePadForTest(owner));

            server.EnterSpace("Owner");
            server.EnterShipInterior("Owner"); // the interior is a world of its own — placing the ship there must not move the claim
            Assert.True(server.InShipInterior("Owner"));

            Assert.Equal(PadEast, server.HeldBasePadForTest(owner));
            Assert.Equal(-1, owner.AssignedPadIndex);
        }
    }

    [Fact]
    public void ARefusedMapTakeOff_KeepsTheBasePadTheShipStandsOn()
    {
        var server = Started(out var repo, "refused", ship: true, startPlanet: "varied");
        using (repo)
        {
            var owner = Player(server, "Owner");
            FoundBase(server, owner, Core);
            Assert.Null(server.PlaceBasePadForTest(owner, PadEast));
            server.EnterSpace("Owner");
            server.LandOnBasePadForTest(owner, PadEast);
            Assert.Equal(PadEast, server.HeldBasePadForTest(owner));

            var origin = server.Galaxy.FindBody(owner.CurrentLocationId)!;
            var destination = server.Galaxy.AllBodies().First(b => b.Kind == BlocksBeyondTheStars.Shared.World.CelestialKind.Planet
                && b.SystemId == origin.SystemId && b.Id != origin.Id && _content.GetPlanet(b.PlanetType ?? string.Empty) is not null);
            owner.State.LandedBodies.Add(destination.Id);
            owner.State.AboardShip = false; // the take-off is refused: "board your ship first"

            server.TravelIntentForTest(owner, new TravelIntent { DestinationBodyId = destination.Id });

            Assert.False(server.InSpace("Owner"));
            Assert.Equal(PadEast, server.HeldBasePadForTest(owner));
        }
    }

    [Fact]
    public void ALandingNobodyPicked_PrefersTheOwnPad_AndACommunalPickReleasesIt()
    {
        var server = Started(out var repo, "auto", ship: true);
        using (repo)
        {
            var owner = Player(server, "Owner");
            FoundBase(server, owner, Core);
            Assert.Null(server.PlaceBasePadForTest(owner, PadEast));

            server.EnterSpace("Owner");
            server.LandOnCurrentBodyForTest(owner);
            Assert.Equal(PadEast, server.HeldBasePadForTest(owner));

            server.EnterSpace("Owner");
            server.LandOnCurrentBodyForTest(owner, 1);
            Assert.Null(server.HeldBasePadForTest(owner));
            Assert.Equal(1, owner.AssignedPadIndex);
        }
    }

    [Fact]
    public void MiningTheCore_MakesThePadInactive_AndANewCoreBringsItBack()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var owner = Player(server, "Owner");
            owner.State.Inventory.Add("base_core", 2, 16);
            server.PlaceBlock("Owner", Core.X, Core.Y, Core.Z, "base_core");
            Assert.Single(server.BaseSnapshots);
            Assert.Null(server.PlaceBasePadForTest(owner, PadEast));
            string body = owner.CurrentLocationId;

            server.MineBlock("Owner", Core.X, Core.Y, Core.Z);
            Assert.Empty(server.BaseSnapshots);
            Assert.Single(server.BasePadSnapshots); // still registered …
            Assert.Empty(server.BasePadListForTest(owner, body)); // … but offered to nobody

            server.PlaceBlock("Owner", Core.X, Core.Y, Core.Z, "base_core");
            Assert.Single(server.BasePadListForTest(owner, body));
        }
    }

    [Fact]
    public void AMarketBase_OffersItsPadsOnAlliesAndEveryone_ToTheMerchants_ButNeverOnlyMe()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var owner = Player(server, "Owner");
            FoundBase(server, owner, Core);
            Assert.Null(server.PlaceBasePadForTest(owner, PadEast));
            server.SetBasePadForTest(owner, PadEast, string.Empty, BasePadAccess.Everyone);
            Assert.Null(server.MarketBasePadForTest(owner)); // no Market focus yet

            server.YieldForTest(owner, new YieldIntent
            {
                Action = YieldIntent.ActionFocus,
                Kind = YieldIntent.KindBase,
                BodyId = owner.CurrentLocationId,
                CellX = Core.X,
                CellY = Core.Y,
                CellZ = Core.Z,
                Focus = BaseFocus.Market,
            });
            Assert.Equal(PadEast, server.MarketBasePadForTest(owner));

            // #3175: the default "Me + allies" takes them too — a player who sets "Market" wants traders.
            server.SetBasePadForTest(owner, PadEast, string.Empty, BasePadAccess.Allies);
            Assert.Equal(PadEast, server.MarketBasePadForTest(owner));

            server.SetBasePadForTest(owner, PadEast, string.Empty, BasePadAccess.Owner); // "Only me": the merchants keep off
            Assert.Null(server.MarketBasePadForTest(owner));
        }
    }

    [Fact]
    public void Pads_SurviveARestart_WithTheirNameAndAccess()
    {
        var server = Started(out var repo, "restart");
        var owner = Player(server, "Owner");
        FoundBase(server, owner, Core);
        Assert.Null(server.PlaceBasePadForTest(owner, PadEast));
        server.SetBasePadForTest(owner, PadEast, "Home Field", BasePadAccess.Everyone);
        server.Stop();
        repo.Dispose();

        var again = Started(out var repo2, "restart");
        using (repo2)
        {
            var pad = Assert.Single(again.BasePadSnapshots);
            Assert.Equal((PadEast, "Home Field", "Owner", BasePadAccess.Everyone), (pad.Cell, pad.Name, pad.OwnerId, pad.Access));
        }
    }
}
