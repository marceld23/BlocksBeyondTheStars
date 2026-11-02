// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shared build categories of <see cref="CraftingTechShipUI"/> (#3102–#3109): the grouped category sidebar of the
    /// Crafting and Blueprints tabs, the Ship tab's module kinds, search by category name and the "Belongs to" row. The
    /// membership itself lives in <see cref="ContentCategories"/> (Shared, from <c>data/categories.json</c>) — a thing may
    /// sit in several categories.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        /// <summary>The Ship tab's sidebar key prefix of a module kind ("kind:storage") — its pages "modules", "fleet",
        /// "build" and "paint" share the key space with the category keys, so the kinds carry a prefix.</summary>
        private const string ModuleKindPrefix = "kind:";

        /// <summary>A sidebar icon that names an item ("item:speeder") instead of a UI icon: its real icon is laid over.</summary>
        private const string ItemIconPrefix = "item:";

        private ContentCategories Cats => Game?.Content?.Categories ?? ContentCategories.Empty;

        private string CategoryName(string key) => Cats.Find(key) is { } c ? L(c.NameKey) : key;

        /// <summary>The grouped category entries of a sidebar: a heading per group, then its categories that have at least
        /// one member in this tab (<paramref name="hasMembers"/>). <paramref name="keyPrefix"/> prefixes the entry keys.</summary>
        private IEnumerable<(string key, string label, string icon)> CategoryEntries(System.Func<string, bool> hasMembers,
            string keyPrefix = "", bool headings = true)
        {
            string labelPrefix = headings ? string.Empty : "· "; // without headings they are sub-entries (the module kinds)
            var shown = Cats.Categories.Where(c => hasMembers(c.Key)).ToList();
            var groupKeys = Cats.Groups.Select(g => g.Key).ToList();
            foreach (var g in Cats.Groups)
            {
                var inGroup = shown.Where(c => c.Group == g.Key).ToList();
                if (inGroup.Count == 0)
                {
                    continue;
                }

                if (headings)
                {
                    yield return ("head:" + g.Key, L(g.NameKey), string.Empty);
                }

                foreach (var c in inGroup)
                {
                    yield return (keyPrefix + c.Key, labelPrefix + L(c.NameKey), SidebarIcon(c));
                }
            }

            foreach (var c in shown.Where(c => !groupKeys.Contains(c.Group)))
            {
                yield return (keyPrefix + c.Key, labelPrefix + L(c.NameKey), SidebarIcon(c)); // a category without a known group still shows
            }
        }

        private static string SidebarIcon(CategoryDefinition c)
            => !string.IsNullOrEmpty(c.IconItem) ? ItemIconPrefix + c.IconItem + "|" + c.Icon : c.Icon;

        /// <summary>The UI icon name <see cref="UiKit.AddButton"/> gets for a sidebar entry (the fallback of an item icon).</summary>
        private static string SidebarIconName(string icon)
        {
            if (string.IsNullOrEmpty(icon) || !icon.StartsWith(ItemIconPrefix, System.StringComparison.Ordinal))
            {
                return icon;
            }

            int bar = icon.IndexOf('|');
            return bar >= 0 ? icon.Substring(bar + 1) : "cat_all";
        }

        /// <summary>Lays the item's real icon over a sidebar button built with <see cref="SidebarIconName"/>'s fallback.</summary>
        private void ApplySidebarItemIcon(Button b, string icon)
        {
            if (b == null || string.IsNullOrEmpty(icon) || !icon.StartsWith(ItemIconPrefix, System.StringComparison.Ordinal))
            {
                return;
            }

            int bar = icon.IndexOf('|');
            string item = icon.Substring(ItemIconPrefix.Length, (bar >= 0 ? bar : icon.Length) - ItemIconPrefix.Length);
            var sprite = IconResolver.Resolve(item, Game);
            var img = b.transform.childCount > 1 ? b.transform.GetChild(0).GetComponent<Image>() : null;
            if (sprite != null && img != null)
            {
                img.sprite = sprite;
                img.color = IconResolver.Tint(item, Game);
            }
        }

        /// <summary>The Crafting tab's category entries: the grouped categories with at least one (non-market) recipe, then
        /// the special pages under "Extras".</summary>
        private IEnumerable<(string key, string label, string icon)> CraftCategoryEntries()
        {
            var crafted = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var r in Game.Content.Recipes.Values)
            {
                if (r.Station != CraftingStation.Market && r.Outputs.Count > 0)
                {
                    crafted.Add(r.Outputs[0].Item);
                }
            }

            var present = new HashSet<string>(crafted.SelectMany(i => Cats.OfItem(i)), System.StringComparer.Ordinal);
            foreach (var e in CategoryEntries(present.Contains))
            {
                yield return e;
            }

            yield return ("head:extras", L("ui.craft.group_extras"), string.Empty);
            yield return ("color", L("ui.craft.cat_color"), "cat_blocks");
            yield return ("shape", L("ui.craft.cat_shape"), "cat_blocks");
            yield return ("market", L("ui.craft.cat_market"), "cat_cargo");
        }

        /// <summary>The Blueprints tab's category entries: every category with at least one blueprint.</summary>
        private IEnumerable<(string key, string label, string icon)> TechCategoryEntries()
        {
            var present = new HashSet<string>(Game.Content.Blueprints.Keys.SelectMany(k => Cats.OfBlueprint(k)), System.StringComparer.Ordinal);
            return CategoryEntries(present.Contains);
        }

        /// <summary>#3109: the Ship tab's module kinds — the categories of the buildable modules except "Ship" itself (they
        /// all are), as sub-entries under "Modules".</summary>
        private IEnumerable<(string key, string label, string icon)> ModuleKindEntries()
        {
            var present = new HashSet<string>(Game.Content.ShipModules.Values.Where(m => !m.Mandatory).SelectMany(m => Cats.OfModule(m.Key)),
                System.StringComparer.Ordinal);
            present.Remove("ship");
            return CategoryEntries(present.Contains, ModuleKindPrefix, headings: false);
        }

        /// <summary>The module kind the Ship tab's sidebar picked, or null on its other pages.</summary>
        private string ModuleKind()
            => _category != null && _category.StartsWith(ModuleKindPrefix, System.StringComparison.Ordinal)
                ? _category.Substring(ModuleKindPrefix.Length)
                : null;

        /// <summary>Whether a recipe's output belongs to the Crafting tab's current category.</summary>
        private bool CraftItemInCategory(string item) => _category == "all" || Cats.OfItem(item).Contains(_category);

        /// <summary>#3107: a search matches the name OR the name of a category the thing belongs to ("Crystal" lists the whole
        /// Crystal Net, "Transport" every vehicle).</summary>
        private bool MatchesSearchIn(string label, IReadOnlyList<string> categories)
        {
            if (MatchesSearch(label))
            {
                return true;
            }

            foreach (string c in categories)
            {
                if (MatchesSearch(CategoryName(c)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>#3108: "Belongs to: [A] [B]" — one button per category, each picking that category
        /// (<paramref name="onPick"/>). Nothing when the thing has no category. Returns the next y.</summary>
        private float BelongsToRow(IReadOnlyList<string> categories, float y, System.Action<string> onPick)
        {
            if (categories == null || categories.Count == 0)
            {
                return y;
            }

            UiKit.AddText(_detail, 8, y, 620, 24, L("ui.craft.belongs_to"), 18, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 28f;
            var chips = categories.Select(c => (CategoryName(c), (System.Action)(() => onPick(c))));
            return ChipRow(chips, y) + 6f;
        }

        /// <summary>A wrapping row of small buttons in the detail pane, each as wide as its label. Returns the y below it.</summary>
        private float ChipRow(IEnumerable<(string label, System.Action onClick)> chips, float y, float x0 = 20f)
        {
            const float right = 616f, height = 36f, gap = 8f;
            float x = x0;
            bool any = false;
            foreach (var (label, onClick) in chips)
            {
                float w = Mathf.Min(right - x0, UiKit.MeasureWidth(label, 16) + 28f);
                if (x > x0 && x + w > right)
                {
                    x = x0;
                    y += height + gap;
                }

                var b = UiKit.AddButton(_detail, x, y, w, height, string.Empty, onClick);
                var t = UiKit.AddText(b.transform, 8, 0, w - 16, height, label, 16, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.FitLabel(t, 10, 16);
                x += w + gap;
                any = true;
            }

            return any ? y + height + gap : y;
        }

        /// <summary>Picks a category of the current tab from a "Belongs to" button: the sidebar entry in the Crafting and
        /// Blueprints tabs, the module kind in the Ship tab, the filter in the inventory. The selection stays.</summary>
        private void PickCategory(string key)
        {
            switch (_mode)
            {
                case Mode.Ship: _category = ModuleKindPrefix + key; break;
                case Mode.Inventory:
                    _invFilter = key;
                    if (_category != "cargo" && _category != "catalog")
                    {
                        _category = "personal"; // the suit and sample pages have no filter strip — the backpack shows it
                    }

                    BuildHeader();
                    break;
                default: _category = key; break;
            }

            _search = _mode == Mode.Inventory ? _search : string.Empty;
            if (_mode != Mode.Inventory)
            {
                BuildHeader(); // the search box shows the cleared text
            }

            RebuildSidebar();
            RebuildList();
            RebuildDetail();
        }
    }
}
