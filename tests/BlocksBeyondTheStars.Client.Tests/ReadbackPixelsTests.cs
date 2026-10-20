// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Text.Json;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Client.Feedback;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The CPU half of the GPU reads (#2390, #2845): a browser screenshot arrived as three squeezed copies of
/// the frame because WebGPU's readback answered with four bytes per pixel where three were laid out, and the frame
/// covered only the top-left three quarters of the capture because the copied back buffer was smaller than the screen
/// size the engine believed. Plus the graphics node every report carries since (#2844).
/// </summary>
public sealed class ReadbackPixelsTests
{
    /// <summary>A packed RGBA32 image whose pixel (x, y) is (x, y, 7, 255) — every pixel tells where it came from.</summary>
    private static byte[] Gradient(int w, int h, int stride = 0)
    {
        stride = stride == 0 ? w * 4 : stride;
        var raw = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * stride) + (x * 4);
                raw[i] = (byte)x;
                raw[i + 1] = (byte)y;
                raw[i + 2] = 7;
                raw[i + 3] = 255;
            }
        }

        return raw;
    }

    [Fact]
    public void Rgba_ToRgb24_KeepsEveryPixel_InsteadOfSmearingFourBytesOverThree()
    {
        var rgb = ReadbackPixels.FromRgba(Gradient(5, 3), 5, 3, dstBytesPerPixel: 3, flipRows: false)!;

        Assert.Equal(5 * 3 * 3, rgb.Length);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                int o = ((y * 5) + x) * 3;
                Assert.Equal((x, y, 7), (rgb[o], rgb[o + 1], rgb[o + 2]));
            }
        }
    }

    [Fact]
    public void RowPadding_IsTakenFromTheDataLength_AndRowsFlipWhenAsked()
    {
        // WebGPU pads every row of a copy to 256 bytes: a 3-pixel row is 12 bytes of picture and 244 of padding.
        var rgba = ReadbackPixels.FromRgba(Gradient(3, 4, stride: 256), 3, 4, dstBytesPerPixel: 4, flipRows: true)!;

        Assert.Equal(3 * 4 * 4, rgba.Length);
        Assert.Equal(3, rgba[1]);                       // the first output row is the last input row
        Assert.Equal(0, rgba[((3 * 3) + 2) * 4 + 1]);    // and the last output row the first
        Assert.Equal(2, rgba[((3 * 3) + 2) * 4]);
    }

    [Fact]
    public void TooLittleData_IsNoImage()
    {
        Assert.Null(ReadbackPixels.FromRgba(new byte[10], 4, 4, 3, flipRows: false));
    }

    /// <summary>A capture cleared to the sentinel colour with a frame copied into one corner.</summary>
    private static byte[] CaptureWithFrame(int w, int h, int frameW, int frameH, bool frameAtBottom)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = ((y * w) + x) * 4;
                bool covered = x < frameW && (frameAtBottom ? y < frameH : y >= h - frameH);
                px[i] = covered ? (byte)40 : ReadbackPixels.SentinelR;
                px[i + 1] = covered ? (byte)90 : ReadbackPixels.SentinelG;
                px[i + 2] = covered ? (byte)30 : ReadbackPixels.SentinelB;
                px[i + 3] = covered ? (byte)255 : ReadbackPixels.SentinelA;
            }
        }

        return px;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APartlyCoveredCapture_IsCutToTheFrame_LessTheSeam(bool frameAtBottom)
    {
        // The browser case: a 1600×824 back buffer copied into a capture sized for 2133×1098, downscaled — the frame
        // fills three quarters of each side.
        var px = CaptureWithFrame(40, 20, 30, 15, frameAtBottom);

        Assert.True(ReadbackPixels.TryFindCoveredRect(px, 40, 20, out int x0, out int y0, out int w, out int h));

        Assert.Equal(0, x0);
        Assert.Equal(29, w); // 30 covered columns, one shaved off the cut side
        Assert.Equal(14, h);
        Assert.Equal(frameAtBottom ? 0 : 6, y0);

        var cut = ReadbackPixels.Crop(px, 40, 4, x0, y0, w, h);
        for (int i = 0; i < cut.Length; i += 4)
        {
            Assert.False(ReadbackPixels.IsSentinel(cut, i), "no clear colour may survive the cut");
        }
    }

    [Fact]
    public void AFullyCoveredCapture_IsLeftAlone()
    {
        Assert.False(ReadbackPixels.TryFindCoveredRect(CaptureWithFrame(16, 8, 16, 8, true), 16, 8, out _, out _, out _, out _));
    }

    [Fact]
    public void AnUncoveredCapture_IsLeftAlone_RatherThanCutToNothing()
    {
        Assert.False(ReadbackPixels.TryFindCoveredRect(CaptureWithFrame(16, 8, 0, 0, true), 16, 8, out _, out _, out _, out _));
    }

    [Fact]
    public void DropAlpha_PacksThreeBytesPerPixel()
    {
        var rgb = ReadbackPixels.DropAlpha(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 2);
        Assert.Equal(new byte[] { 1, 2, 3, 5, 6, 7 }, rgb);
    }

    [Fact]
    public void AReport_SaysHowTheFrameWasDrawn()
    {
        var info = new GraphicsReportInfo
        {
            Api = "WebGPU",
            UvStartsAtTop = true,
            Preset = "Medium",
            PresetAuto = true,
            VisorEffects = true,
            VisorPipeline = true,
            HudOrientation = "flipped",
            RenderScale = 1f,
            Msaa = 2,
            Antialiasing = "SubpixelMorphologicalAntiAliasing",
            Hdr = true,
            ScreenWidth = 2133,
            ScreenHeight = 1098,
            LastCaptureCover = "1200x618 of 1600x824",
        };
        var reportJson = new Dictionary<string, object>();

        info.WriteTo(reportJson);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(reportJson));
        var g = doc.RootElement.GetProperty("graphics");
        Assert.Equal("WebGPU", g.GetProperty("api").GetString());
        Assert.Equal("Medium", g.GetProperty("preset").GetString());
        Assert.True(g.GetProperty("visorPipeline").GetBoolean());
        Assert.Equal("flipped", g.GetProperty("hudOrientation").GetString());
        Assert.Equal("2133x1098", g.GetProperty("screen").GetString());
        Assert.Equal("SubpixelMorphologicalAntiAliasing", g.GetProperty("antialiasing").GetString());
        Assert.Equal("1200x618 of 1600x824", g.GetProperty("captureCover").GetString());
    }

    [Fact]
    public void AnUncutCapture_LeavesNoCoverKey()
    {
        var reportJson = new Dictionary<string, object>();
        new GraphicsReportInfo { Api = "Direct3D11" }.WriteTo(reportJson);

        var g = (Dictionary<string, object>)reportJson["graphics"];
        Assert.False(g.ContainsKey("captureCover"));
        Assert.Equal("off", g["hudOrientation"]);
    }
}
