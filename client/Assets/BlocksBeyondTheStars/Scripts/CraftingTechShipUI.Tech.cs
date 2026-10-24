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
    /// The Tech tab of <see cref="CraftingTechShipUI"/>: the blueprint list with its tiers and research states,
    /// the blueprint detail and what a node unlocks.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        private float BuildTechList()
        {
            // A progression tree: order by tier (prerequisite depth) and indent each tier, so the unlock
            // chain reads top-to-bottom / left-to-right like a tech tree; A–Z in the game language within a tier (#2566).
            var shown = Game.Content.Blueprints.Values
                .Where(bp => (_category == "all" || bp.Category == _category) && MatchesSearch(L($"blueprint.{bp.Key}.name"))
                             && (!_craftableOnly || ResearchableNow(bp)))
                .OrderBy(TechTier).ThenBy(bp => L($"blueprint.{bp.Key}.name"), NameOrder)
                .ToList();

            float y = 0f;

            // Header (#1103): how much of the tree is done and what the player could research next — the
            // "how far am I" line the tab never had. Counts the whole tree, not the current filter.
            {
                int total = Game.Content.Blueprints.Count;
                int done = 0;
                BlueprintDefinition next = null;
                foreach (var bp in Game.Content.Blueprints.Values)
                {
                    if (Game.UnlockedBlueprints.Contains(bp.Key))
                    {
                        done++;
                        continue;
                    }

                    if (ResearchableNow(bp) && (next == null || bp.KnowledgeCost < next.KnowledgeCost))
                    {
                        next = bp; // the cheapest researchable one is the natural "next"
                    }
                }

                UiKit.AddText(_listContent, 0, y, 760, 28,
                    L("ui.tech.progress").Replace("{done}", Mathf.Min(done, total).ToString()).Replace("{total}", total.ToString()),
                    20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 28f;
                string nextLine = next != null
                    ? L("ui.tech.next").Replace("{name}", L($"blueprint.{next.Key}.name"))
                    : L("ui.tech.next_none");
                UiKit.AddText(_listContent, 0, y, 760, 24, nextLine, 16, next != null ? UiKit.TextCol : UiKit.CyanDim, TextAnchor.MiddleLeft);
                y += 34f;
            }

            int lastTier = -1;
            foreach (var bp in shown)
            {
                int tier = TechTier(bp);
                if (tier != lastTier)
                {
                    UiKit.AddText(_listContent, 0, y, 760, 24, L("ui.tech.tier") + " " + (tier + 1), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                    y += 26f;
                    lastTier = tier;
                }

                var (label, col) = TechStatus(bp);
                // The card names the knowledge cost against the balance (#1191) so the player sees at a glance
                // which nodes their knowledge already covers — the detail pane used to be the only place.
                if (bp.KnowledgeCost > 0 && !Game.UnlockedBlueprints.Contains(bp.Key))
                {
                    label += "  ·  " + L("ui.tech.card_knowledge")
                        .Replace("{have}", Game.Knowledge.ToString())
                        .Replace("{need}", bp.KnowledgeCost.ToString());
                }

                AddCard(y, L($"blueprint.{bp.Key}.name"), "cat_tech", label, col, bp.Key, () => { _selected = bp.Key; RebuildDetail(); }, Mathf.Min(tier, 4) * 28f, contentKey: bp.Key);
                y += 88f;
            }

            if (_craftableOnly && shown.Count == 0)
            {
                // Filter on, nothing left: say so instead of leaving an empty pane under the progress header.
                var none = UiKit.AddText(_listContent, 0, y, 760, 52, L("ui.tech.researchable_now_none"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                none.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 56f;
            }

            return y;
        }

        /// <summary>The "Researchable now" filter (#2563; "Enough knowledge" before, #1191, which left prerequisites and
        /// materials out and so still listed "Materials missing" / "Locked" cards): not yet researched, prerequisites done,
        /// the knowledge balance covers the cost and the research materials are owned. One test with the card status, the
        /// header's "next" line and the Tech badge — see <see cref="BlueprintResearch"/>.</summary>
        private bool ResearchableNow(BlueprintDefinition bp) => ResearchState(bp) == BlueprintResearchState.Researchable;

        /// <summary>Where <paramref name="bp"/> stands for this player (<see cref="BlueprintResearch.StateOf"/>), from the
        /// researched set, the knowledge balance and what inventory + cargo hold.</summary>
        private BlueprintResearchState ResearchState(BlueprintDefinition bp)
            => BlueprintResearch.StateOf(bp, Game.UnlockedBlueprints, Game.Knowledge, Owned, Game.Content); // #2958: groups

        private readonly Dictionary<string, int> _tierCache = new();

        /// <summary>Tier = longest prerequisite chain depth (0 = no prerequisites). Memoised, cycle-safe.</summary>
        private int TechTier(BlueprintDefinition bp)
        {
            if (_tierCache.TryGetValue(bp.Key, out var t))
            {
                return t;
            }

            _tierCache[bp.Key] = 0; // guard against cycles
            int max = 0;
            foreach (var pre in bp.Prerequisites)
            {
                var pd = Game.Content.GetBlueprint(pre);
                if (pd != null)
                {
                    max = Mathf.Max(max, TechTier(pd) + 1);
                }
            }

            _tierCache[bp.Key] = max;
            return max;
        }

        private float DetailTech()
        {
            var bp = Game.Content.GetBlueprint(_selected);
            if (bp == null)
            {
                return 0f;
            }

            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 40, L($"blueprint.{bp.Key}.name"), 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 48f;
            string desc = Desc($"blueprint.{bp.Key}.desc");
            if (!string.IsNullOrEmpty(desc))
            {
                y = AddDetailDescription(desc, y);
            }

            var (status, col) = TechStatus(bp);
            UiKit.AddText(_detail, 8, y, 620, 28, L("ui.tech.status") + ": " + status, 20, col, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 36f;

            if (bp.Prerequisites.Count > 0)
            {
                UiKit.AddText(_detail, 8, y, 620, 26, L("ui.tech.prereqs"), 20, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                y += 30f;
                foreach (var pre in bp.Prerequisites)
                {
                    bool ok = Game.UnlockedBlueprints.Contains(pre);
                    UiKit.AddText(_detail, 20, y, 620, 26, $"{(ok ? "✓" : "✗")} {L($"blueprint.{pre}.name")}", 18,
                        ok ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft);
                    y += 28f;
                }
            }

            y = TechUnlocksSection(bp.Key, y);

            if (bp.UnlockCost.Count > 0 || bp.KnowledgeCost > 0)
            {
                y += 6f;
                UiKit.AddText(_detail, 8, y, 620, 26, L("ui.tech.cost"), 20, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                y += 30f;
                // Knowledge points (earned by scanning) are a separate cost from the material list — show it too,
                // otherwise the player can't see why a researched-looking node still won't unlock.
                if (bp.KnowledgeCost > 0)
                {
                    bool kok = Game.Knowledge >= bp.KnowledgeCost;
                    UiKit.AddText(_detail, 20, y, 620, 26, $"{(kok ? "✓" : "✗")} {L("ui.tech.knowledge")}  {Game.Knowledge}/{bp.KnowledgeCost}", 18,
                        kok ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft);
                    y += 28f;
                }

                foreach (var c in bp.UnlockCost)
                {
                    y = IngredientRow(c, y, 18);
                }
            }

            y += 10f;
            bool already = Game.UnlockedBlueprints.Contains(bp.Key);
            bool ready = !already && bp.Prerequisites.All(Game.UnlockedBlueprints.Contains) && HasAll(bp.UnlockCost)
                       && Game.Knowledge >= bp.KnowledgeCost;
            // Research is done at the cockpit (#1074): the server rejects elsewhere, so the button says so
            // instead of staying live and failing with a toast.
            bool can = ready && ResearchOkNow();
            if (ready && !ResearchOkNow())
            {
                UiKit.AddText(_detail, 8, y, 620, 26, NeedStationText("research"), 18, new Color(1f, 0.8f, 0.4f), TextAnchor.UpperLeft);
                y += 30f;
            }

            var btn = UiKit.AddButton(_detail, 8, y, 280, 56, already ? L("ui.tech.unlocked") : L("ui.action.unlock"), () => { Game.Network.SendUnlock(bp.Key); });
            SetInteractable(btn, can);
            y += 70f;
            return y;
        }

        /// <summary>
        /// #2250: "Unlocks" — what researching the blueprint opens: the functions it names (the bio lab's Change tab, …),
        /// the items its recipes make, the ship modules it allows and the blueprints it leads to. The pane only ever
        /// looked backwards (prerequisites), so a chain like the bio lab's read as five unrelated nodes. Long lists are
        /// cut with "… and N more".
        /// </summary>
        private float TechUnlocksSection(string blueprintKey, float y)
        {
            var unlocks = BlocksBeyondTheStars.Shared.Content.BlueprintUnlocks.For(Game.Content, blueprintKey);
            if (unlocks.IsEmpty)
            {
                return y;
            }

            y += 6f;
            UiKit.AddText(_detail, 8, y, 620, 26, L("ui.tech.unlocks"), 20, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 30f;

            foreach (var feature in unlocks.Features)
            {
                y = TechUnlockLine("• " + L(feature), y, UiKit.TextCol);
            }

            var items = new List<string>();
            foreach (var item in unlocks.Items)
            {
                items.Add(ItemName(item));
            }

            y = TechUnlockList("ui.tech.unlocks_items", items, y);

            var modules = new List<string>();
            foreach (var key in unlocks.Modules)
            {
                var module = Game.Content.GetShipModule(key);
                modules.Add(module != null ? L(module.NameKey) : key);
            }

            y = TechUnlockList("ui.tech.unlocks_modules", modules, y);

            var next = new List<string>();
            foreach (var key in unlocks.LeadsTo)
            {
                next.Add(L($"blueprint.{key}.name"));
            }

            return TechUnlockList("ui.tech.leads_to", next, y);
        }

        /// <summary>One "Label: a, b, c … and N more" line of the unlocks section (nothing when the list is empty).</summary>
        private float TechUnlockList(string labelKey, List<string> names, float y)
        {
            const int shown = 6;
            if (names.Count == 0)
            {
                return y;
            }

            string list = string.Join(", ", names.Take(shown));
            if (names.Count > shown)
            {
                list += " " + L("ui.tech.and_more").Replace("{n}", (names.Count - shown).ToString());
            }

            return TechUnlockLine(L(labelKey) + ": " + list, y, UiKit.CyanDim);
        }

        private float TechUnlockLine(string text, float y, Color color)
        {
            var t = UiKit.AddText(_detail, 20, y, 600, 26, text, 18, color, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            float h = Mathf.Max(26f, t.preferredHeight);
            t.rectTransform.sizeDelta = new Vector2(600f, h);
            return y + h + 2f;
        }

        /// <summary>The card / detail status of a blueprint. Knowledge and materials are separate gates (#1191): a node
        /// that only lacks knowledge used to read "Materials missing", sending the player to mine when they should scan;
        /// knowledge is named first (<see cref="BlueprintResearch.StateOf"/>). "Unlockable" is exactly what the
        /// "Researchable now" filter keeps (#2563).</summary>
        private (string, Color) TechStatus(BlueprintDefinition bp) => ResearchState(bp) switch
        {
            BlueprintResearchState.Researched => (L("ui.tech.unlocked"), UiKit.Ok),
            BlueprintResearchState.Locked => (L("ui.tech.locked"), new Color(0.6f, 0.6f, 0.7f)),
            BlueprintResearchState.KnowledgeMissing => (L("ui.tech.knowledge_missing"), new Color(1f, 0.7f, 0.3f)),
            BlueprintResearchState.MaterialsMissing => (L("ui.tech.materials_missing"), new Color(1f, 0.7f, 0.3f)),
            _ => (L("ui.tech.unlockable"), UiKit.Cyan),
        };

        /// <summary>True when at least one blueprint is unlockable right now (prerequisites met, materials owned and
        /// enough knowledge points) — drives the "new research available" badge on the Tech menu entry.</summary>
        private bool AnyBlueprintUnlockable()
        {
            if (Game?.Content?.Blueprints == null)
            {
                return false;
            }

            foreach (var bp in Game.Content.Blueprints.Values)
            {
                if (ResearchableNow(bp))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
