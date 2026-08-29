// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The bundled block tiles (<c>client/Assets/Resources/textures/*.bytes</c>, raw 64×64 RGBA32) must ship
/// fully OPAQUE. A tile's alpha is not translucency to the block shaders, it is a <em>meaning</em>:
/// <c>BlockAtlas</c> clips foliage on it, and <c>BlockAtlasTransparent</c> read "alpha &lt; 0.95" as
/// "this face is water".
/// <para>
/// #1372: the generator prompt for <c>glass_clear</c> asked for "perfectly clear colourless glass" and
/// the image model answered with a fully transparent PNG — 3844 of 4096 pixels at alpha 0. That did not
/// produce see-through glass; it routed every cockpit canopy into the water branch, which rendered it as
/// a pond (animated refraction, SSR, and a forced opaque composite). The shader now picks clear glass and
/// fire out of that branch explicitly, and <c>bundle_textures.py</c> flattens block-tile alpha — this
/// guard pins the asset side so a re-rolled tile fails here instead of on a player's canopy.
/// </para>
/// <para>
/// The cutouts that ARE wanted come from deliberate, separate steps — <c>bake_leaf_alpha.py</c> for
/// foliage, <c>bundle_fire.py</c> for the flame — never from the image model, so they are named here.
/// </para>
/// </summary>
public sealed class BlockTileAlphaTests
{
    private const int Tile = 64;
    private const int RawSize = Tile * Tile * 4;

    /// <summary>Alpha byte for the shaders' "fully opaque" threshold (0.95 × 255 ≈ 242).</summary>
    private const byte OpaqueFloor = 242;

    /// <summary>Tiles whose alpha channel is a deliberate cutout mask, baked after bundling.</summary>
    private static bool IsIntentionalCutout(string key)
        => key.StartsWith("flora_", StringComparison.Ordinal)          // bake_leaf_alpha.py
        || key is "tree_leaves" or "pine_needles" or "palm_frond"      // bake_leaf_alpha.py
        || key is "fire"                                               // bundle_fire.py — the flame silhouette
        || key.StartsWith("creature_", StringComparison.Ordinal)       // billboards, not block tiles
        || key.StartsWith("microfauna_", StringComparison.Ordinal)
        || key.StartsWith("avatar_", StringComparison.Ordinal);

    private static string TextureDir()
    {
        string path = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources", "textures");
        Assert.True(Directory.Exists(path), $"Bundled texture directory not found at {path} — did the tiles move?");
        return path;
    }

    [Fact]
    public void BlockTiles_ShipFullyOpaque_ExceptTheDeliberatelyBakedCutouts()
    {
        var offenders = new List<string>();
        var files = Directory.GetFiles(TextureDir(), "*.bytes");
        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            string key = Path.GetFileNameWithoutExtension(file);
            if (IsIntentionalCutout(key))
            {
                continue;
            }

            byte[] raw = File.ReadAllBytes(file);
            Assert.True(raw.Length == RawSize, $"{key}.bytes is {raw.Length} bytes, expected {RawSize} (64×64 RGBA32).");

            int transparent = 0;
            for (int i = 3; i < raw.Length; i += 4)
            {
                if (raw[i] < OpaqueFloor)
                {
                    transparent++;
                }
            }

            if (transparent > 0)
            {
                offenders.Add($"{key} ({transparent}/{raw.Length / 4} px below alpha {OpaqueFloor})");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Block tiles must ship opaque — the block shaders read tile alpha as a meaning, not as "
            + "translucency (a see-through tile used to make the face render as WATER, #1372). Either "
            + "re-bundle the tile with tools/ai-assets/bundle_textures.py (it flattens block-tile alpha), "
            + "or add it to IsIntentionalCutout if the cutout is deliberate. Offenders: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The blocks drawn by <c>BlockAtlasTransparent</c> — the shader that used to decide "am I water?" from
    /// tile alpha alone. <c>water</c> gets its translucency at runtime from
    /// <c>BlockTextureAtlas.FadeTileAlpha(0.28)</c>, never from the asset, and <c>fire</c> is the one
    /// deliberate cutout in this set.
    /// </summary>
    [Theory]
    [InlineData("glass")]
    [InlineData("glass_clear")]
    [InlineData("force_field")]
    [InlineData("energy_fence")]
    [InlineData("energy_gate")]
    [InlineData("water")]
    public void TransparentShaderTiles_AreOpaqueInTheAsset(string key)
    {
        string file = Path.Combine(TextureDir(), key + ".bytes");
        if (!File.Exists(file))
        {
            return; // not every transparent block has a bundled tile — the atlas paints those procedurally
        }

        byte[] raw = File.ReadAllBytes(file);
        Assert.Equal(RawSize, raw.Length);

        for (int i = 3; i < raw.Length; i += 4)
        {
            Assert.True(
                raw[i] >= OpaqueFloor,
                $"{key}.bytes has a pixel at alpha {raw[i]}. BlockAtlasTransparent treats a see-through tile "
                + "as WATER, so this pane would render with the water refraction/SSR block instead of as glass (#1372).");
        }
    }
}
