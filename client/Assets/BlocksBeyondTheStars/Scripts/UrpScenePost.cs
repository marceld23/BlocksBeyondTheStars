// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Builds a global URP post-processing Volume at runtime (ACES tonemapping + bloom + vignette + a gentle
    /// colour grade + per-biome mood LUT) — the cinematic look the game uses. The project always runs URP (every
    /// quality level assigns it). Wired up in WorldRig. The camera + URP asset have post-processing on by default.
    /// </summary>
    public sealed class UrpScenePost : MonoBehaviour
    {
        /// <summary>Set while active under URP so <see cref="Sky"/> can drive the per-system/biome grade
        /// (the colour-grade + mood-LUT path).</summary>
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

        /// <summary>Menu/splash attract scene: keep bloom tight so the emissive-dense backdrop (nebula dome, sun
        /// rays, engine glow, stars) reads crisp instead of washing into a soft haze. Set before Start runs.</summary>
        public bool ShellMode;

        /// <summary>Player look-effect toggles (mirror <see cref="ClientSettings"/>). Wired from WorldRig.</summary>
        public bool LensFlareEnabled = true;
        public bool MotionBlurEnabled = true;

        /// <summary>Global scene brightness (1.0 = neutral). Drives the colour grade's post-exposure so every world
        /// brightens/darkens uniformly. Wired from WorldRig; the settings slider pushes it live via <see cref="SetBrightness"/>.</summary>
        public float Brightness = 1.15f;

        /// <summary>The Cosiness slider (#2820), 0..1 (default 0.5): softer contrast, lifted shadows, warmth and a little
        /// more glow — scaled by the place (<see cref="SetCozyContext"/>), so the home stands out and the biomes keep
        /// their identity. Colour work only: URP bakes all of it into its grading LUT. Wired from WorldRig; the settings
        /// stepper pushes it live via <see cref="SetCosiness"/>.</summary>
        public float Cosiness = 0.5f;

        private ColorAdjustments _grade;
        private Bloom _bloom;
        private WhiteBalance _whiteBalance;
        private LiftGammaGain _liftGammaGain;
        private ShadowsMidtonesHighlights _smh;
        private float _menuBlurLevel; // #2810: eased 0..1 so the menu blur fades in instead of snapping

        // Cozy package (#2813/#2815/#2820/#2823): the place factor, the home blend and the evening share Sky feeds each
        // frame, and the rest level the bed drives — all 0..1 and already eased by their owners.
        private float _cozyPlace, _home, _evening, _rest;

        private static readonly Vector4 CoolShadows = new Vector4(0.92f, 0.97f, 1.06f, 0f);
        private static readonly Vector4 HomeShadows = new Vector4(1.03f, 1.0f, 0.95f, 0f);
        private static readonly Vector4 NeutralTone = new Vector4(1f, 1f, 1f, 0f);
        private static readonly int CozyLightId = Shader.PropertyToID("_Sc_CozyLight");
        private ColorLookup _lut;
        private DepthOfField _menuBlur;
        private Vignette _vignette;
        private ChromaticAberration _chroma;
        private FilmGrain _grain;
        private MotionBlur _motionBlur;
        private ScreenSpaceLensFlare _lensFlare;
        private float _speed; // 0..1 camera-motion intensity driver for the motion blur (set via SetMotion)

        private const float BaseVignette = 0.18f; // was 0.26 — with the AO stack it read as a dark, closed-in frame (#1457)
        private Tonemapping _tonemap;

        /// <summary>ACES on an HDR buffer, Neutral on an LDR one — ACES over clamped LDR pixels only darkens the
        /// midtones (#1457). Called from ClientSettings whenever the preset flips HDR.</summary>
        public void SetTonemapForHdr(bool hdr)
            => _tonemap?.mode.Override(hdr ? TonemappingMode.ACES : TonemappingMode.Neutral);
        private float _damagePulse;   // decaying 0..1 → a red-tinted vignette kick on damage
        private float _oxygenAlarm;   // 0..1 low-O₂ alarm level (driven by HudUi each frame)
        private float _burstTimer, _burstDuration, _burstChroma, _burstGrain;

        // Per-biome cinematic mood LUTs (WS4): procedural 2D strip LUTs baked once per biome and cached. The
        // ColorLookup volume folds the chosen one into the Uber post pass for ~free — a teal-orange tonal split +
        // contrast/saturation shaping the per-component ColourAdjustments grade can't express. Driven by Sky.
        private const float MoodContribution = 0.6f; // how strongly the mood LUT blends in (0..1)
        private const int LutSize = 32;              // MUST match the URP asset's m_ColorGradingLutSize
        private readonly Dictionary<string, Texture2D> _moodLuts = new Dictionary<string, Texture2D>();
        private string _activeMood;

        private void Start()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                enabled = false; // defensive: the project always runs URP, but bail cleanly if it somehow isn't
                return;
            }

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            _tonemap = profile.Add<Tonemapping>(true);
            // Filmic ACES needs an HDR buffer to have anything to roll off; on an LDR buffer (WebGL Low/Potato,
            // #1424) it only crushed the midtones of an already-clamped frame — the browser build read darker
            // and flatter than the same scene on desktop (#1457). Neutral keeps the midtones there.
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            SetTonemapForHdr(rp == null || rp.supportsHDR);

            var bloom = profile.Add<Bloom>(true);
            _bloom = bloom;
            if (ShellMode)
            {
                // Tighter bloom for the attract scene: higher threshold + lower intensity/scatter so only the
                // brightest cores glow and the backdrop stays sharp (the menu has far more emissive pixels than play).
                bloom.threshold.Override(1.1f);
                bloom.intensity.Override(0.28f);
                bloom.scatter.Override(0.4f);
            }
            else
            {
                bloom.threshold.Override(PlayBloomThreshold);
                bloom.intensity.Override(PlayBloomIntensity);
                bloom.scatter.Override(0.6f);
            }

            _vignette = profile.Add<Vignette>(true);
            _vignette.intensity.Override(BaseVignette);
            _vignette.smoothness.Override(0.4f);

            // Event-burst effects (damaged visor, EMP, …): inactive until Burst() fires them.
            _chroma = profile.Add<ChromaticAberration>(false);
            _chroma.intensity.Override(0f);
            _grain = profile.Add<FilmGrain>(false);
            _grain.intensity.Override(0f);

            _grade = profile.Add<ColorAdjustments>(true);
            _grade.postExposure.Override(ExposureFor(Brightness));
            _grade.contrast.Override(ContrastOffset); // was 6 — the extra contrast pushed the shadow half of every daylight frame down (#1457)
            _grade.saturation.Override(6f);
            _grade.colorFilter.Override(Color.white);

            // Cinematic teal-orange baseline: cool the shadows, warm the highlights a touch. Subtle and global —
            // the per-biome ApplyGrade colour filter still rides on top. This is the single biggest "looks like
            // a finished game" win per line; it shapes the whole frame's mood without touching gameplay clarity.
            // The cozy grade (#2820) eases the cool shadows toward neutral, and at home toward a warm lean.
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(CoolShadows);                             // shadows lean cool/blue
            smh.midtones.Override(NeutralTone);
            smh.highlights.Override(new Vector4(1.05f, 1.0f, 0.93f, 0f));  // highlights lean warm
            _smh = smh;

            // Cozy grade (#2820/#2813): warmth and a shadow lift, both at zero until the slider and the place ask for
            // them. Baked into URP's grading LUT with the rest, so they cost nothing per pixel — on WebGL/Potato too.
            _whiteBalance = profile.Add<WhiteBalance>(true);
            _whiteBalance.temperature.Override(0f);
            _whiteBalance.tint.Override(0f);
            _liftGammaGain = profile.Add<LiftGammaGain>(true);
            _liftGammaGain.lift.Override(NeutralTone);

            // Per-biome cinematic mood LUT (WS4): inactive until Sky picks a biome via SetMoodLut. The menu/attract
            // scene never calls it, so its backdrop stays ungraded by the LUT.
            _lut = profile.Add<ColorLookup>(false);
            _lut.contribution.Override(0f);

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
                bool open = Game.ScreenMenuOpen; // #2530: not under the taming panel — the creature must stay sharp
                // #2810: fade over ~0.25 s instead of snapping. The gaussian radius cannot go to zero, so the blur's
                // near edge walks in from far away: at 0 nothing on screen is blurred, at 1 everything past arm's length.
                _menuBlurLevel = Mathf.MoveTowards(_menuBlurLevel, open ? 1f : 0f, Time.unscaledDeltaTime * 4f);
                bool on = _menuBlurLevel > 0.001f;
                if (_menuBlur.active != on)
                {
                    _menuBlur.active = on;
                }

                if (on)
                {
                    float t = _menuBlurLevel * _menuBlurLevel * (3f - 2f * _menuBlurLevel);
                    _menuBlur.gaussianStart.Override(Mathf.Lerp(60f, 0.4f, t));
                    _menuBlur.gaussianEnd.Override(Mathf.Lerp(120f, 6f, t));
                }
            }

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
                // #2823: resting in bed — the frame closes in a little, like heavy eyelids.
                _vignette.intensity.Override(BaseVignette + pulse + dmg + _rest * 0.14f);

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
        /// saturation/contrast map from the old multiplier form (1.0 = neutral) to URP's −100..100 scale.
        /// A running memory flashback (#762) folds a desaturating, cooling pulse into the same write, so it
        /// composes with Sky's per-frame calls instead of fighting them.</summary>
        public void ApplyGrade(Color blendedTint, float saturation, float contrast)
        {
            _baseTint = blendedTint;
            _baseSaturation = saturation;
            _baseContrast = contrast;
            ApplyGradeNow();
        }

        private Color _baseTint = Color.white;
        private float _baseSaturation = 1f, _baseContrast = 1f;
        private float _flashback; // 0..1 memory-flashback level (#762): desaturates + cools the grade

        /// <summary>Memory-flashback level (#762): 0 = off, 1 = full desaturated/cooled recall look.
        /// Re-applies the stored base grade immediately, so it also works where Sky isn't driving.</summary>
        public void SetFlashback(float level01)
        {
            level01 = Mathf.Clamp01(level01);
            if (Mathf.Approximately(level01, _flashback))
            {
                return;
            }

            _flashback = level01;
            ApplyGradeNow();
        }

        /// <summary>The neutral world's ColorAdjustments contrast. #2803: the per-frame grade used to add +6 on top of the
        /// biome contrast, so the 6 → 3 softening of #1457 only ever reached the menu (in the world: 9 … 20).</summary>
        private const float ContrastOffset = 3f;

        private const float PlayBloomThreshold = 0.9f, PlayBloomIntensity = 0.5f;

        private static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>The slider at the current place (0..1): what the outdoor-facing cozy terms scale with.</summary>
        private float CozyAmount => ShellMode ? 0f : Mathf.Clamp01(Cosiness * _cozyPlace);

        /// <summary>The home grade's weight (0..1): the home blend, full from half the slider up, nothing at 0 % (#2813).</summary>
        private float HomeAmount => ShellMode ? 0f : _home * Mathf.Clamp01(Cosiness * 2f);

        private void ApplyGradeNow()
        {
            if (_grade == null)
            {
                return;
            }

            float k = CozyAmount;
            float h = HomeAmount;
            float soft = Smooth01(k / 0.6f);              // 0–60 %: softer contrast, lifted shadows
            float warm = Smooth01((k - 0.15f) / 0.6f);    // from 15 %: warmth
            float glow = Smooth01((k - 0.5f) / 0.5f);     // from 50 %: a little more glow
            float evening = _evening * h;

            // At home the planet's own grade fades out — the cabin does not take the lava world's tint or the ice
            // world's chill; the biome comes back as the hatch opens.
            float baseSat = Mathf.Lerp(_baseSaturation, 1f, h);
            float baseContrast = Mathf.Lerp(_baseContrast, 1f, h);
            Color baseTint = Color.Lerp(_baseTint, Color.white, h * 0.8f);

            float sat = Mathf.Lerp(baseSat, 0.25f, _flashback);
            Color tint = Color.Lerp(baseTint, new Color(0.78f, 0.86f, 0.98f), _flashback * 0.6f);
            _grade.colorFilter.Override(Color.Lerp(Color.white, tint, 0.7f));
            // A little extra saturation at home keeps the cabin's colours rich under the warmer white balance (without
            // it, a blue hatch washed to grey in the first capture).
            _grade.saturation.Override(Mathf.Clamp((sat - 1f) * 100f + 6f + 8f * h - 10f * _rest, -100f, 100f));
            _grade.contrast.Override(Mathf.Clamp((baseContrast - 1f) * 100f + ContrastOffset - 6f * soft - 3f * h, -100f, 100f));

            if (_whiteBalance != null)
            {
                _whiteBalance.temperature.Override(Mathf.Clamp(10f * warm + 5f * h + 12f * evening + 14f * _rest, -100f, 100f));
                _whiteBalance.tint.Override(0f);
            }

            if (_liftGammaGain != null)
            {
                // A small lift only: more turned the dark colours grey instead of soft.
                _liftGammaGain.lift.Override(new Vector4(1f, 1f, 1f, 0.02f * soft + 0.01f * h));
            }

            if (_smh != null)
            {
                Vector4 shadows = Vector4.Lerp(CoolShadows, NeutralTone, Mathf.Max(Smooth01((k - 0.2f) / 0.5f), h));
                _smh.shadows.Override(Vector4.Lerp(shadows, HomeShadows, h * 0.6f));
            }

            if (_bloom != null && !ShellMode)
            {
                _bloom.threshold.Override(Mathf.Lerp(PlayBloomThreshold, 0.8f, glow));
                _bloom.intensity.Override(Mathf.Lerp(PlayBloomIntensity, 0.62f, glow));
                _bloom.tint.Override(Color.Lerp(Color.white, new Color(1f, 0.93f, 0.82f), glow));
            }

            if (_lut != null && _lut.active)
            {
                _lut.contribution.Override(MoodContribution * (1f - 0.75f * h)); // the cool shadow split stays outside
            }

            // Lamps a touch stronger at the warm end of the slider (BlockAtlas multiplies the baked block light by it).
            Shader.SetGlobalFloat(CozyLightId, ShellMode ? 1f : 1f + 0.2f * glow + 0.1f * h);
            ApplyExposure();
        }

        /// <summary>The cosiness slider, live from the settings (#2820).</summary>
        public void SetCosiness(float cosiness01)
        {
            Cosiness = Mathf.Clamp01(cosiness01);
            ApplyGradeNow();
        }

        /// <summary>Per-frame cozy context from <see cref="Sky"/>: how cosy this place may get (0 = a guardian site … 1 =
        /// home), the eased home blend (#2813) and the evening share of the cabin light (#2815). Applied with the next
        /// <see cref="ApplyGrade"/>, which Sky calls right after.</summary>
        public void SetCozyContext(float place01, float home01, float evening01)
        {
            _cozyPlace = Mathf.Clamp01(place01);
            _home = Mathf.Clamp01(home01);
            _evening = Mathf.Clamp01(evening01);
        }

        /// <summary>Resting in a bed (#2823): 0 = awake … 1 = eyes closing — warmer, a little dimmer, a softer frame.
        /// The bed eases it; the grade applies it at once.</summary>
        public void SetRest(float level01)
        {
            level01 = Mathf.Clamp01(level01);
            if (Mathf.Approximately(level01, _rest))
            {
                return;
            }

            _rest = level01;
            ApplyGradeNow();
        }

        /// <summary>Maps the 1.0-neutral brightness setting to a post-exposure stop offset. 1.0 keeps the tuned
        /// neutral (+0.08 EV); above/below lifts/drops the whole graded frame uniformly.</summary>
        private static float ExposureFor(float brightness) => 0.08f + (brightness - 1f) * 1.2f;

        /// <summary>The cozy grade never darkens the frame (#2820): a warmer white balance takes some blue — and with it
        /// some luminance — so the exposure gives that back; the rest look (#2823) is the one deliberate dimming.</summary>
        private float CozyExposure()
        {
            float k = CozyAmount;
            float warm = Smooth01((k - 0.15f) / 0.6f);
            return 0.05f * warm + 0.04f * HomeAmount - 0.6f * _rest;
        }

        private float _adaptationEv; // #2395: the eye adaptation's offset (AtmosphereProbe), on top of the slider

        /// <summary>Live brightness update from the settings slider — re-exposes the colour grade for every world.</summary>
        public void SetBrightness(float brightness)
        {
            Brightness = brightness;
            ApplyExposure();
        }

        /// <summary>Eye adaptation (#2395): an exposure offset in EV the dark earns, eased by <see cref="AtmosphereProbe"/>
        /// (slow into the dark, quick back into the light). Composes with the brightness slider; 0 = no adaptation.</summary>
        public void SetAdaptation(float ev)
        {
            if (Mathf.Approximately(ev, _adaptationEv))
            {
                return;
            }

            _adaptationEv = ev;
            ApplyExposure();
        }

        private void ApplyExposure()
        {
            if (_grade != null)
            {
                _grade.postExposure.Override(ExposureFor(Brightness) + _adaptationEv + CozyExposure());
            }
        }

        /// <summary>Selects the cinematic mood LUT for the current biome (WS4): a teal-orange tonal split + contrast/
        /// saturation shaping layered on top of <see cref="ApplyGrade"/>. Bakes the LUT lazily (cached per biome) and
        /// folds it into the post pass. A null/empty biome (space view, menu) turns the LUT off. Called by <see cref="Sky"/>.</summary>
        public void SetMoodLut(string biome)
        {
            if (_lut == null)
            {
                return;
            }

            string key = MoodKey(biome);
            if (string.IsNullOrEmpty(key))
            {
                if (_lut.active) { _lut.active = false; }
                _lut.contribution.Override(0f);
                _activeMood = null;
                return;
            }

            if (key != _activeMood)
            {
                _activeMood = key;
                if (!_moodLuts.TryGetValue(key, out var tex) || tex == null)
                {
                    tex = BakeMoodLut(MoodFor(key));
                    _moodLuts[key] = tex;
                }

                _lut.texture.Override(tex);
            }

            _lut.contribution.Override(MoodContribution * (1f - 0.75f * HomeAmount)); // #2813: the cool split stays outside
            if (!_lut.active) { _lut.active = true; }
        }

        /// <summary>A biome's grade character: contrast + saturation around grey, an overall gain, and a shadow→highlight
        /// tint split (the cinematic lever the per-channel grade can't do). All values centre on 1.0 (= neutral).</summary>
        private readonly struct Mood
        {
            public readonly float Contrast, Saturation, Gain;
            public readonly Vector3 ShadowTint, HighlightTint;

            public Mood(float contrast, float saturation, float gain, Vector3 shadowTint, Vector3 highlightTint)
            {
                Contrast = contrast;
                Saturation = saturation;
                Gain = gain;
                ShadowTint = shadowTint;
                HighlightTint = highlightTint;
            }
        }

        /// <summary>Maps a biome string to a mood key (mirrors Sky.GradeFor's grouping). Null/empty → "" (LUT off);
        /// any other unmatched biome → the gentle "default" film look.</summary>
        private static string _moodBiome, _moodKeyCached; // #1554: called per frame from Sky.SetGrade

        private static string MoodKey(string biome)
        {
            if (_moodKeyCached != null && string.Equals(biome, _moodBiome, System.StringComparison.Ordinal))
            {
                return _moodKeyCached;
            }

            _moodBiome = biome;
            _moodKeyCached = MoodKeyUncached(biome);
            return _moodKeyCached;
        }

        private static string MoodKeyUncached(string biome)
        {
            switch ((biome ?? string.Empty).ToLowerInvariant())
            {
                case "": return "";
                case "jungle": case "forest": return "jungle";
                case "desert": return "desert";
                case "ice": case "frozen": return "ice";
                case "lava": case "volcanic": case "arena_nigra": return "lava"; // #2078: the black dunes take the volcanic mood
                case "gas_giant": return "ice"; // #2112: the cold mood for the gas giant's endless storm
                case "swamp": return "swamp";
                case "crystal": return "crystal";
                default: return "default";
            }
        }

        private static Mood MoodFor(string key)
        {
            switch (key)
            {
                case "jungle":  return new Mood(1.05f, 1.12f, 1.00f, new Vector3(0.95f, 1.00f, 1.00f), new Vector3(1.02f, 1.05f, 0.95f));
                case "desert":  return new Mood(1.12f, 0.98f, 1.01f, new Vector3(1.00f, 0.98f, 0.92f), new Vector3(1.08f, 1.02f, 0.88f));
                case "ice":     return new Mood(1.10f, 0.94f, 1.00f, new Vector3(0.92f, 0.99f, 1.10f), new Vector3(0.98f, 1.01f, 1.06f));
                case "lava":    return new Mood(1.16f, 1.08f, 1.00f, new Vector3(0.88f, 0.92f, 1.02f), new Vector3(1.12f, 0.98f, 0.86f));
                case "swamp":   return new Mood(1.02f, 0.85f, 0.99f, new Vector3(0.97f, 1.00f, 0.95f), new Vector3(1.00f, 1.02f, 0.93f));
                case "crystal": return new Mood(1.06f, 1.14f, 1.00f, new Vector3(1.00f, 0.95f, 1.08f), new Vector3(1.05f, 0.98f, 1.08f));
                default:        return new Mood(1.06f, 1.04f, 1.00f, new Vector3(0.96f, 0.99f, 1.06f), new Vector3(1.05f, 1.00f, 0.94f));
            }
        }

        /// <summary>Bakes a mood into a URP 2D strip LUT: a (size²×size) linear texture whose neutral identity is
        /// <c>(x%size, y, x/size)/(size-1)</c> — the exact layout URP's GetLutStripValue samples. URP looks the user
        /// LUT up in sRGB, so the grade is authored directly in display space; identity reproduces the frame.</summary>
        private static Texture2D BakeMoodLut(in Mood m)
        {
            int w = LutSize * LutSize; // 1024
            int h = LutSize;           // 32
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "MoodLut",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear, // ApplyLut2D relies on bilinear for the r/g interpolation
                hideFlags = HideFlags.HideAndDontSave,
            };

            var px = new Color32[w * h];
            float inv = 1f / (LutSize - 1);
            for (int y = 0; y < h; y++)
            {
                float g = y * inv;
                for (int x = 0; x < w; x++)
                {
                    float r = (x % LutSize) * inv;
                    float b = (x / LutSize) * inv;
                    Vector3 c = Grade(new Vector3(r, g, b), m);
                    px[y * w + x] = new Color32(
                        (byte)(Mathf.Clamp01(c.x) * 255f + 0.5f),
                        (byte)(Mathf.Clamp01(c.y) * 255f + 0.5f),
                        (byte)(Mathf.Clamp01(c.z) * 255f + 0.5f),
                        255);
                }
            }

            tex.SetPixels32(px);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
        }

        /// <summary>The film grade applied to one neutral LUT entry, in sRGB/display space: contrast and saturation
        /// around mid-grey, then a smooth shadow→highlight tint split, then gain. Caller clamps to [0,1].</summary>
        private static Vector3 Grade(Vector3 c, in Mood m)
        {
            c.x = (c.x - 0.5f) * m.Contrast + 0.5f;
            c.y = (c.y - 0.5f) * m.Contrast + 0.5f;
            c.z = (c.z - 0.5f) * m.Contrast + 0.5f;

            float luma = c.x * 0.2126f + c.y * 0.7152f + c.z * 0.0722f;
            c.x = Mathf.Lerp(luma, c.x, m.Saturation);
            c.y = Mathf.Lerp(luma, c.y, m.Saturation);
            c.z = Mathf.Lerp(luma, c.z, m.Saturation);

            float t = Mathf.Clamp01(luma);
            t = t * t * (3f - 2f * t); // smoothstep: weight toward highlights
            c.x *= Mathf.Lerp(m.ShadowTint.x, m.HighlightTint.x, t) * m.Gain;
            c.y *= Mathf.Lerp(m.ShadowTint.y, m.HighlightTint.y, t) * m.Gain;
            c.z *= Mathf.Lerp(m.ShadowTint.z, m.HighlightTint.z, t) * m.Gain;
            return c;
        }

        private void OnDestroy()
        {
            foreach (var tex in _moodLuts.Values)
            {
                if (tex != null) { Destroy(tex); }
            }

            _moodLuts.Clear();

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
