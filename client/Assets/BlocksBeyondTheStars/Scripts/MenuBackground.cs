// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The animated backdrop of the shell screens — behind the title splash, the main menu and the loading screen (#3705): the
    /// one fixed motif, the thunderbolt flying toward a "varied" world with clouds (<see cref="ShowcaseScene"/>, built from the
    /// game's own parts: the flight view's planet renderer and the thunderbolt's shipped layout), under the real twinkling
    /// <see cref="Starfield"/> and a <see cref="NebulaField"/> dome, through the cinematic post stack
    /// (<see cref="UrpScenePost"/>). The star stands to the right, a little behind the camera, so the planet turns its day side
    /// to the viewer and the ship's flank catches the light; the motif fills the free middle of the menu (title and buttons on
    /// the left, the cards on the right). Its own camera renders it, so the UI draws on top.
    /// <para>It stays the scene the automatic quality calibration measures (<see cref="AutoQualityCalibrator"/>) — simpler on
    /// Potato/Low (<see cref="ShowcaseScene"/>) — and it is what the poster capture photographs (<c>-capturePoster</c>, with
    /// <see cref="PosterFraming"/>: the motif centred under the logo, full detail).</para>
    /// </summary>
    public sealed class MenuBackground : MonoBehaviour
    {
        /// <summary>The shell — supplies the game content (loaded before this is spawned) and the settings. Set by
        /// <see cref="AppShell"/> right after AddComponent.</summary>
        public AppShell Shell;

        /// <summary>The backdrop on screen, or null (the poster capture waits on it).</summary>
        public static MenuBackground Active { get; private set; }

        /// <summary>Set by the poster capture before the backdrop is (re)built: the motif framed as a poster — centred, under
        /// the logo — at full detail whatever the preset.</summary>
        public static bool PosterFraming { get; set; }

        // --- The composition (art-tunable). The camera sits at the origin looking along +z (60° vertical field of view). ---

        /// <summary>Toward the star: from the right and a little behind the camera — the planet's day side faces the viewer.</summary>
        private static readonly Vector3 SunDir = new Vector3(0.82f, 0.32f, -0.48f).normalized;

        private static readonly Vector3 PlanetPos = new Vector3(10f, 0.5f, 48f); // right of centre, clear of the button column
        private const float PlanetDiameter = 32f;
        private static readonly Vector3 ShipPos = new Vector3(0.4f, -1.5f, 11f); // in front of the planet's night side
        private static readonly Vector3 ShipAngles = new Vector3(-4f, 40f, -6f); // nose toward the planet, flank to the light
        private const float ShipLength = 5.6f;

        /// <summary>The poster's camera turn: up and to the right, so the motif sits in the middle below the logo.</summary>
        private static readonly Vector3 PosterCameraAngles = new Vector3(-6f, 6f, 0f);

        private Camera _cam;
        private ShowcaseScene _scene;
        private float _t;

        /// <summary>True once the planet wears its world map and the ship is built — the poster capture waits for it.</summary>
        public bool Ready => _scene != null && _scene.MapReady && _scene.Ship != null;

        private void Start()
        {
            var camGo = new GameObject("MenuCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.015f, 0.025f, 0.06f);
            _cam.farClipPlane = 600f;
            _cam.transform.localPosition = Vector3.zero;
            _cam.transform.localRotation = PosterFraming ? Quaternion.Euler(PosterCameraAngles) : Quaternion.identity;

            // Space-sky shader globals so the voxel ship (block atlas) reads lit from the scene's star even though no live
            // Sky runs in the shell; mirrors what Sky does while the space view owns the camera.
            ApplySpaceLighting(SunDir);

            // Real twinkling starfield + per-system nebula dome (menu mode: fixed brightness, no live game).
            var stars = gameObject.AddComponent<Starfield>();
            stars.Camera = _cam;
            stars.MenuBrightness = 1f;
            var nebula = gameObject.AddComponent<NebulaField>();
            nebula.Camera = _cam;
            nebula.MenuBrightness = 0.9f; // near the in-game space brightness so the gas dome reads vivid, not washed out
            nebula.MenuSeed = 142;        // curated seed (art-tunable in-build): a colourful patch layout

            var settings = Shell != null ? Shell.Settings : null;
            var preset = PosterFraming ? QualityPreset.High : settings?.Preset ?? QualityPreset.Medium;
            bool calm = !PosterFraming && settings != null && settings.ReducedEffects;
            _scene = new ShowcaseScene(transform, Shell != null ? Shell.Content : null, preset, calm);
            _scene.BuildPlanet(PlanetPos, PlanetDiameter, SunDir);
            _scene.BuildShip(ShipLength, default); // the thunderbolt as shipped: one motif for everyone, no player paint
            PoseShip(0f);
            AddPost(settings, preset);
        }

        private void Update()
        {
            _t += Time.deltaTime;
            _scene?.Tick(Time.deltaTime);
            PoseShip(_t);
        }

        private void OnEnable()
        {
            Active = this;
            if (_cam != null)
            {
                ApplySpaceLighting(SunDir); // the intro film lit the blocks from its own star while this was parked
            }
        }

        private void OnDisable()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        /// <summary>Frees the scene's materials, textures and atlas lease — the backdrop is recreated on every return to the
        /// menu, so without this each cycle leaked a full second atlas (#423). The sky's own meshes and materials go with this
        /// GameObject and the return-to-menu <c>Resources.UnloadUnusedAssets</c> pass.</summary>
        private void OnDestroy()
        {
            _scene?.Dispose();
            _scene = null;
            if (Active == this)
            {
                Active = null;
            }
        }

        /// <summary>Sets the space-sky shader globals so the block-atlas voxel ship is full-bright and lit from
        /// <paramref name="sunDir"/> (toward the scene's star) in the shell — no live <see cref="Sky"/> runs here; mirrors
        /// Sky's space-view branch. Headlamps off. Internal: the intro cinematic (#759) renders the same kind of shell scene.</summary>
        internal static void ApplySpaceLighting(Vector3 sunDir)
        {
            Shader.SetGlobalColor("_Sc_Light", Color.white);
            Shader.SetGlobalVector("_Sc_SunDir", sunDir.normalized);
            Shader.SetGlobalColor("_Sc_FloraTint", new Color(0f, 0f, 0f, 0f));
            Headlamp.AllOff(); // suit headlamps off (#3229: the own and the other players')
            Shader.SetGlobalFloat("_Sc_Indoor", 0f);
            Shader.SetGlobalVector("_Sc_Fog", new Vector4(0f, 1f, 0f, 0f)); // distance haze off
        }

        /// <summary>The ship's gentle drift: a slow bob and sway around its pose (still with reduced effects).</summary>
        private void PoseShip(float t)
        {
            var ship = _scene != null ? _scene.Ship : null;
            if (ship == null)
            {
                return;
            }

            float k = _scene.Calm ? 0f : 1f;
            ship.localPosition = ShipPos + new Vector3(0f, Mathf.Sin(t * 0.6f) * 0.3f * k, 0f);
            ship.localRotation = Quaternion.Euler(
                ShipAngles.x + Mathf.Sin(t * 0.5f) * 1.5f * k,
                ShipAngles.y + Mathf.Sin(t * 0.3f) * 2.5f * k,
                ShipAngles.z);
        }

        /// <summary>Cinematic post (bloom + ACES tonemap + vignette + lens flare) — the same look the game uses, via the URP
        /// Volume. Respects the player's preset (#1421): Medium+ keeps the full attract look (High post), Low/Potato get their
        /// own cheap tier.</summary>
        private void AddPost(ClientSettings settings, QualityPreset preset)
        {
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
            {
                return;
            }

            var post = gameObject.AddComponent<UrpScenePost>();
            post.Preset = preset >= QualityPreset.Medium ? QualityPreset.High : preset;
            post.LensFlareEnabled = true;
            post.MotionBlurEnabled = false;
            post.ShellMode = true; // tighter bloom so the attract scene reads crisp, not hazy

            // A code-created URP camera has post-processing OFF by default — ApplyCameraLook turns it on and picks the
            // preset's renderer tier, or the global Volume never touches the menu scene.
            var camData = _cam.GetUniversalAdditionalCameraData();
            if (camData != null && settings != null)
            {
                settings.ApplyCameraLook(camData);
            }
            else if (camData != null)
            {
                camData.renderPostProcessing = true;
            }
        }
    }
}
