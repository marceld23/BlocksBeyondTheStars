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
        }

        private readonly Dictionary<string, Entry> _enemies = new();
        private bool _subscribed;
        private WeaponFx _weapons; // shared VFX layer (laser beams), resolved lazily
        private static Material _hideMat, _hideDarkMat, _eyeMat, _clawMat;

        /// <summary>Range at which a hovering scan-drone opens fire on the player (blocks). Ground robots have
        /// no ranged attack — they only claw in melee.</summary>
        private const float DroneFireRange = 16f;

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

            var seen = new HashSet<string>();
            foreach (var e in Game.PlanetEnemies)
            {
                seen.Add(e.Id);
                if (!_enemies.TryGetValue(e.Id, out var en))
                {
                    en = e.Kind == "ScanDrone" ? BuildDrone(e.Id) : Build(e.Id);
                    en.Target = en.Settled = new Vector3(e.X, e.Y, e.Z);
                    _enemies[e.Id] = en;
                }

                en.Target = new Vector3(e.X, e.Y, e.Z);
                en.Settled = Vector3.Lerp(en.Settled, en.Target, Time.deltaTime * 8f);

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
                Vector3 face = e.Hostile && nearPlayer && !playerAboard ? toPlayer : vel;
                if (face.sqrMagnitude > 0.01f)
                {
                    en.Root.transform.rotation = Quaternion.Slerp(
                        en.Root.transform.rotation, Quaternion.LookRotation(face.normalized), Time.deltaTime * 6f);
                }

                en.Root.transform.position = scenePos;
                Animate(en, vel.magnitude / Mathf.Max(Time.deltaTime, 1e-4f), e.Hostile);

                var audio = ClientAudio.Instance;
                if (audio != null)
                {
                    // Periodic menacing growl, spatialised at the enemy.
                    if (Time.time >= en.NextGrowl)
                    {
                        en.NextGrowl = Time.time + Random.Range(6f, 14f);
                        audio.At("enemy_growl", en.Root.transform.position, en.Pitch, 0.9f);
                    }

                    // Hurt flinch + bark on a hull drop (the player's hit landed).
                    if (en.PrevHull >= 0f && e.Hull < en.PrevHull - 0.25f)
                    {
                        en.FlinchUntil = Time.time + 0.25f;
                        audio.At("enemy_hurt", en.Root.transform.position, en.Pitch, 0.9f);
                    }

                    // Hostile attack (throttled). Hovering drones snipe with a red laser from afar; ground
                    // robots only claw in melee range. Suppressed entirely once the player is aboard the ship —
                    // they've broken off pursuit, so no laser bolts or claw swipes follow them inside.
                    if (e.Hostile && !playerAboard && Time.time >= en.NextAttack)
                    {
                        if (en.IsDrone)
                        {
                            if (toPlayer.sqrMagnitude < DroneFireRange * DroneFireRange)
                            {
                                en.NextAttack = Time.time + Random.Range(0.7f, 1.3f);
                                en.AttackUntil = Time.time + 0.18f; // brief charge/recoil tic
                                FireDroneLaser(en, audio);
                            }
                        }
                        else if (toPlayer.sqrMagnitude < 7f)
                        {
                            en.NextAttack = Time.time + Random.Range(1.4f, 2.8f);
                            en.AttackUntil = Time.time + 0.35f;
                            audio.At("enemy_attack", en.Root.transform.position, en.Pitch);
                        }
                    }
                }

                en.PrevHull = e.Hull;
            }

            // Remove enemies whose entity is gone (killed / out of range).
            if (_enemies.Count > seen.Count)
            {
                var stale = new List<string>();
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
                }
            }
        }

        /// <summary>Death bark at the fallen enemy (the list sync removes the body right after).</summary>
        private void OnDefeated(BlocksBeyondTheStars.Networking.Messages.PlanetEnemyDefeated m)
        {
            if (_enemies.TryGetValue(m.Id, out var en))
            {
                ClientAudio.Instance?.At("enemy_die", en.Root.transform.position, en.Pitch);
            }
        }

        /// <summary>The hovering scan-drone's ranged attack: a short red laser beam from its eye to the player
        /// (with a little scatter) plus the attack zap. Cosmetic only — the server's proximity aura applies the
        /// actual damage, so this is a render-side mirror of the space <c>UpdateHostileFire</c> tracers.</summary>
        private void FireDroneLaser(Entry en, ClientAudio audio)
        {
            audio?.At("enemy_attack", en.Root.transform.position, en.Pitch);

            _weapons ??= FindObjectOfType<WeaponFx>();
            if (_weapons == null || Game == null)
            {
                return;
            }

            // Muzzle = the drone's glowing red eye (world space); aim at the player's chest with slight scatter.
            Vector3 muzzle = en.Body.TransformPoint(new Vector3(0f, -0.02f, 0.26f));
            Vector3 target = Game.PlayerPosition + Vector3.up * 0.9f
                + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(-0.2f, 0.2f), Random.Range(-0.3f, 0.3f));
            _weapons.Shoot(muzzle, target, new Color(1f, 0.2f, 0.12f)); // angry red bolt (matches space hostiles)
        }

        /// <summary>Drives the stalk/attack/flinch pose from movement + state (no Animator — procedural).</summary>
        private void Animate(Entry en, float speed, bool hostile)
        {
            if (en.IsDrone)
            {
                // Hovering scan-drone: a gentle bob + a slow scanning yaw; no limbs to pose.
                float pitch = Mathf.Sin((Time.time + en.Seed) * 2f) * 4f;
                if (Time.time < en.AttackUntil)
                {
                    pitch -= 12f; // a brief nose-up recoil kick when it fires
                }

                en.Body.localRotation = Quaternion.Euler(pitch, (Time.time * 50f) % 360f, 0f);
                return;
            }

            float moving = Mathf.Clamp01(speed / 2.5f);
            en.WalkPhase += Time.deltaTime * (3f + speed * 1.6f);
            float t = Time.time + en.Seed;

            float swing = Mathf.Sin(en.WalkPhase) * Mathf.Lerp(4f, 38f, moving);
            float armL = -swing, armR = swing, legL = swing, legR = -swing;
            float bodyPitch = Mathf.Lerp(10f, 18f, moving); // hunched stalk, deeper while moving
            float headYaw = moving < 0.05f ? Mathf.Sin(t * 0.6f) * 18f : 0f; // slow menacing look-around

            if (hostile)
            {
                // Arms raised, claws forward when hunting.
                armL = Mathf.Min(armL, -28f) - 14f;
                armR = Mathf.Min(armR, -28f) - 14f;
            }

            if (Time.time < en.AttackUntil)
            {
                // Claw swipe: the right arm whips from raised to a downward slash.
                float k = 1f - (en.AttackUntil - Time.time) / 0.35f;
                armR = Mathf.Lerp(-130f, 45f, Mathf.SmoothStep(0f, 1f, k));
                bodyPitch += 8f * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI); // lunge into the swipe
            }

            if (Time.time < en.FlinchUntil)
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
            _hideMat = new Material(lit) { color = ShaderColor.Srgb(new Color(0.13f, 0.14f, 0.16f)) };      // dark plating
            _hideDarkMat = new Material(lit) { color = ShaderColor.Srgb(new Color(0.08f, 0.085f, 0.10f)) }; // darker joints
            if (plateTex != null)
            {
                _hideMat.mainTexture = plateTex;
                _hideDarkMat.mainTexture = plateTex;
            }

            _clawMat = new Material(lit) { color = ShaderColor.Srgb(new Color(0.34f, 0.36f, 0.40f)) }; // metal trim / antennae / feet
            _eyeMat = new Material(unlit) { color = ShaderColor.Srgb(new Color(1f, 0.18f, 0.14f)) };   // glowing red sensors (bloom picks it up)
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

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.PlanetEnemyDefeated -= OnDefeated;
            }
        }
    }
}
