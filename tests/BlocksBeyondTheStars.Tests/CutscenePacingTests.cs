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
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3686 — story scenes played late and at the wrong place (the Sounders' landing on top of the choir tower). The server
/// drops a queued scene the player's story has moved past — a step's scene once the pack's next step is through, a scene
/// with <c>staleAfterStep</c> once that step is, a VEGA chapter opening once a later chapter has begun — stamps it seen and
/// lets the lines it held go out; the next scene of the same story follows after 15 s, another story's after 90 s; and the
/// place-bound Sounders and tutorial scenes play where they happen (<c>playInPlace</c>).
/// </summary>
public sealed class CutscenePacingTests : IDisposable
{
    private const string C = StoryPackFixtures.Cutscenes;
    private const string V = StoryRegistry.DefaultStoryId;
    private const string Other = "pacing_other";

    private readonly string _root;
    private readonly List<SqliteWorldRepository> _repos = new();

    public CutscenePacingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_pacing_" + Guid.NewGuid().ToString("N"));
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

    private (SvGameServer Server, RecordingTransport Transport) World(string world, GameContent content, bool vega = false)
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
        if (!vega)
        {
            config.Rules.StoryId = StoryRegistry.NoneStoryId; // VEGA stays quiet; only the test pack talks
        }

        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var server = new SvGameServer(config, content, transport, repo);
        server.Start();
        return (server, transport);
    }

    /// <summary>The <c>cutscene_test</c> fixture pack, optionally edited, plus a chapterless second player pack with one card
    /// (<see cref="Other"/>/<c>other.card</c>) — a scene of another story.</summary>
    private static GameContent PackContent(Action<StoryDefinition>? edit = null)
    {
        var content = StoryPackFixtures.ContentWith(C);
        edit?.Invoke(content.Stories[C]);
        var other = new StoryDefinition
        {
            Id = Other,
            Kind = StoryRegistry.KindSide,
            Scope = StoryRegistry.ScopePlayer,
            Cutscenes =
            {
                new CutsceneDefinition
                {
                    Id = "other.card",
                    Tier = CutsceneTiers.Card,
                    Shots = { new CutsceneShot { Duration = 4 } },
                },
            },
        };
        content.SetStories(content.Stories.Values.Concat(new[] { other }).ToList());
        return content;
    }

    private static PlayerSession Join(SvGameServer server, int conn, string name = "Pilot")
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 },
        }));
        return server.Sessions[conn];
    }

    /// <summary>The client is in the world: the fixture's welcome and the chained part card went out just now.</summary>
    private static void Ready(SvGameServer server, PlayerSession p)
    {
        server.AdvanceUptimeForTest(SvGameServer.CutsceneJoinSettleSeconds);
        server.TickCutscenesForTest(p);
    }

    private static void Wait(SvGameServer server, PlayerSession p, double seconds)
    {
        server.AdvanceUptimeForTest(seconds);
        server.TickCutscenesForTest(p);
    }

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private static bool Queued(PlayerSession p, string id) => p.Story.CutsceneQueue.Any(q => q.Id == id);

    // ---------------- A step's scene, overtaken by the next step ----------------

    [Fact]
    public void AStepScene_WaitingInTheQueue_IsDroppedOnceTheNextStepIsDone_AndItsLineGoesOut()
    {
        var (server, transport) = World("step_overtaken", PackContent());
        var p = Join(server, 1);
        Ready(server, p); // welcome + strip@one went out: the cooldown runs
        transport.Sent.Clear();

        server.RaiseGameEventForTest(p, GameEventKeys.MineAny, "stone"); // one.1 done → first_mine (its onComplete) waits
        Assert.True(Queued(p, "first_mine"));
        Assert.DoesNotContain("story.cutscene_test.one1.done", transport.LineKeys(p)); // the card shows it: held with it

        Wait(server, p, 5);
        Assert.True(Queued(p, "first_mine")); // not before the next step is through
        Assert.False(Has(p, StoryPackKeys.CutsceneSeen(C, "first_mine")));

        server.RaiseGameEventForTest(p, GameEventKeys.CraftAny, "plank"); // two.1 done: the player is past the first mine
        server.TickCutscenesForTest(p);
        Assert.False(Queued(p, "first_mine"));
        Assert.True(Has(p, StoryPackKeys.CutsceneSeen(C, "first_mine"))); // stamped seen: never re-offered at a join
        Assert.Contains("story.cutscene_test.one1.done", transport.LineKeys(p)); // the line it held reaches the panel

        Wait(server, p, SvGameServer.CutsceneCooldownSeconds);
        Wait(server, p, SvGameServer.CutsceneCooldownSeconds);
        Assert.DoesNotContain("first_mine", transport.Plays(p));
        Assert.Contains("big_moment", transport.Plays(p)); // two.1's own onStart scene: no step after it — it plays
    }

    [Fact]
    public void AStepScene_CommittedWhenItsNextStepWasThroughAlready_StillPlays()
    {
        var (server, transport) = World("step_late", PackContent());
        var p = Join(server, 1);
        Ready(server, p);
        p.State.Milestones.Add(StoryPackKeys.StepDone(C, "two.1")); // an open extra finished after the step behind it

        Assert.True(server.PlayCutsceneForTest(p, C, "first_mine"));
        Assert.True(Queued(p, "first_mine"));
        Wait(server, p, SvGameServer.CutsceneSamePackCooldownSeconds);
        Assert.Contains("first_mine", transport.Plays(p)); // its moment is the player's now — it plays
    }

    [Fact]
    public void AnOwedStepScene_ReOfferedAtTheJoin_IsDroppedWhenTheStoryMovedOn()
    {
        var (server, transport) = World("step_join", PackContent());
        var p = Join(server, 1);
        Ready(server, p);
        p.State.Milestones.Add(StoryPackKeys.CutsceneSent(C, "first_mine")); // the client got it but never played it
        p.State.Milestones.Add(StoryPackKeys.StepDone(C, "one.1"));
        p.State.Milestones.Add(StoryPackKeys.StepDone(C, "two.1"));
        transport.Sent.Clear();

        server.CutscenesOnJoinForTest(p);
        Wait(server, p, SvGameServer.CutsceneCooldownSeconds);
        Assert.DoesNotContain("first_mine", transport.Plays(p));
        Assert.True(Has(p, StoryPackKeys.CutsceneSeen(C, "first_mine")));
    }

    // ---------------- staleAfterStep: a scene the server's code plays ----------------

    [Fact]
    public void StaleAfterStep_DropsAServerPlayedScene_OnceThatStepIsThrough()
    {
        var content = PackContent(pack => pack.FindCutscene("anchored")!.Options.StaleAfterStep = "two.1");
        var (server, transport) = World("stale_after", content);
        var p = Join(server, 1);
        Ready(server, p);

        Assert.True(server.PlayCutsceneForTest(p, C, "anchored", anchor: new Vector3f(10f, 64f, 10f)));
        Assert.True(Queued(p, "anchored")); // the same story's cooldown runs
        Assert.True(p.State.CutsceneAnchors.Count > 0);

        server.RaiseGameEventForTest(p, GameEventKeys.MineAny, "stone"); // one.1 — not the named step
        Assert.True(Queued(p, "anchored"));

        server.RaiseGameEventForTest(p, GameEventKeys.CraftAny, "plank"); // two.1
        server.TickCutscenesForTest(p);
        Assert.False(Queued(p, "anchored"));
        Assert.True(Has(p, StoryPackKeys.CutsceneSeen(C, "anchored")));
        Assert.Empty(p.State.CutsceneAnchors); // the stored place goes with it
        Wait(server, p, SvGameServer.CutsceneCooldownSeconds);
        Assert.DoesNotContain("anchored", transport.Plays(p));
    }

    [Fact]
    public void StaleAfterStep_MustNameAStepOfThePack()
    {
        var def = StoryPackFixtures.Load(C);
        var installed = new Dictionary<string, StoryDefinition>(StoryPackFixtures.ContentWith(C).Stories);
        def.FindCutscene("anchored")!.Options.StaleAfterStep = "nope.1";
        var issues = StoryPackValidator.Validate(def, StoryPackValidationTests.Context(installed, StoryPackFixtures.Dir(C)));
        Assert.Contains(issues, i => i.Severity == StoryPackIssueSeverity.Error && i.Path.EndsWith(".options.staleAfterStep", StringComparison.Ordinal));

        def.FindCutscene("anchored")!.Options.StaleAfterStep = "two.1";
        issues = StoryPackValidator.Validate(def, StoryPackValidationTests.Context(installed, StoryPackFixtures.Dir(C)));
        Assert.DoesNotContain(issues, i => i.Path.EndsWith(".options.staleAfterStep", StringComparison.Ordinal));
    }

    // ---------------- The cooldown within one story ----------------

    [Fact]
    public void TheSameStorysNextScene_FollowsAfter15Seconds_AnotherStorysAfter90()
    {
        var (server, transport) = World("same_pack", PackContent());
        var p = Join(server, 1);
        Ready(server, p); // the fixture pack's welcome + part card: the clock starts

        Assert.True(server.PlayCutsceneForTest(p, C, "anchored"));
        Wait(server, p, SvGameServer.CutsceneSamePackCooldownSeconds - 1);
        Assert.DoesNotContain("anchored", transport.Plays(p));
        Wait(server, p, 1);
        Assert.Contains("anchored", transport.Plays(p)); // the same story: 15 s

        Assert.True(server.PlayCutsceneForTest(p, Other, "other.card"));
        Wait(server, p, SvGameServer.CutsceneSamePackCooldownSeconds);
        Wait(server, p, SvGameServer.CutsceneCooldownSeconds - SvGameServer.CutsceneSamePackCooldownSeconds - 1);
        Assert.DoesNotContain("other.card", transport.Plays(p)); // another story: still the full 90 s
        Wait(server, p, 1);
        Assert.Contains("other.card", transport.Plays(p, Other));
    }

    // ---------------- VEGA: a chapter opening a later chapter overtook ----------------

    [Fact]
    public void AVegaChapterOpening_IsDroppedOnceALaterChapterBegan_TheCurrentOneAndTheBeatCardsPlay()
    {
        var (server, transport) = World("vega_overtaken", ContentLoader.LoadFromDirectory(TestPaths.DataDir()), vega: true);
        var p = Join(server, 1, "Rookie");
        Ready(server, p);
        server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(new CutsceneSeenIntent { PackId = V, Ids = new[] { StoryCutsceneIds.Prologue } }));
        server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
        server.SkipTutorialForTest(p); // the story track starts in chapter 1: its card goes out
        server.TickCutscenesForTest(p);
        Assert.Contains("ch1.open", transport.Plays(p, V));

        Assert.True(server.PlayCutsceneForTest(p, V, "ch2.open")); // chapter 2's opening waits behind the cooldown
        Wait(server, p, 5);
        Assert.True(Queued(p, "ch2.open")); // chapter 2 lies ahead: nothing overtook it

        server.AdminAdvanceStory(25); // beats 1–5: the world is in chapter 3 ("The Guardian") now
        Assert.Equal("guardian", server.TrackChapterIdForTest(p));
        server.TickCutscenesForTest(p);
        Assert.False(Queued(p, "ch2.open"));
        Assert.True(Has(p, StoryPackKeys.CutsceneSeen(V, "ch2.open")));
        Assert.Contains("story.vega.beat02", transport.LineKeys(p)); // its beat still reaches the panel

        for (int i = 0; i < 6; i++)
        {
            Wait(server, p, SvGameServer.CutsceneCooldownSeconds);
        }

        var plays = transport.Plays(p, V);
        Assert.DoesNotContain("ch2.open", plays);
        Assert.Contains("ch3.open", plays); // the player's current chapter: never dropped
        Assert.Contains("b03", plays);      // beat cards are no chapter openings: they play
        Assert.Contains("b04", plays);
    }

    // ---------------- The data ----------------

    [Fact]
    public void ThePlaceBoundStoryScenes_PlayInPlace_AndTheServerPlayedOnesNameTheirOvertakingStep()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var sounders = content.Stories["sounders"];
        foreach (string id in new[]
                 {
                     "snd.stones_sing", "snd.shrine_answers", "snd.first_echo", "snd.great_song", "snd.song_of_return",
                     "snd.landing", "snd.elder", "snd.choir_tower", "snd.bridge_song", "snd.soundi_joins",
                     "snd.threshold", "snd.other_song", // #3910: over there the player is never "at home"
                 })
        {
            Assert.True(sounders.FindCutscene(id)!.Options.PlayInPlace, id);
        }

        // Every Sounders scene that takes the camera either plays in place or is "now or never" (the arrival).
        Assert.All(sounders.Cutscenes.Where(c => c.Tier >= CutsceneTiers.Scene && c.TakesCamera),
            c => Assert.True(c.Options.PlayInPlace || c.Options.CooldownExempt, c.Id));

        Assert.Equal("s2.1", sounders.FindCutscene("snd.stones_sing")!.Options.StaleAfterStep);
        Assert.Equal("s5.2", sounders.FindCutscene("snd.first_echo")!.Options.StaleAfterStep);
        Assert.Equal("s8.2", sounders.FindCutscene("snd.choir_tower")!.Options.StaleAfterStep);
        var stepIds = sounders.Chapters.SelectMany(ch => ch.Steps).Select(s => s.Id).ToHashSet();
        Assert.All(sounders.Cutscenes.Where(c => c.Options.StaleAfterStep.Length > 0), c => Assert.Contains(c.Options.StaleAfterStep, stepIds));

        var tutorial = content.Stories["tutorial"];
        Assert.True(tutorial.FindCutscene("tut.first_base")!.Options.PlayInPlace);
        Assert.True(tutorial.FindCutscene("tut.first_landing")!.Options.PlayInPlace);
        Assert.False(tutorial.FindCutscene("tut.ship_commissioned")!.Options.PlayInPlace); // commissioned at the new ship
    }
}
