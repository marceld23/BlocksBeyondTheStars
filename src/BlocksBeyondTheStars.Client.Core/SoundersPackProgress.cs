// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Sounders;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How far the local player's Sounders pack has come, read from the Journal's <see cref="StoryPackStates"/> the server
    /// pushes (#3367) — for the views that only change looks with the story: the faint Quali shimmer at space anomalies from
    /// chapter 5 (the Echoes) on, and the current task the free-play melody bar names (#3679, <see cref="CurrentTaskKey"/>).
    /// Render only, never a gate on anything the server decides.
    /// </summary>
    public static class SoundersPackProgress
    {
        /// <summary>The chapter the Quali shimmer waits for: 5, the Echoes (chapter id <see cref="EchoesChapterId"/>).</summary>
        public const int QualiShimmerChapter = 5;

        /// <summary>The id of chapter 5 in the pack's <c>story.json</c>.</summary>
        public const string EchoesChapterId = "s5";

        /// <summary>
        /// Whether the player's pack <paramref name="packId"/> has reached chapter <paramref name="chapterId"/> (1-based
        /// <paramref name="chapterNumber"/> when no chapter carries that id): that chapter or a later one is running, done,
        /// skipped or replayed, or the whole pack is through. False while the pack is off, switched off or not started.
        /// </summary>
        public static bool ReachedChapter(StoryPackStates? states, string packId, string chapterId, int chapterNumber)
        {
            var pack = FindPack(states, packId);
            if (pack == null || !pack.Enabled || pack.OptedOut || !pack.Started)
            {
                return false;
            }

            if (pack.Done)
            {
                return true;
            }

            int from = Array.FindIndex(pack.Chapters, c => string.Equals(c.Id, chapterId, StringComparison.Ordinal));
            if (from < 0)
            {
                from = chapterNumber - 1;
            }

            for (int i = Math.Max(0, from); i < pack.Chapters.Length; i++)
            {
                if (pack.Chapters[i].State is StoryChapterStates.Active or StoryChapterStates.Done or StoryChapterStates.Skipped
                    or StoryChapterStates.Replay)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether a space anomaly shows the faint Quali shimmer for this player (chapter 5 of the Sounders pack).</summary>
        public static bool QualiShimmer(StoryPackStates? states)
            => ReachedChapter(states, SoundersIds.PackId, EchoesChapterId, QualiShimmerChapter);

        /// <summary>
        /// #3679: the chip text key of the Sounders task the player is at — the pack's step on VEGA's chip, else its first
        /// open task (an open extra last) — or null: the pack is off, switched off, not started, through, or has no task with
        /// a chip text. The free-play melody bar (the Resonance Blaster in hand, at a rune stone) shows it instead of only
        /// the key legend, so a child sees what the story wants next.
        /// </summary>
        public static string? CurrentTaskKey(StoryPackStates? states)
        {
            var pack = FindPack(states, SoundersIds.PackId);
            if (pack == null || !pack.Enabled || pack.OptedOut || !pack.Started || pack.Done)
            {
                return null;
            }

            NetStoryStep? best = null;
            foreach (var step in pack.Steps ?? Array.Empty<NetStoryStep>())
            {
                if (step == null || string.IsNullOrEmpty(step.ChipKey))
                {
                    continue;
                }

                if (step.OnChip)
                {
                    return step.ChipKey;
                }

                if (best == null || (best.Extra && !step.Extra) || (best.Extra == step.Extra && step.Index < best.Index))
                {
                    best = step;
                }
            }

            return best?.ChipKey;
        }

        private static NetStoryPack? FindPack(StoryPackStates? states, string packId)
        {
            foreach (var p in states?.Packs ?? Array.Empty<NetStoryPack>())
            {
                if (p != null && string.Equals(p.Id, packId, StringComparison.Ordinal))
                {
                    return p;
                }
            }

            return null;
        }
    }
}
