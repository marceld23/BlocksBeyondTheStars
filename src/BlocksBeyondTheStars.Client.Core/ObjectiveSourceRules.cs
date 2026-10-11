// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How the client reads VEGA's objective chip (#2462). The server now says where the objective comes from
    /// (<see cref="ShipAiLine.ObjectiveSource"/>) and whether the tutorial still runs
    /// (<see cref="ShipAiLine.TutorialActive"/>); before, the client guessed both from the <c>story.obj.</c> key
    /// prefix. That guess broke as soon as the finale objective sat on the chip while the lessons were unfinished: the
    /// Settings button turned from "Skip tutorial" into "Restart tutorial", which wipes the lessons (B03), and the
    /// hints mute hid the finale. An older server sends neither field — the prefix guess stays for it, now with the
    /// finale key recognised so it is never muted. Unity-free so the rules are covered by plain .NET tests.
    /// </summary>
    public static class ObjectiveSourceRules
    {
        /// <summary>The key prefix of the post-tutorial story objectives (the old way to recognise them).</summary>
        public const string StoryKeyPrefix = "story.obj.";

        /// <summary>The finale objective's key ("confront the Guardian core").</summary>
        public const string FinaleKey = "story.obj.finale";

        /// <summary>The chip's source: the server's when it sent one, otherwise the guess an older server needs —
        /// nothing for an empty chip, the finale for its key, story coaching for any other <c>story.obj.</c> key and the
        /// tutorial for everything else.</summary>
        public static string Effective(string? source, string? objectiveKey)
        {
            if (!string.IsNullOrEmpty(source))
            {
                return source!;
            }

            if (string.IsNullOrEmpty(objectiveKey))
            {
                return ObjectiveSources.None;
            }

            if (string.Equals(objectiveKey, FinaleKey, StringComparison.Ordinal))
            {
                return ObjectiveSources.Finale;
            }

            return objectiveKey!.StartsWith(StoryKeyPrefix, StringComparison.Ordinal)
                ? ObjectiveSources.Story
                : ObjectiveSources.Tutorial;
        }

        /// <summary>Whether the VEGA hints switch hides the chip. Only the optional story coaching follows it; the
        /// tutorial, the finale, a mission — and any source this client does not know yet — always show.</summary>
        public static bool HiddenByHintsMute(string? effectiveSource, bool hintsOn)
            => !hintsOn && string.Equals(effectiveSource, ObjectiveSources.Story, StringComparison.Ordinal);

        /// <summary>Whether the player's tutorial is running — the Settings button offers "Skip tutorial" then and
        /// "Restart tutorial" otherwise. A server that sends the source says it outright, whatever the chip shows; for
        /// an older one any non-story chip still means a lesson.</summary>
        public static bool TutorialRunning(string? source, string? objectiveKey, bool tutorialActive)
        {
            if (!string.IsNullOrEmpty(source))
            {
                return tutorialActive;
            }

            return !string.IsNullOrEmpty(objectiveKey)
                && !objectiveKey!.StartsWith(StoryKeyPrefix, StringComparison.Ordinal);
        }
    }
}
