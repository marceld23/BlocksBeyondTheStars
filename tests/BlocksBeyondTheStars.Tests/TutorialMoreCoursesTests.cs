// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

/// <summary>
/// #2482 — step 21 of the story packs (#2459): the tutorial's deep dives of the part "More to discover" — C9 Animal friends,
/// C10 Gardener and lab, C11 Neighbours, C12 Defence, C13 On the move, C15 Together, C16 Creative tools, C18 Explorer
/// (synthesis §3.2/§3.4/§3.5, E9). Their shape and wording; the courses a world rules out (no combat, no animals, no
/// settlements, a singleplayer world) left out of the Journal and never offered; the offers at their moments; and a walk
/// through every course with the real handlers — a herd fed and an animal tamed (the banner once), a sapling planted, a
/// campfire, a sample and the bio lab, a settler talked to, a robber chased away, a wall climbed, a glider and a boat, two
/// players chatting, trading, allying and pinging with an observer who never counts, a painted block, a looted chest and a
/// ruin reached — plus every new verb from its own handler.
/// </summary>
public sealed class TutorialMoreCoursesTests : IDisposable
{
    private const string T = SvGameServer.OfficialTutorialPackId;

    private static readonly string[] Courses = { "c9", "c10", "c11", "c12", "c13", "c15", "c16", "c18" };

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public TutorialMoreCoursesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_more_courses_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Records every server send so a test can assert who was told what, in order.</summary>
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

        public List<TMsg> To<TMsg>(PlayerSession who) => Sent.Where(x => x.Conn == who.ConnectionId).Select(x => x.Msg).OfType<TMsg>().ToList();

        public List<string> LineKeys(PlayerSession who) => To<ShipAiLine>(who).Where(l => l.LineKey.Length > 0).Select(l => l.LineKey).ToList();

        public List<string> Plays(PlayerSession who) => To<CutscenePlay>(who).Select(c => c.CutsceneId).ToList();

