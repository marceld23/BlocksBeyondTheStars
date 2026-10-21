// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Textures;
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
/// foliage, <c>bundle_fire.py</c> for the flame, <c>bake_prop_alpha.py</c> for the torch and lantern — never
/// from the image model, so they are named here.
/// </para>
/// </summary>
public sealed class BlockTileAlphaTests
{
    private const int Tile = 64;
    private const int RawSize = Tile * Tile * 4;

    /// <summary>Alpha byte for the shaders' "fully opaque" threshold (0.95 × 255 ≈ 242).</summary>
    private const byte OpaqueFloor = TextureTiles.OpaqueFloor;

    /// <summary>Tiles whose alpha channel is a deliberate cutout mask (foliage, fire, the torch and lantern
    /// billboards) or that are not block tiles at all (creature, micro-fauna and avatar maps). The rule itself
    /// lives in <see cref="TextureTiles.AlphaModeOf"/> (#1952) — the same one the client's loaders, the texture
    /// editor and the server's world-texture validation use, so this test cannot drift from them.</summary>
    private static bool IsIntentionalCutout(string key)
        => TextureTiles.AlphaModeOf(key) != TextureAlphaMode.Opaque;

    private static string TextureDir()
    {
        string path = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources", "textures");
        Assert.True(Directory.Exists(path), $"Bundled texture directory not found at {path} — did the tiles move?");
        return path;
    }

    private const string AnimSuffix = "__anim";

