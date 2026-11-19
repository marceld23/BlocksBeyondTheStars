// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Playtests;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The playtest catalogue's shared model: reading (tolerant and bounded for the game, as written for the author),
/// writing, the validator's rules, the fixed list of world start commands, the version window and the text cleaner.
/// The catalogue is fetched text shown inside the game, so the bounds and the cleaning are pinned here.
/// </summary>
public sealed class PlaytestCatalogTests
{
    private const string FullTask = """
        {
          "id": "heavy-shot", "rev": 2, "sort": 10, "area": "ingame",
          "minVersion": "2026.11.21",
          "title": { "de": "Der schwere Schuss", "en": "The heavy shot" },
          "intro": { "de": "Probiere den schweren Schuss aus.", "en": "Try the heavy shot." },
          "minutes": 10,
          "world": { "seed": 4250961102051003344, "startPlanet": "gas_giant", "preset": "sandbox",
                     "options": { "Hazards": 1 },
                     "commands": [ { "cmd": "set_time", "arg": "18:30" },
                                   { "cmd": "give_item", "arg": "iron_ingot", "count": 20 },
                                   { "cmd": "teleport_to_location", "x": 12.5, "y": 64, "z": -3 } ] },
          "steps": [
            { "id": "launch", "text": { "de": "Starte ins All.", "en": "Launch into space." },
              "check": { "event": "space:enter", "arg": "launch" } },
            { "id": "rocks", "text": { "de": "Zerstöre drei Asteroiden.", "en": "Break three asteroids." },
              "check": { "event": "asteroid:destroyed", "required": 3 } },
            { "id": "feel", "text": { "de": "Fühlt sich der Schuss schwer an?", "en": "Does the shot feel heavy?" } }
          ],
          "questions": [
            { "id": "fun", "type": "stars", "text": { "de": "Wie viel Spaß?", "en": "How much fun?" } },
            { "id": "hard", "type": "choice", "text": { "de": "Wie schwer?", "en": "How hard?" },
              "options": [ { "id": "easy", "text": { "de": "Leicht", "en": "Easy" } },
                           { "id": "tough", "text": { "de": "Schwer", "en": "Hard" } } ] }
          ],
          "thanks": { "de": "Danke fürs Testen!", "en": "Thanks for testing!" }
        }
        """;

    private static PlaytestTask Written(string json = FullTask)
    {
        Assert.True(PlaytestCatalogJson.TryReadTask(json, out var task, PlaytestReadMode.AsWritten));
        return task;
    }

    [Fact]
    public void FullTask_ReadsEveryField_AndValidatesClean()
    {
        var task = Written();

        Assert.Equal("heavy-shot", task.Id);
        Assert.Equal(2, task.Rev);
        Assert.Equal("Der schwere Schuss", task.Title.De);
        Assert.Equal(3, task.Steps.Count);
        Assert.Equal("space:enter", task.Steps[0].Check!.Event);
        Assert.Equal(3, task.Steps[1].Check!.Required);
        Assert.Null(task.Steps[2].Check);
        Assert.Equal(2, task.Questions.Count);
        Assert.Equal(2, task.Questions[1].Options.Count);
        Assert.Equal(4250961102051003344L, task.World!.Seed);
        Assert.Equal("gas_giant", task.World.StartPlanet);
        Assert.Equal(PlaytestWorldPresets.TutorialOff, task.World.Tutorial); // the default keeps the training out
        Assert.False(task.World.Story); // … and the story: a test world is there for its task
        Assert.Equal(1, task.World.Options["Hazards"]);
        Assert.Equal(3, task.World.Commands.Count);
        Assert.Equal(12.5f, task.World.Commands[2].X);

        Assert.Empty(PlaytestCatalogValidator.Validate(task));
    }

    [Fact]
    public void ATestWorld_KeepsTheStory_OnlyWhenTheTaskAsksForIt()
    {
        // The switch for a task about the story's beginning: read by the game and by the author's reader alike,
        // written only when it is on, and anything that is not `true` leaves the story off.
        string withStory = FullTask.Replace("\"preset\": \"sandbox\"", "\"preset\": \"sandbox\", \"story\": true");
        Assert.NotEqual(FullTask, withStory);

        var written = Written(withStory);
        Assert.True(written.World!.Story);
        Assert.True(PlaytestCatalogJson.TryReadTask(withStory, out var shown));
        Assert.True(shown.World!.Story);
        Assert.Empty(PlaytestCatalogValidator.Validate(written));

        string compact = PlaytestCatalogJson.WriteTask(written);
        Assert.Contains("\"story\":true", compact);
        Assert.True(PlaytestCatalogJson.TryReadTask(compact, out var again, PlaytestReadMode.AsWritten));
        Assert.True(again.World!.Story);

        Assert.DoesNotContain("\"story\"", PlaytestCatalogJson.WriteTask(Written())); // off is not written
        Assert.False(Written(FullTask.Replace("\"preset\": \"sandbox\"", "\"preset\": \"sandbox\", \"story\": \"yes\"")).World!.Story);
    }

