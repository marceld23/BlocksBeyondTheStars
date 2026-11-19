// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Playtests;

/// <summary>
/// The playtest catalogue: the list of playtest tasks the report inbox serves and the game client shows on its
/// "Test Pilot" page. The catalogue is NOT part of a build — the client fetches it, so a task can be added, changed
/// or switched off without a release. One model for the inbox (which stores and validates tasks), the client (which
/// shows them) and the tests; read with <see cref="PlaytestCatalogJson"/>, checked with
/// <see cref="PlaytestCatalogValidator"/>.
/// </summary>
public sealed class PlaytestCatalog
{
    /// <summary>The format number. A client that reads a higher number than <see cref="PlaytestLimits.Schema"/>
    /// hides the page instead of guessing.</summary>
    public int Schema { get; set; } = PlaytestLimits.Schema;

    /// <summary>The kill switch: false hides every task on every client.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The tasks, in display order.</summary>
    public List<PlaytestTask> Tasks { get; set; } = new();
}

/// <summary>One playtest task: what to try, the steps to tick off and the questions of the closing report.</summary>
public sealed class PlaytestTask
{
    /// <summary>Stable id (<c>a-z</c>, <c>0-9</c>, <c>-</c>). Progress and reports are booked under it.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Counts up with every change of the task; a report names the revision it answered.</summary>
    public int Rev { get; set; } = 1;

    /// <summary>False keeps the task stored but hides it from clients.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Display order, ascending.</summary>
    public int Sort { get; set; }

    /// <summary><see cref="PlaytestAreas.Ingame"/> or <see cref="PlaytestAreas.Editor"/>.</summary>
    public string Area { get; set; } = PlaytestAreas.Ingame;

    /// <summary>For an editor task: which editor (<see cref="PlaytestEditorEvents.Editors"/>).</summary>
    public string Editor { get; set; } = string.Empty;

    /// <summary>The first game version the task is shown in (<c>2026.11.21</c>); empty = any.</summary>
    public string MinVersion { get; set; } = string.Empty;

    /// <summary>The last game version the task is shown in; empty = any.</summary>
    public string MaxVersion { get; set; } = string.Empty;

    /// <summary>The task's name.</summary>
    public PlaytestText Title { get; set; } = new();

    /// <summary>What the task is about, shown above the steps.</summary>
    public PlaytestText Intro { get; set; } = new();

    /// <summary>Roughly how long the task takes, in minutes; 0 = not said.</summary>
    public int Minutes { get; set; }

    /// <summary>A prepared singleplayer world for the task, or null.</summary>
    public PlaytestWorld? World { get; set; }

    /// <summary>The steps, in order.</summary>
    public List<PlaytestStep> Steps { get; set; } = new();

    /// <summary>The fixed questions of the closing report — all mandatory.</summary>
    public List<PlaytestQuestion> Questions { get; set; } = new();

    /// <summary>The developer's thank-you, sent as the answer when the report is confirmed.</summary>
    public PlaytestText Thanks { get; set; } = new();

    /// <summary>True opens a finished task again when <see cref="Rev"/> went up ("please test again").</summary>
    public bool Retest { get; set; }
}

/// <summary>A text of the catalogue in German and English. The catalogue is fetched, so its texts ride in it and
/// not in the build's locale files; every other language reads English.</summary>
public sealed class PlaytestText
{
    /// <summary>German.</summary>
    public string De { get; set; } = string.Empty;

    /// <summary>English — also the text for every other language.</summary>
    public string En { get; set; } = string.Empty;

    /// <summary>True when neither language has a text.</summary>
    public bool IsEmpty => De.Length == 0 && En.Length == 0;

    /// <summary>The text for a locale code (<c>de</c>, <c>de-DE</c> → German; everything else English), falling
    /// back to the other language when the wanted one is missing.</summary>
    public string For(string? localeCode)
    {
        bool german = localeCode is not null && localeCode.StartsWith("de", StringComparison.OrdinalIgnoreCase);
        string wanted = german ? De : En;
        return wanted.Length > 0 ? wanted : german ? En : De;
    }
}

