// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// One awake guardian giant on screen (#3676) — presentation only: the server owns the fight, its moves, the damage and
    /// the block body. The view exists while a fight is live (<see cref="GuardianGiantFight"/>): it builds the block-limb
    /// model (<see cref="GuardianGiantModel"/>), places it on the server's planet-enemy position (the same buffered
    /// interpolation as every planet machine) and heading, plays the server's move on the server's clock
    /// (<see cref="GuardianGiantMotion"/>, blended over <see cref="GuardianGiantFight.BlendSeconds"/>), and draws what the move
    /// needs — the warnings, the impacts, the rock, the beam, the search light, the sounds (GuardianGiantView.Fx.cs) and the
    /// lights (GuardianGiantView.Lights.cs). It rises out of the lying block body and ends in it: a rise starts exactly on
    /// the cells the server just removed, and after a collapse or a lie-down the model holds the lying pose until the client
    /// world shows the stamped blocks, then goes. Owned by <see cref="WorldEntities"/> (WorldEntities.GuardianGiants.cs).
    /// </summary>
    public sealed partial class GuardianGiantView
    {
        /// <summary>After a fight ended lying, the model waits at most this long for the stamped blocks (seconds).</summary>
        private const float StampWaitSeconds = 1.5f;

        /// <summary>The stamped blocks are in the client world: the model stays this much longer, until they are meshed.</summary>
        private const float StampMeshSeconds = 0.12f;

        private readonly GameBootstrap _game;
        private readonly Transform _parent;
        private readonly MotionInterpolator _interp = new MotionInterpolator(initialInterval: 0.15, minDelay: 0.12, maxDelay: 0.8);
        private readonly float _blinkSeed;
        private GuardianGiantRig _rig;
        private NetCombatEntity _lastPushed;
        private Vector3 _stand;          // the standing root, world coordinates (canonical)
        private bool _hasStand;
        private Vector3 _vel;
        private float _yaw;
        private float _yawTarget;
        private bool _hasYaw;
        private float _speed;
        private float _walk;
        private Vector3Int _anchor;      // the lying pose it rises from / falls into (the status's anchor)
        private int _facing;
        private bool _hasAnchor;
        private bool _dead;              // the collapse landed: lights out for good
        private string _registeredId;
        private float _prevHull = -1f;
        private float _holdSince = -1f;
        private float _stampedAt = -1f;

        public GuardianGiantView(GameBootstrap game, Transform parent, string siteId)
        {
            _game = game;
            _parent = parent;
            SiteId = siteId;
            _blinkSeed = (siteId?.GetHashCode() ?? 0) % 997 * 0.013f;
        }

        /// <summary>The fight's record key.</summary>
        public string SiteId { get; }

        /// <summary>Whether a fight wants a model now: it is live and not past the end of a collapse or a lie-down (the view
        /// that showed it is finishing or gone — a late status must not bring the giant back).</summary>
        public static bool Wanted(GuardianGiantTrack t, float now)
            => GuardianGiantFight.Live(t, now) && !(EndsLying(t.Move) && t.Elapsed(now) >= GuardianGiantRules.Spec(t.Move).Duration);

        /// <summary>One frame: <paramref name="e"/> is the giant's planet-enemy entry this frame (null while it is not in the
        /// list — rising before it appears, after the defeat). False = the view is done; the caller disposes it.</summary>
        public bool Tick(GuardianGiantTrack t, NetCombatEntity e, float now, float dt)
        {
            CaptureAnchor(t);
            if (!EnsureModel(t, now))
            {
                return GuardianGiantFight.Live(t, now);
            }

            bool live = GuardianGiantFight.Live(t, now);
            bool ended = !live || (EndsLying(t.Move) && t.Elapsed(now) >= GuardianGiantRules.Spec(t.Move).Duration);
            bool endsLying = EndsLying(t.Move) || (t.Move == GuardianGiantMove.None && EndsLying(t.PrevMove));
            if (ended && (!endsLying || !_hasAnchor))
            {
                return false; // the fight ended standing (out of range, a dropped link): the model just goes
            }

            Follow(e, dt);
            Register(e, t);
            DetectHit(e);
            float moving = Mathf.Clamp01(_speed / (GuardianGiantRules.WalkSpeed * 0.8f));
            float prevWalk = _walk;
            _walk += dt * GuardianGiantMotion.WalkCycleRate(_speed);
            var frame = ended ? GuardianGiantFrame.Lying : Evaluate(t, now, moving);
            ApplyPose(frame, t, now);
            if (!ended)
            {
                Events(t, now);
                Footfalls(t, prevWalk, moving);
            }
            else
            {
                HideWarnings();
            }

            UpdateLights(frame, t, now);
            if (_walk > 1000f)
            {
                _walk -= 1000f; // whole strides: the feet keep their phase
            }

            return !ended || !StampedOrTimedOut(now);
        }

        /// <summary>Removes everything the view made (the model, the rock, the warnings, the lights) and its hit boxes from
        /// the aim lookup.</summary>
        public void Dispose()
        {
            Unregister();
            DisposeFx();
            DisposeLights();
            if (_rig?.Root != null)
            {
                Object.Destroy(_rig.Root);
            }

            if (_rig?.GlowBody != null)
            {
                Object.Destroy(_rig.GlowBody);
            }

            if (_rig?.GlowVisor != null)
            {
                Object.Destroy(_rig.GlowVisor);
            }

            _rig = null;
        }

        /// <summary>Whether a move ends in the lying block body (the server stamps it at the move's end).</summary>
        private static bool EndsLying(GuardianGiantMove move) => move is GuardianGiantMove.Collapse or GuardianGiantMove.LieDown;

        /// <summary>The pose now: the current move, blended in over the previous one.</summary>
        private GuardianGiantFrame Evaluate(GuardianGiantTrack t, float now, float moving)
        {
            var cur = GuardianGiantMotion.Evaluate(t.Move, t.Elapsed(now), _walk, moving);
            float blend = t.Blend(now);
            if (blend >= 1f)
            {
                return cur;
            }

            var prev = GuardianGiantMotion.Evaluate(t.PrevMove, t.PrevElapsed(now), _walk, moving);
            return GuardianGiantFrame.Lerp(prev, cur, blend);
        }

        /// <summary>The lying pose's anchor: from every status of a move that starts or ends lying (and from the first status
        /// heard, so the pose is always defined).</summary>
        private void CaptureAnchor(GuardianGiantTrack t)
        {
            var s = t.Status;
            if (s == null || (_hasAnchor && t.Move != GuardianGiantMove.Rise && !EndsLying(t.Move)))
            {
                return;
            }

            _anchor = new Vector3Int(s.AnchorX, s.AnchorY, s.AnchorZ);
            _facing = s.Facing;
            _hasAnchor = true;
        }

        /// <summary>The model exists and wears the right body: the status's seed (the rusted plates) and the lights on until the
        /// collapse lands (also for a player who arrives after it landed). False until the block atlas is ready.</summary>
        private bool EnsureModel(GuardianGiantTrack t, float now)
        {
            if (!_dead && ((t.Move == GuardianGiantMove.Collapse && t.Elapsed(now) >= GuardianGiantRules.Spec(GuardianGiantMove.Collapse).Impact)
                           || (t.Move == GuardianGiantMove.None && t.PrevMove == GuardianGiantMove.Collapse)))
            {
                _dead = true;
            }

            if (_rig == null)
            {
                _rig = GuardianGiantModel.CreateRig(_parent, "GuardianGiant", colliders: true);
                var red = new Color(1f, 0.2f, 0.14f);
                GuardianGiantModel.AddGlows(_rig, new Material(FxKit.Unlit(red)), new Material(FxKit.Unlit(red)));
                SetColliders(false);
            }

            int seed = t.Status?.Seed ?? 0;
            bool lit = !_dead;
            if (_rig.Seed == seed && _rig.Lit == lit)
            {
                return true;
            }

            return GuardianGiantModel.MeshLimbs(_rig, _game.Content, _game.Atlas, _game.ChunkMaterial, _game.ChunkMaterialTransparent, seed, lit)
                   || _rig.Seed != int.MinValue;
        }

        /// <summary>Follows the server: the planet-enemy entry through the buffered interpolation on the server clock (like
        /// every planet machine), its heading eased; without an entry the last known spot — or, before the first, the ground
        /// under the lying anchor.</summary>
        private void Follow(NetCombatEntity e, float dt)
        {
            int circ = _game.Circumference;
            if (e != null)
            {
                if (!ReferenceEquals(e, _lastPushed))
                {
                    _lastPushed = e;
                    _interp.Push(_game.PlanetEnemiesLocalTime, new Vector3f(e.X, e.Y, e.Z), new Vector3f(e.VelX, e.VelY, e.VelZ),
                        _game.PlanetEnemiesServerTime > 0, 0f, 0, circ);
                }

                if (_interp.Sample(_game.WorldTime, circ, out var drawn))
                {
                    _stand = new Vector3(drawn.Position.X, drawn.Position.Y, drawn.Position.Z);
                    _vel = new Vector3(drawn.Velocity.X, 0f, drawn.Velocity.Z);
                    _hasStand = true;
                }

                _yawTarget = e.Yaw;
                if (!_hasYaw)
                {
                    _yaw = _yawTarget;
                    _hasYaw = true;
                }
            }
            else
            {
                _vel = Vector3.zero;
            }

            if (!_hasStand && _hasAnchor)
            {
                var guess = GuardianGiantPose.StandingRootGuess(_anchor.x, _anchor.y, _anchor.z);
                _stand = new Vector3(guess.X, guess.Y, guess.Z);
            }

            if (!_hasYaw && _hasAnchor)
            {
                _yaw = _yawTarget = GuardianGiantPose.LyingYaw(_facing); // it stands up about its heels, facing that way
            }

            _yaw = Mathf.MoveTowardsAngle(_yaw, _yawTarget, 120f * dt);
            _speed += (_vel.magnitude - _speed) * (1f - Mathf.Exp(-dt / 0.12f));
        }

        /// <summary>The hit boxes go into the aim lookup (<see cref="CreatureView.RegisterGiant"/>) while the giant is a target —
        /// in the planet-enemy list and not collapsing — and out again (switched off) otherwise.</summary>
        private void Register(NetCombatEntity e, GuardianGiantTrack t)
        {
            bool target = e != null && t.Move != GuardianGiantMove.Collapse && t.Move != GuardianGiantMove.None;
            if (target && _registeredId != e.Id)
            {
                Unregister();
                SetColliders(true);
                CreatureView.RegisterGiant(e.Id, _rig.Root, GiantRideRules.GuardianGiantPlan);
                _registeredId = e.Id;
            }
            else if (!target && _registeredId != null)
            {
                Unregister();
            }
        }

        private void Unregister()
        {
            if (_registeredId != null)
            {
                CreatureView.UnregisterGiant(_registeredId);
                EnemyHealthBars.Forget(_registeredId);
                _registeredId = null;
            }

            SetColliders(false);
        }

        private void SetColliders(bool on)
        {
            if (_rig == null)
            {
                return;
            }

            foreach (var c in _rig.Colliders)
            {
                if (c != null && c.enabled != on)
                {
                    c.enabled = on;
                }
            }
        }

        /// <summary>The ending lying pose is held until the stamped blocks are in the client world (and had a moment to be
        /// meshed), or <see cref="StampWaitSeconds"/> passed.</summary>
        private bool StampedOrTimedOut(float now)
        {
            if (_holdSince < 0f)
            {
                _holdSince = now;
            }

            if (_stampedAt < 0f && BodyStamped())
            {
                _stampedAt = now;
            }

            return (_stampedAt >= 0f && now - _stampedAt >= StampMeshSeconds) || now - _holdSince >= StampWaitSeconds;
        }

        /// <summary>Whether the block body lies at the anchor: one torso plate's cell is solid in the client world.</summary>
        private bool BodyStamped()
        {
            if (_game.World == null || !_hasAnchor)
            {
                return false;
            }

            var (lx, ly, lz) = GuardianGiantBody.ToLying(-2, 8, -1);
            var (fx, fz) = GuardianGiantBody.Face(lx, lz, _facing);
            return !_game.World.GetBlock(_anchor.x + fx, _anchor.y + ly, _anchor.z + fz).IsAir;
        }
    }
}
