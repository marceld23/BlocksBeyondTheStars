using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace BlocksBeyondTheStars.Launcher;

/// <summary>
/// Renders the two bitmaps the Velopack MSI wizard uses (WixUI_Bmp_Dialog 493×312 and WixUI_Bmp_Banner
/// 493×58), in the same dark-space look as the loading splash but laid out for the standard WiX dialogs:
/// the wizard draws its own (dark) text over these bitmaps, so the regions where text sits are kept light
/// while the game art lives where WiX leaves space (the left band of the dialog bitmap, the right edge of
/// the banner). This keeps the installer on-brand AND readable. Invoked at pack time by
/// scripts/publish-client-installer.ps1 via the launcher's --render-msi-* modes.
/// </summary>
internal static class MsiArt
{
    // Shared palette with the splash (see SplashForm).
    private static readonly Color SpaceTop = Color.FromArgb(16, 22, 44);
    private static readonly Color SpaceBottom = Color.FromArgb(4, 6, 14);
    private static readonly Color Cyan = Color.FromArgb(125, 222, 236);
    private static readonly Color TextLight = Color.FromArgb(233, 244, 255);
    private static readonly Color PanelWhite = Color.FromArgb(250, 251, 254);

    private const string Title = "Blocks Beyond the Stars";

    /// <summary>The WelcomeDlg/ExitDialog background (default 493×312): dark space art on the left band where
    /// WiX shows no text, a clean light panel on the right where the welcome/finish text is drawn.</summary>
    internal static void PaintDialog(Graphics g, int w, int h)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Light panel fills everything (the text area on the right stays readable).
        using (var panel = new SolidBrush(PanelWhite))
        {
            g.FillRectangle(panel, 0, 0, w, h);
        }

        // Left art band — WiX's Welcome/Exit text sits to the right of this, so it can be full dark art.
        int band = (int)(w * 0.34f); // ~164px at 493
        var bandRect = new Rectangle(0, 0, band, h);
        using (var grad = new LinearGradientBrush(bandRect, SpaceTop, SpaceBottom, 90f))
        {
            g.FillRectangle(grad, bandRect);
        }

        PaintStars(g, band, h, 46, 20260619);

        // Cyan seam between art and panel.
        using (var seam = new SolidBrush(Cyan))
        {
            g.FillRectangle(seam, band - 2, 0, 2, h);
        }

        // Title stacked in the art band.
        var centre = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        using var titleFont = FitFont(g, "Blocks Beyond", band * 0.84f, Math.Max(12f, h * 0.072f));
        using var subBrush = new SolidBrush(Cyan);
        using var titleBrush = new SolidBrush(TextLight);
        using var studioFont = new Font("Segoe UI Semibold", Math.Max(9f, h * 0.040f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var studioBrush = new SolidBrush(Color.FromArgb(196, 214, 234));
        g.DrawString("Blocks", titleFont, titleBrush, new RectangleF(0, h * 0.34f, band, h * 0.10f), centre);
        g.DrawString("Beyond", titleFont, titleBrush, new RectangleF(0, h * 0.44f, band, h * 0.10f), centre);
        g.DrawString("the Stars", titleFont, subBrush, new RectangleF(0, h * 0.54f, band, h * 0.10f), centre);
        g.DrawString("JuMaVe Games", studioFont, studioBrush, new RectangleF(0, h * 0.86f, band, h * 0.08f), centre);
    }

    /// <summary>The top banner shown on the interior wizard pages (default 493×58). WiX draws the dark page
    /// title on the LEFT, so we keep the background light and place a small space emblem on the right.</summary>
    internal static void PaintBanner(Graphics g, int w, int h)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var panel = new SolidBrush(PanelWhite))
        {
            g.FillRectangle(panel, 0, 0, w, h);
        }

        // Right emblem zone: a faint dark space patch with a small planet + stars, fading in from the right.
        int zone = (int)(w * 0.26f);
        var zoneRect = new Rectangle(w - zone, 0, zone, h);
        using (var grad = new LinearGradientBrush(zoneRect, Color.FromArgb(0, SpaceTop), Color.FromArgb(235, SpaceBottom), 0f))
        {
            g.FillRectangle(grad, zoneRect);
        }
        PaintStars(g, zone, h, 16, 70260619, offsetX: w - zone);

        // A small cyan-rimmed planet near the right edge.
        float pr = h * 0.34f;
        float pcx = w - zone * 0.34f;
        float pcy = h * 0.5f;
        using (var planet = new SolidBrush(Color.FromArgb(40, 70, 120)))
        {
            g.FillEllipse(planet, pcx - pr, pcy - pr, pr * 2, pr * 2);
        }
        using (var rim = new Pen(Cyan, 1.4f))
        {
            g.DrawEllipse(rim, pcx - pr, pcy - pr, pr * 2, pr * 2);
        }

        // Cyan baseline accent across the whole banner.
        using (var line = new SolidBrush(Cyan))
        {
            g.FillRectangle(line, 0, h - 2, w, 2);
        }
    }

    /// <summary>A fixed-seed star scatter (so renders are reproducible), confined to a w×h area.</summary>
    private static void PaintStars(Graphics g, int w, int h, int count, int seed, int offsetX = 0)
    {
        var rng = new Random(seed);
        for (int i = 0; i < count; i++)
        {
            float sx = offsetX + (float)rng.NextDouble() * w;
            float sy = (float)rng.NextDouble() * h;
            int alpha = 35 + rng.Next(110);
            float r = rng.NextDouble() < 0.15 ? 2.2f : 1.2f;
            using var dot = new SolidBrush(Color.FromArgb(alpha, 205, 225, 255));
            g.FillEllipse(dot, sx, sy, r, r);
        }
    }

    /// <summary>A bold Segoe UI font shrunk until <paramref name="sample"/> fits <paramref name="maxWidth"/>.</summary>
    private static Font FitFont(Graphics g, string sample, float maxWidth, float startPx)
    {
        var f = new Font("Segoe UI Semibold", startPx, FontStyle.Bold, GraphicsUnit.Pixel);
        while (f.Size > 11f && g.MeasureString(sample, f).Width > maxWidth)
        {
            float next = f.Size - 1f;
            f.Dispose();
            f = new Font("Segoe UI Semibold", next, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        return f;
    }
}
