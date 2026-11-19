// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Shared.Playtests;

/// <summary>One finding of <see cref="PlaytestCatalogValidator"/>.</summary>
public sealed class PlaytestProblem
{
    /// <summary>Creates a finding.</summary>
    public PlaytestProblem(bool isError, string where, string message)
    {
        IsError = isError;
        Where = where;
        Message = message;
    }

    /// <summary>True: the task must not be saved. False: a warning the author may accept.</summary>
    public bool IsError { get; }

    /// <summary>Where in the task (<c>steps[2].check</c>).</summary>
    public string Where { get; }

    /// <summary>What is wrong, in English (the admin page and the sync tool show it to the author).</summary>
    public string Message { get; }

    /// <inheritdoc />
    public override string ToString() => (IsError ? "error" : "warning") + " " + Where + ": " + Message;
}

/// <summary>
/// The one rule set for a playtest task, used by the inbox's admin page, its scriptable routes and the tests. It
/// checks a task read <see cref="PlaytestReadMode.AsWritten"/>, so it can name everything the safe reader would
/// silently cut or drop. An event name the build does not know is only a WARNING: a task may be written for a
/// release whose event the inbox's build does not ship yet, and the client then lets the player tick the step off.
/// An event the build knows to be newer than the first Test Pilot client is an ERROR without a matching
/// <c>minVersion</c> (<see cref="PlaytestEventVersions"/>): that client cannot close such a step.
/// </summary>
public static partial class PlaytestCatalogValidator
{
    /// <summary>Every finding for <paramref name="task"/>; empty = fine.</summary>
    public static List<PlaytestProblem> Validate(PlaytestTask task)
    {
        var problems = new List<PlaytestProblem>();
        void Error(string where, string message) => problems.Add(new PlaytestProblem(true, where, message));
        void Warn(string where, string message) => problems.Add(new PlaytestProblem(false, where, message));

        if (!IsWellFormedId(task.Id))
        {
            Error("id", $"needs 1 to {PlaytestLimits.MaxIdLength} characters of a-z, 0-9 and '-'");
        }

        if (task.Rev < 1)
        {
            Error("rev", "must be 1 or more");
        }

        if (!PlaytestAreas.IsKnown(task.Area))
        {
            Error("area", $"must be '{PlaytestAreas.Ingame}' or '{PlaytestAreas.Editor}'");
        }
        else if (task.Area == PlaytestAreas.Editor && !PlaytestEditorEvents.IsKnownEditor(task.Editor))
        {
            Error("editor", "an editor task names its editor: " + string.Join(", ", PlaytestEditorEvents.Editors));
        }
        else if (task.Area == PlaytestAreas.Ingame && task.Editor.Length > 0)
        {
            Warn("editor", "is ignored for an in-game task");
        }

        CheckVersions(task, Error);
        CheckText(task.Title, "title", PlaytestLimits.MaxTitleLength, required: true, Error);
        CheckText(task.Intro, "intro", PlaytestLimits.MaxTextLength, required: true, Error);
        CheckText(task.Thanks, "thanks", PlaytestLimits.MaxTextLength, required: false, Error);
        if (task.Minutes < 0 || task.Minutes > 600)
        {
            Error("minutes", "must be between 0 and 600");
        }

        CheckSteps(task, Error, Warn);
        CheckEventVersions(task, Error);
        CheckStepOrder(task, Warn);
        CheckQuestions(task, Error);
        if (task.World is { } world)
        {
            CheckWorld(task, world, Error, Warn);
            CheckWorldFitsSteps(task, world, Warn);
        }

        return problems;
    }

