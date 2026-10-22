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
    /// Crafting rules of <see cref="CraftingTechShipUI"/> shared by the Crafting, Tech, Ship, Inventory and Suit
    /// tabs: free crafting and the catalog gate, ingredient rows and costs, whether a recipe can be crafted and fits,
    /// disassembly, and category/search matching.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        /// <summary>#1930: the "All items" page exists while the player plays the Creative game mode (Sandbox, or their own
        /// Creative override) — the rules the server sends are already the player's effective ones.</summary>
        private bool CatalogAvailable() => FreeCrafting();

        /// <summary>#1936: true while this player crafts for FREE — the Creative game mode (world Sandbox, or their own
        /// Creative override). The server then produces any recipe without materials, blueprint, station or market, so
        /// the menu must not keep the button grey: the world-create hint promises "crafting costs no materials".</summary>
        private bool FreeCrafting() => Game?.Rules != null && Game.Rules.GameMode == "Creative";

        /// <summary>Fraction of a crafted item's recipe inputs recovered on disassembly (mirrors the server).</summary>
        private const float DisassemblyRecoveryRate = 0.5f;

        /// <summary>The non-market crafting recipe that produces <paramref name="item"/> (so it can be
        /// disassembled), plus its per-craft output count; (null, 1) when the item isn't craftable.</summary>
        private (RecipeDefinition, int) DisassembleRecipe(string item)
        {
            foreach (var r in Game.Content.Recipes.Values)
            {
                if (r.Station == CraftingStation.Market || r.Inputs.Count == 0)
                {
                    continue;
                }

                var output = r.Outputs.FirstOrDefault(o => o.Item == item);
                if (output != null)
                {
                    return (r, Mathf.Max(1, output.Count));
                }
            }

            return (null, 1);
        }

        /// <summary>One ✓/✗ have/need ingredient row plus a source tag (#1016): tells the player whether the
        /// material is itself craftable or a raw resource to find in the world. A craftable ingredient the
        /// player is still short of also lists what crafting the missing amount takes, indented beneath —
        /// one recipe level deep, enough to see what to actually gather without rendering a whole tree.</summary>
        private float IngredientRow(ItemAmount inp, float y, int size)
        {
            int have = Owned(inp.Item);
            bool ok = have >= inp.Count;
            bool craftable = Game.Content.CraftDepth(inp.Item) > 0;
            UiKit.AddText(_detail, 20, y, 620, size + 8, $"{(ok ? "✓" : "✗")} {ItemName(inp.Item)}  {have}/{inp.Count}", size,
                ok ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft);
            // Right-anchored, so its right edge must clear the detail viewport (636 wide, RectMask2D) AND the
            // 8-px inline scrollbar that overlays the viewport's right edge — x = 20 + 620 = 640 lost the last
            // glyph ("craftabl|e", #1057). 20 + 596 = 616 leaves 20 px, well clear of both.
            UiKit.AddText(_detail, 20, y, 596, size + 8, L(craftable ? "ui.craft.src_craftable" : "ui.craft.src_raw"),
                size - 4, UiKit.CyanDim, TextAnchor.UpperRight);
            y += size + 10f;
            if (!craftable || ok)
            {
                return y;
            }

            var (sub, perCraft) = DisassembleRecipe(inp.Item);
            if (sub == null)
            {
                return y; // craftable per CraftDepth, but its recipe has no inputs — nothing to break down
            }

            int crafts = (inp.Count - have + perCraft - 1) / perCraft;
            foreach (var si in sub.Inputs)
            {
                int need = si.Count * crafts;
                int subHave = Owned(si.Item);
                bool subOk = subHave >= need;
                bool subCraftable = Game.Content.CraftDepth(si.Item) > 0;
                UiKit.AddText(_detail, 44, y, 596, size + 4,
                    $"{(subOk ? "✓" : "✗")} {ItemName(si.Item)}  {subHave}/{need} · {L(subCraftable ? "ui.craft.src_craftable" : "ui.craft.src_raw")}",
                    size - 4, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += size + 6f;
            }

            return y;
        }

        private float CostBlock(IEnumerable<ItemAmount> cost, string blueprint, float y)
        {
            UiKit.AddText(_detail, 8, y, 620, 26, L("ui.craft.needs"), 20, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 32f;
            foreach (var c in cost)
            {
                y = IngredientRow(c, y, 18);
            }

            if (!string.IsNullOrEmpty(blueprint))
            {
                bool bp = BlueprintOk(blueprint);
                UiKit.AddText(_detail, 8, y, 620, 26, $"{(bp ? "✓" : "✗")} {L("ui.craft.blueprint")}: {L($"blueprint.{blueprint}.name")}", 18,
                    bp ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft);
                y += 30f;
            }

            return y + 8f;
        }

        // --- logic helpers ---

        private bool CanCraft(RecipeDefinition r, out string reason)
        {
            if (FreeCrafting())
            {
                // Everything the server skips in this mode is skipped here too — only the room for the result is
                // real (the server refuses a craft that does not fit, so the button would lie).
                if (!ResultFits(r))
                {
                    reason = L("ui.craft.inventory_full");
                    return false;
                }

                reason = string.Empty;
                return true;
            }

            if (!BlueprintOk(r.RequiredBlueprint))
            {
                reason = L("ui.craft.need_blueprint");
                return false;
            }

            if (!HasAll(r.Inputs))
            {
                reason = L("ui.craft.need_materials");
                return false;
            }

            // Market (barter) trades need a vendor (or your ship's trade console); everything else needs
            // the mode's crafting station.
            if (r.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Market)
            {
                if (!Game.MarketAvailable)
                {
                    reason = L("ui.craft.need_market");
                    return false;
                }

                if (!Game.MarketOfferedHere(r))
                {
                    reason = L("ui.craft.market_elsewhere");
                    return false;
                }
            }
            else if (r.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Factory)
            {
                // A factory recipe is craftable only while standing at a factory terminal that offers it on
                // its roster (the server enforces the same). Factory terminals only exist in spawned factories.
                if (IsFactoryRecipeAway(r))
                {
                    reason = L("ui.craft.go_to_factory");
                    return false;
                }
            }
            else if (!StationAvailable(r.Station))
            {
                // Per RECIPE, from the server's station set (#1070) — a forge recipe says "forge", not "workshop".
                reason = NeedStationText(StationKeyOf(r.Station));
                return false;
            }

            // The result must also FIT somewhere (server: MaterialPool.CanFit, #600) — otherwise the card
            // reads "craftable" and the button is live, but the server refuses with @inventory_full and the
            // only feedback is an easy-to-miss toast (LAN playtest: full 24-slot backpack, the inputs only
            // shrink stacks without freeing a slot, and the new tool needs a fresh one).
            if (!ResultFits(r))
            {
                reason = L("ui.craft.inventory_full");
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>Client-side dry-run of the server's <c>MaterialPool.CanFit</c> for a single craft:
        /// every output must fit on top of what is held right now — stack top-up first, then free slots,
        /// spilling into the ship's cargo hold only while aboard (the exact pool the server hands the
        /// crafted items to). Deliberately as pessimistic as the server: inputs are NOT removed first, so
        /// a craft whose inputs would free a slot still reads as blocked — the server refuses it too.</summary>
        private bool ResultFits(RecipeDefinition r)
        {
            var personal = SimStacks(Game.Personal);
            var cargo = Game.Aboard ? SimStacks(Game.Cargo) : null;
            int freePersonal = Mathf.Max(0, PersonalSlotTotal - personal.Count);
            int freeCargo = cargo != null ? Mathf.Max(0, Game.CargoSlots - cargo.Count) : 0;

            foreach (var output in r.Outputs)
            {
                if (output.Count <= 0)
                {
                    continue;
                }

                int maxStack = Game.Content.MaxStackOf(output.Item);
                int remaining = SimAdd(personal, ref freePersonal, output.Item, output.Count, maxStack);
                if (remaining > 0 && cargo != null)
                {
                    remaining = SimAdd(cargo, ref freeCargo, output.Item, remaining, maxStack);
                }

                if (remaining > 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Mutable copy of an inventory snapshot for the fit dry-run (occupied slots only).</summary>
        private sealed class SimStack
        {
            public string Item = string.Empty;
            public int Count;
        }

        private static List<SimStack> SimStacks(BlocksBeyondTheStars.Networking.Messages.NetItemStack[] slots)
        {
            var list = new List<SimStack>();
            if (slots != null)
            {
                foreach (var s in slots)
                {
                    list.Add(new SimStack { Item = s.Item, Count = s.Count });
                }
            }

            return list;
        }

        /// <summary>Adds <paramref name="count"/> of an item to the simulated stacks the way the server's
        /// <c>Inventory.Add</c> does (top up same-item stacks, then open new stacks in free slots) and
        /// returns what found no room. Outputs of one recipe compete for the same free slots.</summary>
        private static int SimAdd(List<SimStack> stacks, ref int freeSlots, string item, int count, int maxStack)
        {
            foreach (var s in stacks)
            {
                if (count <= 0)
                {
                    break;
                }

                if (s.Item != item || s.Count >= maxStack)
                {
                    continue;
                }

                int put = System.Math.Min(maxStack - s.Count, count);
                s.Count += put;
                count -= put;
            }

            while (count > 0 && freeSlots > 0)
            {
                int put = System.Math.Min(maxStack, count);
                stacks.Add(new SimStack { Item = item, Count = put });
                freeSlots--;
                count -= put;
            }

            return count;
        }

        /// <summary>Reachability tier for the list ordering (#826): 0 = craftable now (blueprint unlocked +
        /// materials owned), 1 = blueprint unlocked but materials missing, 2 = a factory recipe with no terminal
        /// offering it in reach (<paramref name="stationInReach"/> false), 3 = blueprint locked. Bench proximity is
        /// deliberately NOT part of the tier — the order must not reshuffle while walking around; the bench only
        /// keeps gating the status colour and the craft button. The factory terminal is the one exception: a factory
        /// recipe is the bulk twin of a bench recipe with the same output name, a terminal is a rare find rather than
        /// a bench you walk past, and sorted above its twin the factory card read as "this needs a factory".</summary>
        private int ReachTier(string requiredBlueprint, List<BlocksBeyondTheStars.Shared.Definitions.ItemAmount> cost, bool stationInReach = true)
        {
            if (FreeCrafting())
            {
                return 0; // everything is craftable now
            }

            if (!BlueprintOk(requiredBlueprint))
            {
                return 3;
            }

            if (!stationInReach)
            {
                return 2;
            }

            return HasAll(cost) ? 0 : 1;
        }

        /// <summary>True for a factory recipe while no spawned factory terminal in reach has it on its roster — the
        /// server's gate for factory crafts (<c>HandleCraft</c>). Shared by <see cref="CanCraft"/> and the list order,
        /// so the craft button and the card position never disagree. False for every other station.</summary>
        private static bool IsFactoryRecipeAway(RecipeDefinition r)
        {
            if (r.Station != CraftingStation.Factory)
            {
                return false;
            }

            string[] roster = System.Array.Empty<string>();
            bool atFactory = FactoryView.Instance != null && FactoryView.Instance.PlayerAtTerminal(out roster);
            return !atFactory || System.Array.IndexOf(roster, r.Key) < 0;
        }

        private bool BlueprintOk(string bp) => string.IsNullOrEmpty(bp) || Game.UnlockedBlueprints.Contains(bp);
        private bool HasAll(IEnumerable<ItemAmount> cost) => cost.All(c => Owned(c.Item) >= c.Count);

        private int Owned(string item)
        {
            int n = 0;
            if (Game.Personal != null)
            {
                foreach (var s in Game.Personal) if (s.Item == item) n += s.Count;
            }

            if (Game.Cargo != null)
            {
                foreach (var s in Game.Cargo) if (s.Item == item) n += s.Count;
            }

            return n;
        }

        private bool MatchesCategory(string item)
        {
            if (_category == "all")
            {
                return true;
            }

            var def = Game.Content.GetItem(item);
            if (def == null)
            {
                return false;
            }

            return _category switch
            {
                "weapon" => def.Tool?.Kind == ToolKind.Weapon,
                "tool" => def.Category == ItemCategory.Tool && def.Tool?.Kind != ToolKind.Weapon,
                "suit" => BlocksBeyondTheStars.Shared.State.SuitEquipment.IsSuitGear(def),
                "consumable" => def.Category == ItemCategory.Consumable,
                "component" => (def.Category == ItemCategory.Component || def.Category == ItemCategory.Material) && !BlocksBeyondTheStars.Shared.State.SuitEquipment.IsSuitGear(def),
                "machine" => def.CraftTab == "machines",
                "block" => (def.Category == ItemCategory.Block || !string.IsNullOrEmpty(def.PlacesBlock)) && def.CraftTab != "machines",
                _ => true,
            };
        }

        private bool MatchesSearch(string label)
            => string.IsNullOrEmpty(_search) || label.ToLowerInvariant().Contains(_search.ToLowerInvariant());

        /// <summary>A recipe's card and page title (#2544): its own <c>nameKey</c> when it has one (the hand-built "Wooden
        /// boat" beside the workshop's "Boat"), else the output item's name.</summary>
        private string RecipeTitle(RecipeDefinition r, string outItem)
            => !string.IsNullOrEmpty(r.NameKey) && Game?.Localizer?.Has(r.NameKey) == true ? L(r.NameKey) : ItemName(outItem);
    }
}
