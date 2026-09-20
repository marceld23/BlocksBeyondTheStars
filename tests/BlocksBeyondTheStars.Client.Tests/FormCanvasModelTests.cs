// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The model of the main-menu form editor (#1960): what it lets through is what the server registers, a form
/// may grow over several blocks (#1961), and every action can be taken back.
/// </summary>
public sealed class FormCanvasModelTests
{
    /// <summary>Fills the bottom layer of every block of the footprint — the simplest valid form.</summary>
    private static void Floor(FormCanvasModel m)
    {
        m.SetLayer(0);
        m.FillLayer();
    }

    [Fact]
    public void AOneBlockForm_EncodesToTheLegacyLength_AndLoadsBack()
    {
        var m = new FormCanvasModel();
        Floor(m);

        string voxels = m.Encode();

        Assert.Equal(CustomShape.LargeChars, voxels.Length);
        Assert.True(CustomShape.IsValidVoxels(voxels));

        var back = new FormCanvasModel();
        Assert.True(back.Load(voxels));
        Assert.Equal((1, 1, 1, 8), (back.Width, back.Height, back.Length, back.Grid));
        Assert.True(back.Get(3, 0, 5));
        Assert.False(back.Get(3, 1, 5));
        Assert.False(back.Dirty);
        Assert.False(back.CanUndo); // the history belongs to the form that was open before
    }

    [Fact]
    public void AFormOverSeveralBlocks_EncodesToTheMultiCellPayload_AndLoadsBack()
    {
        var m = new FormCanvasModel();
        Assert.True(m.SetFootprint(2, 2, 1));
        Floor(m);            // the two bottom blocks
        m.SetLayer(8);
        m.FillLayer();       // the two top blocks

        string voxels = m.Encode();

        Assert.StartsWith("m1:221:", voxels);
        Assert.True(CustomShape.IsValidVoxels(voxels));
        Assert.True(CustomShape.FitsBudget(voxels));

        var back = new FormCanvasModel();
        Assert.True(back.Load(voxels));
        Assert.Equal((2, 2, 1), (back.Width, back.Height, back.Length));
        Assert.True(back.Get(15, 8, 7));
        Assert.False(back.Get(15, 9, 7));
        Assert.Equal(voxels, back.Encode());
    }

    [Fact]
    public void TheModel_NamesWhatIsWrong_AndEncodesNothingUntilItIsFixed()
    {
        var m = new FormCanvasModel();
        Assert.Equal(FormProblem.Empty, m.Problem(out _));
        Assert.Equal(string.Empty, m.Encode());

        m.SetFootprint(2, 1, 1);
        m.Paint(0, 0, true); // only the first block holds something
        Assert.Equal(FormProblem.EmptyCell, m.Problem(out int cell));
        Assert.Equal(1, cell);
        Assert.Equal(string.Empty, m.Encode());

        for (int y = 0; y < m.SizeY; y++)
        {
            m.SetLayer(y);
            m.FillLayer();
        }

        Assert.Equal(FormProblem.SolidCubes, m.Problem(out _));

        // a 3-D checkerboard in block 1: far more boxes than a collider can afford
        m.ClearAll();
        Floor(m);
        for (int y = 1; y < 8; y++)
        {
            m.SetLayer(y);
            for (int z = 0; z < 8; z++)
            {
                for (int x = 8; x < 16; x++)
                {
                    m.Paint(x, z, ((x + y + z) & 1) == 0);
                }
            }
        }

        Assert.Equal(FormProblem.TooDetailed, m.Problem(out cell));
        Assert.Equal(1, cell);
        Assert.True(m.BoxesOfCell(1) > CustomShape.MaxBoxes);
        Assert.Equal(m.BoxesOfCell(1), m.MaxBoxesUsed());
    }

    [Theory]
    [InlineData(4, 1, 1)] // a side over three blocks
    [InlineData(3, 3, 1)] // nine blocks
    [InlineData(0, 1, 1)]
    public void AFootprintTheGameCannotPlace_IsRefused(int w, int h, int l)
    {
        var m = new FormCanvasModel();

        Assert.False(m.SetFootprint(w, h, l));
        Assert.Equal((1, 1, 1), (m.Width, m.Height, m.Length));
    }

