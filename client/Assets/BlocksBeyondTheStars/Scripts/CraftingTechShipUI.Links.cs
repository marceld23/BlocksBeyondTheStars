// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The links between the tabs of <see cref="CraftingTechShipUI"/> (#3106, #3112): jumping to a recipe, a blueprint, a
    /// ship module or a ship build page with the list scrolled to it, the blueprint line with its "Go to blueprint" button,
    /// and an item's "Needed for" section — the reverse of an ingredient list (<see cref="ItemUses"/>).
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        /// <summary>How many "Needed for" buttons one kind shows before "… and N more".</summary>
        private const int NeededForChips = 8;

        /// <summary>The y of the selected card in the list just built (-1 when it is not in the list), for the jumps.</summary>
        private float _selectedCardY = -1f;

        /// <summary>The (non-market) recipe whose card makes <paramref name="item"/> — the bench recipe before its factory twin.</summary>
        private RecipeDefinition RecipeForItem(string item)
            => Game.Content.Recipes.Values
                .Where(r => r.Station != CraftingStation.Market && r.Outputs.Count > 0 && r.Outputs[0].Item == item)
                .OrderBy(r => r.Station == CraftingStation.Factory ? 1 : 0)
                .FirstOrDefault();

        private void JumpToItemRecipe(string item)
        {
            var r = RecipeForItem(item);
            if (r != null)
            {
                JumpToRecipe(r.Key);
            }
        }

        /// <summary>Opens the Blueprints tab on <paramref name="blueprint"/> (#3106).</summary>
        private void JumpToBlueprint(string blueprint)
        {
            Menu?.OpenTech();
            ShowMode(Mode.Tech); // switch now — see JumpToRecipe
            JumpTo("all", blueprint);
        }

        /// <summary>Opens the Ship tab's module page on <paramref name="module"/>.</summary>
        private void JumpToModule(string module)
        {
            Menu?.OpenShip();
            ShowMode(Mode.Ship);
            JumpTo("modules", "mod:" + module);
        }

        /// <summary>Opens the Ship tab's build page on <paramref name="ship"/>.</summary>
        private void JumpToShipBuild(string ship)
        {
            Menu?.OpenShip();
            ShowMode(Mode.Ship);
            JumpTo("build", "newship:" + ship);
        }

        /// <summary>Selects <paramref name="selected"/> on the <paramref name="category"/> page of the current tab, clears the
        /// search and the "Craftable now" / "Researchable now" filter (a jump inside one tab keeps the mode, so the filter
        /// could hide the target) and scrolls the list so the card is in view.</summary>
        private void JumpTo(string category, string selected)
        {
            _category = category;
            _selected = selected;
            _search = string.Empty;
            _craftableOnly = false;
            BuildHeader();
            RebuildSidebar();
            RebuildList();
            RebuildDetail();
            ScrollListToSelected();
        }

        /// <summary>Scrolls the list to the selected card (recorded by <see cref="AddCard"/>), a little below the top edge.</summary>
        private void ScrollListToSelected()
        {
            if (_listContent == null || _selectedCardY < 0f)
            {
                return;
            }

            float viewHeight = (_listContent.parent as RectTransform)?.rect.height ?? 0f;
            float maxY = Mathf.Max(0f, _listContent.sizeDelta.y - viewHeight);
            _listContent.anchoredPosition = new Vector2(_listContent.anchoredPosition.x, Mathf.Clamp(_selectedCardY - 120f, 0f, maxY));
        }

        /// <summary>"✓/✗ Blueprint: X" with a "Go to blueprint" button beside it (#3106) — in the recipe, module and ship-build
        /// details. Returns the next y.</summary>
        private float BlueprintLine(string blueprint, float y)
        {
            bool ok = BlueprintOk(blueprint);
            var line = UiKit.AddText(_detail, 8, y + 5, 400, 26, $"{(ok ? "✓" : "✗")} {L("ui.craft.blueprint")}: {L($"blueprint.{blueprint}.name")}", 18,
                ok ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft);
            UiKit.FitLabel(line, 12, 18);
            var b = UiKit.AddButton(_detail, 416, y, 200, 36, string.Empty, () => JumpToBlueprint(blueprint));
            var t = UiKit.AddText(b.transform, 8, 0, 184, 36, L("ui.craft.go_to_blueprint"), 16, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.FitLabel(t, 10, 16);
            return y + 44f;
        }

        /// <summary>#3112 "Needed for": the things an item goes into — what recipes make with it, the blueprints whose research
        /// costs it, the modules and ships built from it — each a button to that page. Nothing when it is used nowhere.</summary>
        private float NeededForSection(string item, float y)
        {
            var uses = ItemUses.For(Game.Content, item);
            if (uses.IsEmpty)
            {
                return y;
            }

            y += 6f;
            UiKit.AddText(_detail, 8, y, 620, 26, L("ui.craft.needed_for"), 20, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 30f;
            var products = uses.Products
                .OrderBy(p => RecipeForItem(p) is { } r && !string.IsNullOrEmpty(r.RequiredBlueprint) && !BlueprintOk(r.RequiredBlueprint) ? 1 : 0)
                .ThenBy(ItemName, NameOrder)
                .Select(p => (ItemName(p), (System.Action)(() => JumpToItemRecipe(p))));
            y = UsesLine("ui.craft.needed_crafting", products.ToList(), y);
            y = UsesLine("ui.craft.needed_research",
                uses.Blueprints.Select(b => (L($"blueprint.{b}.name"), (System.Action)(() => JumpToBlueprint(b)))).ToList(), y);
            y = UsesLine("ui.craft.needed_modules",
                uses.Modules.Select(m => (L($"module.{m}.name"), (System.Action)(() => JumpToModule(m)))).ToList(), y);
            return UsesLine("ui.craft.needed_ships",
                uses.Ships.Select(s => (L($"ship.{s}.name"), (System.Action)(() => JumpToShipBuild(s)))).ToList(), y);
        }

        /// <summary>One kind of "Needed for": "Crafting (12)" and up to <see cref="NeededForChips"/> buttons, "… and N more".</summary>
        private float UsesLine(string labelKey, List<(string label, System.Action onClick)> chips, float y)
        {
            if (chips.Count == 0)
            {
                return y;
            }

            UiKit.AddText(_detail, 20, y, 600, 24, L(labelKey).Replace("{n}", chips.Count.ToString()), 17, UiKit.TextCol, TextAnchor.UpperLeft);
            y += 28f;
            y = ChipRow(chips.Take(NeededForChips), y);
            if (chips.Count > NeededForChips)
            {
                UiKit.AddText(_detail, 20, y, 600, 24, L("ui.tech.and_more").Replace("{n}", (chips.Count - NeededForChips).ToString()), 16,
                    UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 28f;
            }

            return y + 4f;
        }
    }
}
