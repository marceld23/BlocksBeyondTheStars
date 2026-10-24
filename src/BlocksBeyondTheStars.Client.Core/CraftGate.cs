// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What keeps a recipe from being crafted right now, as <see cref="CraftGate.Check"/> names it. The crafting
    /// menu turns each value into its reason line.</summary>
    public enum CraftBlock
    {
        /// <summary>Nothing does — the craft button is live.</summary>
        None = 0,

        /// <summary>The result does not fit in the backpack, nor (aboard) in the cargo hold (#600).</summary>
        InventoryFull = 1,

        /// <summary>The recipe's blueprint is not researched yet.</summary>
        NeedBlueprint = 2,

        /// <summary>An input is missing.</summary>
        NeedMaterials = 3,

        /// <summary>A market (barter) recipe with no vendor in reach and the player not aboard.</summary>
        NeedMarket = 4,

        /// <summary>A market recipe that is not on offer here, today.</summary>
        MarketElsewhere = 5,

        /// <summary>A factory recipe with no factory terminal in reach that has it on its roster.</summary>
        GoToFactory = 6,

        /// <summary>The recipe's crafting station is out of reach.</summary>
        NeedStation = 7,
    }

    /// <summary>
    /// What the crafting gate reads about the player right now: plain values from the last server updates, plus four
    /// answers only the Unity side can give (vendor reach, today's market stock, the factory terminal in reach, the
    /// station set). The menu passes those as delegates it binds once, so building an input costs no allocation.
    /// </summary>
    public readonly struct CraftGateInput
    {
        public CraftGateInput(GameContent content, bool creative, ICollection<string> unlockedBlueprints,
            NetItemStack[]? personal, NetItemStack[]? cargo, NetItemStack[]? equipment, bool aboard, int personalSlots, int cargoSlots,
            Func<bool> marketAvailable, Func<RecipeDefinition, bool> marketOfferedHere,
            Func<RecipeDefinition, bool> factoryAway, Func<CraftingStation, bool> stationAvailable)
        {
            Content = content;
            Creative = creative;
            UnlockedBlueprints = unlockedBlueprints;
            Personal = personal;
            Cargo = cargo;
            Equipment = equipment;
            Aboard = aboard;
            PersonalSlots = personalSlots;
            CargoSlots = cargoSlots;
            MarketAvailable = marketAvailable;
            MarketOfferedHere = marketOfferedHere;
            FactoryAway = factoryAway;
            StationAvailable = stationAvailable;
        }

        /// <summary>The loaded content (max stack sizes, recipes).</summary>
        public GameContent Content { get; }

        /// <summary>True while the player plays the Creative game mode: the server crafts for free then (#1936).</summary>
        public bool Creative { get; }

        /// <summary>The blueprints the player has researched.</summary>
        public ICollection<string> UnlockedBlueprints { get; }

        /// <summary>The backpack's occupied slots, as the server sent them.</summary>
        public NetItemStack[]? Personal { get; }

        /// <summary>The cargo hold's occupied slots (empty while not aboard — the server sends none then).</summary>
        public NetItemStack[]? Cargo { get; }

        /// <summary>The worn suit gear (#2110): an upgrade recipe may consume the piece being worn (tank II from a worn
        /// tank I), so it counts toward the inputs — never toward the room for the result.</summary>
        public NetItemStack[]? Equipment { get; }

        /// <summary>True while the player is aboard their ship: only then do crafted items spill into the cargo hold.</summary>
        public bool Aboard { get; }

        /// <summary>How many slots the backpack has.</summary>
        public int PersonalSlots { get; }

        /// <summary>How many slots the cargo hold has.</summary>
        public int CargoSlots { get; }

        /// <summary>Whether the player can barter at a market right now (aboard, or at a vendor).</summary>
        public Func<bool> MarketAvailable { get; }

        /// <summary>Whether a market recipe is on offer right here, today.</summary>
        public Func<RecipeDefinition, bool> MarketOfferedHere { get; }

        /// <summary>True for a factory recipe while no factory terminal in reach has it on its roster.</summary>
        public Func<RecipeDefinition, bool> FactoryAway { get; }

        /// <summary>True when the server says a crafting station is usable right now (hand: always).</summary>
        public Func<CraftingStation, bool> StationAvailable { get; }
    }

    /// <summary>
    /// The crafting menu's rules (#2932): whether a recipe can be crafted and why not, whether its result fits, how many
    /// the player can afford, where a card sorts and which recipe an item breaks down into. The Crafting, Tech, Ship,
    /// Inventory and Suit tabs of <c>CraftingTechShipUI</c> all ask this; it mirrors the server's craft gates
    /// (<c>HandleCraft</c>, <c>MaterialPool</c>), the server still decides. Engine-free so the headless suite pins it
    /// against the server's <c>MaterialPool</c>.
    /// </summary>
    public static class CraftGate
    {
        /// <summary>The first gate that blocks an order of <paramref name="count"/> × <paramref name="r"/>, in the order the
        /// reason line names them: in Creative only the room for the result; otherwise blueprint, materials, then the market
        /// (vendor, then today's offer), the factory terminal or the recipe's station, and last the room for the result.
        /// Materials and room are checked for the whole order, as the server does (#2937): a batch that only partly fits is
        /// refused as a whole.</summary>
        public static CraftBlock Check(RecipeDefinition r, in CraftGateInput input, int count = 1)
        {
            count = BatchOf(count);
            if (input.Creative)
            {
                // Everything the server skips in this mode is skipped here too — only the room for the result is
                // real (the server refuses a craft that does not fit, so the button would lie).
                return ResultFits(r, input, count) ? CraftBlock.None : CraftBlock.InventoryFull;
            }

            if (!BlueprintOk(r.RequiredBlueprint, input))
            {
                return CraftBlock.NeedBlueprint;
            }

            if (!HasAll(r.Inputs, input, count))
            {
                return CraftBlock.NeedMaterials;
            }

            // Market (barter) trades need a vendor (or your ship's trade console); everything else needs
            // the mode's crafting station.
            if (r.Station == CraftingStation.Market)
            {
                if (!input.MarketAvailable())
                {
                    return CraftBlock.NeedMarket;
                }

                if (!input.MarketOfferedHere(r))
                {
                    return CraftBlock.MarketElsewhere;
                }
            }
            else if (r.Station == CraftingStation.Factory)
            {
                // A factory recipe is craftable only while standing at a factory terminal that offers it on
                // its roster (the server enforces the same). Factory terminals only exist in spawned factories.
                if (input.FactoryAway(r))
                {
                    return CraftBlock.GoToFactory;
                }
            }
            else if (!input.StationAvailable(r.Station))
            {
                // Per RECIPE, from the server's station set (#1070) — a forge recipe says "forge", not "workshop".
                return CraftBlock.NeedStation;
            }

            // The result must also FIT somewhere (server: MaterialPool.CanFit, #600) — otherwise the card
            // reads "craftable" and the button is live, but the server refuses with @inventory_full and the
            // only feedback is an easy-to-miss toast (LAN playtest: full 24-slot backpack, the inputs only
            // shrink stacks without freeing a slot, and the new tool needs a fresh one).
            return ResultFits(r, input, count) ? CraftBlock.None : CraftBlock.InventoryFull;
        }

        /// <summary>Client-side dry-run of the server's <c>MaterialPool.CanFit</c> for an order of
        /// <paramref name="count"/> crafts (the outputs scaled ×N, as the server scales them, #2937):
        /// every output must fit on top of what is held right now — stack top-up first, then free slots,
        /// spilling into the ship's cargo hold only while aboard (the exact pool the server hands the
        /// crafted items to). Deliberately as pessimistic as the server: inputs are NOT removed first, so
        /// a craft whose inputs would free a slot still reads as blocked — the server refuses it too.</summary>
        public static bool ResultFits(RecipeDefinition r, in CraftGateInput input, int count = 1)
        {
            count = BatchOf(count);
            var personal = SimStacks(input.Personal);
            var cargo = input.Aboard ? SimStacks(input.Cargo) : null;
            int freePersonal = Math.Max(0, input.PersonalSlots - personal.Count);
            int freeCargo = cargo != null ? Math.Max(0, input.CargoSlots - cargo.Count) : 0;

            foreach (var output in r.Outputs)
            {
                if (output.Count <= 0)
                {
                    continue;
                }

                int maxStack = input.Content.MaxStackOf(output.Item);
                int remaining = SimAdd(personal, ref freePersonal, output.Item, output.Count * count, maxStack);
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

        /// <summary>How many of a recipe the player can currently afford, from their owned inputs. Capped at a
        /// full stack — that is also what the server accepts in one craft order.</summary>
        public static int MaxCraftable(RecipeDefinition r, in CraftGateInput input)
        {
            int cap = ItemDefinition.DefaultMaxStack;
            if (input.Creative)
            {
                return cap; // nothing is consumed — a full stack per order, the same ceiling the server clamps to
            }

            int m = cap;
            foreach (var inp in r.Inputs)
            {
                if (inp.Count <= 0)
                {
                    continue;
                }

                m = Math.Min(m, Owned(inp, input) / inp.Count);
            }

            return Math.Clamp(m, 0, cap);
        }

        /// <summary>Reachability tier for the list ordering (#826): 0 = craftable now (blueprint unlocked +
        /// materials owned), 1 = blueprint unlocked but materials missing, 2 = a factory recipe with no terminal
        /// offering it in reach (<paramref name="stationInReach"/> false), 3 = blueprint locked. Bench proximity is
        /// deliberately NOT part of the tier — the order must not reshuffle while walking around; the bench only
        /// keeps gating the status colour and the craft button. The factory terminal is the one exception: a factory
        /// recipe is the bulk twin of a bench recipe with the same output name, a terminal is a rare find rather than
        /// a bench you walk past, and sorted above its twin the factory card read as "this needs a factory".</summary>
        public static int ReachTier(string? requiredBlueprint, IEnumerable<ItemAmount> cost, bool stationInReach, in CraftGateInput input)
        {
            if (input.Creative)
            {
                return 0; // everything is craftable now
            }

            if (!BlueprintOk(requiredBlueprint, input))
            {
                return 3;
            }

            if (!stationInReach)
            {
                return 2;
            }

            return HasAll(cost, input) ? 0 : 1;
        }

        /// <summary>True when no blueprint is required or the player has researched it.</summary>
        public static bool BlueprintOk(string? bp, in CraftGateInput input)
            => string.IsNullOrEmpty(bp) || input.UnlockedBlueprints.Contains(bp);

        /// <summary>True when the player owns every listed amount <paramref name="count"/> times over (<see cref="Owned"/>) —
        /// the server's <c>MaterialPool.Has</c> over the scaled inputs.</summary>
        public static bool HasAll(IEnumerable<ItemAmount> cost, in CraftGateInput input, int count = 1)
        {
            foreach (var c in cost)
            {
                if (Owned(c, input) < c.Count * count)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>How many of <paramref name="item"/> the player may craft with — what the server's
        /// <c>MaterialPool.Count</c> counts (#2937): the backpack, the cargo hold (the hold the server sends only while
        /// aboard) and the worn gear (#2110: tank II takes the worn tank I), each with the pieces the bio lab changed
        /// (#2206, <see cref="ItemMods.IsChangedVariant"/>).</summary>
        public static int Owned(string item, in CraftGateInput input)
            => CountIn(input.Personal, item) + CountIn(input.Cargo, item) + CountIn(input.Equipment, item);

        /// <summary>How many of an ingredient the player may craft with: <see cref="Owned(string, in CraftGateInput)"/> of
        /// the item and, for a group ingredient (#2958: any log), of every other member of its group — the server's
        /// <c>MaterialPool.Count(ItemAmount)</c>.</summary>
        public static int Owned(ItemAmount need, in CraftGateInput input)
        {
            int n = Owned(need.Item, input);
            foreach (var member in input.Content.GroupMembersOf(need))
            {
                if (member != need.Item)
                {
                    n += Owned(member, input);
                }
            }

            return n;
        }

        private static int CountIn(NetItemStack[]? stacks, string item)
        {
            int n = 0;
            if (stacks != null)
            {
                foreach (var s in stacks)
                {
                    if (s.Item == item || ItemMods.IsChangedVariant(s.Item, item))
                    {
                        n += s.Count;
                    }
                }
            }

            return n;
        }

        /// <summary>The server's bound on one craft order (<c>HandleCraft</c>): at least one, at most a full stack.</summary>
        private static int BatchOf(int count) => Math.Clamp(count, 1, ItemDefinition.DefaultMaxStack);

        /// <summary>The non-market crafting recipe among <paramref name="recipes"/> that produces <paramref name="item"/> (so
        /// it can be disassembled), plus its per-craft output count; (null, 1) when the item isn't craftable.</summary>
        public static (RecipeDefinition?, int) DisassembleRecipe(string item, IEnumerable<RecipeDefinition> recipes)
        {
            foreach (var r in recipes)
            {
                if (r.Station == CraftingStation.Market || r.Inputs.Count == 0)
                {
                    continue;
                }

                var output = r.Outputs.FirstOrDefault(o => o.Item == item);
                if (output != null)
                {
                    return (r, Math.Max(1, output.Count));
                }
            }

            return (null, 1);
        }

        /// <summary>Mutable copy of an inventory snapshot for the fit dry-run (occupied slots only).</summary>
        private sealed class SimStack
        {
            public string Item = string.Empty;
            public int Count;
        }

        private static List<SimStack> SimStacks(NetItemStack[]? slots)
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

                int put = Math.Min(maxStack - s.Count, count);
                s.Count += put;
                count -= put;
            }

            while (count > 0 && freeSlots > 0)
            {
                int put = Math.Min(maxStack, count);
                stacks.Add(new SimStack { Item = item, Count = put });
                freeSlots--;
                count -= put;
            }

            return count;
        }
    }
}
