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
    /// The Companions tab of <see cref="CraftingTechShipUI"/>: the roster of tamed creatures with rename and release, and
    /// (#3344) who follows — per row "Follows me / Stays home / On board" with its toggle and "Set home here", plus
    /// "Take all / Leave all" above the list. The row decisions live in <see cref="CompanionSelection"/> (Client.Core).
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        // --- Companions tab (tamed creatures): roster with rename + release; design docs/developer/CREATURE_TAMING.md ---

        /// <summary>Per-companion in-progress rename text (keyed by companion id), so typing survives a rebuild.</summary>
        private readonly System.Collections.Generic.Dictionary<string, string> _companionDraft = new();

        /// <summary>One companion row's height: name, details, bond bar, the name/feed/release buttons and the follow row.</summary>
        private const float CompanionRowHeight = 190f;

        private float BuildCompanionsList()
        {
            var all = Game.Companions?.Companions ?? System.Array.Empty<NetCompanion>();
            bool hereOnly = _category != "all";
            // #3344: "On this world" lists the companions here — and those that wait on board, which are with the player.
            var shown = all.Where(c => !hereOnly || CompanionSelection.ShownHere(c)).ToList();

            float y = 0f;
            if (all.Length > 0)
            {
                y = BuildCompanionsFollowBar(y);
            }

            if (shown.Count == 0)
            {
                UiKit.AddText(_listContent, 8, y, 760, 64, L("ui.companions.empty"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                return y + 74f;
            }

            foreach (var c in shown)
            {
                BuildCompanionRow(c, y);
                y += CompanionRowHeight + CompanionGearRowHeight(c); // #3398/#3399: the robot's gear row
            }

            return y;
        }

        /// <summary>#3344: "Take all" / "Leave all" — every companion follows, or every one stays home / waits on board.</summary>
        private float BuildCompanionsFollowBar(float y)
        {
            UiKit.AddButton(_listContent, 8, y, 200, 40, L("ui.companions.take_all"), () => Game.Network?.SendSetAllCompanionsFollow(true));
            UiKit.AddButton(_listContent, 216, y, 200, 40, L("ui.companions.leave_all"), () => Game.Network?.SendSetAllCompanionsFollow(false));
            return y + 54f;
        }

        /// <summary>One companion: its name, details and bond, the rename/feed/release buttons and the follow row.</summary>
        private void BuildCompanionRow(NetCompanion c, float y)
        {
            string dot = c.Present ? "<color=#52E0A0>●</color> " : "<color=#6A7480>●</color> ";
            string title = string.IsNullOrEmpty(c.Name) ? c.SpeciesName : c.Name;
            UiKit.AddText(_listContent, 8, y + 4, 700, 26, dot + title, 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);

            string state = c.Present ? L("ui.companions.present") : L("ui.companions.away");
            // #3344: the server says which companions travel (the robot, Rudolpho, the Soundi) — no more guessing by body plan.
            string where = CompanionSelection.Travels(c) ? L("ui.companions.robot_follows") : $"{L("ui.companions.home")}: {c.HomeBodyName}";
            string sub = $"{c.SpeciesName}  ·  {where}  ·  {L("ui.companions.bond")} {c.Bond}  ·  {state}";
            UiKit.AddText(_listContent, 28, y + 30, 700, 22, sub, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);

            BuildCompanionBond(c, y);
            BuildCompanionActions(c, y);
            BuildCompanionFollowRow(c, y);
            BuildCompanionGearRow(c, y + CompanionRowHeight - 6f); // #3398/#3399: the robot's weapon and jetpack, "Take back"
        }

        /// <summary>Bond bar (#1225): the number finally buys something, so show where it stands and where the next step is —
        /// tick marks at the three tiers (wider fetch / bandit ward / scouting).</summary>
        private void BuildCompanionBond(NetCompanion c, float y)
        {
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
        }

        /// <summary>Rename, feed and release (switch off / send home; a story companion has no release — it waits on board).</summary>
        private void BuildCompanionActions(NetCompanion c, float y)
        {
            string id = c.Id;
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
            bool robot = CompanionSelection.IsRobot(c), reindeer = CompanionSelection.IsReindeer(c);
            var feed = UiKit.AddButton(_listContent, 454, y + 68, 130, 44, L("ui.companions.feed"), () => Game.Network?.SendFeedCompanion(id));
            if (!c.CanFeed)
            {
                feed.interactable = false;
                UiKit.AddText(_listContent, 28, y + 114, 700, 18,
                    robot ? L("ui.companions.robot_bond") : reindeer ? L("ui.companions.reindeer_bond")
                        : c.Bond >= 100 ? L("ui.companions.feed_full") : L("ui.companions.feed_hint"),
                    12, UiKit.CyanDim, TextAnchor.MiddleLeft);
            }

            // #3295: "release" on the robot switches it off — it folds back into its item; #3322: Rudolpho goes home (his bell);
            // #3361: a story companion is never released (it waits on board — the follow row below).
            string releaseKey = CompanionSelection.ReleaseKey(c);
            if (releaseKey != null)
            {
                var rel = UiKit.AddButton(_listContent, 592, y + 68, 130, 44, L(releaseKey), () => Game.Network?.SendReleaseCompanion(id));
                rel.GetComponent<Image>().color = new Color(0.5f, 0.22f, 0.22f);
            }
        }

        /// <summary>#3344: "Follows me / Stays home / On board", the toggle that changes it, and "Set home here" for a planet
        /// pet that stays home while it is here.</summary>
        private void BuildCompanionFollowRow(NetCompanion c, float y)
        {
            string id = c.Id;
            var state = CompanionSelection.StateOf(c);
            string colour = state == CompanionFollowState.Follows ? "#52E0A0"
                : state == CompanionFollowState.StaysHome ? "#E0C052"
                : "#7FB8FF";
            UiKit.AddText(_listContent, 28, y + 138, 280, 40, $"<color={colour}>{L(CompanionSelection.StateKey(c))}</color>", 16,
                UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);

            bool following = c.Following;
            UiKit.AddButton(_listContent, 316, y + 136, 200, 44, L(CompanionSelection.ToggleKey(c)),
                () => Game.Network?.SendSetCompanionFollow(id, !following));
            if (CompanionSelection.CanSetHome(c))
            {
                UiKit.AddButton(_listContent, 524, y + 136, 198, 44, L("ui.companions.set_home"),
                    () => Game.Network?.SendSetCompanionHomeHere(id));
            }
        }
    }
}
