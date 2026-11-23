// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The colour-slot palette of the two form editors: five buttons (main, second, accent, dark, glow,
    /// <see cref="ShapeColourSlots"/>) in the editors' uGUI-in-code style, each with its digit, a chip of its canvas colour
    /// and a localised tooltip. The canvas draws a cell in its slot's colour (<see cref="CanvasColour"/>). A slot is not the
    /// colour the world shows — that comes from the block the form is made of — so the canvas colours only tell the slots
    /// apart.
    /// </summary>
    internal static class ShapeSlotPalette
    {
        private static readonly string[] Keys = { "main", "second", "accent", "dark", "glow" };

        /// <summary>The canvas colour of a stored cell value (1..15); main and 6..f in the editors' cyan.</summary>
        public static Color CanvasColour(int value)
        {
            switch (ShapeColourSlots.SlotOfValue(value))
            {
                case ShapeColourSlots.Second: return new Color(0.80f, 0.96f, 1.00f);
                case ShapeColourSlots.Accent: return new Color(1.00f, 0.58f, 0.22f);
                case ShapeColourSlots.Dark: return new Color(0.14f, 0.40f, 0.56f);
                case ShapeColourSlots.Glow: return new Color(1.00f, 0.92f, 0.35f);
                default: return UiKit.Cyan;
            }
        }

        /// <summary>Builds the five buttons in a row at (x, y) in <paramref name="card"/>, <paramref name="w"/> wide in all;
        /// <paramref name="onPick"/> gets the slot (1..5). Main starts selected. Returns the buttons (index = slot − 1).</summary>
        public static Button[] Build(Transform card, float x, float y, float w, float h, Func<string, string> localize, Action<int> onPick)
        {
            const float gap = 6f;
            float bw = (w - (gap * (ShapeColourSlots.Count - 1))) / ShapeColourSlots.Count;
            var buttons = new Button[ShapeColourSlots.Count];
            for (int i = 0; i < buttons.Length; i++)
            {
                int slot = i + 1;
                string key = "ui.shape.slot." + Keys[i];
                var button = UiKit.AddTierButton(card, x + (i * (bw + gap)), y, bw, h, slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    () =>
                    {
                        Select(buttons, slot);
                        onPick?.Invoke(slot);
                    }, i == 0 ? ButtonTier.Primary : ButtonTier.Quiet);
                UiKit.AddImage(button.transform, 6f, h - 10f, bw - 12f, 5f, UiKit.SolidSprite, CanvasColour(slot));
                UiTooltip.Attach(button.gameObject, () => localize(key), () => localize(key + "_tip"));
                buttons[i] = button;
            }

            return buttons;
        }

        /// <summary>Marks <paramref name="slot"/> as the chosen one (look only).</summary>
        public static void Select(Button[] buttons, int slot)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                UiKit.StyleTier(buttons[i], i == slot - 1 ? ButtonTier.Primary : ButtonTier.Quiet);
            }
        }
    }
}
