// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3899 — the three acts of the sky-lights as story missions of the VEGA pack: "The Sky-Light" (chapter 2), "The Sleeper"
/// (chapter 5) and "ECHO's Call" (chapter 6), all kept after their chapter. The data (chapters, order, the keep switch, the
/// conditions, every text in English and German within its budget), each mission handed out in its chapter after the
/// missions before it and played through on the real handlers (a stamped tower's console and capsule tank, the guard's
/// dialog, the summoned cruiser, its wreck, the bridge console), surviving a chapter change; what was done before the
/// mission counting (a tower found, read or lit, a chip carried, ECHO already free) and what somebody else did while a step
/// ran (a tank opened far away, another pilot's fight); a world without towers skipping the first two silently and still
/// offering the third; the story's summon (after a while in flight, once per flight, again on a later flight, never in
/// the Guardian's system, never once ECHO is free; an ambush cruiser becoming hers); the four scenes.
/// </summary>
public sealed class SkyLightsMissionsTests : IDisposable
{
    private const string V = StoryRegistry.DefaultStoryId;
    private const string Chip = SvGameServer.SpsTowerChipItem;
    private const string Cannon = "ship_cannon_1";

    private static readonly string[] Missions = { "m_sky_light", "m_sleeper", "m_echo_call" };

    private readonly SpsTowerLifeWorlds _worlds = new("bbts_sky_light_missions_");

    public void Dispose() => _worlds.Dispose();

    // ---------------------------------------------------------------- helpers

    private static StoryDefinition Vega => SpsTowerLifeWorlds.Content.Stories[V];

    private static StoryMission Mission(string id) => Vega.Chapters.SelectMany(c => c.Missions).Single(m => m.Id == id);

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private static string Done(string mission) => StoryPackKeys.ChapterDone(V, mission);

    private static string Skipped(string mission) => StoryPackKeys.ChapterSkipped(V, mission);

    private static string StepDone(string step) => StoryPackKeys.StepDone(V, step);

    private static string Id(PlayerSession p) => p.State.PlayerId;

    /// <summary>The rules a space boss needs: PvE in space, ship weapons that hit machines, the space hostiles on.</summary>
    private static void SpaceBossRules(ServerConfig c)
    {
        c.Rules.FreeSpaceFlight = true;
        c.Rules.SpaceCombat = SpaceCombatMode.PvE;
        c.Rules.ShipWeapons = ShipWeaponMode.NpcsOnly;
        c.Rules.SpaceNpcEnemies = AlienActivity.Rare;
        c.Rules.AlienUfos = AlienActivity.Off;
        c.Rules.Bandits = AlienActivity.Off;
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    /// <summary>Every queued story scene went out, then the 1 Hz mission passes ran.</summary>
    private static void Settle(SvGameServer server, PlayerSession p)
    {
        for (int i = 0; i < 40 && (i < 4 || p.Story.CutsceneQueue.Count > 0); i++)
        {
            server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
            server.TickCutscenesForTest(p);
        }

        Assert.Empty(p.Story.CutsceneQueue);
        server.TickStoryMissionsForTest(p);
        server.TickSkyLightMissionsForTest(p);
    }

    /// <summary>Sends the scenes that still wait behind the cutscene cooldown (a scene the game's code played a moment after
    /// another one of the same story).</summary>
    private static void FlushScenes(SvGameServer server, PlayerSession p)
    {
        for (int i = 0; i < 8 && p.Story.CutsceneQueue.Count > 0; i++)
        {
            server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
            server.TickCutscenesForTest(p);
        }

        Assert.Empty(p.Story.CutsceneQueue);
    }

    /// <summary>Lets the current chapter's missions that come before <paramref name="mission"/> go by (skipped in the Journal
    /// — their own tests play them) until it runs, and returns the missions that ran first, in order.</summary>
    private static List<string> SkipTo(SvGameServer server, PlayerSession p, string mission)
    {
        var before = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            Settle(server, p);
            var running = server.RunningMissionsForTest(p);
            if (running.Contains(mission))
            {
                return before;
            }

            foreach (string other in running.Where(m => !Missions.Contains(m)))
            {
                before.Add(other);
                Deliver(server, p, new StoryPackIntent { PackId = V, Action = StoryPackActions.SkipChapter, ChapterId = other });
            }
        }

        Assert.Contains(mission, server.RunningMissionsForTest(p));
        return before;
    }

    private static int Count(RecordingTransport t, PlayerSession p, string lineKey) => t.LineKeys(p).Count(k => k == lineKey);

    private static List<string> MissionLines(RecordingTransport t, PlayerSession p, string mission)
        => t.LineKeys(p).Where(k => k.StartsWith("story.vega.m." + mission.Substring(2) + ".", StringComparison.Ordinal)).ToList();

    /// <summary>Talks the guard's own dialog through (five nodes, the first reply each).</summary>
    private static void TalkThrough(SvGameServer server, PlayerSession p, SpsTowerRecord tower)
    {
        var guard = server.SleeperForTest(tower)!.Value;
        p.State.Position = new Vector3f(guard.Pos.X + 1f, guard.Pos.Y, guard.Pos.Z);
        server.TalkToNpcForTest(Id(p), guard.NpcId);
        Assert.Equal((SvGameServer.SleeperDialogKey, 0), server.ActiveDialogForTest(Id(p)));
        for (int node = 0; node < 5; node++)
        {
            server.ChooseDialogForTest(Id(p), 0);
        }

        Assert.Null(server.ActiveDialogForTest(Id(p)));
    }

    /// <summary>Puts a joined player at the helm of an armed ship that can jump — nothing hunts them (god mode), so the
    /// ambient machines of a flight never decide a test.</summary>
    private static void Fly(SvGameServer server, PlayerSession p, float z = 0f)
    {
        p.State.GodMode = true;
        p.Liveness.HeartbeatTracked = false; // a test client sends no keep-alive while the server ticks for a minute
        var ship = p.Ships[p.ActiveShipId];
        ship.Modules.Add(Cannon);
        ship.Modules.Add("jump_generator");
        p.State.AboardShip = true;
        Deliver(server, p, new EnterSpaceIntent()); // the real launch from the helm
        Assert.True(server.InSpace(Id(p)));
        server.ShipMove(Id(p), 0f, 0f, z);
    }

    private static void Run(SvGameServer server, double seconds, double step = 0.5)
    {
        for (double t = 0; t < seconds - 1e-9; t += step)
        {
            server.Tick(step);
        }
    }

