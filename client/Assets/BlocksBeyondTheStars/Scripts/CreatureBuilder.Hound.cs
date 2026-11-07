// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Schuppenhund (#3327, Jafar's sketch): the standard quadruped body plus what makes it read as a dog — a muzzle (the
    /// upper half on the skull, the lower half on the hinged jaw, so it opens with the voice and the bite), a dark nose, two
    /// rows of teeth, two ears in the world's rolled style (registered as the rig's ears, so the animator sways, flicks, pricks
    /// and pins them), three claws on every foot and a long, thin tail. The scale hide on every part comes from
    /// <c>CreatureHides</c>. The parts read the same traits any species may carry (<see cref="NetCreature.SnoutLength"/>,
    /// <see cref="NetCreature.EarStyle"/>, <see cref="NetCreature.HasTeeth"/>, <see cref="NetCreature.HasClaws"/>), so an older
    /// server's species — all zero — builds exactly as before.
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        private static readonly Color HoundToothColor = new Color(0.95f, 0.93f, 0.84f);
        private static readonly Color HoundNoseColor = new Color(0.08f, 0.07f, 0.07f);

        /// <summary>The muzzle, nose, teeth and ears on the head being built (after <see cref="AddHeadBox"/>, which set
        /// <c>_jawPivot</c> and <c>_faceZ</c>). <paramref name="w"/>/<paramref name="h"/>/<paramref name="d"/> are the head box,
        /// <paramref name="headZ"/> its centre in front of the head pivot.</summary>
        private void AddHoundHead(NetCreature c, float unit, float w, float h, float d, float headZ)
        {
            float muzzle = CreatureFootprint.HoundMuzzleDepth(c.SnoutLength, unit); // the server's head reach uses the same depth
            if (muzzle > 0f && _jawPivot != null)
            {
                AddHoundMuzzle(c, unit, w, h, d, headZ, muzzle);
            }

            AddHoundEars(c, unit, w, h, headZ, d);
        }

        private void AddHoundMuzzle(NetCreature c, float unit, float w, float h, float d, float headZ, float muzzle)
        {
            float faceZ = headZ + d * 0.5f;

            // Upper muzzle: the lower part of the skull, narrower than the face so the eyes at its sides stay clear.
            float upperH = h * 0.34f, upperY = -h * 0.03f, upperW = w * 0.52f;
            float upperZ = faceZ + muzzle * 0.5f;
            AddPartTo(_headPivot, "MuzzleUpper", new Vector3(0f, upperY, upperZ), new Vector3(upperW, upperH, muzzle), _coat.Mat(CoatPart.Head));
            AddPartTo(_headPivot, "Nose", new Vector3(0f, upperY + upperH * 0.32f, faceZ + muzzle + unit * 0.025f),
                new Vector3(upperW * 0.5f, upperH * 0.36f, unit * 0.06f), Lit(HoundNoseColor, null));

            // Lower muzzle: on the jaw's pivot, in front of the jaw box (the jaw pivot sits 0.44d behind the head's centre and
            // its box reaches 0.88d forward of it), so it opens with every call and every bite.
            float lowerH = h * 0.24f, lowerW = upperW * 0.9f;
            float jawFront = d * 0.88f;
            float lowerLen = muzzle * 0.92f;
            AddPartTo(_jawPivot, "MuzzleLower", new Vector3(0f, 0f, jawFront + lowerLen * 0.5f), new Vector3(lowerW, lowerH, lowerLen), _coat.Mat(CoatPart.Jaw));

            if (!c.HasTeeth)
            {
                return;
            }

            // Two rows of teeth — Jafar's zigzag: the upper row hangs from the muzzle's underside, the lower row stands on the
            // jaw's top; the front pair is longer (the fangs).
            var toothMat = Lit(HoundToothColor, null);
            float tooth = unit * 0.06f;
            const int perSide = 3;
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                for (int i = 0; i < perSide; i++)
                {
                    float f = (i + 0.5f) / perSide; // 0..1 from the face to the tip
                    bool fang = i == perSide - 1;
                    float len = tooth * (fang ? 2.2f : 1.4f);
                    float x = sx * upperW * 0.36f;
                    AddPartTo(_headPivot, "ToothU" + side + i,
                        new Vector3(x, upperY - upperH * 0.5f - len * 0.4f, faceZ + muzzle * f),
                        new Vector3(tooth, len, tooth), toothMat);
                    AddPartTo(_jawPivot, "ToothL" + side + i,
                        new Vector3(x * 0.9f, lowerH * 0.5f + len * 0.35f, jawFront + lowerLen * f),
                        new Vector3(tooth * 0.9f, len * 0.8f, tooth * 0.9f), toothMat);
                }
            }
        }

        /// <summary>Two ears on the crown, toward the back of the skull, on pivots at their base: pointed (short, upright),
        /// tall (long, upright) or swept (laid back along the skull).</summary>
        private void AddHoundEars(NetCreature c, float unit, float w, float h, float headZ, float d)
        {
            var style = System.Enum.TryParse<CreatureEarStyle>(c.EarStyle, out var parsed) ? parsed : CreatureEarStyle.None;
            if (style == CreatureEarStyle.None)
            {
                return;
            }

            float earH = unit * (style == CreatureEarStyle.Tall ? 0.6f : 0.38f);
            float earW = unit * 0.2f;
            float earD = unit * 0.09f;
            float tilt = style == CreatureEarStyle.Swept ? -62f : -8f; // swept ears lie back; the others lean back a touch
            var mat = _coat.Mat(CoatPart.Ornament);
            for (int e = 0; e < 2; e++)
            {
                float sx = e == 0 ? -1f : 1f;
                var ear = AddPivotPart(_headPivot, e == 0 ? "EarL" : "EarR",
                    new Vector3(sx * w * 0.3f, h * 0.5f, headZ - d * 0.12f),
                    new Vector3(0f, earH * 0.5f, 0f), new Vector3(earW, earH, earD), mat);
                ear.localRotation = Quaternion.Euler(tilt, 0f, -sx * 9f); // a slight outward splay
                AddPartTo(ear, "EarTip", new Vector3(0f, earH + earW * 0.2f, 0f), new Vector3(earW * 0.5f, earW * 0.45f, earD), mat);
                _ears.Add(ear);
            }
        }

        /// <summary>Three claws at the front of every sole — the "3" on each foot of the sketch. Horn-coloured, per species.</summary>
        private void AddHoundClaws(NetCreature c, float legThick)
        {
            if (!c.HasClaws)
            {
                return;
            }

            var clawMat = HornMaterial(c);
            float soleFront = legThick * 0.3f + legThick * 0.9f; // the sole box sits 0.3·thick forward, 1.8·thick deep
            float claw = legThick * 0.16f;
            foreach (var leg in _legs)
            {
                if (leg?.Foot == null)
                {
                    continue;
                }

                for (int k = 0; k < 3; k++)
                {
                    float x = (k - 1) * legThick * 0.42f;
                    AddPartTo(leg.Foot, "Claw" + k, new Vector3(x, -legThick * 0.4f, soleFront + claw * 0.9f),
                        new Vector3(claw, claw, claw * 2.2f), clawMat);
                }
            }
        }
    }
}
