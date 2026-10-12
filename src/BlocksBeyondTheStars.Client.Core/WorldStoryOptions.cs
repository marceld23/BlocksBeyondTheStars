// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The "Stories" choices of world creation (#2472, synthesis §5.8): the world's main story, how fast it unfolds (the
    /// story pace, <c>GameRules.StoryDensity</c>) and the tutorial's world rule (<c>GameRules.TutorialPolicy</c>). Unity-free,
    /// so the summary row, the list of main stories and the server arguments are unit-tested; the Unity world-options page
    /// (<c>UiWorldOptions</c>) only draws it and the in-game world rules reuse the names. Defaults are the server's (the
    /// default story, Normal, Auto) and <see cref="ToArgs"/> emits only what differs — an untouched page creates exactly the
    /// world it created before. Players read "Stories" / "Geschichten", never "pack" (that word belongs to the structure
    /// packs on the same screen).
    /// </summary>
    public sealed class WorldStoryOptions
    {
        /// <summary>Story pace steps, index = <c>StoryDensity</c> enum order (the server's words).</summary>
        public static readonly string[] DensitySteps = { "Sparse", "Normal", "Dense" };

        /// <summary>Tutorial rule steps, index = <c>TutorialPolicy</c> enum order (the server's words).</summary>
        public static readonly string[] TutorialSteps = { "Auto", "OptIn", "Off" };

        /// <summary>The CLI words of <see cref="TutorialSteps"/> (<c>--tutorial auto|optin|off</c>).</summary>
        public static readonly string[] TutorialCliWords = { "auto", "optin", "off" };

        /// <summary>Locale keys naming the pace steps (the world-creation slider's words, also the in-game rules row).</summary>
        public static readonly string[] DensityNameKeys = { "ui.worldopt.density_sparse", "ui.worldopt.density_normal", "ui.worldopt.density_dense" };

        /// <summary>Locale keys naming the tutorial steps.</summary>
        public static readonly string[] TutorialNameKeys = { "ui.worldopt.tutorial_auto", "ui.worldopt.tutorial_optin", "ui.worldopt.tutorial_off" };

        /// <summary>Locale keys of the one-line explanation under each tutorial step.</summary>
        public static readonly string[] TutorialHintKeys = { "ui.worldopt.tutorial_auto_hint", "ui.worldopt.tutorial_optin_hint", "ui.worldopt.tutorial_off_hint" };

        /// <summary>Index of the server default pace (<c>Normal</c>).</summary>
        public const int NormalDensity = 1;

        /// <summary>Index of the server default tutorial rule (<c>Auto</c>).</summary>
        public const int AutoTutorial = 0;

        /// <summary>The most main-story choices the page lists (stories, then "None" — the page is a fixed grid; the
        /// default story and "None" always make the cut). WorldOptionsLayoutTests holds the page's sums.</summary>
        public const int MaxChoices = 8;

        /// <summary>The main story: an installed world story's id, or <see cref="StoryRegistry.NoneStoryId"/> for none
        /// (sandbox). Empty reads as the default story.</summary>
        public string StoryId { get; set; } = StoryRegistry.DefaultStoryId;

        /// <summary>Index into <see cref="DensitySteps"/>.</summary>
        public int Density { get; set; } = NormalDensity;

        /// <summary>Index into <see cref="TutorialSteps"/>.</summary>
        public int Tutorial { get; set; } = AutoTutorial;

        /// <summary>No story (sandbox).</summary>
        public bool IsNone => string.Equals(StoryId, StoryRegistry.NoneStoryId, StringComparison.OrdinalIgnoreCase);

        /// <summary>The default story (also an empty id).</summary>
        public bool IsDefaultStory => IsDefault(StoryId);

        /// <summary>Copies every choice from <paramref name="other"/> (a preset keeps the UI bindings alive).</summary>
        public void CopyFrom(WorldStoryOptions other)
        {
            if (other is null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            StoryId = other.StoryId;
            Density = other.Density;
            Tutorial = other.Tutorial;
        }

        /// <summary>Server CLI overrides for the choices — only the non-default ones, each with a leading space (the
        /// format of <c>WorldCreationOptions.ToArgs</c>). The server reads them at world creation only and bakes them into
        /// the save's rules; a later launch never changes them.</summary>
        public string ToArgs()
        {
            var sb = new StringBuilder();
            if (IsNone)
            {
                sb.Append(" --story ").Append(StoryRegistry.NoneStoryId);
            }
            else if (!IsDefaultStory && !StoryRegistry.IsReservedPackId(StoryId) && IsArgSafe(StoryId))
            {
                sb.Append(" --story ").Append(StoryId);
            }

            int density = Clamp(Density, DensitySteps.Length);
            if (density != NormalDensity)
            {
                sb.Append(" --story-density ").Append(DensitySteps[density]);
            }

            int tutorial = Clamp(Tutorial, TutorialSteps.Length);
            if (tutorial != AutoTutorial)
            {
                sb.Append(" --tutorial ").Append(TutorialCliWords[tutorial]);
            }

            return sb.ToString();
        }

        /// <summary>The main-story choices for the page: every installed pack that may be a world's story
        /// (<see cref="StoryRegistry.CanBeWorldStory"/> — no tutorial, no player pack, no beat-less pack), the default story
        /// first, then by priority (high first) and id; "None" always last. Without content (or without the default pack)
        /// the default story is listed anyway — the server installs its built-in copy. At most <see cref="MaxChoices"/>.</summary>
        public static IReadOnlyList<string> MainStoryChoices(IEnumerable<StoryDefinition>? stories)
        {
            var others = (stories ?? Enumerable.Empty<StoryDefinition>())
                .Where(StoryRegistry.CanBeWorldStory)
                .Where(d => !IsDefault(d.Id) && IsArgSafe(d.Id))
                .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(d => d.Priority)
                .ThenBy(d => d.Id, StringComparer.Ordinal)
                .Select(d => d.Id);

            var list = new List<string> { StoryRegistry.DefaultStoryId };
            list.AddRange(others.Take(MaxChoices - 2));
            list.Add(StoryRegistry.NoneStoryId);
            return list;
        }

        /// <summary>Keeps the choices inside what the page can offer: a story id that is not among
        /// <paramref name="choices"/> (a pack removed since) falls back to the default story; the indexes are clamped.</summary>
        public void Normalize(IReadOnlyList<string> choices)
        {
            if (choices is null)
            {
                throw new ArgumentNullException(nameof(choices));
            }

            if (IsDefaultStory || !choices.Any(c => string.Equals(c, StoryId, StringComparison.OrdinalIgnoreCase)))
            {
                StoryId = StoryRegistry.DefaultStoryId;
            }

            Density = Clamp(Density, DensitySteps.Length);
            Tutorial = Clamp(Tutorial, TutorialSteps.Length);
        }

        /// <summary>Whether <paramref name="id"/> is the chosen main story (the default story also matches an empty id).</summary>
        public bool IsChosen(string id)
            => IsDefault(id) ? IsDefaultStory : string.Equals(id, StoryId, StringComparison.OrdinalIgnoreCase);

        /// <summary>The full name of a main-story choice for the list: "None (sandbox)" for none, else the pack's name
        /// (the built-in fallback name for the default story when this client has no text for it, else the id).</summary>
        public static string StoryName(string id, IReadOnlyDictionary<string, StoryDefinition>? stories,
            Func<string, string> localize, Func<string, bool> has)
        {
            if (string.Equals(id, StoryRegistry.NoneStoryId, StringComparison.OrdinalIgnoreCase))
            {
                return localize("ui.worldopt.story_none");
            }

            var def = Find(id, stories);
            if (def != null && has(def.NameKey))
            {
                return localize(def.NameKey);
            }

            return IsDefault(id) ? localize("ui.worldopt.story_default") : id;
        }

        /// <summary>The short name of a choice for the summary row ("VEGA", "None"): the pack's short name, else its name.</summary>
        public static string StoryShortName(string id, IReadOnlyDictionary<string, StoryDefinition>? stories,
            Func<string, string> localize, Func<string, bool> has)
        {
            if (string.Equals(id, StoryRegistry.NoneStoryId, StringComparison.OrdinalIgnoreCase))
            {
                return localize("ui.worldopt.story_none_short");
            }

            var def = Find(id, stories);
            if (def != null && has(def.ShortNameKey))
            {
                return localize(def.ShortNameKey);
            }

            return StoryName(id, stories, localize, has);
        }

        /// <summary>The one line under the main-story list: the chosen story's description, the "no story" line for none,
        /// or empty when the pack has no description this client can show.</summary>
        public static string StoryDescription(string id, IReadOnlyDictionary<string, StoryDefinition>? stories,
            Func<string, string> localize, Func<string, bool> has)
        {
            if (string.Equals(id, StoryRegistry.NoneStoryId, StringComparison.OrdinalIgnoreCase))
            {
                return localize("ui.worldopt.story_none_hint");
            }

            var def = Find(id, stories);
            return def != null && has(def.DescriptionKey) ? localize(def.DescriptionKey) : string.Empty;
        }

        /// <summary>The installed tutorial pack the tutorial rule governs (kind <c>tutorial</c>, highest priority, then id),
        /// or null — the page then shows the rule without the pack's description.</summary>
        public static StoryDefinition? TutorialPack(IEnumerable<StoryDefinition>? stories)
            => (stories ?? Enumerable.Empty<StoryDefinition>())
                .Where(d => string.Equals(d.Kind, StoryRegistry.KindTutorial, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.Priority)
                .ThenBy(d => d.Id, StringComparer.Ordinal)
                .FirstOrDefault();

        /// <summary>The main page's summary value, e.g. "VEGA · Tutorial Auto ›" — the short story name (with the pace in
        /// brackets when it is not Normal and a story runs) and the tutorial rule (<c>ui.worldopt.stories_summary</c>,
        /// <c>ui.worldopt.stories_paced</c>).</summary>
        public string Summary(IReadOnlyDictionary<string, StoryDefinition>? stories, Func<string, string> localize, Func<string, bool> has)
        {
            if (localize is null)
            {
                throw new ArgumentNullException(nameof(localize));
            }

            if (has is null)
            {
                throw new ArgumentNullException(nameof(has));
            }

            string story = StoryShortName(IsDefaultStory ? StoryRegistry.DefaultStoryId : StoryId, stories, localize, has);
            int density = Clamp(Density, DensitySteps.Length);
            if (!IsNone && density != NormalDensity)
            {
                story = Format(localize("ui.worldopt.stories_paced"), story, localize(DensityNameKeys[density]));
            }

            string tutorial = localize(TutorialNameKeys[Clamp(Tutorial, TutorialSteps.Length)]);
            return Format(localize("ui.worldopt.stories_summary"), story, tutorial);
        }

        /// <summary>The <see cref="TutorialSteps"/> index of a server word ("Auto" / "OptIn" / "Off", case-insensitive), or
        /// -1 (an older server sends nothing).</summary>
        public static int TutorialIndex(string? serverWord) => IndexOf(TutorialSteps, serverWord);

        /// <summary>The <see cref="DensitySteps"/> index of a server word ("Sparse" / "Normal" / "Dense"), or -1.</summary>
        public static int DensityIndex(string? serverWord) => IndexOf(DensitySteps, serverWord);

        private static int IndexOf(string[] steps, string? word)
        {
            if (string.IsNullOrEmpty(word))
            {
                return -1;
            }

            for (int i = 0; i < steps.Length; i++)
            {
                if (string.Equals(steps[i], word, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static StoryDefinition? Find(string id, IReadOnlyDictionary<string, StoryDefinition>? stories)
        {
            if (stories is null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            if (stories.TryGetValue(id, out var def))
            {
                return def;
            }

            foreach (var kv in stories)
            {
                if (string.Equals(kv.Key, id, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Value;
                }
            }

            return null;
        }

        private static bool IsDefault(string? id)
            => string.IsNullOrEmpty(id) || string.Equals(id, StoryRegistry.DefaultStoryId, StringComparison.OrdinalIgnoreCase);

        /// <summary>A pack id that can ride on the server command line unquoted (letters, digits, '_' and '-').</summary>
        private static bool IsArgSafe(string id)
            => !string.IsNullOrEmpty(id) && id.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');

        private static int Clamp(int value, int count) => value < 0 ? 0 : value >= count ? count - 1 : value;

        /// <summary>string.Format that never throws on a broken translation (missing placeholder or stray brace).</summary>
        private static string Format(string template, params object[] args)
        {
            try
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, args);
            }
            catch (FormatException)
            {
                return string.Join(" · ", args);
            }
        }
    }
}
