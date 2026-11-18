// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feel;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The held item has a little weight of its own: it trails the view when the player looks around (look sway) and
    /// dips when the feet land. Both are small offsets on the rest pose, flattened by the "Camera motion" comfort switch.
    /// </summary>
    public sealed partial class Viewmodel
    {
        private const float SwayPerLook = 0.0035f;  // metres of trail per unit of look input, as a 60 fps frame delivers it
        private const float SwayLookFrame = 1f / 60f; // the frame length SwayPerLook was tuned at, seconds
        private const float SwayMinFrame = 0.002f;  // a frame shorter than this counts as this long (no blown-up look rate)
        private const float SwayLimit = 0.022f;     // the furthest the item trails, metres
        private const float SwayFollowRate = 11f;   // how fast the trail follows / settles (per second)
        private const float SwayTiltPerMetre = 220f; // degrees of roll and pitch per metre of trail
        private const float LandFallSpeed = 3f;     // a fall slower than this is a step down, not a landing
        private const float LandDipDepth = 0.05f;   // the deepest dip of a hard landing, metres
        private const float LandDipRecover = 9f;    // how fast the item comes back up (per second)

        private Vector2 _sway;
        private float _landDip;
        private float _prevVy;

        /// <summary>Adds the look sway and the landing dip to the right hand's pose (and mirrors the dip to the left).</summary>
        private void PoseWeight(float dt, float vy, ref Vector3 posOff, ref Vector3 rot, ref Vector3 posL)
        {
            // A landing: the fall just stopped.
            if (_prevVy < -LandFallSpeed && vy > -1f)
            {
                _landDip = Mathf.Max(_landDip, Mathf.Clamp01(-_prevVy / 14f) * LandDipDepth);
            }

            _prevVy = vy;
            _landDip *= Mathf.Exp(-LandDipRecover * dt);

            // The look input is what one FRAME turned, so the same turn reads twice as large at 30 fps as at 60: it is
            // brought to the turn of a 60 fps frame first. Behind a menu the pointer moves, not the view — no trail then.
            float perFrame = SwayPerLook * SwayLookFrame / Mathf.Max(dt, SwayMinFrame);
            var want = FxKit.CameraMotion && !LookHeldElsewhere()
                ? new Vector2(
                    Mathf.Clamp(-InputMap.LookX() * perFrame, -SwayLimit, SwayLimit),
                    Mathf.Clamp(InputMap.LookY() * perFrame, -SwayLimit, SwayLimit))
                : Vector2.zero;
            _sway = Vector2.Lerp(_sway, want, FlightFeel.Follow(SwayFollowRate, dt));

            float dip = FxKit.CameraMotion ? _landDip : 0f;
            posOff += new Vector3(_sway.x, _sway.y - dip, 0f);
            rot += new Vector3(-_sway.y * SwayTiltPerMetre, 0f, -_sway.x * SwayTiltPerMetre);
            posL += new Vector3(_sway.x, _sway.y - dip, 0f);
        }

        /// <summary>True while the look input does not turn the view — a menu, the chat, a cutscene or the death screen
        /// owns it (the cases in which <see cref="PlayerController"/> and the spacewalk stop looking around).</summary>
        private bool LookHeldElsewhere()
            => Game != null && (Game.MenuOpen || Game.ChatTyping || Game.CinematicCameraActive || Game.AwaitingRespawnConfirm
                                || GameBootstrap.CutsceneActive || OnScreenKeyboardUi.IsOpen);
    }
}
