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
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2473 — step 14 of the story packs (#2459): the basic training's cards (<c>data/stories/tutorial/cutscenes.json</c>).
/// The cards' shape (the §5.3 ids, tiers, titles only, never the camera for a card, the interruption budget) and their
/// bindings (the welcome as part 1's opener, the part card for parts 2–6, the part banner, the first launch and landing, the
/// graduation card); the flow for a fresh player (VEGA's prologue straight into the welcome, the part cards behind the 90 s
/// cooldown and dropped once their part is over, the "now or never" launch, the landing with the world's name leading into
/// the graduation card); once per player through "I know the game" and a full restart; older clients and migrated saves; the
/// shared argument rule (chapter title, place and count; the body's name) and its validator checks.
/// </summary>
public sealed class TutorialCardsTests : IDisposable
{
    private const string T = SvGameServer.OfficialTutorialPackId;
    private const string V = StoryRegistry.DefaultStoryId;

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public TutorialCardsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_tutorial_cards_" + Guid.NewGuid().ToString("N"));
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

        public CutscenePlay Play(PlayerSession who, string id) => To<CutscenePlay>(who).Single(c => c.CutsceneId == id);
    }

    private SqliteWorldRepository Repo(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return repo;
    }

    private (SvGameServer Server, RecordingTransport Transport) World(string world, Action<ServerConfig>? configure = null, SqliteWorldRepository? repo = null)
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
        config.Rules.FreeSpaceFlight = true; // the lift-off part with its flight firsts
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, repo ?? Repo(world));
        server.Start();
        return (server, transport);
    }

    /// <summary>A save that already holds a player (an older build's state).</summary>
    private SqliteWorldRepository SaveWith(string world, string name, IEnumerable<string> milestones)
    {
        var repo = Repo(world);
        repo.Initialize();
        var state = new PlayerState { PlayerId = name, Name = name };
        state.Milestones.UnionWith(milestones);
        repo.SavePlayer(state);
        return repo;
    }

    /// <summary>The real join path. <paramref name="cutscenes"/>: a current client; without, a client of the packs era that
    /// plays no cutscenes; <paramref name="packs"/> false: a released (legacy) client.</summary>
    private static PlayerSession Join(SvGameServer server, int conn, string name, bool cutscenes = true, bool packs = true)
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = !packs ? Array.Empty<string>()
                : cutscenes ? new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 } : new[] { ClientFeatures.Packs1 },
        }));
        return server.Sessions[conn];
    }

    /// <summary>The client is in the world and the cutscene queue had its tick.</summary>
    private static void Ready(SvGameServer server, PlayerSession p)
    {
        server.AdvanceUptimeForTest(SvGameServer.CutsceneJoinSettleSeconds);
        server.TickCutscenesForTest(p);
    }

    /// <summary>The 90 s cooldown runs out and the queue has its tick.</summary>
    private static void Cooldown(SvGameServer server, PlayerSession p)
    {
        server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
        server.TickCutscenesForTest(p);
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private StoryDefinition Pack => _content.Stories[T];

    private CutsceneDefinition Card(string id) => Pack.FindCutscene(id)!;

    private StoryStep StepDef(string id) => Pack.Chapters.SelectMany(c => c.Steps).Single(s => s.Id == id);

    private static string ChapterArg(string chapter, int place) => string.Join(LineText.ArgSeparator.ToString(), "story.tutorial.ch." + chapter, place.ToString(), "6");

    /// <summary>Plays the step that owns the chip — raising the event its real handler raises, or reporting its UI flag —
    /// while <paramref name="keepGoing"/> says so for the chip step's id (TutorialPackTests' walker).</summary>
    private List<string> Walk(SvGameServer server, PlayerSession p, Func<string, bool>? keepGoing = null, int maxSteps = 80)
    {
        var played = new List<string>();
        for (int i = 0; i < maxSteps; i++)
        {
            string? id = server.ChipStepForTest(p, T);
            if (id is null || (keepGoing is not null && !keepGoing(id)))
            {
                break;
            }

            var step = StepDef(id);
            string evt = step.Objective.Event;
            if (evt.EndsWith("*", StringComparison.Ordinal))
            {
                evt = GameEventKeys.All.First(k => GameEventKeys.Matches(evt, k));
            }

            string arg = (step.Objective.Arg ?? string.Empty).Split('|')[0];
            if (GameEventKeys.IsUiEvent(evt))
            {
                Deliver(server, p, new UiTutorialEvent { Id = evt.Substring(GameEventKeys.UiPrefix.Length) });
            }
            else
            {
                server.RaiseGameEventForTest(p, evt, arg, step.Objective.Required);
            }

            played.Add(id);
            server.TickStoryPacksForTest(p);
        }

        return played;
    }

    // ---------------- The cards ----------------

    [Fact]
    public void TheCards_UseTheBindingIds_AndTheTiersOfSynthesis64()
    {
        var ids = Pack.Cutscenes.Select(c => c.Id).ToList();
        Assert.Equal(new[] { "tut.welcome", "tut.strip", "tut.done", "tut.course", "tut.first_launch", "tut.first_dock", "tut.first_landing", "tut.first_jump", "tut.graduation" }, ids);
        Assert.All(ids, id => Assert.Contains(id, StoryCutsceneIds.Tutorial));
        foreach (string later in new[] { "tut.first_base", "tut.ship_commissioned", "tut.first_circuit", "tut.first_tame" })
        {
            Assert.Null(Pack.FindCutscene(later)); // they come with their chapters (the later deep dives)
        }

        Assert.Equal((CutsceneTiers.Card, false), (Card("tut.welcome").Tier, Card("tut.welcome").Template));
        Assert.Equal("b1", Card("tut.welcome").Chapter);
        Assert.Equal((CutsceneTiers.Card, true), (Card("tut.strip").Tier, Card("tut.strip").Template));
        Assert.Equal((CutsceneTiers.Banner, true), (Card("tut.done").Tier, Card("tut.done").Template));
        Assert.Equal((CutsceneTiers.Card, true), (Card("tut.course").Tier, Card("tut.course").Template));
        Assert.False(Card("tut.strip").Options.Replayable); // the Story tab lists the moments, not every part's card
        Assert.False(Card("tut.done").Options.Replayable);

        var launch = Card("tut.first_launch");
        Assert.Equal(CutsceneTiers.Scene, launch.Tier);
        Assert.False(launch.TakesCamera); // a letterbox over the launch rise — the camera stays the flight's
        Assert.False(launch.EffectiveHoldWorld);
        Assert.False(launch.EffectiveLockInput);
        Assert.True(launch.Options.CooldownExempt); // now or never: the rise does not wait
        Assert.All(launch.Shots, s => Assert.Equal(CutsceneCameras.Overlay, s.Camera.Type));

        var landing = Card("tut.first_landing");
        Assert.Equal(CutsceneTiers.Scene, landing.Tier);
        Assert.True(landing.TakesCamera);
        Assert.True(landing.EffectiveHoldWorld); // a singleplayer world holds while the camera circles the ship
        Assert.Equal(CutsceneArgSources.Body, landing.Arg);
        Assert.False(landing.Options.CooldownExempt); // it waits for the landing's loading veil, so it is no "now or never"
        Assert.Equal(new[] { "tut.graduation" }, landing.Options.ChainNext);
        Assert.True(landing.Shots.Sum(s => s.Duration) <= 12f); // synthesis §6.6: tutorial scenes ≤ 12 s

        var graduation = Card("tut.graduation");
        Assert.Equal(CutsceneTiers.Card, graduation.Tier);
        Assert.Equal(new[] { "story.tutorial.graduated" }, graduation.ReplaceLineKeys);
        // "graduation → ch1.open / recap" (§5.3 "Verkettungen"; VEGA's chapter 1 card since #2474)
        Assert.Equal(new[] { StoryCutsceneIds.Recap, V + "/ch1.open" }, graduation.Options.ChainNext);
    }

    [Fact]
    public void ACard_NeverTakesTheCamera_NorHolds_AndShowsTitlesOnly_SoItNeverWaitsForTheReader()
    {
        foreach (var card in Pack.Cutscenes.Where(c => c.Tier <= CutsceneTiers.Card))
        {
            Assert.False(card.TakesCamera, card.Id);
            Assert.False(card.EffectiveHoldWorld, card.Id);
            Assert.False(card.EffectiveLockInput, card.Id);
            Assert.True(card.Shots.Sum(s => s.Duration) <= 11f, card.Id);
            foreach (var caption in card.Shots.SelectMany(s => s.Captions))
            {
                // A subtitle or speaker page would wait for "continue" (Story text: wait for me) while the player walks on —
                // only the graduation shows a line, VEGA's own closing line, which it takes out of the panel.
                bool title = caption.Style == CutsceneCaptionStyles.TitleCard;
                Assert.True(title || card.ReplaceLineKeys.Contains(caption.Key), $"{card.Id}: {caption.Style} '{caption.Key}'");
            }
        }

        Assert.All(Card("tut.first_launch").Shots.SelectMany(s => s.Captions), c => Assert.Equal(CutsceneCaptionStyles.TitleCard, c.Style));
    }

    [Fact]
    public void TheBindings_PutACardOnEveryPart_AndTheFirstsOnTheirSteps()
    {
        Assert.Equal(string.Empty, Pack.IntroCutscene); // the welcome is part 1's opener: a migrated player who starts later gets that part's card
        Assert.Equal(new[] { "tut.welcome", "tut.strip", "tut.strip", "tut.strip", "tut.strip", "tut.strip" }, Pack.Chapters.Where(c => !c.Optional).Select(c => c.OpenCutscene));
        Assert.All(Pack.Chapters.Where(c => c.Optional), c => Assert.Equal("tut.course", c.OpenCutscene)); // #2479: every deep dive
        Assert.All(Pack.Chapters, c => Assert.Equal("tut.done", c.CloseCutscene));
        Assert.Equal("tut.first_launch", StepDef("b6.1").OnComplete.Cutscene);
        Assert.Equal("tut.first_landing", StepDef("b6.7").OnComplete.Cutscene);
        Assert.Equal(string.Empty, StepDef("b6.7a").OnComplete.Cutscene); // a landing on a known world is no "new world"
        Assert.Equal("tut.graduation", Pack.Graduation.Cutscene);
        Assert.Contains("tutorial/tut.welcome", _content.Stories[V].FindCutscene(StoryCutsceneIds.Prologue)!.Options.ChainNext);
        Assert.Empty(StoryPackValidator.Validate(Pack, StoryPackValidationTests.Context()).Where(i => i.Severity == StoryPackIssueSeverity.Error));
    }

    [Fact]
    public void TheBasicTraining_HasAtMostEightHeavierMoments_OnePerPart_ButTheFlightFirsts()
    {
        int heavy = 0;
        foreach (var chapter in Pack.Chapters.Where(c => !c.Optional))
        {
            var bound = new[] { chapter.OpenCutscene, chapter.CloseCutscene }
                .Concat(chapter.Steps.SelectMany(s => new[] { s.OnStart.Cutscene, s.OnComplete.Cutscene }))
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => Card(id))
                .Count(c => c.Tier >= CutsceneTiers.Card);
            Assert.True(chapter.Id == "b6" || bound <= 1, $"{chapter.Id} has {bound} cards or scenes");
            heavy += bound;
        }

        Assert.True(heavy <= 8, $"{heavy} cards or scenes in the basic training (synthesis §6.4: at most 8)");
    }

    [Fact]
    public void TheTexts_ReadAsPartCards_InBothLanguages_AndThePanelLinesNoLongerRepeatThePart()
    {
        var en = _content.CreateLocalizer(GameLocale.English);
        var de = _content.CreateLocalizer(GameLocale.German);
        string arg = string.Join(LineText.ArgSeparator.ToString(), en.Get("story.tutorial.ch.b3"), "3", "6");
        Assert.Equal("Basic training · Part 3 of 6", LineText.Compose(en.Get("story.tutorial.cut.part.kicker"), arg, a => a));
        Assert.Equal("Mining and carrying", LineText.Compose(en.Get("story.tutorial.cut.title_arg"), arg, a => a));
        Assert.Equal("Grundausbildung · Teil 3 von 6", LineText.Compose(de.Get("story.tutorial.cut.part.kicker"), arg, a => a));
        Assert.Equal("Graduate", en.Get("achv.graduate.name"));

        // G12: a card never repeats the panel line — the part's number lives on its card now, not in the first step's line.
        foreach (var chapter in Pack.Chapters)
        {
            foreach (var step in chapter.Steps)
            {
                foreach (string key in new[] { step.Explain.StartKey }.Concat(step.Variants.Select(v => v.StartKey)).Where(k => !string.IsNullOrEmpty(k)))
                {
                    Assert.DoesNotContain(" of 6", en.Get(key), StringComparison.Ordinal);
                    Assert.DoesNotContain(" von 6", de.Get(key), StringComparison.Ordinal);
                }
            }
        }
    }

    // ---------------- The flow ----------------

    [Fact]
    public void AFreshPlayer_GetsThePrologue_ThenTheWelcomeAtOnce_AndNoSecondCardForPartOne()
    {
        var (server, transport) = World("cards_fresh");
        var p = Join(server, 1, "Rookie");
        Assert.True(Has(p, StoryPackKeys.CutsceneSent(T, "tut.welcome"))); // committed at the join, sent once the client is in

        Ready(server, p);
        Assert.Equal(new[] { StoryCutsceneIds.Prologue, "tut.welcome" }, transport.Plays(p)); // chained: no 90 s gap
        Assert.Equal(ChapterArg("b1", 1), transport.Play(p, "tut.welcome").Arg);
        Assert.DoesNotContain(transport.Plays(p), id => id.StartsWith("tut.strip", StringComparison.Ordinal));
        Assert.Contains("story.tutorial.b1.walk.start", transport.LineKeys(p));
    }

    [Fact]
    public void WithoutTheStory_TheWelcomeOpensTheTraining_OnItsOwn()
    {
        var (server, transport) = World("cards_none", c => c.Rules.StoryId = StoryRegistry.NoneStoryId);
        var p = Join(server, 1, "Sandboxer");
        Ready(server, p);
        Assert.Equal(new[] { "tut.welcome" }, transport.Plays(p));
    }

    [Fact]
    public void PartCards_WaitForTheCooldown_BannersDoNot_AndACardIsDroppedOnceItsPartIsOver()
    {
        var (server, transport) = World("cards_cooldown");
        var p = Join(server, 1, "Rookie");
        Ready(server, p);

        Walk(server, p, id => id.StartsWith("b1.", StringComparison.Ordinal));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "b1")));
        Assert.Contains("tut.done@b1", transport.Plays(p)); // a banner never waits
        Assert.Equal(ChapterArg("b1", 1), transport.Play(p, "tut.done@b1").Arg);
        Assert.True(Has(p, StoryPackKeys.CutsceneSent(T, "tut.strip@b2")));
        Assert.DoesNotContain("tut.strip@b2", transport.Plays(p)); // the welcome went out just now

        Cooldown(server, p);
        Assert.Equal(ChapterArg("b2", 2), transport.Play(p, "tut.strip@b2").Arg);

        // Parts 2 and 3 in a rush: part 3's card waits behind the cooldown while part 3 goes by — it is dropped, not sent late.
        Walk(server, p, id => id.StartsWith("b2.", StringComparison.Ordinal) || id.StartsWith("b3.", StringComparison.Ordinal));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "b3")));
        Assert.Contains("tut.done@b3", transport.Plays(p));
        Cooldown(server, p);
        Assert.DoesNotContain("tut.strip@b3", transport.Plays(p));
        Assert.True(Has(p, StoryPackKeys.CutsceneSeen(T, "tut.strip@b3"))); // never offered again
        Assert.Contains("tut.strip@b4", transport.Plays(p)); // part 4 still runs
        Assert.Equal(1, transport.Plays(p).Count(id => id == "tut.strip@b2"));
    }

    [Fact]
    public void TheFirstLaunch_PlaysAtOnce_EvenInTheCooldown_AndOnlyOnce()
    {
        var (server, transport) = World("cards_launch");
        var p = Join(server, 1, "Pilot");
        Ready(server, p);
        Walk(server, p, id => id != "b6.1");
        Assert.Equal("b6.1", server.ChipStepForTest(p, T));
        Cooldown(server, p); // the lift-off part's card
        Assert.Contains("tut.strip@b6", transport.Plays(p));

        server.RaiseGameEventForTest(p, GameEventKeys.SpaceEnter, GameEventKeys.SpaceLaunch);
        Assert.Equal("tut.first_launch", transport.Plays(p).Last()); // 90 s have not passed since the part card
        Assert.Equal(string.Empty, transport.Play(p, "tut.first_launch").Arg);

        server.RaiseGameEventForTest(p, GameEventKeys.SpaceEnter, GameEventKeys.SpaceLaunch);
        server.TickStoryPacksForTest(p);
        Assert.Equal(1, transport.Plays(p).Count(id => id == "tut.first_launch"));
    }

    [Fact]
    public void TheFirstLanding_CarriesTheWorldsName_AndLeadsStraightIntoTheGraduationCard()
    {
        var (server, transport) = World("cards_landing");
        var p = Join(server, 1, "Pilot");
        Ready(server, p);
        Walk(server, p, id => id != "b6.7");
        Assert.Equal("b6.7", server.ChipStepForTest(p, T));
        Cooldown(server, p);

        server.RaiseGameEventForTest(p, GameEventKeys.VisitWorld, p.CurrentLocationId);
        Cooldown(server, p);
        string world = server.Galaxy.FindBody(p.CurrentLocationId)!.Name;
        Assert.False(string.IsNullOrEmpty(world));
        Assert.Equal(world, transport.Play(p, "tut.first_landing").Arg); // the title is the world's name

        Walk(server, p); // the rest of the part: the trip home, then the graduation
        Assert.True(Has(p, StoryPackKeys.Done(T)));
        var plays = transport.To<CutscenePlay>(p);
        Assert.Contains(plays, c => c.CutsceneId == "tut.done@b6");
        int graduation = plays.FindIndex(c => c.CutsceneId == "tut.graduation");
        Assert.True(graduation >= 0);
        // #2474: the graduation starts VEGA's story track; its chapter 1 card follows the graduation card at once (chained).
        Assert.Equal(new[] { (T, "tut.graduation"), (V, "ch1.open") }, plays.Skip(graduation).Select(c => (c.PackId, c.CutsceneId)));
        Cooldown(server, p);
        Assert.Equal(1, transport.Plays(p).Count(id => id == "tut.graduation"));
    }

    [Fact]
    public void TheGraduationCard_ShowsTheClosingLine_ComesOnce_AndAFullRestartShowsNoCardAgain()
    {
        var (server, transport) = World("cards_graduation");
        var p = Join(server, 1, "Graduate");
        Ready(server, p);
        Walk(server, p);
        for (int i = 0; i < 4; i++)
        {
            Cooldown(server, p);
        }

        Assert.Contains("tut.graduation", transport.Plays(p));
        Assert.Equal(string.Empty, transport.Play(p, "tut.graduation").Arg);
        Assert.Contains("story.tutorial.graduated", transport.LineKeys(p)); // still sent: the log, and a client without the card
        Assert.Contains("graduate", p.State.Achievements);
        int plays = transport.Plays(p).Count;

        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.RestartPack });
        Walk(server, p);
        for (int i = 0; i < 4; i++)
        {
            Cooldown(server, p);
        }

        Assert.True(Has(p, StoryPackKeys.Done(T)));
        Assert.Equal(plays, transport.Plays(p).Count); // no welcome, part card, banner, first or graduation card a second time
    }

    [Fact]
    public void AGraduationCardHeldByTheCooldown_HoldsItsLineToo_SoThePanelNeverSaysItFirst()
    {
        var (server, transport) = World("cards_hold");
        var p = Join(server, 1, "Graduate");
        string system = server.Galaxy.FindBody(p.CurrentLocationId)!.SystemId;
        foreach (var body in server.Galaxy.AllBodies().Where(b => b.SystemId == system))
        {
            p.State.LandedBodies.Add(body.Id); // the landing stand-in: no first-landing scene chains into the graduation
        }

        Ready(server, p);
        Walk(server, p);
        Assert.True(Has(p, StoryPackKeys.Done(T)));
        Assert.DoesNotContain("tut.graduation", transport.Plays(p)); // the first launch went out just now
        Assert.DoesNotContain("story.tutorial.graduated", transport.LineKeys(p)); // its line waits with it

        Cooldown(server, p);
        var sent = transport.Sent.Where(x => x.Conn == p.ConnectionId).Select(x => x.Msg).ToList();
        int card = sent.FindIndex(m => m is CutscenePlay c && c.CutsceneId == "tut.graduation");
        int line = sent.FindIndex(m => m is ShipAiLine l && l.LineKey == "story.tutorial.graduated");
        Assert.True(card >= 0 && line > card, $"card at {card}, line at {line}"); // the client claims the line for the card
    }

    [Fact]
    public void IKnowTheGame_ShowsNoMoreCards()
    {
        var (server, transport) = World("cards_skip");
        var p = Join(server, 1, "Veteran");
        Ready(server, p);
        int before = transport.To<CutscenePlay>(p).Count(c => c.PackId == T);

        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });
        for (int i = 0; i < 3; i++)
        {
            Cooldown(server, p);
        }

        Assert.True(Has(p, StoryPackKeys.Done(T)));
        Assert.Equal(before, transport.To<CutscenePlay>(p).Count(c => c.PackId == T)); // no part banner for skipped parts, no graduation
        Assert.False(Has(p, StoryPackKeys.CutsceneSent(T, "tut.graduation")));
        // #2474: the skip starts VEGA's story track — its chapter 1 card is the one card that comes
        Assert.Contains(transport.To<CutscenePlay>(p), c => c.PackId == V && c.CutsceneId == "ch1.open");
    }

    [Fact]
    public void AClientWithoutCutscenes_GetsEveryLine_AndTheCardsAreStampedSeen()
    {
        var (server, transport) = World("cards_nocutscenes");
        var p = Join(server, 1, "Headless", cutscenes: false);
        Walk(server, p);

        Assert.Empty(transport.Plays(p));
        Assert.True(Has(p, StoryPackKeys.Done(T)));
        foreach (string id in new[] { "tut.welcome", "tut.done@b1", "tut.strip@b2", "tut.strip@b6", "tut.done@b6", "tut.first_launch", "tut.first_landing", "tut.graduation" })
        {
            Assert.True(Has(p, StoryPackKeys.CutsceneSeen(T, id)), id); // never owed
        }

        var lines = transport.LineKeys(p);
        Assert.Contains("story.tutorial.b1.walk.start", lines);
        Assert.Contains("story.tutorial.graduated", lines); // the graduation line carries the moment
    }

    [Fact]
    public void AReleasedClient_InTheLegacyTutorial_GetsNoCard()
    {
        var (server, transport) = World("cards_legacy");
        var p = Join(server, 1, "Oldtimer", packs: false);
        Walk(server, p);
        Assert.Empty(transport.Plays(p));
        Assert.True(Has(p, StoryPackKeys.CutsceneSeen(T, "tut.strip@b2")));
    }

    [Fact]
    public void AMidTrainingSave_GetsThePartCardOfItsPart_NotTheWelcome_AndAVeteranNone()
    {
        // An older build's save on a VEGA world: the old catch-up always left B0, so its prologue counts as seen (P1).
        var midRepo = SaveWith("cards_mid", "Midway",
            new[] { StoryLegacy.IntroMilestone, StoryLegacy.StagePrefix + "eat", StoryLegacy.StagePrefix + "mine", StoryPackKeys.Beat(V, 0) });
        var (mid, midTransport) = World("cards_mid", repo: midRepo);
        var m = Join(mid, 1, "Midway");
        Ready(mid, m);
        Assert.True(Has(m, StoryPackKeys.ChapterDone(T, "b1"))); // evidenced by the old onboarding keys
        Assert.Equal(new[] { "tut.strip@b2" }, midTransport.Plays(m));

        var allStages = StoryLegacy.StageIds.Select(s => StoryLegacy.StagePrefix + s).Append(StoryLegacy.IntroMilestone).Append(StoryPackKeys.Beat(V, 0));
        var vetRepo = SaveWith("cards_veteran", "Veteran", allStages);
        var (vet, vetTransport) = World("cards_veteran", repo: vetRepo);
        var v = Join(vet, 1, "Veteran");
        Ready(vet, v);
        Cooldown(vet, v);
        Assert.True(Has(v, StoryPackKeys.Done(T)));
        Assert.DoesNotContain(vetTransport.To<CutscenePlay>(v), c => c.PackId == T);
    }

    // ---------------- The argument rule (shared by server and client) ----------------

    [Fact]
    public void TheArgument_OfAChapterBinding_IsTheTitlePlaceAndCount_AndASourceNamesWhereThePlayerIs()
    {
        Assert.Equal(ChapterArg("b3", 3), CutsceneArgs.For(Pack, Card("tut.strip"), "tut.strip@b3"));
        Assert.Equal(ChapterArg("b1", 1), CutsceneArgs.For(Pack, Card("tut.welcome"), "tut.welcome")); // its own chapter
        Assert.Equal(ChapterArg("b6", 6), CutsceneArgs.For(Pack, Card("tut.done"), "tut.done@b6.1")); // an owner step's chapter
        Assert.Equal(string.Empty, CutsceneArgs.For(Pack, Card("tut.first_launch"), "tut.first_launch"));
        Assert.Equal("Kepler", CutsceneArgs.For(Pack, Card("tut.first_landing"), "tut.first_landing", "Kepler", "Sol"));
        Assert.Equal("Sol", CutsceneArgs.For(Pack, new CutsceneDefinition { Id = "x", Arg = CutsceneArgSources.System }, "x", "Kepler", "Sol"));

        // An optional chapter counts among the optional ones.
        var probe = new StoryDefinition { Id = "probe" };
        probe.Chapters.Add(new StoryChapter { Id = "a", TitleKey = "k.a" });
        probe.Chapters.Add(new StoryChapter { Id = "x", TitleKey = "k.x", Optional = true });
        probe.Chapters.Add(new StoryChapter { Id = "b", TitleKey = "k.b" });
        var card = new CutsceneDefinition { Id = "c", Template = true };
        Assert.Equal(string.Join(LineText.ArgSeparator.ToString(), "k.x", "1", "1"), CutsceneArgs.For(probe, card, "c@x"));
        Assert.Equal(string.Join(LineText.ArgSeparator.ToString(), "k.b", "2", "2"), CutsceneArgs.For(probe, card, "c@b"));

        // The owner a template previews as: the first chapter that binds it, else the first chapter.
        Assert.Equal("b2", CutsceneArgs.DefaultOwner(Pack, Card("tut.strip")));
        Assert.Equal("b1", CutsceneArgs.DefaultOwner(Pack, Card("tut.done")));
        Assert.Equal("c2", CutsceneArgs.DefaultOwner(Pack, Card("tut.course"))); // #2479: the first deep dive that binds it
    }

    [Fact]
    public void Compose_FillsLaterSlots_EvenWithoutASlotZero()
    {
        string arg = string.Join(LineText.ArgSeparator.ToString(), "Mining", "3", "6");
        Assert.Equal("Part 3 of 6", LineText.Compose("Part {1} of {2}", arg, a => a));
        Assert.Equal("Part … of …", LineText.Compose("Part {1} of {2}", null, a => a));
        Assert.True(LineText.HasSlot("x {12} y"));
        Assert.False(LineText.HasSlot("{key:Interact} {x} {} {"));
        Assert.Equal("{name} stays", LineText.Compose("{name} stays", arg, a => a));
    }

    [Fact]
    public void TheValidator_RefusesAnUnknownArgumentSource_AndAGraduationCardThatDoesNotExist()
    {
        var pack = _content.Stories[T];
        string savedArg = Card("tut.first_landing").Arg;
        string savedCard = pack.Graduation.Cutscene;
        try
        {
            Card("tut.first_landing").Arg = "ship";
            pack.Graduation.Cutscene = "tut.nope";
            var errors = StoryPackValidator.Validate(pack, StoryPackValidationTests.Context()).Where(i => i.Severity == StoryPackIssueSeverity.Error).ToList();
            Assert.Contains(errors, e => e.Path.EndsWith(".arg", StringComparison.Ordinal) && e.Message.Contains("'ship'", StringComparison.Ordinal));
            Assert.Contains(errors, e => e.Path == "graduation.cutscene");
        }
        finally
        {
            Card("tut.first_landing").Arg = savedArg;
            pack.Graduation.Cutscene = savedCard;
        }
    }
}
