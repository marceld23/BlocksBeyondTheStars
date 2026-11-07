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
    /// The Sounder (#3355, after the school club's sketches): tall and thin, three to four blocks high, upright on two very long
    /// legs with toeless feet, <b>four</b> thin, slightly wavy arms in two pairs (registered as <see cref="ArmRig"/>s with a
    /// <see cref="ArmRig.Row"/>, so the animator swings them like a biped's and lets the lower pair wave a beat behind), a small
    /// head with <b>no eyes</b>, long hare-like ears on pivots the animator sways, a mouth that is always a little open (they
    /// sing) on a hinged chin the voice moves, and the rectangular <b>slot on the chest</b> where the obsidian armour holds a Song
    /// Disc. A living Sounder wears its skin (the server's <c>ColorRgb</c>, earthy or indigo) and a woven sash (<c>BellyRgb</c>);
    /// an <b>Echo</b> is the same body in a translucent, glowing teal that floats a hand above the ground with slowly drifting
    /// arms (<see cref="RigDescription.Drift"/>). Every share comes from <see cref="SounderRules"/>, the numbers the server
    /// measures the body by. Also the dispatch of every body of Soaniri.
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        /// <summary>The materials of one Sounder build.</summary>
        private sealed class SounderMats
        {
            public Material Skin, SkinDark, Inner, Sash, Mouth, Slot;
        }

        /// <summary>The see-through materials (Echoes, a Quali's bell) by colour and alpha — shared, never mutated.</summary>
        private static readonly Dictionary<Color, Material> GhostCache = new Dictionary<Color, Material>();

        /// <summary>The bodies of Soaniri (#3355, #3359): false for every other plan.</summary>
        private bool BuildSoundersBody(GameObject root, NetCreature c)
        {
            switch (c.BodyPlan)
            {
                case "Sounder":
                    BuildSounder(root, c, echo: false);
                    return true;
                case "Soundi":
                    BuildSoundi(root, c); // CreatureBuilder.Soundi.cs
                    return true;
                case "Soundrak":
                    BuildSoundrak(root, c); // CreatureBuilder.Soundrak.cs
                    return true;
                case "Soundeling":
                    BuildSoundeling(root, c); // CreatureBuilder.Soundeling.cs
                    return true;
                case "Quali":
                    BuildQuali(root, c); // CreatureBuilder.Soundeling.cs
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>#3355: the NPC view's entry — builds a Sounder (or, with <paramref name="echo"/>, an Echo) from a descriptor the
        /// view fills from the NPC (size, skin, sash, seed).</summary>
        public void BuildSounderNpc(GameObject root, NetCreature c, bool echo)
        {
            EnsureTextures();
            BuildSounder(root, c, echo);
        }

        /// <summary>Builds the Sounder body and hands it to the animator as a two-legged, four-armed rig.</summary>
        private void BuildSounder(GameObject root, NetCreature c, bool echo)
        {
            float h = SounderRules.HeightFor(c.Size);
            var m = SounderMaterials(c, echo);
            _bodyMat = m.Skin;

            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);
            float legLen = h * SounderRules.LegShare;
            float torsoH = h * SounderRules.TorsoShare;
            for (int side = 0; side < 2; side++)
            {
                float x = (side == 0 ? -1f : 1f) * h * 0.035f;
                _legs.Add(AddSounderLeg(body.transform, side, new Vector3(x, legLen, 0f), legLen, h * 0.042f, m));
            }

            AddSounderTorso(body.transform, h, legLen, torsoH, m, echo);
            AddSounderArms(body.transform, h, legLen, torsoH, m);
            AddSounderHead(body.transform, h, legLen + torsoH, m);
            if (echo)
            {
                AddSoftLight(body.transform, new Vector3(0f, h * 0.65f, 0f), h * 1.4f, 0.9f, Rgb((int)SounderRules.EchoGlow));
            }

            int idh = CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed);
            var rig = Describe(c, body.transform, 0.5f * SounderRules.ClampSize(c.Size), legLen, idh);
            rig.Arms = _arms.ToArray();
            rig.Drift = echo;
            rig.Hover = echo ? SounderRules.EchoHover : 0f;
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(rig);
        }

        /// <summary>A living Sounder: skin, a darker skin for feet and hands, pinkish inner ears, the woven sash, the dark mouth and
        /// slot (the sleep dim and a soft red when it is angry apply). An Echo: one translucent teal, the inner ears and sash glow.</summary>
        private static SounderMats SounderMaterials(NetCreature c, bool echo)
        {
            if (echo)
            {
                Color teal = Rgb(c.ColorRgb != 0 ? c.ColorRgb : (int)SounderRules.EchoTeal);
                var ghost = Ghost(teal, 0.42f);
                var glow = Ghost(Rgb((int)SounderRules.EchoGlow), 0.62f);
                return new SounderMats
                {
                    Skin = ghost,
                    SkinDark = ghost,
                    Inner = glow,
                    Sash = glow,
                    Mouth = Unlit(new Color(teal.r * 0.45f, teal.g * 0.45f, teal.b * 0.45f)),
                    Slot = Unlit(new Color(0.1f, 0.35f, 0.38f)),
                };
            }

            var mood = MoodOf(c, 0.2f, true, false);
            Color skin = c.ColorRgb != 0 ? Rgb(c.ColorRgb) : Rgb(0x4B4E8C);
            Color sash = c.BellyRgb != 0 ? Rgb(c.BellyRgb) : Rgb(0xC9A14A);
            Color Tint(Color col, float shade)
            {
                var t = mood.Tint(col);
                float k = shade * mood.Dim;
                return new Color(t.r * k, t.g * k, t.b * k, 1f);
            }

            return new SounderMats
            {
                Skin = Lit(Tint(skin, 1f), HideOr("skin")),
                SkinDark = Lit(Tint(skin, 0.78f), HideOr("skin")),
                Inner = Lit(Tint(Color.Lerp(skin, new Color(0.95f, 0.75f, 0.8f), 0.45f), 1f), null),
                Sash = Lit(Tint(sash, 1f), HideOr("wool")),
                Mouth = Unlit(new Color(0.16f, 0.08f, 0.12f)),
                Slot = Unlit(new Color(0.07f, 0.06f, 0.09f)),
            };
        }

        /// <summary>A see-through material (the always-included Cloud shader, alpha-blended — the medusa bell's).</summary>
        private static Material Ghost(Color tint, float alpha)
        {
            var key = new Color(tint.r, tint.g, tint.b, alpha);
            if (GhostCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { color = key };
            GhostCache[key] = m;
            return m;
        }

        /// <summary>One very long, thin leg — hip → knee → foot, the knee folding back like ours — on a toeless foot: one rounded
        /// pad and a heel.</summary>
        private LegRig AddSounderLeg(Transform parent, int side, Vector3 hipPos, float legLen, float thick, SounderMats m)
        {
            float upper = legLen * 0.52f;
            float lower = legLen - upper;
            var hip = NewPivot(parent, side == 0 ? "LegL0" : "LegR0", hipPos);
            AddPartTo(hip, "Thigh", new Vector3(0f, -upper * 0.5f, 0f), new Vector3(thick, upper, thick), m.Skin);
            var knee = NewPivot(hip, "Knee", new Vector3(0f, -upper, 0f));
            AddPartTo(knee, "Shin", new Vector3(0f, -lower * 0.5f, 0f), new Vector3(thick * 0.8f, lower, thick * 0.8f), m.Skin);
            var foot = NewPivot(knee, "Foot", new Vector3(0f, -lower, 0f));
            AddPartTo(foot, "Sole", new Vector3(0f, -thick * 0.2f, thick * 0.45f), new Vector3(thick * 1.25f, thick * 0.4f, thick * 2.2f), m.SkinDark);
            AddPartTo(foot, "Heel", new Vector3(0f, -thick * 0.15f, -thick * 0.25f), Vector3.one * (thick * 0.9f), m.SkinDark, PrimitiveType.Sphere);
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
                KneeSign = 1, // an upright two-legger folds the shin back
                HipRest = hip.localRotation,
                HipRestPos = hip.localPosition,
            };
        }

        /// <summary>The thin torso: pelvis, a narrow waist, the chest with the disc slot in its raised rim and — on a living Sounder
        /// — the woven sash round the waist with its long end hanging at the side.</summary>
        private void AddSounderTorso(Transform body, float h, float legLen, float torsoH, SounderMats m, bool echo)
        {
            float w = h * SounderRules.TorsoWidthShare, d = w * 0.62f;
            AddPartTo(body, "Pelvis", new Vector3(0f, legLen + (h * 0.01f), 0f), new Vector3(w * 0.85f, h * 0.05f, d), m.Skin);
            AddPartTo(body, "Waist", new Vector3(0f, legLen + (torsoH * 0.3f), 0f), new Vector3(w * 0.72f, torsoH * 0.45f, d * 0.9f), m.Skin);
            float chestY = legLen + (torsoH * 0.72f);
            AddPartTo(body, "Chest", new Vector3(0f, chestY, 0f), new Vector3(w, torsoH * 0.5f, d), m.Skin);
            float slotY = chestY + (torsoH * 0.03f);
            AddPartTo(body, "SlotRim", new Vector3(0f, slotY, (d * 0.5f) + (h * 0.002f)), new Vector3(w * 0.46f, torsoH * 0.28f, h * 0.006f), m.SkinDark);
            AddPartTo(body, "Slot", new Vector3(0f, slotY, (d * 0.5f) + (h * 0.006f)), new Vector3(w * 0.34f, torsoH * 0.2f, h * 0.004f), m.Slot);
            if (!echo)
            {
                AddPartTo(body, "Sash", new Vector3(0f, legLen + (torsoH * 0.12f), 0f), new Vector3(w * 0.9f, torsoH * 0.12f, d * 1.08f), m.Sash);
                AddPartTo(body, "SashEnd", new Vector3(w * 0.32f, legLen - (torsoH * 0.08f), d * 0.52f),
                    new Vector3(w * 0.18f, torsoH * 0.38f, h * 0.008f), m.Sash);
            }
        }

        /// <summary>Four thin arms in two pairs: the upper pair from the shoulders, a shorter lower pair under them, a little
        /// forward and splayed further out — upper arm, elbow, forearm, and a hand bent a little off the forearm's line (the
        /// sketches' slightly wavy arms).</summary>
        private void AddSounderArms(Transform body, float h, float legLen, float torsoH, SounderMats m)
        {
            float w = h * SounderRules.TorsoWidthShare;
            float thick = h * 0.022f;
            for (int row = 0; row < 2; row++)
            {
                float reach = h * (row == 0 ? SounderRules.UpperArmShare : SounderRules.LowerArmShare);
                float upper = reach * 0.46f, lower = reach * 0.42f, hand = reach * 0.12f;
                float y = legLen + (torsoH * (row == 0 ? 0.93f : 0.62f));
                float x = (w * (row == 0 ? 0.55f : 0.45f)) + (thick * 0.6f);
                for (int side = 0; side < 2; side++)
                {
                    float sx = side == 0 ? -1f : 1f;
                    var shoulder = NewPivot(body, (side == 0 ? "ArmL" : "ArmR") + row, new Vector3(sx * x, y, row == 0 ? 0f : h * 0.012f));
                    AddPartTo(shoulder, "UpperArm", new Vector3(0f, -upper * 0.5f, 0f), new Vector3(thick, upper, thick), m.Skin);
                    var elbow = NewPivot(shoulder, "Elbow", new Vector3(0f, -upper, 0f));
                    AddPartTo(elbow, "Forearm", new Vector3(0f, -lower * 0.5f, 0f), new Vector3(thick * 0.85f, lower, thick * 0.85f), m.Skin);
                    var wrist = NewPivot(elbow, "Wrist", new Vector3(0f, -lower, 0f));
                    wrist.localRotation = Quaternion.Euler(-14f, 0f, sx * 10f);
                    AddPartTo(wrist, "Hand", new Vector3(0f, -hand * 0.5f, 0f), new Vector3(thick * 1.5f, hand, thick * 0.7f), m.SkinDark);
                    elbow.localRotation = Quaternion.Euler(-8f - (6f * row), 0f, -sx * 6f);
                    shoulder.localRotation = Quaternion.Euler(row == 0 ? 0f : -10f, 0f, -sx * (row == 0 ? 7f : 15f));
                    _arms.Add(new ArmRig
                    {
                        Shoulder = shoulder,
                        Elbow = elbow,
                        Side = side,
                        Row = row,
                        ShoulderRest = shoulder.localRotation,
                        ElbowRest = elbow.localRotation,
                    });
                }
            }
        }

        /// <summary>The thin neck and the small head on it — the neck is what the animator turns (gaze, look-around), as on the
        /// biped. No eyes at all: a smooth face with the dark, open mouth over a hinged chin, and the long ears on the crown.</summary>
        private void AddSounderHead(Transform body, float h, float shoulderY, SounderMats m)
        {
            float neckH = h * SounderRules.NeckShare, headH = h * SounderRules.HeadShare;
            var neck = NewPivot(body, "Head", new Vector3(0f, shoulderY, 0f));
            AddPartTo(neck, "Neck", new Vector3(0f, neckH * 0.5f, 0f), new Vector3(h * 0.03f, neckH * 1.1f, h * 0.03f), m.Skin);
            _headPivot = NewPivot(neck, "HeadCentre", new Vector3(0f, neckH + (headH * 0.5f), h * 0.008f));
            float hw = headH * 0.78f, hd = headH * 0.85f;
            AddPartTo(_headPivot, "Skull", new Vector3(0f, headH * 0.06f, 0f), new Vector3(hw, headH * 0.82f, hd), m.Skin);
            AddPartTo(_headPivot, "Crown", new Vector3(0f, headH * 0.4f, -hd * 0.05f), new Vector3(hw * 0.8f, headH * 0.18f, hd * 0.8f), m.Skin);
            AddPartTo(_headPivot, "Mouth", new Vector3(0f, -headH * 0.18f, (hd * 0.5f) + (h * 0.001f)), new Vector3(hw * 0.42f, headH * 0.12f, h * 0.004f), m.Mouth);
            _jawPivot = NewPivot(_headPivot, "Jaw", new Vector3(0f, -headH * 0.26f, hd * 0.2f));
            AddPartTo(_jawPivot, "Chin", new Vector3(0f, -headH * 0.06f, hd * 0.18f), new Vector3(hw * 0.6f, headH * 0.14f, hd * 0.42f), m.SkinDark);
            _jawPivot.localRotation = Quaternion.Euler(9f, 0f, 0f); // a little open at rest: a Sounder is mostly singing
            _heads.Add(neck);
            _jaws.Add(_jawPivot);
            AddSounderEars(h, headH, hw, m);
        }

        /// <summary>Two long hare ears on the crown — a base with a lighter inner panel and a tip that bends back at the top —
        /// each on a pivot the animator sways and flicks.</summary>
        private void AddSounderEars(float h, float headH, float hw, SounderMats m)
        {
            float ear = h * SounderRules.EarShare;
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                var root = NewPivot(_headPivot, side == 0 ? "EarL" : "EarR", new Vector3(sx * hw * 0.28f, headH * 0.45f, -headH * 0.05f));
                root.localRotation = Quaternion.Euler(-10f, 0f, -sx * 12f); // up, a little back and out
                AddPartTo(root, "EarBase", new Vector3(0f, ear * 0.27f, 0f), new Vector3(h * 0.032f, ear * 0.56f, h * 0.012f), m.Skin);
                AddPartTo(root, "EarInner", new Vector3(0f, ear * 0.3f, h * 0.007f), new Vector3(h * 0.02f, ear * 0.48f, h * 0.004f), m.Inner);
                var tip = NewPivot(root, "EarTip", new Vector3(0f, ear * 0.55f, 0f));
                tip.localRotation = Quaternion.Euler(-12f, 0f, sx * 6f);
                AddPartTo(tip, "EarTipPart", new Vector3(0f, ear * 0.22f, 0f), new Vector3(h * 0.026f, ear * 0.46f, h * 0.011f), m.Skin);
                _ears.Add(root);
            }
        }

        /// <summary>A soft point light (an Echo's glow, a Soundeling's, a Quali's), mirrored into the FX light bridge like every
        /// glowing creature's (#2151).</summary>
        private void AddSoftLight(Transform parent, Vector3 at, float range, float intensity, Color color)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            _glow = go.AddComponent<Light>();
            FxLightBridge.Mirror(_glow);
            _glow.type = LightType.Point;
            _glow.range = range;
            _glow.intensity = intensity;
            _glow.color = color;
            _glow.shadows = LightShadows.None;
        }
    }
}