    /// <summary>Shoots the live boss of the pilot's flight down (it stands still in front of the pilot, its guns silent) and
    /// returns the wreck it leaves.</summary>
    private static string ShootDown(SvGameServer server, PlayerSession pilot)
    {
        var before = server.CruiserWreckIdsForTest;
        var boss = server.SpaceBossForTest(Id(pilot))!;
        server.ShipMove(Id(pilot), 0f, 0f, 0f);
        boss.Position = new Vector3f(0f, 0f, 30f);
        boss.Boss!.Speed = 0.001f;
        boss.DamagePerSecond = 0f;
        boss.HeavyDamage = 0f;
        boss.Shield = 0f;
        boss.Hull = 1f;
        server.FireWeapon(Id(pilot), Cannon, boss.Id);
        Assert.Null(server.SpaceBossForTest(Id(pilot)));
        return Assert.Single(server.CruiserWreckIdsForTest, id => !before.Contains(id));
    }

    private static void Board(SvGameServer server, PlayerSession pilot, string wreckId)
    {
        Assert.True(server.InSpace(Id(pilot)));

        var wreck = server.CruiserWreckForTest(wreckId)!.Value;
        server.ShipMove(Id(pilot), wreck.Position.X, wreck.Position.Y, wreck.Position.Z + wreck.Half.Z + 6f);
        server.BoardStation(Id(pilot), wreckId);
        Assert.True(server.InStation(Id(pilot)));
    }

    /// <summary>A world in chapter 6 with the space-boss rules, and the given pilots with "ECHO's Call" running.</summary>
    private (SvGameServer Server, RecordingTransport Transport, PlayerSession[] Pilots) EchoWorld(string name, params string[] pilots)
    {
        var (server, t) = _worlds.Start(name, configure: SpaceBossRules);
        var joined = pilots.Select((n, i) => SpsTowerLifeWorlds.Join(server, i + 1, n)).ToArray();
        SpsTowerLifeWorlds.AdvanceTo(server, 11); // chapter 6, "It Still Sleeps"
        foreach (var p in joined)
        {
            Assert.Equal(new[] { "m_jump" }, SkipTo(server, p, "m_echo_call")); // after the jump generator, never before
        }

        return (server, t, joined);
    }

    // ---------------------------------------------------------------- the data

    [Fact]
    public void TheThreeMissions_SitInTheirChapters_AreKept_AndEveryTextExistsWithinItsBudget()
    {
        // Chapter and order: after the observatory (or its stand-in), after the radar, after the jump generator.
        string[] In(string chapter) => Vega.Chapters.Single(c => c.Id == chapter).Missions.Select(m => m.Id).ToArray();
        Assert.Equal(new[] { "m_observatory", "m_archive", "m_sky_light" }, In("service"));
        Assert.Equal(new[] { "m_radar", "m_sleeper" }, In("what_you_are"));
        Assert.Equal(new[] { "m_jump", "m_echo_call" }, In("sleeps"));
        Assert.Equal(new[] { 2, 5, 6 }, new[] { "service", "what_you_are", "sleeps" }.Select(c => Vega.Chapters.FindIndex(x => x.Id == c)));

        // Kept on the mission itself (no stand-in), feasible by a fact of the world that does not change.
        foreach (string id in Missions)
        {
            var mission = Mission(id);
            Assert.True(mission.Keep, id);
            Assert.Equal(string.Empty, mission.Alternative);
            Assert.All(mission.Steps, s => Assert.Equal((StoryStepTiers.Core, StoryStepKinds.Do), (s.Tier, s.Kind)));
            Assert.All(mission.Steps, s => Assert.Equal(string.Empty, s.Alternative)); // no stand-in step either
        }

        Assert.Equal(new[] { "world:sps_towers" }, Mission("m_sky_light").FeasibleIf);
        Assert.Equal(new[] { "world:sps_towers" }, Mission("m_sleeper").FeasibleIf);
        Assert.Equal(new[] { "world:space_bosses" }, Mission("m_echo_call").FeasibleIf);
        Assert.Equal(Missions.Length, Vega.Chapters.SelectMany(c => c.Missions).Count(m => m.Keep)); // nothing else is kept

        // The steps are the towers', the sleeper's and the cruiser's own events.
        (string Event, string Arg)[] Steps(string id) => Mission(id).Steps.Select(s => (s.Objective.Event, s.Objective.Arg)).ToArray();
        Assert.Equal(new[] { (GameEventKeys.SpsTowerFound, string.Empty), (GameEventKeys.LoreRead, SvGameServer.SpsTowerLoreSite) }, Steps("m_sky_light"));
        Assert.Equal(
            new[]
            {
                (GameEventKeys.CraftAny, Chip), (GameEventKeys.SpsTowerLit, string.Empty), (GameEventKeys.CapsuleOpened, string.Empty),
                (GameEventKeys.DialogDone, SvGameServer.SleeperDialogKey),
            },
            Steps("m_sleeper"));
        Assert.Equal(
            new[]
            {
                (GameEventKeys.SpaceEnter, string.Empty), (GameEventKeys.CruiserDefeated, string.Empty),
                (GameEventKeys.CruiserBoarded, CruiserWreckRules.BoardedEcho), (GameEventKeys.EchoFreed, string.Empty),
            },
            Steps("m_echo_call"));
        Assert.NotNull(SpsTowerLifeWorlds.Content.GetRecipe(Chip)); // the craft step names a real recipe
        Assert.Contains(GameEventKeys.SpsTowerFound, GameEventKeys.All);

        // Every text — the variants' too — in English and German, within the budgets VEGA's panel and the chip have.
        var en = SpsTowerLifeWorlds.PackTable("en");
        var de = SpsTowerLifeWorlds.PackTable("de");
        foreach (string id in Missions)
        {
            var mission = Mission(id);
            Assert.True(en.ContainsKey(mission.TitleKey) && de.ContainsKey(mission.TitleKey), mission.TitleKey);
            foreach (var step in mission.Steps)
            {
                var chips = new[] { step.Explain.ChipKey }.Concat(step.Variants.Select(v => v.ChipKey)).Where(k => k.Length > 0).ToList();
                var lines = new[] { step.Explain.StartKey, step.Explain.HintKey, step.Explain.DoneKey }
                    .Concat(step.Variants.SelectMany(v => new[] { v.StartKey, v.HintKey, v.DoneKey })).Where(k => k.Length > 0).ToList();
                Assert.NotEmpty(chips);
                Assert.False(string.IsNullOrEmpty(step.Explain.StartKey), step.Id);
                Assert.False(string.IsNullOrEmpty(step.Explain.HintKey), step.Id);
                foreach (string chip in chips)
                {
                    Assert.True(en.ContainsKey(chip) && de.ContainsKey(chip), chip);
                    Assert.True(StoryPackValidator.ShownLength(en[chip]) <= StoryPackValidator.ChipMaxEn, chip);
                    Assert.True(StoryPackValidator.ShownLength(de[chip]) <= StoryPackValidator.ChipMaxDe, chip);
                }

                foreach (string line in lines)
                {
                    Assert.True(en.ContainsKey(line) && de.ContainsKey(line), line);
                    Assert.True(StoryPackValidator.ShownLength(en[line]) <= StoryPackValidator.LineMaxEn, line);
                    Assert.True(StoryPackValidator.ShownLength(de[line]) <= StoryPackValidator.LineMaxDe, line);
                }
            }
        }

        Assert.Equal(("The Sky-Light", "The Sleeper", "ECHO's Call"),
            (en["story.vega.m.sky_light.title"], en["story.vega.m.sleeper.title"], en["story.vega.m.echo_call.title"]));
        Assert.Equal(("Das Himmelslicht", "Der Schläfer", "ECHOs Ruf"),
            (de["story.vega.m.sky_light.title"], de["story.vega.m.sleeper.title"], de["story.vega.m.echo_call.title"]));

        // Chapter 2 knows the Service and nothing of the Guardian or of what the players are: its mission never says so.
        foreach (string key in en.Keys.Where(k => k.StartsWith("story.vega.m.sky_light.", StringComparison.Ordinal)))
        {
            foreach (string word in new[] { "Guardian", "clone", "imprint", "ECHO", "verdict" })
            {
                Assert.DoesNotContain(word, en[key], StringComparison.OrdinalIgnoreCase);
            }
        }

        // The mission's last word is an insight (kept in VEGA's notes), mirrored in the built-in story.
        var last = Assert.Single(Vega.Insights, i => i.Event == SvGameServer.MissionInsightEvent("m_echo_call"));
        Assert.Equal(("story.vega.insight.echo", 11), (last.TextKey, last.MinBeat));
        Assert.Contains(StoryRegistry.Default.Insights, i => i.Event == last.Event && i.TextKey == last.TextKey && i.OnceKey == last.OnceKey);
    }

