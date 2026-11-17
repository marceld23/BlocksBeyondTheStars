// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Client.Feel;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The inventory's finish: a slot tells what it holds on a rest of the pointer (<see cref="UiTooltip"/>), a slot can
    /// be dragged onto another with the mouse (<see cref="UiDragSlot"/> — the same move the two clicks send), and a
    /// piece of suit gear shows its numbers next to the piece it would replace (<see cref="GearCompare"/>).
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        /// <summary>Gives a backpack / quick-bar slot its tooltip and its drag and drop.</summary>
        private void AttachSlotFeel(Button slot, int index)
        {
            if (slot == null)
            {
                return;
            }

            string item = Game.ItemInSlot(index);
            bool filled = !string.IsNullOrEmpty(item);
            UiDragSlot.Attach(slot.gameObject, "inv", index, filled, from => DropOnInventorySlot(from, index));
            if (filled)
            {
                UiTooltip.Attach(slot.gameObject, () => SlotTitle(index), () => SlotBody(index));
            }
        }

        /// <summary>A slot was dragged onto another: the move (or swap) the click-to-pick would have sent.</summary>
        private void DropOnInventorySlot(int from, int to)
        {
            ClearPick();
            if (from == to)
            {
                return;
            }

            Game.Network?.SendMoveItem(from, to); // the server validates, as for the two clicks
            ClientAudio.Instance?.Cue("ui_click");
        }

        private string SlotTitle(int index)
        {
            string item = Game.ItemInSlot(index);
            if (string.IsNullOrEmpty(item))
            {
                return null;
            }

            int count = Game.CountInSlot(index);
            return count > 1 ? $"{ItemName(item)}  ×{count}" : ItemName(item);
        }

        private string SlotBody(int index)
        {
            string item = Game.ItemInSlot(index);
            return string.IsNullOrEmpty(item) ? string.Empty : UiTooltip.FirstSentence(Desc($"item.{ItemKey.Base(item)}.desc"));
        }

        /// <summary>The detail pane's gear block: what a wearable piece does, and — when another piece is worn in its
        /// slot — how each number differs from that one (▲ better, ▼ worse). Returns the next y.</summary>
        private float AddGearComparison(string item, float y)
        {
            var def = Game.Content.GetItem(ItemKey.Base(item));
            if (def == null || string.IsNullOrEmpty(def.EquipSlot))
            {
                return y;
            }

            var worn = WornCounterpart(def);
            if (worn == null)
            {
                string effects = EffectSummary(def);
                if (effects.Length == 0)
                {
                    return y;
                }

                var line = UiKit.AddText(_detail, 8, y, 620, 26, effects, 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                line.horizontalOverflow = HorizontalWrapMode.Wrap;
                float h = Mathf.Max(26f, line.preferredHeight);
                line.rectTransform.sizeDelta = new Vector2(620f, h);
                return y + h + 8f;
            }

            var title = UiKit.AddText(_detail, 8, y, 620, 26, L("ui.equip.compare_title").Replace("{name}", ItemName(worn.Key)),
                18, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.FitLabel(title, 12, 18);
            y += 30f;
            foreach (var cmp in GearCompare.Lines(def, worn))
            {
                UiKit.AddText(_detail, 16, y, 380, 26, L(cmp.EffectKey).Replace("{value}", cmp.Value.ToString()), 18, UiKit.TextCol,
                    TextAnchor.MiddleLeft);
                string delta = cmp.Delta > 0 ? $"▲ {cmp.Delta}" : cmp.Delta < 0 ? $"▼ {-cmp.Delta}" : "=";
                UiKit.AddText(_detail, 404, y, 120, 26, delta, 18,
                    cmp.Delta > 0 ? UiKit.Ok : cmp.Delta < 0 ? UiKit.Warn : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 28f;
            }

            return y + 8f;
        }

        /// <summary>The pin button of a recipe's detail page: pins the recipe to the HUD (one at a time) or takes it off
        /// again. The pin lives in the player's settings, so it is still there after a restart.</summary>
        private void AddPinButton(string recipeKey, float y)
        {
            var settings = Game.Settings;
            if (settings == null)
            {
                return;
            }

            bool pinned = settings.PinnedRecipe == recipeKey;
            var pin = UiKit.AddButton(_detail, 300, y, 320, 56, L(pinned ? "ui.craft.unpin" : "ui.craft.pin"), () =>
            {
                settings.PinnedRecipe = pinned ? string.Empty : recipeKey;
                settings.Save();
                RebuildDetail();
            }, "map_mark_star");
            if (pinned)
            {
                Highlight(pin);
            }
        }

        /// <summary>The worn piece <paramref name="candidate"/> would replace (same body slot, another item), or null.</summary>
        private ItemDefinition WornCounterpart(ItemDefinition candidate)
            => Game.WornKeys()
                .Select(key => Game.Content.GetItem(ItemKey.Base(key)))
                .FirstOrDefault(worn => GearCompare.Replaces(candidate, worn));
    }
}
