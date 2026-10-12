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
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2478 — step 17b of the story packs (#2459): VEGA's finale set pieces, the duel's dialogue mode on the server side, the
/// ending on the director, the beat moment b10 and the finale fixes. The data (the finale bindings and their mirror, the
/// tiers and cameras, the core's voice); the flow per player and in multiplayer: the arrival card for the pilot who warps in,
/// the descent over the shaft that shows the guardian_core hint, the breach on the core world and the duel banner elsewhere,
/// the duel's answers only from the core world (B50) and its current node for whoever lands or rejoins mid-duel, the
/// stand-down and the ending on the director (an older client keeps StoryResolved — never both), the catch-up for a player
/// who missed the win (with the achievement for whoever had heard B12, E23 — an observer included), the machines standing
/// down on every loaded world (B51), the pause whitelist and the recap that never folds the ending away.
/// </summary>
public sealed class VegaFinaleSetPiecesTests : IDisposable
{
    private const string V = StoryRegistry.DefaultStoryId;

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public VegaFinaleSetPiecesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_vega_finale_" + Guid.NewGuid().ToString("N"));
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
        public List<string> Plays(PlayerSession who) => To<CutscenePlay>(who).Where(c => c.PackId == V).Select(c => c.CutsceneId).ToList();

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

    /// <summary>A world with the tutorial off, so a joining player's story track starts at the join.</summary>
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
        config.Rules.TutorialPolicy = TutorialPolicy.Off;
        config.Rules.FreeSpaceFlight = true;
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, repo ?? Repo(world));
        server.Start();
        return (server, transport);
    }

    /// <summary>The real join path; <paramref name="cutscenes"/> false = a client of the packs era that plays no scenes.</summary>
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

    /// <summary>The cooldown runs out <paramref name="times"/> times, each followed by a queue tick — every queued scene (and
    /// the lines it holds) goes out.</summary>
    private static void Drain(SvGameServer server, PlayerSession p, int times = 24)
    {
        server.AdvanceUptimeForTest(SvGameServer.CutsceneJoinSettleSeconds);
        for (int i = 0; i < times; i++)
        {
            server.TickCutscenesForTest(p);
            server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
        }

        server.TickCutscenesForTest(p);
    }

    /// <summary>A player in the story (the tutorial is off, so the track starts at the join), the join's scenes sent.</summary>
    private static PlayerSession Started(SvGameServer server, int conn, string name, bool cutscenes = true)
    {
        var p = Join(server, conn, name, cutscenes);
        Assert.Contains(StoryPackKeys.Started(V), p.State.Milestones);
        Drain(server, p);
        return p;
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private static bool HasFinaleAchievement(PlayerSession p)
        => p.State.AchievementCounters.TryGetValue(AchievementCounters.StoryFinale, out int n) && n > 0;

    private StoryDefinition Vega => _content.Stories[V];

    private CutsceneDefinition Scene(string id) => Vega.FindCutscene(id)!;

    /// <summary>Puts the player on the core world, inside the chamber in front of the column, and channels the hack open.</summary>
    private static Vector3i HackTheCore(SvGameServer server, PlayerSession pilot)
    {
        var centre = server.LoadGuardianCoreForTest(); // the core world is loaded and the active cursor
        pilot.CurrentLocationId = SvGameServer.GuardianCoreBodyId;
        pilot.State.Position = new Vector3f(centre.X + 2.5f, centre.Y + 0.5f, centre.Z + 2.5f);
        for (int i = 0; i < 20 && !server.IsCoreHackedForTest; i++)
        {
            server.CoreHackTickForTest(pilot.State.PlayerId);
        }

        Assert.True(server.IsCoreHackedForTest);
        return centre;
    }

    // ---------------- The data ----------------

    [Fact]
    public void TheFinale_BindsItsSetPieces_InThePackAndTheBuiltInMirror()
    {
        var expected = new Dictionary<string, string>
        {
            [StoryFinaleStages.Arrival] = "fin.arrival",
            [StoryFinaleStages.Descent] = "fin.descent",
            [StoryFinaleStages.Breach] = "fin.breach",
            [StoryFinaleStages.Duel] = "fin.duel",
            [StoryFinaleStages.Standdown] = "fin.standdown",
            [StoryFinaleStages.Resolution] = StoryCutsceneIds.Resolution,
        };
        Assert.Equal("guardian_core", Vega.Finale.Type);
        Assert.Equal(expected, Vega.Finale.Cutscenes);
        Assert.Equal(expected, StoryRegistry.Default.Finale.Cutscenes); // the built-in fallback mirrors it (it defines no scenes)
        Assert.All(expected.Values, id => Assert.NotNull(Vega.FindCutscene(id)));
        Assert.All(StoryCutsceneIds.Vega, id => Assert.NotNull(Vega.FindCutscene(id))); // every VEGA scene of §5.3 exists now

        // The core's voice in the duel dialogue: its name, Guardian red, the anomaly's hum.
        var core = Assert.Single(Vega.Speakers);
        Assert.Equal(("core", "ui.finale.duel_title", "#FF5A4A", "anomaly_hum"), (core.Id, core.NameKey, core.Color, core.LineStart));
        Assert.Same(core, Vega.FindSpeaker("core"));
        Assert.Same(Vega.Narrator, Vega.FindSpeaker(string.Empty));
        Assert.Null(Vega.FindSpeaker("yara"));
    }

    [Fact]
    public void TheSetPieces_HaveTheirTiersCamerasAndChains()
    {
        var arrival = Scene("fin.arrival"); // a "now or never" card on the warp-in: no camera, free input
        Assert.Equal(CutsceneTiers.Card, arrival.Tier);
        Assert.False(arrival.TakesCamera);
        Assert.True(arrival.Options.CooldownExempt);
        Assert.Equal(new[] { "fin.descent" }, arrival.Options.ChainNext);
        Assert.Equal("story.vega.ch.core", Assert.Single(arrival.Shots.SelectMany(s => s.Captions)).Key);

        var descent = Scene("fin.descent"); // over the shaft; it shows the guardian_core hint and gives it back on an early skip
        Assert.Equal(CutsceneTiers.Scene, descent.Tier);
        Assert.Equal(new[] { "vega.hint.guardian_core" }, descent.ReplaceLineKeys);
        Assert.Equal(new[] { "vega.hint.guardian_core" }, descent.Options.ReturnOnSkip);
        Assert.Contains(descent.Shots, s => s.Camera.Type == CutsceneCameras.Flyover && s.Camera.Anchor == CutsceneAnchors.Core
                                            && s.Captions.Any(c => c.Key == "vega.hint.guardian_core" && c.Style == CutsceneCaptionStyles.Speaker));

        var breach = Scene("fin.breach"); // the core wakes, close in its chamber — at once (the dialogue follows)
        Assert.Equal(CutsceneTiers.Scene, breach.Tier);
        Assert.True(breach.Options.CooldownExempt);
        Assert.Contains(breach.Shots, s => s.Camera.Type == CutsceneCameras.Static && s.Camera.Anchor == CutsceneAnchors.Core);
        Assert.Equal("music_boss_dialogue", breach.Music);

        var duel = Scene("fin.duel"); // the banner for the finale's players away from the core world
        Assert.Equal(CutsceneTiers.Banner, duel.Tier);
        Assert.False(duel.Options.Replayable);

        var standdown = Scene("fin.standdown"); // VEGA's resolution line over the sleeping core, then the ending at once
        Assert.Equal(CutsceneTiers.SetPiece, standdown.Tier);
        Assert.Equal(new[] { "story.vega.finale_resolved" }, standdown.ReplaceLineKeys);
        Assert.Equal(new[] { StoryCutsceneIds.Resolution }, standdown.Options.ChainNext);
        Assert.Equal("music_boss_resolution", standdown.Music);

        var ending = Scene(StoryCutsceneIds.Resolution); // card → credits → epilogue, replayable
        Assert.Equal(CutsceneTiers.SetPiece, ending.Tier);
        Assert.True(ending.Options.Replayable);
        Assert.Equal(new[] { "story.vega.beat13" }, ending.ReplaceLineKeys);
        var captions = ending.Shots.SelectMany(s => s.Captions).ToList();
        Assert.Contains(captions, c => c.Style == CutsceneCaptionStyles.Credits && c.Key == "ui.credits.body");
        Assert.Contains(captions, c => c.Style == CutsceneCaptionStyles.ReadingFrame && c.Key == "story.vega.beat13"
                                       && c.KickerKey == "story.vega.epilogue.title");
        Assert.Equal("music_boss_resolution", ending.Music);
        Assert.True(ending.Shots.Sum(s => s.Duration) <= 45f); // about the old ending's 43 s
    }

    [Fact]
    public void TheBeatMoment_b10_ChartsTheOtherPlayers_TheOtherBeatsStayCards()
    {
        var b10 = Scene("b10");
        Assert.Equal(CutsceneTiers.Scene, b10.Tier);
        Assert.Contains(b10.Shots, s => s.Camera.Type == CutsceneCameras.Chart && s.Camera.Anchor == CutsceneAnchors.Players
                                        && s.Captions.Any(c => c.Key == "story.vega.beat10"));
        Assert.Equal(new[] { "story.vega.beat10" }, b10.ReplaceLineKeys);
        Assert.Equal("story.vega.cut.b10.title", b10.Shots[0].Captions[0].Key); // the same title as the card it was

        foreach (string id in new[] { "b01", "b03", "b04", "b07", "b08" })
        {
            Assert.Equal(CutsceneTiers.Card, Scene(id).Tier); // maintainer decision (a): free input
            Assert.False(Scene(id).TakesCamera, id);
        }
    }

    [Fact]
    public void TheDuelNode_CarriesItsCountAndTheColumn_AndAnOlderServersMessageReadsAsNone()
    {
        var message = new CoreDialogueMessage
        {
            Node = 1,
            NodeCount = 4,
            PromptKey = "core.duel.n1.prompt",
            ChoiceKeys = new[] { "a", "b" },
            HasAnchor = true,
            AnchorX = 48.5f,
            AnchorY = 41f,
            AnchorZ = 24.5f,
        };
        var decoded = Assert.IsType<CoreDialogueMessage>(NetCodec.Decode(NetCodec.Encode(message)));
        Assert.Equal((4, true, 48.5f, 41f, 24.5f), (decoded.NodeCount, decoded.HasAnchor, decoded.AnchorX, decoded.AnchorY, decoded.AnchorZ));

        var old = new CoreDialogueMessage { Node = 0, PromptKey = "p" };
        Assert.Equal((0, false), (old.NodeCount, old.HasAnchor));
    }

    // ---------------- The arrival ----------------

    [Fact]
    public void TheArrival_PlaysForThePilotWhoWarpsIn_Once_AndForNobodyElse()
    {
        var (server, transport) = World("fin_arrival");
        var pilot = Started(server, 1, "Pilot");
        var friend = Started(server, 2, "Friend");
        server.AdminRevealFinale();
        Drain(server, pilot);
        Drain(server, friend);
        transport.Sent.Clear();

        server.JumpIntoGuardianSystemForTest(pilot);
        Drain(server, pilot);
        Drain(server, friend);

        Assert.True(server.InSpace(pilot.State.PlayerId));
        Assert.Contains("fin.arrival", transport.Plays(pilot));
        Assert.True(Has(pilot, StoryPackKeys.CutsceneSent(V, "fin.arrival")));
        Assert.DoesNotContain("fin.arrival", transport.Plays(friend)); // only whoever is in the Guardian system

        transport.Sent.Clear();
        server.JumpIntoGuardianSystemForTest(pilot); // once per player
        Drain(server, pilot);
        Assert.DoesNotContain("fin.arrival", transport.Plays(pilot));
    }

    [Fact]
    public void TheArrival_IsStampedSeenForAnOlderClient_AndNeverPlaysAfterTheWin()
    {
        var (server, transport) = World("fin_arrival_old");
        var old = Started(server, 1, "Old", cutscenes: false);
        var late = Started(server, 2, "Late");
        server.AdminRevealFinale();

        server.JumpIntoGuardianSystemForTest(old);
        Assert.Empty(transport.To<CutscenePlay>(old));
        Assert.True(Has(old, StoryPackKeys.CutsceneSeen(V, "fin.arrival"))); // nothing owed — never offered again

        server.MarkGuardianDefeatedForTest();
        Drain(server, late);
        transport.Sent.Clear();
        server.JumpIntoGuardianSystemForTest(late);
        Drain(server, late);
        Assert.DoesNotContain("fin.arrival", transport.Plays(late)); // the core is quiet: no "arrival" any more
    }

    // ---------------- The descent ----------------

    [Fact]
    public void TheDescent_FliesOverTheShaft_AndShowsTheGuardianCoreHint_BeforeThePanelGetsIt()
    {
        var (server, transport) = World("fin_descent");
        var p = Started(server, 1, "Lander");
        server.AdminRevealFinale();
        Drain(server, p);
        transport.Sent.Clear();

        server.AdminGotoCore(p); // the landing on the core world (the chamber is stamped on its first load)
        Drain(server, p);

        var play = Assert.Single(transport.To<CutscenePlay>(p), c => c.CutsceneId == "fin.descent");
        var shaft = server.CoreShaftAnchorForTest!.Value;
        Assert.True(play.HasAnchor);
        Assert.Equal((shaft.X, shaft.Y, shaft.Z), (play.AnchorX, play.AnchorY, play.AnchorZ));
        var (scene, line) = transport.Order(p, "fin.descent", "vega.hint.guardian_core");
        Assert.True(scene >= 0 && line > scene, $"scene at {scene}, line at {line}"); // the client keeps the hint for the scene

        // The anchor sits on the shaft the stamp carved: open from the chamber's ceiling up to the surface.
        var column = server.CoreColumnAnchorForTest!.Value;
        int x = (int)Math.Floor(column.X), z = (int)Math.Floor(column.Z) + 3;
        for (int y = (int)column.Y + 5; y < (int)shaft.Y; y++)
        {
            Assert.True(server.World.GetBlock(new Vector3i(x, y, z)).IsAir, $"the shaft is blocked at y={y}");
        }

        // Gone before watching it: the next join offers it again — with its place (the queue was lost, the shaft was not).
        server.DisconnectLocalPlayerForTest("Lander");
        transport.Sent.Clear();
        var back = Join(server, 2, "Lander");
        Drain(server, back);
        var again = Assert.Single(transport.To<CutscenePlay>(back), c => c.CutsceneId == "fin.descent");
        Assert.True(again.HasAnchor);
        Assert.Equal((shaft.X, shaft.Y, shaft.Z), (again.AnchorX, again.AnchorY, again.AnchorZ));
    }

    // ---------------- The breach and the duel ----------------

    [Fact]
    public void TheHack_BreachesOnTheCoreWorld_TheOthersGetTheBanner_AndOnlyTheCoreWorldAnswers()
    {
        var (server, transport) = World("fin_breach");
        var core = Started(server, 1, "Core");
        var away = Started(server, 2, "Away");
        server.AdminRevealFinale();
        Drain(server, core);
        Drain(server, away);
        transport.Sent.Clear();

        var centre = HackTheCore(server, core);
        Drain(server, core);
        Drain(server, away);

        // The core world: the breach, framed on the column (the centre of its floor cell), before the first node.
        var breach = Assert.Single(transport.To<CutscenePlay>(core), c => c.CutsceneId == "fin.breach");
        Assert.True(breach.HasAnchor);
        Assert.Equal((centre.X + 0.5f, (float)centre.Y, centre.Z + 0.5f), (breach.AnchorX, breach.AnchorY, breach.AnchorZ));
        var coreStream = transport.To(core);
        int scene = coreStream.FindIndex(m => m is CutscenePlay c && c.CutsceneId == "fin.breach");
        int node = coreStream.FindIndex(m => m is CoreDialogueMessage d && !d.Won);
        Assert.True(scene >= 0 && node > scene, $"breach at {scene}, node at {node}");
        Assert.DoesNotContain("fin.duel", transport.Plays(core));

        // Elsewhere: the banner, never the breach.
        Assert.Contains("fin.duel", transport.Plays(away));
        Assert.DoesNotContain("fin.breach", transport.Plays(away));

        // Both hear the node (the dialogue shows only on the core world — the client's rule): the count and the column.
        var first = transport.To<CoreDialogueMessage>(away).Last();
        Assert.Equal((0, 4, "core.duel.n0.prompt"), (first.Node, first.NodeCount, first.PromptKey));
        Assert.True(first.HasAnchor);

        // B50: an answer from anywhere but the core world is dropped; the core world's counts.
        server.CoreDialogueChoiceForTest(away.State.PlayerId, 0);
        Assert.Equal(0, server.DuelNodeForTest);
        server.CoreDialogueChoiceForTest(core.State.PlayerId, 0);
        Assert.Equal(1, server.DuelNodeForTest);
    }

    [Fact]
    public void APilotFlyingOverTheCore_CannotAnswerTheDuel()
    {
        var (server, transport) = World("fin_pilot");
        var core = Started(server, 1, "Core");
        var pilot = Started(server, 2, "Pilot");
        server.AdminRevealFinale();
        server.JumpIntoGuardianSystemForTest(pilot); // anchored on the core body — but in its flight
        Assert.Equal(SvGameServer.GuardianCoreBodyId, pilot.CurrentLocationId);
        Assert.True(server.InSpace(pilot.State.PlayerId));

        HackTheCore(server, core);
        Drain(server, pilot);
        Assert.Contains("fin.duel", transport.Plays(pilot)); // the flight is no core world: the banner

        server.CoreDialogueChoiceForTest(pilot.State.PlayerId, 0);
        Assert.Equal(0, server.DuelNodeForTest);
    }

    [Fact]
    public void WhoeverLandsOrRejoinsMidDuel_SeesItsCurrentNode_AndNoDescent()
    {
        var (server, transport) = World("fin_midduel");
        var core = Started(server, 1, "Core");
        var lander = Started(server, 2, "Lander");
        server.AdminRevealFinale();
        HackTheCore(server, core);
        server.CoreDialogueChoiceForTest(core.State.PlayerId, 0); // the duel at node 1
        Drain(server, lander);
        transport.Sent.Clear();

        server.AdminGotoCore(lander);
        Drain(server, lander);
        var node = Assert.Single(transport.To<CoreDialogueMessage>(lander));
        Assert.Equal((1, "core.duel.n1.prompt"), (node.Node, node.PromptKey));
        Assert.DoesNotContain("fin.descent", transport.Plays(lander)); // the duel already runs: straight to the core

        // A relog in the middle of the duel (it used to show nothing until somebody else answered).
        server.DisconnectLocalPlayerForTest(core.State.PlayerId);
        transport.Sent.Clear();
        var back = Join(server, 3, "Core");
        var again = transport.To<CoreDialogueMessage>(back).Last();
        Assert.Equal((1, "core.duel.n1.prompt", false), (again.Node, again.PromptKey, again.Won));
    }

    [Fact]
    public void WhileHeld_ADuelAnswerIsServed()
    {
        var (server, transport) = World("fin_held");
        var solo = server.AddLocalPlayer("Solo");
        server.StartStoryTrackForTest(solo);
        server.AdminRevealFinale();
        HackTheCore(server, solo);
        server.PauseForTest(solo, true); // the duel dialogue holds a singleplayer world
        Assert.True(server.IsPaused);

        Deliver(server, solo, new CoreDialogueChoiceIntent { ChoiceIndex = 0 });

        Assert.Equal(1, server.DuelNodeForTest);
        Assert.True(server.IsPaused);
        Assert.NotEmpty(transport.To<CoreDialogueMessage>(solo));
    }

    // ---------------- The win: the stand-down and the ending ----------------

    [Fact]
    public void TheWin_PlaysTheStanddownAndTheEnding_OnTheDirector_AnOlderClientKeepsStoryResolved_NeverBoth()
    {
        var (server, transport) = World("fin_win");
        var crew = Started(server, 1, "Crew");
        var old = Started(server, 2, "Old", cutscenes: false);
        server.AdminRevealFinale();
        Drain(server, crew);
        Drain(server, old);
        transport.Sent.Clear();

        server.WinDuelForTest();
        Drain(server, crew);
        Drain(server, old);

        // The director: the stand-down, then the ending (chained) — each before the line it shows.
        var plays = transport.Plays(crew);
        int standdown = plays.IndexOf("fin.standdown");
        Assert.True(standdown >= 0 && plays.IndexOf(StoryCutsceneIds.Resolution) > standdown, string.Join(", ", plays));
        var (s1, l1) = transport.Order(crew, "fin.standdown", "story.vega.finale_resolved");
        Assert.True(s1 >= 0 && l1 > s1, $"stand-down at {s1}, its line at {l1}");
        var (s2, l2) = transport.Order(crew, StoryCutsceneIds.Resolution, "story.vega.beat13");
        Assert.True(s2 >= 0 && l2 > s2, $"ending at {s2}, the epilogue at {l2}");
        Assert.Empty(transport.To<StoryResolved>(crew));
        Assert.True(Has(crew, StoryPackKeys.CutsceneSent(V, StoryCutsceneIds.Resolution)));
        Assert.True(Has(crew, StoryPackKeys.Prefix(V) + "resolved")); // an older build of this save does not replay it at the door
        Assert.True(HasFinaleAchievement(crew));

        // An older client: the lines and the old cinematic, no scene.
        Assert.Single(transport.To<StoryResolved>(old));
        Assert.Empty(transport.To<CutscenePlay>(old));
        Assert.Contains("story.vega.finale_resolved", transport.LineKeys(old));
        Assert.Contains("story.vega.beat13", transport.LineKeys(old));
        Assert.True(HasFinaleAchievement(old));

        // Confirmed, the ending counts as watched (the Journal note) — and "watch the ending again" replays it on the director.
        Deliver(server, crew, new CutsceneSeenIntent { PackId = V, Ids = new[] { "fin.standdown", StoryCutsceneIds.Resolution } });
        Assert.True(Has(crew, StoryPackKeys.CutsceneSeen(V, StoryCutsceneIds.Resolution)));
        transport.Sent.Clear();
        Deliver(server, crew, new RequestStoryResolutionIntent());
        Deliver(server, old, new RequestStoryResolutionIntent());
        var replay = Assert.Single(transport.To<CutscenePlay>(crew));
        Assert.Equal((StoryCutsceneIds.Resolution, true), (replay.CutsceneId, replay.Replay));
        Assert.Empty(transport.To<StoryResolved>(crew));
        Assert.Single(transport.To<StoryResolved>(old));
    }

    [Fact]
    public void APlayerWhoMissedTheWin_GetsTheStanddownAndTheEnding_AtTheJoin_WithTheAchievement_AndOnlyOnce()
    {
        // "Away" heard the arc out (B12) before the win but was offline for it (E23).
        var repo = Repo("fin_away");
        repo.Initialize();
        var state = new PlayerState { PlayerId = "Away", Name = "Away" };
        state.Milestones.Add(StoryPackKeys.Started(V));
        state.Milestones.Add(StoryPackKeys.Migrated(V));
        state.Milestones.Add(StoryPackKeys.CutsceneSeen(V, StoryCutsceneIds.Prologue));
        for (int i = 0; i <= 12; i++)
        {
            state.Milestones.Add(StoryPackKeys.Beat(V, i));
        }

        repo.SavePlayer(state);
        var (server, transport) = World("fin_away", repo: repo);
        server.AdminRevealFinale();
        server.MarkGuardianDefeatedForTest();

        var p = Join(server, 1, "Away");
        Drain(server, p);
        var plays = transport.Plays(p);
        Assert.Contains("fin.standdown", plays);
        Assert.Contains(StoryCutsceneIds.Resolution, plays);
        Assert.Empty(transport.To<StoryResolved>(p));
        Assert.True(HasFinaleAchievement(p));
        var (scene, line) = transport.Order(p, "fin.standdown", "story.vega.finale_resolved");
        Assert.True(scene >= 0 && line > scene, $"stand-down at {scene}, its line at {line}");

        // Watched the stand-down, gone before the ending: the next join owes the ending alone — no second line, no StoryResolved.
        Deliver(server, p, new CutsceneSeenIntent { PackId = V, Ids = new[] { "fin.standdown" } });
        server.DisconnectLocalPlayerForTest("Away");
        transport.Sent.Clear();
        var back = Join(server, 2, "Away");
        Drain(server, back);
        Assert.Equal(new[] { StoryCutsceneIds.Resolution }, transport.Plays(back).Where(id => id is "fin.standdown" or StoryCutsceneIds.Resolution));
        Assert.DoesNotContain("story.vega.finale_resolved", transport.LineKeys(back));
        Assert.Empty(transport.To<StoryResolved>(back));

        // Watched: never again unasked.
        Deliver(server, back, new CutsceneSeenIntent { PackId = V, Ids = new[] { StoryCutsceneIds.Resolution } });
        server.DisconnectLocalPlayerForTest("Away");
        transport.Sent.Clear();
        var third = Join(server, 3, "Away");
        Drain(server, third);
        Assert.DoesNotContain(StoryCutsceneIds.Resolution, transport.Plays(third));
        Assert.Empty(transport.To<StoryResolved>(third));
    }

    [Fact]
    public void TheRecap_NeverFoldsTheEndingAway()
    {
        // A player with three chapter scenes AND the stand-down and the ending owed from a session that ended too soon.
        var repo = Repo("fin_recap");
        repo.Initialize();
        var state = new PlayerState { PlayerId = "Owed", Name = "Owed" };
        state.Milestones.Add(StoryPackKeys.Started(V));
        state.Milestones.Add(StoryPackKeys.Migrated(V));
        state.Milestones.Add(StoryPackKeys.CutsceneSeen(V, StoryCutsceneIds.Prologue));
        for (int i = 0; i <= 12; i++)
        {
            state.Milestones.Add(StoryPackKeys.Beat(V, i));
        }

        foreach (string id in new[] { "ch5.open", "ch6.open", "ch7.open", "fin.standdown", StoryCutsceneIds.Resolution })
        {
            state.Milestones.Add(StoryPackKeys.CutsceneSent(V, id));
        }

        repo.SavePlayer(state);
        var (server, transport) = World("fin_recap", repo: repo);
        server.AdminRevealFinale();
        server.MarkGuardianDefeatedForTest();

        var p = Join(server, 1, "Owed");
        Drain(server, p);
        var plays = transport.Plays(p);
        Assert.Contains(StoryCutsceneIds.Recap, plays);         // the three chapter scenes became the recap …
        Assert.DoesNotContain("ch5.open", plays);
        Assert.Contains("fin.standdown", plays);                // … the ending stays itself
        Assert.Contains(StoryCutsceneIds.Resolution, plays);
        Assert.DoesNotContain("story.vega.finale_resolved", transport.LineKeys(p)); // handled: no second delivery
        Assert.Empty(transport.To<StoryResolved>(p));
    }

    [Fact]
    public void AnObserverWhoHadHeardTheArc_GetsTheEndingAndTheAchievement_WhenObservingEnds()
    {
        var (server, transport) = World("fin_observer", c => c.FleetAdminPlayers = new List<string> { "Staff" });
        var staff = Started(server, 1, "Staff");
        Assert.True(staff.IsFleetAdmin);
        server.AdminRevealFinale(); // B12 heard
        Assert.True(Has(staff, StoryPackKeys.Beat(V, 12)));
        server.HandleForTest(staff, new AdminCommandIntent { Command = "spectate", StringArg = "on" });
        Assert.True(staff.Spectating);

        server.WinDuelForTest();
        Assert.False(HasFinaleAchievement(staff)); // an observer is not aboard (N1)
        Assert.False(Has(staff, StoryPackKeys.CutsceneSent(V, StoryCutsceneIds.Resolution)));

        server.HandleForTest(staff, new AdminCommandIntent { Command = "spectate", StringArg = "off" });
        Assert.False(staff.Spectating);
        Assert.True(HasFinaleAchievement(staff)); // E23: they had heard the arc out before the win
        Assert.True(Has(staff, StoryPackKeys.CutsceneSent(V, "fin.standdown")));
        Assert.True(Has(staff, StoryPackKeys.CutsceneSent(V, StoryCutsceneIds.Resolution)));
        Drain(server, staff);
        Assert.Contains(StoryCutsceneIds.Resolution, transport.Plays(staff));
    }

    // ---------------- B51: every world's machines stand down ----------------

    [Fact]
    public void TheWin_DespawnsTheMachines_OfEveryLoadedWorld()
    {
        var (server, _) = World("fin_b51");
        server.AddLocalPlayer("Pilot");
        server.AdminRevealFinale();
        string home = server.ActiveLocationId;
        server.SpawnPlanetEnemyAtForTest(new Vector3f(10f, 80f, 10f));
        server.LoadGuardianCoreForTest(); // a second resident world, now the active cursor
        server.SpawnPlanetEnemyAtForTest(new Vector3f(10f, 80f, 10f));
        Assert.Equal(1, server.PlanetEnemyCountOnForTest(home));
        Assert.Equal(1, server.PlanetEnemyCountOnForTest(SvGameServer.GuardianCoreBodyId));

        server.MarkGuardianDefeatedForTest();

        Assert.Equal(0, server.PlanetEnemyCountOnForTest(home)); // it used to clear only the active world
        Assert.Equal(0, server.PlanetEnemyCountOnForTest(SvGameServer.GuardianCoreBodyId));
    }
}
