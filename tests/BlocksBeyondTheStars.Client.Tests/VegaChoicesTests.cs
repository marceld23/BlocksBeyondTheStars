// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2499 — VEGA's buttons on the client side: <see cref="ShipAiLine.Choices"/> read into the panel's two pills
/// (<see cref="VegaChoiceSet"/>: the primary on the continue pill, the secondary where the prologue's skip pill is — an
/// unreadable entry keeps its place, a "skip" never moves up onto the continue control), the continue control's tap / hold
/// (<see cref="VegaChoiceInput"/>), the pill texts, the drop of a skipped pack's waiting lines — and the whole question seen
/// by a real <see cref="NetworkClient"/> that announces <c>choices1</c> against the real server.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class VegaChoicesTests
{
    private const string Tutorial = "tutorial";
    private const float Frame = 1f / 60f;

    private static string Entry(string label, string action, string pack = Tutorial, string chapter = "")
        => ShipAiChoices.Encode(label, action, pack, chapter);

    // ---------------- Reading the line's buttons ----------------

    [Fact]
    public void APlainLine_HasNoButtons()
    {
        Assert.Same(VegaChoiceSet.None, VegaChoiceSet.Parse(null));
        Assert.Same(VegaChoiceSet.None, VegaChoiceSet.Parse(System.Array.Empty<string>()));
        Assert.False(VegaChoiceSet.None.HasAny);
        Assert.Same(VegaChoice.PlainContinue, VegaChoiceSet.None.Primary);
        Assert.Null(VegaChoiceSet.None.Secondary);
    }

    [Fact]
    public void TheQuestion_ReadsAsLetsGoOnTheContinueControl_AndTheSkipBeside()
    {
        var set = VegaChoiceSet.Parse(new[]
        {
            Entry("ui.vega.choice.lets_go", ShipAiChoices.Continue),
            Entry("ui.vega.choice.know_game", StoryPackActions.SkipPack),
        });

        Assert.True(set.HasAny);
        Assert.Equal("ui.vega.choice.lets_go", set.Primary.LabelKey);
        Assert.False(set.Primary.SendsIntent); // "Let's go" only goes on
        Assert.False(set.Primary.DropsPackLines);
        Assert.NotNull(set.Secondary);
        Assert.Equal(StoryPackActions.SkipPack, set.Secondary!.Action);
        Assert.Equal(Tutorial, set.Secondary.PackId);
        Assert.True(set.Secondary.SendsIntent);
        Assert.True(set.Secondary.DropsPackLines); // the training's waiting lines go with the skip
    }

    [Fact]
    public void AnOffer_ReadsAsLaterAndStartNow_WithTheChapter()
    {
        var set = VegaChoiceSet.Parse(new[]
        {
            Entry("ui.vega.choice.later", StoryPackActions.DeclineChapter, chapter: "c2"),
            Entry("ui.vega.choice.start_now", StoryPackActions.StartChapter, chapter: "c2"),
            Entry("ui.vega.choice.extra", ShipAiChoices.Continue), // a third button is never drawn
        });

        Assert.Equal((StoryPackActions.DeclineChapter, "c2"), (set.Primary.Action, set.Primary.ChapterId));
        Assert.Equal((StoryPackActions.StartChapter, "c2"), (set.Secondary!.Action, set.Secondary.ChapterId));
        Assert.False(set.Primary.DropsPackLines);
        Assert.False(set.Secondary.DropsPackLines);
    }

    [Fact]
    public void AnUnreadableFirstButton_LeavesThePlainContinue_TheSecondNeverMovesUp()
    {
        var set = VegaChoiceSet.Parse(new[]
        {
            Entry("ui.vega.choice.new", "someFutureAction"),
            Entry("ui.vega.choice.know_game", StoryPackActions.SkipPack),
        });

        Assert.Same(VegaChoice.PlainContinue, set.Primary); // N stays a harmless continue
        Assert.Equal(StoryPackActions.SkipPack, set.Secondary!.Action);
        Assert.True(set.HasAny);
    }

    [Theory]
    [InlineData("garbled")] // no separator
    [InlineData("ui.x\u001FskipPack\u001F")] // a pack action without its pack
    [InlineData("ui.x\u001FstartChapter\u001Ftutorial")] // a chapter action without its chapter
    [InlineData("ui.x\u001FoptOut\u001Ftutorial")] // an action the panel never offers
    public void AnUnusableEntry_IsNoButton(string entry)
    {
        Assert.Same(VegaChoiceSet.None, VegaChoiceSet.Parse(new[] { entry }));
        var set = VegaChoiceSet.Parse(new[] { Entry("ui.vega.choice.lets_go", ShipAiChoices.Continue), entry });
        Assert.Null(set.Secondary);
        Assert.Equal("ui.vega.choice.lets_go", set.Primary.LabelKey);
    }

    // ---------------- The continue control: tap = primary, hold = secondary ----------------

    private static VegaChoicePick Hold(VegaChoiceInput input, float seconds, bool hasSecondary = true)
    {
        var pick = input.Tick(down: true, held: true, Frame, hasSecondary);
        for (float t = 0f; pick == VegaChoicePick.None && t < seconds; t += Frame)
        {
            pick = input.Tick(down: false, held: true, Frame, hasSecondary);
        }

        return pick;
    }

    [Fact]
    public void ATap_PicksThePrimary_WhenLetGo()
    {
        var input = new VegaChoiceInput();
        Assert.Equal(VegaChoicePick.None, input.Tick(down: true, held: true, Frame, hasSecondary: true));
        Assert.True(input.Holding);
        Assert.Equal(VegaChoicePick.None, input.Tick(down: false, held: true, 0.2f, hasSecondary: true));
        Assert.InRange(input.HoldProgress, 0.2f, 0.3f);
        Assert.Equal(VegaChoicePick.Primary, input.Tick(down: false, held: false, Frame, hasSecondary: true));
        Assert.False(input.Holding);
        Assert.Equal(0f, input.HoldProgress);
    }

    [Fact]
    public void AHold_PicksTheSecondary_OnlyAfterTheHoldTime()
    {
        var input = new VegaChoiceInput();
        Assert.Equal(VegaChoicePick.None, Hold(input, VegaChoiceInput.HoldSeconds - 0.1f));
        Assert.True(input.Holding);
        Assert.Equal(VegaChoicePick.Secondary, Hold(new VegaChoiceInput(), VegaChoiceInput.HoldSeconds + 0.1f));

        // The key still held afterwards starts nothing new — a press must begin on the line.
        var after = new VegaChoiceInput();
        Assert.Equal(VegaChoicePick.None, after.Tick(down: false, held: true, 1f, hasSecondary: true));
        Assert.False(after.Holding);
    }

    [Fact]
    public void WithoutASecondary_OrWithoutAHold_ThePressIsThePrimaryAtOnce()
    {
        Assert.Equal(VegaChoicePick.Primary, new VegaChoiceInput().Tick(down: true, held: true, Frame, hasSecondary: false));
        // The action list's "continue" (an injected press) is down without being held.
        Assert.Equal(VegaChoicePick.Primary, new VegaChoiceInput().Tick(down: true, held: false, Frame, hasSecondary: true));
    }

    [Fact]
    public void AReset_ForgetsTheHold_AndABadFrameTimeAddsNothing()
    {
        var input = new VegaChoiceInput();
        input.Tick(down: true, held: true, Frame, hasSecondary: true);
        input.Tick(down: false, held: true, float.NaN, hasSecondary: true);
        input.Tick(down: false, held: true, float.PositiveInfinity, hasSecondary: true);
        Assert.Equal(0f, input.HeldSeconds);
        input.Reset();
        Assert.False(input.Holding);
        Assert.Equal(VegaChoicePick.None, input.Tick(down: false, held: false, Frame, hasSecondary: true)); // a release after a reset is no tap
    }

    // ---------------- The pill texts ----------------

    [Fact]
    public void ThePills_NameTheControl_AndTouchShowsTheLabelAlone()
    {
        Assert.Equal("Let's go · [N]", VegaChoiceLabels.Primary("Let's go", "[N]"));
        Assert.Equal("Let's go", VegaChoiceLabels.Primary("Let's go", string.Empty));
        Assert.Equal("Hold [N]: I know the game — skip", VegaChoiceLabels.Secondary("I know the game — skip", "Hold {0}: {1}", "[N]"));
        Assert.Equal("(B) halten: Jetzt starten", VegaChoiceLabels.Secondary("Jetzt starten", "{0} halten: {1}", "(B)"));
        Assert.Equal("Jetzt starten", VegaChoiceLabels.Secondary("Jetzt starten", "{0} halten: {1}", string.Empty));
        Assert.Equal("Start now", VegaChoiceLabels.Secondary("Start now", "Hold {0: {1}", "[N]")); // a broken translation
    }

    // ---------------- The queue ----------------

    [Fact]
    public void ASkip_DropsOnlyTheSkippedPacksWaitingLines_InOrder()
    {
        var queue = new VegaLineQueue();
        queue.Enqueue(new VegaQueuedLine("walk", false, "story.tutorial.b1.walk.start", Tutorial), VegaLineClass.Step, 0);
        queue.Enqueue(new VegaQueuedLine("vega", false, "story.vega.ch1.lead", "vega_protocol"), VegaLineClass.Step, 0);
        queue.Enqueue(new VegaQueuedLine("jump", false, "story.tutorial.b1.jump.start", Tutorial), VegaLineClass.Step, 0);
        queue.Enqueue(new VegaQueuedLine("break", false, "ui.reminder.break"), VegaLineClass.Story, 0);

        Assert.Equal(2, queue.DropPack(Tutorial));
        Assert.Equal(new[] { "vega", "break" }, queue.Lines.Select(l => l.Text));
        Assert.Equal(0, queue.DropPack(string.Empty));
        Assert.Same(VegaChoiceSet.None, queue.Lines.First().Choices);
    }

    // ---------------- A real client ----------------

    [Fact]
    public void ShowsChoices_TogglesTheFeatureWord_AndKeepsTheRest()
    {
        using var client = new NetworkClient(new BlocksBeyondTheStars.Networking.Transport.LoopbackClientTransport(new BlocksBeyondTheStars.Networking.Transport.LoopbackLink()));
        Assert.False(client.ShowsChoices); // a headless client draws no buttons
        client.PlaysCutscenes = true;
        client.ShowsChoices = true;
        client.ShowsChoices = true;
        Assert.Equal(new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1, ClientFeatures.Choices1 }, client.Features);
        client.ShowsChoices = false;
        Assert.Equal(new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 }, client.Features);
    }

    [Fact]
    public void ARealClient_IsAsked_PicksTheSkip_AndHearsWhereTheTrainingWaits()
    {
        using var h = new ClientServerHarness(
            BlocksBeyondTheStars.Shared.Content.ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()),
            c => c.Rules.StoryId = "none");
        var lines = new List<ShipAiLine>();
        h.Client.ShipAiLineReceived += lines.Add;
        h.Client.ShowsChoices = true;
        h.Join("Rookie");
        Assert.NotNull(h.JoinAccepted);
        Assert.True(h.PumpUntil(() => lines.Any(l => l.LineKey == "story.tutorial.ask_skip")));

        var ask = lines.Single(l => l.LineKey == "story.tutorial.ask_skip");
        var set = VegaChoiceSet.Parse(ask.Choices);
        Assert.Equal("ui.vega.choice.lets_go", set.Primary.LabelKey);
        Assert.Equal("ui.vega.choice.know_game", set.Secondary!.LabelKey);

        // The panel's queue: the question, then the first lesson waiting behind it — the skip drops the lesson.
        var queue = new VegaLineQueue();
        foreach (var line in lines.Where(l => l.LineKey.Length > 0))
        {
            queue.Enqueue(new VegaQueuedLine(line.LineKey, false, line.LineKey, line.PackId, VegaChoiceSet.Parse(line.Choices)),
                VegaLineQueue.ClassOf(line.Kind, line.PackId), 0);
        }

        Assert.True(queue.TryDequeue(out var question));
        Assert.Equal("story.tutorial.ask_skip", question.Key);
        Assert.Contains(queue.Lines, l => l.Key == "story.tutorial.b1.walk.start");
        var skip = question.Choices.Secondary!;
        h.Client.SendStoryPack(skip.PackId, skip.Action, skip.ChapterId);
        queue.DropPack(skip.PackId);
        Assert.DoesNotContain(queue.Lines, l => l.PackId == Tutorial);

        Assert.True(h.PumpUntil(() => lines.Any(l => l.LineKey == "story.tutorial.skipped_hint")));
        Assert.Single(lines, l => l.LineKey == "story.tutorial.skipped_hint");
    }

    [Fact]
    public void AHeadlessClient_IsNeverAsked()
    {
        using var h = new ClientServerHarness(
            BlocksBeyondTheStars.Shared.Content.ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()),
            c => c.Rules.StoryId = "none");
        var lines = new List<ShipAiLine>();
        h.Client.ShipAiLineReceived += lines.Add;
        h.Join("Headless");
        Assert.True(h.PumpUntil(() => lines.Any(l => l.LineKey == "story.tutorial.b1.walk.start")));
        Assert.DoesNotContain(lines, l => l.LineKey == "story.tutorial.ask_skip" || l.Choices.Length > 0);
    }
}
