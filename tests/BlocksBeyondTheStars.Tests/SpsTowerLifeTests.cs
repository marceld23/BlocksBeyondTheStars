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
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3895 / #3896 — the life of a guide tower: the relay console's records without a chip (in their order, one piece of ECHO's
/// call per tower, <c>lore:read</c> with the site kind), the hacking chip that lights the tower once (one chip, every lamp,
/// the record, the event, what the tower still knows on the map), the later player whose own step counts, VEGA's lines by what
/// the player's story knows, the settlers' rumour and flavour tags, the story predicates and the guide marker's three cases.
/// The guard post and the sleeper are in <see cref="SleeperTests"/>.
/// </summary>
public sealed class SpsTowerLifeTests : IDisposable
{
    internal const string V = StoryRegistry.DefaultStoryId;
    internal const string Chip = SvGameServer.SpsTowerChipItem;

    private readonly SpsTowerLifeWorlds _worlds = new("bbts_spstower_life_");

    public void Dispose() => _worlds.Dispose();

    private static ushort Id(string block) => SpsTowerLifeWorlds.Content.GetBlock(block)!.NumericId.Value;

    // ---------------------------------------------------------------- the console without a chip

    [Fact]
    public void TheConsole_WithoutAChip_ShowsItsRecordsInOrder_ThenOnePieceOfEchosCall_AndSaysWhatAChipWouldDo()
    {
        var (server, t) = _worlds.Start("life_console");
        var p = SpsTowerLifeWorlds.Join(server, 1, "Pilot");
        var tower = SpsTowerLifeWorlds.Stamp(server, p, "sps_tower_stump nopost");
        var heard = new SpsTowerLifeWorlds.Listener();
        server.AddGameEventListener(GameEventKeys.LoreRead, heard);
        server.AddGameEventListener(GameEventKeys.SpsTowerLit, heard);

        // The real path once: the use key aimed at the console, in reach.
        Assert.True(server.BlockHasUse(SpsTowerRules.ConsoleBlock));
        var console = tower.Console!.ToVector();
        Assert.Equal(Id(SpsTowerRules.ConsoleBlock), server.World.GetBlock(console).Value);
        p.State.Position = new Vector3f(console.X + 1.5f, console.Y, console.Z + 0.5f);
        server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(new UseBlockIntent { X = console.X, Y = console.Y, Z = console.Z }));
        var first = Assert.Single(t.To<LoreTextRevealed>(p));
        Assert.Equal(("tower_ships", SvGameServer.SpsTowerLoreSite, "lore.site.tower_ships"), (first.Key, first.Site, first.TextKey));
        Assert.Equal(new[] { (GameEventKeys.LoreRead, "sps_tower") }, heard.Events);
        Assert.Contains("@srv.sps_tower.console_locked", t.TextsTo(p));
        Assert.Equal(1, t.LineKeys(p).Count(k => k == "story.vega.tower.read.puzzled"));

        // Every further use: the next record, then this tower's piece of ECHO's call — and then nothing new.
        for (int i = 0; i < 6; i++)
        {
            Assert.True(server.UseSpsTowerConsoleForTest(p, tower));
        }

        Assert.Equal(new[] { "tower_ships", "tower_recall", "tower_red_night", "tower_guard_log", "tower_echo_1" },
            t.To<LoreTextRevealed>(p).Select(l => l.Key));
        Assert.All(t.To<LoreTextRevealed>(p), l => Assert.Equal("sps_tower", l.Site));
        Assert.Equal(5, heard.Events.Count(e => e == (GameEventKeys.LoreRead, "sps_tower")));
        Assert.Equal(7, t.TextsTo(p).Count(x => x == "@srv.sps_tower.console_locked")); // every use says what the console waits for
        Assert.Equal(1, t.LineKeys(p).Count(k => k == "story.vega.tower.read.puzzled")); // VEGA comments once per tower
        Assert.False(tower.Lit);
        Assert.DoesNotContain(heard.Events, e => e.Key == GameEventKeys.SpsTowerLit);
        Assert.All(tower.TowerLamps, l => Assert.Equal(Id(SpsTowerRules.LampBlock), server.World.GetBlock(l.ToVector()).Value));

