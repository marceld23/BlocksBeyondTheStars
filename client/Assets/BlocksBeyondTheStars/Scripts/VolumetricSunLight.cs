// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Volumetric sun light (#3060, the "Details (High)" epic #3066): real sunbeams in the air — through a forest canopy,
    /// out of a cave mouth, through a ship window or an open base door — on the High preset of a desktop client
    /// (<see cref="HighDetails.VolumetricLight"/>). Owns the <c>BlocksBeyondTheStars/VolumetricLight</c> material and the
    /// <see cref="VolumetricLightPass"/>; each frame it asks <see cref="VolumetricLightPolicy"/> whether the beams run (sun
    /// up, air or indoors, no flight view, no screen menu, not under water), eases them in and out, and enqueues the pass
    /// on the gameplay camera and the capture clones (<see cref="Sky.WorldCameras"/>) — never on the HUD, preview or probe
    /// cameras. #3232: the suit headlamps' beams ride the same pass, with their own target
    /// (<see cref="VolumetricLightPolicy.LampTargetFor"/>: at any hour, while a lamp is lit) and fade. Torches, lanterns and
    /// lava are the baked flood-fill light (ADR 0003) and get no beams. While the switch is on, the light-shaft cards stand down (<see cref="WorldRig.ShaftCardsFor"/>); the
    /// SunRays sky billboard stays.
    /// </summary>
    public sealed class VolumetricSunLight : MonoBehaviour
    {
        // ---- Tuning (subtle by default). The shader header (VolumetricLight.shader) says what each term does. ----

        /// <summary>Master gain of the added light.</summary>
        private const float Intensity = 0.45f;

        /// <summary>Haze density: the optical depth across the haze's far distance at full max haze. The open air's share
        /// of the beams — kept low, it mostly brightens the air toward the sun.</summary>
        private const float HazeDensity = 0.5f;

        /// <summary>Share of the height fog's density (0.035 per metre at full mist, as in the haze): morning valley mist.</summary>
        private const float MistShare = 0.6f;

        /// <summary>How much of the density also dims the light further along the same ray (never the scene).</summary>
        private const float Extinction = 0.5f;

        /// <summary>Dust per metre aboard and at home (the haze is off indoors), and how far from the camera it reaches —
        /// a room's worth, so it never veils the view out of a door.</summary>
        private const float IndoorDust = 0.12f;
        private const float IndoorDustReach = 14f;

        /// <summary>Dust per metre outdoors under a roof, a cave ceiling or a canopy, and its reach.</summary>
        private const float ShelterDust = 0.025f;
        private const float ShelterDustReach = 32f;

        /// <summary>Henyey-Greenstein forward lobe toward the sun, and its share of the phase (the rest scatters evenly).</summary>
        private const float PhaseG = 0.6f;
        private const float PhaseShare = 0.7f;

        /// <summary>Share of the haze's hue (the sky, warmed toward the sun) in the beam colour; the rest is the sun's light.</summary>
        private const float HazeTint = 0.35f;

        /// <summary>Cap on the light added to one pixel (linear), so a bright beam never blows the bloom out.</summary>
        private const float MaxAdd = 1.5f;

        /// <summary>Seconds the whole effect takes to fade in or out (sunrise, a menu, leaving the water).</summary>
        private const float FadeSeconds = 0.4f;

        /// <summary>Seconds the indoor dust takes to settle in when stepping aboard (and to clear when leaving).</summary>
        private const float DustFadeSeconds = 1f;

        /// <summary>When the beams are drawn. After the transparents, not before: the water's screen-space refraction
        /// writes an opaque result and would erase a beam in the air above a lake, and a window pane would dim a beam in
        /// the room in front of it. The depth texture is opaque-only either way.</summary>
        private const RenderPassEvent PassEvent = RenderPassEvent.AfterRenderingTransparents;

        /// <summary>Below this strength the pass is not drawn at all.</summary>
        private const float MinStrength = 0.002f;

        /// <summary>#3232: gain of the headlamp beams (× the LED colour × the lamps' own falloff — Headlamp.hlsl).</summary>
        private const float LampIntensity = 1.2f;

        /// <summary>#3232: the lamp phase's Henyey-Greenstein g (half of the phase scatters evenly in the shader).</summary>
        private const float LampPhaseG = 0.45f;

        private static readonly int SunDirId = Shader.PropertyToID("_Sc_FogSunDir");

        public GameBootstrap Game;
        public Camera Camera;
        public Sky Sky;

        private Material _mat;
        private VolumetricLightPass _pass;
        private float _strength;       // eased 0..1
        private float _lampStrength;   // #3232: the headlamp beams, eased 0..1
        private float _dust;           // eased density per metre
        private float _dustReach = IndoorDustReach;

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/VolumetricLight");
            if (GraphicsSettings.currentRenderPipeline == null || shader == null || !shader.isSupported)
            {
                // Built-in RP, a stripped build or a device without the URP SubShader: no beams, nothing else changes.
                Debug.Log("[VolumetricSunLight] shader unavailable — volumetric sun light off.");
                enabled = false;
                return;
            }

            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _pass = new VolumetricLightPass(_mat, PassEvent);
            if (HighDetails.DesktopClass)
            {
                Prewarm(shader);
            }
        }

        /// <summary>Compiles the march's shadow variants now — WorldRig builds this behind the world-loading veil, like
        /// <see cref="ShaderPrewarm"/> — so the first sunbeam of a session does not hitch. The shared FX list there does
        /// not fit: it warms only the keyword-less variant of passes with a LightMode tag, and these passes have none
        /// (SRPDefaultUnlit) while High runs the cascade variant.</summary>
        private static void Prewarm(Shader shader)
        {
            var collection = new ShaderVariantCollection();
            foreach (var keyword in new[] { "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE" })
            {
                try
                {
                    collection.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.ScriptableRenderPipelineDefaultUnlit, keyword));
                }
                catch (System.ArgumentException)
                {
                    // The build stripped that variant: nothing to warm.
                }
            }

            collection.WarmUp();
            Destroy(collection);
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;

        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            if (_mat != null)
            {
                Destroy(_mat);
            }
        }

        private void Update()
        {
            if (_pass == null || Game == null || Camera == null || !HighDetails.VolumetricLight)
            {
                _strength = 0f; // switched off (or a lower preset): gone at once, Medium and below stay untouched
                _lampStrength = 0f;
                _dust = 0f;
                return;
            }

            bool indoors = (Game.Aboard && !Game.InEva) || Game.AtHome;
            bool hazeOn = Sky == null || Sky.FogEnabled;
            var target = VolumetricLightPolicy.TargetFor(
                enabled: true,
                spaceView: Game.SpaceViewActive,
                menuOpen: Game.ScreenMenuOpen,
                air: Game.FogActive,
                hazeOn: hazeOn,
                indoors: indoors,
                sheltered: !Game.ExposedToSky,
                sunUp: Shader.GetGlobalVector(SunDirId).y,
                underwater: Sky.UnderwaterFog,
                weather: Game.Environment != null ? Game.Environment.Intensity : 0f);
            var lampTarget = VolumetricLightPolicy.LampTargetFor(true, Headlamp.AnyLit, Game.SpaceViewActive, Game.ScreenMenuOpen,
                Game.FogActive, hazeOn, indoors, !Game.ExposedToSky, Sky.UnderwaterFog); // #3232: the headlamp beams, at any hour

            // Unscaled time: the fades must finish while a menu has paused the world.
            float dt = Time.unscaledDeltaTime;
            _strength = Mathf.MoveTowards(_strength, target.Strength, dt / FadeSeconds);
            _lampStrength = Mathf.MoveTowards(_lampStrength, lampTarget.Strength, dt / FadeSeconds);
            var dust = target.Strength > 0f ? target.Dust : lampTarget.Dust; // the same rules; at night only the lamps ask
            float dustTarget = dust switch
            {
                VolumetricDust.Indoor => IndoorDust,
                VolumetricDust.Shelter => ShelterDust,
                _ => 0f,
            };
            if (dust != VolumetricDust.None)
            {
                _dustReach = dust == VolumetricDust.Indoor ? IndoorDustReach : ShelterDustReach;
            }

            _dust = Mathf.MoveTowards(_dust, dustTarget, IndoorDust * dt / DustFadeSeconds);
            LogState(target);

            _pass.March = new Vector4(0f, HazeDensity, MistShare, Extinction); // x: the pass puts the camera's shadow distance
            _pass.Dust = new Vector4(_dust, _dustReach, PhaseG, PhaseShare);
            _pass.Light = new Vector4(Intensity * _strength, HazeTint, MaxAdd, 0f);
            _pass.Lamp = new Vector4(LampIntensity * _lampStrength, LampPhaseG, Headlamp.Range, 0f);
        }

        private string _logged;

        /// <summary>One log line whenever the beams' state changes (strength in tenths, the dust kind) — the capture and
        /// bug-report logs show whether the effect ran at all.</summary>
        private void LogState(VolumetricLightTarget target)
        {
            string state = $"strength {target.Strength:0.0} dust {target.Dust}";
            if (state != _logged)
            {
                _logged = state;
                Debug.Log($"[VolumetricSunLight] {state}");
            }
        }

        /// <summary>Enqueues the pass on the world cameras only, right before each renders (code-only, like the visor
        /// composite — no renderer-asset feature, so the build script's renderer clean-up never touches it).</summary>
        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_pass == null || (_strength < MinStrength && _lampStrength < MinStrength) || cam == null || (cam != Camera && !Sky.WorldCameras.Contains(cam)))
            {
                return;
            }

            cam.GetUniversalAdditionalCameraData()?.scriptableRenderer?.EnqueuePass(_pass);
        }
    }
}
