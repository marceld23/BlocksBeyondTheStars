// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The pose of <see cref="GuardianGiantView"/>: the root between the lying pose at the anchor and the standing one at the
    /// server's position (a leap flies it along an arc to the aim), the six bones from the <see cref="GuardianGiantFrame"/>,
    /// the head turned to the aim, a shudder and the client-only flinch of a hit limb.
    /// </summary>
    public sealed partial class GuardianGiantView
    {
        /// <summary>How long a hit limb jerks (seconds).</summary>
        private const float FlinchSeconds = 0.25f;

        private GuardianGiantPart _flinchPart;
        private float _flinchUntil = -1f;
        private Vector3 _jumpFrom;
        private bool _jumpFromSet;

        /// <summary>Puts the root and the bones into <paramref name="f"/>.</summary>
        private void ApplyPose(in GuardianGiantFrame f, GuardianGiantTrack t, float now)
        {
            var root = _rig.Root.transform;
            var stand = StandingScenePos(f, t, now);
            var lying = _hasAnchor ? _game.ScenePos(_anchor.x, _anchor.y, _anchor.z) : stand; // the anchor cell's corner is the root
            var lyingRot = Quaternion.Euler(0f, GuardianGiantPose.LyingYaw(_facing), 0f) * Quaternion.Euler(GuardianGiantPose.LyingPitch, 0f, 0f);
            var standRot = Quaternion.Euler(0f, _yaw, 0f);
            float shake = f.Shudder * (UiKit.ReducedMotion ? 0.25f : 1f);
            float n1 = (Mathf.PerlinNoise(Time.time * 17f, 0.31f) * 2f) - 1f;
            float n2 = (Mathf.PerlinNoise(Time.time * 17f, 4.73f) * 2f) - 1f;
            root.position = Vector3.LerpUnclamped(lying, stand, f.Rise) + (Vector3.up * f.RootY) + (new Vector3(n1, 0f, n2) * (0.06f * shake));
            root.rotation = Quaternion.SlerpUnclamped(lyingRot, standRot, f.Rise) * Quaternion.Euler(n2 * 1.5f * shake, 0f, n1 * 1.5f * shake);

            Bone(GuardianGiantPart.Torso, f.TorsoPitch, f.TorsoYaw, f.TorsoRoll);
            float headPitch = f.HeadPitch, headYaw = f.HeadYaw;
            if (f.LookAtAim > 0.01f)
            {
                LookAtAim(t.Status, ref headPitch, ref headYaw, Mathf.Clamp01(f.LookAtAim));
            }

            Bone(GuardianGiantPart.Head, headPitch, headYaw, 0f);
            Bone(GuardianGiantPart.ArmLeft, f.ArmLPitch, 0f, f.ArmLRoll);
            Bone(GuardianGiantPart.ArmRight, f.ArmRPitch, 0f, f.ArmRRoll);
            Leg(GuardianGiantPart.LegLeft, f.LegLPitch, f.LegLLift);
            Leg(GuardianGiantPart.LegRight, f.LegRPitch, f.LegRLift);
        }

        /// <summary>The standing root's scene position: the server's position — during a jump attack the arc from the take-off
        /// point to the aim (<see cref="GuardianGiantFrame.Air"/>), then a short glide back onto the server's spot.</summary>
        private Vector3 StandingScenePos(in GuardianGiantFrame f, GuardianGiantTrack t, float now)
        {
            var stand = _game.ScenePos(_stand.x, _stand.y, _stand.z);
            if (t.Move != GuardianGiantMove.JumpAttack)
            {
                _jumpFromSet = false;
                return stand;
            }

            var aim = AimScene(t.Status);
            if (f.Air >= 0f)
            {
                if (!_jumpFromSet)
                {
                    _jumpFrom = stand;
                    _jumpFromSet = true;
                }

                float peak = GuardianGiantPose.ArcPeak(Vector3.Distance(_jumpFrom, aim), 4f);
                return ToUnity(GuardianGiantPose.Arc(ToShared(_jumpFrom), ToShared(aim), f.Air, peak));
            }

            var spec = GuardianGiantRules.Spec(GuardianGiantMove.JumpAttack);
            float el = t.Elapsed(now);
            return _jumpFromSet && el > spec.Impact
                ? Vector3.Lerp(aim, stand, GuardianGiantPose.Smooth(spec.Impact, spec.Impact + 0.5f, el))
                : stand;
        }

        /// <summary>Turns the head toward the attack's aim point by <paramref name="weight"/> (clamped to what a neck can do).</summary>
        private void LookAtAim(GuardianGiantStatus s, ref float pitch, ref float yaw, float weight)
        {
            var head = _rig.Bone(GuardianGiantPart.Head);
            var dir = head.parent.InverseTransformDirection(AimScene(s) - head.TransformPoint(GuardianGiantModel.VisorLocal));
            if (dir.sqrMagnitude < 0.01f)
            {
                return;
            }

            float aimYaw = Mathf.Clamp(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, -70f, 70f);
            float aimPitch = Mathf.Clamp(Mathf.Atan2(-dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg, -40f, 50f);
            pitch = Mathf.Lerp(pitch, aimPitch, weight);
            yaw = Mathf.Lerp(yaw, aimYaw, weight);
        }

        private void Bone(GuardianGiantPart part, float pitch, float yaw, float roll)
            => _rig.Bone(part).localRotation = Quaternion.Euler(pitch + Flinch(part), yaw, roll);

        private void Leg(GuardianGiantPart part, float pitch, float lift)
        {
            var bone = _rig.Bone(part);
            bone.localPosition = _rig.Rest[(int)part] + (Vector3.up * lift);
            bone.localRotation = Quaternion.Euler(pitch + Flinch(part), 0f, 0f);
        }

        /// <summary>The jerk of a hit limb: a short recoil that does not break the move it plays.</summary>
        private float Flinch(GuardianGiantPart part)
        {
            if (part != _flinchPart || Time.time >= _flinchUntil || UiKit.ReducedMotion)
            {
                return 0f;
            }

            float k = 1f - ((_flinchUntil - Time.time) / FlinchSeconds);
            return -10f * Mathf.Sin(k * Mathf.PI);
        }

        /// <summary>A hull drop on the planet-enemy entry: the limb nearest the local player's shot (else the torso) flashes and
        /// jerks, and the crosshair's hit marker fires when the shot was ours.</summary>
        private void DetectHit(NetCombatEntity e)
        {
            if (e == null)
            {
                return;
            }

            if (_prevHull >= 0f && e.Hull < _prevHull - 0.25f)
            {
                bool mine = _game.LastShotTargetId == e.Id && Time.time - _game.LastShotTime < 0.6f;
                if (mine)
                {
                    _game.Views.Hud?.ShowHitMarker();
                }

                _flinchPart = mine ? NearestLimbToAim() : GuardianGiantPart.Torso;
                _flinchUntil = Time.time + FlinchSeconds;
                var col = _rig.Colliders[(int)_flinchPart];
                var at = col != null ? col.bounds.center : _rig.Root.transform.position + (Vector3.up * 8f);
                FxKit.Flash(at, new Color(1f, 0.85f, 0.7f), 2.2f * FxKit.FlashScale);
                Weapons?.Sparks(at, new Color(1f, 0.6f, 0.25f), 8);
            }

            _prevHull = e.Hull;
        }

        /// <summary>The limb whose hit box lies closest to the camera's line of sight (what the player aimed at).</summary>
        private GuardianGiantPart NearestLimbToAim()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return GuardianGiantPart.Torso;
            }

            var origin = cam.transform.position;
            var dir = cam.transform.forward;
            var best = GuardianGiantPart.Torso;
            float bestDist = float.MaxValue;
            foreach (var part in GuardianGiantBody.Parts)
            {
                var col = _rig.Colliders[(int)part];
                if (col == null)
                {
                    continue;
                }

                var to = col.bounds.center - origin;
                float d = Vector3.Cross(dir, to).magnitude;
                if (Vector3.Dot(dir, to) > 0f && d < bestDist)
                {
                    bestDist = d;
                    best = part;
                }
            }

            return best;
        }

        private Vector3 AimScene(GuardianGiantStatus s) => _game.ScenePos(s.AimX, s.AimY, s.AimZ);

        private static Vector3f ToShared(Vector3 v) => new Vector3f(v.x, v.y, v.z);

        private static Vector3 ToUnity(Vector3f v) => new Vector3(v.X, v.Y, v.Z);
    }
}
