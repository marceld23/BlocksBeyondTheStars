// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Playtests;

namespace BlocksBeyondTheStars.Client.Playtests
{
    /// <summary>Why a task's test world cannot be created by this build.</summary>
    public enum PlaytestWorldRefusal
    {
        /// <summary>It can.</summary>
        None,

        /// <summary>The task brings no world.</summary>
        NoWorld,

        /// <summary>The task is for an editor — editors need no world.</summary>
        EditorTask,

        /// <summary>The world starts on a planet type this build does not know. Creating it anyway would silently
        /// start on another planet and save that — so it is refused.</summary>
        UnknownPlanet,
    }

    /// <summary>
    /// A task's prepared world, checked and ready for the client to create: the name, the seed, the start planet, the
    /// preset, the world options this build knows, and the start commands from the fixed list. The catalogue is
    /// fetched text, so everything is checked here once more — a name the build does not know is dropped, a command
    /// off the list never appears in <see cref="Commands"/>.
    /// </summary>
    public sealed class PlaytestWorldPlan
    {
        /// <summary>The prefix of every test world's name: the client only ever runs start commands in a world whose
        /// name starts with it AND that it created itself for a task.</summary>
        public const string WorldNamePrefix = "Testpilot - ";

        /// <summary>
        /// The world options a catalogue may set — the create-world panel's sliders by the names of the client's
        /// options. A name that is not here is ignored. The value is the slider's index, cut to
        /// <see cref="MaxOptionIndex"/>.
        /// </summary>
        public static readonly IReadOnlyList<string> AllowedOptions = new[]
        {
            "Creatures", "PlanetEnemies", "SpaceNpcs", "Ufos", "Bandits", "Flora", "Ore", "Settlements", "Wrecks", "Vaults",
            "Stations", "Wormholes", "DebrisFields", "Derelicts", "GiantWrecks", "Exotic", "Oxygen", "Hunger", "Hazards",
            "DeathPenalty", "UniverseSize",
        };

        /// <summary>The highest slider index a catalogue may ask for.</summary>
        public const int MaxOptionIndex = 5;

        public PlaytestWorldRefusal Refusal { get; private set; }

        /// <summary>The name the world is created under (and found again by).</summary>
        public string WorldName { get; private set; } = string.Empty;

        public long Seed { get; private set; }

        public string StartPlanet { get; private set; } = string.Empty;

        /// <summary>One of <see cref="PlaytestWorldPresets.All"/>.</summary>
        public string Preset { get; private set; } = PlaytestWorldPresets.Standard;

        /// <summary>One of <see cref="PlaytestWorldPresets.TutorialRules"/>.</summary>
        public string Tutorial { get; private set; } = PlaytestWorldPresets.TutorialOff;

        /// <summary>Whether the world starts with the game's story; false (the default) starts it without one.</summary>
        public bool Story { get; private set; }

