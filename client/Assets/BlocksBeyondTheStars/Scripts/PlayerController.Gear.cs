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
    /// The worn active gear and the HUD gear strip (#2290): the suit lamp (<see cref="UpdateLamp"/>, <see cref="PublishLamp"/>), the
    /// stealth toggle, the periodic check of the worn gear (<see cref="UpdateGearPeriodically"/>), the jetpack's thrust and
    /// flames (<see cref="UpdateJetpack"/>) and the spring boots' jump (<see cref="SpringJump"/>).
    /// </summary>
    public sealed partial class PlayerController
    {
        // ---- The worn active gear, for the HUD gear strip (#2290) ----

        /// <summary>The suit lamp is lit: switched on AND worn (taking it off darkens it; wearing it again relights it). #3339:
        /// any lamp the lamp key switches — the suit lamp or the Ressaw lamp (<see cref="GameBootstrap.WearsLamp"/>).</summary>
        public bool LampOn => _lampOn && WearsLamp();

        /// <summary>#3339: a lamp is worn — the suit lamp, or a night-sight lamp whose night sight the server keeps running
        /// while it shines.</summary>
        private bool WearsLamp() => Game != null && Game.WearsLamp();

        /// <summary>The jetpack is firing right now (the state last reported to the server, which drains the energy).</summary>
        public bool JetpackActive => _jetpackActive;

        /// <summary>Switches the worn suit lamp on or off — what the lamp key does, and the HUD gear strip's lamp tap (#2290).
        /// Without a worn lamp it only says where to put one on (#2291) instead of flipping a switch nothing shows.</summary>
        public void ToggleLamp()
        {
            if (!WearsLamp())
            {
                GearHint("ui.hud.no_lamp");
                return;
            }

            _lampOn = !_lampOn;
            ClientAudio.Instance?.Cue("lamp_toggle");
        }

        /// <summary>Asks the server to switch the stealth suit's cloak on or off (#2291) — the stealth key, the ACT list and
        /// the gear strip's tap. The server decides (worn suit, energy left); its answer flips <c>Game.Stealthed</c>, and the
        /// cloak's sound plays on that answer, not here. Without the suit worn it only says where to put it on.</summary>
        public void ToggleStealth()
        {
            if (!HasItem("stealth_suit"))
            {
                GearHint("ui.hud.no_stealth");
                return;
            }

            Game?.Network?.SendToggleStealth();
        }

        private float _gearHintAt = -10f;

        /// <summary>A "you are not wearing that" toast (#2291), at most one every few seconds so a held key cannot spam it.</summary>
        private void GearHint(string key)
        {
            if (Game?.Localizer == null || Time.unscaledTime - _gearHintAt < 3f)
            {
                return;
            }

            _gearHintAt = Time.unscaledTime;
            Game.ShowMessage(Game.Localizer.Get(key));
        }

        private bool _jetpackActive; // last reported jetpack thrust state (server drains energy on this)

        private int _gearLook = -1; // the GearLook mask the avatar was last dressed in
        private float _gearTimer;
        private float _gearWeight; // #2206: the slow-down of worn gear the bio lab made heavier (0 = none)
        private float _jumpBoost;  // #2295: how much higher the worn gear jumps (0..1, the spring boots' 0.6)

        /// <summary>Mirrors the player's WORN gear (#2110) onto the third-person avatar — the same <see cref="GearLook"/>
        /// mask the server sends everybody else — refreshed a couple of times a second so it tracks a change of clothes
        /// without polling hard; the avatar is only rebuilt when the mask changes.</summary>
        private void UpdateGearPeriodically()
        {
            _gearTimer -= Time.deltaTime;
            if (_gearTimer > 0f || Avatar == null || Game?.Equipment == null)
            {
                return;
            }

            _gearTimer = 0.5f;

            // #2192: the worn climbing gear — one formula with the item data (best piece counts). #2295: the spring boots'
            // jump the same way.
            if (Game.Content != null)
            {
                _climbGearGrip = SuitEquipment.ClimbGrip(Game.Content.Items.Values, HasItem);
                _climbGearIce = SuitEquipment.ClimbIce(Game.Content.Items.Values, HasItem);
                _jumpBoost = SuitEquipment.JumpBoost(Game.Content.Items.Values, HasItem);
            }

            // #2202/#2206: a grip preparation and what the lab changed on the worn gear join the same formula, under
            // its cap; gear the lab made heavier slows the walk (read in Move).
            _climbGearGrip = Mathf.Clamp(
                _climbGearGrip + Game.Bio.GripBonus
                + BlocksBeyondTheStars.Shared.Bio.GearMods.Bonus(Game.WornKeys(), BlocksBeyondTheStars.Shared.Bio.ModStat.Grip),
                0f, SuitEquipment.MaxClimbGrip);
            _gearWeight = BlocksBeyondTheStars.Shared.Bio.GearMods.Bonus(Game.WornKeys(), BlocksBeyondTheStars.Shared.Bio.ModStat.Weight);

            int look = GearLook.Mask(HasItem);
            if (look != _gearLook)
            {
                _gearLook = look;
                Avatar.SetGear(look);
                _viewmodel?.SetClimbGear(GearLook.Has(look, GearLook.ClimbingGloves), GearLook.Has(look, GearLook.ClimbingClaws)); // #2287: the pads show on the first-person climbing hands too
            }
        }

        private bool _lampOn;

        /// <summary>The lamp shines this frame (worn, switched on, no cinematic) — <see cref="PublishLamp"/> places it.</summary>
        private bool _lampLit;

        /// <summary>Decides whether the suit headlamp (toggle L) shines this frame — a lamp must be worn (the <c>suit_lamp</c>
        /// or, #3339, the <c>ressaw_lamp</c>, whose night sight the server runs on this same report) —
        /// and reports every effective change to the server: VEGA's "it's dark and your lamp is off" tip (#1077) and,
        /// since #3234, the other players see the beam. #3154: dark while a cinematic owns the camera (the lift-off shot lit
        /// the ship from its own camera) — not switched off: it shines again after. The light itself is a shader spotlight
        /// (ADR 0003, <see cref="Headlamp"/>), placed in <see cref="PublishLamp"/> once the camera has moved this frame.</summary>
        private void UpdateLamp()
        {
            bool on = _lampOn && Camera != null && WearsLamp(); // #3339: the Ressaw lamp shines too
            _lampLit = on && !(Game != null && Game.CinematicCameraActive);
            if (!_lampLit)
            {
                Headlamp.ClearLocal();
            }

            Avatar?.SetLampLit(on); // #3231: the lamp on the own avatar's helmet glows (third person)

            if (on != _lampReported)
            {
                _lampReported = on;
                Game?.Network?.SendSetLamp(on);
            }
        }

        /// <summary>The lamp state last reported to the server (a fresh session starts "off" on both ends).</summary>
        private bool _lampReported;

        /// <summary>
        /// #3229/#3231: puts the lit lamp where it is worn, after the camera has moved this frame (LateUpdate — the beam
        /// no longer trails a fast turn by a frame). First person: on the right of the helmet, a little above the eye, so
        /// surfaces get relief from it; whenever the camera is away from the eye — third person, the speeder's chase camera —
        /// the lamp on the avatar's helmet, not the camera behind it. Both aim at the point the crosshair covers a few
        /// metres out.
        /// </summary>
        private void PublishLamp()
        {
            if (!_lampLit || Camera == null)
            {
                return;
            }

            var eye = Camera.transform;
            bool awayFromEye = ThirdPerson || (eye.position - transform.TransformPoint(FirstPersonEye)).sqrMagnitude > 1f;
            Vector3 lamp = awayFromEye && Avatar != null && Avatar.TryHelmetLamp(out var helmet) ? helmet : Headlamp.HelmetFromEye(eye);
            float behind = Vector3.Distance(eye.position, lamp); // third person: aim past the avatar, not at its back
            var aim = eye.position + (eye.forward * (HeadlampRules.AimDistance + behind));
            var axis = aim - lamp;
            Headlamp.PublishLocal(lamp, axis.sqrMagnitude > 1e-6f ? axis.normalized : eye.forward);
        }

        /// <summary>True while the gear is WORN (#2110: the suit's slots, never the backpack) — the lamp, the jetpack and
        /// the avatar's plates all read this.</summary>
        private bool HasItem(string key) => Game != null && Game.Wears(key);

        /// <summary>True when the player can fire the jetpack: carries one and has suit energy left.</summary>
        private bool CanJetpack() => Game != null && Game.SuitEnergy > 0f && HasItem("jetpack");

        // Persistent jetpack thrust emitters (#1511), created on first use and toggled per frame.
        private ParticleSystem _jetFlameL, _jetFlameR;

        /// <summary>Drives the jetpack thrust VFX/audio while firing and reports state edges to the server
        /// (which is authoritative for the suit-energy drain).</summary>
        private void UpdateJetpack(bool active)
        {
            if (active)
            {
                ClientAudio.Instance?.JetTick();
                if (Weapons != null && _jetFlameL == null && _jetFlameR == null)
                {
                    // Twin thrust flames at the player's feet (offset left/right of the pack) — persistent looping
                    // emitters (#1511), created once and switched on/off below instead of a fresh burst per frame.
                    _jetFlameL = Weapons.CreateThrustFlame(transform, new Vector3(-0.2f, -0.1f, 0f), new Color(1f, 0.72f, 0.3f));
                    _jetFlameR = Weapons.CreateThrustFlame(transform, new Vector3(0.2f, -0.1f, 0f), new Color(1f, 0.55f, 0.2f));
                }
            }

            WeaponFx.SetThrust(_jetFlameL, active);
            WeaponFx.SetThrust(_jetFlameR, active);

            if (active != _jetpackActive)
            {
                _jetpackActive = active;
                Game?.Network?.SendSetJetpack(active);
            }
        }

        // ---- The spring boots (#2295) ------------------------------------------------------------------------------

        /// <summary>The spring boots' share of the jump impulse: jump HEIGHT grows with the square of the impulse, so
        /// "60 % higher" is the root of 1.6. 1 without them.</summary>
        private float SpringJumpImpulse() => Mathf.Sqrt(1f + _jumpBoost);

        private float _springJumpAt = -10f;

        /// <summary>A jump on the spring boots: their twang and the coils stretching on the avatar. Debounced, so a jump
        /// blocked by a low ceiling (the body stays grounded and re-jumps every frame) does not rattle.</summary>
        private void SpringJump()
        {
            if (Time.time - _springJumpAt < 0.25f)
            {
                return;
            }

            _springJumpAt = Time.time;
            ClientAudio.Instance?.Cue("spring_jump", 0.7f);
            Avatar?.SpringStretch();
        }
    }
}
