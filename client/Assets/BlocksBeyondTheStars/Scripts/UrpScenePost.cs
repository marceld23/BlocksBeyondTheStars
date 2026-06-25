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

        private ColorAdjustments _grade;
        private ColorLookup _lut;
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

            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.ACES); // filmic ACES tonemap

            var bloom = profile.Add<Bloom>(true);
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
                bloom.threshold.Override(0.9f);
                bloom.intensity.Override(0.5f);
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
                bool open = Game.MenuOpen;
                if (_menuBlur.active != open)
                {
                    _menuBlur.active = open;
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

        /// <summary>Maps the 1.0-neutral brightness setting to a post-exposure stop offset. 1.0 keeps the tuned
        /// neutral (+0.08 EV); above/below lifts/drops the whole graded frame uniformly.</summary>
        private static float ExposureFor(float brightness) => 0.08f + (brightness - 1f) * 1.2f;

        /// <summary>Live brightness update from the settings slider — re-exposes the colour grade for every world.</summary>
        public void SetBrightness(float brightness)
        {
            Brightness = brightness;
            if (_grade != null)
            {
                _grade.postExposure.Override(ExposureFor(brightness));
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

            _lut.contribution.Override(MoodContribution);
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
        private static string MoodKey(string biome)
        {
            switch ((biome ?? string.Empty).ToLowerInvariant())
            {
                case "": return "";
                case "jungle": case "forest": return "jungle";
                case "desert": return "desert";
                case "ice": case "frozen": return "ice";
                case "lava": case "volcanic": return "lava";
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
