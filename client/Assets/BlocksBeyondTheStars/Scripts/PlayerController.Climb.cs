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
    /// Wall climbing on planets, moons and asteroids (#2188–#2193): the grip probes, holding on and moving along a wall
    /// (<see cref="UpdateWallClimb"/>), the no-room hint, letting go (<see cref="LetGo"/>, <see cref="EndClimb"/>), the
    /// pull-up over a ledge (<see cref="StartPullUp"/>), the climb pose and the one-time climbing hint.
    /// </summary>
    public sealed partial class PlayerController
    {
        // ---------------------------------------------------------------------------------------------------------
        // #2188–#2193: wall climbing on planets, moons and asteroids
        // ---------------------------------------------------------------------------------------------------------
        //
        // Jump at a wall, push towards it and hold Jump: you hold on (#2384 — holding on is a held button). On the wall
        // you climb up (push in), down (pull away), sideways (along it) or hang still (Jump alone); letting go of Jump
        // slides you down, crouch drops you off; at the top you pull yourself over the edge. A jump that falls short of a
        // low wall pulls you over it on the way up (#2385). The grip tires (ClimbGrip) but is never drawn on the HUD —
        // you feel it: slower, then a tremble and a breath, then a slow slide. Ladders keep their own rules and win;
        // water, flight, space, stations and the ship interior have no climbing. All of it is on-foot movement, which
        // the client owns; other players only learn the pose (MoveIntent.Climbing).

        /// <summary>How hard a climber leans into the wall, m/s — keeps the capsule touching it.</summary>
        private const float WallPress = 1f;

        /// <summary>The little shove off the wall when letting go, m/s.</summary>
        private const float LetGoPush = 2.5f;

        /// <summary>#2386: how long the pull-up must stay refused before the HUD says why, s — no flash while the hands
        /// pass along an edge.</summary>
        private const float ClimbNoRoomDelay = 0.25f;

        /// <summary>#2386: the least time between two "no room to pull up" messages, s.</summary>
        private const float ClimbNoRoomPause = 8f;

        private readonly ClimbGrip _grip = new ClimbGrip();
        private ClimbProbe _climbProbe;
        private ClientWorld _climbProbeWorld;
        private BlocksBeyondTheStars.Shared.Content.GameContent _climbProbeContent;
        private ClimbSurface[] _climbHoldById = System.Array.Empty<ClimbSurface>();
        private bool[] _climbSolidById = System.Array.Empty<bool>();
        private bool _climbing;
        private WallHold _wall;
        private float _climbVy;
        private float _regrabAt;
        private bool _pullingUp;
        private Vector3 _pullFrom, _pullTo;
        private float _pullT;
        private float _climbGearGrip;   // worn gear: share of the grip drain taken away (#2192)
        private bool _climbGearIce;     // worn gear: icy walls hold (#2192)
        private bool _climbSliding;     // sliding down: Jump let go (#2384) or the grip spent
        private float _climbStrain;     // eased 0..1 — the camera tremble and the avatar's shake
        private bool _climbPose;        // climbing a wall or a ladder: the avatar pose + the flag the server mirrors
        private bool _avatarTurned;     // the third-person avatar is turned to face the wall
        private float _climbTapTimer, _climbBreathTimer, _climbSlideTimer, _climbHintTimer;
        private bool _climbNoRoomHinted;    // #2386: this climb already said why the pull-up is refused
        private float _climbNoRoomFor;      // #2386: how long the pull-up has been refused while pushing up
        private float _climbNoRoomNextAt;   // #2386: no new message before this time
        private bool _climbJumpLatch;       // #2384: Jump still held as the grip after a climb — no jump until it is let go

        /// <summary>True while the player hangs on a wall or pulls up over its edge (not on a ladder).</summary>
        public bool ClimbingWall => _climbing || _pullingUp;

        /// <summary>The climbing probe for the current world + content: block data and "has a collider" folded into one
        /// table per block id, rebuilt only when the world or the content changes.</summary>
        private ClimbProbe ClimbProbeNow()
        {
            var world = Game?.World;
            var content = Game?.Content;
            if (world == null || content == null)
            {
                return null;
            }

            if (_climbProbe != null && ReferenceEquals(world, _climbProbeWorld) && ReferenceEquals(content, _climbProbeContent))
            {
                return _climbProbe;
            }

            int max = 0;
            foreach (var def in content.Blocks.Values)
            {
                max = Mathf.Max(max, def.NumericId.Value);
            }

            _climbHoldById = new ClimbSurface[max + 1];
            _climbSolidById = new bool[max + 1];
            foreach (var def in content.Blocks.Values)
            {
                // A hold needs a collider too: props, plants and tree crowns (#2184) have none.
                bool collides = IsCollidingKey(def.Key);
                _climbSolidById[def.NumericId.Value] = collides;
                _climbHoldById[def.NumericId.Value] = collides ? ClimbSurfaces.Of(def) : ClimbSurface.None;
            }

            _climbProbeWorld = world;
            _climbProbeContent = content;
            _climbProbe = new ClimbProbe(ClimbHoldAt, ClimbSolidAt);
            return _climbProbe;
        }

        private ClimbSurface ClimbHoldAt(int x, int y, int z)
        {
            int id = _climbProbeWorld.GetBlock(x, y, z).Value;
            return id < _climbHoldById.Length ? ClimbSurfaces.ForClimber(_climbHoldById[id], _climbGearIce) : ClimbSurface.None;
        }

        private bool ClimbSolidAt(int x, int y, int z)
        {
            int id = _climbProbeWorld.GetBlock(x, y, z).Value;
            return id < _climbSolidById.Length && _climbSolidById[id];
        }

        /// <summary>Where climbing exists at all: on foot under gravity on a planet, moon or asteroid — not above the
        /// atmosphere, not in a station or the ship, not swimming, flying, sneaking, riding or seated.</summary>
        private bool ClimbAllowedHere(bool inWater, bool onLadder)
            => Game != null && !inWater && !onLadder && !_flying && !_crouched
               && !Game.OnFootInSpace && !Game.StationZeroG && !Game.InEva && !Game.Aboard
               && string.IsNullOrEmpty(Game.CurrentStationId) && _trainFrame == null && _seatCell is null;

        /// <summary>
        /// The wall-climbing step (#2188–#2190, #2384–#2386): grabs a wall, climbs it, slides down it, lets go, or starts a
        /// pull-up. Returns true while the player is on a wall this frame — <paramref name="move"/> then carries the
        /// sideways move and the lean into the wall, and <see cref="_climbVy"/> the vertical speed. Also refills the grip on
        /// the ground and on a ladder.
        /// </summary>
        private bool UpdateWallClimb(bool grounded, bool inWater, bool onLadder, float h, float v, ref Vector3 move)
        {
            float dt = Time.deltaTime;
            if (!_climbing && (grounded || onLadder))
            {
                _grip.Refill(dt); // standing — on the ground or a ladder rung — the hands recover
            }

            var probe = ClimbProbeNow();
            if (probe == null || !ClimbAllowedHere(inWater, onLadder))
            {
                if (_climbing)
                {
                    LetGo(push: false);
                }

                _climbStrain = Mathf.MoveTowards(_climbStrain, 0f, dt * 2f);
                return false;
            }

            var pos = transform.position;
            Vector3 wish = (transform.right * h) + (transform.forward * v);
            // #2384: holding on is a held button — Jump (Space, pad (A), touch JUMP). A window that lost focus reads every
            // key as up; a climber on the wall keeps holding then instead of sliding off while alt-tabbed.
            bool holding = InputMap.JumpHeld() || (_climbing && !Application.isFocused);

            if (!_climbing)
            {
                _climbStrain = Mathf.MoveTowards(_climbStrain, 0f, dt * 2f);
                if (grounded)
                {
                    MaybeShowClimbHint(probe, pos, wish);
                    return false;
                }

                // Grabbing is deliberate: in the air, pushing at the wall, not letting go, and not flying the jetpack —
                // holding Jump with a jetpack keeps you flying; an empty tank at a cliff lets you grab it instead.
                if (wish.sqrMagnitude < 0.09f || InputMap.CrouchHeld() || (holding && CanJetpack()))
                {
                    return false;
                }

                // A jump that falls short of a ledge pulls you over it — what makes a two-block wall crossable. #2385: as
                // soon as the edge is in reach, on the way up too; a ledge the jump clears by itself (a one-block step,
                // spring boots, a light world) is landed on instead. Needs no held Jump: running and hopping is enough.
                if (probe.TryPullUpFromJump(pos.x, pos.y, pos.z, wish.x, wish.z, _verticalVelocity, _effGravity,
                        out var ledge, out float lx, out float ly, out float lz))
                {
                    _wall = ledge;
                    StartPullUp(new Vector3(lx, ly, lz));
                    return false;
                }

                // #2384: a grab needs Jump held; #2385: a jump still rising fast carries you to its top before you grab.
                if (!holding || !ClimbProbe.MayGrab(_verticalVelocity) || Time.time < _regrabAt || !_grip.CanGrab
                    || !probe.TryFindWall(pos.x, pos.y, pos.z, wish.x, wish.z, out var hold))
                {
                    return false;
                }

                _climbing = true;
                _wall = hold;
                _climbVy = 0f;
                _verticalVelocity = 0f; // the grab IS the landing — no fall is reported
                _climbTapTimer = 0.3f;
                _climbNoRoomHinted = false;
                _climbNoRoomFor = 0f;
                ClientAudio.Instance?.Cue("climb_grab", 0.5f);
                Weapons?.Dust(pos + (new Vector3(hold.DirX, 0f, hold.DirZ) * 0.45f) + (Vector3.up * 1.2f));
            }

            // Drop off on purpose (crouch), or because there is nothing left to hold.
            if (InputMap.CrouchHeld())
            {
                LetGo(push: true);
                return false;
            }

            var ahead = probe.Ahead(pos.x, pos.y, pos.z, _wall, out var surface);
            if (ahead == WallAhead.Lost)
            {
                LetGo(push: false);
                return false;
            }

            // Steering is relative to the wall, so it reads the same wherever you look: push into it = up, pull away =
            // down, along it = sideways, Jump alone = hang still (#2384: Jump is the grip now, so mining a vein works).
            var into = new Vector3(_wall.DirX, 0f, _wall.DirZ);
            var along = new Vector3(_wall.DirZ, 0f, -_wall.DirX);
            float push = Vector3.Dot(wish, into);
            float side = Vector3.Dot(wish, along);
            bool up = push > 0.3f;
            bool down = !up && push < -0.3f;
            float sideDir = Mathf.Abs(side) > 0.3f ? Mathf.Sign(side) : 0f;

            float vy = 0f;
            float lateral;
            var motion = ClimbMotion.Hang;
            float slide = ClimbGrip.SlideSpeedFor(holding, _grip.Exhausted);
            _climbSliding = slide > 0f;
            if (_climbSliding)
            {
                // #2384: Jump let go, or the grip spent — a slide (far below the safe-landing speed) instead of a fall;
                // steering still works, and pressing Jump again catches you while the grip lasts.
                vy = -slide;
                lateral = sideDir * ClimbGrip.SideSpeed * 0.5f;
                _climbNoRoomFor = 0f;
            }
            else
            {
                float pace = _grip.SpeedFactor; // a tiring grip climbs slower
                bool refused = false;
                if (up)
                {
                    if (ahead == WallAhead.Ledge)
                    {
                        // The hands are at the top edge: pull over it if there is room to stand.
                        if (probe.TryFindLedge(pos.x, pos.y, pos.z, _wall, 0.05f, out float tx, out float ty, out float tz))
                        {
                            StartPullUp(new Vector3(tx, ty, tz));
                            return false;
                        }

                        refused = true; // #2386: no room on top, a blocked column or an edge without hold
                    }
                    else if (!probe.BlockedAbove(pos.x, pos.y, pos.z))
                    {
                        vy = ClimbGrip.UpSpeed * pace;
                        motion = ClimbMotion.Up;
                    }
                }
                else if (down)
                {
                    vy = -ClimbGrip.DownSpeed * pace;
                    motion = ClimbMotion.Down;
                }

                lateral = sideDir * ClimbGrip.SideSpeed * pace;
                if (lateral != 0f && motion == ClimbMotion.Hang)
                {
                    motion = ClimbMotion.Side;
                }

                // #2386: a climber pushing up at an edge that refuses the pull-up is told why, instead of hanging silently.
                _climbNoRoomFor = refused ? _climbNoRoomFor + dt : 0f;
                if (_climbNoRoomFor >= ClimbNoRoomDelay)
                {
                    ClimbNoRoomHint();
                }
            }

            // Stop at the wall's side edge instead of hanging on to thin air.
            if (lateral != 0f)
            {
                var step = along * (lateral * dt);
                if (!probe.WallContinues(pos.x, pos.y, pos.z, _wall, step.x, step.z))
                {
                    lateral = 0f;
                }
            }

            if (!_climbSliding)
            {
                _grip.Drain(dt, motion, _gFactor, surface, _climbGearGrip);
            }

            // Back on the ground while not climbing up: walk from here.
            if (grounded && vy <= 0f)
            {
                EndClimb();
                _climbJumpLatch = holding; // #2384: the held grip is no jump on arrival
                return false;
            }

            _climbVy = vy;
            move = (along * lateral) + (into * WallPress);
            _climbStrain = Mathf.MoveTowards(_climbStrain, _grip.Strain, dt * 2f);
            ClimbFeedback(dt, moving: lateral != 0f || vy != 0f, pos);
            return true;
        }

        /// <summary>#2386: the hands are at the top edge but the pull-up is refused — no room for a standing body on top,
        /// the climber's own column blocked above, or an edge without hold. Says so on the HUD once per climb, and not
        /// more often than every <see cref="ClimbNoRoomPause"/> seconds.</summary>
        private void ClimbNoRoomHint()
        {
            if (_climbNoRoomHinted || Time.time < _climbNoRoomNextAt || Game?.Localizer == null)
            {
                return;
            }

            _climbNoRoomHinted = true;
            _climbNoRoomNextAt = Time.time + ClimbNoRoomPause;
            Game.ShowMessage(Game.Localizer.Get("ui.hud.climb_no_room"));
        }

        /// <summary>Sounds of the climb, all from the shipped set or the three climbing clips: the wall's own step
        /// sound per hand-over-hand, a breath while the grip strains, a scrape while sliding.</summary>
        private void ClimbFeedback(float dt, bool moving, Vector3 pos)
        {
            var audio = ClientAudio.Instance;
            if (audio == null)
            {
                return;
            }

            if (_climbSliding)
            {
                _climbSlideTimer -= dt;
                if (_climbSlideTimer <= 0f)
                {
                    _climbSlideTimer = 0.9f;
                    audio.Cue("climb_slide", 0.4f);
                }
            }
            else if (moving)
            {
                _climbTapTimer -= dt;
                if (_climbTapTimer <= 0f)
                {
                    _climbTapTimer = 0.55f / Mathf.Max(0.5f, _grip.SpeedFactor);
                    var wallCell = pos + (new Vector3(_wall.DirX, 0f, _wall.DirZ) * ClimbProbe.WallReach) + (Vector3.up * ClimbProbe.HandHeight);
                    audio.Cue(StepCueFor(BlockKeyAt(wallCell)), 0.3f);
                }
            }

            if (_grip.Strain > 0f)
            {
                _climbBreathTimer -= dt;
                if (_climbBreathTimer <= 0f)
                {
                    _climbBreathTimer = Mathf.Lerp(2.2f, 1.3f, _grip.Strain);
                    audio.Cue("climb_strain", 0.3f + (0.3f * _grip.Strain));
                }
            }
            else
            {
                _climbBreathTimer = 0.4f;
            }
        }

        /// <summary>Lets go of the wall: a short cooldown before the next grab, and a small shove off it when the
        /// player chose to (crouch).</summary>
        private void LetGo(bool push)
        {
            _climbing = false;
            _climbSliding = false;
            _climbJumpLatch = InputMap.JumpHeld(); // #2384: a grip still held through the fall is no hop on landing
            _regrabAt = Time.time + ClimbGrip.RegrabCooldown;
            _verticalVelocity = Mathf.Min(_climbVy, 0f);
            if (push)
            {
                _knock = new Vector3(-_wall.DirX, 0f, -_wall.DirZ) * LetGoPush;
            }
        }

        /// <summary>Ends any climb at once (feet on the ground, a teleport, a vehicle, a seat) and stands the avatar
        /// straight again.</summary>
        private void EndClimb()
        {
            if (_pullingUp && _controller != null)
            {
                _controller.enabled = true;
            }

            _climbing = false;
            _pullingUp = false;
            _climbSliding = false;
            _climbStrain = 0f;
            _viewmodel?.SetClimbing(false); // #2287: the held item comes back up
            if (_climbPose || _avatarTurned)
            {
                _climbPose = false;
                Avatar?.SetClimbing(false);
                ResetAvatarTurn();
            }
        }

        private void StartPullUp(Vector3 target)
        {
            _climbing = false;
            _climbSliding = false;
            _pullingUp = true;
            _pullFrom = transform.position;
            _pullTo = target;
            _pullT = 0f;
            _verticalVelocity = 0f;
            ClientAudio.Instance?.Cue("climb_grab", 0.45f);
        }

        /// <summary>Drives a running pull-up (#2190): the body rises in its own column first, then swings over the
        /// edge onto the top. The probe has already checked both paths for room, so the controller can stay off for
        /// these few frames. Returns true while the pull-up owns the body.</summary>
        private bool StepPullUp()
        {
            if (!_pullingUp)
            {
                return false;
            }

            _pullT += Time.deltaTime / ClimbGrip.PullUpSeconds;
            float t = Mathf.Clamp01(_pullT);
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.6f));
            float over = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.4f) / 0.6f));
            _controller.enabled = false;
            transform.position = new Vector3(
                Mathf.Lerp(_pullFrom.x, _pullTo.x, over),
                Mathf.Lerp(_pullFrom.y, _pullTo.y + 0.02f, rise),
                Mathf.Lerp(_pullFrom.z, _pullTo.z, over));
            _moving = true;
            UpdateJetpack(false);
            UpdateGlide(false);
            UpdateClimbPose(onLadder: false);
            if (t >= 1f)
            {
                _pullingUp = false;
                _controller.enabled = true;
                _verticalVelocity = -1f;
                _wasGrounded = true; // standing up on the ledge is no landing
                _climbJumpLatch = InputMap.JumpHeld(); // #2384: Jump held as the grip is no hop on top
            }

            return true;
        }

        /// <summary>The avatar's climb pose (local third person; the presence flag tells everyone else), turned to
        /// face the wall while on it.</summary>
        private void UpdateClimbPose(bool onLadder)
        {
            bool onWall = _climbing || _pullingUp;
            _climbPose = onWall || onLadder;
            // #2287: the first-person hands climb on the same signals as the avatar (rhythm, strain, slide, pull-up).
            _viewmodel?.SetClimbing(_climbPose, _climbStrain, _climbSliding, _pullingUp);
            if (Avatar == null)
            {
                return;
            }

            Avatar.SetClimbing(_climbPose, _climbStrain, _climbSliding, _pullingUp);
            // #2435/#2434: the own avatar in the third-person view squats with the crouch and nods with the camera, the
            // same two signals the presence hands everyone else; the root drops with the eased blend so the feet stay down.
            Avatar.SetCrouched(_crouched && !_climbPose);
            Avatar.SetLookPitch(_pitch);
            if (!_avatarBaseYKnown)
            {
                _avatarBaseY = Avatar.transform.localPosition.y;
                _avatarBaseYKnown = true;
            }

            var avatarPos = Avatar.transform.localPosition;
            avatarPos.y = _avatarBaseY - PlayerAvatar.CrouchDrop * Avatar.CrouchBlend;
            Avatar.transform.localPosition = avatarPos;
            if (onWall)
            {
                Avatar.transform.localRotation = Quaternion.Euler(0f, Mathf.DeltaAngle(transform.eulerAngles.y, _wall.FacingYaw), 0f);
                _avatarTurned = true;
            }
            else
            {
                ResetAvatarTurn();
            }
        }

        private float _avatarBaseY;          // #2435: the avatar's resting height under the player root
        private bool _avatarBaseYKnown;

        private void ResetAvatarTurn()
        {
            if (_avatarTurned && Avatar != null)
            {
                Avatar.transform.localRotation = Quaternion.identity;
            }

            _avatarTurned = false;
        }

        /// <summary>The first time a player stands in front of a tall climbable wall without ever having climbed, VEGA
        /// says how (#2194) — once, remembered in the client settings like the other one-shot lessons.</summary>
        private void MaybeShowClimbHint(ClimbProbe probe, Vector3 pos, Vector3 wish)
        {
            // With VEGA's hints off the lesson waits (#2462, B31) — it used to be marked shown and then muted, so it
            // never came once the hints were back on.
            var settings = Game?.Settings;
            if (settings == null || settings.ClimbHintShown || !settings.VegaHints || wish.sqrMagnitude < 0.09f)
            {
                return;
            }

            _climbHintTimer -= Time.deltaTime;
            if (_climbHintTimer > 0f)
            {
                return;
            }

            _climbHintTimer = 0.5f;
            if (probe.TryFindWall(pos.x, pos.y, pos.z, wish.x, wish.z, out var hold)
                && probe.HoldAt(pos.x, pos.y, pos.z, hold.DirX, hold.DirZ, 2.4f) != ClimbSurface.None)
            {
                // #2551: the flag is spent when the line comes up on screen. A lesson that waited unseen in VEGA's queue
                // (the player walked on — aboard, it read as nonsense) is dropped after 30 s and may come at the next wall.
                VegaPanel.Instance?.SayLocal("vega.hint.climb", _climbHintShown ??= MarkClimbHintShown);
            }
        }

        private System.Action _climbHintShown; // cached: the probe may ask every half second while the lesson waits

        private void MarkClimbHintShown()
        {
            var settings = Game?.Settings;
            if (settings != null && !settings.ClimbHintShown)
            {
                settings.ClimbHintShown = true;
                settings.Save();
            }
        }
    }
}
