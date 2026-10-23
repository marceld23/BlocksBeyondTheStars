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
        private (RecipeDefinition, int) DisassembleRecipe(string item) => CraftGate.DisassembleRecipe(item, Game.Content.Recipes.Values);

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
        // The rules live in Client.Core's CraftGate (#2932), tested headless against the server's MaterialPool; these
        // wrappers hand it what the menu knows about the player right now and keep every call site as it was.

        // The gate's four Unity-side answers, bound once from method groups so building a Gate costs no allocation.
        private System.Func<bool> _gateMarketAvailable;
        private System.Func<RecipeDefinition, bool> _gateMarketOfferedHere;
        private System.Func<RecipeDefinition, bool> _gateFactoryAway;
        private System.Func<CraftingStation, bool> _gateStationAvailable;

        /// <summary>What <see cref="CraftGate"/> reads about the player right now (a struct — no allocation).</summary>
        private CraftGateInput Gate => new CraftGateInput(Game.Content, FreeCrafting(), Game.UnlockedBlueprints,
            Game.Personal, Game.Cargo, Game.Equipment, Game.Aboard, PersonalSlotTotal, Game.CargoSlots,
            _gateMarketAvailable ??= MarketAvailableNow, _gateMarketOfferedHere ??= MarketOfferedHereNow,
            _gateFactoryAway ??= IsFactoryRecipeAway, _gateStationAvailable ??= StationAvailable);

        private bool MarketAvailableNow() => Game.MarketAvailable;
        private bool MarketOfferedHereNow(RecipeDefinition r) => Game.MarketOfferedHere(r);

        /// <summary>Whether an order of <paramref name="count"/> × <paramref name="r"/> can be crafted right now, and if not
        /// the reason line for the first gate that blocks it (<see cref="CraftGate.Check"/> keeps the order).</summary>
        private bool CanCraft(RecipeDefinition r, int count, out string reason)
        {
            var block = CraftGate.Check(r, Gate, count);
            reason = block switch
            {
                CraftBlock.InventoryFull => L("ui.craft.inventory_full"),
                CraftBlock.NeedBlueprint => L("ui.craft.need_blueprint"),
                CraftBlock.NeedMaterials => L("ui.craft.need_materials"),
                CraftBlock.NeedMarket => L("ui.craft.need_market"),
                CraftBlock.MarketElsewhere => L("ui.craft.market_elsewhere"),
                CraftBlock.GoToFactory => L("ui.craft.go_to_factory"),
                // Per RECIPE, from the server's station set (#1070) — a forge recipe says "forge", not "workshop".
                CraftBlock.NeedStation => NeedStationText(StationKeyOf(r.Station)),
                _ => string.Empty,
            };
            return block == CraftBlock.None;
        }

        /// <summary>Reachability tier for the list ordering (#826) — see <see cref="CraftGate.ReachTier"/>.</summary>
        private int ReachTier(string requiredBlueprint, List<BlocksBeyondTheStars.Shared.Definitions.ItemAmount> cost, bool stationInReach = true)
            => CraftGate.ReachTier(requiredBlueprint, cost, stationInReach, Gate);

        /// <summary>True for a factory recipe while no spawned factory terminal in reach has it on its roster — the
        /// server's gate for factory crafts (<c>HandleCraft</c>). Shared by <see cref="CanCraft"/> and the list order,
        /// so the craft button and the card position never disagree. False for every other station.</summary>
        private bool IsFactoryRecipeAway(RecipeDefinition r)
        {
            if (r.Station != CraftingStation.Factory)
            {
                return false;
            }

            string[] roster = System.Array.Empty<string>();
            bool atFactory = Game?.Views.Factories != null && Game.Views.Factories.PlayerAtTerminal(out roster);
            return !atFactory || System.Array.IndexOf(roster, r.Key) < 0;
        }

        private bool BlueprintOk(string bp) => CraftGate.BlueprintOk(bp, Gate);
        private bool HasAll(IEnumerable<ItemAmount> cost) => CraftGate.HasAll(cost, Gate);
        private int Owned(string item) => CraftGate.Owned(item, Gate);

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
