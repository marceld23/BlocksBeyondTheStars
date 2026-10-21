// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2459 — the gap closure before the story-packs PR: a cutscene's <c>custom</c> anchor is saved with the promise and frames
/// the same place when the scene is re-offered at the join (on that world only); the join catch-up queues the world story's
/// scenes first (the cold open before the tutorial's welcome card); the suit fighting the climate, a board job and a meal
/// for a companion are reported by their real handlers (the meal finishes the animal friends course, the jetpack and the
/// speeder the "on the move" course); "the tip becomes the offer" — at a deep dive's moment VEGA offers the course instead of
/// saying its tip first or right after (the tool-tier wall, a data cache in reach, the comm radio's Crystal Net line), while
/// a player without that offer still hears the tip; and a replayed course passes a research step whose blueprint is known.
/// </summary>
public sealed class StoryGapClosureTests : IDisposable
{
    private const string T = SvGameServer.OfficialTutorialPackId;
    private const string C = StoryPackFixtures.Cutscenes;
    private const string V = StoryRegistry.DefaultStoryId;

    /// <summary>VEGA's context tips that could take the tip cadence slot in these tests — retired except the one under test.</summary>
    private static readonly string[] NoisyTips =
    {
        "o2", "energy", "hunger", "cold", "heat", "medkit", "hull_low", "lamp_off", "lamp_missing", "torch_underground", "crystal_night_light", "eat_now", "wrong_tool", "tier_gate", "scanner_idle",
        "scanner_unknown", "speeder_far", "ship_far", "rare_ore_near", "needed_ore_near", "data_cache_near", "craftable_now",
        "blueprint_affordable", "settlement_near", "ruin_near", "factory_near", "treasure_near", "trader_near", "tameable_near",
        "fragment_signal", "player_near", "asteroid_near", "asteroid_no_tool", "station_near",
    };

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public StoryGapClosureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_gap_closure_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A world without a world story (unless <paramref name="configure"/> sets one) and without a starter ship, its
    /// settlements, wrecks and factories — research is open everywhere, nothing else talks.</summary>
    private (SvGameServer Server, RecordingTransport Transport) World(string world, Action<ServerConfig>? configure = null, GameContent? content = null)
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
            PlaceFactories = false,
        };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.StoryId = StoryRegistry.NoneStoryId;
        configure?.Invoke(config);
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var server = new SvGameServer(config, content ?? _content, transport, repo);
        server.Start();
        return (server, transport);
    }

    /// <summary>The real join path of a current client (packs and cutscenes).</summary>
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

    /// <summary>A local player past the basic training ("I know the game"), on foot. Several of them stand together, so the
    /// "Together" course (a player nearby) is marked offered — it has its own tests.</summary>
    private static PlayerSession LocalPastTraining(SvGameServer server, string name)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        server.TickStoryPacksForTest(p);
        server.SkipTutorialForTest(p);
        Assert.Contains(StoryPackKeys.Done(T), p.State.Milestones);
        p.StoryPackPlaySeconds += 90; // #2498: the skip suggests the flight courses silently — an offer; the next waits the gap
        p.State.Milestones.Add(StoryPackKeys.ChapterOffered(T, "c15"));
        return p;
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    private static void Start(SvGameServer server, PlayerSession p, string chapter)
        => Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.StartChapter, ChapterId = chapter });

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private static bool Done(PlayerSession p, string step) => Has(p, StoryPackKeys.StepDone(T, step));

    private static void Give(PlayerSession p, string item, int count) => p.State.Inventory.Add(item, count, 999);

    private static void Ticks(SvGameServer server, int ticks, double dt = 0.1)
    {
        for (int i = 0; i < ticks; i++)
        {
            server.TickForTest(dt);
        }
    }

    private void Fill(SvGameServer server, string block, int x0, int x1, int y0, int y1, int z0, int z1)
    {
        var id = block == "air" ? BlocksBeyondTheStars.Shared.Primitives.BlockId.Air : _content.GetBlock(block)!.NumericId;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), id);
                }
    }

    /// <summary>A stone slab a dozen blocks above the terrain (no ore or crystal within the tip probe, still well inside the
    /// air), the player on it, and every context tip but <paramref name="keep"/> retired — so the one under test owns the
    /// tip cadence.</summary>
    private Vector3i StandAlone(SvGameServer server, PlayerSession p, int x, string keep)
    {
        const int z = 0;
        int top = int.MinValue;
        for (int dx = -4; dx <= 4; dx += 2)
            for (int dz = -4; dz <= 4; dz += 2)
            {
                for (int scan = 220; scan > -200; scan--)
                {
                    if (!server.World.GetBlock(new Vector3i(x + dx, scan, z + dz)).IsAir)
                    {
                        top = Math.Max(top, scan);
                        break;
                    }
                }
            }

        int y = top + 12;
        Fill(server, "stone", x - 3, x + 3, y, y, z - 3, z + 3);
        Fill(server, "air", x - 3, x + 3, y + 1, y + 4, z - 3, z + 3);
        p.State.Position = new Vector3f(x + 0.5f, y + 1f, z + 0.5f);
        p.State.AboardShip = false;
        foreach (string tip in NoisyTips.Where(t => t != keep))
        {
            server.VegaTipLearnedForTest(p.State.PlayerId, tip);
        }

        server.SkipVegaTipCooldownsForTest(p.State.PlayerId); // past the join's quiet minute
        return new Vector3i(x, y, z);
    }

    // ---------------- A cutscene's custom anchor is saved with the promise ----------------

    [Fact]
    public void ACustomAnchor_IsSavedWithThePromise_AndFramesThePlaceAgainAtTheJoin_OnThatWorldOnly()
    {
        var (server, transport) = World("anchor", content: StoryPackFixtures.ContentWith(C));
        var p = Join(server, 1, "Pilot");
        server.AdvanceUptimeForTest(SvGameServer.CutsceneJoinSettleSeconds + SvGameServer.CutsceneCooldownSeconds);
        server.TickCutscenesForTest(p);
        server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
        server.TickCutscenesForTest(p);

        var spot = new Vector3f(12.5f, 70f, -3.5f);
        Assert.True(server.PlayCutsceneForTest(p, C, "anchored", "x", spot));
        var stored = Assert.Single(p.State.CutsceneAnchors); // nothing for the scenes without an anchor
        Assert.Equal(C + "/anchored", stored.Key);
        Assert.Equal((p.CurrentLocationId, 12.5f, 70f, -3.5f), (stored.Value.LocationId, stored.Value.X, stored.Value.Y, stored.Value.Z));

        // Gone before the receipt: the save carries the place, the join offers the scene again with it.
        server.DisconnectLocalPlayerForTest("Pilot");
        transport.Sent.Clear();
        var back = Join(server, 2, "Pilot");
        Assert.Equal(spot, back.CutsceneQueue.Single(q => q.Id == "anchored").Anchor);
        server.AdvanceUptimeForTest(SvGameServer.CutsceneJoinSettleSeconds);
        for (int i = 0; i < 6 && !transport.Plays(back).Contains("anchored"); i++)
        {
            server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
            server.TickCutscenesForTest(back);
        }

        var play = transport.To<CutscenePlay>(back).Single(c => c.CutsceneId == "anchored");
        Assert.True(play.HasAnchor);
        Assert.Equal((12.5f, 70f, -3.5f), (play.AnchorX, play.AnchorY, play.AnchorZ));

        // Elsewhere the place means nothing — the scene comes without it (the client frames the player).
        string home = back.CurrentLocationId;
        back.CutsceneQueue.Clear();
        back.CurrentLocationId = "elsewhere";
        server.CutscenesOnJoinForTest(back);
        Assert.Null(back.CutsceneQueue.Single(q => q.Id == "anchored").Anchor);
        back.CurrentLocationId = home;

        // The receipt forgets it.
        Deliver(server, back, new CutsceneSeenIntent { PackId = C, Ids = new[] { "anchored" } });
        Assert.Empty(back.State.CutsceneAnchors);
    }

    [Fact]
    public void TheAnchors_SurviveASave_AnOlderSaveHasNone_AndAnEmptyMapDoesNotGrowIt()
    {
        var state = new PlayerState { PlayerId = "p", Name = "P" };
        state.CutsceneAnchors["pack/scene@c1"] = new CutsceneAnchor { LocationId = "sys1-p1", X = 1.5f, Y = 2f, Z = -3f };
        string json = JsonSerializer.Serialize(StateMapper.ToSnapshot(state));
        var back = StateMapper.PlayerFromJson(json, "p", new JsonSerializerOptions());
        var at = back.CutsceneAnchors["pack/scene@c1"];
        Assert.Equal(("sys1-p1", 1.5f, 2f, -3f), (at.LocationId, at.X, at.Y, at.Z));

        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        Assert.True(node.Remove("CutsceneAnchors"));
        Assert.Empty(StateMapper.PlayerFromJson(node.ToJsonString(), "p", new JsonSerializerOptions()).CutsceneAnchors);

        Assert.Null(StateMapper.ToSnapshot(new PlayerState { PlayerId = "q", Name = "Q" }).CutsceneAnchors);
    }

    // ---------------- The join catch-up's order ----------------

    [Fact]
    public void TheJoinCatchUp_QueuesTheWorldStoryFirst_NotInTheSavesOrder()
    {
        var (server, _) = World("join_order", c => c.Rules.StoryId = V);
        var p = server.AddLocalPlayer("Late");
        p.ClientFeatures.Add(ClientFeatures.Cutscenes1);

        // The tutorial's promise first in the save: the old catch-up queued the welcome card ahead of the cold open, which
        // then waited out the cooldown behind it (the prologue chains into the welcome, not the other way round).
        var rest = p.State.Milestones.Where(m => !m.Contains(":cut:", StringComparison.Ordinal)).ToList();
        p.State.Milestones = new HashSet<string>(
            new[] { StoryPackKeys.CutsceneSent(T, "tut.welcome"), StoryPackKeys.CutsceneSent(V, StoryCutsceneIds.Prologue) }.Concat(rest));
        p.CutsceneQueue.Clear();

        server.CutscenesOnJoinForTest(p);
        Assert.Equal(new[] { StoryCutsceneIds.Prologue, "tut.welcome" }, p.CutsceneQueue.Select(q => q.Id));
        Assert.Equal(new[] { V, T }, p.CutsceneQueue.Select(q => q.PackId));
    }

    // ---------------- Events from their real handlers ----------------

    [Fact]
    public void TheSuitStartingToFightTheCold_IsReportedOnce_AndAgainWhenItStartsAgain()
    {
        var (server, _) = World("climate", c =>
        {
            c.StartPlanet = "ice";
            c.Seed = 77;
        });
        var events = new RecordingGameEventListener();
        server.AddGameEventListener("*", events);
        var p = server.AddLocalPlayer("Frosty");
        p.State.Position = new Vector3f(500f, 150f, 500f); // on foot in the open, high up: the cold at its cap
        p.State.AboardShip = false;

        Ticks(server, 30);
        Assert.True(p.State.SuitClimateActive);
        var first = Assert.Single(events.Of(GameEventKeys.SuitClimate));
        Assert.Equal(("Frosty", "cold"), (first.Player, first.Arg));

        Ticks(server, 30);
        Assert.Single(events.Of(GameEventKeys.SuitClimate)); // the moment it starts, not every tick it runs

        // The climate off (a Creative override) and on again: a new start, a new report.
        p.State.ModeOverride = PlayerModeOverride.Creative;
        Ticks(server, 3);
        Assert.False(p.State.SuitClimateActive);
        p.State.ModeOverride = PlayerModeOverride.None;
        Ticks(server, 30);
        Assert.Equal(2, events.Of(GameEventKeys.SuitClimate).Count());
    }

    [Fact]
    public void ABoardJob_IsReportedWithItsId_AStarterJobIsNot()
    {
        var (server, _) = World("board_job", c => c.World.SpaceStations = Frequency.Frequent);
        var events = new RecordingGameEventListener();
        server.AddGameEventListener("*", events);
        var p = LocalPastTraining(server, "Trader");
        string id = p.State.PlayerId;

        server.AcceptMission(id, "first_iron"); // a starter job, open anywhere from minute one (#2460, B58)
        Assert.Contains(events.Of(GameEventKeys.MissionAccepted), e => e.Arg == "first_iron");
        Assert.Empty(events.Of(GameEventKeys.MissionBoardAccepted));

        p.State.AboardShip = true; // a launch starts aboard
        server.EnterSpace(id);
        var station = server.SpaceEntitiesFor(id).First(e => e.Kind == CombatEntityKind.SpaceStation);
        server.ShipMove(id, station.Position.X, station.Position.Y, station.Position.Z - 8f);
        server.BoardStation(id, station.Id);
        string job = server.StationMissionIds.First();
        p.State.Position = server.SpaceStationMarkers.First(m => m.Type == "mission_board").Pos;
        server.AcceptMission(id, job);

        var board = Assert.Single(events.Of(GameEventKeys.MissionBoardAccepted));
        Assert.Equal((id, job), (board.Player, board.Arg));
    }

    [Fact]
    public void AMealForTheCompanion_FinishesTheAnimalFriendsCourse_NotOnlyAName()
    {
        var (server, _) = World("cared_fed");
        var events = new RecordingGameEventListener();
        server.AddGameEventListener("*", events);
        var p = LocalPastTraining(server, "Keeper");
        var species = server.SpeciesRoster.First();
        var pet = new TamedCreature
        {
            Id = "pet_1",
            HomeBodyId = server.ActiveLocationId,
            Name = "Flöckchen",
            SpeciesId = species.Id,
            Species = species,
            SizeScale = 1f,
            Bond = 45,
        };
        p.State.TamedCreatures.Add(pet);
        server.SpawnCompanionsForTest(p.State.PlayerId);
        server.CompanionEntitiesForTest(p.State.PlayerId).Single().Position = p.State.Position; // fed at arm's length (#1295)

        p.State.Milestones.Add(StoryPackKeys.StepDone(T, "c9.1"));
        p.State.Milestones.Add(StoryPackKeys.StepDone(T, "c9.2"));
        Start(server, p, "c9");
        Assert.True(Done(p, "c9.3")); // a companion at the player's side: taming is known
        Assert.Equal("c9.4", server.ChipStepForTest(p, T));

        Give(p, "forage_bait", 1);
        server.FeedCompanionForTest(p.State.PlayerId, pet.Id);
        Assert.Contains(events.Of(GameEventKeys.CompanionCared), e => e.Player == p.State.PlayerId && e.Arg == GameEventKeys.CompanionFedArg);
        Assert.True(Done(p, "c9.4"));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c9")));
    }

    [Fact]
    public void TheOnTheMoveCourse_TakesTheJetpackAndTheSpeeder_Too()
    {
        var (server, _) = World("jetpack_speeder");
        var p = LocalPastTraining(server, "Racer");
        p.State.UnlockedBlueprints.Add("jetpack"); // the airborne step's feasibility; no rope gun, no train here
        p.State.Milestones.Add(StoryPackKeys.StepDone(T, "c13.1"));
        Start(server, p, "c13");
        Assert.Equal("c13.3", server.ChipStepForTest(p, T));

        TestGear.Wear(p.State, "jetpack");
        p.State.SuitEnergy = 100f;
        Deliver(server, p, new SetJetpackIntent { Active = true });
        Assert.True(Done(p, "c13.3"));
        Assert.Equal("c13.4", server.ChipStepForTest(p, T));

        // A hover speeder deployed on dry stone and boarded (vehicle:boarded speeder).
        Fill(server, "stone", -12, 40, 61, 63, -12, 12);
        Fill(server, "air", -12, 40, 64, 68, -12, 12);
        p.State.Position = new Vector3f(0.5f, 64f, 0.5f);
        p.State.Inventory.Add("speeder", 1, 1);
        string speeder = server.DeploySpeederForTest(p.State.PlayerId);
        Assert.NotEqual(string.Empty, speeder);
        p.State.Position = server.SpeederSnapshots.Single().Pos;
        server.EnterSpeederForTest(p.State.PlayerId, speeder);
        Assert.True(Done(p, "c13.4"));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c13"))); // no train, beam blocks read: the course is through
    }

    // ---------------- The tip becomes the offer ----------------

    [Fact]
    public void TheToolTierWall_IsToldByTheWorkshopCourseOffer_NotByATipRightAfterIt()
    {
        var (server, transport) = World("tip_offer_wall");
        server.SetDayFractionForTest(0.5);
        var titanium = _content.GetBlock("titanium_ore")!.NumericId;

        // Two players hit the wall: one through the workshop course already, one who never took it.
        var veteran = LocalPastTraining(server, "Veteran");
        veteran.State.Milestones.Add(StoryPackKeys.ChapterDone(T, "c1"));
        var rookie = LocalPastTraining(server, "Rookie");
        foreach (var (who, x) in new[] { (veteran, 40), (rookie, -40) })
        {
            var at = StandAlone(server, who, x, keep: "tier_gate");
            server.World.SetBlock(at, titanium);
            server.MineBlockOnce(who.State.PlayerId, at.X, at.Y, at.Z); // the starter drill cannot open titanium ore
            Assert.True(server.WorldPredicateForTest(who, StoryWorldPredicates.TierGate));
        }

        Ticks(server, 4, 1.1);

        // Without a course to offer, VEGA explains the wall as before.
        Assert.Contains("vega.hint.tier_gate", transport.LineKeys(veteran));
        Assert.DoesNotContain("Workshop and titanium", transport.OfferArgs(veteran));

        // With one, the offer is the telling: no tip after it, and the tip counts as told (it does not come later either).
        Assert.Equal("Workshop and titanium", Assert.Single(transport.OfferArgs(rookie)));
        Assert.DoesNotContain("vega.hint.tier_gate", transport.LineKeys(rookie));
        var tip = server.VegaTipStateForTest(rookie.State.PlayerId, "tier_gate");
        Assert.Equal(1, tip.Count);
        Assert.True(tip.CooldownUntil > tip.Uptime + 300, "the tip's own cooldown started with the offer");
        Assert.True(tip.ReadyAt > tip.Uptime, "the offer took the tips' cadence slot");
    }

    [Fact]
    public void ADataCacheInReach_IsToldByTheResearchCourseOffer_AndThePlayerWhoTookItHearsTheTip()
    {
        var dataCache = _content.GetBlock("data_cache")!.NumericId;
        string[] Run(string world, bool courseThrough)
        {
            var (server, transport) = World(world);
            server.SetDayFractionForTest(0.5);
            var p = LocalPastTraining(server, "Digger");
            if (courseThrough)
            {
                p.State.Milestones.Add(StoryPackKeys.ChapterDone(T, "c2"));
            }

            var at = StandAlone(server, p, 0, keep: "data_cache_near");
            server.World.SetBlock(new Vector3i(at.X + 2, at.Y + 1, at.Z), dataCache);
            Ticks(server, 8, 1.1);
            Assert.Equal(courseThrough ? 0 : 1, transport.OfferArgs(p).Count(o => o == "Knowledge and research"));
            return transport.LineKeys(p).ToArray();
        }

        // The course's moment: the offer, never the tip a few seconds later.
        var offered = Run("tip_offer_cache", courseThrough: false);
        Assert.DoesNotContain("vega.hint.data_cache_near", offered);

        // The course through: VEGA's tip as before (the scene really is a tip moment).
        var told = Run("tip_offer_cache_done", courseThrough: true);
        Assert.Contains("vega.hint.data_cache_near", told);
    }

    [Fact]
    public void TheCommRadiosCrystalNetLine_BecomesTheCrystalWorkshopOffer()
    {
        var (server, transport) = World("tip_offer_radio");
        server.SetDayFractionForTest(0.5);
        var newcomer = LocalPastTraining(server, "Newcomer");
        var later = LocalPastTraining(server, "Later");
        later.State.Milestones.Add(StoryPackKeys.ChapterDeclined(T, "c8")); // put off: nothing to offer, the line stays
        server.TickStoryPacksForTest(newcomer);
        Assert.Empty(transport.OfferArgs(newcomer)); // no other course is due — the radio is the moment
        foreach (var p in new[] { newcomer, later })
        {
            p.State.KnowledgePoints = 100;
            foreach (var cost in _content.Blueprints["comm_radio"].UnlockCost)
            {
                Give(p, cost.Item, cost.Count);
            }

            server.UnlockBlueprint(p.State.PlayerId, "comm_radio"); // the real research
            Assert.Contains("comm_radio", p.State.UnlockedBlueprints);
        }

        Assert.Contains("vega.hint.crystal_researchable", transport.LineKeys(later));

        Assert.DoesNotContain("vega.hint.crystal_researchable", transport.LineKeys(newcomer));
        Assert.Contains("vega:hint:crystal_researchable", newcomer.State.Milestones); // told by the offer
        server.TickStoryPacksForTest(newcomer);
        Assert.Equal("Crystal workshop", Assert.Single(transport.OfferArgs(newcomer)));
        Assert.Contains("crystal_researchable", Pack.Chapters.Single(c => c.Id == "c8").Steps.Single(s => s.Id == "c8.0").HintRefs);
    }

    [Fact]
    public void ADueOffer_AbsorbsOnlyTheHintsItsCourseTeaches()
    {
        var (server, _) = World("absorb_rule");
        server.SetDayFractionForTest(0.5);
        var p = LocalPastTraining(server, "Scout");
        Assert.False(server.OfferAbsorbsHintForTest(p, "data_cache_near")); // no moment, no offer due

        p.VegaTipLive = new[] { "data_cache_near" }; // the research course's moment
        Assert.True(server.OfferAbsorbsHintForTest(p, "data_cache_near"));
        Assert.True(server.OfferAbsorbsHintForTest(p, "blueprint_affordable")); // the course teaches it too (c2.3)
        Assert.False(server.OfferAbsorbsHintForTest(p, "settlement_near"));     // another course's tip
        Assert.False(server.OfferAbsorbsHintForTest(p, "o2"));                  // no course names it

        p.State.Milestones.Add(StoryPackKeys.ChapterDeclined(T, "c2")); // "Later": no offer due, the tip speaks again
        Assert.False(server.OfferAbsorbsHintForTest(p, "data_cache_near"));
    }

    // ---------------- A replay passes a research step it cannot ask for again ----------------

    [Fact]
    public void AReplayedCourse_PassesAResearchStepWhoseBlueprintIsKnown()
    {
        var (server, _) = World("replay_research");
        var p = LocalPastTraining(server, "Gardener");
        p.State.UnlockedBlueprints.Add("bio_lab");
        Start(server, p, "c10");

        void WalkToTheLab()
        {
            server.RaiseGameEventForTest(p, GameEventKeys.PlantAny, "sapling");
            server.RaiseGameEventForTest(p, GameEventKeys.CraftStation, "campfire");
            server.RaiseGameEventForTest(p, GameEventKeys.SampleTaken, "plant");
        }

        WalkToTheLab();
        Assert.Equal("c10.5", server.ChipStepForTest(p, T)); // the first run: the blueprint is evidence (alreadyDoneIf)
        server.RaiseGameEventForTest(p, GameEventKeys.BioLabUsed, "analyse");
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c10")));

        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.ReplayChapter, ChapterId = "c10" });
        Assert.True(Has(p, StoryPackKeys.ChapterReplay(T, "c10")));
        WalkToTheLab();
        Assert.Equal("c10.5", server.ChipStepForTest(p, T)); // the bio lab cannot be researched twice — the replay moves on
        server.RaiseGameEventForTest(p, GameEventKeys.BioLabUsed, "analyse");
        Assert.False(Has(p, StoryPackKeys.ChapterReplay(T, "c10"))); // the replay is through
    }

    private StoryDefinition Pack => _content.Stories[T];
}
