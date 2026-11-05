// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The player's own body sounds (#3239/#3240): footsteps whose rhythm follows the walk (three takes per surface, the
    /// feet alternating, a little random pitch and level), the climber's hand-over-hand taps from the same takes, a landing
    /// chosen by the fall speed, the water (a splash in and out, strokes, a quiet bubble bed while the head is under, a gasp
    /// after a long dive), the light breath and heartbeat layer, and the ears: in third person the scene's one
    /// AudioListener sits at the head instead of 3.5 m behind it. The rules live Unity-free in Client.Core
    /// (<see cref="FootstepRules"/>, <see cref="SwimSounds"/>, <see cref="BodySounds"/>); this part only feeds them and
    /// plays what they pick through <see cref="ClientAudio.PlayOwn"/> (2D, pitched, the ship's room and the suit applied).
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>The scene's one AudioListener, on a child of the camera (wired by WorldRig) — it turns with the camera.
        /// In third person (and in a speeder's chase view) <see cref="UpdateEars"/> puts it at the head, everywhere else
        /// (first person, the flight view, a cinematic) it sits on the camera itself (#3240).</summary>
        public Transform Ears;

        private const float OwnStepLevel = 0.45f;
        private const float ClimbTapLevel = 0.25f;
        private const float BubblesLevel = 0.1f;
        private const float BubblesFadePerSecond = 0.15f;

        /// <summary>Where the head is sampled for "under water" — the same height as ClientAudio's underwater muffle.</summary>
        private const float HeadHeight = 1.5f;

        /// <summary>A body moved further than this (m) in one frame was put there (a respawn, a beam), not walked.</summary>
        private const float AudioSnapDistance = 4f;

        private readonly System.Random _audioRng = new System.Random();
        private FootstepCadence _cadence;
        private FootstepCadence _climbCadence;
        private SwimSounds _swimLayer;
        private BodySounds _bodyLayer;
        private AudioSource _bubbles;
        private bool _bubblesMissing;
        private Vector3 _audioLastPos;
        private bool _audioPosKnown;
        private float _airSeconds;
        private int _runFrame = -1;
        private int _hardLandingFrame = -1;
        private int _headUnderFrame = -1;

        private FootstepCadence Cadence => _cadence ??= new FootstepCadence(_audioRng);

        private SwimSounds SwimLayer => _swimLayer ??= new SwimSounds(_audioRng);

        private BodySounds BodyLayer => _bodyLayer ??= new BodySounds(_audioRng);

        /// <summary>Per on-foot frame from <see cref="Move"/>, after the body moved: the footsteps, the water and the time in
        /// the air that a landing reads.</summary>
        private void MoveAudio(bool grounded, bool inWater, float h, float v, float prevVy)
        {
            float dt = Time.deltaTime;
            var pos = transform.position;
            bool feetWet = FeetInWater();
            bool snapped = !_audioPosKnown || (pos - _audioLastPos).sqrMagnitude > AudioSnapDistance * AudioSnapDistance;
            _audioPosKnown = true;
            _audioLastPos = pos;
            if (snapped)
            {
                SwimLayer.Resync(feetWet); // no splash, landing or gasp from being put somewhere
                _airSeconds = 0f;
                return;
            }

            TrackAirTime(grounded, inWater, dt);
            bool input = Mathf.Abs(h) + Mathf.Abs(v) > 0.1f;
            var velocity = _controller.velocity;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            // The landing frame (grounded, not yet _wasGrounded) belongs to LandingAudio — no footstep on top of the landing.
            bool walking = grounded && _wasGrounded && !inWater && input && speed > FootstepRules.MinStepSpeed;
            var gait = FootstepRules.GaitFor(speed, _effMoveSpeed, _crouched);
            if (Cadence.Tick(dt, walking, gait, speed, _effMoveSpeed, out var step))
            {
                PlayStep(SurfaceStep(), step, OwnStepLevel);
            }

            if (walking && gait == FootGait.Run)
            {
                _runFrame = Time.frameCount;
            }

            // #2134: a gas giant's gas sea is swum like water but is no water — no splash, stroke, bubbles or gasp in it.
            if (InGasSea(pos))
            {
                SwimLayer.Resync(false);
                return;
            }

            bool headUnder = Game != null && Game.IsWaterAt(pos + (Vector3.up * HeadHeight));
            if (headUnder)
            {
                _headUnderFrame = Time.frameCount;
            }

            bool stroking = input || InputMap.JumpHeld();
            PlayOwnCue(SwimLayer.Tick(new SwimInput(dt, feetWet, inWater, stroking, headUnder, -prevVy)));
        }

        /// <summary>The landing's sound by the fall speed (#3239), from <see cref="Move"/>'s landing frame: nothing for a
        /// stair or a slope, a footstep for a hop, "land" for a drop, "land_heavy" near the safe fall speed. Feet that came
        /// down in water already splashed on the way in.</summary>
        private void LandingAudio(float prevVy)
        {
            float air = _airSeconds;
            _airSeconds = 0f;
            if (FeetInWater())
            {
                return;
            }

            var landing = FootstepRules.Landing(-prevVy, _effSafeFallSpeed, air);
            if (landing.Tier == LandingTier.None)
            {
                return;
            }

            if (landing.Tier == LandingTier.Soft)
            {
                PlayStep(SurfaceStep(), Cadence.Next(FootGait.Walk), landing.Volume);
            }
            else
            {
                float pitch = FootstepRules.Jitter(_audioRng.NextDouble(), FootstepRules.PitchJitter);
                ClientAudio.Instance?.PlayOwn(landing.Clip, landing.Volume, pitch);
            }

            Cadence.Landed(); // the next footstep comes one stride after the landing, not on top of it
            if (landing.Tier == LandingTier.Heavy)
            {
                _hardLandingFrame = Time.frameCount; // the body layer exhales a moment later
            }
        }

        /// <summary>How long the feet have been off the ground — a landing after less is a stair, not a hop.</summary>
        private void TrackAirTime(bool grounded, bool inWater, float dt)
        {
            if (inWater)
            {
                _airSeconds = 0f; // swimming is no time in the air
            }
            else if (!grounded)
            {
                _airSeconds += dt;
            }
            else if (_wasGrounded)
            {
                _airSeconds = 0f; // the landing frame itself (grounded, not yet _wasGrounded) keeps it for LandingAudio
            }
        }

        private bool InGasSea(Vector3 pos)
        {
            string key = BlockKeyAt(pos + (Vector3.up * 0.6f));
            return key == "gas" || key == "gas_dense";
        }

        /// <summary>#3239: a climber's hand-over-hand tap on the wall — the wall's own step takes, quietly.</summary>
        private void PlayClimbTap(string surfaceCue)
        {
            _climbCadence ??= new FootstepCadence(_audioRng);
            PlayStep(surfaceCue, _climbCadence.Next(FootGait.Walk), ClimbTapLevel);
        }

        /// <summary>One footfall on a surface: the take's clip (falling back to take 1 when it is not bundled) at the shape's
        /// level and pitch.</summary>
        private static void PlayStep(string surfaceCue, StepShape step, float level)
        {
            var audio = ClientAudio.Instance;
            if (audio != null)
            {
                audio.PlayOwn(FootstepSurface.TakeClip(surfaceCue, step.Take, audio.Has), level * step.Volume, step.Pitch);
            }
        }

        private static void PlayOwnCue(BodyCue cue)
        {
            if (!cue.IsNone)
            {
                ClientAudio.Instance?.PlayOwn(cue.Id, cue.Volume, cue.Pitch);
            }
        }

        /// <summary>Per frame from LateUpdate, whatever owned the body this frame (on foot, the flight view, a menu): the
        /// ears, the bubble bed and the breath and heartbeat layer.</summary>
        private void LateAudio()
        {
            UpdateEars();
            TickBubbles();
            TickBodyLayer();
        }

        /// <summary>#3240: in third person the camera hangs 3.5 m behind the player, so the player's own sounds came from
        /// behind and the world was heard from a spot nobody stands on. The ears go to the head there (turning with the
        /// camera, as they are its child) and back onto the camera whenever something else holds it — the flight view
        /// re-parents the camera, a cinematic or a cutscene moves it.</summary>
        private void UpdateEars()
        {
            if (Ears == null || Camera == null)
            {
                return;
            }

            bool atHead = Game != null && (ThirdPerson || !string.IsNullOrEmpty(Game.InSpeeder))
                && !Game.SpaceViewActive && !Game.CinematicCameraActive && !GameBootstrap.CutsceneActive
                && Camera.transform.parent == transform;
            if (atHead)
            {
                Ears.position = transform.TransformPoint(Vector3.Lerp(FirstPersonEye, CrouchEye, _crouchT));
            }
            else if (Ears.localPosition != Vector3.zero)
            {
                Ears.localPosition = Vector3.zero;
            }
        }

        /// <summary>#3240: a very quiet bubble bed while the head is under water — it fades in and out, and stops whenever the
        /// on-foot frame did not run (a menu, the flight view) or the player is dead.</summary>
        private void TickBubbles()
        {
            bool under = _headUnderFrame == Time.frameCount && Game != null && Game.Health > 0f;
            if (_bubbles == null)
            {
                if (!under || _bubblesMissing)
                {
                    return;
                }

                var clip = Resources.Load<AudioClip>("audio/underwater_bubbles");
                if (clip == null)
                {
                    _bubblesMissing = true;
                    return;
                }

                _bubbles = gameObject.AddComponent<AudioSource>();
                _bubbles.playOnAwake = false;
                _bubbles.loop = true;
                _bubbles.spatialBlend = 0f;
                _bubbles.volume = 0f;
                _bubbles.clip = clip;
            }

            float target = under ? BubblesLevel * ClientAudio.EffectsVolume : 0f;
            // Unscaled: a paused world (time scale 0) behind a menu still fades the bed out.
            _bubbles.volume = Mathf.MoveTowards(_bubbles.volume, target, Time.unscaledDeltaTime * BubblesFadePerSecond);
            if (_bubbles.volume > 0f && !_bubbles.isPlaying)
            {
                _bubbles.Play();
            }
            else if (_bubbles.volume <= 0f && _bubbles.isPlaying)
            {
                _bubbles.Stop();
            }
        }

        /// <summary>#3240: the light body layer — an exhale after a long run, burn or a hard landing, quicker breaths on low
        /// oxygen, a soft heartbeat on low health. Off while dead or respawning, in a menu, a cinematic or a cutscene.</summary>
        private void TickBodyLayer()
        {
            if (Game == null || ClientAudio.Instance == null)
            {
                return;
            }

            bool active = _spawned && !_settling && Game.Health > 0f && !Game.AwaitingRespawnConfirm && !Game.Spectating
                && !Game.MenuOpen && !Game.CinematicCameraActive && !GameBootstrap.CutsceneActive;
            int frame = Time.frameCount;
            var input = new BodyInput(Time.deltaTime, active, Game.Health / DamageFeedback.PlayerMaxHealth, Game.Oxygen,
                _headUnderFrame == frame, _runFrame == frame, _jetpackActive, _hardLandingFrame == frame);
            PlayOwnCue(BodyLayer.Tick(input));
        }
    }
}
