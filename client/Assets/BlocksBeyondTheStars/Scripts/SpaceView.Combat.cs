// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Ship combat in flight: the ship-systems quick-bar (laser, tractor beam), choosing a target and firing at it,
    /// incoming hostile fire, hits, sparks and explosions, and the NPC traders' warp flashes.
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>Another pilot fired a ship weapon in this flight instance (#2158): the same look as our own shots,
        /// from their hull to the target point (both in the instance frame).</summary>
        private void OnRemoteShipFx(BlocksBeyondTheStars.Networking.Messages.ActionFx m)
        {
            if (!_active || _root == null || m == null || m.Outcome || m.PlayerId == Game.LocalPlayerId
                || (m.Kind != BlocksBeyondTheStars.Shared.Definitions.FxActionKinds.Shot && m.Kind != BlocksBeyondTheStars.Shared.Definitions.FxActionKinds.Scan)
                || !_remotePlayers.TryGetValue(m.PlayerId, out var av) || av.Root == null)
            {
                return;
            }

            var look = FxLook.ForModule(Game.Content, m.ItemKey);
            var target = _root.transform.TransformPoint(new Vector3(m.ToX, m.ToY, m.ToZ));
            if (m.Kind == BlocksBeyondTheStars.Shared.Definitions.FxActionKinds.Scan)
            {
                // #2237: another pilot's ship scan — the light fan from their nose and a pulse on what they read.
                var fan = FxKit.BeamMaterial("shipscanfan", look.Color2, coreWidth: 0.6f, noiseScale: 2.5f, noiseSpeed: 20f, intensity: 1.6f);
                FxKit.Beam(av.Root.transform.position, target, look.Color, 0.05f, 0.35f, fan);
                SpaceFx.ScanPulse(target, look.Color, 6f);
                return;
            }

            SpaceFx.Fire(look, av.Root.transform, new Bounds(Vector3.zero, new Vector3(2.4f, 1.2f, 4f)), target, look.Is("drill_beam"));
        }

        /// <summary>A space entity was destroyed (#2156): hostiles explode, salvage drops just go (the tractor shows
        /// those). Asteroid bodies break up in <see cref="OnStructEntityDestroyed"/>.</summary>
        private void OnEntityDestroyedFx(BlocksBeyondTheStars.Networking.Messages.SpaceEntityDestroyed m)
        {
            if (!_active || m == null || !_entities.TryGetValue(m.Id, out var go) || go == null)
            {
                return;
            }

            _entityKinds.TryGetValue(m.Id, out var kind);
            float scale = kind switch
            {
                "Cruiser" => 2.2f,
                "BanditShip" => 1.5f,
                "Ufo" => 1.2f,
                "Drone" => 0.9f,
                _ => 0f,
            };
            if (scale <= 0f)
            {
                return;
            }

            SpaceFx.Explosion(go.transform.position, scale, go, kind == "BanditShip" ? new Color(0.55f, 0.75f, 1f) : new Color(1f, 0.4f, 0.3f));
            go.SetActive(false); // what is left of the model goes with the next entity sync
        }


        /// <summary>An orange spark burst at the ship when a hit lands (paired with the screen flash).</summary>
        private void SpawnHitSparks(float mag)
        {
            if (Camera == null)
            {
                return;
            }

            // #2156: weightless glowing sparks off the hull plus a flash (was a handful of opaque cubes).
            Vector3 at = _ship != null ? _ship.transform.position : Camera.transform.position + Camera.transform.forward * 6f;
            if (_ship != null)
            {
                var b = ShipLocalBounds();
                at = _ship.transform.TransformPoint(b.center + Vector3.Scale(Random.onUnitSphere, b.extents));
            }

            HullHit(at, mag);
        }

        /// <summary>A big debris + fireball burst at <paramref name="at"/> when the ship is destroyed in flight —
        /// the explosion that used to be missing (only a screen flash played). Reuses the spark debris, scaled up
        /// and longer-lived, plus a few bright fireball cores.</summary>
        private void SpawnShipExplosion(Vector3 at) => SpawnParticleExplosion(at, 1.4f);

        /// <summary>A one-shot ParticleSystem explosion (expanding orange fireball + a fast spark burst), parented
        /// under the space root and self-destroying when it finishes (stopAction = Destroy). Replaces the old
        /// Unlit cube debris. Reusable for ship destruction, asteroid breaks, etc.</summary>
        private void SpawnParticleExplosion(Vector3 at, float scale)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Particle");
            if (shader == null)
            {
                return;
            }

            var go = new GameObject("Explosion");
            if (_root != null)
            {
                go.transform.SetParent(_root.transform, true);
            }

            go.transform.position = at;

            // Fireball: a short outward burst of big additive orange bits that expand then fade to dark red.
            var fire = go.AddComponent<ParticleSystem>();
            fire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var fm = fire.main;
            fm.loop = false;
            fm.duration = 0.4f;
            fm.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.0f);
            fm.startSpeed = new ParticleSystem.MinMaxCurve(3f, 11f);
            fm.startSize = new ParticleSystem.MinMaxCurve(0.6f * scale, 1.5f * scale);
            fm.startColor = new Color(1f, 0.7f, 0.32f, 1f);
            fm.maxParticles = 70;
            fm.simulationSpace = ParticleSystemSimulationSpace.World;
            fm.gravityModifier = 0f;
            fm.stopAction = ParticleSystemStopAction.Destroy; // tears down the whole GO (incl. child sparks) when done
            var fe = fire.emission;
            fe.rateOverTime = 0f;
            fe.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)28) });
            var fsh = fire.shape;
            fsh.shapeType = ParticleSystemShapeType.Sphere;
            fsh.radius = 0.4f * scale;
            var fcol = fire.colorOverLifetime;
            fcol.enabled = true;
            var fgrad = new Gradient();
            fgrad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.92f, 0.6f), 0f),
                    new GradientColorKey(new Color(1f, 0.5f, 0.18f), 0.5f),
                    new GradientColorKey(new Color(0.5f, 0.12f, 0.05f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.45f), new GradientAlphaKey(0f, 1f) });
            fcol.color = new ParticleSystem.MinMaxGradient(fgrad);
            var fsol = fire.sizeOverLifetime;
            fsol.enabled = true;
            fsol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.15f)); // expand
            var fr = fire.GetComponent<ParticleSystemRenderer>();
            fr.material = new Material(shader) { mainTexture = ThrusterDot() };
            fr.renderMode = ParticleSystemRenderMode.Billboard;
            fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fr.receiveShadows = false;
            fr.sortMode = ParticleSystemSortMode.None;
            fire.Play();

            // Sparks: a faster, smaller white-gold burst that streaks outward.
            var sgo = new GameObject("Sparks");
            sgo.transform.SetParent(go.transform, false);
            sgo.transform.position = at;
            var sp = sgo.AddComponent<ParticleSystem>();
            sp.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sm = sp.main;
            sm.loop = false;
            sm.duration = 0.3f;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.85f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(11f, 28f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            sm.startColor = new Color(1f, 0.9f, 0.55f, 1f);
            sm.maxParticles = 90;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            sm.gravityModifier = 0f;
            var se = sp.emission;
            se.rateOverTime = 0f;
            se.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(40f * scale)) });
            var ssh = sp.shape;
            ssh.shapeType = ParticleSystemShapeType.Sphere;
            ssh.radius = 0.2f * scale;
            var scol = sp.colorOverLifetime;
            scol.enabled = true;
            var sgrad = new Gradient();
            sgrad.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.6f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            scol.color = new ParticleSystem.MinMaxGradient(sgrad);
            var sr = sgo.GetComponent<ParticleSystemRenderer>();
            sr.material = new Material(shader) { mainTexture = ThrusterDot() };
            sr.renderMode = ParticleSystemRenderMode.Billboard;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
            sr.sortMode = ParticleSystemSortMode.None;
            sp.Play();
        }

        /// <summary>Plays the queued NPC-trader warp flashes (a localized hyperspace burst at a world position),
        /// so every player in the instance sees traders arrive and depart. Drained once per frame.</summary>
        private void DrainWarpFx()
        {
            var pending = Game != null ? Game.PendingWarpFx : null;
            if (pending == null || pending.Count == 0 || _root == null)
            {
                return;
            }

            foreach (var fx in pending)
            {
                // Trader positions are instance-local (the same space remote poses use), so map through _root.
                var at = _root.transform.TransformPoint(new Vector3(fx.X, fx.Y, fx.Z));
                if (fx.Style == "wormhole")
                {
                    WormholeVisuals.FlashAt(at, fx.Arriving); // #2242: another pilot vanishing into / shooting out of a rift
                    continue;
                }

                if (fx.Raider && fx.Arriving)
                {
                    ClientAudio.Instance?.At("raider_warp_in", at, 1f, 1f); // #2357: the raider's ominous arrival
                }

                // #2157: a stretched light streak + flash + ring (was a burst of cubes).
                var dir = Camera != null ? (at - Camera.transform.position) : Vector3.forward;
                dir = Vector3.Cross(dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward, Vector3.up);
                SpaceFx.WarpZip(at, dir.sqrMagnitude > 1e-4f ? dir : Vector3.right, fx.Arriving);
            }

            pending.Clear();
        }

        /// <summary>Rebuilds the quick-bar of ship systems from the active ship's fitted modules: one entry
        /// PER fitted weapon (#1263 — the list used to stop at the first weapon it met, and every ship starts
        /// with the basic laser, so a breaker or cannon built later was fitted, paid for and never selectable),
        /// labelled by module name so laser ≠ breaker, plus the tractor beam. Always offers at least the
        /// laser so a fresh ship can fight.</summary>
        private void RebuildSystems()
        {
            _systems.Clear();
            var mods = Game.ShipCombat?.Modules;
            if (mods != null)
            {
                foreach (var m in mods)
                {
                    if (IsWeaponModule(m))
                    {
                        _systems.Add(new ShipSystem { Label = Loc("module." + m + ".name", m), Kind = "laser", WeaponKey = m });
                    }
                }
            }

            // #2237: no weapon fitted → no laser slot. The old fallback showed a "Laser" on every unarmed ship (a self-built
            // one never has a weapon) that the server then refused with "no weapon"; the scanner below is always there.

            if (mods != null && System.Array.IndexOf(mods, "tractor_beam") >= 0)
            {
                _systems.Add(new ShipSystem { Label = Loc("ui.space.sys_tractor", "Tractor"), Kind = "tractor" });
            }

            // #2237: every ship's scanner — the cockpit carries tier 1 — on the LAST slot, so the weapon numbers stay.
            var scanner = CurrentScanner();
            _systems.Add(new ShipSystem { Label = Loc("ui.space.sys_scanner", "Scanner"), Kind = "scanner", WeaponKey = scanner.ModuleKey });

            _selectedSystem = Mathf.Clamp(_selectedSystem, 0, _systems.Count - 1);
        }

        /// <summary>The ship modules the flight quick-bar can fire (mirrors the server's weapon table).</summary>
        private static bool IsWeaponModule(string m)
            => m == "ship_laser_basic" || m == "ship_cannon_1" || m == "laser_cannon_2" || m == "asteroid_breaker";

        // The tractor auto-locks (and the server pulls in) salvage from as far as the laser reaches — matches
        // the server's TractorReach so the beam never visually "hits" a drop the server then rejects as too far.
        private const float TractorLockRange = 45f;

        /// <summary>The nearest salvage drop within tractor reach (omnidirectional — unlike the laser, the tractor
        /// has no firing arc). Drives the lock reticle and which drop a pull collects.</summary>
        private BlocksBeyondTheStars.Networking.Messages.NetCombatEntity BestTractorTarget()
        {
            var space = Game.Space;
            if (space == null || _ship == null)
            {
                return null;
            }

            Vector3 shipPos = _ship.transform.localPosition;
            if (TryLockedDrop(shipPos, TractorLockRange, out var lockedDrop))
            {
                return lockedDrop; // #2277: a drop locked with "target ahead" is the one the beam pulls
            }

            BlocksBeyondTheStars.Networking.Messages.NetCombatEntity best = null;
            float bestSq = TractorLockRange * TractorLockRange;
            foreach (var e in space.Entities)
            {
                if (!BlocksBeyondTheStars.Client.Core.SpaceTargeting.IsCollectableKind(e.Kind))
                {
                    continue; // drops and the debris fields' capsules (#2353)
                }

                float sq = (new Vector3(e.X, e.Y, e.Z) - shipPos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = e;
                }
            }

            return best;
        }

        /// <summary>Manual tractor pull: tells the server to pull in the auto-locked salvage drop (or, with none
        /// locked, a blind radius sweep) and draws the cyan beam to the drop + a ping.</summary>
        private void ActivateTractor(BlocksBeyondTheStars.Networking.Messages.NetCombatEntity target)
        {
            Game.Network?.SendTractorPull(target?.Id ?? string.Empty);
            Vector3 from = _ship.transform.localPosition;
            Vector3 to = target != null
                ? new Vector3(target.X, target.Y, target.Z)               // beam to the actual drop
                : from + _ship.transform.localRotation * Vector3.forward * 12f; // nothing locked: a short sweep
            SpaceFx.Tractor(_root.transform.TransformPoint(from), _root.transform.TransformPoint(to), FxLook.ForModule(Game.Content, "tractor_beam").Color);
            ClientAudio.Instance?.Cue("scan_ping");
        }

        /// <summary>The best entity to fire on (#693). AutoAim rule ON: the nearest asteroid/hostile within
        /// range inside a tight forward cone (point the nose at it). AutoAim OFF: only a target the nose ray
        /// genuinely passes through counts — line it up or the trigger stays cold.</summary>
        private BlocksBeyondTheStars.Networking.Messages.NetCombatEntity BestFireTarget(string weaponKey)
        {
            var space = Game.Space;
            if (space == null || _ship == null)
            {
                return null;
            }

            float range = WeaponRangeFor(weaponKey);
            Vector3 shipPos = _ship.transform.localPosition;
            Vector3 fwd = _ship.transform.localRotation * Vector3.forward;
            bool autoAim = Game.AutoAimOn;

            // #2277: the locked target first — in range and inside ±40° with AutoAim on (the server allows ±60°).
            if (TryLockAssist(weaponKey, range, shipPos, fwd, out var lockedTarget))
            {
                return lockedTarget;
            }

            BlocksBeyondTheStars.Networking.Messages.NetCombatEntity best = null;
            float bestScore = 0f;      // auto-aim: alignment/distance score
            float bestDist = range;    // boresight: nearest body the ray pierces
            foreach (var e in space.Entities)
            {
                if (!BlocksBeyondTheStars.Client.Core.SpaceTargeting.IsFireTargetKind(e.Kind))
                {
                    continue; // (a Wreck is salvage, #1664 — carved with the mining beam like a rock)
                }

                Vector3 to = new Vector3(e.X, e.Y, e.Z) - shipPos;
                float dist = to.magnitude;
                if (dist > range || dist < 0.001f)
                {
                    continue;
                }

                float align = Vector3.Dot(to / dist, fwd); // 1 = dead ahead
                if (autoAim)
                {
                    if (align < SpaceAutoAimCone)
                    {
                        continue;
                    }

                    float score = align - (dist / range) * 0.25f; // prefer aligned + close
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = e;
                    }
                }
                else
                {
                    // Boresight: perpendicular distance of the nose ray from the body's centre must be
                    // inside its (generous) radius. Nearest pierced body wins.
                    float along = align * dist;
                    if (along <= 0f || along >= bestDist)
                    {
                        continue;
                    }

                    float missSq = dist * dist - along * along;
                    float radius = 2.5f * Mathf.Max(1f, e.Scale);
                    if (missSq <= radius * radius)
                    {
                        bestDist = along;
                        best = e;
                    }
                }
            }

            return best;
        }

        /// <summary>Sends the fire intent + plays the laser beam, impact flash and sound. Mining (asteroid)
        /// reads amber; combat reads cyan.</summary>
        private void FireAt(BlocksBeyondTheStars.Networking.Messages.NetCombatEntity target, string weaponKey)
        {
            Vector3 fwd = _ship.transform.localRotation * Vector3.forward;
            Game.Network?.SendFireWeapon(weaponKey, target.Id,
                new BlocksBeyondTheStars.Shared.Geometry.Vector3f(fwd.x, fwd.y, fwd.z));
            Game.LastShotTargetId = target.Id;
            Game.LastShotTime = Time.time;
            OnShotFired(target); // #2327: a shot at a rock with nothing locked locks it
            bool mining = BlocksBeyondTheStars.Client.Core.SpaceTargeting.IsMiningKind(target.Kind); // a derelict (#1664) and wreckage (#2353) are mined like rock
            Color col = mining ? new Color(1f, 0.7f, 0.25f) : new Color(0.45f, 1f, 1f);

            // #2156: every module its own look (data-driven fx) — they all drew the same 0.16-unit cube.
            _ = col;
            Vector3 hit = _root.transform.TransformPoint(new Vector3(target.X, target.Y, target.Z));
            SpaceFx.Fire(FxLook.ForModule(Game.Content, weaponKey), _ship.transform, ShipLocalBounds(), hit, mining);
            ClientAudio.Instance?.Cue(mining ? "ship_mine" : "ship_laser");
            // #2158: the other pilots in this flight instance see the shot too (cosmetic, instance-frame positions).
            Game.Network?.SendFx(BlocksBeyondTheStars.Shared.Definitions.FxActionKinds.Shot, weaponKey,
                _ship.transform.localPosition, new Vector3(target.X, target.Y, target.Z), true);
        }

        private void OnShipCombat(BlocksBeyondTheStars.Networking.Messages.ShipCombatStatus s)
        {
            // Scale the jolt + red flash to how hard we were hit, and *set* (not accumulate) them — so a
            // steady trickle of chip damage is a faint rumble, while a real hit is a sharp jolt. Accumulating
            // every tick made continuous fire pin the shake/flash at maximum (the ship "being shaken").
            float shieldDrop = _lastShield >= 0f ? Mathf.Max(0f, _lastShield - s.Shield) : 0f;
            float hullDrop = _lastHull >= 0f ? Mathf.Max(0f, _lastHull - s.Hull) : 0f;
            float drop = Mathf.Max(shieldDrop, hullDrop);
            if (drop > 0.05f && _active)
            {
                float mag = Mathf.Clamp01(drop / 8f); // a full jolt only for a big single hit (~8 dmg)
                _hitFlash = Mathf.Max(_hitFlash, 0.2f + 0.6f * mag);
                FxCamera.AddTrauma(0.1f + 0.45f * mag); // #2152: rotational trauma shake (was a positional jitter)
                if (hullDrop > 0.05f)
                {
                    SpawnHitSparks(mag); // visible damage at the hull, not just a screen tint
                }
                else if (InDebrisFieldNow() && !HostileInAttackRange())
                {
                    // #2355: a shield drop with no hull damage and no attacker inside a debris field is the rubble
                    // tapping the shield — the clank at the hull and a few sparks (the shield-hit cue plays anyway).
                    if (_ship != null)
                    {
                        ClientAudio.Instance?.At("debris_bump", _ship.transform.position, 1f, 0.9f);
                    }

                    SpawnHitSparks(mag * 0.5f);
                }
            }

            if (_active && _lastShield > 0.5f && s.Shield <= 0.01f)
            {
                _shield?.Shatter(); // #2156: the shield breaks — the bubble bursts into hex sparkles
            }

            _lastHull = s.Hull;
            _lastShield = s.Shield;
        }

        /// <summary>Ship destroyed in flight (server recovered it to base): burst the hull into an explosion at
        /// its position before the view tears down. Previously only DeathFx flashed the screen — no explosion
        /// played in space. A normal return from space (ShipDisabled == false) does nothing here.</summary>
        private void OnSpaceClosedFx(BlocksBeyondTheStars.Networking.Messages.SpaceClosed m)
        {
            if (!m.ShipDisabled || !_active)
            {
                return;
            }

            Vector3 at = _ship != null
                ? _ship.transform.position
                : (Camera != null ? Camera.transform.position + Camera.transform.forward * 6f : SceneOrigin);
            SpawnShipExplosion(at); // the boom + screen flash are already played by ClientAudio / DeathFx
            SpaceFx.Explosion(at, 1.6f, null, new Color(1f, 0.5f, 0.3f)); // #2156: shockwave ring, fireball glows, light
            _hitFlash = Mathf.Max(_hitFlash, 0.9f);
            FxCamera.AddTrauma(0.8f);
            _shipDestroyed = true; // once the player confirms the death screen, tear down at once (no recovery descent)

            // Hide the doomed hull so the player sees the debris, not an intact ship flying the recovery descent.
            if (_ship != null)
            {
                foreach (var r in _ship.GetComponentsInChildren<Renderer>())
                {
                    r.enabled = false;
                }
            }
        }

        private void SpawnTractorBeam(Vector3 from, Vector3 to)
            => SpaceFx.Tractor(_root.transform.TransformPoint(from), _root.transform.TransformPoint(to), FxLook.ForModule(Game.Content, "tractor_beam").Color);

        // Incoming hostile fire is an invisible damage "aura" server-side (no projectile entity), so the player
        // never saw enemy shots. Mirror it visually: each in-range hostile flashes a red laser bolt at the ship.
        private const float HostileFireRange = BlocksBeyondTheStars.Client.Core.SpaceTargeting.AttackRange; // the server's ShipEngageRange
        private readonly Dictionary<string, float> _hostileFireCd = new Dictionary<string, float>();
        private readonly List<string> _hostileFireStale = new List<string>();

        /// <summary>Draws each in-range hostile firing red laser bolts at the ship (purely visual — damage stays
        /// server-authoritative). Per-hostile cadence so a pack reads as several attackers, not a strobe.</summary>
        private void UpdateHostileFire(float dt)
        {
            var space = Game.Space;
            if (space == null || _ship == null)
            {
                return;
            }

            // #2712: in Sandbox no hostile fires on this ship (the server deals no damage and its hostiles hunt someone
            // else), so no bolts either. The rules carry the mode this player plays — a per-player override included.
            if (Game.Rules != null && Game.Rules.GameMode == "Creative")
            {
                _hostileFireCd.Clear();
                return;
            }

            Vector3 shipLp = _ship.transform.localPosition;
            _hostileFireStale.Clear();
            foreach (var key in _hostileFireCd.Keys)
            {
                _hostileFireStale.Add(key);
            }

            foreach (var e in space.Entities)
            {
                if (!e.Hostile || !BlocksBeyondTheStars.Client.Core.SpaceTargeting.IsHostileShipKind(e.Kind))
                {
                    continue;
                }

                _hostileFireStale.Remove(e.Id);
                var from = new Vector3(e.X, e.Y, e.Z);
                if ((from - shipLp).sqrMagnitude > HostileFireRange * HostileFireRange)
                {
                    _hostileFireCd[e.Id] = Random.Range(0.15f, 0.5f); // re-arm; no shot until it closes in
                    continue;
                }

                float cd = (_hostileFireCd.TryGetValue(e.Id, out var v) ? v : 0f) - dt;
                if (cd <= 0f)
                {
                    SpawnHostileShot(from, shipLp);
                    cd = Random.Range(0.4f, 0.8f);
                }

                _hostileFireCd[e.Id] = cd;
            }

            foreach (var id in _hostileFireStale)
            {
                _hostileFireCd.Remove(id); // hostile gone (destroyed / left the instance)
            }
        }

        private void SpawnHostileShot(Vector3 from, Vector3 to)
        {
            // #2156: a glowing red bolt (was a lance cube). Where it lands on the shield bubble the hex shield ripples;
            // with the shield down the hull sparks instead. Purely visual — the server's aura owns the damage.
            if (_ship == null)
            {
                return;
            }

            _ = to;
            var wFrom = _root.transform.TransformPoint(from);
            var b = ShipLocalBounds();
            var toShip = (_ship.transform.position - wFrom).normalized;
            var hitLocal = b.center - _ship.transform.InverseTransformDirection(toShip) * b.extents.magnitude * 0.6f
                           + Random.insideUnitSphere * 0.5f;
            var wTo = _ship.transform.TransformPoint(hitLocal);
            var ship = _ship;
            SpaceFx.HostileBolt(wFrom, wTo, new Color(1f, 0.25f, 0.15f), () =>
            {
                if (ship == null)
                {
                    return;
                }

                var status = Game.ShipCombat;
                float shieldFrac = status != null && status.ShieldMax > 0f ? status.Shield / status.ShieldMax : 0f;
                if (_shield != null && shieldFrac > 0.01f)
                {
                    _shield.Hit(wTo, shieldFrac);
                }
                else
                {
                    HullHit(wTo, 0.4f);
                }
            });
        }

        /// <summary>Sparks, a flash and a few bits off the hull where a hit landed (shield down).</summary>
        private void HullHit(Vector3 at, float mag)
        {
            FxKit.Flash(at, new Color(1f, 0.75f, 0.35f), 1f + mag, 0.12f);
            FxKit.Burst(FxKit.Kind.Motes, at, 6 + Mathf.RoundToInt(mag * 10f), Vector3.zero, 0f, 3f, 9f, 0.06f, 0.12f, 0.25f, 0.5f,
                new Color(1f, 0.62f, 0.2f), Color.white);
            FxKit.Burst(FxKit.Kind.DebrisFloat, at, 2 + Mathf.RoundToInt(mag * 4f), Vector3.zero, 0f, 1.5f, 4f, 0.08f, 0.16f, 0.8f, 1.2f,
                new Color(0.4f, 0.42f, 0.46f));
            FxLights.Flash(at, new Color(1f, 0.6f, 0.25f), 1.2f + mag, 7f, 0.18f);
        }

        private void UpdateBeams(float dt)
        {
            for (int i = _beams.Count - 1; i >= 0; i--)
            {
                var b = _beams[i];
                b.Life -= dt;
                if (b.Life <= 0f || b.Go == null)
                {
                    if (b.Go != null)
                    {
                        Destroy(b.Go);
                    }

                    _beams.RemoveAt(i);
                    continue;
                }

                float k = b.Life / b.Max; // taper the beam as it fades
                var s = b.Go.transform.localScale;
                b.Go.transform.localScale = new Vector3(0.18f * k, 0.18f * k, s.z);
            }
        }
    }
}
