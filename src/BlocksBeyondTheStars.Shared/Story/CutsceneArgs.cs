// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Localization;

namespace BlocksBeyondTheStars.Shared.Story;

/// <summary>
/// The argument a pack binding plays a cutscene with (<c>CutscenePlay.Arg</c>, the captions' <c>{0}</c>, <c>{1}</c>…) —
/// one rule for the server (every send, the join catch-up, a replay, the admin preview) and the client (the dev palette's
/// local plays, the "Watch again" names), so a scene reads the same everywhere (#2467, #2473).
/// <list type="bullet">
/// <item>A scene bound to a chapter — a template instance's owner chapter (or the chapter of its owner step), else the
/// scene's own <c>chapter</c> — plays with that chapter's title <b>key</b> as <c>{0}</c> (the client localizes an argument
/// that is a key), its place among the pack's chapters of the same kind (mandatory or optional, counted from 1) as
/// <c>{1}</c> and how many there are as <c>{2}</c>: the tutorial's part cards read "Part 3 of 6".</item>
/// <item>A scene whose <see cref="CutsceneDefinition.Arg"/> names a source plays with that value instead: <c>body</c> = the
/// name of the body the player is on (the first landing), <c>system</c> = the name of the player's star system.</item>
/// <item>Anything else plays without an argument.</item>
/// </list>
/// Several values are joined by <see cref="LineText.ArgSeparator"/>.
/// </summary>
public static class CutsceneArgs
{
    /// <summary>The chapter a scene is bound to: an instance's owner chapter (or the chapter of its owner step), else the
    /// scene's own <c>chapter</c>; null when it has none.</summary>
    public static StoryChapter? ChapterOf(StoryDefinition pack, CutsceneDefinition def, string cutsceneId)
    {
        var chapters = pack?.Chapters ?? new List<StoryChapter>();
        string owner = StoryCutsceneIds.OwnerOf(cutsceneId ?? string.Empty);
        if (owner.Length > 0)
        {
            foreach (var chapter in chapters)
            {
                if (chapter.Id == owner || (chapter.Steps?.Any(st => st.Id == owner) ?? false))
                {
                    return chapter;
                }
            }
        }

        return string.IsNullOrEmpty(def?.Chapter) ? null : chapters.FirstOrDefault(c => c.Id == def!.Chapter);
    }

    /// <summary>The place of <paramref name="chapter"/> among the pack's chapters of the same kind (mandatory chapters
    /// among the mandatory ones, an optional chapter among the optional ones), counted from 1, and how many there are.</summary>
    public static (int Place, int Count) PlaceOf(StoryDefinition pack, StoryChapter chapter)
    {
        var sameKind = (pack?.Chapters ?? new List<StoryChapter>()).Where(c => c.Optional == chapter.Optional).ToList();
        return (sameKind.IndexOf(chapter) + 1, sameKind.Count);
    }

    /// <summary>The argument for playing <paramref name="cutsceneId"/> (a definition or instance id of
    /// <paramref name="def"/>) of <paramref name="pack"/>. <paramref name="bodyName"/> and <paramref name="systemName"/> are
    /// where the player is (only read by a scene whose <see cref="CutsceneDefinition.Arg"/> asks for them; empty when
    /// unknown — the slot then reads "…").</summary>
    public static string For(StoryDefinition pack, CutsceneDefinition def, string cutsceneId, string? bodyName = null, string? systemName = null)
    {
        if (def is null)
        {
            return string.Empty;
        }

        switch (def.Arg ?? string.Empty)
        {
            case CutsceneArgSources.Body:
                return bodyName ?? string.Empty;
            case CutsceneArgSources.System:
                return systemName ?? string.Empty;
        }

        var chapter = ChapterOf(pack, def, cutsceneId);
        if (chapter is null || string.IsNullOrEmpty(chapter.TitleKey))
        {
            return string.Empty;
        }

        var (place, count) = PlaceOf(pack, chapter);
        return string.Join(LineText.ArgSeparator.ToString(),
            chapter.TitleKey,
            place.ToString(CultureInfo.InvariantCulture),
            count.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The owner a template plays as when nobody bound it for this play (the admin preview, the dev palette): the
    /// first chapter that binds it (opener or closer), else the first step that binds it, else the first chapter, else the
    /// pack itself.</summary>
    public static string DefaultOwner(StoryDefinition pack, CutsceneDefinition def)
    {
        var chapters = pack?.Chapters ?? new List<StoryChapter>();
        string id = def?.Id ?? string.Empty;
        foreach (var chapter in chapters)
        {
            if (string.Equals(chapter.OpenCutscene, id, StringComparison.Ordinal) || string.Equals(chapter.CloseCutscene, id, StringComparison.Ordinal))
            {
                return chapter.Id;
            }
        }

        foreach (var step in chapters.SelectMany(c => c.Steps ?? new List<StoryStep>()))
        {
            if (string.Equals(step.OnStart?.Cutscene, id, StringComparison.Ordinal) || string.Equals(step.OnComplete?.Cutscene, id, StringComparison.Ordinal))
            {
                return step.Id;
            }
        }

        return chapters.FirstOrDefault()?.Id ?? pack?.Id ?? string.Empty;
    }
}
