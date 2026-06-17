using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Builds a global URP post-processing Volume at runtime (ACES tonemapping + bloom + vignette + a gentle
    /// colour grade) when URP is active — restoring (and improving) the cinematic look the old Built-in-RP
    /// <see cref="PostFx"/> gave via OnRenderImage, which URP's render graph can't run. No-op under Built-in RP
    /// (PostFx handles that). Wired up in WorldRig. The camera + URP asset have post-processing on by default.
    /// </summary>
    public sealed class UrpScenePost : MonoBehaviour
    {
        /// <summary>Set while active under URP so <see cref="Sky"/> can drive the per-system/biome grade
        /// (the URP replacement for the old PostComposite `_Sc_GradeTint` path).</summary>
        public static UrpScenePost Instance { get; private set; }

        /// <summary>For the menu-blur quick-win: while the in-game menu is open, a gaussian depth-of-field
        /// blurs the world behind the translucent panels (premium glass look + readable text).</summary>
        public GameBootstrap Game;

        /// <summary>Skip the dynamic comfort/event effects (alarm pulse, damage kick, bursts) for the
        /// accessibility "reduced effects" setting. Wired from WorldRig.</summary>
        public bool ReducedEffects;

        /// <summary>Quality preset — gates the cost-bearing look effects (lens flare from Medium, motion blur from
        /// High). Wired from WorldRig.</summary>
        public QualityPreset Preset = QualityPreset.Medium;

        /// <summary>Player look-effect toggles (mirror <see cref="ClientSettings"/>). Wired from WorldRig.</summary>
        public bool LensFlareEnabled = true;
        public bool MotionBlurEnabled = true;
        public bool VolumetricFogEnabled = true;

        private static readonly int VolFogId = Shader.PropertyToID("_VolFog");

        private ColorAdjustments _grade;
        private DepthOfField _menuBlur;
        private Vignette _vignette;
        private ChromaticAberration _chroma;
        private FilmGrain _grain;
        private MotionBlur _motionBlur;
        private ScreenSpaceLensFlare _lensFlare;
        private float _speed; // 0..1 camera-motion intensity driver for the motion blur (set via SetMotion)

        private const float BaseVignette = 0.26f;
        private float _damagePulse;   // decaying 0..1 → a red-tinted vignette kick on damage
        private float _oxygenAlarm;   // 0..1 low-O₂ alarm level (driven by HudUi each frame)
        private float _burstTimer, _burstDuration, _burstChroma, _burstGrain;

        private void Start()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                enabled = false; // Built-in RP → the OnRenderImage PostFx stack is in charge
                return;
            }

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.ACES); // filmic, matches the old PostComposite ACES curve

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(0.5f);
            bloom.scatter.Override(0.6f);

            _vignette = profile.Add<Vignette>(true);
            _vignette.intensity.Override(BaseVignette);
            _vignette.smoothness.Override(0.4f);

            // Event-burst effects (damaged visor, EMP, …): inactive until Burst() fires them.
            _chroma = profile.Add<ChromaticAberration>(false);
            _chroma.intensity.Override(0f);
            _grain = profile.Add<FilmGrain>(false);
            _grain.intensity.Override(0f);

            _grade = profile.Add<ColorAdjustments>(true);
            _grade.postExposure.Override(0.08f);
            _grade.contrast.Override(6f);
            _grade.saturation.Override(6f);
            _grade.colorFilter.Override(Color.white);

            // Cinematic teal-orange baseline: cool the shadows, warm the highlights a touch. Subtle and global —
            // the per-biome ApplyGrade colour filter still rides on top. This is the single biggest "looks like
            // a finished game" win per line; it shapes the whole frame's mood without touching gameplay clarity.
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.92f, 0.97f, 1.06f, 0f));    // shadows lean cool/blue
            smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f));
            smh.highlights.Override(new Vector4(1.05f, 1.0f, 0.93f, 0f));  // highlights lean warm

            // Screen-space lens flare: streaks/ghosts off the HDR-bright pixels (sun, engines, glow blocks,
            // force fields). Pure sci-fi flavour, ~free. Gated to Medium+ and the player toggle in Update.
            _lensFlare = profile.Add<ScreenSpaceLensFlare>(false);
            _lensFlare.intensity.Override(0.6f);
            _lensFlare.tintColor.Override(new Color(0.7f, 0.82f, 1f)); // cool sci-fi tint
            _lensFlare.firstFlareIntensity.Override(0.6f);
            _lensFlare.secondaryFlareIntensity.Override(0.5f);
            _lensFlare.warpedFlareIntensity.Override(0.5f);
            _lensFlare.streaksIntensity.Override(0.7f);

            // Camera motion blur for flight/driving speed. URP motion blur is camera-only (blurs on camera move),
            // so it stays dormant on foot and only smears when the ship/speeder swings the view. High+ only.
            _motionBlur = profile.Add<MotionBlur>(false);
            _motionBlur.intensity.Override(0f);
            _motionBlur.clamp.Override(0.05f);

            // Menu blur (graphics quick-win): a gaussian DoF that blurs everything past arm's length while
            // the in-game menu is open. Inactive during play; toggled in Update from Game.MenuOpen.
            _menuBlur = profile.Add<DepthOfField>(false);
            _menuBlur.mode.Override(DepthOfFieldMode.Gaussian);
            _menuBlur.gaussianStart.Override(0.4f);
            _menuBlur.gaussianEnd.Override(6f);
            _menuBlur.gaussianMaxRadius.Override(1.2f);

            var volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.profile = profile;
            Instance = this;
        }

        private void Update()
        {
            if (_menuBlur != null && Game != null)
            {
                bool open = Game.MenuOpen;
                if (_menuBlur.active != open)
                {
                    _menuBlur.active = open;
                }
            }

            // Volumetric fog master switch (the full-screen pass reads this global): the player toggle, from
            // Medium up (the depth texture it needs is off on Potato/Low). Sky.cs sets the per-world density;
            // this just gates the whole effect to a cheap passthrough when off.
            Shader.SetGlobalFloat(VolFogId, VolumetricFogEnabled && Preset >= QualityPreset.Medium ? 1f : 0f);

            // Lens flare: enabled by the player toggle from Medium upward.
            if (_lensFlare != null)
            {
                bool on = LensFlareEnabled && Preset >= QualityPreset.Medium;
                if (_lensFlare.active != on) { _lensFlare.active = on; }
            }

            // Motion blur: High+ and the toggle, never in reduced-effects. Intensity tracks the camera-speed
            // driver (SetMotion), so on-foot it's ~0 and only flight/driving smears the frame.
            if (_motionBlur != null)
            {
                bool allowed = MotionBlurEnabled && !ReducedEffects && Preset >= QualityPreset.High;
                float target = allowed ? Mathf.Clamp01(_speed) * 0.35f : 0f;
                bool on = target > 0.001f;
                if (_motionBlur.active != on) { _motionBlur.active = on; }
                if (on) { _motionBlur.intensity.Override(target); }
                _speed = Mathf.MoveTowards(_speed, 0f, Time.deltaTime * 1.5f); // decays unless SetMotion refreshes it
            }

            UpdateDynamicFx();
        }

        /// <summary>Feeds the camera-motion intensity (0..1) that scales the flight/driving motion blur. Call each
        /// frame from the ship/speeder controllers with normalized speed; it decays on its own when not refreshed.</summary>
        public void SetMotion(float intensity01) => _speed = Mathf.Max(_speed, Mathf.Clamp01(intensity01));

        /// <summary>Animates the runtime-driven effects each frame: the low-O₂ vignette pulse, the
        /// damage vignette kick (both gated by ReducedEffects) and the timed chroma/grain bursts.</summary>
        private void UpdateDynamicFx()
        {
            float dt = Time.deltaTime;
            _damagePulse = Mathf.MoveTowards(_damagePulse, 0f, dt * 2.2f);
            if (_burstTimer > 0f)
            {
                _burstTimer = Mathf.Max(0f, _burstTimer - dt);
            }

            if (_vignette != null)
            {
                float alarm = ReducedEffects ? 0f : _oxygenAlarm;
                // The pulse speeds up and deepens as oxygen runs out (3..7 Hz-ish breathing rhythm).
                float pulse = alarm > 0f
                    ? (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (3f + alarm * 4f))) * 0.12f * alarm
                    : 0f;
                float dmg = ReducedEffects ? 0f : _damagePulse * 0.16f;
                _vignette.intensity.Override(BaseVignette + pulse + dmg);

                Color c = Color.black;
                if (dmg > 0.001f)
                {
                    c = Color.Lerp(c, new Color(0.45f, 0.02f, 0.02f), Mathf.Clamp01(_damagePulse));
                }

                if (alarm > 0f)
                {
                    c = Color.Lerp(c, new Color(0.05f, 0.15f, 0.45f), Mathf.Clamp01(alarm * 0.8f));
                }

                _vignette.color.Override(c);
            }

            float k = !ReducedEffects && _burstTimer > 0f && _burstDuration > 0f ? _burstTimer / _burstDuration : 0f;
            if (_chroma != null)
            {
                bool on = k > 0f && _burstChroma > 0f;
                if (_chroma.active != on) { _chroma.active = on; }
                if (on) { _chroma.intensity.Override(_burstChroma * k); }
            }

            if (_grain != null)
            {
                bool on = k > 0f && _burstGrain > 0f;
                if (_grain.active != on) { _grain.active = on; }
                if (on) { _grain.intensity.Override(_burstGrain * k); }
            }
        }

        /// <summary>A short red vignette kick when the player takes damage (decays over ~0.5 s).</summary>
        public void PulseVignette(float strength)
            => _damagePulse = Mathf.Max(_damagePulse, Mathf.Clamp01(strength));

        /// <summary>Low-oxygen alarm level: 0 = off, 1 = critical. Drives a pulsing blue vignette.</summary>
        public void SetOxygenAlarm(float level01)
            => _oxygenAlarm = Mathf.Clamp01(level01);

        /// <summary>A timed chromatic-aberration / film-grain burst for events (damaged visor, EMP,
        /// entering an old wreck). Both intensities fade linearly to zero over the duration.</summary>
        public void Burst(float chroma, float grain, float duration)
        {
            _burstChroma = Mathf.Clamp01(chroma);
            _burstGrain = Mathf.Clamp01(grain);
            _burstDuration = _burstTimer = Mathf.Max(0.05f, duration);
        }

        /// <summary>Applies the per-system/biome "mood" grade (URP path of <c>Sky.SetGrade</c>): the blended
        /// biome×sun tint becomes the colour filter (folded by the same 0.7 strength the Built-in grade used),
        /// saturation/contrast map from the old multiplier form (1.0 = neutral) to URP's −100..100 scale.</summary>
        public void ApplyGrade(Color blendedTint, float saturation, float contrast)
        {
            if (_grade == null)
            {
                return;
            }

            _grade.colorFilter.Override(Color.Lerp(Color.white, blendedTint, 0.7f));
            _grade.saturation.Override(Mathf.Clamp((saturation - 1f) * 100f + 6f, -100f, 100f));
            _grade.contrast.Override(Mathf.Clamp((contrast - 1f) * 100f + 6f, -100f, 100f));
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
