// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3860: the sprint swim's level pose. A figure that swims fast (<see cref="SwimRules.SprintPoseSpeed"/>, measured from
    /// its own motion like every other pose, so the own figure and a remote one read alike) lies forward in the water and
    /// crawls: the arms reach overhead in turn, the legs flutter, the head lifts to look ahead. The parts all hang straight off
    /// the figure's root, so the tilt is the root's own: whoever places the figure (the own player's controller, the remote
    /// players) applies <see cref="SwimTilt"/> and <see cref="SwimTiltOffset"/>, which turn the body about its chest.
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        /// <summary>How far forward (degrees) a level swimmer lies.</summary>
        private const float SwimLevelPitch = 72f;

        /// <summary>The height (scale 1) the body turns about — the chest, which stays at the waterline.</summary>
        private const float SwimPivotHeight = 1.05f;

        /// <summary>How fast (per second) the body goes level and back upright.</summary>
        private const float SwimLevelRate = 3.5f;

        /// <summary>Below this speed (m/s) a level swimmer rights itself again — under <see cref="SwimRules.SprintPoseSpeed"/>,
        /// so a speed hovering at the threshold does not flip the pose back and forth.</summary>
        private const float SwimLevelKeepSpeed = SwimRules.SprintPoseSpeed - 0.7f;

        private float _swimLevel;
        private Vector3 _swimOffsetPlaced;

        /// <summary>0 upright … 1 lying level in the water.</summary>
        public float SwimLevel => _swimLevel;

        /// <summary>The extra pitch of the figure's root while it swims level (identity when upright).</summary>
        public Quaternion SwimTilt => Quaternion.Euler(SwimLevelPitch * _swimLevel, 0f, 0f);

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

        /// <summary>The root position with the placed swim offset taken out — what the speed measurement reads.</summary>
        private Vector3 MotionPosition()
            => transform.position - (Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * _swimOffsetPlaced);

        /// <summary>Eases the level pose toward lying (a fast swimmer) or upright.</summary>
        private void EaseSwimLevel(float dt, float speed)
        {
            bool level = _swimming && !_crouched
                && (_swimLevel > 0.5f ? speed > SwimLevelKeepSpeed : SwimRules.LiesLevel(_swimming, speed));
            _swimLevel = Mathf.MoveTowards(_swimLevel, level ? 1f : 0f, dt * SwimLevelRate);
        }

        /// <summary>Blends the crawl over the breast stroke by <see cref="SwimLevel"/>: the arms reach overhead in turn, the
        /// legs flutter, the head lifts to look ahead.</summary>
        private void PoseCrawl(float t, ref float armL, ref float armR, ref float elbowL, ref float elbowR, ref float legL,
            ref float legR, ref float kneeL, ref float kneeR, ref float rollL, ref float rollR, ref float nod)
        {
            float k = _swimLevel;
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