    // ---------------------------------------------------------------- The Sky-Light

    [Fact]
    public void TheSkyLight_ComesAfterTheChaptersFirstMission_StaysOpenWhenTheChapterMovesOn_AndEndsAtARealConsole()
    {
        var (server, t) = _worlds.Start("slm_sky_light");
        var p = SpsTowerLifeWorlds.Join(server, 1, "Pilot");
        Assert.True(server.EvaluateStoryConditionForTest("world:sps_towers", p));
        Assert.False(server.EvaluateStoryConditionForTest("world:sps_tower_known", p));
        SpsTowerLifeWorlds.AdvanceTo(server, 2); // chapter 2, "The Service"

        var before = SkipTo(server, p, "m_sky_light");
        Assert.NotEmpty(before); // the observatory, or the archive standing in for it — the sky-light never comes first
        Assert.All(before, m => Assert.Contains(m, new[] { "m_observatory", "m_archive" }));
        Assert.Equal("m_sky_light.1", server.MissionChipStepForTest(p));
        Assert.Equal("story.vega.m.sky_light.1.obj", server.ObjectiveKeyForTest(Id(p)));

        // No tower is known yet: VEGA says where the first one will be, not "I know of one".
        Assert.Equal(1, Count(t, p, "story.vega.m.sky_light.1.start_unknown"));
        Assert.Equal(0, Count(t, p, "story.vega.m.sky_light.1.start"));

        // A tower stands in front of the player: the approach is the towers' own event.
        var tower = SpsTowerLifeWorlds.Stamp(server, p, "sps_tower_stump post");
        server.TickSpsTowerFoundForTest(p);
        Assert.True(Has(p, StepDone("m_sky_light.1")));
        Assert.Equal(1, Count(t, p, "story.vega.m.sky_light.1.done"));
        Assert.Equal("m_sky_light.2", server.MissionChipStepForTest(p));
        Assert.Equal(1, Count(t, p, "story.vega.m.sky_light.2.start"));

        // The story moves on to chapter 3: a kept mission is not let go — it runs beside the new chapter's own.
        SpsTowerLifeWorlds.AdvanceTo(server, 5);
        Assert.Equal("guardian", server.TrackChapterIdForTest(p));
        Settle(server, p);
        Assert.False(Has(p, Skipped("m_sky_light")));
        Assert.Equal(new[] { "m_sky_light", "m_runes" }, server.RunningMissionsForTest(p));
        Assert.Equal(new[] { "m_runes", "m_sky_light" }, server.JournalMissionsForTest(p).Select(m => m.Id)); // the kept one after the chapter's own
        Assert.Equal("m_runes.1", server.MissionChipStepForTest(p)); // the current chapter's mission has the chip first
        Deliver(server, p, new StoryPackIntent { PackId = V, Action = StoryPackActions.SkipChapter, ChapterId = "m_runes" });
        Assert.Equal("m_sky_light.2", server.MissionChipStepForTest(p)); // …and then the kept one again

        // The relay console's first record ends it — with VEGA's line about the settlers' legend.
        SpsTowerLifeWorlds.StandAt(p, tower.Console!.ToVector());
        Assert.True(server.UseSpsTowerConsoleForTest(p, tower));
        Assert.True(Has(p, "lore:tower_ships"));
        Assert.True(Has(p, Done("m_sky_light")));
        Assert.Equal(1, Count(t, p, "story.vega.m.sky_light.2.done"));
        Assert.DoesNotContain("m_sky_light", server.RunningMissionsForTest(p));
        Assert.Contains("sky-lights", SpsTowerLifeWorlds.PackTable("en")["story.vega.m.sky_light.2.done"], StringComparison.Ordinal);
        Assert.Contains("Himmelslichter", SpsTowerLifeWorlds.PackTable("de")["story.vega.m.sky_light.2.done"], StringComparison.Ordinal);
    }

    [Fact]
    public void ATowerFoundOrReadBeforeTheMission_CountsForIt()
    {
        var (server, t) = _worlds.Start("slm_sky_light_before");
        var near = SpsTowerLifeWorlds.Join(server, 1, "Near");
        var reader = SpsTowerLifeWorlds.Join(server, 2, "Reader");
        var tower = SpsTowerLifeWorlds.Stamp(server, near, "sps_tower_stump post"); // found before VEGA ever asked
        Assert.True(server.EvaluateStoryConditionForTest("world:sps_tower_near", near));
        Assert.True(server.EvaluateStoryConditionForTest("world:sps_tower_known", near));
        SpsTowerLifeWorlds.StandAt(reader, tower.Console!.ToVector());
        Assert.True(server.UseSpsTowerConsoleForTest(reader, tower)); // …and one of them read its console already
        Assert.True(Has(reader, "lore:tower_ships"));

        SpsTowerLifeWorlds.AdvanceTo(server, 2);
        SkipTo(server, near, "m_sky_light");
        Assert.True(Has(near, StepDone("m_sky_light.1"))); // standing at the tower: the first step is through, silently
        Assert.Equal("m_sky_light.2", server.MissionChipStepForTest(near));
        Assert.Equal(new[] { "story.vega.m.sky_light.2.start" }, MissionLines(t, near, "m_sky_light"));

        // The reader has nothing left to do: the mission is done the moment its turn comes, without a word.
        for (int i = 0; i < 4 && !Has(reader, Done("m_sky_light")); i++)
        {
            Settle(server, reader);
            foreach (string other in server.RunningMissionsForTest(reader).Where(m => m != "m_sky_light").ToList())
            {
                Deliver(server, reader, new StoryPackIntent { PackId = V, Action = StoryPackActions.SkipChapter, ChapterId = other });
            }
        }

        Assert.True(Has(reader, Done("m_sky_light")));
        Assert.Empty(MissionLines(t, reader, "m_sky_light"));
    }

