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
    /// The Companions tab of <see cref="CraftingTechShipUI"/>: the roster of tamed creatures with rename and release.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        // --- Companions tab (tamed creatures): roster with rename + release; design docs/developer/CREATURE_TAMING.md ---

        /// <summary>Per-companion in-progress rename text (keyed by companion id), so typing survives a rebuild.</summary>
        private readonly System.Collections.Generic.Dictionary<string, string> _companionDraft = new();

        private float BuildCompanionsList()
        {
            var all = Game.Companions?.Companions ?? System.Array.Empty<NetCompanion>();
            bool hereOnly = _category != "all";
            var shown = all.Where(c => !hereOnly || c.Present).ToList();

            float y = 0f;
            if (shown.Count == 0)
            {
                UiKit.AddText(_listContent, 8, y, 760, 64, L("ui.companions.empty"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                return y + 74f;
            }

            foreach (var c in shown)
            {
                string id = c.Id;
                string dot = c.Present ? "<color=#52E0A0>●</color> " : "<color=#6A7480>●</color> ";
                string title = string.IsNullOrEmpty(c.Name) ? c.SpeciesName : c.Name;
                UiKit.AddText(_listContent, 8, y + 4, 700, 26, dot + title, 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);

                string state = c.Present ? L("ui.companions.present") : L("ui.companions.away");
                string sub = $"{c.SpeciesName}  ·  {L("ui.companions.home")}: {c.HomeBodyName}  ·  {L("ui.companions.bond")} {c.Bond}  ·  {state}";
                UiKit.AddText(_listContent, 28, y + 30, 700, 22, sub, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);

                // Bond bar (#1225): the number finally buys something, so show where it stands and where the
                // next step is — tick marks at the three tiers (wider fetch / bandit ward / scouting).
                const float barW = 420f;
                UiKit.AddImage(_listContent, 28, y + 54, barW, 6, UiKit.SolidSprite, new Color(0.1f, 0.18f, 0.3f));
                UiKit.AddImage(_listContent, 28, y + 54, barW * Mathf.Clamp01(c.Bond / 100f), 6, UiKit.SolidSprite, UiKit.Cyan);
                foreach (int tier in new[] { 50, 70, 90 })
                {
                    UiKit.AddImage(_listContent, 28 + barW * tier / 100f - 1f, y + 51, 2, 12, UiKit.SolidSprite,
                        c.Bond >= tier ? UiKit.Ok : UiKit.CyanDim);
                }

                string tierText = c.Bond >= 90 ? L("ui.companions.tier_scout")
                    : c.Bond >= 70 ? L("ui.companions.tier_ward")
                    : c.Bond >= 50 ? L("ui.companions.tier_fetch")
                    : L("ui.companions.tier_none");
                UiKit.AddText(_listContent, 460, y + 46, 300, 22, tierText, 13, UiKit.CyanDim, TextAnchor.MiddleLeft);

                string draft = _companionDraft.TryGetValue(id, out var d) ? d : c.Name;
                var field = UiKit.AddInput(_listContent, 8, y + 68, 300, 44, draft, v => _companionDraft[id] = v, L("ui.companion.placeholder"));
                field.characterLimit = 24;
                field.lineType = InputField.LineType.SingleLine;
                UiKit.AddButton(_listContent, 316, y + 68, 130, 44, L("ui.companions.rename"), () =>
                {
                    string nm = _companionDraft.TryGetValue(id, out var dd) ? dd : c.Name;
                    Game.Network?.SendSetCompanionName(id, nm);
                });

                // Feed (#1225): one bait from the pack, +5 bond. Dimmed — not hidden — when it would do
                // nothing right now, so a player can see the button exists and read why it is grey.
                var feed = UiKit.AddButton(_listContent, 454, y + 68, 130, 44, L("ui.companions.feed"), () => Game.Network?.SendFeedCompanion(id));
                if (!c.CanFeed)
                {
                    feed.interactable = false;
                    UiKit.AddText(_listContent, 28, y + 114, 700, 18,
                        c.Bond >= 100 ? L("ui.companions.feed_full") : L("ui.companions.feed_hint"), 12, UiKit.CyanDim, TextAnchor.MiddleLeft);
                }

                var rel = UiKit.AddButton(_listContent, 592, y + 68, 130, 44, L("ui.companions.release"), () => Game.Network?.SendReleaseCompanion(id));
                rel.GetComponent<Image>().color = new Color(0.5f, 0.22f, 0.22f);

                y += 140f;
            }

            return y;
        }
    }
}
