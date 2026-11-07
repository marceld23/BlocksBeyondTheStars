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
    /// chapter 5 (the Echoes) on. Render only, never a gate on anything the server decides.
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
            NetStoryPack? pack = null;
            foreach (var p in states?.Packs ?? Array.Empty<NetStoryPack>())
            {
                if (string.Equals(p.Id, packId, StringComparison.Ordinal))
                {
                    pack = p;
                    break;
                }
            }

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
    }
}
