using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// In-world decor for the ship's interactive stations (cockpit/room-identity pass): the cockpit
    /// gets a console with an animated screen and a holographic system map (built from the star-map
    /// data, visible when the player stands near), the medbay a softly pulsing heal-tank, the lab and
    /// ship console flickering terminal screens, and the workshop an occasional spark burst. Entirely
    /// code-built (DoorView pattern) and render-only — the server stays authoritative over what the
    /// stations DO. Decor follows <see cref="GameBootstrap.ScenePos"/> every frame (torus wrap).
    /// </summary>
    public sealed class StationDecorView : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private NetShipStation[] _builtFor;
        private readonly List<(GameObject Go, Vector3 World)> _decor = new();

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (!ReferenceEquals(_builtFor, Game.Stations))
            {
                Rebuild();
            }

            foreach (var (go, world) in _decor)
            {
                if (go != null)
                {
                    go.transform.position = Game.ScenePos(world.x, world.y, world.z);
                }
            }
        }

        private void Rebuild()
        {
            foreach (var (go, _) in _decor)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _decor.Clear();
            _builtFor = Game.Stations;
            foreach (var s in _builtFor ?? System.Array.Empty<NetShipStation>())
            {
                GameObject go = s.Type switch
                {
                    "cockpit" => BuildCockpit(),
                    "medbay" => BuildMedbay(),
                    "lab" => BuildTerminal(new Color(0.35f, 1f, 0.60f)),    // lab reads green
                    "console" => BuildTerminal(new Color(0.40f, 0.85f, 1f)), // ship console reads cyan
                    "workshop" => BuildWorkshop(),
                    _ => null,
                };

                if (go == null)
                {
                    continue;
                }

                go.transform.SetParent(transform, false);
                // The station marker block fills its cell — the decor sits on top of it.
                _decor.Add((go, new Vector3(s.X, s.Y + 1f, s.Z)));
            }
        }

        // --- builders -------------------------------------------------------------------------

        /// <summary>Console block + tilted animated screen + the holographic system map above it.</summary>
        private GameObject BuildCockpit()
        {
            var root = new GameObject("CockpitDecor");

            var basePlate = Cube(root.transform, new Vector3(0f, 0.18f, 0f), new Vector3(0.85f, 0.36f, 0.45f),
                Unlit(new Color(0.16f, 0.18f, 0.22f)));
            basePlate.name = "ConsoleBase";

            var screen = Cube(root.transform, new Vector3(0f, 0.52f, -0.05f), new Vector3(0.72f, 0.34f, 0.05f),
                Unlit(new Color(0.30f, 0.75f, 0.95f)));
            screen.name = "ConsoleScreen";
            screen.transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            screen.GetComponent<Renderer>().sharedMaterial.renderQueue = 2000;
            screen.AddComponent<ScreenFlicker>().Base = new Color(0.30f, 0.75f, 0.95f);

            var holo = new GameObject("HoloMap");
            holo.transform.SetParent(root.transform, false);
            holo.transform.localPosition = new Vector3(0f, 1.15f, 0f);
            var map = holo.AddComponent<HoloMap>();
            map.Game = Game;

            return root;
        }

        /// <summary>The heal-tank: a translucent capsule that breathes a soft cyan glow.</summary>
        private GameObject BuildMedbay()
        {
            var root = new GameObject("MedbayDecor");
            var tank = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            tank.name = "HealTank";
            StripCollider(tank);
            tank.transform.SetParent(root.transform, false);
            tank.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            tank.transform.localScale = new Vector3(0.55f, 0.75f, 0.55f);
            var mat = Translucent(new Color(0.45f, 0.90f, 1f, 0.30f));
            tank.GetComponent<Renderer>().sharedMaterial = mat;
            var pulse = tank.AddComponent<GlowPulse>();
            pulse.Mat = mat;
            pulse.Base = new Color(0.45f, 0.90f, 1f, 0.30f);
            return root;
        }

        /// <summary>A small terminal: dark base + a flickering coloured screen.</summary>
        private GameObject BuildTerminal(Color screenColor)
        {
            var root = new GameObject("TerminalDecor");
            Cube(root.transform, new Vector3(0f, 0.14f, 0f), new Vector3(0.55f, 0.28f, 0.35f),
                Unlit(new Color(0.15f, 0.16f, 0.19f)));
            var screen = Cube(root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.5f, 0.30f, 0.05f),
                Unlit(screenColor));
            screen.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            screen.AddComponent<ScreenFlicker>().Base = screenColor;
            return root;
        }

        /// <summary>The workshop spits a short orange spark burst every few seconds while the player is near.</summary>
        private GameObject BuildWorkshop()
        {
            var root = new GameObject("WorkshopDecor");
            root.AddComponent<SparkTicker>();
            return root;
        }

        // --- shared helpers -------------------------------------------------------------------

        private static GameObject Cube(Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static Material Unlit(Color c)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            return new Material(shader) { color = ShaderColor.Srgb(c) };
        }

        private static Material Translucent(Color c)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(shader);
            mat.SetColor("_Color", ShaderColor.Srgb(c));
            mat.renderQueue = 3000;
            return mat;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
        }

        private static bool CameraNear(Transform t, float dist)
        {
            var cam = Camera.main;
            return cam != null && (cam.transform.position - t.position).sqrMagnitude < dist * dist;
        }

        // --- animators ------------------------------------------------------------------------

        /// <summary>A display that breathes and occasionally blips brighter (Perlin-driven, no per-frame GC).</summary>
        private sealed class ScreenFlicker : MonoBehaviour
        {
            public Color Base;

            private Material _mat;
            private float _seed;

            private void Start()
            {
                _mat = GetComponent<Renderer>()?.sharedMaterial;
                _seed = (transform.position.x + transform.position.z) * 0.37f;
            }

            private void Update()
            {
                if (_mat == null || !CameraNear(transform, 24f))
                {
                    return;
                }

                float n = Mathf.PerlinNoise(Time.time * 2.2f, _seed);
                float k = 0.65f + 0.35f * n + (n > 0.92f ? 0.4f : 0f); // breathing + rare bright blip
                _mat.color = ShaderColor.Srgb(Base * k);
            }
        }

        /// <summary>A slow alpha breathing on a translucent material (the medbay tank glow).</summary>
        private sealed class GlowPulse : MonoBehaviour
        {
            public Material Mat;
            public Color Base;

            private void Update()
            {
                if (Mat == null || !CameraNear(transform, 24f))
                {
                    return;
                }

                var c = Base;
                c.a = Base.a * (0.75f + 0.25f * Mathf.Sin(Time.time * 1.6f));
                Mat.SetColor("_Color", ShaderColor.Srgb(c));
            }
        }

        /// <summary>Workshop ambience: a small orange spark burst every 4–7 s while the player is near.</summary>
        private sealed class SparkTicker : MonoBehaviour
        {
            private float _next;
            private Material _mat;

            private void Start()
            {
                _mat = Unlit(new Color(1f, 0.62f, 0.18f));
                _next = Time.time + 2f;
            }

            private void Update()
            {
                if (Time.time < _next || !CameraNear(transform, 18f))
                {
                    return;
                }

                _next = Time.time + Random.Range(4f, 7f);
                for (int i = 0; i < 4; i++)
                {
                    var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    StripCollider(p);
                    p.transform.position = transform.position + new Vector3(0f, 0.4f, 0f);
                    p.transform.localScale = Vector3.one * 0.06f;
                    p.GetComponent<Renderer>().sharedMaterial = _mat;
                    var bit = p.AddComponent<SparkBit>();
                    bit.Vel = new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(1.0f, 2.2f), Random.Range(-1.2f, 1.2f));
                }
            }
        }

        /// <summary>A short-lived spark cube: arcs under gravity, shrinks, self-destroys.</summary>
        private sealed class SparkBit : MonoBehaviour
        {
            public Vector3 Vel;

            private const float Life = 0.5f;
            private float _t;

            private void Update()
            {
                _t += Time.deltaTime;
                Vel += Vector3.down * 9f * Time.deltaTime;
                transform.position += Vel * Time.deltaTime;
                transform.localScale = Vector3.one * 0.06f * Mathf.Max(0f, 1f - _t / Life);
                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }

        /// <summary>The cockpit hologram: the active system's bodies as tinted orbiting dots around a
        /// warm star over a faint projection disc. Built lazily from the star-map data on first
        /// approach; slowly rotates; collapses to zero scale while the player is away.</summary>
        private sealed class HoloMap : MonoBehaviour
        {
            public GameBootstrap Game;

            private bool _built;

            private void Update()
            {
                bool near = CameraNear(transform, 5f);
                if (near && !_built)
                {
                    Build();
                }

                transform.localScale = Vector3.Lerp(transform.localScale, near ? Vector3.one : Vector3.zero, Time.deltaTime * 6f);
                if (near)
                {
                    transform.Rotate(0f, 12f * Time.deltaTime, 0f, Space.Self);
                }
            }

            private void Build()
            {
                _built = true;
                transform.localScale = Vector3.zero;

                // Projection disc under the bodies.
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cube);
                StripCollider(disc);
                disc.transform.SetParent(transform, false);
                disc.transform.localPosition = new Vector3(0f, -0.18f, 0f);
                disc.transform.localScale = new Vector3(1.2f, 0.01f, 1.2f);
                disc.GetComponent<Renderer>().sharedMaterial = Translucent(new Color(0.35f, 0.8f, 1f, 0.10f));

                // Central star.
                Dot(Vector3.zero, 0.10f, new Color(1f, 0.85f, 0.5f));

                int i = 0;
                foreach (var body in ActiveSystemBodies())
                {
                    if (i >= 5)
                    {
                        break;
                    }

                    float radius = 0.28f + i * 0.16f;
                    float angle = (body.Id?.GetHashCode() ?? i * 977) * 0.013f;
                    var pos = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    Dot(pos, 0.05f, PlanetTypeColor(body.PlanetType));
                    i++;
                }
            }

            private void Dot(Vector3 localPos, float size, Color c)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                StripCollider(go);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = localPos;
                go.transform.localScale = Vector3.one * size;
                go.GetComponent<Renderer>().sharedMaterial = Unlit(c);
            }

            private IEnumerable<NetBody> ActiveSystemBodies()
            {
                var map = Game?.StarMap;
                if (map?.Systems == null)
                {
                    yield break;
                }

                NetStarSystem active = null;
                foreach (var sys in map.Systems)
                {
                    foreach (var b in sys.Bodies)
                    {
                        if (b.Id == map.ActiveLocationId)
                        {
                            active = sys;
                            break;
                        }
                    }

                    if (active != null)
                    {
                        break;
                    }
                }

                active ??= map.Systems.Length > 0 ? map.Systems[0] : null;
                if (active == null)
                {
                    yield break;
                }

                foreach (var b in active.Bodies)
                {
                    yield return b;
                }
            }

            private static Color PlanetTypeColor(string planetType) => (planetType ?? string.Empty) switch
            {
                "ice" or "frozen" or "tundra" => new Color(0.70f, 0.85f, 1f),
                "lava" or "volcanic" or "ashen" => new Color(1f, 0.50f, 0.20f),
                "desert" or "savanna" => new Color(0.90f, 0.75f, 0.45f),
                "jungle" or "forest" or "swamp" or "fungal" => new Color(0.40f, 0.80f, 0.40f),
                "ocean" => new Color(0.30f, 0.60f, 1f),
                "crystal" or "crystal_living" => new Color(0.80f, 0.70f, 1f),
                _ => new Color(0.60f, 0.70f, 0.75f),
            };
        }
    }
}
