// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Animated space-scene backdrop for the shell screens — now rendered with the SAME systems the live game uses
    /// so the attract scene reflects the real look: the real twinkling <see cref="Starfield"/> + per-system
    /// <see cref="NebulaField"/> gas dome, a layered glowing star (corona/core/centre, <see cref="SkyVisuals"/>) like
    /// the in-game space sun, the player's REAL voxel ship meshed from the block atlas (<see cref="ShipMeshBuilder"/>,
    /// with the chosen hull colour + engine glow), and the cinematic post stack (<see cref="UrpScenePost"/>: bloom,
    /// ACES tonemap, vignette, lens flare). A sun-lit-phased planet (<c>SkyBodyPhase</c> terminator) + cloud shell +
    /// atmosphere haze + moon + drifting phase-lit asteroids keep the original composition.
    /// Falls back to a hand-built cube ship only when the block atlas/content isn't available. Rendered by its own
    /// camera so the menu UI draws on top.
    /// </summary>
    public sealed class MenuBackground : MonoBehaviour
    {
        /// <summary>The shell — supplies <see cref="GameContent"/> (loaded before this is spawned) for the block
        /// atlas + the player's chosen hull colour. Set by <see cref="AppShell"/> right after AddComponent.</summary>
        public AppShell Shell;

        private Camera _cam;
        private Transform _planet;
        private Transform _clouds;
        private Transform _moon;
        private Transform _ship;
        private readonly Transform[] _asteroids = new Transform[7];
        private Renderer _beacon;
        private Material _engineMat;
        private Transform _engineGlowL, _engineGlowR;
        private Light _engineLight;
        private float _t;

        // Real-look render context (the block atlas + chunk materials, built once for the menu like GameBootstrap does).
        private BlockTextureAtlas _atlas;
        private Material _chunkMat, _chunkMatT;

        // Voxel-ship thruster bookkeeping (the FX ride the unscaled root so the ship's uniform scale doesn't shrink them).
        private Transform _thruster;
        private float _shipExtent = 6f, _shipScale = 1f;

        // The visible sun direction (shared by the layered star, the body phase-lighting and the block-atlas sun
        // shading) and its distance from the camera (so the bodies can be lit from the true sun position).
        private static readonly Vector3 SunDir = new Vector3(0.5f, 0.42f, 0.85f).normalized;
        private const float SunDist = 260f;

        private void Start()
        {
            var camGo = new GameObject("MenuCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.015f, 0.025f, 0.06f);
            _cam.farClipPlane = 600f;
            _cam.transform.localPosition = Vector3.zero;
            _cam.transform.localRotation = Quaternion.identity;

            // Space-sky shader globals so the voxel ship (block atlas) reads full-bright + directionally lit even
            // though no live Sky runs in the shell; mirrors what Sky does while the space view owns the camera.
            ApplySpaceLighting();

            // Real twinkling starfield + per-system nebula dome (menu mode: fixed brightness, no live game).
            var stars = gameObject.AddComponent<Starfield>();
            stars.Camera = _cam;
            stars.MenuBrightness = 1f;
            var nebula = gameObject.AddComponent<NebulaField>();
            nebula.Camera = _cam;
            nebula.MenuBrightness = 0.9f; // near the in-game space brightness so the gas dome reads vivid, not washed out
            nebula.MenuSeed = 142;        // curated seed (art-tunable in-build): a more colourful patch layout than the old 73

            BuildSun();
            BuildRenderContext();

            var rock = LoadTex("stone");
            var iceTex = LoadTex("ice");

            // Bodies are sun-lit-phased from the true sun position (a day/night terminator), like the in-game orbit
            // view — not flat key light. The phase direction is the world dir from each body toward the sun.
            Vector3 sunPos = SunDir * SunDist;

            var planetPos = new Vector3(11f, -3f, 34f);
            _planet = MakeSphere("Planet", planetPos, 26f,
                LitPhase(new Color(0.30f, 0.50f, 0.70f), sunPos - planetPos, iceTex, new Vector2(4f, 2f))).transform;
            _clouds = BuildCloudShell(_planet);
            BuildAtmosphereHaze(_planet, new Color(0.55f, 0.75f, 1f)); // thin bluish rim glow, like the orbit view
            var moonPos = new Vector3(-16f, 11f, 60f);
            _moon = MakeSphere("Moon", moonPos, 5f,
                LitPhase(new Color(0.60f, 0.60f, 0.64f), sunPos - moonPos, rock, new Vector2(2f, 1f))).transform;

            // Prefer the REAL voxel ship (block atlas, hull paint, engine glow); fall back to the cube silhouette.
            _ship = BuildVoxelShipModel() ?? BuildShip(LoadTex("iron_wall"));
            _ship.localPosition = new Vector3(-2.5f, -1.5f, 9f);
            _ship.localRotation = Quaternion.Euler(6f, 28f, -3f);

            var rng = new System.Random(99);
            for (int i = 0; i < _asteroids.Length; i++)
            {
                var apos = new Vector3((float)(rng.NextDouble() * 28 - 14), (float)(rng.NextDouble() * 16 - 8), 14f + (float)rng.NextDouble() * 24f);
                var a = Cube("Asteroid", transform, apos,
                    Vector3.one * (0.6f + (float)rng.NextDouble() * 1.6f),
                    LitPhase(new Color(0.45f, 0.40f, 0.34f), sunPos - apos, rock));
                _asteroids[i] = a.transform;
            }

            // Cinematic post (bloom + ACES tonemap + vignette + lens flare) — the same look the game uses, via the
            // URP Volume. The project always runs URP (every quality level assigns it), so there's no Built-in path.
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
            {
                var post = gameObject.AddComponent<UrpScenePost>();
                post.Preset = QualityPreset.High;
                post.LensFlareEnabled = true;
                post.MotionBlurEnabled = false;
                post.ShellMode = true; // tighter bloom so the attract scene reads crisp, not hazy

                // A code-created URP camera has post-processing OFF by default — turn it on or the global Volume
                // (bloom/tonemap/vignette/lens flare) never touches the menu scene.
                var camData = _cam.GetUniversalAdditionalCameraData();
                if (camData != null)
                {
                    camData.renderPostProcessing = true;
                }
            }
        }

        private void Update()
        {
            _t += Time.deltaTime;

            if (_planet != null)
            {
                _planet.localRotation = Quaternion.Euler(8f, _t * 1.6f, 0f);
            }

            if (_clouds != null)
            {
                _clouds.localRotation = Quaternion.Euler(0f, _t * 2.6f, 0f); // drift relative to the surface
            }

            if (_ship != null)
            {
                _ship.localPosition = new Vector3(-2.5f, -1.5f + Mathf.Sin(_t * 0.6f) * 0.35f, 9f);
                _ship.localRotation = Quaternion.Euler(6f + Mathf.Sin(_t * 0.5f) * 2f, 28f + Mathf.Sin(_t * 0.3f) * 3f, -3f);

                // Blinking beacon.
                if (_beacon != null)
                {
                    bool on = Mathf.Sin(_t * 4f) > 0.4f;
                    _beacon.sharedMaterial.color = ShaderColor.Srgb(on ? new Color(1f, 0.35f, 0.35f) : new Color(0.3f, 0.06f, 0.06f));
                }

                // Engine flicker: glow length + light intensity pulse.
                float pulse = 0.8f + Mathf.Sin(_t * 14f) * 0.12f + Mathf.Sin(_t * 5f) * 0.08f;
                if (_engineGlowL != null) _engineGlowL.localScale = new Vector3(0.5f, 0.5f, 1.4f * pulse);
                if (_engineGlowR != null) _engineGlowR.localScale = new Vector3(0.5f, 0.5f, 1.4f * pulse);
                if (_engineMat != null) _engineMat.color = ShaderColor.Srgb(new Color(0.5f, 0.85f, 1f) * (0.9f + pulse * 0.2f));
                if (_engineLight != null) _engineLight.intensity = 1.8f + pulse * 0.8f;

                // Keep the (unscaled) thruster plume pinned to the ship's rear as it bobs/rolls.
                if (_thruster != null)
                {
                    Quaternion rot = _ship.localRotation;
                    _thruster.localPosition = _ship.localPosition + rot * (Vector3.back * (_shipExtent * _shipScale * 0.5f + 0.3f));
                    _thruster.localRotation = rot * Quaternion.Euler(0f, 180f, 0f);
                }
            }

            for (int i = 0; i < _asteroids.Length; i++)
            {
                var a = _asteroids[i];
                if (a != null)
                {
                    a.localRotation = Quaternion.Euler(_t * (12f + i * 3f), _t * (8f + i * 2f), 0f);
                    a.localPosition += new Vector3(0.06f + i * 0.004f, 0f, 0f) * Time.deltaTime;
                    if (a.localPosition.x > 18f)
                    {
                        a.localPosition = new Vector3(-18f, a.localPosition.y, a.localPosition.z);
                    }
                }
            }
        }

        /// <summary>Sets the space-sky shader globals so the block-atlas voxel ship is full-bright + directionally
        /// lit in the shell (no live <see cref="Sky"/> here); mirrors Sky's space-view branch. Headlamp off.</summary>
        private static void ApplySpaceLighting()
        {
            Shader.SetGlobalColor("_Sc_Light", Color.white);
            Shader.SetGlobalVector("_Sc_SunDir", SunDir); // light the voxel ship from the VISIBLE sun, not a third direction
            Shader.SetGlobalColor("_Sc_GradeTint", new Color(0f, 0f, 0f, 0f));
            Shader.SetGlobalColor("_Sc_FloraTint", new Color(0f, 0f, 0f, 0f));
            Shader.SetGlobalColor("_Sc_LampColor", new Color(0f, 0f, 0f, 0f)); // suit headlamp off
            Shader.SetGlobalFloat("_Sc_Indoor", 0f);
            Shader.SetGlobalVector("_Sc_Fog", new Vector4(0f, 1f, 0f, 0f)); // distance haze off
        }

        /// <summary>Builds the block texture atlas + chunk materials once for the menu (same recipe as
        /// GameBootstrap), so the real voxel ship can be meshed. No-op without content or the atlas shader.</summary>
        private void BuildRenderContext()
        {
            var content = Shell != null ? Shell.Content : null;
            var atlasShader = Shader.Find("BlocksBeyondTheStars/BlockAtlas");
            if (content == null || atlasShader == null)
            {
                return;
            }

            _atlas = new BlockTextureAtlas(content);
            _chunkMat = new Material(atlasShader) { mainTexture = _atlas.Texture };
            _chunkMat.SetTexture("_NormalTex", _atlas.NormalTexture);

            var transparentShader = Shader.Find("BlocksBeyondTheStars/BlockAtlasTransparent");
            if (transparentShader != null)
            {
                _chunkMatT = new Material(transparentShader) { mainTexture = _atlas.Texture };
            }
        }

        /// <summary>The system star as a LAYERED glow (coloured corona → near-white core → blazing white centre) at
        /// <see cref="SunDir"/>, billboarded toward the static menu camera — the same build the in-game space view
        /// uses (SpaceView.MakeSunLayer). No god-ray fan, to match what the game actually shows in space. Blooms via
        /// the post stack.</summary>
        private void BuildSun()
        {
            Vector3 pos = SunDir * SunDist;
            Quaternion billboard = Quaternion.LookRotation(-pos);
            Color sun = new Color(1f, 0.96f, 0.88f);

            var glowShader = Shader.Find("BlocksBeyondTheStars/SunGlow") ?? Shader.Find("Unlit/Color");
            Material Layer(Color c)
            {
                var m = new Material(glowShader) { mainTexture = SkyVisuals.GlowTexture() };
                m.SetColor("_Color", ShaderColor.Srgb(c));
                return m;
            }

            MakeQuad("SunCorona", pos, billboard, SunDist * 0.34f, Layer(sun));                              // outer corona (star colour)
            MakeQuad("SunCore", pos, billboard, SunDist * 0.18f, Layer(Color.Lerp(sun, Color.white, 0.85f))); // hot near-white core
            MakeQuad("SunCenter", pos, billboard, SunDist * 0.085f, Layer(Color.white));                      // blazing white centre

            // A directional-ish point light far along the sun direction so the lit primitives catch a warm key.
            var lightGo = new GameObject("SunLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = pos;
            var lamp = lightGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = sun;
            lamp.range = 1200f;
            lamp.intensity = 1.1f;
            lamp.shadows = LightShadows.None;
        }

        /// <summary>Builds the player's REAL voxel ship (block atlas + hull paint + engine glow) under this scene,
        /// scaled to read at the original ship's on-screen size. Returns null when the atlas/content isn't ready
        /// (caller falls back to the cube silhouette).</summary>
        private Transform BuildVoxelShipModel()
        {
            var content = Shell != null ? Shell.Content : null;
            if (content == null || _atlas == null || _chunkMat == null)
            {
                return null;
            }

            var design = BuildShipDesign(content);
            if (design == null)
            {
                return null;
            }

            Color hull = Shell != null && Shell.Settings != null ? Shell.Settings.HullColor : new Color(0.82f, 0.84f, 0.88f);
            var go = ShipMeshBuilder.BuildVoxelShip(content, _atlas, _chunkMat, _chunkMatT, transform, design, out float extent, hull);
            if (go == null)
            {
                return null;
            }

            float scale = 5.5f / Mathf.Max(1f, extent); // frame it to roughly the old cube ship's apparent length
            go.transform.localScale = Vector3.one * scale;
            _shipExtent = extent;
            _shipScale = scale;

            AttachShipFx(go.transform, extent);
            return go.transform;
        }

        /// <summary>Attaches engine glow quads + a pulsing point light + a top beacon to a (uniformly scaled) ship
        /// root, wiring the same fields the cube ship animates, plus an additive thruster plume on the unscaled root.</summary>
        private void AttachShipFx(Transform ship, float extent)
        {
            float rearZ = -extent * 0.5f; // ship-local (block) rear, scaled with the root

            _engineMat = Unlit(new Color(0.5f, 0.85f, 1f));
            _engineGlowL = Cube("GlowL", ship, new Vector3(-extent * 0.18f, 0f, rearZ - 0.7f), new Vector3(0.5f, 0.5f, 1.4f), _engineMat).transform;
            _engineGlowR = Cube("GlowR", ship, new Vector3(extent * 0.18f, 0f, rearZ - 0.7f), new Vector3(0.5f, 0.5f, 1.4f), _engineMat).transform;

            _beacon = Cube("Beacon", ship, new Vector3(0f, extent * 0.2f, 0f), Vector3.one * 0.4f, Unlit(new Color(1f, 0.3f, 0.3f))).GetComponent<Renderer>();

            var lightGo = new GameObject("EngineLight");
            lightGo.transform.SetParent(ship, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, rearZ - 0.5f);
            _engineLight = lightGo.AddComponent<Light>();
            _engineLight.type = LightType.Point;
            _engineLight.color = new Color(0.5f, 0.85f, 1f);
            _engineLight.range = 14f;
            _engineLight.intensity = 2.2f;
            _engineLight.shadows = LightShadows.None;

            _thruster = BuildThruster();
        }

        /// <summary>An additive exhaust plume (kept on the UNSCALED scene root so the ship's uniform scale doesn't
        /// shrink it; pinned to the rear each frame in Update). Returns null if no particle shader is present.</summary>
        private Transform BuildThruster()
        {
            var go = new GameObject("Thruster");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.startLifetime = 0.45f;
            main.startSpeed = 7f;
            main.startSize = 0.7f;
            main.startColor = new Color(0.5f, 0.85f, 1f, 0.85f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 140;

            var emission = ps.emission;
            emission.rateOverTime = 70f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 7f;
            shape.radius = 0.12f;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var pShader = Shader.Find("BlocksBeyondTheStars/Particle") ?? Shader.Find("Sprites/Default");
            if (pShader != null)
            {
                renderer.material = new Material(pShader);
            }

            ps.Play();
            return go.transform;
        }

        /// <summary>Authors a compact but real fighter as a sparse voxel <c>SpaceShipDesign</c> using actual block
        /// ids (iron_wall hull, glass canopy, cyan-glowing engine nozzles), so the menu renders it through the real
        /// atlas mesher. Returns null if the hull block is missing.</summary>
        private static BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign BuildShipDesign(GameContent content)
        {
            var hullDef = content.GetBlock("iron_wall");
            if (hullDef == null)
            {
                return null;
            }

            ushort hull = hullDef.NumericId.Value;
            var glassDef = content.GetBlock("glass");
            ushort glass = glassDef != null ? glassDef.NumericId.Value : hull;
            int glow = (0x40 << 16) | (0xC8 << 8) | 0xFF; // cyan engine glow

            var cells = new Dictionary<(int, int, int), (ushort Id, int Glow)>();
            void Add(int x, int y, int z, ushort id, int g = 0) => cells[(x, y, z)] = (id, g);

            // Fuselage (3 wide × 2 tall × 10 long) + a belly keel for depth.
            for (int z = 0; z <= 9; z++)
            for (int x = -1; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            {
                Add(x, y, z, hull);
            }

            for (int z = 1; z <= 8; z++)
            {
                Add(0, -1, z, hull);
            }

            // Nose taper.
            Add(0, 0, 10, hull);
            Add(0, 1, 10, hull);
            Add(0, 0, 11, hull);

            // Tail engine mount.
            for (int x = -1; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            {
                Add(x, y, -1, hull);
            }

            // Swept wings.
            for (int z = 2; z <= 5; z++)
            {
                Add(-2, 0, z, hull);
                Add(-3, 0, z, hull);
                Add(2, 0, z, hull);
                Add(3, 0, z, hull);
            }

            Add(-4, 0, 2, hull);
            Add(4, 0, 2, hull);

            // Vertical tail fin.
            Add(0, 2, 0, hull);
            Add(0, 2, 1, hull);

            // Glass canopy.
            for (int z = 6; z <= 8; z++)
            for (int x = -1; x <= 1; x++)
            {
                Add(x, 2, z, glass);
            }

            Add(0, 2, 5, glass);

            // Glowing engine nozzles.
            Add(-1, 0, -2, hull, glow);
            Add(1, 0, -2, hull, glow);

            int n = cells.Count;
            var xs = new int[n];
            var ys = new int[n];
            var zs = new int[n];
            var blocks = new ushort[n];
            var glows = new int[n];
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            bool anyGlow = false;
            int i = 0;
            foreach (var kv in cells)
            {
                var (cx, cy, cz) = kv.Key;
                xs[i] = cx; ys[i] = cy; zs[i] = cz;
                blocks[i] = kv.Value.Id;
                glows[i] = kv.Value.Glow;
                if (kv.Value.Glow != 0) anyGlow = true;
                if (cx < minX) minX = cx; if (cy < minY) minY = cy; if (cz < minZ) minZ = cz;
                if (cx > maxX) maxX = cx; if (cy > maxY) maxY = cy; if (cz > maxZ) maxZ = cz;
                i++;
            }

            return new BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign
            {
                Id = "menu:ship",
                Kind = "ship",
                X = xs,
                Y = ys,
                Z = zs,
                Block = blocks,
                Glow = anyGlow ? glows : System.Array.Empty<int>(),
                Width = maxX - minX + 1,
                Height = maxY - minY + 1,
                Length = maxZ - minZ + 1,
            };
        }

        private Transform BuildShip(Texture2D hullTex)
        {
            var ship = new GameObject("Ship").transform;
            ship.SetParent(transform, false);
            var hull = Lit(new Color(0.62f, 0.64f, 0.70f), hullTex);
            var glass = Unlit(new Color(0.3f, 0.7f, 0.95f));
            var engine = Unlit(new Color(0.35f, 0.8f, 1f));

            Cube("Body", ship, new Vector3(0f, 0f, 0f), new Vector3(2.0f, 1.0f, 4.2f), hull);
            Cube("WingL", ship, new Vector3(-1.6f, 0f, -0.4f), new Vector3(1.4f, 0.25f, 1.7f), hull);
            Cube("WingR", ship, new Vector3(1.6f, 0f, -0.4f), new Vector3(1.4f, 0.25f, 1.7f), hull);
            Cube("Cockpit", ship, new Vector3(0f, 0.5f, 1.5f), new Vector3(1.1f, 0.6f, 1.3f), glass);
            Cube("EngL", ship, new Vector3(-0.7f, 0f, -2.4f), new Vector3(0.7f, 0.7f, 0.6f), engine);
            Cube("EngR", ship, new Vector3(0.7f, 0f, -2.4f), new Vector3(0.7f, 0.7f, 0.6f), engine);

            // Navigation lights: red to port (left), green to starboard (right), white headlights up front.
            Cube("NavRed", ship, new Vector3(-2.25f, 0.05f, -0.4f), Vector3.one * 0.28f, Unlit(new Color(1f, 0.25f, 0.25f)));
            Cube("NavGreen", ship, new Vector3(2.25f, 0.05f, -0.4f), Vector3.one * 0.28f, Unlit(new Color(0.3f, 1f, 0.4f)));
            Cube("HeadL", ship, new Vector3(-0.5f, -0.15f, 2.15f), Vector3.one * 0.22f, Unlit(new Color(1f, 1f, 0.92f)));
            Cube("HeadR", ship, new Vector3(0.5f, -0.15f, 2.15f), Vector3.one * 0.22f, Unlit(new Color(1f, 1f, 0.92f)));

            // Blinking top beacon.
            _beacon = Cube("Beacon", ship, new Vector3(0f, 0.7f, 0.2f), Vector3.one * 0.2f, Unlit(new Color(1f, 0.3f, 0.3f))).GetComponent<Renderer>();

            // Glowing engine exhaust trails + a pulsing point light.
            _engineMat = Unlit(new Color(0.5f, 0.85f, 1f));
            _engineGlowL = Cube("GlowL", ship, new Vector3(-0.7f, 0f, -3.1f), new Vector3(0.5f, 0.5f, 1.4f), _engineMat).transform;
            _engineGlowR = Cube("GlowR", ship, new Vector3(0.7f, 0f, -3.1f), new Vector3(0.5f, 0.5f, 1.4f), _engineMat).transform;

            var lightGo = new GameObject("EngineLight");
            lightGo.transform.SetParent(ship, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, -3f);
            _engineLight = lightGo.AddComponent<Light>();
            _engineLight.type = LightType.Point;
            _engineLight.color = new Color(0.5f, 0.85f, 1f);
            _engineLight.range = 14f;
            _engineLight.intensity = 2.2f;
            _engineLight.shadows = LightShadows.None;

            _shipExtent = 6f;
            _shipScale = 1f;
            _thruster = BuildThruster();
            return ship;
        }

        private Transform BuildCloudShell(Transform planet)
        {
            var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "CloudShell";
            StripCollider(shell);
            shell.transform.SetParent(planet, false);
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localScale = Vector3.one * 1.04f;

            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(shader) { mainTexture = CloudTexture() };
            mat.renderQueue = 3000;
            mat.SetColor(Shader.PropertyToID("_Color"), ShaderColor.Srgb(new Color(0.95f, 0.97f, 1f, 0.7f)));
            shell.GetComponent<Renderer>().sharedMaterial = mat;
            return shell.transform;
        }

        /// <summary>A thin translucent atmosphere shell over a planet — a faint bluish rim glow like the in-game
        /// orbit view, sitting just outside the cloud shell so the planet reads as having an atmosphere.</summary>
        private void BuildAtmosphereHaze(Transform planet, Color tint)
        {
            var haze = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            haze.name = "Atmosphere";
            StripCollider(haze);
            haze.transform.SetParent(planet, false);
            haze.transform.localPosition = Vector3.zero;
            haze.transform.localScale = Vector3.one * 1.06f;

            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(shader) { mainTexture = Texture2D.whiteTexture, renderQueue = 2999 };
            mat.SetColor(Shader.PropertyToID("_Color"), ShaderColor.Srgb(new Color(tint.r, tint.g, tint.b, 0.12f)));
            var mr = haze.GetComponent<Renderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static Texture2D CloudTexture()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n * Mathf.PI * 2f, v = y / (float)n * Mathf.PI * 2f;
                    float f = 0.5f + 0.25f * Mathf.Sin(u * 3f + Mathf.Sin(v * 2f))
                                   + 0.15f * Mathf.Sin(v * 4f + Mathf.Cos(u * 3f))
                                   + 0.10f * Mathf.Sin((u + v) * 5f);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((f - 0.55f) * 3f));
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        private static GameObject MakeSphere(string name, Vector3 pos, float scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            StripCollider(go);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>A camera-facing billboarded quad (collider stripped) for the sun disc / god-ray fan.</summary>
        private Transform MakeQuad(string name, Vector3 pos, Quaternion rot, float scale, Material mat)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            StripCollider(quad);
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = pos;
            quad.transform.localRotation = rot;
            quad.transform.localScale = Vector3.one * scale;
            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return quad.transform;
        }

        private static GameObject Cube(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
        }

        private static Material Unlit(Color c)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            return new Material(shader) { color = ShaderColor.Srgb(c) };
        }

        /// <summary>Lit material (fixed key light) with an optional tiled block texture.</summary>
        private static Material Lit(Color c, Texture2D tex = null, Vector2 tiling = default)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { color = ShaderColor.Srgb(c) };
            if (tex != null)
            {
                m.mainTexture = tex;
                if (tiling != default)
                {
                    m.mainTextureScale = tiling;
                }
            }

            return m;
        }

        /// <summary>Sun-lit-phase material for a celestial body (the same <c>SkyBodyPhase</c> shader the in-game orbit
        /// view uses), lit from the world direction <paramref name="sunDir"/> so a day/night terminator emerges
        /// instead of flat key light. Falls back to <see cref="Lit"/> when the phase shader isn't available.</summary>
        private static Material LitPhase(Color c, Vector3 sunDir, Texture2D tex = null, Vector2 tiling = default)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/SkyBodyPhase");
            if (shader == null)
            {
                return Lit(c, tex, tiling);
            }

            var m = new Material(shader) { color = ShaderColor.Srgb(c) };
            m.SetVector("_PhaseSunDir", sunDir.sqrMagnitude > 1e-6f ? (Vector4)sunDir.normalized : new Vector4(0f, 0f, 1f, 0f));
            if (tex != null)
            {
                m.mainTexture = tex;
                if (tiling != default)
                {
                    m.mainTextureScale = tiling;
                }
            }

            return m;
        }

        /// <summary>Loads a bundled block texture (Resources/textures/&lt;key&gt;.bytes raw 64x64 RGBA32,
        /// via LoadRawTextureData from the core module — no ImageConversion dependency).</summary>
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
    }
}
