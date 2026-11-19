// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Playtests;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3852: in a world the HUD shows the first open step of a task. A step the tester has to tick off holds the card
/// there while the game recognises later steps behind it — so the validator names a hand-ticked step that stands
/// before a recognised one. An editor task is exempt (its whole list is on the screen), and so is a task that asks
/// its questions at the end.
/// </summary>
public sealed class PlaytestCatalogStepOrderTests
{
    private const string Walk = """{ "id": "walk", "text": { "de": "Lauf.", "en": "Walk." }, "check": { "event": "move:walked" } }""";
    private const string Mine = """{ "id": "mine", "text": { "de": "Bau ab.", "en": "Mine." }, "check": { "event": "mine:any" } }""";
    private const string Feel = """{ "id": "feel", "text": { "de": "Wie war es?", "en": "How was it?" } }""";
    private const string Look = """{ "id": "look", "text": { "de": "Schau hin.", "en": "Look." } }""";
    private const string Open = """{ "id": "open", "text": { "de": "Öffne ihn.", "en": "Open it." }, "check": { "event": "editor:opened", "arg": "ship" } }""";
    private const string Save = """{ "id": "save", "text": { "de": "Speichere.", "en": "Save." }, "check": { "event": "editor:saved", "arg": "ship" } }""";

    private static PlaytestTask Task(string area, params string[] steps)
    {
        string editor = area == "editor" ? "\"editor\": \"ship\"," : string.Empty;
        string json = $$"""
            { "id": "order", "rev": 1, "area": "{{area}}", {{editor}}
              "title": { "de": "Reihenfolge", "en": "Order" }, "intro": { "de": "Probiere es.", "en": "Try it." },
              "steps": [ {{string.Join(",", steps)}} ] }
            """;
        Assert.True(PlaytestCatalogJson.TryReadTask(json, out var task, PlaytestReadMode.AsWritten));
        return task;
    }

    [Fact]
    public void RecognisedStepsFirst_QuestionsAtTheEnd_IsFine()
    {
        Assert.Empty(PlaytestCatalogValidator.Validate(Task("ingame", Walk, Mine, Feel, Look)));
    }

    [Fact]
    public void ATaskOfHandTickedStepsOnly_IsFine()
    {
        Assert.Empty(PlaytestCatalogValidator.Validate(Task("ingame", Feel, Look)));
    }

    [Fact]
    public void AHandTickedStep_BeforeARecognisedOne_IsAWarning_ThatNamesBothSteps()
    {
        var problem = Assert.Single(PlaytestCatalogValidator.Validate(Task("ingame", Walk, Feel, Mine, Look)));

        Assert.False(problem.IsError);
        Assert.Equal("steps[1]", problem.Where);
        Assert.Contains("steps[2]", problem.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void EveryHandTickedStep_BeforeTheLastRecognisedOne_IsNamed()
    {
        var problems = PlaytestCatalogValidator.Validate(Task("ingame", Feel, Walk, Look, Mine));

        Assert.Equal(new[] { "steps[0]", "steps[2]" }, problems.Select(p => p.Where).ToArray());
        Assert.All(problems, p => Assert.False(p.IsError));
    }

    [Fact]
    public void AnEditorTask_MayMixThem_ItsListIsOnTheScreen()
    {
        Assert.Empty(PlaytestCatalogValidator.Validate(Task("editor", Open, Feel, Look, Save)));
    }
}
