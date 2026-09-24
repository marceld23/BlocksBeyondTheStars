// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>What a face part of the flowerling is drawn as.</summary>
public enum FloralFacePart : byte
{
    Grin,  // the calm face: a dark smile bar and its two raised corners
    Maw,   // the hostile face: the dark-red open mouth
    Tooth, // white teeth in and under the maw
}

/// <summary>One box of the flowerling's face. <see cref="OnJaw"/> parts hang on the hinged jaw pivot (they drop
/// with it), the rest on the head pivot. Centre and size are in that pivot's local frame.</summary>
public readonly struct FloralFaceBox
{
    public FloralFaceBox(FloralFacePart part, bool onJaw, float cx, float cy, float cz, float sx, float sy, float sz)
    {
        Part = part;
        OnJaw = onJaw;
        CenterX = cx;
        CenterY = cy;
        CenterZ = cz;
        SizeX = sx;
        SizeY = sy;
        SizeZ = sz;
    }

    public FloralFacePart Part { get; }
    public bool OnJaw { get; }
    public float CenterX { get; }
    public float CenterY { get; }
    public float CenterZ { get; }
    public float SizeX { get; }
    public float SizeY { get; }
    public float SizeZ { get; }
}

/// <summary>
/// The flowerling's face (#1760, fixed by #1997), kept out of the Unity builder so its one rule is testable: every face
/// part must stand IN FRONT of the head. The first version put the maw and its teeth inside the jaw and upper-head cubes
/// and the calm grin 0.01 of the head depth behind the face, so neither mood ever showed.
///
/// The head is the one <c>CreatureBuilder.AddHeadBox</c> builds: an upper box (the top 70 %) centred at
/// (0, 0.15h, headZ) with depth d, and a hinged jaw (the lower 30 %) whose pivot sits at its rear, (0, −0.35h,
/// headZ − 0.44d), with its box 0.88d deep in front of the pivot. The face's front plane is headZ + d/2.
/// </summary>
public static class FloralFaceLayout
{
    /// <summary>The share of the head height that is jaw — the same constant the builder splits the head with.</summary>
    public const float JawShare = 0.3f;

    /// <summary>How far the jaw hangs open while the flowerling is angry (degrees): a snarl, not a yawn.</summary>
    public const float SnarlDeg = 18f;

    /// <summary>How far a face part stands proud of the face (fraction of the head depth).</summary>
    private const float Proud = 0.02f;

    /// <summary>The jaw pivot in the head pivot's frame — must match <c>AddHeadBox</c>.</summary>
    public static (float X, float Y, float Z) JawPivot(float h, float d, float headZ)
        => (0f, -h * (0.5f - JawShare * 0.5f), headZ - d * 0.44f);

    /// <summary>The front plane of the head (head pivot frame).</summary>
    public static float FaceFrontZ(float d, float headZ) => headZ + d * 0.5f;

    /// <summary>The face boxes for a head of width <paramref name="w"/>, height <paramref name="h"/>, depth
    /// <paramref name="d"/> centred at <paramref name="headZ"/>: a grin while calm, a maw with teeth while hostile.</summary>
    public static IReadOnlyList<FloralFaceBox> Build(float w, float h, float d, float headZ, bool hostile)
    {
        float front = FaceFrontZ(d, headZ);
        float plate = d * 0.04f;
        var boxes = new List<FloralFaceBox>(9);
        if (!hostile)
        {
            // A wide smile under the eyes with two raised corners, standing proud of the face.
            float z = front + d * Proud;
            boxes.Add(new FloralFaceBox(FloralFacePart.Grin, false, 0f, -h * 0.14f, z, w * 0.62f, h * 0.06f, plate));
            boxes.Add(new FloralFaceBox(FloralFacePart.Grin, false, -w * 0.31f, -h * 0.08f, z, w * 0.07f, h * 0.12f, plate));
            boxes.Add(new FloralFaceBox(FloralFacePart.Grin, false, w * 0.31f, -h * 0.08f, z, w * 0.07f, h * 0.12f, plate));
            return boxes;
        }

        // The open maw over the seam between head and jaw, the upper teeth hanging from its top edge.
        float mawZ = front + d * Proud;
        float mawTop = -h * 0.07f, mawBottom = -h * 0.33f;
        boxes.Add(new FloralFaceBox(FloralFacePart.Maw, false, 0f, (mawTop + mawBottom) * 0.5f, mawZ, w * 0.72f, mawTop - mawBottom, plate));
        float toothZ = mawZ + plate;
        for (int t = 0; t < 4; t++)
        {
            float tx = -w * 0.27f + w * 0.54f * t / 3f;
            boxes.Add(new FloralFaceBox(FloralFacePart.Tooth, false, tx, mawTop - h * 0.06f, toothZ, w * 0.09f, h * 0.12f, plate));
        }

        // The lower teeth ride on the jaw, standing on its front top edge — they drop with it when it snarls or bites.
        var (_, jy, jz) = JawPivot(h, d, headZ);
        float lowerZ = toothZ - jz; // the same plane as the upper teeth while the jaw is shut
        float lowerY = (-h * 0.18f) - jy;
        for (int t = 0; t < 3; t++)
        {
            float tx = -w * 0.2f + w * 0.4f * t / 2f;
            boxes.Add(new FloralFaceBox(FloralFacePart.Tooth, true, tx, lowerY, lowerZ, w * 0.09f, h * 0.1f, plate));
        }

        return boxes;
    }

    /// <summary>The front-most Z of a face box in the HEAD pivot's frame, with the jaw shut.</summary>
    public static float FrontZInHead(FloralFaceBox box, float h, float d, float headZ)
    {
        float z = box.CenterZ + box.SizeZ * 0.5f;
        return box.OnJaw ? z + JawPivot(h, d, headZ).Z : z;
    }
}
