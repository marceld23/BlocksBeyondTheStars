// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feel;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How moving on foot FEELS — the small rules on top of the movement chain, each of them pure in
    /// <c>Client.Core/Feel</c>: the walk's own start and stop curve (<see cref="WalkInertia"/>), the jump's edge grace
    /// and remembered tap (<see cref="JumpAssist"/>), the eye that stays behind on an auto-step
    /// (<see cref="StepSmoothing"/>) and the third-person camera's soft vertical follow. Nothing here changes a speed,
    /// a jump height or what the server is told.
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>The third-person camera never hangs further above or below its place than this (metres).</summary>
        private const float ThirdPersonLagLimit = 0.6f;

        /// <summary>How fast the third-person camera follows the body's height (per second, exponential).</summary>
        private const float ThirdPersonFollowRate = 12f;

        private readonly JumpAssist _jumpAssist = new JumpAssist();
        private readonly WalkInertia _walk = new WalkInertia();
        private readonly StepSmoothing _stepSmooth = new StepSmoothing();
        private float _thirdPersonY = float.NaN; // the height the third-person camera has followed to (NaN = snap next frame)
        private int _walkFrame = -1;       // the frame the walk input was last eased in (ReadWalkInput)
        private int _jumpAssistFrame = -1; // the frame the jump assist was last fed in (TickJumpAssist)

        /// <summary>This frame's movement input: the raw keys and stick, eased by the walk's own curve. A capture walk
        /// (screenshots, clips) keeps its scripted values.</summary>
        private void ReadWalkInput(out float h, out float v)
        {
            if (_captureWalk)
            {
                h = _captureH;
                v = _captureV;
                return;
            }

            // A menu, the chat, a seat, a bed, a speeder or the helm had the input and this step did not run: what the keys
            // said before is no step now (on a ledge it would be one too many) — the body stands until it is asked again.
            if (Time.frameCount - _walkFrame > 1)
            {
                _walk.Reset();
            }

            _walkFrame = Time.frameCount;

            // A hold under the feet or the hands answers at once; the air and the water carry the body on.
            bool firm = (Game != null && Game.Spectating)
                || (_controller.enabled && _controller.isGrounded) || _climbing || _pullingUp || _flying;
            if (!firm)
            {
                ProbeFooting(out _, out firm); // a ladder's rungs are a hold too
            }

            _walk.Step(InputMap.MoveXRaw(), InputMap.MoveYRaw(), Time.deltaTime, firm);
            h = _walk.X;
            v = _walk.Y;
        }

        /// <summary>Feeds the jump's edge grace and remembered tap — only the free body (walking, falling) has them.</summary>
        private void TickJumpAssist(bool grounded, bool special)
        {
            // The on-foot step was skipped (a menu, a seat, a pull-up): a tap or an edge from before it is no jump now.
            if (Time.frameCount - _jumpAssistFrame > 1)
            {
                _jumpAssist.Clear();
            }

            _jumpAssistFrame = Time.frameCount;
            bool free = !special && !_flying && !_awaitingFloor && !(Game != null && Game.OnFootInSpace);
            _jumpAssist.Tick(Time.deltaTime, free, grounded, InputMap.JumpDown());
        }

        /// <summary>Just off an edge the feet walked off, a press of Jump still jumps. True when it did.</summary>
        private bool TryCoyoteJump()
        {
            if (!InputMap.JumpDown() || _climbJumpLatch || _verticalVelocity > 0f || !_jumpAssist.CoyoteReady)
            {
                return false;
            }

            BeginJump(cue: true);
            return true;
        }

        /// <summary>The jump impulse (gravity, bio lab and spring boots included) — the one place a jump starts.</summary>
        private void BeginJump(bool cue)
        {
            bool springs = _jumpBoost > 0f; // #2295: the spring boots jump higher and have their own sound
            if (cue && !springs)
            {
                ClientAudio.Instance?.Cue("jump", 0.6f);
                EffortBreath(); // #3767: now and then a short breath of effort, in the figure's voice
            }

            _verticalVelocity = _effJumpSpeed * BioJumpImpulse() * SpringJumpImpulse();
            if (springs)
            {
                SpringJump();
            }

            _jumpAssist.Jumped();
        }

        /// <summary>After the controller moved: an auto-step (a slab, a stair tread) leaves the eye where it was.</summary>
        private void AbsorbStep(float yBefore, bool grounded, bool special)
        {
            float rise = transform.position.y - yBefore;
            if (grounded && !special && !_flying && _verticalVelocity <= 0f && StepSmoothing.IsStep(rise, Time.deltaTime))
            {
                _stepSmooth.Absorb(rise);
            }
        }

        /// <summary>How far the third-person camera sits off its rigid place this frame (metres, vertical): it follows
        /// the body's height softly, so a jump, a landing and a step do not jerk the picture. Rigid with "Camera
        /// motion" off; snaps on a teleport.</summary>
        private float ThirdPersonLag(float dt)
        {
            float y = transform.position.y;
            if (!CameraMotion || float.IsNaN(_thirdPersonY) || Mathf.Abs(_thirdPersonY - y) > 3f)
            {
                _thirdPersonY = y;
                return 0f;
            }

            _thirdPersonY = Mathf.Lerp(_thirdPersonY, y, FlightFeel.Follow(ThirdPersonFollowRate, dt));
            _thirdPersonY = Mathf.Clamp(_thirdPersonY, y - ThirdPersonLagLimit, y + ThirdPersonLagLimit);
            return _thirdPersonY - y;
        }

        private float _avatarHealth = -1f;

        /// <summary>The own figure's body states, once a frame: it swims in water, flinches when the health drops and
        /// sinks down when it is gone (a respawn elsewhere stands it up again — the figure notices it moved).</summary>
        private void UpdateAvatarFeel()
        {
            if (Avatar == null || Game == null)
            {
                return;
            }

            bool swimming = _controller.enabled && IsSubmerged();
            Avatar.SetSwimming(swimming);
            if (swimming)
            {
                Game.SwimmingFrame = Time.frameCount; // #3924: the controls line names the swim keys meanwhile
            }

            float health = Game.Health;
            if (_avatarHealth >= 0f && health < _avatarHealth - 0.5f)
            {
                if (health <= 0f)
                {
                    Avatar.SetDown(true);
                }
                else
                {
                    Avatar.Flinch();
                }
            }

            _avatarHealth = health;
        }

        /// <summary>A teleport, a respawn or a rescue: nothing carries over — no leftover walk, grace or camera lag.</summary>
        private void ResetFeel()
        {
            _walk.Reset();
            _jumpAssist.Clear();
            _stepSmooth.Reset();
            _thirdPersonY = float.NaN;
        }
    }
}
