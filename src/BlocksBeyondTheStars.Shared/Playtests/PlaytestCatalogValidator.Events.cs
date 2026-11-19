// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Shared.Playtests;

/// <summary>
/// Since which release a game event exists — for the events that are newer than the Test Pilot page itself (#3833).
/// The first client with the page (<see cref="FirstClient"/>) shows a step that waits for a server event without its
/// "Done" button, and its own server never raises an event it does not know: such a step could never be closed there.
/// So a task that waits for a newer event must not be shown to that client — it needs a <c>minVersion</c>. Later
/// clients take an unknown check off by themselves (<c>PlaytestTracker.SetCatalog</c>); the rule stays for as long as
/// the first client is out there.
/// <para>A new game event a playtest may wait for gets a row here, with the first release that can carry it.</para>
/// </summary>
public static class PlaytestEventVersions
{
    /// <summary>The first release with the Test Pilot page — the one client that cannot close a step whose event it
    /// does not know.</summary>
    public const string FirstClient = "2026.11.23";

    /// <summary>The first release that can carry the manoeuvre, throw, emote and fallen events (#3831, #3832): the one
    /// after <see cref="FirstClient"/>, whatever its number turns out to be.</summary>
    public const string FeelEvents = "2026.11.24";

    private static readonly Dictionary<string, string> Since = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [GameEventKeys.ShipRolled] = FeelEvents,
        [GameEventKeys.ShipBoosted] = FeelEvents,
        [GameEventKeys.ShipEvaded] = FeelEvents,
        [GameEventKeys.CarryLifted] = FeelEvents,
        [GameEventKeys.ThrowAny] = FeelEvents,
        [GameEventKeys.ThrowHit] = FeelEvents,
        [GameEventKeys.EmotePlayed] = FeelEvents,
        [GameEventKeys.PlayerFallen] = FeelEvents,
        [GameEventKeys.PlayerRevived] = FeelEvents,
    };

    /// <summary>The release <paramref name="key"/> exists from, or null for an event the first client knows already.</summary>
    public static string? SinceVersion(string key) => Since.TryGetValue(key, out string? version) ? version : null;

    /// <summary>
    /// The lowest <c>minVersion</c> a task needs for a step that waits for <paramref name="eventPattern"/> (a key, or
    /// a <c>prefix*</c> pattern), or null when none is needed: the pattern matches an event the first client knows, so
    /// the step can close there. A pattern that only matches newer events needs the earliest of them.
    /// </summary>
    public static string? RequiredFor(string eventPattern)
    {
        string? lowest = null;
        foreach (string key in GameEventKeys.All)
        {
            if (!GameEventKeys.Matches(eventPattern, key))
            {
                continue;
            }

            if (SinceVersion(key) is not { } since)
            {
                return null;
            }

            if (lowest is null || Lower(since, lowest))
            {
                lowest = since;
            }
        }

        return lowest;
    }

    private static bool Lower(string a, string b)
        => PlaytestVersion.TryParse(a, out var va) && PlaytestVersion.TryParse(b, out var vb) && PlaytestVersion.Compare(va, vb) < 0;
}

/// <summary>The validator's rules about events: which release knows one, and which world a step can happen in.</summary>
public static partial class PlaytestCatalogValidator
{
    /// <summary>Events that cannot happen in a sandbox world: it runs in Creative mode, where nothing hurts the player
    /// and hostiles do not see them.</summary>
    private static readonly string[] NeedsAWorldThatHurts =
    {
        GameEventKeys.PlayerRespawned, GameEventKeys.PlayerFallen, GameEventKeys.PlayerRevived, GameEventKeys.ShipEvaded,
    };

    /// <summary>Events of a fight. In a sandbox world the fight is one-sided, so a step there tells nothing about it.</summary>
    private static readonly string[] Fights =
    {
        GameEventKeys.DefeatAny, GameEventKeys.GuardianGiantDefeated, GameEventKeys.ThrowHit,
    };

    /// <summary>The <see cref="GameEventKeys.DefeatAny"/> arguments a peaceful world has nothing for.</summary>
    private static readonly string[] PeacefulHasNone =
    {
        GameEventKeys.DefeatMachine, GameEventKeys.DefeatBandit, GameEventKeys.DefeatSpaceMachine, GameEventKeys.DefeatRaider,
    };

    /// <summary>The world options that bring hostiles back into a peaceful world.</summary>
    private static readonly string[] HostileOptions = { "PlanetEnemies", "Bandits", "SpaceNpcs", "Ufos" };