    // ---------------------------------------------------------------- The Sleeper

    [Fact]
    public void TheSleeper_IsBuiltLitOpenedAndTalkedThrough_AndStaysOpenWhenTheChapterMovesOn()
    {
        var (server, t) = _worlds.Start("slm_sleeper");
        var p = SpsTowerLifeWorlds.Join(server, 1, "Pilot");
        SpsTowerLifeWorlds.AdvanceTo(server, 9); // chapter 5, "What You Are"
        Assert.Equal(new[] { "m_radar" }, SkipTo(server, p, "m_sleeper")); // after the radar
        Assert.Equal("m_sleeper.1", server.MissionChipStepForTest(p));
        Assert.Equal(1, Count(t, p, "story.vega.m.sleeper.1.start"));

        // 1 — the chip is forged (the workshop recipe's craft event).
        server.RaiseGameEventForTest(p, GameEventKeys.CraftAny, "titanium_plate");
        Assert.False(Has(p, StepDone("m_sleeper.1")));
        server.RaiseGameEventForTest(p, GameEventKeys.CraftAny, Chip);
        Assert.True(Has(p, StepDone("m_sleeper.1")));
        Assert.Equal(1, Count(t, p, "story.vega.m.sleeper.1.done"));
        Assert.Equal(1, Count(t, p, "story.vega.m.sleeper.2.start"));

        // 2 — a guide tower is lit at its relay console (one chip).
        var tower = SpsTowerLifeWorlds.Stamp(server, p, "sps_tower_stump post");
        p.State.Inventory.Add(Chip, 2, 16);
        SpsTowerLifeWorlds.StandAt(p, tower.Console!.ToVector());
        Assert.True(server.UseSpsTowerConsoleForTest(p, tower));
        Assert.True(tower.Lit);
        Assert.True(Has(p, StepDone("m_sleeper.2")));
        Assert.Equal(1, Count(t, p, "story.vega.m.sleeper.2.done"));
        FlushScenes(server, p);
        Assert.Contains(SvGameServer.SpsTowerMessageCutscene, t.Plays(p)); // the tower's message, played by the tower's code
        Assert.Equal("m_sleeper.3", server.MissionChipStepForTest(p));
        Assert.Equal("story.vega.m.sleeper.3.obj", server.ObjectiveKeyForTest(Id(p))); // a guard post is known: its own words
        Assert.Equal(1, Count(t, p, "story.vega.m.sleeper.3.start"));

        // 3 — the capsule tank of its guard post is opened (the second chip): the guard steps out.
        Assert.True(server.UseCapsuleTankForTest(p, tower));
        Assert.True(tower.TankOpened);
        Assert.True(Has(p, StepDone("m_sleeper.3")));
        Assert.Equal(0, p.State.Inventory.CountOf(Chip));
        FlushScenes(server, p);
        Assert.Contains(SvGameServer.SleeperTankCutscene, t.Plays(p));
        Assert.Equal("m_sleeper.4", server.MissionChipStepForTest(p));
        Assert.Equal(1, Count(t, p, "story.vega.m.sleeper.4.start"));

        // The story moves on to chapter 6: the kept mission runs on, beside the jump generator's.
        SpsTowerLifeWorlds.AdvanceTo(server, 11);
        Settle(server, p);
        Assert.Equal(new[] { "m_sleeper", "m_jump" }, server.RunningMissionsForTest(p));
        Assert.False(Has(p, Skipped("m_sleeper")));
        Deliver(server, p, new StoryPackIntent { PackId = V, Action = StoryPackActions.SkipChapter, ChapterId = "m_jump" });
        Assert.Equal("m_sleeper.4", server.MissionChipStepForTest(p));

        // 4 — the guard's dialog, talked through to its end.
        TalkThrough(server, p, tower);
        Assert.True(Has(p, Done("m_sleeper")));
        Assert.Equal(1, Count(t, p, "story.vega.m.sleeper.4.done"));

        // "He is like you" is the sleeper's insight — it follows near him, because the chapter knows what the players are.
        server.TickSleeperAftermathForTest(p);
        Assert.Equal(1, Count(t, p, "story.vega.insight.sps_guard"));
    }

