// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The first-person side of a block carried over the head (#3766). The block is held up with BOTH hands (#3828 — the
    /// player's report: "lifting looks odd from first person"): it rises to the top middle of the view, level and bigger,
    /// and two suit arms come up from below to its lower corners — the aim point between them stays free. Before, the
    /// carry only shifted the one-handed rest pose, which put the block ON the aim point with the open end of its
    /// forearm in the middle of the view. The numbers are <see cref="BlockCarryPose"/>.
    /// </summary>
    public sealed partial class Viewmodel
    {
        private bool _carrying;
        private float _carryView; // 0..1
        private Transform _carryArmR, _carryArmL;

        /// <summary>Starts or ends the overhead carry.</summary>
        public void SetCarrying(bool on) => _carrying = on;

        /// <summary>This frame's shift of the held item's rest position (zero when nothing is carried): from the rest
        /// pose — with whatever stance and grip lift it has — to the block's place over the head.</summary>
        private Vector3 CarryShift()
        {
            _carryView = BlockCarryPose.Ease(_carryView, _carrying, Time.deltaTime);
            if (_carryView <= 0f)
            {
                return Vector3.zero;
            }

            var to = BlockCarryPose.ViewBlockPos;
            var rest = RestPos + _stanceShift + new Vector3(0f, _grip != null ? GripLift : 0f, 0f);
            return (new Vector3(to.X, to.Y, to.Z) - rest) * _carryView;
        }

        /// <summary>This frame's turn of the held item: from its rest angles to level, tipped a little so its underside shows.</summary>
        private Vector3 CarryTurn()
            => _carryView <= 0f ? Vector3.zero : (new Vector3(BlockCarryPose.ViewBlockPitch, 0f, 0f) - RestEuler - _stanceTurn) * _carryView;

        /// <summary>
        /// Once a frame after the holder was posed: the carried block grows, the one hand under it goes (two arms hold it
        /// now) and the arms rise to its lower corners — or everything is put back. <paramref name="shown"/> is false while
        /// the first-person hands are not drawn at all or the climb has them.
        /// </summary>
        private void PoseCarry(bool shown)
        {
            float up = shown ? _carryView : 0f;
            if (_mesh != null)
            {
                float scale = ItemScale * Mathf.Lerp(1f, BlockCarryPose.ViewBlockScale, up);
                if (!Mathf.Approximately(_mesh.transform.localScale.x, scale))
                {
                    _mesh.transform.localScale = Vector3.one * scale;
                }
            }

            if (_grip != null && _grip.Hand != null && _grip.Hand.activeSelf != (up <= 0f))
            {
                _grip.Hand.SetActive(up <= 0f);
            }

            if (up <= 0f)
            {
                if (_carryArmR != null)
                {
                    Destroy(_carryArmR.gameObject); // rebuilt on the next lift — in the arm paint of that moment
                    Destroy(_carryArmL.gameObject);
                    _carryArmR = _carryArmL = null;
                }

                return;
            }

            if (_carryArmR == null)
            {
                var tint = HeldItem.For(null, null).tint; // the suit's arm colour, like the bare hand (#1033)
                _carryArmR = HeldItem.BuildCarryArm(transform, tint, left: false).transform;
                _carryArmL = HeldItem.BuildCarryArm(transform, tint, left: true).transform;
            }

            PoseCarryArm(_carryArmR, +1f, up);
            PoseCarryArm(_carryArmL, -1f, up);
        }

        private void PoseCarryArm(Transform arm, float side, float up)
        {
            var p = BlockCarryPose.ViewArmPos;
            var e = BlockCarryPose.ViewArmEuler;
            float rise = Mathf.SmoothStep(0f, 1f, up);
            arm.localPosition = Compensated(new Vector3(side * p.X, p.Y - (SinkDepth * (1f - rise)), p.Z));
            arm.localEulerAngles = new Vector3(e.X, side * e.Y, side * e.Z);
            arm.localScale = Vector3.one * (ItemScale * _fovScale);
        }
    }
}
