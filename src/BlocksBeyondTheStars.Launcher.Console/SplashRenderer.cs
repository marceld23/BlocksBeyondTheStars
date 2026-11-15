// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Reflection;
using SkiaSharp;

namespace BlocksBeyondTheStars.Launcher;

/// <summary>
/// The Linux launcher's splash picture (SkiaSharp): the Windows launcher's look (<c>SplashForm.PaintSplash</c>). With the
/// game's own poster compiled in (#3706, <c>client/Branding/launcher-splash.jpg</c> — the corvette before the "varied" world
/// with the block logo) it draws only "Loading…", the bar and the studio line on a dark band at the bottom; without it the
/// procedural space gradient, stars and title stand in. Drawn into a bitmap for the splash window
/// (<see cref="X11SplashWindow"/>) or encoded to a PNG (<c>--render-splash</c>, a visual check).
/// </summary>
internal static class SplashRenderer
{
    private const int Seed = 20260616;
    private const int StarCount = 70;
    private const string PosterResource = "splash-art"; // the csproj's LogicalName (a JPEG)

    private static readonly SKColor Cyan = new(0x7D, 0xDE, 0xEC);

    /// <summary>The compiled-in poster (or a <c>splash.png</c> next to the launcher), or null.</summary>
    public static SKBitmap? LoadPoster()
    {
        try
        {
            string png = Path.Combine(AppContext.BaseDirectory, "splash.png");
            if (File.Exists(png))
            {
                return SKBitmap.Decode(png);
            }

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PosterResource);
            return stream != null ? SKBitmap.Decode(stream) : null;
        }
        catch
        {
            return null; // never keep the game from starting over a picture
        }
    }

    public static void RenderSplash(string outputPath, int width, int height, bool showBar)
    {
        using var poster = LoadPoster();
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            Draw(canvas, width, height, showBar, 0.5f, poster, SplashLocalization.LoadingText(AppContext.BaseDirectory));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);
    }

    /// <summary>Draws the whole splash for a <paramref name="width"/>×<paramref name="height"/> area.</summary>
    public static void Draw(SKCanvas canvas, int width, int height, bool showBar, float barPhase, SKBitmap? poster, string loadingText)
    {
        if (poster != null)
        {
            DrawPoster(canvas, width, height, poster, loadingText);
            if (showBar)
            {
                DrawBar(canvas, width, height, barPhase, height * 0.86f);
            }

            return;
        }

        DrawProcedural(canvas, width, height, loadingText);
        if (showBar)
        {
            DrawBar(canvas, width, height, barPhase, height * 0.62f);
        }
    }

    private static void DrawPoster(SKCanvas canvas, int width, int height, SKBitmap poster, string loadingText)
    {
        using (var image = SKImage.FromBitmap(poster))
        using (var paint = new SKPaint { IsAntialias = true })
        {
            canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        }

        float top = height * 0.56f;
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, top), new SKPoint(0, height),
                   [new SKColor(5, 8, 18, 0), new SKColor(5, 8, 18, 225)], [0f, 1f], SKShaderTileMode.Clamp))
        using (var scrim = new SKPaint { Shader = shader })
        {
            canvas.DrawRect(new SKRect(0, top, width, height), scrim);
        }

        using var bold = SKTypeface.FromFamilyName("sans-serif", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var body = SKTypeface.FromFamilyName("sans-serif", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        Text(canvas, loadingText, width / 2f, height * 0.81f, bold, Math.Max(9f, height * 0.056f), Cyan);
        Text(canvas, "JuMaVe Games  ·  (c) by Justus Dütscher und Marcel Dütscher", width / 2f, height * 0.955f, body,
            Math.Max(8f, height * 0.036f), new SKColor(0xAA, 0xBC, 0xD2));
    }

    private static void DrawProcedural(SKCanvas canvas, int width, int height, string loadingText)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, height),
            [new SKColor(0x10, 0x16, 0x2C), new SKColor(0x04, 0x06, 0x0E)],
            [0f, 1f],
            SKShaderTileMode.Clamp);
        using var bgPaint = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawRect(new SKRect(0, 0, width, height), bgPaint);

        var rng = new Random(Seed);
        for (int i = 0; i < StarCount; i++)
        {
            float sx = (float)rng.NextDouble() * width;
            float sy = (float)rng.NextDouble() * (height * 0.74f);
            int alpha = 35 + rng.Next(95);
            float r = rng.NextDouble() < 0.15 ? 2.3f : 1.3f;
            using var starPaint = new SKPaint { Color = new SKColor(205, 225, 255, (byte)alpha), IsAntialias = true };
            canvas.DrawCircle(sx, sy, r, starPaint);
        }

        using var titleFace = SKTypeface.FromFamilyName("sans-serif", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var bodyFace = SKTypeface.FromFamilyName("sans-serif", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        const string titleText = "Blocks Beyond the Stars";
        using var titleFont = new SKFont(titleFace, Math.Max(11f, height * 0.130f));
        while (titleFont.Size > 12f && titleFont.MeasureText(titleText) > width * 0.86f)
        {
            titleFont.Size -= 1f;
        }

        using (var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText(titleText, width / 2f, height * 0.32f, SKTextAlign.Center, titleFont, titlePaint);
        }

        Text(canvas, loadingText, width / 2f, height * 0.47f, bodyFace, Math.Max(9f, height * 0.052f), Cyan);
        Text(canvas, "JuMaVe Games", width / 2f, height * 0.72f, bodyFace, Math.Max(9f, height * 0.050f), new SKColor(0xD4, 0xE2, 0xF2));
        Text(canvas, "(c) by Justus Dütscher und Marcel Dütscher", width / 2f, height * 0.84f, bodyFace, Math.Max(8f, height * 0.038f),
            new SKColor(0x96, 0xAC, 0xC6));
    }

    /// <summary>The indeterminate bar: a highlight sweeping along a dim track (clipped to it), top edge at <paramref name="by"/>.</summary>
    private static void DrawBar(SKCanvas canvas, int width, int height, float barPhase, float by)
    {
        float bx = width * 0.16f, bw = width * 0.68f, bh = Math.Max(4f, height * 0.018f);
        using (var trackPaint = new SKPaint { Color = new SKColor(70, 120, 160, 200), IsAntialias = true })
        {
            canvas.DrawRect(bx, by, bw, bh, trackPaint);
        }

        float segW = bw * 0.28f;
        float segX = bx - segW + barPhase * (bw + segW);
        canvas.Save();
        canvas.ClipRect(new SKRect(bx, by, bx + bw, by + bh));
        using (var fillPaint = new SKPaint { Color = Cyan })
        {
            canvas.DrawRect(segX, by, segW, bh, fillPaint);
        }

        canvas.Restore();
    }

    private static void Text(SKCanvas canvas, string text, float x, float y, SKTypeface face, float size, SKColor color)
    {
        using var font = new SKFont(face, size);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, x, y, SKTextAlign.Center, font, paint);
    }
}
