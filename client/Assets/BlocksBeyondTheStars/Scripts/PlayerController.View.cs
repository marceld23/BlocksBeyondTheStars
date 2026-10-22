// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The player's view: first/third-person camera mode and base field of view (<see cref="ApplyCameraMode"/>), the held
    /// item's viewmodel with its draw cue and swing (<see cref="RefreshHeldItem"/>, <see cref="TriggerSwing"/>), the
    /// third-person boom that keeps the camera out of walls (<see cref="ResolveCameraBoom"/>), the per-frame camera feel
    /// (<see cref="UpdateCameraFeel"/>) and the free look while control is frozen (<see cref="LookAround"/>).
    /// </summary>
    public sealed partial class PlayerController
    {
        // Camera feel (first-person head-bob, FOV kick; shakes, kicks and punches live in FxCamera since #2152).
        private float _bobPhase;
        private float _fovEased; // the eased FOV without the FxCamera punch (the punch is added on top each frame)

        private float _baseFov = 60f;

        private Viewmodel _viewmodel;
        private string _heldKey = "\0"; // forces the first refresh
        private bool _heldGloves, _heldShockGloves; // #2278: what TriggerSwing plays — read once per hotbar change, not per swing

        /// <summary>The player's field-of-view setting (#1590), pushed live from the settings screen. Only the
        /// BASE value changes: <see cref="UpdateCameraFeel"/> owns the camera and eases toward it every frame,
        /// with the walking kick and the binocular zoom staying relative to it. When nothing is zoomed the camera
        /// is written directly, so a stepper press from the pause menu shows the new view at once instead of
        /// drifting there over a second; the viewmodel re-scales so the held item keeps its screen size (#1591).</summary>
        public void SetBaseFov(float fov)
        {
            _baseFov = fov;
            _viewmodel?.SetReferenceFov(fov);
            if (Camera != null && !(_optic != null && _optic.Raised))
            {
                Camera.fieldOfView = fov;
            }
        }

        private void ApplyCameraMode()
        {
            if (ThirdPerson)
            {
                _optic?.Lower(); // the scope is a first-person instrument; UpdateCameraFeel ignores zoom in 3rd
            }

            if (Camera != null)
            {
                Camera.transform.localPosition = ThirdPerson ? ThirdPersonEye : FirstPersonEye;
                _cameraBoomLength = -1f; // the spring arm re-measures from the full offset (#1460)
            }

            // Show the avatar only in third-person (otherwise the camera is inside the head); the
            // first-person viewmodel is the opposite.
            Avatar?.SetVisible(ThirdPerson);
            _viewmodel?.SetVisible(!ThirdPerson);
        }

        /// <summary>Plays the tool swing on both the third-person avatar and the first-person viewmodel. The glove weapons
        /// (#2278) punch instead: the energy gloves left and right in turn, the shock gloves with both palms.</summary>
        private void TriggerSwing()
        {
            if (_heldGloves)
            {
                Avatar?.Punch(push: _heldShockGloves);
            }
            else
            {
                Avatar?.Swing();
            }

            _viewmodel?.Swing();
        }

        /// <summary>Mirrors the selected hotbar item into the avatar hand + viewmodel (rebuilds on change).</summary>
        private void RefreshHeldItem()
        {
            string key = Game?.ItemInSlot(Game.SelectedHotbarSlot) ?? string.Empty;
            bool dirty = Game != null && Game.HeldItemDirty; // appearance edit: same key, new arm paint (#1464)
            int slot = Game != null ? Game.SelectedHotbarSlot : -1;
            if (key == _heldKey && !dirty)
            {
                _heldSlot = slot; // the same item in another slot: that slot is the one that may run empty next (#2778)
                return;
            }

            if (Game != null)
            {
                Game.HeldItemDirty = false;
            }

            bool firstRefresh = _heldKey == "\0";
            bool sameItem = key == _heldKey;
            // #2778: a switch puts the old item away and draws the new one; when the selected slot just ran empty (the last
            // block placed, the item dropped or moved away) there is nothing to put away — the empty hand only comes up.
            var swap = firstRefresh || sameItem ? Viewmodel.Swap.Instant
                : string.IsNullOrEmpty(key) && slot == _heldSlot ? Viewmodel.Swap.DrawOnly
                : Viewmodel.Swap.Holster;
            _heldSlot = slot;
            _heldKey = key;
            _optic?.SetHeldItem(key); // swapping away from the binoculars can never strand a zoomed view
            var (kind, tint, blockKey) = HeldItem.For(Game?.Content, key);
            _heldGloves = kind == HeldItem.Kind.Gloves;
            _heldShockGloves = _heldGloves && FxLook.ForItem(Game?.Content, key).Is(FxStyles.ShockPush);
            if (_heldGloves && !firstRefresh && !sameItem)
            {
                ClientAudio.Instance?.Cue("glove_charge", 0.5f); // #2278: the gloves hum awake as they are pulled on
            }
            else if (swap == Viewmodel.Swap.Holster && Time.time - _lastDrawCue > DrawCueGap)
            {
                ClientAudio.Instance?.Cue("gear_equip", DrawCueVolume); // #2778: the draw, quietly (not once per scrolled slot)
                _lastDrawCue = Time.time;
            }

            var look = Game?.LocalToolLook(key); // the player's own look for this tool (#1963), null = standard
            Avatar?.SetHeldItem(kind, tint, blockKey, key, look, animate: swap != Viewmodel.Swap.Instant,
                holster: swap == Viewmodel.Swap.Holster);
            _viewmodel?.SetHeldItem(kind, tint, blockKey, key, look, swap);
        }

        // #2778: the held item's last slot (a slot that ran empty draws without a holster) and the draw cue's rate limit.
        private int _heldSlot = -1;
        private float _lastDrawCue = -10f;
        private const float DrawCueGap = 0.18f;
        private const float DrawCueVolume = 0.35f;

        private void LookAround()
        {
            // A magnified view multiplies every mouse movement by the same factor, so the sensitivity has to
            // come down with the field of view — at 6× an unscaled mouse is unusable.
            float sens = MouseSensitivity * (_optic != null && _optic.Raised ? _optic.SensitivityScale : 1f);
            float mx = InputMap.LookX() * sens;
            float my = InputMap.LookY() * sens * (InvertY ? -1f : 1f);
            transform.Rotate(0f, mx, 0f);
            _pitch = Mathf.Clamp(_pitch - my, -89f, 89f);
            if (Camera != null)
            {
                Camera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }
        }

        private const float CameraBoomRadius = 0.2f;   // sphere-cast radius: keeps the near plane (0.1) clear of the wall
        private const float CameraBoomMinimum = 0.35f; // never closer than this to the pivot — you still see yourself
        private float _cameraBoomLength = -1f;         // current boom length; pulls in instantly, extends smoothly

        /// <summary>Where a boom camera may sit: <paramref name="desired"/> unless world geometry sits between
        /// the pivot and it, in which case the camera is pulled in front of that geometry. Pull-in is instant
        /// (a wall must never be seen from inside), the return eases out. The player's own capsule overlaps the
        /// cast's start and is therefore ignored by Unity; creature meshes carry no colliders by design.</summary>
        private Vector3 ResolveCameraBoom(Vector3 pivot, Vector3 desired, float dt)
        {
            Vector3 to = desired - pivot;
            float full = to.magnitude;
            if (full < 0.001f)
            {
                return desired;
            }

            Vector3 dir = to / full;
            float allowed = full;
            if (Physics.SphereCast(pivot, CameraBoomRadius, dir, out var hit, full, Physics.DefaultRaycastLayers & WorldRayMask, QueryTriggerInteraction.Ignore)
                && hit.collider != _controller)
            {
                allowed = Mathf.Max(CameraBoomMinimum, hit.distance - 0.05f);
            }

            _cameraBoomLength = _cameraBoomLength < 0f || allowed < _cameraBoomLength
                ? allowed
                : Mathf.MoveTowards(_cameraBoomLength, allowed, dt * 6f);
            return pivot + dir * _cameraBoomLength;
        }

        /// <summary>First-person camera feel: a subtle walking head-bob, a small forward FOV kick while
        /// moving, and a decaying shake on landing/impacts. Composed over the look pitch each frame.</summary>
        private void UpdateCameraFeel()
        {
            if (Camera == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            // #2152: trauma shake, weapon kick and FOV punch (scaled by Screen shake / Camera motion inside FxCamera).
            // The flight view samples it itself while it owns the camera.
            var feel = Vector3.zero;
            float fovPunch = 0f;
            if (Game == null || !Game.SpaceViewActive)
            {
                FxCamera.Sample(dt, out feel, out fovPunch);
            }

            if (_fovEased <= 0f)
            {
                _fovEased = Camera.fieldOfView;
            }

            if (ThirdPerson)
            {
                _fovEased = Mathf.MoveTowards(_fovEased, _baseFov, dt * 30f);
                Camera.fieldOfView = _fovEased + fovPunch * 0.25f;
                // Spring arm: the boom used to be a fixed offset, so with your back to any wall within 3.5 m the
                // camera sat inside the block and the world showed through the culled back faces (#1460).
                Camera.transform.position = ResolveCameraBoom(
                    transform.TransformPoint(FirstPersonEye), transform.TransformPoint(ThirdPersonEye), dt);
                ApplyRestRise(dt); // #2823
                return;
            }

            // The optic's zoom is applied HERE because this method owns the field of view: it rewrites it every
            // frame, so a zoom written from anywhere else would be eased straight back out.
            bool zoomed = _optic != null && _optic.Raised;

            // Comfort: with CameraMotion off, bob/FOV-kick/shake all flatten to a steady camera. A magnified
            // view amplifies the bob just like it amplifies the mouse, so damp it by the same factor.
            float motion = (CameraMotion ? 1f : 0f) * (zoomed ? _optic.MotionScale : 1f);
            float amt = (_moving ? 1f : 0f) * motion;
            // #2188: on a wall the bob is the slower, deeper hand-over-hand reach instead of the stride.
            bool onWall = _climbing || _pullingUp;
            // #2562: wrapped to one full cycle (both waves below repeat every 2π) so hours of walking never grow the
            // phase until float precision makes the bob stutter.
            _bobPhase = Mathf.Repeat(_bobPhase + (dt * (_moving ? (onWall ? 6f : 9f) : 0f) * motion), 2f * Mathf.PI);
            float bobY = Mathf.Sin(_bobPhase * 2f) * (onWall ? 0.05f : 0.035f) * amt;
            float bobX = Mathf.Cos(_bobPhase) * 0.025f * amt;

            // #2189: a tiring grip is felt, never shown — the view trembles a little (the comfort switch flattens it).
            if (_climbStrain > 0.01f)
            {
                float tremble = _climbStrain * motion;
                bobX += Mathf.Sin(Time.time * 37f) * 0.012f * tremble;
                bobY += Mathf.Sin((Time.time * 29f) + 1.3f) * 0.010f * tremble;
            }
            // Ease the eye down toward the crouch height (_crouchT set in UpdateCrouch) as the baseline the bob rides on.
            Vector3 eye = Vector3.Lerp(FirstPersonEye, CrouchEye, _crouchT);
            Camera.transform.localPosition = eye + new Vector3(bobX, bobY, 0f);

            // A big jump (raising, stepping or dropping the optic) travels fast so it feels like a click; the
            // small walking kick keeps its original gentle 40°/s drift.
            float targetFov = zoomed ? _optic.TargetFov(_baseFov) : _baseFov + (_moving ? 4f * motion : 0f);
            float fovRate = Mathf.Abs(_fovEased - targetFov) > 6f ? 160f : 40f;
            _fovEased = Mathf.MoveTowards(_fovEased, targetFov, dt * fovRate);
            Camera.fieldOfView = _fovEased + (zoomed ? 0f : fovPunch * 0.25f);

            // A magnified view would magnify the shake too — damp it like the bob.
            float damp = zoomed ? _optic.MotionScale : 1f;
            Camera.transform.localEulerAngles = new Vector3(_pitch + feel.x * damp, feel.y * damp, feel.z * damp);
            ApplyRestRise(dt); // #2823: just up from a bed, the eye is still on its way from the pillow
        }
    }
}
