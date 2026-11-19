// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Shared.Playtests;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// A task's prepared world as the client plans it — only what this build knows, only commands from the fixed list —
/// the developer's own inbox for local builds, and the signal hub the editors report to.
/// </summary>
public sealed class PlaytestWorldPlanTests
{
    private static PlaytestTask Task(string extra = "", string area = "ingame")
    {
        string json = $$"""
            { "id": "gas-test", "area": "{{area}}", {{(area == "editor" ? "\"editor\": \"ship\"," : string.Empty)}}
              "title": { "de": "Gasriese: Landung?", "en": "Gas giant: landing?" },
              "steps": [ { "id": "s", "text": { "de": "a", "en": "a" } } ]
              {{extra}} }
            """;
        Assert.True(PlaytestCatalogJson.TryReadTask(json, out var task));
        return task;
    }

    private const string World = """
        , "world": { "seed": 42, "startPlanet": "gas_giant", "preset": "sandbox",
                     "options": { "Hazards": 1, "Creatures": 99, "NotAnOption": 3 },
                     "commands": [ { "cmd": "set_time", "arg": "18:30" }, { "cmd": "give_item", "arg": "iron_ingot", "count": 20 } ] }
        """;

    [Fact]
    public void APlan_CarriesTheWorld_WithOnlyTheOptionsThisBuildKnows()
    {
        var plan = PlaytestWorldPlan.For(Task(World), planet => planet == "gas_giant", "de");

        Assert.Equal(PlaytestWorldRefusal.None, plan.Refusal);
        Assert.Equal("Testpilot - Gasriese  Landung", plan.WorldName); // nothing a folder name cannot hold
        Assert.True(PlaytestWorldPlan.IsTestWorldName(plan.WorldName));
        Assert.Equal(42, plan.Seed);
        Assert.Equal("gas_giant", plan.StartPlanet);
        Assert.Equal(PlaytestWorldPresets.Sandbox, plan.Preset);
        Assert.Equal(PlaytestWorldPresets.TutorialOff, plan.Tutorial);
        Assert.False(plan.Story); // a test world starts without the story …
        Assert.Equal(1, plan.Options["Hazards"]);
        Assert.Equal(PlaytestWorldPlan.MaxOptionIndex, plan.Options["Creatures"]); // cut to the slider's range
        Assert.False(plan.Options.ContainsKey("NotAnOption"));
        Assert.Equal(new[] { "set_time", "give_item" }, plan.Commands.ConvertAll(c => c.Cmd));
    }

    [Fact]
    public void ATaskAboutTheStory_KeepsTheStoryInItsWorld()
    {
        // … unless the task switches it on (a task that tests the opening scene).
        string withStory = World.Replace("\"preset\": \"sandbox\"", "\"preset\": \"sandbox\", \"story\": true");

        var plan = PlaytestWorldPlan.For(Task(withStory), _ => true, "en");

        Assert.Equal(PlaytestWorldRefusal.None, plan.Refusal);
        Assert.True(plan.Story);
    }

    [Fact]
    public void AnUnknownPlanet_RefusesTheWorld_InsteadOfStartingSomewhereElse()
    {
        var plan = PlaytestWorldPlan.For(Task(World), _ => false, "en");

        Assert.Equal(PlaytestWorldRefusal.UnknownPlanet, plan.Refusal);
        Assert.Equal(string.Empty, plan.WorldName);
        Assert.Empty(plan.Commands);
    }

    [Fact]
    public void ATaskWithoutAWorld_AndAnEditorTask_HaveNoWorldToCreate()
    {
        Assert.Equal(PlaytestWorldRefusal.NoWorld, PlaytestWorldPlan.For(Task(), _ => true, "en").Refusal);
        Assert.Equal(PlaytestWorldRefusal.EditorTask, PlaytestWorldPlan.For(Task(World, area: "editor"), _ => true, "en").Refusal);
    }

    [Fact]
    public void WorldNames_AreBounded_AndOnlyTestWorldsCarryThePrefix()
    {
        var task = Task();
        task.Title.En = new string('x', 200);

        string name = PlaytestWorldPlan.NameFor(task, "en");

        Assert.True(name.Length <= 60);
        Assert.StartsWith(PlaytestWorldPlan.WorldNamePrefix, name);
        Assert.False(PlaytestWorldPlan.IsTestWorldName("Meine Welt"));
        Assert.False(PlaytestWorldPlan.IsTestWorldName(null));
    }