    /// <summary>#3833: a step that waits for an event newer than the first Test Pilot client needs a task that client
    /// is not shown.</summary>
    private static void CheckEventVersions(PlaytestTask task, Action<string, string> error)
    {
        bool hasMin = PlaytestVersion.TryParse(task.MinVersion, out var min);
        for (int i = 0; i < task.Steps.Count; i++)
        {
            if (task.Steps[i].Check is not { Event.Length: > 0 } check
                || PlaytestEventVersions.RequiredFor(check.Event) is not { } required
                || !PlaytestVersion.TryParse(required, out var needed))
            {
                continue;
            }

            if (!hasMin || PlaytestVersion.Compare(min, needed) < 0)
            {
                error($"steps[{i}].check.event",
                    $"'{check.Event}' exists from v{required} on — set minVersion to {required} or later, or leave the check out: "
                    + $"the v{PlaytestEventVersions.FirstClient} client can never close a step that waits for an event it does not know");
            }
        }
    }

    /// <summary>
    /// #3852: in a world the HUD shows the first open step, so a step the tester has to tick off holds the card there
    /// while later steps are recognised behind it. An in-game task therefore lets the game recognise the doing first
    /// and asks the judging at the end. An editor task is exempt: its whole list is on the screen, buttons included.
    /// </summary>
    private static void CheckStepOrder(PlaytestTask task, Action<string, string> warn)
    {
        if (task.Area != PlaytestAreas.Ingame)
        {
            return;
        }

        int lastRecognised = -1;
        for (int i = 0; i < task.Steps.Count; i++)
        {
            if (task.Steps[i].Check is { Event.Length: > 0 })
            {
                lastRecognised = i;
            }
        }

        for (int i = 0; i < lastRecognised; i++)
        {
            if (task.Steps[i].Check is null)
            {
                warn($"steps[{i}]", $"is ticked off by the tester but stands before steps[{lastRecognised}], which the game recognises — "
                                    + "the HUD shows the first open step and would stay on this one. Recognise the doing first, ask the judging at the end");
            }
        }
    }

    /// <summary>#3837: a step that the task's own test world cannot bring about.</summary>
    private static void CheckWorldFitsSteps(PlaytestTask task, PlaytestWorld world, Action<string, string> warn)
    {
        bool sandbox = world.Preset == PlaytestWorldPresets.Sandbox;
        bool peaceful = world.Preset == PlaytestWorldPresets.Peaceful && !TurnsHostilesOn(world);
        if (!sandbox && !peaceful)
        {
            return;
        }

        for (int i = 0; i < task.Steps.Count; i++)
        {
            if (task.Steps[i].Check is not { Event.Length: > 0 } check)
            {
                continue;
            }

            string where = $"steps[{i}].check.event";
            if (sandbox && MatchesAny(check.Event, NeedsAWorldThatHurts))
            {
                warn(where, $"'{check.Event}' cannot happen in a sandbox world: nothing hurts the player there and hostiles do not see them");
            }
            else if (sandbox && MatchesAny(check.Event, Fights))
            {
                warn(where, $"'{check.Event}' in a sandbox world is a one-sided fight — hostiles do not see the player, so it says nothing about how hard it is");
            }
            else if (peaceful && (GameEventKeys.Matches(check.Event, GameEventKeys.ShipEvaded) || OnlyHostileDefeats(check)))
            {
                warn(where, $"'{check.Event}' needs machines, robbers or hostile ships — a peaceful world has none (turn them on in world.options, or use the standard preset)");
            }
        }
    }

    private static bool TurnsHostilesOn(PlaytestWorld world)
    {
        foreach (string option in HostileOptions)
        {
            if (world.Options.TryGetValue(option, out int level) && level > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAny(string eventPattern, string[] keys)
    {
        foreach (string key in keys)
        {
            if (GameEventKeys.Matches(eventPattern, key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A <c>defeat:any</c> check whose argument list names hostiles only (<c>machine|raider</c>) — an animal,
    /// which a peaceful world has, would not close it.</summary>
    private static bool OnlyHostileDefeats(PlaytestCheck check)
    {
        if (check.Event != GameEventKeys.DefeatAny || check.Arg.Length == 0)
        {
            return false;
        }

        foreach (string arg in check.Arg.Split('|'))
        {
            if (Array.IndexOf(PeacefulHasNone, arg.Trim()) < 0)
            {
                return false;
            }
        }

        return true;
    }
}
