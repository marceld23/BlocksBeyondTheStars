// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3094 ("the robbers don't look like all the other people — keep their mask, but generate the hair like the villagers',
    /// just no beard"): what the human rig needs to be a robber — the cloth mask over the lower face (<see cref="SetRobberMask"/>),
    /// the weapon stance of a robber who fights (<see cref="SetStance"/>: the gunner aims, the blade robber brandishes) and the
    /// gunner's recoil (<see cref="Recoil"/>). Built by <c>WorldEntities</c> on the same <see cref="Build"/> as the villagers,
    /// so a robber has their head, hair rule and jointed limbs. Also the figure's own materials (<see cref="OwnLit"/>), which
    /// now go with it: robbers and villagers come into reach and leave all session long.
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        /// <summary>#3094: how a robber holds its weapon while it fights.</summary>
        public enum Stance
        {
            /// <summary>The ordinary pose (the weapon at the side).</summary>
            None,

            /// <summary>The gunner: the arm straight out at the target, the barrel level along it.</summary>
            Aim,

            /// <summary>The blade robber: the forearm across in front, the blade up.</summary>
            Brandish,
        }

        // The stance's arm (the joint angles of the walk: negative = forward and up). Aim: the upper arm level, the elbow
        // straight. Brandish: the upper arm forward, the forearm level in front — the blade then stands upright in the fist.
        private const float AimUpperArm = -84f, AimElbow = -4f;
        private const float BrandishUpperArm = -36f, BrandishElbow = -54f;
        private const float StanceBlendRate = 6f;  // per second
        private const float RecoilSeconds = 0.18f;
        private const float RecoilLift = 12f;      // degrees the barrel kicks up

        private Stance _stance;
        private Stance _stanceShown = Stance.Aim; // the stance easing out keeps its arm
        private float _stanceBlend;
        private float _recoilTimer;

        private readonly List<Material> _ownMats = new List<Material>();

        /// <summary>#3094: takes up (or, with <see cref="Stance.None"/>, lowers) a weapon stance — eased in and out; a swing or
        /// a blow still takes the arm over while it plays.</summary>
        public void SetStance(Stance stance)
        {
            _stance = stance;
            if (stance != Stance.None)
            {
                _stanceShown = stance;
            }
        }

        /// <summary>#3094: the gunner's shot — the barrel kicks up a little and settles.</summary>
        public void Recoil() => _recoilTimer = RecoilSeconds;

        /// <summary>The stance over the pose underneath (<see cref="Animate"/>): the right arm eases into the aim or the
        /// brandish, and an aimed barrel is held level along the arm.</summary>
        private void PoseStance(float dt, ref float armR, ref float elbowR, ref float level)
        {
            bool free = _stance != Stance.None && !_lying && !_climbing && !_seated && !_gliding;
            _stanceBlend = Mathf.MoveTowards(_stanceBlend, free ? 1f : 0f, dt * StanceBlendRate);
            if (_stanceBlend <= 0f)
            {
                _recoilTimer = 0f;
                return;
            }

            bool aim = _stanceShown == Stance.Aim;
            float upper = aim ? AimUpperArm : BrandishUpperArm;
            float elbow = aim ? AimElbow : BrandishElbow;
            float kick = 0f;
            if (_recoilTimer > 0f)
            {
                _recoilTimer -= dt;
                kick = Mathf.Sin(Mathf.Clamp01(1f - (_recoilTimer / RecoilSeconds)) * Mathf.PI);
                upper -= RecoilLift * kick;
            }

            float k = Mathf.SmoothStep(0f, 1f, _stanceBlend);
            armR = Mathf.Lerp(armR, upper, k);
            elbowR = Mathf.Lerp(elbowR, elbow, k);
            if (aim)
            {
                level = Mathf.Max(level, k * (1f - (0.5f * kick))); // the barrel tips up with the kick, then levels again
            }
        }

        /// <summary>
        /// #3094: the robber's cloth mask, pulled up over nose and mouth — the "this one is a robber" cue (never a red band:
        /// red belongs to the Guardians, #601). A cloth wrap round the lower head (head-LOCAL units: the head is a 0.46-scaled
        /// unit cube, its front at z = 0.5) that stops just under the eyes, a knot at the back, and the mouth hidden under it.
        /// Called once, right after <see cref="Build"/> with <see cref="LowerFace.Bare"/> — a robber wears no beard, the mask
        /// would cover it anyway.
        /// </summary>
        public void SetRobberMask(Color cloth)
        {
            if (_head == null)
            {
                return;
            }

            var mat = OwnLit(cloth, _suitTex); // the suit weave reads as fabric
            AddCube("MaskWrap", _head, new Vector3(0f, -0.27f, 0f), new Vector3(1.05f, 0.48f, 1.05f), mat);
            AddCube("MaskNose", _head, new Vector3(0f, -0.09f, 0.53f), new Vector3(0.22f, 0.12f, 0.06f), mat);
            AddCube("MaskKnot", _head, new Vector3(0f, -0.12f, -0.56f), new Vector3(0.16f, 0.14f, 0.1f), mat);
            AddCube("MaskTailL", _head, new Vector3(-0.05f, -0.3f, -0.57f), new Vector3(0.07f, 0.24f, 0.04f), mat);
            AddCube("MaskTailR", _head, new Vector3(0.06f, -0.32f, -0.56f), new Vector3(0.07f, 0.2f, 0.04f), mat);
            foreach (var feature in _faceFeatures)
            {
                if (feature != null && feature.name == "Mouth")
                {
                    feature.SetActive(false); // under the cloth — and a renderer toggle would come back with SetVisible
                }
            }
        }

        /// <summary>#3094: a lit material that belongs to this figure and is destroyed with it (<see cref="DestroyOwnMaterials"/>).
        /// The gear's and the body paint's materials keep their own bookkeeping.</summary>
        private Material OwnLit(Color color, Texture2D tex)
        {
            var m = Lit(color, tex);
            _ownMats.Add(m);
            return m;
        }

        private void DestroyOwnMaterials()
        {
            foreach (var m in _ownMats)
            {
                BlockCubeFaces.DestroyAsset(m); // Destroy in play, DestroyImmediate in an EditMode test
            }

            _ownMats.Clear();
        }
    }
}
