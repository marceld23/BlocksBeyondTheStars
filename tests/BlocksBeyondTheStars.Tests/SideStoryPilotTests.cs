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
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The repository's <c>data/</c> folder copied once per test class, with the pilot side story dropped in as
/// <c>stories/side_pilot/</c> — the way an author installs a pack (#2483).</summary>
public sealed class SidePilotDataFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_pilot_data_" + Guid.NewGuid().ToString("N"));

    public SidePilotDataFixture()
    {
        DataDir = StoryPackFixtures.CopyDataWith(_root, StoryPackFixtures.SidePilot);
    }

    /// <summary>The copied data folder (the shipped packs plus <c>stories/side_pilot/</c>).</summary>
    public string DataDir { get; }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }
}

/// <summary>
/// #2483 — step 22 of the story packs (#2459): "a new story is data only". The pilot side story
/// (<c>tests/…/Fixtures/story_packs/side_pilot</c>, a test pack the game never ships) is dropped into a data folder and
/// loaded by the real content loader, passes the validator without a warning, starts by its own start rule (or early, from
/// the Journal), plays through with real gameplay handlers (the lamp, mining, the quick bar, walking), ledgers its chapter
/// cards and its banner, shows in the Journal and completes — and it runs at the same time as the basic training and VEGA's
/// story for the same player, with one chip in the binding order and one Journal listing all three. Not one line of C#
/// was written for the pack.
/// </summary>
public sealed class SideStoryPilotTests : IDisposable, IClassFixture<SidePilotDataFixture>
{
    private const string P = StoryPackFixtures.SidePilot;
    private const string Tutorial = SvGameServer.OfficialTutorialPackId;
    private const string Vega = StoryRegistry.DefaultStoryId;
    private const string Juno = "juno";

    private readonly SidePilotDataFixture _data;
    private readonly string _root;
    private readonly List<SqliteWorldRepository> _repos = new();

