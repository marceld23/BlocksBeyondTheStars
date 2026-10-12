// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2483 — several storylines at once, seen from a real client (a real <c>NetworkClient</c> against a real server): the basic
/// training, VEGA's story and the pilot side story (the data-only test pack under
/// <c>tests/BlocksBeyondTheStars.Tests/Fixtures/story_packs/side_pilot</c>) each get their own Journal category, in the
/// server's order, next to VEGA's notes and the player's notes; the Journal opens on the story that owns the chip, and a
/// pinned side story takes the chip with its own short name as the label.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class SideStoryPilotClientTests
{
    private const string Pilot = "side_pilot";

    private static string PilotDir()
        => Path.Combine(ClientTestPaths.RepoRoot(), "tests", "BlocksBeyondTheStars.Tests", "Fixtures", "story_packs", Pilot);

    /// <summary>The shipped content plus the pilot pack (the server sends keys; the test localizes with the pack's English table).</summary>
    private static (GameContent Content, Dictionary<string, string> English) ContentWithPilot()
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        var pilot = JsonSerializer.Deserialize<StoryDefinition>(File.ReadAllText(Path.Combine(PilotDir(), "story.json")), ContentLoader.JsonOptions)!;
        pilot.Cutscenes.AddRange(ContentLoader.LoadPackCutscenes(PilotDir()));
        content.SetStories(content.Stories.Values.Append(pilot).ToList());
        var english = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(PilotDir(), "locales", "en.json")));
        return (content, english);
    }

    [Fact]
    public void ARealClient_ListsTheTrainingVegaAndTheSideStory_AsJournalCategories_AndLabelsAPinnedSideStoryOnTheChip()
    {
        var (content, english) = ContentWithPilot();
        using var h = new ClientServerHarness(content);
        var states = new List<StoryPackStates>();
        var objectives = new List<ObjectiveList>();
        h.Client.StoryPackStatesReceived += states.Add;
        h.Client.ObjectiveListReceived += objectives.Add;
        h.Join("Kid");
        Assert.NotNull(h.JoinAccepted);
        Assert.True(h.PumpUntil(() => states.Count > 0 && objectives.Count > 0));

        var game = content.CreateLocalizer(BlocksBeyondTheStars.Shared.Localization.GameLocale.English);
        string L(string key) => english.TryGetValue(key, out var text) ? text : game.Get(key);
        bool Has(string key) => english.ContainsKey(key) || game.Has(key);

        // The side story waits for part 1 of the training — the Journal lists it already (it may be started early).
        h.Client.SendStoryPack(Pilot, StoryPackActions.StartPack);
        Assert.True(h.PumpUntil(() => states.Last().Packs.Any(p => p.Id == Pilot && p.Started)));
        var categories = JournalCategories.Build(states.Last(), "vega_protocol", "story.vega_protocol.name", worldStoryActive: true, L, Has);
        Assert.Equal(
            new[] { "pack:tutorial", "pack:vega_protocol", "pack:" + Pilot, JournalCategories.TipsId, JournalCategories.NotesId },
            categories.Select(c => c.Id));
        Assert.Equal("The Beacon Keeper", categories[2].Label);

        // The training owns the chip, so the Journal opens on it; the side story's row follows in the objective list.
        Assert.True(h.PumpUntil(() => objectives.Last().Items.Length == 2));
        Assert.Equal(new[] { "tutorial", Pilot }, objectives.Last().Items.Select(i => i.PackId));
        Assert.Equal("pack:tutorial", JournalCategories.Default(categories, states.Last(), objectives.Last()));

        // "Track": the side story takes the chip — labelled with its own short name — and the Journal opens on it.
        h.Client.SendStoryPack(Pilot, StoryPackActions.Track);
        Assert.True(h.PumpUntil(() => objectives.Last().Items.FirstOrDefault()?.PackId == Pilot && states.Last().TrackedPackId == Pilot));
        var chip = objectives.Last().Items[0];
        Assert.True(chip.Pinned);
        Assert.Equal("story.side_pilot.p1.lamp.chip", chip.Key);
        string shortName = ObjectiveTracker.ShortNameKey(chip.PackId, states.Last(), content.Stories.Values);
        Assert.Equal("Beacon: Switch on your lamp", ObjectiveTracker.ChipBody(ObjectiveTracker.Prefix(shortName, L, Has), L(chip.Key)));
        categories = JournalCategories.Build(states.Last(), "vega_protocol", "story.vega_protocol.name", worldStoryActive: true, L, Has);
        Assert.Equal("pack:" + Pilot, JournalCategories.Default(categories, states.Last(), objectives.Last()));
    }
}
