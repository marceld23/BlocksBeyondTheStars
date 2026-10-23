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
    /// The Crafting tab of <see cref="CraftingTechShipUI"/>: the recipe list, the colour and shape pages with their
    /// swatch and form grids (the colour palette the hotbar shares), the custom-form editor hand-off and the recipe
    /// detail with its craft count.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        private int _craftCount = 1;          // how many of the selected recipe to craft at once
        private string _craftCountKey = string.Empty; // recipe the count belongs to (reset on a new selection)

        // Colour palette for the always-available Dye/Glow action (swatch grid; 0xRRGGBB): a spread of
        // vivid hues plus a greyscale row, matching the user's "palette + fine picker" choice. The hotbar's colour
        // menu (HotbarActionUi) offers this same list. #2826: an amber after the orange and a warm white closing the
        // hues — lamplight colours for a cosy room; seven columns keep the grid at three rows on both surfaces.
        internal static readonly int[] ColorPalette =
        {
            0xE03A3A, 0xE07A2A, 0xFFB050, 0xE0C020, 0x8FD030, 0x35C04A, 0x2FB0A0,
            0x2F8FE0, 0x2F4FE0, 0x6A3FE0, 0xB23FE0, 0xE03FA0, 0xE83060, 0xFFD8A8,
            0xFFFFFF, 0xC8D0D8, 0x8A94A0, 0x4A5260, 0x2A3038, 0x12161A,
        };

        /// <summary>Swatches per row of <see cref="ColorPalette"/> (#2826: 7 × 64 px + gaps = 516 px in the 636 px pane).</summary>
        internal const int SwatchColumns = 7;

        private static Color RgbToColor(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        private float BuildCraftingList()
        {
            if (_category == "color")
            {
                return BuildColorList();
            }

            if (_category == "shape")
            {
                return BuildShapeList();
            }

            float y = 0f;
            var entries = new List<(RecipeDefinition r, string outItem, bool can)>();
            foreach (var r in Game.Content.Recipes.Values)
            {
                var outItem = r.Outputs.FirstOrDefault();
                if (outItem == null)
                {
                    continue;
                }

                // Market (barter) recipes live only under the "market" category; everything else hides them. And only
                // the deals on offer HERE: aboard that is the themeless ones — the grocer's stew is bought at the grocer,
                // it used to show in the ship as "Baubar" with a live button the server then refused.
                bool isMarket = r.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Market;
                if (_category == "market")
                {
                    if (!isMarket || !Game.MarketOfferedHere(r) || !MatchesSearch(RecipeTitle(r, outItem.Item)))
                    {
                        continue;
                    }
                }
                else if (isMarket || !MatchesCategory(outItem.Item) || !MatchesSearch(RecipeTitle(r, outItem.Item)))
                {
                    continue;
                }

                bool can = CanCraft(r, 1, out _);
                if (_craftableOnly && !can)
                {
                    continue;
                }

                entries.Add((r, outItem.Item, can));
            }

            // Reachability ordering (#826): craftable first, blueprint-unlocked-but-missing-materials in the
            // middle, blueprint-locked last; within a tier A–Z by the card title in the game language (#2566 — it
            // was "simpler recipes first", which read as no order at all; with "Craftable now" on, the list is
            // simply alphabetical). The within-tier key is static (inventory-independent), so cards only move when
            // they change tier. Market keeps the authored order — barter offers are a vendor's stall, not a
            // progression list.
            // A factory recipe is the bulk twin of a bench recipe and carries the same output name, so while no
            // terminal offering it is in reach it sorts BEHIND its twin (tier 2), and with one in reach the twins share
            // a title and the bench card still comes first — a factory "Eisenplatte" above the workbench one sent a
            // pilot repairing at the cockpit looking for a factory (2026-10-09).
            if (_category != "market")
            {
                entries = entries
                    .OrderBy(e => ReachTier(e.r.RequiredBlueprint, e.r.Inputs, stationInReach: !IsFactoryRecipeAway(e.r)))
                    .ThenBy(e => RecipeTitle(e.r, e.outItem), NameOrder)
                    .ThenBy(e => e.r.Station == CraftingStation.Factory ? 1 : 0)
                    .ToList();
            }

            foreach (var e in entries)
            {
                string key = e.r.Key;
                bool trade = e.r.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Market;
                string badge = trade
                    ? (e.can ? L("ui.craft.tradable") : L("ui.vendor.cant_afford"))
                    : (e.can ? L("ui.craft.ready") : L("ui.craft.blocked"));
                // The factory twin names its terminal — two cards reading "Eisenplatte" with the same icon told a
                // player nothing about which one the ship's workshop makes.
                string cardTitle = RecipeTitle(e.r, e.outItem);
                if (e.r.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Factory)
                {
                    cardTitle += " · " + L("ui.craft.station_factory");
                }

                AddCard(y, cardTitle, IconFor(e.outItem), badge,
                    e.can ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), key, () => { _selected = key; RebuildDetail(); }, contentKey: e.outItem);
                y += 88f;
            }

            return y;
        }

        /// <summary>Lists the player's tintable building materials for the always-available Dye/Glow action.</summary>
        private float BuildColorList()
        {
            float y = 0f;
            var seen = new HashSet<string>();
            if (Game.Personal != null)
            {
                foreach (var s in Game.Personal)
                {
                    if (s == null || string.IsNullOrEmpty(s.Item) || !seen.Add(s.Item))
                    {
                        continue;
                    }

                    var item = Game.Content.GetItem(s.Item);
                    if (item == null || string.IsNullOrEmpty(item.PlacesBlock))
                    {
                        continue;
                    }

                    var blk = Game.Content.GetBlock(item.PlacesBlock);
                    if (blk == null || !blk.Tintable || !MatchesSearch(ItemName(s.Item)))
                    {
                        continue;
                    }

                    string key = "color:" + s.Item;
                    AddCard(y, ItemName(s.Item), IconFor(s.Item), "×" + Owned(s.Item), UiKit.CyanDim, key,
                        () => { _selected = key; RebuildDetail(); }, contentKey: s.Item);
                    y += 88f;
                }
            }

            if (y == 0f)
            {
                UiKit.AddText(_listContent, 8, 8, 700, 30, L("ui.color.none"), 22, UiKit.CyanDim, TextAnchor.UpperLeft);
                y = 40f;
            }

            return y;
        }

        /// <summary>Detail pane for the Dye/Glow action: a colour palette that recolours the selected material
        /// (top) or turns it into a coloured light source consuming a crystal (bottom).</summary>
        private float DetailColor()
        {
            string src = _selected.Substring("color:".Length);
            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 40, ItemName(src), 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 46f;
            UiKit.AddText(_detail, 8, y, 620, 48, "×" + Owned(src) + "   ·   " + L("ui.color.help"), 18, UiKit.CyanDim, TextAnchor.UpperLeft).horizontalOverflow = HorizontalWrapMode.Wrap;
            y += 54f;

            // Dye (surface tint) — a free 1:1 recolour.
            UiKit.AddText(_detail, 8, y, 620, 28, L("ui.color.dye"), 22, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 34f;
            y = AddSwatchGrid(y, src, glow: false);
            y += 14f;

            // Glow (coloured light source) — consumes a crystal per unit.
            UiKit.AddText(_detail, 8, y, 620, 28, L("ui.color.glow"), 22, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 32f;
            int crystals = Owned("crystal");
            UiKit.AddText(_detail, 8, y, 620, 24, L("ui.color.glow_cost") + "  " + crystals + "/1", 18,
                crystals > 0 ? UiKit.CyanDim : new Color(1f, 0.6f, 0.4f), TextAnchor.UpperLeft);
            y += 30f;
            y = AddSwatchGrid(y, src, glow: true);
            return y + 8f;
        }

        /// <summary>A grid of colour swatch buttons; clicking one sends the Dye (or Glow) craft for the source.</summary>
        private float AddSwatchGrid(float y, string src, bool glow)
        {
            const int cols = SwatchColumns;
            const float sw = 64f, gap = 10f;
            bool blocked = glow && Owned("crystal") < 1;
            for (int i = 0; i < ColorPalette.Length; i++)
            {
                int rgb = ColorPalette[i];
                float bx = 8 + (i % cols) * (sw + gap);
                float by = y + (i / cols) * (sw + gap);
                var b = UiKit.AddButton(_detail, bx, by, sw, sw, string.Empty,
                    () => { Game.Network.SendTintCraft(src, glow ? 0 : rgb, glow ? rgb : 0); });
                if (b.GetComponent<Image>() is { } img)
                {
                    img.color = RgbToColor(rgb);
                }

                if (blocked)
                {
                    SetInteractable(b, false);
                }
            }

            int rows = (ColorPalette.Length + cols - 1) / cols;
            return y + rows * (sw + gap);
        }

        // The forms the always-available "Shape" action can craft (shape index → locale key). 0 = plain cube
        // (reverts a shaped material). Indices match BlocksBeyondTheStars.Shared.World.BlockShape.
        // The pickable forms are the shared BuiltInForms.Options (#1975): one list for this tab, the hotbar's form
        // menu and the build editors' form picker.

        /// <summary>Lists the player's shapeable building materials for the always-available Shape action.</summary>
        private float BuildShapeList()
        {
            float y = 0f;
            var seen = new HashSet<string>();
            if (Game.Personal != null)
            {
                foreach (var s in Game.Personal)
                {
                    if (s == null || string.IsNullOrEmpty(s.Item) || !seen.Add(s.Item))
                    {
                        continue;
                    }

                    var item = Game.Content.GetItem(s.Item);
                    if (item == null || string.IsNullOrEmpty(item.PlacesBlock))
                    {
                        continue;
                    }

                    var blk = Game.Content.GetBlock(item.PlacesBlock);
                    if (blk == null || !blk.Shapeable || !MatchesSearch(ItemName(s.Item)))
                    {
                        continue;
                    }

                    string key = "shape:" + s.Item;
                    AddCard(y, ItemName(s.Item), IconFor(s.Item), "×" + Owned(s.Item), UiKit.CyanDim, key,
                        () => { _selected = key; RebuildDetail(); }, contentKey: s.Item);
                    y += 88f;
                }
            }

            if (y == 0f)
            {
                UiKit.AddText(_listContent, 8, 8, 700, 30, L("ui.shape.none"), 22, UiKit.CyanDim, TextAnchor.UpperLeft);
                y = 40f;
            }

            return y;
        }

        /// <summary>Detail pane for the Shape action: a grid of form buttons that re-form the selected material
        /// (the current form is shown disabled; "cube" reverts it). Free 1:1, like dyeing. The placement
        /// direction is decided from where the player faces, so it isn't chosen here.</summary>
        private float DetailShape()
        {
            string src = _selected.Substring("shape:".Length);
            int current = ItemKey.Shape(src);
            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 40, ItemName(src), 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 46f;
            UiKit.AddText(_detail, 8, y, 620, 48, "×" + Owned(src) + "   ·   " + L("ui.shape.help"), 18, UiKit.CyanDim, TextAnchor.UpperLeft).horizontalOverflow = HorizontalWrapMode.Wrap;
            y += 54f;
            return AddShapeGrid(y, src, current);
        }

        /// <summary>A grid of form buttons; clicking one sends the Shape craft for the source material. The
        /// button for the material's current form is shown disabled.</summary>
        private float AddShapeGrid(float y, string src, int current)
        {
            const int cols = 2;
            const float bw = 300f, bh = 50f, gap = 10f;
            for (int i = 0; i < BuiltInForms.Options.Count; i++)
            {
                int shape = BuiltInForms.Options[i].Shape;
                string loc = BuiltInForms.Options[i].LocKey;
                float bx = 8 + (i % cols) * (bw + gap);
                float by = y + (i / cols) * (bh + gap);
                bool isCurrent = shape == current;
                int target = shape; // capture for the closure
                var b = UiKit.AddButton(_detail, bx, by, bw, bh, L(loc),
                    () => { Game.Network.SendShapeCraft(src, target); });
                if (isCurrent)
                {
                    SetInteractable(b, false);
                }
            }

            int rows = (BuiltInForms.Options.Count + cols - 1) / cols;
            y += rows * (bh + gap) + 16f;
            return AddCustomFormSection(y, src, current);
        }

        /// <summary>"My forms" (#845): the player's own saved forms, craftable out of the selected material
        /// exactly like a built-in one. Only shown once the shaping tool is in the inventory — that tool is
        /// what unlocks designing; the built-in forms above stay free for everyone.</summary>
        private float AddCustomFormSection(float y, string src, int current)
        {
            UiKit.AddText(_detail, 8, y, 620, 30, L("ui.shape.custom.section"), 20, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 36f;

            if (!HasShapeTool())
            {
                UiKit.AddText(_detail, 8, y, 620, 44, L("ui.shape.custom.locked"), 16, UiKit.CyanDim, TextAnchor.UpperLeft)
                    .horizontalOverflow = HorizontalWrapMode.Wrap;
                return y + 50f;
            }

            const float bw = 300f, bh = 50f, gap = 10f;
            UiKit.AddButton(_detail, 8, y, bw, bh, L("ui.shape.custom.design_new"), () => OpenFormEditor(src, string.Empty, string.Empty));
            y += bh + gap;

            var forms = CustomShapeLibrary.List();
            if (forms.Count == 0)
            {
                UiKit.AddText(_detail, 8, y, 620, 44, L("ui.shape.custom.empty"), 16, UiKit.CyanDim, TextAnchor.UpperLeft)
                    .horizontalOverflow = HorizontalWrapMode.Wrap;
                return y + 50f;
            }

            for (int i = 0; i < forms.Count; i++)
            {
                var form = forms[i];
                float bx = 8 + (i % 2) * (bw + gap);
                float by = y + (i / 2) * (bh + gap);
                // Craft it straight from the material; the editor is only needed to CHANGE a form.
                var b = UiKit.AddButton(_detail, bx, by, bw - 108f, bh, form.Name,
                    () => Game.Network.SendCustomShapeCraft(src, form.Voxels, form.Name));
                UiKit.AddButton(_detail, bx + bw - 100f, by, 48f, bh, "✎", () => OpenFormEditor(src, form.Voxels, form.Name));
                // Stamp it onto a blank stencil to give it away (#846) — the same craft, a different source.
                UiKit.AddButton(_detail, bx + bw - 48f, by, 48f, bh, "▤", () => StampStencil(form.Voxels, form.Name));

                // A form the material already carries cannot be re-crafted into itself.
                if (current != 0 && Game.CustomShapes != null
                    && Game.CustomShapes.TryGetVoxels(current, out string currentVoxels) && currentVoxels == form.Voxels)
                {
                    SetInteractable(b, false);
                }
            }

            int formRows = (forms.Count + 1) / 2;
            return y + formRows * (bh + gap) + 8f;
        }

        /// <summary>True when the shaping tool is anywhere in the player's inventory (holding it is only
        /// needed for the in-world actions). The server re-checks this — the button is a courtesy, not a gate.</summary>
        private bool HasShapeTool()
        {
            foreach (var stack in Game.Personal)
            {
                if (stack != null && ItemKey.Base(stack.Item) == "shape_tool")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Stamps a form onto a blank stencil so it can be handed to another player (#846). The
        /// server runs the same 1:1 exchange it runs for a material — the stencil's item key just carries a
        /// form index instead of a block doing so.</summary>
        private void StampStencil(string voxels, string name)
        {
            bool hasBlank = false;
            foreach (var stack in Game.Personal)
            {
                if (stack != null && stack.Item == "shape_stencil")
                {
                    hasBlank = true;
                    break;
                }
            }

            if (!hasBlank)
            {
                Game.ShowMessage(L("ui.shape.custom.stencil_none"));
                return;
            }

            Game.Network.SendCustomShapeCraft("shape_stencil", voxels, name);
            Game.ShowMessage(L("ui.shape.custom.stencil_done"));
        }

        /// <summary>Opens the form editor from the crafting menu, where a material IS selected — so Apply
        /// crafts the form out of it (and keeps it in the library).</summary>
        private void OpenFormEditor(string src, string voxels, string name)
        {
            var go = new GameObject("ShapeEditor");
            var editor = go.AddComponent<ShapeEditor>();
            editor.Game = Game;
            editor.InitialVoxels = voxels;
            editor.InitialName = name;
            editor.Localizer = key => Game.Localizer?.Get(key) ?? key;
            editor.OnSaveDesign = CustomShapeLibrary.Save;
            editor.LibraryProvider = CustomShapeLibrary.List;
            editor.OnApply = (v, n) =>
            {
                CustomShapeLibrary.Save(v, n);
                Game.Network.SendCustomShapeCraft(src, v, n);
            };
            editor.OnClosed = RebuildList; // the new form should show up in the list behind it
        }

        private float DetailCrafting()
        {
            if (_selected.StartsWith("color:", System.StringComparison.Ordinal))
            {
                return DetailColor();
            }

            if (_selected.StartsWith("shape:", System.StringComparison.Ordinal))
            {
                return DetailShape();
            }

            var r = Game.Content.GetRecipe(_selected);
            if (r == null)
            {
                return 0f;
            }

            var outItem = r.Outputs.First();
            float y = 0f;
            string heading = RecipeTitle(r, outItem.Item) + (outItem.Count > 1 ? $"  ×{outItem.Count}" : "");
            if (r.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Factory)
            {
                heading += " · " + L("ui.craft.station_factory"); // the card's suffix, so the page matches the card
            }

            UiKit.AddText(_detail, 8, y, 620, 40, heading, 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 48f;
            string desc = Desc(string.IsNullOrEmpty(r.DescriptionKey) ? $"item.{outItem.Item}.desc" : r.DescriptionKey);
            if (!string.IsNullOrEmpty(desc))
            {
                y = AddDetailDescription(desc, y);
            }

            UiKit.AddText(_detail, 8, y, 620, 28, L("ui.craft.needs"), 22, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 34f;
            foreach (var inp in r.Inputs)
            {
                y = IngredientRow(inp, y, 20);
            }

            y += 8f;
            // "Station: [icon] Workbench ✓/✗" — the BLOCK's name and tile (#1071), ticked when the server says
            // it is in reach right now (hand/market/factory keep their own wording). #2585: aboard, a station a module
            // provides is that MODULE ("Refinery module ✗", with the module's icon) — a forge in the ship is decoration.
            {
                string stKey = StationKeyOf(r.Station);
                bool gated = r.Station != CraftingStation.Hand && r.Station != CraftingStation.Market && r.Station != CraftingStation.Factory;
                string stModule = gated && AboardShipNow() ? CraftStationText.ModuleKey(stKey) : null;
                var stSprite = !gated ? null : stModule != null ? IconResolver.Resolve(stModule, Game) : StationSprite(stKey);
                float sx = 8f;
                string label = L("ui.craft.station") + ": ";
                if (stSprite != null)
                {
                    UiKit.AddText(_detail, sx, y, 100, 26, label, 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                    sx += 92f;
                    UiKit.AddIconSprite(_detail, sx, y + 1, 22, stSprite, Color.white);
                    sx += 26f;
                    label = string.Empty;
                }

                string stName = gated ? CraftStationText.StationLineName(stKey, StationName(stKey), AboardShipNow(), L) : L("ui.craft.station_" + stKey);
                bool ok = !gated || StationAvailable(r.Station);
                UiKit.AddText(_detail, sx, y, 620 - sx, 26, label + stName + (gated ? (ok ? "  ✓" : "  ✗") : string.Empty), 18,
                    ok ? UiKit.CyanDim : new Color(1f, 0.8f, 0.4f), TextAnchor.UpperLeft);
            }

            y += 30f;
            if (!string.IsNullOrEmpty(r.RequiredBlueprint))
            {
                bool bp = BlueprintOk(r.RequiredBlueprint);
                UiKit.AddText(_detail, 8, y, 620, 26, $"{(bp ? "✓" : "✗")} {L("ui.craft.blueprint")}: {L($"blueprint.{r.RequiredBlueprint}.name")}", 18,
                    bp ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft);
                y += 30f;
            }

            y += 10f;
            if (FreeCrafting())
            {
                UiKit.AddText(_detail, 8, y, 620, 26, L("ui.craft.free"), 18, UiKit.Ok, TextAnchor.UpperLeft);
                y += 30f;
            }

            // Quantity stepper — craft more than one at a time (the server crafts N in a single action).
            if (_craftCountKey != r.Key)
            {
                _craftCount = 1;
                _craftCountKey = r.Key;
            }

            int maxCraft = Mathf.Max(1, MaxCraftable(r));
            _craftCount = Mathf.Clamp(_craftCount, 1, maxCraft);

            // The gate checks the whole ×N order, like the server (#2937): a batch that only partly fits is refused.
            bool can = CanCraft(r, _craftCount, out string reason);
            if (!can)
            {
                y = AddWrappedDetailLine(reason, y, 26f, 18, new Color(1f, 0.6f, 0.4f)) + 4f;
            }

            UiKit.AddButton(_detail, 8, y, 50, 56, "-", () => { _craftCount = Mathf.Max(1, _craftCount - 1); RebuildDetail(); });
            UiKit.AddText(_detail, 62, y, 92, 56, _craftCount.ToString(), 24, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(_detail, 158, y, 50, 56, "+", () => { _craftCount = Mathf.Min(maxCraft, _craftCount + 1); RebuildDetail(); });
            UiKit.AddButton(_detail, 214, y, 74, 56, L("ui.craft.max"), () => { _craftCount = maxCraft; RebuildDetail(); });
            y += 66f;

            int n = _craftCount;
            string verb = r.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Market ? L("ui.vendor.trade") : L("ui.action.craft");
            var btn = UiKit.AddButton(_detail, 8, y, 280, 56, verb + (maxCraft > 1 ? " ×" + n : string.Empty), () => { Game.Network.SendCraft(r.Key, n); });
            SetInteractable(btn, can);
            y += 70f;
            return y;
        }

        /// <summary>How many of a recipe the player can currently afford, from their owned inputs. Capped at a
        /// full stack — that is also what the server accepts in one craft order.</summary>
        private int MaxCraftable(RecipeDefinition r) => CraftGate.MaxCraftable(r, Gate);
    }
}