        public List<string> Offers(PlayerSession who)
            => To<ShipAiLine>(who).Where(l => l.LineKey == SvGameServer.ChapterOfferLine).Select(l => l.LineArg).ToList();
    }

    private SqliteWorldRepository Repo(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return repo;
    }

    /// <summary>A world without a world story (the courses are the tutorial's) and without a starter ship; several players
    /// may join (a world for more than one player). <paramref name="configure"/> may change the rules — the config object
    /// stays the server's, so a test can switch a rule live.</summary>
    private (SvGameServer Server, RecordingTransport Transport, ServerConfig Config) World(string world, Action<ServerConfig>? configure = null, string planet = "rocky")
    {
        var transport = new RecordingTransport();
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 4242,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            ViewDistanceChunks = 1,
            MaxPlayers = 6,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceFactories = false,
            PlaceBanditCamps = false,
        };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.StoryId = StoryRegistry.NoneStoryId;
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, Repo(world));
        server.Start();
        return (server, transport, config);
    }

    /// <summary>The real join path of a current client (in the basic training).</summary>
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

    /// <summary>A local player (a client with cutscenes) past the basic training ("I know the game"), on foot, at
    /// <paramref name="x"/> blocks east of the spawn — far enough from the others that "a player nearby" is no moment.</summary>
    private static PlayerSession LocalPastTraining(SvGameServer server, string name, float x = 0f)
    {
        var p = server.AddLocalPlayer(name);
        p.ClientFeatures.Add(ClientFeatures.Cutscenes1);
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(p.State.Position.X + x, p.State.Position.Y, p.State.Position.Z);
        server.TickStoryPacksForTest(p);
        server.SkipTutorialForTest(p);
        Assert.Contains(StoryPackKeys.Done(T), p.State.Milestones);
        p.StoryPackPlaySeconds += 90; // #2498: the skip suggests the flight courses silently — an offer; the next waits the gap
        return p;
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    private static void Start(SvGameServer server, PlayerSession p, string chapter)
        => Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.StartChapter, ChapterId = chapter });

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private static bool Done(PlayerSession p, string step) => Has(p, StoryPackKeys.StepDone(T, step));

    private static bool Skipped(PlayerSession p, string step) => Has(p, StoryPackKeys.StepSkipped(T, step));

    private static bool CourseDone(PlayerSession p, string chapter) => Has(p, StoryPackKeys.ChapterDone(T, chapter));

    private static void Give(PlayerSession p, string item, int count) => p.State.Inventory.Add(item, count, 999);

    private static void Unlock(PlayerSession p, params string[] blueprints)
    {
        foreach (string bp in blueprints)
        {
            p.State.UnlockedBlueprints.Add(bp);
        }
    }

    private StoryDefinition Pack => _content.Stories[T];

    private StoryChapter Chapter(string id) => Pack.Chapters.Single(c => c.Id == id);

    private StoryStep StepDef(string id) => Pack.Chapters.SelectMany(c => c.Steps).Single(s => s.Id == id);

    private static Dictionary<string, string> PackLocale(string lang)
        => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(TestPaths.DataDir(), "stories", T, "locales", lang + ".json")))!;

    private static List<string> JournalChapters(SvGameServer server, PlayerSession p)
        => server.StoryPackStatesForTest(p).Packs.Single(x => x.Id == T).Chapters.Select(c => c.Id).ToList();

    private static NetStoryChapter ChapterState(SvGameServer server, PlayerSession p, string chapter)
        => server.StoryPackStatesForTest(p).Packs.Single(x => x.Id == T).Chapters.Single(c => c.Id == chapter);

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

    /// <summary>A flat stone pad well above the terrain around (cx, cz); returns its top.</summary>
    private int BuildPad(SvGameServer server, int cx, int cz, int r)
    {
        int max = int.MinValue;
        for (int dx = -r - 4; dx <= r + 4; dx += 2)
            for (int dz = -r - 4; dz <= r + 4; dz += 2)
            {
                max = Math.Max(max, SurfaceTopY(server, cx + dx, cz + dz));
            }

        int padY = max + 8;
        Fill(server, "stone", cx - r, cx + r, padY, padY, cz - r, cz + r);
        return padY;
    }

    // ---------------- The courses ----------------

    [Fact]
    public void TheMoreCourses_AreOptionalChapters_WithTheirVisibilityMomentsBannerAndOneTeachingVoice()
    {
        var courses = Pack.Chapters.Where(c => Courses.Contains(c.Id)).ToList();
        Assert.Equal(Courses, courses.Select(c => c.Id));
        foreach (var c in courses)
        {
            Assert.True(c.Optional, c.Id);
            Assert.Equal(("tut.course", "tut.done"), (c.OpenCutscene, c.CloseCutscene));
            Assert.NotEmpty(c.Requires);
            Assert.NotEmpty(c.OfferWhen); // every course has its own moment — or the Journal
            Assert.All(c.Steps, s => Assert.StartsWith(c.Id + ".", s.Id));
            Assert.InRange(c.Steps.Count(s => s.Kind == StoryStepKinds.Do), 1, 6); // a short course
        }

        // Synthesis §3.2: the moments.
        Assert.Equal(new[] { "any:[world:tameable_near,world:herd_begging]" }, Chapter("c9").OfferWhen);
        Assert.Equal(new[] { "any:[world:seed_carried,blueprint:bio_lab]" }, Chapter("c10").OfferWhen);
        Assert.Equal(new[] { "world:settlement_near" }, Chapter("c11").OfferWhen);
        Assert.Equal(new[] { "any:[world:bandit_hail,world:hostile_near]" }, Chapter("c12").OfferWhen);
        Assert.Equal(new[] { "any:[world:climbing,blueprint:climbing_gloves,blueprint:glider]" }, Chapter("c13").OfferWhen);
        Assert.Equal(new[] { "world:player_near" }, Chapter("c15").OfferWhen);
        Assert.Equal(new[] { "any:[blueprint:paint_tool,blueprint:shape_tool]" }, Chapter("c16").OfferWhen);
        Assert.Equal(new[] { "any:[world:ruin_near,world:treasure_near,world:wreck_signal]" }, Chapter("c18").OfferWhen);

        // Synthesis §3.5: what the world rules can rule out — those courses are left out (visibleIf), the others always exist.
        Assert.Equal(new[] { "world:creatures" }, Chapter("c9").VisibleIf);
        Assert.Equal(new[] { "world:settlements" }, Chapter("c11").VisibleIf);
        Assert.Equal(new[] { "any:[world:enemies,world:bandits,all:[world:space_enemies,world:free_flight]]" }, Chapter("c12").VisibleIf);
        Assert.Equal(new[] { "world:multiplayer" }, Chapter("c15").VisibleIf);
        Assert.All(new[] { "c10", "c13", "c16", "c18" }, id => Assert.Empty(Chapter(id).VisibleIf));
        Assert.All(Pack.Chapters.Where(c => !Courses.Contains(c.Id)), c => Assert.Empty(c.VisibleIf));

        // E9: Defence is never suggested at the graduation, and the graduation's three suggestions stay.
        Assert.Equal(new[] { "c3", "c4", "c1" }, Pack.Graduation.Suggest);

        // The first companion: a banner (synthesis §6.4: T1, 4 s, the camera untouched) on the tame.
        Assert.Equal("tut.first_tame", StepDef("c9.3").OnComplete.Cutscene);
        var banner = Pack.FindCutscene("tut.first_tame")!;
        Assert.Equal(CutsceneTiers.Banner, banner.Tier);
        Assert.False(banner.TakesCamera);
        Assert.False(banner.EffectiveHoldWorld);
        Assert.False(banner.EffectiveLockInput);
        Assert.All(StoryCutsceneIds.Tutorial, id => Assert.NotNull(Pack.FindCutscene(id))); // every tutorial card id now exists

        // One teaching voice: the hints and tips the step lines say themselves — safety tips stay VEGA's.
        Assert.Equal(new[] { "feed_herd" }, StepDef("c9.2").HintRefs);
        Assert.Equal(new[] { "favourite_food", "tameable_near" }, StepDef("c9.3").HintRefs);
        Assert.Equal(new[] { "plant_refused" }, StepDef("c10.1").HintRefs);
        Assert.Equal(new[] { "first_sample" }, StepDef("c10.3").HintRefs);
        Assert.Equal(new[] { "bio_lab_unlocked", "first_analysis", "bio_lab_tabs" }, StepDef("c10.5").HintRefs); // #2459: + the client lesson
        Assert.Equal(new[] { "settlement_near" }, StepDef("c11.1").HintRefs);
        Assert.Equal(new[] { "rope_gun" }, StepDef("c13.2").HintRefs);
        Assert.Equal(new[] { "player_near" }, StepDef("c15.1").HintRefs);
        Assert.Equal(new[] { "treasure_near" }, StepDef("c18.1").HintRefs);
        Assert.Equal(new[] { "poi", "ruin_near" }, StepDef("c18.2").HintRefs);
        string[] safety = { "energy", "hunger", "cold", "heat", "medkit", "hull_low", "sample_case_full", "companion_fetch" };
        Assert.All(courses.SelectMany(c => c.Steps), s => Assert.Empty((s.HintRefs ?? new List<string>()).Intersect(safety)));

        // Every course step listens to a verb the server raises.
        foreach (var step in courses.SelectMany(c => c.Steps).Where(s => s.Kind != StoryStepKinds.Read))
        {
            Assert.Contains(step.Objective.Event, GameEventKeys.All);
        }

        // The whole pack stays a readable size (synthesis §3.10: ≈ 450 keys in all).
        Assert.InRange(PackLocale("en").Count, 400, 500);
        Assert.Empty(StoryPackValidator.Validate(Pack, StoryPackValidationTests.Context()).Where(i => i.Severity == StoryPackIssueSeverity.Error));
    }

    [Fact]
    public void TheDefenceCourse_SaysFendOffAndChaseAway_AndTheTogetherCourse_NamesNobody()
    {
        var en = PackLocale("en");
        var de = PackLocale("de");
        var defence = en.Keys.Where(k => k.StartsWith("story.tutorial.c12.", StringComparison.Ordinal) || k.StartsWith("story.tutorial.ch.c12", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(defence);
        string allEn = string.Join(" ", defence.Select(k => en[k])).ToLowerInvariant();
        string allDe = string.Join(" ", defence.Select(k => de[k])).ToLowerInvariant();
        Assert.Contains("fend off", allEn);
        Assert.Contains("chase", allEn);
        Assert.Contains("abwehr", allDe);
        Assert.Contains("verjag", allDe);
        var cruel = new Regex(@"\b(kill|killing|killed|slay|destroy|murder|blood)\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        var cruelDe = new Regex(@"(töte|tötet|getötet|vernicht|umbring|blut)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        Assert.DoesNotMatch(cruel, allEn);
        Assert.DoesNotMatch(cruelDe, allDe);

        // "Together" never puts anybody's name in a line: no slot in any of its texts (an observer could be named otherwise).
        foreach (string key in en.Keys.Where(k => k.StartsWith("story.tutorial.c15.", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("{0}", en[key]);
            Assert.DoesNotContain("{0}", de[key]);
        }
    }

    // ---------------- World rules ----------------

    [Fact]
    public void CoursesAWorldRulesOut_AreLeftOut_NeverOffered_AndShowUpWhenTheRulesAllowThem()
    {
        // A peaceful singleplayer world without animals or settlements (the family preset's spirit, synthesis §3.5).
        var (server, transport, config) = World("rules_off", c =>
        {
            c.MaxPlayers = 1;
            c.Rules.PlanetEnemies = AlienActivity.Off;
            c.Rules.Bandits = AlienActivity.Off;
            c.Rules.SpaceCombat = SpaceCombatMode.Off;
            c.Rules.CreatureAbundance = AlienActivity.Off;
            c.World.Settlements = Frequency.Off;
        });
        var p = LocalPastTraining(server, "Kid");
        var listed = JournalChapters(server, p);
        Assert.DoesNotContain("c9", listed);
        Assert.DoesNotContain("c11", listed);
        Assert.DoesNotContain("c12", listed);
        Assert.DoesNotContain("c15", listed);
        Assert.All(new[] { "c10", "c13", "c16", "c18", "c1", "c2", "c8" }, id => Assert.Contains(id, listed)); // the Journal lists every other course

        // Never started, never offered — even at their "moments".
        Start(server, p, "c12");
        Assert.False(Has(p, StoryPackKeys.ChapterActive(T, "c12")));
        p.BanditDemandId = 1;
        p.VegaTipLive = new[] { "settlement_near", "tameable_near" };
        server.TickStoryPacksForTest(p, 3);
        Assert.Empty(transport.Offers(p));

        // The admin switches robbers on: Defence exists now — offered at the hold-up, started, then the rule goes off again
        // and the running course stays listed.
        config.Rules.Bandits = AlienActivity.Normal;
        Assert.Contains("c12", JournalChapters(server, p));
        server.TickStoryPacksForTest(p);
        Assert.Equal("Defence", Assert.Single(transport.Offers(p)));
        Start(server, p, "c12");
        Assert.True(Has(p, StoryPackKeys.ChapterActive(T, "c12")));
        config.Rules.Bandits = AlienActivity.Off;
        Assert.Equal(StoryChapterStates.Active, ChapterState(server, p, "c12").State);

        // A world for more than one player lists "Together"; animals and settlements bring their courses back.
        config.MaxPlayers = 4;
        config.Rules.CreatureAbundance = AlienActivity.Normal;
        listed = JournalChapters(server, p);
        Assert.Contains("c15", listed);
        Assert.Contains("c9", listed);
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.Multiplayer));
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.Creatures));
        Assert.False(server.WorldPredicateForTest(p, StoryWorldPredicates.Settlements));
    }

    // ---------------- Offers ----------------

    [Fact]
    public void TheCourses_AreOfferedAtTheirMoments_NeverDuringTheTraining()
    {
        var (server, transport, _) = World("offers");

        // During the training no moment counts.
        var rookie = Join(server, 1, "Rookie");
        rookie.VegaTipLive = new[] { "tameable_near", "settlement_near", "ruin_near" };
        Give(rookie, "sapling", 2);
        server.TickStoryPacksForTest(rookie, 3);
        Assert.Empty(transport.Offers(rookie));

        PlayerSession Past(string name, int slot) => LocalPastTraining(server, name, 200f * slot);

        // C9: VEGA's tameable_near tip (a translator in hand, an animal in reach).
        var tamer = Past("Tamer", 1);
        server.TickStoryPacksForTest(tamer);
        Assert.Empty(transport.Offers(tamer));
        tamer.VegaTipLive = new[] { "tameable_near" };
        Assert.True(server.WorldPredicateForTest(tamer, StoryWorldPredicates.TameableNear));
        server.TickStoryPacksForTest(tamer);
        Assert.Equal("Animal friends", Assert.Single(transport.Offers(tamer)));
        server.TickStoryPacksForTest(tamer, 120);
        Assert.Single(transport.Offers(tamer)); // once

        // C10: a sapling in the backpack — or the bio lab researched.
        var gardener = Past("Gardener", 2);
        Give(gardener, "sapling", 1);
        Assert.True(server.WorldPredicateForTest(gardener, StoryWorldPredicates.SeedCarried));
        server.TickStoryPacksForTest(gardener);
        Assert.Equal("Gardener and lab", Assert.Single(transport.Offers(gardener)));
        var scholar = Past("Scholar", 3);
        Assert.False(server.WorldPredicateForTest(scholar, StoryWorldPredicates.SeedCarried));
        Unlock(scholar, "bio_lab");
        server.TickStoryPacksForTest(scholar);
        Assert.Equal("Gardener and lab", Assert.Single(transport.Offers(scholar)));

        // C11: a settlement close by (the tip pass).
        var neighbour = Past("Neighbour", 4);
        neighbour.VegaTipLive = new[] { "settlement_near" };
        server.TickStoryPacksForTest(neighbour);
        Assert.Equal("Neighbours", Assert.Single(transport.Offers(neighbour)));

        // C13: the first climb after the training (the pose a move report carries).
        var climber = Past("Climber", 5);
        var at = climber.State.Position;
        Deliver(server, climber, new MoveIntent { X = at.X, Y = at.Y, Z = at.Z });
        Deliver(server, climber, new MoveIntent { X = at.X, Y = at.Y + 0.5f, Z = at.Z, Climbing = true });
        Assert.True(server.WorldPredicateForTest(climber, StoryWorldPredicates.Climbing));
        server.TickStoryPacksForTest(climber);
        Assert.Equal("On the move", Assert.Single(transport.Offers(climber)));

        // C16: the paint tool researched.
        var painter = Past("Painter", 6);
        Unlock(painter, "paint_tool");
        server.TickStoryPacksForTest(painter);
        Assert.Equal("Creative tools", Assert.Single(transport.Offers(painter)));

        // C18: VEGA's ruin tip — the treasure and wreck tips are moments too.
        var explorer = Past("Explorer", 7);
        explorer.VegaTipLive = new[] { "ruin_near" };
        server.TickStoryPacksForTest(explorer);
        Assert.Equal("Explorer", Assert.Single(transport.Offers(explorer)));
        explorer.VegaTipLive = new[] { "wreck_signal_manual" };
        Assert.True(server.WorldPredicateForTest(explorer, StoryWorldPredicates.WreckSignal));
        explorer.VegaTipLive = new[] { "treasure_near" };
        Assert.True(server.WorldPredicateForTest(explorer, StoryWorldPredicates.TreasureNear));

        // C12: something hostile close by on a world with machines (the hold-up is the other moment, below).
        var guard = Past("Guard", 8);
        Assert.False(server.WorldPredicateForTest(guard, StoryWorldPredicates.HostileNear));
        var g = guard.State.Position;
        server.SpawnPlanetEnemyAtForTest(new Vector3f(g.X + 6f, g.Y, g.Z));
        Assert.True(server.WorldPredicateForTest(guard, StoryWorldPredicates.HostileNear));
        server.TickStoryPacksForTest(guard);
        Assert.Equal("Defence", Assert.Single(transport.Offers(guard)));
    }

    // ---------------- C12 Defence ----------------

    [Fact]
    public void TheDefenceCourse_ComesAtAHoldUp_TellsAboutRobbers_AndChasesTheRobberAway()
    {
        // BanditTests' arena: robbers on, no machines.
        var (server, transport, _) = World("defence", c =>
        {
            c.Seed = 9;
            c.Rules.PlanetEnemies = AlienActivity.Off;
            c.Rules.Bandits = AlienActivity.Normal;
        });
        var p = LocalPastTraining(server, "Mark");
        var inv = p.State.Inventory;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            inv.SetSlot(i, null);
        }

        inv.SetSlot(0, new ItemStack("iron_ore", 100));
        inv.SetSlot(1, new ItemStack("machete", 1));
        server.SpawnBanditAtForTest(new Vector3f(p.State.Position.X + 3f, p.State.Position.Y, p.State.Position.Z + 3f), p.State.PlayerId);
        server.Tick(0.1);
        Assert.NotEqual(0, server.PendingBanditDemandIdForTest("Mark")); // the hold-up
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.BanditHail));
        server.TickStoryPacksForTest(p);
        Assert.Equal("Defence", Assert.Single(transport.Offers(p)));

        Start(server, p, "c12");
        var lines = transport.LineKeys(p);
        Assert.Contains("story.tutorial.c12.robbers.start", lines); // the robbers first, where the world has them
        Assert.Contains("story.tutorial.c12.fend.start", lines);
        Assert.Equal("c12.2", server.ChipStepForTest(p, T));

        var bandit = Assert.Single(server.Bandits);
        bandit.Hull = 1f;
        for (int i = 0; i < 10 && server.Bandits.Count > 0; i++)
        {
            server.AttackEntity("Mark", bandit.Id); // the real attack: refusing, then the robber is chased off
        }

        Assert.Empty(server.Bandits);
        Assert.True(Done(p, "c12.2"));
        Assert.True(Skipped(p, "c12.3")); // no hostile ships on this world (space combat is off by default)
        Assert.True(CourseDone(p, "c12"));
        Assert.Contains("tut.done@c12", transport.Plays(p));
    }

    // ---------------- C9 Animal friends ----------------

    private const string MmpId = "au_mini_michi_paul";

    /// <summary>Keeps every other wild animal out of the way (none begs or rushes a piece while the test runs).</summary>
    private static void Quiet(SvGameServer server, ICollection<string> mine)
    {
        foreach (var c in server.Creatures)
        {
            if (!mine.Contains(c.Id) && !c.IsCompanion)
            {
                c.BegCooldownUntil = 1e9;
                c.FrozenTimer = 1e9;
            }
        }
    }

    /// <summary>Throws one piece from the selected slot (the real Feed action) and ticks until it is eaten.</summary>
    private static void ThrowAndWaitUntilEaten(SvGameServer server, string playerId, ICollection<string> mine)
    {
        for (int i = 0; i < 120 && server.ThrownFoodCellForTest() is null; i++)
        {
            Quiet(server, mine);
            server.ThrowFoodForTest(playerId, 0);
            if (server.ThrownFoodCellForTest() is null)
            {
                Ticks(server, 1);
            }
        }

        Assert.NotNull(server.ThrownFoodCellForTest());
        for (int i = 0; i < 150 && server.ThrownFoodCellForTest() is not null; i++)
        {
            Quiet(server, mine);
            Ticks(server, 1);
        }

        Assert.Null(server.ThrownFoodCellForTest());
    }

    [Fact]
    public void TheAnimalFriendsCourse_ScansFeedsAndTamesARealHerd_WithTheBannerOnce()
    {
        var (server, transport, _) = World("animals", planet: "jungle");
        var p = LocalPastTraining(server, "Paul");
        const int cx = 300, cz = 300;
        int padY = BuildPad(server, cx, cz, 16);
        p.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);
        p.State.Yaw = 0f;
        p.State.Inventory.SetSlot(0, new ItemStack("fruit_banana", 6));
        p.State.SelectedHotbarSlot = 0;

        Start(server, p, "c9");
        Assert.Equal("c9.1", server.ChipStepForTest(p, T));
        Assert.Contains("story.tutorial.c9.scan.start", transport.LineKeys(p)); // machines roam here: "keep your distance"
        server.ScanSubject("Paul", "creature", MmpId);
        Assert.True(Done(p, "c9.1"));
        Assert.Equal("c9.2", server.ChipStepForTest(p, T));
        Assert.True(Has(p, "vega:hint:feed_herd")); // the step's line is the Feed lesson — VEGA's hint counts as told

        // The herd comes begging (the course's moment, world:herd_begging) …
        var herd = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            herd.Add(server.SpawnCreatureAtForTest(new Vector3f(cx + 5.5f + i, padY + 1, cz + 0.5f + (i % 2)), MmpId));
        }

        Quiet(server, herd);
        Ticks(server, 30);
        Assert.Contains(herd, id => server.BegPhaseForTest(id) == "Beg");
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.HerdBegging));

        // … the first banana thrown is the feed (creature:fed), and the herd's favourite: the second tames one.
        ThrowAndWaitUntilEaten(server, "Paul", herd);
        Assert.True(Done(p, "c9.2"));
        Assert.Equal("c9.3", server.ChipStepForTest(p, T));
        Assert.True(Has(p, "vega:hint:favourite_food")); // c9.3 says "feed its favourite a few times"
        ThrowAndWaitUntilEaten(server, "Paul", herd);
        Ticks(server, 2); // the tame runs a tick after the meal
        var companion = Assert.Single(server.TamedCreaturesForTest("Paul"));
        Assert.True(Done(p, "c9.3"));
        Assert.Equal(1, transport.Plays(p).Count(c => c == "tut.first_tame")); // "Tamed · A new friend!"
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.CompanionOwned));

        // Naming it is looking after it (companion:cared) — the course is through.
        Assert.Equal("c9.4", server.ChipStepForTest(p, T));
        server.RenameCompanionForTest("Paul", companion.Id, "Bananafan");
        Assert.True(Done(p, "c9.4"));
        Assert.True(CourseDone(p, "c9"));
        Assert.Contains("tut.done@c9", transport.Plays(p));

        // A replay shows no second banner (a companion already at the player's side completes c9.3 silently only outside a
        // replay — in the replay the player tames again, and the banner is once per player).
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.ReplayChapter, ChapterId = "c9" });
        Assert.True(Has(p, StoryPackKeys.ChapterReplay(T, "c9")));
        server.RaiseGameEventForTest(p, GameEventKeys.ScanPerformed, "creature");
        server.RaiseGameEventForTest(p, GameEventKeys.CreatureFed, "fruit_banana");
        server.RaiseGameEventForTest(p, GameEventKeys.TameAny, MmpId);
        Assert.Equal(1, transport.Plays(p).Count(c => c == "tut.first_tame"));
    }

    [Fact]
    public void TheAnimalFriendsCourse_OnAPeacefulWorld_SaysAllCalm_AndAnOldCompanionCountsSilently()
    {
        var (server, transport, _) = World("animals_calm", c => c.Rules.PlanetEnemies = AlienActivity.Off);
        var p = LocalPastTraining(server, "Fern");
        p.State.TamedCreatures.Add(new TamedCreature { Id = "pet_1", Name = "Old friend", SpeciesId = "x", HomeBodyId = p.CurrentLocationId });
        p.State.Milestones.Add(StoryPackKeys.StepDone(T, "c9.1"));
        p.State.Milestones.Add(StoryPackKeys.StepDone(T, "c9.2"));
        Start(server, p, "c9");
        Assert.True(Done(p, "c9.3")); // a companion at the player's side: taming is known
        Assert.DoesNotContain("tut.first_tame", transport.Plays(p)); // no banner for an old friend
        Assert.Equal("c9.4", server.ChipStepForTest(p, T));

        // The peaceful line: no "keep your distance" where nothing attacks.
        var q = LocalPastTraining(server, "Moss", 200f);
        Start(server, q, "c9");
        Assert.Contains("story.tutorial.c9.scan.start_calm", transport.LineKeys(q));
        Assert.DoesNotContain("story.tutorial.c9.scan.start", transport.LineKeys(q));
    }

    // ---------------- C10 Gardener and lab ----------------

    [Fact]
    public void TheGardenerCourse_PlantsCooksSamplesResearchesAndAnalyses_ThroughTheRealHandlers()
    {
        var (server, transport, _) = World("garden", planet: "jungle");
        var p = LocalPastTraining(server, "Gardener");
        const int cx = 60, cz = 60;
        int padY = BuildPad(server, cx, cz, 6);
        int feet = padY + 1;
        p.State.Position = new Vector3f(cx + 0.5f, feet, cz + 0.5f);

        Start(server, p, "c10");
        Assert.Equal("c10.1", server.ChipStepForTest(p, T));
        Assert.True(Has(p, "vega:hint:plant_refused")); // the step's lines tell where a seedling grows

        // Plant a sapling on dirt (the real place handler).
        server.World.SetBlock(new Vector3i(cx + 2, padY, cz), _content.GetBlock("dirt")!.NumericId);
        Give(p, "sapling", 2);
        server.PlaceBlock("Gardener", cx + 2, feet, cz, "sapling");
        Assert.True(Done(p, "c10.1"));

        // A campfire: torches from wood (any campfire recipe counts).
        Assert.Equal("c10.2", server.ChipStepForTest(p, T));
        Assert.Contains("story.tutorial.c10.cook.start", transport.LineKeys(p));
        server.World.SetBlock(new Vector3i(cx - 2, feet, cz), _content.GetBlock("campfire")!.NumericId);
        Give(p, "wood_log", 2);
        server.Craft("Gardener", "torch_campfire");
        Assert.True(Done(p, "c10.2"));

        // A sample: mining a plant puts one into the case (and VEGA's "a sample!" counts as told by the step's line).
        Assert.Equal("c10.3", server.ChipStepForTest(p, T));
        Assert.True(Has(p, "vega:hint:first_sample"));
        var bush = new Vector3i(cx, feet, cz + 2);
        server.World.SetBlock(new Vector3i(cx, padY, cz + 2), _content.GetBlock("dirt")!.NumericId);
        server.World.SetBlock(bush, _content.GetBlock("flora_bush")!.NumericId);
        p.State.Inventory.SetSlot(p.State.SelectedHotbarSlot, null); // bare hands for a bush
        server.MineBlock("Gardener", bush.X, bush.Y, bush.Z);
        Assert.True(Done(p, "c10.3"));

        // Research the bio lab at the cockpit's validator, then analyse a sample at a placed lab.
        Assert.Equal("c10.4", server.ChipStepForTest(p, T));
        var bp = _content.Blueprints["bio_lab"];
        p.State.KnowledgePoints = Math.Max(p.State.KnowledgePoints, bp.KnowledgeCost);
        foreach (var cost in bp.UnlockCost)
        {
            Give(p, cost.Item, cost.Count);
        }

        server.UnlockBlueprint("Gardener", "bio_lab");
        Assert.True(Done(p, "c10.4"));
        Assert.Equal("c10.5", server.ChipStepForTest(p, T));
        Assert.True(Has(p, "vega:hint:bio_lab_unlocked")); // where the lab is built: the step's line
        uint seed = server.GiveFloraSampleForTest(p, "flora_bush", 2);
        server.World.SetBlock(new Vector3i(cx + 1, feet, cz - 1), _content.GetBlock(BlocksBeyondTheStars.Shared.Bio.BioItems.Lab)!.NumericId);
        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Analyse, Sample = seed });
        Assert.True(Done(p, "c10.5"));
        Assert.Contains("story.tutorial.c10.lab.done", transport.LineKeys(p)); // what comes after the first analysis
        Assert.True(CourseDone(p, "c10"));

        // Without hunger the campfire is a little workshop, not a kitchen.
        var (calm, calmTransport, _) = World("garden_nohunger", c => c.Rules.HungerConsumption = HungerConsumption.Off);
        var q = LocalPastTraining(calm, "Sprout");
        q.State.Milestones.Add(StoryPackKeys.StepDone(T, "c10.1"));
        Start(calm, q, "c10");
        Assert.Contains("story.tutorial.c10.cook.start_nohunger", calmTransport.LineKeys(q));
    }

    // ---------------- C11 Neighbours ----------------

    [Fact]
    public void TheNeighboursCourse_TalksToASettler_AnswersAndOpensThePeopleList()
    {
        using var w = new NpcLifeWorld(_content, "neighbours", configure: c => c.Rules.StoryId = StoryRegistry.NoneStoryId);
        var p = w.Player;
        p.ClientFeatures.Add(ClientFeatures.Packs1);
        w.Server.TickStoryPacksForTest(p);
        w.Server.SkipTutorialForTest(p);
        int baseId = w.FoundBase();
        w.Set(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4, "streamer_post");
        w.Server.ScanBaseLifeForTest();
        var streamer = Assert.Single(w.Server.BaseResidentsForTest(baseId), r => r.Job == "streamer");
        var at = w.Server.NpcStateForTest(streamer.NpcId)!.Value.Pos;
        p.State.Position = new Vector3f(at.X + 1.5f, at.Y, at.Z);
        w.Server.TickProfessionsForTest();
        w.Server.TickProfessionsForTest();

        Assert.Contains("c11", w.Server.StoryPackStatesForTest(p).Packs.Single(x => x.Id == T).Chapters.Select(c => c.Id)); // settlements on
        Deliver(w.Server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.StartChapter, ChapterId = "c11" });
        Assert.Equal("c11.1", w.Server.ChipStepForTest(p, T));
        Assert.True(Has(p, "vega:hint:settlement_near")); // the step's line is VEGA's "a settlement close by"

        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, streamer.NpcId); // npc:talked
        Assert.True(Done(p, "c11.1"));
        w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 0); // npc:answered
        Assert.True(Done(p, "c11.2"));
        Deliver(w.Server, p, new RequestKnownNpcsIntent()); // the "People you know" list (npc:people)
        Assert.True(Done(p, "c11.3"));
        Assert.True(CourseDone(p, "c11"));
        var lines = w.Transport.Sent.Where(s => s.Conn == p.ConnectionId).Select(s => s.Msg).OfType<ShipAiLine>().Select(l => l.LineKey).ToList();
        Assert.Contains("story.tutorial.c11.jobs.start", lines); // who helps with what
    }

    // ---------------- C13 On the move ----------------

    [Fact]
    public void TheOnTheMoveCourse_ClimbsGlidesAndBoardsABoat_AndTellsAboutWhatComesLater()
    {
        var (server, transport, _) = World("moving");
        var p = LocalPastTraining(server, "Pilot");
        Unlock(p, "glider"); // the wing researched; no rope gun, no train on this world
        Start(server, p, "c13");
        Assert.Equal("c13.1", server.ChipStepForTest(p, T));

        // Climb 3 m: three reports in the climbing pose, each a metre higher (the first report adopts the spawn).
        var at = p.State.Position;
        Deliver(server, p, new MoveIntent { X = at.X, Y = at.Y, Z = at.Z });
        Deliver(server, p, new MoveIntent { X = at.X, Y = at.Y + 6f, Z = at.Z, Climbing = true }); // a jump of 6: never climbing
        Assert.False(Done(p, "c13.1"));
        for (int i = 1; i <= 3; i++)
        {
            Deliver(server, p, new MoveIntent { X = at.X, Y = at.Y + 6f + i, Z = at.Z, Climbing = true });
        }

        Assert.True(Done(p, "c13.1"));

        // No rope gun yet: the line about later; the glider: worn and opened (move:airborne).
        Assert.True(Skipped(p, "c13.2"));
        Assert.Contains("story.tutorial.c13.rope.start_later", transport.LineKeys(p));
        Assert.Equal("c13.3", server.ChipStepForTest(p, T));
        TestGear.Wear(p.State, "glider");
        server.SetGlidingForTest("Pilot", true);
        Assert.True(Done(p, "c13.3"));

        // A boat: launched at a pool and boarded (vehicle:boarded boat).
        Assert.Equal("c13.4", server.ChipStepForTest(p, T));
        Fill(server, "stone", 4, 16, 73, 79, 6, 18);
        Fill(server, "air", 4, 16, 80, 84, 6, 18);
        Fill(server, "water", 8, 12, 78, 79, 12, 15);
        p.State.Position = new Vector3f(10.5f, 80f, 10.5f);
        p.State.Yaw = 0f;
        p.State.SuitEnergy = 100f;
        p.State.Inventory.Add("boat", 1, 1);
        string boat = server.DeployVehicleForTest("Pilot", "boat");
        Assert.NotEqual(string.Empty, boat);
        p.State.Position = server.SpeederSnapshots.Single().Pos;
        server.EnterSpeederForTest("Pilot", boat);
        Assert.True(Done(p, "c13.4"));

        // No train runs here: the line about building one; beam blocks and lifts — and the course is through.
        Assert.True(Skipped(p, "c13.5"));
        var lines = transport.LineKeys(p);
        Assert.Contains("story.tutorial.c13.train.start_later", lines);
        Assert.Contains("story.tutorial.c13.beam.start", lines);
        Assert.True(CourseDone(p, "c13"));
    }

    [Fact]
    public void ATrain_OnTheWorld_MakesTheRideFeasible_AndBoardingItIsTheEvent()
    {
        // RailTests' line: three pylons and a cab on the salt flats.
        var (server, _, _) = World("train", c => { c.Seed = 1; c.PlaceIntercityRail = false; }, planet: "salt_flats");
        var events = new RecordingGameEventListener();
        server.AddGameEventListener("*", events);
        var b = LocalPastTraining(server, "Builder");
        Assert.False(server.WorldPredicateForTest(b, StoryWorldPredicates.TrainHere));
        const int y = 200;
        b.State.Fly = true;
        b.State.Inventory.SetSlot(5, new ItemStack("rail_pylon", 64));
        b.State.Inventory.SetSlot(7, new ItemStack("rail_cab", 1));
        foreach (int x in new[] { 0, 30, 60 })
        {
            b.State.Position = new Vector3f(x + 0.5f, y + 2, 0.5f);
            server.PlaceBlock(b.State.PlayerId, x, y, 0, "rail_pylon");
        }

        var cabAt = new Vector3f(30.5f, y + 1.5f, 0.5f);
        b.State.Position = new Vector3f(cabAt.X, cabAt.Y + 1f, cabAt.Z + 1.5f);
        server.UseGadgetForTest(b.State.PlayerId, "rail_cab", cabAt);
        var train = Assert.Single(server.TrainsForTest());
        Assert.True(server.WorldPredicateForTest(b, StoryWorldPredicates.TrainHere));

        b.State.Position = server.WagonPoseForTest(train.Id, 0)!.Value.Pos;
        server.EnterTrainForTest(b.State.PlayerId, train.Id, 0);
        Assert.Contains(events.Of(GameEventKeys.VehicleBoarded), e => e.Arg == GameEventKeys.VehicleTrain && e.Player == b.State.PlayerId);
    }

    // ---------------- C15 Together ----------------

    [Fact]
    public void TheTogetherCourse_ChatsTradesAlliesAndPings_AndAnObserverNeverCounts()
    {
        var (server, transport, _) = World("together");
        var alice = LocalPastTraining(server, "Alice");
        var bob = LocalPastTraining(server, "Bob");
        bob.State.Position = new Vector3f(alice.State.Position.X + 2f, alice.State.Position.Y, alice.State.Position.Z);

        // Another player close by is the moment — for both of them.
        Assert.True(server.WorldPredicateForTest(alice, StoryWorldPredicates.PlayerNear));
        server.TickStoryPacksForTest(alice);
        server.TickStoryPacksForTest(bob);
        Assert.Equal("Together", Assert.Single(transport.Offers(alice)));
        Assert.Equal("Together", Assert.Single(transport.Offers(bob)));

        // An invisible observer beside a lone player is nobody: no moment, no offer (#2460 N2).
        var carol = LocalPastTraining(server, "Carol", 500f);
        var watcher = LocalPastTraining(server, "Watcher", 500f);
        watcher.State.Position = new Vector3f(carol.State.Position.X + 2f, carol.State.Position.Y, carol.State.Position.Z);
        watcher.Spectating = true;
        Assert.False(server.WorldPredicateForTest(carol, StoryWorldPredicates.PlayerNear));
        server.TickStoryPacksForTest(carol, 3);
        Assert.Empty(transport.Offers(carol));

        // Chat: without a worn radio the line says how to get one.
        Start(server, alice, "c15");
        Assert.Equal("c15.1", server.ChipStepForTest(alice, T));
        Assert.Contains("story.tutorial.c15.chat.start_radio", transport.LineKeys(alice));
        server.Chat("Alice", "hello");
        Assert.False(Done(alice, "c15.1")); // refused: no radio
        TestGear.Wear(alice.State, "comm_radio");
        alice.LastChatTick = Environment.TickCount - 10_000;
        server.Chat("Alice", "hello Bob");
        Assert.True(Done(alice, "c15.1"));

        // A trade face to face (trade:player).
        Assert.Equal("c15.2", server.ChipStepForTest(alice, T));
        Give(alice, "iron_ore", 4);
        Give(bob, "carbon", 2);
        server.RequestTrade("Alice", "Bob");
        server.RespondTrade("Bob", true);
        server.SetTradeOffer("Alice", new[] { new ItemAmount("iron_ore", 2) });
        server.SetTradeOffer("Bob", new[] { new ItemAmount("carbon", 1) });
        server.ConfirmTrade("Alice");
        server.ConfirmTrade("Bob");
        Assert.True(Done(alice, "c15.2"));

        // Allies (ally:joined, both sides).
        Assert.Equal("c15.3", server.ChipStepForTest(alice, T));
        server.RequestAlliance("Alice", "Bob");
        server.RespondAlliance("Bob", "Alice", accept: true);
        Assert.True(server.AreAllied("Alice", "Bob"));
        Assert.True(Done(alice, "c15.3"));

        // A ping (marker:set ping), then "be kind" — the course is through.
        Assert.Equal("c15.4", server.ChipStepForTest(alice, T));
        var at = alice.State.Position;
        Deliver(server, alice, new MarkerActionIntent { Kind = "ping", X = at.X + 3f, Y = at.Y, Z = at.Z });
        Assert.True(Done(alice, "c15.4"));
        Assert.Contains("story.tutorial.c15.kind.start", transport.LineKeys(alice));
        Assert.True(CourseDone(alice, "c15"));
    }

    // ---------------- C16 Creative tools ----------------

    [Fact]
    public void TheCreativeToolsCourse_PaintsABlock_FormsAndColoursStacks()
    {
        var (server, transport, _) = World("creative_tools");
        var p = LocalPastTraining(server, "Artist");
        Unlock(p, "paint_tool");
        Start(server, p, "c16");
        Assert.Equal("c16.1", server.ChipStepForTest(p, T));

        // The paint tool on a placed block (paint:block).
        var pos = new Vector3i(5, 300, 0);
        server.World.SetBlock(pos, _content.GetBlock("stone")!.NumericId);
        p.State.Position = new Vector3f(5.5f, 301f, 0.5f);
        Deliver(server, p, new PaintBlockIntent { X = pos.X, Y = pos.Y, Z = pos.Z, Pixels = new string('3', 1024) });
        Assert.True(Done(p, "c16.1"));

        // A form and a colour from the slot actions (craft:shape, craft:tint).
        Assert.Equal("c16.2", server.ChipStepForTest(p, T));
        Give(p, "stone", 8);
        server.ShapeCraft("Artist", "stone", (int)BlockShape.Sphere);
        Assert.True(Done(p, "c16.2"));
        Assert.Equal("c16.3", server.ChipStepForTest(p, T));
        Deliver(server, p, new TintCraftIntent { SourceItemKey = "stone", Tint = 0x3366cc, Count = 2 });
        Assert.True(Done(p, "c16.3"));

        // A pasted copy (the blueprint tool's paste raises build:any blueprint_paste — #2465), then the look.
        Assert.Equal("c16.4", server.ChipStepForTest(p, T));
        server.RaiseGameEventForTest(p, GameEventKeys.BuildAny, GameEventKeys.BlueprintPasteArg, 12);
        Assert.True(Done(p, "c16.4"));
        Assert.Contains("story.tutorial.c16.look.start", transport.LineKeys(p));
        Assert.True(CourseDone(p, "c16"));
    }

    // ---------------- C18 Explorer ----------------

    [Fact]
    public void TheExplorerCourse_LootsAChest_ReachesARuin_AndFindsAWreck()
    {
        var (server, transport, _) = World("explorer");
        var p = LocalPastTraining(server, "Seeker");
        Start(server, p, "c18");
        Assert.Equal("c18.1", server.ChipStepForTest(p, T));

        // A world chest looted empty (loot:any).
        var f = p.State.Position;
        var cell = new Vector3i((int)Math.Floor(f.X) + 1, (int)Math.Floor(f.Y), (int)Math.Floor(f.Z));
        server.AddContainerForTest(new StoredContainer
        {
            Id = "loot_chest_course",
            Planet = p.CurrentLocationId,
            Kind = "chest",
            Position = cell,
            Items = new List<ItemStack> { new("iron_plate", 3) },
        });
        server.LootContainer(p.State.PlayerId, "loot_chest_course");
        Assert.True(Done(p, "c18.1"));

        // A buried ruin's entrance in reach: the 1 Hz advisor raises poi:reached before VEGA's "structural echoes" — which the
        // step's line already said.
        Assert.Equal("c18.2", server.ChipStepForTest(p, T));
        Assert.True(Has(p, "vega:hint:poi"));
        server.AddVaultEntranceForTest(new Vector3i((int)f.X + 10, (int)f.Y, (int)f.Z));
        server.TickForTest(1.1);
        Assert.True(Done(p, "c18.2"));
        Assert.Contains("story.tutorial.c18.tips.start", transport.LineKeys(p));

        // A wreck in space (salvage:found, raised by the wreck and capsule handlers — #2479).
        Assert.Equal("c18.4", server.ChipStepForTest(p, T));
        server.RaiseGameEventForTest(p, GameEventKeys.SalvageFound, GameEventKeys.SalvageWreck);
        Assert.True(CourseDone(p, "c18"));

        // A world without vaults or crashed wrecks: the line says so instead, and without free flight no wreck step.
        var (bare, bareTransport, _) = World("explorer_bare", c =>
        {
            c.World.Vaults = Frequency.Off;
            c.World.PlanetWrecks = Frequency.Off;
            c.Rules.FreeSpaceFlight = false;
        });
        var q = LocalPastTraining(bare, "Wanderer");
        q.State.Milestones.Add(StoryPackKeys.StepDone(T, "c18.1"));
        Start(bare, q, "c18");
        Assert.True(Skipped(q, "c18.2"));
        Assert.Contains("story.tutorial.c18.ruin.start_none", bareTransport.LineKeys(q));
        Assert.True(Skipped(q, "c18.4"));
        Assert.True(CourseDone(q, "c18"));
    }

    // ---------------- The new verbs ----------------

    [Fact]
    public void TheNewVerbs_AreRaisedByTheirHandlers()
    {
        var (server, _, _) = World("verbs");
        var events = new RecordingGameEventListener();
        server.AddGameEventListener("*", events);
        var a = LocalPastTraining(server, "Ann");
        var b = LocalPastTraining(server, "Ben");

        // A crew: the newcomer and the founder who invited them (ally:joined crew).
        server.CrewActionForTest("Ann", "create", "Stargazers");
        string crewId = server.CrewSnapshots.Single().Id;
        server.CrewActionForTest("Ann", "invite", target: "Ben");
        server.CrewActionForTest("Ben", "accept", crewId);
        Assert.Contains(events.Of(GameEventKeys.AllyJoined), e => e.Player == "Ben" && e.Arg == GameEventKeys.AllyCrew);
        Assert.Contains(events.Of(GameEventKeys.AllyJoined), e => e.Player == "Ann" && e.Arg == GameEventKeys.AllyCrew);

        // The jetpack fires only when worn (move:airborne jetpack).
        a.State.SuitEnergy = 100f;
        Deliver(server, a, new SetJetpackIntent { Active = true });
        Assert.Empty(events.Of(GameEventKeys.MoveAirborne));
        TestGear.Wear(a.State, "jetpack");
        Deliver(server, a, new SetJetpackIntent { Active = true });
        Assert.Contains(events.Of(GameEventKeys.MoveAirborne), e => e.Player == "Ann" && e.Arg == GameEventKeys.AirborneJetpack);

        // Climbing counts only in the pose, and only real metres (a report 4+ blocks up is a teleport).
        var at = a.State.Position;
        Deliver(server, a, new MoveIntent { X = at.X, Y = at.Y, Z = at.Z });
        Deliver(server, a, new MoveIntent { X = at.X, Y = at.Y + 2f, Z = at.Z }); // a jump, not a climb
        Deliver(server, a, new MoveIntent { X = at.X, Y = at.Y + 2.5f, Z = at.Z, Climbing = true });
        Deliver(server, a, new MoveIntent { X = at.X, Y = at.Y + 3.5f, Z = at.Z, Climbing = true });
        Assert.Equal(1, events.Of(GameEventKeys.MoveClimbed).Sum(e => e.Amount));

        // A chat line out on the radio — not a refused one, not a command.
        b.LastChatTick = Environment.TickCount - 10_000;
        server.Chat("Ben", "hi");
        Assert.Empty(events.Of(GameEventKeys.ChatSent));
        TestGear.Wear(b.State, "comm_radio");
        server.Chat("Ben", "hi");
        Assert.Single(events.Of(GameEventKeys.ChatSent));

        // A hover speeder deployed on dry stone and boarded (vehicle:boarded speeder).
        Fill(server, "stone", -12, 40, 61, 63, -12, 12);
        Fill(server, "air", -12, 40, 64, 68, -12, 12);
        b.State.Position = new Vector3f(0.5f, 64f, 0.5f);
        b.State.SuitEnergy = 100f;
        b.State.Inventory.Add("speeder", 1, 1);
        string speeder = server.DeploySpeederForTest("Ben");
        Assert.NotEqual(string.Empty, speeder);
        b.State.Position = server.SpeederSnapshots.Single().Pos;
        server.EnterSpeederForTest("Ben", speeder);
        Assert.Contains(events.Of(GameEventKeys.VehicleBoarded), e => e.Player == "Ben" && e.Arg == GameEventKeys.VehicleSpeeder);

        // A sapling planted (plant:any) and a world-predicate check of what plants: the sapling is no longer carried after.
        Fill(server, "dirt", 50, 50, 49, 49, 50, 50);
        Fill(server, "air", 50, 50, 50, 52, 50, 50);
        a.State.Inventory.Add("sapling", 1, 99);
        Assert.True(server.WorldPredicateForTest(a, StoryWorldPredicates.SeedCarried));
        a.State.Position = new Vector3f(51.5f, 50f, 51.5f);
        server.PlaceBlock("Ann", 50, 50, 50, "sapling");
        Assert.Contains(events.Of(GameEventKeys.PlantAny), e => e.Player == "Ann");
        Assert.False(server.WorldPredicateForTest(a, StoryWorldPredicates.SeedCarried));
    }
}
