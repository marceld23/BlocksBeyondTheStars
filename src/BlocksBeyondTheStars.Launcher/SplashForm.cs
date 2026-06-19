using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BlocksBeyondTheStars.Launcher;

/// <summary>
/// A borderless loading splash shown the instant the launcher starts, covering the black pre-engine gap
/// before the Unity player creates its window. It polls the game process and hands off — closing itself —
/// as soon as the player's main window appears (Unity's own splash then takes over). A safety timeout makes
/// sure the splash never lingers if the window never reports. The look is procedural (dark space gradient +
/// stars + title + indeterminate bar); drop a <c>splash.png</c> next to the launcher to override the
/// background. All layout is derived from the live client size, so it stays crisp and readable at any DPI.
/// </summary>
internal sealed class SplashForm : Form
{
    private const int PollIntervalMs = 33;       // ~30 fps for the animated bar + the window poll
    private const int GraceMs = 350;             // let the player paint its first frame before we vanish
    private const int SafetyTimeoutMs = 45000;   // never hang on the splash if the window never appears

    private readonly Process _game;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly string _loadingText;

    private int _elapsedMs;
    private int _sinceWindowMs = -1;             // -1 until the player window first appears
    private float _barPhase;
    private bool _fading;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public SplashForm(Process game)
    {
        _game = game;
        _loadingText = SplashLocalization.LoadingText(AppContext.BaseDirectory);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;     // positioned in OnLoad once the DPI-scaled size is known
        AutoScaleMode = AutoScaleMode.None;           // we scale manually from DeviceDpi (predictable + crisp)
        ShowInTaskbar = true;
        TopMost = true;
        DoubleBuffered = true;
        Text = "Blocks Beyond the Stars";
        Size = new Size(560, 320);
        BackColor = Color.FromArgb(6, 9, 18);

        // Share the game's icon (embedded via <ApplicationIcon>) for the taskbar / Alt-Tab entry.
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // No embedded icon (e.g. a debug run) — leave the default.
        }

        // Optional custom art: a splash.png next to the launcher overrides the procedural background.
        try
        {
            string png = Path.Combine(AppContext.BaseDirectory, "splash.png");
            if (File.Exists(png))
            {
                BackgroundImage = Image.FromFile(png);
            }
        }
        catch
        {
            // Fall back to the procedural background.
        }

        _timer.Interval = PollIntervalMs;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Scale the logical 560x320 to the monitor's DPI ourselves, then centre on that monitor's work area.
        float scale = DeviceDpi / 96f;
        Size = new Size((int)(560 * scale), (int)(320 * scale));
        var area = Screen.FromHandle(Handle).WorkingArea;
        Location = new Point(area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _elapsedMs += PollIntervalMs;
        _barPhase += 0.018f;
        if (_barPhase > 1f)
        {
            _barPhase -= 1f;
        }

        if (!_fading)
        {
            try
            {
                _game.Refresh();
                if (_game.HasExited)
                {
                    BeginClose(activateGame: false);
                }
                else if (_game.MainWindowHandle != IntPtr.Zero && _sinceWindowMs < 0)
                {
                    _sinceWindowMs = 0; // window just appeared — start the short hand-off grace
                }
            }
            catch
            {
                // Process queries can briefly throw while the child is starting/tearing down — ignore.
            }

            if (_sinceWindowMs >= 0)
            {
                _sinceWindowMs += PollIntervalMs;
            }

            if (_sinceWindowMs >= GraceMs || _elapsedMs >= SafetyTimeoutMs)
            {
                BeginClose(activateGame: true);
            }
        }

        if (_fading)
        {
            Opacity -= 0.12;
            if (Opacity <= 0.02)
            {
                _timer.Stop();
                Close();
                return;
            }
        }

        Invalidate();
    }

