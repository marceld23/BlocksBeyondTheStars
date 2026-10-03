// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// A bred plant (#2209) keeps its packed form in the cell's glow channel. The light index must not read that as a
/// light colour: the plant lights only when its form glows, in its own colour dimmed by the glow level — on both
/// registry paths (the live edit and the chunk-store scan). Every other block keeps the glow-is-a-colour rule.
/// </summary>
public sealed class BredPlantLightTests
{
    private const ushort Hybrid = 60; // registered as a form block
    private const ushort Stone = 7;   // an ordinary block: a glow modifier makes it a light of that colour
    private const int Tint = 0x80C040;
    private const int Cs = WorldConstants.ChunkSize;

    private static ClientWorld World()
    {
        var w = new ClientWorld();
        w.SetCircumference(512);
        w.SetBlockLightResolver(_ => 0);
        w.SetFormBlocks(new[] { Hybrid });
        w.StoreChunk(new ChunkCoord(0, 0, 0), new ushort[Cs * Cs * Cs]);
        return w;
    }

    private static int Form(int glow) => FloraForm.Pack(new FloraGenome
    {
        BodyBlock = "flora_fern",
        CrownBlock = "flora_flower",
        Layout = FloraForm.LayoutTiered,
        Size = 2,
        TintRgb = Tint,
        Glow = glow,
    });

    [Fact]
    public void PlantWithoutGlow_IsNoLight()
    {
        var w = World();
        int form = Form(glow: 0);
        Assert.True(FloraForm.IsForm(form)); // the channel is not empty — it is just no light colour
        Assert.True(w.ApplyBlockChange(3, 4, 5, Hybrid, tint: Tint, glow: form, out _));

        Assert.Empty(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void GlowingPlant_CastsItsColour_DimmedByTheGlowLevel(int glow)
    {
        var w = World();
        Assert.True(w.ApplyBlockChange(3, 4, 5, Hybrid, tint: Tint, glow: Form(glow), out _));

        var hit = Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
        Assert.Equal(new Vector3i(3, 4, 5), hit.Pos);
        Assert.Equal(FloraTints.ToRgb24((0x80 / 255f, 0xC0 / 255f, 0x40 / 255f), FloraForm.LightOf(glow)), hit.Rgb);
        Assert.NotEqual(Form(glow), hit.Rgb); // never the packed form itself
    }

    [Fact]
    public void GlowingPlantWithoutAColour_GlowsWhite()
    {
        var w = World();
        Assert.True(w.ApplyBlockChange(3, 4, 5, Hybrid, tint: 0, glow: Form(glow: 3), out _));

        var hit = Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
        Assert.Equal(FloraTints.ToRgb24((1f, 1f, 1f), FloraForm.LightOf(3)), hit.Rgb);
    }

    [Fact]
    public void HarvestingTheGlowingPlant_RemovesItsLight()
    {
        var w = World();
        Assert.True(w.ApplyBlockChange(3, 4, 5, Hybrid, tint: Tint, glow: Form(glow: 2), out _));
        Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));

        Assert.True(w.ApplyBlockChange(3, 4, 5, 0, tint: 0, glow: 0, out _));
        Assert.Empty(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
    }

    [Fact]
    public void OrdinaryGlowBlock_StillLightsInItsGlowColour()
    {
        var w = World();
        Assert.True(w.ApplyBlockChange(3, 4, 5, Stone, tint: 0xFF00FF, glow: 0x00FF00, out _));

        var hit = Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
        Assert.Equal(0x00FF00, hit.Rgb);
    }

    [Fact]
    public void ChunkStoreScan_AppliesTheSameRule()
    {
        // The bulk path: a stored chunk with a dark plant, a glowing plant and an ordinary glow block.
        var w = new ClientWorld();
        w.SetCircumference(512);
        w.SetBlockLightResolver(_ => 0);
        w.SetFormBlocks(new[] { Hybrid });

        var blocks = new ushort[Cs * Cs * Cs];
        int dark = WorldConstants.LocalIndex(1, 2, 3), bright = WorldConstants.LocalIndex(4, 5, 6), glowStone = WorldConstants.LocalIndex(7, 8, 9);
        blocks[dark] = Hybrid;
        blocks[bright] = Hybrid;
        blocks[glowStone] = Stone;
        w.StoreChunk(new ChunkCoord(0, 0, 0), blocks,
            modIndex: new[] { dark, bright, glowStone },
            modTint: new[] { Tint, Tint, 0 },
            modGlow: new[] { Form(glow: 0), Form(glow: 1), 0x3366FF });

        var lights = w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4);
        Assert.Equal(2, lights.Count);
        Assert.Contains(lights, l => l.Pos == new Vector3i(4, 5, 6) && l.Rgb == ClientWorld.FormLight(Tint, Form(glow: 1)));
        Assert.Contains(lights, l => l.Pos == new Vector3i(7, 8, 9) && l.Rgb == 0x3366FF);
    }

    [Fact]
    public void WithoutTheRegistration_TheOldRuleApplies()
    {
        // Guards the opt-in: only a registered block is read as a form.
        var w = World();
        w.SetFormBlocks(null);
        int form = Form(glow: 0);
        Assert.True(w.ApplyBlockChange(3, 4, 5, Hybrid, tint: Tint, glow: form, out _));

        Assert.Equal(form, Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4)).Rgb);
    }

    [Fact]
    public void FormLight_IsZero_ForAChannelThatHoldsNoForm()
    {
        Assert.Equal(0, ClientWorld.FormLight(Tint, 0));
        Assert.Equal(0, ClientWorld.FormLight(Tint, 0x00FF00)); // a plain colour, bit 23 clear
    }
}
