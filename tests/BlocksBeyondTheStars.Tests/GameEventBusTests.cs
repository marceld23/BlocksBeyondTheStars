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
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Records every game event it is handed — the stand-in for the story-pack step runtime (#2466).</summary>
internal sealed class RecordingGameEventListener : IGameEventListener
{
    public List<(string Player, string Key, string Arg, int Amount)> Events { get; } = new();

    public Action<PlayerSession, string>? OnEvent { get; set; }

    public void OnGameEvent(PlayerSession actor, string key, string arg, int amount)
    {
        Events.Add((actor.State.PlayerId, key, arg, amount));
        OnEvent?.Invoke(actor, key);
    }

    public IEnumerable<(string Player, string Key, string Arg, int Amount)> Of(string key) => Events.Where(e => e.Key == key);
}

/// <summary>
/// #2465 — step 6 of the story packs (#2459): the game-event bus. One server entry point, <c>RaiseGameEvent</c>, sits
/// next to the achievement / VEGA / story hooks and hands facts <c>(key, arg)</c> to registered listeners: mining,
/// crafting, research and the landings carry the right argument (<c>flight|quick|admin</c>), the join's own placement
/// and an observer raise nothing, nothing is written into the achievement counters, and a listener can neither break
/// the gameplay handler nor trip the delivery by (un)registering while it runs.
/// </summary>
public sealed class GameEventBusTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public GameEventBusTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_events_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var repo in _repos)
        {
            try { repo.Dispose(); } catch { /* best effort */ }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private ServerConfig Config(string world)
    {
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
        };
        config.Rules.FreeSpaceFlight = true;
        return config;
    }

    private SqliteWorldRepository Repo(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return repo;
    }

    private SvGameServer Started(string world, RecordingGameEventListener? listener = null)
    {
        var server = new SvGameServer(Config(world), _content, new LoopbackServerTransport(new LoopbackLink()), Repo(world));
        server.Start();
        if (listener is not null)
        {
            server.AddGameEventListener("*", listener);
        }

        return server;
    }

    private CelestialBody OtherBodyInSystem(SvGameServer server, string fromBodyId)
    {
        var origin = server.Galaxy.FindBody(fromBodyId)!;
        return server.Galaxy.AllBodies().First(b =>
            b.Kind == CelestialKind.Planet
            && !string.IsNullOrEmpty(b.PlanetType)
            && _content.GetPlanet(b.PlanetType!) is not null
            && b.SystemId == origin.SystemId
            && b.Id != origin.Id);
    }

    private void MineOne(SvGameServer server, PlayerSession p, string blockKey, int x)
    {
        var pos = new Vector3i(x, 60, 20);
        p.State.Position = new Vector3f(pos.X + 1.2f, pos.Y + 0.5f, pos.Z + 0.5f);
        server.World.SetBlock(pos, _content.GetBlock(blockKey)!.NumericId);
        server.MineBlock(p.State.PlayerId, pos.X, pos.Y, pos.Z);
    }

    // ---------------- The bus itself ----------------

    [Fact]
    public void ExactAndWildcardListeners_GetWhatTheyAskFor_AndCanBeRemoved()
    {
        var server = Started("bus_registry");
        var p = server.AddLocalPlayer("Ada");
        var mine = new RecordingGameEventListener();
        var crafts = new RecordingGameEventListener();
        var all = new RecordingGameEventListener();
        server.AddGameEventListener(GameEventKeys.MineAny, mine);
        server.AddGameEventListener("craft:*", crafts);
        server.AddGameEventListener("*", all);

        Assert.True(server.HasGameEventListener(GameEventKeys.MineAny));
        Assert.True(server.HasGameEventListener(GameEventKeys.CraftShape)); // the craft:* prefix
        server.RaiseGameEventForTest(p, GameEventKeys.MineAny, "stone", 2);
        server.RaiseGameEventForTest(p, GameEventKeys.CraftTint, "stone");
        server.RaiseGameEventForTest(p, GameEventKeys.EatAny, "berry");

        Assert.Equal(new[] { ("Ada", "mine:any", "stone", 2) }, mine.Events);
        Assert.Equal(new[] { ("Ada", "craft:tint", "stone", 1) }, crafts.Events);
        Assert.Equal(3, all.Events.Count);

        Assert.True(server.RemoveGameEventListener(GameEventKeys.MineAny, mine));
        Assert.True(server.RemoveGameEventListener("craft:*", crafts));
        Assert.False(server.RemoveGameEventListener("craft:*", crafts)); // only once
        Assert.True(server.RemoveGameEventListener("*", all));
        Assert.False(server.HasGameEventListener(GameEventKeys.MineAny));
        server.RaiseGameEventForTest(p, GameEventKeys.MineAny, "stone");
        Assert.Single(mine.Events);

        Assert.Throws<ArgumentException>(() => server.AddGameEventListener("cr*ft", mine));
        Assert.Throws<ArgumentException>(() => server.AddGameEventListener(string.Empty, mine));
    }

    /// <summary>A non-positive amount, a missing key and a session that never joined raise nothing.</summary>
    [Fact]
    public void Guards_DropEmptyAndNonPositiveEvents()
    {
        var listener = new RecordingGameEventListener();
        var server = Started("bus_guards", listener);
        var p = server.AddLocalPlayer("Ada");

        server.RaiseGameEventForTest(p, GameEventKeys.MineAny, "stone", 0);
        server.RaiseGameEventForTest(p, GameEventKeys.MineAny, "stone", -3);
        server.RaiseGameEventForTest(p, string.Empty);
        server.RaiseGameEventForTest(new PlayerSession(999, new PlayerState { PlayerId = "ghost", Name = "ghost" }), GameEventKeys.MineAny);

        Assert.Empty(listener.Events);
    }

    /// <summary>A listener that throws is logged and dropped: the miner still gets the block and the next listener its event.</summary>
    [Fact]
    public void AThrowingListener_NeitherBreaksTheHandler_NorTheOtherListeners()
    {
        var server = Started("bus_throw");
        var p = server.AddLocalPlayer("Ada");
        p.State.AboardShip = false;
        p.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));
        var thrower = new RecordingGameEventListener { OnEvent = (_, _) => throw new InvalidOperationException("boom") };
        var witness = new RecordingGameEventListener();
        server.AddGameEventListener(GameEventKeys.MineAny, thrower);
        server.AddGameEventListener(GameEventKeys.MineAny, witness);

        MineOne(server, p, "stone", 20);

        Assert.Single(witness.Events);
        Assert.True(p.State.Inventory.CountOf("stone") > 0, "the mined block still reached the inventory");
    }

    /// <summary>The step runtime will activate the next step from inside a delivery — (un)registering then must not
    /// trip the iteration in progress, and takes effect from the next event.</summary>
    [Fact]
    public void AListenerMayRegisterAndUnregister_WhileAnEventIsDelivered()
    {
        var server = Started("bus_reentrant");
        var p = server.AddLocalPlayer("Ada");
        var next = new RecordingGameEventListener();
        var first = new RecordingGameEventListener();
        first.OnEvent = (_, _) =>
        {
            server.RemoveGameEventListener(GameEventKeys.MineAny, first);
            server.AddGameEventListener(GameEventKeys.MineAny, next);
        };
        server.AddGameEventListener(GameEventKeys.MineAny, first);

        server.RaiseGameEventForTest(p, GameEventKeys.MineAny, "stone");
        server.RaiseGameEventForTest(p, GameEventKeys.MineAny, "dirt");

        Assert.Single(first.Events);
        Assert.Equal("dirt", Assert.Single(next.Events).Arg);
    }

    // ---------------- Hook sites ----------------

    /// <summary>Mining raises <c>mine:any</c> with the block key, next to the achievement counter — which the event
    /// does not touch (the counter still counts one per block, exactly as before).</summary>
    [Fact]
    public void Mining_RaisesMineAny_WithTheBlockKey_AndLeavesTheCountersAlone()
    {
        var listener = new RecordingGameEventListener();
        var server = Started("bus_mine", listener);
        var p = server.AddLocalPlayer("Miner");
        p.State.AboardShip = false;
        p.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));

        MineOne(server, p, "iron_ore", 20);
        MineOne(server, p, "stone", 21);

        Assert.Equal(new[] { "iron_ore", "stone" }, listener.Of(GameEventKeys.MineAny).Select(e => e.Arg));
        Assert.Equal(2, p.State.AchievementCounters[AchievementCounters.MineAny]);
        Assert.Equal(1, p.State.AchievementCounters[AchievementCounters.Mine("iron_ore")]);
        Assert.DoesNotContain(p.State.AchievementCounters.Keys, k => k.StartsWith("scan:performed", StringComparison.Ordinal));
    }

    /// <summary>A craft raises <c>craft:any</c> with the recipe and the batch count; a form craft raises
    /// <c>craft:shape</c> plus <c>insight:shape_noncube</c> for a non-cube, and never <c>craft:any</c>.</summary>
    [Fact]
    public void Crafting_RaisesCraftAny_WithTheRecipe_AndFormsRaiseTheirOwnKeys()
    {
        var listener = new RecordingGameEventListener();
        var server = Started("bus_craft", listener);
        var p = server.AddLocalPlayer("Crafter");
        p.State.AboardShip = false;
        p.State.Inventory.Add("wood_log", 8, 99);
        p.State.Inventory.Add("stone", 8, 99);

        server.Craft("Crafter", "campfire", 2);
        server.ShapeCraft("Crafter", "stone", (int)BlockShape.Sphere);

        var craft = Assert.Single(listener.Of(GameEventKeys.CraftAny));
        Assert.Equal(("Crafter", "campfire", 2), (craft.Player, craft.Arg, craft.Amount));
        Assert.Equal("stone", Assert.Single(listener.Of(GameEventKeys.CraftShape)).Arg);
        Assert.Equal("stone", Assert.Single(listener.Of(GameEventKeys.InsightShapeNoncube)).Arg);
        Assert.Equal(1, p.State.AchievementCounters[AchievementCounters.CraftAny]); // one per craft call, as before
    }

    [Fact]
    public void ResearchAndScans_CarryTheBlueprintAndTheReadoutKind()
    {
        var listener = new RecordingGameEventListener();
        var server = Started("bus_research", listener);
        var p = server.AddLocalPlayer("Scholar");
        var bp = _content.GetBlueprint("machete")!;
        p.State.KnowledgePoints = bp.KnowledgeCost;
        foreach (var cost in bp.UnlockCost)
        {
            p.State.Inventory.Add(cost.Item, cost.Count, 99);
        }

        p.State.Inventory.SetSlot(2, new ItemStack("hand_scanner", 1));

        server.UnlockBlueprint("Scholar", bp.Key);
        server.ScanSubject("Scholar", "block", "iron_ore");
        server.ScanSubject("Scholar", "block", "iron_ore"); // a repeat: performed again, but no new discovery

        Assert.Equal(bp.Key, Assert.Single(listener.Of(GameEventKeys.ResearchAny)).Arg);
        Assert.Equal(2, listener.Of(GameEventKeys.ScanPerformed).Count());
        Assert.All(listener.Of(GameEventKeys.ScanPerformed), e => Assert.Equal("block", e.Arg));
        Assert.Single(listener.Of(GameEventKeys.ScanAny));
    }

    /// <summary>The launch and the landings carry their call-site context: a take-off is <c>launch</c>, setting down
    /// on the body you launched from is a <c>flight</c> landing, the map's quick travel is <c>quick</c> (plus the first
    /// visit of that body), and the admin's finale shortcut is <c>admin</c>.</summary>
    [Fact]
    public void Landings_CarryFlightQuickOrAdmin_AndTheLaunchIsALaunch()
    {
        var listener = new RecordingGameEventListener();
        var server = Started("bus_land", listener);
        var p = server.AddLocalPlayer("Pilot");
        server.Ship.Modules.Add("jump_generator");
        string home = p.CurrentLocationId;

        server.EnterSpace("Pilot");
        Assert.True(server.InSpace("Pilot"));
        server.LandOnBody("Pilot", home);
        Assert.False(server.InSpace("Pilot"));

        server.SetInstantTravelForTest(true);
        var dest = OtherBodyInSystem(server, home);
        Assert.True(server.QuickTravelForTest("Pilot", dest.Id));

        server.AdminGotoCore(p);

        Assert.Equal(GameEventKeys.SpaceLaunch, Assert.Single(listener.Of(GameEventKeys.SpaceEnter)).Arg);
        Assert.Equal(
            new[] { GameEventKeys.TravelFlight, GameEventKeys.TravelQuick, GameEventKeys.TravelAdmin },
            listener.Of(GameEventKeys.TravelLanded).Select(e => e.Arg));
        Assert.Contains(listener.Of(GameEventKeys.VisitBody), e => e.Arg == dest.Id);
        Assert.Contains(listener.Of(GameEventKeys.HyperjumpAny), e => e.Arg == SvGameServer.GuardianFinaleSystemId);
    }

    /// <summary>#1614: a trip chosen on the map from a parked ship flies by itself and lands through the flight
    /// landing — for the story it is the map's travel, <c>quick</c>. The network join before it raised nothing.</summary>
    [Fact]
    public void AMapTripFromAParkedShip_LandsAsQuickTravel_AndTheNetworkJoinRaisedNothing()
    {
        var listener = new RecordingGameEventListener();
        var link = new LoopbackLink();
        var transport = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var networked = new SvGameServer(Config("bus_transit_net"), _content, transport, Repo("bus_transit_net"));
        networked.Start();
        networked.AddGameEventListener("*", listener);
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Pilot" }), DeliveryMode.ReliableOrdered);
        networked.Tick(0.1);
        client.Poll();

        var pilot = networked.Sessions.Values.Single();
        Assert.False(pilot.InJoinPlacement);
        Assert.Empty(listener.Events); // the spawn body counted as visited, the ship set up — none of it the player's doing

        var destination = OtherBodyInSystem(networked, pilot.CurrentLocationId);
        pilot.State.LandedBodies.Add(destination.Id);
        client.Send(NetCodec.Encode(new TravelIntent { DestinationBodyId = destination.Id }), DeliveryMode.ReliableOrdered);
        networked.Tick(0.1);
        client.Send(NetCodec.Encode(new TransitLaunchDoneIntent()), DeliveryMode.ReliableOrdered);
        networked.Tick(0.1);

        Assert.Equal(destination.Id, pilot.CurrentLocationId);
        Assert.Equal(GameEventKeys.TravelQuick, Assert.Single(listener.Of(GameEventKeys.TravelLanded)).Arg);
        Assert.Equal(GameEventKeys.SpaceLaunch, Assert.Single(listener.Of(GameEventKeys.SpaceEnter)).Arg);
    }

    /// <summary>The local join (AddLocalPlayer) raises nothing either — not even the spawn body's first "visit".</summary>
    [Fact]
    public void TheJoinPlacement_RaisesNothing()
    {
        var listener = new RecordingGameEventListener();
        var server = Started("bus_join", listener);

        var p = server.AddLocalPlayer("Newcomer");

        Assert.Contains(p.CurrentLocationId, p.State.LandedBodies); // the join did book the spawn body…
        Assert.Empty(listener.Events);                              // …without telling any step
        Assert.False(p.InJoinPlacement);
    }

    /// <summary>A fleet admin watching in observer mode raises nothing — not by mining, not by landing, not by a
    /// raw raise — while the achievement path keeps its own (unchanged) behaviour.</summary>
    [Fact]
    public void AnObserver_RaisesNothing()
    {
        var listener = new RecordingGameEventListener();
        var server = Started("bus_observer", listener);
        var watcher = server.AddLocalPlayer("Watcher");
        watcher.Spectating = true;
        watcher.State.AboardShip = false;
        watcher.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));

        MineOne(server, watcher, "stone", 30);
        server.RaiseGameEventForTest(watcher, GameEventKeys.CraftAny, "campfire");
        server.SetInstantTravelForTest(true);
        server.QuickTravelForTest("Watcher", OtherBodyInSystem(server, watcher.CurrentLocationId).Id);

        Assert.Empty(listener.Events);
    }
}
