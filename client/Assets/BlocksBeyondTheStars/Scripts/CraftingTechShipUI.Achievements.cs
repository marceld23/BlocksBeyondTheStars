// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BlocksBeyondTheStars.Client.Portal;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Achievements tab of <see cref="CraftingTechShipUI"/>: every achievement grouped by category with its
    /// progress, plus the journey counters.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        // --- Achievements tab ---

        /// <summary>
        /// The achievements page: every achievement grouped by category, with a progress bar and an "x/y" count
        /// so it doubles as a "what can I do next?" list — a player asked for achievements with rewards, and the
        /// progress is what makes an unearned one useful rather than just a locked box.
        /// <para>
        /// Rows are laid out at ABSOLUTE offsets (no LayoutGroup): a VerticalLayoutGroup with wrapped text
        /// overflows its rows here, which is a trap this codebase has hit before.
        /// </para>
        /// </summary>
        private float BuildAchievementList()
        {
            float y = 8f;
            var all = Game?.Achievements;

            // The page opens with a Progress block (#1103): the whole-game "how far am I" that used to exist
            // nowhere — research, discoveries, story and the raw journey counters — before the goal list.
            y = BuildProgressBlock(y);

            if (all == null || all.Length == 0)
            {
                UiKit.AddText(_listContent, 8, y, 760, 34, L("ui.achv.none"), 20, UiKit.CyanDim, TextAnchor.MiddleLeft);
                return y + 44f;
            }

            int done = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Earned) done++;
            }

            UiKit.AddText(_listContent, 8, y, 760, 36,
                L("ui.achv.summary").Replace("{done}", done.ToString()).Replace("{total}", all.Length.ToString()),
                24, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 46f;

            // Group by category, keeping the authored order inside each group.
            var seen = new System.Collections.Generic.List<string>();
            foreach (var a in all)
            {
                string cat = a.Category ?? string.Empty;
                if (!seen.Contains(cat)) seen.Add(cat);
            }

            foreach (var cat in seen)
            {
                y = AllianceSection(string.IsNullOrEmpty(cat) ? L("ui.achv.title") : L("achv.category." + cat), y);

                foreach (var a in all)
                {
                    if ((a.Category ?? string.Empty) != cat)
                    {
                        continue;
                    }

                    y = AchievementRow(y, a);
                }

                y += 8f;
            }

            return y + 12f;
        }

        /// <summary>The Progress block (#1103): research N of M, discoveries, story %, achievements done — one line
        /// each — then the raw lifetime counters as a two-column "Journey" grid. Everything here is data the
        /// server already sends; the block just puts it in one place. Absolute rows, no LayoutGroup.</summary>
        private float BuildProgressBlock(float y)
        {
            const float RowW = 760f;
            UiKit.AddText(_listContent, 8, y, RowW, 36, L("ui.progress.title"), 24, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 42f;

            int bpTotal = Game?.Content?.Blueprints?.Count ?? 0;
            int bpDone = Game?.UnlockedBlueprints?.Count ?? 0;
            var lines = new List<string>
            {
                L("ui.progress.research").Replace("{done}", Mathf.Min(bpDone, bpTotal).ToString()).Replace("{total}", bpTotal.ToString()),
                L("ui.progress.discoveries").Replace("{count}", (Game?.Discoveries?.Count ?? 0).ToString()),
            };

            var story = Game?.Story;
            if (story != null && story.Active)
            {
                int pct = story.ProgressTarget > 0 ? Mathf.Clamp(Mathf.RoundToInt(100f * story.Progress / story.ProgressTarget), 0, 100) : 0;
                lines.Add(L("ui.progress.story").Replace("{pct}", pct.ToString()));
            }

            var all = Game?.Achievements;
            if (all != null && all.Length > 0)
            {
                int done = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].Earned) done++;
                }

                lines.Add(L("ui.progress.achievements").Replace("{done}", done.ToString()).Replace("{total}", all.Length.ToString()));
            }

            // The SPS relay network (#1125): only once the first relay is done — a fresh save stays short.
            var relayNet = Game?.RelayNetwork;
            if (relayNet != null && relayNet.Enabled && relayNet.Relays != null)
            {
                int relaysDone = 0;
                for (int i = 0; i < relayNet.Relays.Length; i++)
                {
                    if (relayNet.Relays[i] != null && relayNet.Relays[i].Completed) relaysDone++;
                }

                if (relaysDone > 0)
                {
                    lines.Add(L("ui.progress.relays")
                        .Replace("{relays}", relaysDone.ToString())
                        .Replace("{lanes}", (relayNet.LaneSystemA?.Length ?? 0).ToString()));
                }
            }

            foreach (var line in lines)
            {
                UiKit.AddText(_listContent, 20, y, RowW - 28f, 26, line, 18, UiKit.TextCol, TextAnchor.MiddleLeft);
                y += 28f;
            }

            // The discoveries count is only a number here — the list itself, with WHERE each was found (#1843),
            // is the Codex "Discoveries" chapter. One button deep-links straight into it.
            UiKit.AddButton(_listContent, 20, y + 4f, 400, 40, L("ui.wiki.discoveries.open"), () => Menu?.OpenWiki("discoveries"));
            y += 50f;

            // Journey: the raw counters, two per row. Only counters the server has actually reported show up,
            // so a fresh save reads short rather than as a wall of zeros.
            var counters = Game?.AchievementCounters;
            if (counters != null && counters.Count > 0)
            {
                y += 6f;
                UiKit.AddText(_listContent, 8, y, RowW, 30, L("ui.progress.journey"), 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 34f;

                var cells = new List<string>();
                foreach (var counter in JourneyCounters)
                {
                    if (counters.TryGetValue(counter, out int n) && n > 0)
                    {
                        cells.Add(L("ui.progress.stat." + counter.Replace(':', '_')) + ": " + n.ToString("N0"));
                    }
                }

                for (int i = 0; i < cells.Count; i += 2)
                {
                    UiKit.AddText(_listContent, 20, y, 360, 26, cells[i], 17, UiKit.TextCol, TextAnchor.MiddleLeft);
                    if (i + 1 < cells.Count)
                    {
                        UiKit.AddText(_listContent, 400, y, 360, 26, cells[i + 1], 17, UiKit.TextCol, TextAnchor.MiddleLeft);
                    }

                    y += 26f;
                }
            }

            return y + 14f;
        }

        /// <summary>The counters the Journey grid shows, in display order (keys as the server names them).</summary>
        private static readonly string[] JourneyCounters =
        {
            "visit:body", "visit:system", "hyperjump:any", "mine:any", "build:any", "craft:any",
            "scan:any", "research:any", "defeat:any", "tame:any", "mission:completed", "loot:any",
            "relay:commissioned",
        };

        /// <summary>One achievement row: name, description, a filled bar and the tally (or "Done").</summary>
        private float AchievementRow(float y, BlocksBeyondTheStars.Networking.Messages.NetAchievement a)
        {
            const float RowW = 760f;
            var nameCol = a.Earned ? UiKit.Ok : UiKit.TextCol;

            UiKit.AddText(_listContent, 8, y, RowW - 150f, 28, (a.Earned ? "✓ " : "") + L($"achv.{a.Key}.name"),
                20, nameCol, TextAnchor.MiddleLeft, FontStyle.Bold);

            string tally = a.Earned
                ? L("ui.achv.done")
                : L("ui.achv.progress")
                    .Replace("{done}", a.Progress.ToString())
                    .Replace("{total}", a.Target.ToString());
            UiKit.AddText(_listContent, RowW - 140f, y, 140f, 28, tally, 18,
                a.Earned ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleRight);
            y += 28f;

            UiKit.AddText(_listContent, 20, y, RowW - 28f, 24, L($"achv.{a.Key}.desc"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 26f;

            // Progress bar: a dim track with a filled portion on top. Earned rows read as full and green.
            float frac = a.Target > 0 ? Mathf.Clamp01((float)a.Progress / a.Target) : 0f;
            if (a.Earned)
            {
                frac = 1f;
            }

            UiKit.AddImage(_listContent, 20, y, RowW - 28f, 8f, null, new Color(1f, 1f, 1f, 0.10f));
            if (frac > 0f)
            {
                UiKit.AddImage(_listContent, 20, y, (RowW - 28f) * frac, 8f, null,
                    a.Earned ? UiKit.Ok : UiKit.Cyan);
            }

            return y + 20f;
        }
    }
}
