// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>A capsule — the segment A→B swept by a sphere of <see cref="Radius"/>. The giants' hit and danger volumes.</summary>
public readonly struct GiantCapsule
{
    public GiantCapsule(Vector3f a, Vector3f b, float radius)
    {
        A = a;
        B = b;
        Radius = radius;
    }

    public Vector3f A { get; }
    public Vector3f B { get; }
    public float Radius { get; }

    /// <summary>The point on the capsule's core segment closest to <paramref name="p"/>.</summary>
    public Vector3f ClosestOnSegment(Vector3f p)
    {
        float abx = B.X - A.X, aby = B.Y - A.Y, abz = B.Z - A.Z;
        float len2 = abx * abx + aby * aby + abz * abz;
        float t = len2 < 1e-6f ? 0f : ((p.X - A.X) * abx + (p.Y - A.Y) * aby + (p.Z - A.Z) * abz) / len2;
        t = t < 0f ? 0f : t > 1f ? 1f : t;
        return new Vector3f(A.X + abx * t, A.Y + aby * t, A.Z + abz * t);
    }

    /// <summary>Distance from <paramref name="p"/> to the capsule's SURFACE (≤ 0 inside).</summary>
    public float Distance(Vector3f p)
    {
        var c = ClosestOnSegment(p);
        float dx = p.X - c.X, dy = p.Y - c.Y, dz = p.Z - c.Z;
        return (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz) - Radius;
    }

    /// <summary>The point on the capsule's surface nearest <paramref name="p"/> (the centre line when p is inside).</summary>
    public Vector3f ClosestSurfacePoint(Vector3f p)
    {
        var c = ClosestOnSegment(p);
        float dx = p.X - c.X, dy = p.Y - c.Y, dz = p.Z - c.Z;
        float d = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (d <= Radius || d < 1e-5f)
        {
            return p;
        }

        float k = Radius / d;
        return new Vector3f(c.X + dx * k, c.Y + dy * k, c.Z + dz * k);
    }
}

