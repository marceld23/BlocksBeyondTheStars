// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #4012: the client side of the dyed flower seeds — the menus offer the Dye action for a dyeable seed (colour only), the
/// mesher marks a dyed accent flower with the dyed-blossom pick, both block-shader passes colour only its blossom, and the
/// plant body near the eye wears the dye on the blossom and the tile's own leaves.
/// </summary>
public sealed class DyedFlowerClientTests
{
    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars" }.Concat(parts).ToArray()));

    [Fact]
    public void TheMenus_OfferTheDye_ForADyeableSeed_WithoutGlowOrDesigns()
    {
        string wheel = Read("Scripts", "HotbarActionUi.cs");
        Assert.Contains("Game.Content.IsDyeable(HeldItemDef())", wheel);
        Assert.Contains("bool dyeOnly = GameContent.IsDyeOnly(HeldItemDef());", wheel);
        Assert.Contains("var glowBtn = dyeOnly ? null :", wheel);
        Assert.Contains("Game.Content.IsDyeable(item)", Read("Scripts", "CraftingTechShipUI.Crafting.cs"));
        Assert.Contains("GameContent.IsDyeOnly(Game.Content.GetItem(src))", Read("Scripts", "CraftingTechShipUI.Crafting.cs"));
        Assert.Contains("Game.Content.IsDyeable(selItemDef)", Read("Scripts", "HudUi.Hotbar.cs"));
    }

    [Fact]
    public void TheMesher_MarksADyedAccentFlower_WithTheDyedBlossomPick()
    {
        string mesher = Read("Scripts", "ChunkMesher.cs");
        Assert.Contains("public const float DyedBlossomCode = 0.4f;", mesher);
        Assert.Contains("bool dyedBlossom = dyed && isFlora && !bred && (tf & TraitAccentFlora) != 0;", mesher);
        Assert.Contains("(dyedBlossom ? PlantCell.DyedBlossom : 0)", mesher);

        // The pick survives the shader's decode, with and without the plant body's 0.04 mark, inside the plant window.
        foreach (float code in new[] { 3f + 0.4f, 3f + 0.4f + 0.04f })
        {
            Assert.InRange(code, 2.5f, 3.5f);
            Assert.Equal(4f, MathF.Round((code - MathF.Round(code)) * 10f));
        }
    }

    [Fact]
    public void BothBlockShaderPasses_ColourOnlyTheBlossom_OfADyedFlower()
    {
        string shader = Read("Shaders", "BlockAtlas.shader");
        Assert.Equal(2, Regex.Matches(shader, @"i\.skyl\.y > 2\.5 && BbtsAccentPick\(i\.leaf\.x\) > 3\.5", RegexOptions.None,
            TimeSpan.FromSeconds(1)).Count);
        Assert.Equal(2, Regex.Matches(shader, @"albedo = BbtsDyedBlossom\(albedo, i\.leaf\.yzw\)", RegexOptions.None,
            TimeSpan.FromSeconds(1)).Count);

        string accent = Read("Shaders", "FloraAccent.hlsl");
        Assert.Contains("float3 BbtsDyedBlossom(float3 albedo, float3 dye)", accent);
        Assert.Contains("return lerp(albedo, acc, BbtsBlossomMask(g));", accent); // leaves and stem keep the tile's colours
        Assert.Contains("float m = BbtsBlossomMask(g);", accent); // the wild plants' accents use the very same test
    }

    [Fact]
    public void ThePlantBody_WearsTheDyeOnTheBlossom_AndTheTilesOwnLeaves()
    {
        string bodies = Read("Scripts", "PlantBodies.cs");
        Assert.Contains("public const byte DyedBlossom = 64;", bodies);
        Assert.Contains("else if ((cell.Flags & PlantCell.DyedBlossom) != 0)", bodies);
        Assert.Contains("accent = new Vector4(dye.r, dye.g, dye.b, 1f);", bodies);
    }
}
