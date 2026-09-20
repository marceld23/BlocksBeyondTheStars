// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The model of the tool-look editor (#1963): what it encodes is what the server accepts, and every
/// action — a stroke, a glow switch, a helper — can be taken back.</summary>
public sealed class ToolLookCanvasModelTests
{
    [Fact]
    public void WhatIsPainted_EncodesToALookTheServerAccepts_AndLoadsBack()
    {
        var m = new ToolLookCanvasModel();
        m.SetLayer(2);
        m.BeginStroke();
        for (int z = 3; z < 12; z++)
        {
            m.Paint(4, z, 12);
        }

        m.EndStroke();
        m.ToggleGlow(12);

        string look = m.Encode();

        Assert.True(ToolLook.IsValid(look));
        Assert.Equal(1, m.PartsUsed());
        var back = new ToolLookCanvasModel();
        Assert.True(back.Load(look));
        Assert.Equal(12, back.Get(4, 2, 5));
        Assert.True(back.Glows(12));
        Assert.False(back.Dirty);
        Assert.False(back.CanUndo);
    }

    [Fact]
    public void NothingDrawn_OrTooManyParts_EncodesNothing_ButThePreviewStillShows()
    {
        var m = new ToolLookCanvasModel();
        Assert.Equal(string.Empty, m.Encode());
        Assert.Empty(m.PreviewBoxes());

        for (int y = 0; y < ToolLook.SizeY; y++)
        {
            m.SetLayer(y);
            for (int z = 0; z < ToolLook.SizeZ; z++)
            {
                for (int x = 0; x < ToolLook.SizeX; x++)
                {
                    if (((x + y + z) & 1) == 0)
                    {
                        m.Paint(x, z, 3);
                    }
                }
            }
        }

        Assert.True(m.PartsUsed() > ToolLook.MaxParts);
        Assert.Equal(string.Empty, m.Encode());
        Assert.Equal(m.PartsUsed(), m.PreviewBoxes().Count);
    }

    [Fact]
    public void AStroke_AGlowSwitch_AndAHelper_AreEachOneUndoStep()
    {
        var m = new ToolLookCanvasModel();
        m.SetLayer(0);
        m.BeginStroke();
        m.Paint(0, 0, 5);
        m.Paint(1, 0, 5);
        m.EndStroke();
        m.ToggleGlow(5);
        m.MirrorX();
        Assert.Equal(5, m.Get(ToolLook.SizeX - 1, 0, 0));

        Assert.True(m.Undo()); // the mirror
        Assert.Equal(0, m.Get(ToolLook.SizeX - 1, 0, 0));
        Assert.True(m.Glows(5));

        Assert.True(m.Undo()); // the glow
        Assert.False(m.Glows(5));

        Assert.True(m.Undo()); // the stroke
        Assert.True(m.IsEmpty);
        Assert.False(m.CanUndo);

        Assert.True(m.Redo());
        Assert.Equal(5, m.Get(1, 0, 0));
    }

    [Fact]
    public void Erasing_IsPaintingColourZero_AndBadInputChangesNothing()
    {
        var m = new ToolLookCanvasModel();
        m.SetLayer(1);
        Assert.True(m.Paint(2, 2, 8));
        Assert.True(m.Paint(2, 2, 0));
        Assert.True(m.IsEmpty);

        Assert.False(m.Paint(-1, 0, 3));
        Assert.False(m.Paint(0, ToolLook.SizeZ, 3));
        Assert.False(m.Paint(0, 0, 16)); // no such colour
        m.ToggleGlow(0);                 // "empty" cannot glow
        Assert.Equal(0, m.GlowMask);
        Assert.False(m.Load("t1:garbage"));
    }

    [Fact]
    public void TheLayerHelpers_Work()
    {
        var m = new ToolLookCanvasModel();
        m.SetLayer(0);
        m.Paint(3, 3, 2);
        m.SetLayer(1);
        m.CopyLayerBelow();
        Assert.Equal(2, m.Get(3, 1, 3));

        m.ClearLayer();
        Assert.Equal(0, m.Get(3, 1, 3));
        Assert.Equal(2, m.Get(3, 0, 3));

        m.ClearAll();
        Assert.True(m.IsEmpty);
        m.SetLayer(99);
        Assert.Equal(ToolLook.SizeY - 1, m.Layer);
    }
}
