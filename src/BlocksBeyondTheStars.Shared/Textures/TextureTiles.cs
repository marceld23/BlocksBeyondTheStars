// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Textures;

/// <summary>What a texture tile's alpha channel means — and therefore what it may contain.</summary>
public enum TextureAlphaMode
{
    /// <summary>A block tile. The block shaders read tile alpha as a MEANING, not as translucency: the opaque
    /// shader clips foliage on it, and the transparent shader treats a see-through tile as water (#1372). So the
    /// tile must be fully opaque — glass gets its translucency from the material, water from the atlas.</summary>
    Opaque,

    /// <summary>A tile whose alpha is a deliberate cutout mask: foliage, billboards (plants, torch, lantern) and
    /// the fire silhouette.</summary>
    Cutout,

    /// <summary>Not a block tile at all (creature hides, avatar fabrics, micro-fauna sprites, icons): any alpha.</summary>
    Free,
}

/// <summary>
/// The one place that knows the texture tile format and the alpha rule (#1952). The atlas loader, the local
/// texture pack, the texture editor, the server's validation of world textures and the asset test all ask here,
/// so they cannot drift apart. Pure data — no engine types.
/// </summary>
public static class TextureTiles
{
    /// <summary>Edge length of a tile in pixels.</summary>
    public const int Size = 64;

    /// <summary>Bytes of one RGBA32 frame (rows stored bottom-up, the layout the client's raw loader expects).</summary>
    public const int BytesPerFrame = Size * Size * 4;

    /// <summary>Most frames an animated texture may have (each frame is one atlas slot).</summary>
    public const int MaxFrames = 8;

    /// <summary>The alpha byte the shaders still read as "fully opaque" (0.95 × 255).</summary>
    public const byte OpaqueFloor = 242;

    /// <summary>Longest texture key.</summary>
    public const int MaxKeyLength = 64;

    private static readonly int[] Speeds = { 2, 4, 8, 12 };

    /// <summary>The animation speeds a texture may choose from, in frames per second.</summary>
    public static IReadOnlyList<int> AllowedFps => Speeds;

    /// <summary>True when <paramref name="frames"/> frames at <paramref name="fps"/> describe a legal texture: one
    /// still frame (fps ignored), or 2..<see cref="MaxFrames"/> frames at an allowed speed.</summary>
    public static bool IsValidAnimation(int frames, int fps)
    {
        if (frames == 1)
        {
            return true;
        }

        return frames >= 2 && frames <= MaxFrames && Array.IndexOf(Speeds, fps) >= 0;
    }

    /// <summary>A texture key is a lowercase identifier: letters, digits and underscores (block keys, and the
    /// <c>creature_*</c> / <c>prop_*</c> families follow this already).</summary>
    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
        {
            return false;
        }

        foreach (char c in key)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>What the alpha channel of the tile <paramref name="key"/> means.</summary>
    public static TextureAlphaMode AlphaModeOf(string key)
    {
        if (key.StartsWith("creature_", StringComparison.Ordinal)
            || key.StartsWith("microfauna_", StringComparison.Ordinal)
            || key.StartsWith("avatar_", StringComparison.Ordinal))
        {
            return TextureAlphaMode.Free;
        }

        if (key.StartsWith("flora_", StringComparison.Ordinal)
            || key is "tree_leaves" or "pine_needles" or "palm_frond" or "giant_leaves" // cutout crowns
            || key is "fire"                                                            // the flame silhouette
            || key is "torch" or "lantern")                                             // billboards (#1957)
        {
            return TextureAlphaMode.Cutout;
        }

        return TextureAlphaMode.Opaque;
    }

    /// <summary>Forces the alpha rule onto raw RGBA32 pixels in place: an <see cref="TextureAlphaMode.Opaque"/>
    /// tile becomes fully opaque, the other modes are left alone. Returns how many pixels it changed.</summary>
    public static int EnforceAlpha(byte[] rgba, TextureAlphaMode mode)
    {
        if (mode != TextureAlphaMode.Opaque)
        {
            return 0;
        }

        int changed = 0;
        for (int i = 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] != 255)
            {
                rgba[i] = 255;
                changed++;
            }
        }

        return changed;
    }

    /// <summary>How many pixels of raw RGBA32 data the shaders would NOT read as fully opaque.</summary>
    public static int CountSeeThrough(byte[] rgba)
    {
        int n = 0;
        for (int i = 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] < OpaqueFloor)
            {
                n++;
            }
        }

        return n;
    }
}
