// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Other players' footsteps and landings (#3239) — a friend walking past used to be silent. The presence carries no
    /// "on the ground" and no speed, so both are read from the drawn body: how fast it moves along the ground, and whether a
    /// solid cell (a parked ship's first) holds it up. The same rules as the player's own feet pick the rhythm, the take
    /// and the landing (<see cref="FootstepRules"/>). Only the <see cref="StepVoices"/> nearest players within
    /// <see cref="StepHearRange"/> step audibly, each in its own rhythm, and the mixer's RemotePlayer budget caps the
    /// voices. A cloaked, seated, climbing, gliding, roped or jetpacking player makes no steps, nor does a train rider (the
    /// wagon carries the drawn body, so its motion is no walk).
    /// </summary>
    public sealed partial class RemotePlayers
    {
        private const float StepHearRange = 16f;
        private const int StepVoices = 3;
        private const float RemoteStepLevel = 0.35f;

        /// <summary>A remote landing against the player's own (the <see cref="FootstepRules.Landing"/> levels are the own body's).</summary>
        private const float RemoteLandingScale = 0.75f;

        /// <summary>Further than this (m) in one frame is a teleport or a seam, not a walk.</summary>
        private const float RemoteSnapDistance = 4f;

        /// <summary>Falling faster than this (m/s) is off the ground even over a solid cell; rising faster is a jump.</summary>
        private const float RemoteFallSpeed = 2.5f;

        private const float RemoteRiseSpeed = 4.5f;

        private readonly System.Random _stepRng = new System.Random();
        private readonly List<Remote> _stepHeard = new List<Remote>();
        private System.Func<int, int, int, string> _stepShipKey, _stepWorldKey;
        private Vector3 _stepEars;

        /// <summary>One remote's motion read from its drawn body, and its own step rhythm.</summary>
        private sealed class StepState
        {
            public FootstepCadence Cadence;
            public Vector3 LastPos;
            public bool Known;
            public bool Grounded;
            public float Speed;          // along the ground, smoothed (m/s)
            public float AirSeconds;     // off the ground so far
            public float FallSpeed;      // the fastest descent of this time in the air
            public LandingSound Landing; // the landing that ended a time in the air this frame
        }

        /// <summary>Per frame, after every body is posed: reads each remote's motion, then plays the steps of the nearest few.</summary>
        private void TickRemoteSteps()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            var audio = ClientAudio.Instance;
            bool mute = audio == null || Game == null || Game.SpaceViewActive || GameBootstrap.CutsceneActive;
            _stepEars = Game != null ? Game.PlayerPosition : Vector3.zero;
            _stepHeard.Clear();
            foreach (var r in _remotes.Values)
            {
                if (r.Go == null)
                {
                    continue;
                }

                r.Steps ??= new StepState { Cadence = new FootstepCadence(_stepRng) };
                TrackRemoteMotion(r, r.Steps, dt);
                if (!mute && !r.Hidden && !r.TimedOut && StepDistanceSq(r) <= StepHearRange * StepHearRange)
                {
                    _stepHeard.Add(r);
                }
            }

            if (_stepHeard.Count > StepVoices)
            {
                _stepHeard.Sort(CompareStepDistance);
            }

            foreach (var r in _remotes.Values)
            {
                if (r.Steps != null && r.Go != null)
                {
                    int rank = _stepHeard.IndexOf(r);
                    StepRemote(r, r.Steps, dt, rank >= 0 && rank < StepVoices ? audio : null);
                }
            }
        }

        private float StepDistanceSq(Remote r) => (r.Go.transform.position - _stepEars).sqrMagnitude;

        private int CompareStepDistance(Remote a, Remote b) => StepDistanceSq(a).CompareTo(StepDistanceSq(b));

        /// <summary>Reads this frame's motion from the drawn body: its speed along the ground, whether it stands, and a landing
        /// that just ended a time in the air. Anything that is no walk (hidden, seated, climbing, gliding, roped, jetpacking,
        /// riding a train, swimming, a teleport) starts the reading afresh.</summary>
        private void TrackRemoteMotion(Remote r, StepState s, float dt)
        {
            s.Landing = LandingSound.Silent;
            var pos = r.Go.transform.position;
            bool off = r.Hidden || r.TimedOut || r.Seated || r.Climbing || r.Gliding || r.Roped || r.Jetpacking || r.Frame.Length > 0;
            if (!s.Known || off || (pos - s.LastPos).sqrMagnitude > RemoteSnapDistance * RemoteSnapDistance || RemoteSwims(pos))
            {
                s.Known = true;
                s.LastPos = pos;
                s.Grounded = false;
                s.Speed = 0f;
                s.AirSeconds = 0f;
                s.FallSpeed = 0f;
                return;
            }

            var velocity = (pos - s.LastPos) / dt;
            s.LastPos = pos;
            s.Speed = Mathf.Lerp(s.Speed, new Vector2(velocity.x, velocity.z).magnitude, 1f - Mathf.Exp(-dt * 10f));
            s.Grounded = velocity.y > -RemoteFallSpeed && velocity.y < RemoteRiseSpeed && RemoteSupported(pos);
            if (!s.Grounded)
            {
                s.AirSeconds += dt;
                s.FallSpeed = Mathf.Max(s.FallSpeed, -velocity.y);
            }
            else if (s.AirSeconds > 0f)
            {
                s.Landing = FootstepRules.Landing(s.FallSpeed, FootstepRules.DefaultSafeFallSpeed, s.AirSeconds);
                s.AirSeconds = 0f;
                s.FallSpeed = 0f;
            }
        }

        /// <summary>Plays one remote's landing or footstep when it is among the heard ones (<paramref name="audio"/> set);
        /// the others only let their rhythm run idle.</summary>
        private void StepRemote(Remote r, StepState s, float dt, ClientAudio audio)
        {
            if (audio == null)
            {
                s.Cadence.Tick(dt, false, FootGait.Walk, 0f, FootstepRules.ReferenceWalkSpeed, out _);
                return;
            }

            var pos = r.Go.transform.position;
            if (s.Landing.Tier != LandingTier.None)
            {
                float level = s.Landing.Volume * RemoteLandingScale;
                if (s.Landing.Tier == LandingTier.Soft)
                {
                    PlayRemoteStep(audio, RemoteSurface(pos), s.Cadence.Next(FootGait.Walk), pos, level);
                }
                else
                {
                    float pitch = FootstepRules.Jitter(_stepRng.NextDouble(), FootstepRules.PitchJitter);
                    audio.AtTagged(s.Landing.Clip, pos, pitch, level, AudioCategory.RemotePlayer);
                }

                s.Cadence.Landed();
                return;
            }

            bool walking = s.Grounded && s.Speed > FootstepRules.MinStepSpeed;
            var gait = FootstepRules.GaitFor(s.Speed, FootstepRules.ReferenceWalkSpeed, r.Crouching);
            if (s.Cadence.Tick(dt, walking, gait, s.Speed, FootstepRules.ReferenceWalkSpeed, out var step))
            {
                PlayRemoteStep(audio, RemoteSurface(pos), step, pos, RemoteStepLevel);
            }
        }

        private static void PlayRemoteStep(ClientAudio audio, string surfaceCue, StepShape step, Vector3 pos, float level)
            => audio.AtTagged(FootstepSurface.TakeClip(surfaceCue, step.Take, audio.Has), pos, step.Pitch, level * step.Volume,
                AudioCategory.RemotePlayer);

        /// <summary>What a remote player stands on: shallow water, the cover at the feet, else the cell under them — a parked
        /// ship's first, as for the player's own feet.</summary>
        private string RemoteSurface(Vector3 pos)
        {
            if (Game != null && Game.IsWaterAt(pos + (Vector3.up * 0.1f)))
            {
                return FootstepSurface.WaterCue;
            }

            EnsureStepLookups();
            return FootstepSurface.CueUnderFeet(_stepShipKey, _stepWorldKey, pos.x, pos.y, pos.z);
        }

        /// <summary>A cell with a collider holds the feet up — a parked ship's first, then the world's.</summary>
        private bool RemoteSupported(Vector3 pos)
        {
            EnsureStepLookups();
            string key = FootstepSurface.KeyAtPoint(_stepShipKey, _stepWorldKey, pos.x, pos.y - 0.5f, pos.z);
            return PlayerController.IsCollidingKey(key);
        }

        /// <summary>The chest is in water: the body swims, it does not walk.</summary>
        private bool RemoteSwims(Vector3 pos) => Game != null && Game.IsWaterAt(pos + (Vector3.up * 1.1f));

        private void EnsureStepLookups()
        {
            if (_stepShipKey == null)
            {
                _stepShipKey = StepShipKeyAt;
                _stepWorldKey = StepWorldKeyAt;
            }
        }

        private string StepShipKeyAt(int x, int y, int z)
        {
            if (Game == null || Game.Content == null || Game.LandedShips.Count == 0)
            {
                return null;
            }

            var id = Game.LandedShipBlockAt(x, y, z, out _, out _);
            return id.IsAir ? null : Game.Content.BlockById(id)?.Key;
        }

        private string StepWorldKeyAt(int x, int y, int z)
            => Game?.World == null || Game.Content == null ? null : Game.Content.BlockById(Game.World.GetBlock(x, y, z))?.Key;
    }
}
