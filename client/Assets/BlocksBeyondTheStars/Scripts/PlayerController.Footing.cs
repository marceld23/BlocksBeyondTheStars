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
    /// Footing and the block lookups under the body: riding a lift (<see cref="RideBy"/>), server snaps
    /// (<see cref="SnapTo"/>), the never-fall-out-of-the-world guards (<see cref="GuardAgainstFallingOut"/>), the step
    /// offset, the block and liquid keys behind collision (<see cref="IsCollidingKey"/>, shared with the remote players)
    /// and the footstep surface, which reads a parked ship first and then the world (<see cref="ShipOrWorldKeyAt"/>).
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>#2266: a lift platform under the player moved by <paramref name="dy"/> this frame — the player rides along
        /// (the capsule is moved with the platform, never pushed into it; a rising floor also ends any fall).</summary>
        public void RideBy(float dy)
        {
            if (_controller == null || !_controller.enabled || Mathf.Abs(dy) < 1e-5f)
            {
                return;
            }

            _controller.enabled = false;
            transform.position += new Vector3(0f, dy, 0f);
            _controller.enabled = true;
            if (dy > 0f && _verticalVelocity < 0f)
            {
                _verticalVelocity = 0f;
            }
        }

        /// <summary>Teleports the player (CharacterController toggled so the move isn't blocked) and zeroes fall speed.</summary>
        private void SnapTo(Vector3 pos)
        {
            EndClimb(); // #2188: a teleport, respawn or rescue never leaves us hanging on a wall that is no longer there
            UpdateGlide(false); // #2296: nor gliding on
            _grip.Reset();
            _controller.enabled = false;
            transform.position = pos;
            _controller.enabled = true;
            _verticalVelocity = 0f;
        }

        // --- Never fall out of the world -----------------------------------------------------------------
        // Reported by a player: "Beim Bauen bin ich von einem Block gefallen, ich wollte mich mit einem Block
        // retten, doch beim Platzieren war ich an der Stelle des Blocks und bin durch ihn durchgefallen.
        // Daraufhin bin ich durch die Blöcke durchgefallen ohne Ende."
        //
        // The server deliberately ALLOWS placing into your own feet cell so you can pillar-jump, on the
        // assumption that "the client collider just lifts you onto the new block". That holds at rest or on the
        // way up — but not in a fast fall: the capsule ends up inside the new solid cell with a large downward
        // velocity, and Unity's depenetration resolves it downward, straight through the blocks below.

        /// <summary>The last position at which the player stood safely on the ground — the anchor the
        /// out-of-world guard restores to.</summary>
        private Vector3 _lastSafeGround;
        private bool _hasSafeGround;

        /// <summary>Consecutive frames spent stuck inside solid geometry (see <see cref="GuardAgainstFallingOut"/>).</summary>
        private int _embeddedFrames;

        /// <summary>Frames of being inside a block before the guard yanks the player back. A couple of frames of
        /// overlap is normal while a chunk re-meshes; a persistent overlap is a desync we must not ride out.</summary>
        private const int EmbeddedFramesBeforeRescue = 12;

        /// <summary>Below this the player has left the world for good — nothing is generated down here (it mirrors
        /// the server's build-band floor), so there is nothing left to land on.</summary>
        private const float OutOfWorldY = -2100f;

        /// <summary>
        /// A block just became solid where the player is standing. Lift them onto its top instead of leaving
        /// Unity's depenetration to guess a direction — guessing "down" is what made a player fall through the
        /// world after saving themselves mid-fall with a block. Called for every block change, so it also covers
        /// another player or a server path filling the cell.
        /// </summary>
        public void LiftOutOfBlockAt(int cellX, int cellY, int cellZ)
        {
            if (_controller == null || !_controller.enabled || (Game != null && Game.Spectating))
            {
                return;
            }

            // Does the cell overlap the capsule? Feet at transform.position, head StandHeight above; the capsule
            // is `radius` wide, so a block in a NEIGHBOURING column can overlap it too (#1460: the guard used to
            // consider the centre column only, leaving the eye inside a block a friend placed beside you).
            var pos = transform.position;
            float radius = _controller.radius;
            float dx = pos.x - (cellX + 0.5f);
            float dz = pos.z - (cellZ + 0.5f);
            if (Mathf.Abs(dx) >= 0.5f + radius || Mathf.Abs(dz) >= 0.5f + radius)
            {
                return;
            }

            float feet = pos.y;
            float head = pos.y + (_crouched ? CrouchHeight : StandHeight);
            if (cellY + 1f <= feet + 0.001f || cellY >= head - 0.001f)
            {
                return; // entirely below the feet or above the head — no overlap
            }

            bool ownColumn = Mathf.FloorToInt(pos.x) == cellX && Mathf.FloorToInt(pos.z) == cellZ;
            if (ownColumn)
            {
                SnapTo(new Vector3(pos.x, cellY + 1f, pos.z));
                return;
            }

            // A side overlap: nudge out of the cell along the axis with the most overlap instead of lifting.
            float pushX = 0f, pushZ = 0f;
            if (Mathf.Abs(dx) >= Mathf.Abs(dz))
            {
                pushX = Mathf.Sign(dx) * (0.5f + radius - Mathf.Abs(dx) + 0.02f);
            }
            else
            {
                pushZ = Mathf.Sign(dz) * (0.5f + radius - Mathf.Abs(dz) + 0.02f);
            }

            SnapTo(new Vector3(pos.x + pushX, pos.y, pos.z + pushZ));
        }

        /// <summary>
        /// Last line of defence against ending up outside the world: remembers the last spot the player stood on
        /// safely and restores it if they spend several frames inside solid geometry or drop below the world
        /// floor. Independent of any particular cause — whatever desync puts the player inside a block, this
        /// gets them out instead of letting them fall forever.
        /// </summary>
        private void GuardAgainstFallingOut(bool grounded, bool inWater, bool onLadder)
        {
            // A climbing player legitimately stands INSIDE the ladder cell, and a swimmer inside water — neither
            // is "stuck in geometry". Observers pass through everything by design.
            if (onLadder || inWater || (Game != null && Game.Spectating))
            {
                _embeddedFrames = 0;
                return;
            }

            // Fell out of the bottom of the world — nothing below will ever stop us.
            if (transform.position.y < OutOfWorldY)
            {
                if (_hasSafeGround)
                {
                    SnapTo(_lastSafeGround);
                }

                _embeddedFrames = 0;
                return;
            }

            // Solid at chest height OR at eye height means the capsule is inside geometry, not merely brushing
            // it (the eye check is what stops a block placed at head height from x-raying the world, #1460).
            // Only blocks that really collide count — walking through grass or past a wall torch is not being stuck.
            float eyeHeight = _crouched ? CrouchEye.y : FirstPersonEye.y;
            bool embedded = EnclosedAt(transform.position + Vector3.up * 0.9f)
                || EnclosedAt(transform.position + Vector3.up * eyeHeight);
            if (embedded)
            {
                if (++_embeddedFrames >= EmbeddedFramesBeforeRescue && _hasSafeGround)
                {
                    // #1488: say so. A player reported a "falling" jolt while standing on a solid station floor
                    // and building at its edge — the prime suspect is this rescue firing while the chunk's
                    // collider is still being re-baked off-thread. The line names the spot and whether a bake
                    // was pending, so the next report settles it.
                    var at = transform.position;
                    bool bakePending = Game != null && Game.ColliderBakePendingAt(at);
                    Debug.Log($"[FallGuard] rescued to {_lastSafeGround} after {_embeddedFrames} embedded frames at {at} " +
                              $"(world {Game?.LoadingPlanetType ?? "?"}, grounded {grounded}, collider bake pending {bakePending})");
                    SnapTo(_lastSafeGround);
                    _embeddedFrames = 0;
                }

                return;
            }

            _embeddedFrames = 0;

            // Standing on real ground with clear space around us: this is a spot worth coming back to.
            if (grounded && _verticalVelocity <= 0f)
            {
                _lastSafeGround = transform.position;
                _hasSafeGround = true;
            }
        }

        /// <summary>
        /// Keeps the auto-step from punching the player's head into a low ceiling. The 1.8 m capsule leaves only
        /// ~0.14 m of clearance in a 2-block-high gap, and a 0.6 m step sweep eats far more than that — so the
        /// player wedged and could not walk through a 2-high opening they had built (reported as "Das 2 Blöcke
        /// Problem"; the earlier skin-width fix only removed part of the cause). Step height is therefore capped
        /// to the headroom actually available, which still climbs slabs and stair treads in the open.
        /// </summary>
        private void UpdateStepOffset(Vector3 move)
        {
            float capsuleTop = _crouched ? CrouchHeight : StandHeight;

            // Walk upward from the head in step-sized samples and stop at the first solid cell. #1790: the capsule is
            // radius + skin wide, so the probe covers its whole footprint — the centre, the four sides and the four
            // diagonals — not just the column under transform.position. Walking a 2-high corridor a little off the
            // column centre, or reaching a lintel whose ceiling block sits in the NEXT column, the single centre sample
            // still read "air", the full 0.6 m sweep stayed armed and the player wedged exactly as before ("Ich kann
            // immernoch nicht durch 2 Blöcke hohe Gänge"). Same neighbouring-column reach the wedge guard in
            // LiftOutOfBlockAt already uses (#1460).
            float headroom = DefaultStepOffset;
            float reach = _controller.radius + _controller.skinWidth;
            float diag = reach * 0.7071f;
            var feet = transform.position;

            // #1837: the lintel of a 2-high doorway sits in the NEXT column, and the step-up engages the moment the
            // forward sweep touches it — with the capsule centre still radius + skin (plus a frame of approach) short
            // of that column, where every footprint sample above reads air. So the 0.6 m sweep stayed armed, the
            // raised capsule (1.8 + 0.6) met the lintel at 2.0 and the walk wedged; a crouch or a jump got through
            // ("Ich kann immernoch nur unter 2 Blöcken durch wenn ich springe oder mich ducke"). Sample the column
            // ahead along the move as well — centre and both shoulders — so the cap is set before the sweep can reach.
            bool moving = move.x * move.x + move.z * move.z > 1e-6f;
            float ax = 0f, az = 0f, lx = 0f, lz = 0f;
            if (moving)
            {
                var dir = new Vector3(move.x, 0f, move.z).normalized;
                ax = dir.x * (reach + AheadProbe);
                az = dir.z * (reach + AheadProbe);
                lx = -dir.z * reach;
                lz = dir.x * reach;
            }

            for (float probe = 0.1f; probe <= DefaultStepOffset + 0.05f; probe += 0.1f)
            {
                float up = capsuleTop + probe;
                if (CeilingAt(feet, up, 0f, 0f)
                    || CeilingAt(feet, up, reach, 0f) || CeilingAt(feet, up, -reach, 0f)
                    || CeilingAt(feet, up, 0f, reach) || CeilingAt(feet, up, 0f, -reach)
                    || CeilingAt(feet, up, diag, diag) || CeilingAt(feet, up, -diag, diag)
                    || CeilingAt(feet, up, diag, -diag) || CeilingAt(feet, up, -diag, -diag)
                    || (moving && (CeilingAt(feet, up, ax, az)
                        || CeilingAt(feet, up, ax + lx, az + lz) || CeilingAt(feet, up, ax - lx, az - lz))))
                {
                    headroom = Mathf.Max(0f, probe - 0.1f);
                    break;
                }
            }

            _controller.stepOffset = Mathf.Min(DefaultStepOffset, headroom);
        }

        /// <summary>A colliding block <paramref name="up"/> above the feet, sampled <paramref name="dx"/>/<paramref name="dz"/>
        /// off the capsule axis (the step-offset probe's footprint samples, #1790).</summary>
        private bool CeilingAt(Vector3 feet, float up, float dx, float dz)
            => IsCollidingKey(BlockKeyAt(new Vector3(feet.x + dx, feet.y + up, feet.z + dz)));

        /// <summary>The step height used in the open — matches the value WorldRig sets up so a slab (0.5) and each
        /// stair tread are walked up without jumping.</summary>
        private const float DefaultStepOffset = 0.6f;

        /// <summary>How far beyond the capsule edge the step-offset probe looks along the move (#1837): past the
        /// contact distance (radius + skin) and a frame of approach, into the column the next sweep would enter.</summary>
        private const float AheadProbe = 0.55f;

        /// <summary>#2438: whether the cell at <paramref name="world"/> can really enclose the player — a colliding block key
        /// AND a form that fills the cell. The fall guard used to ask the key alone, so a carpet, a plate or a slab laid into
        /// the feet cell read as "embedded in stone" and teleported the player back to the last safe spot ("with plates and
        /// carpets I always get teleported to my last position").</summary>
        private bool EnclosedAt(Vector3 world)
        {
            if (Game?.World == null || !IsCollidingKey(BlockKeyAt(world)))
            {
                return false;
            }

            int x = Mathf.FloorToInt(world.x), y = Mathf.FloorToInt(world.y), z = Mathf.FloorToInt(world.z);
            return BlocksBeyondTheStars.Shared.World.BlockShapeFacts.CanEnclosePlayer(
                BlocksBeyondTheStars.Shared.World.ShapeCode.ShapeOf(Game.World.GetShape(x, y, z)));
        }

        /// <summary>The content key of the block at a world position (null if the world/content isn't ready).</summary>
        private string BlockKeyAt(Vector3 world)
        {
            if (Game?.World == null || Game.Content == null)
            {
                return null;
            }

            var def = Game.Content.BlockById(Game.World.GetBlock(
                Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y), Mathf.FloorToInt(world.z)));
            return def?.Key;
        }

        /// <summary>The keys of the still liquids by block data (#2106: oil) — filled from the content once it is known
        /// (<see cref="RefreshLiquidKeys"/>), so the static footing checks can treat them like water without a key list.</summary>
        private static readonly System.Collections.Generic.HashSet<string> LiquidKeys = new System.Collections.Generic.HashSet<string>();
        private static BlocksBeyondTheStars.Shared.Content.GameContent _liquidKeysFor;

        private void RefreshLiquidKeys()
        {
            var content = Game?.Content;
            if (content == null || ReferenceEquals(content, _liquidKeysFor))
            {
                return;
            }

            LiquidKeys.Clear();
            foreach (var def in content.Blocks.Values)
            {
                if (def.Liquid)
                {
                    LiquidKeys.Add(def.Key);
                }
            }

            _liquidKeysFor = content;
        }

        /// <summary>A block key that gives solid footing to stand on (anything placed, but not air or a fluid you'd
        /// sink through — water, lava, or a still liquid such as oil). Nor a tree crown (#2184): leaves, needles and
        /// fronds are walked through like plants, so you drop through a crown, stand up inside one, and the sneak
        /// edge-stop does not mistake a crown below a ledge for ground.</summary>
        private static bool IsSolidKey(string key)
            => !string.IsNullOrEmpty(key) && key != "air" && key != "water" && key != "lava" && !LiquidKeys.Contains(key)
               && !TreeFoliage.IsKey(key);

        /// <summary>
        /// A block that actually has a COLLIDER — i.e. one the capsule can be blocked by or stuck inside.
        /// Cross-billboard props (small flora and the torch) are meshed without a collider on purpose, so the
        /// player walks straight through them; treating them as solid would make the out-of-world guard think a
        /// player standing in a grass tuft (or next to a wall torch) was embedded in geometry, and would make a
        /// torch overhead cancel the auto-step. Tree crowns (#2184) are excluded already by <see cref="IsSolidKey"/>.
        /// The prop list (flora, torch/lantern lit or unlit, ladder, fire, energy gate) is the Shared one the server's
        /// movement mirror reads too (#2533), so the two can no longer drift.
        /// </summary>
        internal static bool IsCollidingKey(string key)
            => IsSolidKey(key) && !BlocksBeyondTheStars.Shared.Definitions.WalkThroughBlocks.IsProp(key);

        /// <summary>Whether there is solid footing just past the player's edge in a horizontal direction — used by
        /// the sneak edge-stop. Samples a little ahead of the capsule and allows a single step-down (so you can
        /// still sneak down a slab/stair), but reports "no ground" over a real drop into open air.</summary>
        private bool GroundAhead(Vector3 horizontalDir)
        {
            if (horizontalDir.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            // #1309: probe just past the CENTRE, not past the capsule's rim. At radius + 0.15 the sample sat
            // 0.5 blocks ahead, so the stop fired as soon as the centre came within half a block of the edge —
            // i.e. in the middle of the last block, which made building an outer wall face from its top
            // impossible without scaffolding.
            // #1486: probe BEHIND the centre. With the sample 0.08 ahead the centre — and the eye above it —
            // stopped just short of the edge, so the outer face of the block underfoot was never in view and
            // "Blöcke an Außenwände setzen geht immer noch nicht". Now the stop fires only once the centre
            // would hang SneakEdgeOverhang past the edge: the eye clears the rim and can aim down onto the
            // outer face, while the trailing part of the footprint still stands on the block — the way
            // sneaking overhangs in every other voxel builder. The probe also covers THIS frame's step, so a
            // long frame cannot carry the centre past the edge unseen.
            float reach = horizontalDir.magnitude * Time.deltaTime - SneakEdgeOverhang;
            Vector3 ahead = transform.position + horizontalDir.normalized * reach;

            // #2743: a monorail wagon's floor holds wherever its box is (wagon to wagon across the gap); its door openings
            // and the train's open ends fall through to the probe below, which finds air there.
            if (_trainFrame != null && _trainView != null && _trainView.WagonAt(ahead, out _, RailRules.WagonGap) != null)
            {
                return true;
            }

            // #2743: a parked ship's deck or roof is ground too (it is its own object, not the world's grid).
            return IsSolidKey(ShipOrWorldKeyAt(ahead - Vector3.up * 0.5f))
                || IsSolidKey(ShipOrWorldKeyAt(ahead - Vector3.up * 1.2f));
        }

        /// <summary>Picks the footstep clip from the block under the player's feet (key heuristic) — a parked ship's deck
        /// first, then the world (#2743: the ship is its own object, so the world alone read air there and every step on a
        /// ship sounded like rock). A monorail wagon's floor is steel.</summary>
        private string SurfaceStep()
        {
            if (_trainFrame != null)
            {
                return FootstepSurface.MetalCue;
            }

            if (Game?.World == null || Game.Content == null)
            {
                return FootstepSurface.DefaultCue;
            }

            var p = transform.position;
            return FootstepSurface.CueFor(ShipOrWorldKeyAt(new Vector3(p.x, p.y - 0.5f, p.z)));
        }

        /// <summary>The footstep clip for a block key (key heuristic) — the floor underfoot, or the wall a climber's
        /// hands and boots touch (#2188).</summary>
        private static string StepCueFor(string key) => FootstepSurface.CueFor(key);

        private System.Func<int, int, int, string> _shipKeyAtFn, _worldKeyAtFn;

        /// <summary>#2743: the content key of the block at a world position — a parked ship's cell (ship-as-object, see
        /// <see cref="GameBootstrap.LandedShipBlockAt"/>; also the ship interior in space and a docked friend's ship) first,
        /// then the world's. For what the player stands on or holds on to (footsteps, the sneak edge-stop, ladders); the
        /// fluid probes keep <see cref="BlockKeyAt"/>, the world alone.</summary>
        private string ShipOrWorldKeyAt(Vector3 world)
        {
            if (_shipKeyAtFn == null)
            {
                _shipKeyAtFn = ShipKeyAt;
                _worldKeyAtFn = WorldKeyAt;
            }

            return FootstepSurface.KeyAtPoint(_shipKeyAtFn, _worldKeyAtFn, world.x, world.y, world.z);
        }

        private string ShipKeyAt(int x, int y, int z)
        {
            if (Game == null || Game.Content == null || Game.LandedShips.Count == 0)
            {
                return null;
            }

            var id = Game.LandedShipBlockAt(x, y, z, out _, out _);
            return id.IsAir ? null : Game.Content.BlockById(id)?.Key;
        }

        private string WorldKeyAt(int x, int y, int z)
            => Game?.World == null || Game.Content == null ? null : Game.Content.BlockById(Game.World.GetBlock(x, y, z))?.Key;
    }
}
