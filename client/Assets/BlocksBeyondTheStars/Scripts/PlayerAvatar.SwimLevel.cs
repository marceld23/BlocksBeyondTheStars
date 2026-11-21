// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3860/#3924: the swimmer's body tilt and strokes. A swimming figure tilts with its swim (<see cref="SwimRules.BodyTilt"/>
    /// — the speed along the way it faces and its vertical speed, measured from its own motion like every other pose, so the
    /// own figure and a remote one read alike): nearly still it treads water upright (arms sculling, slow kicks), a slow swim
    /// is a breast stroke, the plain swim lies forward in a crawl (~60°), the sprint nearly level, a dive goes head first and
    /// a swim backwards leans back a little. The parts all hang straight off the figure's root, so the tilt is the root's own:
    /// whoever places the figure (the own player's controller, the remote players) applies <see cref="SwimTilt"/> and
    /// <see cref="SwimTiltOffset"/>, which turn the body about its chest. Pose only — the collision capsule stays upright.
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        /// <summary>The height (scale 1) the body turns about — the chest, which stays at the waterline.</summary>
        private const float SwimPivotHeight = 1.05f;

        /// <summary>How fast (degrees per second) the body tilts toward its swim and back upright — the old level pose's pace
        /// (72° in 1 / 3.5 s).</summary>
        private const float SwimTiltRate = 252f;

        private float _swimTilt;  // degrees forward (negative leans back)
        private float _fwdSmooth; // the low-passed speed along the way the figure faces (m/s)
        private Vector3 _swimOffsetPlaced;

        /// <summary>How far the body is turned in the water: 0 upright, 1 level (more: head down in a dive; a lean back
        /// counts by its size too).</summary>
        public float SwimLevel => Mathf.Abs(_swimTilt) / 90f;

        /// <summary>The extra pitch of the figure's root while it swims (identity when upright).</summary>
        public Quaternion SwimTilt => Quaternion.Euler(_swimTilt, 0f, 0f);

        /// <summary>The shift (in the figure's yaw frame) that makes <see cref="SwimTilt"/> turn the body about its chest
        /// instead of its feet.</summary>
        public Vector3 SwimTiltOffset
        {
            get
            {
                var pivot = new Vector3(0f, SwimPivotHeight * Mathf.Max(0.2f, transform.lossyScale.y), 0f);
                return pivot - (SwimTilt * pivot);
            }
        }

        /// <summary>The placer reports the offset it applied this frame, so the motion the poses read leaves it out (else
        /// easing in would read as a step backwards and flip the pose).</summary>
        public void PlacedSwimOffset(Vector3 offset) => _swimOffsetPlaced = offset;

        /// <summary>The figure's yaw frame: its rotation with the swim tilt taken out. The rotation's own Euler decomposition
        /// is no use for it — past 90° of pitch (a dive) it hands the yaw back turned by 180°.</summary>
        private Quaternion YawFrame()
        {
            var facing = transform.rotation * Quaternion.Inverse(SwimTilt) * Vector3.forward;
            facing.y = 0f;
            return facing.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(facing) : Quaternion.identity;
        }

        /// <summary>The root position with the placed swim offset taken out — what the speed measurement reads.</summary>
        private Vector3 MotionPosition() => transform.position - (YawFrame() * _swimOffsetPlaced);

        /// <summary>Eases the body's tilt toward its swim's (<see cref="SwimRules.BodyTilt"/>), or upright out of the water.
        /// <paramref name="step"/> is this frame's level motion, <paramref name="vy"/> the smoothed vertical speed and
        /// <paramref name="lowPass"/> the speed filter's weight — the forward speed goes through the same filter.</summary>
        private void EaseSwimTilt(float dt, Vector3 step, float vy, float lowPass)
        {
            float forward = Vector3.Dot(step, YawFrame() * Vector3.forward) / dt;
            _fwdSmooth += (Mathf.Clamp(forward, -14f, 14f) - _fwdSmooth) * lowPass;
            bool swims = _swimming && !_crouched && !_climbing && !_lying && !_seated;
            float target = swims ? SwimRules.BodyTilt(_fwdSmooth, vy) : 0f;
            _swimTilt = Mathf.MoveTowards(_swimTilt, target, dt * SwimTiltRate);
        }

        /// <summary>The swim's limbs: a breast stroke, the treading pose over it while the body is near upright
        /// (<see cref="SwimRules.TreadBlend"/>) and the crawl over both as it lies forward (<see cref="SwimRules.CrawlBlend"/>).</summary>
        private void PoseSwim(float t, ref float armL, ref float armR, ref float elbowL, ref float elbowR, ref float legL,
            ref float legR, ref float kneeL, ref float kneeR, ref float rollL, ref float rollR, ref float headYaw, ref float nod)
        {
            // A breast stroke: the arms reach forward and sweep out, the legs kick in turn.
            float stroke = Mathf.Sin(t * 3.2f);
            float kick = Mathf.Sin(t * 6.4f);
            armL = armR = -92f + (28f * stroke);
            elbowL = elbowR = -18f - (22f * Mathf.Max(0f, -stroke));
            rollL = -(18f + (26f * Mathf.Max(0f, -stroke)));
            rollR = 18f + (26f * Mathf.Max(0f, -stroke));
            legL = 12f * kick;
            legR = -12f * kick;
            kneeL = 14f + (12f * Mathf.Max(0f, kick));
            kneeR = 14f + (12f * Mathf.Max(0f, -kick));
            headYaw = 0f;
            PoseTread(t, SwimRules.TreadBlend(_swimTilt), ref armL, ref armR, ref elbowL, ref elbowR, ref legL, ref legR,
                ref kneeL, ref kneeR, ref rollL, ref rollR);
            PoseCrawl(t, SwimRules.CrawlBlend(_swimTilt), ref armL, ref armR, ref elbowL, ref elbowR, ref legL, ref legR,
                ref kneeL, ref kneeR, ref rollL, ref rollR, ref nod);
        }

        /// <summary>Blends treading water over the stroke by <paramref name="k"/>: the arms scull just under the surface (the
        /// hands sweep out and in again), the legs kick slowly in turn, like pedalling.</summary>
        private static void PoseTread(float t, float k, ref float armL, ref float armR, ref float elbowL, ref float elbowR,
            ref float legL, ref float legR, ref float kneeL, ref float kneeR, ref float rollL, ref float rollR)
        {
            if (k <= 0f)
            {
                return;
            }

            float scull = Mathf.Sin(t * 2.6f);
            float pedal = Mathf.Sin(t * 2.2f);
            armL = Mathf.Lerp(armL, -38f, k);
            armR = Mathf.Lerp(armR, -38f, k);
            elbowL = Mathf.Lerp(elbowL, -30f, k);
            elbowR = Mathf.Lerp(elbowR, -30f, k);
            rollL = Mathf.Lerp(rollL, -(42f + (16f * scull)), k);
            rollR = Mathf.Lerp(rollR, 42f + (16f * scull), k);
            legL = Mathf.Lerp(legL, -22f + (16f * pedal), k);
            legR = Mathf.Lerp(legR, -22f - (16f * pedal), k);
            kneeL = Mathf.Lerp(kneeL, 30f + (22f * Mathf.Max(0f, pedal)), k);
            kneeR = Mathf.Lerp(kneeR, 30f + (22f * Mathf.Max(0f, -pedal)), k);
        }

        /// <summary>Blends the crawl over the stroke by <paramref name="k"/>: the arms reach overhead in turn, the legs
        /// flutter, the head lifts to look ahead.</summary>
        private static void PoseCrawl(float t, float k, ref float armL, ref float armR, ref float elbowL, ref float elbowR,
            ref float legL, ref float legR, ref float kneeL, ref float kneeR, ref float rollL, ref float rollR, ref float nod)
        {
            if (k <= 0f)
            {
                return;
            }

            float p = t * 4.2f;
            float flutter = Mathf.Sin(p * 2f);
            armL = Mathf.Lerp(armL, -150f + (75f * Mathf.Sin(p)), k);
            armR = Mathf.Lerp(armR, -150f + (75f * Mathf.Sin(p + Mathf.PI)), k);
            elbowL = Mathf.Lerp(elbowL, -10f - (20f * Mathf.Max(0f, Mathf.Cos(p))), k);
            elbowR = Mathf.Lerp(elbowR, -10f - (20f * Mathf.Max(0f, -Mathf.Cos(p))), k);
            rollL = Mathf.Lerp(rollL, -8f, k);
            rollR = Mathf.Lerp(rollR, 8f, k);
            legL = Mathf.Lerp(legL, 14f * flutter, k);
            legR = Mathf.Lerp(legR, -14f * flutter, k);
            kneeL = Mathf.Lerp(kneeL, 6f + (8f * Mathf.Max(0f, flutter)), k);
            kneeR = Mathf.Lerp(kneeR, 6f + (8f * Mathf.Max(0f, -flutter)), k);
            nod = Mathf.Lerp(nod, -50f, k); // the head lifts to look ahead over the water
        }
    }
}
