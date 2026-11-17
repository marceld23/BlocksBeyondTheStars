// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Client.Feel;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the newer ship weapons look like at the helm (#3757, #3758) — the server resolves every shot; this is the
    /// picture and the choice of target:
    /// <list type="bullet">
    /// <item>a <b>homing</b> weapon (the missile) fires at the locked target, wherever the nose points;</item>
    /// <item>a combat weapon may fire on a <b>station</b>: an intact one's shield bubble takes the shot, a derelict's
    /// hull breaks (the server takes the blocks out);</item>
    /// <item>a <b>scatter</b> weapon draws a bolt to every hostile in its cone;</item>
    /// <item>a hostile with a <b>shield</b> shows its bubble where a shot lands, and the bubble bursts when it is gone;</item>
    /// <item>the <b>point-defence turret</b> fires by itself, a silenced hostile sparks, and another pilot's roll shows.</item>
    /// </list>
    /// </summary>
    public sealed partial class SpaceView
    {
        private static readonly Color IonCol = new Color(0.5f, 0.71f, 1f);
        private static readonly Color IonCore = new Color(0.92f, 0.96f, 1f);

        private readonly Dictionary<string, FxShield> _shields = new Dictionary<string, FxShield>(); // entity / station id → its bubble
        private readonly Dictionary<string, float> _shieldSeen = new Dictionary<string, float>();    // the last shield share seen
        private readonly List<NetCombatEntity> _scatterTargets = new List<NetCombatEntity>();
        private float _turretCd;
        private float _sparkCd;
        private float _lockHintAt = -10f;

        /// <summary>The scene goes: the bubbles went with their hosts.</summary>
        private void ClearCombatExtras()
        {
            _shields.Clear();
            _shieldSeen.Clear();
            _scatterTargets.Clear();
            _turretCd = _sparkCd = 0f;
            BoostLit = false;
        }

        /// <summary>Where an entity is drawn right now (its view, which interpolates) — else where the server put it.</summary>
        private Vector3 EntityWorld(NetCombatEntity e)
            => _entities.TryGetValue(e.Id, out var go) && go != null
                ? go.transform.position
                : _root.transform.TransformPoint(new Vector3(e.X, e.Y, e.Z));

        // ---------------------------------------------------------------- targets

        /// <summary>A homing weapon's target: the locked hostile — or the locked station — within its range, wherever the
        /// nose points. Without one, a pull of the trigger says how to get one.</summary>
        private NetCombatEntity HomingTarget(string weaponKey, float range, Vector3 shipPos)
        {
            if (TryLockedEntity(out var locked))
            {
                bool hostile = locked.Hostile && SpaceTargeting.IsHostileShipKind(locked.Kind);
                if (hostile && (new Vector3(locked.X, locked.Y, locked.Z) - shipPos).sqrMagnitude <= range * range)
                {
                    return locked;
                }

                if (locked.Kind == "SpaceStation" && StationDistance(locked, shipPos) <= range)
                {
                    return locked;
                }
            }

            if (InputMap.PrimaryDown() && Time.time - _lockHintAt > 2f)
            {
                _lockHintAt = Time.time; // not again on every click
                var loc = Game.Localizer;
                Game.ShowMessage(loc != null
                    ? HudUi.ExpandKeyTokens(loc, loc.Get("ui.space.missile_needs_lock"))
                    : "The missile needs a locked target in range.");
            }

            return null;
        }

        /// <summary>The station the nose points at within the weapon's range — only for a weapon that fires on stations,
        /// and only when nothing else is in the sights.</summary>
        private NetCombatEntity StationFireTarget(string weaponKey, float range, Vector3 shipPos, Vector3 fwd)
        {
            var space = Game.Space;
            if (space == null || !ShipWeaponView.FiresOnStations(Game.Content?.GetShipModule(weaponKey)))
            {
                return null;
            }

            NetCombatEntity best = null;
            float bestDist = range;
            foreach (var e in space.Entities)
            {
                if (e.Kind == "SpaceStation" && StationRay(e, shipPos, fwd, 1f, out float dist) && dist <= bestDist)
                {
                    bestDist = dist;
                    best = e;
                }
            }

            return best;
        }

        /// <summary>Where the ray from <paramref name="from"/> along <paramref name="dir"/> (flight space) meets a station:
        /// its hull box grown by <paramref name="grow"/> (1 = the hull itself, more = its shield bubble's reach), or the
        /// placeholder's shell while the hull is not here yet.</summary>
        private bool StationRay(NetCombatEntity e, Vector3 from, Vector3 dir, float grow, out float dist)
        {
            dist = 0f;
            Vector3 centre = new Vector3(e.X, e.Y, e.Z);
            Vector3 half = Vector3.one * (StationKeepOut * Mathf.Max(1f, e.Scale));
            if (_structs.TryGetValue(e.Id, out var vs) && vs.IsStation && vs.Cells != null && vs.Cells.Count > 0)
            {
                centre = vs.Pos;
                half = vs.Half;
            }

            half *= grow;
            float tMin = 0f, tMax = float.MaxValue;
            for (int axis = 0; axis < 3; axis++)
            {
                float o = from[axis] - centre[axis];
                float d = dir[axis];
                if (Mathf.Abs(d) < 1e-6f)
                {
                    if (Mathf.Abs(o) > half[axis])
                    {
                        return false;
                    }

                    continue;
                }

                float a = (-half[axis] - o) / d;
                float b = (half[axis] - o) / d;
                tMin = Mathf.Max(tMin, Mathf.Min(a, b));
                tMax = Mathf.Min(tMax, Mathf.Max(a, b));
                if (tMin > tMax)
                {
                    return false;
                }
            }

            dist = tMin;
            return true;
        }

        /// <summary>The further hostiles a scatter shot at <paramref name="aimed"/> reaches — the server's rule: in range,
        /// inside the cone around the aimed one, the nearest first.</summary>
        private void CollectScatterTargets(ShipModuleDefinition def, NetCombatEntity aimed, float range, Vector3 shipPos)
        {
            _scatterTargets.Clear();
            var space = Game.Space;
            int more = ShipWeaponRules.Targets(def) - 1;
            if (space == null || more <= 0)
            {
                return;
            }

            float coneCos = ShipWeaponRules.ConeCos(def);
            Vector3 a = new Vector3(aimed.X, aimed.Y, aimed.Z) - shipPos;
            foreach (var e in space.Entities)
            {
                if (e.Id == aimed.Id || !e.Hostile || !SpaceTargeting.IsHostileShipKind(e.Kind))
                {
                    continue;
                }

                Vector3 b = new Vector3(e.X, e.Y, e.Z) - shipPos;
                if (b.sqrMagnitude <= range * range && ShipWeaponRules.InCone(a.x, a.y, a.z, b.x, b.y, b.z, coneCos))
                {
                    _scatterTargets.Add(e);
                }
            }

            _scatterTargets.Sort((x, y) => (new Vector3(x.X, x.Y, x.Z) - shipPos).sqrMagnitude.CompareTo((new Vector3(y.X, y.Y, y.Z) - shipPos).sqrMagnitude));
            if (_scatterTargets.Count > more)
            {
                _scatterTargets.RemoveRange(more, _scatterTargets.Count - more);
            }
        }

        // ---------------------------------------------------------------- the own shot

        /// <summary>The picture and the sound of the own shot at <paramref name="target"/>, and its relay to the other pilots.</summary>
        private void ShowOwnShot(NetCombatEntity target, string weaponKey)
        {
            var look = FxLook.ForModule(Game.Content, weaponKey);
            var def = Game.Content?.GetShipModule(weaponKey);
            Vector3 shipPos = _ship.transform.localPosition;
            Vector3 end = new Vector3(target.X, target.Y, target.Z);
            bool mining = SpaceTargeting.IsMiningKind(target.Kind); // a derelict (#1664) and wreckage (#2353) are mined like rock
            if (target.Kind == "SpaceStation")
            {
                end = ShowStationShot(target, look, WeaponRangeFor(weaponKey), ShipWeaponRules.IsHoming(def));
            }
            else
            {
                ShowShotAt(target, look, mining, first: true);
                CollectScatterTargets(def, target, WeaponRangeFor(weaponKey), shipPos);
                foreach (var other in _scatterTargets)
                {
                    ShowShotAt(other, look, false, first: false);
                }
            }

            PlayOwnShotSound(look.Style, mining); // #3238: a cannon fires the recorded ship-weapon shot (SpaceView.Audio.cs)
            // #2158: the other pilots in this flight instance see the shot too (cosmetic, instance-frame positions).
            Game.Network?.SendFx(FxActionKinds.Shot, weaponKey, shipPos, end, true);
        }

        /// <summary>One shot at one entity; where it carries a shield, the bubble answers when the shot arrives.</summary>
        private void ShowShotAt(NetCombatEntity target, FxLook look, bool mining, bool first)
        {
            Vector3 hit = EntityWorld(target);
            string id = target.Id;
            var entity = target;
            SpaceFx.Fire(look, _ship.transform, ShipLocalBounds(), hit, mining, () => EntityWorld(entity), first);
            if (target.Shield > 0.01f)
            {
                var from = _ship.transform;
                float level = target.Shield;
                FxKit.Delay(SpaceFx.TravelSeconds(look, Vector3.Distance(from.position, hit)), () =>
                {
                    if (from != null)
                    {
                        EntityShieldHit(id, from.position, level, look.Color);
                    }
                });
            }
        }

        /// <summary>A shot at a station: it ends on the shield bubble of an intact one (which ripples there) or on the
        /// hull of a derelict. Returns the end point in the flight space.</summary>
        private Vector3 ShowStationShot(NetCombatEntity station, FxLook look, float range, bool homing)
        {
            Vector3 shipPos = _ship.transform.localPosition;
            Vector3 centre = _structs.TryGetValue(station.Id, out var vs) && vs.IsStation ? vs.Pos : new Vector3(station.X, station.Y, station.Z);
            Vector3 dir = homing ? (centre - shipPos).normalized : _ship.transform.localRotation * Vector3.forward;
            bool shielded = SpaceCombatRules.StationHasShield(station.Id);
            float dist;
            if (!StationRay(station, shipPos, dir, shielded ? StationShieldReach : 1f, out dist) && !StationRay(station, shipPos, dir, 1f, out dist))
            {
                dist = Mathf.Min(range, (centre - shipPos).magnitude);
            }

            Vector3 end = shipPos + (dir * dist);
            Vector3 world = _root.transform.TransformPoint(end);
            SpaceFx.Fire(look, _ship.transform, ShipLocalBounds(), world, !shielded);
            if (shielded)
            {
                string id = station.Id;
                Vector3 outward = _root.transform.TransformDirection(-dir);
                FxKit.Delay(SpaceFx.TravelSeconds(look, Vector3.Distance(_ship.transform.position, world)), () => StationShieldHit(id, world, outward, look.Color));
            }

            return end;
        }

        // ---------------------------------------------------------------- shields

        /// <summary>How far outside its hull box a station's shield bubble takes a shot (a share of the box).</summary>
        private const float StationShieldReach = 1.2f;

        /// <summary>The bubble of an entity or a station, made on first need. A bubble whose host went is made anew.</summary>
        private FxShield ShieldFor(string id, Transform host, Bounds local)
        {
            if (_shields.TryGetValue(id, out var shield) && shield != null)
            {
                return shield;
            }

            shield = FxShield.Attach(host, local);
            if (shield != null)
            {
                _shields[id] = shield;
            }

            return shield;
        }

        /// <summary>The hull of a model in its own frame, from its renderers (effects' particle renderers left out).</summary>
        private static Bounds LocalBoundsOf(GameObject go)
        {
            bool any = false;
            var world = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.GetComponent<FxShield>() != null)
                {
                    continue;
                }

                if (any)
                {
                    world.Encapsulate(r.bounds);
                }
                else
                {
                    world = r.bounds;
                    any = true;
                }
            }

            var s = go.transform.lossyScale;
            var size = new Vector3(world.size.x / Mathf.Max(0.01f, s.x), world.size.y / Mathf.Max(0.01f, s.y), world.size.z / Mathf.Max(0.01f, s.z));
            float widest = Mathf.Max(2f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
            return new Bounds(go.transform.InverseTransformPoint(world.center), Vector3.one * widest); // a round bubble: the model spins
        }

        /// <summary>A shot arrived at a hostile that carries a shield: the bubble ripples on the side it came from.</summary>
        private void EntityShieldHit(string id, Vector3 fromWorld, float level, Color color)
        {
            if (!_entities.TryGetValue(id, out var go) || go == null)
            {
                return;
            }

            var shield = ShieldFor(id, go.transform, LocalBoundsOf(go));
            if (shield == null)
            {
                return;
            }

            Vector3 outward = (fromWorld - shield.transform.position).normalized;
            Vector3 at = shield.transform.TransformPoint(shield.transform.InverseTransformDirection(outward).normalized * 0.5f);
            shield.Hit(at, level);
            SpaceCombatFx.ShieldDeflect(at, outward, color);
            ClientAudio.Instance?.At("ship_shield_hit", at, 1.15f, 0.8f);
        }

        /// <summary>A shot ended on an intact station's shield.</summary>
        private void StationShieldHit(string id, Vector3 at, Vector3 outward, Color color)
        {
            Transform host = null;
            var local = new Bounds(Vector3.zero, Vector3.one * (StationKeepOut * 2f));
            if (_structs.TryGetValue(id, out var vs) && vs.Root != null)
            {
                host = vs.Root.transform;
                local = new Bounds(Vector3.zero, vs.Half * 2f);
            }
            else if (_entities.TryGetValue(id, out var go) && go != null)
            {
                host = go.transform;
                local = LocalBoundsOf(go);
            }

            if (host == null)
            {
                return;
            }

            var shield = ShieldFor(id, host, local);
            shield?.Hit(at, 1f);
            SpaceCombatFx.ShieldDeflect(at, outward, color);
            ClientAudio.Instance?.At("ship_shield_hit", at, 0.85f, 1f);
        }

        // ---------------------------------------------------------------- every frame

        /// <summary>Once a frame while flying: a hostile's shield that just went bursts, a silenced hostile sparks, and
        /// the point-defence turret draws its shots.</summary>
        private void UpdateCombatExtras(float dt)
        {
            var space = Game.Space;
            if (space == null || _ship == null || _root == null)
            {
                return;
            }

            _sparkCd -= dt;
            bool spark = _sparkCd <= 0f;
            if (spark)
            {
                _sparkCd = 0.14f;
            }

            foreach (var e in space.Entities)
            {
                if (!SpaceTargeting.IsHostileShipKind(e.Kind))
                {
                    continue;
                }

                if (_shieldSeen.TryGetValue(e.Id, out float last) && last > 0.01f && e.Shield <= 0.01f
                    && _entities.TryGetValue(e.Id, out var host) && host != null)
                {
                    ShieldFor(e.Id, host.transform, LocalBoundsOf(host))?.Shatter();
                }

                _shieldSeen[e.Id] = e.Shield;
                if (spark && e.Staggered && _entities.TryGetValue(e.Id, out var dazed) && dazed != null)
                {
                    SpaceCombatFx.IonBurst(IonCol, IonCore, dazed.transform.position, 0.35f); // an ion pulse holds it
                }
            }

            UpdateTurretFx(dt, space);
        }

        /// <summary>The point-defence turret's shots. The server fires it; the client draws a shot on the turret's own
        /// cadence whenever a hostile is in its range and the world lets ship weapons fire.</summary>
        private void UpdateTurretFx(float dt, SpaceState space)
        {
            _turretCd -= dt;
            if (_turretCd > 0f || _eva || _phase != Phase.Cruise)
            {
                return;
            }

            _turretCd = 0.3f;
            if (Game.ShipCombat == null || !Game.ShipCombat.CombatWeaponsOn)
            {
                return; // the world lets no ship weapon fire on hostiles — the server's turret stays silent too
            }

            ShipModuleDefinition turret = null;
            string turretKey = null;
            var mods = Game.ShipCombat?.Modules;
            if (mods != null)
            {
                foreach (var m in mods)
                {
                    var def = Game.Content?.GetShipModule(m);
                    if (ShipWeaponRules.IsAuto(def))
                    {
                        turret = def;
                        turretKey = m;
                        break;
                    }
                }
            }

            if (turret == null)
            {
                _turretCd = 1f;
                return;
            }

            float range = WeaponRangeFor(turretKey);
            Vector3 shipPos = _ship.transform.localPosition;
            NetCombatEntity nearest = null;
            float nearestSq = range * range;
            foreach (var e in space.Entities)
            {
                float sq = (new Vector3(e.X, e.Y, e.Z) - shipPos).sqrMagnitude;
                if (e.Hostile && SpaceTargeting.IsHostileShipKind(e.Kind) && sq <= nearestSq)
                {
                    nearestSq = sq;
                    nearest = e;
                }
            }

            if (nearest == null)
            {
                return;
            }

            _turretCd = Mathf.Max(0.2f, WeaponCooldownFor(turretKey));
            ShowShotAt(nearest, FxLook.ForModule(Game.Content, turretKey), false, first: false);
            ClientAudio.Instance?.Cue(FlightWeaponCue.Laser, 0.35f);
        }

        // ---------------------------------------------------------------- other pilots

        /// <summary>Another pilot rolls (the server's word, #3753): their hull turns once about its nose.</summary>
        private void OnRemoteRoll(ActionFx m)
        {
            if (!_active || m.PlayerId == Game.LocalPlayerId || !_remotePlayers.TryGetValue(m.PlayerId, out var av) || av.Ship == null)
            {
                return;
            }

            float dir = m.ToX - m.FromX < 0f ? -1f : 1f;
            var hull = av.Ship.transform;
            var rest = hull.localRotation;
            FxKit.Animate(ShipManeuverRules.RollSeconds,
                t => hull.localRotation = rest * Quaternion.Euler(0f, 0f, -dir * 360f * ShipRollState.Ease(t)),
                () => hull.localRotation = rest,
                av.Ship);
            ClientAudio.Instance?.At(ClientAudio.Instance.Has("ship_roll") ? "ship_roll" : "rcs_puff", hull.position, 1f, 0.8f);
        }
    }
}
