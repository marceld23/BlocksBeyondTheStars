using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Splash screen (`anf_textures.md` §3): a dark space backdrop with a drifting starfield and
    /// an animated, extruded ("3D"-looking) SPACECRAFT title that fades/scales in. Shown every
    /// start, skippable, and bridges content-load time. Pure IMGUI — no bundled art needed.
    /// </summary>
    public sealed class SplashScreen
    {
        private const float Duration = 3.2f;
        private const int StarCount = 140;

        private readonly AppShell _shell;
        private float _elapsed;

        private readonly float[] _starX = new float[StarCount];
        private readonly float[] _starY = new float[StarCount];
        private readonly float[] _starSize = new float[StarCount];
        private readonly float[] _starPhase = new float[StarCount];

        private GUIStyle _titleStyle;
        private GUIStyle _subStyle;
        private Texture2D _pixel;

        public SplashScreen(AppShell shell)
        {
            _shell = shell;

            var rng = new System.Random(20260531);
            for (int i = 0; i < StarCount; i++)
            {
                _starX[i] = (float)rng.NextDouble();
                _starY[i] = (float)rng.NextDouble();
                _starSize[i] = 1f + (float)rng.NextDouble() * 2.2f;
                _starPhase[i] = (float)rng.NextDouble() * 6.28f;
            }
        }

        public void Update()
        {
            if (_shell.Phase != ShellPhase.Splash)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed >= Duration || (_elapsed > 0.4f && Input.anyKeyDown))
            {
                _shell.GoTo(ShellPhase.MainMenu);
            }
        }

        public void Draw()
        {
            EnsureStyles();
            UiScale.Begin(); // lay out in a virtual 1080p space so the splash scales with resolution

            float w = UiScale.Width, h = UiScale.Height;
            float t = _elapsed;

            // The animated MenuBackground space scene shows through behind the splash (same as the
            // menu), so we only draw a faint darkening for title readability — no separate starfield.
            Fill(new Rect(0, 0, w, h), new Color(0.02f, 0.04f, 0.09f, 0.35f));

            // --- Animated extruded title ---
            float intro = Mathf.Clamp01(t / 0.9f);
            float ease = 1f - (1f - intro) * (1f - intro); // ease-out
            float titleAlpha = ease;
            float scale = 0.85f + 0.15f * ease + 0.02f * Mathf.Sin(t * 2f); // settle + gentle breathe
            float cx = w / 2f;
            float cy = h * 0.42f - (1f - ease) * 30f; // slide up into place

            var prevMatrix = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), new Vector2(cx, cy));

            var titleRect = new Rect(cx - 500, cy - 60, 1000, 120);
            const string title = "SPACECRAFT";

            // Cyan glow halo behind.
            DrawText(titleRect, title, _titleStyle, new Color(0.22f, 0.75f, 0.95f, 0.25f * titleAlpha), new Vector2(0, 0), 10f);

            // Extruded depth (dark blue, receding).
            for (int d = 8; d >= 1; d--)
            {
                float k = d / 8f;
                var c = new Color(0.05f, 0.12f * (1 - k) + 0.04f, 0.22f * (1 - k) + 0.05f, titleAlpha);
                DrawText(titleRect, title, _titleStyle, c, new Vector2(d * 1.4f, d * 1.4f), 0f);
            }

            // Bright front face.
            DrawText(titleRect, title, _titleStyle, new Color(0.85f, 0.95f, 1f, titleAlpha), Vector2.zero, 0f);

            GUI.matrix = prevMatrix;

            // --- Tagline + build badge + skip hint (fade in slightly later) ---
            float lateAlpha = Mathf.Clamp01((t - 0.8f) / 0.8f);
            DrawCentered(new Rect(0, h * 0.56f, w, 26), _shell.L("ui.splash.tagline"), _subStyle,
                new Color(0.75f, 0.85f, 0.95f, lateAlpha));
            DrawCentered(new Rect(0, h * 0.60f, w, 22), $"{_shell.L("ui.splash.build")}   v{AppShell.Version}", _subStyle,
                new Color(1f, 0.72f, 0.2f, lateAlpha));
            DrawCentered(new Rect(0, h * 0.90f, w, 22), _shell.L("ui.splash.skip"), _subStyle,
                new Color(0.7f, 0.8f, 0.9f, 0.6f * lateAlpha));

            UiScale.End();
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _pixel = Texture2D.whiteTexture;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 72,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        private void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _pixel);
            GUI.color = prev;
        }

        private void DrawText(Rect rect, string text, GUIStyle style, Color color, Vector2 offset, float expand)
        {
            var prev = style.normal.textColor;
            style.normal.textColor = color;
            var r = new Rect(rect.x + offset.x - expand, rect.y + offset.y - expand, rect.width + expand * 2, rect.height + expand * 2);
            GUI.Label(r, text, style);
            style.normal.textColor = prev;
        }

        private void DrawCentered(Rect rect, string text, GUIStyle style, Color color)
        {
            var prev = style.normal.textColor;
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
            style.normal.textColor = prev;
        }
    }
}
