// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Core
{
    /// <summary>How one chapter shows in the Story tab (#2470).</summary>
    public enum StoryChapterRowState
    {
        /// <summary>Behind the player — titled, its beats re-readable.</summary>
        Done,

        /// <summary>The player's chapter now — titled and highlighted.</summary>
        Current,

        /// <summary>Not reached yet — "???", no spoiler.</summary>
        Locked,
    }

    /// <summary>One chapter row of the Story tab: the chapter, its state and the beats of it this player has heard.</summary>
    public sealed class StoryChapterRow
    {
        public StoryChapterRow(int index, StoryChapter chapter, StoryChapterRowState state, IReadOnlyList<string> beatKeys)
        {
            Index = index;
            Chapter = chapter;
            State = state;
            BeatKeys = beatKeys;
        }

        /// <summary>The chapter number (0 = the prologue "Awakening").</summary>
        public int Index { get; }

        public StoryChapter Chapter { get; }

        public StoryChapterRowState State { get; }

        /// <summary>The heard beats of this chapter as text keys, in arc order.</summary>
        public IReadOnlyList<string> BeatKeys { get; }
    }

    /// <summary>
    /// The Story tab's chapter list of the world story (#2470, synthesis §5.8 "VEGA-Seite"): every chapter of the pack in
    /// order — the reached ones titled (done / current), the future ones as "???" — with the beats this player has heard
    /// grouped under the chapter they belong to (<see cref="StoryChapters.ChapterIndexOfBeat"/>). Before the player's story
    /// track starts only the prologue is reached. A server that predates the chapters sends no chapter: the list then
    /// follows the beats the player heard. Pure — unit-tested headless in Client.Tests.
    /// </summary>
    public static class StoryChapterList
    {
        /// <summary>Builds the rows. <paramref name="chapterId"/> is the player's chapter (<c>StoryStateMessage.Chapter</c>),
        /// <paramref name="worldChapterId"/> the world's (empty from an older server).</summary>
        public static List<StoryChapterRow> Build(StoryDefinition? pack, IEnumerable<int>? heard, string? chapterId, bool started,
            string? worldChapterId)
        {
            var rows = new List<StoryChapterRow>();
            if (pack is null || pack.Chapters.Count == 0)
            {
                return rows;
            }

            var beats = new List<int>(new HashSet<int>(heard ?? Array.Empty<int>()));
            beats.Sort();
            bool currentServer = !string.IsNullOrEmpty(worldChapterId);
            int current = currentServer && started ? Math.Max(0, StoryChapters.IndexOf(pack, chapterId)) : 0;
            foreach (int b in beats)
            {
                current = Math.Max(current, StoryChapters.ChapterIndexOfBeat(pack, b)); // never hide a beat the player heard
            }

            for (int i = 0; i < pack.Chapters.Count; i++)
            {
                var keys = new List<string>();
                foreach (int b in beats)
                {
                    if (StoryChapters.ChapterIndexOfBeat(pack, b) == i && BeatKey(pack, b) is { Length: > 0 } key)
                    {
                        keys.Add(key);
                    }
                }

                var state = i < current ? StoryChapterRowState.Done : i == current ? StoryChapterRowState.Current : StoryChapterRowState.Locked;
                rows.Add(new StoryChapterRow(i, pack.Chapters[i], state, keys));
            }

            return rows;
        }

        /// <summary>The row's heading: "Prologue — Awakening", "Chapter 3 — The Guardian", a future one "Chapter 4 — ???".</summary>
        public static string Label(StoryChapterRow row, Func<string, string> localize)
        {
            if (row is null || localize is null)
            {
                return string.Empty;
            }

            string title = row.State == StoryChapterRowState.Locked ? localize("ui.story.chapter_locked") : localize(row.Chapter.TitleKey);
            if (row.Index == 0)
            {
                return localize("ui.story.prologue") + " — " + title;
            }

            return Format(localize("ui.story.chapter_row"), row.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), title);
        }

        /// <summary>The status line "The crew of this world is in chapter N." (empty when the world's chapter is unknown).</summary>
        public static string CrewLine(StoryDefinition? pack, string? worldChapterId, Func<string, string> localize)
        {
            int index = pack is null ? -1 : StoryChapters.IndexOf(pack, worldChapterId);
            return index < 0 || localize is null
                ? string.Empty
                : Format(localize("ui.story.crew_chapter"), index.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Empty);
        }

        /// <summary>The argument of "The story so far" on demand (the built-in recap card played locally): the title keys of the
        /// chapters from chapter 1 up to the player's chapter, joined like the server's recap. Empty before chapter 2.</summary>
        public static string RecapArg(StoryDefinition? pack, string? chapterId)
        {
            int current = pack is null ? -1 : StoryChapters.IndexOf(pack, chapterId);
            if (pack is null || current < 2)
            {
                return string.Empty;
            }

            var keys = new List<string>();
            for (int i = 1; i <= current; i++)
            {
                if (!string.IsNullOrEmpty(pack.Chapters[i].TitleKey))
                {
                    keys.Add(pack.Chapters[i].TitleKey);
                }
            }

            return string.Join(LineText.ArgSeparator.ToString(), keys);
        }

        private static string BeatKey(StoryDefinition pack, int index)
        {
            foreach (var beat in pack.Beats)
            {
                if (beat.Index == index)
                {
                    return beat.TextKey ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static string Format(string template, string a, string b)
        {
            try
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, template ?? string.Empty, a, b);
            }
            catch (FormatException)
            {
                return template ?? string.Empty;
            }
        }
    }
}
