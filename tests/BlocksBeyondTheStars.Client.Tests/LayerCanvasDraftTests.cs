// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3693 / #3689 / #3695: the layer-canvas models on the editors' shared undo history — "unsaved" is answered right across
/// undo — and their raw canvas text, which keeps a half-drawn form or an over-budget look for a draft (the stored formats
/// refuse both) and brings it back as ONE undoable step.
/// </summary>
public sealed class LayerCanvasDraftTests
{
    [Fact]
    public void Form_UndoingBackToTheSavedState_IsNotUnsaved_AndRedoIsAgain()
    {
        var m = new FormCanvasModel();
        m.FillLayer();
        m.MarkSaved();
        m.ClearLayer();
        Assert.True(m.Dirty);

        Assert.True(m.Undo());
        Assert.False(m.Dirty);
        Assert.True(m.Redo());
        Assert.True(m.Dirty);
    }

    [Fact]
    public void Form_RawCanvas_KeepsAHalfDrawnFormOverSeveralBlocks_AndComesBackAsOneUndoStep()
    {
        var m = new FormCanvasModel();
        Assert.True(m.SetFootprint(2, 1, 1));
        m.SetLayer(0);
        m.Paint(1, 1, true); // only the first block holds something: not a valid form yet
        Assert.Equal(string.Empty, m.Encode());
        string raw = m.Raw();

        var back = new FormCanvasModel();
        Assert.True(back.ReplaceRaw(raw));
        Assert.Equal(raw, back.Raw());
        Assert.Equal((2, 1, 1, CustomShape.GridLarge), (back.Width, back.Height, back.Length, back.Grid));
        Assert.True(back.Dirty);

        Assert.True(back.Undo()); // the whole restore is one step
        Assert.Equal((1, 1, 1), (back.Width, back.Height, back.Length));
        Assert.False(back.Get(1, 0, 1));
        Assert.False(back.CanUndo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1,1,1,8")]
    [InlineData("1,1,1,8:0101")]                // too short for 8³
    [InlineData("4,1,1,8:0")]                   // wider than a form may be
    [InlineData("2,1,1,4:0")]                   // the coarse grid only for one block
    [InlineData("1,1,1,6:0")]
    [InlineData("-1,1,1,8:0")]
    public void Form_BrokenRawCanvas_IsRefused_AndNothingChanges(string? raw)
    {
        var m = new FormCanvasModel();
        m.FillLayer();
        string before = m.Raw();

        Assert.False(m.ReplaceRaw(raw));
        Assert.Equal(before, m.Raw());
    }

    [Fact]
    public void Form_APastedForm_IsOneUndoableStep()
    {
        var source = new FormCanvasModel();
        source.FillLayer();
        source.SetLayer(1);
        source.Paint(2, 2, true);
        string voxels = source.Encode();

        var m = new FormCanvasModel();
        Assert.True(m.Replace(voxels));
        Assert.Equal(voxels, m.Encode());
        Assert.True(m.Undo());
        Assert.Equal(string.Empty, m.Encode());
        Assert.False(m.Replace("not a form"));
    }

    [Fact]
    public void Look_RawCanvas_KeepsAnEmptiedOrOverBudgetLook_AndComesBackAsOneUndoStep()
    {
        var m = new ToolLookCanvasModel();
        for (int y = 0; y < 3; y++)
        {
            m.SetLayer(y);
            for (int x = 0; x < ToolLook.SizeX; x++)
            {
                for (int z = 0; z < ToolLook.SizeZ; z++)
                {
                    m.Paint(x, z, 1 + ((x + z + y) % 2)); // a 3-D checkerboard of two colours: far over the part budget
                }
            }
        }

        m.ToggleGlow(3);
        Assert.Equal(string.Empty, m.Encode());
        string raw = m.Raw();

        var back = new ToolLookCanvasModel();
        Assert.True(back.ReplaceRaw(raw));
        Assert.Equal(raw, back.Raw());
        Assert.True(back.Glows(3));
        Assert.True(back.Dirty);
        Assert.True(back.Undo());
        Assert.True(back.IsEmpty);
        Assert.False(back.Glows(3));
        Assert.False(back.Dirty, "undone back to the state it was opened in");
    }

    [Fact]
    public void Look_ARawCanvasWithAColourOutsideThePalette_IsRefused()
    {
        var m = new ToolLookCanvasModel();
        string raw = m.Raw();
        string bad = raw.Substring(0, raw.Length - 1) + "f"; // palette index 15

        Assert.Equal(ToolLook.Palette.Count <= 15, !m.ReplaceRaw(bad));
        Assert.False(m.ReplaceRaw("t1:0000:12"));
        Assert.False(m.ReplaceRaw(null));
    }
}
