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
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2475 — step 16 of the story packs (#2459): VEGA's chapter leads, the chapter bonus and the peaceful-world pacing (synthesis
/// §4.7–§4.9, option O2 "the score is the clock, the chapter leads are the compass"). The data (the leads per chapter, the bonus,
/// the factor), the lead vocabulary and its validator rules, each lead type's resolution — feasible here or not, a stand-in when
/// not — per player in multiplayer, the chip's rank and keys (a current client <c>story.vega.obj.*</c>, an older one the old
/// <c>story.obj.*</c>), the Journal's tasks, the bonus once per save and chapter (never after B12), the one scale wrapper with its
/// clamped display and the one-time jump of an existing peaceful save, the memories' second way in, the Crystal workshop leaving
/// the score, the tutorial never feeding it, and that leads are resolved on events, not per tick.
/// </summary>
public sealed class VegaChapterLeadsTests : IDisposable
{
    private const string V = StoryRegistry.DefaultStoryId;
    private const string Tutorial = "tutorial";

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public VegaChapterLeadsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_vega_leads_" + Guid.NewGuid().ToString("N"));
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

    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m));
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m));
        }

        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }

        public List<T> To<T>(PlayerSession who) => Sent.Where(x => x.Conn == who.ConnectionId).Select(x => x.Msg).OfType<T>().ToList();

        public List<string> VegaPlays(PlayerSession who) => To<CutscenePlay>(who).Where(c => c.PackId == V).Select(c => c.CutsceneId).ToList();
    }

    private SqliteWorldRepository Repo(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return repo;
    }

    private (SvGameServer Server, RecordingTransport Transport) World(string world, Action<ServerConfig>? configure = null,
        SqliteWorldRepository? repo = null, GameContent? content = null)
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
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, content ?? _content, transport, repo ?? Repo(world));
        server.Start();
        return (server, transport);
    }

    private static void TutorialOff(ServerConfig c) => c.Rules.TutorialPolicy = TutorialPolicy.Off;

    /// <summary>A world whose rules field no machines (the peaceful / family presets), the tutorial off.</summary>
    private static void Peaceful(ServerConfig c)
    {
        TutorialOff(c);
        c.Rules.PlanetEnemies = AlienActivity.Off;
        c.Rules.SpaceCombat = SpaceCombatMode.Off;
    }

    /// <summary>The real join path. <paramref name="packs"/>: a current client (<c>packs1</c> + <c>cutscenes1</c>); without, an
    /// older client.</summary>
    private static PlayerSession Join(SvGameServer server, int conn, string name, bool packs = true)
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = packs ? new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 } : Array.Empty<string>(),
        }));
        return server.Sessions[conn];
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    /// <summary>Adds the milestones the world still needs to reveal beat <paramref name="beat"/> (Normal density, no fragments
    /// or kills) — on a world with machines, where the score is milestones × 2.</summary>
    private void AdvanceTo(SvGameServer server, int beat)
    {
        int threshold = Vega.Beats[beat].Threshold;
        int target = (threshold + 1) / 2;
        int missing = target - server.StorySnapshot.Milestones;
        if (missing > 0)
        {
            server.AdminAdvanceStory(missing);
        }

        Assert.True(server.StorySnapshot.BeatsRevealed > beat, $"B{beat} was not revealed");
    }

    /// <summary>Guarantees at least <paramref name="count"/> unread net fragments on the active world (the surface roll may be
    /// dry): walks data-terminal spots until the deterministic per-spot roll places them.</summary>
    private static void EnsureFragments(SvGameServer server, int count = 1)
    {
        for (int i = 0; i < 128 && server.NetFragmentCount < count; i++)
        {
            server.PlaceStructureFragmentForTest("data_terminal", 100f + (i * 7f), 40f, -80f + (i * 5f));
        }

        Assert.True(server.NetFragmentCount >= count, "the data_terminal markers did not roll enough fragments");
    }

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private StoryDefinition Vega => _content.Stories[V];

    private static string LeadKey(string chapter, int n) => StoryPackKeys.Lead(V, chapter, n);

    // ---------------- The data ----------------

    [Fact]
    public void TheVegaPack_CarriesTheLeadsOfSynthesis47_TheBonusAndThePacing()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["awakening"] = Array.Empty<string>(),
            ["signature"] = new[] { "fragment" },
            ["service"] = new[] { "unique_site:observatory", "fragment" },
            ["guardian"] = new[] { "monument", "settlement_help" },
            ["verdict"] = new[] { "unique_site:derelict", "heal_tank" },
            ["what_you_are"] = new[] { "research:radar_array", "settlement_help" },
            ["sleeps"] = new[] { "research:jump_generator", "module:jump_generator" },
            ["core"] = new[] { "finale" },
            ["resolution"] = new[] { "relay" },
        };
        foreach (var chapter in Vega.Chapters)
        {
            Assert.Equal(expected[chapter.Id], chapter.Leads.Select(l => l.Type));
            Assert.All(chapter.Leads, l => Assert.Null(StoryLeadTypes.Problem(l.Type)));
        }

        Assert.Equal(3, Vega.LeadBonusMilestones);
        Assert.True(Vega.NormaliseUnavailableDrivers);
        Assert.Equal(new[] { StoryMemoryTriggers.Lead, StoryMemoryTriggers.Monument }, Vega.MemoryTriggers);

        // The cap: only chapters that open before the last beat can pay — six of them, +6 points each = 36 before B12.
        int last = Vega.Beats.Count - 1;
        int paying = Vega.Chapters.Count(c => c.Leads.Count > 0 && c.FromBeat >= 1 && c.FromBeat < last);
        Assert.Equal(6, paying);
        Assert.Equal(36, paying * Vega.LeadBonusMilestones * Vega.MilestoneWeight);
    }

    [Fact]
    public void EveryLeadText_ExistsInEnglishAndGerman_AndFitsTheChip()
    {
        var en = PackTable("en");
        var de = PackTable("de");
        var leadKeys = en.Keys.Where(k => k.StartsWith(StoryLeads.ObjPrefix, StringComparison.Ordinal)).ToList();
        Assert.Equal(16, leadKeys.Count);
        foreach (string key in leadKeys)
        {
            Assert.True(de.ContainsKey(key), $"{key} has no German text");
            Assert.True(StoryPackValidator.ShownLength(en[key]) <= StoryPackValidator.ChipMaxEn, $"{key} (EN) is too long for the chip");
            Assert.True(StoryPackValidator.ShownLength(de[key]) <= StoryPackValidator.ChipMaxDe, $"{key} (DE) is too long for the chip");
            Assert.DoesNotContain("Story:", en[key], StringComparison.Ordinal); // the short name "VEGA:" labels it now
        }

        Assert.Equal(leadKeys.OrderBy(k => k), de.Keys.Where(k => k.StartsWith(StoryLeads.ObjPrefix, StringComparison.Ordinal)).OrderBy(k => k));

        // Every key the runtime can pick has a text: each chapter lead's, the stand-ins', the rules-aware variants.
        var picked = Vega.Chapters.SelectMany(c => StoryLeads.EffectiveLeads(c))
            .SelectMany(l => new[] { StoryLeads.ObjKey(l, true, false), StoryLeads.ObjKey(l, false, false), StoryLeads.ObjKey(l, true, true) })
            .Concat(new[] { StoryLeads.ExploreObjKey(true), StoryLeads.ExploreObjKey(false) })
            .Distinct();
        Assert.All(picked, k => Assert.True(en.ContainsKey(k), $"the runtime may pick '{k}', which has no text"));

        // The old keys stay as they were — older clients still read them.
        var core = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "en.json")),
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip })!;
        foreach (string old in new[] { "story.obj.fragment_on", "story.obj.fragment_here", "story.obj.search", "story.obj.help", "story.obj.help_peaceful", "story.obj.finale" })
        {
            Assert.True(core.ContainsKey(old), old);
        }
    }

    private static Dictionary<string, string> PackTable(string code)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(TestPaths.DataDir(), "stories", V, "locales", code + ".json")),
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip })!;

    [Theory]
    [InlineData("fragment", null)]
    [InlineData("unique_site:observatory", null)]
    [InlineData("unique_site:shrine", null)]
    [InlineData("unique_site:derelict", null)]
    [InlineData("research:radar_array", null)]
    [InlineData("module:jump_generator", null)]
    [InlineData("finale_jump", null)]
    [InlineData("explore", null)]
    [InlineData("", "no type")]
    [InlineData("dig_here", "unknown lead type")]
    [InlineData("unique_site", "needs an argument")]
    [InlineData("unique_site:castle", "unknown unique site")]
    [InlineData("fragment:frag_vega_signature", "takes no argument")]
    [InlineData("research", "needs an argument")]
    public void TheLeadVocabulary_KnowsItsWords_AndItsArguments(string type, string? problem)
    {
        string? actual = StoryLeadTypes.Problem(type);
        if (problem is null)
        {
            Assert.Null(actual);
        }
        else
        {
            Assert.Contains(problem, actual, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AChaptersEffectiveLeads_AreItsOwnThenTheStandInsItDoesNotList()
    {
        string Types(string chapter) => string.Join(",", StoryLeads.EffectiveLeads(Vega.Chapters.Single(c => c.Id == chapter)).Select(l => l.Type));
        Assert.Equal("fragment,settlement_help", Types("signature"));
        Assert.Equal("unique_site:observatory,fragment,settlement_help", Types("service"));
        Assert.Equal("monument,settlement_help,fragment", Types("guardian"));
        Assert.Equal("research:radar_array,settlement_help,fragment", Types("what_you_are"));
        Assert.Equal(1, StoryLeads.DeclaredCount(Vega.Chapters.Single(c => c.Id == "signature")));
    }

    [Fact]
    public void TheValidators_ReportABrokenLead_AnUnknownBlueprintOrModule_AndAnUnknownMemoryTrigger()
    {
        string raw = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "stories", V, "story.json"));
        var broken = JsonSerializer.Deserialize<StoryDefinition>(raw, ContentLoader.JsonOptions)!;
        broken.Chapters[1].Leads.Add(new StoryLead { Type = "dig_here" });
        broken.Chapters[2].Leads.Add(new StoryLead { Type = "research:no_such_blueprint" });
        broken.Chapters[3].Leads.Add(new StoryLead { Type = "module:no_such_module" });
        broken.MemoryTriggers.Add("moonlight");
        broken.LeadBonusMilestones = 40;
        var errors = StoryPackValidator.Validate(broken, StoryPackValidationTests.Context())
            .Where(i => i.Severity == StoryPackIssueSeverity.Error).Select(i => i.Path + ": " + i.Message).ToList();
        Assert.Contains(errors, e => e.Contains("chapters[1].leads[1].type") && e.Contains("unknown lead type"));
        Assert.Contains(errors, e => e.Contains("blueprint 'no_such_blueprint' does not exist"));
        Assert.Contains(errors, e => e.Contains("ship module 'no_such_module' does not exist"));
        Assert.Contains(errors, e => e.Contains("unknown memory trigger 'moonlight'"));
        Assert.Contains(errors, e => e.StartsWith("leadBonusMilestones", StringComparison.Ordinal));

        // The shipped pack validates clean.
        Assert.DoesNotContain(StoryPackValidator.Validate(Vega, StoryPackValidationTests.Context(), raw), i => i.Severity == StoryPackIssueSeverity.Error);
    }

    // ---------------- Pacing ----------------

    [Fact]
    public void TheNormalisation_IsComputedFromThePack_AndTheShownScoreIsClamped()
    {
        Assert.Equal(204f / 164f, StoryEngine.UnavailableKillNormalisation(Vega), 4);
        Assert.Equal(1f, StoryEngine.UnavailableKillNormalisation(new StoryDefinition { KillContributionCap = 0, Beats = Vega.Beats }));
        Assert.Equal(1f, StoryEngine.UnavailableKillNormalisation(new StoryDefinition()));

        var state = new StoryState { BeatsRevealed = 9 }; // B0..B8 revealed (B8 at 104)
        Assert.Equal(104, StoryEngine.DisplayProgress(Vega, state, 90));   // never below the last revealed beat
        Assert.Equal(120, StoryEngine.DisplayProgress(Vega, state, 120));
        Assert.Equal(204, StoryEngine.DisplayProgress(Vega, state, 260));  // never above the target
        Assert.Equal(0, StoryEngine.DisplayProgress(Vega, new StoryState(), 0));
    }

    [Fact]
    public void APeacefulWorld_MakesUpTheKillShare_ThroughTheOneScale_AndTheMeterNeverRunsBackwards()
    {
        var (combat, _) = World("pace_combat", TutorialOff);
        Assert.Equal(1f, combat.StoryScaleForTest, 4);

        var (dense, _) = World("pace_dense", c => { Peaceful(c); c.Rules.StoryDensity = StoryDensity.Dense; });
        Assert.Equal(1.5f * 204f / 164f, dense.StoryScaleForTest, 4);

        var (server, _) = World("pace_peaceful", Peaceful);
        Assert.Equal(204f / 164f, server.StoryScaleForTest, 4);
        var admin = Join(server, 1, "Admin");
        admin.State.Role = PlayerRole.WorldAdmin;

        server.AdminAdvanceStory(50); // raw 100 → 124 on a peaceful world: B8 (104) is revealed, B9 (126) is not
        Assert.Equal(9, server.StorySnapshot.BeatsRevealed);
        Assert.Equal(124, server.StoryProgressShownForTest);
        Assert.Equal(124, server.StoryStateForTest("Admin")!.Progress); // the meter reads the same wrapper

        // Machines switched on mid-save: the factor goes, the heard beats stay, the meter holds at B8's threshold.
        Deliver(server, admin, new SetWorldRulesIntent { PlanetEnemies = "Normal" });
        Assert.Equal(1f, server.StoryScaleForTest, 4);
        Assert.Equal(9, server.StorySnapshot.BeatsRevealed);
        Assert.Equal(104, server.StoryProgressShownForTest);
        Assert.Equal(104, server.StoryStateForTest("Admin")!.Progress);

        // …and off again: the factor is back at once.
        Deliver(server, admin, new SetWorldRulesIntent { PlanetEnemies = "Off" });
        Assert.Equal(124, server.StoryProgressShownForTest);

        // The admin's reveal_finale reads the same scale: on a peaceful world it adds fewer milestones (a world with machines:
        // 204 / 2 + 2 = 104).
        int before = server.StorySnapshot.Milestones;
        server.AdminRevealFinale();
        Assert.Equal(Vega.Beats.Count, server.StorySnapshot.BeatsRevealed);
        Assert.InRange(server.StorySnapshot.Milestones - before, 84, 85);
    }

    [Fact]
    public void AnExistingPeacefulSave_JumpsOnceAtItsFirstStart_AndTheCatchUpAbsorbsTheJump()
    {
        // A save from before this build: raw score 122 (61 milestones) — B8 revealed, its player heard up to B8.
        SqliteWorldRepository Save(string world)
        {
            var repo = Repo(world);
            repo.Initialize();
            repo.SaveStoryState(new StoredStoryState { Active = true, StoryId = V, Milestones = 61, BeatsRevealed = 9 });
            var state = new PlayerState { PlayerId = "Kid", Name = "Kid", KnowledgePoints = 8 };
            state.Milestones.Add(StoryPackKeys.Started(V));
            state.Milestones.Add(StoryPackKeys.Migrated(V));
            state.Milestones.Add(StoryPackKeys.CutsceneSeen(V, StoryCutsceneIds.Prologue));
            for (int i = 0; i <= 8; i++)
            {
                state.Milestones.Add(StoryPackKeys.Beat(V, i));
            }

            repo.SavePlayer(state);
            return repo;
        }

        // On a world with machines nothing changes.
        var (combat, _) = World("jump_combat", TutorialOff, Save("jump_combat"));
        Assert.Equal(9, combat.StorySnapshot.BeatsRevealed);

        // On a peaceful world the first start with the factor crosses B9 and B10 at once (122 × 1.244 = 152) — once.
        var (server, transport) = World("jump_peaceful", Peaceful, Save("jump_peaceful"));
        Assert.Equal(11, server.StorySnapshot.BeatsRevealed);

        // The returning player gets the two beats silently in the Journal with ONE line, "the story so far" and chapter 5's
        // opening — not two scenes and two lines.
        var kid = Join(server, 1, "Kid");
        Assert.True(Has(kid, StoryPackKeys.Beat(V, 9)) && Has(kid, StoryPackKeys.Beat(V, 10)));
        var news = Assert.Single(transport.To<ShipAiLine>(kid), l => l.LineKey == StoryNarratorLines.JournalNews);
        Assert.Equal("2", news.LineArg);
        Assert.DoesNotContain(transport.To<ShipAiLine>(kid), l => l.LineKey is "story.vega.beat09" or "story.vega.beat10");

        server.AdvanceUptimeForTest(SvGameServer.CutsceneJoinSettleSeconds);
        for (int i = 0; i < 4; i++)
        {
            server.TickCutscenesForTest(kid);
            server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
        }

        var plays = transport.VegaPlays(kid);
        Assert.Contains(StoryCutsceneIds.Recap, plays);
        Assert.Contains("ch5.open", plays);
        Assert.True(plays.IndexOf(StoryCutsceneIds.Recap) < plays.IndexOf("ch5.open"));
        Assert.DoesNotContain("b10", plays);
    }

    // ---------------- Lead resolution per type ----------------

    [Fact]
    public void AFragmentLead_PointsAtThisWorld_AndAnOlderClientKeepsTheOldKey()
    {
        var (server, _) = World("lead_fragment", TutorialOff);
        var pilot = Join(server, 1, "Pilot");
        var old = Join(server, 2, "Old", packs: false);
        Assert.True(server.StoryTrackStarted(pilot) && server.StoryTrackStarted(old)); // the tutorial is off: both started
        EnsureFragments(server, 2);
        string home = server.Galaxy.FindBody(pilot.CurrentLocationId)!.Name;

        var lead = server.CurrentLeadForTest(pilot)!;
        Assert.Equal(("signature", 1, StoryLeadTypes.Fragment), (lead.ChapterId, lead.Number, lead.Kind));
        Assert.Equal((StoryLeads.FragmentOnKey, home, ObjectiveSources.Story), server.StoryObjectiveForTest(pilot));
        Assert.Equal(StoryLeads.FragmentOnKey, server.ObjectiveKeyForTest("Pilot"));
        Assert.Equal(home, server.ObjectiveArgForTest("Pilot"));
        Assert.Equal(V, server.ObjectivePackForTest("Pilot"));

        // The older client reads today's key on the same world at the same time.
        Assert.Equal("story.obj.fragment_on", server.ObjectiveKeyForTest("Old"));
        Assert.Equal(home, server.ObjectiveArgForTest("Old"));
    }

    [Fact]
    public void AnInfeasibleLead_GivesWayToTheStandIns_ThenToExplore_WhoseTextFollowsTheRules()
    {
        var (server, _) = World("lead_fallback", TutorialOff);
        var p = Join(server, 1, "Pilot");

        // Nowhere a fragment, a settlement or a monument could be (a location no world is loaded for): the explore fallback.
        p.CurrentLocationId = "test:void";
        var objective = server.StoryObjectiveForTest(p);
        Assert.Null(server.CurrentLeadForTest(p));
        Assert.Equal((StoryLeads.ExploreKey, ObjectiveSources.Story), (objective.Key, objective.Source));
        Assert.All(server.LeadsForTest(p), l => Assert.False(l.Feasible));

        var (peaceful, _) = World("lead_fallback_peaceful", Peaceful);
        var kid = Join(peaceful, 1, "Kid");
        kid.CurrentLocationId = "test:void";
        Assert.Equal(StoryLeads.ExplorePeacefulKey, peaceful.StoryObjectiveForTest(kid).Key); // no "drive back the machines"
    }

    [Fact]
    public void AMachinesLead_IsOnlyFeasibleWhereTheRulesFieldMachines_AndADestroyedMachineFollowsIt()
    {
        // No VEGA chapter lists "machines" — a pack copy whose chapter 1 does (a fresh content load, the shared one stays as is).
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.Stories[V].Chapters.Single(c => c.Id == "signature").Leads = new List<StoryLead> { new() { Type = StoryLeadTypes.Machines } };

        var (combat, _) = World("lead_machines", TutorialOff, content: content);
        var pilot = Join(combat, 1, "Pilot");
        var lead = combat.CurrentLeadForTest(pilot)!;
        Assert.Equal((StoryLeadTypes.Machines, "story.vega.obj.machines"), (lead.Kind, lead.ObjKey));
        combat.RaiseGameEventForTest(pilot, GameEventKeys.DefeatAny, GameEventKeys.DefeatCreature); // an animal is no machine
        Assert.False(Has(pilot, LeadKey("signature", 1)));
        combat.RaiseGameEventForTest(pilot, GameEventKeys.DefeatAny, GameEventKeys.DefeatMachine);
        Assert.True(Has(pilot, LeadKey("signature", 1)));

        var (peaceful, _) = World("lead_machines_peaceful", Peaceful, content: content);
        var kid = Join(peaceful, 1, "Kid");
        Assert.False(peaceful.LeadsForTest(kid)[0].Feasible); // no machines here: never asked to fight them
        Assert.NotEqual(StoryLeadTypes.Machines, peaceful.CurrentLeadForTest(kid)?.Kind);
    }

    [Fact]
    public void AUniqueSiteLead_NeedsTheSite_ASkippedSiteIsNotFeasible_AndALandingThereFollowsIt()
    {
        var (server, _) = World("lead_site", c => { TutorialOff(c); c.Rules.InstantTravel = true; });
        var a = Join(server, 1, "Ann");
        var b = Join(server, 2, "Ben");
        AdvanceTo(server, 2); // chapter 2, "The Service"
        EnsureFragments(server);
        string site = server.UniqueSiteBodyForTest("observatory")!;
        Assert.NotEqual(a.CurrentLocationId, site);

        var lead = server.CurrentLeadForTest(a)!;
        Assert.Equal(("service", 1, "unique_site:observatory"), (lead.ChapterId, lead.Number, lead.Lead.Type));
        Assert.Equal("story.vega.obj.site.observatory", lead.ObjKey);
        Assert.Equal(server.Galaxy.FindBody(site)!.Name, lead.ChipArg);

        // The site's world was stamped before the sites existed: it never gets one — the chapter's next lead takes over.
        server.RecordUniqueSiteSkipForTest(site, "observatory");
        Assert.Equal(StoryLeadTypes.Fragment, server.CurrentLeadForTest(a)!.Kind);
        Assert.False(server.LeadsForTest(a)[0].Feasible);

        // Ben lands there (the arrival event) — his lead, the chapter's bonus; Ann had been there before (evidence) — hers.
        int milestones = server.StorySnapshot.Milestones;
        server.RaiseGameEventForTest(b, GameEventKeys.VisitBody, site);
        Assert.True(Has(b, LeadKey("service", 1)));
        Assert.False(Has(a, LeadKey("service", 1)));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones);

        a.State.LandedBodies.Add(site);
        server.TickStoryLeadsForTest(a);
        Assert.True(Has(a, LeadKey("service", 1)));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones); // once per save and chapter
    }

    [Fact]
    public void AMonumentLead_PingsTheUnreadMonument_ForThisPlayerOnly_AndAReadingFollowsIt()
    {
        var (server, _) = World("lead_monument", TutorialOff);
        var a = Join(server, 1, "Ann");
        var b = Join(server, 2, "Ben");
        var at = a.State.Position;
        server.SpawnMonumentForTest(new Vector3f(at.X + 12, at.Y, at.Z + 12), "obelisk"); // stamped with the world, in the game
        AdvanceTo(server, 5); // chapter 3, "The Guardian"

        var lead = server.CurrentLeadForTest(a)!;
        Assert.Equal(("guardian", StoryLeadTypes.Monument), (lead.ChapterId, lead.Kind));
        Assert.NotNull(lead.GroundTarget);

        server.TickStoryLeadsForTest(a);
        Assert.Contains(server.VisibleMarkersForTest("Ann"), m => m.Ping && m.OwnerId == "Ann");
        Assert.DoesNotContain(server.VisibleMarkersForTest("Ben"), m => m.Ping); // a private marker, never RevealPoi
        server.TickStoryLeadsForTest(a);
        Assert.Single(server.VisibleMarkersForTest("Ann"), m => m.Ping); // once per lead, not every second

        // Ben has read this monument already: not feasible for him — his chip goes on to the next lead.
        b.State.ScannedNames[$"monument:{b.CurrentLocationId}:obelisk"] = "Obelisk";
        Assert.NotEqual(StoryLeadTypes.Monument, server.CurrentLeadForTest(b)?.Kind);

        server.RaiseGameEventForTest(a, GameEventKeys.ScanAny, "monument");
        Assert.True(Has(a, LeadKey("guardian", 1)));
        Assert.NotEqual(StoryLeadTypes.Monument, server.CurrentLeadForTest(a)?.Kind);
    }

    [Fact]
    public void ASettlementLead_IsFollowedByAJobThatCountsForTheStory_NotByATeachingOne()
    {
        var (server, _) = World("lead_settlement", TutorialOff);
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, 5); // chapter 3 lists settlement_help as its second lead
        server.AddMissionDefForTest(new MissionDefinition { Id = "teach_test_1", ChainId = "teach_test", Step = 1, Surface = "radio", CountsForStory = false });
        server.AddMissionDefForTest(new MissionDefinition { Id = "help_test_1", ChainId = "help_test", Step = 1, Surface = "radio" });

        server.RaiseGameEventForTest(p, GameEventKeys.MissionTurnedIn, "teach_test_1");
        Assert.False(Has(p, LeadKey("guardian", 2)));
        server.RaiseGameEventForTest(p, GameEventKeys.MissionTurnedIn, "help_test_1");
        Assert.True(Has(p, LeadKey("guardian", 2)));
        Assert.False(Has(p, LeadKey("guardian", 1))); // one action, one lead
    }

    [Fact]
    public void AHealTankLead_NeedsTheLandedShip_AndIsFollowedByStandingAtTheTank()
    {
        var (server, _) = World("lead_heal", c => { TutorialOff(c); c.PlaceStarterShip = true; });
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, 6); // chapter 4, "The Verdict"
        var lead = server.LeadsForTest(p).Single(l => l.Kind == StoryLeadTypes.HealTank);
        Assert.True(lead.Feasible);
        var tank = lead.GroundTarget!.Value;

        p.State.Position = new Vector3f(tank.X + 30, tank.Y, tank.Z + 30);
        server.TickStoryLeadsForTest(p);
        Assert.False(Has(p, LeadKey("verdict", 2)));

        p.State.Position = tank;
        server.TickStoryLeadsForTest(p);
        Assert.True(Has(p, LeadKey("verdict", 2)));

        var (shipless, _) = World("lead_heal_none", TutorialOff);
        var q = Join(shipless, 1, "Walker");
        AdvanceTo(shipless, 6);
        Assert.False(shipless.LeadsForTest(q).Single(l => l.Kind == StoryLeadTypes.HealTank).Feasible); // no landed ship, no tank
    }

    [Fact]
    public void AResearchLead_IsPerPlayer_FollowedByEvidenceOrTheEvent_AndPaysTheBonusOnce()
    {
        var (server, _) = World("lead_research", TutorialOff);
        var a = Join(server, 1, "Ann");
        var b = Join(server, 2, "Ben");
        AdvanceTo(server, 9); // chapter 5, "What You Are"
        var lead = server.CurrentLeadForTest(a)!;
        Assert.Equal(("what_you_are", 1, "research:radar_array"), (lead.ChapterId, lead.Number, lead.Lead.Type));
        Assert.Equal("story.vega.obj.research", lead.ObjKey);
        Assert.Equal("Radar Array", lead.ChipArg);

        int milestones = server.StorySnapshot.Milestones;
        a.State.UnlockedBlueprints.Add("radar_array"); // researched before: the evidence books it at the next pass
        server.TickStoryLeadsForTest(a);
        Assert.True(Has(a, LeadKey("what_you_are", 1)));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones);

        // Ben still has it on his chip (still useful direction) — his research books his lead, no second bonus.
        Assert.Equal("research:radar_array", server.CurrentLeadForTest(b)!.Lead.Type);
        server.RaiseGameEventForTest(b, GameEventKeys.ResearchAny, "radar_array");
        Assert.True(Has(b, LeadKey("what_you_are", 1)));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones);
        Assert.NotEqual("research:radar_array", server.CurrentLeadForTest(a)?.Lead.Type); // the chip moved on
    }

    [Fact]
    public void AModuleLead_IsFollowedByFittingTheModule_EvenWhileTheResearchLeadIsOpen()
    {
        var (server, _) = World("lead_module", TutorialOff);
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, 11); // chapter 6, "It Still Sleeps"
        Assert.Equal("research:jump_generator", server.CurrentLeadForTest(p)!.Lead.Type);
        Assert.Equal("Jump Generator", server.LeadsForTest(p)[1].ChipArg);

        server.RaiseGameEventForTest(p, GameEventKeys.ModuleBuilt, "jump_generator");
        Assert.True(Has(p, LeadKey("sleeps", 2)));
        Assert.False(Has(p, LeadKey("sleeps", 1)));
        Assert.Equal("research:jump_generator", server.CurrentLeadForTest(p)!.Lead.Type);
    }

    [Fact]
    public void TheFinaleLead_AsksForAJumpGeneratorFirst_IsNeverMuted_PaysNoBonus_AndTheRelaysCloseThePostGame()
    {
        var (server, _) = World("lead_finale", TutorialOff);
        var p = Join(server, 1, "Pilot");
        var old = Join(server, 2, "Old", packs: false);
        server.AdminRevealFinale();
        Assert.Equal("core", server.TrackChapterIdForTest(p));
        Assert.Equal((StoryLeads.FinaleJumpKey, ObjectiveSources.Finale), (server.ObjectiveKeyForTest("Pilot"), server.ObjectiveSourceForTest("Pilot")));
        Assert.Equal("story.obj.finale", server.ObjectiveKeyForTest("Old"));

        server.AdminGotoCore(p); // in the Guardian system there is no jump left to make
        Assert.Equal(StoryLeads.FinaleKey, server.ObjectiveKeyForTest("Pilot"));

        server.MarkGuardianDefeatedForTest();
        Assert.Equal("resolution", server.TrackChapterIdForTest(p));
        Assert.Equal(("story.vega.obj.relay", ObjectiveSources.Story), (server.ObjectiveKeyForTest("Pilot"), server.ObjectiveSourceForTest("Pilot")));

        int milestones = server.StorySnapshot.Milestones;
        server.RaiseGameEventForTest(p, GameEventKeys.RelayCommissioned, "relay_1");
        Assert.True(Has(p, LeadKey("resolution", 1)));
        Assert.Equal(milestones, server.StorySnapshot.Milestones); // no bonus after B12 — the cap
        Assert.Equal(string.Empty, server.ObjectiveKeyForTest("Pilot")); // no fallback after the win: the chip clears as before
    }

    // ---------------- Chip rank and the Journal ----------------

    [Fact]
    public void TheLead_RanksBelowTheBasicTraining_AndTheFinaleInItsSystemRanksAboveBoth()
    {
        var (server, _) = World("lead_rank");
        var p = Join(server, 1, "Pilot");
        Assert.Equal(ObjectiveSources.Tutorial, server.ObjectiveSourceForTest("Pilot"));

        server.StartStoryTrackForTest(p); // "Start the story now" while the basic training runs
        Assert.StartsWith(StoryLeads.ObjPrefix, server.StoryObjectiveForTest(p).Key, StringComparison.Ordinal);
        Assert.Equal(ObjectiveSources.Tutorial, server.ObjectiveSourceForTest("Pilot")); // rank 3 beats rank 5

        p.State.Milestones.Add(StoryPackKeys.OptOut(Tutorial));
        Assert.Equal(ObjectiveSources.Story, server.ObjectiveSourceForTest("Pilot"));
        Assert.StartsWith(StoryLeads.ObjPrefix, server.ObjectiveKeyForTest("Pilot"));

        p.State.Milestones.Remove(StoryPackKeys.OptOut(Tutorial));
        server.AdminRevealFinale();
        server.AdminGotoCore(p);
        Assert.Equal((StoryLeads.FinaleKey, ObjectiveSources.Finale), (server.ObjectiveKeyForTest("Pilot"), server.ObjectiveSourceForTest("Pilot")));
    }

    [Fact]
    public void TheJournal_ListsTheChaptersLeadsAsTasks_OnceTheTrackStarted()
    {
        var (server, _) = World("lead_journal", c => { TutorialOff(c); c.Rules.InstantTravel = true; });
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, 2);
        EnsureFragments(server);

        var vega = server.StoryPackStatesForTest(p).Packs.Single(x => x.Id == V);
        Assert.Equal(new[] { "unique_site:observatory", "fragment" }, vega.Leads.Select(l => l.Type));
        Assert.Equal(new[] { true, false }, vega.Leads.Select(l => l.Current));
        Assert.All(vega.Leads, l => Assert.Equal("service", l.ChapterId));
        Assert.Equal("story.vega.obj.site.observatory", vega.Leads[0].Key);
        Assert.False(vega.Leads[0].Done);

        p.State.LandedBodies.Add(server.UniqueSiteBodyForTest("observatory")!);
        server.TickStoryLeadsForTest(p);
        vega = server.StoryPackStatesForTest(p).Packs.Single(x => x.Id == V);
        Assert.Equal(new[] { true, false }, vega.Leads.Select(l => l.Done));
        Assert.Equal(new[] { false, true }, vega.Leads.Select(l => l.Current));

        // Before the track starts the page has no tasks (the story is held for that player).
        var (held, _) = World("lead_journal_held");
        var newcomer = Join(held, 1, "Newcomer");
        Assert.False(held.StoryTrackStarted(newcomer));
        Assert.Empty(held.StoryPackStatesForTest(newcomer).Packs.Single(x => x.Id == V).Leads);

        // The wire carries them (additive field).
        var decoded = (StoryPackStates)NetCodec.Decode(NetCodec.Encode(server.StoryPackStatesForTest(p)))!;
        Assert.Equal(2, decoded.Packs.Single(x => x.Id == V).Leads.Length);
    }

    // ---------------- Memories, the Crystal workshop, the tutorial ----------------

    [Fact]
    public void OnAPeacefulWorld_AFollowedLeadOrAMonumentReading_UnlocksTheNextMemory()
    {
        var (server, _) = World("memory_peaceful", Peaceful);
        var kid = Join(server, 1, "Kid");
        EnsureFragments(server);
        Assert.Equal(0, server.PlayerMemoryCount("Kid"));

        Assert.True(server.PickUpNetFragmentForTest(server.NetFragmentSnapshots[0].Id, "Kid")); // chapter 1's lead
        Assert.True(Has(kid, LeadKey("signature", 1)));
        Assert.Equal(1, server.PlayerMemoryCount("Kid"));

        server.RaiseGameEventForTest(kid, GameEventKeys.ScanAny, "monument"); // no monument lead open: the memory trigger
        Assert.Equal(2, server.PlayerMemoryCount("Kid"));

        // Where machines can drop memories, neither does.
        var (combat, _) = World("memory_combat", TutorialOff);
        var pilot = Join(combat, 1, "Pilot");
        EnsureFragments(combat);
        Assert.True(combat.PickUpNetFragmentForTest(combat.NetFragmentSnapshots[0].Id, "Pilot"));
        Assert.True(Has(pilot, LeadKey("signature", 1)));
        combat.RaiseGameEventForTest(pilot, GameEventKeys.ScanAny, "monument");
        Assert.Equal(0, combat.PlayerMemoryCount("Pilot"));
    }

    [Fact]
    public void TheCrystalWorkshop_NoLongerAddsStoryMilestones_WhileAStoryJobStillDoes()
    {
        var workshop = _content.Missions.Values.Where(m => m.Id.StartsWith("crystal_workshop_", StringComparison.Ordinal)).ToList();
        Assert.Equal(6, workshop.Count);
        Assert.All(workshop, m => Assert.False(m.CountsForStory, m.Id));
        Assert.All(_content.Missions.Values.Except(workshop), m => Assert.True(m.CountsForStory, m.Id));

        var (server, _) = World("workshop", TutorialOff);
        var p = Join(server, 1, "Builder");
        int milestones = server.StorySnapshot.Milestones;
        p.State.Missions.Add(new MissionProgress { MissionId = "crystal_workshop_1", ChainId = "crystal_workshop", ObjectiveProgress = { 1 } });
        server.TurnInMission("Builder", "crystal_workshop_1");
        Assert.Equal(MissionStatus.TurnedIn, p.State.Missions.Single(m => m.MissionId == "crystal_workshop_1").Status);
        Assert.Equal(milestones, server.StorySnapshot.Milestones);

        server.AddMissionDefForTest(new MissionDefinition
        {
            Id = "story_job_1",
            Objectives = { new MissionObjective { Type = MissionObjectiveType.Defeat, Target = "machine", Required = 1 } },
        });
        p.State.Missions.Add(new MissionProgress { MissionId = "story_job_1", ObjectiveProgress = { 1 } });
        server.TurnInMission("Builder", "story_job_1");
        Assert.Equal(milestones + 1, server.StorySnapshot.Milestones);
    }

    [Fact]
    public void TheTutorialsBookkeeping_NeverFeedsTheVegaScore()
    {
        var (server, _) = World("tutorial_score");
        var p = Join(server, 1, "Newcomer");
        var before = server.StorySnapshot;

        server.RaiseGameEventForTest(p, "move:walked", amount: 8); // the first lesson's walk
        Assert.True(Has(p, StoryPackKeys.StepDone(Tutorial, "b1.1")));
        server.SkipTutorialForTest(p); // and the rest skipped
        Assert.True(server.StoryTrackStarted(p));

        var after = server.StorySnapshot;
        Assert.Equal((before.Milestones, before.Fragments, before.Kills), (after.Milestones, after.Fragments, after.Kills));
    }

    // ---------------- #2477: the story pass reaches every joined player ----------------

    [Fact]
    public void APilotOnAnUnloadedWorld_GetsTheStoryPass_FromTheServerTick_OnceASecond()
    {
        // A pilot cruising after an in-flight hyperjump occupies a body whose world is not loaded — TickShipAi runs per loaded
        // world and never reached them, so the evidence below waited for the landing. The server tick runs the pass for them.
        var (server, _) = World("lead_space", TutorialOff);
        var pilot = Join(server, 1, "Pilot");
        var walker = Join(server, 2, "Walker");
        AdvanceTo(server, 9); // chapter 5, "What You Are": research the radar array
        Assert.Equal("research:radar_array", server.CurrentLeadForTest(pilot)!.Lead.Type);

        pilot.CurrentLocationId = "planet_never_loaded";
        pilot.State.UnlockedBlueprints.Add("radar_array"); // researched: the evidence books it at the next pass
        double pilotSeconds = pilot.StoryPackPlaySeconds, walkerSeconds = walker.StoryPackPlaySeconds;
        for (int i = 0; i < 6; i++)
        {
            server.Tick(0.5);
        }

        Assert.True(Has(pilot, LeadKey("what_you_are", 1)));
        Assert.False(Has(walker, LeadKey("what_you_are", 1))); // per player

        // Once a second for both — the walker by the world's own pass, the pilot by the new one; nobody twice.
        Assert.Equal(3.0, pilot.StoryPackPlaySeconds - pilotSeconds);
        Assert.Equal(3.0, walker.StoryPackPlaySeconds - walkerSeconds);
    }

    // ---------------- Cost ----------------

    [Fact]
    public void Leads_AreResolvedOnlyWhenSomethingRelevantChanged_NotEveryTick()
    {
        var (server, _) = World("lead_cost", TutorialOff);
        var p = Join(server, 1, "Pilot");
        _ = server.ObjectiveKeyForTest("Pilot");
        int resolutions = server.LeadResolutionsForTest;

        for (int i = 0; i < 30; i++)
        {
            _ = server.ObjectiveKeyForTest("Pilot");
            server.TickStoryLeadsForTest(p);
            _ = server.JournalLeadsForTest(p);
        }

        Assert.Equal(resolutions, server.LeadResolutionsForTest);

        server.RaiseGameEventForTest(p, GameEventKeys.ModuleBuilt, "radar_array"); // an event that matters
        _ = server.ObjectiveKeyForTest("Pilot");
        Assert.Equal(resolutions + 1, server.LeadResolutionsForTest);

        p.CurrentLocationId = "test:void"; // the player moved: resolved once more
        _ = server.ObjectiveKeyForTest("Pilot");
        _ = server.ObjectiveKeyForTest("Pilot");
        Assert.Equal(resolutions + 2, server.LeadResolutionsForTest);
    }
}
