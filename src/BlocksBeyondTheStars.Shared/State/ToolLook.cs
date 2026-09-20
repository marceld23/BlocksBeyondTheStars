// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// A player's own look for a tool (#1963): a small COLOURED voxel model that replaces what a drill, gun, blade or
/// scanner looks like in THIS player's hand. Like the pixel face and the body paint it belongs to the player, not
/// to the item — it is chosen in the main menu, sent on join, stored with the player and shown to everyone who
/// sees them. It is cosmetic only: reach, damage and hitbox are the item's.
///
/// <para>The payload: <c>"t1:" GGGG ":"</c> followed by one hex digit per voxel of a
/// <see cref="SizeX"/>×<see cref="SizeY"/>×<see cref="SizeZ"/> grid (x fastest, then z, then y). <c>0</c> is
/// empty, <c>1..f</c> index the fixed <see cref="Palette"/>; <c>GGGG</c> is a bit mask of the palette entries
/// that glow. A fixed palette keeps the payload small and makes a look impossible to abuse as a carrier for
/// arbitrary data. The voxels are merged per colour into boxes (<see cref="ToParts"/>) — the same
/// <see cref="HeldModelPart"/>s an official tool model is made of, so the client draws both the same way.</para>
/// </summary>
public static class ToolLook
{
    public const string Prefix = "t1:";

    /// <summary>Grid across the hand (left–right).</summary>
    public const int SizeX = 8;

    /// <summary>Grid up–down.</summary>
    public const int SizeY = 8;

    /// <summary>Grid along the tool (it points away from the holder).</summary>
    public const int SizeZ = 16;

    /// <summary>Edge of one voxel in metres — the whole grid is 0.32 × 0.32 × 0.64 m, a big tool but a tool.</summary>
    public const float Voxel = 0.04f;

    /// <summary>Where voxel (0,0,0) starts in the holder's frame: centred across, the grip below the hand line,
    /// a little of the tool behind the hand.</summary>
    public const float OriginX = -SizeX * Voxel / 2f, OriginY = -0.20f, OriginZ = -0.10f;

    public const int VoxelCount = SizeX * SizeY * SizeZ;

    /// <summary>Most boxes a look may need after merging — the cap every held model has.</summary>
    public const int MaxParts = HeldModelPart.MaxParts;

    /// <summary>Looks one player may have.</summary>
    public const int MaxLooksPerPlayer = 16;

    private const int HeaderChars = 8; // "t1:" + 4 + ":"

    /// <summary>The fifteen colours of a look (index 1..15; 0 is empty).</summary>
    public static readonly IReadOnlyList<string> Palette = new[]
    {
        string.Empty,
        "#1d2026", "#3a3f4b", "#8c94a3", "#c7ccd6", "#f2f4f7", // graphite … white
        "#6b4526", "#b5651d",                                  // wood, copper
        "#d83a34", "#f28c28", "#f2d03b",                       // red, orange, yellow
        "#3fa84a", "#39c6d6", "#2f6fe0",                       // green, cyan, blue
        "#8e4be0", "#f05fc2",                                  // violet, pink
    };

    public static int IndexOf(int x, int y, int z) => (((y * SizeZ) + z) * SizeX) + x;

