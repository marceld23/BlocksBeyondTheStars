// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Cutscenes
{
    /// <summary>One "Watch again" row of the Story tab.</summary>
    public sealed class CutsceneReplayEntry
    {
        public CutsceneReplayEntry(StoryDefinition pack, CutsceneDefinition definition, string cutsceneId)
        {
            Pack = pack;
            Definition = definition;
            CutsceneId = cutsceneId;
        }

        public StoryDefinition Pack { get; }

        public CutsceneDefinition Definition { get; }

        /// <summary>The (instance) id the replay asks for.</summary>
        public string CutsceneId { get; }
    }

    /// <summary>
    /// The client's view of the cutscenes a player has seen (#2468): the Story tab's "Watch again" list and the name each
    /// row shows. The seen set comes from the server (<c>StoryStateMessage.SeenCutscenes</c>, <c>pack/id</c>) plus what
    /// this client played since; only scenes of installed packs that the pack lets the player replay are listed.
    /// </summary>
    public static class CutsceneCatalog
    {
        /// <summary>The longest row text taken from a plain caption.</summary>
        public const int MaxLabelChars = 60;

        /// <summary>The seen-set entry of a scene: <c>pack/id</c>.</summary>
        public static string SeenKey(string packId, string cutsceneId) => packId + StoryCutsceneIds.PackSeparator + cutsceneId;

        /// <summary>Splits a <see cref="SeenKey"/> back into pack and (instance) id.</summary>
        public static bool TrySplitSeenKey(string? key, out string packId, out string cutsceneId)
        {
            packId = cutsceneId = string.Empty;
            int slash = key?.IndexOf(StoryCutsceneIds.PackSeparator) ?? -1;
            if (slash <= 0 || slash >= key!.Length - 1)
            {
                return false;
            }

            packId = key.Substring(0, slash);
            cutsceneId = key.Substring(slash + 1);
            return true;
        }

        /// <summary>The replayable seen scenes of <paramref name="packs"/>, grouped by pack in the given pack order and in
        /// each pack's definition order (instances of one template by their owner's place in the chapters). #2497:
        /// <paramref name="exclude"/> leaves scenes out by their <see cref="SeenKey"/> (the ending, which the Journal's
        /// "Watch the ending again" replays — <see cref="EndingIds"/>).</summary>
        public static IReadOnlyList<CutsceneReplayEntry> ReplayEntries(IEnumerable<StoryDefinition> packs, IEnumerable<string> seen,
            IEnumerable<string>? exclude = null)
        {
            var set = new HashSet<string>(seen ?? Array.Empty<string>(), StringComparer.Ordinal);
            set.ExceptWith(exclude ?? Array.Empty<string>());
            var entries = new List<CutsceneReplayEntry>();
            foreach (var pack in packs ?? Array.Empty<StoryDefinition>())
            {
                if (pack?.Cutscenes is null || pack.Cutscenes.Count == 0)
                {
                    continue;
                }

                var mine = new List<(int Def, int Owner, string Id, CutsceneDefinition D)>();
                foreach (string key in set)
                {
                    if (!TrySplitSeenKey(key, out string packId, out string id) || packId != pack.Id || StoryCutsceneIds.IsBuiltIn(id))
                    {
                        continue;
                    }

                    var def = pack.FindCutscene(id);
                    bool instance = id.IndexOf(StoryCutsceneIds.InstanceSeparator) >= 0;
                    if (def is null || instance != def.Template || !(def.Options?.Replayable ?? true))
                    {
                        continue;
                    }

                    mine.Add((pack.Cutscenes.IndexOf(def), OwnerOrder(pack, id), id, def));
                }

                foreach (var e in mine.OrderBy(m => m.Def).ThenBy(m => m.Owner).ThenBy(m => m.Id, StringComparer.Ordinal))
                {
                    entries.Add(new CutsceneReplayEntry(pack, e.D, e.Id));
                }
            }

            return entries;
        }

        /// <summary>
        /// The name of a scene in the list: its title card ("Prologue — The VEGA Protocol"), else the bound chapter's title,
        /// else the start of its first caption, else its id. A template instance reads with its owner chapter's title as
        /// the <c>{0}</c> — the same text the scene shows (#2473: with its place and the count, "Part 3 of 6"). A title that
        /// is only known when the scene plays (the first landing's body name) leaves just its kicker.
        /// </summary>
        public static string Label(StoryDefinition pack, CutsceneDefinition def, string cutsceneId, CutsceneTextSource text)
        {
            var chapter = ChapterOf(pack, def, cutsceneId);
            string chapterTitle = chapter is { TitleKey.Length: > 0 } ? text.Localize(chapter.TitleKey) : string.Empty;
            string arg = CutscenePlan.ResolveArg(CutsceneArgs.For(pack, def, cutsceneId), text);
            var captions = (def.Shots ?? new List<CutsceneShot>()).SelectMany(s => s?.Captions ?? new List<CutsceneCaption>()).Where(c => c != null).ToList();

            var title = captions.FirstOrDefault(c => c.Style == CutsceneCaptionStyles.TitleCard && !string.IsNullOrEmpty(c.Key));
            if (title != null)
            {
                string rawMain = text.Localize(title.Key);
                string main = LineText.Compose(rawMain, arg, text.Glyph);
                string kicker = string.IsNullOrEmpty(title.KickerKey) ? string.Empty : LineText.Compose(text.Localize(title.KickerKey), arg, text.Glyph);
                if (arg.Length == 0 && LineText.HasSlot(rawMain) && kicker.Length > 0)
                {
                    return kicker; // its title is filled in only when it plays
                }

                return kicker.Length > 0 ? kicker + " — " + main : main;
            }

            if (chapterTitle.Length > 0)
            {
                return chapterTitle;
            }

            var first = captions.FirstOrDefault(c => !string.IsNullOrEmpty(c.Key));
            if (first != null)
            {
                string line = LineText.Compose(text.Localize(first.Key), null, text.Glyph).Replace('\n', ' ').Trim();
                return line.Length <= MaxLabelChars ? line : line.Substring(0, MaxLabelChars - 1).TrimEnd() + "…";
            }

            return cutsceneId;
        }

        /// <summary>#2497: the scenes of a pack's ending — its finale's <c>standdown</c> and <c>resolution</c> bindings
        /// (<c>story.json</c> <c>finale.cutscenes</c>; VEGA: <c>fin.standdown</c>, <c>resolution</c>). The Journal's "Watch the
        /// ending again" replays both in that order, so "Watch again" leaves them out instead of listing the ending three
        /// times. Empty for a pack without a finale.</summary>
        public static IReadOnlyList<string> EndingIds(StoryDefinition? pack)
        {
            var ids = new List<string>();
            var bindings = pack?.Finale?.Cutscenes;
            if (bindings is null)
            {
                return ids;
            }

            foreach (string stage in new[] { StoryFinaleStages.Standdown, StoryFinaleStages.Resolution })
            {
                if (bindings.TryGetValue(stage, out string? id) && !string.IsNullOrEmpty(id) && !ids.Contains(id!))
                {
                    ids.Add(id!);
                }
            }

            return ids;
        }

        /// <summary>#2497: <see cref="EndingIds"/> as <see cref="SeenKey"/>s — the <c>exclude</c> of <see cref="ReplayEntries"/>.</summary>
        public static IEnumerable<string> EndingKeys(StoryDefinition? pack)
            => pack is null ? Array.Empty<string>() : EndingIds(pack).Select(id => SeenKey(pack.Id, id));

        /// <summary>The chapter a scene belongs to: an instance's owner chapter (or the chapter of its owner step), else the
        /// scene's own <c>chapter</c> — the server's rule (<see cref="CutsceneArgs.ChapterOf"/>).</summary>
        public static StoryChapter? ChapterOf(StoryDefinition pack, CutsceneDefinition def, string cutsceneId)
            => CutsceneArgs.ChapterOf(pack, def, cutsceneId);

        /// <summary>The argument a scene plays with when this client plays it on its own (the dev palette, the clip
        /// recorder): the server's rule (<see cref="CutsceneArgs.For"/>) with the body and star system the player is on, read
        /// from the star map (#2473).</summary>
        public static string PlayArg(StoryDefinition pack, CutsceneDefinition def, string cutsceneId, StarMapData? map)
        {
            string body = string.Empty;
            string system = string.Empty;
            string here = map?.ActiveLocationId ?? string.Empty;
            foreach (var sys in map?.Systems ?? Array.Empty<NetStarSystem>())
            {
                foreach (var b in sys?.Bodies ?? Array.Empty<NetBody>())
                {
                    if (b != null && here.Length > 0 && b.Id == here)
                    {
                        body = b.Name ?? string.Empty;
                        system = sys!.Name ?? string.Empty;
                    }
                }
            }

            return CutsceneArgs.For(pack, def, cutsceneId, body, system);
        }

        private static int OwnerOrder(StoryDefinition pack, string id)
        {
            string owner = StoryCutsceneIds.OwnerOf(id);
            if (owner.Length == 0)
            {
                return -1;
            }

            for (int i = 0; i < pack.Chapters.Count; i++)
            {
                var chapter = pack.Chapters[i];
                if (chapter.Id == owner || (chapter.Steps?.Any(st => st.Id == owner) ?? false))
                {
                    return i;
                }
            }

            return int.MaxValue;
        }
    }
}
