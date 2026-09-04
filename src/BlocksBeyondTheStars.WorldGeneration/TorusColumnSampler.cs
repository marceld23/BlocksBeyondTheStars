// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #1527: <see cref="Noise.ValueTorus"/> for one world column, one field at a time — bit-identical to the
/// original, only cheaper. For a fixed (worldX, worldZ) and field the torus projection (4 trig calls) and
/// therefore the lattice x/z cell, the two layer seeds of <c>Value5D</c>/<c>Value4D</c> and their blend
/// weights never change; only <c>worldY / scaleY</c> moves. And the eight corner hashes of <c>Value3D</c>
/// only change when the floored y crosses an integer — every 16 (caves), 9 (ore) or 4.5 (fine ore)
/// blocks — so the y-loop of a chunk re-evaluated 32 hashes + 4 trig calls per voxel for values that are
/// constant across most of the column.
/// <para>
/// The sampler stores exactly the doubles the original computed (<c>cx, cy, zx, zy</c> → floors, smooth
/// weights, layer seeds) and per lattice row the four x-interpolated corner pairs of each of the four
/// <c>Value3D</c> layers; <see cref="Sample"/> finishes with the very same <c>Lerp</c> chain in the same
/// order. Every intermediate is the same IEEE operation on the same operands, so the result is the same
/// bits — <c>TorusColumnSamplerTests</c> proves it against <see cref="Noise.ValueTorus"/> and the world-gen
/// goldens pin the chunks.
/// </para>
/// </summary>
public struct TorusColumnSampler
{
    // Value3D layer seeds: (Value5D layer A|B) × (Value4D layer A|B).
    private long _sAA, _sAB, _sBA, _sBB;
    private long _x0, _z0, _y0;
    private double _tx, _tz, _tw, _tv, _scaleY;
    // Per lattice row (current _y0): the x-lerped corner pairs (x00, x10, x01, x11) of each layer.
    private double _aa00, _aa10, _aa01, _aa11;
    private double _ab00, _ab10, _ab01, _ab11;
    private double _ba00, _ba10, _ba01, _ba11;
    private double _bb00, _bb10, _bb01, _bb11;

    /// <summary>False until the sampler has been built for the current column (see <see cref="ResetAll"/>).</summary>
    public bool Ready;

    /// <summary>Builds the column state exactly as <see cref="Noise.ValueTorus"/> derives it from its arguments.</summary>
    public TorusColumnSampler(long seed, double worldX, double worldZ, double circX, double circZ,
        double scaleX, double scaleY, double scaleZ)
    {
        // ValueTorus: the two circles of the torus projection.
        double thetaX = Noise.Tau * worldX / circX;
        double radX = circX / (scaleX * Noise.Tau);
        double cx = radX * System.Math.Cos(thetaX);
        double cy = radX * System.Math.Sin(thetaX);

        double thetaZ = Noise.Tau * worldZ / circZ;
        double radZ = circZ / (scaleZ * Noise.Tau);
        double zx = radZ * System.Math.Cos(thetaZ);
        double zy = radZ * System.Math.Sin(thetaZ);

        // Value5D(seed, x: cx, y: worldY / scaleY, z: zx, w: cy, v: zy)
        long v0 = (long)System.Math.Floor(zy);
        _tv = Noise.Smooth(zy - v0);
        // Value4D(sa|sb, x: cx, y, z: zx, w: cy)
        long w0 = (long)System.Math.Floor(cy);
        _tw = Noise.Smooth(cy - w0);
        unchecked
        {
            long sa5 = seed + v0 * Noise.Value5DLayerPrime;
            long sb5 = seed + (v0 + 1) * Noise.Value5DLayerPrime;
            _sAA = sa5 + w0 * Noise.Value4DLayerPrime;
            _sAB = sa5 + (w0 + 1) * Noise.Value4DLayerPrime;
            _sBA = sb5 + w0 * Noise.Value4DLayerPrime;
            _sBB = sb5 + (w0 + 1) * Noise.Value4DLayerPrime;
        }

        // Value3D(layer, x: cx, y, z: zx)
        _x0 = (long)System.Math.Floor(cx);
        _z0 = (long)System.Math.Floor(zx);
        _tx = Noise.Smooth(cx - _x0);
        _tz = Noise.Smooth(zx - _z0);
        _scaleY = scaleY;
        _y0 = long.MinValue;
        _aa00 = _aa10 = _aa01 = _aa11 = 0.0;
        _ab00 = _ab10 = _ab01 = _ab11 = 0.0;
        _ba00 = _ba10 = _ba01 = _ba11 = 0.0;
        _bb00 = _bb10 = _bb01 = _bb11 = 0.0;
        Ready = true;
    }

