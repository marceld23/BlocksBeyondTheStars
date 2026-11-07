// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Rudolpho, the alien reindeer (#3322) — the companion the star bell from door 24 of the Advent calendar calls. A deer on
    /// the standard four-legged rig (so <see cref="CreatureAnimator"/> walks, breathes, grazes and flicks its ears like any
    /// quadruped): four slim legs on hooves, a deer's body in a shaggy warm-brown coat with a cream ruff and belly, a long
    /// neck, a head with ears and <b>two pairs of eyes</b> (he is from out here, after all), a short tail and <b>branching
    /// antlers</b> whose tips glow a soft cool white-blue. His <b>red lantern nose</b> is an unlit red block on the snout with
    /// a warm red-orange point light (<see cref="FxLightBridge"/>) that lights a cave or a night around him like a small
    /// lantern. Proportions follow the standard plan's unit, the size the server measures his body by.
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        /// <summary>The sleigh-bell jingle Rudolpho arrives with (tools/ai-assets, #3322).</summary>
        public const string ReindeerBellSound = "reindeer_bells";

        /// <summary>The nose: a bright red that reads at once, never the enemies' darker red.</summary>
        private static readonly Color ReindeerNose = new Color(1f, 0.16f, 0.1f);

        /// <summary>The lantern's light — a warm red-orange, softer than the nose itself.</summary>
        private static readonly Color ReindeerNoseLight = new Color(1f, 0.45f, 0.26f);

        /// <summary>The antler tips' glow — a soft cool white-blue.</summary>
        private static readonly Color ReindeerTipGlow = new Color(0.78f, 0.93f, 1f);

        /// <summary>How far the lantern nose lights, in blocks — a small lantern, whatever his size.</summary>
        private const float ReindeerLanternRange = 9f;

        /// <summary>How bright the lantern is (the FX light maps 2 → 1.2, "a bright lamp" is 1).</summary>
        private const float ReindeerLanternIntensity = 2f;

        /// <summary>The materials of one reindeer build.</summary>
        private sealed class ReindeerMats
        {
            public Material Coat, Ruff, Belly, Shank, Hoof, Antler, Tip, Nose, Eye;
        }

        /// <summary>The authored companions' bodies: the hunter robot (#3295) and Rudolpho (#3322). False for every other plan.</summary>
        private bool BuildCompanionBody(GameObject root, NetCreature c)
        {
            switch (c.BodyPlan)
            {
                case "Robot":
                    BuildRobot(root, c); // CreatureBuilder.Robot.cs
                    return true;
                case "Reindeer":
                    BuildReindeer(root, c);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Builds Rudolpho: legs, body, neck, head (eyes, ears, antlers, nose), tail, the lantern light, then hands the
        /// rig to the animator as a four-legged standard body.</summary>
        private void BuildReindeer(GameObject root, NetCreature c)
        {
            float u = 0.5f * Mathf.Clamp(c.Size, 0.4f, 3f); // the standard plan's unit — the server's footprint uses it too
            var m = ReindeerMaterials(c);
            _bodyMat = m.Coat;

            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);
            float legLen = 1.3f * u;
            float bodyY = legLen + 0.3f * u;
            AddReindeerBody(body.transform, bodyY, u, m);
            for (int row = 0; row < 2; row++)
            {
                float z = row == 0 ? 0.6f * u : -0.72f * u;
                for (int side = 0; side < 2; side++)
                {
                    float x = (side == 0 ? -1f : 1f) * 0.27f * u;
                    _legs.Add(AddReindeerLeg(body.transform, row, side, new Vector3(x, legLen, z), legLen, 0.17f * u, m));
                }
            }

            var face = AddReindeerNeckAndHead(body.transform, bodyY, u, m);
            AddReindeerFace(c, face, u, m);
            AddReindeerAntlers(face, u, m);
            AddReindeerNose(face, u, m);
            AddTail(body.transform, new Vector3(0f, bodyY + 0.18f * u, -0.98f * u), 0.32f * u, 0.17f * u, 2, m.Ruff);

            if (root.GetComponent<ReindeerBells>() == null)
            {
                root.AddComponent<ReindeerBells>(); // the jingle when he appears near the player
            }

            int idh = CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed);
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(Describe(c, body.transform, u, legLen, idh));
        }

        /// <summary>His fixed look: the server's warm brown and cream (the hostile red cast and the sleep dim still apply; the
        /// companions' green cast does not — like the robot, he is recognised by his nose and his name).</summary>
        private static ReindeerMats ReindeerMaterials(NetCreature c)
        {
            var mood = MoodOf(c, 0.25f, true, false);
            Color brown = c.ColorRgb != 0 ? Rgb(c.ColorRgb) : new Color(0.54f, 0.35f, 0.23f);
            Color cream = c.BellyRgb != 0 ? Rgb(c.BellyRgb) : new Color(0.92f, 0.86f, 0.78f);
            Color Tint(Color col, float shade = 1f)
            {
                var t = mood.Tint(col);
                float k = shade * mood.Dim;
                return new Color(t.r * k, t.g * k, t.b * k, 1f);
            }

            return new ReindeerMats
            {
                Coat = Lit(Tint(brown), HideOr("shaggy")),
                Ruff = Lit(Tint(cream), HideOr("shaggy")),
                Belly = Lit(Tint(cream), HideOr("fur")),
                Shank = Lit(Tint(brown, 0.6f), HideOr("fur")),
                Hoof = Lit(new Color(0.18f, 0.14f, 0.12f), null),
                Antler = Lit(new Color(0.8f, 0.7f, 0.54f), null),
                Tip = Unlit(ReindeerTipGlow),
                Nose = Unlit(ReindeerNose),
                Eye = EyeFaceMaterial(c),
            };
        }

        /// <summary>The deer body: a deeper chest and a rump, a cream belly, a shaggy cream ruff at the throat and a mane of
        /// tufts along the back.</summary>
        private void AddReindeerBody(Transform body, float bodyY, float u, ReindeerMats m)
        {
            AddPartTo(body, "Chest", new Vector3(0f, bodyY + 0.04f * u, 0.4f * u), new Vector3(0.8f, 0.78f, 1f) * u, m.Coat);
            AddPartTo(body, "Rump", new Vector3(0f, bodyY, -0.5f * u), new Vector3(0.76f, 0.7f, 0.96f) * u, m.Coat);
            AddPartTo(body, "Belly", new Vector3(0f, bodyY - 0.34f * u, -0.04f * u), new Vector3(0.66f, 0.16f, 1.6f) * u, m.Belly);
            AddPartTo(body, "Ruff", new Vector3(0f, bodyY - 0.08f * u, 0.9f * u), new Vector3(0.66f, 0.62f, 0.24f) * u, m.Ruff);
            for (int i = 0; i < 4; i++)
            {
                // The shaggy coat: tufts that stand a little proud of the back and the flanks, each a touch off the last.
                float z = Mathf.Lerp(0.7f, -0.8f, i / 3f) * u;
                float top = (z >= 0f ? 0.42f : 0.35f) * u; // the chest stands a little higher than the rump
                float lift = (i % 2 == 0 ? 0.03f : 0f) * u;
                AddPartTo(body, "Tuft" + i, new Vector3(0f, bodyY + top + lift, z), new Vector3(0.5f, 0.08f, 0.3f) * u, m.Coat);
                AddPartTo(body, "FlankShag" + i, new Vector3(0f, bodyY - 0.28f * u, z), new Vector3(0.84f, 0.1f, 0.3f) * u, m.Coat);
            }
        }

        /// <summary>One slim jointed leg — the hip → knee → foot pivots the gait and the foot planner drive, a coat thigh, a
        /// darker shank and a hoof. Fore legs fold back, hind legs forward, as on every quadruped (<see cref="AddLeg"/>).</summary>
        private LegRig AddReindeerLeg(Transform parent, int row, int side, Vector3 hipPos, float legLen, float thick, ReindeerMats m)
        {
            float upper = legLen * UpperLegShare;
            float lower = legLen - upper;
            var hip = NewPivot(parent, (side == 0 ? "LegL" : "LegR") + row, hipPos);
            AddPartTo(hip, "Thigh", new Vector3(0f, -upper * 0.5f, 0f), new Vector3(thick * 1.25f, upper, thick * 1.35f), m.Coat);
            var knee = NewPivot(hip, "Knee", new Vector3(0f, -upper, 0f));
            AddPartTo(knee, "Shank", new Vector3(0f, -lower * 0.5f, 0f), new Vector3(thick * 0.75f, lower, thick * 0.75f), m.Shank);
            var foot = NewPivot(knee, "Foot", new Vector3(0f, -lower, 0f));
            AddPartTo(foot, "Hoof", new Vector3(0f, -thick * 0.2f, thick * 0.2f), new Vector3(thick * 1.1f, thick * 0.45f, thick * 1.3f), m.Hoof);
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

        /// <summary>The long neck leaning forward (on a fixed tilt under the animator's neck joint, which owns the joint's
        /// rotation) with a cream mane down its front, and the head joint on top. Returns the head's face frame — the
        /// animator turns the head joint, the face hangs under it pointing forward and a little down.</summary>
        private Transform AddReindeerNeckAndHead(Transform body, float bodyY, float u, ReindeerMats m)
        {
            var neck = NewPivot(body, "Neck0", new Vector3(0f, bodyY + 0.28f * u, 0.66f * u));
            _neckChain.Add(neck);
            var lean = NewPivot(neck, "NeckLean", Vector3.zero);
            lean.localRotation = Quaternion.Euler(24f, 0f, 0f);
            AddPartTo(lean, "NeckSeg0", new Vector3(0f, 0.36f * u, 0f), new Vector3(0.34f, 0.8f, 0.4f) * u, m.Coat);
            AddPartTo(lean, "NeckMane", new Vector3(0f, 0.3f * u, 0.2f * u), new Vector3(0.28f, 0.7f, 0.1f) * u, m.Ruff);

            var head = NewPivot(lean, "Head", new Vector3(0f, 0.74f * u, 0.02f * u));
            _heads.Add(head);
            var face = NewPivot(head, "Face", Vector3.zero);
            face.localRotation = Quaternion.Euler(-14f, 0f, 0f); // undo most of the neck's lean: the snout points forward
            _headPivot = face;
            return face;
        }

        /// <summary>Skull, muzzle and hinged jaw, two pairs of eyes (a big lower pair and a small upper one — he is an alien
        /// reindeer) with lids, and two ears on pivots the animator flicks.</summary>
        private void AddReindeerFace(NetCreature c, Transform face, float u, ReindeerMats m)
        {
            AddPartTo(face, "Skull", new Vector3(0f, 0.05f * u, 0.16f * u), new Vector3(0.42f, 0.38f, 0.48f) * u, m.Coat);
            AddPartTo(face, "Muzzle", new Vector3(0f, -0.04f * u, 0.52f * u), new Vector3(0.28f, 0.26f, 0.4f) * u, m.Coat);
            _jawPivot = AddPivotPart(face, "Jaw", new Vector3(0f, -0.14f * u, 0.28f * u), new Vector3(0f, -0.03f * u, 0.2f * u),
                new Vector3(0.24f, 0.09f, 0.4f) * u, m.Belly);
            _jaws.Add(_jawPivot);

            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                // The big pair sits on the sides of the head like a deer's; the small pair looks ahead from the brow, above
                // the muzzle, so a friend in front of him sees all four.
                var big = new Vector3(sx * 0.2f * u, 0.07f * u, 0.3f * u);
                var small = new Vector3(sx * 0.13f * u, 0.17f * u, 0.4f * u);
                AddEye(face, "Eye" + side, big, 0.13f * u, new Vector3(sx * 0.62f, 0.05f, 0.78f).normalized, m.Eye);
                AddEyelid(big, 0.13f * u, m.Coat);
                AddEye(face, "EyeUpper" + side, small, 0.085f * u, new Vector3(sx * 0.35f, 0.15f, 0.92f).normalized, m.Eye);
                AddEyelid(small, 0.085f * u, m.Coat);

                var ear = AddPivotPart(face, side == 0 ? "EarL" : "EarR", new Vector3(sx * 0.2f * u, 0.2f * u, 0.02f * u),
                    new Vector3(0f, 0.12f * u, 0f), new Vector3(0.07f, 0.24f, 0.13f) * u, m.Coat);
                ear.localRotation = Quaternion.Euler(-10f, 0f, -sx * 62f); // out to the side, a little back
                _ears.Add(ear);
            }
        }

        /// <summary>Branching antlers: on each side a main beam that sweeps up and back, then forward, with a brow tine, a
        /// forward tine and an outward tine — every tip softly glowing white-blue.</summary>
        private void AddReindeerAntlers(Transform face, float u, ReindeerMats m)
        {
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                var antlerBase = NewPivot(face, side == 0 ? "AntlerL" : "AntlerR", new Vector3(sx * 0.11f * u, 0.22f * u, 0.04f * u));
                var beam = AddAntlerSegment(antlerBase, "Beam", Vector3.zero, new Vector3(-24f, 0f, -sx * 26f), 0.46f * u, 0.07f * u, m, false);
                AddAntlerSegment(beam, "BrowTine", new Vector3(0f, 0.08f * u, 0f), new Vector3(72f, 0f, sx * 6f), 0.2f * u, 0.05f * u, m, true);
                AddAntlerSegment(beam, "FrontTine", new Vector3(0f, 0.3f * u, 0f), new Vector3(58f, 0f, 0f), 0.24f * u, 0.05f * u, m, true);
                var upper = AddAntlerSegment(beam, "Beam2", new Vector3(0f, 0.46f * u, 0f), new Vector3(30f, 0f, sx * 12f), 0.36f * u, 0.06f * u, m, true);
                AddAntlerSegment(upper, "OuterTine", new Vector3(0f, 0.16f * u, 0f), new Vector3(10f, 0f, -sx * 48f), 0.22f * u, 0.045f * u, m, true);
            }
        }

        /// <summary>One antler piece on its own node at <paramref name="at"/> turned by <paramref name="euler"/>: a thin box
        /// <paramref name="length"/> long up the node's y, and, when <paramref name="tipped"/>, a glowing tip at its end.</summary>
        private Transform AddAntlerSegment(Transform parent, string name, Vector3 at, Vector3 euler, float length, float thick,
            ReindeerMats m, bool tipped)
        {
            var node = NewPivot(parent, name, at);
            node.localRotation = Quaternion.Euler(euler);
            AddPartTo(node, name + "Bone", new Vector3(0f, length * 0.5f, 0f), new Vector3(thick, length, thick), m.Antler);
            if (tipped)
            {
                AddPartTo(node, name + "Tip", new Vector3(0f, length, 0f), Vector3.one * (thick * 1.45f), m.Tip);
            }

            return node;
        }

        /// <summary>The red lantern nose on the snout and its light: warm red-orange, about nine blocks — enough to light a
        /// dark cave or a night around him like a small lantern.</summary>
        private void AddReindeerNose(Transform face, float u, ReindeerMats m)
        {
            AddPartTo(face, "Nose", new Vector3(0f, -0.01f * u, 0.74f * u), new Vector3(0.17f, 0.15f, 0.1f) * u, m.Nose);
            var go = new GameObject("NoseLantern");
            go.transform.SetParent(face, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.86f * u);
            _glow = go.AddComponent<Light>();
            FxLightBridge.Mirror(_glow);
            _glow.type = LightType.Point;
            _glow.range = ReindeerLanternRange;
            _glow.intensity = ReindeerLanternIntensity;
            _glow.color = ReindeerNoseLight;
            _glow.shadows = LightShadows.None;
        }

        /// <summary>Plays the sleigh-bell jingle once when Rudolpho appears near the camera — called by his bell, or arriving
        /// with his owner on a new world.</summary>
        private sealed class ReindeerBells : MonoBehaviour
        {
            private const float HearRange = 20f;

            private void Start()
            {
                var audio = ClientAudio.Instance;
                var cam = Camera.main;
                if (audio == null || cam == null
                    || (cam.transform.position - transform.position).sqrMagnitude > HearRange * HearRange)
                {
                    return;
                }

                audio.At(ReindeerBellSound, transform.position + Vector3.up, 1f, 0.85f);
            }
        }
    }
}
