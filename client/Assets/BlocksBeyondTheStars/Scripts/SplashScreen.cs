using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Splash screen (`anf_textures.md` §3): the animated menu space scene shows through behind an
    /// extruded ("3D"-looking) BLOCKS BEYOND THE STARS title that fades/scales/slides in, with a tagline, build
    /// badge and skip hint. Shown every start, skippable, and bridges content-load time. Modern uGUI
    /// build on its own DPI-scaled overlay canvas (no bundled art needed); driven from AppShell.Update.
    /// </summary>
    public sealed class SplashScreen
    {
        private const float Duration = 3.2f;

        private readonly AppShell _shell;
        private float _elapsed;

        private Canvas _canvas;
        private RectTransform _title;
        private CanvasGroup _titleGroup;
        private CanvasGroup _lateGroup;

        public SplashScreen(AppShell shell) => _shell = shell;

        public void Update()
        {
            if (_shell.Phase != ShellPhase.Splash)
            {
                if (_canvas != null)
                {
                    Object.Destroy(_canvas.gameObject);
                    _canvas = null;
                }

                return;
            }

            EnsureBuilt();
            _elapsed += Time.deltaTime;
            Animate(_elapsed);

            if (_elapsed >= Duration || (_elapsed > 0.4f && Input.anyKeyDown))
            {
                _shell.GoTo(ShellPhase.MainMenu);
            }
        }

        private void Animate(float t)
        {
            float intro = Mathf.Clamp01(t / 0.9f);
            float ease = 1f - (1f - intro) * (1f - intro); // ease-out
            float scale = 0.85f + 0.15f * ease + 0.02f * Mathf.Sin(t * 2f); // settle + gentle breathe

            _titleGroup.alpha = ease;
            _title.localScale = new Vector3(scale, scale, 1f);
            _title.anchoredPosition = new Vector2(0f, 56f + 30f * ease); // slide up into place

            _lateGroup.alpha = Mathf.Clamp01((t - 0.8f) / 0.8f);
        }

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }

            _elapsed = 0f;
            _canvas = UiKit.CreateCanvas("Splash");
            _canvas.sortingOrder = 40;
            var root = _canvas.transform;

            // Faint darkening for title readability (the menu space scene shows through behind).
            var ov = new GameObject("Overlay", typeof(RectTransform));
            ov.transform.SetParent(root, false);
            var ort = ov.GetComponent<RectTransform>();
            ort.anchorMin = Vector2.zero;
            ort.anchorMax = Vector2.one;
            ort.offsetMin = ort.offsetMax = Vector2.zero;
            var ovImg = ov.AddComponent<Image>();
            ovImg.sprite = UiKit.SolidSprite;
            ovImg.color = new Color(0.02f, 0.04f, 0.09f, 0.35f);
            ovImg.raycastTarget = false;

            // Extruded animated title (scaled/faded/slid as a group).
            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(root, false);
            _title = titleGo.GetComponent<RectTransform>();
            _title.anchorMin = _title.anchorMax = _title.pivot = new Vector2(0.5f, 0.5f);
            _title.sizeDelta = new Vector2(1100f, 160f);
            _titleGroup = titleGo.AddComponent<CanvasGroup>();

            const string title = "BLOCKS BEYOND THE STARS";
            var glow = new Color(0.22f, 0.75f, 0.95f, 0.3f);
            Centered(_title, new Vector2(-5f, 0f), 72, glow, title, true);
            Centered(_title, new Vector2(5f, 0f), 72, glow, title, true);
            for (int d = 8; d >= 1; d--)
            {
                float k = d / 8f;
                var c = new Color(0.05f, 0.12f * (1 - k) + 0.04f, 0.22f * (1 - k) + 0.05f, 1f);
                Centered(_title, new Vector2(d * 1.4f, -d * 1.4f), 72, c, title, true);
            }

            Centered(_title, Vector2.zero, 72, new Color(0.85f, 0.95f, 1f), title, true);

            // Tagline + build badge + skip hint (fade in slightly later).
            var lateGo = new GameObject("Late", typeof(RectTransform));
            lateGo.transform.SetParent(root, false);
            var lrt = lateGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            _lateGroup = lateGo.AddComponent<CanvasGroup>();

            Centered(lateGo.transform, new Vector2(0f, -65f), 18, new Color(0.75f, 0.85f, 0.95f), _shell.L("ui.splash.tagline"), false);
            Centered(lateGo.transform, new Vector2(0f, -108f), 18, new Color(1f, 0.72f, 0.2f), $"{_shell.L("ui.splash.build")}   v{AppShell.Version}", false);
            Centered(lateGo.transform, new Vector2(0f, -150f), 17, new Color(0.5f, 0.85f, 1f), _shell.L("ui.splash.contribute"), true);
            Centered(lateGo.transform, new Vector2(0f, -432f), 18, new Color(0.7f, 0.8f, 0.9f, 0.6f), _shell.L("ui.splash.skip"), false);

            Animate(0f);
        }

        private static Text Centered(Transform parent, Vector2 pos, int fontSize, Color color, string text, bool bold)
        {
            var go = new GameObject("T", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1200f, fontSize + 24f);
            rt.anchoredPosition = pos;
            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.fontSize = fontSize;
            t.color = color;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }
    }
}