    /// <summary>A payload the game can draw: the exact length, lowercase hex, a glow mask, at least one voxel,
    /// and no more boxes than a held model may have. Empty is NOT valid here — an empty string means "no look"
    /// and is handled by the caller.</summary>
    public static bool IsValid(string? model)
    {
        if (model is null || model.Length != HeaderChars + VoxelCount
            || !model.StartsWith(Prefix, System.StringComparison.Ordinal) || model[HeaderChars - 1] != ':'
            || !TryGlowMask(model, out _))
        {
            return false;
        }

        bool any = false;
        for (int i = HeaderChars; i < model.Length; i++)
        {
            char c = model[i];
            if (!(c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }

            any |= c != '0';
        }

        return any && Merge(model).Count <= MaxParts;
    }

    /// <summary>Builds a payload from palette indices (0 = empty) and the glow mask. Empty when the result would
    /// not be valid.</summary>
    public static string Compose(IReadOnlyList<byte> voxels, int glowMask)
    {
        if (voxels == null || voxels.Count != VoxelCount)
        {
            return string.Empty;
        }

        var chars = new char[HeaderChars + VoxelCount];
        Prefix.CopyTo(0, chars, 0, Prefix.Length);
        string mask = (glowMask & 0xFFFE).ToString("x4", CultureInfo.InvariantCulture);
        mask.CopyTo(0, chars, Prefix.Length, 4);
        chars[HeaderChars - 1] = ':';
        for (int i = 0; i < VoxelCount; i++)
        {
            int v = voxels[i] & 0xF;
            chars[HeaderChars + i] = (char)(v < 10 ? '0' + v : 'a' + (v - 10));
        }

        string model = new string(chars);
        return IsValid(model) ? model : string.Empty;
    }

    /// <summary>The palette indices of a payload (0 = empty) and its glow mask; false for garbage.</summary>
    public static bool TryRead(string? model, out byte[] voxels, out int glowMask)
    {
        voxels = System.Array.Empty<byte>();
        glowMask = 0;
        if (model is null || model.Length != HeaderChars + VoxelCount || !model.StartsWith(Prefix, System.StringComparison.Ordinal)
            || !TryGlowMask(model, out glowMask))
        {
            return false;
        }

        voxels = new byte[VoxelCount];
        for (int i = 0; i < VoxelCount; i++)
        {
            int v = HexValue(model[HeaderChars + i]);
            if (v < 0)
            {
                voxels = System.Array.Empty<byte>();
                return false;
            }

            voxels[i] = (byte)v;
        }

        return true;
    }

    /// <summary>One merged box of a look, in voxels: <c>[X0,X1)</c> × <c>[Y0,Y1)</c> × <c>[Z0,Z1)</c>.</summary>
    public readonly record struct Box(int X0, int Y0, int Z0, int X1, int Y1, int Z1, int Color);

    /// <summary>Greedy-merges the voxels PER COLOUR into boxes (grow along +X, then +Z, then +Y) — the same
    /// deterministic, integer-only pass <c>CustomShape.Merge</c> uses, so server and client agree on the count.</summary>
    public static List<Box> Merge(string model)
    {
        var boxes = new List<Box>();
        if (!TryRead(model, out byte[] v, out _))
        {
            return boxes;
        }

        var claimed = new bool[VoxelCount];
        for (int y = 0; y < SizeY; y++)
        {
            for (int z = 0; z < SizeZ; z++)
            {
                for (int x = 0; x < SizeX; x++)
                {
                    int start = IndexOf(x, y, z);
                    int color = v[start];
                    if (color == 0 || claimed[start])
                    {
                        continue;
                    }

                    bool Free(int fx, int fy, int fz)
                    {
                        int i = IndexOf(fx, fy, fz);
                        return v[i] == color && !claimed[i];
                    }

                    int x1 = x + 1;
                    while (x1 < SizeX && Free(x1, y, z))
                    {
                        x1++;
                    }

                    bool RowFree(int ry, int rz)
                    {
                        for (int rx = x; rx < x1; rx++)
                        {
                            if (!Free(rx, ry, rz))
                            {
                                return false;
                            }
                        }

                        return true;
                    }

                    int z1 = z + 1;
                    while (z1 < SizeZ && RowFree(y, z1))
                    {
                        z1++;
                    }

                    bool SlabFree(int sy)
                    {
                        for (int sz = z; sz < z1; sz++)
                        {
                            if (!RowFree(sy, sz))
                            {
                                return false;
                            }
                        }

                        return true;
                    }

                    int y1 = y + 1;
                    while (y1 < SizeY && SlabFree(y1))
                    {
                        y1++;
                    }

                    for (int cy = y; cy < y1; cy++)
                    {
                        for (int cz = z; cz < z1; cz++)
                        {
                            for (int cx = x; cx < x1; cx++)
                            {
                                claimed[IndexOf(cx, cy, cz)] = true;
                            }
                        }
                    }

                    boxes.Add(new Box(x, y, z, x1, y1, z1, color));
                }
            }
        }

        return boxes;
    }

    /// <summary>The look as held-model parts, ready for the client's held-item builder. Empty for garbage.</summary>
    public static List<HeldModelPart> ToParts(string? model)
    {
        var parts = new List<HeldModelPart>();
        if (!IsValid(model) || !TryGlowMask(model!, out int glowMask))
        {
            return parts;
        }

        foreach (var box in Merge(model!))
        {
            float w = (box.X1 - box.X0) * Voxel, h = (box.Y1 - box.Y0) * Voxel, d = (box.Z1 - box.Z0) * Voxel;
            parts.Add(new HeldModelPart
            {
                P = new[] { OriginX + (box.X0 * Voxel) + (w / 2f), OriginY + (box.Y0 * Voxel) + (h / 2f), OriginZ + (box.Z0 * Voxel) + (d / 2f) },
                S = new[] { w, h, d },
                C = Palette[box.Color],
                G = (glowMask & (1 << box.Color)) != 0,
            });
        }

        return parts;
    }

    private static bool TryGlowMask(string model, out int mask)
    {
        mask = 0;
        for (int i = 0; i < 4; i++)
        {
            int v = HexValue(model[Prefix.Length + i]); // lowercase hex only, no whitespace, no sign
            if (v < 0)
            {
                mask = 0;
                return false;
            }

            mask = (mask << 4) | v;
        }

        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => -1,
    };
}