    [Fact]
    public void BlockTiles_ShipFullyOpaque_ExceptTheDeliberatelyBakedCutouts()
    {
        var offenders = new List<string>();
        var files = Directory.GetFiles(TextureDir(), "*.bytes");
        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            // "<key>__anim.bytes" holds frames 2..n of an animated tile (#1957) — the same rule as its first frame.
            bool extraFrames = name.EndsWith(AnimSuffix, System.StringComparison.Ordinal);
            string key = extraFrames ? name.Substring(0, name.Length - AnimSuffix.Length) : name;
            if (IsIntentionalCutout(key))
            {
                continue;
            }

            byte[] raw = File.ReadAllBytes(file);
            if (extraFrames)
            {
                Assert.True(raw.Length > 0 && raw.Length % RawSize == 0 && raw.Length / RawSize <= TextureTiles.MaxFrames - 1,
                    $"{name}.bytes is {raw.Length} bytes — expected 1 to {TextureTiles.MaxFrames - 1} frames of {RawSize} bytes.");
                Assert.True(File.Exists(Path.Combine(TextureDir(), key + ".bytes")), $"{name}.bytes has no first frame {key}.bytes.");
            }
            else
            {
                Assert.True(raw.Length == RawSize, $"{key}.bytes is {raw.Length} bytes, expected {RawSize} (64×64 RGBA32).");
            }

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
    /// #2520: the billboard tiles must actually BE cutouts. Being classified <see cref="TextureAlphaMode.Cutout"/>
    /// only exempts a tile from the opaque rule above — it never checked that a mask was baked, and the torch and
    /// lantern shipped fully opaque for months: every wall torch stood on a dark card (the lantern on a beige one)
    /// that its own light lit up grey. A billboard needs a real share of fully transparent pixels around the
    /// object, and still a body that survives the shader's 0.5 clip.
    /// </summary>
    [Theory]
    [InlineData("torch")]
    [InlineData("lantern")]
    [InlineData("fire")]
    public void BillboardCutoutTiles_HaveARealShareOfTransparentPixels(string key)
    {
        Assert.Equal(TextureAlphaMode.Cutout, TextureTiles.AlphaModeOf(key));
        string file = Path.Combine(TextureDir(), key + ".bytes");
        Assert.True(File.Exists(file), $"{key}.bytes is missing — the billboard has no tile.");

        byte[] raw = File.ReadAllBytes(file);
        Assert.Equal(RawSize, raw.Length);
        int clear = 0, body = 0;
        for (int i = 3; i < raw.Length; i += 4)
        {
            if (raw[i] == 0)
            {
                clear++;
            }
            else if (raw[i] >= 128)
            {
                body++;
            }
        }

        int pixels = raw.Length / 4;
        Assert.True(
            clear >= pixels / 4,
            $"{key}.bytes has only {clear}/{pixels} fully transparent pixels — the billboard would draw its background "
            + "card. Bake the mask (tools/ai-assets/bake_prop_alpha.py for torch/lantern, bundle_fire.py for fire).");
        Assert.True(body >= pixels / 20, $"{key}.bytes keeps only {body}/{pixels} pixels above the 0.5 clip — the object itself was cut away.");
    }

    /// <summary>
    /// #2774: a flora tile baked as ONE object on a ground (the black-ground and ground-key sets of
    /// <c>bake_leaf_alpha.py</c>) must be cut the right way round: the ground gone, the object kept. #2772: the
    /// darkness cut meant for dense leaves inverted <c>flora_dryshrub</c> — the shrub was the darkest thing in its
    /// tile, so the shrub was cut and the sand card stayed, and no test noticed from June to October 2026. An
    /// inverted mask keeps the ground at the tile's sides, which the side-column check catches at once.
    /// </summary>
    [Fact]
    public void ObjectOnGroundFloraTiles_KeepTheObject_AndCutTheGround()
    {
        string script = BakeScript();
        var keyed = PythonSetKeys(script, "DARK_GROUND").Union(PythonSetKeys(script, "GROUND_KEY")).Union(BillboardLookKeys()).ToList();
        Assert.Contains("flora_dryshrub", keyed);

        var offenders = new List<string>();
        foreach (string key in keyed)
        {
            byte[] raw = BundledTile(key);
            int pixels = raw.Length / 4, clear = 0, body = 0, sideClear = 0;
            for (int i = 0; i < pixels; i++)
            {
                byte a = raw[i * 4 + 3];
                clear += a == 0 ? 1 : 0;
                body += a >= 128 ? 1 : 0;
                int x = i % Tile;
                if ((x == 0 || x == Tile - 1) && a == 0)
                {
                    sideClear++;
                }
            }

            if (sideClear < 2 * Tile * 9 / 10)
            {
                offenders.Add($"{key}: only {sideClear}/{2 * Tile} pixels of the left and right columns are cut — the ground stands beside the object (an inverted mask?)");
            }

            if (clear < pixels / 4)
            {
                offenders.Add($"{key}: only {clear}/{pixels} pixels are fully transparent — the billboard draws its background card");
            }

            if (body < pixels / 20)
            {
                offenders.Add($"{key}: only {body}/{pixels} pixels survive the 0.5 clip — the object itself was cut away");
            }
        }

        Assert.True(offenders.Count == 0, "Re-bake with tools/ai-assets/bake_leaf_alpha.py --key <key>. " + string.Join("; ", offenders));
    }

    /// <summary>
    /// #2773: a cross billboard roots at its tile's bottom row (<c>ChunkMesher.AddCrossPlant</c>), so a plant that
    /// stands on the ground must touch the tile's bottom edge, or it floats above the ground by the margin under it.
    /// <c>bake_leaf_alpha.py</c> lowers the <c>ROOTED</c> tiles onto that edge; this pins it. The raw tiles are stored
    /// bottom-up, so the first row of bytes is the tile's bottom row.
    /// </summary>
    [Fact]
    public void RootedFloraBillboards_StandOnTheTilesBottomEdge()
    {
        string script = BakeScript();
        var keyed = PythonSetKeys(script, "DARK_GROUND").Union(PythonSetKeys(script, "GROUND_KEY")).Union(BillboardLookKeys()).ToHashSet();
        var rooted = PythonSetKeys(script, "ROOTED").Union(BillboardLookKeys()).ToHashSet();
        Assert.NotEmpty(rooted);

        var offenders = new List<string>();
        foreach (string key in rooted)
        {
            if (!keyed.Contains(key))
            {
                offenders.Add($"{key}: ROOTED but neither in DARK_GROUND nor in GROUND_KEY — only an object on a ground can be lowered");
                continue;
            }

            byte[] raw = BundledTile(key);
            int bottom = 0, topClear = 0;
            for (int x = 0; x < Tile; x++)
            {
                bottom += raw[x * 4 + 3] >= 128 ? 1 : 0;
                topClear += raw[((Tile - 1) * Tile + x) * 4 + 3] == 0 ? 1 : 0;
            }

            if (bottom == 0)
            {
                offenders.Add($"{key}: nothing of the plant touches the tile's bottom row — the billboard floats above the ground");
            }

            if (topClear < Tile * 9 / 10)
            {
                offenders.Add($"{key}: only {topClear}/{Tile} pixels of the top row are cut — upside down, or the ground was kept?");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("; ", offenders));
    }

    private static string BakeScript()
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "tools", "ai-assets", "bake_leaf_alpha.py"));

