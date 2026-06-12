using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Colour-space boundary helper (linear-migration): colours composed in C# are authored/tuned as
    /// sRGB values, but <see cref="Shader.SetGlobalColor"/> / <see cref="Material.SetColor"/> upload raw
    /// floats — Unity only auto-converts serialized (inspector) colours. Route every sRGB-authored colour
    /// that a shader multiplies into world rendering through <see cref="Srgb"/>, so the Linear pipeline
    /// receives the linear equivalent and the look matches the Gamma-era tuning. Engine-managed colour
    /// properties (Light.color, RenderSettings.*, Camera.backgroundColor, uGUI Graphic.color, the URP
    /// volume colorFilter) convert themselves — do NOT wrap those.
    /// </summary>
    internal static class ShaderColor
    {
        /// <summary>sRGB-authored colour → the value to upload to a shader uniform / mesh stream.
        /// Alpha passes through untouched (several globals carry flags/strength in alpha).
        /// No-op while the project renders in Gamma space.</summary>
        public static Color Srgb(Color c)
            => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
    }
}
