// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The beak (#3974, the birds of Earth; #3975, the dolphin's snout) and a chicken's comb, on the standard body's head. Like
    /// the hound's muzzle, the beak's upper half sits on the skull and its lower half on the hinged jaw, so it opens with every
    /// call. Its depth in front of the face is <see cref="CreatureBeaks.Depth"/> — the server's head reach reads the same
    /// number. The parts read traits any species may carry (<see cref="NetCreature.BeakStyle"/>, <see cref="NetCreature.HasComb"/>),
    /// so an older server's species — all none — builds exactly as before.
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        private static readonly Color BeakTipShade = new Color(0.78f, 0.78f, 0.78f);

        /// <summary>Half the jaw box's height as a share of the head's (<see cref="AddHeadBox"/>: the jaw is the lower 30 %).</summary>
        private const float JawHalfShare = 0.15f;

        /// <summary>The beak and the comb on the head being built (after <see cref="AddHeadBox"/>, which set <c>_jawPivot</c>).
        /// <paramref name="w"/>/<paramref name="h"/>/<paramref name="d"/> are the head box, <paramref name="headZ"/> its centre in
        /// front of the head pivot.</summary>
        private void AddBeak(NetCreature c, float unit, float w, float h, float d, float headZ)
        {
            var style = CreatureBeaks.Parse(c.BeakStyle);
            float len = CreatureBeaks.Depth(style, c.BeakLength, unit); // the server's head reach uses the same depth
            if (len > 0f && _jawPivot != null)
            {
                var mats = BeakMaterials(c, style);
                switch (style)
                {
                    case CreatureBeakStyle.Hooked:
                        AddHookedBeak(w, h, d, headZ, len, mats.Upper, mats.Lower);
                        break;
                    case CreatureBeakStyle.Rostrum:
                        AddRostrum(w, h, d, headZ, len, mats.Upper, mats.Lower);
                        break;
                    default:
                        AddConeBeak(w, h, d, headZ, len, mats.Upper, mats.Lower);
                        break;
                }
            }

            if (c.HasComb)
            {
                AddComb(w, h, d, headZ, len);
            }
        }

        /// <summary>The beak's materials: a rostrum wears the head's coat (upper) and the jaw's (lower); a bird's beak the record's
        /// colour (or the species' horn colour), its lower half a shade darker.</summary>
        private (Material Upper, Material Lower) BeakMaterials(NetCreature c, CreatureBeakStyle style)
        {
            if (style == CreatureBeakStyle.Rostrum)
            {
                return (_coat.Mat(CoatPart.Head), _coat.Mat(CoatPart.Jaw));
            }

            if (c.BeakRgb == 0)
            {
                var horn = HornMaterial(c);
                return (horn, horn);
            }

            Color beak = Rgb(c.BeakRgb);
            return (Lit(beak, null), Lit(beak * BeakTipShade, null));
        }

        /// <summary>A chicken's beak: a short cone — a base block on the skull below the eyes and a narrower tip in front of it,
        /// the lower half a flat wedge on the jaw.</summary>
        private void AddConeBeak(float w, float h, float d, float headZ, float len, Material upper, Material lower)
        {
            float faceZ = headZ + d * 0.5f;
            float bw = w * 0.3f, bh = h * 0.2f;
            float y = -h * 0.2f + bh * 0.5f; // its bottom on the jaw line, under the eyes
            AddPartTo(_headPivot, "BeakUpper", new Vector3(0f, y, faceZ + len * 0.3f), new Vector3(bw, bh, len * 0.6f), upper);
            AddPartTo(_headPivot, "BeakTip", new Vector3(0f, y - bh * 0.15f, faceZ + len * 0.78f), new Vector3(bw * 0.55f, bh * 0.6f, len * 0.44f), upper);
            AddLowerBeak(w * 0.26f, h * 0.1f, h, d, len * 0.75f, lower);
        }

        /// <summary>A parrot's or an eagle's beak: a deep upper half whose tip curves down over the lower one (a hook block at its
        /// front, hanging), and a short lower half on the jaw.</summary>
        private void AddHookedBeak(float w, float h, float d, float headZ, float len, Material upper, Material lower)
        {
            float faceZ = headZ + d * 0.5f;
            float bw = w * 0.36f, bh = h * 0.32f;
            float y = -h * 0.2f + bh * 0.5f;
            AddPartTo(_headPivot, "BeakUpper", new Vector3(0f, y, faceZ + len * 0.35f), new Vector3(bw, bh, len * 0.7f), upper);
            AddPartTo(_headPivot, "BeakHook", new Vector3(0f, y - bh * 0.35f, faceZ + len * 0.82f), new Vector3(bw * 0.6f, bh * 0.85f, len * 0.36f), upper);
            AddLowerBeak(w * 0.3f, h * 0.14f, h, d, len * 0.55f, lower);
        }

        /// <summary>A dolphin's beaked snout: a long, rounded jaw pair in the head's own colours — the upper half on the skull, the
        /// lower a touch longer on the jaw (a dolphin's lower jaw leads).</summary>
        private void AddRostrum(float w, float h, float d, float headZ, float len, Material upper, Material lower)
        {
            float faceZ = headZ + d * 0.5f;
            float bw = w * 0.42f, bh = h * 0.2f;
            float y = -h * 0.2f + bh * 0.5f;
            AddPartTo(_headPivot, "BeakUpper", new Vector3(0f, y, faceZ + len * 0.48f), new Vector3(bw, bh, len * 0.96f), upper);
            AddLowerBeak(bw * 0.92f, h * 0.17f, h, d, len, lower);
        }

        /// <summary>The beak's lower half on the jaw's pivot, in front of the jaw box (the pivot sits 0.44d behind the head's centre,
        /// so the face is 0.94d in front of it), its top on the jaw's top — it opens with every call.</summary>
        private void AddLowerBeak(float bw, float bh, float h, float d, float len, Material mat)
        {
            float jawTop = h * JawHalfShare; // the jaw box is the lower 30 % of the head (AddHeadBox)
            AddPartTo(_jawPivot, "BeakLower", new Vector3(0f, jawTop - bh * 0.5f, d * 0.94f + len * 0.5f), new Vector3(bw, bh, len), mat);
        }

        /// <summary>A chicken's comb — three red lobes along the crown, the middle one tallest — and a wattle hanging under the
        /// beak from the jaw, so it swings with the calls.</summary>
        private void AddComb(float w, float h, float d, float headZ, float beakLen)
        {
            var red = Lit(Rgb(CreatureBeaks.CombRgb), null);
            float top = h * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                float lobeH = h * (i == 1 ? 0.34f : 0.24f);
                float z = headZ + d * (0.28f - 0.24f * i);
                AddPartTo(_headPivot, "Comb" + i, new Vector3(0f, top + lobeH * 0.45f, z), new Vector3(w * 0.12f, lobeH, d * 0.26f), red);
            }

            if (_jawPivot == null)
            {
                return;
            }

            float wattleH = h * 0.26f;
            AddPartTo(_jawPivot, "Wattle", new Vector3(0f, -h * JawHalfShare - wattleH * 0.4f, d * 0.94f + beakLen * 0.15f),
                new Vector3(w * 0.14f, wattleH, d * 0.12f), red);
        }
    }
}
