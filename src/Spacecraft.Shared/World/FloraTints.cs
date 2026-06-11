namespace Spacecraft.Shared.World;

/// <summary>
/// Per-(species × planet) flora colours: every flora species rolls ONE deterministic colour per world
/// (uniform within the world, different on the next), applied on top of the desaturated tile by the
/// block shader's tint channel. Pure function of (world seed, location key, block key) so server,
/// client and every player agree without any network traffic.
/// </summary>
public static class FloraTints
{
    /// <summary>The tint colour (RGB 0..1) for a flora block key on a given world. Hue is fully random
    /// per (species, world); saturation/value stay in a friendly band so plants remain readable and the
    /// shader's luminance-times-tint never blows out.</summary>
    public static (float R, float G, float B) For(long worldSeed, string locationKey, string blockKey)
    {
        ulong h = Hash($"{worldSeed}|{locationKey}|flora-tint|{blockKey}");
        float hue = (h % 3600UL) / 3600f;                       // 0..1 — anything goes, alien worlds allowed
        float sat = 0.45f + ((h >> 12) % 1000UL) / 1000f * 0.4f; // 0.45..0.85
        float val = 0.85f + ((h >> 24) % 1000UL) / 1000f * 0.3f; // 0.85..1.15
        return HsvToRgb(hue, sat, val);
    }

    /// <summary>FNV-1a (stable across platforms/runs — string.GetHashCode is randomized per process).</summary>
    private static ulong Hash(string s)
    {
        unchecked
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s)
            {
                h = (h ^ c) * 1099511628211UL;
            }

            return h;
        }
    }

    private static (float R, float G, float B) HsvToRgb(float h, float s, float v)
    {
        float c = v * s;
        float hp = h * 6f;
        float x = c * (1f - System.Math.Abs(hp % 2f - 1f));
        (float r, float g, float b) = ((int)hp % 6) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        float m = v - c;
        return (Clamp01(r + m), Clamp01(g + m), Clamp01(b + m));
    }

    private static float Clamp01(float f) => f < 0f ? 0f : f > 1f ? 1f : f;
}