    [Theory]
    [InlineData("0.1.0-dev", "http://localhost:31418", "dev", true)]
    [InlineData("0.1.0-dev", "http://127.0.0.1:31418/", "dev", true)]
    [InlineData("0.1.0-dev", "https://reports.example.com", "dev", true)]
    [InlineData("0.1.0-dev", "http://evil.example.com", "dev", false)] // plain http only on the loopback
    [InlineData("0.1.0-dev", "http://localhost:31418", "", false)]
    [InlineData("0.1.0-dev", "", "dev", false)]
    [InlineData("2026.11.22", "http://localhost:31418", "dev", false)] // a release build never leaves the official inbox
    public void TheDevelopersOwnInbox_IsReadOnlyByADevelopmentBuild(string version, string url, string key, bool accepted)
    {
        string? Env(string name) => name == PlaytestDevInbox.UrlVariable ? url : name == PlaytestDevInbox.KeyVariable ? key : null;

        bool ok = PlaytestDevInbox.TryRead(version, Env, out string endpoint, out string apiKey);

        Assert.Equal(accepted, ok);
        if (accepted)
        {
            Assert.EndsWith("/api/bugreport", endpoint);
            Assert.DoesNotContain("//api", endpoint);
            Assert.Equal("dev", apiKey);
            Assert.EndsWith("/api/playtests", PlaytestCatalogClient.EndpointFor(endpoint));
        }
    }

    [Fact]
    public void Signals_ReachWhoeverListens_AndNothingWithoutAListener()
    {
        var seen = new List<string>();
        void Listener(string key, string arg, int amount) => seen.Add($"{key}|{arg}|{amount}");

        PlaytestSignals.Raise(PlaytestEditorEvents.Saved, PlaytestEditorEvents.Ship); // nobody listens: no throw
        PlaytestSignals.Raised += Listener;
        try
        {
            PlaytestSignals.Raise(PlaytestEditorEvents.Saved, PlaytestEditorEvents.Ship);
            PlaytestSignals.Raise(PlaytestEditorEvents.Opened, PlaytestEditorEvents.Texture, 2);
            PlaytestSignals.Raise(string.Empty, "x");
            PlaytestSignals.Raise(PlaytestEditorEvents.Saved, "x", 0);
        }
        finally
        {
            PlaytestSignals.Raised -= Listener;
        }

        Assert.Equal(new[] { "editor:saved|ship|1", "editor:opened|texture|2" }, seen);
    }

    [Fact]
    public void AnEditorTask_IsTickedByEditorSignals_AndAsksNoServerForThem()
    {
        const string json = """
            { "id": "ship-task", "area": "editor", "editor": "ship", "title": { "de": "Schiff", "en": "Ship" },
              "steps": [ { "id": "open", "text": { "de": "a", "en": "a" }, "check": { "event": "editor:opened", "arg": "ship" } },
                         { "id": "save", "text": { "de": "a", "en": "a" }, "check": { "event": "editor:saved", "arg": "ship" } },
                         { "id": "menu", "text": { "de": "a", "en": "a" }, "check": { "event": "ui:menu_open" } } ] }
            """;
        Assert.True(PlaytestCatalogJson.TryReadCatalog("{ \"tasks\": [ " + json + " ] }", out var catalog));
        var tracker = new PlaytestTracker(new PlaytestProgress(string.Empty));
        tracker.SetCatalog(catalog, "0.1.0-dev");
        Assert.True(tracker.Activate("ship-task", 100));

        Assert.Empty(tracker.WatchPatterns()); // editor and UI events are the client's own
        Assert.Equal(0, tracker.OnEvent(PlaytestEditorEvents.Opened, PlaytestEditorEvents.Texture, 1, 100)); // another editor
        Assert.Equal(1, tracker.OnEvent(PlaytestEditorEvents.Opened, PlaytestEditorEvents.Ship, 1, 100));
        Assert.Equal("save", tracker.CurrentStep!.Id);

        tracker.Deactivate();
        Assert.Equal(0, tracker.OnEvent(PlaytestEditorEvents.Saved, PlaytestEditorEvents.Ship, 1, 100)); // put aside: nothing counts
        Assert.Null(tracker.CurrentStep);
        Assert.False(tracker.Activate("no-such-task", 100));
    }
}
