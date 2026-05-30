namespace Spacecraft.WorldGeneration;

/// <summary>
/// Dependency-free, deterministic value noise (hash-based) with fractal Brownian motion.
/// Lives in a netstandard2.1 library so the client and server generate identical worlds.
/// </summary>
public static class Noise
{
    /// <summary>Deterministic 64-bit hash of integer coordinates seeded by <paramref name="seed"/>.</summary>
    public static ulong Hash(long seed, long x, long y, long z)
    {
        unchecked
        {
            ulong h = (ulong)seed * 0x9E3779B97F4A7C15UL;
            h ^= (ulong)x * 0xC2B2AE3D27D4EB4FUL;
            h = (h << 31 | h >> 33) * 0x165667B19E3779F9UL;
            h ^= (ulong)y * 0x27D4EB2F165667C5UL;
            h = (h << 29 | h >> 35) * 0x85EBCA77C2B2AE63UL;
            h ^= (ulong)z * 0x9E3779B185EBCA87UL;
            h ^= h >> 33;
            h *= 0xFF51AFD7ED558CCDUL;
            h ^= h >> 33;
            return h;
        }
    }

    /// <summary>Deterministic pseudo-random double in [0, 1) for the given coordinates.</summary>
    public static double Value01(long seed, long x, long y, long z)
        => (Hash(seed, x, y, z) >> 11) * (1.0 / 9007199254740992.0); // 53-bit mantissa

    private static double Smooth(double t) => t * t * (3.0 - 2.0 * t);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>2D value noise in [0,1], sampled at continuous coordinates.</summary>
    public static double Value2D(long seed, double x, double z)
    {
        long x0 = (long)System.Math.Floor(x);
        long z0 = (long)System.Math.Floor(z);
        double tx = Smooth(x - x0);
        double tz = Smooth(z - z0);

        double v00 = Value01(seed, x0, 0, z0);
        double v10 = Value01(seed, x0 + 1, 0, z0);
        double v01 = Value01(seed, x0, 0, z0 + 1);
        double v11 = Value01(seed, x0 + 1, 0, z0 + 1);

        return Lerp(Lerp(v00, v10, tx), Lerp(v01, v11, tx), tz);
    }

    /// <summary>3D value noise in [0,1], sampled at continuous coordinates.</summary>
    public static double Value3D(long seed, double x, double y, double z)
    {
        long x0 = (long)System.Math.Floor(x);
        long y0 = (long)System.Math.Floor(y);
        long z0 = (long)System.Math.Floor(z);
        double tx = Smooth(x - x0);
        double ty = Smooth(y - y0);
        double tz = Smooth(z - z0);

        double c000 = Value01(seed, x0, y0, z0);
        double c100 = Value01(seed, x0 + 1, y0, z0);
        double c010 = Value01(seed, x0, y0 + 1, z0);
        double c110 = Value01(seed, x0 + 1, y0 + 1, z0);
        double c001 = Value01(seed, x0, y0, z0 + 1);
        double c101 = Value01(seed, x0 + 1, y0, z0 + 1);
        double c011 = Value01(seed, x0, y0 + 1, z0 + 1);
        double c111 = Value01(seed, x0 + 1, y0 + 1, z0 + 1);

        double x00 = Lerp(c000, c100, tx);
        double x10 = Lerp(c010, c110, tx);
        double x01 = Lerp(c001, c101, tx);
        double x11 = Lerp(c011, c111, tx);
        return Lerp(Lerp(x00, x10, ty), Lerp(x01, x11, ty), tz);
    }

    /// <summary>Fractal Brownian motion (summed octaves) of 2D value noise, normalized to [0,1].</summary>
    public static double Fbm2D(long seed, double x, double z, int octaves, double lacunarity = 2.0, double gain = 0.5)
    {
        double sum = 0;
        double amp = 1;
        double freq = 1;
        double norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Value2D(seed + i * 1013, x * freq, z * freq);
            norm += amp;
            amp *= gain;
            freq *= lacunarity;
        }

        return norm > 0 ? sum / norm : 0;
    }
}
