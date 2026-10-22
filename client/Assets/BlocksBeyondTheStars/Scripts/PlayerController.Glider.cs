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
    /// The glider (#2296): arming it in a fall, the glide's steering, sink and wind drift (<see cref="Glide"/>), the refusal
    /// where there is no air to fly in, and opening / folding the wing (<see cref="UpdateGlide"/>).
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>The glider's wing is open (#2296) — set where the glide opens and folds (<see cref="UpdateGlide"/>); the
        /// HUD gear strip lights the glider from it.</summary>
        public bool Gliding { get; private set; }

        // ---- The glider (#2296) ----------------------------------------------------------------------------------
        // Holding Jump while falling with a worn glider opens the wing: the fall slows to a steady sink and the player
        // sails forward where they look. Like the jetpack's thrust it is the client's own on-foot movement and costs no
        // energy; the server only checks that a glider is worn (a refusal folds the wing) and shows the wing to others.

        /// <summary>The steady sink under the open wing, m/s — the same on every world: gravity only decides how quickly a
        /// glide that started slower than this reaches it. Far under the safe landing speed, so a glide never lands hard.</summary>
        private const float GlideSinkSpeed = 2.5f;

        /// <summary>How hard the opening wing brakes a faster fall, m/s² (gravity is cancelled on top of it).</summary>
        private const float GlideBrake = 35f;

        private const float GlideCruiseSpeed = 8f; // m/s forward, hands off
        private const float GlideFastSpeed = 10f;  // with forward held
        private const float GlideSlowSpeed = 4f;   // with back held — the brake
        private const float GlideSideSpeed = 3f;   // sideways drift with left / right
        private const float GlideAccel = 6f;       // m/s² — the glide's speed has momentum, the walk's does not
        private const float GlideWindDrift = 2.5f; // m/s downwind in a full gale (#900: the wind holds a hanging player)

        private Vector3 _glideVel;           // the glide's horizontal velocity
        private int _gliderRejectionsSeen;   // GameBootstrap.GliderRejections when last checked
        private bool _glideRefused;          // the server refused the wing: it stays folded until Jump is let go
        private bool _gliderNoAirHinted;     // "without air …" is said once per session

        /// <summary>How far below the take-off height a fall must reach before a held Jump opens the wing, m — so an
        /// ordinary hop with Jump held (pillar building, hopping along) stays a hop.</summary>
        private const float GlideArmDrop = 1.5f;

        private float _airTakeoffY;   // where the current fall left the ground (or the water, the ladder, the wall)
        private bool _glidePressArmed; // Jump pressed again in the air: open the wing as soon as we fall

        /// <summary>Whether a held Jump may open (or keep) the wing (#2296): an open wing stays open; otherwise only on the
        /// way down, and only on a real drop — past <see cref="GlideArmDrop"/> below the take-off — or after a fresh press
        /// in the air.</summary>
        private bool GlideArmed()
            => Gliding || (_verticalVelocity < 0f && (_glidePressArmed || transform.position.y < _airTakeoffY - GlideArmDrop));

        /// <summary>Whether the air here can carry the glider: a world with an atmosphere (an airless body reports a space
        /// sky and no air density), below its line (<c>OnFootInSpace</c> is the server's "above the atmosphere"), not on a
        /// spacewalk or floating in a station's zero-g; inside a station only where it breathes (a player-built deck is open
        /// space, #1473). Water needs no check: swimming wins in <see cref="Move"/> before the airborne branch.</summary>
        private bool GlideAirHere()
        {
            if (Game == null || Game.OnFootInSpace || Game.InEva || Game.StationZeroG)
            {
                return false;
            }

            var env = Game.Environment;
            if (env == null || env.SpaceSky || env.AtmosphereDensity <= 0f)
            {
                return false;
            }

            return string.IsNullOrEmpty(Game.CurrentStationId) || env.Breathable;
        }

        /// <summary>One glide step in the air: the sink (<see cref="GlideSink"/>) and the forward sail — the horizontal
        /// velocity eases toward cruise speed along the view (forward speeds up, back brakes, left / right drift sideways),
        /// a gale pushes it downwind, and a wall it ran into eats the speed it took.</summary>
        private void Glide(float h, float v, ref Vector3 move)
        {
            if (!Gliding)
            {
                _glideVel = new Vector3(move.x, 0f, move.z); // the wing opens with the run the jump had
            }
            else
            {
                var actual = _controller.velocity;
                actual.y = 0f;
                if (actual.sqrMagnitude < _glideVel.sqrMagnitude)
                {
                    _glideVel = actual; // blocked (a wall, a ledge): carry on from what we really did
                }
            }

            // The view's flattened forward is the body's: the camera only pitches under it (first and third person).
            float speed = v >= 0f ? Mathf.Lerp(GlideCruiseSpeed, GlideFastSpeed, v) : Mathf.Lerp(GlideCruiseSpeed, GlideSlowSpeed, -v);
            Vector3 target = (transform.forward * speed) + (transform.right * (h * GlideSideSpeed));
            if (Game != null && Game.WindSpeed > 0.05f && Game.ExposedToSky)
            {
                target += Game.WindVector * GlideWindDrift;
            }

            target.y = 0f;
            _glideVel = Vector3.MoveTowards(_glideVel, target, GlideAccel * Time.deltaTime);
            move.x = _glideVel.x;
            move.z = _glideVel.z;
            GlideSink();
        }

        /// <summary>The open wing's hold on the fall: a fall faster than the sink brakes toward it, a slower one falls on
        /// under gravity until it gets there — never pushed upward. Call after this frame's gravity.</summary>
        private void GlideSink()
        {
            if (_verticalVelocity < -GlideSinkSpeed)
            {
                _verticalVelocity = Mathf.Min(-GlideSinkSpeed, _verticalVelocity + ((GlideBrake + _effGravity) * Time.deltaTime));
            }
        }

        /// <summary>Holding Jump while falling with the glider where there is no air: say why nothing opens, once. Not
        /// before this world's environment has arrived — until then "no air" is only "not known yet".</summary>
        private void GliderNoAirHint()
        {
            if (_gliderNoAirHinted || Game?.Localizer == null || Game.Environment == null)
            {
                return;
            }

            _gliderNoAirHinted = true;
            Game.ShowMessage(Game.Localizer.Get("ui.hud.glider_no_air"));
        }

        /// <summary>Opens or folds the glider's wing: the edge goes to the server (it shows the wing to everyone else) and
        /// the avatar, the opening rustle plays, and every open frame keeps the wind loop up. Mirrors
        /// <see cref="UpdateJetpack"/> — every place that cuts the jetpack folds the wing too.</summary>
        private void UpdateGlide(bool active)
        {
            if (active)
            {
                ClientAudio.Instance?.GlideTick();
            }

            if (active == Gliding)
            {
                return;
            }

            Gliding = active;
            Avatar?.SetGliding(active);
            Game?.Network?.SendSetGliding(active);
            if (active)
            {
                ClientAudio.Instance?.Cue("glider_open", 0.8f);
            }
        }
    }
}
