// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Sounders;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Soundrak (#3359): about ten metres tall (<see cref="SoundrakRules.HeightFor"/>, the height the server's large-body
    /// checks measure), smooth leathery blue-green skin — no fur, nothing leafy or crumbly — on two very short, toeless legs; a
    /// body that is narrow at the bottom, thin in the middle and clearly wider at the top; an oval, bud-like mouth pointing up at
    /// the top (five lips round a dark throat that open with every call and the scream, <see cref="RigDescription.Petals"/>);
    /// tentacles hanging from its back, swaying. The body keeps its colliders (like the arachnid's), so a player bumps into it.
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        /// <summary>The body's widths from the bottom up, as shares of the height — narrow, a thin waist, then widening to the crown.</summary>
        private static readonly float[] SoundrakWidths = { 0.17f, 0.15f, 0.12f, 0.13f, 0.19f, 0.27f, 0.36f, 0.44f };

        /// <summary>Builds the Soundrak: the stubby legs, the stacked body, the bud mouth, the back tentacles.</summary>
        private void BuildSoundrak(GameObject root, NetCreature c)
        {
            float h = SoundrakRules.HeightFor(c.Size);
            var mood = MoodOf(c, 0.2f, true, true);
            Color skinCol = mood.Tint(Rgb(c.ColorRgb != 0 ? c.ColorRgb : 0x2F8F8A)) * mood.Dim;
            Color bellyCol = mood.Tint(Rgb(c.BellyRgb != 0 ? c.BellyRgb : 0x6CC4B4)) * mood.Dim;
            skinCol.a = bellyCol.a = 1f;
            var skin = Lit(skinCol, HideOr(string.IsNullOrEmpty(c.Hide) ? "leather" : c.Hide));
            var belly = Lit(bellyCol, HideOr("leather"));
            var dark = Lit(new Color(skinCol.r * 0.7f, skinCol.g * 0.7f, skinCol.b * 0.7f, 1f), HideOr("leather"));
            _bodyMat = skin;

            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);
            float legLen = h * SoundrakRules.LegShare;
            for (int side = 0; side < 2; side++)
            {
                float x = (side == 0 ? -1f : 1f) * h * 0.07f;
                _legs.Add(AddStubLeg(body.transform, side, new Vector3(x, legLen, 0f), legLen, h * 0.08f, skin, dark));
            }

            float segH = h * 0.1f;
            float topY = AddSoundrakBody(body.transform, legLen, segH, h, skin, belly);
            var petals = AddSoundrakBud(body.transform, topY, h, skin, belly);
            AddSoundrakTentacles(body.transform, legLen, segH, h, dark);

            int idh = CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed);
            var rig = Describe(c, body.transform, h * 0.1f, legLen, idh);
            rig.Petals = petals;
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(rig);
            MakeGiantBody(root); // the colliders kept on the body go onto the giants' layer — the player bumps into it
        }

        /// <summary>The stacked body: the segments of <see cref="SoundrakWidths"/>, slightly oval (their depth 0.85 of their
        /// width), with a lighter front panel on the lower half. Keeps the colliders. Returns the crown's top height.</summary>
        private float AddSoundrakBody(Transform body, float legLen, float segH, float h, Material skin, Material belly)
        {
            _keepColliders = true;
            for (int i = 0; i < SoundrakWidths.Length; i++)
            {
                float w = SoundrakWidths[i] * h;
                float y = legLen + (segH * (i + 0.5f));
                AddPartTo(body, "Body" + i, new Vector3(0f, y, 0f), new Vector3(w, segH * 1.02f, w * 0.85f), skin);
            }

            _keepColliders = false;
            for (int i = 0; i < 4; i++)
            {
                float w = SoundrakWidths[i] * h;
                AddPartTo(body, "Front" + i, new Vector3(0f, legLen + (segH * (i + 0.5f)), (w * 0.425f) + (h * 0.004f)),
                    new Vector3(w * 0.55f, segH * 0.96f, h * 0.008f), belly);
            }

            return legLen + (segH * SoundrakWidths.Length);
        }

        /// <summary>The oval bud mouth on top, pointing up: a rim, a dark throat and five lips on pivots round the opening, leaning
        /// in (closed). A hidden jaw pivot lets the voice's pulse reach the animator, which opens the lips with it.</summary>
        private Transform[] AddSoundrakBud(Transform body, float topY, float h, Material skin, Material belly)
        {
            AddPartTo(body, "BudRim", new Vector3(0f, topY + (h * 0.015f), 0f), new Vector3(h * 0.3f, h * 0.03f, h * 0.25f), skin);
            AddPartTo(body, "Throat", new Vector3(0f, topY + (h * 0.025f), 0f), new Vector3(h * 0.14f, h * 0.03f, h * 0.11f),
                Unlit(new Color(0.24f, 0.07f, 0.15f)));
            var petals = new List<Transform>();
            const int count = 5;
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f;
                float x = Mathf.Sin(a) * h * 0.1f, z = Mathf.Cos(a) * h * 0.08f;
                var lip = NewPivot(body, "Lip" + i, new Vector3(x, topY + (h * 0.02f), z));
                lip.localRotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f) * Quaternion.Euler(-20f, 0f, 0f); // +Z outward, leaning in
                AddPartTo(lip, "LipBlade", new Vector3(0f, h * 0.075f, 0f), new Vector3(h * 0.12f, h * 0.15f, h * 0.035f), i % 2 == 0 ? skin : belly);
                petals.Add(lip);
            }

            _jawPivot = NewPivot(body, "Jaw", new Vector3(0f, topY, 0f)); // carries no part: the voice pulse's handle
            _jaws.Add(_jawPivot);
            return petals.ToArray();
        }

        /// <summary>Tentacles on its back: five chains hanging from the upper back, drifting backwards, swaying (PoseTentacles).</summary>
        private void AddSoundrakTentacles(Transform body, float legLen, float segH, float h, Material mat)
        {
            for (int k = 0; k < 5; k++)
            {
                int seg = 4 + (k % 3);                                  // from the widening part of the body
                float depth = SoundrakWidths[seg] * h * 0.425f;
                float x = Mathf.Lerp(-0.11f, 0.11f, k / 4f) * h;
                float y = legLen + (segH * (seg + 0.6f));
                _tentacleChains.Add(AddChain(body, $"BackTent{k}_", new Vector3(x, y, -depth), -h * 0.02f,
                    4, h * 0.04f, h * 0.1f, 0.15f, mat));
            }
        }

        /// <summary>A very short, toeless leg — hip → knee → foot, a round pad for a foot — for the plodding giant of Soaniri.</summary>
        private LegRig AddStubLeg(Transform parent, int side, Vector3 hipPos, float legLen, float thick, Material skin, Material sole)
        {
            float upper = legLen * 0.5f;
            float lower = legLen - upper;
            var hip = NewPivot(parent, side == 0 ? "LegL0" : "LegR0", hipPos);
            AddPartTo(hip, "Thigh", new Vector3(0f, -upper * 0.5f, 0f), new Vector3(thick, upper, thick), skin);
            var knee = NewPivot(hip, "Knee", new Vector3(0f, -upper, 0f));
            AddPartTo(knee, "Shin", new Vector3(0f, -lower * 0.5f, 0f), new Vector3(thick * 0.9f, lower, thick * 0.9f), skin);
            var foot = NewPivot(knee, "Foot", new Vector3(0f, -lower, 0f));
            AddPartTo(foot, "Pad", new Vector3(0f, -thick * 0.1f, thick * 0.12f), new Vector3(thick * 1.35f, thick * 0.25f, thick * 1.5f), sole);
            return new LegRig
            {
                Hip = hip,
                Knee = knee,
                Foot = foot,
                Side = side,
                Row = 0,
                Rows = 1,
                UpperLen = upper,
                LowerLen = lower,
                KneeSign = 1,
                HipRest = hip.localRotation,
                HipRestPos = hip.localPosition,
            };
        }
    }
}
