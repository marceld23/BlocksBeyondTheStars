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
/// #3685 — the guardian giants in "The VEGA Protocol": chapter 4's mission "The Fallen Giant" (after The Long Quiet, find a lying
/// giant, scan it, VEGA remembers the builders — a beat card, kept in the Journal's VEGA notes) and its stand-in on a world
/// without giants; a defeated giant's score share (once per giant, inside the kill cap) and the fighter's next memory; the memory
/// core read into the Journal; VEGA's giant lines by what the player's story knows; the predicates and the found event.
/// </summary>
public sealed class VegaGuardianGiantStoryTests : IDisposable
{
    private const string V = StoryRegistry.DefaultStoryId;
    private const string Insight = "story:insight:guardian-giant";
    private const string InsightLine = "story.vega.insight.guardian_giant";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_vega_giants_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

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

    private (SvGameServer Server, RecordingTransport Transport) World(string world, Action<ServerConfig>? configure = null, GameContent? content = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var transport = new RecordingTransport();
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            ViewDistanceChunks = 1,
            MaxPlayers = 6,
            PlaceStarterShip = false,
        };
        config.Rules.TutorialPolicy = TutorialPolicy.Off;
        configure?.Invoke(config);
        var server = new SvGameServer(config, content ?? _content, transport, repo);
        server.Start();
        return (server, transport);
    }

    /// <summary>The real join path of a current client (packs1 + cutscenes1), on foot.</summary>
    private static PlayerSession Join(SvGameServer server, int conn, string name)
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

    /// <summary>Every queued story scene went out, then the 1 Hz mission pass ran.</summary>
    private static void Settle(SvGameServer server, PlayerSession p)
    {
        for (int i = 0; i < 40 && (i < 4 || p.Story.CutsceneQueue.Count > 0); i++)
        {
            server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
            server.TickCutscenesForTest(p);
        }

        server.TickStoryMissionsForTest(p);
    }

    /// <summary>Adds the milestones the world still needs to reveal beat <paramref name="beat"/>.</summary>
    private void AdvanceTo(SvGameServer server, int beat)
    {
        int target = (_content.Stories[V].Beats[beat].Threshold + 1) / 2;
        int missing = target - server.StorySnapshot.Milestones;
        if (missing > 0)
        {
            server.AdminAdvanceStory(missing);
        }

        Assert.True(server.StorySnapshot.BeatsRevealed > beat, $"B{beat} was not revealed");
    }

    /// <summary>A pack copy whose two derelict missions this world can never offer (the giant's turn comes at once).</summary>
    private static GameContent WithoutTheDerelictMissions()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        foreach (var mission in content.Stories[V].Chapters.Single(c => c.Id == "verdict").Missions.Where(m => m.Id is "m_long_quiet" or "m_wrecks"))
        {
            mission.FeasibleIf = new List<string> { "world:creative" };
        }

        return content;
    }

    /// <summary>A giant laid on flat ground some way off the player (who waits far away meanwhile), facing +Z.</summary>
    private static GuardianGiantRecord Lay(SvGameServer server, PlayerSession p, bool canWake = false)
    {
        int px = (int)p.State.Position.X, pz = (int)p.State.Position.Z;
        var spot = (from r in Enumerable.Range(3, 10)
                    from a in Enumerable.Range(0, 16)
                    let x = px + (int)Math.Round(Math.Cos(a * Math.PI / 8) * r * 16)
                    let z = pz + (int)Math.Round(Math.Sin(a * Math.PI / 8) * r * 16)
                    let spread = server.HunterSpotSpreadForTest(x, z)
                    where spread is >= 0 and <= 1
                    select (X: x, Z: z)).First();
        p.State.Position = new Vector3f(spot.X + 200.5f, 80f, spot.Z + 0.5f);
        return server.LayGuardianGiantForTest(spot.X, spot.Z, facing: 0, canWake);
    }

    /// <summary>Stands the player on the ground beside the lying body, <paramref name="distance"/> from its centre.</summary>
    private static void StandBeside(SvGameServer server, PlayerSession p, GuardianGiantRecord rec, float distance)
    {
        var (cx, cz) = GuardianGiantBody.LyingCentre(rec.Facing);
        int x = (int)Math.Floor(rec.X + cx + distance), z = (int)Math.Floor(rec.Z + cz);
        p.State.Position = new Vector3f(x + 0.5f, server.GroundFeetForTest(x, z), z + 0.5f);
    }

    /// <summary>A cell of the lying body that really holds its block (the stamp only fills air and plants).</summary>
    private (Vector3i Cell, string Block) BodyCell(SvGameServer server, GuardianGiantRecord rec)
        => GuardianGiantBody.Lying(rec.Seed, lit: false, rec.Facing)
            .Select(c => (Cell: new Vector3i(rec.X + c.Dx, rec.Y + c.Dy, rec.Z + c.Dz), c.Block))
            .First(c => _content.BlockById(server.World.GetBlock(c.Cell))?.Key == c.Block);

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private static string Done(string mission) => StoryPackKeys.ChapterDone(V, mission);

    private static string Skipped(string mission) => StoryPackKeys.ChapterSkipped(V, mission);

    private static Dictionary<string, string> PackTable(string code)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(TestPaths.DataDir(), "stories", V, "locales", code + ".json")),
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip })!;

    // ---------------- The mission ----------------

    [Fact]
    public void TheFallenGiant_ComesAfterTheLongQuiet_AndIsDoneByFindingAndScanningAGiant_ThenVegaRemembers()
    {
        var (server, t) = World("g_mission", c => c.Rules.InstantTravel = true);
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, 6); // chapter 4, "The Verdict"
        Settle(server, p);
        Assert.True(server.EvaluateStoryConditionForTest("world:guardian_giants", p)); // a world of the giants' generation

        // The derelict's mission first (or its stand-in, where the derelict cannot be reached) — the giant never replaces it.
        string first = Assert.Single(server.RunningMissionsForTest(p));
        Assert.Contains(first, new[] { "m_long_quiet", "m_wrecks" });
        if (first == "m_long_quiet")
        {
            server.RaiseGameEventForTest(p, GameEventKeys.StationBoarded, SvGameServer.DerelictStationId);
            server.RevealLoreForTest("Pilot", "derelict");
        }
        else
        {
            server.RaiseGameEventForTest(p, GameEventKeys.ScanAny, "wreck");
        }

        Assert.True(Has(p, Done(first)));
        server.TickStoryMissionsForTest(p);
        Assert.Equal(new[] { "m_fallen_giant" }, server.RunningMissionsForTest(p));
        Assert.Contains("story.vega.m.fallen_giant.1.start", t.LineKeys(p));
        Assert.Equal("m_fallen_giant.1", server.MissionChipStepForTest(p));

        // Found: the player comes within the found range of a lying giant.
        var rec = Lay(server, p);
        server.TickGuardianGiantStoryForTest(p);
        Assert.False(Has(p, StoryPackKeys.StepDone(V, "m_fallen_giant.1"))); // far away still
        StandBeside(server, p, rec, 30f);
        server.TickGuardianGiantStoryForTest(p);
        Assert.True(Has(p, StoryPackKeys.StepDone(V, "m_fallen_giant.1")));
        Assert.Contains("story.vega.m.fallen_giant.1.done", t.LineKeys(p));
        Assert.Equal("m_fallen_giant.2", server.MissionChipStepForTest(p));

        // Examined: any block of its body reads as the giant; VEGA remembers — the card, then her line, kept in her notes.
        var (cell, block) = BodyCell(server, rec);
        var result = server.ScanSubject("Pilot", "block", block, null, cell);
        Assert.Equal(("guardian_giant", "ui.scan.guardian_giant", true), (result.Kind, result.InfoKey, result.FirstTime));
        Assert.True(result.KnowledgeGained > 0);
        Assert.True(Has(p, Done("m_fallen_giant")));
        Assert.Contains("story.vega.m.fallen_giant.2.done", t.LineKeys(p));
        Assert.True(Has(p, Insight));
        Assert.Equal(1, server.StoryStateForTest("Pilot")!.NoteKeys.Count(k => k == InsightLine)); // the Journal keeps it once
        Settle(server, p); // the card goes out, and her line right after it (#2473: a line its queued scene shows waits for it)
        var (scene, line) = t.Order(p, V, "giant.remembers", InsightLine);
        Assert.True(scene >= 0 && line > scene, $"card at {scene}, line at {line}");
        Assert.Equal(1, t.LineKeys(p).Count(k => k == InsightLine));

        // A second scan of the same giant is no new discovery and no new memory.
        var again = server.ScanSubject("Pilot", "block", block, null, cell);
        Assert.False(again.FirstTime);
        Assert.Equal(1, t.LineKeys(p).Count(k => k == InsightLine));
    }

    [Fact]
    public void AWorldWithoutGiants_GetsTheEcho_AWreckScan_AndTheSameMemoryAtItsEnd()
    {
        var (server, t) = World("g_echo", c => c.World.TerrainGeneration = WorldDescription.GuardianGiantsGeneration - 1, WithoutTheDerelictMissions());
        var p = Join(server, 1, "Pilot");
        Assert.False(server.EvaluateStoryConditionForTest("world:guardian_giants", p));
        AdvanceTo(server, 6);
        Settle(server, p);
        Assert.True(Has(p, Skipped("m_long_quiet")));
        Assert.True(Has(p, Skipped("m_fallen_giant")));
        Assert.Equal(new[] { "m_giant_echo" }, server.RunningMissionsForTest(p));
        Assert.Equal("m_giant_echo.1", server.MissionChipStepForTest(p));
        Assert.Contains("story.vega.m.giant_echo.1.start", t.LineKeys(p));

        server.RaiseGameEventForTest(p, GameEventKeys.ScanPerformed, "asteroid"); // not a wreck
        Assert.False(Has(p, Done("m_giant_echo")));
        server.RaiseGameEventForTest(p, GameEventKeys.ScanPerformed, "wreck");
        Assert.True(Has(p, Done("m_giant_echo")));
        Assert.True(Has(p, Insight)); // the insight of the mission's end — the same line, the same once-key
        Assert.Equal(new[] { InsightLine }, server.StoryStateForTest("Pilot")!.NoteKeys.Where(k => k == InsightLine));
        Settle(server, p);
        var (scene, line) = t.Order(p, V, "giant.remembers", InsightLine);
        Assert.True(scene >= 0 && line > scene, $"card at {scene}, line at {line}");
        Assert.Equal(1, t.LineKeys(p).Count(k => k == InsightLine));
    }

    [Fact]
    public void WithoutFreeFlight_TheEchosStandIn_IsStoppingAGuardianMachine()
    {
        var (server, t) = World("g_echo_machines", c =>
        {
            c.World.TerrainGeneration = WorldDescription.GuardianGiantsGeneration - 1;
            c.Rules.FreeSpaceFlight = false;
            c.Rules.PlanetEnemies = AlienActivity.Normal;
        }, WithoutTheDerelictMissions());
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, 6);
        Settle(server, p);
        Assert.Equal(new[] { "m_giant_echo" }, server.RunningMissionsForTest(p));
        Assert.Equal("m_giant_echo.1a", server.MissionChipStepForTest(p));

        server.RaiseGameEventForTest(p, GameEventKeys.DefeatAny, GameEventKeys.DefeatMachine);
        Assert.True(Has(p, Done("m_giant_echo")));
        Assert.Contains("story.vega.m.giant_echo.1a.done", t.LineKeys(p));
        Assert.True(Has(p, Insight));
    }

    // ---------------- The score, the memory, the core ----------------

    [Fact]
    public void ADefeatedGiant_CountsFiveKillsOncePerGiant_InsideTheCap_AndUnlocksEachFightersNextMemoryOnce()
    {
        var (server, _) = World("g_defeat");
        var p = Join(server, 1, "Pilot");
        var q = Join(server, 2, "Wingmate");
        int kills = server.StorySnapshot.Kills;

        server.RaiseGameEventForTest(p, GameEventKeys.GuardianGiantDefeated, "rocky#0");
        server.RaiseGameEventForTest(q, GameEventKeys.GuardianGiantDefeated, "rocky#0"); // the same giant, another fighter
        Assert.Equal(kills + SvGameServer.GuardianGiantStoryKills, server.StorySnapshot.Kills);
        Assert.Equal(1, server.PlayerMemoryCount("Pilot"));
        Assert.Equal(1, server.PlayerMemoryCount("Wingmate"));

        server.RaiseGameEventForTest(p, GameEventKeys.GuardianGiantDefeated, "rocky#0"); // never twice
        Assert.Equal(kills + SvGameServer.GuardianGiantStoryKills, server.StorySnapshot.Kills);
        Assert.Equal(1, server.PlayerMemoryCount("Pilot"));

        server.RaiseGameEventForTest(p, GameEventKeys.GuardianGiantDefeated, "rocky#1"); // another giant
        Assert.Equal(kills + (2 * SvGameServer.GuardianGiantStoryKills), server.StorySnapshot.Kills);
        Assert.Equal(2, server.PlayerMemoryCount("Pilot"));

        // The kills count like any machine's: the pack's cap bounds their share of the score.
        var vega = _content.Stories[V];
        Assert.Equal(vega.KillContributionCap * vega.KillWeight,
            StoryEngine.Progress(vega, new StoryState { MachineKills = vega.KillContributionCap - 2 + SvGameServer.GuardianGiantStoryKills }, 1f));
    }

    [Fact]
    public void TheMemoryCore_IsAnItemWithAnIcon_AndCarryingItReadsItsLastOrderIntoTheJournal_Once()
    {
        var item = Assert.IsType<ItemDefinition>(_content.GetItem(SvGameServer.GuardianGiantCoreItem));
        Assert.InRange(item.MaxStack, 1, 8);
        string icon = Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "Resources", "icons", "item_" + item.Key + ".png");
        Assert.True(File.Exists(icon) && File.Exists(icon + ".meta"), icon);
        Assert.Single(_content.Stories[V].LoreSites, l => l.Site == SvGameServer.GuardianGiantCoreLoreSite);

        var (server, t) = World("g_core");
        var p = Join(server, 1, "Pilot");
        server.TickGuardianGiantStoryForTest(p);
        Assert.Empty(t.To<LoreTextRevealed>(p)); // nothing carried, nothing read

        p.State.Inventory.Add(SvGameServer.GuardianGiantCoreItem, 1, item.MaxStack);
        server.TickGuardianGiantStoryForTest(p);
        server.TickGuardianGiantStoryForTest(p);
        var read = Assert.Single(t.To<LoreTextRevealed>(p));
        Assert.Equal(("giant_core_order", "guardian_giant_core", "lore.site.giant_core_order"), (read.Key, read.Site, read.TextKey));
        Assert.Contains("giant_core_order", server.StoryStateForTest("Pilot")!.FoundLoreKeys); // the Journal's field records
    }

    // ---------------- VEGA's lines ----------------

    [Fact]
    public void VegasGiantLines_FollowWhatThePlayersStoryKnows_AndNeverNameTheGuardianEarly()
    {
        var (server, _) = World("g_lines");
        var p = Join(server, 1, "Pilot");
        string Key(GuardianGiantVegaMoment moment) => server.StoryGuardianGiantVegaKeyForTest(p, moment, "vega.giant.neutral");

        Assert.Equal("story.vega.giant.found.puzzled", Key(GuardianGiantVegaMoment.Found));
        Assert.Equal("vega.giant.neutral", Key(GuardianGiantVegaMoment.Rage)); // a moment without story lines keeps the neutral one
        AdvanceTo(server, 2);
        Assert.Equal("story.vega.giant.stir.service", Key(GuardianGiantVegaMoment.Stir));
        AdvanceTo(server, 5);
        Assert.Equal("story.vega.giant.rise.guardian", Key(GuardianGiantVegaMoment.Rise));
        server.MarkGuardianDefeatedForTest();
        Assert.Equal("story.vega.giant.victory.silent", Key(GuardianGiantVegaMoment.Victory));

        // A player whose own story has not heard the Service or the Guardian yet never hears them named — whatever the crew
        // knows and even after the finale (here: a joiner whose beats are held back).
        var late = Join(server, 2, "Latecomer");
        late.State.Milestones.RemoveWhere(m => m.StartsWith(StoryPackKeys.Prefix(V) + "beat:", StringComparison.Ordinal));
        Assert.Equal("story.vega.giant.found.puzzled", server.StoryGuardianGiantVegaKeyForTest(late, GuardianGiantVegaMoment.Found, "n"));

        // Every story line exists in English and German; the puzzled and the Service's never name the Guardian.
        var en = PackTable("en");
        var de = PackTable("de");
        foreach (var moment in Enum.GetValues<GuardianGiantVegaMoment>().Where(SvGameServer.GuardianGiantMomentHasStoryLines))
        {
            foreach (string level in new[] { "puzzled", "service", "guardian", "silent" })
            {
                string key = "story.vega.giant." + SvGameServer.GuardianGiantMomentWord(moment) + "." + level;
                Assert.True(en.ContainsKey(key) && de.ContainsKey(key), key);
                if (level is "puzzled" or "service")
                {
                    Assert.DoesNotContain("Guardian", en[key], StringComparison.Ordinal);
                    Assert.DoesNotContain("Wächter", de[key], StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void WithoutTheVegaStory_TheGiantLinesStayNeutral()
    {
        var (server, _) = World("g_none", c => c.Rules.StoryId = StoryRegistry.NoneStoryId);
        var p = Join(server, 1, "Pilot");
        Assert.Equal("vega.giant.found", server.StoryGuardianGiantVegaKeyForTest(p, GuardianGiantVegaMoment.Found, "vega.giant.found"));

        int kills = server.StorySnapshot.Kills;
        server.RaiseGameEventForTest(p, GameEventKeys.GuardianGiantDefeated, "rocky#0"); // no story: nothing to count or remember
        Assert.Equal(kills, server.StorySnapshot.Kills);
        Assert.Equal(0, server.PlayerMemoryCount("Pilot"));
    }

    // ---------------- The predicates and the found event ----------------

    [Fact]
    public void ThePredicates_ReadTheWorldsGeneration_TheRecords_AndTheFoundRange_AndFoundIsRaisedOncePerApproach()
    {
        var (older, _) = World("g_older", c => c.World.TerrainGeneration = WorldDescription.GuardianGiantsGeneration - 1);
        var o = Join(older, 1, "Pilot");
        Assert.False(older.EvaluateStoryConditionForTest("world:guardian_giants", o));
        var rec = Lay(older, o);
        Assert.True(older.EvaluateStoryConditionForTest("world:guardian_giants", o)); // a giant lies here after all (admin, a test)
        Assert.False(older.EvaluateStoryConditionForTest("world:guardian_giant_near", o));
        StandBeside(older, o, rec, GuardianGiantRules.FoundRange - 4f);
        Assert.True(older.EvaluateStoryConditionForTest("world:guardian_giant_near", o));

        // The found event: only while somebody listens, once per approach.
        var heard = new List<string>();
        older.TickGuardianGiantStoryForTest(o);
        Assert.Empty(heard);
        var listener = new Recorder(heard);
        older.AddGameEventListener(GameEventKeys.GuardianGiantFound, listener);
        older.TickGuardianGiantStoryForTest(o);
        older.TickGuardianGiantStoryForTest(o);
        Assert.Equal(new[] { rec.SiteId }, heard);
        StandBeside(older, o, rec, GuardianGiantRules.FoundRange + 30f);
        older.TickGuardianGiantStoryForTest(o);
        StandBeside(older, o, rec, 25f);
        older.TickGuardianGiantStoryForTest(o);
        Assert.Equal(new[] { rec.SiteId, rec.SiteId }, heard);
        older.RemoveGameEventListener(GameEventKeys.GuardianGiantFound, listener);
    }

    private sealed class Recorder : IGameEventListener
    {
        private readonly List<string> _heard;

        public Recorder(List<string> heard) => _heard = heard;

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount) => _heard.Add(arg);
    }
}
