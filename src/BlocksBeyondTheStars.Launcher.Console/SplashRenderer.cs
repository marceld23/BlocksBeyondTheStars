using SkiaSharp;

namespace BlocksBeyondTheStars.Launcher;

internal static class SplashRenderer
{
    private const int Seed = 20260616;
    private const int StarCount = 70;

    public static void RenderSplash(string outputPath, int width, int height, bool showBar)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        var rect = new SKRect(0, 0, width, height);

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, height),
            [new SKColor(0x10, 0x16, 0x2C), new SKColor(0x04, 0x06, 0x0E)],
            [0f, 1f],
            SKShaderTileMode.Clamp);
        using var bgPaint = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawRect(rect, bgPaint);

        var rng = new Random(Seed);
        for (int i = 0; i < StarCount; i++)
        {
            float sx = (float)rng.NextDouble() * width;
            float sy = (float)rng.NextDouble() * (height * 0.74f);
            int alpha = 35 + rng.Next(95);
            float r = rng.NextDouble() < 0.15 ? 2.3f : 1.3f;
            using var starPaint = new SKPaint
            {
                Color = new SKColor(205, 225, 255, (byte)alpha),
                IsAntialias = true,
            };
            canvas.DrawCircle(sx, sy, r, starPaint);
        }

        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
        };

        using var titleFont = SKTypeface.FromFamilyName("sans-serif", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var bodyFont = SKTypeface.FromFamilyName("sans-serif", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        float titleSize = Math.Max(11f, height * 0.130f);
        string titleText = "Blocks Beyond the Stars";
        float maxTitleW = width * 0.86f;

        using var skFont = new SKFont(titleFont, titleSize);
        while (skFont.Size > 12f && skFont.MeasureText(titleText) > maxTitleW)
        {
            skFont.Size -= 1f;
        }

        float titleY = height * 0.32f;
        canvas.DrawText(titleText, width / 2f, titleY, SKTextAlign.Center, skFont, textPaint);

        float subSize = Math.Max(9f, height * 0.052f);
        using var subPaint = new SKPaint
        {
            Color = new SKColor(0x7D, 0xDE, 0xEC),
            IsAntialias = true,
        };
        using var subFont = new SKFont(bodyFont, subSize);
        canvas.DrawText("Loading ...", width / 2f, height * 0.47f, SKTextAlign.Center, subFont, subPaint);

        float studioSize = Math.Max(9f, height * 0.050f);
        using var studioPaint = new SKPaint
        {
            Color = new SKColor(0xD4, 0xE2, 0xF2),
            IsAntialias = true,
        };
        using var studioFont = new SKFont(bodyFont, studioSize);
        canvas.DrawText("JuMaVe Games", width / 2f, height * 0.72f, SKTextAlign.Center, studioFont, studioPaint);

        float copySize = Math.Max(8f, height * 0.038f);
        using var copyPaint = new SKPaint
        {
            Color = new SKColor(0x96, 0xAC, 0xC6),
            IsAntialias = true,
        };
        using var copyFont = new SKFont(bodyFont, copySize);
        canvas.DrawText("(c) by Justus Dütscher und Marcel Dütscher", width / 2f, height * 0.84f, SKTextAlign.Center, copyFont, copyPaint);

        if (showBar)
        {
            float bx = width * 0.16f, bw = width * 0.68f, by = height * 0.62f, bh = Math.Max(4f, height * 0.018f);

            using var trackPaint = new SKPaint
            {
                Color = new SKColor(70, 120, 160, 200),
                IsAntialias = true,
            };
            canvas.DrawRect(bx, by, bw, bh, trackPaint);

            float segW = bw * 0.28f;
            float segX = bx - segW + 0.5f * (bw + segW);

            canvas.Save();
            canvas.ClipRect(new SKRect(bx, by, bx + bw, by + bh));
            using var fillPaint = new SKPaint
            {
                Color = new SKColor(0x7D, 0xDE, 0xEC),
            };
            canvas.DrawRect(segX, by, segW, bh, fillPaint);
            canvas.Restore();
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);
    }
}
