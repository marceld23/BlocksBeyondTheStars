// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Running on foot and the body's stamina (#3373–#3377, an idea of Justus'): hold Shift — or click the pad's left stick /
    /// tap the touch RUN button while moving — to run <see cref="SprintState.SpeedFactor"/> times as fast while the shared
    /// <see cref="BodyStamina"/> lasts. Climbing spends the same stamina (<see cref="UpdateWallClimb"/>); it comes back only at
    /// rest and slowly. The HUD draws it as a small bar under the crosshair while it is not full (HudUi.Stamina.cs). Like all
    /// on-foot movement this is the client's: the server trusts the reported position, nothing is saved.
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>How much further the view widens while running, degrees (on top of the walking kick; the "Camera
        /// motion" comfort switch flattens it).</summary>
        private const float SprintFovKick = 4f;

        /// <summary>Below this stamina the first time, VEGA explains the bar once (#3377).</summary>
        private const float StaminaHintBelow = 0.6f;

        private readonly BodyStamina _stamina = new BodyStamina();
        private readonly SprintState _sprint = new SprintState();
        private float _sprintBreathTimer;
        private float _staminaHintTimer;
        private System.Action _staminaHintShown; // cached: the check may run every frame while the lesson waits

        /// <summary>The body's stamina, 0 (spent) … 1 (fresh) — the HUD bar's fill.</summary>
        public float Stamina => _stamina.Value;

        /// <summary>The stamina ran dry and is not yet back to a quarter — the HUD bar wears its warm tint.</summary>
        public bool StaminaSpent => _stamina.Spent;

        /// <summary>Running this frame.</summary>
        public bool Sprinting => _sprint.Active;

        /// <summary>The run switch of the pad / touch is on — the RUN button lights up.</summary>
        public bool SprintLatched => _sprint.Latched;

        /// <summary>The body could run here this frame or the last (on foot, not crouched, swimming, on a wall, …) — the pad's
        /// shared left-stick click is a run only then; in a speeder, an EVA or the water it keeps opening the actions list
        /// (<see cref="InputMap.ContextPressIsSprint"/>). Frame-stamped, so the order of the two scripts' Updates does not
        /// matter and a state that skips the movement step reads false.</summary>
        public bool SprintPossible => Time.frameCount - _sprintPossibleFrame <= 1;

        private int _sprintPossibleFrame = -10;

        /// <summary>
        /// One frame of the run (#3374): reads the held key and the pad/touch switch, decides whether the body runs, spends the
        /// stamina while it does, and returns the factor for the horizontal walk (1 when not running). Called from
        /// <see cref="Move"/> after crouch and flight are known and before the rope and the wall climb, so the climb's rest
        /// check sees this frame's run.
        /// </summary>
        private float UpdateSprint(bool grounded, bool inWater, bool onLadder, float h, float v)
        {
            bool allowed = SprintAllowedHere(inWater, onLadder);
            if (allowed)
            {
                _sprintPossibleFrame = Time.frameCount;
            }

            bool running = _sprint.Step(InputMap.SprintKeyHeld(), InputMap.SprintToggleDown(), h, v, grounded, allowed, _stamina.CanSprint);
            float dt = Time.deltaTime;
            if (running)
            {
                _stamina.Spend(dt * SprintState.DrainPerSecond);
            }

            SprintBreath(dt, running);
            MaybeShowStaminaHint();
            return running ? SprintState.SpeedFactor : 1f;
        }

        /// <summary>Where running exists at all: on foot under gravity, not crouched, swimming, on a ladder, on a wall or the
        /// rope, flying, gliding, on a jetpack burn, seated, resting, riding a train or watching as an observer.</summary>
        private bool SprintAllowedHere(bool inWater, bool onLadder)
            => Game != null && !Game.Spectating && !inWater && !onLadder && !_crouched && !_flying && !_climbing && !_pullingUp
               && !_rope.Attached && !Gliding && !_jetpackActive && !Game.OnFootInSpace && !Game.StationZeroG && !Game.InEva
               && string.IsNullOrEmpty(Game.InSpeeder) && _train.Frame == null && _seatCell is null && !Resting;

        /// <summary>A strained breath while a run has worn the stamina down (the climbing clip, #2189). The long exhale after
        /// the run is the body layer's own (#3240, it hears the run gait).</summary>
        private void SprintBreath(float dt, bool running)
        {
            var audio = ClientAudio.Instance;
            if (audio == null || !running || _stamina.Strain <= 0f)
            {
                _sprintBreathTimer = 0.2f;
                return;
            }

            _sprintBreathTimer -= dt;
            if (_sprintBreathTimer <= 0f)
            {
                _sprintBreathTimer = Mathf.Lerp(2.2f, 1.3f, _stamina.Strain);
                audio.Cue("climb_strain", 0.12f + (0.12f * _stamina.Strain));
            }
        }

        /// <summary>#3377: the first time the stamina runs low (a long run or climb), VEGA says what the new bar is — once,
        /// remembered in the client settings like the climbing lesson. With VEGA's hints off the lesson waits.</summary>
        private void MaybeShowStaminaHint()
        {
            var settings = Game?.Settings;
            if (settings == null || settings.StaminaHintShown || !settings.VegaHints || _stamina.Value >= StaminaHintBelow)
            {
                return;
            }

            _staminaHintTimer -= Time.deltaTime;
            if (_staminaHintTimer > 0f)
            {
                return;
            }

            _staminaHintTimer = 0.5f; // VEGA keeps one copy of a waiting lesson; asking twice a second is enough
            Game.Views.Vega?.SayLocal("vega.hint.stamina", _staminaHintShown ??= MarkStaminaHintShown);
        }

        private void MarkStaminaHintShown()
        {
            var settings = Game?.Settings;
            if (settings != null && !settings.StaminaHintShown)
            {
                settings.StaminaHintShown = true;
                settings.Save();
            }
        }

        /// <summary>A fresh body: full stamina, no run — a spawn, a respawn, a new world. Not every <see cref="SnapTo"/>:
        /// the fall guard and a block placed into one's own column snap too, and must not refill the stamina.</summary>
        private void ResetStamina()
        {
            _stamina.Reset();
            _sprint.Stop();
        }

        /// <summary>
        /// #3373: rests the body where the movement step (whose climb check rests it on the ground and on a ladder) never
        /// runs: on a seat, in a bed, on a train seat, at the helm or in a speeder, and standing on the ground behind a menu,
        /// the chat or a cutscene. The run ends there too. Called once per frame before those branches return.
        /// </summary>
        private void RestStaminaOutsideMove()
        {
            if (Game == null)
            {
                return;
            }

            bool frozen = Game.MenuOpen || Game.ChatTyping || Game.CinematicCameraActive || GameBootstrap.CutsceneActive
                          || OnScreenKeyboardUi.IsOpen;
            bool still = _seatCell is not null || _restCell is not null || Game.TrainSeat >= 0 || Game.SpaceViewActive
                         || !string.IsNullOrEmpty(Game.InSpeeder)
                         || (frozen && !_climbing && _controller != null && _controller.isGrounded);
            if (!still)
            {
                return;
            }

            _stamina.Rest(Time.deltaTime);
            _sprint.Stop();
        }
    }
}