/// <summary>One step of a task: a line the player reads, and optionally how the game can recognise it as done.</summary>
public sealed class PlaytestStep
{
    /// <summary>Stable id within the task.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>What to do.</summary>
    public PlaytestText Text { get; set; } = new();

    /// <summary>How the game recognises the step; null = the player ticks it off.</summary>
    public PlaytestCheck? Check { get; set; }
}

/// <summary>How a step is recognised: an event with an optional argument, counted up to <see cref="Required"/>.
/// The same shape as a story-pack step's objective.</summary>
public sealed class PlaytestCheck
{
    /// <summary>A server game event (<c>GameEventKeys</c>, a <c>prefix*</c> pattern), a client teaching flag
    /// (<c>ui:&lt;id&gt;</c>) or an editor event (<see cref="PlaytestEditorEvents"/>).</summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>The argument that must match — exact, or <c>a|b</c> alternatives; empty = any.</summary>
    public string Arg { get; set; } = string.Empty;

    /// <summary>How often the event must happen.</summary>
    public int Required { get; set; } = 1;

    /// <summary>Whether an event with <paramref name="key"/> and <paramref name="arg"/> counts for this check.</summary>
    public bool Matches(string key, string arg)
    {
        if (!Story.GameEventKeys.Matches(Event, key))
        {
            return false;
        }

        if (Arg.Length == 0)
        {
            return true;
        }

        foreach (string alternative in Arg.Split('|'))
        {
            if (string.Equals(alternative, arg, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>One fixed question of the closing report.</summary>
public sealed class PlaytestQuestion
{
    /// <summary>Stable id within the task.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><see cref="PlaytestQuestionTypes.Stars"/> or <see cref="PlaytestQuestionTypes.Choice"/>.</summary>
    public string Type { get; set; } = PlaytestQuestionTypes.Stars;

    /// <summary>The question.</summary>
    public PlaytestText Text { get; set; } = new();

    /// <summary>The answers of a choice question.</summary>
    public List<PlaytestOption> Options { get; set; } = new();
}

/// <summary>One answer of a choice question.</summary>
public sealed class PlaytestOption
{
    /// <summary>Stable id within the question — what the report carries.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The answer's text.</summary>
    public PlaytestText Text { get; set; } = new();
}

/// <summary>A prepared singleplayer world for a task: the client creates it with these settings on request.</summary>
public sealed class PlaytestWorld
{
    /// <summary>The world seed; 0 = derived from the world name.</summary>
    public long Seed { get; set; }

    /// <summary>The planet type the world starts on (<c>data/planets.json</c>); empty = the usual start.</summary>
    public string StartPlanet { get; set; } = string.Empty;

    /// <summary>One of <see cref="PlaytestWorldPresets.All"/>.</summary>
    public string Preset { get; set; } = PlaytestWorldPresets.Standard;

    /// <summary>The world's tutorial rule (<see cref="PlaytestWorldPresets.TutorialRules"/>); the default keeps the
    /// basic training out of the test.</summary>
    public string Tutorial { get; set; } = PlaytestWorldPresets.TutorialOff;

    /// <summary>Whether the world starts with the game's story (the opening scene, the story's missions). False — the
    /// default — starts it without one: a tester is there for the task, not for a new adventure. A task that tests
    /// the story's beginning switches it on.</summary>
    public bool Story { get; set; }

    /// <summary>World options by name (the create-world panel's sliders) → their index. The client applies only
    /// the names it knows.</summary>
    public Dictionary<string, int> Options { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Commands run once after the world was entered for the first time — only the ones on
    /// <see cref="PlaytestWorldCommands"/>'s list.</summary>
    public List<PlaytestWorldCommand> Commands { get; set; } = new();
}

/// <summary>One start command of a prepared world (<see cref="PlaytestWorldCommands"/>).</summary>
public sealed class PlaytestWorldCommand
{
    /// <summary>The command's name.</summary>
    public string Cmd { get; set; } = string.Empty;

    /// <summary>Its text argument (a clock time, a weather, a place name, an item key).</summary>
    public string Arg { get; set; } = string.Empty;

    /// <summary>The amount of a <see cref="PlaytestWorldCommands.GiveItem"/>.</summary>
    public int Count { get; set; }

    /// <summary>The target of a <see cref="PlaytestWorldCommands.TeleportToLocation"/>.</summary>
    public float X { get; set; }

    /// <summary>See <see cref="X"/>.</summary>
    public float Y { get; set; }

    /// <summary>See <see cref="X"/>.</summary>
    public float Z { get; set; }
}

/// <summary>Where a task takes place.</summary>
public static class PlaytestAreas
{
    /// <summary>In a world.</summary>
    public const string Ingame = "ingame";

    /// <summary>In one of the menu editors.</summary>
    public const string Editor = "editor";

    /// <summary>Whether <paramref name="area"/> is one of the two.</summary>
    public static bool IsKnown(string? area) => area == Ingame || area == Editor;
}

/// <summary>The kinds of fixed questions.</summary>
public static class PlaytestQuestionTypes
{
    /// <summary>One to five stars.</summary>
    public const string Stars = "stars";

    /// <summary>One of the listed answers.</summary>
    public const string Choice = "choice";

    /// <summary>Whether <paramref name="type"/> is one of the two.</summary>
    public static bool IsKnown(string? type) => type == Stars || type == Choice;
}

/// <summary>The presets and tutorial rules a prepared world may name.</summary>
public static class PlaytestWorldPresets
{
    /// <summary>The create-world panel's Standard.</summary>
    public const string Standard = "standard";

    /// <summary>The create-world panel's Peaceful.</summary>
    public const string Peaceful = "peaceful";

    /// <summary>The create-world panel's Sandbox (Creative, every blueprint, every ship, the creative kit).</summary>
    public const string Sandbox = "sandbox";

    /// <summary>Hard Survival.</summary>
    public const string Hard = "hard";

    /// <summary>The tutorial rule "off" — the default of a prepared world.</summary>
    public const string TutorialOff = "off";

    /// <summary>Every preset.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Standard, Peaceful, Sandbox, Hard };

    /// <summary>Every tutorial rule, as the server's <c>--tutorial</c> argument spells them.</summary>
    public static readonly IReadOnlyList<string> TutorialRules = new[] { "auto", "courses", "optin", TutorialOff };
}

/// <summary>The bounds of a catalogue. The reader cuts to them and the validator reports what is over — a fetched
/// text is shown in the game, so nothing about it is trusted.</summary>
public static class PlaytestLimits
{
    /// <summary>The format number this build reads.</summary>
    public const int Schema = 1;

    /// <summary>Tasks in a catalogue.</summary>
    public const int MaxTasks = 40;

    /// <summary>Steps of a task.</summary>
    public const int MaxSteps = 12;

    /// <summary>Fixed questions of a task.</summary>
    public const int MaxQuestions = 6;

    /// <summary>Answers of a choice question.</summary>
    public const int MaxOptions = 6;

    /// <summary>Characters of an id.</summary>
    public const int MaxIdLength = 40;

    /// <summary>Characters of a title or an answer.</summary>
    public const int MaxTitleLength = 60;

    /// <summary>Characters of every other text.</summary>
    public const int MaxTextLength = 400;

    /// <summary>How often a check may ask for its event.</summary>
    public const int MaxRequired = 10000;

    /// <summary>Start commands of a prepared world.</summary>
    public const int MaxCommands = 8;

    /// <summary>World options of a prepared world.</summary>
    public const int MaxWorldOptions = 40;

    /// <summary>The amount of one give command.</summary>
    public const int MaxGiveCount = 999;
}