        /// <summary>The options this build applies, by name.</summary>
        public Dictionary<string, int> Options { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The start commands, every one of them on the fixed list.</summary>
        public List<PlaytestWorldCommand> Commands { get; } = new List<PlaytestWorldCommand>();

        /// <summary>
        /// Plans the test world of <paramref name="task"/>. <paramref name="knowsPlanet"/> answers whether this build's
        /// content has a planet type (<c>data/planets.json</c>); <paramref name="locale"/> picks the title in the name.
        /// </summary>
        public static PlaytestWorldPlan For(PlaytestTask task, Func<string, bool> knowsPlanet, string? locale)
        {
            var plan = new PlaytestWorldPlan();
            if (task.Area == PlaytestAreas.Editor)
            {
                plan.Refusal = PlaytestWorldRefusal.EditorTask;
                return plan;
            }

            if (task.World is not { } world)
            {
                plan.Refusal = PlaytestWorldRefusal.NoWorld;
                return plan;
            }

            if (world.StartPlanet.Length > 0 && !knowsPlanet(world.StartPlanet))
            {
                plan.Refusal = PlaytestWorldRefusal.UnknownPlanet;
                return plan;
            }

            plan.WorldName = NameFor(task, locale);
            plan.Seed = world.Seed;
            plan.StartPlanet = world.StartPlanet;
            plan.Preset = Contains(PlaytestWorldPresets.All, world.Preset) ? world.Preset : PlaytestWorldPresets.Standard;
            plan.Tutorial = Contains(PlaytestWorldPresets.TutorialRules, world.Tutorial) ? world.Tutorial : PlaytestWorldPresets.TutorialOff;
            plan.Story = world.Story;
            foreach (var option in world.Options)
            {
                if (Contains(AllowedOptions, option.Key))
                {
                    plan.Options[option.Key] = Math.Max(0, Math.Min(option.Value, MaxOptionIndex));
                }
            }

            foreach (var command in world.Commands)
            {
                if (plan.Commands.Count < PlaytestLimits.MaxCommands && PlaytestWorldCommands.Problem(command) is null)
                {
                    plan.Commands.Add(command);
                }
            }

            return plan;
        }

        /// <summary>The world name for a task: the prefix and the task's title, with everything a folder name cannot
        /// hold replaced (the save folder is named after the world).</summary>
        public static string NameFor(PlaytestTask task, string? locale)
        {
            string title = task.Title.For(locale);
            var chars = (title.Length > 0 ? title : task.Id).ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (char.IsControl(c) || "\\/:*?\"<>|".IndexOf(c) >= 0)
                {
                    chars[i] = ' ';
                }
            }

            string name = WorldNamePrefix + new string(chars).Trim();
            return name.Length <= 60 ? name : name.Substring(0, 60).TrimEnd();
        }

        /// <summary>Whether <paramref name="worldName"/> is named like a test world.</summary>
        public static bool IsTestWorldName(string? worldName)
            => worldName is not null && worldName.StartsWith(WorldNamePrefix, StringComparison.Ordinal);

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

    /// <summary>
    /// Where a DEVELOPMENT build finds a local report inbox. A released build always talks to the official inbox with
    /// the key baked into it; a local build has no key and therefore no Test Pilot page — unless the developer points
    /// it at an inbox of their own through the environment (<c>BBS_REPORTS_DEV_URL</c> and <c>BBS_REPORTS_DEV_KEY</c>).
    /// Only a build whose version is not a release version reads these, and only an https address or a loopback one
    /// is accepted, so a player's machine can never be redirected this way.
    /// </summary>
    public static class PlaytestDevInbox
    {
        /// <summary>The environment variable with the inbox's base address (<c>http://localhost:31418</c>).</summary>
        public const string UrlVariable = "BBS_REPORTS_DEV_URL";

        /// <summary>The environment variable with that inbox's write key.</summary>
        public const string KeyVariable = "BBS_REPORTS_DEV_KEY";

        /// <summary>
        /// The ingest endpoint (<c>…/api/bugreport</c>) and key of the developer's own inbox, when
        /// <paramref name="gameVersion"/> is a development build and both variables are set and acceptable.
        /// </summary>
        public static bool TryRead(string? gameVersion, Func<string, string?> environment, out string ingestEndpoint, out string apiKey)
        {
            ingestEndpoint = apiKey = string.Empty;
            if (PlaytestVersion.TryParse(gameVersion, out var version) && version.Major >= 2000)
            {
                return false; // a release build: the official inbox, always
            }

            string url = (environment(UrlVariable) ?? string.Empty).Trim().TrimEnd('/');
            string key = (environment(KeyVariable) ?? string.Empty).Trim();
            bool acceptable = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                              || url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
                              || url.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase);
            if (!acceptable || key.Length == 0)
            {
                return false;
            }

            ingestEndpoint = url + "/api/bugreport";
            apiKey = key;
            return true;
        }
    }
}
