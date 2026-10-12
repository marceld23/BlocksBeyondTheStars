// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Story;

/// <summary>The arc events a world pack's chapter can be reached by (<see cref="StoryChapter.FromEvent"/>, #2470).</summary>
public static class StoryChapterEvents
{
    /// <summary>The finale system appeared on the star map (<see cref="StoryState.GuardianSystemRevealed"/>).</summary>
    public const string GuardianRevealed = "guardian_revealed";

    /// <summary>The finale was won (<see cref="StoryState.GuardianDefeated"/>).</summary>
    public const string GuardianDefeated = "guardian_defeated";

    /// <summary>Every known event.</summary>
    public static readonly IReadOnlyList<string> All = new[] { GuardianRevealed, GuardianDefeated };
}

/// <summary>
/// The chapters of a WORLD story pack (#2470, synthesis §4.7): chapter boundaries follow the beats, so the score engine
/// stays untouched. A chapter is <b>reached</b> once the shared arc revealed its <see cref="StoryChapter.FromBeat"/> or
/// raised its <see cref="StoryChapter.FromEvent"/>; the world's chapter is the last one reached. A player's chapter is
/// <b>derived</b>, never stored: <c>max(1, world)</c> once their story track has started (chapter 0 is the prologue, so a
/// track that starts on a young world opens chapter 1). Pure — used by the server and the client's Story tab alike.
/// </summary>
public static class StoryChapters
{
    /// <summary>Whether the arc has reached <paramref name="chapter"/>.</summary>
    public static bool Reached(StoryChapter chapter, int beatsRevealed, bool guardianRevealed, bool guardianDefeated)
    {
        if (chapter is null)
        {
            return false;
        }

        if (chapter.FromBeat >= 0 && beatsRevealed > chapter.FromBeat)
        {
            return true;
        }

        return (string.Equals(chapter.FromEvent, StoryChapterEvents.GuardianRevealed, StringComparison.Ordinal) && guardianRevealed)
               || (string.Equals(chapter.FromEvent, StoryChapterEvents.GuardianDefeated, StringComparison.Ordinal) && guardianDefeated);
    }

    /// <summary>The index of the world's chapter — the last chapter the arc has reached — or 0 when none is reached yet;
    /// -1 when the pack has no chapters.</summary>
    public static int WorldChapterIndex(StoryDefinition def, int beatsRevealed, bool guardianRevealed, bool guardianDefeated)
    {
        if (def is null || def.Chapters.Count == 0)
        {
            return -1;
        }

        int last = 0;
        for (int i = 0; i < def.Chapters.Count; i++)
        {
            if (Reached(def.Chapters[i], beatsRevealed, guardianRevealed, guardianDefeated))
            {
                last = i;
            }
        }

        return last;
    }

    /// <summary>The chapter index of a player whose track has started: <c>max(1, world)</c>, clamped to the pack (chapter 0
    /// is the prologue); -1 when the pack has no chapters.</summary>
    public static int TrackChapterIndex(StoryDefinition def, int worldChapterIndex)
    {
        if (def is null || def.Chapters.Count == 0)
        {
            return -1;
        }

        int first = Math.Min(1, def.Chapters.Count - 1);
        return Math.Min(def.Chapters.Count - 1, Math.Max(first, worldChapterIndex));
    }

    /// <summary>The chapter whose arc start is beat <paramref name="beatIndex"/> (its <see cref="StoryChapter.FromBeat"/>), or
    /// null.</summary>
    public static StoryChapter? OpenedByBeat(StoryDefinition def, int beatIndex)
    {
        if (def is null)
        {
            return null;
        }

        foreach (var chapter in def.Chapters)
        {
            if (chapter.FromBeat >= 0 && chapter.FromBeat == beatIndex)
            {
                return chapter;
            }
        }

        return null;
    }

    /// <summary>The index of the chapter a beat belongs to — the last chapter starting at or before it — or -1 when no
    /// chapter starts by beats (the Story tab then lists the beat without a chapter).</summary>
    public static int ChapterIndexOfBeat(StoryDefinition def, int beatIndex)
    {
        int found = -1;
        if (def is null)
        {
            return found;
        }

        for (int i = 0; i < def.Chapters.Count; i++)
        {
            int from = def.Chapters[i].FromBeat;
            if (from >= 0 && from <= beatIndex && (found < 0 || from >= def.Chapters[found].FromBeat))
            {
                found = i;
            }
        }

        return found;
    }

    /// <summary>The index of a chapter by id, or -1.</summary>
    public static int IndexOf(StoryDefinition def, string? chapterId)
    {
        if (def is null || string.IsNullOrEmpty(chapterId))
        {
            return -1;
        }

        for (int i = 0; i < def.Chapters.Count; i++)
        {
            if (string.Equals(def.Chapters[i].Id, chapterId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
