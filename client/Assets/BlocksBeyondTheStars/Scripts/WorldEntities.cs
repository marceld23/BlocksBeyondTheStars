// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Renders planet enemies as the story's **black three-eyed Guardian robots** (retheme) at the positions
    /// the server reports (<c>GameBootstrap.PlanetEnemies</c>) — a hunched dark-metal body with grasping arms,
    /// digitigrade legs, antenna-like sensor spikes and a row of three glowing RED sensor "eyes" (the red
    /// lights the settlers fear). Self-animated like <see cref="PlayerAvatar"/>/creatures: a speed-driven
    /// stalk cycle, an idle sweep, a swipe lunge when hostile and close, a hurt flinch on hull drops, and
    /// **robotic** growl/attack/hurt/die SFX (`enemy_growl`/`enemy_hurt`/`enemy_attack`/`enemy_die` — ElevenLabs
    /// servo-whir / metallic-clang / electric-zap / power-down, since every planet enemy is now a Guardian
    /// machine). The server stays authoritative over spawns/positions/deaths; the player attacks with F.
    /// </summary>
    public sealed class WorldEntities : MonoBehaviour
    {
        public GameBootstrap Game;

        private sealed class Entry
        {
            public GameObject Root;
            public Transform ArmL, ArmR, LegL, LegR, Head, Body;
            public Vector3 Target;        // canonical world space
            public Vector3 Settled;       // smoothed world position
            public float WalkPhase;
            public float Seed;            // per-enemy phase/variation offset
            public float PrevHull = -1f;
            public float NextGrowl;
            public float NextAttack;
            public float AttackUntil;     // claw-swipe window
            public float FlinchUntil;     // hurt recoil window
            public float Pitch;
            public bool IsDrone;          // flying scan-drone variant (hovers; no limb animation)
            public bool IsBandit;         // humanoid robber (upright gait, talks before it fights)
            public bool IsGunner;         // ranged bandit variant (tracer shots instead of claw swipes)
        }

        private readonly Dictionary<string, Entry> _enemies = new();

        // Reused across frames — per-Update allocations are steady-state GC churn (worst on WebGL).
        private readonly HashSet<string> _seenScratch = new();
        private readonly List<string> _staleScratch = new();
        private bool _subscribed;
        private WeaponFx _weapons; // shared VFX layer (laser beams), resolved lazily
        private static Material _hideMat, _hideDarkMat, _eyeMat, _clawMat;

        /// <summary>Range at which a hovering scan-drone opens fire on the player (blocks). Ground robots have
        /// no ranged attack — they only claw in melee.</summary>
        private const float DroneFireRange = 16f;

        /// <summary>Range at which a hostile bandit gunner fires its tracer (mirrors the server's damage aura).</summary>
        private const float BanditGunFireRange = 8f;

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (!_subscribed && Game.Network != null)
            {
                Game.Network.PlanetEnemyDefeated += OnDefeated;
                _subscribed = true;
            }

            // Everything below runs on the world clock rather than on frame time (#908): while the server holds
            // the world for the pause menu, bandits and beasts stop stepping, growling, lunging and firing —
            // an enemy that kept mauling the player through the "Pause" dialog was the loudest tell of all.
            var seen = _seenScratch;
            seen.Clear();
            var cam = Camera.main; // for the floating health bars (#692)
            foreach (var e in Game.PlanetEnemies)
            {
                seen.Add(e.Id);
                if (!_enemies.TryGetValue(e.Id, out var en))
                {
                    en = e.Kind == "ScanDrone" ? BuildDrone(e.Id)
                        : e.Kind == "Bandit" || e.Kind == "BanditGunner" ? BuildBandit(e.Id, e.Kind == "BanditGunner")
                        : Build(e.Id);
                    en.Target = en.Settled = new Vector3(e.X, e.Y, e.Z);
                    _enemies[e.Id] = en;
                }

                en.Target = new Vector3(e.X, e.Y, e.Z);
                en.Settled = Vector3.Lerp(en.Settled, en.Target, Game.WorldDeltaTime * 8f);

                // Face the walk direction (or the player when hostile and close — it stalks you).
                Vector3 scenePos = Game.ScenePos(en.Settled.x, en.Settled.y, en.Settled.z);
                Vector3 vel = en.Target - en.Settled;
                vel.y = 0f;
                Vector3 toPlayer = Game.PlayerPosition - scenePos;
                toPlayer.y = 0f;
                // A player who has fled into their ship is off-limits: the server already drops them as a
                // target (no hunt, no proximity damage), so mirror that here — the machine stops stalking and
                // holds its fire instead of staring at / shooting the hull.
                bool playerAboard = Game.Aboard;
                bool nearPlayer = toPlayer.sqrMagnitude < 64f;
                // A bandit faces you even while peaceful — it's walking up to TALK (the hold-up), not to graze.
                Vector3 face = (e.Hostile || en.IsBandit) && nearPlayer && !playerAboard ? toPlayer : vel;
                if (face.sqrMagnitude > 0.01f)
                {
                    en.Root.transform.rotation = Quaternion.Slerp(
                        en.Root.transform.rotation, Quaternion.LookRotation(face.normalized), Game.WorldDeltaTime * 6f);
                }

                en.Root.transform.position = scenePos;
                Animate(en, vel.magnitude / Mathf.Max(Game.WorldDeltaTime, 1e-4f), e.Hostile);

                var audio = ClientAudio.Instance;
                if (audio != null)
                {
                    // Periodic menacing growl, spatialised at the enemy (bandits are people — no robot growls).
                    if (!en.IsBandit && Game.WorldTime >= en.NextGrowl)
                    {
                        en.NextGrowl = Game.WorldTime + Random.Range(6f, 14f);
                        audio.At("enemy_growl", en.Root.transform.position, en.Pitch * MachineJitter(), 0.9f);
                    }

                    // Hurt flinch + bark on a hull drop (the player's hit landed).
                    if (en.PrevHull >= 0f && e.Hull < en.PrevHull - 0.25f)
                    {
                        en.FlinchUntil = Game.WorldTime + 0.25f;
                        audio.At("enemy_hurt", en.Root.transform.position, en.Pitch * MachineJitter(), 0.9f);
                    }

                    // Hostile attack (throttled). Hovering drones snipe with a red laser from afar; ground
                    // robots only claw in melee range. Suppressed entirely once the player is aboard the ship —
                    // they've broken off pursuit, so no laser bolts or claw swipes follow them inside.
                    if (e.Hostile && !playerAboard && Game.WorldTime >= en.NextAttack)
                    {
                        if (en.IsDrone)
                        {
                            if (toPlayer.sqrMagnitude < DroneFireRange * DroneFireRange && CanSeePlayer(scenePos))
                            {
                                en.NextAttack = Game.WorldTime + Random.Range(0.7f, 1.3f);
                                en.AttackUntil = Game.WorldTime + 0.18f; // brief charge/recoil tic
                                FireDroneLaser(en, audio);
                            }
                        }
                        else if (en.IsGunner)
                        {
                            // Bandit gunner: tracer shots at aura range (cosmetic — the server aura damages).
                            if (toPlayer.sqrMagnitude < BanditGunFireRange * BanditGunFireRange && CanSeePlayer(scenePos))
                            {
                                en.NextAttack = Game.WorldTime + Random.Range(0.8f, 1.5f);
                                en.AttackUntil = Game.WorldTime + 0.2f;
                                FireDroneLaser(en, audio);
                            }
                        }
                        else if (toPlayer.sqrMagnitude < 7f)
                        {
                            en.NextAttack = Game.WorldTime + Random.Range(1.4f, 2.8f);
                            en.AttackUntil = Game.WorldTime + 0.35f;
                            audio.At("enemy_attack", en.Root.transform.position, en.Pitch * MachineJitter());
                        }
                    }
                }

                en.PrevHull = e.Hull;

                // Floating health bar over machines + bandits (#692); also attributes hull drops to the
                // local player's latest shot for the crosshair hit marker. Same fade band as NPC nameplates.
                EnemyHealthBars.Push(Game, cam, e.Id, en.Root.transform.position + Vector3.up * 2.1f,
                    e.Hull, e.HullMax, friendly: false, fadeStart: 18f, fadeEnd: 28f);
            }

            // Remove enemies whose entity is gone (killed / out of range).
            if (_enemies.Count > seen.Count)
            {
                var stale = _staleScratch;
                stale.Clear();
                foreach (var id in _enemies.Keys)
                {
                    if (!seen.Contains(id))
                    {
                        stale.Add(id);
                    }
                }

                foreach (var id in stale)
                {
                    Destroy(_enemies[id].Root);
                    _enemies.Remove(id);
                    EnemyHealthBars.Forget(id);
                }
            }
        }

        /// <summary>Death bark at the fallen enemy (the list sync removes the body right after).</summary>
        private void OnDefeated(BlocksBeyondTheStars.Networking.Messages.PlanetEnemyDefeated m)
        {
            if (_enemies.TryGetValue(m.Id, out var en))
            {
                ClientAudio.Instance?.At("enemy_die", en.Root.transform.position, en.Pitch * MachineJitter());
            }
        }

        /// <summary>Whether a ranged attacker at <paramref name="shooter"/> has a clear sight line to the
        /// player's chest (#1004). The server gates its hunt lock AND its damage on line-of-sight, so a
        /// player who ducks into a cave takes no hits — but the ranged fire effect used to key on range
        /// alone, so a drone hovering outside visibly sniped them straight through the rock. Mirrors the
        /// server's sight rule: a non-air block whose definition is missing or <c>Solid</c> occludes, and
        /// so do water/lava; endpoint cells are skipped (the bodies aren't occluders).</summary>
        private bool CanSeePlayer(Vector3 shooter)
        {
            if (Game?.World == null)
            {
                return true; // no world to sample — keep the effect rather than silently muting combat
            }

            Vector3 chest = Game.PlayerPosition + Vector3.up * 0.9f; // where the beam aims
            return SightLine.Clear(IsSightBlockingCell, shooter.x, shooter.y, shooter.z, chest.x, chest.y, chest.z);
        }

        /// <summary>Sight-blocking test for one world cell — the client twin of the server's
        /// <c>IsSightBlockingCell</c>. <c>GetBlock</c> canonicalises seam coordinates and reads unloaded
        /// chunks as air (clear), which is the right lenient default for a cosmetic effect.</summary>
        private bool IsSightBlockingCell(int wx, int wy, int wz)
        {
            var id = Game.World.GetBlock(wx, wy, wz);
            if (id.IsAir)
            {
                return false;
            }

            var def = Game.Content?.BlockById(id);
            return def == null || def.Solid || def.Key is "water" or "lava";
        }

        /// <summary>A ranged attacker's shot: a short laser beam to the player (with a little scatter) plus the
        /// attack zap. The scan-drone fires a red bolt from its sensor eye; a bandit gunner fires a cold blue one
        /// from its blaster muzzle. Cosmetic only — the server's proximity aura applies the actual damage, so this
        /// is a render-side mirror of the space <c>UpdateHostileFire</c> tracers.</summary>
        private void FireDroneLaser(Entry en, ClientAudio audio)
        {
            audio?.At("enemy_attack", en.Root.transform.position, en.Pitch * MachineJitter());

            _weapons ??= FindAnyObjectByType<WeaponFx>();
            if (_weapons == null || Game == null)
            {
                return;
            }

            // Muzzle (world space): the bandit's blaster tip in its weapon hand, or the drone's glowing eye.
            // Aim at the player's chest with slight scatter.
            Vector3 muzzle = en.IsBandit
                ? en.ArmR.TransformPoint(new Vector3(0f, -0.66f, 0.4f))
                : en.Body.TransformPoint(new Vector3(0f, -0.02f, 0.26f));
            Vector3 target = Game.PlayerPosition + Vector3.up * 0.9f
                + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(-0.2f, 0.2f), Random.Range(-0.3f, 0.3f));
            // Human tech reads cold blue; the machines keep the angry red that matches the space hostiles.
            _weapons.Shoot(muzzle, target, en.IsBandit ? BanditEnergyColor : new Color(1f, 0.2f, 0.12f));
        }

        /// <summary>Drives the stalk/attack/flinch pose from movement + state (no Animator — procedural).</summary>
        private void Animate(Entry en, float speed, bool hostile)
        {
            if (en.IsDrone)
            {
                // Hovering scan-drone: a gentle bob + a slow scanning yaw; no limbs to pose.
                float pitch = Mathf.Sin((Game.WorldTime + en.Seed) * 2f) * 4f;
                if (Game.WorldTime < en.AttackUntil)
                {
                    pitch -= 12f; // a brief nose-up recoil kick when it fires
                }

                en.Body.localRotation = Quaternion.Euler(pitch, (Game.WorldTime * 50f) % 360f, 0f);
                return;
            }

            float moving = Mathf.Clamp01(speed / 2.5f);
            en.WalkPhase += Game.WorldDeltaTime * (3f + speed * 1.6f);
            float t = Game.WorldTime + en.Seed;

            float swing = Mathf.Sin(en.WalkPhase) * Mathf.Lerp(4f, 38f, moving);
            float armL = -swing, armR = swing, legL = swing, legR = -swing;
            // Bandits walk upright like a person; the machines stalk hunched.
            float bodyPitch = en.IsBandit ? Mathf.Lerp(1f, 6f, moving) : Mathf.Lerp(10f, 18f, moving);
            float headYaw = moving < 0.05f ? Mathf.Sin(t * 0.6f) * 18f : 0f; // slow menacing look-around

            if (hostile)
            {
                if (en.IsBandit)
                {
                    // Weapon arm up: the gunner levels its blaster, the blade bandit brandishes.
                    armR = en.IsGunner ? -78f : Mathf.Min(armR, -40f);
                }
                else
                {
                    // Arms raised, claws forward when hunting.
                    armL = Mathf.Min(armL, -28f) - 14f;
                    armR = Mathf.Min(armR, -28f) - 14f;
                }
            }

            if (Game.WorldTime < en.AttackUntil)
            {
                // Claw swipe: the right arm whips from raised to a downward slash.
                float k = 1f - (en.AttackUntil - Game.WorldTime) / 0.35f;
                armR = Mathf.Lerp(-130f, 45f, Mathf.SmoothStep(0f, 1f, k));
                bodyPitch += 8f * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI); // lunge into the swipe
            }

            if (Game.WorldTime < en.FlinchUntil)
            {
                bodyPitch -= 14f; // recoil back when hit
            }

            en.ArmL.localRotation = Quaternion.Euler(armL, 0f, -12f);
            en.ArmR.localRotation = Quaternion.Euler(armR, 0f, 12f);
            en.LegL.localRotation = Quaternion.Euler(legL, 0f, 0f);
            en.LegR.localRotation = Quaternion.Euler(legR, 0f, 0f);
            en.Body.localRotation = Quaternion.Euler(bodyPitch, 0f, 0f);
            en.Head.localRotation = Quaternion.Euler(-bodyPitch * 0.6f, headYaw, 0f); // head counteracts the hunch
        }

        /// <summary>Builds the blocky alien fiend: hunched textured torso, horned head with glowing eyes,
        /// clawed arms and digitigrade legs hanging from animation pivots. Per-enemy size/pitch variation.</summary>
        private Entry Build(string id)
        {
            EnsureMaterials();
            int h = Hash(id);
            // Per-individual size (a "bell" ±30% from the id, matching the fauna variance) so a pack reads as
            // a mix of runts and big ones, most near the normal size.
            uint uh = (uint)h;
            float sa = (uh & 0xFF) / 255f, sb = ((uh >> 8) & 0xFF) / 255f;
            float size = 1f + ((sa + sb) * 0.5f - 0.5f) * 2f * 0.30f; // ~0.7..1.3, centred 1.0
            var en = new Entry
            {
                Seed = (h & 0x3ff) * 0.137f,
                Pitch = 0.85f + ((h >> 5) % 17) / 17f * 0.35f,    // 0.85..1.2 voice variation
            };

            var root = new GameObject("Enemy");
            root.transform.SetParent(transform, true); // under the game root → not leaked into menus/editors
            root.transform.localScale = Vector3.one * size;
            en.Root = root;

            // Body pivot at the hip so the hunch pitches the whole torso.
            en.Body = Pivot(root.transform, new Vector3(0f, 0.95f, 0f));
            Cube(en.Body, "Torso", new Vector3(0f, 0.32f, 0f), new Vector3(0.62f, 0.7f, 0.42f), _hideMat);
            Cube(en.Body, "Pelvis", new Vector3(0f, -0.06f, 0f), new Vector3(0.5f, 0.24f, 0.36f), _hideDarkMat);
            // Dorsal spikes along the back.
            Cube(en.Body, "Spike1", new Vector3(0f, 0.62f, -0.18f), new Vector3(0.1f, 0.22f, 0.1f), _clawMat);
            Cube(en.Body, "Spike2", new Vector3(0f, 0.44f, -0.24f), new Vector3(0.08f, 0.18f, 0.08f), _clawMat);

            // Head on the body so it hunches with it; horns + a row of glowing eyes.
            en.Head = Pivot(en.Body, new Vector3(0f, 0.78f, 0.08f));
            Cube(en.Head, "Skull", new Vector3(0f, 0.1f, 0f), new Vector3(0.4f, 0.34f, 0.4f), _hideMat);
            Cube(en.Head, "Jaw", new Vector3(0f, -0.06f, 0.12f), new Vector3(0.3f, 0.12f, 0.24f), _hideDarkMat);
            Cube(en.Head, "HornL", new Vector3(-0.16f, 0.3f, -0.04f), new Vector3(0.08f, 0.22f, 0.08f), _clawMat);
            Cube(en.Head, "HornR", new Vector3(0.16f, 0.3f, -0.04f), new Vector3(0.08f, 0.22f, 0.08f), _clawMat);
            Cube(en.Head, "EyeL", new Vector3(-0.1f, 0.12f, 0.2f), new Vector3(0.09f, 0.06f, 0.03f), _eyeMat);
            Cube(en.Head, "EyeR", new Vector3(0.1f, 0.12f, 0.2f), new Vector3(0.09f, 0.06f, 0.03f), _eyeMat);
            Cube(en.Head, "EyeC", new Vector3(0f, 0.2f, 0.2f), new Vector3(0.06f, 0.05f, 0.03f), _eyeMat);

            // Clawed arms from shoulder pivots on the body.
            en.ArmL = Pivot(en.Body, new Vector3(-0.4f, 0.55f, 0f));
            Cube(en.ArmL, "ArmLMesh", new Vector3(0f, -0.3f, 0f), new Vector3(0.16f, 0.62f, 0.16f), _hideMat);
            Cube(en.ArmL, "ClawL", new Vector3(0f, -0.66f, 0.06f), new Vector3(0.14f, 0.16f, 0.22f), _clawMat);
            en.ArmR = Pivot(en.Body, new Vector3(0.4f, 0.55f, 0f));
            Cube(en.ArmR, "ArmRMesh", new Vector3(0f, -0.3f, 0f), new Vector3(0.16f, 0.62f, 0.16f), _hideMat);
            Cube(en.ArmR, "ClawR", new Vector3(0f, -0.66f, 0.06f), new Vector3(0.14f, 0.16f, 0.22f), _clawMat);

            // Digitigrade legs from hip pivots on the root (they carry the body).
            en.LegL = Pivot(root.transform, new Vector3(-0.18f, 0.95f, 0f));
            Cube(en.LegL, "ThighL", new Vector3(0f, -0.28f, 0.04f), new Vector3(0.2f, 0.5f, 0.22f), _hideMat);
            Cube(en.LegL, "ShinL", new Vector3(0f, -0.68f, -0.06f), new Vector3(0.16f, 0.4f, 0.16f), _hideDarkMat);
            Cube(en.LegL, "FootL", new Vector3(0f, -0.9f, 0.08f), new Vector3(0.2f, 0.1f, 0.3f), _clawMat);
            en.LegR = Pivot(root.transform, new Vector3(0.18f, 0.95f, 0f));
            Cube(en.LegR, "ThighR", new Vector3(0f, -0.28f, 0.04f), new Vector3(0.2f, 0.5f, 0.22f), _hideMat);
            Cube(en.LegR, "ShinR", new Vector3(0f, -0.68f, -0.06f), new Vector3(0.16f, 0.4f, 0.16f), _hideDarkMat);
            Cube(en.LegR, "FootR", new Vector3(0f, -0.9f, 0.08f), new Vector3(0.2f, 0.1f, 0.3f), _clawMat);

            return en;
        }

        /// <summary>Builds the flying scan-drone (P4): a small dark hovering pod with a single glowing RED
        /// scanner eye and three sensor fins — the ground counterpart of the space UFO. Dummy limb pivots keep
        /// the shared <see cref="Animate"/> null-safe (drones skip limb posing).</summary>
        private Entry BuildDrone(string id)
        {
            EnsureMaterials();
            int h = Hash(id);
            float size = 0.7f + ((h & 0xFF) / 255f) * 0.2f; // 0.7..0.9 — smaller than the ground robot
            var en = new Entry
            {
                Seed = (h & 0x3ff) * 0.137f,
                Pitch = 1.1f + ((h >> 5) % 17) / 17f * 0.3f,
                IsDrone = true,
            };

            var root = new GameObject("ScanDrone");
            root.transform.SetParent(transform, true);
            root.transform.localScale = Vector3.one * size;
            en.Root = root;

            en.Body = Pivot(root.transform, new Vector3(0f, 0.5f, 0f));
            Cube(en.Body, "Pod", new Vector3(0f, 0f, 0f), new Vector3(0.5f, 0.34f, 0.5f), _hideMat);
            Cube(en.Body, "Underside", new Vector3(0f, -0.18f, 0f), new Vector3(0.3f, 0.12f, 0.3f), _hideDarkMat);
            Cube(en.Body, "Eye", new Vector3(0f, -0.02f, 0.26f), new Vector3(0.16f, 0.1f, 0.06f), _eyeMat); // red scanner
            Cube(en.Body, "FinL", new Vector3(-0.34f, 0.04f, 0f), new Vector3(0.2f, 0.05f, 0.16f), _clawMat);
            Cube(en.Body, "FinR", new Vector3(0.34f, 0.04f, 0f), new Vector3(0.2f, 0.05f, 0.16f), _clawMat);
            Cube(en.Body, "FinB", new Vector3(0f, 0.04f, -0.34f), new Vector3(0.16f, 0.05f, 0.2f), _clawMat);

            en.Head = en.Body; // the eye sits on the body
            // Dummy limb pivots so the shared Animate() never null-refs (drones skip limb posing).
            en.ArmL = Pivot(root.transform, Vector3.zero);
            en.ArmR = Pivot(root.transform, Vector3.zero);
            en.LegL = Pivot(root.transform, Vector3.zero);
            en.LegR = Pivot(root.transform, Vector3.zero);
            return en;
        }

        /// <summary>Builds a humanoid bandit: a blocky PERSON — a face with eye whites, pupils and a brow,
        /// hair, a cloth mask pulled over nose and mouth, a worn jacket and dark trousers — plus a hand weapon
        /// (an energy blade for the melee variant, a snub blaster for the gunner). Skin, hair, jacket and mask
        /// tones all vary per bandit so a camp reads as a group of different people rather than one species.
        /// Deliberately NO red on the head and no <see cref="_eyeMat"/> anywhere: that glowing red belongs to
        /// the Guardian machines, and a red band across the head reads as glowing eyes at distance (#601).
        /// Uses the same pivot skeleton as the robot so <see cref="Animate"/> drives it unchanged, just upright.</summary>
        private Entry BuildBandit(string id, bool gunner)
        {
            EnsureMaterials();
            EnsureBanditMaterials();
            int h = Hash(id);
            float size = 0.95f + ((h & 0xFF) / 255f) * 0.1f; // people vary a little, not like fauna
            var en = new Entry
            {
                Seed = (h & 0x3ff) * 0.137f,
                Pitch = 0.9f + ((h >> 5) % 17) / 17f * 0.25f,
                IsBandit = true,
                IsGunner = gunner,
            };

            var root = new GameObject(gunner ? "BanditGunner" : "Bandit");
            root.transform.SetParent(transform, true);
            root.transform.localScale = Vector3.one * size;
            en.Root = root;

            // Jacket, skin, hair and mask tones all vary per bandit (muted, scruffy tones) — these are people,
            // not a species, so no two robbers in a camp should look stamped from the same mould. The tinted
            // materials come from a shared cache: one per palette entry, not one per spawned bandit.
            var jacket = BanditTint(BanditPalette.Jacket, (h >> 12) % 4);
            var skin = BanditTint(BanditPalette.Skin, (h >> 16) % 5);
            var hair = BanditTint(BanditPalette.Hair, (h >> 20) % 4);
            var mask = BanditTint(BanditPalette.Mask, (h >> 8) % 4);

            en.Body = Pivot(root.transform, new Vector3(0f, 0.95f, 0f));
            Cube(en.Body, "Torso", new Vector3(0f, 0.34f, 0f), new Vector3(0.52f, 0.66f, 0.3f), jacket);
            Cube(en.Body, "Belt", new Vector3(0f, -0.02f, 0f), new Vector3(0.46f, 0.12f, 0.28f), _banditDarkMat);

            // The head. The skull is a 0.34 cube centred at y 0.12, so it spans y -0.05…0.29 and its FRONT face
            // sits at head-local z 0.17 — every face feature has to protrude past that or it is buried inside an
            // opaque cube (the same trap the player avatar's face hit). Layout top-down: hair cap, brow, eyes
            // (white + pupil), then the cloth mask wrapping the lower face over nose and mouth.
            en.Head = Pivot(en.Body, new Vector3(0f, 0.78f, 0f));
            Cube(en.Head, "Skull", new Vector3(0f, 0.12f, 0f), new Vector3(0.34f, 0.34f, 0.34f), skin);
            Cube(en.Head, "Hair", new Vector3(0f, 0.265f, 0f), new Vector3(0.35f, 0.09f, 0.35f), hair);
            Cube(en.Head, "Brow", new Vector3(0f, 0.205f, 0.175f), new Vector3(0.25f, 0.03f, 0.03f), hair);
            Cube(en.Head, "EyeL", new Vector3(-0.075f, 0.15f, 0.175f), new Vector3(0.085f, 0.06f, 0.03f), _banditEyeMat);
            Cube(en.Head, "EyeR", new Vector3(0.075f, 0.15f, 0.175f), new Vector3(0.085f, 0.06f, 0.03f), _banditEyeMat);
            Cube(en.Head, "PupilL", new Vector3(-0.075f, 0.145f, 0.195f), new Vector3(0.04f, 0.045f, 0.02f), _banditDarkMat);
            Cube(en.Head, "PupilR", new Vector3(0.075f, 0.145f, 0.195f), new Vector3(0.04f, 0.045f, 0.02f), _banditDarkMat);
            // Mask: a cloth strip pulled up over nose and mouth — the "this one is a robber" cue that used to be
            // a red headband. It wraps the whole lower head (slightly proud of the skull) and clears the eyes.
            Cube(en.Head, "Mask", new Vector3(0f, 0.035f, 0f), new Vector3(0.355f, 0.13f, 0.355f), mask);

            en.ArmL = Pivot(en.Body, new Vector3(-0.34f, 0.58f, 0f));
            Cube(en.ArmL, "ArmLMesh", new Vector3(0f, -0.28f, 0f), new Vector3(0.15f, 0.56f, 0.15f), jacket);
            Cube(en.ArmL, "HandL", new Vector3(0f, -0.6f, 0f), new Vector3(0.14f, 0.12f, 0.14f), skin);
            en.ArmR = Pivot(en.Body, new Vector3(0.34f, 0.58f, 0f));
            Cube(en.ArmR, "ArmRMesh", new Vector3(0f, -0.28f, 0f), new Vector3(0.15f, 0.56f, 0.15f), jacket);
            Cube(en.ArmR, "HandR", new Vector3(0f, -0.6f, 0f), new Vector3(0.14f, 0.12f, 0.14f), skin);

            // The hand weapon: a snub blaster (gunner) or an energy blade (melee). Both glow COLD BLUE — bought
            // human tech, deliberately not the Guardian machines' red sensor glow.
            if (gunner)
            {
                Cube(en.ArmR, "Gun", new Vector3(0f, -0.66f, 0.16f), new Vector3(0.1f, 0.12f, 0.34f), _banditDarkMat);
                Cube(en.ArmR, "GunTip", new Vector3(0f, -0.66f, 0.36f), new Vector3(0.06f, 0.06f, 0.08f), _banditEnergyMat);
            }
            else
            {
                Cube(en.ArmR, "Hilt", new Vector3(0f, -0.68f, 0.1f), new Vector3(0.08f, 0.1f, 0.14f), _banditDarkMat);
                Cube(en.ArmR, "Blade", new Vector3(0f, -0.68f, 0.36f), new Vector3(0.05f, 0.05f, 0.42f), _banditEnergyMat);
            }

            en.LegL = Pivot(root.transform, new Vector3(-0.14f, 0.95f, 0f));
            Cube(en.LegL, "LegLMesh", new Vector3(0f, -0.48f, 0f), new Vector3(0.18f, 0.9f, 0.2f), _banditDarkMat);
            en.LegR = Pivot(root.transform, new Vector3(0.14f, 0.95f, 0f));
            Cube(en.LegR, "LegRMesh", new Vector3(0f, -0.48f, 0f), new Vector3(0.18f, 0.9f, 0.2f), _banditDarkMat);

            return en;
        }

        /// <summary>Which per-bandit palette a tint comes from (see <see cref="BanditColor"/>).</summary>
        private enum BanditPalette
        {
            Jacket,
            Skin,
            Hair,
            Mask,
        }

        /// <summary>One entry of a per-bandit palette. Muted, scruffy, and — for anything on the head —
        /// deliberately never red: glowing red belongs to the Guardian machines (#601).</summary>
        private static Color BanditColor(BanditPalette palette, int variant) => palette switch
        {
            BanditPalette.Jacket => variant switch
            {
                0 => new Color(0.32f, 0.24f, 0.16f), // worn leather brown
                1 => new Color(0.22f, 0.26f, 0.22f), // faded olive
                2 => new Color(0.26f, 0.22f, 0.28f), // dusty plum
                _ => new Color(0.24f, 0.24f, 0.27f), // grey slate
            },
            // Skin: a spread of human tones so a camp isn't one cloned face.
            BanditPalette.Skin => variant switch
            {
                0 => new Color(0.86f, 0.71f, 0.58f), // light
                1 => new Color(0.78f, 0.60f, 0.45f), // tan (the previous single tone)
                2 => new Color(0.64f, 0.47f, 0.34f), // brown
                3 => new Color(0.48f, 0.34f, 0.24f), // deep brown
                _ => new Color(0.35f, 0.24f, 0.18f), // dark
            },
            // Hair (also used for the brow) — the cap that took the silhouette mass from the old headband.
            BanditPalette.Hair => variant switch
            {
                0 => new Color(0.09f, 0.08f, 0.07f), // black
                1 => new Color(0.24f, 0.16f, 0.10f), // dark brown
                2 => new Color(0.42f, 0.30f, 0.16f), // dirty blond
                _ => new Color(0.40f, 0.39f, 0.38f), // grey / weathered
            },
            // The face mask's cloth — muted scavenged fabric.
            _ => variant switch
            {
                0 => new Color(0.30f, 0.28f, 0.25f), // dusty canvas
                1 => new Color(0.22f, 0.27f, 0.26f), // faded teal-grey
                2 => new Color(0.26f, 0.24f, 0.30f), // washed indigo
                _ => new Color(0.19f, 0.19f, 0.20f), // charcoal
            },
        };

        /// <summary>A tinted material for one palette entry, created once and reused. Bandits spawn and despawn
        /// over a session and <c>new Material</c> instances are not destroyed with their GameObject, so caching
        /// keeps the total bounded at one material per palette entry instead of four per bandit.</summary>
        private static Material BanditTint(BanditPalette palette, int variant)
        {
            int key = ((int)palette * 16) + variant;
            if (!_banditTints.TryGetValue(key, out var mat) || mat == null)
            {
                var template = palette == BanditPalette.Skin ? _banditSkinMat : _banditClothMat;
                mat = new Material(template) { color = ShaderColor.Srgb(BanditColor(palette, variant)) };
                _banditTints[key] = mat;
            }

            return mat;
        }

        private static readonly Dictionary<int, Material> _banditTints = new();

        private static Material _banditSkinMat, _banditClothMat, _banditDarkMat, _banditEyeMat, _banditEnergyMat;

        private static void EnsureBanditMaterials()
        {
            if (_banditSkinMat != null)
            {
                return;
            }

            var lit = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var unlit = Shader.Find("Unlit/Color") ?? lit;
            // Skin and cloth are TEMPLATES — every bandit gets its own tinted instance (see BuildBandit),
            // which copies the _Floor/_Fill lift along with the rest of the material.
            _banditSkinMat = WithFill(new Material(lit) { color = ShaderColor.Srgb(new Color(0.78f, 0.6f, 0.45f)) });
            _banditClothMat = WithFill(new Material(lit) { color = ShaderColor.Srgb(new Color(0.3f, 0.25f, 0.2f)) });
            _banditDarkMat = WithFill(new Material(lit) { color = ShaderColor.Srgb(new Color(0.14f, 0.13f, 0.14f)) });
            _banditEyeMat = WithFill(new Material(lit) { color = ShaderColor.Srgb(new Color(0.95f, 0.96f, 0.98f)) }); // eye whites — LIT, so they never glow
            _banditEnergyMat = new Material(unlit) { color = ShaderColor.Srgb(BanditEnergyColor) };                   // cold blue weapon glow (bloom picks it up)
        }

        /// <summary>The bandits' weapon/tracer glow: cold blue human tech, kept clearly apart from the Guardian
        /// machines' red sensor glow (<see cref="_eyeMat"/>).</summary>
        private static readonly Color BanditEnergyColor = new(0.30f, 0.68f, 1f);

        // LitColor's shader DEFAULTS are _Floor 0.35 / _Fill 0 — every other humanoid/creature material in
        // the game runs at 0.62 / 0.3 (see PlayerAvatar.AvatarFloor); without this lift, bandits and
        // Guardians rendered ~2× darker than everyone else on their shadow side (#711).
        private const float EntityFloor = 0.62f;
        private const float EntityFill = 0.3f;

        /// <summary>Applies the shared ambient floor + fill to a LitColor material (no-op on the Unlit fallback).</summary>
        private static Material WithFill(Material m)
        {
            if (m.HasProperty("_Floor"))
            {
                m.SetFloat("_Floor", EntityFloor);
                m.SetFloat("_Fill", EntityFill);
            }

            return m;
        }

        private static Transform Pivot(Transform parent, Vector3 localPos)
        {
            var t = new GameObject("Pivot").transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        private static void Cube(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col); // visual only — the server owns combat/positions
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        /// <summary>Shared materials for the black Guardian robot (story retheme): dark metal plating (lit +
        /// tinted, casts shadows via LitColor's URP pass), darker joints, mid-grey metal trim (the limbs,
        /// antennae + feet that used to be bone/horn), and unlit glowing RED sensor "eyes" — the three red
        /// lights the settlers fear. An optional <c>enemy_robot</c> plating tile is used if present.</summary>
        private static void EnsureMaterials()
        {
            if (_hideMat != null)
            {
                return;
            }

            var lit = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var unlit = Shader.Find("Unlit/Color") ?? lit;
            var plateTex = LoadTex("enemy_robot"); // optional metal-plating tile (flat dark if absent)
            _hideMat = WithFill(new Material(lit) { color = ShaderColor.Srgb(new Color(0.13f, 0.14f, 0.16f)) });      // dark plating
            _hideDarkMat = WithFill(new Material(lit) { color = ShaderColor.Srgb(new Color(0.08f, 0.085f, 0.10f)) }); // darker joints
            if (plateTex != null)
            {
                _hideMat.mainTexture = plateTex;
                _hideDarkMat.mainTexture = plateTex;
            }

            _clawMat = WithFill(new Material(lit) { color = ShaderColor.Srgb(new Color(0.34f, 0.36f, 0.40f)) }); // metal trim / antennae / feet
            _eyeMat = new Material(unlit) { color = ShaderColor.Srgb(new Color(1f, 0.18f, 0.14f)) };             // glowing red sensors (bloom picks it up)
        }

        private static Texture2D LoadTex(string key)
        {
            var asset = Resources.Load<TextAsset>("textures/" + key);
            if (asset == null || asset.bytes.Length != 64 * 64 * 4)
            {
                return null;
            }

            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            tex.LoadRawTextureData(asset.bytes);
            tex.Apply();
            return tex;
        }

        private static int Hash(string id)
        {
            int h = 0;
            foreach (char c in id ?? string.Empty)
            {
                h = h * 31 + c;
            }

            return h & 0x7fffffff;
        }

        /// <summary>Per-utterance pitch jitter (#876) — subtler than the fauna's (machines are more
        /// uniform), but enough that repeated growls/zaps stop sounding bit-identical.</summary>
        private static float MachineJitter() => Random.Range(0.95f, 1.05f);

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.PlanetEnemyDefeated -= OnDefeated;
            }
        }
    }
}