    /// <summary>The same value <see cref="Noise.ValueTorus"/> returns for this column at <paramref name="worldY"/>.</summary>
    public double Sample(double worldY)
    {
        double y = worldY / _scaleY;
        long y0 = (long)System.Math.Floor(y);
        double ty = Noise.Smooth(y - y0);
        if (y0 != _y0)
        {
            Refresh(y0);
        }

        // Value3D per layer, then Value4D's blend over w, then Value5D's blend over v — the original chain.
        double aa = Noise.Lerp(Noise.Lerp(_aa00, _aa10, ty), Noise.Lerp(_aa01, _aa11, ty), _tz);
        double ab = Noise.Lerp(Noise.Lerp(_ab00, _ab10, ty), Noise.Lerp(_ab01, _ab11, ty), _tz);
        double ba = Noise.Lerp(Noise.Lerp(_ba00, _ba10, ty), Noise.Lerp(_ba01, _ba11, ty), _tz);
        double bb = Noise.Lerp(Noise.Lerp(_bb00, _bb10, ty), Noise.Lerp(_bb01, _bb11, ty), _tz);
        return Noise.Lerp(Noise.Lerp(aa, ab, _tw), Noise.Lerp(ba, bb, _tw), _tv);
    }

    private void Refresh(long y0)
    {
        Corners(_sAA, y0, out _aa00, out _aa10, out _aa01, out _aa11);
        Corners(_sAB, y0, out _ab00, out _ab10, out _ab01, out _ab11);
        Corners(_sBA, y0, out _ba00, out _ba10, out _ba01, out _ba11);
        Corners(_sBB, y0, out _bb00, out _bb10, out _bb01, out _bb11);
        _y0 = y0;
    }

    /// <summary>Value3D's eight corners of the lattice cell (x0, y0, z0), pre-blended along x.</summary>
    private void Corners(long seed, long y0, out double x00, out double x10, out double x01, out double x11)
    {
        double c000 = Noise.Value01(seed, _x0, y0, _z0);
        double c100 = Noise.Value01(seed, _x0 + 1, y0, _z0);
        double c010 = Noise.Value01(seed, _x0, y0 + 1, _z0);
        double c110 = Noise.Value01(seed, _x0 + 1, y0 + 1, _z0);
        double c001 = Noise.Value01(seed, _x0, y0, _z0 + 1);
        double c101 = Noise.Value01(seed, _x0 + 1, y0, _z0 + 1);
        double c011 = Noise.Value01(seed, _x0, y0 + 1, _z0 + 1);
        double c111 = Noise.Value01(seed, _x0 + 1, y0 + 1, _z0 + 1);

        x00 = Noise.Lerp(c000, c100, _tx);
        x10 = Noise.Lerp(c010, c110, _tx);
        x01 = Noise.Lerp(c001, c101, _tx);
        x11 = Noise.Lerp(c011, c111, _tx);
    }

    /// <summary>Marks every slot stale — call at the start of each column; slots rebuild lazily on first use.</summary>
    public static void ResetAll(TorusColumnSampler[] samplers)
    {
        for (int i = 0; i < samplers.Length; i++)
        {
            samplers[i].Ready = false;
        }
    }
}
