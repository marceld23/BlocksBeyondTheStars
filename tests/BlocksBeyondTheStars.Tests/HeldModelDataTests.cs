// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The held models in <c>data/items.json</c> (#1962): every gadget held as a gadget has a model of its own (#2837) — the
/// client used to draw one shared emitter for all of them — and every material a part names (<c>"m"</c>, #2836) is one the
/// client has a texture slot for, so a typo cannot silently fall back to the colour guess.
/// </summary>
public sealed class HeldModelDataTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void EveryGadget_HasAHeldModel()
    {
        var gadgets = Content.Items.Values.Where(i => i.Tool?.Kind == ToolKind.Gadget && i.Vehicle == null).ToList();
        Assert.True(gadgets.Count >= 22, "the 21 gadgets and the rope gun");
        foreach (var item in gadgets)
        {
            Assert.True(item.HeldModel is { Count: >= 4 and <= HeldModelPart.MaxParts }, $"{item.Key} has no held model of its own");
            Assert.All(item.HeldModel!, p => Assert.True(p.IsValid(), $"{item.Key} has a part the game cannot draw"));
        }
    }

    [Fact]
    public void EveryMaterialInAHeldModel_IsAKnownOne()
    {
        var named = Content.Items.Values.Where(i => i.HeldModel != null)
            .SelectMany(i => i.HeldModel!.Where(p => p.M != null).Select(p => (i.Key, p.M))).ToList();
        Assert.True(named.Count > 100, "the gadgets name the material of every part");
        Assert.All(named, t => Assert.True(HeldMaterials.IsKnown(t.M), $"{t.Key}: unknown material '{t.M}'"));
    }

    [Fact]
    public void TheMaterial_IsReadFromTheShortKey()
    {
        // "m" in the data binds to HeldModelPart.M like "p", "s", "c" and "g" do.
        var blade = Content.GetItem("plasma_sword")!.HeldModel![2];
        Assert.Equal(HeldMaterials.Energy, blade.M);
        Assert.Null(Content.GetItem("plasma_sword")!.HeldModel![0].M); // the rest is left to the client's colour rule
    }

    [Fact]
    public void TheMaterials_AreTwelveTextureKeys()
    {
        Assert.Equal(12, HeldMaterials.All.Count);
        Assert.All(HeldMaterials.All, m => Assert.Equal("held_" + m, HeldMaterials.TextureKey(m)));
        Assert.All(HeldMaterials.All, m => Assert.True(BlocksBeyondTheStars.Shared.Textures.TextureTiles.IsValidKey(HeldMaterials.TextureKey(m))));
    }
}
