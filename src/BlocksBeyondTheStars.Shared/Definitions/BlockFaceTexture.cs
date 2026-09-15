// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// One texture slot of a block rendered as a built-in form (#1900): which part of the form and which face side it
/// covers, which tile it shows and — optionally — the region of that tile the face is stretched onto.
/// <para>
/// Without a slot every face shows the slice of the block's own tile it covers ("cut material"), which is right for
/// stone, wood or steel on any form. A tile that is a PICTURE of an object (the bed, the flower pot) only fits the
/// one face it was drawn for, so such a block names a region per part: the bed's two mattress tops each show half
/// of the drawn blanket, its boards the drawn frame.
/// </para>
/// </summary>
public sealed class BlockFaceTexture
{
    /// <summary>Part name (<see cref="ShapeParts.Name(ShapePart)"/>) or <c>"*"</c> for every part.</summary>
    public string Part { get; set; } = "*";

    /// <summary><c>"top"</c>, <c>"bottom"</c>, <c>"side"</c> or <c>"*"</c> for every side.</summary>
    public string Side { get; set; } = "*";

    /// <summary>Block key whose tile the face shows; null = the block's own tile.</summary>
    public string? Tile { get; set; }

    /// <summary>
    /// Optional region <c>[x0, y0, x1, y1]</c> in fractions of the tile IMAGE (0,0 = the top-left pixel as the PNG is
    /// drawn). The face is stretched onto it: (x0, y0) lands on the face's start corner and (x1, y1) on its end corner.
    /// A face's start is its lowest corner along its two axes — top and bottom faces run along X then Z, faces toward
    /// ±X along Z then height, faces toward ±Z along X then height (all in the form's own frame). So for a side face
    /// y0 is the row at its bottom edge and y1 the row at its top edge; swapping a pair mirrors that axis.
    /// Null = no stretching: the face shows the slice of the tile it covers.
    /// </summary>
    public float[]? Rect { get; set; }
}

/// <summary>Resolution and validation of <see cref="BlockFaceTexture"/> slots — pure data, shared by the client's
/// mesher and the content tests.</summary>
public static class BlockFaceTextures
{
    /// <summary><see cref="BlockDefinition.TileKind"/> for a tile that shows a surface (the default).</summary>
    public const string Material = "material";

    /// <summary><see cref="BlockDefinition.TileKind"/> for a tile that is a drawing of a whole object.</summary>
    public const string Picture = "picture";

    /// <summary>The most specific slot for a part and side: part + side, then part + any side, then any part + side,
    /// then any + any. Null when none matches (the face shows the cut material of the block's own tile).</summary>
    public static BlockFaceTexture? Resolve(IReadOnlyList<BlockFaceTexture>? faces, ShapePart part, FaceSide side)
    {
        if (faces == null || faces.Count == 0)
        {
            return null;
        }

        string partName = ShapeParts.Name(part), sideName = ShapeParts.Name(side);
        BlockFaceTexture? best = null;
        int bestScore = -1;
        foreach (var f in faces)
        {
            bool anyPart = f.Part == "*", anySide = f.Side == "*";
            if ((!anyPart && f.Part != partName) || (!anySide && f.Side != sideName))
            {
                continue;
            }

            int score = (anyPart ? 0 : 2) + (anySide ? 0 : 1);
            if (score > bestScore)
            {
                best = f;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>A slot's image region as texture coordinates (v up): the start corner (u0, v0) and end corner (u1, v1).</summary>
    public static (float U0, float V0, float U1, float V1) ToUv(float[] rect)
        => (rect[0], 1f - rect[1], rect[2], 1f - rect[3]);

    /// <summary>Everything wrong with a block's slots, as human-readable lines (empty = valid). <paramref name="tileExists"/>
    /// answers whether a block key has a tile.</summary>
    public static List<string> Validate(BlockDefinition def, Func<string, bool> tileExists)
    {
        var problems = new List<string>();
        if (def.TileKind != null && def.TileKind != Material && def.TileKind != Picture)
        {
            problems.Add($"{def.Key}: tileKind '{def.TileKind}' is neither '{Material}' nor '{Picture}'");
        }

        if (def.Faces == null)
        {
            return problems;
        }

        for (int i = 0; i < def.Faces.Count; i++)
        {
            var f = def.Faces[i];
            if (f.Part != "*" && !ShapeParts.TryParsePart(f.Part, out _))
            {
                problems.Add($"{def.Key}: faces[{i}] has unknown part '{f.Part}'");
            }

            if (f.Side != "*" && !ShapeParts.TryParseSide(f.Side, out _))
            {
                problems.Add($"{def.Key}: faces[{i}] has unknown side '{f.Side}'");
            }

            if (f.Tile != null && !tileExists(f.Tile))
            {
                problems.Add($"{def.Key}: faces[{i}] names tile '{f.Tile}', which is no block");
            }

            if (f.Rect != null && (f.Rect.Length != 4 || Array.Exists(f.Rect, v => v < 0f || v > 1f || float.IsNaN(v))))
            {
                problems.Add($"{def.Key}: faces[{i}] rect must be four fractions in [0, 1]");
            }
        }

        return problems;
    }
}