    [Fact]
    public void TheSleepersSteps_CountWhatWasDoneBefore_AndWhatSomebodyElseDid()
    {
        var (server, t) = _worlds.Start("slm_sleeper_before");
        var ann = SpsTowerLifeWorlds.Join(server, 1, "Ann");
        var ben = SpsTowerLifeWorlds.Join(server, 2, "Ben");
        var home = ann.State.Position;

        // Chapters ago Ann lit a tower that has no guard post, and walked away with a spare chip in her backpack.
        var bare = SpsTowerLifeWorlds.Stamp(server, ann, "sps_tower_broken nopost");
        Assert.False(bare.HasGuardPost);
        ann.State.Inventory.Add(Chip, 2, 16);
        SpsTowerLifeWorlds.StandAt(ann, bare.Console!.ToVector());
        Assert.True(server.UseSpsTowerConsoleForTest(ann, bare));
        ann.State.Position = new Vector3f(home.X + 420f, home.Y, home.Z);
        ben.State.Position = home; // Ben stands near that lit tower
        Assert.False(server.EvaluateStoryConditionForTest("world:sps_tower_lit_near", ann));
        Assert.True(server.EvaluateStoryConditionForTest("world:sps_tower_lit_by_player", ann));
        Assert.True(server.EvaluateStoryConditionForTest("world:hacking_chip_carried", ann));
        Assert.True(server.EvaluateStoryConditionForTest("world:sps_tower_lit_near", ben));
        Assert.False(server.EvaluateStoryConditionForTest("world:sps_tower_lit_by_player", ben));
        Assert.False(server.EvaluateStoryConditionForTest("world:hacking_chip_carried", ben));
        Assert.False(server.EvaluateStoryConditionForTest("world:sps_guard_post_known", ann));

        SpsTowerLifeWorlds.AdvanceTo(server, 9);
        SkipTo(server, ann, "m_sleeper");

        // The chip she carries and the tower she lit count, silently. No guard post is known: the tank step is the same
        // step in other words — it asks for a tower with a post instead of sending her to a tank nobody has seen.
        Assert.True(Has(ann, StepDone("m_sleeper.1")) && Has(ann, StepDone("m_sleeper.2")));
        Assert.Equal("m_sleeper.3", server.MissionChipStepForTest(ann));
        Assert.Equal(new[] { "story.vega.m.sleeper.3.start_unknown" }, MissionLines(t, ann, "m_sleeper"));
        Assert.Equal("story.vega.m.sleeper.3.obj_unknown", server.ObjectiveKeyForTest(Id(ann)));

        var tower = SpsTowerLifeWorlds.Stamp(server, ann, "sps_tower_stump post"); // a tower with its guard post is found
        Assert.True(server.EvaluateStoryConditionForTest("world:sps_guard_post_known", ann));
        Assert.Equal("m_sleeper.3", server.MissionChipStepForTest(ann)); // the same step…
        Assert.Equal("story.vega.m.sleeper.3.obj", server.ObjectiveKeyForTest(Id(ann))); // …with its own words now

        // Ben never forged a chip: a Hunter drops him one while the step runs — carrying it is the evidence.
        SkipTo(server, ben, "m_sleeper");
        Assert.Equal("m_sleeper.1", server.MissionChipStepForTest(ben));
        server.TickSkyLightMissionsForTest(ben);
        Assert.False(Has(ben, StepDone("m_sleeper.1")));
        ben.State.Inventory.Add(Chip, 1, 16);
        server.TickSkyLightMissionsForTest(ben);
        Assert.True(Has(ben, StepDone("m_sleeper.1")));
        Assert.Equal(1, Count(t, ben, "story.vega.m.sleeper.1.done"));
        Assert.True(Has(ben, StepDone("m_sleeper.2"))); // the tower beside him is lit already (by Ann): nothing to light
        Assert.Equal(0, Count(t, ben, "story.vega.m.sleeper.2.start"));
        Assert.Equal("m_sleeper.3", server.MissionChipStepForTest(ben));
        Assert.Equal(1, ben.State.Inventory.CountOf(Chip)); // nothing was taken from him

        // Ann opens the tank far away from Ben: the event reaches only her, the opened tank is Ben's evidence.
        Assert.True(server.UseCapsuleTankForTest(ann, tower));
        Assert.True(Has(ann, StepDone("m_sleeper.3")));
        Assert.False(Has(ben, StepDone("m_sleeper.3")));
        server.TickSkyLightMissionsForTest(ben);
        Assert.True(Has(ben, StepDone("m_sleeper.3")));
        Assert.Equal("m_sleeper.4", server.MissionChipStepForTest(ben));
        Assert.Equal(1, Count(t, ben, "story.vega.m.sleeper.4.start"));

        // Each talks to the guard for themselves; a player who had talked before would be done at once (the dialog's key).
        TalkThrough(server, ben, tower);
        Assert.True(Has(ben, Done("m_sleeper")));
        Assert.False(Has(ann, Done("m_sleeper")));
        Assert.True(server.EvaluateStoryConditionForTest("milestone:dialog:sps_guard:done", ben));
        Assert.False(server.EvaluateStoryConditionForTest("milestone:dialog:sps_guard:done", ann));
    }

    // ---------------------------------------------------------------- a world without towers

