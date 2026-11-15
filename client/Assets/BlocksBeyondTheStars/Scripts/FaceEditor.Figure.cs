// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The appearance screen's "Figure" tab (#3693) — the same in the menu's Avatar Designer and in a world's console: the
    /// body colours with ← / → (one row per part that has a colour of its own: skin, torso, arms, legs — in that order the
    /// arrows used to stand swapped in the designer), the gear preview switches and the figure itself, big, in the gear
    /// they switch on. A colour stepped here goes through <see cref="SetBaseColorOf"/>: one undoable step on its part, and
    /// in a world it reaches the figure (and the other players) at once, like a base colour picked on the paint tabs. The
    /// gear switches only dress the preview figure while this tab is up — they are never saved. A host adds its own rows
    /// under the controls (<see cref="BuildFigureExtras"/>: the designer's skin name and Export).
    /// </summary>
    public sealed partial class FaceEditor
    {
        // The Figure card spans the paint and preview cards (1416 × 830): the controls column on the left, the figure right.
        private const float FigureColW = 600f, FigureRowPitch = 52f, FigureImageW = 520f, FigureImageH = 780f;

        private static readonly string[] GearKeys =
        {
            "ui.avatar.helmet", "ui.avatar.chest", "ui.avatar.legs", "ui.avatar.pack", "ui.avatar.lamp",
        };

        /// <summary>The gear-look bit each preview switch stands for (same order as <see cref="GearKeys"/>).</summary>
        private static readonly int[] GearBits =
        {
            BlocksBeyondTheStars.Shared.State.GearLook.Helmet, BlocksBeyondTheStars.Shared.State.GearLook.Chest,
            BlocksBeyondTheStars.Shared.State.GearLook.Legs, BlocksBeyondTheStars.Shared.State.GearLook.Jetpack,
            BlocksBeyondTheStars.Shared.State.GearLook.Lamp,
        };

        private readonly List<(int Subject, Image Swatch)> _figureSwatches = new List<(int, Image)>();
        private int _gearMask;

        /// <summary>True when this screen has a Figure tab: a host asked for it on the appearance screen.</summary>
        private bool HasFigureTab => !string.IsNullOrEmpty(FigureTabKey) && _charCount > 1;

        /// <summary>The Figure card's content: colour rows, gear switches (with a figure to dress), the host's rows, the
        /// figure — the preview card's figure, shown big while this tab is up.</summary>
        private void BuildFigureCard(Transform card)
        {
            const float x = 24f;
            float y = 20f;
            UiKit.AddKicker(card, x, y, FigureColW, L("ui.avatar.colors"));
            y += 34f;
            for (int i = 0; i < _charCount; i++)
            {
                if (!string.IsNullOrEmpty(_subjects[i].ColorLabelKey) && _subjects[i].SetBaseColor != null)
                {
                    FigureColorRow(card, ref y, i);
                }
            }

            if (_preview != null)
            {
                y += 12f;
                UiKit.AddKicker(card, x, y, FigureColW, L("ui.avatar.gear_preview"));
                y += 34f;
                for (int i = 0; i < GearKeys.Length; i++)
                {
                    FigureGearRow(card, ref y, i);
                }

                var image = new GameObject("FigureImage", typeof(RectTransform));
                image.transform.SetParent(card, false);
                UiKit.Place(image, 1416f - 84f - FigureImageW, 24f, FigureImageW, FigureImageH);
                var raw = image.AddComponent<RawImage>();
                raw.texture = _preview.Texture;
                raw.raycastTarget = false;
            }

            BuildFigureExtras?.Invoke(card, y + 12f);
        }

        /// <summary>One body colour: its name, the swatch, then ← (back) and → (on) through the palette.</summary>
        private void FigureColorRow(Transform card, ref float y, int subject)
        {
            UiKit.AddText(card, 24f, y, 220f, 44f, L(_subjects[subject].ColorLabelKey), 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            var swatch = UiKit.AddImage(card, 252f, y + 6f, 32f, 32f, UiKit.SolidSprite, _subjects[subject].GetBaseColor?.Invoke() ?? Color.white);
            swatch.raycastTarget = false;
            _figureSwatches.Add((subject, swatch));
            UiKit.AddTierButton(card, 300f, y, 80f, 44f, "←", () => StepFigureColor(subject, -1), ButtonTier.Quiet);
            UiKit.AddTierButton(card, 392f, y, 80f, 44f, "→", () => StepFigureColor(subject, 1), ButtonTier.Quiet);
            y += FigureRowPitch;
        }

        private void StepFigureColor(int subject, int dir)
        {
            var s = _subjects[subject];
            if (s.GetBaseColor == null || _hudMode)
            {
                return;
            }

            SetBaseColorOf(subject, AppearancePalette.Next(s.GetBaseColor(), dir));
        }

        /// <summary>One gear preview switch: the suit part is shown on the figure, never saved.</summary>
        private void FigureGearRow(Transform card, ref float y, int which)
        {
            UiKit.AddText(card, 24f, y, 300f, 44f, L(GearKeys[which]), 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddSwitch(card, 330f, y + 6f, (_gearMask & GearBits[which]) != 0, on =>
            {
                _gearMask = on ? _gearMask | GearBits[which] : _gearMask & ~GearBits[which];
                ApplyPreviewGear();
            });
            y += FigureRowPitch;
        }

        /// <summary>The colour rows follow every colour change (a step here, a swatch on a paint tab, an undo, an outfit).</summary>
        private void RefreshFigureSwatches()
        {
            foreach (var (subject, swatch) in _figureSwatches)
            {
                if (swatch != null && subject < _subjects.Count && !_hudMode && _subjects[subject].GetBaseColor != null)
                {
                    swatch.color = _subjects[subject].GetBaseColor();
                }
            }
        }

        /// <summary>The gear goes on the preview figure only while the Figure tab is up: on the paint tabs a helmet would
        /// hide the face being painted.</summary>
        private void ApplyPreviewGear()
        {
            var avatar = _preview != null ? _preview.GetComponentInChildren<PlayerAvatar>(true) : null;
            if (avatar != null)
            {
                avatar.SetGear(_figureShown ? _gearMask : 0);
            }
        }
    }
}