    /// <summary>#2832: the look tiles of the billboard archetypes (<c>"looks"</c> in data/blocks.json, every block but the
    /// tree crowns) — one side-view plant on black each, cut and rooted like the DARK_GROUND + ROOTED tiles.
    /// bake_leaf_alpha.py derives the same set from the same file, so these guards cover every look it bakes. #2851: the
    /// crowns are every <see cref="TreeFoliage.Keys"/> block (the leaf slots and the blossom crown have looks now), and the
    /// bark looks (wood_log, wood_log_b — opaque tiles, the script's OPAQUE_LOOK_KEYS) are no billboards either.</summary>
    private static readonly System.TimeSpan RegexTimeout = System.TimeSpan.FromSeconds(1);

    private static IEnumerable<string> BillboardLookKeys()
    {
        var crowns = new HashSet<string>(TreeFoliage.Keys);
        foreach (string line in File.ReadLines(Path.Combine(ClientTestPaths.RepoRoot(), "data", "blocks.json")))
        {
            var key = Regex.Match(line, @"""key"":\s*""(?<v>[^""]+)""", RegexOptions.ExplicitCapture, RegexTimeout);
            var looks = Regex.Match(line, @"""looks"":\s*\[(?<v>[^\]]*)\]", RegexOptions.ExplicitCapture, RegexTimeout);
            if (!key.Success || !looks.Success || crowns.Contains(key.Groups["v"].Value)
                || TextureTiles.AlphaModeOf(key.Groups["v"].Value) == TextureAlphaMode.Opaque)
            {
                continue;
            }

            foreach (Match look in Regex.Matches(looks.Groups["v"].Value, "\"(?<v>[^\"]+)\"", RegexOptions.ExplicitCapture, RegexTimeout))
            {
                yield return look.Groups["v"].Value;
            }
        }
    }

    /// <summary>The quoted keys of a top-level Python set or dict literal (<c>NAME = { … }</c>, closed by a
    /// <c>}</c> at the start of a line) in the bake script.</summary>
    private static HashSet<string> PythonSetKeys(string script, string name)
    {
        int open = script.IndexOf("\n" + name + " = {", StringComparison.Ordinal);
        Assert.True(open >= 0, $"bake_leaf_alpha.py must declare {name} = {{...}}");
        int close = script.IndexOf("\n}", open + 1, StringComparison.Ordinal);
        Assert.True(close > open, $"the {name} literal must be closed by a '}}' at the start of a line");
        return script.Substring(open, close - open).Split('"')
            .Where((part, index) => index % 2 == 1) // every odd piece sits between two quotes
            .ToHashSet();
    }

    private static byte[] BundledTile(string key)
    {
        string file = Path.Combine(TextureDir(), key + ".bytes");
        Assert.True(File.Exists(file), $"{key}.bytes is missing — the bake script lists a tile that is not bundled.");
        byte[] raw = File.ReadAllBytes(file);
        Assert.Equal(RawSize, raw.Length);
        return raw;
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
