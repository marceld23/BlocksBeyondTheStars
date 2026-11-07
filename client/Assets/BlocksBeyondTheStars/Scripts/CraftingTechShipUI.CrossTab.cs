// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3182: finding things across the Crafting, Blueprints and Ship tabs. A playtester looked for the jump generator under
    /// Crafting → "Ship" and found nothing: that category holds the ship-building blocks only — the modules are built in the
    /// Ship tab — and a search only ever looked at its own tab. Now the Crafting tab's ship category (renamed "Ship parts")
    /// says where the modules are, with a button there, and a search lists what it finds in the other two tabs at the end of
    /// the list ("In the Ship tab: Jump Generator →"); a click opens that tab on the thing, the search kept.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        /// <summary>How many finds in one other tab are listed before "… and N more".</summary>
        private const int CrossTabEntries = 4;

        /// <summary>The tabs a search looks across.</summary>
        private static readonly Mode[] SearchTabs = { Mode.Crafting, Mode.Tech, Mode.Ship };

        /// <summary>#3182: the hint at the top of the Crafting tab's "Ship parts" category — the modules are built in the
        /// Ship tab — with a button that opens its module page. Returns the next y.</summary>
        private float ShipModulesHint(float y)
        {
            var hint = UiKit.AddText(_listContent, 8, y, 764, 50, L("ui.craft.ship_modules_hint"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            y += 54f;
            var b = UiKit.AddButton(_listContent, 8, y, 380, 46, string.Empty,
                () => OpenTabWithSearch(Mode.Ship, "modules", string.Empty, string.Empty));
            var t = UiKit.AddText(b.transform, 10, 0, 360, 46, L("ui.craft.go_to_ship_tab") + "  →", 18, UiKit.TextCol,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.FitLabel(t, 11, 18);
            return y + 58f;
        }

        /// <summary>#3182: with a search typed, what it finds in the OTHER tabs of Crafting / Blueprints / Ship, below the
        /// list — so also right at the top when this tab found nothing. Each find is a button that opens its tab on it, the
        /// search kept (the market's barter page searches its own deals only). Returns the next y.</summary>
        private float CrossTabSection(float y)
        {
            if (string.IsNullOrEmpty(_search) || (_mode == Mode.Crafting && _category == "market"))
            {
                return y;
            }

            var groups = new List<(Mode tab, List<(string name, string category, string selected)> hits)>();
            foreach (var tab in SearchTabs)
            {
                var hits = tab == _mode ? null : SearchHits(tab);
                if (hits != null && hits.Count > 0)
                {
                    groups.Add((tab, hits));
                }
            }

            if (groups.Count == 0)
            {
                return y;
            }

            string search = _search;
            y += y > 0f ? 14f : 0f;
            UiKit.AddText(_listContent, 8, y, 764, 30, L("ui.craft.found_elsewhere"), 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 36f;
            foreach (var (tab, hits) in groups)
            {
                string tabName = L(Tabs.First(t => t.Mode == tab).Loc);
                foreach (var (name, category, selected) in hits.Take(CrossTabEntries))
                {
                    var b = UiKit.AddButton(_listContent, 0, y, 780, 52, string.Empty, () => OpenTabWithSearch(tab, category, selected, search));
                    var label = UiKit.AddText(b.transform, 16, 0, 748, 52, L("ui.craft.found_in").Replace("{tab}", tabName).Replace("{name}", name),
                        20, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    UiKit.FitLabel(label, 12, 20);
                    y += 58f;
                }

                if (hits.Count > CrossTabEntries)
                {
                    UiKit.AddText(_listContent, 16, y, 760, 26, L("ui.tech.and_more").Replace("{n}", (hits.Count - CrossTabEntries).ToString()), 16,
                        UiKit.CyanDim, TextAnchor.MiddleLeft);
                    y += 30f;
                }
            }

            return y;
        }

        /// <summary>What the search finds in <paramref name="tab"/>, by the same rule as that tab's own list (the name or a
        /// category's name, #3107): (name, the page to open, the card to select). Name matches first, then A–Z; a factory
        /// twin of a bench recipe is the same find.</summary>
        private List<(string name, string category, string selected)> SearchHits(Mode tab)
        {
            var hits = new List<(string name, string category, string selected)>();
            void Consider(string name, IReadOnlyList<string> categories, string category, string selected)
            {
                if (MatchesSearchIn(name, categories))
                {
                    hits.Add((name, category, selected));
                }
            }

            switch (tab)
            {
                case Mode.Crafting:
                    foreach (var r in Game.Content.Recipes.Values.Where(r => r.Station != CraftingStation.Market && r.Outputs.Count > 0))
                    {
                        Consider(RecipeTitle(r, r.Outputs[0].Item), Cats.OfItem(r.Outputs[0].Item), "all", r.Key);
                    }

                    break;
                case Mode.Tech:
                    foreach (var bp in Game.Content.Blueprints.Values.Where(BlueprintShown)) // #3350: never a hidden one
                    {
                        Consider(L($"blueprint.{bp.Key}.name"), Cats.OfBlueprint(bp.Key), "all", bp.Key);
                    }

                    break;
                default:
                    foreach (var m in Game.Content.ShipModules.Values.Where(m => !m.Mandatory))
                    {
                        Consider(L($"module.{m.Key}.name"), Cats.OfModule(m.Key), "modules", "mod:" + m.Key);
                    }

                    foreach (var s in Game.Content.Ships.Values.Where(s => !string.IsNullOrEmpty(s.RequiredBlueprint)))
                    {
                        Consider(L($"ship.{s.Key}.name"), Cats.OfShip(s.Key), "build", "newship:" + s.Key);
                    }

                    break;
            }

            return hits.GroupBy(h => h.name).Select(g => g.First())
                .OrderBy(h => MatchesSearch(h.name) ? 0 : 1)
                .ThenBy(h => h.name, NameOrder)
                .ToList();
        }

        /// <summary>Opens <paramref name="tab"/> on <paramref name="category"/> with <paramref name="selected"/> picked and the
        /// search set to <paramref name="search"/>, the list scrolled to the card.</summary>
        private void OpenTabWithSearch(Mode tab, string category, string selected, string search)
        {
            switch (tab)
            {
                case Mode.Crafting: Menu?.OpenCrafting(); break;
                case Mode.Tech: Menu?.OpenTech(); break;
                default: Menu?.OpenShip(); break;
            }

            ShowMode(tab); // switch now — see JumpToRecipe; it clears the search, which comes back below
            _category = category;
            _selected = selected;
            _search = search ?? string.Empty;
            BuildHeader(); // the search box shows the kept text
            RebuildSidebar();
            RebuildList();
            RebuildDetail();
            ScrollListToSelected();
        }
    }
}
