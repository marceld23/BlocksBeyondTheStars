// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Sounders;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Soundi (#3359) — exactly the Soundi of the animated series, and cute is a hard requirement: a knee-high, fluffy,
    /// cat-like body on four short legs, light-blue fur (<c>ColorRgb</c>, #8FCBF2) with mint-green patches (<c>BellyRgb</c>,
    /// #BFF2CF) on the muzzle, the chest, the paws and the insides of the ears; a big round head with <b>no eyes at all</b>, a
    /// little nose and a <b>wide gentle smile</b>; <b>huge pointed ears</b> taller than the head; a bushy tail carried up. The
    /// proportions are <see cref="SoundiRules"/> in the standard unit, so the server's footprint is the drawn body. On the
    /// four-legged rig, so the animator walks, trots, breathes and lies down like any quadruped; the ears sway and flick, the
    /// chin opens with every sung call.
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        /// <summary>The materials of one Soundi build.</summary>
        private sealed class SoundiMats
        {
            public Material Fur, FurShade, Mint, Smile, Nose;
        }

        /// <summary>Builds the Soundi: legs, round fluffy body, the big eyeless head with its smile and huge ears, the bushy tail.</summary>
        private void BuildSoundi(GameObject root, NetCreature c)
        {
            float u = SoundiRules.Unit(c.Size);
            var m = SoundiMaterials(c);
            _bodyMat = m.Fur;

            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);
            float legLen = SoundiRules.LegLength * u;
            float bodyY = legLen + (SoundiRules.BodyHeight * 0.45f * u);
            for (int row = 0; row < 2; row++)
            {
                float z = (row == 0 ? 0.36f : -0.36f) * u;
                for (int side = 0; side < 2; side++)
                {
                    float x = (side == 0 ? -1f : 1f) * 0.3f * u;
                    _legs.Add(AddSoundiLeg(body.transform, row, side, new Vector3(x, legLen, z), legLen, 0.3f * u, m));
                }
            }

            AddSoundiBody(body.transform, bodyY, u, m);
            var face = AddSoundiHead(body.transform, bodyY, u, m);
            AddSoundiEars(face, u, m);
            AddSoundiTail(body.transform, bodyY, u, m);

            int idh = CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed);
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(Describe(c, body.transform, u, legLen, idh));
        }

        /// <summary>The Soundi's fixed look: the fur and the mint from the server (the series' colours), the soft hide, the smile
        /// a deep violet and the nose a soft pink. The sleep dim, a soft red when angry and the companions' cast apply.</summary>
        private static SoundiMats SoundiMaterials(NetCreature c)
        {
            var mood = MoodOf(c, 0.15f, true, true);
            Color fur = Rgb(c.ColorRgb != 0 ? c.ColorRgb : SoundiRules.Fur);
            Color mint = Rgb(c.BellyRgb != 0 ? c.BellyRgb : SoundiRules.Mint);
            string hide = string.IsNullOrEmpty(c.Hide) ? "fur" : c.Hide;
            Color Tint(Color col, float shade)
            {
                var t = mood.Tint(col);
                float k = shade * mood.Dim;
                return new Color(t.r * k, t.g * k, t.b * k, 1f);
            }

            return new SoundiMats
            {
                Fur = Lit(Tint(fur, 1f), HideOr(hide)),
                FurShade = Lit(Tint(fur, 0.88f), HideOr(hide)),
                Mint = Lit(Tint(mint, 1f), HideOr(hide)),
                Smile = Unlit(new Color(0.23f, 0.16f, 0.36f)),
                Nose = Lit(Tint(new Color(0.9f, 0.56f, 0.72f), 1f), null),
            };
        }

        /// <summary>One short, chubby leg with a round mint paw — hip → knee → foot, so the gait and the foot planner drive it.</summary>
        private LegRig AddSoundiLeg(Transform parent, int row, int side, Vector3 hipPos, float legLen, float thick, SoundiMats m)
        {
            float upper = legLen * UpperLegShare;
            float lower = legLen - upper;
            var hip = NewPivot(parent, (side == 0 ? "LegL" : "LegR") + row, hipPos);
            AddPartTo(hip, "Thigh", new Vector3(0f, -upper * 0.5f, 0f), new Vector3(thick, upper, thick * 1.05f), m.Fur);
            var knee = NewPivot(hip, "Knee", new Vector3(0f, -upper, 0f));
            AddPartTo(knee, "Shin", new Vector3(0f, -lower * 0.5f, 0f), new Vector3(thick * 0.85f, lower, thick * 0.85f), m.Fur);
            var foot = NewPivot(knee, "Foot", new Vector3(0f, -lower, 0f));
            AddPartTo(foot, "Paw", new Vector3(0f, -thick * 0.15f, thick * 0.18f), new Vector3(thick * 1.15f, thick * 0.5f, thick * 1.3f), m.Mint);
            return new LegRig
            {
                Hip = hip,
                Knee = knee,
                Foot = foot,
                Side = side,
                Row = row,
                Rows = 2,
                UpperLen = upper,
                LowerLen = lower,
                KneeSign = row == 0 ? 1 : -1,
                HipRest = hip.localRotation,
                HipRestPos = hip.localPosition,
            };
        }

        /// <summary>The round fluffy body: a soft barrel, fluff tufts on the flanks and the back, and the mint chest patch.</summary>
        private void AddSoundiBody(Transform body, float bodyY, float u, SoundiMats m)
        {
            float len = SoundiRules.BodyLength * u, high = SoundiRules.BodyHeight * u, wide = 1.0f * u;
            AddPartTo(body, "Torso", new Vector3(0f, bodyY, 0f), new Vector3(wide, high, len), m.Fur);
            AddPartTo(body, "Rounding", new Vector3(0f, bodyY, 0f), new Vector3(wide * 1.08f, high * 0.8f, len * 0.86f), m.Fur);
            AddPartTo(body, "Chest", new Vector3(0f, bodyY - (high * 0.08f), (len * 0.5f) + (0.02f * u)), new Vector3(wide * 0.62f, high * 0.62f, 0.1f * u), m.Mint);
            for (int i = 0; i < 3; i++)
            {
                // Fluff: tufts that stand a little proud of the flanks and the back, each a touch off the last.
                float z = Mathf.Lerp(0.35f, -0.35f, i / 2f) * len;
                float lift = (i == 1 ? 0.04f : 0f) * u;
                AddPartTo(body, "BackTuft" + i, new Vector3(0f, bodyY + (high * 0.5f) + lift, z), new Vector3(wide * 0.55f, 0.12f * u, len * 0.24f), m.FurShade);
                AddPartTo(body, "FlankTuft" + i, new Vector3(0f, bodyY - (high * 0.28f), z), new Vector3(wide * 1.12f, 0.14f * u, len * 0.22f), m.FurShade);
            }
        }

        /// <summary>The big round head on a neck pivot over the chest (the animator turns the neck): a round skull with fluffy
        /// cheeks and a crown tuft, the mint muzzle with a little nose and the wide smile, and a mint chin on the jaw pivot that
        /// opens as it sings. No eyes. Returns the head frame the ears hang off.</summary>
        private Transform AddSoundiHead(Transform body, float bodyY, float u, SoundiMats m)
        {
            float e = SoundiRules.HeadEdge * u;
            var neck = NewPivot(body, "Head", new Vector3(0f, bodyY + (0.3f * u), 0.45f * u));
            _headPivot = NewPivot(neck, "HeadCentre", new Vector3(0f, 0.42f * u, 0.32f * u));
            AddPartTo(_headPivot, "Skull", Vector3.zero, new Vector3(e, e * 0.88f, e * 0.82f), m.Fur);
            AddPartTo(_headPivot, "SkullRound", Vector3.zero, new Vector3(e * 1.06f, e * 0.72f, e * 0.7f), m.Fur);
            AddPartTo(_headPivot, "CheekL", new Vector3(-e * 0.5f, -e * 0.16f, e * 0.08f), new Vector3(e * 0.2f, e * 0.36f, e * 0.42f), m.FurShade);
            AddPartTo(_headPivot, "CheekR", new Vector3(e * 0.5f, -e * 0.16f, e * 0.08f), new Vector3(e * 0.2f, e * 0.36f, e * 0.42f), m.FurShade);
            AddPartTo(_headPivot, "CrownTuft", new Vector3(0f, e * 0.48f, e * 0.08f), new Vector3(e * 0.3f, e * 0.12f, e * 0.22f), m.FurShade);

            float front = e * 0.41f;
            AddPartTo(_headPivot, "Muzzle", new Vector3(0f, -e * 0.17f, front + (0.03f * u)), new Vector3(e * 0.56f, e * 0.36f, 0.12f * u), m.Mint);
            AddPartTo(_headPivot, "Nose", new Vector3(0f, -e * 0.05f, front + (0.1f * u)), new Vector3(e * 0.13f, e * 0.08f, 0.05f * u), m.Nose);
            AddSoundiSmile(_headPivot, -e * 0.2f, front + (0.1f * u), e, u, m.Smile);

            _jawPivot = NewPivot(_headPivot, "Jaw", new Vector3(0f, -e * 0.35f, front - (0.05f * u)));
            AddPartTo(_jawPivot, "Chin", new Vector3(0f, -0.04f * u, 0.1f * u), new Vector3(e * 0.4f, 0.1f * u, 0.18f * u), m.Mint);
            _heads.Add(neck);
            _jaws.Add(_jawPivot);
            return _headPivot;
        }

        /// <summary>The wide gentle smile: a flat middle and two corners turned up, in deep violet on the muzzle.</summary>
        private void AddSoundiSmile(Transform head, float y, float z, float e, float u, Material mat)
        {
            float t = 0.05f * u;
            AddPartTo(head, "SmileMid", new Vector3(0f, y, z), new Vector3(e * 0.26f, t, t), mat);
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                var corner = NewPivot(head, side == 0 ? "SmileL" : "SmileR", new Vector3(sx * e * 0.13f, y, z));
                corner.localRotation = Quaternion.Euler(0f, 0f, sx * 32f);
                AddPartTo(corner, "SmileCorner", new Vector3(sx * e * 0.07f, 0f, 0f), new Vector3(e * 0.15f, t, t), mat);
            }
        }

        /// <summary>The huge pointed ears — taller than the head — on pivots the animator sways and flicks: three tapering tiers
        /// with a mint inner panel on each, leaning a little out.</summary>
        private void AddSoundiEars(Transform head, float u, SoundiMats m)
        {
            float e = SoundiRules.HeadEdge * u;
            float tall = SoundiRules.EarHeight * u;
            float[] widths = { 0.5f, 0.34f, 0.16f };
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                var ear = NewPivot(head, side == 0 ? "EarL" : "EarR", new Vector3(sx * e * 0.3f, e * 0.38f, -e * 0.04f));
                ear.localRotation = Quaternion.Euler(-6f, 0f, -sx * 14f);
                for (int tier = 0; tier < widths.Length; tier++)
                {
                    float tierH = tall / widths.Length;
                    float y = tierH * (tier + 0.5f);
                    float w = widths[tier] * e;
                    AddPartTo(ear, "EarTier" + tier, new Vector3(0f, y, 0f), new Vector3(w, tierH * 1.04f, 0.1f * u), m.Fur);
                    if (tier < 2)
                    {
                        AddPartTo(ear, "EarInner" + tier, new Vector3(0f, y + (tierH * 0.06f), 0.055f * u), new Vector3(w * 0.6f, tierH * 0.9f, 0.02f * u), m.Mint);
                    }
                }

                _ears.Add(ear);
            }
        }

        /// <summary>The bushy tail, carried up behind: three links that grow fluffier toward a mint tip, handed to the animator
        /// as the tail chain (the beat travels link by link).</summary>
        private void AddSoundiTail(Transform body, float bodyY, float u, SoundiMats m)
        {
            float linkLen = SoundiRules.TailLength * u / 3f;
            float[] thick = { 0.26f * u, 0.38f * u, 0.46f * u };
            var parent = body;
            for (int i = 0; i < thick.Length; i++)
            {
                var link = NewPivot(parent, "Tail" + i, i == 0
                    ? new Vector3(0f, bodyY + (SoundiRules.BodyHeight * 0.28f * u), -SoundiRules.BodyLength * 0.5f * u)
                    : new Vector3(0f, 0f, -linkLen)); // carried up by the animator (PoseSoundi): PoseTail sets the links fresh
                AddPartTo(link, "TailSeg" + i, new Vector3(0f, 0f, -linkLen * 0.5f), new Vector3(thick[i], thick[i], linkLen * 1.08f), i == 2 ? m.Mint : m.Fur);
                _tailChain.Add(link);
                parent = link;
            }
        }
    }
}
