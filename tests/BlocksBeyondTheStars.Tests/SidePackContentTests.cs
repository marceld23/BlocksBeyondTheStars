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
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3347 / #3350 / #3357 — what a SIDE pack brings besides its chapters, driven with the real handlers on the test pack
/// <c>side_extras</c> (<c>Fixtures/story_packs/side_extras</c>): its lore texts (gated by the player's chapter, only for a player
/// whose pack is active), its character's dialog (a reply shown under a condition, a sung reply, a milestone consequence in
/// the pack's own namespace, <c>dialog:done</c> completing a step), its readable texts on the Journal page, the world switch
/// that keeps a pack from ever starting, hidden blueprints and a pack star system revealed per player — VEGA's Guardian
/// system where it always was.
/// </summary>
public sealed class SidePackContentTests : IDisposable
{
    private const string P = StoryPackFixtures.SideExtras;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_sidepack_" + Guid.NewGuid().ToString("N"));
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
        config.Rules.StoryId = StoryRegistry.NoneStoryId; // the pack alone: no VEGA lore or dialogs in the draw
        configure?.Invoke(config);
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var server = new SvGameServer(config, content, transport, repo);
        server.Start();
        return server;
    }

    private static PlayerSession Join(SvGameServer server, int conn, string name)
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = new[] { ClientFeatures.Packs1 },
        }));
        return server.Sessions[conn];
    }

    private static PlayerSession OptOut(PlayerSession p)
    {
        p.State.Milestones.Add(StoryPackKeys.OptOut(P));
        return p;
    }

    private static Vector3f Beside(PlayerSession p) => new(p.State.Position.X + 1f, p.State.Position.Y, p.State.Position.Z);

    // ---------------- Lore ----------------

    [Fact]
    public void ASidePacksLore_IsFoundOnlyWhileThePackIsActive_AndGatedByThePlayersChapter()
    {
        var server = Boot("lore", new RecordingTransport(), StoryPackFixtures.ContentWith(P));
        var a = Join(server, 1, "Ada");
        var b = OptOut(Join(server, 2, "Ben"));
        Assert.True(server.IsStoryPackActive(a, P));
        Assert.False(server.IsStoryPackActive(b, P));

        server.RevealLoreForTest(a.State.PlayerId, "monument");
        server.RevealLoreForTest(a.State.PlayerId, "monument"); // the chapter-2 text stays locked in chapter 1
        Assert.Equal(new[] { "sx_lore_first" }, server.FoundLoreForTest(a.State.PlayerId));

        server.RevealLoreForTest(b.State.PlayerId, "monument");
        Assert.Empty(server.FoundLoreForTest(b.State.PlayerId)); // switched off: the pack's texts are not theirs

        server.RaiseGameEventForTest(a, GameEventKeys.DialogDone, "sx_echo_talk"); // chapter 1 done
        Assert.Contains(StoryPackKeys.ChapterDone(P, "c1"), a.State.Milestones);
        server.RevealLoreForTest(a.State.PlayerId, "monument");
        Assert.Equal(new[] { "sx_lore_first", "sx_lore_later" }, server.FoundLoreForTest(a.State.PlayerId));
    }

    // ---------------- Dialogs ----------------

    [Fact]
    public void ASidePacksDialog_IsPickedForAnActivePlayer_WithConditionalAndSungReplies_AndDialogDoneCompletesAStep()
    {
        var transport = new RecordingTransport();
        var server = Boot("dialog", transport, StoryPackFixtures.ContentWith(P));
        var a = Join(server, 1, "Ada");
        var b = OptOut(Join(server, 2, "Ben"));
        int echo = server.AddCharacterNpcForTest(Beside(a), "sx_echo"); // a code-spawned NPC carrying the character id
        Assert.Contains(server.NpcRosterForTest(), n => n.Id == echo && n.CharacterId == "sx_echo" && n.Name == "Elori");

        // Ben's pack is off: the character has nothing to say to him.
        server.TalkToNpcForTest(b.State.PlayerId, echo);
        Assert.NotEqual("sx_echo_talk", server.ActiveDialogForTest(b.State.PlayerId)?.Key);

        // Ada: the secret reply is hidden until its milestone; the first reply is sung.
        server.TalkToNpcForTest(a.State.PlayerId, echo);
        Assert.Equal(("sx_echo_talk", 0), server.ActiveDialogForTest(a.State.PlayerId));
        var shown = server.ShownDialogChoicesForTest(a.State.PlayerId)!.Value;
        Assert.Equal(2, shown.Choices.Length);
        Assert.Equal(new[] { "ORNE", string.Empty }, shown.Melodies);
        var sent = transport.To<NpcDialogState>(a).Last();
        Assert.Equal(2, sent.Choices.Length);
        Assert.Equal("ORNE", sent.ChoiceMelodies[0]);

        a.State.Milestones.Add("sx:secret");
        server.LeaveDialogForTest(a.State.PlayerId, echo);
        server.TalkToNpcForTest(a.State.PlayerId, echo);
        Assert.Equal(3, server.ShownDialogChoicesForTest(a.State.PlayerId)!.Value.Choices.Length);

        // The sung reply sets the pack's milestone; the closing reply ends the dialog: dialog:done completes step c1.1.
        server.ChooseDialogForTest(a.State.PlayerId, 0, echo);
        Assert.Contains("sx:asked", a.State.Milestones);
        Assert.Equal(("sx_echo_talk", 1), server.ActiveDialogForTest(a.State.PlayerId));
        Assert.DoesNotContain(StoryPackKeys.StepDone(P, "c1.1"), a.State.Milestones);
        server.ChooseDialogForTest(a.State.PlayerId, 0, echo);
        Assert.Null(server.ActiveDialogForTest(a.State.PlayerId));
        Assert.Contains(StoryPackKeys.StepDone(P, "c1.1"), a.State.Milestones);
    }

    [Fact]
    public void AMilestoneConsequence_OutsideThePacksNamespace_IsIgnored()
    {
        var content = StoryPackFixtures.ContentWith(P);
        Assert.True(content.TryGetStory(P, out var def));
        def.Dialogs[0].Nodes[1].Choices[0].Consequence = "milestone:vega:stage:mine"; // a family the game writes
        var server = Boot("namespace", new RecordingTransport(), content);
        var a = Join(server, 1, "Ada");
        int echo = server.AddCharacterNpcForTest(Beside(a), "sx_echo");

        server.TalkToNpcForTest(a.State.PlayerId, echo);
        server.ChooseDialogForTest(a.State.PlayerId, 0, echo);
        server.ChooseDialogForTest(a.State.PlayerId, 0, echo);
        Assert.Contains("sx:asked", a.State.Milestones);
        Assert.DoesNotContain("vega:stage:mine", a.State.Milestones);
        Assert.True(StoryPackMilestones.MaySet(def, "sx:asked"));
        Assert.True(StoryPackMilestones.MaySet(def, "side_extras:x"));
        Assert.False(StoryPackMilestones.MaySet(def, "snd:echo:kamo"));
        Assert.False(StoryPackMilestones.MaySet(null, "sx:asked"));
    }

    // ---------------- Texts ----------------

    [Fact]
    public void APacksTexts_UnlockByTheirCondition_OnItsJournalPage_OnlyWhileThePackIsActive()
    {
        var server = Boot("texts", new RecordingTransport(), StoryPackFixtures.ContentWith(P));
        var a = Join(server, 1, "Ada");
        var b = OptOut(Join(server, 2, "Ben"));
        Assert.Empty(server.UnlockedPackTextsForTest(a, P));

        a.State.Milestones.Add("sx:asked");
        b.State.Milestones.Add("sx:asked");
        var texts = server.UnlockedPackTextsForTest(a, P);
        Assert.Equal(new[] { "t1" }, texts.Select(t => t.Id));
        Assert.Equal("story.side_extras.t1.text", texts[0].TextKey);
        Assert.Empty(server.UnlockedPackTextsForTest(b, P));

        var page = server.StoryPackStatesForTest(a).Packs.Single(p => p.Id == P);
        Assert.Equal(new[] { "t1" }, page.Texts.Select(t => t.Id));
        Assert.Equal("story.side_extras.texts", page.TextsTitleKey);

        server.TickPackExtrasForTest(a); // a new text marks the Journal for a push
        server.TickJournalForTest();
        a.State.Milestones.Remove("sx:asked");
        server.TickPackExtrasForTest(a);
        Assert.True(server.JournalDirtyForTest(a));
    }

    // ---------------- The world switch ----------------

    [Fact]
    public void ASidePackSwitchedOffForTheWorld_NeverStarts_AndIsNotInTheJournal()
    {
        var server = Boot("disabled", new RecordingTransport(), StoryPackFixtures.ContentWith(P),
            c => c.Rules.DisabledStoryPacks = new List<string> { P });
        var a = Join(server, 1, "Ada");
        server.TickPackExtrasForTest(a);
        Assert.DoesNotContain(StoryPackKeys.Started(P), a.State.Milestones);
        Assert.False(server.IsStoryPackActive(a, P));
        Assert.DoesNotContain(server.StoryPackStatesForTest(a).Packs, p => p.Id == P);
        Assert.False(server.EvaluateStoryConditionForTest("pack:side_extras:enabled", a));
    }

    // ---------------- Hidden blueprints ----------------

    [Fact]
    public void AHiddenBlueprint_IsRefusedAndUnlisted_UntilItsConditionHolds()
    {
        var content = StoryPackFixtures.ContentWith(P);
        var bp = content.Blueprints.Values.First(b => b.Prerequisites.Count == 0);
        bp.UnlockCost.Clear(); // this test's own copy: free, so only the hidden gate can refuse it
        bp.KnowledgeCost = 0;
        bp.HiddenUntil = "milestone:sx:found";
        var transport = new RecordingTransport();
        var server = Boot("hidden", transport, content, c => c.Rules.GameMode = GameMode.Creative); // no cockpit needed
        var a = Join(server, 1, "Ada");
        a.State.UnlockedBlueprints.Remove(bp.Key);

        server.TickPackExtrasForTest(a);
        Assert.DoesNotContain(bp.Key, transport.To<BlueprintVisibility>(a).Last().Shown);
        server.UnlockBlueprint(a.State.PlayerId, bp.Key);
        Assert.DoesNotContain(bp.Key, a.State.UnlockedBlueprints);
        Assert.Contains(transport.To<ActionRejected>(a), r => r.Action == "unlock" && r.Reason == "@srv.unlock.unknown");

        a.State.Milestones.Add("sx:found");
        server.TickPackExtrasForTest(a);
        Assert.Contains(bp.Key, transport.To<BlueprintVisibility>(a).Last().Shown);
        server.UnlockBlueprint(a.State.PlayerId, bp.Key);
        Assert.Contains(bp.Key, a.State.UnlockedBlueprints);
    }

    // ---------------- Pack star systems ----------------

    [Fact]
    public void APackSystem_JoinsTheGalaxyOnce_ShowsPerPlayer_GatesTheJump_AndComesBackAfterARestart()
    {
        var transport = new RecordingTransport();
        var server = Boot("systems", transport, StoryPackFixtures.ContentWith(P));
        var a = Join(server, 1, "Ada");
        var b = Join(server, 2, "Ben");
        var guardianBefore = SvGameServer.GuardianFinaleMapPosition(server.Galaxy);

        server.TickPackExtrasForTest(a);
        Assert.False(server.GalaxyHasSystemForTest("sx_home")); // nobody may see it yet

        a.State.Milestones.Add("sx:song");
        server.TickPackExtrasForTest(a);
        server.TickPackExtrasForTest(a);
        Assert.Equal(1, server.GalaxySystemCountForTest("sx_home"));
        Assert.Contains("sx_home", server.StarMapSystemIdsForTest(a));
        Assert.DoesNotContain("sx_home", server.StarMapSystemIdsForTest(b));
        Assert.Contains(transport.To<StarMapData>(a), m => m.Systems.Any(s => s.Id == "sx_home" && s.Name == "Sxhome"));
        Assert.DoesNotContain(transport.To<StarMapData>(b), m => m.Systems.Any(s => s.Id == "sx_home"));

        // Ben may not target it; Ada's jump fails only for the missing jump generator (normal jump rules).
        server.HyperjumpToSystem(b.State.PlayerId, "sx_home");
        Assert.Contains(transport.To<ActionRejected>(b), r => r.Reason == "@srv.space.unknown_system");
        server.HyperjumpToSystem(a.State.PlayerId, "sx_home");
        Assert.DoesNotContain(transport.To<ActionRejected>(a), r => r.Reason == "@srv.space.unknown_system");

        // The Guardian's spot is unmoved by the pack system; a restart (the galaxy regenerated) re-appends it at its pin.
        Assert.Equal(guardianBefore, SvGameServer.GuardianFinaleMapPosition(server.Galaxy));
        var pinned = server.Galaxy.Systems.Single(s => s.Id == "sx_home");
        (float x, float y) = (pinned.MapX, pinned.MapY);
        server.DropSystemForTest("sx_home");
        server.EnsurePackSystemsForTest();
        var back = server.Galaxy.Systems.Single(s => s.Id == "sx_home");
        Assert.Equal((x, y), (back.MapX, back.MapY));
        Assert.Equal(StoryPackSystem.BodyIdOf("sx_home"), Assert.Single(back.Bodies).Id);
    }

    // ---------------- Marker providers and predicates (#3349) ----------------

    [Fact]
    public void APackSite_IsMarkedOnItsWorldOnly_AndTheNewPredicatesEvaluate()
    {
        var server = Boot("sites", new RecordingTransport(), StoryPackFixtures.ContentWith(P));
        var a = Join(server, 1, "Ada");
        var at = new Vector3i(10, 40, 12);
        server.RegisterPackSite(P, "sx_tower", a.CurrentLocationId, at);
        Assert.Equal(new Vector3f(10.5f, 41f, 12.5f), server.PackRevealTargetForTest(a, "pack_site:sx_tower"));
        Assert.Null(server.PackRevealTargetForTest(a, "pack_site:nowhere"));
        server.RegisterPackSite(P, "sx_tower", "some-other-world", at);
        Assert.Null(server.PackRevealTargetForTest(a, "pack_site:sx_tower"));

        bool monuments = server.EvaluateStoryConditionForTest("world:monuments", a);
        Assert.Equal(monuments, server.PackRevealTargetForTest(a, StoryRevealProviders.NearestMonument) is not null);
        _ = server.EvaluateStoryConditionForTest("world:shrine", a); // evaluates without a throw on any galaxy
        Assert.True(server.EvaluateStoryConditionForTest("milestone:story:side_extras:started", a));
    }
}
