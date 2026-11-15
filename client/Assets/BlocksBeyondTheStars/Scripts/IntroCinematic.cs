// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The watchable generic intro (#759): a ~28 s real-time space cinematic between the title splash and
    /// the main menu — starfield/nebula reveal, a slow pan past the sun while the corvette crosses the
    /// frame, a planet approach, and a white-flash hand-off into the menu. Rendered live with the same
    /// systems the game uses (<see cref="Starfield"/>/<see cref="NebulaField"/>/<see cref="UrpScenePost"/>) and, since
    /// #3705, the menu's own motif (<see cref="ShowcaseScene"/>): the corvette from its shipped layout with its soft
    /// engine plumes, flying toward the "varied" world drawn by the flight view's planet renderer — its real map, its
    /// data clouds shaded toward the visible sun (no more milky white shell), its atmosphere rim. Same length, same
    /// beats, same captions; it always matches current art and works on WebGL. Plays once per install (<see cref="ClientSettings.IntroSeen"/>), is skippable
    /// any time on every device (Esc / the pad's cancel button / the on-screen skip button immediately, any key,
    /// pad button or tap after a grace period — #2461), and is re-watchable from the Credits
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
        private const float PlanetDiameter = 52f;
        private const float ShipLength = 4.5f; // the corvette's longest side at the start of its crossing

        private readonly AppShell _shell;
        private bool _replay;
        private float _elapsed;
        private bool _motifBuilt;

        private GameObject _root;
        private Camera _cam;
        private ShowcaseScene _scene;
        private Transform _ship;
        private float _shipBaseScale = 1f;
        private CinematicFrame _frame;

        private readonly System.Func<string, string> _localize; // the shell's L, cached for the per-frame skip label

        public IntroCinematic(AppShell shell)
        {
            _shell = shell;
            _localize = shell.L;
        }

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

            // The browser streams its content in, so the rig is usually built before the content exists — build the motif
            // (planet + corvette) as soon as it lands instead of playing the cinematic without its centrepiece (#831). One
            // attempt per rig: content is either there by then or never. The first leg is stars only, so it is rarely seen.
            if (!_motifBuilt && _shell.Content != null)
            {
                _motifBuilt = true;
                BuildMotif();
            }

            _scene?.Tick(Time.deltaTime); // the planet map bake, its slow turn and the cloud drift
            _elapsed += Time.deltaTime;
            Animate(_elapsed);

            // Skippable on every device (#2461): Esc and the pad's cancel button at once, the on-screen skip button (a
            // tap or a click), and after a short grace any key, pad button or tap — a touch-only player used to have
            // no way out but the full 28 s.
            bool skip = Input.GetKeyDown(KeyCode.Escape) || CinematicFrame.SkipPressed()
                        || (_frame != null && _frame.ConsumeSkipClick())
                        || (_elapsed > 0.7f && (Input.anyKeyDown || TouchBegan()));
            if (Timeline.Done(_elapsed) || skip)
            {
                Finish();
            }
        }

        /// <summary>A finger came down this frame.</summary>
        private static bool TouchBegan()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                if (Input.GetTouch(i).phase == TouchPhase.Began)
                {
                    return true;
                }
            }

            return false;
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

            _frame.SetHint(CinematicFrame.SkipLabel(_localize), CinematicTimeline.FadeWindow(t, 2f, Timeline.Total - 1f, 1f) * 0.6f);

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

            MenuBackground.ApplySpaceLighting(SunDir); // the blocks lit from the visible sun, like the planet

            var stars = _root.AddComponent<Starfield>();
            stars.Camera = _cam;
            stars.MenuBrightness = 1f;
            var nebula = _root.AddComponent<NebulaField>();
            nebula.Camera = _cam;
            nebula.MenuBrightness = 0.9f;
            nebula.MenuSeed = 271; // its own sky — the menu behind uses seed 142

            BuildSun();
            _motifBuilt = _shell.Content != null; // else Update builds it once the content lands
            if (_motifBuilt)
            {
                BuildMotif();
            }

            AddPost(_shell.Settings);
            _frame = CinematicFrame.Create("IntroFrame", 66);
            _frame.transform.SetParent(_root.transform, false);
            _frame.EnableSkipButton(); // tap or click to skip (#2461)
            GameBootstrap.Cutscenes.Begin(CutsceneGate.Intro); // the shared cutscene state (#2461) — ended in DestroyRig
            Animate(0f);
        }

        /// <summary>Respect the player's preset (#1421): Medium+ keeps the full cinematic look (High post), but a device already
        /// downgraded to Low/Potato must not have the shell force it back up — the forced-High post stack + the default
        /// full-res-SSAO renderer made the intro the single heaviest scene on weak browsers. ApplyCameraLook picks the same
        /// renderer tier as in-game.</summary>
        private void AddPost(ClientSettings settings)
        {
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null || settings == null)
            {
                return;
            }

            var post = _root.AddComponent<UrpScenePost>();
            post.Preset = settings.Preset >= QualityPreset.Medium ? QualityPreset.High : settings.Preset;
            post.LensFlareEnabled = true;
            post.MotionBlurEnabled = false;
            post.ShellMode = true;

            var camData = _cam.GetUniversalAdditionalCameraData();
            if (camData != null)
            {
                settings.ApplyCameraLook(camData);
            }
        }

        /// <summary>The layered sun glow (corona → core → centre), the flight view's recipe — the star the pan sweeps past and
        /// the one that lights the planet and the ship.</summary>
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
        }

        /// <summary>The menu's motif in the film's own framing (#3705): the "varied" world from the flight view's renderer, lit
        /// from the visible sun (its phase, the clouds' shading and the rim all agree with the star the pan sweeps past), and
        /// the corvette with its plumes, hidden until its leg.</summary>
        private void BuildMotif()
        {
            var settings = _shell.Settings;
            _scene = new ShowcaseScene(_root.transform, _shell.Content, settings != null ? settings.Preset : QualityPreset.Medium,
                settings != null && settings.ReducedEffects);
            Vector3 sunFromPlanet = SunDir * SunDist - PlanetPos;
            _scene.BuildPlanet(PlanetPos, PlanetDiameter, sunFromPlanet);
            _ship = _scene.BuildShip(ShipLength, default);
            if (_ship != null)
            {
                _shipBaseScale = _ship.localScale.x;
                _ship.gameObject.SetActive(false);
            }
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

        private void DestroyRig()
        {
            GameBootstrap.Cutscenes.End(CutsceneGate.Intro);
            if (_frame != null)
            {
                Object.Destroy(_frame.gameObject);
                _frame = null;
            }

            // Free the motif's atlas lease, materials and textures (the #423 leak lesson from MenuBackground).
            _scene?.Dispose();
            _scene = null;
            Object.Destroy(_root);
            _root = null;
            _cam = null;
            _ship = null;
            _motifBuilt = false;
        }
    }
}
