// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The search and category filter of the inventory pages of <see cref="CraftingTechShipUI"/> (#3110, #3111): the cargo
    /// hold's list narrows to what matches and can be sorted; the backpack grid keeps every slot where it is and dims the
    /// ones that do not match; the Sandbox catalogue narrows like the hold. The filter is a "‹ Category ›" cycle that
    /// steps through "All" and the categories that occur on the page right now — buttons only, so pad and touch work it the
    /// way the mouse does. Presentation only: no slot moves, the server is not asked.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        /// <summary>The picked category of the inventory pages; empty = all.</summary>
        private string _invFilter = string.Empty;

        /// <summary>The cargo hold's order: an index into <see cref="CargoSortKeys"/>.</summary>
        private int _invSort;

        private static readonly string[] CargoSortKeys =
        {
            "ui.inventory.sort_slot", "ui.inventory.sort_category", "ui.inventory.sort_name", "ui.inventory.sort_count",
        };

        /// <summary>The items the current inventory page shows, for the filter's choices.</summary>
        private IEnumerable<string> InventoryPageItems() => _category switch
        {
            "cargo" => (Game.Cargo ?? System.Array.Empty<NetItemStack>()).Select(s => s.Item),
            "catalog" => Game.Content.Items.Keys,
            _ => (Game.Personal ?? System.Array.Empty<NetItemStack>()).Select(s => s.Item),
        };

        /// <summary>The toolbar of the backpack, the cargo hold and the catalogue — one row at the top of the list pane
        /// (#3671): search, the category cycle, then on the backpack (aboard) "Stow all materials", in the hold the order.</summary>
        private void AddInventoryStrip(Transform p)
        {
            bool cargo = _category == "cargo";
            float searchW = cargo ? 340f : 380f;
            float filterW = cargo ? 380f : 400f;
            float x = ListViewX;
            AddSearchBox(p, x, StripY, searchW, StripH);
            x += searchW + 8f;
            AddFilterCycle(p, x, StripY, filterW, StripH);
            x += filterW + 8f;
            float rest = ListViewX + ListViewW - x;
            if (cargo)
            {
                UiKit.AddCycle(p, x, StripY, rest, StripH, L(CargoSortKeys[_invSort % CargoSortKeys.Length]), step =>
                {
                    int n = CargoSortKeys.Length;
                    _invSort = ((_invSort + step) % n + n) % n;
                    RebuildList();
                    return L(CargoSortKeys[_invSort]);
                });
            }
            else if (_category == "personal" && AboardShipNow())
            {
                // The backpack's bulk stow into the hold (the hold exists only aboard) — it used to sit above the grid.
                UiKit.AddButton(p, x, StripY, rest, StripH, L("ui.cargo.stow_all"),
                    () => Game.Network?.SendMoveCargoItem(toCargo: true, item: string.Empty, bulkAll: true), "cat_cargo");
            }
        }

        /// <summary>"‹ Category ›" (<see cref="UiKit.AddCycle"/>): the arrows and the value step through "All" and the
        /// categories of the page's items; the value turns cyan while a category is picked.</summary>
        private void AddFilterCycle(Transform p, float x, float y, float w, float h)
        {
            Text value = null;
            Color idle = Color.white;
            value = UiKit.AddCycle(p, x, y, w, h, FilterLabel(), step =>
            {
                StepFilter(step);
                value.color = string.IsNullOrEmpty(_invFilter) ? idle : UiKit.Cyan;
                return FilterLabel();
            });
            idle = value.color;
            if (!string.IsNullOrEmpty(_invFilter))
            {
                value.color = UiKit.Cyan;
            }
        }

        private string FilterLabel() => string.IsNullOrEmpty(_invFilter) ? L("ui.inventory.filter_all") : CategoryName(_invFilter);

        /// <summary>The filter's choices — "" (all), then the categories of the page's items in the shared order — read at
        /// click time, so a stow or a take since the strip was built counts.</summary>
        private void StepFilter(int step)
        {
            var present = new HashSet<string>(InventoryPageItems().Where(i => !string.IsNullOrEmpty(i)).SelectMany(i => Cats.OfItem(i)),
                System.StringComparer.Ordinal);
            var choices = new List<string> { string.Empty };
            choices.AddRange(Cats.Categories.Select(c => c.Key).Where(present.Contains));
            int i = Mathf.Max(0, choices.IndexOf(_invFilter));
            int n = choices.Count;
            _invFilter = choices[((i + step) % n + n) % n];
            RebuildList(); // the cycle shows the new value itself; the grid dims, the lists narrow
        }

        private bool InventoryFilterActive => !string.IsNullOrEmpty(_invFilter) || !string.IsNullOrEmpty(_search);

        /// <summary>Whether an item passes the inventory filter: in the picked category, and the search finds its name or
        /// one of its categories.</summary>
        private bool InventoryMatches(string item)
        {
            var cats = Cats.OfItem(item);
            if (!string.IsNullOrEmpty(_invFilter) && !cats.Contains(_invFilter))
            {
                return false;
            }

            return MatchesSearchIn(ItemName(item), cats);
        }

        /// <summary>#3111: a backpack slot whose item does not pass the filter fades back — the slots stay where they are.</summary>
        private void DimUnlessMatch(Button slot, string item)
        {
            if (slot != null && !string.IsNullOrEmpty(item) && InventoryFilterActive && !InventoryMatches(item))
            {
                slot.gameObject.AddComponent<CanvasGroup>().alpha = 0.25f;
            }
        }

        /// <summary>#3110: the hold's stacks that pass the filter, in the picked order (storage order, category, A–Z, amount).</summary>
        private List<NetItemStack> CargoView(IEnumerable<NetItemStack> stacks)
        {
            var shown = stacks.Where(s => s != null && InventoryMatches(s.Item));
            return (_invSort % CargoSortKeys.Length) switch
            {
                1 => shown.OrderBy(s => Cats.OrderOf(Cats.OfItem(s.Item).FirstOrDefault())).ThenBy(s => ItemName(s.Item), NameOrder).ToList(),
                2 => shown.OrderBy(s => ItemName(s.Item), NameOrder).ToList(),
                3 => shown.OrderByDescending(s => s.Count).ThenBy(s => ItemName(s.Item), NameOrder).ToList(),
                _ => shown.ToList(),
            };
        }
    }
}
