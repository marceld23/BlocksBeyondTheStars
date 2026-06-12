namespace BlocksBeyondTheStars.WorldGeneration;

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

    private const double Tau = 2.0 * System.Math.PI;

    /// <summary>
    /// 4D value noise in [0,1]. Built from two <see cref="Value3D"/> layers (at integer w) smoothly
    /// interpolated along w, so it stays dependency-free without needing a 4-argument hash.
    /// </summary>
    public static double Value4D(long seed, double x, double y, double z, double w)
    {
        long w0 = (long)System.Math.Floor(w);
        double tw = Smooth(w - w0);
        unchecked
        {
            long sa = seed + w0 * 0x100000001B3L;       // FNV prime, distinct layer per integer w
            long sb = seed + (w0 + 1) * 0x100000001B3L;
            return Lerp(Value3D(sa, x, y, z), Value3D(sb, x, y, z), tw);
        }
    }

    /// <summary>
    /// FBM that is <b>exactly periodic in world-X with period <paramref name="circumference"/></b> while Z
    /// stays linear — the seam-free generator for a cylinder world. The X axis is mapped onto a circle of
    /// matching arc-length frequency (so local detail is unchanged), guaranteeing that the value <i>and its
    /// slope</i> are continuous across X = 0 ≡ X = circumference. <paramref name="scale"/> is the world-units
    /// per noise unit (the old <c>worldX / scale</c> divisor); octaves/lacunarity/gain match <see cref="Fbm2D"/>.
    /// </summary>
    public static double FbmCylX(long seed, double worldX, double worldZ, double circumference, double scale,
        int octaves, double lacunarity = 2.0, double gain = 0.5)
    {
        double theta = Tau * worldX / circumference;
        double radius = circumference / (scale * Tau); // arc-length per `scale` world units == 1 noise unit
        double cx = radius * System.Math.Cos(theta);
        double cy = radius * System.Math.Sin(theta);
        double z = worldZ / scale;

        double sum = 0, amp = 1, freq = 1, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            // Periodic for every freq because cos/sin repeat exactly each lap of worldX.
            sum += amp * Value3D(seed + i * 1013, cx * freq, z * freq, cy * freq);
            norm += amp;
            amp *= gain;
            freq *= lacunarity;
        }

        return norm > 0 ? sum / norm : 0;
    }

    /// <summary>
    /// Single-octave value noise that is <b>exactly periodic in world-X</b> (period
    /// <paramref name="circumference"/>) while Y and Z stay linear — the seam-free counterpart to a 3D
    /// <see cref="Value3D"/> field (caves, ore veins). <paramref name="scaleX"/>/<paramref name="scaleY"/>/
    /// <paramref name="scaleZ"/> are the original per-axis divisors.
    /// </summary>
    public static double ValueCylX(long seed, double worldX, double worldY, double worldZ, double circumference,
        double scaleX, double scaleY, double scaleZ)
    {
        double theta = Tau * worldX / circumference;
        double radius = circumference / (scaleX * Tau);
        double cx = radius * System.Math.Cos(theta);
        double cy = radius * System.Math.Sin(theta);
        return Value4D(seed, cx, worldY / scaleY, worldZ / scaleZ, cy);
    }

    /// <summary>
    /// 5D value noise in [0,1]: two <see cref="Value4D"/> layers (at integer v) interpolated along the fifth
    /// axis — same dependency-free layering trick as <see cref="Value4D"/>, with a distinct layer prime so
    /// the two stacked interpolations stay decorrelated. Used by the torus cave/ore noise (X-circle 2 dims +
    /// Y + Z-circle 2 dims).
    /// </summary>
    public static double Value5D(long seed, double x, double y, double z, double w, double v)
    {
        long v0 = (long)System.Math.Floor(v);
        double tv = Smooth(v - v0);
        unchecked
        {
            const long layerPrime = unchecked((long)0x9E3779B97F4A7C15UL); // distinct from Value4D's FNV prime
            long sa = seed + v0 * layerPrime;
            long sb = seed + (v0 + 1) * layerPrime;
            return Lerp(Value4D(sa, x, y, z, w), Value4D(sb, x, y, z, w), tv);
        }
    }

    /// <summary>
    /// FBM that is <b>exactly periodic in BOTH world axes</b> — X with period <paramref name="circX"/> and Z
    /// with period <paramref name="circZ"/> — the seam-free generator for a TORUS world (round worlds: walk
    /// any direction and loop seamlessly). Both axes are mapped onto circles of matching arc-length frequency
    /// (local detail unchanged), so value and slope are continuous across every seam, diagonals included.
    /// Replaces <see cref="FbmCylX"/> for round worlds.
    /// </summary>
    public static double FbmTorus(long seed, double worldX, double worldZ, double circX, double circZ,
        double scale, int octaves, double lacunarity = 2.0, double gain = 0.5)
    {
        double thetaX = Tau * worldX / circX;
        double radX = circX / (scale * Tau);
        double cx = radX * System.Math.Cos(thetaX);
        double cy = radX * System.Math.Sin(thetaX);

        double thetaZ = Tau * worldZ / circZ;
        double radZ = circZ / (scale * Tau);
        double zx = radZ * System.Math.Cos(thetaZ);
        double zy = radZ * System.Math.Sin(thetaZ);

        double sum = 0, amp = 1, freq = 1, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            // Periodic in both axes for every freq because cos/sin repeat exactly each lap.
            sum += amp * Value4D(seed + i * 1013, cx * freq, zx * freq, cy * freq, zy * freq);
            norm += amp;
            amp *= gain;
            freq *= lacunarity;
        }

        return norm > 0 ? sum / norm : 0;
    }

    /// <summary>
    /// Single-octave value noise <b>periodic in both world axes</b> (X period <paramref name="circX"/>, Z
    /// period <paramref name="circZ"/>) while Y stays linear — the torus counterpart of
    /// <see cref="ValueCylX"/> for caves/ore veins on round worlds.
    /// </summary>
    public static double ValueTorus(long seed, double worldX, double worldY, double worldZ,
        double circX, double circZ, double scaleX, double scaleY, double scaleZ)
    {
        double thetaX = Tau * worldX / circX;
        double radX = circX / (scaleX * Tau);
        double cx = radX * System.Math.Cos(thetaX);
        double cy = radX * System.Math.Sin(thetaX);

        double thetaZ = Tau * worldZ / circZ;
        double radZ = circZ / (scaleZ * Tau);
        double zx = radZ * System.Math.Cos(thetaZ);
        double zy = radZ * System.Math.Sin(thetaZ);

        return Value5D(seed, cx, worldY / scaleY, zx, cy, zy);
    }
}