    [Fact]
    public void GrowingTheFootprint_KeepsWhatIsDrawn_AndShrinkingCutsItOff()
    {
        var m = new FormCanvasModel();
        m.SetLayer(2);
        m.Paint(7, 7, true);

        Assert.True(m.SetFootprint(2, 1, 2));
        Assert.True(m.Get(7, 2, 7));   // still in the first block
        m.Paint(15, 15, true);         // and something in the far block

        Assert.True(m.SetFootprint(1, 1, 1));
        Assert.True(m.Get(7, 2, 7));
        Assert.False(m.Get(15, 2, 15));

        Assert.True(m.Undo());         // the shrink can be taken back, with what it cut off
        Assert.Equal((2, 1, 2), (m.Width, m.Height, m.Length));
        Assert.True(m.Get(15, 2, 15));
    }

    [Fact]
    public void ACoarseSketch_BecomesFine_WhenTheFormGrowsBeyondOneBlock()
    {
        var m = new FormCanvasModel();
        Assert.True(m.SetGrid(CustomShape.GridSmall));
        m.Paint(1, 1, true);
        Assert.Equal(4, m.SizeX);

        Assert.True(m.SetFootprint(2, 1, 1));

        Assert.Equal(CustomShape.GridLarge, m.Grid);
        Assert.True(m.Get(2, 0, 2) && m.Get(3, 0, 3)); // one coarse cell = 2×2×2 fine ones
        Assert.False(m.SetGrid(CustomShape.GridSmall)); // no coarse grid over several blocks
    }

    [Fact]
    public void AStroke_IsOneUndoStep_AndRedoBringsItBack()
    {
        var m = new FormCanvasModel();
        m.BeginStroke();
        m.Paint(0, 0, true);
        m.Paint(1, 0, true);
        m.Paint(2, 0, true);
        m.EndStroke();
        m.Paint(5, 5, true); // a single click outside a stroke is a step of its own

        Assert.True(m.Undo());
        Assert.True(m.Get(2, 0, 0));
        Assert.False(m.Get(5, 0, 5));

        Assert.True(m.Undo());
        Assert.False(m.Get(0, 0, 0));
        Assert.False(m.CanUndo);

        Assert.True(m.Redo());
        Assert.True(m.Redo());
        Assert.True(m.Get(5, 0, 5));
        Assert.False(m.CanRedo);

        m.Undo();
        m.Paint(6, 6, true);
        Assert.False(m.CanRedo); // a new edit forks the history
    }

    [Fact]
    public void AnActionThatChangesNothing_LeavesNoUndoStep()
    {
        var m = new FormCanvasModel();

        m.ClearAll();
        m.CopyLayerBelow(); // layer 0 has nothing below
        m.BeginStroke();
        m.Paint(0, 0, false);
        m.EndStroke();

        Assert.False(m.CanUndo);
        Assert.False(m.Dirty);
    }

    [Fact]
    public void TheHelpers_WorkOnTheWholeForm()
    {
        var m = new FormCanvasModel();
        m.SetFootprint(2, 1, 1);
        m.Paint(0, 0, true);

        m.Mirror(alongX: true);
        Assert.True(m.Get(0, 0, 0) && m.Get(15, 0, 0)); // across BOTH blocks, the original stays

        m.SetLayer(1);
        m.CopyLayerBelow();
        Assert.True(m.Get(15, 1, 0));

        m.Shift(0, 1, 2);
        Assert.False(m.Get(15, 0, 0));
        Assert.True(m.Get(15, 1, 2) && m.Get(15, 2, 2));

        m.SetLayer(2);
        m.ClearLayer();
        Assert.False(m.Get(15, 2, 2));
        Assert.True(m.Get(15, 1, 2));
    }

    [Fact]
    public void Garbage_DoesNotLoad_AndTheOpenFormStays()
    {
        var m = new FormCanvasModel();
        m.Paint(1, 1, true);

        Assert.False(m.Load("m1:211:" + new string('1', 10)));
        Assert.False(m.Load(null));

        Assert.True(m.Get(1, 0, 1));
    }
}