    [Fact]
    public void AWorldWithoutTowers_SkipsTheSkyLightAndTheSleeperSilently_AndStillOffersEchosCall()
    {
        var (server, t) = _worlds.Start("slm_no_towers", configure: c =>
        {
            c.PlaceSpsTowers = false;
            SpaceBossRules(c);
        });
        var p = SpsTowerLifeWorlds.Join(server, 1, "Pilot");
        Assert.False(server.EvaluateStoryConditionForTest("world:sps_towers", p));
        Assert.True(server.EvaluateStoryConditionForTest("world:space_bosses", p));

        void SkipWhatRuns()
        {
            for (int i = 0; i < 4; i++)
            {
                Settle(server, p);
                foreach (string running in server.RunningMissionsForTest(p).Where(m => !Missions.Contains(m)).ToList())
                {
                    Deliver(server, p, new StoryPackIntent { PackId = V, Action = StoryPackActions.SkipChapter, ChapterId = running });
                }
            }
        }

        SpsTowerLifeWorlds.AdvanceTo(server, 2);
        SkipWhatRuns();
        Assert.True(Has(p, Skipped("m_sky_light")));
        Assert.Empty(server.RunningMissionsForTest(p));

        SpsTowerLifeWorlds.AdvanceTo(server, 9);
        SkipWhatRuns();
        Assert.True(Has(p, Skipped("m_sleeper")));
        Assert.Empty(server.RunningMissionsForTest(p));
        Assert.DoesNotContain(t.LineKeys(p), k => k.StartsWith("story.vega.m.sky_light.", StringComparison.Ordinal)
                                                  || k.StartsWith("story.vega.m.sleeper.", StringComparison.Ordinal));
        Assert.Empty(server.JournalMissionsForTest(p));

        // Chapter 6: ECHO's call needs no tower — VEGA picks the call up herself.
        SpsTowerLifeWorlds.AdvanceTo(server, 11);
        Assert.Equal(new[] { "m_jump" }, SkipTo(server, p, "m_echo_call"));
        Assert.Equal("m_echo_call.1", server.MissionChipStepForTest(p));
        Assert.Equal(new[] { "story.vega.m.echo_call.1.start" }, MissionLines(t, p, "m_echo_call"));

        // …and where the rules allow no space boss, that one is skipped as silently.
        var (calm, ct) = _worlds.Start("slm_no_boss", configure: c => c.PlaceSpsTowers = false);
        var q = SpsTowerLifeWorlds.Join(calm, 1, "Pilot");
        Assert.False(calm.EvaluateStoryConditionForTest("world:space_bosses", q));
        SpsTowerLifeWorlds.AdvanceTo(calm, 11);
        for (int i = 0; i < 4; i++)
        {
            Settle(calm, q);
            foreach (string running in calm.RunningMissionsForTest(q).ToList())
            {
                Deliver(calm, q, new StoryPackIntent { PackId = V, Action = StoryPackActions.SkipChapter, ChapterId = running });
            }
        }

        Assert.True(Has(q, Skipped("m_echo_call")));
        Assert.DoesNotContain(ct.LineKeys(q), k => k.StartsWith("story.vega.m.echo_call.", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- ECHO's Call

    [Fact]
    public void EchosCall_BringsHerCruiserOncePerFlight_AgainOnALaterFlight_AndEndsWithHerFreedOnTheBridge()
    {
        var (server, t, pilots) = EchoWorld("slm_echo", "Pilot");
        var p = pilots[0];
        string id = Id(p);
        Assert.Equal("m_echo_call.1", server.MissionChipStepForTest(p));
        Assert.Equal(1, Count(t, p, "story.vega.m.echo_call.1.start")); // no tower pieces read: VEGA hears the call herself
        Assert.Equal(0, Count(t, p, "story.vega.m.echo_call.1.start_pieces"));

        // 1 — flying out. Nothing is called on the ground, and not at once in flight.
        server.AdvanceUptimeForTest(120);
        server.TickSkyLightMissionsForTest(p);
        Assert.Null(server.EchoCallFlightForTest(id));
        Fly(server, p);
        server.TickSkyLightMissionsForTest(p);
        Assert.True(Has(p, StepDone("m_echo_call.1")));
        Assert.Equal("m_echo_call.2", server.MissionChipStepForTest(p));
        Assert.Equal(1, Count(t, p, "story.vega.m.echo_call.2.start"));

        server.TickSkyLightMissionsForTest(p); // the flight is noted from here on
        var flight = server.EchoCallFlightForTest(id)!.Value;
        Assert.False(flight.Summoned);
        Assert.True(flight.Since > 0);
        server.AdvanceUptimeForTest(SvGameServer.EchoCallFlightSeconds - 2);
        server.TickSkyLightMissionsForTest(p);
        Assert.Equal(0.0, server.SpaceBossArrivalInForTest(id));
        Assert.DoesNotContain("vega.cruiser.incoming", t.LineKeys(p));

        // After about 25 seconds of the flight VEGA warns: the story's cruiser is on its way — once.
        server.AdvanceUptimeForTest(3);
        server.TickSkyLightMissionsForTest(p);
        Assert.True(server.EchoCallFlightForTest(id)!.Value.Summoned);
        Assert.InRange(server.SpaceBossArrivalInForTest(id), EscortCruiserRules.WarnSeconds - 0.5, EscortCruiserRules.WarnSeconds);
        Assert.Equal(1, Count(t, p, "vega.cruiser.incoming"));
        server.TickSkyLightMissionsForTest(p);
        Assert.Equal(1, Count(t, p, "vega.cruiser.incoming"));

        // It comes out of hyperspace with ECHO aboard; the arrival card and VEGA's line, once.
        Run(server, EscortCruiserRules.WarnSeconds + 2.0);
        var boss = server.SpaceBossForTest(id);
        Assert.NotNull(boss);
        Assert.True(boss!.Boss!.CarriesEcho);
        server.TickSkyLightMissionsForTest(p);
        Assert.Equal(1, Count(t, p, SvGameServer.EchoArrivalLine));
        Assert.Single(t.Plays(p), c => c == StoryCutsceneIds.CruiserArrives);
        var order = t.Order(p, V, StoryCutsceneIds.CruiserArrives, SvGameServer.EchoArrivalLine);
        Assert.True(order.Scene >= 0 && order.Scene < order.Line, "the card goes out before the line it shows");

        // The pilot flees: it gives up the chase, the step stays open — and this flight brings no second one.
        server.ShipMove(id, -3000f, 0f, 0f);
        Assert.True(server.InSpace(id));
        Run(server, EscortCruiserRules.LeaveSeconds + 6.0);
        Assert.Null(server.SpaceBossForTest(id));
        Assert.Contains("vega.cruiser.gone", t.LineKeys(p));
        Assert.True(server.InSpace(id)); // the pilot simply got away
        Assert.Equal("m_echo_call.2", server.MissionChipStepForTest(p));
        server.AdvanceUptimeForTest(SvGameServer.EchoCallFlightSeconds * 4);
        server.TickSkyLightMissionsForTest(p);
        Run(server, 3.0);
        Assert.Equal(0.0, server.SpaceBossArrivalInForTest(id));
        Assert.Null(server.SpaceBossForTest(id));
        Assert.Equal(1, Count(t, p, "vega.cruiser.incoming"));

        // A later flight (another system: a new flight instance) brings it again.
        string origin = server.Galaxy.FindBody(p.CurrentLocationId)!.SystemId;
        var target = server.Galaxy.Systems.First(s => s.Id != origin
            && s.Bodies.Any(b => !string.IsNullOrEmpty(b.PlanetType) && SpsTowerLifeWorlds.Content.GetPlanet(b.PlanetType!) is not null));
        server.HyperjumpToSystem(id, target.Id);
        Assert.True(server.InSpace(id));
        server.RollEscortCruiserForTest(id, dice: 1.0); // this flight's own rare ambush is decided: none comes
        server.ShipMove(id, 0f, 0f, 0f);
        Assert.Equal((false, 0.0), server.EchoCallFlightForTest(id)!.Value);
        server.TickSkyLightMissionsForTest(p);
        server.AdvanceUptimeForTest(SvGameServer.EchoCallFlightSeconds + 1);
        server.TickSkyLightMissionsForTest(p);
        Assert.Equal(2, Count(t, p, "vega.cruiser.incoming"));
        Run(server, EscortCruiserRules.WarnSeconds + 2.0);
        Assert.True(server.SpaceBossForTest(id)!.Boss!.CarriesEcho);
        server.TickSkyLightMissionsForTest(p);
        Assert.Equal(1, Count(t, p, SvGameServer.EchoArrivalLine)); // VEGA recognised that hull the first time

        // 2 — the cruiser is beaten: its wreck carries ECHO.
        string wreck = ShootDown(server, p);
        Assert.True(Has(p, StepDone("m_echo_call.2")));
        Assert.True(server.CruiserWreckRecordForTest(wreck)!.CarriesEcho);
        Assert.Equal("m_echo_call.3", server.MissionChipStepForTest(p));
        Assert.Equal(1, Count(t, p, "story.vega.m.echo_call.3.start"));
        server.AdvanceUptimeForTest(SvGameServer.EchoCallFlightSeconds * 4);
        server.TickSkyLightMissionsForTest(p);
        Assert.Equal(0.0, server.SpaceBossArrivalInForTest(id)); // her wreck waits: no second cruiser of hers

        // 3 — aboard the wreck.
        Board(server, p, wreck);
        Assert.True(Has(p, StepDone("m_echo_call.3")));
        Assert.Equal("m_echo_call.4", server.MissionChipStepForTest(p));
        Assert.Equal(1, Count(t, p, "story.vega.m.echo_call.4.start"));

        // 4 — a hacking chip at the bridge console: ECHO speaks, the mission is done, VEGA keeps her goodbye.
        Assert.False(server.EvaluateStoryConditionForTest("world:echo_freed", p));
        p.State.Inventory.Add(Chip, 1, 16);
        Assert.True(server.UseBridgeConsoleForTest(id));
        Assert.True(Has(p, StepDone("m_echo_call.4")));
        Assert.True(Has(p, Done("m_echo_call")));
        FlushScenes(server, p);
        Assert.Contains(CruiserWreckRules.EchoCutscene, t.Plays(p));
        Assert.Equal(SvGameServer.EchoLines.Concat(SvGameServer.EchoVegaLines).OrderBy(k => k, StringComparer.Ordinal),
            t.LineKeys(p).Where(k => k.StartsWith("story.vega.echo.", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(1, Count(t, p, "story.vega.insight.echo"));
        Assert.True(server.EvaluateStoryConditionForTest("world:echo_freed", p));
        Assert.False(server.EchoFreedKeyWrittenForTest);
        server.TickSkyLightMissionsForTest(p);
        Assert.True(server.EchoFreedKeyWrittenForTest); // the save remembers it without the wreck's record
    }

    [Fact]
    public void InMultiplayer_AnotherPilotsFightAndFreeing_PassAPlayerOn_AndALateMissionIsDoneAtOnce()
    {
        var (server, t) = _worlds.Start("slm_echo_mp", configure: SpaceBossRules);
        var ann = SpsTowerLifeWorlds.Join(server, 1, "Ann");
        var ben = SpsTowerLifeWorlds.Join(server, 2, "Ben");
        var cara = SpsTowerLifeWorlds.Join(server, 3, "Cara");
        foreach (string piece in SvGameServer.SpsTowerEchoTexts)
        {
            ben.State.Milestones.Add("lore:" + piece); // Ben read all three pieces of her call at the towers
        }

        SpsTowerLifeWorlds.AdvanceTo(server, 11);
        SkipTo(server, ann, "m_echo_call");
        SkipTo(server, ben, "m_echo_call");
        Settle(server, cara);
        Assert.Equal(new[] { "m_jump" }, server.RunningMissionsForTest(cara)); // Cara is still at the jump generator
        Assert.Equal(1, Count(t, ben, "story.vega.m.echo_call.1.start_pieces")); // the pieces fit together
        Assert.Equal(0, Count(t, ben, "story.vega.m.echo_call.1.start"));
        Assert.Equal(1, Count(t, ann, "story.vega.m.echo_call.1.start"));

        Fly(server, ann);
        Fly(server, ben, 60f);
        server.TickSkyLightMissionsForTest(ann);
        server.TickSkyLightMissionsForTest(ben);
        Assert.Equal("m_echo_call.2", server.MissionChipStepForTest(ann));
        Assert.Equal("m_echo_call.2", server.MissionChipStepForTest(ben));

        // Ann fights alone: the defeat is hers, the wreck is everybody's — Ben is passed on to the boarding.
        Assert.NotNull(server.SpawnEscortCruiserForTest(Id(ann), carriesEcho: true));
        string wreck = ShootDown(server, ann);
        Assert.True(Has(ann, StepDone("m_echo_call.2")));
        Assert.False(Has(ben, StepDone("m_echo_call.2")));
        server.TickSkyLightMissionsForTest(ben);
        Assert.True(Has(ben, StepDone("m_echo_call.2")));
        Assert.Equal("m_echo_call.3", server.MissionChipStepForTest(ben));
        Assert.Equal(1, Count(t, ben, "story.vega.m.echo_call.3.start"));
        server.AdvanceUptimeForTest(SvGameServer.EchoCallFlightSeconds * 4);
        server.TickSkyLightMissionsForTest(ben);
        Assert.Equal(0.0, server.SpaceBossArrivalInForTest(Id(ben))); // no cruiser of hers comes for him: her wreck waits

        // Ann frees ECHO while Ben is still out there: his last steps are through — and VEGA tells him.
        Board(server, ann, wreck);
        ann.State.Inventory.Add(Chip, 1, 16);
        Assert.True(server.UseBridgeConsoleForTest(Id(ann)));
        Assert.True(Has(ann, Done("m_echo_call")));
        Assert.False(Has(ben, Done("m_echo_call")));
        server.TickSkyLightMissionsForTest(ben);
        Assert.True(Has(ben, Done("m_echo_call")));
        Assert.False(Has(ben, SvGameServer.EchoFreedMilestone)); // he was not aboard: the event was never his
        Assert.Equal(1, Count(t, ben, "story.vega.insight.echo"));
        Assert.Equal(0, Count(t, ben, "story.vega.m.echo_call.4.start"));

        // Cara gets the mission afterwards: done the moment its turn comes, with VEGA's one line and nothing else.
        Deliver(server, cara, new StoryPackIntent { PackId = V, Action = StoryPackActions.SkipChapter, ChapterId = "m_jump" });
        Settle(server, cara);
        Assert.True(Has(cara, Done("m_echo_call")));
        Assert.Empty(MissionLines(t, cara, "m_echo_call"));
        Assert.Equal(1, Count(t, cara, "story.vega.insight.echo"));

        // Never again in this save: a pilot in flight is not sent another cruiser of hers.
        Fly(server, cara, 120f);
        server.TickSkyLightMissionsForTest(cara);
        server.AdvanceUptimeForTest(SvGameServer.EchoCallFlightSeconds * 4);
        server.TickSkyLightMissionsForTest(cara);
        server.TickSkyLightMissionsForTest(ben);
        Assert.Equal(0.0, server.SpaceBossArrivalInForTest(Id(cara)));
        Assert.True(server.EchoFreedKeyWrittenForTest);
    }

    [Fact]
    public void HerCruiser_NeverComesInTheGuardiansSystem_AndACruiserThatIsAlreadyThere_BecomesHers()
    {
        var (server, t, pilots) = EchoWorld("slm_echo_gates", "Core", "Hunted");
        var core = pilots[0];
        var hunted = pilots[1];

        // The finale opens (chapter 7): the kept mission runs on — but over the Guardian's world no cruiser is called.
        server.AdminRevealFinale();
        server.TickStoryMissionsForTest(core);
        Assert.Contains("m_echo_call", server.RunningMissionsForTest(core));
        core.State.GodMode = true;
        server.JumpIntoGuardianSystemForTest(core);
        Assert.True(server.InSpace(Id(core)));
        server.TickSkyLightMissionsForTest(core); // flying: the first step is through
        Assert.Equal("m_echo_call.2", server.MissionChipStepForTest(core));
        server.TickSkyLightMissionsForTest(core);
        server.AdvanceUptimeForTest(SvGameServer.EchoCallFlightSeconds * 4);
        server.TickSkyLightMissionsForTest(core);
        Assert.Equal((false, 0.0), server.EchoCallFlightForTest(Id(core))!.Value);
        Assert.Equal(0.0, server.SpaceBossArrivalInForTest(Id(core)));

        // Elsewhere an escort cruiser is there already (the rare ambush): it is the first one this pilot meets — hers.
        Fly(server, hunted);
        server.TickSkyLightMissionsForTest(hunted);
        Assert.Equal("m_echo_call.2", server.MissionChipStepForTest(hunted));
        var boss = server.SpawnEscortCruiserForTest(Id(hunted), carriesEcho: false)!;
        Assert.False(boss.Boss!.CarriesEcho);
        server.TickSkyLightMissionsForTest(hunted);
        Assert.True(boss.Boss.CarriesEcho);
        Assert.False(server.EchoCallFlightForTest(Id(hunted))!.Value.Summoned); // no second one is called
        Assert.Equal(1, Count(t, hunted, SvGameServer.EchoArrivalLine));
        string wreck = ShootDown(server, hunted);
        Assert.True(server.CruiserWreckRecordForTest(wreck)!.CarriesEcho);
        Assert.True(Has(hunted, StepDone("m_echo_call.2")));
    }

    // ---------------------------------------------------------------- the scenes

    [Fact]
    public void TheFourScenes_AreDefinedAndListed_AndStageWhatTheCodePlays()
    {
        string[] ids = { StoryCutsceneIds.TowerMessage, StoryCutsceneIds.TankOpens, StoryCutsceneIds.CruiserArrives, StoryCutsceneIds.EchoSpeaks };
        Assert.Equal(new[] { SvGameServer.SpsTowerMessageCutscene, SvGameServer.SleeperTankCutscene, "cruiser.arrives", CruiserWreckRules.EchoCutscene }, ids);
        var en = SpsTowerLifeWorlds.PackTable("en");
        var de = SpsTowerLifeWorlds.PackTable("de");
        foreach (string id in ids)
        {
            Assert.Contains(id, StoryCutsceneIds.Vega);
            var scene = Vega.FindCutscene(id);
            Assert.NotNull(scene);
            Assert.True(scene!.Options.Skippable, id);
            Assert.InRange(scene.Tier, CutsceneTiers.Card, CutsceneTiers.Scene); // short: a card or a staged moment, never a set piece
            Assert.False(scene.Template);
            foreach (var caption in scene.Shots.SelectMany(s => s.Captions))
            {
                foreach (string key in new[] { caption.Key, caption.KickerKey }.Where(k => k.Length > 0))
                {
                    Assert.True(en.ContainsKey(key) && de.ContainsKey(key), $"{id}: {key}");
                }
            }
        }

        // No step and no beat binds them: the game's code plays them at their place.
        Assert.DoesNotContain(Vega.Beats, b => ids.Contains(b.Cutscene));
        Assert.DoesNotContain(Vega.Chapters, c => ids.Contains(c.OpenCutscene));
        Assert.DoesNotContain(Vega.Chapters.SelectMany(c => c.Missions).SelectMany(m => m.Steps),
            s => ids.Contains(s.OnStart.Cutscene) || ids.Contains(s.OnComplete.Cutscene));

        // The two at a tower are staged at the place the code sends (the console, the tank) and never wait for "at home".
        // They belong to no chapter (a tower is lit and a tank opened whenever a player has a chip) and name nobody.
        foreach (string id in new[] { StoryCutsceneIds.TowerMessage, StoryCutsceneIds.TankOpens })
        {
            var scene = Vega.FindCutscene(id)!;
            Assert.Equal(CutsceneTiers.Scene, scene.Tier);
            Assert.All(scene.Shots, s => Assert.Equal(CutsceneAnchors.Custom, s.Camera.Anchor));
            Assert.True(scene.Options.PlayInPlace, id);
            Assert.Equal(string.Empty, scene.Chapter);
            Assert.True(scene.Shots.Sum(s => s.Duration) <= 20f, id);
            foreach (var caption in scene.Shots.SelectMany(s => s.Captions))
            {
                foreach (string word in new[] { "Guardian", "clone", "imprint", "ECHO" })
                {
                    Assert.DoesNotContain(word, en[caption.Key], StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        var tower = Vega.FindCutscene(StoryCutsceneIds.TowerMessage)!;
        Assert.Equal(3, tower.Shots.SelectMany(s => s.Captions).Count(c => c.Style == CutsceneCaptionStyles.Subtitle)); // the console speaks
        Assert.DoesNotContain(tower.Shots.SelectMany(s => s.Captions), c => c.Style == CutsceneCaptionStyles.Speaker);
        var tank = Vega.FindCutscene(StoryCutsceneIds.TankOpens)!;
        Assert.Equal(new[] { SvGameServer.SleeperTankLine }, tank.ReplaceLineKeys); // captions only: the title and VEGA's one line
        Assert.Equal(new[] { SvGameServer.SleeperTankLine },
            tank.Shots.SelectMany(s => s.Captions).Where(c => c.Style == CutsceneCaptionStyles.Speaker).Select(c => c.Key));

        // The two in space and aboard are cards: the live view is the picture, the controls stay the pilot's.
        var arrives = Vega.FindCutscene(StoryCutsceneIds.CruiserArrives)!;
        var speaks = Vega.FindCutscene(StoryCutsceneIds.EchoSpeaks)!;
        foreach (var card in new[] { arrives, speaks })
        {
            Assert.Equal(CutsceneTiers.Card, card.Tier);
            Assert.False(card.TakesCamera);
            Assert.False(card.EffectiveLockInput);
            Assert.False(card.EffectiveHoldWorld);
            Assert.Equal("sleeps", card.Chapter);
        }

        Assert.True(arrives.Options.CooldownExempt); // now or never: the cruiser does not wait for a card
        Assert.Equal(new[] { SvGameServer.EchoArrivalLine }, arrives.ReplaceLineKeys);
        Assert.Contains("escort wing", en[SvGameServer.EchoArrivalLine], StringComparison.Ordinal);
        Assert.Contains("Geleitstaffel", de[SvGameServer.EchoArrivalLine], StringComparison.Ordinal);

        // ECHO's goodbye: her five lines in her own box, VEGA's two answers in hers — the order the bridge code speaks them.
        string[] spoken =
        {
            SvGameServer.EchoLines[0], SvGameServer.EchoLines[1], SvGameServer.EchoVegaLines[0], SvGameServer.EchoLines[2],
            SvGameServer.EchoLines[3], SvGameServer.EchoLines[4], SvGameServer.EchoVegaLines[1],
        };
        var said = speaks.Shots.SelectMany(s => s.Captions).Where(c => c.Style == CutsceneCaptionStyles.Speaker).ToList();
        Assert.Equal(spoken, said.Select(c => c.Key));
        Assert.Equal(spoken, speaks.ReplaceLineKeys);
        Assert.All(said, c => Assert.Equal(SvGameServer.EchoLines.Contains(c.Key) ? "echo" : string.Empty, c.Speaker));
        Assert.All(speaks.Shots, s => Assert.InRange(s.Captions.Count, 1, 1)); // one page per shot
        var echo = Assert.Single(Vega.Speakers, s => s.Id == "echo");
        Assert.Equal("chr.echo.name", echo.NameKey);
        Assert.Equal("ECHO", SpsTowerLifeWorlds.EngineTable("en")[echo.NameKey]);
    }
}
