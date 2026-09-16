// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// NPC professions (Justus' ideas, 2026-09-15): doctor, grocer, arms dealer, sage, animal tamer, blockfarmer, streamer and
/// reporter — the table, their buildings in fresh settlements, and their posts at a base or on a station.
/// </summary>
public sealed class NpcProfessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_professions_" + Guid.NewGuid().ToString("N"));
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void TheTable_IsUnique_AndEveryProfessionHasItsRoomItsBuildingAndItsKeys()
    {
        Assert.Equal(8, NpcProfessions.All.Count);
        Assert.Equal(NpcProfessions.All.Count, NpcProfessions.All.Select(p => p.Job).Distinct().Count());
        Assert.Equal(NpcProfessions.All.Count, NpcProfessions.All.Select(p => p.PostBlock).Distinct().Count());
        Assert.Equal(NpcProfessions.All.Count, NpcProfessions.All.Select(p => p.Function).Distinct().Count());

        foreach (var p in NpcProfessions.All)
        {
            Assert.Contains(p.Role, new[] { "vendor", "settler" });
            Assert.Equal(p.Trades, p.Theme.Length > 0);
            Assert.True(Enum.TryParse<RoomFurnisher.RoomRole>(p.Room, out _), $"{p.Job}: unknown room '{p.Room}'");
            Assert.True(StructureRoles.IsPlotRole(p.Function), $"{p.Job}: '{p.Function}' is not a plot role");
            Assert.Same(p, NpcProfessions.ByMarker(p.Marker));
            Assert.Same(p, NpcProfessions.ByPostBlock(p.PostBlock));
            Assert.Same(p, NpcProfessions.ByFunction(p.Function));
            Assert.Equal(p.Trades, NpcProfessions.IsTradeMarker(p.Marker));
            Assert.True(NpcProfessions.IsStaffedPostMarker(p.Marker));
        }

        // The classic posts keep their meaning; a settler's spot is no post.
        Assert.True(NpcProfessions.IsTradeMarker("vendor"));
        Assert.False(NpcProfessions.IsTradeMarker("mission_board"));
        Assert.True(NpcProfessions.IsStaffedPostMarker("mission_board"));
        Assert.False(NpcProfessions.IsStaffedPostMarker("npc"));
    }

    [Fact]
    public void EveryProfessionBuilding_IsInEveryModularKit_OptionalAndAtMostOnce()
    {
        foreach (var kit in Content.StructureKits.Where(k => k.Key.Contains("_modular_", StringComparison.Ordinal) && k.Kind == StructureKit.KindSettlement))
        {
            string style = kit.Tier is "hamlet" or "village" ? "village" : "town";
            foreach (var p in NpcProfessions.All)
            {
                foreach (string key in new[] { $"{style}_{p.Function}_1", $"{style}_{p.Function}_1_alien" })
                {
                    var entry = Assert.Single(kit.Entries, e => e.Module == key);
                    Assert.False(entry.Required, $"{kit.Key}: {key} must stay optional");
                    Assert.Equal(1, entry.Max);
                }
            }
        }
    }

    [Fact]
    public void AFreshSettlement_WithAProfessionBuilding_StaffsItsPost_WithTheProfessionsNameThemeAndTool()
    {
        for (long seed = 1; seed <= 80; seed++)
        {
            var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "prof_" + seed));
            using (repo)
            {
                var config = new ServerConfig
                {
                    WorldName = "prof_" + seed,
                    Seed = seed,
                    StartPlanet = "jungle",
                    AutoSaveIntervalMinutes = 9999,
                    PlaceStarterShip = false,
                    PlaceSettlements = true,
                    PlaceWrecks = false,
                };
                var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
                server.Start();

                var posts = server.SettlementMarkers.Where(m => NpcProfessions.ByMarker(m.Type) != null).ToList();
                if (posts.Count == 0)
                {
                    continue;
                }

                // One keeper per post (a job may stand in several settlements of the world).
                foreach (var group in posts.GroupBy(m => m.Type))
                {
                    var p = NpcProfessions.ByMarker(group.Key)!;
                    var keepers = server.NpcJobsForTest.Where(n => n.Job == p.Job).ToList();
                    Assert.Equal(group.Count(), keepers.Count);
                    foreach (var npc in keepers)
                    {
                        Assert.Equal(p.Role, npc.Role);
                        Assert.Equal(p.NameKey, npc.NameKey);
                        Assert.Equal(p.Held, npc.Held);
                        if (p.Trades)
                        {
                            Assert.Equal(p.Theme, npc.Theme);
                        }
                    }
                }

                return;
            }
        }

        throw new Xunit.Sdk.XunitException("No fresh settlement with a profession building across 80 seeds.");
    }

    // ---- content: post blocks, offers, dialogues -------------------------------------------------------

    [Fact]
    public void EveryPostBlock_IsABlock_AnItem_ACraftedRecipe_BehindOneBlueprint()
    {
        Assert.NotNull(Content.GetBlueprint("station_profession_posts"));
        foreach (var p in NpcProfessions.All)
        {
            Assert.NotNull(Content.GetBlock(p.PostBlock));
            var item = Content.GetItem(p.PostBlock);
            Assert.NotNull(item);
            Assert.Equal(p.PostBlock, item!.PlacesBlock);
            var recipe = Assert.Single(Content.Recipes.Values, r => r.Outputs.Any(o => o.Item == p.PostBlock));
            Assert.Equal("station_profession_posts", recipe.RequiredBlueprint);
        }

        Assert.NotNull(Content.GetBlock("stretcher"));
        Assert.Contains(Content.Recipes.Values, r => r.Outputs.Any(o => o.Item == "stretcher") && r.Station != CraftingStation.Market);
    }

    [Fact]
    public void EveryTradingProfession_HasItsOwnOffers_AndTheDoctorsBedAndStretcherComeEveryOtherDay()
    {
        foreach (var p in NpcProfessions.All.Where(p => p.Trades))
        {
            var offers = Content.Recipes.Values.Where(r => r.Station == CraftingStation.Market && r.MarketTheme == p.Theme).ToList();
            Assert.True(offers.Count >= 2, $"{p.Job}: {offers.Count} offers");
            Assert.All(offers, r => Assert.All(r.Inputs.Concat(r.Outputs), i => Assert.NotNull(Content.GetItem(i.Item))));
        }

        foreach (string key in new[] { "market_doctor_bed", "market_doctor_stretcher" })
        {
            var r = Content.Recipes[key];
            Assert.Equal(2, r.MarketRotation);
            int offered = Enumerable.Range(0, 20).Count(day => r.OfferedOnDay(day));
            Assert.Equal(10, offered);
            Assert.NotEqual(r.OfferedOnDay(7), r.OfferedOnDay(8)); // every other day, not a random pick
        }

        Assert.True(Content.Recipes["market_doctor_medpack"].OfferedOnDay(3)); // no rotation → always in stock
    }

    [Fact]
    public void EveryProfession_HasItsOwnDialogue_AndNoProfessionTakesTheVendorsFavourChain()
    {
        foreach (var p in NpcProfessions.All)
        {
            var d = Assert.Single(Content.Dialogs, x => x.Job == p.Job);
            Assert.NotEmpty(d.Nodes[0].Choices);
        }

        Assert.Contains(Content.Dialogs.Single(d => d.Job == "streamer").Nodes[0].Choices, c => c.Consequence == "photo");
        Assert.Contains(Content.Dialogs.Single(d => d.Job == "reporter").Nodes[0].Choices, c => c.Consequence == "interview");
        Assert.Contains(Content.Dialogs.Single(d => d.Job == "reporter").Nodes[0].Choices, c => c.Consequence == "news");
    }

    // ---- posts at a base -------------------------------------------------------------------------------

    [Fact]
    public void AClinicPostAtHome_IsStaffedByADoctor_WhoTradesMedicsGoods()
    {
        using var w = new NpcLifeWorld(Content, "prof_clinic");
        int baseId = w.FoundBase();
        var post = new BlocksBeyondTheStars.Shared.Geometry.Vector3i(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4);
        w.Set(post.X, post.Y, post.Z, "clinic_post");
        w.Server.ScanBaseLifeForTest();

        var doctor = Assert.Single(w.Server.BaseResidentsForTest(baseId), r => r.Job == "doctor");
        Assert.Equal("vendor", doctor.Role);
        var npc = Assert.Single(w.Server.NpcJobsForTest, n => n.Job == "doctor");
        Assert.Equal("medics", npc.Theme);
        Assert.Equal("npc.role.doctor", npc.NameKey);
        Assert.Equal("npc_medkit", npc.Held);

        w.Player.State.Position = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(post.X - 1.5f, w.Feet, post.Z + 0.5f);
        Assert.True(w.Server.MarketAvailableForTest(w.Player.State), "barter at the clinic post");
    }

    [Fact]
    public void TheStreamer_AsksOncePerDay_AndYesTellsTheClientToTakeThePhoto()
    {
        using var w = new NpcLifeWorld(Content, "prof_streamer");
        int baseId = w.FoundBase();
        w.Set(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4, "streamer_post");
        w.Server.ScanBaseLifeForTest();
        var streamer = Assert.Single(w.Server.BaseResidentsForTest(baseId), r => r.Job == "streamer");
        var at = w.Server.NpcStateForTest(streamer.NpcId)!.Value.Pos;
        w.Player.State.Position = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(at.X + 1.5f, at.Y, at.Z);

        w.Server.TickProfessionsForTest();
        w.Server.TickProfessionsForTest();
        int asks = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg)
            .OfType<BlocksBeyondTheStars.Networking.Messages.NpcGreeting>().Count(g => g.NpcId == streamer.NpcId);
        Assert.Equal(1, asks); // once per in-game day, not every tick

        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, streamer.NpcId);
        w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 0); // "Sure, let's take a photo!"
        var last = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg)
            .OfType<BlocksBeyondTheStars.Networking.Messages.NpcDialogState>().Last();
        Assert.True(last.End);
        Assert.Equal("photo", last.Action);
        Assert.Equal("npc.activity.posing", w.Server.NpcStateForTest(streamer.NpcId)!.Value.ActivityKey);
    }

    [Fact]
    public void TheReporter_KeepsAnInterview_AsTheBasesNews_AndReadsItBack()
    {
        using var w = new NpcLifeWorld(Content, "prof_reporter");
        int baseId = w.FoundBase();
        w.Set(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4, "press_desk");
        w.Server.ScanBaseLifeForTest();
        var reporter = Assert.Single(w.Server.BaseResidentsForTest(baseId), r => r.Job == "reporter");
        var at = w.Server.NpcStateForTest(reporter.NpcId)!.Value.Pos;
        w.Player.State.Position = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(at.X + 1.5f, at.Y, at.Z);

        // An answer nobody asked for is ignored.
        w.Server.InterviewAnswerForTest(NpcLifeWorld.Owner, reporter.NpcId, "I sneak into the paper");
        Assert.Empty(w.Server.NewsForTest);

        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, reporter.NpcId);
        w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 0); // "Sure — interview me!"
        var asked = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg)
            .OfType<BlocksBeyondTheStars.Networking.Messages.NpcDialogState>().Last();
        Assert.Equal("interview", asked.Action);

        w.Server.InterviewAnswerForTest(NpcLifeWorld.Owner, reporter.NpcId, "I found a cave full of crystals!");
        var article = Assert.Single(w.Server.NewsForTest);
        Assert.Equal("I found a cave full of crystals!", article.Text);
        Assert.Equal(NpcLifeWorld.Owner, article.PlayerName);

        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, reporter.NpcId);
        w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 1); // "What's in the news?"
        var news = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg)
            .OfType<BlocksBeyondTheStars.Networking.Messages.NpcDialogState>().Last();
        Assert.Contains("I found a cave full of crystals!", news.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void InSafeChatMode_TheReporterTakesOnlyTheReadyAnswers()
    {
        using var w = new NpcLifeWorld(Content, "prof_reporter_safe", configure: c => c.Rules.ChatMode = ChatMode.Safe);
        int baseId = w.FoundBase();
        w.Set(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4, "press_desk");
        w.Server.ScanBaseLifeForTest();
        var reporter = Assert.Single(w.Server.BaseResidentsForTest(baseId), r => r.Job == "reporter");
        var at = w.Server.NpcStateForTest(reporter.NpcId)!.Value.Pos;
        w.Player.State.Position = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(at.X + 1.5f, at.Y, at.Z);

        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, reporter.NpcId);
        w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 0);
        w.Server.InterviewAnswerForTest(NpcLifeWorld.Owner, reporter.NpcId, "free text is not for Safe mode");
        Assert.Empty(w.Server.NewsForTest);

        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, reporter.NpcId);
        w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 0);
        w.Server.InterviewAnswerForTest(NpcLifeWorld.Owner, reporter.NpcId, "@news.preset.built");
        Assert.Equal("@news.preset.built", Assert.Single(w.Server.NewsForTest).Text);
    }

    [Fact]
    public void TheTamer_KeepsATameAnimalAtTheirSide_ThatNobodyCanHunt_AndThatGoesWithThem()
    {
        using var w = new NpcLifeWorld(Content, "prof_tamer");
        int baseId = w.FoundBase();
        var post = new BlocksBeyondTheStars.Shared.Geometry.Vector3i(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4);
        w.Set(post.X, post.Y, post.Z, "tamer_post");
        w.Server.ScanBaseLifeForTest();
        var tamer = Assert.Single(w.Server.BaseResidentsForTest(baseId), r => r.Job == "tamer");
        if (!w.Server.SpeciesRoster.Any(sp => sp.Habitat == CreatureHabitat.Land && sp.Temperament is CreatureTemperament.Passive or CreatureTemperament.Skittish))
        {
            return; // this world grew no gentle land animal to tame
        }

        w.Server.TickProfessionsForTest();
        var pet = Assert.Single(w.Server.NpcPetsForTest);
        Assert.Equal(tamer.NpcId, pet.NpcId);
        w.Server.TickProfessionsForTest();
        Assert.Single(w.Server.NpcPetsForTest); // one pet, not one per tick

        w.Server.RemoveBlockForTest(post.X, post.Y, post.Z);
        w.Server.ScanBaseLifeForTest();
        w.Server.TickProfessionsForTest();
        Assert.Empty(w.Server.NpcPetsForTest);
    }

    [Fact]
    public void TheGrocersShop_IsOneClosedRoom_AndTheYardOutsideIsNot()
    {
        using var w = new NpcLifeWorld(Content, "prof_shop");
        w.Hut(NpcLifeWorld.Cx + 10, NpcLifeWorld.Cz, half: 3, doorway: false);
        var inside = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(NpcLifeWorld.Cx + 9.5f, w.Feet, NpcLifeWorld.Cz + 0.5f);
        var alsoInside = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(NpcLifeWorld.Cx + 11.5f, w.Feet, NpcLifeWorld.Cz + 1.5f);
        var outside = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(NpcLifeWorld.Cx + 2.5f, w.Feet, NpcLifeWorld.Cz + 0.5f);
        Assert.True(w.Server.InSameClosedRoomForTest(inside, alsoInside));
        Assert.False(w.Server.InSameClosedRoomForTest(outside, inside));
    }

    // ---------------- stations and the G.D.S. city (2026-09) ----------------

    private static readonly string[] StationJobs = { "doctor", "grocer", "arms_dealer", "sage", "streamer", "reporter" };

    [Fact]
    public void ShippedStationKits_DrawTheProfessionRooms_OnTheHallDeck_EachWithItsKeepersCabin()
    {
        foreach (var kit in Content.StructureKits.Where(k => k.Kind == StructureKit.KindStation))
        {
            int rooms = 0;
            for (long seed = 1; seed <= 24; seed++)
            {
                var s = StationKitComposer.Compose(kit, key => Content.TemplateByKey(StructureKit.KindStation, key), seed, Content, out var composition, out var failure);
                Assert.True(s != null, $"{kit.Key} seed {seed}: {failure}");
                int startDeck = composition!.Modules[0].Y;
                foreach (var m in composition.Modules)
                {
                    var t = Content.TemplateByKey(StructureKit.KindStation, m.Key)!;
                    if (NpcProfessions.ByFunction(t.FunctionOrRole) is not { } p)
                    {
                        continue;
                    }

                    rooms++;
                    Assert.Contains(p.Job, StationJobs); // never the tamer or the blockfarmer in space
                    Assert.Equal(startDeck, m.Y);          // the crew cannot climb: the keeper lives on the hall deck
                    Assert.Single(t.Cells, c => c.Kind == "marker" && c.Id == p.Marker);
                    Assert.Single(t.Cells, c => c.Kind == "marker" && c.Id == "cabin");
                }
            }

            Assert.True(rooms > 0, $"{kit.Key}: no profession room in 24 stations");
        }
    }

    [Fact]
    public void AFreshStation_StaffsItsProfessionRooms_BeforeTheSettlerPosts()
    {
        // Every station kit made to require the clinic and the newsroom — the server staffs both.
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var kits = content.StructureKits.Select(k =>
        {
            var copy = System.Text.Json.JsonSerializer.Deserialize<StructureKit>(System.Text.Json.JsonSerializer.Serialize(k), ContentLoader.JsonOptions)!;
            if (copy.Kind == StructureKit.KindStation)
            {
                foreach (var e in copy.Entries.Where(e => e.Module.EndsWith("_clinic", StringComparison.Ordinal) || e.Module.EndsWith("_newsroom", StringComparison.Ordinal)))
                {
                    e.Required = true;
                    e.Min = 1;
                }
            }

            return copy;
        }).ToList();
        content.SetStructureKits(kits);

        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "prof_station"));
        using (repo)
        {
            var config = new ServerConfig
            {
                WorldName = "prof_station",
                Seed = 42,
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceSettlements = false,
                PlaceWrecks = false,
                World = new BlocksBeyondTheStars.Shared.World.WorldDescription { SpaceStations = BlocksBeyondTheStars.Shared.World.Frequency.Frequent },
            };
            config.Rules.FreeSpaceFlight = true;
            var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
            server.Start();
            string playerId = server.AddLocalPlayer("Crew").State.PlayerId;
            server.EnterSpace(playerId);
            var station = server.SpaceEntitiesFor(playerId).First(e => e.Kind == BlocksBeyondTheStars.GameServer.CombatEntityKind.SpaceStation);
            server.ShipMove(playerId, station.Position.X, station.Position.Y, station.Position.Z - 8f);
            server.BoardStation(playerId, station.Id);

            Assert.StartsWith("station_", server.StationKitForTest(station.Id));
            var jobs = server.NpcJobsForTest;
            var doctor = Assert.Single(jobs, n => n.Job == "doctor");
            Assert.Equal("medics", doctor.Theme);
            Assert.Single(jobs, n => n.Job == "reporter");
            Assert.Contains(jobs, n => n.Job == "quartermaster"); // the services still come first
            Assert.Contains(server.SpaceStationMarkers, m => m.Type == "doctor");
            Assert.Contains(server.SpaceStationMarkers, m => m.Type == "reporter");
        }
    }

    [Fact]
    public void TheGdsCity_GetsItsTwoServicesDistricts_AndTheirSixProfessions()
    {
        var kit = Assert.Single(Content.KitsFor(StructureKit.KindCity, null, null, "gds_desert"));
        var pool = Content.SettlementTemplates.Where(t => t.IsModule).ToList();
        var spec = CityLayoutSpec.FromKit(kit);
        foreach (var services in new[] { "gds_services_1", "gds_services_2" })
        {
            var t = pool.Single(m => m.Key == services);
            Assert.Equal(StructureRoles.CityHousing, t.FunctionOrRole);
        }

        for (long seed = 1; seed <= 6; seed++)
        {
            var composition = new System.Collections.Generic.List<string>();
            var city = CityGenerator.Generate(seed, Content, Array.Empty<CityGenerator.OpenZone>(), null, 0, composition, null, spec, kit, pool);
            Assert.Contains("gds_services_1", composition);
            Assert.Contains("gds_services_2", composition);
            foreach (var job in StationJobs)
            {
                Assert.Single(city.Markers, m => m.Type == job);
            }

            Assert.DoesNotContain(city.Markers, m => m.Type is "tamer" or "blockfarmer"); // no animals and no quarry in the walled city
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