/// <summary>
/// The colossus's body layout (#1999) — one pure description the client builds its cubes from and the server takes its
/// hit capsules and stomp reach from, so what you see is what you hit. Local frame: origin on the ground under the body
/// centre, +Z forward (the nose), +X the right side, +Y up. <see cref="For"/> scales the proportions so the top of the
/// head sits at the species' <see cref="CreatureSpecies.GiantHeight"/>.
/// </summary>
public readonly struct ColossusBody
{
    private ColossusBody(float scale, float legLen, float legThick, float torsoY, float torsoH, float torsoW, float torsoL,
        float hipX, float hipZ, float neckSeg, int neck, float headSize, float headY, float headZ, float tailLen, float top)
    {
        Scale = scale;
        LegLength = legLen;
        LegThickness = legThick;
        TorsoY = torsoY;
        TorsoHeight = torsoH;
        TorsoWidth = torsoW;
        TorsoLength = torsoL;
        HipX = hipX;
        HipZ = hipZ;
        NeckSegment = neckSeg;
        NeckSegments = neck;
        HeadSize = headSize;
        HeadY = headY;
        HeadZ = headZ;
        TailLength = tailLen;
        Top = top;
    }

    /// <summary>World blocks per layout unit.</summary>
    public float Scale { get; }

    /// <summary>Hip height = the full leg (thigh + shin).</summary>
    public float LegLength { get; }
    public float LegThickness { get; }

    /// <summary>The torso box's centre height, height, width and length.</summary>
    public float TorsoY { get; }
    public float TorsoHeight { get; }
    public float TorsoWidth { get; }
    public float TorsoLength { get; }

    /// <summary>The hips sit at (±HipX, LegLength, ±HipZ).</summary>
    public float HipX { get; }
    public float HipZ { get; }

    /// <summary>One neck segment's length, and how many there are (0 = the head sits on the shoulders).</summary>
    public float NeckSegment { get; }
    public int NeckSegments { get; }

    public float HeadSize { get; }
    public float HeadY { get; }
    public float HeadZ { get; }
    public float TailLength { get; }

    /// <summary>The top of the body (the head's crown) — the species height.</summary>
    public float Top { get; }

    /// <summary>How far from under its hip a foot can come down (a stomp's reach).</summary>
    public float FootReach => LegLength * 0.45f;

    /// <summary>The neck's base (the torso's front top) in the local frame.</summary>
    public (float Y, float Z) NeckBase => (TorsoY + TorsoHeight * 0.3f, TorsoLength * 0.45f);

    /// <summary>The neck rises forward at this angle (degrees above the horizontal).</summary>
    public const float NeckPitchDeg = 50f;

    /// <summary>A leg's hip in the local frame: <paramref name="leg"/> 0..3 = front-left, front-right, rear-left, rear-right.</summary>
    public (float X, float Y, float Z) Hip(int leg)
    {
        int row = (leg >> 1) & 1, side = leg & 1;
        return (side == 0 ? -HipX : HipX, LegLength, row == 0 ? HipZ : -HipZ);
    }

    /// <summary>The layout for a colossus of <paramref name="height"/> blocks, leg ratio 0.8–1.3 and 0–4 neck segments.</summary>
    public static ColossusBody For(float height, float legRatio, int neckSegments)
    {
        float lr = legRatio < 0.6f ? 0.6f : legRatio > 1.5f ? 1.5f : legRatio;
        int neck = neckSegments < 0 ? 0 : neckSegments > 4 ? 4 : neckSegments;

        // Nominal proportions in layout units: a long-legged, heavy-bodied giant.
        float legLen = 10f * lr;
        float torsoH = 5f, torsoW = 6.5f, torsoL = 16f;
        float torsoY = legLen + torsoH * 0.35f;
        float neckSeg = 3f;
        float headSize = 4f;
        float pitch = NeckPitchDeg * (float)(System.Math.PI / 180.0);
        float baseY = torsoY + torsoH * 0.3f, baseZ = torsoL * 0.45f;
        float headY, headZ;
        if (neck > 0)
        {
            headY = baseY + neck * neckSeg * (float)System.Math.Sin(pitch) + headSize * 0.35f;
            headZ = baseZ + neck * neckSeg * (float)System.Math.Cos(pitch) + headSize * 0.35f;
        }
        else
        {
            headY = torsoY + torsoH * 0.25f;
            headZ = torsoL * 0.5f + headSize * 0.4f;
        }

        float top = System.Math.Max(headY + headSize * 0.5f, torsoY + torsoH * 0.5f);
        float s = (height <= 1f ? 40f : height) / top;
        return new ColossusBody(s, legLen * s, 1.7f * s, torsoY * s, torsoH * s, torsoW * s, torsoL * s,
            torsoW * 0.42f * s, torsoL * 0.36f * s, neckSeg * s, neck, headSize * s, headY * s, headZ * s, 8f * s, top * s);
    }

    /// <summary>Maps a local point to the world: <paramref name="facing"/> is the heading in radians (dirX = cos, dirZ = sin —
    /// the <see cref="LocomotionController"/> convention); local +Z runs along it and local +X to its right.</summary>
    public static Vector3f ToWorld(Vector3f position, float facing, float lx, float ly, float lz)
    {
        float fx = (float)System.Math.Cos(facing), fz = (float)System.Math.Sin(facing);
        // right = (fz, -fx): the Unity convention for a body looking along (fx, fz).
        return new Vector3f(position.X + fz * lx + fx * lz, position.Y + ly, position.Z - fx * lx + fz * lz);
    }

    /// <summary>The hit volumes in the world: four legs, the torso, the neck and the head(s).</summary>
    public List<GiantCapsule> Capsules(Vector3f position, float facing, int heads = 1)
    {
        var list = new List<GiantCapsule>(8);
        for (int leg = 0; leg < 4; leg++)
        {
            var (hx, hy, hz) = Hip(leg);
            list.Add(new GiantCapsule(ToWorld(position, facing, hx, 0f, hz), ToWorld(position, facing, hx, hy, hz), LegThickness * 0.6f));
        }

        float half = TorsoLength * 0.5f - TorsoHeight * 0.5f;
        list.Add(new GiantCapsule(ToWorld(position, facing, 0f, TorsoY, -half), ToWorld(position, facing, 0f, TorsoY, half),
            System.Math.Max(TorsoHeight, TorsoWidth) * 0.5f));
        var (ny, nz) = NeckBase;
        int n = heads < 1 ? 1 : heads > 3 ? 3 : heads;
        for (int h = 0; h < n; h++)
        {
            float hx = n == 1 ? 0f : (h - (n - 1) * 0.5f) * HeadSize * 1.2f;
            var head = ToWorld(position, facing, hx, HeadY, HeadZ);
            list.Add(new GiantCapsule(ToWorld(position, facing, hx * 0.5f, ny, nz), head, HeadSize * 0.45f));
        }

        return list;
    }
}
