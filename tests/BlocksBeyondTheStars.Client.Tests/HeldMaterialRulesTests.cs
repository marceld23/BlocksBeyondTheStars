// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using System.Numerics;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#2836: every cube of a held model is made of a material — named in the data or guessed from its colour — whose
/// tile repeats in metres. These pin the guess, that every model the game draws ends up with a known material, the UV
/// buckets and the cube mesh, and how a tile is lifted before it is multiplied over a colour.</summary>
public sealed class HeldMaterialRulesTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static HeldItemShapes.Rgb Hex(string hex)
    {
        Assert.True(HeldModelPart.TryParseColor(hex, out float r, out float g, out float b));
        return new HeldItemShapes.Rgb(r, g, b);
    }

    [Theory]
    [InlineData("#7a5230", HeldMaterials.Wood)]      // the wooden pickaxe's handle
    [InlineData("#6b4423", HeldMaterials.Wood)]      // the club
    [InlineData("#8b5a2b", HeldMaterials.Wood)]      // the spear
    [InlineData("#47301a", HeldMaterials.Leather)]   // a dark brown: the bow's leather grip
    [InlineData("#3b3226", HeldMaterials.Leather)]   // the energy gloves' cuff
    [InlineData("#b5651d", HeldMaterials.Metal)]     // copper: a brown too saturated for wood
    [InlineData("#1d2026", HeldMaterials.Rubber)]    // graphite
    [InlineData("#292e38", HeldMaterials.Rubber)]    // the glove cuff
    [InlineData("#333842", HeldMaterials.DarkMetal)] // the grips of the guns
    [InlineData("#474d5c", HeldMaterials.DarkMetal)] // the diamond drill's body (blue-tinted)
    [InlineData("#8c94a3", HeldMaterials.Metal)]
    [InlineData("#758fad", HeldMaterials.Metal)]     // titanium: steel blue is still a grey
    [InlineData("#d9dee6", HeldMaterials.Metal)]
    [InlineData("#ff8a33", HeldMaterials.Plastic)]   // the rope gun's orange grip
    [InlineData("#d83a34", HeldMaterials.Plastic)]
    [InlineData("#39c6d6", HeldMaterials.Plastic)]
    [InlineData("#f2d03b", HeldMaterials.Plastic)]
    public void TheColour_DecidesTheMaterial_OfAPartThatNamesNone(string hex, string material)
        => Assert.Equal(material, HeldMaterialRules.Infer(Hex(hex), glow: false));

    [Fact]
    public void AGlowingPart_IsEnergy_WhateverItsColour()
    {
        Assert.Equal(HeldMaterials.Energy, HeldMaterialRules.Infer(Hex("#7a5230"), glow: true));
        Assert.Equal(HeldMaterials.Energy, HeldMaterialRules.Infer(Hex("#1d2026"), glow: true));
    }

    [Fact]
    public void ANamedMaterial_Wins_AndAnUnknownOneIsGuessed()
    {
        var grey = Hex("#8c94a3");
        Assert.Equal(HeldMaterials.Stone, new HeldItemShapes.Part(Vector3.Zero, Vector3.One, grey, false, HeldMaterials.Stone).Material);
        Assert.Equal(HeldMaterials.Metal, new HeldItemShapes.Part(Vector3.Zero, Vector3.One, grey, false, "unobtainium").Material);
        Assert.Equal(HeldMaterials.Metal, new HeldItemShapes.Part(Vector3.Zero, Vector3.One, grey).Material);
        Assert.Equal(HeldMaterials.Crystal, HeldMaterialRules.Resolve(HeldMaterials.Crystal, grey, glow: true)); // named beats the glow

        var model = new[] { new HeldModelPart { P = new[] { 0f, 0f, 0f }, S = new[] { 0.1f, 0.1f, 0.1f }, C = "#8c94a3", M = "bogus" } };
        Assert.True(model[0].IsValid(), "an unknown material never hides a part");
        Assert.Equal(HeldMaterials.Metal, HeldItemShapes.FromModel(model, grey).Single().Material);
    }

    [Fact]
    public void EveryColourOfAPlayersToolLook_GetsAMaterial()
    {
        var expected = new[]
        {
            HeldMaterials.Rubber, HeldMaterials.DarkMetal, HeldMaterials.Metal, HeldMaterials.Metal, HeldMaterials.Metal, // graphite … white
            HeldMaterials.Wood, HeldMaterials.Metal,                                                                       // wood, copper
            HeldMaterials.Plastic, HeldMaterials.Plastic, HeldMaterials.Plastic,
            HeldMaterials.Plastic, HeldMaterials.Plastic, HeldMaterials.Plastic,
            HeldMaterials.Plastic, HeldMaterials.Plastic,
        };
        for (int i = 1; i < ToolLook.Palette.Count; i++)
        {
            Assert.Equal(expected[i - 1], HeldMaterialRules.Infer(Hex(ToolLook.Palette[i]), glow: false));
        }

        // A look's parts go through the same model path as the item data: every one has a material.
        var voxels = new byte[ToolLook.VoxelCount];
        voxels[ToolLook.IndexOf(3, 0, 0)] = 6; // wood
        voxels[ToolLook.IndexOf(3, 1, 0)] = 13; // blue, glowing
        string look = ToolLook.Compose(voxels, 1 << 13);
        var parts = HeldItemShapes.Parts("Drill", Hex("#808080"), ToolLook.ToParts(look))!;
        Assert.Equal(new[] { HeldMaterials.Wood, HeldMaterials.Energy }, parts.Select(p => p.Material));
    }

    [Fact]
    public void EveryModelTheGameDraws_HasAKnownMaterial()
    {
        var tint = new HeldItemShapes.Rgb(0.6f, 0.6f, 0.6f);
        var all = new List<HeldItemShapes.Part>();
        foreach (var kind in new[] { "Drill", "Gun", "Blade", "Scanner", "Gadget" })
        {
            all.AddRange(HeldItemShapes.Parts(kind, tint)!);
        }

        all.AddRange(HeldItemShapes.NpcToolParts(HeldItemShapes.AxeKind, tint)!);
        all.AddRange(HeldItemShapes.NpcToolParts(HeldItemShapes.BowKind, tint)!);
        all.AddRange(HeldItemShapes.Glove(null, tint));
        all.AddRange(HeldItemShapes.Mirror(HeldItemShapes.ClimbGear(gloves: true, claws: true)));
        all.AddRange(HeldItemShapes.Lump("iron_ore", tint));
        foreach (var item in Content.Items.Values.Where(i => i.HeldModel != null))
        {
            all.AddRange(HeldItemShapes.FromModel(item.HeldModel, tint));
        }

        Assert.All(all, p => Assert.True(HeldMaterials.IsKnown(p.Material), $"'{p.Material}'"));
        Assert.Contains(HeldItemShapes.NpcToolParts(HeldItemShapes.BowKind, tint)!, p => p.Material == HeldMaterials.Leather);
        Assert.Contains(HeldItemShapes.ClimbGear(gloves: true, claws: false), p => p.Material == HeldMaterials.Rubber);
    }

    [Fact]
    public void EveryMaterialInTheItemData_IsOneTheClientKnows()
    {
        var named = Content.Items.Values.Where(i => i.HeldModel != null).SelectMany(i => i.HeldModel!.Select(p => (i.Key, p.M)))
            .Where(t => t.M != null).ToList();
        Assert.NotEmpty(named);
        Assert.All(named, t => Assert.True(HeldMaterials.IsKnown(t.M), $"{t.Key}: material '{t.M}'"));

        // The data's own choice is kept: the plasma sword's blade is energy, the diamond drill's bit crystal.
        Assert.Equal(HeldMaterials.Energy, HeldItemShapes.FromModel(Content.GetItem("plasma_sword")!.HeldModel, Hex("#000000"))[2].Material);
        Assert.Equal(HeldMaterials.Crystal, HeldItemShapes.FromModel(Content.GetItem("diamond_drill")!.HeldModel, Hex("#000000"))[4].Material);
    }

    [Fact]
    public void Mirror_KeepsTheMaterial()
    {
        var parts = HeldItemShapes.Glove(Content.GetItem("energy_gloves")!.HeldModel, Hex("#ffffff"));
        Assert.Equal(parts.Select(p => p.Material), HeldItemShapes.Mirror(parts).Select(p => p.Material));
    }

    [Fact]
    public void TheMaterialKeys_AreTheTwelveHeldTiles()
    {
        Assert.Equal(12, HeldMaterials.All.Count);
        Assert.Equal(HeldMaterials.All.Count, HeldMaterials.All.Distinct().Count());
        Assert.All(HeldMaterials.All, m => Assert.Matches("^held_[a-z]+$", HeldMaterials.TextureKey(m)));
        Assert.False(HeldMaterials.IsKnown(null));
        Assert.False(HeldMaterials.IsKnown("Metal")); // exact names only
    }

    // ---------------- the tile repeats in metres ----------------

    [Fact]
    public void AFaceShowsAsMuchOfTheTileAsThePartIsLong()
    {
        var blade = HeldMaterialRules.UvBucket(new Vector3(0.035f, 0.06f, 0.52f));
        Assert.Equal(21, blade.Z); // 0.52 m = 2.6 tiles of 0.2 m → 21 eighths
        Assert.Equal(2, blade.Y);
        Assert.Equal(1, blade.X);
        Assert.Equal((1, 1, 1), HeldMaterialRules.UvBucket(new Vector3(0.001f, 0f, float.NaN))); // never nothing
        Assert.Equal(HeldMaterialRules.MaxUvSteps, HeldMaterialRules.UvBucket(new Vector3(50f, 1f, 1f)).X);

        var cube = HeldMaterialRules.BuildCube(blade.X, blade.Y, blade.Z);
        Assert.Equal(24, cube.Positions.Length);
        Assert.Equal(36, cube.Triangles.Length);
        for (int f = 0; f < 6; f++)
        {
            var n = cube.Normals[f * 4];
            var maxUv = cube.Uvs.Skip(f * 4).Take(4).Aggregate(Vector2.Zero, Vector2.Max);
            // A side face (normal across X) runs along the blade: 21/8 tiles one way, 2/8 the other.
            if (n.X != 0f)
            {
                Assert.Equal(new Vector2(21 / 8f, 2 / 8f), maxUv);
            }
            else if (n.Y != 0f)
            {
                Assert.Equal(new Vector2(1 / 8f, 21 / 8f), maxUv);
            }
            else
            {
                Assert.Equal(new Vector2(1 / 8f, 2 / 8f), maxUv);
            }
        }
    }

    [Fact]
    public void ThePartCube_IsAUnitCube_WoundOutward()
    {
        var cube = HeldMaterialRules.BuildCube(3, 5, 7);
        Assert.All(cube.Positions, p => Assert.Equal(0.5f, System.Math.Max(System.Math.Abs(p.X), System.Math.Max(System.Math.Abs(p.Y), System.Math.Abs(p.Z))), 5));
        for (int t = 0; t < cube.Triangles.Length; t += 3)
        {
            var a = cube.Positions[cube.Triangles[t]];
            var b = cube.Positions[cube.Triangles[t + 1]];
            var c = cube.Positions[cube.Triangles[t + 2]];
            var n = cube.Normals[cube.Triangles[t]];
            Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), n) > 0f, $"triangle {t / 3} is wound inward");
            Assert.True(Vector3.Dot(a, n) > 0.49f, $"triangle {t / 3} is not on its face");
        }

        Assert.Equal(6, cube.Normals.Distinct().Count());
    }

    // ---------------- the tile is lifted before it tints ----------------

    // A grey tile with a little pattern: every fourth texel is the darker one.
    private static byte[] Tile(byte grey, byte darker, byte alpha = 255)
    {
        var raw = new byte[64 * 64 * 4];
        for (int i = 0; i < raw.Length; i += 4)
        {
            raw[i] = raw[i + 1] = raw[i + 2] = i % 16 == 0 ? darker : grey;
            raw[i + 3] = alpha;
        }

        return raw;
    }

    private static double Mean(byte[] raw)
    {
        double sum = 0;
        for (int i = 0; i < raw.Length; i += 4)
        {
            sum += raw[i] / 255.0;
        }

        return sum / (raw.Length / 4);
    }

    [Fact]
    public void ADarkTile_IsLiftedTowardWhite_AndKeepsItsPattern()
    {
        var dark = Tile(100, 50, alpha: 200);
        byte[] copy = (byte[])dark.Clone();
        var lifted = HeldMaterialRules.LiftTile(dark)!;
        Assert.Equal(copy, dark); // the shared source is never changed
        Assert.Equal(HeldMaterialRules.TargetMean, Mean(lifted), 2);
        Assert.True(lifted[0] < lifted[4], "the darker texel stays darker");
        Assert.Equal(200, lifted[3]); // alpha untouched

        var bright = Tile(250, 240);
        Assert.Equal(bright, HeldMaterialRules.LiftTile(bright));
        Assert.NotSame(bright, HeldMaterialRules.LiftTile(bright));
        Assert.Null(HeldMaterialRules.LiftTile(null));
    }
}
