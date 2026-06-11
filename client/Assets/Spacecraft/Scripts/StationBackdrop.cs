using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// While the player is aboard an orbital station, this renders the view <b>outside</b> the windows:
    /// the planet the station orbits and the system's sun, far off in fixed directions. Combined with the
    /// transparent glass/force-field rendering and the <see cref="Starfield"/>, looking out a station
    /// viewport shows real space — the nearby planet, the star, and the stars beyond. Opaque hull walls
    /// occlude the backdrop, so it only shows through the windows. Hidden everywhere else (it follows the
    /// camera so the bodies stay at "infinity"). Presentation only.
    /// </summary>
    public sealed class StationBackdrop : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private GameObject _root;
        private Transform _sun;
        private Material _sunMat;
        private string _builtKey = string.Empty; // rebuild when the orbited planet / sun colour changes

        private void LateUpdate()
        {
            bool boarded = Game != null && Camera != null
                           && !string.IsNullOrEmpty(Game.StationName) && !Game.SpaceViewActive;
            if (!boarded)
            {
                if (_root != null && _root.activeSelf)
                {
                    _root.SetActive(false);
                }

                return;
            }

            Ensure();
            _root.SetActive(true);

            // Ride with the camera so the planet + sun stay at a fixed direction/distance (no parallax),
            // like a skybox — the hull walls still occlude them, so they only show through the windows.
            _root.transform.position = Camera.transform.position;
            if (_sun != null)
            {
                _sun.rotation = Quaternion.LookRotation(_sun.position - Camera.transform.position, Vector3.up);
            }
        }

        private void Ensure()
        {
            string key = (Game.Environment != null ? Game.Environment.SunColor : 0) + "|" + PlanetBiome();
            if (_root != null && key == _builtKey)
            {
                return;
            }

            if (_root != null)
            {
                Destroy(_root);
            }

            _builtKey = key;
            Build();
        }

        private void Build()
        {
            _root = new GameObject("StationBackdrop");
            _root.transform.SetParent(transform, false);

            // The planet the station orbits — a big, distant sphere (opaque, so hull walls occlude it).
            var planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            planet.name = "BackdropPlanet";
            StripCollider(planet);
            planet.transform.SetParent(_root.transform, false);
            planet.transform.localPosition = new Vector3(230f, -80f, 620f);
            planet.transform.localScale = Vector3.one * 300f;
            var litShader = Shader.Find("Spacecraft/LitColor") ?? Shader.Find("Unlit/Color");
            string biome = PlanetBiome();
            // Data-driven planet colour (surface block + flora/water blend) with the palette as backstop.
            var planetCol = PlanetOrbitLook.GroundColor(
                Game.Content, Game.Atlas, Game.WorldSeed, Game?.LocationName ?? string.Empty, biome, PlanetColor(biome));
            planet.GetComponent<Renderer>().sharedMaterial = new Material(litShader) { color = planetCol };

            // The system's sun, in its own colour (additive glow billboard).
            Color sunCol = Game.Environment != null ? Rgb(Game.Environment.SunColor) : new Color(1f, 0.96f, 0.88f);
            var sun = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sun.name = "BackdropSun";
            StripCollider(sun);
            sun.transform.SetParent(_root.transform, false);
            sun.transform.localPosition = new Vector3(-360f, 170f, 540f);
            sun.transform.localScale = Vector3.one * 150f;
            var sunShader = Shader.Find("Spacecraft/SunGlow") ?? Shader.Find("Unlit/Color");
            _sunMat = new Material(sunShader) { mainTexture = GlowTexture() };
            _sunMat.SetColor("_Color", sunCol);
            var smr = sun.GetComponent<MeshRenderer>();
            smr.sharedMaterial = _sunMat;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smr.receiveShadows = false;
            _sun = sun.transform;

            _root.SetActive(false);
        }

        /// <summary>The orbited planet's type: the active star-map body if known, else the current biome.</summary>
        private string PlanetBiome()
        {
            var map = Game?.StarMap;
            if (map?.Systems != null)
            {
                foreach (var sys in map.Systems)
                {
                    foreach (var b in sys.Bodies)
                    {
                        if (b.Id == map.ActiveLocationId && !string.IsNullOrEmpty(b.PlanetType))
                        {
                            return b.PlanetType;
                        }
                    }
                }
            }

            string biome = Game?.Environment?.Biome;
            return string.IsNullOrEmpty(biome) ? "rock" : biome;
        }

        /// <summary>Planet tint per biome/type (mirrors the space-view palette).</summary>
        private static Color PlanetColor(string key)
        {
            switch ((key ?? string.Empty).ToLowerInvariant())
            {
                case "jungle":
                case "forest": return new Color(0.32f, 0.55f, 0.30f);
                case "desert": return new Color(0.82f, 0.68f, 0.42f);
                case "ice":
                case "frozen": return new Color(0.72f, 0.86f, 0.96f);
                case "lava":
                case "volcanic": return new Color(0.78f, 0.32f, 0.18f);
                case "swamp": return new Color(0.40f, 0.45f, 0.30f);
                case "crystal": return new Color(0.55f, 0.65f, 0.92f);
                case "ocean":
                case "water": return new Color(0.24f, 0.46f, 0.72f);
                case "barren":
                case "asteroid":
                case "rock": return new Color(0.52f, 0.50f, 0.47f);
                default: return new Color(0.40f, 0.52f, 0.66f);
            }
        }

        private static Texture2D GlowTexture()
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

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
