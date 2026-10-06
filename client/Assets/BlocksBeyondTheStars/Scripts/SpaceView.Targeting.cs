// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The flight target lock (#2277) and its polish (#2283). T (pad LB, touch TARGET) cycles through the targets —
    /// attacking hostiles first, the nearest first — and a hold clears the lock; R (pad R3) locks the nearest enemy and
    /// the next nearest on the next press; the right mouse button locks what is under the crosshair. A frame in the
    /// target's disposition colour and shape sits on it, with name, disposition and distance; off screen, an arrow on an
    /// inner ellipse points the way (mirrored correctly behind the camera). Small red ticks point at further attackers,
    /// and an amber arrow at the map waypoint. The ship locks onto a hostile that starts attacking by itself when nothing
    /// is locked, and after a kill moves on to the next attacker.
    ///
    /// <b>Mining (#2326–#2328).</b> "Target ahead" sits in the context-actions list too (pad L3, touch ⋯), so a rock can be
    /// locked without a mouse. With a mining-capable laser selected (<see cref="MiningCycleRange"/>) a shot at a rock or
    /// the wreck with nothing locked locks it, after it breaks the lock moves to the nearest rock in that laser's reach
    /// (attackers first, as before), the label reads "In range" / "Too far" for a rock, and the cycle key lets the three
    /// nearest rocks in weapon range in — unless a hostile is attacking.
    ///
    /// <b>Client presentation only.</b> The lock never reaches the server: it only chooses which target id the client
    /// writes into the intents it already sends — the weapon prefers the lock inside ±40° with AutoAim on (the server's
    /// arc is ±60°), the tractor pulls a locked drop, the scanner reads a locked object in range when nothing is on its
    /// nose. The server validates all of them exactly as before. The pure rules (order, range, edge placement) live in
    /// <see cref="SpaceTargeting"/>.
    /// </summary>
    public sealed partial class SpaceView
    {
        private enum LockSource { None, Entity, Pilot, Body }

        private const float TargetClearHoldSeconds = 0.6f;
        private const float TargetLostSeconds = 1.5f;
        private const float TargetStatusSeconds = 1.6f;
        private const int MaxThreatTicks = 4;
        private const float PilotLockRadius = 2.5f;

        private static readonly Color TargetHostileCol = new Color(1f, 0.35f, 0.35f);
        private static readonly Color TargetCautionCol = new Color(1f, 0.6f, 0.2f);
        private static readonly Color TargetNeutralCol = new Color(0.9f, 0.95f, 1f);
        private static readonly Color TargetWaypointCol = new Color(1f, 0.85f, 0.3f); // the radar's waypoint amber
        private static readonly Color TargetInRangeCol = new Color(0.45f, 1f, 0.55f);
        private static readonly Color TargetTooFarCol = new Color(1f, 0.78f, 0.45f);

        // The lock.
        private LockSource _lockSource;
        private string _lockId;              // entity id, pilot id or body id ("" = the body the flight is anchored on)
        private string _lockKind;            // last known kind (SpaceTargeting.PilotKind / TraderKind / BodyKind for those)
        private bool _lockHostile;           // last known hostile flag (a raider turns hostile when it stops talking)
        private string _lockName = string.Empty;
        private float _lockRadius = PilotLockRadius;
        private Vector3 _lockLocal;          // last known raw instance-frame position
        private string _lockScanKey;         // the scanner's key for the same object ("e:<id>" / "b:<id>"), or null
        private float _lockSince;            // Time.time of the lock (the brackets snap in)
        private float _lockLostAt = -1f;     // Time.time it left the lock range (-1 = in range)
        private string _lockDestroyedId;     // the server reported this locked entity destroyed

        // The cycle key: a tap cycles (on release), a hold clears.
        private bool _tgtNextTracking;
        private bool _tgtNextConsumed;
        private float _tgtNextHeldFor;

        private readonly List<TargetCandidate> _tgtCandidates = new List<TargetCandidate>(64);
        private readonly List<TargetCandidate> _tgtOrdered = new List<TargetCandidate>(64);
        private readonly List<TargetCandidate> _tgtThreats = new List<TargetCandidate>(MaxThreatTicks);
        private readonly AttackWatch _attackWatch = new AttackWatch();

        // The HUD: its own nested canvas under the flight overlay, so the moving frames re-batch only themselves.
        private RectTransform _tgtLayer;
        private SpaceTargetFrame _lockFrame;
        private SpaceEdgeMarker _lockArrow;
        private readonly SpaceEdgeMarker[] _threatTicks = new SpaceEdgeMarker[MaxThreatTicks];
        private SpaceEdgeMarker _wpArrow;
        private Text _tgtStatus;
        private float _tgtStatusUntil;
        private string _tgtStatusText = string.Empty;

        // #1516: the label texts are rebuilt only when what they show changes.
        private string _lockLabelText = string.Empty;
        private string _lockLabelName;
        private TargetDisposition _lockLabelDisp;
        private int _lockLabelUnits = -1, _lockLabelVert = int.MinValue; // in whole flight units, like the radar
        private object _lockLabelLoc;
        private int _lockSubState = -1;
        private object _lockSubLoc;
        private string _lockSubText = string.Empty;
        private Color _lockSubCol = Color.white;
        private int _lockArrowUnits = -1;
        private int _wpArrowUnits = -1;

        private bool HasTargetLock => _lockSource != LockSource.None;

        /// <summary>The radar's range — what the radar shows can be locked (130, 300 with the radar array).</summary>
        private float TargetLockRange()
            => Game.ShipCombat != null && Game.ShipCombat.RadarRange > 1f ? Game.ShipCombat.RadarRange : 130f;

        /// <summary>The Quantum scanner's system ping is running: everything is lockable, like on the radar.</summary>
        private bool TargetPingActive() => Game.SpaceSystemPingUntil > Time.time;

        private string KmFmt() => Game.Localizer != null ? Game.Localizer.Get("ui.space.km_fmt") : null;

        /// <summary>True when <paramref name="id"/> is the locked space entity.</summary>
        private bool IsLockedEntity(string id) => _lockSource == LockSource.Entity && id != null && id == _lockId;

        // ---------------- Per frame ----------------

        /// <summary>One cruise frame of the lock: read the target keys (only when the helm takes input), keep the lock
        /// honest (destroyed, gone, out of range), and lock an attacker by itself when nothing is locked.</summary>
        private void UpdateTargetLock(float dt, bool inputAllowed)
        {
            if (_ship == null || Game.Space == null)
            {
                return;
            }

            CollectTargetCandidates();
            Vector3 ship = _ship.transform.localPosition;
            string newcomer = _attackWatch.Update(_tgtCandidates, ship.x, ship.y, ship.z);

            if (inputAllowed)
            {
                ReadTargetKeys(dt, ship);
            }
            else
            {
                _tgtNextTracking = false;
            }

            RefreshTargetLock(ship);

            // Auto-lock: only with nothing locked, and only on a hostile that has just STARTED attacking — so clearing
            // the lock in the middle of a fight sticks (#2277). A one-time VEGA tip explains the keys and the arrow.
            if (!HasTargetLock && newcomer != null && TryFindCandidate(newcomer, out var attacker))
            {
                LockCandidate(attacker);
                MaybeSayTargetLockHint();
            }
        }

        private void CollectTargetCandidates()
        {
            _tgtCandidates.Clear();
            var space = Game.Space;
            if (space == null)
            {
                return;
            }

            foreach (var e in space.Entities)
            {
                _tgtCandidates.Add(TargetCandidate.FromEntity(e));
            }

            if (space.Players != null)
            {
                foreach (var p in space.Players)
                {
                    if (!string.IsNullOrEmpty(p.PlayerId))
                    {
                        _tgtCandidates.Add(TargetCandidate.FromPilot(p));
                    }
                }
            }
        }

        private bool TryFindCandidate(string id, out TargetCandidate found)
        {
            for (int i = 0; i < _tgtCandidates.Count; i++)
            {
                if (_tgtCandidates[i].Id == id)
                {
                    found = _tgtCandidates[i];
                    return true;
                }
            }

            found = default;
            return false;
        }

        private void ReadTargetKeys(float dt, Vector3 ship)
        {
            // T / pad LB / touch TARGET: a tap cycles when it is let go, a hold of ~0.6 s clears instead. Firing on the
            // release keeps the two apart; a context-list pick (a press without a hold) cycles at once.
            if (InputMap.Down(InputAction.FlightTargetNext))
            {
                _tgtNextTracking = true;
                _tgtNextConsumed = false;
                _tgtNextHeldFor = 0f;
            }

            if (_tgtNextTracking)
            {
                if (InputMap.Held(InputAction.FlightTargetNext))
                {
                    _tgtNextHeldFor += dt;
                    if (!_tgtNextConsumed && _tgtNextHeldFor >= TargetClearHoldSeconds)
                    {
                        _tgtNextConsumed = true;
                        ClearTargetLock(sound: true);
                    }
                }
                else
                {
                    _tgtNextTracking = false;
                    if (!_tgtNextConsumed)
                    {
                        CycleTarget(ship);
                    }
                }
            }

            if (InputMap.Down(InputAction.FlightTargetHostile))
            {
                LockNearestHostile(ship);
            }

            if (InputMap.Down(InputAction.FlightTargetAhead))
            {
                LockTargetAhead(ship);
            }
        }

        /// <summary>"Next target": the entry after the current lock in the freshly sorted cycle list — with the nearest rocks
        /// in it while a mining laser is selected and nobody is attacking (#2328).</summary>
        private void CycleTarget(Vector3 ship)
        {
            SpaceTargeting.Order(_tgtCandidates, ship.x, ship.y, ship.z, TargetLockRange(), TargetPingActive(), _tgtOrdered, MiningCycleRange());
            string current = _lockSource == LockSource.Entity || _lockSource == LockSource.Pilot ? _lockId : null;
            var next = SpaceTargeting.Next(current, _tgtOrdered);
            if (next == null)
            {
                ShowTargetStatus(Loc("ui.space.target.none", "No target in range"));
                return;
            }

            LockCandidate(next.Value);
        }

        /// <summary>"Nearest enemy": the nearest hostile, pressed again the next nearest.</summary>
        private void LockNearestHostile(Vector3 ship)
        {
            SpaceTargeting.Order(_tgtCandidates, ship.x, ship.y, ship.z, TargetLockRange(), TargetPingActive(), _tgtOrdered);
            string current = _lockSource == LockSource.Entity ? _lockId : null;
            var enemy = SpaceTargeting.NearestHostile(current, _tgtOrdered);
            if (enemy == null)
            {
                ShowTargetStatus(Loc("ui.space.target.no_enemy", "No enemy in range"));
                return;
            }

            LockCandidate(enemy.Value);
        }

        /// <summary>"Target ahead": what lies under the crosshair — the scanner's cone rule, an object before a planet.
        /// Asteroids, salvage drops and planets are reached this way only. Nothing there clears the lock.</summary>
        private void LockTargetAhead(Vector3 ship)
        {
            var space = Game.Space;
            Vector3 fwd = _ship.transform.localRotation * Vector3.forward;
            float cone = Game.AutoAimOn ? 12f : 4f;
            float lockRange = TargetLockRange();
            bool ping = TargetPingActive();
            bool found = false;
            TargetCandidate best = default;
            float bestScore = float.MaxValue;
            foreach (var e in space.Entities)
            {
                Vector3 to = new Vector3(e.X, e.Y, e.Z) - ship;
                if (SpaceTargeting.InLockRange(e.Kind, to.magnitude, lockRange, ping)
                    && SpaceTargeting.AheadScore(to.x, to.y, to.z, fwd.x, fwd.y, fwd.z, TargetRadius(e), cone, out float score)
                    && score < bestScore)
                {
                    bestScore = score;
                    best = TargetCandidate.FromEntity(e);
                    found = true;
                }
            }

            if (space.Players != null)
            {
                foreach (var p in space.Players)
                {
                    if (string.IsNullOrEmpty(p.PlayerId))
                    {
                        continue;
                    }

                    Vector3 to = new Vector3(p.X, p.Y, p.Z) - ship;
                    if (SpaceTargeting.InLockRange(SpaceTargeting.PilotKind, to.magnitude, lockRange, ping)
                        && SpaceTargeting.AheadScore(to.x, to.y, to.z, fwd.x, fwd.y, fwd.z, PilotLockRadius, cone, out float score)
                        && score < bestScore)
                    {
                        bestScore = score;
                        best = TargetCandidate.FromPilot(p);
                        found = true;
                    }
                }
            }

            if (found)
            {
                LockCandidate(best);
                return;
            }

            // Only with no object on the nose do the planets get a chance (like the scanner).
            int bodyIndex = -1;
            for (int i = 0; i < _landables.Count; i++)
            {
                var body = _landables[i];
                Vector3 to = body.Pos - ship;
                if (SpaceTargeting.AheadScore(to.x, to.y, to.z, fwd.x, fwd.y, fwd.z, body.Radius, cone, out float score) && score < bestScore)
                {
                    bestScore = score;
                    bodyIndex = i;
                }
            }

            if (bodyIndex >= 0)
            {
                var body = _landables[bodyIndex];
                LockBody(body.Id ?? string.Empty, body.Name, body.Pos, body.Radius);
                return;
            }

            if (HasTargetLock)
            {
                ClearTargetLock(sound: true);
            }
        }

        // ---------------- Lock state ----------------

        private void LockCandidate(TargetCandidate c)
        {
            var source = c.IsPilot || c.IsTrader ? LockSource.Pilot : LockSource.Entity;
            if (_lockSource == source && _lockId == c.Id)
            {
                return;
            }

            bool had = HasTargetLock;
            _lockSource = source;
            _lockId = c.Id;
            _lockKind = c.Kind;
            _lockHostile = c.Hostile;
            _lockLocal = new Vector3(c.X, c.Y, c.Z);
            _lockScanKey = source == LockSource.Entity ? "e:" + c.Id : null;
            _lockName = c.Id;
            _lockRadius = PilotLockRadius;
            if (source == LockSource.Entity && TryLockedEntity(out var e))
            {
                _lockName = TargetName(e);
                _lockRadius = TargetRadius(e);
            }
            else if (source == LockSource.Pilot && TryLockedPilot(out var p) && !string.IsNullOrEmpty(p.Name))
            {
                _lockName = p.Name;
            }

            BeginLock(had);
        }

        private void LockBody(string id, string name, Vector3 pos, float radius)
        {
            if (_lockSource == LockSource.Body && _lockId == id)
            {
                return;
            }

            bool had = HasTargetLock;
            _lockSource = LockSource.Body;
            _lockId = id;
            _lockKind = SpaceTargeting.BodyKind;
            _lockHostile = false;
            _lockLocal = pos;
            _lockScanKey = "b:" + id;
            _lockName = string.IsNullOrEmpty(name) ? "?" : name;
            _lockRadius = radius;
            BeginLock(had);
        }

        private void BeginLock(bool had)
        {
            _lockSince = Time.time;
            _lockLostAt = -1f;
            _lockDestroyedId = null;
            _lockLabelUnits = -1; // rebuild the label for the new target
            _lockSubState = -1;
            _lockArrowUnits = -1;
            var audio = ClientAudio.Instance;
            if (audio == null)
            {
                return;
            }

            // #2283: a soft click when switching, a friendly two-tone on a fresh lock — a touch sharper on an enemy.
            var disposition = LockDisposition();
            if (had)
            {
                audio.Cue("target_cycle", 0.6f);
            }
            else if (disposition == TargetDisposition.Hostile || disposition == TargetDisposition.Caution)
            {
                audio.Cue("target_lock_hostile", 0.5f);
            }
            else
            {
                audio.Cue("target_lock", 0.45f);
            }
        }

        private void ClearTargetLock(bool sound)
        {
            bool had = HasTargetLock;
            _lockSource = LockSource.None;
            _lockId = null;
            _lockKind = null;
            _lockHostile = false;
            _lockScanKey = null;
            _lockLostAt = -1f;
            _lockDestroyedId = null;
            _lockFrame?.Hide();
            _lockArrow?.Hide();
            if (sound && had)
            {
                ClientAudio.Instance?.Cue("target_lost", 0.5f);
            }
        }

        /// <summary>The flight view closes (landing, docking, the ship's interior, a jump, a new instance): no lock, no
        /// remembered attackers and nothing of the targeting HUD outlive it.</summary>
        private void ResetTargetLock()
        {
            ClearTargetLock(sound: false);
            _attackWatch.Reset();
            _tgtNextTracking = false;
            _tgtCandidates.Clear();
            _tgtStatusUntil = 0f;
            HideTargetHud();
        }

        /// <summary>Keeps the lock honest: follows the snapshot, releases it beyond the range (with the hysteresis and
        /// a 1.5 s "Target lost"), and after a kill moves on to the next attacker — or, when a rock broke under a mining
        /// laser, to the nearest rock in its reach (#2327) — else clears.</summary>
        private void RefreshTargetLock(Vector3 ship)
        {
            switch (_lockSource)
            {
                case LockSource.Entity:
                    if (TryLockedEntity(out var e))
                    {
                        _lockKind = e.Kind;
                        _lockHostile = e.Hostile;
                        _lockLocal = new Vector3(e.X, e.Y, e.Z);
                        var c = TargetCandidate.FromEntity(e);
                        UpdateLockRange(SpaceTargeting.StillValid(c, ship.x, ship.y, ship.z, TargetLockRange(), TargetPingActive()));
                    }
                    else
                    {
                        string goneId = _lockId;
                        string goneKind = _lockKind;
                        bool destroyed = _lockDestroyedId != null && _lockDestroyedId == goneId;
                        // A pulled-in drop or a rescued life pod leaving is the point of locking them — no "lost".
                        bool collected = _lockKind == SpaceTargeting.ResourceDrop || _lockKind == SpaceTargeting.EscapePod;
                        ClearTargetLock(sound: !destroyed && !collected);
                        if (collected)
                        {
                            break;
                        }

                        if (destroyed)
                        {
                            ShowTargetStatus(Loc("ui.space.target.destroyed", "Target destroyed"));
                            // Keep the fight flowing: on to the nearest hostile attacking right now, else nothing.
                            SpaceTargeting.Order(_tgtCandidates, ship.x, ship.y, ship.z, TargetLockRange(), TargetPingActive(), _tgtOrdered);
                            var next = SpaceTargeting.FirstAttacking(_tgtOrdered, goneId);
                            if (next != null)
                            {
                                LockCandidate(next.Value);
                            }
                            else if (SpaceTargeting.IsMiningKind(goneKind))
                            {
                                // #2327: the mining flow — the rock broke, on to the nearest rock the selected mining laser
                                // reaches (nothing with the tractor, the scanner or a pure combat cannon up).
                                float mining = MiningCycleRange();
                                var rock = mining > 0f ? SpaceTargeting.NearestMineable(_tgtCandidates, ship.x, ship.y, ship.z, mining, goneId) : null;
                                if (rock != null)
                                {
                                    LockCandidate(rock.Value);
                                }
                            }
                        }
                        else
                        {
                            ShowTargetStatus(Loc("ui.space.target.lost", "Target lost"));
                        }
                    }

                    break;

                case LockSource.Pilot:
                    if (TryLockedPilot(out var p))
                    {
                        _lockLocal = new Vector3(p.X, p.Y, p.Z);
                        var c = TargetCandidate.FromPilot(p);
                        UpdateLockRange(SpaceTargeting.StillValid(c, ship.x, ship.y, ship.z, TargetLockRange(), TargetPingActive()));
                    }
                    else if (!_remotePlayers.ContainsKey(_lockId))
                    {
                        // A pose can miss a snapshot or two (#955); only once the avatar is gone has the pilot left.
                        ClearTargetLock(sound: true);
                        ShowTargetStatus(Loc("ui.space.target.lost", "Target lost"));
                    }

                    break;

                case LockSource.Body:
                    if (!TryLockedBody(out _))
                    {
                        ClearTargetLock(sound: false);
                    }

                    break;
            }
        }

        private void UpdateLockRange(bool valid)
        {
            if (valid)
            {
                _lockLostAt = -1f;
                return;
            }

            if (_lockLostAt < 0f)
            {
                _lockLostAt = Time.time; // "Target lost" shows on the frame / arrow for a moment, then the lock goes
                ClientAudio.Instance?.Cue("target_lost", 0.4f);
            }
            else if (Time.time - _lockLostAt >= TargetLostSeconds)
            {
                ClearTargetLock(sound: false);
                ShowTargetStatus(Loc("ui.space.target.lost", "Target lost"));
            }
        }

        /// <summary>The server destroyed an entity — if it is the locked one, the lock moves on (see RefreshTargetLock).</summary>
        private void OnLockedEntityDestroyed(SpaceEntityDestroyed m)
        {
            if (m != null && IsLockedEntity(m.Id))
            {
                _lockDestroyedId = m.Id;
            }
        }

        private bool TryLockedEntity(out NetCombatEntity entity)
        {
            entity = null;
            var space = Game.Space;
            if (_lockSource != LockSource.Entity || space == null)
            {
                return false;
            }

            foreach (var e in space.Entities)
            {
                if (e.Id == _lockId)
                {
                    entity = e;
                    return true;
                }
            }

            return false;
        }

        private bool TryLockedPilot(out NetSpacePlayer pilot)
        {
            pilot = null;
            var players = Game.Space?.Players;
            if (_lockSource != LockSource.Pilot || players == null)
            {
                return false;
            }

            foreach (var p in players)
            {
                if (p.PlayerId == _lockId)
                {
                    pilot = p;
                    return true;
                }
            }

            return false;
        }

        private bool TryLockedBody(out int index)
        {
            index = -1;
            if (_lockSource != LockSource.Body)
            {
                return false;
            }

            for (int i = 0; i < _landables.Count; i++)
            {
                if ((_landables[i].Id ?? string.Empty) == _lockId)
                {
                    index = i;
                    return true;
                }
            }

            return false;
        }

        private TargetDisposition LockDisposition()
        {
            bool trader = _lockSource == LockSource.Pilot && SpaceTargeting.IsTraderId(_lockId);
            return SpaceTargeting.Classify(_lockKind, _lockHostile, isPilot: _lockSource == LockSource.Pilot && !trader, isTrader: trader);
        }

        /// <summary>The locked object's smoothed scene position — the interpolated model, never the 5 Hz raw snapshot,
        /// or the frame would jitter.</summary>
        private Vector3 LockedWorldPosition()
        {
            switch (_lockSource)
            {
                case LockSource.Entity:
                    if (_entities.TryGetValue(_lockId, out var go) && go != null && go.activeSelf)
                    {
                        return go.transform.position;
                    }

                    if (_structs.TryGetValue(_lockId, out var vs) && vs != null)
                    {
                        return _root.transform.TransformPoint(vs.Pos);
                    }

                    break;

                case LockSource.Pilot:
                    if (_remotePlayers.TryGetValue(_lockId, out var av) && av != null && av.Root != null)
                    {
                        return av.Root.transform.position;
                    }

                    break;

                case LockSource.Body:
                    if (TryLockedBody(out int bi))
                    {
                        return _root.transform.TransformPoint(_landables[bi].Pos);
                    }

                    break;
            }

            return _root.transform.TransformPoint(_lockLocal);
        }

        // ---------------- Hooks for the ship systems ----------------

        /// <summary>The weapon's lock assist (#2277): with AutoAim on, a locked fire target inside the weapon's range and
        /// ±40° of the nose is preferred over whatever else is better aligned. AutoAim off keeps the lock display-only —
        /// the boresight rule stays honest — and nothing behind the ship is ever offered (the server would refuse it), nor
        /// a target the weapon is not built for (the breaker on a drone).</summary>
        private bool TryLockAssist(string weaponKey, float range, Vector3 shipPos, Vector3 fwd, out NetCombatEntity target)
        {
            target = null;
            if (!Game.AutoAimOn || _lockLostAt >= 0f || !TryLockedEntity(out var locked)
                || !SpaceTargeting.WeaponSuits(WeaponClassFor(weaponKey), locked.Kind))
            {
                return false;
            }

            Vector3 to = new Vector3(locked.X, locked.Y, locked.Z) - shipPos;
            float dist = to.magnitude;
            if (dist > range || dist < 0.001f || Vector3.Dot(to / dist, fwd) < SpaceTargeting.LockAssistMinDot)
            {
                return false;
            }

            target = locked;
            return true;
        }

        /// <summary>The fitted module's <c>weapon_class</c> (0 mining tool, 1 combat, 2 both; 1 when unknown — the server's
        /// default too).</summary>
        private int WeaponClassFor(string weaponKey)
        {
            var stats = Game.Content?.GetShipModule(weaponKey)?.Stats;
            return stats != null && stats.TryGetValue("weapon_class", out var v) ? (int)v : 1;
        }

        /// <summary>The mining context (#2327/#2328): the selected ship system is a laser whose <c>weapon_class</c> can mine
        /// (the breaker, the starter laser) — its range; 0 with the tractor, the scanner or a pure combat cannon selected.
        /// The cycle lets the nearest rocks in, the lock moves on to the next rock after a kill and a locked rock reads
        /// "In range" only in this context, so the ship-systems bar says what the lock is for.</summary>
        private float MiningCycleRange()
        {
            if (_systems.Count == 0 || _selectedSystem >= _systems.Count || _systems[_selectedSystem].Kind != "laser")
            {
                return 0f;
            }

            string key = _systems[_selectedSystem].WeaponKey;
            return SpaceTargeting.CanMine(WeaponClassFor(key)) ? WeaponRangeFor(key) : 0f;
        }

        /// <summary>#2327: a shot at a rock or the wreck with nothing locked locks it — the ship marks what the beam carves.
        /// It never swaps a lock the pilot chose (a lock on a station stays through a shot at a rock), and only fills an
        /// empty lock: clearing it and firing again is the pilot mining again.</summary>
        private void OnShotFired(NetCombatEntity target)
        {
            if (!HasTargetLock && target != null && SpaceTargeting.IsMiningKind(target.Kind))
            {
                LockCandidate(TargetCandidate.FromEntity(target));
            }
        }

        /// <summary>The tractor pulls a locked salvage drop in its reach (#2277).</summary>
        private bool TryLockedDrop(Vector3 shipPos, float reach, out NetCombatEntity drop)
        {
            drop = null;
            if (!TryLockedEntity(out var locked) || !SpaceTargeting.IsCollectableKind(locked.Kind) // a drop or a salvage capsule (#2353)
                || (new Vector3(locked.X, locked.Y, locked.Z) - shipPos).sqrMagnitude > reach * reach)
            {
                return false;
            }

            drop = locked;
            return true;
        }

        /// <summary>The scanner reads a locked object it can scan, in its range, without precise aiming (#2277 — the
        /// server checks only the range); a locked planet is read from anywhere, like a planet on the nose. Only the
        /// fallback: whatever is on the nose comes first (<see cref="BestScanTarget"/>).</summary>
        private bool TryLockedScanTarget(ShipScannerSpec scanner, Vector3 shipPos, out ScanTarget target)
        {
            target = default;
            if (_lockSource == LockSource.Entity && TryLockedEntity(out var e) && IsScannableKind(e.Kind))
            {
                var pos = new Vector3(e.X, e.Y, e.Z);
                float dist = (pos - shipPos).magnitude;
                if (dist > scanner.Range || dist < 0.01f)
                {
                    return false;
                }

                target = new ScanTarget
                {
                    Key = _lockScanKey,
                    Id = e.Id,
                    Kind = e.Kind,
                    Name = TargetName(e),
                    Local = pos,
                    Radius = TargetRadius(e),
                    Hostile = e.Hostile,
                };
                return true;
            }

            if (_lockSource == LockSource.Body && TryLockedBody(out int bi))
            {
                var body = _landables[bi];
                target = new ScanTarget
                {
                    Key = _lockScanKey,
                    Id = _lockId,
                    IsBody = true,
                    Kind = SpaceTargeting.BodyKind,
                    Name = body.Name,
                    Local = body.Pos,
                    Radius = body.Radius,
                };
                return true;
            }

            return false;
        }

        /// <summary>The crosshair turns red while the locked ENEMY is the weapon's firing solution — "shoot now".</summary>
        private bool LockedEnemyInSights()
            => _fireTargetId != null && IsLockedEntity(_fireTargetId) && LockDisposition() == TargetDisposition.Hostile
               && _systems.Count > 0 && _selectedSystem < _systems.Count && _systems[_selectedSystem].Kind == "laser";

        /// <summary>The scanner is drawing its own frame (with the hold ring) on the locked object — one frame, not two.</summary>
        private bool ScannerShowsLock()
            => _lockScanKey != null && _scanFrame != null && _scanFrame.Visible && _scanTargetKey == _lockScanKey;

        /// <summary>The scanner frames something else: the lock frame steps back (thinner, dimmer).</summary>
        private bool ScannerBusyElsewhere() => _scanFrame != null && _scanFrame.Visible && _scanTargetKey != _lockScanKey;

        private void MaybeSayTargetLockHint()
        {
            var settings = Game.Settings;
            if (settings == null || settings.TargetLockHintShown)
            {
                return;
            }

            settings.TargetLockHintShown = true;
            settings.Save();
            VegaPanel.Instance?.SayLocal("vega.hint.target_lock");
        }

        // ---------------- HUD ----------------

        /// <summary>The targeting layer of the flight overlay: a full-screen nested canvas for the lock frame, the scanner
        /// frame, the edge arrows and ticks (#2283).</summary>
        private RectTransform TargetLayer()
        {
            if (_tgtLayer == null && _ui != null)
            {
                var go = new GameObject("TargetLayer", typeof(RectTransform));
                go.transform.SetParent(_ui.transform, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                go.AddComponent<Canvas>(); // nested: only this layer re-batches when the brackets and arrows move
                _tgtLayer = rt;
            }

            return _tgtLayer;
        }

        private void EnsureTargetUi()
        {
            if (_lockFrame != null || TargetLayer() == null)
            {
                return;
            }

            // Back to front: the waypoint arrow and the threat ticks under the lock frame and its arrow.
            _wpArrow = new SpaceEdgeMarker(_tgtLayer, "WaypointArrow", 22f, withLabel: true);
            for (int i = 0; i < _threatTicks.Length; i++)
            {
                _threatTicks[i] = new SpaceEdgeMarker(_tgtLayer, "ThreatTick" + i, 13f, withLabel: false);
            }

            _lockFrame = new SpaceTargetFrame(_tgtLayer, "TargetLock", chargeRing: false);
            _lockArrow = new SpaceEdgeMarker(_tgtLayer, "TargetArrow", 28f, withLabel: true);
            _tgtStatus = SpaceTargetFrame.NewText(_tgtLayer, "TargetStatus", 20, TextAnchor.MiddleCenter, new Vector2(620f, 28f));
            _tgtStatus.fontStyle = FontStyle.Bold;
            _tgtStatus.color = UiKit.TextCol;
            _tgtStatus.rectTransform.anchoredPosition = new Vector2(0f, -128f); // under the "Press E to dock" line
            _tgtStatus.enabled = false;
        }

        private void HideTargetHud()
        {
            _lockFrame?.Hide();
            _lockArrow?.Hide();
            _wpArrow?.Hide();
            for (int i = 0; i < _threatTicks.Length; i++)
            {
                _threatTicks[i]?.Hide();
            }

            if (_tgtStatus != null && _tgtStatus.enabled)
            {
                _tgtStatus.enabled = false;
            }
        }

        /// <summary>A short line under the crosshair: "Target destroyed", "Target lost", "No target in range".</summary>
        private void ShowTargetStatus(string text)
        {
            EnsureTargetUi();
            if (_tgtStatus == null)
            {
                return;
            }

            text ??= string.Empty;
            if (!string.Equals(text, _tgtStatusText, System.StringComparison.Ordinal))
            {
                _tgtStatusText = text;
                _tgtStatus.text = text;
            }

            _tgtStatusUntil = Time.time + TargetStatusSeconds;
        }

        /// <summary>LateUpdate: the lock frame or its edge arrow, the threat ticks and the waypoint arrow — free flight
        /// only (no EVA, no pad chooser, no menu).</summary>
        private void DrawTargetHud()
        {
            EnsureTargetUi();
            if (_lockFrame == null)
            {
                return;
            }

            bool show = _active && _phase == Phase.Cruise && !_eva && !_confirmLand && !Game.MenuOpen && Camera != null && _ship != null && _root != null;
            if (!show)
            {
                HideTargetHud();
                return;
            }

            var canvasRect = (RectTransform)_ui.transform;
            float scale = Mathf.Max(0.01f, _ui.scaleFactor);
            float margin = 28f * scale; // a target this close to the screen edge already gets the arrow
            DrawLockMarker(canvasRect, scale, margin);
            DrawThreatTicks(canvasRect, margin);
            DrawWaypointArrow(canvasRect, margin);

            bool status = Time.time < _tgtStatusUntil;
            if (_tgtStatus.enabled != status)
            {
                _tgtStatus.enabled = status;
            }
        }

        private void DrawLockMarker(RectTransform canvasRect, float scale, float margin)
        {
            if (!HasTargetLock)
            {
                _lockFrame.Hide();
                _lockArrow.Hide();
                return;
            }

            Vector3 world = LockedWorldPosition();
            Vector3 screen = Camera.WorldToScreenPoint(world);
            var place = SpaceTargeting.EdgePlacement(screen.x, screen.y, screen.z, Screen.width, Screen.height, margin: margin);
            var disposition = LockDisposition();
            bool lost = _lockLostAt >= 0f;
            Color col = DispositionColor(disposition);
            if (lost)
            {
                col.a = 0.55f;
            }

            Vector3 delta = _root.transform.InverseTransformPoint(world) - _ship.transform.localPosition;
            float dist = delta.magnitude;

            if (place.OnScreen)
            {
                _lockArrow.Hide();
                if (ScannerShowsLock())
                {
                    _lockFrame.Hide(); // the scanner's frame, with its hold ring, stands for both
                    return;
                }

                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var anchored);
                float r = _lockRadius * _root.transform.lossyScale.x;
                Vector3 edge = Camera.WorldToScreenPoint(world + Camera.transform.right * r);
                float half = Mathf.Clamp(Vector2.Distance(screen, edge) / scale * 1.25f, 26f, 240f);
                float snap = FxKit.ReduceFlashes ? 1f : Mathf.Clamp01((Time.time - _lockSince) / 0.16f);
                half *= Mathf.Lerp(1.8f, 1f, 1f - (1f - snap) * (1f - snap));
                bool thin = ScannerBusyElsewhere();
                Color frameCol = thin ? new Color(col.r, col.g, col.b, col.a * 0.7f) : col;
                _lockFrame.Place(anchored, half, ShapeFor(disposition), frameCol, thin ? 2f : 3f, brackets: true,
                    mark: disposition == TargetDisposition.Hostile);
                UpdateLockLabel(disposition, col, dist, delta.y, lost);
                return;
            }

            _lockFrame.Hide();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, new Vector2(place.X, place.Y), null, out var at);
            // The arrow pulses slowly while the locked enemy attacks — just steady and bright with "Reduce flashes".
            bool attacking = SpaceTargeting.IsAttacking(_lockKind, _lockHostile, dist);
            float pulse = attacking && !FxKit.ReduceFlashes ? 1f + 0.14f * Mathf.Sin(Time.time * 6f) : 1f;
            bool hollow = disposition == TargetDisposition.Neutral || disposition == TargetDisposition.Friendly;
            _lockArrow.Place(at, place.AngleDeg, col, hollow, doubled: disposition == TargetDisposition.Hostile, scale: pulse);
            int units = Mathf.RoundToInt(dist);
            if (units != _lockArrowUnits)
            {
                _lockArrowUnits = units;
                _lockArrow.SetLabel(SpaceDistance.Label(units, KmFmt()));
            }
        }

        /// <summary>Name, disposition and distance (with the ▲/▼ height cue) under the frame; for an enemy "In range" or
        /// "Too far — fly closer" for the selected weapon (for a rock: for the selected mining laser, #2327); "Target lost"
        /// while it is out of range.</summary>
        private void UpdateLockLabel(TargetDisposition disposition, Color col, float dist, float dy, bool lost)
        {
            // Whole flight units (= 10 km steps), like the radar's readouts: the same object reads the same number on
            // both, and the text is rebuilt when that step changes, not every frame of a fly-by.
            var loc = Game.Localizer;
            int units = Mathf.RoundToInt(dist);
            int vertUnits = Mathf.RoundToInt(dy);
            if (units != _lockLabelUnits || vertUnits != _lockLabelVert || disposition != _lockLabelDisp
                || !ReferenceEquals(_lockLabelName, _lockName) || !ReferenceEquals(loc, _lockLabelLoc))
            {
                _lockLabelUnits = units;
                _lockLabelVert = vertUnits;
                _lockLabelDisp = disposition;
                _lockLabelName = _lockName;
                _lockLabelLoc = loc;
                _lockLabelText = _lockName + "\n" + DispositionWord(disposition) + " · " + SpaceRadarMath.DistanceWithHeight(units, vertUnits, KmFmt());
            }

            _lockFrame.SetLabel(_lockLabelText, col);

            int sub = 0;
            if (lost)
            {
                sub = 3;
            }
            else if (disposition == TargetDisposition.Hostile || disposition == TargetDisposition.Caution)
            {
                float range = LockWeaponRange();
                sub = range <= 0f ? 0 : dist <= range ? 1 : 2;
            }
            else if (_lockSource == LockSource.Entity && SpaceTargeting.IsMiningKind(_lockKind))
            {
                // #2327: a rock or the wreck reads "In range" / "Too far" for the selected mining laser — the breaker's 40 is
                // the number a miner needs; nothing with another system up.
                float range = MiningCycleRange();
                sub = range <= 0f ? 0 : dist <= range ? 1 : 2;
            }

            if (sub != _lockSubState || !ReferenceEquals(loc, _lockSubLoc))
            {
                _lockSubState = sub;
                _lockSubLoc = loc;
                switch (sub)
                {
                    case 1:
                        _lockSubText = Loc("ui.space.target.in_range", "In range");
                        _lockSubCol = TargetInRangeCol;
                        break;
                    case 2:
                        _lockSubText = Loc("ui.space.target.too_far", "Too far — fly closer");
                        _lockSubCol = TargetTooFarCol;
                        break;
                    case 3:
                        _lockSubText = Loc("ui.space.target.lost", "Target lost");
                        _lockSubCol = UiKit.TextCol;
                        break;
                    default:
                        _lockSubText = string.Empty;
                        break;
                }
            }

            _lockFrame.SetSubLabel(_lockSubText, _lockSubCol);
        }

        /// <summary>The selected weapon's reach — or, with the tractor or scanner selected, the longest fitted weapon's;
        /// 0 on an unarmed ship (no "in range" line then).</summary>
        private float LockWeaponRange()
        {
            if (_systems.Count == 0)
            {
                return 0f;
            }

            if (_selectedSystem < _systems.Count && _systems[_selectedSystem].Kind == "laser")
            {
                return WeaponRangeFor(_systems[_selectedSystem].WeaponKey);
            }

            float best = 0f;
            foreach (var s in _systems)
            {
                if (s.Kind == "laser")
                {
                    best = Mathf.Max(best, WeaponRangeFor(s.WeaponKey));
                }
            }

            return best;
        }

        /// <summary>#2283: a small red tick on the same ellipse toward each further hostile attacking right now (at most
        /// four, nearest first, no text) — only for those off screen; one in view shows itself and its health bar.</summary>
        private void DrawThreatTicks(RectTransform canvasRect, float margin)
        {
            Vector3 ship = _ship.transform.localPosition;
            SpaceTargeting.NearestAttackers(_tgtCandidates, ship.x, ship.y, ship.z, _lockSource == LockSource.Entity ? _lockId : null,
                MaxThreatTicks, _tgtThreats);
            int shown = 0;
            for (int i = 0; i < _tgtThreats.Count && shown < _threatTicks.Length; i++)
            {
                var t = _tgtThreats[i];
                Vector3 world = _entities.TryGetValue(t.Id, out var go) && go != null
                    ? go.transform.position
                    : _root.transform.TransformPoint(new Vector3(t.X, t.Y, t.Z));
                Vector3 s = Camera.WorldToScreenPoint(world);
                var p = SpaceTargeting.EdgePlacement(s.x, s.y, s.z, Screen.width, Screen.height, margin: margin);
                if (p.OnScreen)
                {
                    continue;
                }

                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, new Vector2(p.X, p.Y), null, out var at);
                _threatTicks[shown++].Place(at, p.AngleDeg, TargetHostileCol, hollow: false, doubled: false);
            }

            for (int i = shown; i < _threatTicks.Length; i++)
            {
                _threatTicks[i].Hide();
            }
        }

        /// <summary>#2283: the map waypoint gets the edge arrow too — amber, with the waypoint glyph and its distance; in
        /// view just the glyph and distance on the point. Not when the waypoint IS the locked target (one arrow, not two).</summary>
        private void DrawWaypointArrow(RectTransform canvasRect, float margin)
        {
            if (!TryResolveSpaceWaypoint(out var wpLocal, out _) || WaypointIsLock())
            {
                _wpArrow.Hide();
                return;
            }

            Vector3 world = _root.transform.TransformPoint(wpLocal);
            Vector3 s = Camera.WorldToScreenPoint(world);
            var p = SpaceTargeting.EdgePlacement(s.x, s.y, s.z, Screen.width, Screen.height, margin: margin);
            if (p.OnScreen)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, s, null, out var point);
                _wpArrow.PlacePoint(point, TargetWaypointCol);
            }
            else
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, new Vector2(p.X, p.Y), null, out var at);
                _wpArrow.Place(at, p.AngleDeg, TargetWaypointCol, hollow: false, doubled: false);
            }

            int units = Mathf.RoundToInt((wpLocal - _ship.transform.localPosition).magnitude);
            if (units != _wpArrowUnits)
            {
                _wpArrowUnits = units;
                _wpArrow.SetLabel("⌖ " + SpaceDistance.Label(units, KmFmt()));
            }
        }

        private bool WaypointIsLock()
        {
            string wp = Game.SpaceWaypointId;
            if (!HasTargetLock || string.IsNullOrEmpty(wp))
            {
                return false;
            }

            if (wp == HomeWaypointId)
            {
                return _lockSource == LockSource.Body && string.IsNullOrEmpty(_lockId);
            }

            return (_lockSource == LockSource.Entity || _lockSource == LockSource.Body) && wp == _lockId;
        }

        private static Color DispositionColor(TargetDisposition d) => d switch
        {
            TargetDisposition.Hostile => TargetHostileCol,
            TargetDisposition.Caution => TargetCautionCol,
            TargetDisposition.Friendly => UiKit.Cyan,
            _ => TargetNeutralCol,
        };

        private static TargetFrameShape ShapeFor(TargetDisposition d) => d switch
        {
            TargetDisposition.Hostile => TargetFrameShape.Diamond,
            TargetDisposition.Caution => TargetFrameShape.DiamondOutline,
            TargetDisposition.Friendly => TargetFrameShape.Ring,
            _ => TargetFrameShape.Corners,
        };

        private string DispositionWord(TargetDisposition d)
        {
            switch (d)
            {
                case TargetDisposition.Hostile:
                    return Loc("ui.space.target.hostile", "Enemy");
                case TargetDisposition.Caution:
                    return Loc("ui.space.target.caution", "Demands cargo");
                case TargetDisposition.Friendly:
                    return Loc("ui.space.target.friendly", "Friendly");
                default:
                    return _lockSource == LockSource.Pilot && SpaceTargeting.IsTraderId(_lockId)
                        ? Loc("ui.space.target.trader", "Trader")
                        : Loc("ui.space.target.neutral", "Neutral");
            }
        }
    }
}