        // A second tower holds the NEXT piece of the call for this player (its four records are read already).
        p.State.Position = new Vector3f(p.State.Position.X + 220, p.State.Position.Y, p.State.Position.Z);
        var second = SpsTowerLifeWorlds.Stamp(server, p, "sps_tower_broken nopost");
        Assert.True(server.UseSpsTowerConsoleForTest(p, second));
        Assert.True(server.UseSpsTowerConsoleForTest(p, second));
        Assert.Equal(new[] { "tower_echo_1", "tower_echo_2" }, t.To<LoreTextRevealed>(p).Select(l => l.Key).Where(k => k.StartsWith("tower_echo", StringComparison.Ordinal)));
    }

    // ---------------------------------------------------------------- the chip

    [Fact]
    public void TheChip_LightsTheTowerOnce_EveryLampTheRecordTheEventAndTheMap_AndALaterPlayersStepCountsWithoutAChip()
    {
        var (server, t) = _worlds.Start("life_chip", settlements: true);
        Assert.True(server.InhabitedSettlementCount > 0, "the test world needs a settlement");
        var p = SpsTowerLifeWorlds.Join(server, 1, "Pilot");
        var tower = SpsTowerLifeWorlds.StampAwayFromTheVillage(server, p, "sps_tower_leaning nopost");
        var other = SpsTowerLifeWorlds.StampAwayFromTheVillage(server, p, "sps_tower_broken nopost", further: 260);
        Assert.False(server.SpsTowerRevealed(other));
        var heard = new SpsTowerLifeWorlds.Listener();
        server.AddGameEventListener(GameEventKeys.SpsTowerLit, heard);
        SpsTowerLifeWorlds.StandAt(p, tower.Console!.ToVector());
        Assert.False(server.EvaluateStoryConditionForTest("world:sps_tower_lit_near", p));
        Assert.True(server.EvaluateStoryConditionForTest("world:sps_tower_known", p));

        p.State.Inventory.Add(Chip, 2, 16);
        t.Sent.Clear();
        Assert.True(server.UseSpsTowerConsoleForTest(p, tower));

        // One chip, every lamp, the record (saved), the event.
        Assert.Equal(1, p.State.Inventory.CountOf(Chip));
        Assert.True(tower.Lit);
        Assert.True(tower.TowerLamps.Count >= 6);
        Assert.All(tower.TowerLamps, l => Assert.Equal(Id(SpsTowerRules.LitLampBlock), server.World.GetBlock(l.ToVector()).Value));
        Assert.Equal(new[] { (GameEventKeys.SpsTowerLit, tower.SiteId) }, heard.Events);
        var sound = Assert.Single(t.To<SoundFx>(p), s => s.SoundId == SvGameServer.SpsTowerLightsSound);
        Assert.True(Math.Abs(sound.X - (tower.Console!.X + 0.5f)) < 0.01f);
        Assert.Contains("@srv.sps_tower.lit", t.TextsTo(p));
        Assert.Equal(1, t.LineKeys(p).Count(k => k == "story.vega.tower.lit.puzzled"));

        // The clients see the blue run up the tower: the lowest lamp at once, the top one about two seconds later.
        ushort blue = Id(SpsTowerRules.LitLampBlock);
        int told = t.To<BlockChanged>(p).Count(b => b.Block == blue);
        Assert.InRange(told, 1, tower.TowerLamps.Count - 1);
        Assert.True(server.SpsLampQueueForTest > 0);
        server.AdvanceUptimeForTest(SvGameServer.SpsTowerLampSweepSeconds + 0.1);
        server.TickSpsTowerLifeForTest();
        Assert.Equal(0, server.SpsLampQueueForTest);
        Assert.Equal(tower.TowerLamps.Count, t.To<BlockChanged>(p).Count(b => b.Block == blue));

        // The tower's message: the pack's scene when it has one, else this tower's piece of ECHO's call.
        // (#3899: the pack defines the scene — it is committed to the player's ledger and may still wait behind the cooldown.)
        bool scene = p.State.Milestones.Contains(StoryPackKeys.CutsceneSent(V, SvGameServer.SpsTowerMessageCutscene));
        Assert.True(scene ^ t.To<LoreTextRevealed>(p).Any(l => l.Key == "tower_echo_1"));

        // What it still knows: the settlements counted, the other guide tower on everyone's map.
        string line = Assert.Single(t.TextsTo(p), x => x.StartsWith("The tower shows what it still knows", StringComparison.Ordinal));
        Assert.Contains("settlements: " + server.SettlementsForTest.Count, line, StringComparison.Ordinal);
        Assert.Matches("other places: [1-9]", line); // the other tower at least (never the lit tower itself)
        Assert.True(server.SpsTowerRevealed(other));
        Assert.Equal(2, server.PlanetPoisForTest(p.State.PlayerId).Count(poi => poi.Type == SpsTowerRules.MapType));
        Assert.Contains(server.PlanetPoisForTest(p.State.PlayerId), poi => poi.Type == "settlement");
        Assert.True(server.EvaluateStoryConditionForTest("world:sps_tower_lit_near", p));

        // Never twice: the second chip stays, no second event — the console just shows its next record.
        int texts = t.To<LoreTextRevealed>(p).Count;
        Assert.True(server.UseSpsTowerConsoleForTest(p, tower));
        Assert.Equal(1, p.State.Inventory.CountOf(Chip));
        Assert.Single(heard.Events);
        Assert.Equal(texts + 1, t.To<LoreTextRevealed>(p).Count);

        // A player who comes later finds it blue: VEGA says so, the same records, their own step counts — and no chip is taken.
        var q = SpsTowerLifeWorlds.Join(server, 2, "Later");
        q.State.Inventory.Add(Chip, 1, 16);
        SpsTowerLifeWorlds.StandAt(q, tower.Console!.ToVector());
        server.TickSpsTowerLifeForTest();
        Assert.Equal(new[] { "vega.tower.found_blue" }, t.LineKeys(q).Where(k => k.Contains("tower", StringComparison.Ordinal)));
        Assert.True(server.EvaluateStoryConditionForTest("world:sps_tower_lit_near", q));
        Assert.True(server.UseSpsTowerConsoleForTest(q, tower));
        Assert.True(server.UseSpsTowerConsoleForTest(q, tower));
        Assert.Equal(1, q.State.Inventory.CountOf(Chip));
        Assert.Equal(2, heard.Events.Count); // once for the later player, however often they use the console
        Assert.Equal("tower_ships", t.To<LoreTextRevealed>(q)[0].Key);
        Assert.DoesNotContain("@srv.sps_tower.console_locked", t.TextsTo(q));

        // The record survives a restart.
        server.Stop();
        _worlds.CloseLast();
        var (again, _) = _worlds.Start("life_chip", settlements: true);
        Assert.True(again.Metadata.SpsTowers.Single(x => x.SiteId == tower.SiteId).Lit);
        var back = again.Metadata.SpsTowers.Single(x => x.SiteId == tower.SiteId);
        Assert.All(back.TowerLamps, l => Assert.Equal(blue, again.World.GetBlock(l.ToVector()).Value));
    }

    // ---------------------------------------------------------------- VEGA, the settlers

    [Fact]
    public void VegasTowerLines_FollowWhatThePlayersStoryKnows_AndNeverNameTheGuardianEarly()
    {
        var (server, t) = _worlds.Start("life_lines");
        var p = SpsTowerLifeWorlds.Join(server, 1, "Pilot");
        string Key(SvGameServer.SpsTowerVegaMoment moment) => server.SpsTowerVegaKeyForTest(p, moment);

        Assert.Equal("story.vega.tower.found.puzzled", Key(SvGameServer.SpsTowerVegaMoment.Found));
        SpsTowerLifeWorlds.AdvanceTo(server, 2);
        Assert.Equal("story.vega.tower.read.service", Key(SvGameServer.SpsTowerVegaMoment.Read));
        SpsTowerLifeWorlds.AdvanceTo(server, 5);
        Assert.Equal("story.vega.tower.found.service", Key(SvGameServer.SpsTowerVegaMoment.Found)); // the Guardian alone is not the verdict
        SpsTowerLifeWorlds.AdvanceTo(server, 6);
        Assert.Equal("story.vega.tower.lit.verdict", Key(SvGameServer.SpsTowerVegaMoment.Lit));
        server.MarkGuardianDefeatedForTest();
        Assert.Equal("story.vega.tower.found.blue", Key(SvGameServer.SpsTowerVegaMoment.Found));

        // A player whose own story has not heard the Service yet stays puzzled, whatever the crew knows.
        var late = SpsTowerLifeWorlds.Join(server, 2, "Latecomer");
        late.State.Milestones.RemoveWhere(m => m.StartsWith(StoryPackKeys.Prefix(V) + "beat:", StringComparison.Ordinal));
        Assert.Equal("story.vega.tower.found.puzzled", server.SpsTowerVegaKeyForTest(late, SvGameServer.SpsTowerVegaMoment.Found));

        // The first approach speaks once per tower; reading it lets VEGA remember the sky-lights (the insight), once.
        var tower = SpsTowerLifeWorlds.Stamp(server, p, "sps_tower_stump nopost");
        SpsTowerLifeWorlds.StandAt(p, tower.Console!.ToVector());
        late.State.Position = new Vector3f(p.State.Position.X + 400, p.State.Position.Y, p.State.Position.Z);
        t.Sent.Clear();
        server.TickSpsTowerLifeForTest();
        server.TickSpsTowerLifeForTest();
        Assert.Equal(new[] { "story.vega.tower.found.blue" }, t.LineKeys(p));
        Assert.Empty(t.LineKeys(late)); // far away
        Assert.True(server.UseSpsTowerConsoleForTest(p, tower));
        Assert.True(server.UseSpsTowerConsoleForTest(p, tower));
        Assert.Equal(1, t.LineKeys(p).Count(k => k == "story.vega.tower.read.blue"));
        Assert.Equal(1, t.LineKeys(p).Count(k => k == "story.vega.insight.sps_tower"));
        Assert.Contains("story:insight:sps-tower", p.State.Milestones);

        // Every line exists in English and German; the puzzled and the Service's never name the Guardian.
        var en = SpsTowerLifeWorlds.PackTable("en");
        var de = SpsTowerLifeWorlds.PackTable("de");
        foreach (var moment in Enum.GetValues<SvGameServer.SpsTowerVegaMoment>())
        {
            foreach (string level in new[] { "puzzled", "service", "verdict", "blue" })
            {
                string key = "story.vega.tower." + SvGameServer.SpsTowerMomentWord(moment) + "." + level;
                Assert.True(en.ContainsKey(key) && de.ContainsKey(key), key);
                if (level is "puzzled" or "service")
                {
                    Assert.DoesNotContain("Guardian", en[key], StringComparison.Ordinal);
                    Assert.DoesNotContain("Wächter", de[key], StringComparison.Ordinal);
                }
            }
        }

        foreach (string key in new[] { "story.vega.insight.sps_tower", "story.vega.insight.sps_guard", SvGameServer.SleeperTankLine, SvGameServer.SleeperUnsureLine })
        {
            Assert.True(en.ContainsKey(key) && de.ContainsKey(key), key);
        }

        // The console's records are evidence: none of them names the Guardian or says what the players are.
        foreach (string lore in SvGameServer.SpsTowerRecordTexts.Concat(SvGameServer.SpsTowerEchoTexts))
        {
            var site = Assert.Single(SpsTowerLifeWorlds.Content.Stories[V].LoreSites, l => l.Key == lore);
            Assert.Equal((SvGameServer.SpsTowerLoreSite, 0), (site.Site, site.MinKnowledge));
            Assert.True(en.ContainsKey(site.TextKey) && de.ContainsKey(site.TextKey), site.TextKey);
            Assert.DoesNotContain("Guardian", en[site.TextKey], StringComparison.Ordinal);
            Assert.DoesNotContain("clone", en[site.TextKey], StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void WithoutTheVegaStory_TheTowerStillLights_WithNeutralLines_AndItsTankStaysSealed()
    {
        var (server, t) = _worlds.Start("life_none", configure: c => c.Rules.StoryId = StoryRegistry.NoneStoryId);
        var p = SpsTowerLifeWorlds.Join(server, 1, "Pilot");
        var tower = SpsTowerLifeWorlds.Stamp(server, p, "sps_tower_stump post");
        Assert.Equal("vega.tower.found", server.SpsTowerVegaKeyForTest(p, SvGameServer.SpsTowerVegaMoment.Found));
        Assert.False(server.SleeperCanWake);

        // No story, no records: the console only says it is locked.
        Assert.True(server.UseSpsTowerConsoleForTest(p, tower));
        Assert.Empty(t.To<LoreTextRevealed>(p));
        Assert.Contains("@srv.sps_tower.console_locked", t.TextsTo(p));

        // The tank takes no chip here; the tower does.
        p.State.Inventory.Add(Chip, 2, 16);
        Assert.True(server.UseCapsuleTankForTest(p, tower));
        Assert.Contains("@srv.capsule.silent", t.TextsTo(p));
        Assert.False(tower.TankOpened);
        Assert.Equal(2, p.State.Inventory.CountOf(Chip));
        Assert.True(server.UseSpsTowerConsoleForTest(p, tower));
        Assert.True(tower.Lit);
        Assert.Equal(1, p.State.Inventory.CountOf(Chip));
        Assert.Contains("vega.tower.lit", t.LineKeys(p));

        var en = SpsTowerLifeWorlds.EngineTable("en");
        var de = SpsTowerLifeWorlds.EngineTable("de");
        foreach (string key in new[]
                 {
                     "vega.tower.found", "vega.tower.found_blue", "vega.tower.read", "vega.tower.lit", "npc.hint.sps_tower", "npc.hint.sps_tower_blue",
                     "srv.sps_tower.console_locked", "srv.sps_tower.console_done", "srv.sps_tower.lit", "srv.sps_tower.revealed",
                     "srv.sps_tower.revealed_settlements", "srv.capsule.sealed", "srv.capsule.silent", "srv.capsule.opened", "srv.capsule.guard_left",
                     SvGameServer.SleeperNameKey, SvGameServer.SleeperRoleKey,
                 })
        {
            Assert.True(en.ContainsKey(key) && de.ContainsKey(key), key);
        }
    }

    [Fact]
    public void TheSettlers_PointAtTheirSkyLight_AndTheirWorldTagsFollowItsColour()
    {
        var (server, _) = _worlds.Start("life_settlers");
        var p = SpsTowerLifeWorlds.Join(server, 1, "Pilot");
        Assert.Equal(string.Empty, server.SpsTowerRumourForTest(p)); // no tower: no rumour
        Assert.DoesNotContain(SvGameServer.SpsTowerRedTag, server.WorldStoryTagsForTest());

        var tower = SpsTowerLifeWorlds.Stamp(server, p, "sps_tower_stump nopost");
        Assert.False(server.SpsTowerRevealed(tower));
        string rumour = server.SpsTowerRumourForTest(p);
        Assert.Contains("sky-light", rumour, StringComparison.Ordinal);
        Assert.DoesNotContain("{", rumour, StringComparison.Ordinal);
        Assert.True(server.SpsTowerRevealed(tower));
        Assert.Equal(string.Empty, server.SpsTowerRumourForTest(p)); // on the map already

        Assert.Contains(SvGameServer.SpsTowerRedTag, server.WorldStoryTagsForTest());
        Assert.DoesNotContain(SvGameServer.SpsTowerBlueTag, server.WorldStoryTagsForTest());
        p.State.Inventory.Add(Chip, 1, 16);
        Assert.True(server.UseSpsTowerConsoleForTest(p, tower));
        Assert.Contains(SvGameServer.SpsTowerBlueTag, server.WorldStoryTagsForTest());
        Assert.DoesNotContain(SvGameServer.SpsTowerRedTag, server.WorldStoryTagsForTest());

        // The pack's flavour lines hang on those tags: fear and legend while it is red, the blue light afterwards.
        var lines = SpsTowerLifeWorlds.Content.Stories[V].FlavourLines;
        Assert.True(lines.Count(l => l.WorldTags.Contains(SvGameServer.SpsTowerRedTag)) >= 3);
        Assert.Single(lines, l => l.WorldTags.Contains(SvGameServer.SpsTowerBlueTag));
        var en = SpsTowerLifeWorlds.PackTable("en");
        var de = SpsTowerLifeWorlds.PackTable("de");
        Assert.All(lines.Where(l => l.WorldTags.Count > 0), l => Assert.True(en.ContainsKey(l.TextKey) && de.ContainsKey(l.TextKey), l.TextKey));
    }

    // ---------------------------------------------------------------- the guide marker

    [Fact]
    public void TheGuideMarker_PointsAtTheConsoleHere_NamesAVisitedWorldFromAfar_AndStaysAwayWhileNoTowerIsKnown()
    {
        var (server, _) = _worlds.Start("life_marker");
        var p = SpsTowerLifeWorlds.Join(server, 1, "Pilot");
        Assert.True(StoryRevealProviders.IsKnown(StoryRevealProviders.NearestSpsTower));
        Assert.True(StoryRevealProviders.IsKnown(StoryRevealProviders.NearestSpsGuard));

        // No tower known anywhere: no marker, and the predicate a step's text variant asks says so.
        Assert.Equal(((Vector3f?)null, (string?)null), server.SpsMarkerForTest(p));
        Assert.False(server.EvaluateStoryConditionForTest("world:sps_tower_known", p));
        Assert.False(server.EvaluateStoryConditionForTest("world:sps_guard_post_known", p));
        Assert.False(server.EvaluateStoryConditionForTest("world:sps_guard_awake", p));

        // On a world with towers: the nearest one's relay console.
        string home = p.CurrentLocationId;
        var near = SpsTowerLifeWorlds.Stamp(server, p, "sps_tower_stump post");
        var here = p.State.Position;
        p.State.Position = new Vector3f(here.X + 260, here.Y, here.Z);
        var far = SpsTowerLifeWorlds.Stamp(server, p, "sps_tower_broken nopost");
        Assert.True(server.EvaluateStoryConditionForTest("world:sps_tower_known", p));
        Assert.True(server.EvaluateStoryConditionForTest("world:sps_guard_post_known", p));

        var (spot, elsewhere) = server.SpsMarkerForTest(p);
        Assert.Null(elsewhere);
        Assert.Equal(new Vector3f(far.Console!.X + 0.5f, far.Console.Y + 1f, far.Console.Z + 0.5f), spot);
        p.State.Position = here;
        Assert.Equal(new Vector3f(near.Console!.X + 0.5f, near.Console.Y + 1f, near.Console.Z + 0.5f), server.SpsMarkerForTest(p).Spot);

        // The guard's marker: the sealed tank of the nearest guard post (the far tower has none).
        p.State.Position = new Vector3f(here.X + 260, here.Y, here.Z);
        var tank = near.Tank!;
        Assert.Equal(new Vector3f(tank.X + 0.5f, tank.Y + 1f, tank.Z + 0.5f), server.SpsMarkerForTest(p, guard: true).Spot);

        // From a world without one: the visited body that has one is named instead — no ground marker.
        string away = server.LandableBodiesForTest().First(b => b.Id != home).Id;
        p.CurrentLocationId = away;
        Assert.Equal(((Vector3f?)null, (string?)home), server.SpsMarkerForTest(p));
        Assert.Equal(((Vector3f?)null, (string?)home), server.SpsMarkerForTest(p, guard: true));
        p.CurrentLocationId = home;
    }
}

/// <summary>The worlds the tower-life and sleeper tests play in: meadowlands with the VEGA story, cheats on (an admin stands
/// the towers by hand), nothing else placed unless asked.</summary>
internal sealed class SpsTowerLifeWorlds : IDisposable
{
    internal static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root;
    private readonly List<SqliteWorldRepository> _repos = new();

    public SpsTowerLifeWorlds(string prefix) => _root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _repos.ForEach(r =>
        {
            try { r.Dispose(); } catch (ObjectDisposedException) { /* closed for a restart */ }
        });
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    /// <summary>Closes the last world's repository (before the same save is started again).</summary>
    public void CloseLast() => _repos[^1].Dispose();

    public (SvGameServer Server, RecordingTransport Transport) Start(string name, bool settlements = false, Action<ServerConfig>? configure = null)
    {
        var transport = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 20261121,
            StartPlanet = "meadowlands",
            AutoSaveIntervalMinutes = 9999,
            MaxPlayers = 6,
            PlaceStarterShip = false,
            PlaceSettlements = settlements,
            PlaceLandmarks = false,
            PlaceGiantWrecks = false,
            PlaceGuardianGiants = false,
            PlaceFactories = false,
            PlaceMonuments = false,
            PlaceBanditCamps = false,
            PlaceLodges = false,
            PlacePioneers = false,
            PlaceIntercityRail = false,
            PlaceRailRuins = false,
            SpsTowerChance = 0.0, // no rolled tower: the tests stand theirs by hand
            Rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true },
        };
        config.Rules.TutorialPolicy = TutorialPolicy.Off;
        configure?.Invoke(config);
        var server = new SvGameServer(config, Content, transport, repo);
        server.Start();
        return (server, transport);
    }

    /// <summary>The real join path of a current client (packs1 + cutscenes1), on foot.</summary>
    public static PlayerSession Join(SvGameServer server, int conn, string name)
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 },
        }));
        var p = server.Sessions[conn];
        p.State.AboardShip = false;
        return p;
    }

    /// <summary>Stands a tower by hand six blocks in front of <paramref name="by"/> (<c>/spstower &lt;key&gt; post|nopost</c>)
    /// and returns its record.</summary>
    public static SpsTowerRecord Stamp(SvGameServer server, PlayerSession by, string arg)
    {
        var role = by.State.Role;
        by.State.Role = PlayerRole.WorldAdmin;
        int before = server.Metadata.SpsTowers.Count;
        server.HandleForTest(by, new AdminCommandIntent { Command = "spstower", StringArg = arg });
        by.State.Role = role;
        Assert.Equal(before + 1, server.Metadata.SpsTowers.Count);
        return server.Metadata.SpsTowers[^1];
    }

    /// <summary>Stands a tower by hand well outside the world's settlements (<paramref name="further"/> blocks further out).</summary>
    public static SpsTowerRecord StampAwayFromTheVillage(SvGameServer server, PlayerSession by, string arg, int further = 0)
    {
        var towns = server.SettlementsForTest;
        int x = towns.Max(s => s.MaxX) + 140 + further;
        int z = (towns[0].MinZ + towns[0].MaxZ) / 2;
        by.State.Position = new Vector3f(x + 0.5f, by.State.Position.Y, z + 0.5f);
        return Stamp(server, by, arg);
    }

    /// <summary>Stands the player beside a cell (in reach of it).</summary>
    public static void StandAt(PlayerSession p, Vector3i cell) => p.State.Position = new Vector3f(cell.X + 1.5f, cell.Y, cell.Z + 0.5f);

    /// <summary>Adds the milestones the world still needs to reveal beat <paramref name="beat"/>.</summary>
    public static void AdvanceTo(SvGameServer server, int beat)
    {
        int target = (Content.Stories[SpsTowerLifeTests.V].Beats[beat].Threshold + 1) / 2;
        int missing = target - server.StorySnapshot.Milestones;
        if (missing > 0)
        {
            server.AdminAdvanceStory(missing);
        }

        Assert.True(server.StorySnapshot.BeatsRevealed > beat, $"B{beat} was not revealed");
    }

    public static Dictionary<string, string> PackTable(string code)
        => Table(Path.Combine(TestPaths.DataDir(), "stories", SpsTowerLifeTests.V, "locales", code + ".json"));

    public static Dictionary<string, string> EngineTable(string code) => Table(Path.Combine(TestPaths.DataDir(), "locales", code + ".json"));

    private static Dictionary<string, string> Table(string path)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path),
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;

    internal sealed class Listener : IGameEventListener
    {
        public List<(string Key, string Arg)> Events { get; } = new();

        public List<string> Actors { get; } = new();

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount)
        {
            Events.Add((key, arg));
            Actors.Add(actor.State.Name);
        }
    }
}