    [Fact]
    public void WriteThenRead_KeepsTheTask()
    {
        var task = Written();

        string compact = PlaytestCatalogJson.WriteTask(task);
        string indented = PlaytestCatalogJson.WriteTask(task, indented: true);
        Assert.Contains("Zerstöre", compact); // readable in an editing form: no \u escapes for umlauts
        Assert.Contains("\n", indented);

        Assert.True(PlaytestCatalogJson.TryReadTask(compact, out var again, PlaytestReadMode.AsWritten));
        Assert.Equal(compact, PlaytestCatalogJson.WriteTask(again));
        Assert.Empty(PlaytestCatalogValidator.Validate(again));
    }

    [Fact]
    public void Catalog_RoundTrips_AndKeepsOrder()
    {
        var catalog = new PlaytestCatalog();
        catalog.Tasks.Add(Written());
        var second = Written();
        second.Id = "second";
        catalog.Tasks.Add(second);

        Assert.True(PlaytestCatalogJson.TryReadCatalog(PlaytestCatalogJson.WriteCatalog(catalog), out var read));

        Assert.True(read.Enabled);
        Assert.Equal(new[] { "heavy-shot", "second" }, read.Tasks.Select(t => t.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    public void UnusableJson_IsRefused_AndReadsAsDisabled(string? json)
    {
        Assert.False(PlaytestCatalogJson.TryReadCatalog(json, out var catalog));
        Assert.False(catalog.Enabled);
        Assert.Empty(catalog.Tasks);
        Assert.False(PlaytestCatalogJson.TryReadTask(json, out _));
    }

    [Fact]
    public void NewerSchema_ReadsAsDisabled_ForTheGame()
    {
        string json = """{ "schema": 99, "enabled": true, "tasks": [ { "id": "x" } ] }""";

        Assert.True(PlaytestCatalogJson.TryReadCatalog(json, out var catalog));

        Assert.False(catalog.Enabled);
        Assert.Empty(catalog.Tasks);
    }

    [Fact]
    public void SafeRead_CleansTexts_AndCutsToTheBounds()
    {
        string longText = new string('a', PlaytestLimits.MaxTextLength + 50);
        string steps = string.Join(",", Enumerable.Range(0, PlaytestLimits.MaxSteps + 5)
            .Select(i => $$"""{ "id": "s{{i}}", "text": { "de": "x", "en": "x" } }"""));
        string json = $$"""
            { "id": "t", "title": { "de": "<size=80>Groß</size>", "en": "<b>Big</b> https://example.com/x now" },
              "intro": { "de": "{{longText}}", "en": "ok" },
              "steps": [ {{steps}} ] }
            """;

        Assert.True(PlaytestCatalogJson.TryReadTask(json, out var task));

        Assert.Equal("Groß", task.Title.De);
        Assert.Equal("Big  now", task.Title.En);
        Assert.Equal(PlaytestLimits.MaxTextLength, task.Intro.De.Length);
        Assert.Equal(PlaytestLimits.MaxSteps, task.Steps.Count);
    }

    [Fact]
    public void SafeRead_DropsWhatTheGameCannotUse()
    {
        string json = """
            { "id": "t", "title": { "de": "T", "en": "T" },
              "world": { "preset": "nonsense", "tutorial": "nonsense",
                         "commands": [ { "cmd": "godmode" }, { "cmd": "set_time", "arg": "18:30" },
                                       { "cmd": "give_item", "arg": "iron_ingot", "count": 5000 } ] },
              "steps": [ { "id": "ok", "text": { "de": "a", "en": "a" }, "check": { "arg": "x" } },
                         { "id": "Bad Id", "text": { "de": "a", "en": "a" } },
                         { "id": "empty", "text": { } } ],
              "questions": [ { "id": "q1", "type": "essay", "text": { "de": "a", "en": "a" } },
                             { "id": "q2", "type": "choice", "text": { "de": "a", "en": "a" },
                               "options": [ { "id": "only", "text": { "de": "a", "en": "a" } } ] },
                             { "id": "q3", "type": "stars", "text": { "de": "a", "en": "a" } } ] }
            """;

        Assert.True(PlaytestCatalogJson.TryReadTask(json, out var task));

        Assert.Equal(new[] { "ok" }, task.Steps.Select(s => s.Id));
        Assert.Null(task.Steps[0].Check); // a check without an event leaves the step to the player
        Assert.Equal(new[] { "q3" }, task.Questions.Select(q => q.Id)); // an unaskable mandatory question must not exist
        Assert.Equal(PlaytestWorldPresets.Standard, task.World!.Preset);
        Assert.Equal(PlaytestWorldPresets.TutorialOff, task.World.Tutorial);
        Assert.Equal(new[] { "set_time" }, task.World.Commands.Select(c => c.Cmd)); // off the list, or over the amount: gone
    }

    [Fact]
    public void SafeCatalogRead_LeavesOutTasksWithoutIdTitleOrSteps()
    {
        string json = """
            { "tasks": [ { "id": "no-steps", "title": { "de": "a", "en": "a" } },
                         { "title": { "de": "a", "en": "a" }, "steps": [ { "id": "s", "text": { "de": "a", "en": "a" } } ] },
                         { "id": "fine", "title": { "de": "a", "en": "a" }, "steps": [ { "id": "s", "text": { "de": "a", "en": "a" } } ] },
                         42 ] }
            """;

        Assert.True(PlaytestCatalogJson.TryReadCatalog(json, out var catalog));

        Assert.Equal(new[] { "fine" }, catalog.Tasks.Select(t => t.Id));
    }

    [Fact]
    public void Validator_NamesEveryMistake()
    {
        string json = """
            { "id": "Bad Id", "rev": 0, "area": "editor", "editor": "content",
              "minVersion": "soon", "maxVersion": "2026.1.1",
              "title": { "de": "nur deutsch" },
              "intro": { "de": "a <b>fett</b>", "en": "see www.example.com" },
              "steps": [ { "id": "a", "text": { "de": "x", "en": "x" }, "check": { "event": "", "required": 0 } },
                         { "id": "a", "text": { "de": "x", "en": "x" } } ],
              "questions": [ { "id": "q", "type": "stars", "text": { "de": "x", "en": "x" },
                               "options": [ { "id": "o", "text": { "de": "x", "en": "x" } } ] },
                             { "id": "c", "type": "choice", "text": { "de": "x", "en": "x" } } ],
              "world": { "preset": "nonsense", "startPlanet": "Gas Giant", "commands": [ { "cmd": "godmode" } ] } }
            """;

        var where = PlaytestCatalogValidator.Validate(Written(json)).Where(p => p.IsError).Select(p => p.Where).ToList();

        foreach (string expected in new[]
                 {
                     "id", "rev", "editor", "minVersion", "title.en", "intro.de", "intro.en", "steps[0].check.event",
                     "steps[0].check.required", "steps[1].id", "questions[0].options", "questions[1].options",
                     "world.preset", "world.startPlanet", "world.commands[0]",
                 })
        {
            Assert.Contains(expected, where);
        }
    }

    [Fact]
    public void Validator_UnknownEvent_IsOnlyAWarning()
    {
        var task = Written();
        task.Steps[0].Check!.Event = "future:event";

        var problems = PlaytestCatalogValidator.Validate(task);

        Assert.False(PlaytestCatalogValidator.HasErrors(problems));
        Assert.Contains(problems, p => !p.IsError && p.Where == "steps[0].check.event");
    }

    [Theory]
    [InlineData("mine:any", true)]
    [InlineData("craft:*", true)]
    [InlineData("crystal:*", true)]
    [InlineData("ui:menu_open", true)]
    [InlineData("ui:nonsense", false)]
    [InlineData("editor:saved", true)]
    [InlineData("editor:*", true)]
    [InlineData("nope:any", false)]
    [InlineData("", false)]
    public void KnownEvents_CoverServerUiAndEditorEvents(string pattern, bool known)
        => Assert.Equal(known, PlaytestCatalogValidator.IsKnownEvent(pattern));

    [Fact]
    public void Check_MatchesKeyPatternAndArgumentAlternatives()
    {
        var check = new PlaytestCheck { Event = "scan:performed", Arg = "asteroid|anomaly" };
        Assert.True(check.Matches("scan:performed", "anomaly"));
        Assert.False(check.Matches("scan:performed", "flora"));
        Assert.False(check.Matches("scan:any", "anomaly"));

        var any = new PlaytestCheck { Event = "editor:*" };
        Assert.True(any.Matches("editor:saved", "ship"));
    }

    [Theory]
    [InlineData("set_time", "18:30", 0, true)]
    [InlineData("set_weather", "storm", 0, true)]
    [InlineData("teleport_to_named", "village 2", 0, true)]
    [InlineData("give_item", "iron_ingot", 1, true)]
    [InlineData("give_item", "iron_ingot", 999, true)]
    [InlineData("give_item", "iron_ingot", 1000, false)]
    [InlineData("give_item", "iron_ingot", 0, false)]
    [InlineData("give_item", "Iron Ingot", 5, false)]
    [InlineData("set_time", "", 0, false)]
    [InlineData("set_time", "18:30; /godmode", 0, false)]
    [InlineData("godmode", "", 0, false)]
    [InlineData("fly", "", 0, false)]
    [InlineData("goto", "sys1-p3", 0, false)]
    public void WorldCommands_OnlyTheFixedList_WithPlainArguments(string cmd, string arg, int count, bool allowed)
    {
        var command = new PlaytestWorldCommand { Cmd = cmd, Arg = arg, Count = count };

        Assert.Equal(allowed, PlaytestWorldCommands.Problem(command) is null);
    }

    [Fact]
    public void WorldCommands_TeleportNeedsFiniteCoordinates()
    {
        var command = new PlaytestWorldCommand { Cmd = PlaytestWorldCommands.TeleportToLocation, X = 1, Y = float.NaN, Z = 3 };
        Assert.NotNull(PlaytestWorldCommands.Problem(command));

        command.Y = 64;
        Assert.Null(PlaytestWorldCommands.Problem(command));
    }

    [Theory]
    [InlineData("2026.11.22", "2026.11.21", "", true)]
    [InlineData("2026.11.20", "2026.11.21", "", false)]
    [InlineData("2026.11.21", "2026.11.21", "2026.11.21", true)]
    [InlineData("2026.12.1", "", "2026.11.30", false)]
    [InlineData("2026.9.18", "2026.11.1", "", false)] // numeric, not text order: 9 < 11
    [InlineData("v2026.11.22", "2026.11.21", "", true)]
    [InlineData("0.1.0-dev", "2026.11.21", "2026.11.21", true)] // a development build sees everything
    [InlineData("garbage", "2026.11.21", "", true)]
    [InlineData("2026.11.22", "", "", true)]
    public void VersionWindow(string game, string min, string max, bool shown)
        => Assert.Equal(shown, PlaytestVersion.InRange(game, min, max));

    [Theory]
    [InlineData("de", "Hallo")]
    [InlineData("de-DE", "Hallo")]
    [InlineData("en", "Hello")]
    [InlineData("fr", "Hello")]
    [InlineData(null, "Hello")]
    public void Text_PicksGermanOrEnglish(string? locale, string expected)
        => Assert.Equal(expected, new PlaytestText { De = "Hallo", En = "Hello" }.For(locale));

    [Fact]
    public void Text_FallsBackToTheOtherLanguage()
    {
        Assert.Equal("Hallo", new PlaytestText { De = "Hallo" }.For("en"));
        Assert.Equal("Hello", new PlaytestText { En = "Hello" }.For("de"));
    }

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("<color=red>rot</color>", "rot")]
    [InlineData("a > b", "a  b")]
    [InlineData("line one\nline two", "line one\nline two")]
    [InlineData("tab\there", "tabhere")]
    [InlineData("see HTTPS://Example.com/a?b=c for more", "see  for more")]
    [InlineData("  spaced  ", "spaced")]
    public void Cleaner_RemovesTagsLinksAndControlCharacters(string text, string expected)
        => Assert.Equal(expected, PlaytestTextCleaner.Clean(text, 400));

    [Theory]
    [InlineData("heavy-shot", true)]
    [InlineData("a", true)]
    [InlineData("t2", true)]
    [InlineData("", false)]
    [InlineData("-lead", false)]
    [InlineData("trail-", false)]
    [InlineData("Upper", false)]
    [InlineData("with space", false)]
    [InlineData("under_score", false)]
    [InlineData("../etc", false)]
    public void Ids_AreUrlAndFileSafe(string id, bool ok)
        => Assert.Equal(ok, PlaytestCatalogValidator.IsWellFormedId(id));

    [Fact]
    public void EditorList_IsTheSevenPlayerEditors()
    {
        Assert.Equal(7, PlaytestEditorEvents.Editors.Count);
        Assert.DoesNotContain("content", PlaytestEditorEvents.Editors);  // the two developer editors stay out
        Assert.DoesNotContain("material", PlaytestEditorEvents.Editors);
        Assert.All(PlaytestEditorEvents.All, key => Assert.True(PlaytestEditorEvents.IsEditorEvent(key)));
    }
}
