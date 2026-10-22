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
    /// The energy rope gun (#2317/#2321): aiming and firing (<see cref="FireRope"/>), the server's confirmation
    /// (<see cref="OnRopeConfirmed"/>), the swing and the reel (<see cref="UpdateRope"/>), the rope on a moving frame,
    /// the pose and the drawn rope line.
    /// </summary>
    public sealed partial class PlayerController
    {
        // ---- The energy rope gun (#2317/#2321) ---------------------------------------------------------------------
        // Left-click shoots a rope at a block face; the server confirms the anchor (range, a solid block, line of sight)
        // and the RopeRig (Client.Core) reels the body in while the secondary button is held, holds it when let go, and
        // ends the pull by face: land on a top, pull up over a free ledge, hang anywhere else. Like the glider and the
        // climb it is the client's own on-foot movement; the server keeps the anchor for the others and charges the shot.

        private readonly RopeRig _rope = new RopeRig();
        private readonly Vector3f[] _ropePoints = new Vector3f[RopeLine.PointCount];
        private Vector3? _ropePendingAnchor;   // shot, waiting for the server's outcome (or its refusal)
        private Vector3 _ropePendingNormal;
        private Vector3Int _ropePendingCell;
        private float _ropePendingAt;
        private LineRenderer _ropeLine;
        private FxLook _ropeLook;
        private float _ropeVy;                 // the rig's vertical speed this frame while it owns the body
        private float _ropeKnotTimer;
        private const float RopePendingSeconds = 1.5f;

        /// <summary>The energy rope is out — shot and stuck (#2321).</summary>
        public bool Roped => _rope.Attached;

        private bool HoldingRopeGun() => Game != null && Game.ItemInSlot(Game.SelectedHotbarSlot) == RopeRules.ItemKey;

        private float RopeRange() => HeldTool() is { } tool && tool.Range > 0f ? tool.Range : RopeRules.DefaultLength;

        private static Vector3 ToVector3(Vector3f v) => new Vector3(v.X, v.Y, v.Z);

        /// <summary>A block cell the rope may hold on to: one with a collider (props, plants and tree crowns have none).</summary>
        private bool RopeSolidAt(int x, int y, int z)
        {
            string key = BlockKeyAt(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));
            return key != null && IsCollidingKey(key);
        }

        /// <summary>The block face under the crosshair within the rope's range: the hit point, its outward normal and the cell.</summary>
        private bool RopeAim(out Vector3 point, out Vector3 normal, out Vector3Int cell)
        {
            point = default;
            normal = Vector3.up;
            cell = default;
            if (Camera == null || Game?.World == null)
            {
                return false;
            }

            var o = Camera.transform.position;
            var dir = Camera.transform.forward;
            if (!TerrainHit(o, dir, RopeRange(), out cell, out float dist) || !RopeSolidAt(cell.x, cell.y, cell.z))
            {
                return false;
            }

            point = o + dir * dist;
            normal = FaceNormal(point, dir);
            return true;
        }

        /// <summary>The primary button with the rope gun in hand: aim, send the hit point as the gadget's use, and play the
        /// shot. The pull starts when the server confirms (<see cref="OnRopeConfirmed"/>). A rope already out is let go
        /// first — a new shot replaces it. Nothing in reach: the rope fizzles at the muzzle, nothing is sent.</summary>
        private void FireRope()
        {
            if (Game?.Network == null || Camera == null)
            {
                return;
            }

            if (_ropePendingAnchor != null && Time.time - _ropePendingAt < RopePendingSeconds)
            {
                return; // a shot is in flight: its outcome must not attach a second aim
            }

            if (_rope.Attached)
            {
                ReleaseRope();
            }

            _ropeLook = FxLook.ForItem(Game.Content, RopeRules.ItemKey);
            var muzzle = Muzzle(Camera.transform);
            if (!RopeAim(out var point, out var normal, out var cell))
            {
                RopeFx.Fizzle(_ropeLook, muzzle + Camera.transform.forward * 0.3f);
                ClientAudio.Instance?.Cue("rope_release", 0.35f);
                return;
            }

            _ropePendingAnchor = point;
            _ropePendingNormal = normal;
            _ropePendingCell = cell;
            _ropePendingAt = Time.time;
            Game.Network.SendUseGadget(RopeRules.ItemKey, point);
            TriggerSwing();
            RopeFx.Shot(_ropeLook, muzzle, point, local: true);
            ClientAudio.Instance?.Cue("rope_fire", 0.8f);
        }

        /// <summary>The server accepted the shot (the gadget outcome of the rope, #2319): the rope is out. The pending aim is
        /// the one used — the server echoes the point it stored, wrapped to the canonical copy of the world.</summary>
        public void OnRopeConfirmed(Vector3 target)
        {
            if (_ropePendingAnchor is not { } anchor || Time.time - _ropePendingAt > RopePendingSeconds)
            {
                Game?.Network?.SendReleaseRope(); // a late confirmation: the server holds an anchor nobody here shows
                _ropePendingAnchor = null;
                return;
            }

            _ropePendingAnchor = null;
            var normal = _ropePendingNormal;
            var cell = _ropePendingCell;
            if (_climbing || _pullingUp)
            {
                EndClimb(); // the rope takes over from the wall
            }

            _rope.Attach(new Vector3f(anchor.x, anchor.y, anchor.z), new Vector3f(normal.x, normal.y, normal.z),
                new Vector3i(cell.x, cell.y, cell.z), RopeRange(), RopeSolidAt);
            _airTakeoffY = transform.position.y; // a slack rope catches a fall measured from here
            if (_ropeLine == null)
            {
                _ropeLine = RopeFx.Rent(_ropeLook);
            }

            ClientAudio.Instance?.At("rope_anchor", anchor, 1f, 0.9f);
            UpdateRopePose();
        }

        /// <summary>Lets go of the rope: the rig forgets it, the server is told, the fall speed is capped so the rope never
        /// hands out a fall that hurts, and the line fades with a fizzle at the anchor.</summary>
        private void ReleaseRope()
        {
            bool wasOut = _rope.Attached;
            _rope.Release();
            _ropePendingAnchor = null;
            if (wasOut)
            {
                RopeGone(fizzle: true);
            }
        }

        /// <summary>After the rig let go by itself (arrival, a snap) or <see cref="ReleaseRope"/>: the wire, the cap, the look.</summary>
        private void RopeGone(bool fizzle)
        {
            _verticalVelocity = RopeRig.CappedFall(_verticalVelocity, _effSafeFallSpeed);
            Game?.Network?.SendReleaseRope();
            var anchor = ToVector3(_rope.Anchor);
            if (_ropeLine != null)
            {
                var hand = Camera != null ? Muzzle(Camera.transform) : transform.position + Vector3.up * 1.2f;
                RopeFx.FadeOut(ref _ropeLine, _ropeLook, hand, anchor);
            }

            if (fizzle)
            {
                RopeFx.Fizzle(_ropeLook, anchor);
                ClientAudio.Instance?.Cue("rope_release", 0.7f);
            }

            UpdateRopePose();
        }

        /// <summary>Whether nothing solid lies between the chest and the anchor (the anchor's own block excepted).</summary>
        private bool RopeSightClear()
        {
            var from = transform.position + Vector3.up * RopeRules.ChestHeight;
            var span = ToVector3(_rope.Anchor) - from;
            float full = span.magnitude;
            float len = full - 0.3f;
            if (len <= 0.5f)
            {
                return true;
            }

            var dir = span / full;
            int steps = Mathf.CeilToInt(len / 0.25f);
            int px = int.MinValue, py = int.MinValue, pz = int.MinValue;
            for (int s = 1; s <= steps; s++)
            {
                var p = from + dir * (s * len / steps);
                int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
                if (x == px && y == py && z == pz)
                {
                    continue;
                }

                px = x;
                py = y;
                pz = z;
                if (x == _rope.Cell.X && y == _rope.Cell.Y && z == _rope.Cell.Z)
                {
                    continue;
                }

                if (RopeSolidAt(x, y, z))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The rope's share of a movement frame (#2321). True when the rig owns the body (pulling or hanging):
        /// <paramref name="move"/> and <see cref="_ropeVy"/> then carry its velocity in place of the walk and the gravity.
        /// Crouch lets go, Jump hops and lets go; water, a ladder, flight, space and a hotbar change drop the rope.</summary>
        private bool UpdateRope(bool grounded, bool inWater, bool onLadder, float h, float v, ref Vector3 move)
        {
            if (_ropePendingAnchor != null && Time.time - _ropePendingAt > RopePendingSeconds)
            {
                _ropePendingAnchor = null; // refused, or the outcome never came — either way the server must not keep an anchor we do not show
                Game?.Network?.SendReleaseRope();
            }

            if (!_rope.Attached)
            {
                return false;
            }

            if (inWater || onLadder || _flying || !HoldingRopeGun() || (Game != null && Game.OnFootInSpace))
            {
                ReleaseRope();
                return false;
            }

            if (_rope.Pulling || _rope.Hanging)
            {
                if (InputMap.CrouchHeld())
                {
                    ReleaseRope();
                    return false;
                }

                if (InputMap.JumpDown())
                {
                    ReleaseRope();
                    _verticalVelocity = RopeRules.HopSpeed; // a hop: Jump kept held grabs the wall at its top (#2384), or a low ledge is reached
                    return false;
                }
            }

            bool pull = InputMap.SecondaryHeld();
            float fallDrop = grounded ? 0f : _airTakeoffY - transform.position.y;
            var steer = (transform.right * h + transform.forward * v) * _effMoveSpeed;
            var feet = transform.position;
            var step = _rope.Step(new Vector3f(feet.x, feet.y, feet.z), Time.deltaTime, pull, grounded, fallDrop, RopeSightClear(),
                new Vector3f(steer.x, steer.y, steer.z));
            switch (step.Event)
            {
                case RopeEvent.Snapped:
                    RopeGone(fizzle: true);
                    return false;
                case RopeEvent.Landed:
                    RopeGone(fizzle: false);
                    _verticalVelocity = -1f; // the feet are on the block: a step, not a landing
                    return false;
                case RopeEvent.PullUp:
                    RopeGone(fizzle: false);
                    StartPullUp(ToVector3(_rope.PullUpTarget)); // over the edge with the climb's pull-up
                    return true;
                case RopeEvent.Hang:
                    ClientAudio.Instance?.Cue("rope_anchor", 0.35f); // the winch locks
                    break;
            }

            UpdateRopePose();
            if (!step.Owns)
            {
                return false;
            }

            if (_climbing)
            {
                EndClimb(); // a slack rope reeled in from a wall: the rope takes over from the climb
            }

            move = new Vector3(step.Velocity.X, 0f, step.Velocity.Z);
            _ropeVy = step.Velocity.Y;
            if (_rope.Pulling)
            {
                ClientAudio.Instance?.RopeTick();
            }

            return true;
        }

        /// <summary>Every frame, before the controller's early returns: the rope cannot survive a seat, a vehicle, a train,
        /// the space view or a teleport; the line follows the hand; the crosshair learns whether a shot would hold.</summary>
        private void UpdateRopeFrame()
        {
            if (Game == null)
            {
                return;
            }

            bool cannotHold = Game.SpaceViewActive || !string.IsNullOrEmpty(Game.InSpeeder) || _trainFrame != null
                              || _seatCell != null || !_spawned || _settling;
            if ((_rope.Attached || _ropePendingAnchor != null) && cannotHold)
            {
                ReleaseRope();
            }

            Game.RopeAimValid = !_rope.Attached && !cannotHold && !Game.MenuOpen && HoldingRopeGun() && RopeAim(out _, out _, out _);
            if (!_rope.Attached)
            {
                return;
            }

            if (_ropeLine == null)
            {
                _ropeLine = RopeFx.Rent(_ropeLook);
            }

            var hand = Camera != null ? Muzzle(Camera.transform) : transform.position + Vector3.up * 1.2f;
            RopeFx.Draw(_ropeLine, hand, ToVector3(_rope.Anchor), slack: _rope.State == RopeState.Attached, Time.time, _ropePoints);
            _ropeKnotTimer -= Time.deltaTime;
            if (_ropeKnotTimer <= 0f)
            {
                _ropeKnotTimer = 0.15f;
                RopeFx.Knot(_ropeLook, ToVector3(_rope.Anchor));
            }
        }

        private void UpdateRopePose()
        {
            var anchor = ToVector3(_rope.Anchor);
            _viewmodel?.SetRoped(_rope.Attached, _rope.Pulling, anchor);
            Avatar?.SetRoped(_rope.Attached, anchor);
        }
    }
}
