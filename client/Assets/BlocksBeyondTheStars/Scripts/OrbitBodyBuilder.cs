// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using static BlocksBeyondTheStars.Client.ModelKit;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What one orbit body is: its world (seed, body, planet type, size, terrain options) and how much of it to draw.</summary>
    internal sealed class OrbitBodySpec
    {
        public GameContent Content;
        public BlockTextureAtlas Atlas;
        public long WorldSeed;

        /// <summary>The body's look key: its location id, else its location name — salts the map bake, the cloud pattern and
        /// the world's sky and cloud tints.</summary>
        public string Key = string.Empty;

        /// <summary>The "System · Body" key the world's vegetation colour is rolled with.</summary>
        public string FloraKey = string.Empty;

        public string PlanetType = string.Empty;

        /// <summary>The star map's kind ("Planet", "Moon", "AsteroidField") — an airless moon is cratered.</summary>
        public string Kind = string.Empty;

        public int Circumference;
        public bool Continents;
        public int Generation;
        public bool LavaCoreVolcanoes;

        /// <summary>The planetary ring style (#596), 0 = none.</summary>
        public int RingSeed;

        /// <summary>The system star's hue (normalised colour) — it washes the body, its clouds and its rim.</summary>
        public Color SunHue = Color.white;

        /// <summary>Queue order of the map bake (higher first).</summary>
        public int MapPriority = 1;

        /// <summary>The baked map's size: the flight view's small orbit map by default; the shell's showcase planet fills a
        /// third of the screen and asks for more (a 96 × 48 map read as a blurred mosaic on the poster).</summary>
        public int MapWidth = OrbitBodyBuilder.MapWidth;
        public int MapHeight = OrbitBodyBuilder.MapHeight;

        /// <summary>Detail switches: the shell backdrop drops the rim on Potato/Low (#3705); the flight view draws all.</summary>
        public bool Clouds = true;
        public bool Limb = true;
    }

    /// <summary>
    /// The real planet renderer of the orbit view, shared by everything that shows a planet from space (#3705): the flight view
    /// (<see cref="SpaceView"/>), the shell backdrop and the first-start intro film (<see cref="ShowcaseScene"/>) and the story's
    /// <c>space_vista</c> stage (<see cref="CutsceneStage"/>). One body is the pole-free globe (<see cref="FxKit.GlobeMesh"/>,
    /// #3127) sun-phase-lit in the flat data-driven colour until its REAL world map is baked off the main thread
    /// (<see cref="WorldMinimap.RequestGlobe"/>, #2172/#3128), a cloud shell from the planet's data, shaded toward its star
    /// (#2170/#2175), the atmosphere limb (<c>PlanetLimb</c>, #2400) and its rings (#596). Extracted from
    /// <c>SpaceView.SpawnBody</c> unchanged — the flight view keeps its weather, spin and storms on top, through the parts
    /// <see cref="Body"/> hands back. Nothing here needs a server: the shell builds a world the client generates itself.
    /// </summary>
    internal static class OrbitBodyBuilder
    {
        /// <summary>The map texture size of an orbit body (the flight view's, also in the shell).</summary>
        public const int MapWidth = 96;
        public const int MapHeight = 48;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ShadeColorId = Shader.PropertyToID("_ShadeColor");
        private static readonly int CloudSunDirId = Shader.PropertyToID("_CloudSunDir");
        private static readonly int SunShadeId = Shader.PropertyToID("_SunShade");
        private static readonly int BulgeId = Shader.PropertyToID("_Bulge");

        /// <summary>The parts of one built body, for the owner's per-frame look (weather, spin, drift).</summary>
        internal sealed class Body
        {
            public Transform Sphere;
            public PlanetType Planet;
            public OrbitAtmosphere Atmosphere;
            public Material SurfaceMat;
            public Transform Shell;
            public Material ShellMat;
            public Texture2D ShellTex;
            public Color32[] ShellBuffer;
            public float[] Noise;
            public float ShellPhaseDeg;
            public Material HazeMat;
            public Color HazeColor;
            public Transform Ring;
            public Quaternion RingBase = Quaternion.identity;

            /// <summary>The baked world map once it is in (null until then).</summary>
            public PlanetMapData Map;
        }

        /// <summary>
        /// Builds one body under <paramref name="parent"/> at <paramref name="localPos"/>, <paramref name="diameter"/> across,
        /// lit from <paramref name="sunDir"/> (world space, toward the star). Every material and texture it makes goes into
        /// <paramref name="owned"/> (the caller destroys them with the scene). <paramref name="onMap"/> runs once the world map
        /// is on the sphere (never when the bake fails or the scene is gone by then).
        /// </summary>
        public static Body Build(OrbitBodySpec spec, Transform parent, string name, Vector3 localPos, float diameter, Vector3 sunDir,
            List<Object> owned, System.Action<Body> onMap = null)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.GetComponent<MeshFilter>().sharedMesh = FxKit.GlobeMesh; // #3127: no wedges at the poles
            StripCollider(sphere);
            sphere.transform.SetParent(parent, false);
            sphere.transform.localPosition = localPos;
            sphere.transform.localScale = Vector3.one * diameter;

            var body = new Body { Sphere = sphere.transform, Planet = spec.Content?.GetPlanet(spec.PlanetType ?? string.Empty) };
            body.SurfaceMat = SurfaceMaterial(spec, body, sunDir, owned, onMap);
            sphere.GetComponent<Renderer>().sharedMaterial = body.SurfaceMat;

            // Atmosphere from DATA (#2170): no clouds on airless bodies, the type's cloud cover otherwise, the world's own cloud
            // and sky tints — the hard-coded table left 33 of 51 types cloudless from orbit.
            body.Atmosphere = OrbitLook.For(body.Planet, spec.WorldSeed, spec.Key, spec.Generation); // #2692: the save's generation
            if (spec.Clouds && body.Atmosphere.HasClouds)
            {
                AddCloudShell(body, spec.SunHue, sunDir, CloudSeed(spec.Key), spec.Key, owned);
            }

            if (spec.Limb && body.Atmosphere.HasAir)
            {
                AddLimb(body, spec.SunHue, sunDir, owned);
            }

            // Planetary rings (#596): a seeded, tilted band disc — queue 3001 so it draws after the haze (2999) and the cloud
            // shell (3000); the opaque planet sphere occludes the far half.
            if (spec.RingSeed != 0)
            {
                var ring = PlanetRings.Attach(body.Sphere, spec.RingSeed, spec.SunHue, 0.55f, 3001, out _);
                body.Ring = ring != null ? ring.transform : null;
                body.RingBase = ring != null ? ring.transform.localRotation : Quaternion.identity;
            }

            return body;
        }

        /// <summary>Stable per-body seed from its id/name so each planet's cloud shell pattern differs.</summary>
        public static int CloudSeed(string key)
        {
            int h = 17;
            foreach (char ch in key ?? string.Empty)
            {
                h = h * 31 + ch;
            }

            return h;
        }

        /// <summary>Data-driven orbital look before the map is in: sphere texture = the type's REAL surface block, tinted with a
        /// partial cast of the mixed orbit colour (ground + this world's own flora hue + water/lava) — so a mud flat reads brown
        /// and a lush world reads in ITS vegetation colour. Falls back to the legacy palette when the type is unknown (the old
        /// hardcoded map mis-coloured every newer planet type).</summary>
        public static (Color Tint, string Tex) FlatLook(GameContent content, BlockTextureAtlas atlas, long worldSeed, string planetType,
            string floraKey)
        {
            var legacy = LegacyLook(planetType);
            var planet = content?.GetPlanet(planetType ?? string.Empty);
            if (planet == null)
            {
                return legacy;
            }

            if (planet.IsGasWorld)
            {
                return (new Color(0.96f, 0.9f, 0.84f), "gas"); // #2112: the banded cloud tops are the whole face of a gas giant
            }

            Color ground = PlanetOrbitLook.GroundColor(content, atlas, worldSeed, floraKey, planetType, legacy.Tint);
            // Partial cast only: the surface texture already carries the ground colour — full multiplication would
            // double-saturate and darken the sphere.
            return (Color.Lerp(Color.white, ground, 0.55f), planet.SurfaceBlock);
        }

        /// <summary>The flat look first (ground + this world's flora + water/lava), then the real map swapped in once its bake
        /// finishes off the main thread — building a whole system never freezes the frame.</summary>
        private static Material SurfaceMaterial(OrbitBodySpec spec, Body body, Vector3 sunDir, List<Object> owned,
            System.Action<Body> onMap)
        {
            var look = FlatLook(spec.Content, spec.Atlas, spec.WorldSeed, spec.PlanetType, spec.FloraKey);
            Color bodyTint = Color.Lerp(look.Tint, look.Tint * spec.SunHue, 0.35f);
            var flatTex = LoadTex(look.Tex);
            var mat = LitPhase(bodyTint, sunDir, flatTex, new Vector2(3f, 2f));
            owned?.Add(mat);
            if (flatTex != null)
            {
                owned?.Add(flatTex); // replaced by the baked map later — destroyed with the scene, not leaked
            }

            if (body.Planet == null || spec.Content == null)
            {
                return mat;
            }

            // Airless moons carry craters, as the server generates them (#2170).
            bool cratered = spec.Kind == "Moon" && body.Planet.IsAirless;
            var request = WorldMinimap.MakeRequest(spec.Content, spec.Atlas, spec.WorldSeed, spec.FloraKey, spec.PlanetType,
                spec.Circumference, spec.MapWidth, spec.MapHeight, spec.Key, spec.Continents, spec.Generation, cratered, spec.LavaCoreVolcanoes);
            Color washTint = Color.Lerp(Color.white, spec.SunHue, 0.35f);
            var sphere = body.Sphere;
            WorldMinimap.RequestGlobe(request, (tex, data) => // #3128: the map with polar caps; data stays the raw map
            {
                if (mat == null || sphere == null)
                {
                    return; // the scene was torn down while the map baked
                }

                mat.mainTexture = tex;
                mat.mainTextureScale = Vector2.one;
                mat.color = ShaderColor.Srgb(washTint);
                body.Map = data;
                onMap?.Invoke(body);
            }, spec.MapPriority);
            return mat;
        }

        /// <summary>Adds a semi-transparent cloud shell over the sphere (#2170/#2175) at the planet's base cover. Its texture's
        /// colour channel carries the cloud tint (the flight view recomposes it with the live weather: storm-dark, snow-white,
        /// acid, ash, sand, spores), the material only the star's light and the night-side shade, so the day/night terminator
        /// matches the planet's own.</summary>
        private static void AddCloudShell(Body body, Color sunHue, Vector3 sunDir, int seed, string key, List<Object> owned)
        {
            var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "CloudShell";
            shell.GetComponent<MeshFilter>().sharedMesh = FxKit.GlobeMesh; // #3127: the planet's own mapping
            StripCollider(shell);
            shell.transform.SetParent(body.Sphere, false);
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localScale = Vector3.one * 1.035f; // just above the surface

            var tex = PlanetWeatherVisuals.NewShellTexture("CloudShell_" + key);
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(shader) { mainTexture = tex, renderQueue = 3000 };
            var lit = Color.Lerp(Color.white, sunHue, 0.35f);
            lit.a = 0.9f;
            mat.SetColor(ColorId, ShaderColor.Srgb(lit));
            var shade = new Color(0.22f, 0.23f, 0.27f, lit.a);
            mat.SetColor(ShadeColorId, ShaderColor.Srgb(shade));
            mat.SetVector(CloudSunDirId, sunDir.sqrMagnitude > 1e-4f ? sunDir.normalized : Vector3.up);
            mat.SetFloat(SunShadeId, 1f);
            mat.SetFloat(BulgeId, 0f);

            var mr = shell.GetComponent<Renderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            body.Shell = shell.transform;
            body.ShellMat = mat;
            body.ShellTex = tex;
            body.ShellBuffer = new Color32[PlanetWeatherVisuals.ShellW * PlanetWeatherVisuals.ShellH];
            body.Noise = PlanetWeatherVisuals.Noise(seed);
            body.ShellPhaseDeg = Mathf.Repeat(seed * 0.37f, 360f); // every planet's pattern starts elsewhere
            owned?.Add(mat);
            owned?.Add(tex);
            PlanetWeatherVisuals.ComposeShell(tex, body.ShellBuffer, body.Noise, null, null, body.Atmosphere.CloudDensity,
                body.Atmosphere.CloudRgb, body.Atmosphere.CloudRgb, body.Atmosphere.CloudRgb, 0f);
        }

        /// <summary>The atmosphere limb (#2400): a thin shell whose glow sits on the rim and the sun-facing side, warm along the
        /// terminator, dark on the night side (PlanetLimb.shader) — a breathable atmosphere reads as a denser glow than a toxic
        /// one; airless bodies stay crisp bare rock.</summary>
        private static void AddLimb(Body body, Color sunHue, Vector3 sunDir, List<Object> owned)
        {
            float atm = body.Atmosphere.Breathable ? 1f : 0.7f;
            var haze = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            haze.name = "Atmosphere";
            haze.GetComponent<MeshFilter>().sharedMesh = FxKit.GlobeMesh; // #3127: a round rim, no 24-segment facets
            StripCollider(haze);
            haze.transform.SetParent(body.Sphere, false);
            haze.transform.localScale = Vector3.one * 1.06f;
            var hShader = Shader.Find("BlocksBeyondTheStars/PlanetLimb") ?? Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var sky = PlanetWeatherVisuals.Rgb(body.Atmosphere.SkyRgb);
            var hCol = Color.Lerp(Color.Lerp(sky, Color.white, 0.25f), sunHue, 0.25f);
            hCol.a = 0.35f + 0.35f * atm;
            var hMat = new Material(hShader) { mainTexture = Texture2D.whiteTexture, renderQueue = 2999 };
            hMat.SetColor(ColorId, ShaderColor.Srgb(hCol));
            hMat.SetVector("_SunDir", sunDir);
            hMat.SetColor("_Warm", ShaderColor.Srgb(Color.Lerp(new Color(1f, 0.6f, 0.3f), sunHue, 0.4f)));
            var hMr = haze.GetComponent<Renderer>();
            hMr.sharedMaterial = hMat;
            hMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hMr.receiveShadows = false;
            body.HazeMat = hMat;
            body.HazeColor = hCol;
            owned?.Add(hMat);
        }

        /// <summary>Maps a biome / planet-type key to a planet tint + a fitting block texture. Unknown keys get a stable
        /// hash-derived colour so every world type still looks distinct.</summary>
        private static (Color Tint, string Tex) LegacyLook(string key)
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
                case "asteroid_kuba_w": return (new Color(0.40f, 0.62f, 0.36f), "grass"); // #3307: Jafar's green asteroid
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
    }
}
