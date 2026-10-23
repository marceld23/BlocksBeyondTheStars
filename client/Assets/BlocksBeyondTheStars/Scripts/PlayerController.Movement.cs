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
    /// The body's motion: per-world gravity (<see cref="RecomputeGravity"/>), the walking / swimming / ladder / jetpack
    /// frame (<see cref="Move"/>) with its water and ledge probes, gravity-only frames, the re-entry brake below a
    /// planet's line, the hover that waits for the floor after a spawn (<see cref="UpdateFloorWait"/>), creative flight,
    /// crouch, the observer's collision-free flight (<see cref="SpectatorMove"/>), and the capture tooling's body hooks
    /// (<see cref="SetCapturePose"/>, <see cref="SetWalkInput"/>, <see cref="SetLookAngles"/>).
    /// </summary>
    public sealed partial class PlayerController
    {
        // --- Observer mode (issue #487) -------------------------------------------------------------

        /// <summary>Base flight speed while observing (blocks/s). Deliberately not much faster than a walk by
        /// default: every metre flown streams chunks, and a fast observer is a chunk-generation firehose on the
        /// server (see the planet-movement-lag findings). <see cref="SpectatorBoost"/> is the opt-in burst.</summary>
        private const float SpectatorSpeed = 12f;
        private const float SpectatorBoost = 3f;      // hold sprint
        private const float SpectatorMaxSpeed = 60f;  // hard cap, wheel adjustment included
        private float _spectatorSpeedScale = 1f;

        /// <summary>Free flight with no collision and no gravity. The CharacterController is switched off
        /// entirely (the same trick <see cref="SnapTo"/> uses) so walls, terrain and ship hulls are simply not
        /// there — an observer inspecting a sealed base must be able to get inside it.</summary>
        private void SpectatorMove(float h, float v)
        {
            if (_controller.enabled)
            {
                _controller.enabled = false; // noclip: nothing to collide with while observing
            }

            _verticalVelocity = 0f;
            _moving = Mathf.Abs(h) + Mathf.Abs(v) > 0.1f;

            // The wheel tunes cruise speed so a single base and a whole continent are both comfortable. Reusing
            // the hotbar scroll is free here: the hotbar is hidden while observing, so nothing else wants it.
            float wheel = InputMap.HotbarScroll();
            if (Mathf.Abs(wheel) > 0.01f)
            {
                _spectatorSpeedScale = Mathf.Clamp(_spectatorSpeedScale * (wheel > 0f ? 1.25f : 0.8f), 0.25f, 5f);
            }

            float speed = SpectatorSpeed * _spectatorSpeedScale;
            if (InputMap.Held(InputAction.SpeederBoost)) // LeftShift by default — "go faster" in both contexts
            {
                speed *= SpectatorBoost;
            }

            speed = Mathf.Min(speed, SpectatorMaxSpeed);

            // Fly where you look (including pitch) — a strictly horizontal WASD would make descending into a
            // cave needlessly fiddly.
            Vector3 forward = Camera != null ? Camera.transform.forward : transform.forward;
            Vector3 move = (transform.right * h + forward * v) * speed;
            if (InputMap.JumpHeld())
            {
                move += Vector3.up * speed;
            }

            if (InputMap.CrouchHeld())
            {
                move -= Vector3.up * speed;
            }

            transform.position += move * Time.deltaTime;
        }

        private void ApplyGravityOnly()
        {
            // A held world (#908) holds the body too. Without this, opening the pause menu mid-jump or mid-fall
            // kept dropping the player through a world that had otherwise stopped — the single most obvious way
            // the "pause" failed to look like one. The vertical velocity is left untouched, so resuming carries
            // the fall on from exactly where it stopped instead of restarting it.
            if (Game != null && Game.WorldPaused)
            {
                return;
            }

            // #2188: a climber who opens a menu keeps holding the wall (no grip spent) instead of dropping off it.
            if (_climbing || _pullingUp)
            {
                return;
            }

            // #2321: the winch holds behind a menu too — a pull or a hang keeps the height until the panel closes.
            if (_rope.Pulling || _rope.Hanging)
            {
                return;
            }

            bool grounded = _controller.isGrounded;
            UpdateFloorWait(grounded);
            TrackReentry(grounded);
            if (grounded)
            {
                _verticalVelocity = -1f;
                UpdateGlide(false); // #2296: touched down under the menu
            }
            else if (_awaitingFloor)
            {
                _verticalVelocity = 0f; // no floor streamed yet — a menu open at spawn must not drop us either
            }
            else if (Game != null && Game.OnFootInSpace)
            {
                // #2276: zero-g holds behind a menu too. A floating player who opened the inventory used to drop under
                // full gravity (above a planet's line, or out of a station's gravity box) — and below the line that
                // could be a real fall. Ease to a stop: the height is kept while the menu is open.
                _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, 0f, SpaceFloatAccel * Time.deltaTime);
            }
            else
            {
                _verticalVelocity -= _effGravity * Time.deltaTime;
                ApplyReentryBrake();
                if (Gliding && HasItem("glider"))
                {
                    GlideSink(); // #2296: a wing open when the menu came up keeps carrying us (see the menu branch)
                    UpdateGlide(true);
                }
                else
                {
                    UpdateGlide(false); // the glider was taken off in the Suit tab mid-glide: the wing is gone
                }
            }

            _controller.Move(new Vector3(0f, _verticalVelocity, 0f) * Time.deltaTime);
        }

        /// <summary>#2276: arms the re-entry brake on the frame the player sinks back below a planet's atmosphere line, and
        /// clears it at the first landing — on ground, in water, on a ladder, on a wall or in flight.</summary>
        private void TrackReentry(bool landed)
        {
            bool abovePlanet = Game != null && Game.OnFootAbovePlanet;
            if (_wasAbovePlanet && !abovePlanet && Game != null && !Game.OnFootInSpace)
            {
                _reentryBrake = true;
            }

            if (landed)
            {
                _reentryBrake = false;
            }

            _wasAbovePlanet = abovePlanet;
        }

        /// <summary>#2276: while the re-entry brake is armed, caps the fall speed below this world's safe landing speed —
        /// the suit's brake thrusters — so the way down from above the atmosphere looks (and is) survivable. The server
        /// holds its own fall grace until the first landing as well.</summary>
        private void ApplyReentryBrake()
        {
            if (!_reentryBrake)
            {
                return;
            }

            float cap = Mathf.Min(ReentryMaxFallSpeed, _effSafeFallSpeed * 0.9f);
            if (_verticalVelocity < -cap)
            {
                _verticalVelocity = -cap;
            }
        }

        /// <summary>Ends the post-spawn hover (#773) as soon as there is something to stand on — the collider
        /// under our feet, or one streaming in below us — and hard-caps it so a spawn chunk that never arrives
        /// falls back to the old behaviour (drop, then the server's void rescue) instead of hovering forever.</summary>
        private void UpdateFloorWait(bool grounded)
        {
            if (!_awaitingFloor)
            {
                return;
            }

            _awaitFloorTimer += Time.deltaTime;
            if (grounded || _awaitFloorTimer > AwaitFloorMaxSeconds || ColliderBelow())
            {
                _awaitingFloor = false;
            }
        }

        /// <summary>True when a streamed collider (terrain, ship deck, pad) sits within a short drop below us —
        /// after a snap onto a floor cell, only the floor right under the feet counts (#1276).</summary>
        private bool ColliderBelow()
            => Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out var hit, _snapOntoFloor ? SnapFloorMaxDrop : 10f, WorldRayMask)
               && hit.collider != _controller;

        /// <summary>Recompute the live movement constants from this world's gravity multiplier (sent in
        /// <see cref="WorldEnvironment.GravityFactor"/>). Lighter worlds → higher jumps + faster walk; heavier
        /// worlds → still ≥1-block jumps but slower walk + faster falls. Jetpack net thrust and the safe fall
        /// distance are held constant so nothing breaks at the extremes. Cheap: the trig only runs when the
        /// factor actually changes (once per world, not per frame).</summary>
        private void RecomputeGravity()
        {
            float f = Game?.Environment != null ? Game.Environment.GravityFactor : 1f;
            if (f <= 0.05f) f = 1f;          // guard a missing/zero value — fall back to the baseline
            f = Mathf.Clamp(f, 0.2f, 2.5f);  // safety rails beyond the server's authored band
            if (Mathf.Approximately(f, _gFactor)) return;
            _gFactor = f;

            _effGravity = Gravity * f;

            // Jump: keep today's ~1.2-block jump as the FLOOR (so one block is always clearable) and let lighter
            // worlds jump proportionally higher. targetHeight = baseHeight × max(1, 1/f); impulse = √(2·g·h).
            float baseHeight = (JumpSpeed * JumpSpeed) / (2f * Gravity); // = 1.225 blocks at the inspector defaults
            float targetHeight = baseHeight * Mathf.Max(1f, 1f / f);
            _effJumpSpeed = Mathf.Sqrt(2f * _effGravity * targetHeight);

            // Walk: lighter gravity → floatier, faster strides (1/√f), clamped so it never gets silly.
            _effMoveSpeed = Mathf.Clamp(MoveSpeed / Mathf.Sqrt(f), MoveSpeed * 0.55f, MoveSpeed * 1.6f);

            // Jetpack: preserve the baseline NET thrust (accel − gravity) so it still lifts under heavy gravity
            // (a fixed 26 accel can't beat a >26 pull) and doesn't rocket away under light gravity.
            _effJetpackAccel = _effGravity + (JetpackAccel - Gravity);

            // Fall damage: scale the safe-impact speed by √f so the number of blocks you can fall unharmed stays
            // about the same regardless of how fast this world accelerates you downward.
            _effSafeFallSpeed = SafeFallSpeed * Mathf.Sqrt(f);
        }

        /// <summary>#2202: what a jump preparation (or its leaden catch) does to the jump impulse. Jump HEIGHT grows with
        /// the square of the impulse, so a 40 % higher jump is the root of 1.4 on the impulse.</summary>
        private float BioJumpImpulse() => Game != null ? Mathf.Sqrt(Mathf.Max(0f, Game.Bio.JumpFactor)) : 1f;

        private void Move()
        {
            float h = _captureWalk ? _captureH : InputMap.MoveX();
            float v = _captureWalk ? _captureV : InputMap.MoveY();

            // Observer mode (issue #487): free flight straight through geometry. Handled before every normal
            // movement branch because none of them apply — no gravity, no ground, no water, no fall damage,
            // no footsteps to give the invisible admin away.
            if (Game != null && Game.Spectating)
            {
                UpdateGlide(false);
                SpectatorMove(h, v);
                return;
            }

            // #2190: a pull-up over a ledge owns the body until it stands on top (the controller is off meanwhile).
            if (StepPullUp())
            {
                return;
            }

            if (!_controller.enabled)
            {
                // Left observer mode (or a capture snap): the controller owns collision again from here.
                _controller.enabled = true;
                _verticalVelocity = 0f;
            }

            // #2202/#2206: a speed preparation, its sluggish catch and heavy changed gear scale the walk. Applied here
            // and not in RecomputeGravity, which only runs when the world's gravity changes.
            float bioMove = Game != null ? Game.Bio.MoveFactor(_gearWeight) : 1f;
            Vector3 move = (transform.right * h + transform.forward * v) * (_effMoveSpeed * bioMove);

            float prevVy = _verticalVelocity; // captured before the grounded reset (for landing shake)
            bool grounded = _controller.isGrounded;
            bool inWater = IsSubmerged();
            bool onLadder = !inWater && OnLadder();
            UpdateFloorWait(grounded);

            // Crouch/sneak: shrink the capsule + slow the walk while held on the ground.
            UpdateCrouch(grounded, inWater, onLadder || _climbing);
            if (_crouched)
            {
                move *= CrouchSpeedMul;
            }

            UpdateFlight(grounded, inWater, onLadder || _climbing);
            if (_flying)
            {
                move *= FlyHorizontalMul;
            }

            // #2321: the energy rope — pulling or hanging replaces the walk, the gravity and the wall climb below; a ledge
            // arrival starts the pull-up, which then owns the next frames like a climb's does.
            bool roped = UpdateRope(grounded, inWater, onLadder, h, v, ref move);

            // #2188–#2190: wall climbing — after water, ladder and flight are known (they all win), before the
            // vertical branches below, which a climb replaces. A pull-up started here owns the next frames.
            bool climbing = !roped && UpdateWallClimb(grounded, inWater, onLadder, h, v, ref move);
            if (_pullingUp)
            {
                UpdateJetpack(false);
                UpdateGlide(false);
                UpdateClimbPose(onLadder && !grounded);
                return;
            }

            UpdateClimbPose(onLadder && !grounded); // a ladder counts once the feet leave the ground
            _moving = (inWater || grounded || onLadder || climbing) && (Mathf.Abs(h) + Mathf.Abs(v) > 0.1f);
            TrackReentry(grounded || inWater || onLadder || climbing || _flying || roped);

            // #2296: a wing the server refused (no glider worn as it sees it) folds, and stays folded until Jump is let go.
            if (Game != null && Game.GliderRejections != _gliderRejectionsSeen)
            {
                _gliderRejectionsSeen = Game.GliderRejections;
                _glideRefused |= Gliding;
            }

            if (_glideRefused && !InputMap.JumpHeld())
            {
                _glideRefused = false;
            }

            // #2384: Jump held as the grip through a climb's end jumps only after it is let go once — or pressed afresh
            // (a release that happened while a menu was open is never seen here).
            if (_climbJumpLatch && (!InputMap.JumpHeld() || InputMap.JumpDown()))
            {
                _climbJumpLatch = false;
            }

            bool jetpacking = false;
            bool gliding = false;
            if (grounded || inWater || onLadder || climbing || _flying || roped)
            {
                // #2296: where a fall starts — the glider opens only on a real drop below it, or on a fresh press in the air.
                _airTakeoffY = transform.position.y;
                _glidePressArmed = false;
            }

            if (inWater)
            {
                // Buoyant swimming: drift down slowly when idle, hold Jump to rise and surface; water also
                // breaks a fall (the big downward speed eases out instead of slamming the seabed). No jetpack.
                //
                // Climb-out assist (#131): the slow swim-up (4/s, no jump impulse) can't clear a 1-block bank, so
                // you bob against the shore and slide back. At the surface, when you press Jump while pushing
                // forward (A) — or simply swim into a low ≤1-block bank (B) — give a real jump impulse and keep
                // full forward speed so you mount the land instead.
                bool pushing = Mathf.Abs(h) + Mathf.Abs(v) > 0.1f;
                // Open air above the head — #2134: a still liquid (a gas giant's gas, oil) over the head is no "surface" either;
                // it used to hand out the climb-out hop at any depth in the gas.
                string aboveHead = BlockKeyAt(transform.position + Vector3.up * 1.9f);
                bool atSurface = aboveHead != "water" && !LiquidKeys.Contains(aboveHead ?? string.Empty);
                bool climbingOut = pushing && atSurface && (InputMap.JumpHeld() || LedgeAhead(move));
                if (climbingOut)
                {
                    _verticalVelocity = _effJumpSpeed * BioJumpImpulse(); // real hop out of the water (full forward speed kept below)
                }
                else
                {
                    // #2134: the dense gas under a gas giant's gas sea pulls you down — you sink fast, Jump only wins a
                    // slow climb, and every stroke is heavy.
                    bool dense = BlockKeyAt(transform.position + Vector3.up * 1.1f) == "gas_dense";
                    float target = InputMap.JumpHeld() ? (dense ? DenseGasRiseSpeed : SwimUpSpeed) : -(dense ? DenseGasSinkSpeed : SwimSinkSpeed);
                    _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, target, SwimAccel * Time.deltaTime);
                    move *= dense ? DenseGasSpeedMul : SwimSpeedMul;
                }
            }
            else if (onLadder)
            {
                // Climbing (Minecraft-style): no gravity while on a ladder. Hold Jump or push forward to go up,
                // crouch (Ctrl/C) or pull back to go down; otherwise cling with a gentle slide. (#126)
                bool up = InputMap.JumpHeld() || v > 0.1f;
                bool down = InputMap.CrouchHeld() || v < -0.1f;
                _verticalVelocity = up ? ClimbSpeed : (down ? -ClimbSpeed : -1f);
            }
            else if (roped)
            {
                // #2321: on the rope — no gravity; UpdateRope set the move and the vertical speed from the rig. A touch
                // of the ground while the winch moves the body is no landing (like the pull-up's), so a steep reel
                // never reports its own speed as a fall on a light world.
                _verticalVelocity = _ropeVy;
                _wasGrounded = true;
            }
            else if (climbing)
            {
                // #2188: hanging on a wall — no gravity, no jetpack; UpdateWallClimb already steered the move along the
                // wall and picked the vertical speed (up, down, hold, or a slide: Jump let go or the grip spent, #2384).
                _verticalVelocity = _climbVy;
            }
            else if (_flying)
            {
                // Creative flight: no gravity at all. Jump rises, crouch (Ctrl/C) sinks, and letting go holds
                // altitude — the same feel as Minecraft's creative flight. Collision stays ON (this is flight,
                // not the observer mode's noclip), so you can still land on and build against things.
                _verticalVelocity = (InputMap.JumpHeld() ? FlySpeed : 0f) - (InputMap.CrouchHeld() ? FlySpeed : 0f);
            }
            else if (grounded)
            {
                bool springs = _jumpBoost > 0f; // #2295: the spring boots jump higher and have their own sound
                if (InputMap.JumpDown() && !springs)
                {
                    ClientAudio.Instance?.Cue("jump", 0.6f);
                }

                if (InputMap.JumpHeld() && !_climbJumpLatch)
                {
                    _verticalVelocity = _effJumpSpeed * BioJumpImpulse() * SpringJumpImpulse();
                    if (springs)
                    {
                        SpringJump();
                    }
                }
                else
                {
                    _verticalVelocity = -1f;
                }
            }
            else if (Game != null && Game.OnFootInSpace)
            {
                // Above the atmosphere there is no gravity: float, never fall. Jump rises, crouch (Ctrl/C)
                // sinks. With neither held, a station's zero-g drifts to a gentle stop; above a PLANET's line the
                // suit sinks slowly back toward the air (#2276) — walking off a summit that pokes into space used to
                // float the player out at summit height with no idea how to get down. Jump still rises (tower building).
                bool up = InputMap.JumpHeld();
                bool down = InputMap.CrouchHeld();
                float lift = up || down
                    ? (up ? SpaceFloatSpeed : 0f) - (down ? SpaceFloatSpeed : 0f)
                    : (Game.OnFootAbovePlanet ? -SpaceSinkSpeed : 0f);
                _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, lift, SpaceFloatAccel * Time.deltaTime);
            }
            else if (_awaitingFloor)
            {
                _verticalVelocity = 0f; // the spawn floor hasn't streamed in yet — hold altitude, don't drop (#773)
            }
            else
            {
                _verticalVelocity -= _effGravity * Time.deltaTime;
                if (InputMap.JumpDown())
                {
                    _glidePressArmed = true; // #2296: a second press in the air asks for the wing at once
                }

                // Jetpack: hold Jump in the air to thrust upward (needs the item + suit energy). The server
                // drains energy on the reported state and forces it off when empty (SuitEnergy then hits 0).
                if (InputMap.JumpHeld() && CanJetpack())
                {
                    jetpacking = true;
                    _verticalVelocity += _effJetpackAccel * Time.deltaTime;
                    if (_verticalVelocity > JetpackMaxRise)
                    {
                        _verticalVelocity = JetpackMaxRise;
                    }

                    // #900: hanging in the air with no ground under you, the wind has a hold on you. A gale
                    // pushes a hovering player noticeably downwind — enough to matter when lining up a
                    // landing, never enough to take control away.
                    if (Game != null && Game.WindSpeed > 0.05f && Game.ExposedToSky)
                    {
                        var gust = Game.WindVector * (WindDriftPerSecond * Time.deltaTime);
                        move.x += gust.x;
                        move.z += gust.z;
                    }
                }
                else if (InputMap.JumpHeld() && !_glideRefused && GlideArmed() && HasItem("glider"))
                {
                    // #2296: the glider — hold Jump on the way down and the wing opens, where there is air. It shares the
                    // back slot with the jetpack, so the two never meet here.
                    if (GlideAirHere())
                    {
                        gliding = true;
                        Glide(h, v, ref move);
                    }
                    else
                    {
                        GliderNoAirHint();
                    }
                }

                ApplyReentryBrake(); // #2276: back below the line — the suit brakes the fall until the first landing
            }

            UpdateJetpack(jetpacking);
            UpdateGlide(gliding);

            move.y = _verticalVelocity;
            if (_knock.sqrMagnitude > 0.01f)
            {
                move.x += _knock.x; // #1998: a giant's push rides on top of the player's own move
                move.z += _knock.z;
                _knock = Vector3.MoveTowards(_knock, Vector3.zero, 14f * Time.deltaTime);
            }

            // Sneak edge-stop: while crouched and standing on the ground, cancel any horizontal component that
            // would carry the feet off a ledge into open air (checked per axis so you can still slide ALONG the
            // edge). This is what lets the player lean out over a cave/ledge to place a bridging block instead of
            // walking off and falling. Jumping (upward velocity) is exempt so you can still hop off deliberately.
            if (_crouched && grounded && _verticalVelocity <= 0f)
            {
                if (Mathf.Abs(move.x) > 0.01f && !GroundAhead(new Vector3(move.x, 0f, 0f)))
                {
                    move.x = 0f;
                }

                if (Mathf.Abs(move.z) > 0.01f && !GroundAhead(new Vector3(0f, 0f, move.z)))
                {
                    move.z = 0f;
                }

                // …and then the DIAGONAL, which the per-axis pass alone lets through. Walking off an outside
                // corner, each single axis still finds floor along its own edge, so neither test fires — while
                // the combined step lands over the void and the player falls anyway. A player found it and put
                // it plainly: "In Minecraft fällt man nicht runter wenn man sneakt aber hir schon."
                if (Mathf.Abs(move.x) > 0.01f && Mathf.Abs(move.z) > 0.01f
                    && !GroundAhead(new Vector3(move.x, 0f, move.z)))
                {
                    move.x = 0f;
                    move.z = 0f;
                }
            }

            // Cap the auto-step to the headroom above the head before moving, so a 2-block-high opening stays
            // walkable instead of wedging the capsule (see UpdateStepOffset).
            UpdateStepOffset(move);
            if (climbing)
            {
                _controller.stepOffset = 0f; // #2188: no auto-step hop at the wall's lip — the pull-up does that properly
            }

            _controller.Move(move * Time.deltaTime);

            // Last line of defence: if that move left us inside geometry (or below the world), get back out.
            GuardAgainstFallingOut(grounded, inWater, onLadder);

            // Round worlds: latitude (Z) wraps seamlessly like longitude now — the old invisible pole
            // barrier is gone. The transform runs unbounded in both axes; the server canonicalises the
            // authoritative position and chunks reposition to the nearest copy (SceneX/SceneZ).

            // Footsteps while walking on the ground; landing thud after a fall.
            if (grounded && Mathf.Abs(h) + Mathf.Abs(v) > 0.1f)
            {
                _stepTimer -= Time.deltaTime;
                if (_stepTimer <= 0f)
                {
                    _stepTimer = 0.45f;
                    ClientAudio.Instance?.Cue(SurfaceStep(), 0.45f);
                }
            }
            else
            {
                _stepTimer = 0f;
            }

            if (grounded && !_wasGrounded && !inWater)
            {
                ClientAudio.Instance?.Cue("land", 0.6f);
                Weapons?.Dust(transform.position);
                // Impact kick. #2746: the pad does not buzz on every jump's landing — only a hard one rumbles (below).
                FxCamera.AddTrauma(Mathf.Clamp01(-prevVy / 12f) * 0.55f, rumble: false);
                bool realFall = !FeetInWater() && !_flying && _train.Frame == null;
                if (realFall && -prevVy > _effSafeFallSpeed * 0.6f)
                {
                    PadRumble.Landing(hard: -prevVy > _effSafeFallSpeed);
                }

                if (_jumpBoost > 0f && prevVy < -3f)
                {
                    Avatar?.SpringCompress(); // #2295: the coils take the landing (a step down is no landing)
                }

                // A hard landing hurts: report the impact speed so the server (which owns health) applies
                // fall damage. Small drops/jumps stay below the safe threshold and do nothing. Deep water breaks
                // the fall via the swim branch — but landing in even ONE block of water should cushion it too,
                // like Minecraft (Severin playtest: shallow water still hurt because the chest wasn't submerged).
                // Flying down onto the ground is a landing, not a fall — the descent is powered, not a drop.
                // A glide (#2296) needs no exception: it lands at its 2.5 m/s sink, far under any safe speed. The spring
                // boots (#2295) need none either — their higher jump lands at most ~11 m/s (on the heaviest, 1.6 g worlds),
                // under the server's safe 14 m/s, and the server takes their fall protection off any real fall.
                if (-prevVy > _effSafeFallSpeed && realFall) // #2113: the frame's motion is never a fall
                {
                    Game?.Network?.SendFallDamage(-prevVy);
                }
            }

            _wasGrounded = grounded;
        }

        /// <summary>True when the player's upper body sits in water — the cue to switch to swimming (sampled at chest
        /// height, so wading through shallow water still walks; only deep water swims). A submerged kelp stalk, ladder
        /// or form counts as water (#1902), so a diver swims through a kelp forest instead of dropping to the seabed.</summary>
        private bool IsSubmerged() => Game != null && Game.IsWaterAt(transform.position + Vector3.up * 1.1f);

        /// <summary>True when the player's feet touch water on landing — sampled low so even a single block of
        /// water counts. Used to cushion the fall (no splash damage) the way any depth of water does in Minecraft;
        /// <see cref="IsSubmerged"/> alone (chest height) missed shallow pools (Severin playtest).</summary>
        private bool FeetInWater() =>
            Game != null
            && (Game.IsWaterAt(transform.position + Vector3.up * 0.1f) || Game.IsWaterAt(transform.position + Vector3.up * 0.6f));

        /// <summary>True when a low (≤1 block) solid bank sits directly ahead of the swimmer — a wall at knee
        /// height with clear space just above it — the cue to mantle out of the water onto land (#131).</summary>
        private bool LedgeAhead(Vector3 moveDir)
        {
            Vector3 flat = new Vector3(moveDir.x, 0f, moveDir.z);
            if (flat.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            Vector3 ahead = transform.position + flat.normalized * 0.7f;
            return IsBlockingAt(ahead + Vector3.up * 0.5f)        // bank face at ~knee height
                && !IsBlockingAt(ahead + Vector3.up * 1.6f);      // and open space to stand on top
        }

        /// <summary>True when a solid (standable/blocking) block sits at this world position. Air, water, lava and the
        /// still liquids (#2134: a gas giant's gas is no bank to mount) don't block; everything else does — a coarse check
        /// used to spot a low bank when climbing out of water.</summary>
        private bool IsBlockingAt(Vector3 world)
        {
            if (Game?.World == null || Game.Content == null)
            {
                return false;
            }

            var id = Game.World.GetBlock(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y), Mathf.FloorToInt(world.z));
            if (id.IsAir)
            {
                return false;
            }

            string key = Game.Content.BlockById(id)?.Key;
            return key != "water" && key != "lava" && !LiquidKeys.Contains(key ?? string.Empty);
        }

        /// <summary>True when the player overlaps a ladder block (sampled at shin + chest height), the cue to
        /// switch to climbing instead of falling/walking. (#126) A ladder furnishing a parked ship counts too (#2743).</summary>
        private bool OnLadder() =>
            ShipOrWorldKeyAt(transform.position + Vector3.up * 0.3f) == "ladder"
            || ShipOrWorldKeyAt(transform.position + Vector3.up * 1.1f) == "ladder";

        /// <summary>
        /// Drives creative flight (#836): a double-tap of jump toggles it, and it ends when the world stops
        /// allowing it, when the player touches down, or in water / on a ladder, which have their own vertical
        /// rules and would fight it.
        /// <para>
        /// Landing turns it off deliberately: without that, walking around a base means constantly holding
        /// crouch to stay down. Toggling back on is one double-tap away.
        /// </para>
        /// </summary>
        private void UpdateFlight(bool grounded, bool inWater, bool onLadder)
        {
            bool allowed = Game != null && Game.CanFly;
            if (!allowed || inWater || onLadder)
            {
                _flying = false;
                return;
            }

            if (InputMap.JumpDown())
            {
                // A tap within the window of the previous one = the toggle. Taking OFF from the ground needs the
                // first tap's jump to have happened, which is why this reads the tap times rather than grounded.
                if (_lastJumpTapTime > 0f && Time.time - _lastJumpTapTime <= FlyDoubleTapWindow)
                {
                    _flying = !_flying;
                    _lastJumpTapTime = -1f; // consumed: a third tap starts a fresh pair, not another toggle
                    _verticalVelocity = 0f;
                    ClientAudio.Instance?.Cue("jump", 0.5f);
                }
                else
                {
                    _lastJumpTapTime = Time.time;
                }
            }

            if (_flying && grounded && _verticalVelocity <= 0f && !InputMap.JumpHeld())
            {
                _flying = false; // touched down under our own weight — walk from here
            }
        }

        /// <summary>True while the player is in creative flight — the HUD shows the mode, and fall damage
        /// never applies.</summary>
        public bool Flying => _flying;

        /// <summary>Drives the crouch/sneak state each frame: sets it from the input while grounded, keeps it on if a
        /// ceiling would block standing back up, snaps the collider to the crouched capsule, and eases the camera.</summary>
        private void UpdateCrouch(bool grounded, bool inWater, bool onLadder)
        {
            bool want = InputMap.CrouchHeld() && grounded && !inWater && !onLadder;

            // Don't stand up into a block: if crouched under a low ceiling, stay crouched until there's headroom.
            if (_crouched && !want && IsSolidKey(BlockKeyAt(transform.position + Vector3.up * (StandHeight - 0.1f))))
            {
                want = true;
            }

            _crouched = want;
            _crouchT = Mathf.MoveTowards(_crouchT, _crouched ? 1f : 0f, Time.deltaTime * 8f);

            // The collider snaps to the crouched capsule (physics can't lerp cleanly). Lowering the centre with the
            // height keeps the feet planted, so shrinking/growing never pops the player up or down.
            float targetH = _crouched ? CrouchHeight : StandHeight;
            if (!Mathf.Approximately(_controller.height, targetH))
            {
                _controller.height = targetH;
                _controller.center = new Vector3(0f, targetH * 0.5f, 0f);
            }
        }

        /// <summary>Automation/capture hook (<see cref="ScreenshotDirector"/>): pose the on-foot player at a world
        /// position + facing so an outdoor planet shot can step out of the landed ship onto open terrain. SnapTo
        /// bypasses collision for the move; with no mouse input during a capture run the look sticks, and gravity
        /// then settles the player onto the ground.</summary>
        public void SetCapturePose(Vector3 pos, float yaw, float pitch)
        {
            SnapTo(pos);
            transform.eulerAngles = new Vector3(0f, yaw, 0f);
            _pitch = Mathf.Clamp(pitch, -89f, 89f);
            if (Camera != null)
            {
                Camera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }
        }

        /// <summary>Cinematic-capture hooks (<see cref="ClipDirector"/>): drive the on-foot player's walk + look
        /// without keyboard input so a recorded clip shows the character actually moving. <see cref="_captureWalk"/>
        /// gates this — when false the normal <c>InputMap.MoveX/MoveY</c> path is untouched in regular play.</summary>
        private bool _captureWalk;
        private float _captureH, _captureV;
        public void SetWalkInput(float horizontal, float vertical)
        {
            _captureH = horizontal;
            _captureV = vertical;
            _captureWalk = true;
        }

        public void ClearWalkInput() => _captureWalk = false;

        /// <summary>Set the body yaw + look pitch directly (same as the rotation half of <see cref="SetCapturePose"/>),
        /// for a cinematic look-around while walking.</summary>
        public void SetLookAngles(float yaw, float pitch)
        {
            transform.eulerAngles = new Vector3(0f, yaw, 0f);
            _pitch = Mathf.Clamp(pitch, -89f, 89f);
            if (Camera != null)
            {
                Camera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }
        }
    }
}
