// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3941: colour slots per micro cell — the per-slot merge the client draws, the rule that turns a slot into
/// vertex values, and the form editor's model keeping the values through paint, load, save, drafts and undo.
/// </summary>
public sealed class ShapeColourSlotsTests
{
    private static string RandomBitmap(int seed, int grid, double fill, int slots)
    {
        var rng = new Random(seed);
        var chars = new char[grid * grid * grid];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = rng.NextDouble() < fill ? (char)('1' + rng.Next(slots)) : '0';
        }

        return new string(chars);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void OneSlot_MergesIntoExactlyTheValueBlindBoxes(int grid)
    {
        for (int seed = 0; seed < 40; seed++)
        {
            string bitmap = RandomBitmap(seed, grid, 0.6, 1);
            var plain = CustomShape.Merge(bitmap);
            var bySlot = ShapeColourSlots.Merge(bitmap);
            Assert.Equal(plain, bySlot.Select(b => b.Box).ToList());
            Assert.All(bySlot, b => Assert.Equal(ShapeColourSlots.Main, b.Slot));
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void SlottedForm_CoversTheSameCells_WithoutOverlap_KeepsTheSlot_AndIsDeterministic(int grid)
    {
        for (int seed = 0; seed < 40; seed++)
        {
            string bitmap = RandomBitmap(seed + 100, grid, 0.55, 5);
            var boxes = ShapeColourSlots.Merge(bitmap);
            var covered = new int[bitmap.Length];
            foreach (var sb in boxes)
            {
                var b = sb.Box;
                for (int y = b.Y0; y < b.Y1; y++)
                {
                    for (int z = b.Z0; z < b.Z1; z++)
                    {
                        for (int x = b.X0; x < b.X1; x++)
                        {
                            int i = CustomShape.IndexOf(x, y, z, grid);
                            covered[i]++;
                            Assert.Equal(ShapeColourSlots.SlotOf(bitmap[i]), sb.Slot);
                        }
                    }
                }
            }

            for (int i = 0; i < bitmap.Length; i++)
            {
                Assert.Equal(bitmap[i] == '0' ? 0 : 1, covered[i]); // same cells as the value-blind merge, each once
            }

            Assert.Equal(boxes.Select(b => (b.Box, b.Slot)), ShapeColourSlots.Merge(bitmap).Select(b => (b.Box, b.Slot)));
        }
    }

    [Fact]
    public void SlotsOfChars_SixToF_ReadAsMain_AndAnOldFormHasNoSlots()
    {
        Assert.Equal(ShapeColourSlots.Empty, ShapeColourSlots.SlotOf('0'));
        Assert.Equal(ShapeColourSlots.Main, ShapeColourSlots.SlotOf('1'));
        Assert.Equal(ShapeColourSlots.Glow, ShapeColourSlots.SlotOf('5'));
        Assert.Equal(ShapeColourSlots.Main, ShapeColourSlots.SlotOf('6'));
        Assert.Equal(ShapeColourSlots.Main, ShapeColourSlots.SlotOf('f'));
        Assert.False(ShapeColourSlots.HasSlots(new string('1', 32) + new string('0', 32)));
        Assert.False(ShapeColourSlots.HasSlots(new string('7', 32) + new string('0', 32)));
        Assert.True(ShapeColourSlots.HasSlots(new string('1', 31) + "3" + new string('0', 32)));
    }

    [Fact]
    public void OverBudgetPerSlot_IsDrawnInOneColour_AsTheValueBlindBoxes()
    {
        // A full 8³ block minus one corner cell, the slots in a 3-D checker: few boxes value-blind, 255 per slot.
        var chars = new char[CustomShape.LargeChars];
        for (int y = 0; y < 8; y++)
        {
            for (int z = 0; z < 8; z++)
            {
                for (int x = 0; x < 8; x++)
                {
                    chars[CustomShape.IndexOf(x, y, z, 8)] = ((x + y + z) & 1) == 0 ? '1' : '2';
                }
            }
        }

        chars[0] = '0';
        string bitmap = new string(chars);
        Assert.True(CustomShape.FitsBudget(bitmap));
        Assert.False(ShapeColourSlots.FitsBudget(bitmap));
        var visible = ShapeColourSlots.VisibleBoxes(bitmap, slotsOn: true);
        Assert.Equal(CustomShape.Merge(bitmap), visible.Select(b => b.Box).ToList());
        Assert.All(visible, b => Assert.Equal(ShapeColourSlots.Main, b.Slot));
    }

    [Fact]
    public void SwitchOff_DrawsTheValueBlindBoxes()
    {
        string bitmap = new string('2', 32) + new string('0', 32);
        var visible = ShapeColourSlots.VisibleBoxes(bitmap, slotsOn: false);
        Assert.Equal(CustomShape.Merge(bitmap), visible.Select(b => b.Box).ToList());
        Assert.All(visible, b => Assert.Equal(ShapeColourSlots.Main, b.Slot));
    }

    // ---------------------------------------------------------------- the slot rule

    private static ShapeFaceLook Plain(float mode = 0f, float r = 0f, float g = 0f, float b = 0f)
        => new ShapeFaceLook { Gloss = 0.2f, Metal = 0f, Shade = 0.76f, Emission = 0f, Mode = mode, TintR = r, TintG = g, TintB = b };

    [Fact]
    public void Main_LeavesTheFaceAsItIs()
    {
        var look = Plain(3f, 0.8f, 0.2f, 0.1f);
        Assert.Equal(look, ShapeColourSlotRule.Apply(ShapeColourSlots.Main, look));
        Assert.Equal(look, ShapeColourSlotRule.Apply(7, look));
    }

    [Fact]
    public void Second_OnAPlainBlock_MultipliesTheTileBrighter_AndOnADyedOne_LiftsTheDye()
    {
        var plain = ShapeColourSlotRule.Apply(ShapeColourSlots.Second, Plain());
        Assert.Equal(2f, plain.Mode);
        Assert.Equal(ShapeColourSlotRule.SecondPlain, plain.TintR);
        var dyed = ShapeColourSlotRule.Apply(ShapeColourSlots.Second, Plain(3f, 0.5f, 0.2f, 0.1f));
        Assert.Equal(3f, dyed.Mode);
        Assert.Equal(0.5f * ShapeColourSlotRule.SecondTint, dyed.TintR, 4);
    }

    [Fact]
    public void Dark_LowersTheShade_Glow_GlowsAndAccent_TurnsTheDyeToTheOppositeHue()
    {
        var dark = ShapeColourSlotRule.Apply(ShapeColourSlots.Dark, Plain());
        Assert.Equal(0.76f * ShapeColourSlotRule.DarkShade, dark.Shade, 4);
        Assert.Equal(0f, dark.Mode);

        var glow = ShapeColourSlotRule.Apply(ShapeColourSlots.Glow, Plain());
        Assert.True(glow.Emission >= ShapeColourSlotRule.GlowEmission);
        Assert.Equal(1f, glow.Shade);

        var accent = ShapeColourSlotRule.Apply(ShapeColourSlots.Accent, Plain(3f, 0.9f, 0.1f, 0.1f)); // red dye
        Assert.Equal(3f, accent.Mode);
        Assert.True(accent.TintG > accent.TintR && accent.TintB > accent.TintR); // toward cyan

        var bark = ShapeColourSlotRule.Apply(ShapeColourSlots.Accent, Plain(4f, 0.3f, 0.2f, 0.1f));
        Assert.Equal(3f, bark.Mode); // the bark hue's complement as a dye

        var stone = ShapeColourSlotRule.Apply(ShapeColourSlots.Accent, Plain());
        Assert.Equal(0f, stone.Mode); // no hue to take: a polished metal inlay
        Assert.True(stone.Gloss >= 0.85f && stone.Metal >= 0.8f);
    }

    // ---------------------------------------------------------------- the editor's model

    [Fact]
    public void Model_PaintsWithTheChosenSlot_SavesAndLoadsIt_AndUndoes()
    {
        var m = new FormCanvasModel();
        m.FillLayer();
        m.PaintValue = ShapeColourSlots.Accent;
        Assert.True(m.Paint(2, 3, true));
        Assert.Equal(ShapeColourSlots.Accent, m.ValueAt(2, 0, 3));
        Assert.False(m.Paint(2, 3, true)); // the same value again changes nothing

        string voxels = m.Encode();
        Assert.Contains("3", voxels, StringComparison.Ordinal);
        Assert.True(CustomShape.IsValidVoxels(voxels));

        var back = new FormCanvasModel();
        Assert.True(back.Load(voxels));
        Assert.Equal(ShapeColourSlots.Accent, back.ValueAt(2, 0, 3));
        Assert.Equal(ShapeColourSlots.Main, back.ValueAt(1, 0, 1));
        Assert.Equal(voxels, back.Encode());

        Assert.True(m.Undo());
        Assert.Equal(ShapeColourSlots.Main, m.ValueAt(2, 0, 3));
        Assert.True(m.Paint(2, 3, false));
        Assert.Equal(0, m.ValueAt(2, 0, 3));
    }

    [Fact]
    public void Model_AnOldForm_RoundTripsUnchanged()
    {
        var chars = new char[CustomShape.SmallChars];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = i % 3 == 0 ? '1' : '0';
        }

        string old = new string(chars);
        var m = new FormCanvasModel();
        Assert.True(m.Load(old));
        Assert.Equal(old, m.Encode());
    }

    [Fact]
    public void Model_KeepsSlotsThroughMirrorShiftGridAndDrafts()
    {
        var m = new FormCanvasModel();
        m.PaintValue = ShapeColourSlots.Glow;
        m.Paint(1, 0, true);
        m.Mirror(alongX: true);
        Assert.Equal(ShapeColourSlots.Glow, m.ValueAt(6, 0, 0));
        m.Shift(0, 0, 2);
        Assert.Equal(ShapeColourSlots.Glow, m.ValueAt(6, 0, 2));
        m.SetGrid(CustomShape.GridSmall); // nearest sample: the coarse cell (3, 0, 1) reads the fine cell (6, 0, 2)
        Assert.Equal(ShapeColourSlots.Glow, m.ValueAt(3, 0, 1));

        string raw = m.Raw();
        var back = new FormCanvasModel();
        Assert.True(back.ReplaceRaw(raw));
        Assert.Equal(raw, back.Raw());
        Assert.Equal(ShapeColourSlots.Glow, back.ValueAt(3, 0, 1));
    }

    [Fact]
    public void Model_CountsWhatIsDrawn_AndRefusesAFormOverThePerSlotBudget()
    {
        var m = new FormCanvasModel();
        for (int layer = 0; layer < 2; layer++)
        {
            m.SetLayer(layer);
            for (int z = 0; z < 8; z++)
            {
                for (int x = 0; x < 8; x++)
                {
                    m.PaintValue = ((x + z + layer) & 1) == 0 ? 1 : 2;
                    m.Paint(x, z, true);
                }
            }
        }

        Assert.Equal(FormProblem.None, m.Problem(out _)); // value-blind: one slab
        m.CountSlotBoxes = true;
        Assert.True(m.BoxesOfCell(0) > CustomShape.MaxBoxes);
        Assert.Equal(FormProblem.TooDetailed, m.Problem(out _));
        Assert.Equal(string.Empty, m.Encode());
    }
}
