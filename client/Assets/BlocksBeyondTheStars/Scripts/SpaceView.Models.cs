// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;
using static BlocksBeyondTheStars.Client.ModelKit;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Model helpers of the flight scene: the Guardian plating, the energy-field and glow materials, the planet look
    /// table, and the station, drone, UFO, raider and cruiser models. The generic cube and material helpers (lit,
    /// unlit, textured, sun-lit phase, block tiles) live in <see cref="ModelKit"/>, imported with <c>using static</c>.
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>Hull material of the Guardian machines (drone, UFO, cruiser dark blocks): the grey
        /// circuit-plating tile the planet robots wear (#1337/#1338) — tinted near 1 because the tile is authored
        /// at its final greys and <see cref="ModelKit.LoadTex"/> does not brighten — with the same ambient floor/fill the
        /// planet entities got in #711. Before, these hulls used the black <c>carbon</c> coal tile at a dark tint
        /// and LitColor's default 0.35 floor, so the shadow side sank to a black silhouette. Falls back to the
        /// plain <c>iron_wall</c> hull plate at a darker tint if the tile is missing (stripped build).</summary>
        private static Material GuardianPlating(Color tint)
        {
            var tex = LoadTex("enemy_robot");
            return GuardianLift(tex != null
                ? Lit(tint, tex, new Vector2(2f, 2f))
                : Lit(tint * 0.5f, LoadTex("iron_wall"), new Vector2(2f, 2f)));
        }

        /// <summary>The entity ambient floor + fill (<c>WorldEntities.EntityFloor/EntityFill</c>) on a LitColor
        /// material — no-op on the Unlit fallback.</summary>
        private static Material GuardianLift(Material m)
        {
            if (m.HasProperty("_Floor"))
            {
                m.SetFloat("_Floor", 0.62f);
                m.SetFloat("_Fill", 0.3f);
            }

            return m;
        }

        /// <summary>A real station model: an iron hull hub with a glass viewport collar, docking arms with end
        /// pods, solar wings and a beacon mast — textured (iron/carbon/glass), not a flat cube.</summary>
        private GameObject BuildStationModel(Transform parent)
        {
            var root = new GameObject("Station");
            root.transform.SetParent(parent, false);
            var hull = LitTex("iron_wall", new Color(0.82f, 0.84f, 0.88f));
            var dark = LitTex("carbon", new Color(0.72f, 0.72f, 0.76f));
            var glass = LitTex("glass", new Color(0.7f, 0.9f, 1f));
            var panel = Unlit(new Color(0.18f, 0.34f, 0.68f)); // solar cells
            var light = Unlit(new Color(1f, 0.85f, 0.4f));

            Cube("Hub", root.transform, Vector3.zero, new Vector3(4f, 4f, 4f), hull);
            Cube("Collar", root.transform, Vector3.zero, new Vector3(5.6f, 1f, 5.6f), dark);
            Cube("Viewband", root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(4.2f, 1f, 4.2f), glass);
            Cube("ArmX", root.transform, Vector3.zero, new Vector3(11f, 0.8f, 0.8f), dark);
            Cube("ArmZ", root.transform, Vector3.zero, new Vector3(0.8f, 0.8f, 11f), dark);
            Cube("PodXp", root.transform, new Vector3(5f, 0f, 0f), new Vector3(1.8f, 1.8f, 1.8f), hull);
            Cube("PodXn", root.transform, new Vector3(-5f, 0f, 0f), new Vector3(1.8f, 1.8f, 1.8f), hull);
            Cube("PodZp", root.transform, new Vector3(0f, 0f, 5f), new Vector3(1.8f, 1.8f, 1.8f), hull);
            Cube("PodZn", root.transform, new Vector3(0f, 0f, -5f), new Vector3(1.8f, 1.8f, 1.8f), hull);
            Cube("SolarL", root.transform, new Vector3(-7.4f, 0f, 0f), new Vector3(3.2f, 0.15f, 5.2f), panel);
            Cube("SolarR", root.transform, new Vector3(7.4f, 0f, 0f), new Vector3(3.2f, 0.15f, 5.2f), panel);
            Cube("Mast", root.transform, new Vector3(0f, 2.8f, 0f), new Vector3(0.4f, 2.6f, 0.4f), dark);
            Cube("Beacon", root.transform, new Vector3(0f, 4.4f, 0f), new Vector3(0.7f, 0.7f, 0.7f), light);
            root.AddComponent<Spin>().Configure(Vector3.up, 3f); // a slow, stately rotation
            return root;
        }

        /// <summary>A real drone model: an angular grey circuit-plated body with a glowing red sensor eye and
        /// side pods (Guardian plating, #1337), plus an unlit red equatorial threat strip so the grey core
        /// still reads against grey asteroid rock from any side (#1840, parity with the planet-side drone).</summary>
        private GameObject BuildDroneModel(Transform parent)
        {
            var root = new GameObject("Drone");
            root.transform.SetParent(parent, false);
            var body = GuardianPlating(new Color(0.95f, 0.95f, 0.97f));
            var trim = GuardianLift(LitTex("iron_wall", new Color(0.6f, 0.5f, 0.55f)));

            Cube("Core", root.transform, Vector3.zero, new Vector3(1.3f, 0.9f, 1.3f), body);
            Cube("Eye", root.transform, new Vector3(0f, 0f, 0.8f), new Vector3(0.45f, 0.45f, 0.4f), Unlit(new Color(1f, 0.25f, 0.2f)));
            Cube("ThreatStrip", root.transform, Vector3.zero, new Vector3(1.36f, 0.08f, 1.36f), Unlit(new Color(1f, 0.25f, 0.2f))); // #1840: pokes 0.03 out of the core on every side
            Cube("PodL", root.transform, new Vector3(-0.9f, 0f, -0.1f), new Vector3(0.4f, 0.4f, 1f), trim);
            Cube("PodR", root.transform, new Vector3(0.9f, 0f, -0.1f), new Vector3(0.4f, 0.4f, 1f), trim);
            Cube("Fin", root.transform, new Vector3(0f, 0.7f, -0.3f), new Vector3(0.2f, 0.7f, 0.8f), trim);
            root.AddComponent<Spin>().Configure(Vector3.up, 18f); // restless hover spin
            return root;
        }

        /// <summary>A real UFO model: a chunky grey circuit-plated saucer (Guardian plating, #1337) with a hostile
        /// glowing red-orange dome, a deep underside hull and big red threat lights — reads as a clearly visible
        /// enemy, not the old flat, pale-green "peaceful" disc (nor the near-black gunmetal that followed it).</summary>
        private GameObject BuildUfoModel(Transform parent)
        {
            var root = new GameObject("Ufo");
            root.transform.SetParent(parent, false);

            var hull = GuardianPlating(new Color(0.92f, 0.92f, 0.95f)); // dark-grey circuit plating (the tile carries the greys)

            // Upper + lower dishes form a taller, more readable saucer silhouette than the old single flat disc.
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Saucer";
            StripCollider(disc);
            disc.transform.SetParent(root.transform, false);
            disc.transform.localScale = new Vector3(2.9f, 0.42f, 2.9f);
            disc.GetComponent<Renderer>().sharedMaterial = hull;

            var underside = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            underside.name = "Underside";
            StripCollider(underside);
            underside.transform.SetParent(root.transform, false);
            underside.transform.localPosition = new Vector3(0f, -0.32f, 0f);
            underside.transform.localScale = new Vector3(1.7f, 0.3f, 1.7f);
            underside.GetComponent<Renderer>().sharedMaterial = GuardianPlating(new Color(0.55f, 0.55f, 0.58f)); // darker underside, same plating

            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "Dome";
            StripCollider(dome);
            dome.transform.SetParent(root.transform, false);
            dome.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            dome.transform.localScale = new Vector3(1.5f, 1.05f, 1.5f);
            dome.GetComponent<Renderer>().sharedMaterial = Unlit(new Color(1f, 0.35f, 0.12f)); // glowing hostile red-orange

            // Bigger, brighter red threat lights around the rim + a glowing weapon emitter on the underside (where
            // its shots originate).
            var threat = Unlit(new Color(1f, 0.12f, 0.05f));
            Cube("LightF", root.transform, new Vector3(0f, -0.15f, 1.2f), new Vector3(0.5f, 0.3f, 0.5f), threat);
            Cube("LightB", root.transform, new Vector3(0f, -0.15f, -1.2f), new Vector3(0.5f, 0.3f, 0.5f), threat);
            Cube("LightL", root.transform, new Vector3(-1.2f, -0.15f, 0f), new Vector3(0.5f, 0.3f, 0.5f), threat);
            Cube("LightR", root.transform, new Vector3(1.2f, -0.15f, 0f), new Vector3(0.5f, 0.3f, 0.5f), threat);
            Cube("Emitter", root.transform, new Vector3(0f, -0.55f, 0f), new Vector3(0.42f, 0.42f, 0.42f), Unlit(new Color(1f, 0.7f, 0.2f)));
            root.AddComponent<Spin>().Configure(Vector3.up, 36f); // a fast saucer spin
            return root;
        }

        /// <summary>The bandit raider: an angular dark wedge with rust-red trim, a narrow cockpit slit and
        /// twin outrigger blades — clearly a different silhouette from the saucer UFO, reads as a predator.</summary>
        private GameObject BuildBanditShipModel(Transform parent)
        {
            var root = new GameObject("BanditShip");
            root.transform.SetParent(parent, false);

            var hull = LitTex("carbon", new Color(0.20f, 0.19f, 0.22f));      // near-black plating
            var trim = LitTex("iron_wall", new Color(0.55f, 0.22f, 0.16f));   // rust-red raider trim
            var glowRed = Unlit(new Color(1f, 0.25f, 0.15f));
            var glowAmber = Unlit(new Color(1f, 0.6f, 0.2f));

            // A forward-swept wedge body.
            Cube("Nose", root.transform, new Vector3(0f, 0f, 1.4f), new Vector3(0.7f, 0.5f, 1.6f), hull);
            Cube("Body", root.transform, new Vector3(0f, 0f, 0f), new Vector3(1.4f, 0.7f, 1.8f), hull);
            Cube("Tail", root.transform, new Vector3(0f, 0.1f, -1.4f), new Vector3(1.0f, 0.5f, 1.0f), trim);
            Cube("CockpitSlit", root.transform, new Vector3(0f, 0.26f, 0.9f), new Vector3(0.5f, 0.12f, 0.5f), glowAmber);

            // Outrigger blades left/right — the pirate silhouette.
            Cube("BladeL", root.transform, new Vector3(-1.3f, 0f, -0.2f), new Vector3(1.2f, 0.12f, 1.6f), trim);
            Cube("BladeR", root.transform, new Vector3(1.3f, 0f, -0.2f), new Vector3(1.2f, 0.12f, 1.6f), trim);
            Cube("TipL", root.transform, new Vector3(-1.9f, 0f, 0.5f), new Vector3(0.25f, 0.18f, 0.9f), hull);
            Cube("TipR", root.transform, new Vector3(1.9f, 0f, 0.5f), new Vector3(0.25f, 0.18f, 0.9f), hull);

            // Engines + running lights.
            Cube("EngineL", root.transform, new Vector3(-0.45f, 0f, -2.0f), new Vector3(0.35f, 0.35f, 0.3f), glowRed);
            Cube("EngineR", root.transform, new Vector3(0.45f, 0f, -2.0f), new Vector3(0.35f, 0.35f, 0.3f), glowRed);
            Cube("LightL", root.transform, new Vector3(-1.9f, 0.12f, 1.0f), new Vector3(0.12f, 0.12f, 0.12f), glowRed);
            Cube("LightR", root.transform, new Vector3(1.9f, 0.12f, 1.0f), new Vector3(0.12f, 0.12f, 0.12f), glowRed);

            return root;
        }

        /// <summary>A real cruiser model: an elongated iron hull with a glass bridge and twin glowing engines. Its
        /// dark spine + engine blocks wear the Guardian circuit plating (#1337) so the finale gauntlet reads as one
        /// machine family; the pale iron hull stays as it was.</summary>
        private GameObject BuildCruiserModel(Transform parent)
        {
            var root = new GameObject("Cruiser");
            root.transform.SetParent(parent, false);
            var hull = LitTex("iron_wall", new Color(0.7f, 0.55f, 0.55f));
            var dark = GuardianPlating(new Color(0.8f, 0.78f, 0.8f));

            Cube("Hull", root.transform, Vector3.zero, new Vector3(2f, 1.2f, 5f), hull);
            Cube("Spine", root.transform, new Vector3(0f, 0.8f, -0.5f), new Vector3(0.8f, 0.6f, 3f), dark);
            Cube("Bridge", root.transform, new Vector3(0f, 0.7f, 1.8f), new Vector3(1f, 0.7f, 1.2f), LitTex("glass", new Color(0.6f, 0.8f, 1f)));
            Cube("EngineL", root.transform, new Vector3(-0.7f, 0f, -2.6f), new Vector3(0.7f, 0.7f, 0.9f), dark);
            Cube("EngineR", root.transform, new Vector3(0.7f, 0f, -2.6f), new Vector3(0.7f, 0.7f, 0.9f), dark);
            Cube("GlowL", root.transform, new Vector3(-0.7f, 0f, -3.1f), new Vector3(0.4f, 0.4f, 0.3f), Unlit(new Color(1f, 0.5f, 0.3f)));
            Cube("GlowR", root.transform, new Vector3(0.7f, 0f, -3.1f), new Vector3(0.4f, 0.4f, 0.3f), Unlit(new Color(1f, 0.5f, 0.3f)));
            return root;
        }

        /// <summary>A soft radial glow (bright core → transparent rim) for the sun billboard + flare sprites.</summary>
        private static Texture2D GenerateGlowTexture()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                float core = Mathf.Clamp01(1f - d * 4f);
                float halo = Mathf.Pow(Mathf.Clamp01(1f - d), 2.5f);
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(core * 0.85f + halo * 0.6f));
            }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static Vector3 EntityScale(string kind) => kind switch
        {
            "Asteroid" => Vector3.one * 2.4f,
            "Ufo" => new Vector3(2.4f, 0.7f, 2.4f),
            "Cruiser" => new Vector3(3f, 1.5f, 5f),
            "SpaceStation" => new Vector3(8f, 5f, 8f),
            "Wreck" => new Vector3(4f, 2.5f, 6f), // #1664: only until its voxel hull design arrives
            "ResourceDrop" => Vector3.one * 0.7f,
            "Debris" => Vector3.one * 1.2f,       // #2353: only until its voxel design arrives
            "SalvageCapsule" => Vector3.one * 0.9f,
            _ => Vector3.one * 1.1f,
        };

        private static Color EntityColor(string kind) => kind switch
        {
            "Asteroid" => new Color(0.45f, 0.42f, 0.38f),
            "Wreck" => new Color(0.55f, 0.45f, 0.32f), // scorched plating — amber-grey like its radar blip
            "Debris" => new Color(0.5f, 0.42f, 0.36f),      // #2353: torn plating
            "DebrisField" => new Color(0.85f, 0.55f, 0.3f), // #2353: the field's dusty copper
            "SalvageCapsule" => new Color(0.4f, 0.95f, 0.85f),
            "Ufo" => new Color(0.6f, 0.35f, 0.8f),
            "Cruiser" => new Color(0.7f, 0.3f, 0.3f),
            "SpaceStation" => new Color(0.62f, 0.66f, 0.72f),
            "ResourceDrop" => new Color(0.5f, 0.9f, 1f),
            _ => new Color(0.85f, 0.2f, 0.2f),
        };

        /// <summary>A translucent tinted "energy field" quad material — the same alpha-blend Cloud shader the
        /// on-foot energy doors + the atmosphere halo use, so the ship's rear hatch reads as a powered energy
        /// door (the colour's alpha sets how solid the field looks). Falls back to Unlit/Transparent.</summary>
        private static Material EnergyField(Color c)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var m = new Material(shader) { mainTexture = Texture2D.whiteTexture, renderQueue = 3000 };
            m.SetColor("_Color", ShaderColor.Srgb(c));
            return m;
        }

        /// <summary>Slowly tumbles an asteroid about a fixed random axis (purely cosmetic).</summary>
        private sealed class Spin : MonoBehaviour
        {
            private Vector3 _axis = Vector3.up;
            private float _speed = 12f;
            private bool _configured;

            /// <summary>Pins a fixed axis + speed (deg/s); otherwise a random tumble is chosen on Start.</summary>
            public void Configure(Vector3 axis, float speed)
            {
                _axis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.up;
                _speed = speed;
                _configured = true;
            }

            private void Start()
            {
                if (_configured)
                {
                    return;
                }

                _axis = Random.onUnitSphere;
                _speed = Random.Range(7f, 20f); // degrees / second
            }

            private void Update() => transform.Rotate(_axis, _speed * Time.deltaTime, Space.Self);
        }

        /// <summary>Maps a biome / planet-type key to a planet tint + a fitting block texture. Unknown
        /// keys get a stable hash-derived colour so every world type still looks distinct.</summary>
        private static (Color tint, string tex) PlanetLook(string key)
        {
            switch ((key ?? string.Empty).ToLowerInvariant())
            {
                case "jungle":
                case "forest": return (new Color(0.32f, 0.55f, 0.30f), "grass");
                case "desert": return (new Color(0.82f, 0.68f, 0.42f), "sand");
                case "ice":
                case "frozen": return (new Color(0.72f, 0.86f, 0.96f), "ice");
                case "lava":
                case "volcanic": return (new Color(0.78f, 0.32f, 0.18f), "lava");
                case "swamp": return (new Color(0.40f, 0.45f, 0.30f), "mud");
                case "crystal": return (new Color(0.55f, 0.65f, 0.92f), "crystal");
                case "ocean":
                case "water": return (new Color(0.24f, 0.46f, 0.72f), "water");
                case "asteroid_icy": return (new Color(0.76f, 0.86f, 0.92f), "ice");
                case "asteroid_crystal": return (new Color(0.55f, 0.65f, 0.92f), "crystal");
                case "asteroid_metallic": return (new Color(0.44f, 0.43f, 0.46f), "stone");
                case "asteroid_carbon": return (new Color(0.26f, 0.25f, 0.24f), "stone");
                case "barren":
                case "asteroid":
                case "rock": return (new Color(0.52f, 0.50f, 0.47f), "stone");
                default:
                    return (HashColor(key), "stone");
            }
        }

        private static Color HashColor(string key)
        {
            int h = 0;
            foreach (char c in key ?? string.Empty)
            {
                h = h * 31 + c;
            }

            var rng = new System.Random(h);
            return new Color(
                0.40f + 0.45f * (float)rng.NextDouble(),
                0.40f + 0.45f * (float)rng.NextDouble(),
                0.40f + 0.45f * (float)rng.NextDouble());
        }

        /// <summary>Stable per-body seed from its id/name so each planet's cloud shell pattern differs.</summary>
        private static int CloudShellSeed(string key)
        {
            int h = 17;
            foreach (char ch in key ?? string.Empty)
            {
                h = h * 31 + ch;
            }

            return h;
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
