// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Textures;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3939: the texture editor's soft tools on the canvas model — "calm when saving" baked only into what is
/// saved, the soft and smoothing brushes, the whole-tile actions as one undo step each, and "brightness only".
/// </summary>
public sealed class PixelCanvasSoftToolsTests
{
    private static readonly TexelColor Red = new(255, 0, 0);

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

    private static byte[] Stripes()
    {
        // Black and white columns: a busy tile whose mean is mid grey.
        var raw = Flat(0, 0, 0);
        for (int i = 0; i < raw.Length; i += 4)
        {
            if ((i / 4) % 2 == 0)
            {
                raw[i] = raw[i + 1] = raw[i + 2] = 255;
            }
        }

        return raw;
    }

    [Fact]
    public void SaveCalm_IsBakedIntoTheSavedFramesOnly()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Stripes() });
        model.SetSaveCalm(40);

        Assert.Equal(40, model.SaveCalm);
        Assert.Equal(Stripes(), model.Frames[0]);                          // the canvas keeps the painting
        Assert.Equal(TileFilters.Calm(Stripes(), 0.4), model.SavedFrames()[0]); // what is saved is calmed
        Assert.Equal(TileFilters.Calm(Stripes(), 0.4), model.ShownFrame(0));     // and shown as it will be saved
        Assert.True(model.Dirty);
    }

    [Fact]
    public void SaveCalm_IsOneUndoStep_AndClamped()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Stripes() });
        model.SetSaveCalm(30);
        model.SetSaveCalm(30); // no change, no step
        model.SetSaveCalm(250);

        Assert.Equal(100, model.SaveCalm);
        Assert.True(model.Undo());
        Assert.Equal(30, model.SaveCalm);
        Assert.True(model.Undo());
        Assert.Equal(0, model.SaveCalm);
        Assert.False(model.Dirty); // back to the loaded state
        Assert.False(model.CanUndo);
    }

    [Fact]
    public void LoadWithCalm_OpensClean_AndLoadResetsTheCalm()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque);
        model.LoadWithCalm(new[] { Stripes() }, 0, TextureAlphaMode.Opaque, 55);
        Assert.Equal(55, model.SaveCalm);
        Assert.False(model.Dirty);

        model.Load(new[] { Stripes() }, 0);
        Assert.Equal(0, model.SaveCalm);
    }

    [Fact]
    public void ReplaceAllWithCalm_IsOneUndoStep()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(1, 2, 3) });
        model.ReplaceAll(new[] { Stripes() }, 0, 20);

        Assert.Equal(20, model.SaveCalm);
        Assert.Equal(Stripes(), model.Frames[0]);
        Assert.True(model.Undo());
        Assert.Equal(0, model.SaveCalm);
        Assert.Equal(Flat(1, 2, 3), model.Frames[0]);
    }

    [Fact]
    public void SoftBrush_FallsOffTowardItsRim_AndNeverPassesItsOpacity()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(0, 0, 0) });
        model.BeginSoftStroke();
        for (int i = 0; i < 20; i++)
        {
            model.SoftStrokeTo(32, 32, 32, 32, Red, 4); // the mouse rests: the stroke must not build up
        }

        model.CommitEdit();

        byte centre = model.Get(32, 32).R, near = model.Get(34, 32).R, rim = model.Get(35, 32).R;
        Assert.Equal(TileFilters.ToByte(255 * PixelCanvasModel.SoftCentreOpacity), centre);
        Assert.True(centre > near && near > rim, $"centre {centre}, near {near}, rim {rim}");
        Assert.Equal(0, model.Get(36, 32).R); // beyond the rim (radius 3)
        Assert.Equal(0, model.Get(32, 32).G); // a blend toward red, nothing else

        // One stroke = one undo step.
        Assert.True(model.Undo());
        Assert.Equal(Flat(0, 0, 0), model.Frames[0]);
    }

    [Fact]
    public void SoftBrush_IsDeterministic_AndBlendsWithWhatIsThere()
    {
        byte[] Paint()
        {
            var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Stripes() });
            model.BeginSoftStroke();
            model.SoftStrokeTo(10, 10, 40, 20, Red, 3);
            model.SoftStrokeTo(40, 20, 50, 50, Red, 3);
            model.CommitEdit();
            return model.Frames[0];
        }

        var a = Paint();
        Assert.Equal(a, Paint());
        int o = (((63 - 10) * 64) + 10) * 4; // screen (10, 10): a white texel of the stripes, now partly red
        Assert.Equal(255, a[o]);
        Assert.InRange(a[o + 1], 1, 254);
    }

    [Fact]
    public void SoftEraser_OnACutoutTile_FadesTheAlpha_AndKeepsTheColour()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Cutout, new[] { Flat(20, 200, 40) });
        model.BeginSoftStroke();
        model.SoftStrokeTo(30, 30, 30, 30, TexelColor.Clear, 2);
        model.CommitEdit();

        var c = model.Get(30, 30);
        Assert.Equal(TileFilters.ToByte(255 * (1 - PixelCanvasModel.SoftCentreOpacity)), c.A);
        Assert.Equal(200, c.G);
    }

    [Fact]
    public void SmoothBrush_SmoothsWhereItPasses_KeepsAlpha_AndIsOneStep()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Free, new[] { Stripes() });
        model.BeginSoftStroke();
        model.SmoothStrokeTo(20, 20, 24, 20, 3);
        model.CommitEdit();

        Assert.Equal(TileFilters.Smooth(Stripes())[(((63 - 20) * 64) + 20) * 4], model.Get(20, 20).R); // full at the centre
        Assert.Equal(255, model.Get(0, 0).R);   // far away: untouched
        Assert.Equal(255, model.Get(22, 20).A); // alpha kept
        Assert.True(model.Undo());
        Assert.Equal(Stripes(), model.Frames[0]);
    }

    private static byte[] Busy()
    {
        // A tile with structure at several scales (not flattened by one smoothing pass) and a seam.
        var raw = Flat(0, 0, 0);
        for (int i = 0; i < raw.Length; i += 4)
        {
            int x = (i / 4) % 64, y = (i / 4) / 64;
            raw[i] = (byte)(x * 4);
            raw[i + 1] = (byte)(((x / 8) + (y / 8)) % 2 == 0 ? 200 : 40);
            raw[i + 2] = (byte)((x * y) % 256);
        }

        return raw;
    }

    [Fact]
    public void WholeTileActions_AreOneUndoStepEach()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Busy() });
        model.SmoothAll();
        Assert.Equal(TileFilters.Smooth(Busy()), model.Frames[0]);
        model.CalmAll();
        model.MakeSeamless();

        Assert.True(model.Undo());
        Assert.True(model.Undo());
        Assert.Equal(TileFilters.Smooth(Busy()), model.Frames[0]);
        Assert.True(model.Undo());
        Assert.Equal(Busy(), model.Frames[0]);
        Assert.False(model.CanUndo);
    }

    [Fact]
    public void MakeSeamless_ReportsTheSeamBeforeAndAfter()
    {
        var tile = Flat(0, 0, 0);
        for (int x = 0; x < 64; x++)
        {
            for (int y = 0; y < 64; y++)
            {
                // A hard seam left↔right over a fine grain (a pure ramp would only be scaled down by the heal, and the
                // measure, a ratio of steps, does not see a scale).
                tile[((y * 64) + x) * 4] = (byte)(x * 4);
                uint h = unchecked((uint)((x * 73856093) ^ (y * 19349663)));
                h ^= h >> 13;
                h = unchecked(h * 0x5bd1e995u);
                h ^= h >> 15;
                tile[(((y * 64) + x) * 4) + 1] = (byte)(100 + (h % 40)); // an unstructured grain (a lattice would need alignment)
            }
        }

        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { tile });
        var (before, after) = model.MakeSeamless();
        Assert.True(before > 5.0 && after <= 1.3, $"{before:0.00} → {after:0.00}");
    }

    [Fact]
    public void BrightnessOnly_ChangesTheBrightness_NotTheHue()
    {
        var leaf = new TexelColor(60, 160, 40);
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(leaf.R, leaf.G, leaf.B) }) { LuminanceOnly = true };

        model.BeginEdit();
        model.Paint(5, 5, new TexelColor(250, 250, 250), 1); // white paint …
        model.CommitEdit();
        model.Fill(40, 40, new TexelColor(20, 20, 20));      // … and a dark fill

        var lit = model.Get(5, 5);
        var dark = model.Get(40, 40);
        Assert.True(lit.G > leaf.G && dark.G < leaf.G);
        Assert.True(lit.G > lit.R && lit.R > lit.B, $"lit {lit.R},{lit.G},{lit.B} lost the leaf's hue order");
        Assert.True(dark.G > dark.R && dark.R >= dark.B, $"dark {dark.R},{dark.G},{dark.B} lost the leaf's hue order");
        Assert.InRange(TileFilters.Luma(lit.R, lit.G, lit.B), 240, 255);
        Assert.Equal(TileFilters.Greyscale(model.Frames[0]), model.ShownFrame(0)); // the canvas shows brightness alone
    }

    [Fact]
    public void BrightnessOnly_SoftBrushKeepsTheHueToo()
    {
        var leaf = new TexelColor(60, 160, 40);
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(leaf.R, leaf.G, leaf.B) }) { LuminanceOnly = true };
        model.BeginSoftStroke();
        model.SoftStrokeTo(30, 30, 30, 30, new TexelColor(255, 0, 255), 4); // a magenta of mid brightness
        model.CommitEdit();

        var c = model.Get(30, 30);
        Assert.True(c.G > c.R && c.R > c.B, $"{c.R},{c.G},{c.B}");
    }

    [Fact]
    public void Off_TheHardToolsPaintAsBefore()
    {
        var model = new PixelCanvasModel(TextureAlphaMode.Opaque, new[] { Flat(60, 160, 40) });
        model.Fill(0, 0, Red);
        Assert.Equal(Red, model.Get(10, 10));
        Assert.Equal(model.Frames[0], model.ShownFrame(0));
    }
}