    /// <summary>Whether any finding of <paramref name="problems"/> forbids saving.</summary>
    public static bool HasErrors(IEnumerable<PlaytestProblem> problems)
    {
        foreach (var p in problems)
        {
            if (p.IsError)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An id of the catalogue: 1 to <see cref="PlaytestLimits.MaxIdLength"/> characters of <c>a-z</c>,
    /// <c>0-9</c> and <c>-</c>, neither starting nor ending with the dash. It is used in a URL and as a file name.</summary>
    public static bool IsWellFormedId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id!.Length > PlaytestLimits.MaxIdLength || id[0] == '-' || id[id.Length - 1] == '-')
        {
            return false;
        }

        foreach (char c in id)
        {
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether <paramref name="eventPattern"/> names an event some build raises: a server game event
    /// (exact or a <c>prefix*</c> pattern), a client teaching flag or an editor event.</summary>
    public static bool IsKnownEvent(string eventPattern)
    {
        if (string.IsNullOrEmpty(eventPattern))
        {
            return false;
        }

        if (GameEventKeys.IsUiEvent(eventPattern))
        {
            return GameEventKeys.IsKnownUiEventId(eventPattern.Substring(GameEventKeys.UiPrefix.Length));
        }

        foreach (string key in PlaytestEditorEvents.All)
        {
            if (GameEventKeys.Matches(eventPattern, key))
            {
                return true;
            }
        }

        foreach (string key in GameEventKeys.All)
        {
            if (GameEventKeys.Matches(eventPattern, key))
            {
                return true;
            }
        }

        return false;
    }

    private static void CheckVersions(PlaytestTask task, Action<string, string> error)
    {
        bool hasMin = task.MinVersion.Length > 0;
        bool hasMax = task.MaxVersion.Length > 0;
        (int, int, int) min = default, max = default;
        if (hasMin && !PlaytestVersion.TryParse(task.MinVersion, out min))
        {
            error("minVersion", "is not a version like 2026.11.21");
            hasMin = false;
        }

        if (hasMax && !PlaytestVersion.TryParse(task.MaxVersion, out max))
        {
            error("maxVersion", "is not a version like 2026.11.21");
            hasMax = false;
        }

        if (hasMin && hasMax && PlaytestVersion.Compare(min, max) > 0)
        {
            error("maxVersion", "is lower than minVersion — no build would ever show the task");
        }
    }

    private static void CheckText(PlaytestText text, string where, int maxLength, bool required, Action<string, string> error)
    {
        if (required || !text.IsEmpty)
        {
            if (text.De.Trim().Length == 0)
            {
                error(where + ".de", "the German text is missing");
            }

            if (text.En.Trim().Length == 0)
            {
                error(where + ".en", "the English text is missing");
            }
        }

        CheckOneText(text.De, where + ".de", maxLength, error);
        CheckOneText(text.En, where + ".en", maxLength, error);
    }

    private static void CheckOneText(string text, string where, int maxLength, Action<string, string> error)
    {
        if (text.Length > maxLength)
        {
            error(where, $"is {text.Length} characters long, the limit is {maxLength}");
        }

        if (PlaytestTextCleaner.HasMarkupOrLink(text))
        {
            error(where, "must not contain '<', '>' or a link — the game would remove it");
        }
    }

    private static void CheckSteps(PlaytestTask task, Action<string, string> error, Action<string, string> warn)
    {
        if (task.Steps.Count == 0)
        {
            error("steps", "a task needs at least one step");
        }
        else if (task.Steps.Count > PlaytestLimits.MaxSteps)
        {
            error("steps", $"has {task.Steps.Count} steps, the limit is {PlaytestLimits.MaxSteps}");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < task.Steps.Count; i++)
        {
            var step = task.Steps[i];
            string where = $"steps[{i}]";
            if (!IsWellFormedId(step.Id))
            {
                error(where + ".id", "is missing or not a-z, 0-9, '-'");
            }
            else if (!seen.Add(step.Id))
            {
                error(where + ".id", $"'{step.Id}' is used twice");
            }

            CheckText(step.Text, where + ".text", PlaytestLimits.MaxTextLength, required: true, error);
            if (step.Check is not { } check)
            {
                continue;
            }

            if (check.Event.Length == 0)
            {
                error(where + ".check.event", "is missing (leave 'check' out for a step the player ticks off)");
            }
            else if (!IsKnownEvent(check.Event))
            {
                warn(where + ".check.event", $"'{check.Event}' is not an event this build knows — a client that does not know it either lets the player tick the step off (v{PlaytestEventVersions.FirstClient} cannot: give the task a minVersion above it)");
            }
            else if (task.Area == PlaytestAreas.Editor && !PlaytestEditorEvents.IsEditorEvent(check.Event))
            {
                warn(where + ".check.event", "an editor runs without a world, so only 'editor:' events can be recognised there");
            }

            if (check.Required < 1 || check.Required > PlaytestLimits.MaxRequired)
            {
                error(where + ".check.required", $"must be between 1 and {PlaytestLimits.MaxRequired}");
            }
        }
    }

    private static void CheckQuestions(PlaytestTask task, Action<string, string> error)
    {
        if (task.Questions.Count > PlaytestLimits.MaxQuestions)
        {
            error("questions", $"has {task.Questions.Count} questions, the limit is {PlaytestLimits.MaxQuestions}");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < task.Questions.Count; i++)
        {
            var question = task.Questions[i];
            string where = $"questions[{i}]";
            if (!IsWellFormedId(question.Id))
            {
                error(where + ".id", "is missing or not a-z, 0-9, '-'");
            }
            else if (!seen.Add(question.Id))
            {
                error(where + ".id", $"'{question.Id}' is used twice");
            }

            CheckText(question.Text, where + ".text", PlaytestLimits.MaxTextLength, required: true, error);
            if (!PlaytestQuestionTypes.IsKnown(question.Type))
            {
                error(where + ".type", $"must be '{PlaytestQuestionTypes.Stars}' or '{PlaytestQuestionTypes.Choice}'");
                continue;
            }

            if (question.Type == PlaytestQuestionTypes.Stars)
            {
                if (question.Options.Count > 0)
                {
                    error(where + ".options", "a stars question has no options");
                }

                continue;
            }

            CheckOptions(question, where, error);
        }
    }

    private static void CheckOptions(PlaytestQuestion question, string where, Action<string, string> error)
    {
        if (question.Options.Count < 2 || question.Options.Count > PlaytestLimits.MaxOptions)
        {
            error(where + ".options", $"a choice question needs 2 to {PlaytestLimits.MaxOptions} options");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int k = 0; k < question.Options.Count; k++)
        {
            var option = question.Options[k];
            string optionWhere = $"{where}.options[{k}]";
            if (!IsWellFormedId(option.Id))
            {
                error(optionWhere + ".id", "is missing or not a-z, 0-9, '-'");
            }
            else if (!seen.Add(option.Id))
            {
                error(optionWhere + ".id", $"'{option.Id}' is used twice");
            }

            CheckText(option.Text, optionWhere + ".text", PlaytestLimits.MaxTitleLength, required: true, error);
        }
    }

    private static void CheckWorld(PlaytestTask task, PlaytestWorld world, Action<string, string> error, Action<string, string> warn)
    {
        if (task.Area == PlaytestAreas.Editor)
        {
            warn("world", "is ignored for an editor task");
        }

        if (!Contains(PlaytestWorldPresets.All, world.Preset))
        {
            error("world.preset", "must be one of: " + string.Join(", ", PlaytestWorldPresets.All));
        }

        if (!Contains(PlaytestWorldPresets.TutorialRules, world.Tutorial))
        {
            error("world.tutorial", "must be one of: " + string.Join(", ", PlaytestWorldPresets.TutorialRules));
        }

        if (world.StartPlanet.Length > 0 && !IsPlanetKey(world.StartPlanet))
        {
            error("world.startPlanet", "must be a planet type key (a-z, 0-9, _)");
        }

        if (world.Options.Count > PlaytestLimits.MaxWorldOptions)
        {
            error("world.options", $"has {world.Options.Count} entries, the limit is {PlaytestLimits.MaxWorldOptions}");
        }

        if (world.Commands.Count > PlaytestLimits.MaxCommands)
        {
            error("world.commands", $"has {world.Commands.Count} commands, the limit is {PlaytestLimits.MaxCommands}");
        }

        for (int i = 0; i < world.Commands.Count; i++)
        {
            if (PlaytestWorldCommands.Problem(world.Commands[i]) is { } problem)
            {
                error($"world.commands[{i}]", problem);
            }
        }
    }

    private static bool IsPlanetKey(string key)
    {
        if (key.Length > PlaytestLimits.MaxIdLength)
        {
            return false;
        }

        foreach (char c in key)
        {
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(IReadOnlyList<string> list, string value)
    {
        foreach (string item in list)
        {
            if (string.Equals(item, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