    public SideStoryPilotTests(SidePilotDataFixture data)
    {
        _data = data;
        _root = Path.Combine(Path.GetTempPath(), "bbts_pilot_" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer Boot(string world, RecordingTransport transport, GameContent content, Action<ServerConfig>? configure = null)
    {
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
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var server = new SvGameServer(config, content, transport, repo);
        server.Start();
        return server;
    }

    private static PlayerSession Join(SvGameServer server, int conn, string name, bool cutscenes)
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = cutscenes ? new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 } : new[] { ClientFeatures.Packs1 },
        }));
        return server.Sessions[conn];
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    /// <summary>The argument of the chapter card <c>pilot.part</c> bound to <paramref name="chapter"/>: the chapter's title key,
    /// its place among the pack's chapters and their count (<see cref="CutsceneArgs.For"/>).</summary>
    private static string ChapterArg(string chapter, int place)
        => string.Join(LineText.ArgSeparator.ToString(), "story.side_pilot.ch." + chapter, place.ToString(System.Globalization.CultureInfo.InvariantCulture), "2");

    /// <summary>Three blocks of mud beside the player, mined by the real mining path (the starter drill in slot 1).</summary>
    private static void MineThreeBlocks(SvGameServer server, GameContent content, PlayerSession p)
    {
        var at = p.State.Position;
        int x = (int)Math.Floor(at.X), y = (int)Math.Floor(at.Y), z = (int)Math.Floor(at.Z);
        var mud = content.GetBlock("mud")!.NumericId;
        foreach (var cell in new[] { new Vector3i(x + 1, y, z), new Vector3i(x, y, z + 1), new Vector3i(x - 1, y, z) })
        {
            server.World.SetBlock(cell, mud);
            for (int hit = 0; hit < 8 && !server.World.GetBlock(cell).IsAir; hit++)
            {
                server.MineBlockOnce(p.State.PlayerId, cell.X, cell.Y, cell.Z);
            }

            Assert.True(server.World.GetBlock(cell).IsAir, $"the mud at {cell.X},{cell.Y},{cell.Z} did not break");
        }
    }

    /// <summary>Walks one metre at a time along +X through the real move handler (it measures while a step listens).</summary>
    private static void Walk(SvGameServer server, PlayerSession p, int metres)
    {
        var start = p.State.Position;
        for (int i = 1; i <= metres; i++)
        {
            Deliver(server, p, new MoveIntent { X = start.X + i, Y = start.Y, Z = start.Z });
        }
    }

    // ---------------- Load and validation: a dropped-in folder ----------------

    [Fact]
    public void TheDroppedInFolder_Loads_Localizes_AndValidates_WithoutAWarning()
    {
        var warnings = new List<string>();
        var content = ContentLoader.LoadFromDirectory(_data.DataDir, warn: warnings.Add);
        Assert.DoesNotContain(warnings, w => w.Contains(P, StringComparison.Ordinal) || w.Contains("Locale file", StringComparison.Ordinal)
                                             || w.Contains("Story pack", StringComparison.Ordinal));

        // The real loader found it next to the shipped packs, with its scenes and its texts.
        Assert.True(content.TryGetStory(P, out var def));
        Assert.Equal(StoryRegistry.KindSide, def.Kind);
        Assert.Equal(StoryRegistry.ScopePlayer, StoryRegistry.EffectiveScope(def));
        Assert.False(StoryRegistry.CanBeWorldStory(def)); // a player pack never becomes the save's story
        Assert.True(content.TryGetWorldStory(Vega, out _));
        Assert.Equal(Juno, def.Narrator.Id);
        Assert.Equal(new[] { "p1", "p2" }, def.Chapters.Select(c => c.Id));
        Assert.Equal(new[] { "pilot.part", "pilot.lit" }, def.Cutscenes.Select(c => c.Id));
        Assert.True(content.TryGetStory(Tutorial, out _)); // the shipped packs are still all there
        Assert.Equal("The Beacon Keeper", content.CreateLocalizer(GameLocale.English).Get("story.side_pilot.name"));
        Assert.Equal("Die Leuchtfeuer-Wärterin", content.CreateLocalizer(GameLocale.German).Get("story.side_pilot.name"));
        Assert.Equal("The Beacon Keeper", content.CreateLocalizer(GameLocale.French).Get("story.side_pilot.name")); // English fills in

        // The C# validator (the twin of tools/validate_story_pack.py): no error, no warning.
        string raw = File.ReadAllText(Path.Combine(_data.DataDir, "stories", P, "story.json"));
        var issues = StoryPackValidator.Validate(def, StoryPackValidationTests.Context(content.Stories, StoryPackFixtures.Dir(P)), raw);
        Assert.True(issues.Count == 0, string.Join("\n", issues));
    }

    // ---------------- Alone: start, play, cards, Journal, done ----------------

    [Fact]
    public void ThePilot_StartsByItself_AndPlaysThroughWithRealHandlers_CardsLedgered_JournalDone()
    {
        var content = StoryPackFixtures.ContentWith(P); // no tutorial pack installed
        var transport = new RecordingTransport();
        var server = Boot("alone", transport, content, c => c.Rules.StoryId = StoryRegistry.NoneStoryId);
        var p = Join(server, 1, "Pilot", cutscenes: true);
        string id = p.State.PlayerId;

        // No basic training runs for this player ("not:pack:tutorial:enabled"), so the start rule holds at the join.
        Assert.True(Has(p, StoryPackKeys.Started(P)));
        var first = Assert.Single(transport.PackLines(p, P));
        Assert.Equal("story.side_pilot.p1.lamp.start", first.LineKey);
        Assert.Equal(Juno, first.Speaker); // the pack's own narrator, no code for it
        Assert.Equal("story.side_pilot.p1.lamp.chip", server.ObjectiveKeyForTest(id));
        Assert.Equal(P, server.ObjectivePackForTest(id));
        Assert.Equal(ObjectiveSources.Story, server.ObjectiveSourceForTest(id));

        // Chapter 1's card: committed at the join, sent once the client is in the world, confirmed by the receipt.
        Assert.True(Has(p, StoryPackKeys.CutsceneSent(P, "pilot.part@p1")));
        Assert.Empty(transport.Plays(p));
        server.AdvanceUptimeForTest(SvGameServer.CutsceneJoinSettleSeconds);
        server.TickCutscenesForTest(p);
        var card = Assert.Single(transport.To<CutscenePlay>(p));
        Assert.Equal(P, card.PackId);
        Assert.Equal("pilot.part@p1", card.CutsceneId);
        Assert.Equal(ChapterArg("p1", 1), card.Arg); // "Part 1 of 2 · A light in the dark"
        Deliver(server, p, new CutsceneSeenIntent { PackId = P, Ids = new[] { "pilot.part@p1" } });
        Assert.True(Has(p, StoryPackKeys.CutsceneSeen(P, "pilot.part@p1")));

        // p1.1 — the suit lamp, through its real handler.
        Deliver(server, p, new SetLampIntent { On = true });
        Assert.True(Has(p, StoryPackKeys.StepDone(P, "p1.1")));
        var keys = transport.PackLines(p, P).Select(l => l.LineKey).ToList();
        Assert.Contains("story.side_pilot.p1.lamp.done", keys);
        Assert.Contains("story.side_pilot.p1.storm.start", keys); // the read step
        Assert.Contains("story.side_pilot.p1.dig.start", keys);
        Assert.Equal("story.side_pilot.p1.dig.chip", server.ObjectiveKeyForTest(id));

        // p1.3 — three blocks mined; the read step completes with it, chapter 1 is through, chapter 2 begins.
        MineThreeBlocks(server, content, p);
        Assert.True(Has(p, StoryPackKeys.StepDone(P, "p1.3")));
        Assert.True(Has(p, StoryPackKeys.StepDone(P, "p1.2")));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(P, "p1")));
        keys = transport.PackLines(p, P).Select(l => l.LineKey).ToList();
        Assert.Contains("story.side_pilot.p1.dig.done", keys);
        Assert.Contains("story.side_pilot.p2.slot.start", keys);

        // Chapter 2's card (the same template, its own ledger entry) waits for the card cooldown, then goes out.
        Assert.True(Has(p, StoryPackKeys.CutsceneSent(P, "pilot.part@p2")));
        server.TickCutscenesForTest(p);
        Assert.Equal(new[] { "pilot.part@p1" }, transport.Plays(p));
        server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
        server.TickCutscenesForTest(p);
        Assert.Equal(new[] { "pilot.part@p1", "pilot.part@p2" }, transport.Plays(p));
        Assert.Equal(ChapterArg("p2", 2), transport.To<CutscenePlay>(p)[1].Arg);

        // p2.1 — another quick-bar slot; p2.2 — eight metres on foot. The banner plays, then the done line; the pack is done.
        Deliver(server, p, new SelectHotbarIntent { Slot = 1 });
        Assert.True(Has(p, StoryPackKeys.StepDone(P, "p2.1")));
        Walk(server, p, 12);
        Assert.True(Has(p, StoryPackKeys.StepDone(P, "p2.2")));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(P, "p2")));
        Assert.True(Has(p, StoryPackKeys.Done(P)));
        Assert.True(Has(p, StoryPackKeys.CutsceneSent(P, "pilot.lit")));
        Assert.Equal("pilot.lit", transport.Plays(p).Last()); // a banner never waits for the cooldown
        Assert.Equal("story.side_pilot.p2.walk.done", transport.PackLines(p, P).Last().LineKey);
        Assert.All(transport.PackLines(p, P), l => Assert.Equal(Juno, l.Speaker));
        Assert.Equal(string.Empty, server.ObjectiveKeyForTest(id)); // nothing left on the chip

        // The Journal: one story (no world story here, no tutorial installed), both parts done, the badge.
        var states = server.StoryPackStatesForTest(p);
        var entry = Assert.Single(states.Packs);
        Assert.Equal(P, entry.Id);
        Assert.Equal(StoryRegistry.KindSide, entry.Kind);
        Assert.Equal(StoryRegistry.ScopePlayer, entry.Scope);
        Assert.Equal("story.side_pilot.short", entry.ShortNameKey);
        Assert.True(entry.Started && entry.Done && entry.Badge && !entry.OptedOut);
        Assert.Equal(new[] { "p1", "p2" }, entry.Chapters.Select(c => c.Id));
        Assert.All(entry.Chapters, c => Assert.Equal(StoryChapterStates.Done, c.State));
        Assert.Empty(server.ObjectiveListForTest(p).Items);
    }

    // ---------------- Together: the basic training, VEGA's story and the side story for one player ----------------

    [Fact]
    public void ThePilot_RunsNextToTheBasicTrainingAndVega_OneChipInBindingOrder_OneJournal()
    {
        var content = ContentLoader.LoadFromDirectory(_data.DataDir); // the shipped packs + the dropped-in folder
        var transport = new RecordingTransport();
        var server = Boot("together", transport, content); // the default world: VEGA's story, the tutorial on Auto
        var p = Join(server, 1, "Kid", cutscenes: false);
        string id = p.State.PlayerId;

        // Three stories for one player: the basic training runs and owns the chip (rank 3), VEGA's story is held for the
        // training, the side story waits for part 1 of the training (its start rule) — and may be started early.
        Assert.True(Has(p, StoryPackKeys.Started(Tutorial)));
        Assert.False(server.StoryTrackStarted(p));
        Assert.False(Has(p, StoryPackKeys.Started(P)));
        Assert.Equal(Tutorial, server.ObjectivePackForTest(id));
        Assert.Equal(ObjectiveSources.Tutorial, server.ObjectiveSourceForTest(id));

        var states = server.StoryPackStatesForTest(p);
        Assert.Equal(new[] { Tutorial, Vega, P }, states.Packs.Select(x => x.Id)); // Journal order: tutorials, the world story, the rest
        Assert.Equal(new[] { StoryRegistry.KindTutorial, StoryRegistry.KindStory, StoryRegistry.KindSide }, states.Packs.Select(x => x.Kind));
        Assert.Equal(new[] { StoryRegistry.ScopePlayer, StoryRegistry.ScopeWorld, StoryRegistry.ScopePlayer }, states.Packs.Select(x => x.Scope));
        Assert.True(states.Packs[2].Enabled && states.Packs[2].Startable && !states.Packs[2].Started);

        // The Journal's "Start" (earlyOptIn): the side story starts now, its narrator speaks — the chip stays the training's,
        // the objective list carries both (VEGA has no row before its track starts).
        Deliver(server, p, new StoryPackIntent { PackId = P, Action = StoryPackActions.StartPack });
        Assert.True(Has(p, StoryPackKeys.Started(P)));
        var start = Assert.Single(transport.PackLines(p, P));
        Assert.Equal(("story.side_pilot.p1.lamp.start", Juno), (start.LineKey, start.Speaker));
        Assert.Equal(Tutorial, server.ObjectivePackForTest(id));
        Assert.Equal(new[] { Tutorial, P }, server.ObjectiveListForTest(p).Items.Select(i => i.PackId));

        // A real handler completes the side story's step in the background; the training's step is untouched.
        string? trainingStep = server.ChipStepForTest(p, Tutorial);
        Assert.NotNull(trainingStep);
        Deliver(server, p, new SetLampIntent { On = true });
        Assert.True(Has(p, StoryPackKeys.StepDone(P, "p1.1")));
        Assert.Equal(trainingStep, server.ChipStepForTest(p, Tutorial));
        Assert.Equal(Tutorial, server.ObjectivePackForTest(id));

        // Pinned (the Journal's "Track"), the side story takes the chip above the training (rank 2); unpinned it gives it back.
        Deliver(server, p, new StoryPackIntent { PackId = P, Action = StoryPackActions.Track });
        Assert.Equal(P, server.ObjectivePackForTest(id));
        Assert.Equal("story.side_pilot.p1.dig.chip", server.ObjectiveKeyForTest(id));
        Assert.True(server.ObjectiveListForTest(p).Items[0].Pinned);
        Deliver(server, p, new StoryPackIntent { PackId = P, Action = StoryPackActions.Untrack });
        Assert.Equal(Tutorial, server.ObjectivePackForTest(id));

        // "I know the game": the training is skipped, VEGA's story starts and owns the chip (rank 5) above the side story (7).
        Deliver(server, p, new StoryPackIntent { PackId = Tutorial, Action = StoryPackActions.SkipPack });
        Assert.True(server.StoryTrackStarted(p));
        Assert.Equal(Vega, server.ObjectivePackForTest(id));
        Assert.Equal(ObjectiveSources.Story, server.ObjectiveSourceForTest(id));
        Assert.Equal(new[] { Vega, P }, server.ObjectiveListForTest(p).Items.Select(i => i.PackId));

        // The side story plays to its end under VEGA's chip — real mining, the quick bar, walking.
        MineThreeBlocks(server, content, p);
        Assert.True(Has(p, StoryPackKeys.ChapterDone(P, "p1")));
        Deliver(server, p, new SelectHotbarIntent { Slot = 1 });
        Walk(server, p, 12);
        Assert.True(Has(p, StoryPackKeys.Done(P)));
        Assert.Equal(Vega, server.ObjectivePackForTest(id));
        Assert.Equal(new[] { Vega }, server.ObjectiveListForTest(p).Items.Select(i => i.PackId));

        // One Journal with three stories in their states; each story speaks with its own narrator.
        states = server.StoryPackStatesForTest(p);
        Assert.Equal(new[] { Tutorial, Vega, P }, states.Packs.Select(x => x.Id));
        Assert.False(states.Packs[0].MandatoryRunning);
        Assert.True(states.Packs[1].Started);
        Assert.True(states.Packs[2].Done && states.Packs[2].Badge);
        Assert.All(transport.PackLines(p, P), l => Assert.Equal(Juno, l.Speaker));
        Assert.All(transport.PackLines(p, Tutorial), l => Assert.Equal(StoryNarrator.DefaultId, l.Speaker));

        // A second player who never opens the Journal: the side story starts by itself once part 1 of the training is
        // through (skipped counts) — next to the training, which keeps the chip. Progress is per player.
        var q = Join(server, 2, "Pal", cutscenes: false);
        Assert.False(Has(q, StoryPackKeys.Started(P)));
        Deliver(server, q, new StoryPackIntent { PackId = Tutorial, Action = StoryPackActions.SkipChapter, ChapterId = "b1" });
        Assert.True(Has(q, StoryPackKeys.ChapterSkipped(Tutorial, "b1")));
        Assert.True(Has(q, StoryPackKeys.Started(P)));
        Assert.False(Has(q, StoryPackKeys.StepDone(P, "p1.1")));
        Assert.Equal(Tutorial, server.ObjectivePackForTest(q.State.PlayerId));
        Assert.Equal(new[] { Tutorial, P }, server.ObjectiveListForTest(q).Items.Select(i => i.PackId));
    }
}
