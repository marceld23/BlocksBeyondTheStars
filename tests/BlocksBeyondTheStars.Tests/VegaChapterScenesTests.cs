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
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2474 — step 15 of the story packs (#2459): VEGA's chapter scenes and beat cards (maintainer decision (a): chapter openings
/// staged, B5/B9/B12 big scenes, the other beats as cards in the frame with free input). The data: every chapter of the arc
/// bound to its opening, the beats that open no chapter to their card, the tiers, the captions that reuse the beat texts and
/// keep them out of VEGA's panel (no double reading), the chapter numbers as the Story tab shows them, the stingers. The
/// server flow: the track start (the recap, only the current chapter's scene; chapter 1's card followed by B1's card), a live
/// beat as a card for players whose track started (held for the others), the Guardian reveal with chapter 7's set piece, an
/// older client's lines, and the join catch-up of a player who was away (B09: the recap across a chapter, one card for one
/// missed beat).
/// </summary>
public sealed class VegaChapterScenesTests : IDisposable
{
    private const string V = StoryRegistry.DefaultStoryId;

    private static readonly string[] BeatCards = { "b01", "b03", "b04", "b07", "b08", "b10" };

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public VegaChapterScenesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_vega_scenes_" + Guid.NewGuid().ToString("N"));
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

        public List<object> To(PlayerSession who) => Sent.Where(x => x.Conn == who.ConnectionId).Select(x => x.Msg).ToList();

        public List<T> To<T>(PlayerSession who) => To(who).OfType<T>().ToList();

        public List<string> LineKeys(PlayerSession who) => To<ShipAiLine>(who).Where(l => l.LineKey.Length > 0).Select(l => l.LineKey).ToList();

        /// <summary>The VEGA scenes the player was sent, in order.</summary>
        public List<string> VegaPlays(PlayerSession who) => To<CutscenePlay>(who).Where(c => c.PackId == V).Select(c => c.CutsceneId).ToList();

        /// <summary>Where a scene and a line went out in the player's stream (-1 = never).</summary>
        public (int Scene, int Line) Order(PlayerSession who, string scene, string lineKey)
        {
            var sent = To(who);
            return (sent.FindIndex(m => m is CutscenePlay c && c.PackId == V && c.CutsceneId == scene),
                sent.FindIndex(m => m is ShipAiLine l && l.LineKey == lineKey));
        }
    }

    private SqliteWorldRepository Repo(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return repo;
    }

    private (SvGameServer Server, RecordingTransport Transport) World(string world, Action<ServerConfig>? configure = null,
        SqliteWorldRepository? repo = null)
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
        var server = new SvGameServer(config, _content, transport, repo ?? Repo(world));
        server.Start();
        return (server, transport);
    }

    /// <summary>A save holding a player whose story track started before (the tutorial off for the world, the migration
    /// done): <paramref name="heardUpTo"/> beats heard, <paramref name="seenScenes"/> watched.</summary>
    private SqliteWorldRepository SavedStoryPlayer(string world, string name, int heardUpTo, params string[] seenScenes)
    {
        var repo = Repo(world);
        repo.Initialize();
        var state = new PlayerState { PlayerId = name, Name = name, KnowledgePoints = heardUpTo };
        state.Milestones.Add(StoryPackKeys.Started(V));
        state.Milestones.Add(StoryPackKeys.Migrated(V));
        state.Milestones.Add(StoryPackKeys.CutsceneSeen(V, StoryCutsceneIds.Prologue));
        for (int i = 0; i <= heardUpTo; i++)
        {
            state.Milestones.Add(StoryPackKeys.Beat(V, i));
        }

        foreach (string scene in seenScenes)
        {
            state.Milestones.Add(StoryPackKeys.CutsceneSeen(V, scene));
        }

        repo.SavePlayer(state);
        return repo;
    }

    private static void TutorialOff(ServerConfig c) => c.Rules.TutorialPolicy = TutorialPolicy.Off;

    /// <summary>The real join path. <paramref name="cutscenes"/>: a current client (it plays cutscenes); without, a client of
    /// the packs era that plays none.</summary>
    private static PlayerSession Join(SvGameServer server, int conn, string name, bool cutscenes = true)
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = cutscenes ? new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 } : new[] { ClientFeatures.Packs1 },
        }));
        return server.Sessions[conn];
    }

    /// <summary>The client is in the world, its prologue (and the welcome chained to it) went out and was watched; the
    /// recording starts afresh.</summary>
    private static void Settled(SvGameServer server, RecordingTransport transport, PlayerSession p)
    {
        server.AdvanceUptimeForTest(SvGameServer.CutsceneJoinSettleSeconds);
        server.TickCutscenesForTest(p);
        Deliver(server, p, new CutsceneSeenIntent { PackId = V, Ids = new[] { StoryCutsceneIds.Prologue } });
        server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
        transport.Sent.Clear();
    }

    /// <summary>The cooldown runs out <paramref name="times"/> times, each followed by a queue tick.</summary>
    private static void Drain(SvGameServer server, PlayerSession p, int times = 4)
    {
        server.AdvanceUptimeForTest(SvGameServer.CutsceneJoinSettleSeconds);
        for (int i = 0; i < times; i++)
        {
            server.TickCutscenesForTest(p);
            server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
        }

        server.TickCutscenesForTest(p);
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private StoryDefinition Vega => _content.Stories[V];

    private CutsceneDefinition Scene(string id) => Vega.FindCutscene(id)!;

    private StoryBeat BeatDef(int n) => Vega.Beats.Single(b => b.Index == n);

    /// <summary>#2478: a scene bound to a finale stage (the arrival … the ending) — not one of the arc's chapter or beat scenes.</summary>
    private bool IsFinaleScene(string id) => Vega.Finale.Cutscenes.ContainsValue(id);

    // Milestones (×2 points each) the world needs to reach a beat's threshold at Normal density.
    private const int ToB1 = 3;
    private const int ToB3 = 12;
    private const int ToB4 = 18;
    private const int ToB5 = 25;

    // ---------------- The data ----------------

    [Fact]
    public void EveryChapterOfTheArc_OpensWithItsScene_AndEveryOtherBeatCarriesItsCard()
    {
        var chapters = Vega.Chapters;
        Assert.Equal(StoryCutsceneIds.Prologue, chapters[0].OpenCutscene);
        for (int n = 1; n <= 7; n++)
        {
            Assert.Equal($"ch{n}.open", chapters[n].OpenCutscene);
            var scene = Scene($"ch{n}.open");
            Assert.Equal(chapters[n].Id, scene.Chapter); // the card's "Chapter n" and title come from its own chapter
        }

        Assert.Equal(string.Empty, chapters[8].OpenCutscene); // the ending comes with the finale (step 17b)

        foreach (var beat in Vega.Beats)
        {
            string expected = BeatCards.Contains($"b{beat.Index:00}") ? $"b{beat.Index:00}" : string.Empty;
            Assert.Equal(expected, beat.Cutscene);
            if (expected.Length > 0)
            {
                Assert.Equal(StoryChapters.ChapterIndexOfBeat(Vega, beat.Index), chapters.IndexOf(chapters.Single(c => c.Id == Scene(expected).Chapter)));
            }
        }

        // A beat that opens a chapter has no card of its own (its chapter's opening shows it) — except B1 (see below).
        Assert.All(Vega.Beats.Where(b => b.Index > 1 && StoryChapters.OpenedByBeat(Vega, b.Index) is not null), b => Assert.Equal(string.Empty, b.Cutscene));

        // The built-in fallback mirrors the bindings (it defines no scenes: a bound id only logs once, the line plays).
        var builtin = StoryRegistry.Default;
        Assert.Equal(chapters.Select(c => c.OpenCutscene), builtin.Chapters.Select(c => c.OpenCutscene));
        Assert.Equal(Vega.Beats.Select(b => b.Cutscene), builtin.Beats.Select(b => b.Cutscene));
        Assert.Empty(builtin.Cutscenes);
    }

    [Fact]
    public void TheTiers_FollowDecisionA_CardsKeepTheInputFree_AndOnlyTheBigScenesTakeTheCamera()
    {
        // #2478: b10 is the one beat moment (the live chart of the other players in multiplayer; a card alone) — see
        // VegaFinaleSetPiecesTests. Every other beat stays a card.
        foreach (string id in new[] { "ch1.open" }.Concat(BeatCards.Where(b => b != "b10")))
        {
            var card = Scene(id);
            Assert.Equal(CutsceneTiers.Card, card.Tier);
            Assert.False(card.TakesCamera, id);
            Assert.False(card.EffectiveHoldWorld, id);
            Assert.False(card.EffectiveLockInput, id); // free input — no card ever freezes the controls
            Assert.True(card.Options.Skippable, id);
        }

        var ch2 = Scene("ch2.open"); // VEGA's memory of the star chart
        Assert.Equal(CutsceneTiers.Scene, ch2.Tier);
        Assert.Contains(ch2.Shots, s => s.Camera.Type == CutsceneCameras.Chart && s.Grade == CutsceneGrades.Memory);

        var ch3 = Scene("ch3.open"); // "The Guardian" in the sleeping-core diorama (#2477; the sky before)
        Assert.Equal(CutsceneTiers.Scene, ch3.Tier);
        Assert.Contains(ch3.Shots, s => s.Camera.Type == CutsceneCameras.Diorama && s.Camera.Anchor == CutsceneAnchors.Core
                                        && s.Captions.Any(c => c.Key == "story.vega.beat05"));

        // #2477: the former chapter cards 4 and 6 are staged now (synthesis §6.5 lists them as T3) — a calm space vista for
        // "The Verdict", VEGA's memory chart drifting to its rim for "It Still Sleeps"; both in her memory tones.
        var ch4 = Scene("ch4.open");
        Assert.Equal(CutsceneTiers.Scene, ch4.Tier);
        Assert.Contains(ch4.Shots, s => s.Camera.Type == CutsceneCameras.SpaceVista && s.Grade == CutsceneGrades.Memory
                                        && s.Captions.Any(c => c.Key == "story.vega.beat06"));
        var ch6 = Scene("ch6.open");
        Assert.Equal(CutsceneTiers.Scene, ch6.Tier);
        Assert.Contains(ch6.Shots, s => s.Camera.Type == CutsceneCameras.Diorama && s.Camera.Anchor == CutsceneAnchors.StarMap
                                        && s.Grade == CutsceneGrades.Memory && s.Captions.Any(c => c.Key == "story.vega.beat11"));

        var ch5 = Scene("ch5.open"); // "What You Are" at the player's real heal tank
        Assert.Equal(CutsceneTiers.Scene, ch5.Tier);
        Assert.Contains(ch5.Shots, s => s.Camera.Type == CutsceneCameras.Static && s.Camera.Anchor == CutsceneAnchors.HealTank
                                        && s.Captions.Any(c => c.Key == "story.vega.beat09"));
        Assert.Contains(ch5.Shots, s => s.Sfx.Any(x => x.Cue == "clone_tank_bubble"));

        var ch7 = Scene("ch7.open"); // B12 + the Guardian system on the star map
        Assert.Equal(CutsceneTiers.SetPiece, ch7.Tier);
        Assert.Contains(ch7.Shots, s => s.Camera.Type == CutsceneCameras.Chart && s.Camera.Anchor == CutsceneAnchors.Core
                                        && s.Captions.Any(c => c.Key == "story.vega.guardian_revealed"));
        Assert.True(ch7.EffectiveHoldWorld); // a singleplayer world holds while its camera is taken (never in MP — the client)

        // Beats fire on score, not on a place: every camera is place-neutral (a synthetic stage always is, #2477; the other
        // players on the chart, #2478) or anchored to the player's own ship. (The finale's set pieces have places of their
        // own — VegaFinaleSetPiecesTests.)
        var placeNeutral = new[]
        {
            CutsceneAnchors.Player, CutsceneAnchors.Ship, CutsceneAnchors.HealTank, CutsceneAnchors.StarMap, CutsceneAnchors.Core,
            CutsceneAnchors.Players,
        };
        foreach (var scene in Vega.Cutscenes.Where(c => c.Id != StoryCutsceneIds.Prologue && !IsFinaleScene(c.Id)))
        {
            Assert.All(scene.Shots.Where(s => !CutsceneCameras.IsSynthetic(s.Camera.Type)),
                s => Assert.Contains(string.IsNullOrEmpty(s.Camera.Anchor) ? CutsceneAnchors.Player : s.Camera.Anchor, placeNeutral));
            Assert.All(scene.Shots, s => Assert.NotEqual(CutsceneAnchors.Custom, s.Camera.Anchor));
            float length = scene.Shots.Sum(s => s.Duration);
            Assert.True(length <= (scene.Tier == CutsceneTiers.SetPiece ? 40f : 30f), $"{scene.Id}: {length} s"); // synthesis §6.6
        }
    }

    /// <summary>#2477: the scenes upgraded with a synthetic stage keep their words — the chapter's title card, then the beat
    /// in VEGA's box — and stay within the length of a VEGA opening (synthesis §6.6: 12–25 s).</summary>
    [Theory]
    [InlineData("ch3.open", "story.vega.ch.guardian", "story.vega.beat05", CutsceneCameras.Diorama)]
    [InlineData("ch4.open", "story.vega.ch.verdict", "story.vega.beat06", CutsceneCameras.SpaceVista)]
    [InlineData("ch6.open", "story.vega.ch.sleeps", "story.vega.beat11", CutsceneCameras.Diorama)]
    public void TheStagedOpenings_KeepTheirCaptions_AndTheOpeningsLength(string id, string titleKey, string beatKey, string camera)
    {
        var scene = Scene(id);
        var captions = scene.Shots.SelectMany(s => s.Captions).ToList();
        Assert.Equal(new[] { titleKey, beatKey }, captions.Select(c => c.Key));
        Assert.Equal("story.vega.cut.chapter", captions[0].KickerKey);
        Assert.Equal(CutsceneCaptionStyles.Speaker, captions[1].Style);
        Assert.Equal(new[] { beatKey }, scene.ReplaceLineKeys);

        Assert.Equal(CutsceneCameras.PlayerView, scene.Shots[0].Camera.Type); // out of black onto the title
        var staged = scene.Shots[1];
        Assert.Equal(camera, staged.Camera.Type);
        Assert.Contains(beatKey, staged.Captions.Select(c => c.Key)); // VEGA speaks over the synthetic stage
        float length = scene.Shots.Sum(s => s.Duration);
        Assert.InRange(length, 12f, 25f);
        Assert.True(scene.Options.Skippable);
    }

    [Fact]
    public void EveryCaption_ReusesTheBeatsOwnText_AndKeepsItOutOfThePanel_SoNothingIsReadTwice()
    {
        var beatKeys = Vega.Beats.Select(b => b.TextKey).ToHashSet();
        foreach (var scene in Vega.Cutscenes.Where(c => c.Id != StoryCutsceneIds.Prologue && !IsFinaleScene(c.Id)))
        {
            foreach (var caption in scene.Shots.SelectMany(s => s.Captions))
            {
                if (caption.Style == CutsceneCaptionStyles.Speaker)
                {
                    Assert.Contains(caption.Key, scene.ReplaceLineKeys); // the panel does not say it again
                    Assert.True(beatKeys.Contains(caption.Key) || caption.Key == "story.vega.guardian_revealed", $"{scene.Id}: {caption.Key}");
                }
                else
                {
                    Assert.Equal(CutsceneCaptionStyles.TitleCard, caption.Style); // no new lore: titles only besides VEGA's lines
                    Assert.True(caption.Key.StartsWith("story.vega.ch.", StringComparison.Ordinal)
                                || caption.Key.StartsWith("story.vega.cut.", StringComparison.Ordinal), $"{scene.Id}: {caption.Key}");
                }
            }
        }

        // Each opening shows the beat that starts its chapter — but chapter 1's: a track may start before B1 was crossed.
        Assert.Empty(Scene("ch1.open").ReplaceLineKeys);
        for (int n = 2; n <= 7; n++)
        {
            var chapter = Vega.Chapters[n];
            Assert.Contains(BeatDef(chapter.FromBeat).TextKey, Scene($"ch{n}.open").ReplaceLineKeys);
        }

        foreach (string card in BeatCards)
        {
            int beat = int.Parse(card.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(new[] { BeatDef(beat).TextKey }, Scene(card).ReplaceLineKeys);
        }

        // The Guardian reveal's jump-generator line: shown by chapter 7's set piece, given back to the panel on an early skip.
        Assert.Equal(new[] { "story.vega.beat12", "story.vega.guardian_revealed" }, Scene("ch7.open").ReplaceLineKeys);
        Assert.Equal(new[] { "story.vega.guardian_revealed" }, Scene("ch7.open").Options.ReturnOnSkip);
    }

    [Fact]
    public void TheChapterCards_ReadAsTheStoryTabNumbersThem_InBothLanguages()
    {
        string arg = CutsceneArgs.For(Vega, Scene("ch3.open"), "ch3.open");
        Assert.Equal(new[] { "story.vega.ch.guardian", "3", "8" }, LineText.SplitArgs(arg));
        Assert.Equal("1", LineText.SplitArgs(CutsceneArgs.For(Vega, Scene("ch1.open"), "ch1.open"))[1]);
        Assert.Equal("7", LineText.SplitArgs(CutsceneArgs.For(Vega, Scene("ch7.open"), "ch7.open"))[1]);
        Assert.Equal("0", LineText.SplitArgs(CutsceneArgs.For(Vega, Scene(StoryCutsceneIds.Prologue), StoryCutsceneIds.Prologue))[1]);

        var en = _content.CreateLocalizer(GameLocale.English);
        var de = _content.CreateLocalizer(GameLocale.German);
        string kicker = Scene("ch3.open").Shots[0].Captions[0].KickerKey;
        Assert.Equal("Chapter 3", LineText.Compose(en.Get(kicker), arg, action => action));
        Assert.Equal("Kapitel 3", LineText.Compose(de.Get(kicker), arg, action => action));
        Assert.Equal("The Guardian", en.Get(Scene("ch3.open").Shots[0].Captions[0].Key));

        // The beat cards: "VEGA remembers" over the beat's own title.
        var b03 = Scene("b03").Shots[0].Captions[0];
        Assert.Equal("VEGA remembers", en.Get(b03.KickerKey));
        Assert.Equal("VEGA erinnert sich", de.Get(b03.KickerKey));
        Assert.Equal("Not scattered — erased", en.Get(b03.Key));
        Assert.Equal("Nicht verstreut — vernichtet", de.Get(b03.Key));
    }

    [Fact]
    public void TheStingers_AreSmallClips_AndEveryChapterCardPlaysOne()
    {
        string audio = Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "Resources", "audio");
        foreach (string sting in new[] { "chapter_sting", "chapter_sting_memory", "chapter_sting_reveal" })
        {
            var clip = new FileInfo(Path.Combine(audio, sting + ".mp3"));
            Assert.True(clip.Exists, sting);
            Assert.True(clip.Length <= 64 * 1024, $"{sting}: {clip.Length} bytes — the browser downloads every Resources clip"); // §6.6
            Assert.True(File.Exists(clip.FullName + ".meta"), sting + ".meta");
        }

        for (int n = 1; n <= 7; n++)
        {
            Assert.Contains(Scene($"ch{n}.open").Shots.SelectMany(s => s.Sfx), x => x.Cue.StartsWith("chapter_sting", StringComparison.Ordinal));
        }
    }

    // ---------------- The track start ----------------

    [Fact]
    public void ATrackStartInChapterOne_PlaysChapterOnesCard_ThenB1sCardAtOnce_WithTheLineHeldForIt()
    {
        var (server, transport) = World("start_ch1");
        server.AdminAdvanceStory(ToB1); // B1 crossed before the newcomer arrives
        var p = Join(server, 1, "Rookie");
        Settled(server, transport, p);

        server.SkipTutorialForTest(p);
        server.TickCutscenesForTest(p);

        Assert.Equal(new[] { "ch1.open", "b01" }, transport.VegaPlays(p)); // chained: no 90 s gap, no recap in chapter 1
        var (scene, line) = transport.Order(p, "b01", "story.vega.beat01");
        Assert.True(scene >= 0 && line > scene, $"card at {scene}, line at {line}"); // the client claims the line for its card
        var play = transport.To<CutscenePlay>(p).Single(c => c.CutsceneId == "ch1.open");
        Assert.Equal("1", LineText.SplitArgs(play.Arg)[1]); // "Chapter 1"
    }

    [Fact]
    public void ATrackStartBeforeB1_PlaysOnlyTheChapterCard_AndB1BringsItsCardLater()
    {
        var (server, transport) = World("start_ch0");
        var p = Join(server, 1, "Rookie");
        Settled(server, transport, p);

        server.SkipTutorialForTest(p);
        Drain(server, p);
        Assert.Equal(new[] { "ch1.open" }, transport.VegaPlays(p));
        Assert.DoesNotContain("story.vega.beat01", transport.LineKeys(p));

        server.AdminAdvanceStory(ToB1);
        Drain(server, p);
        Assert.Equal(new[] { "ch1.open", "b01" }, transport.VegaPlays(p)); // the opening once; the beat's card now
        var (scene, line) = transport.Order(p, "b01", "story.vega.beat01");
        Assert.True(scene >= 0 && line > scene);
    }

    [Fact]
    public void ATrackStartLaterOn_PlaysTheRecap_ThenOnlyTheCurrentChaptersScene_WhichShowsItsBeat()
    {
        var (server, transport) = World("start_ch3");
        server.AdminAdvanceStory(ToB5); // chapter 3 "The Guardian"
        var p = Join(server, 1, "Rookie");
        Settled(server, transport, p);

        server.SkipTutorialForTest(p);
        Drain(server, p);

        Assert.Equal(new[] { StoryCutsceneIds.Recap, "ch3.open" }, transport.VegaPlays(p)); // never ch1/ch2 nor the beat cards
        Assert.DoesNotContain(transport.LineKeys(p), k => k.StartsWith("story.vega.beat", StringComparison.Ordinal));
        Assert.All(Enumerable.Range(1, 5), n => Assert.True(Has(p, StoryPackKeys.Beat(V, n)))); // in the Journal
        Assert.False(Has(p, StoryPackKeys.CutsceneSent(V, "ch1.open")));
        Assert.False(Has(p, StoryPackKeys.CutsceneSent(V, "b04")));
    }

    [Fact]
    public void TheGuardianReveal_AtTheTrackStart_IsChapterSevensSetPiece_WithTheJumpGeneratorLineAfterIt()
    {
        var (server, transport) = World("start_ch7");
        var p = Join(server, 1, "Rookie");
        Settled(server, transport, p);
        server.AdminRevealFinale();

        server.SkipTutorialForTest(p);
        Drain(server, p);

        Assert.Equal(new[] { StoryCutsceneIds.Recap, "ch7.open" }, transport.VegaPlays(p));
        var (scene, line) = transport.Order(p, "ch7.open", "story.vega.guardian_revealed");
        Assert.True(scene >= 0 && line > scene, $"scene at {scene}, line at {line}"); // still sent: the panel's skip fallback, the log
    }

    // ---------------- Live beats ----------------

    [Fact]
    public void ALiveBeat_IsACardForPlayersWhoseTrackStarted_AndHeldForTheOthers()
    {
        var (server, transport) = World("live_card");
        var crew = Join(server, 1, "Crew");
        Settled(server, transport, crew);
        server.SkipTutorialForTest(crew);
        Drain(server, crew);
        var rookie = Join(server, 2, "Rookie"); // still in basic training
        Settled(server, transport, rookie);
        transport.Sent.Clear();

        server.AdminAdvanceStory(ToB3); // B1, B2, B3
        Drain(server, crew, 6);
        Drain(server, rookie, 6);

        Assert.Equal(new[] { "b01", "ch2.open", "b03" }, transport.VegaPlays(crew)); // one per cooldown, in arc order
        foreach (var (scene, key) in new[] { ("b01", "story.vega.beat01"), ("ch2.open", "story.vega.beat02"), ("b03", "story.vega.beat03") })
        {
            var (s, l) = transport.Order(crew, scene, key);
            Assert.True(s >= 0 && l > s, $"{scene}: scene at {s}, line at {l}");
        }

        Assert.Empty(transport.VegaPlays(rookie));
        Assert.DoesNotContain(transport.LineKeys(rookie), k => k.StartsWith("story.vega.beat", StringComparison.Ordinal));
        Assert.False(Has(rookie, StoryPackKeys.Beat(V, 3)));
    }

    [Fact]
    public void B12_PlaysChapterSevensSetPiece_AndBothItsLinesFollowTheScene()
    {
        var (server, transport) = World("live_b12");
        var p = Join(server, 1, "Crew");
        Settled(server, transport, p);
        server.SkipTutorialForTest(p);
        server.AdminAdvanceStory(88); // B11: chapter 6
        Drain(server, p, 12);
        transport.Sent.Clear();

        server.AdminRevealFinale(); // B12 and the Guardian system in one call
        Drain(server, p);

        Assert.Equal(new[] { "ch7.open" }, transport.VegaPlays(p));
        foreach (string key in new[] { "story.vega.beat12", "story.vega.guardian_revealed" })
        {
            var (scene, line) = transport.Order(p, "ch7.open", key);
            Assert.True(scene >= 0 && line > scene, $"{key}: scene at {scene}, line at {line}");
        }
    }

    [Fact]
    public void AnOlderClient_HearsTheBeatLines_GetsNoScene_AndTheScenesAreNeverOwed()
    {
        var (server, transport) = World("older");
        server.AdminAdvanceStory(ToB5);
        var p = Join(server, 1, "Older", cutscenes: false);

        server.SkipTutorialForTest(p);
        Drain(server, p);
        Assert.Empty(transport.VegaPlays(p));
        Assert.Contains("story.vega.beat05", transport.LineKeys(p)); // the newest beat, as before the chapter scenes
        Assert.True(Has(p, StoryPackKeys.CutsceneSeen(V, "ch3.open")));

        server.AdminAdvanceStory(33 - ToB5); // B6 opens chapter 4
        Drain(server, p);
        Assert.Empty(transport.VegaPlays(p));
        Assert.Contains("story.vega.beat06", transport.LineKeys(p));
        Assert.True(Has(p, StoryPackKeys.CutsceneSeen(V, "ch4.open")));
    }

    // ---------------- B09: the join catch-up of a player who was away ----------------

    [Fact]
    public void ARejoinAcrossChapters_GetsTheRecapAndTheCurrentChaptersScene_PlusTheJournalLine()
    {
        var repo = SavedStoryPlayer("away_far", "Away", heardUpTo: 1, "ch1.open", "b01");
        var (server, transport) = World("away_far", TutorialOff, repo);
        server.AdminAdvanceStory(ToB5); // B2…B5 while the player was offline: chapters 2 and 3

        var p = Join(server, 1, "Away");
        Drain(server, p);

        var news = Assert.Single(transport.To<ShipAiLine>(p), l => l.LineKey == StoryNarratorLines.JournalNews);
        Assert.Equal("4", news.LineArg);
        Assert.Equal(new[] { StoryCutsceneIds.Recap, "ch3.open" }, transport.VegaPlays(p)); // the recap leads into the scene at once
        var recap = transport.To<CutscenePlay>(p).Single(c => c.CutsceneId == StoryCutsceneIds.Recap);
        Assert.Equal(new[] { "story.vega.ch.signature", "story.vega.ch.service" }, LineText.SplitArgs(recap.Arg));
        Assert.DoesNotContain(transport.LineKeys(p), k => k.StartsWith("story.vega.beat", StringComparison.Ordinal));
        Assert.All(Enumerable.Range(2, 4), n => Assert.True(Has(p, StoryPackKeys.Beat(V, n))));
    }

    [Fact]
    public void ARejoinWithinTheChapter_GetsTheJournalLineOnly()
    {
        var repo = SavedStoryPlayer("away_near", "Away", heardUpTo: 2, "ch1.open", "b01", "ch2.open");
        var (server, transport) = World("away_near", TutorialOff, repo);
        server.AdminAdvanceStory(ToB4); // B3, B4 — still chapter 2

        var p = Join(server, 1, "Away");
        Drain(server, p);

        Assert.Single(transport.To<ShipAiLine>(p), l => l.LineKey == StoryNarratorLines.JournalNews);
        Assert.Empty(transport.VegaPlays(p)); // no recap within a chapter, the chapter's scene was watched
        Assert.DoesNotContain(transport.LineKeys(p), k => k.StartsWith("story.vega.beat", StringComparison.Ordinal));
    }

    [Fact]
    public void OneMissedBeat_PlaysItsCard_AndTheLineFollowsIt()
    {
        var repo = SavedStoryPlayer("away_one", "Away", heardUpTo: 3, "ch1.open", "b01", "ch2.open", "b03");
        var (server, transport) = World("away_one", TutorialOff, repo);
        server.AdminAdvanceStory(ToB4); // B4 only

        var p = Join(server, 1, "Away");
        Drain(server, p);

        Assert.DoesNotContain(transport.To<ShipAiLine>(p), l => l.LineKey == StoryNarratorLines.JournalNews);
        Assert.Equal(new[] { "b04" }, transport.VegaPlays(p));
        var (scene, line) = transport.Order(p, "b04", "story.vega.beat04");
        Assert.True(scene >= 0 && line > scene, $"card at {scene}, line at {line}");
    }

    [Fact]
    public void AMissedChapterOpener_CommitsItsSceneBeforeItsLine_SoTheClientNeverReadsItTwice()
    {
        var repo = SavedStoryPlayer("away_opener", "Away", heardUpTo: 4, "ch1.open", "b01", "ch2.open", "b03", "b04");
        var (server, transport) = World("away_opener", TutorialOff, repo);
        server.AdminAdvanceStory(ToB5); // B5 only: it opens chapter 3

        var p = Join(server, 1, "Away");
        Drain(server, p);

        Assert.Equal(new[] { "ch3.open" }, transport.VegaPlays(p));
        var (scene, line) = transport.Order(p, "ch3.open", "story.vega.beat05");
        Assert.True(scene >= 0 && line > scene, $"scene at {scene}, line at {line}");
    }
}
