// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Textures;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The texture editor's canvas (#1955): true-colour frames and the tools that change them. The Unity side only
/// draws the frames, so what a tool does — and what one undo takes back — is pinned here.
/// </summary>
public sealed class PixelCanvasModelTests
{
    private static readonly TexelColor Red = new(255, 0, 0);
    private static readonly TexelColor Blue = new(0, 0, 255);
    private static readonly TexelColor HalfGreen = new(0, 255, 0, 128);

    private static byte[] Flat(byte r, byte g, byte b, byte a = 255)
    {
        var raw = new byte[TextureTiles.BytesPerFrame];
        for (int i = 0; i < raw.Length; i += 4)
        {
            raw[i] = r;
            raw[i + 1] = g;
            raw[i + 2] = b;
            raw[i + 3] = a;
        }

        return raw;
    }

    [Fact]
    public void TopLeftOnScreen_IsTheLastRowOfTheRawTile()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(10, 10, 10) });

        model.BeginEdit();
        model.Paint(0, 0, Red);
        model.CommitEdit();

        // Raw tiles store rows bottom-up (what LoadRawTextureData expects): screen row 0 is raw row 63.
        var raw = model.Frames[0];
        int topLeft = (63 * 64 + 0) * 4;
        Assert.Equal(255, raw[topLeft]);
        Assert.Equal(10, raw[0]); // the bottom-left texel is untouched
        Assert.Equal(Red, model.Get(0, 0));
    }

    [Fact]
    public void AnOpaqueTile_CannotGetAHole()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(10, 10, 10) });

        model.BeginEdit();
        model.Paint(5, 5, HalfGreen);        // a colour picked with alpha…
        model.Erase(6, 6, opaqueFallback: Blue); // …and the eraser
        model.CommitEdit();

        Assert.Equal(255, model.Get(5, 5).A);
        Assert.Equal(Blue, model.Get(6, 6));
        Assert.Equal(0, TextureTiles.CountSeeThrough(model.Frames[0]));
    }

    [Fact]
    public void ACutoutTile_ErasesToTransparent_AndKeepsPaintedAlpha()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Cutout, new[] { Flat(10, 10, 10) });

        model.BeginEdit();
        model.Erase(6, 6, opaqueFallback: Blue);
        model.Paint(7, 7, HalfGreen);
        model.CommitEdit();

        Assert.Equal(0, model.Get(6, 6).A);
        Assert.Equal(128, model.Get(7, 7).A);
    }

    [Fact]
    public void LoadingASeeThroughBlockTile_MakesItOpaque()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(10, 10, 10, a: 0) });

        Assert.Equal(0, TextureTiles.CountSeeThrough(model.Frames[0]));
    }

    [Fact]
    public void AStroke_IsOneUndoStep_AndRedoBringsItBack()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(10, 10, 10) });

        model.BeginEdit();
        model.Paint(1, 1, Red);
        model.StrokeTo(1, 1, 20, 1, Red);
        model.CommitEdit();
        Assert.Equal(Red, model.Get(10, 1));
        Assert.True(model.Dirty);

        Assert.True(model.Undo());
        Assert.Equal(new TexelColor(10, 10, 10), model.Get(10, 1));
        Assert.Equal(new TexelColor(10, 10, 10), model.Get(1, 1));
        Assert.False(model.CanUndo);

        Assert.True(model.Redo());
        Assert.Equal(Red, model.Get(10, 1));
    }

    [Fact]
    public void AClickThatChangesNothing_IsNotAStep()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(255, 0, 0) });

        model.BeginEdit();
        model.Paint(3, 3, Red); // already red
        model.CommitEdit();

        Assert.False(model.CanUndo);
        Assert.False(model.Dirty);
    }

    [Fact]
    public void AFastDrag_LeavesNoGaps()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(0, 0, 0) });

        model.Line(2, 2, 40, 25, Red);

        // Every column between the two ends has at least one red texel.
        for (int x = 2; x <= 40; x++)
        {
            bool any = false;
            for (int y = 0; y < 64; y++)
            {
                any |= model.Get(x, y) == Red;
            }

            Assert.True(any, $"column {x} has a gap");
        }
    }

    [Fact]
    public void Fill_StaysInsideTheOutline_UnlessAskedForEverywhere()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(0, 0, 0) });
        model.Rectangle(10, 10, 20, 20, Red, filled: false); // a red frame on black

        model.Fill(15, 15, Blue);
        Assert.Equal(Blue, model.Get(15, 15));
        Assert.Equal(new TexelColor(0, 0, 0), model.Get(0, 0)); // outside the frame: untouched
        Assert.Equal(Red, model.Get(10, 15));

        model.Fill(0, 0, new TexelColor(9, 9, 9), everywhere: true); // every black texel, connected or not
        Assert.Equal(new TexelColor(9, 9, 9), model.Get(63, 63));
        Assert.Equal(Blue, model.Get(15, 15));
    }

    [Fact]
    public void Mirror_PaintsTheTwin()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(0, 0, 0) }) { MirrorX = true, MirrorY = true };

        model.BeginEdit();
        model.Paint(3, 5, Red);
        model.CommitEdit();

        Assert.Equal(Red, model.Get(60, 5));
        Assert.Equal(Red, model.Get(3, 58));
        Assert.Equal(Red, model.Get(60, 58));
    }

    [Fact]
    public void Shift_RollsAroundTheEdges_SoASeamCanBeFixedInTheMiddle()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(0, 0, 0) });
        model.BeginEdit();
        model.Paint(63, 0, Red);
        model.CommitEdit();

        model.Shift(2, 3);
        Assert.Equal(Red, model.Get(1, 3));

        model.Shift(-2, -3);
        Assert.Equal(Red, model.Get(63, 0)); // and back — nothing was lost at the edge
    }

    [Fact]
    public void Flip_MirrorsTheWholeFrame()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(0, 0, 0) });
        model.BeginEdit();
        model.Paint(1, 2, Red);
        model.CommitEdit();

        model.Flip(horizontal: true);
        Assert.Equal(Red, model.Get(62, 2));
        model.Flip(horizontal: false);
        Assert.Equal(Red, model.Get(62, 61));
    }

    [Fact]
    public void Adjust_ChangesColour_NeverAlpha()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Cutout, new[] { Flat(100, 100, 100, a: 77) });

        model.Adjust(brightness: 0.2f, contrast: 0f, saturation: 0f);

        Assert.True(model.Get(0, 0).R > 100);
        Assert.Equal(77, model.Get(0, 0).A);
    }

    [Fact]
    public void Frames_DuplicateDeleteMove_AndTheFrameLimit()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(1, 1, 1) });
        Assert.Equal(0, model.Fps);

        Assert.True(model.DuplicateFrame());
        Assert.Equal(2, model.FrameCount);
        Assert.Equal(1, model.ActiveFrame);
        Assert.Equal(8, model.Fps); // a second frame makes it an animation at the middle speed

        model.BeginEdit();
        model.Paint(0, 0, Red);
        model.CommitEdit();
        Assert.Equal(Red, model.Get(1, 0, 0));
        Assert.NotEqual(Red, model.Get(0, 0, 0)); // frames are independent copies

        Assert.True(model.MoveFrame(-1));
        Assert.Equal(0, model.ActiveFrame);
        Assert.Equal(Red, model.Get(0, 0, 0));

        while (model.DuplicateFrame())
        {
        }

        Assert.Equal(TextureTiles.MaxFrames, model.FrameCount);

        while (model.DeleteFrame())
        {
        }

        Assert.Equal(1, model.FrameCount);
        Assert.Equal(0, model.Fps); // one frame is a still texture again
    }

    [Fact]
    public void Undo_AlsoTakesBackFrameOperations()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(1, 1, 1) });
        model.DuplicateFrame();

        Assert.True(model.Undo());
        Assert.Equal(1, model.FrameCount);
        Assert.Equal(0, model.Fps);
    }

    [Fact]
    public void OnlyOfferedSpeedsAreAccepted()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(1, 1, 1), Flat(2, 2, 2) }, fps: 4);
        Assert.Equal(4, model.Fps);

        model.SetFps(5);
        Assert.Equal(4, model.Fps);
        model.SetFps(12);
        Assert.Equal(12, model.Fps);
    }

    [Fact]
    public void DominantColors_OfferTheTilesOwnPalette_MostUsedFirst()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(0, 0, 0) });
        model.Rectangle(0, 0, 9, 9, Red, filled: true); // 100 red texels on 3996 black ones

        var colors = model.DominantColors(4);

        Assert.Equal(2, colors.Count);
        Assert.Equal(new TexelColor(0, 0, 0), colors[0]);
        Assert.Equal(Red, colors[1]);
    }

    [Fact]
    public void CopyFrames_IsADeepCopy_ThatSavingCannotCorrupt()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(1, 1, 1) });

        var copy = model.CopyFrames();
        copy[0][0] = 200;

        Assert.NotEqual(200, model.Frames[0][0]);
    }
}
