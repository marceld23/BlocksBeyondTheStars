using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Renders planet enemies (M25) as procedural blocky alien fiends at the positions the server reports
    /// (<c>GameBootstrap.PlanetEnemies</c>) — a hunched chitinous body with clawed arms, digitigrade legs, a
    /// horned head and glowing eyes, skinned with the AI-generated <c>enemy_hide</c> tile. Self-animated like
    /// <see cref="PlayerAvatar"/>/creatures: a speed-driven stalk cycle, an idle menace-sway, a claw-swipe
    /// attack lunge when hostile and close to the player, a hurt flinch on hull drops, and growl/attack/hurt/
    /// die vocals (ElevenLabs). The server stays authoritative over spawns/positions/deaths; the player
    /// attacks with F (PlayerController).
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
        }

        private readonly Dictionary<string, Entry> _enemies = new();
        private bool _subscribed;
        private static Material _hideMat, _hideDarkMat, _eyeMat, _clawMat;

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
                    en = Build(e.Id);
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
                bool nearPlayer = toPlayer.sqrMagnitude < 64f;
                Vector3 face = e.Hostile && nearPlayer ? toPlayer : vel;
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

                    // Claw-swipe attack when hostile and in melee range of the player (throttled).
                    if (e.Hostile && Time.time >= en.NextAttack && toPlayer.sqrMagnitude < 7f)
                    {
                        en.NextAttack = Time.time + Random.Range(1.4f, 2.8f);
                        en.AttackUntil = Time.time + 0.35f;
                        audio.At("enemy_attack", en.Root.transform.position, en.Pitch);
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

        /// <summary>Drives the stalk/attack/flinch pose from movement + state (no Animator — procedural).</summary>
        private void Animate(Entry en, float speed, bool hostile)
        {
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
            float size = 0.92f + (h % 23) / 23f * 0.28f;          // 0.92..1.2 — slight size variation
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

        /// <summary>Shared enemy materials: the AI-generated chitin hide tile (lit + tinted, casts shadows via
        /// LitColor's URP pass), a darker joint variant, bony claws/horns, and unlit glowing eyes.</summary>
        private static void EnsureMaterials()
        {
            if (_hideMat != null)
            {
                return;
            }

            var lit = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var unlit = Shader.Find("Unlit/Color") ?? lit;
            var hideTex = LoadTex("enemy_hide");
            _hideMat = new Material(lit) { color = new Color(0.72f, 0.2f, 0.16f) };
            _hideDarkMat = new Material(lit) { color = new Color(0.4f, 0.12f, 0.1f) };
            if (hideTex != null)
            {
                _hideMat.mainTexture = hideTex;
                _hideDarkMat.mainTexture = hideTex;
            }

            _clawMat = new Material(lit) { color = new Color(0.85f, 0.8f, 0.66f) };  // bone
            _eyeMat = new Material(unlit) { color = new Color(1f, 0.85f, 0.2f) };    // glowing amber (bloom picks it up)
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