    /// <summary>Starts the fade-out and, if the player is up, brings its window to the front so it has focus
    /// once the (top-most) splash disappears.</summary>
    private void BeginClose(bool activateGame)
    {
        if (_fading)
        {
            return;
        }

        _fading = true;
        if (activateGame)
        {
            try
            {
                if (!_game.HasExited && _game.MainWindowHandle != IntPtr.Zero)
                {
                    SetForegroundWindow(_game.MainWindowHandle);
                }
            }
            catch
            {
                // Best-effort focus hand-off; Unity activates its own window anyway.
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintSplash(e.Graphics, ClientSize.Width, ClientSize.Height, _barPhase, _loadingText, BackgroundImage);
    }

    /// <summary>Draws the whole splash into <paramref name="g"/> for a <paramref name="w"/>×<paramref name="h"/>
    /// area. Pure of any window state so it can be unit-rendered to a bitmap for visual checks. The title font
    /// auto-fits the width (never wraps/clips) and all sizes scale with the area, so it reads at any DPI.
    /// Pass <paramref name="showBar"/> = false to omit the indeterminate bar — used when rendering the static
    /// image for the Velopack installer, which draws its own (live) progress bar over the same art.</summary>
    internal static void PaintSplash(Graphics g, int w, int h, float barPhase, string loadingText, Image? background, bool showBar = true)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, w, h);
        if (background != null)
        {
            g.DrawImage(background, rect);
        }
        else
        {
            using var grad = new LinearGradientBrush(rect, Color.FromArgb(16, 22, 44), Color.FromArgb(4, 6, 14), 90f);
            g.FillRectangle(grad, rect);

            // A scatter of stars (fixed seed → identical every frame, so it doesn't shimmer).
            var rng = new Random(20260616);
            for (int i = 0; i < 70; i++)
            {
                float sx = (float)rng.NextDouble() * w;
                float sy = (float)rng.NextDouble() * (h * 0.74f);
                int alpha = 35 + rng.Next(95);
                float r = rng.NextDouble() < 0.15 ? 2.3f : 1.3f;
                using var dot = new SolidBrush(Color.FromArgb(alpha, 205, 225, 255));
                g.FillEllipse(dot, sx, sy, r, r);
            }
        }

        var cyan = Color.FromArgb(125, 222, 236);
        var titleColor = Color.FromArgb(233, 244, 255);
        const string titleText = "Blocks Beyond the Stars";

        // Auto-fit the title to the width so it never wraps or clips, whatever the size/DPI.
        float maxTitleW = w * 0.86f;
        var titleFont = new Font("Segoe UI Semibold", Math.Max(11f, h * 0.130f), FontStyle.Bold, GraphicsUnit.Pixel);
        while (titleFont.Size > 12f && g.MeasureString(titleText, titleFont).Width > maxTitleW)
        {
            float next = titleFont.Size - 1f;
            titleFont.Dispose();
            titleFont = new Font("Segoe UI Semibold", next, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        var centre = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        using (titleFont)
        using (var subFont = new Font("Segoe UI", Math.Max(9f, h * 0.052f), FontStyle.Regular, GraphicsUnit.Pixel))
        using (var studioFont = new Font("Segoe UI Semibold", Math.Max(9f, h * 0.050f), FontStyle.Bold, GraphicsUnit.Pixel))
        using (var copyFont = new Font("Segoe UI", Math.Max(8f, h * 0.038f), FontStyle.Regular, GraphicsUnit.Pixel))
        using (var titleBrush = new SolidBrush(titleColor))
        using (var subBrush = new SolidBrush(cyan))
        using (var studioBrush = new SolidBrush(Color.FromArgb(212, 226, 242)))
        using (var copyBrush = new SolidBrush(Color.FromArgb(150, 172, 198)))
        {
            g.DrawString(titleText, titleFont, titleBrush, new RectangleF(0, h * 0.24f, w, h * 0.18f), centre);
            g.DrawString(loadingText, subFont, subBrush, new RectangleF(0, h * 0.45f, w, h * 0.10f), centre);
            // Studio + copyright footer (below the progress bar).
            g.DrawString("JuMaVe Games", studioFont, studioBrush, new RectangleF(0, h * 0.70f, w, h * 0.10f), centre);
            g.DrawString("(c) by Justus Dütscher und Marcel Dütscher", copyFont, copyBrush, new RectangleF(0, h * 0.82f, w, h * 0.10f), centre);
        }

        if (!showBar)
        {
            return;
        }

        // Indeterminate bar: a soft highlight sweeping along a dim track (clipped to the track).
        float bx = w * 0.16f, bw = w * 0.68f, by = h * 0.62f, bh = Math.Max(4f, h * 0.018f);
        using (var track = new SolidBrush(Color.FromArgb(70, 120, 160, 200)))
        {
            g.FillRectangle(track, bx, by, bw, bh);
        }

        float segW = bw * 0.28f;
        float segX = bx - segW + barPhase * (bw + segW);
        var prevClip = g.Clip;
        g.SetClip(new RectangleF(bx, by, bw, bh));
        using (var fill = new SolidBrush(cyan))
        {
            g.FillRectangle(fill, segX, by, segW, bh);
        }

        g.Clip = prevClip;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            BackgroundImage?.Dispose();
        }

        base.Dispose(disposing);
    }
}
