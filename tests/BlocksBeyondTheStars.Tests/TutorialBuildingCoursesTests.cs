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
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2480 — step 19 of the story packs (#2459): the tutorial's deep dives of the parts "Ship building" and "Base building" —
/// C1 Workshop and titanium, C6 Ship builder, C7 Homestead, C17 Station builder (synthesis §3.2/§3.4). Their shape (optional
/// chapters, hard prerequisites, the moments they are offered at, the two new cards, the hints they absorb); every new verb
/// raised by its real gameplay handler; and a walk through every course with the real handlers — the trade console, a forge
/// and a titanium plate; a module, a repair, building onto the parked ship, a keel, commissioning and switching ships; a
/// base core, building in its zone, lights, a door, a sealed room, a wall ring and a bed; a station core on a spacewalk, its
/// hull and zero-g building — also with the world rules and the evidence that skip steps, and an ally's base (E32).
/// </summary>
public sealed class TutorialBuildingCoursesTests : IDisposable
{
    private const string T = SvGameServer.OfficialTutorialPackId;

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public TutorialBuildingCoursesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_building_courses_" + Guid.NewGuid().ToString("N"));
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

    private SqliteWorldRepository Repo(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return repo;
    }

    /// <summary>A world with free flight and no world story (the courses are the tutorial's; VEGA's chapters would only add
    /// cards and lines around them).</summary>
    private (SvGameServer Server, RecordingTransport Transport) World(string world, Action<ServerConfig>? configure = null)
    {
        var transport = new RecordingTransport();
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            ViewDistanceChunks = 1,
            MaxPlayers = 6,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.StoryId = StoryRegistry.NoneStoryId;
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, Repo(world));

        // #2769: the 1 Hz tutorial check refreshes a base's air on demand, within what is left of the tick's wall-clock
        // budget; the ticks that would finish the refill in the game (the tick driver guarantees every fill progress) are
        // the ones these tests skip with AdvanceUptimeForTest. A budget that never binds lets the check finish a small
        // room's refill itself on any runner — the check still starts and runs it, only the clock cannot cut it short.
        // Air slicing itself is pinned by the air tests (BaseAirBulkheadTests).
        server.AirTickBudgetMsForTest = 60_000;
        server.Start();
        return (server, transport);
    }

    /// <summary>The real join path as a current client (packs and cutscenes).</summary>
    private static PlayerSession Join(SvGameServer server, int conn, string name)
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 },
        }));
        return server.Sessions[conn];
    }

    /// <summary>A local player past the basic training ("I know the game"), so every course is available;
    /// <paramref name="cutscenes"/> makes it a client that plays them.</summary>
    private static PlayerSession LocalPastTraining(SvGameServer server, string name, bool cutscenes = false)
    {
        var p = server.AddLocalPlayer(name);
        if (cutscenes)
        {
            p.ClientFeatures.Add(ClientFeatures.Cutscenes1);
        }

        server.TickStoryPacksForTest(p);
        server.SkipTutorialForTest(p);
        Assert.Contains(StoryPackKeys.Done(T), p.State.Milestones);
        p.Story.StoryPackPlaySeconds += 90; // #2498: the skip suggests the flight courses silently — an offer; the next waits the gap
        p.State.Milestones.Add(StoryPackKeys.ChapterOffered(T, "c15")); // #2482: these players stand together — "Together" has its own tests
        return p;
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    private static void Start(SvGameServer server, PlayerSession p, string chapter)
        => Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.StartChapter, ChapterId = chapter });

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private static bool Done(PlayerSession p, string step) => Has(p, StoryPackKeys.StepDone(T, step));

    private static bool Skipped(PlayerSession p, string step) => Has(p, StoryPackKeys.StepSkipped(T, step));

    private static bool Active(PlayerSession p, string step) => p.State.StepProgress.ContainsKey(StoryPackKeys.ProgressKey(T, step));

    private static void Give(PlayerSession p, string item, int count) => p.State.Inventory.Add(item, count, 999);

    private StoryDefinition Pack => _content.Stories[T];

    private StoryChapter Chapter(string id) => Pack.Chapters.Single(c => c.Id == id);

    private StoryStep StepDef(string id) => Pack.Chapters.SelectMany(c => c.Steps).Single(s => s.Id == id);

    private CutsceneDefinition Card(string id) => Pack.FindCutscene(id)!;

    private static NetStoryChapter ChapterState(SvGameServer server, PlayerSession p, string chapter)
        => server.StoryPackStatesForTest(p).Packs.Single(x => x.Id == T).Chapters.Single(c => c.Id == chapter);

    /// <summary>The client is in the world, and the 90 s cooldown ran out — queued cards go out.</summary>
    private static void Cooldown(SvGameServer server, PlayerSession p)
    {
        server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
        server.TickCutscenesForTest(p);
    }

    /// <summary>A world with a wildcard listener (so the "only while a step listens" paths run).</summary>
    private (SvGameServer Server, RecordingGameEventListener Events) Verbs(string world, Action<ServerConfig>? configure = null)
    {
        var (server, _) = World(world, configure);
        var events = new RecordingGameEventListener();
        server.AddGameEventListener("*", events);
        return (server, events);
    }

    private static int SurfaceTopY(SvGameServer server, int x, int z)
    {
        for (int y = 220; y > -200; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 0;
    }

    private static int MaxTopY(SvGameServer server, int cx, int cz, int r)
    {
        int max = int.MinValue;
        for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            {
                max = Math.Max(max, SurfaceTopY(server, cx + dx, cz + dz));
            }

        return max;
    }

    // ---------------- The courses ----------------

    [Fact]
    public void TheBuildingCourses_HaveTheirPrerequisitesMomentsCardsAndAbsorbedHints()
    {
        Assert.Equal(new[] { "chapter:tutorial:b4" }, Chapter("c1").Requires);
        Assert.Equal(new[] { "any:[world:tier_gate,all:[step:tutorial:c6.3,not:blueprint:ship_builder]]" }, Chapter("c1").OfferWhen);
        Assert.Equal(new[] { "chapter:tutorial:b5" }, Chapter("c6").Requires);
        Assert.Equal(new[] { "any:[world:module_affordable,world:hull_damaged]" }, Chapter("c6").OfferWhen);
        Assert.Equal(new[] { "chapter:tutorial:b4" }, Chapter("c7").Requires);
        Assert.Equal(new[] { "any:[world:base_core_ready,all:[world:night_outside,not:world:base_here]]" }, Chapter("c7").OfferWhen);
        Assert.Equal(new[] { "chapter:tutorial:c3", "world:free_flight" }, Chapter("c17").Requires); // the pilot course's spacewalk first
        Assert.Equal(new[] { "any:[world:station_core_ready,chapter:tutorial:c6]" }, Chapter("c17").OfferWhen);
        Assert.Contains("c1", Pack.Graduation.Suggest);
        foreach (string id in new[] { "c1", "c6", "c7", "c17" })
        {
            var c = Chapter(id);
            Assert.True(c.Optional);
            Assert.Equal(("tut.course", "tut.done"), (c.OpenCutscene, c.CloseCutscene));
            Assert.All(c.Steps, s => Assert.StartsWith(id + ".", s.Id));
        }

        // Titanium plates are a hard prerequisite from the blueprint on (synthesis §3.2): the course waits there, and says why.
        Assert.Equal(new[] { "any:[chapter:tutorial:c1,blueprint:ship_builder]" }, StepDef("c6.5").Requires);
        Assert.Equal(StoryStepKinds.Read, StepDef("c6.4").Kind);
        // The research course uses the terrain scanner once the titanium route is known (step 18's stand-in otherwise).
        Assert.Equal(new[] { "any:[blueprint:terrain_scanner,chapter:tutorial:c1]" }, StepDef("c2.6").FeasibleIf);

        // One teaching voice: the steps that teach a hint hold it.
        Assert.Equal(new[] { "tier_gate" }, StepDef("c1.1").HintRefs);
        Assert.Equal(new[] { "hull_low" }, StepDef("c6.2").HintRefs);
        Assert.Equal(new[] { "base_walls", "base_airless" }, StepDef("c7.2").HintRefs); // the step right after the founding
        Assert.Contains("base_airless", StepDef("c7.5").HintRefs);
        Assert.Contains("base_walls", StepDef("c7.6").HintRefs);

        // The lights step names light blocks (and the campfire) only.
        foreach (string arg in StepDef("c7.3").Objective.Arg.Split('|'))
        {
            var block = _content.GetBlock(arg);
            Assert.NotNull(block);
            Assert.True(block!.Category == "light" || arg == "campfire", arg);
        }

        // The two firsts (synthesis §6.4): staged, size-aware, a singleplayer world held — and ≤ 12 s.
        Assert.Equal("tut.first_base", StepDef("c7.1").OnComplete.Cutscene);
        Assert.Equal("tut.ship_commissioned", StepDef("c6.7").OnComplete.Cutscene);
        foreach (string id in new[] { "tut.first_base", "tut.ship_commissioned" })
        {
            var card = Card(id);
            Assert.Equal(CutsceneTiers.Scene, card.Tier);
            Assert.True(card.TakesCamera, id);
            Assert.True(card.EffectiveHoldWorld, id);
            Assert.False(card.Options.CooldownExempt, id); // it waits for a safe moment
            Assert.True(card.Shots.Sum(s => s.Duration) <= 12f, id);
            Assert.Contains(id, StoryCutsceneIds.Tutorial);
        }

        Assert.All(Card("tut.ship_commissioned").Shots, s => Assert.Equal(CutsceneAnchors.Ship, s.Camera.Anchor)); // the NEW ship, scaled
        Assert.Empty(StoryPackValidator.Validate(Pack, StoryPackValidationTests.Context()).Where(i => i.Severity == StoryPackIssueSeverity.Error));
    }

    // ---------------- Offers ----------------

    [Fact]
    public void TheToolTierWall_OffersTheWorkshopCourse_ButNeverDuringTheBasicTraining()
    {
        var (server, transport) = World("offer_c1");
        var titanium = _content.GetBlock("titanium_ore")!.NumericId;

        // During the training a refused swing is the basic training's own reaction (b3.3) — no course is offered.
        var rookie = Join(server, 1, "Rookie");
        rookie.State.AboardShip = false;
        var under = new Vector3i((int)Math.Floor(rookie.State.Position.X), (int)Math.Floor(rookie.State.Position.Y) - 1, (int)Math.Floor(rookie.State.Position.Z));
        server.World.SetBlock(under, titanium);
        server.MineBlockOnce(rookie.State.PlayerId, under.X, under.Y, under.Z); // the starter drill cannot open titanium ore
        Assert.True(server.WorldPredicateForTest(rookie, StoryWorldPredicates.TierGate));
        server.TickStoryPacksForTest(rookie, 5);
        Assert.Empty(transport.Offers(rookie));

        // Past the training: the refusal is the workshop course's moment.
        var miner = LocalPastTraining(server, "Miner");
        miner.State.AboardShip = false;
        Assert.False(server.WorldPredicateForTest(miner, StoryWorldPredicates.TierGate));
        var rock = new Vector3i(under.X + 3, under.Y, under.Z);
        server.World.SetBlock(rock, titanium);
        miner.State.Position = new Vector3f(rock.X + 0.5f, rock.Y + 1f, rock.Z + 0.5f);
        server.MineBlockOnce(miner.State.PlayerId, rock.X, rock.Y, rock.Z);
        Assert.True(server.WorldPredicateForTest(miner, StoryWorldPredicates.TierGate));
        server.TickStoryPacksForTest(miner);
        var offer = Assert.Single(transport.Offers(miner));
        Assert.Equal("Workshop and titanium", offer.LineArg);
        Assert.Equal(StoryChapterStates.Offered, ChapterState(server, miner, "c1").State);

        // The moment passes: a minute later the refusal no longer counts.
        server.AdvanceUptimeForTest(61);
        Assert.False(server.WorldPredicateForTest(miner, StoryWorldPredicates.TierGate));
    }

    [Fact]
    public void TheOtherBuildingCourses_AreOfferedAtTheirMoments()
    {
        var (server, transport) = World("offer_moments");

        // C6: a damaged hull (it never heals by itself).
        var captain = LocalPastTraining(server, "Captain");
        Assert.False(server.WorldPredicateForTest(captain, StoryWorldPredicates.HullDamaged));
        Assert.False(server.WorldPredicateForTest(captain, StoryWorldPredicates.ModuleAffordable)); // the free starter modules are fitted
        server.SetShipHullForTest(captain.State.PlayerId, 30f);
        Assert.True(server.WorldPredicateForTest(captain, StoryWorldPredicates.HullDamaged));
        server.TickStoryPacksForTest(captain);
        Assert.Equal("Ship builder", Assert.Single(transport.Offers(captain)).LineArg);

        // C6's other moment: a module whose blueprint and materials the player has.
        var engineer = LocalPastTraining(server, "Engineer");
        engineer.State.UnlockedBlueprints.Add("cargo_expansion_1");
        Assert.False(server.WorldPredicateForTest(engineer, StoryWorldPredicates.ModuleAffordable));
        foreach (var cost in _content.GetShipModule("cargo_hold_1")!.BuildCost)
        {
            Give(engineer, cost.Item, cost.Count);
        }

        Assert.True(server.WorldPredicateForTest(engineer, StoryWorldPredicates.ModuleAffordable));

        // C7: a base core carried …
        var settler = LocalPastTraining(server, "Settler");
        Assert.False(server.WorldPredicateForTest(settler, StoryWorldPredicates.BaseCoreReady));
        Give(settler, "base_core", 1);
        server.TickStoryPacksForTest(settler);
        Assert.Equal("Homestead", Assert.Single(transport.Offers(settler)).LineArg);

        // C17: the pilot course through and a station core carried.
        var builder = LocalPastTraining(server, "Orbiter");
        Give(builder, "station_core", 1);
        Assert.True(server.WorldPredicateForTest(builder, StoryWorldPredicates.StationCoreReady));
        server.TickStoryPacksForTest(builder);
        Assert.Empty(transport.Offers(builder)); // locked: the pilot course first
        builder.State.Milestones.Add(StoryPackKeys.ChapterDone(T, "c3"));
        server.TickStoryPacksForTest(builder);
        Assert.Equal("Navigator", Assert.Single(transport.Offers(builder)).LineArg); // the pilot course's own follow-up first …
        server.TickStoryPacksForTest(builder, 91);
        Assert.Equal("Station builder", transport.Offers(builder).Last().LineArg); // … the station course after the gap
        Assert.Equal(2, transport.Offers(builder).Count);

        // C7's night moment: out on foot after dark with no base on this world (the others are aboard their ships).
        var camper = LocalPastTraining(server, "Camper");
        camper.State.AboardShip = false;
        server.SetLocalDayFractionForTest(0.5, camper.State.Position.X); // midday
        Assert.False(server.WorldPredicateForTest(camper, StoryWorldPredicates.NightOutside));
        server.TickStoryPacksForTest(camper);
        Assert.Empty(transport.Offers(camper));
        server.SetLocalDayFractionForTest(0.95, camper.State.Position.X);
        Assert.True(server.WorldPredicateForTest(camper, StoryWorldPredicates.NightOutside));
        server.TickStoryPacksForTest(camper);
        Assert.Equal("Homestead", Assert.Single(transport.Offers(camper)).LineArg);
        camper.State.AboardShip = true;
        Assert.False(server.WorldPredicateForTest(camper, StoryWorldPredicates.NightOutside)); // aboard is not "outside"
    }

    // ---------------- The new verbs, raised by their real handlers ----------------

    [Fact]
    public void TheShipVerbs_RepairEditKeelAndSwitch_AreReportedFromTheirHandlers()
    {
        var (server, events) = Verbs("verb_ship", c =>
        {
            c.Seed = 7;
            c.PlaceStarterShip = true;
            c.World.TerrainGeneration = 0;
        });
        var p = server.AddLocalPlayer("Pilot");
        string id = p.State.PlayerId;
        var (_, max) = server.ShipHullForTest(id);

        server.SetShipHullForTest(id, max - 20f);
        Give(p, "iron_plate", 5);
        server.RepairShipForTest(id, new RepairShipIntent { Mode = "all" });
        Assert.Equal(GameEventKeys.ShipRepairAll, Assert.Single(events.Of(GameEventKeys.ShipRepaired)).Arg);
        server.RepairShipForTest(id, new RepairShipIntent { Mode = "all" }); // intact: nothing repaired, nothing raised
        Assert.Single(events.Of(GameEventKeys.ShipRepaired));

        var hole = server.CarveFirstShipCellForTest(id);
        Give(p, hole.Item, 1);
        server.RepairShipForTest(id, new RepairShipIntent { Mode = "cell", X = hole.X, Y = hole.Y, Z = hole.Z });
        Assert.Contains(events.Of(GameEventKeys.ShipRepaired), e => e.Arg == GameEventKeys.ShipRepairCell);

        // Building into the parked ship's cabin (the landed-ship edit).
        p.State.InstantBuild = true;
        GrowParkedShip(server, p);
        Assert.Equal("iron_wall", Assert.Single(events.Of(GameEventKeys.ShipEdited)).Arg);

        // A keel, a commissioned hull, a switch back to the starter.
        var keel = LayKeel(server, p);
        var keelEvent = Assert.Single(events.Of(GameEventKeys.ShipKeelPlaced));
        Assert.StartsWith("built_custom_", keelEvent.Arg);
        CommissionAt(server, p, keel);
        Assert.Single(events.Of(GameEventKeys.ShipCommissioned));
        Deliver(server, p, new SwitchShipIntent { ShipId = "nowhere" }); // not a ship of theirs: refused, nothing raised
        Assert.Empty(events.Of(GameEventKeys.ShipSwitched));
        Deliver(server, p, new SwitchShipIntent { ShipId = "default" });
        Assert.Equal("default", Assert.Single(events.Of(GameEventKeys.ShipSwitched)).Arg);
    }

    [Fact]
    public void TheBaseVerbs_CountOwnAndAlliedBasesOnly_AndADoorWhenItOpens()
    {
        var (server, events) = Verbs("verb_base");
        var (owner, padY) = OnPad(server, "Owner");
        var core = new Vector3i(Cx, padY + 1, Cz);
        Give(owner, "base_core", 1);
        server.PlaceBlock(owner.State.PlayerId, core.X, core.Y, core.Z, "base_core");
        Assert.Single(server.BaseSnapshots);
        Assert.Empty(events.Of(GameEventKeys.BaseBuilt)); // the core itself founds the base, it is not "built at home"
        Assert.True(server.WorldPredicateForTest(owner, StoryWorldPredicates.BaseHere));

        Give(owner, "stone", 4);
        server.PlaceBlock(owner.State.PlayerId, Cx + 3, padY + 1, Cz + 4, "stone");
        var built = Assert.Single(events.Of(GameEventKeys.BaseBuilt));
        Assert.Equal(("Owner", "stone"), (built.Player, built.Arg));

        // An ally (or a crewmate — AreAllied answers both) builds at home there too; a stranger neither counts nor builds.
        var friend = OnFoot(server, "Friend", padY);
        var stranger = OnFoot(server, "Stranger", padY);
        Assert.False(server.WorldPredicateForTest(friend, StoryWorldPredicates.BaseHere));
        server.RequestAllianceForTest("Friend", "Owner");
        server.RespondAllianceForTest("Owner", "Friend", true);
        Assert.True(server.AreAllied("Owner", "Friend"));
        Assert.True(server.WorldPredicateForTest(friend, StoryWorldPredicates.BaseHere));
        Assert.False(server.WorldPredicateForTest(stranger, StoryWorldPredicates.BaseHere));
        Give(friend, "stone", 1);
        server.PlaceBlock("Friend", Cx + 2, padY + 1, Cz + 4, "stone");
        Assert.Contains(events.Of(GameEventKeys.BaseBuilt), e => e.Player == "Friend");
        Give(stranger, "stone", 1);
        server.PlaceBlock("Stranger", Cx + 1, padY + 1, Cz + 4, "stone"); // the zone is protected
        Assert.DoesNotContain(events.Of(GameEventKeys.BaseBuilt), e => e.Player == "Stranger");

        // A wooden door: opening it is the lesson, closing it raises nothing.
        Give(owner, "door_wood", 1);
        server.PlaceBlock("Owner", Cx + 5, padY + 1, Cz + 1, "door_wood");
        var door = server.DoorSnapshots.Single(d => d.Kind == "wood");
        owner.State.Position = new Vector3f(Cx + 4.5f, padY + 1, Cz + 2.5f);
        server.InteractDoorForTest(owner, door.Id);
        Assert.Equal("wood", Assert.Single(events.Of(GameEventKeys.DoorOpened)).Arg);
        server.InteractDoorForTest(owner, door.Id);
        Assert.Single(events.Of(GameEventKeys.DoorOpened));

        // A bed as the wake-up spot.
        PlaceBedAndSleep(server, owner, padY);
        Assert.Equal(GameEventKeys.SpawnBed, Assert.Single(events.Of(GameEventKeys.SpawnSet)).Arg);
    }

    [Fact]
    public void TheStationVerbs_TheCoreAndZeroGBuilding_AreReportedFromTheirHandlers()
    {
        var (server, events) = Verbs("verb_station");
        var p = server.AddLocalPlayer("Orbiter");
        string stationId = BuildStation(server, p);
        Assert.Equal(stationId, Assert.Single(events.Of(GameEventKeys.StationCoreDeployed)).Arg);
        Assert.Equal(stationId, Assert.Single(events.Of(GameEventKeys.StationCommissioned)).Arg);

        BoardStation(server, p, stationId);
        server.SetStationZeroGForTest("Orbiter", true);
        server.SetStationZeroGForTest("Orbiter", true); // nothing to flip
        server.SetStationZeroGForTest("Orbiter", false);
        Assert.Equal(new[] { "on", "off" }, events.Of(GameEventKeys.StationZeroG).Select(e => e.Arg));
    }

    // ---------------- C1 Workshop and titanium ----------------

    [Fact]
    public void TheWorkshopCourse_WalksThroughTheRealHandlers_ConsoleForgeAndATitaniumPlate()
    {
        var (server, transport) = World("c1_walk");
        var p = LocalPastTraining(server, "Smith", cutscenes: true);
        string id = p.State.PlayerId;
        Start(server, p, "c1");
        Assert.Contains("story.tutorial.c1.wall.start", transport.LineKeys(p));
        Assert.True(Active(p, "c1.1"));
        Assert.True(server.HintHeldByTutorialForTest(p, "tier_gate")); // the course explains the wall — the tip waits
        Assert.Equal("c1.2", server.ChipStepForTest(p, T));

        // Iron ore for titanium ore at the ship's trade console (aboard), twice.
        Assert.True(p.State.AboardShip);
        Give(p, "iron_ore", 10);
        server.Craft(id, "market_iron_to_titanium");
        Assert.False(Done(p, "c1.2"));
        server.Craft(id, "market_iron_to_titanium");
        Assert.True(Done(p, "c1.2"));
        Assert.Equal(2, p.State.Inventory.CountOf("titanium_ore"));

        // The forge at the ship's workshop — no blueprint.
        Assert.Null(_content.GetRecipe("forge")!.RequiredBlueprint); // E7: the forge stays free
        Give(p, "stone", 6);
        Give(p, "iron_plate", 4);
        server.Craft(id, "forge");
        Assert.True(Done(p, "c1.3"));

        // Placed on the world, the forge smelts the plate.
        p.State.AboardShip = false;
        int x = (int)Math.Floor(p.State.Position.X), z = (int)Math.Floor(p.State.Position.Z);
        int ground = SurfaceTopY(server, x + 2, z);
        p.State.Position = new Vector3f(x + 0.5f, ground + 1f, z + 0.5f);
        server.PlaceBlock(id, x + 2, ground + 1, z, "forge");
        Assert.Equal("forge", _content.BlockById(server.World.GetBlock(new Vector3i(x + 2, ground + 1, z)))!.Key);
        server.Craft(id, "titanium_plate");
        Assert.Equal(1, p.State.Inventory.CountOf("titanium_plate"));
        Assert.True(Done(p, "c1.4"));

        // The closing read step, then the course is through: its banner at once (the course card, still waiting behind the
        // training's welcome, is dropped — its course is over).
        Assert.Contains("story.tutorial.c1.next.start", transport.LineKeys(p));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c1")));
        Assert.True(Has(p, StoryPackKeys.CutsceneSent(T, "tut.course@c1")));
        Assert.Contains("tut.done@c1", transport.Plays(p));
    }

    [Fact]
    public void TheWorkshopCourse_InCreative_SaysCraftingIsFree_AndCountsAForgeCraftedLongAgo()
    {
        var (server, transport) = World("c1_creative", c => c.Rules.GameMode = GameMode.Creative);
        var p = LocalPastTraining(server, "Builder");
        p.State.AchievementCounters[AchievementCounters.Craft("forge")] = 1;
        Start(server, p, "c1");
        Assert.Contains("story.tutorial.c1.wall.start_free", transport.LineKeys(p));
        Assert.DoesNotContain("story.tutorial.c1.wall.start", transport.LineKeys(p));

        server.Craft(p.State.PlayerId, "market_iron_to_titanium", 2);
        Assert.True(Done(p, "c1.2"));
        Assert.True(Done(p, "c1.3")); // evidence: the forge crafted before, done silently
        Assert.DoesNotContain("story.tutorial.c1.forge.start", transport.LineKeys(p));
        Assert.Equal("c1.4", server.ChipStepForTest(p, T));
    }

    // ---------------- C6 Ship builder ----------------

    [Fact]
    public void TheShipBuilderCourse_WalksThroughTheRealHandlers_ModuleRepairGrowthKeelCommissionAndSwitch()
    {
        var (server, transport) = World("c6_walk", c =>
        {
            c.Seed = 7;
            c.PlaceStarterShip = true;
            c.World.TerrainGeneration = 0;
        });
        var p = LocalPastTraining(server, "Pilot", cutscenes: true);
        string id = p.State.PlayerId;
        var (_, max) = server.ShipHullForTest(id);
        server.SetShipHullForTest(id, max * 0.3f); // a dented hull: the repair lesson has something to do
        Start(server, p, "c6");
        Assert.Equal("c6.1", server.ChipStepForTest(p, T));
        Assert.Contains("story.tutorial.c6.module.start", transport.LineKeys(p));

        // A module at the ship's workshop.
        p.State.UnlockedBlueprints.Add("cargo_expansion_1");
        foreach (var cost in _content.GetShipModule("cargo_hold_1")!.BuildCost)
        {
            Give(p, cost.Item, cost.Count);
        }

        Assert.True(server.BuildModuleForTest(id, "cargo_hold_1"));
        Assert.True(Done(p, "c6.1"));

        // The repair at the console — VEGA's own low-hull tip waits while the step teaches it.
        Assert.Equal("c6.2", server.ChipStepForTest(p, T));
        Assert.True(server.HintHeldByTutorialForTest(p, "hull_low"));
        Give(p, "iron_plate", 20);
        Assert.True(server.RepairShipForTest(id, new RepairShipIntent { Mode = "all" }));
        Assert.True(Done(p, "c6.2"));
        Assert.False(server.WorldPredicateForTest(p, StoryWorldPredicates.HullDamaged));
        Assert.False(Has(p, StoryPackKeys.StepDone(T, "c6.2a"))); // the stand-in never ran

        // Growing the parked ship.
        p.State.InstantBuild = true;
        GrowParkedShip(server, p);
        Assert.True(Done(p, "c6.3"));

        // No titanium route yet: the course says so and waits at the blueprint …
        Assert.Contains("story.tutorial.c6.titanium.start", transport.LineKeys(p));
        Assert.False(Active(p, "c6.5"));
        server.TickStoryPacksForTest(p, 30);
        Assert.False(Active(p, "c6.5"));
        Assert.Equal(StoryChapterStates.Active, ChapterState(server, p, "c6").State);

        // … until the workshop course is through.
        p.State.Milestones.Add(StoryPackKeys.ChapterDone(T, "c1"));
        server.TickStoryPacksForTest(p);
        Assert.Equal("c6.5", server.ChipStepForTest(p, T));
        p.State.KnowledgePoints = 40;
        foreach (var cost in _content.Blueprints["ship_builder"].UnlockCost)
        {
            Give(p, cost.Item, cost.Count);
        }

        p.State.AboardShip = true;
        p.State.Position = server.StationPosition("cockpit")!.Value; // research happens at the cockpit
        server.UnlockBlueprint(id, "ship_builder");
        Assert.True(Done(p, "c6.5"));

        // A keel, the hull around it, commissioned at the helm — the commissioning card.
        var keel = LayKeel(server, p);
        Assert.True(Done(p, "c6.6"));
        CommissionAt(server, p, keel);
        Assert.True(Done(p, "c6.7"));
        Assert.True(Has(p, StoryPackKeys.CutsceneSent(T, "tut.ship_commissioned")));
        Cooldown(server, p); // the course card first …
        Assert.Contains("tut.course@c6", transport.Plays(p));
        Cooldown(server, p); // … the commissioning scene after the usual gap
        Assert.Contains("tut.ship_commissioned", transport.Plays(p));

        // The fleet: back to the starter.
        Assert.Equal("c6.8", server.ChipStepForTest(p, T));
        Deliver(server, p, new SwitchShipIntent { ShipId = "default" });
        Assert.True(Done(p, "c6.8"));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c6")));
        Assert.Contains("tut.done@c6", transport.Plays(p)); // a banner never waits
        Assert.Equal(1, transport.Plays(p).Count(c => c == "tut.ship_commissioned")); // once per player
    }

    [Fact]
    public void TheShipBuilderCourse_ExplainsWhatTheWorldCannotShow_AndCountsWhatThePlayerHasBuilt()
    {
        var (server, transport) = World("c6_rules", c => c.CreativeUnlockAllBlueprints = true);
        var p = LocalPastTraining(server, "Veteran", cutscenes: true);
        p.State.Milestones.Add(StoryPackKeys.ChapterDone(T, "c1"));
        // A self-built ship commissioned long ago.
        p.Ships["built_custom_old"] = new ShipState { ShipType = ShipState.CustomShipType, Commissioned = true };
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.KeelLaid));
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.ShipBuilt));

        Start(server, p, "c6");
        server.RaiseGameEventForTest(p, GameEventKeys.ModuleBuilt, "cargo_hold_1");
        Assert.True(Done(p, "c6.1"));

        // An undamaged hull: the stand-in explains the repair, the course goes on.
        Assert.True(Skipped(p, "c6.2"));
        Assert.Contains("story.tutorial.c6.repair.start_later", transport.LineKeys(p));
        server.RaiseGameEventForTest(p, GameEventKeys.ShipEdited, "iron_wall");
        Assert.True(Done(p, "c6.3"));

        // The titanium route known: no waiting line; the blueprint (handed out here), the keel and the commissioned ship are
        // evidence — done silently, so the commissioning card is not shown for a ship built long ago.
        Assert.True(Skipped(p, "c6.4"));
        Assert.DoesNotContain("story.tutorial.c6.titanium.start", transport.LineKeys(p));
        foreach (string known in new[] { "c6.5", "c6.6", "c6.7" })
        {
            Assert.True(Done(p, known), known);
        }

        Assert.False(Has(p, StoryPackKeys.CutsceneSent(T, "tut.ship_commissioned")));
        Assert.Equal("c6.8", server.ChipStepForTest(p, T));
    }

    // ---------------- C7 Homestead ----------------

    [Fact]
    public void TheHomesteadCourse_WalksThroughTheRealHandlers_BaseLightsDoorSealedRoomWallRingAndBed()
    {
        var (server, transport) = World("c7_walk"); // rocky: an airless world with the oxygen rule on
        var p = LocalPastTraining(server, "Settler", cutscenes: true);
        string id = p.State.PlayerId;
        int padY = BuildPad(server);
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(Cx + 3.5f, padY + 1, Cz + 2.5f);
        Start(server, p, "c7");
        Assert.Equal("c7.1", server.ChipStepForTest(p, T));
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.Airless));

        // The base core founds the base: the base card, and the founding hints count as told by the course (no VEGA hint).
        Give(p, "base_core", 1);
        server.PlaceBlock(id, Cx, padY + 1, Cz, "base_core");
        Assert.True(Done(p, "c7.1"));
        Assert.True(Has(p, StoryPackKeys.CutsceneSent(T, "tut.first_base")));
        Assert.DoesNotContain("vega.hint.base_walls", transport.LineKeys(p));
        Assert.DoesNotContain("vega.hint.base_airless", transport.LineKeys(p));
        Assert.True(Has(p, "vega:hint:base_walls"));
        Assert.Contains("story.tutorial.c7.zone.start", transport.LineKeys(p));

        // 10 blocks in the zone.
        Give(p, "stone", 10);
        for (int dx = -4; dx <= 5; dx++)
        {
            server.PlaceBlock(id, Cx + dx, padY + 1, Cz + 4, "stone");
        }

        Assert.True(Done(p, "c7.2"));

        // 2 lights.
        Give(p, "torch", 2);
        server.PlaceBlock(id, Cx + 4, padY + 1, Cz - 1, "torch");
        server.PlaceBlock(id, Cx + 5, padY + 1, Cz - 1, "torch");
        Assert.True(Done(p, "c7.3"));

        // A wooden door, opened.
        Give(p, "door_wood", 1);
        server.PlaceBlock(id, Cx + 5, padY + 1, Cz + 1, "door_wood");
        var door = server.DoorSnapshots.Single(d => d.Kind == "wood");
        server.InteractDoorForTest(p, door.Id);
        Assert.True(Done(p, "c7.4"));

        // A sealed room around the core (stone walls and roof on the stone pad).
        Assert.Equal("c7.5", server.ChipStepForTest(p, T));
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
                for (int dy = 1; dy <= 4; dy++)
                {
                    bool shell = Math.Abs(dx) == 2 || Math.Abs(dz) == 2 || dy == 4;
                    if (shell)
                    {
                        server.World.SetBlock(new Vector3i(Cx + dx, padY + dy, Cz + dz), stone);
                    }
                }

        server.AdvanceUptimeForTest(2); // past the air fill's recompute interval (the founding computed the empty cube)
        server.TutorialStateEventsForTest(p);
        Assert.True(Done(p, "c7.5"));
        Assert.False(Has(p, StoryPackKeys.StepDone(T, "c7.5a")));

        // A closed wall ring, 2 high, with the player inside.
        Assert.Equal("c7.6", server.ChipStepForTest(p, T));
        for (int dx = -Ring; dx <= Ring; dx++)
            for (int dz = -Ring; dz <= Ring; dz++)
            {
                if (Math.Abs(dx) == Ring || Math.Abs(dz) == Ring)
                {
                    server.World.SetBlock(new Vector3i(Cx + dx, padY + 1, Cz + dz), stone);
                    server.World.SetBlock(new Vector3i(Cx + dx, padY + 2, Cz + dz), stone);
                }
            }

        server.AdvanceUptimeForTest(10); // past the wall fill's cache
        server.TutorialStateEventsForTest(p);
        Assert.True(Done(p, "c7.6"));

        // A bed as the wake-up spot.
        PlaceBedAndSleep(server, p, padY);
        Assert.True(Done(p, "c7.7"));

        // Visitors are off in this world: the residents' line is skipped, the course is through.
        Assert.True(Skipped(p, "c7.8"));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c7")));
        Cooldown(server, p);
        Assert.Contains("tut.first_base", transport.Plays(p));
        Assert.Contains("tut.done@c7", transport.Plays(p));
        Assert.Equal(1, transport.Plays(p).Count(c => c == "tut.first_base"));
    }

    [Fact]
    public void TheHomesteadCourse_CountsAnAlliesBase_AndExplainsAirOnlyWhereItMatters()
    {
        // A breathable world: an ally's base on it counts as the player's home (E32); the sealed room is only explained.
        var (server, transport) = World("c7_ally", c => c.StartPlanet = "jungle");
        int padY = BuildPad(server);
        var owner = OnFoot(server, "Owner", padY);
        Give(owner, "base_core", 1);
        server.PlaceBlock("Owner", Cx, padY + 1, Cz, "base_core");
        Assert.Single(server.BaseSnapshots);

        var friend = LocalPastTraining(server, "Friend", cutscenes: true);
        friend.State.AboardShip = false;
        friend.State.Position = new Vector3f(Cx + 3.5f, padY + 1, Cz + 2.5f);
        server.RequestAllianceForTest("Friend", "Owner");
        server.RespondAllianceForTest("Owner", "Friend", true);
        Assert.False(server.WorldPredicateForTest(friend, StoryWorldPredicates.Airless));

        Start(server, friend, "c7");
        Assert.True(Done(friend, "c7.1")); // the ally's base on this world: done silently …
        Assert.False(Has(friend, StoryPackKeys.CutsceneSent(T, "tut.first_base"))); // … so no "base founded" card
        Give(friend, "stone", 10);
        for (int dx = -4; dx <= 5; dx++)
        {
            server.PlaceBlock("Friend", Cx + dx, padY + 1, Cz + 4, "stone"); // building at the ally's home counts
        }

        Assert.True(Done(friend, "c7.2"));
        server.RaiseGameEventForTest(friend, GameEventKeys.BaseBuilt, "torch", 2);
        server.RaiseGameEventForTest(friend, GameEventKeys.DoorOpened, "wood");
        Assert.True(Skipped(friend, "c7.5")); // breathable air: nothing to seal …
        Assert.Contains("story.tutorial.c7.air.start_later", transport.LineKeys(friend)); // … only explained
        Assert.Equal("c7.6", server.ChipStepForTest(friend, T));

        // No oxygen rule at all: neither the sealed room nor its explanation.
        var (airless, airlessTransport) = World("c7_no_oxygen", c => c.Rules.OxygenConsumption = OxygenConsumption.Off);
        var q = LocalPastTraining(airless, "Settler");
        Start(airless, q, "c7");
        foreach (var (key, arg, n) in new[] { (GameEventKeys.BaseFounded, "", 1), (GameEventKeys.BaseBuilt, "stone", 10), (GameEventKeys.BaseBuilt, "torch", 2), (GameEventKeys.DoorOpened, "wood", 1) })
        {
            airless.RaiseGameEventForTest(q, key, arg, n);
        }

        Assert.True(Skipped(q, "c7.5"));
        Assert.True(Skipped(q, "c7.5a"));
        Assert.DoesNotContain(airlessTransport.LineKeys(q), k => k.StartsWith("story.tutorial.c7.air.", StringComparison.Ordinal));
        Assert.Equal("c7.6", airless.ChipStepForTest(q, T));
    }

    /// <summary>#2488: the wall-ring lesson asks for a fenced-in YARD — the base wall model alone (one outside-in fill for
    /// every yard, courtyard and room, unchanged for the animals) also counts the room the player just sealed in c7.5 and a
    /// closed cave near the base. The lesson therefore also wants open sky over the player's head.</summary>
    [Fact]
    public void TheWallRingLesson_NeedsOpenSky_TheSealedRoomOrAClosedCaveIsNoYard()
    {
        var (server, _) = World("c7_sky"); // rocky: an airless world with the oxygen rule on
        var p = LocalPastTraining(server, "Settler");
        string id = p.State.PlayerId;
        int padY = BuildPad(server);
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(Cx + 1.5f, padY + 1, Cz + 1.5f); // where the sealed room will stand
        Start(server, p, "c7");
        Give(p, "base_core", 1);
        server.PlaceBlock(id, Cx, padY + 1, Cz, "base_core");
        Assert.True(Done(p, "c7.1"));
        foreach (var (key, arg, n) in new[] { (GameEventKeys.BaseBuilt, "stone", 10), (GameEventKeys.BaseBuilt, "torch", 2), (GameEventKeys.DoorOpened, "wood", 1) })
        {
            server.RaiseGameEventForTest(p, key, arg, n);
        }

        Assert.Equal("c7.5", server.ChipStepForTest(p, T));

        // The sealed room of c7.5, with the player inside: the room holds air — and is no yard.
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
                for (int dy = 1; dy <= 4; dy++)
                {
                    if (Math.Abs(dx) == 2 || Math.Abs(dz) == 2 || dy == 4)
                    {
                        server.World.SetBlock(new Vector3i(Cx + dx, padY + dy, Cz + dz), stone);
                    }
                }

        server.AdvanceUptimeForTest(2);
        server.TutorialStateEventsForTest(p);
        Assert.True(Done(p, "c7.5"));
        Assert.Equal("c7.6", server.ChipStepForTest(p, T));
        Assert.True(server.InWalledBaseAreaForTest(Cx + 1, padY + 1, Cz + 1)); // the animals' wall model: enclosed, as before
        server.AdvanceUptimeForTest(10);
        server.TutorialStateEventsForTest(p);
        Assert.False(Done(p, "c7.6"), "the sealed room completed the wall-ring lesson");

        // A closed cave under the pad, inside the base's reach: no yard either.
        int caveX = Cx + 3, caveZ = Cz + 3;
        int caveFeet = SurfaceTopY(server, caveX, caveZ) - 5;
        for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
                for (int dy = -1; dy <= 3; dy++)
                {
                    bool hollow = Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1 && dy is 0 or 1;
                    server.World.SetBlock(new Vector3i(caveX + dx, caveFeet + dy, caveZ + dz), hollow ? BlockId.Air : stone);
                }

        p.State.Position = new Vector3f(caveX + 0.5f, caveFeet, caveZ + 0.5f);
        server.AdvanceUptimeForTest(10);
        Assert.True(server.InWalledBaseAreaForTest(caveX, caveFeet, caveZ)); // enclosed for the animals
        server.TutorialStateEventsForTest(p);
        Assert.False(Done(p, "c7.6"), "a closed cave completed the wall-ring lesson");

        // A roofless wall ring, the player in the yard under the open sky: the lesson is done.
        for (int dx = -Ring; dx <= Ring; dx++)
            for (int dz = -Ring; dz <= Ring; dz++)
            {
                if (Math.Abs(dx) == Ring || Math.Abs(dz) == Ring)
                {
                    server.World.SetBlock(new Vector3i(Cx + dx, padY + 1, Cz + dz), stone);
                    server.World.SetBlock(new Vector3i(Cx + dx, padY + 2, Cz + dz), stone);
                }
            }

        p.State.Position = new Vector3f(Cx + 3.5f, padY + 1, Cz + 2.5f);
        server.AdvanceUptimeForTest(10);
        server.TutorialStateEventsForTest(p);
        Assert.True(Done(p, "c7.6"));
    }

    // ---------------- C17 Station builder ----------------

    [Fact]
    public void TheStationBuilderCourse_WalksThroughTheRealHandlers_CoreHullAndZeroGBuilding()
    {
        var (server, transport) = World("c17_walk");
        var p = LocalPastTraining(server, "Orbiter");
        p.State.Milestones.Add(StoryPackKeys.ChapterDone(T, "c3")); // the pilot course (its spacewalk) is through
        Start(server, p, "c17");
        Assert.Equal("c17.1", server.ChipStepForTest(p, T));
        Assert.Contains("story.tutorial.c17.core.start", transport.LineKeys(p));

        string stationId = BuildStation(server, p, afterCore: () => Assert.True(Done(p, "c17.1")));
        Assert.True(Done(p, "c17.2"));
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.StationOwned));

        BoardStation(server, p, stationId);
        server.SetStationZeroGForTest(p.State.PlayerId, true);
        Assert.True(Done(p, "c17.3"));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c17")));
    }

    [Fact]
    public void TheStationBuilderCourse_NeedsThePilotCourseAndFreeFlight()
    {
        var (server, _) = World("c17_locked");
        var p = LocalPastTraining(server, "Walker");
        var locked = ChapterState(server, p, "c17");
        Assert.Equal(StoryChapterStates.Locked, locked.State);
        Assert.Equal(new[] { "story.tutorial.ch.c3" }, locked.RequiresKeys); // "needs: Pilot"
        Start(server, p, "c17");
        Assert.False(Has(p, StoryPackKeys.ChapterActive(T, "c17")));

        var (grounded, _) = World("c17_noflight", c => c.Rules.FreeSpaceFlight = false);
        var q = LocalPastTraining(grounded, "Walker");
        q.State.Milestones.Add(StoryPackKeys.ChapterDone(T, "c3"));
        Assert.Equal(StoryChapterStates.Locked, ChapterState(grounded, q, "c17").State);
    }

    // ---------------- C2 meets C1 ----------------

    [Fact]
    public void TheResearchCourse_TeachesTheTerrainScanner_OnceTheWorkshopCourseIsThrough()
    {
        var (server, transport) = World("c2_after_c1");
        var p = LocalPastTraining(server, "Prospector");
        p.State.Milestones.Add(StoryPackKeys.ChapterDone(T, "c1"));
        Start(server, p, "c2");
        foreach (var (key, arg) in new[] { (GameEventKeys.ScanAny, "block"), (GameEventKeys.MineAny, "data_cache"), (GameEventKeys.ResearchAny, "paint_tool"), (GameEventKeys.MinigamePlayed, "blockfall"), (GameEventKeys.ScanAny, "monument") })
        {
            server.RaiseGameEventForTest(p, key, arg, 3);
        }

        // No terrain-scanner blueprint, but the titanium route is known: the real step, with the research variant line.
        Assert.Equal("c2.6", server.ChipStepForTest(p, T));
        Assert.False(Skipped(p, "c2.6"));
        Assert.Contains("story.tutorial.c2.scanner.start_research", transport.LineKeys(p));
        Assert.DoesNotContain("story.tutorial.c2.scanner.start_later", transport.LineKeys(p));
    }

    // ---------------- Fixtures ----------------

    private const int Cx = 40, Cz = 40, Ring = 6;

    /// <summary>A 21×21 stone pad floating above every natural feature at (<see cref="Cx"/>, <see cref="Cz"/>) — the walled
    /// yard's fixture (BaseWalledYardTests). Returns the pad's floor Y (feet level = padY + 1).</summary>
    private int BuildPad(SvGameServer server)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        int padY = MaxTopY(server, Cx, Cz, 12) + 8;
        for (int dx = -10; dx <= 10; dx++)
            for (int dz = -10; dz <= 10; dz++)
            {
                server.World.SetBlock(new Vector3i(Cx + dx, padY, Cz + dz), stone);
            }

        return padY;
    }

    private (PlayerSession Player, int PadY) OnPad(SvGameServer server, string name)
    {
        int padY = BuildPad(server);
        return (OnFoot(server, name, padY), padY);
    }

    private static PlayerSession OnFoot(SvGameServer server, string name, int padY)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(Cx + 3.5f, padY + 1, Cz + 2.5f);
        return p;
    }

    /// <summary>Places a bed on the pad and makes it the player's wake-up spot (E at the bed).</summary>
    private static void PlaceBedAndSleep(SvGameServer server, PlayerSession p, int padY)
    {
        string id = p.State.PlayerId;
        p.State.Position = new Vector3f(Cx + 3.5f, padY + 1, Cz - 2.5f);
        p.State.Inventory.Add("bed", 1, 99);
        server.PlaceBlock(id, Cx + 4, padY + 1, Cz - 4, "bed");
        server.SetSpawnPoint(id, Cx + 4, padY + 1, Cz - 4);
    }

    /// <summary>Builds one wall block into the parked ship's cabin, next to the rear hatch (DoorwayTests' cabin cell).</summary>
    private static void GrowParkedShip(SvGameServer server, PlayerSession p)
    {
        string id = p.State.PlayerId;
        var hatch = server.BuildShipStructureForTest(id).DoorCells.Single(c => c.Z == 0);
        var (origin, _) = server.LandedShipBoundsForTest(id);
        p.State.Position = new Vector3f(origin.X + hatch.X + 0.5f, origin.Y + 1f, origin.Z + 1.5f);
        server.HandleStructureEditForTest(id, new StructureEditIntent { StructureId = "ship:" + id, X = hatch.X, Y = hatch.Y, Z = hatch.Z + 2, ItemKey = "iron_wall" });
    }

    /// <summary>Lays a keel 30 blocks off the parked ship (CustomShipTests' recipe) and returns its world cell; the player stands
    /// beside it with instant build.</summary>
    private static Vector3i LayKeel(SvGameServer server, PlayerSession p)
    {
        string id = p.State.PlayerId;
        var (origin, _) = server.LandedShipBoundsForTest(id);
        int x = origin.X + 30, z = origin.Z + 30;
        int sy = SurfaceTopY(server, x, z);
        var core = new Vector3i(x, sy + 1, z);
        p.State.InstantBuild = true;
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(core.X + 2.5f, core.Y + 1.5f, core.Z + 2.5f);
        server.PlaceShipCoreForTest(id, core.X, core.Y, core.Z);
        return core;
    }

    /// <summary>Builds CustomShipTests' valid 5×4×5 hull around the keel (floor, a wall ring with a slide door, a roof, a helm
    /// and an engine inside) and commissions it at the helm.</summary>
    private static void CommissionAt(SvGameServer server, PlayerSession p, Vector3i keel)
    {
        string id = p.State.PlayerId;
        string yard = "shipyard:" + id;
        void Edit(int x, int y, int z, string item)
            => server.HandleStructureEditForTest(id, new StructureEditIntent { StructureId = yard, X = x, Y = y, Z = z, Mine = false, ItemKey = item });

        for (int x = 0; x < 5; x++)
            for (int z = 0; z < 5; z++)
            {
                if (x != 0 || z != 0)
                {
                    Edit(x, 0, z, "iron_wall");
                }
            }

        for (int y = 1; y <= 2; y++)
            for (int x = 0; x < 5; x++)
                for (int z = 0; z < 5; z++)
                {
                    bool ring = x == 0 || x == 4 || z == 0 || z == 4;
                    if (ring && !(x == 1 && y == 1 && z == 0))
                    {
                        Edit(x, y, z, "iron_wall");
                    }
                }

        Edit(1, 1, 0, "door_slide");
        for (int x = 0; x < 5; x++)
            for (int z = 0; z < 5; z++)
            {
                Edit(x, 3, z, "iron_wall");
            }

        Edit(2, 1, 2, "ship_helm");
        Edit(1, 1, 1, "ship_engine");
        p.State.Position = new Vector3f(keel.X + 2.5f, keel.Y + 1f, keel.Z + 2.5f);
        server.CommissionShipForTest(id);
        Assert.Contains(p.Ships.Values, s => s.IsCustom && s.Commissioned);
    }

    /// <summary>Deploys a station core on a spacewalk and builds StationGravityTests' sealed 5×5×5 iron shell with a slide door
    /// around it (instant build) — commissioned. Returns the station id.</summary>
    private static string BuildStation(SvGameServer server, PlayerSession p, Action? afterCore = null)
    {
        string id = p.State.PlayerId;
        server.EnterSpace(id);
        p.State.InEva = true;
        p.State.InstantBuild = true;
        server.DeployStationCoreForTest(id);
        afterCore?.Invoke();
        string station = server.OwnedStationIdForTest(id)!;
        for (int x = -2; x <= 2; x++)
            for (int y = -2; y <= 2; y++)
                for (int z = -2; z <= 2; z++)
                {
                    bool shell = Math.Abs(x) == 2 || Math.Abs(y) == 2 || Math.Abs(z) == 2;
                    if (shell && !(x == 2 && y == -1 && z == 0))
                    {
                        server.HandleStructureEditForTest(id, new StructureEditIntent { StructureId = station, X = x, Y = y, Z = z, Mine = false, ItemKey = "iron_wall" });
                    }
                }

        server.HandleStructureEditForTest(id, new StructureEditIntent { StructureId = station, X = 2, Y = -1, Z = 0, Mine = false, ItemKey = "door_slide" });
        Assert.True(server.StationIsBoardableForTest(station));
        return station;
    }

    private static void BoardStation(SvGameServer server, PlayerSession p, string stationId)
    {
        string id = p.State.PlayerId;
        var contact = server.SpaceEntitiesFor(id).First(e => e.Id == stationId);
        server.ShipMove(id, contact.Position.X, contact.Position.Y, contact.Position.Z - 6f);
        server.BoardStation(id, stationId);
        Assert.True(server.InStation(id));
    }
}
