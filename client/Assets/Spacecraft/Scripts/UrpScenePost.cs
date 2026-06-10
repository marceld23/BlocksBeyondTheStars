using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Spacecraft.Client
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

        private ColorAdjustments _grade;

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

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.26f);
            vignette.smoothness.Override(0.4f);

            _grade = profile.Add<ColorAdjustments>(true);
            _grade.postExposure.Override(0.08f);
            _grade.contrast.Override(6f);
            _grade.saturation.Override(6f);
            _grade.colorFilter.Override(Color.white);

            var volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.profile = profile;
            Instance = this;
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
