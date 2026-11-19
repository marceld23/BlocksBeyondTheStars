// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Playtests;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The validator's rules about a step's event. #3833: an event newer than the first Test Pilot client needs a
/// <c>minVersion</c> — that client shows no "Done" button for a step that waits for a server event and never hears one
/// it does not know, so the step could not be closed. #3837: a step the task's own test world cannot bring about (a
/// fall in a sandbox world, a raider in a peaceful one) is named, so the author sees it before a tester is stuck.
/// </summary>
public sealed class PlaytestCatalogEventRulesTests
{
    private static PlaytestTask Task(string eventKey, string arg = "", string minVersion = "", string preset = "standard", string options = "")
    {
        string json = $$"""
            { "id": "rules", "rev": 1, "area": "ingame", "minVersion": "{{minVersion}}",
              "title": { "de": "Regeln", "en": "Rules" }, "intro": { "de": "Probiere es.", "en": "Try it." },
              "world": { "seed": 7, "startPlanet": "rocky", "preset": "{{preset}}", "options": { {{options}} } },
              "steps": [ { "id": "do", "text": { "de": "Tu es.", "en": "Do it." },
                           "check": { "event": "{{eventKey}}", "arg": "{{arg}}" } } ] }
            """;
        Assert.True(PlaytestCatalogJson.TryReadTask(json, out var task, PlaytestReadMode.AsWritten));
        return task;
    }

    private static List<PlaytestProblem> At(PlaytestTask task)
        => PlaytestCatalogValidator.Validate(task).Where(p => p.Where == "steps[0].check.event").ToList();

    // ---- #3833: events newer than the first client -----------------------------------------------------------------

    [Fact]
    public void ANewerEvent_WithoutAMinVersion_IsAnError_ThatNamesTheVersion()
    {
        var problem = Assert.Single(At(Task(GameEventKeys.ShipRolled)));

        Assert.True(problem.IsError);
        Assert.Contains(PlaytestEventVersions.FeelEvents, problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026.11.23", true)]   // the first client itself: it cannot close the step
    [InlineData("2026.11.21", true)]
    [InlineData("2026.11.24", false)]
    [InlineData("2026.12.1", false)]   // the next release may as well open a new month
    [InlineData("2027.1.1", false)]
    public void TheMinVersion_MustKeepTheFirstClientOut(string minVersion, bool refused)
    {
        var problems = At(Task(GameEventKeys.ThrowAny, minVersion: minVersion));

        Assert.Equal(refused, problems.Any(p => p.IsError));
    }

    [Fact]
    public void AnEventTheFirstClientKnows_NeedsNoMinVersion()
    {
        Assert.Empty(At(Task(GameEventKeys.MoveWalked)));
        Assert.Empty(At(Task(GameEventKeys.PlayerRespawned)));
    }

    [Fact]
    public void APattern_NeedsAMinVersion_OnlyWhenEverythingItMatchesIsNewer()
    {
        Assert.Equal(PlaytestEventVersions.FeelEvents, PlaytestEventVersions.RequiredFor("throw:*"));
        Assert.Equal(PlaytestEventVersions.FeelEvents, PlaytestEventVersions.RequiredFor("emote:played"));
        Assert.Null(PlaytestEventVersions.RequiredFor("player:*")); // player:respawned is old: the step can close there
        Assert.Null(PlaytestEventVersions.RequiredFor("ship:*"));   // ship:returned, ship:repaired …
        Assert.Null(PlaytestEventVersions.RequiredFor("nope:any")); // unknown: the validator's warning, not this rule
        Assert.Null(PlaytestEventVersions.RequiredFor("editor:saved"));
        Assert.Null(PlaytestEventVersions.RequiredFor("ui:menu_open"));
    }

    [Fact]
    public void EveryVersionedEvent_IsAPublishedEvent_AndItsVersionIsAboveTheFirstClient()
    {
        Assert.True(PlaytestVersion.TryParse(PlaytestEventVersions.FirstClient, out var first));
        int versioned = 0;
        foreach (string key in GameEventKeys.All)
        {
            if (PlaytestEventVersions.SinceVersion(key) is not { } since)
            {
                continue;
            }

            versioned++;
            Assert.True(PlaytestVersion.TryParse(since, out var version), key);
            Assert.True(PlaytestVersion.Compare(version, first) > 0, key);
        }

        Assert.Equal(9, versioned); // #3831 + #3832 — a new event a playtest may wait for gets its row in the table
    }

    // ---- #3837: a step the test world cannot bring about ----------------------------------------------------------

    [Theory]
    [InlineData("player:respawned")]
    [InlineData("player:fallen")]
    [InlineData("ship:evaded")]
    [InlineData("defeat:any")]
    [InlineData("guardian_giant:defeated")]
    public void InASandboxWorld_AFallAndAFight_AreNamed(string eventKey)
    {
        var task = Task(eventKey, minVersion: PlaytestEventVersions.FeelEvents, preset: "sandbox");

        var problem = Assert.Single(At(task));
        Assert.False(problem.IsError); // the author may accept it
        Assert.Contains("sandbox", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("hard")]
    public void InAWorldThatHurts_TheSameSteps_AreFine(string preset)
    {
        Assert.Empty(At(Task(GameEventKeys.DefeatAny, preset: preset)));
        Assert.Empty(At(Task(GameEventKeys.PlayerRespawned, preset: preset)));
    }

    [Fact]
    public void InAPeacefulWorld_OnlyStepsThatNeedHostiles_AreNamed()
    {
        Assert.Single(At(Task(GameEventKeys.DefeatAny, arg: "raider|machine", preset: "peaceful")));
        Assert.Single(At(Task(GameEventKeys.ShipEvaded, minVersion: PlaytestEventVersions.FeelEvents, preset: "peaceful")));

        Assert.Empty(At(Task(GameEventKeys.DefeatAny, preset: "peaceful")));                    // an animal counts
        Assert.Empty(At(Task(GameEventKeys.DefeatAny, arg: "creature|machine", preset: "peaceful")));
        Assert.Empty(At(Task(GameEventKeys.MoveWalked, preset: "peaceful")));
        Assert.Empty(At(Task(GameEventKeys.DefeatAny, arg: "raider", preset: "peaceful", options: "\"SpaceNpcs\": 3"))); // turned on again
    }
}
