// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The watchable generic intro (#759): a ~28 s real-time space cinematic between the title splash and
    /// the main menu — starfield/nebula reveal, a slow pan past the sun while a voxel ship crosses the
    /// frame, a planet approach, and a white-flash hand-off into the menu. Rendered live with the same
    /// systems the game uses (<see cref="Starfield"/>/<see cref="NebulaField"/>/<see cref="ShipMeshBuilder"/>/
    /// <see cref="UrpScenePost"/>, the <see cref="MenuBackground"/> recipes), so it always matches current
    /// art and works on WebGL. Plays once per install (<see cref="ClientSettings.IntroSeen"/>), is skippable
    /// any time (Esc immediately, any key after a grace period), and is re-watchable from the Credits
    /// screen. Driven from <see cref="AppShell.Update"/> like the splashes; captions are re-localized every
    /// frame, so a late-arriving WebGL localizer self-heals.
    /// </summary>
    public sealed class IntroCinematic
    {
        // Legs: stars reveal → sun pan + ship crossing → planet approach → final push + flash.
        private static readonly CinematicTimeline Timeline = new CinematicTimeline(6f, 9f, 9f, 4f);

        // Caption windows (absolute seconds) — one card per phase of the sequence.
        private const float Card1Start = 1.5f, Card1End = 7f;
        private const float Card2Start = 10f, Card2End = 18f;
        private const float Card3Start = 19.5f, Card3End = 26.5f;
        private const float CaptionFade = 0.9f;

        // The visible sun sits a little left of the camera's sweep so the pan crosses it.
        private static readonly Vector3 SunDir = Quaternion.Euler(-14f, -15f, 0f) * Vector3.forward;
        private const float SunDist = 260f;

        private static readonly Vector3 PlanetPos = new Vector3(46f, -12f, 143f); // yaw ≈ +18°, below eye line

        private readonly AppShell _shell;
        private bool _replay;
        private float _elapsed;
        private bool _shipBuildAttempted;

        private GameObject _root;
        private Camera _cam;
        private Transform _ship;
        private float _shipBaseScale = 1f;
        private Transform _planet, _clouds;
        private CinematicFrame _frame;

        // Menu-style render context for the voxel ship (freed with the rig, see #423).
        private BlockTextureAtlas _atlas;
        private Material _chunkMat, _chunkMatT;

        public IntroCinematic(AppShell shell) => _shell = shell;

        /// <summary>Arms the next run as a replay (Credits button): the seen flag is not stamped again.</summary>
        public void BeginReplay() => _replay = true;

        public void Update()
        {
            if (_shell.Phase != ShellPhase.Intro)
            {
                if (_root != null)
                {
                    DestroyRig();
                }

                return;
            }

            EnsureBuilt();

            // The browser streams its content in, so the rig is usually built before the ship design
            // exists — pick it up as soon as it lands instead of playing the cinematic without its
            // centrepiece (#831). One attempt per rig: content is either there by then or never.
            if (!_shipBuildAttempted && _shell.Content != null)
            {
                _shipBuildAttempted = true;
                BuildShip();
            }

            _elapsed += Time.deltaTime;
            Animate(_elapsed);

            bool skip = Input.GetKeyDown(KeyCode.Escape) || (_elapsed > 0.7f && Input.anyKeyDown);
            if (Timeline.Done(_elapsed) || skip)
            {
                Finish();
            }
        }

        private void Finish()
        {
            bool replay = _replay;
            _replay = false;
            _shell.OnIntroFinished(replay); // stamps IntroSeen (first run) and returns to the main menu
        }

        private void Animate(float t)
        {
            var (leg, progress) = Timeline.At(t);
            float eased = CinematicTimeline.EaseInOut(progress);

            // Camera: one continuous slow yaw sweep, per-leg targets; the last leg adds a dolly push.
            float yaw = leg switch
            {
                0 => Mathf.Lerp(-40f, -25f, eased),
                1 => Mathf.Lerp(-25f, 0f, eased),
                2 => Mathf.Lerp(0f, 18f, eased),
                _ => Mathf.Lerp(18f, 20f, eased),
            };
            var camT = _cam.transform;
            camT.localRotation = Quaternion.Euler(-3f, yaw, 0f);
            camT.localPosition = leg == 3
                ? Vector3.Lerp(Vector3.zero, (PlanetPos - Vector3.zero).normalized * 45f, eased)
                : Vector3.zero;

            AnimateShip(leg, eased, t);

            if (_planet != null)
            {
                _planet.localRotation = Quaternion.Euler(6f, t * 1.4f, 0f);
            }

            if (_clouds != null)
            {
                _clouds.localRotation = Quaternion.Euler(0f, t * 2.4f, 0f);
            }

            // Chrome: fade from black, letterbox in/out, captions, skip hint, final flash.
            _frame.SetFade(Mathf.Clamp01(1f - t / 2f));
            float letterIn = CinematicTimeline.EaseOut(Mathf.Clamp01(t / 1.6f));
            float letterOut = 1f - CinematicTimeline.EaseInOut(Mathf.Clamp01((t - (Timeline.Total - 1.2f)) / 1.2f));
            _frame.SetLetterbox(Mathf.Min(letterIn, letterOut));

            float a1 = CinematicTimeline.FadeWindow(t, Card1Start, Card1End, CaptionFade);
            float a2 = CinematicTimeline.FadeWindow(t, Card2Start, Card2End, CaptionFade);
            float a3 = CinematicTimeline.FadeWindow(t, Card3Start, Card3End, CaptionFade);
            if (a1 >= a2 && a1 >= a3)
            {
                _frame.SetCaption(_shell.L("ui.intro.card1"), a1);
            }
            else if (a2 >= a3)
            {
                _frame.SetCaption(_shell.L("ui.intro.card2"), a2);
            }
            else
            {
                _frame.SetCaption(_shell.L("ui.intro.card3"), a3);
            }

            _frame.SetHint(_shell.L("ui.intro.skip"), CinematicTimeline.FadeWindow(t, 2f, Timeline.Total - 1f, 1f) * 0.6f);

            // White reveal flash over the last 1.2 s — the hand-off lands on the menu at the peak.
            _frame.SetFlash(Mathf.Clamp01((t - (Timeline.Total - 1.2f)) / 1.2f) * 0.85f);
        }

        private void AnimateShip(int leg, float eased, float t)
        {
            if (_ship == null)
            {
                return;
            }

            // Off-screen until the sun-pan leg; then one continuous crossing toward the planet.
            Vector3 crossStart = new Vector3(-34f, -6f, 22f);
            Vector3 crossMid = new Vector3(10f, -3f, 42f);
            Vector3 nearPlanet = PlanetPos + new Vector3(-8f, 5f, -18f);

            Vector3 pos;
            float scale;
            switch (leg)
            {
                case 0:
                    _ship.gameObject.SetActive(false);
                    return;
                case 1:
                    pos = Vector3.Lerp(crossStart, crossMid, eased);
                    scale = 1f;
                    break;
                case 2:
                    pos = Vector3.Lerp(crossMid, nearPlanet, eased);
                    scale = Mathf.Lerp(1f, 0.2f, eased);
                    break;
                default:
                    pos = Vector3.Lerp(nearPlanet, PlanetPos, eased * 0.5f);
                    scale = Mathf.Lerp(0.2f, 0.06f, eased);
                    break;
            }

            if (!_ship.gameObject.activeSelf)
            {
                _ship.gameObject.SetActive(true);
            }

            Vector3 heading = leg == 1 ? (crossMid - crossStart) : (nearPlanet - crossMid);
            _ship.localPosition = pos + new Vector3(0f, Mathf.Sin(t * 0.8f) * 0.4f, 0f); // gentle drift
            _ship.localRotation = Quaternion.LookRotation(heading.normalized) * Quaternion.Euler(2f, 0f, -4f);
            _ship.localScale = Vector3.one * (_shipBaseScale * scale);
        }

        private void EnsureBuilt()
        {
            if (_root != null)
            {
                return;
            }

            _elapsed = 0f;
            _root = new GameObject("IntroCinematic");

            var camGo = new GameObject("IntroCamera");
            camGo.transform.SetParent(_root.transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.01f, 0.02f, 0.05f);
            _cam.farClipPlane = 700f;
            _cam.depth = 5f; // above the (deactivated) menu backdrop camera, defensive

            MenuBackground.ApplySpaceLighting();

            var stars = _root.AddComponent<Starfield>();
            stars.Camera = _cam;
            stars.MenuBrightness = 1f;
            var nebula = _root.AddComponent<NebulaField>();
            nebula.Camera = _cam;
            nebula.MenuBrightness = 0.9f;
            nebula.MenuSeed = 271; // its own sky — the menu behind uses seed 142

            BuildSun();
            BuildPlanet();
            _shipBuildAttempted = _shell.Content != null; // else Update retries once the content lands
            BuildShip();

            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
            {
                var post = _root.AddComponent<UrpScenePost>();
                post.Preset = QualityPreset.High;
                post.LensFlareEnabled = true;
                post.MotionBlurEnabled = false;
                post.ShellMode = true;

                var camData = _cam.GetUniversalAdditionalCameraData();
                if (camData != null)
                {
                    camData.renderPostProcessing = true;
                }
            }

            _frame = CinematicFrame.Create("IntroFrame", 66);
            _frame.transform.SetParent(_root.transform, false);
            Animate(0f);
        }

        /// <summary>The layered sun glow (corona → core → centre), the MenuBackground recipe.</summary>
        private void BuildSun()
        {
            Vector3 pos = SunDir * SunDist;
            Quaternion billboard = Quaternion.LookRotation(-pos);
            Color sun = new Color(1f, 0.95f, 0.86f);

            var glowShader = Shader.Find("BlocksBeyondTheStars/SunGlow") ?? Shader.Find("Unlit/Color");
            Material Layer(Color c)
            {
                var m = new Material(glowShader) { mainTexture = SkyVisuals.GlowTexture() };
                m.SetColor("_Color", ShaderColor.Srgb(c));
                return m;
            }

            Quad("SunCorona", pos, billboard, SunDist * 0.34f, Layer(sun));
            Quad("SunCore", pos, billboard, SunDist * 0.18f, Layer(Color.Lerp(sun, Color.white, 0.85f)));
            Quad("SunCenter", pos, billboard, SunDist * 0.085f, Layer(Color.white));

            var lightGo = new GameObject("SunLight");
            lightGo.transform.SetParent(_root.transform, false);
            lightGo.transform.localPosition = pos;
            var lamp = lightGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = sun;
            lamp.range = 1400f;
            lamp.intensity = 1.1f;
            lamp.shadows = LightShadows.None;
        }

        private void BuildPlanet()
        {
            Vector3 sunPos = SunDir * SunDist;

            var planetGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            planetGo.name = "IntroPlanet";
            Object.Destroy(planetGo.GetComponent<Collider>());
            planetGo.transform.SetParent(_root.transform, false);
            planetGo.transform.localPosition = PlanetPos;
            planetGo.transform.localScale = Vector3.one * 52f;
            planetGo.GetComponent<Renderer>().sharedMaterial =
                LitPhase(new Color(0.28f, 0.52f, 0.68f), sunPos - PlanetPos);
            _planet = planetGo.transform;

            // Cloud + atmosphere shells, the MenuBackground look.
            _clouds = Shell("CloudShell", 1.04f, new Color(0.95f, 0.97f, 1f, 0.7f), 3000);
            Shell("Atmosphere", 1.06f, new Color(0.55f, 0.75f, 1f, 0.12f), 2999);
        }

        private Transform Shell(string name, float scale, Color tint, int queue)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_planet, false);
            go.transform.localScale = Vector3.one * scale;

            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(shader) { mainTexture = Texture2D.whiteTexture, renderQueue = queue };
            mat.SetColor(Shader.PropertyToID("_Color"), ShaderColor.Srgb(tint));
            var mr = go.GetComponent<Renderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        /// <summary>The voxel fighter through the real atlas mesher (menu recipe), with a simple engine
        /// glow; hidden until its leg. Content not ready yet (the browser streams it) → no ship for now;
        /// <see cref="Update"/> calls this again the moment the content lands.</summary>
        private void BuildShip()
        {
            var content = _shell.Content;
            var atlasShader = Shader.Find("BlocksBeyondTheStars/BlockAtlas");
            if (content == null || atlasShader == null)
            {
                return;
            }

            var design = MenuBackground.BuildShipDesign(content);
            if (design == null)
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

            Color hull = _shell.Settings != null ? _shell.Settings.HullColor : new Color(0.82f, 0.84f, 0.88f);
            var go = ShipMeshBuilder.BuildVoxelShip(content, _atlas, _chunkMat, _chunkMatT, _root.transform,
                design, out float extent, hull);
            if (go == null)
            {
                return;
            }

            _shipBaseScale = 4.5f / Mathf.Max(1f, extent);
            go.transform.localScale = Vector3.one * _shipBaseScale;
            _ship = go.transform;
            _ship.gameObject.SetActive(false);

            // Engine glow: two additive-ish cubes + a point light at the rear (menu recipe, simplified).
            var glowMat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque"))
            {
                color = ShaderColor.Srgb(new Color(0.5f, 0.85f, 1f)),
            };
            EngineCube(new Vector3(-extent * 0.18f, 0f, -extent * 0.5f - 0.7f), glowMat);
            EngineCube(new Vector3(extent * 0.18f, 0f, -extent * 0.5f - 0.7f), glowMat);

            var lightGo = new GameObject("EngineLight");
            lightGo.transform.SetParent(_ship, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, -extent * 0.5f - 0.5f);
            var engineLight = lightGo.AddComponent<Light>();
            engineLight.type = LightType.Point;
            engineLight.color = new Color(0.5f, 0.85f, 1f);
            engineLight.range = 12f;
            engineLight.intensity = 2f;
            engineLight.shadows = LightShadows.None;
        }

        private void EngineCube(Vector3 localPos, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "EngineGlow";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_ship, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(0.5f, 0.5f, 1.6f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private Transform Quad(string name, Vector3 pos, Quaternion rot, float scale, Material mat)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_root.transform, false);
            quad.transform.localPosition = pos;
            quad.transform.localRotation = rot;
            quad.transform.localScale = Vector3.one * scale;
            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return quad.transform;
        }

        private Material LitPhase(Color c, Vector3 sunDir)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/SkyBodyPhase")
                         ?? Shader.Find("BlocksBeyondTheStars/LitColor")
                         ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { color = ShaderColor.Srgb(c) };
            if (m.HasProperty("_PhaseSunDir"))
            {
                m.SetVector("_PhaseSunDir",
                    sunDir.sqrMagnitude > 1e-6f ? (Vector4)sunDir.normalized : new Vector4(0f, 0f, 1f, 0f));
            }

            return m;
        }

        private void DestroyRig()
        {
            if (_frame != null)
            {
                Object.Destroy(_frame.gameObject);
                _frame = null;
            }

            Object.Destroy(_root);
            _root = null;
            _cam = null;
            _ship = null;
            _shipBuildAttempted = false;
            _planet = null;
            _clouds = null;

            // Free the intro's own atlas + materials (the #423 leak lesson from MenuBackground).
            if (_chunkMat != null)
            {
                Object.Destroy(_chunkMat);
                _chunkMat = null;
            }

            if (_chunkMatT != null)
            {
                Object.Destroy(_chunkMatT);
                _chunkMatT = null;
            }

            _atlas?.Destroy();
            _atlas = null;
        }
    }
}
