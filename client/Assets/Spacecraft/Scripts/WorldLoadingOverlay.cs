using UnityEngine;
using UnityEngine.UI;

namespace Spacecraft.Client
{
    /// <summary>
    /// Full-screen loading curtain shown while a new world streams in (joining, landing on a planet,
    /// boarding a station — anything that fires <see cref="GameBootstrap.WorldLoadStarted"/>). It hides
    /// the pop-in (chunks/ship/station assembling) behind a dark veil with the destination name, the
    /// world type and a small spinner, then fades away once <see cref="GameBootstrap.WorldReady"/> goes
    /// true (the player has settled onto solid ground). Hyperspace jumps are masked by
    /// <see cref="HyperspaceWarp"/> instead, so those don't raise this overlay.
    /// Pure uGUI on a DPI-scaled canvas above everything; no bundled art.
    /// </summary>
    public sealed class WorldLoadingOverlay : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int DotCount = 12;       // spinner dots around the ring
        private const float Ring = 30f;        // spinner radius (reference units)
        private const float MinShow = 0.7f;    // never just flash, even if the world is ready instantly
        private const float MaxShow = 25f;     // hard safety: drop the veil even if "ready" never arrives
        private const float FadeIn = 0.30f;
        private const float FadeOut = 0.55f;

        private Canvas _canvas;
        private Image _backdrop;
        private Text _title;
        private Text _subtitle;
        private Text _footer;
        private Image[] _dots;
        private string _footerBase = "Loading world";

        private bool _armed;    // a load began; waiting for the in-space descent to finish before veiling
        private bool _active;   // the veil is on screen
        private bool _fadingOut;
        private float _t;       // seconds since the veil became visible
        private float _alpha;   // 0 hidden → 1 fully opaque veil

        // WorldRig sets Game right after AddComponent, so subscribe in Start (not OnEnable, which would
        // run during AddComponent while Game is still null).
        private void Start()
        {
            if (Game != null)
            {
                Game.WorldLoadStarted += OnWorldLoadStarted;
            }
        }

        private void OnDestroy()
        {
            if (Game != null)
            {
                Game.WorldLoadStarted -= OnWorldLoadStarted;
            }

            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        private void OnWorldLoadStarted()
        {
            // Arm now, but don't raise the veil yet: a planet landing first plays the ship's descent in the
            // space view (the surface build-up is hidden behind the space scene anyway). We only veil once
            // we're back on the surface and the world still isn't ready — see Update.
            _armed = true;
        }

        private void Update()
        {
            if (_armed)
            {
                bool inSpace = Game != null && Game.SpaceViewActive;
                if (inSpace)
                {
                    return; // hold through the descent; the space scene already masks the build-up
                }

                _armed = false;

                // If the surface streamed in during the descent there's nothing to hide — skip the veil.
                if (Game != null && Game.WorldReady && _active == false)
                {
                    return;
                }

                EnsureBuilt();
                RefreshLabels();
                _t = 0f;
                _fadingOut = false;
                if (!_active)
                {
                    _alpha = 0f;
                    _active = true;
                    _canvas.enabled = true;
                    _backdrop.raycastTarget = true; // swallow clicks while the world loads
                }
            }

            if (!_active)
            {
                return;
            }

            _t += Time.deltaTime;

            // Begin the fade-out once the world is ready (and we've shown it long enough), or after the
            // safety timeout so a stuck load can never trap the player behind the veil.
            bool ready = Game != null && Game.WorldReady;
            if (!_fadingOut && ((ready && _t >= MinShow) || _t >= MaxShow))
            {
                _fadingOut = true;
            }

            float target = _fadingOut ? 0f : 1f;
            float rate = _fadingOut ? 1f / FadeOut : 1f / FadeIn;
            _alpha = Mathf.MoveTowards(_alpha, target, rate * Time.deltaTime);

            Apply();

            if (_fadingOut && _alpha <= 0.001f)
            {
                _active = false;
                _backdrop.raycastTarget = false;
                _canvas.enabled = false;
            }
        }

        private void Apply()
        {
            // Near-black veil tinted toward the space background, fully opaque at peak so nothing shows through.
            _backdrop.color = new Color(0.02f, 0.03f, 0.06f, _alpha);

            _title.color = WithAlpha(new Color(0.86f, 0.95f, 1.00f), _alpha);
            _subtitle.color = WithAlpha(UiKit.CyanDim, _alpha * 0.95f);

            // Footer "Loading world" with animated trailing dots for a sense of progress.
            int dots = Mathf.Clamp(Mathf.FloorToInt(_t * 2.5f) % 4, 0, 3);
            _footer.text = _footerBase + new string('.', dots);
            _footer.color = WithAlpha(new Color(0.55f, 0.82f, 1.00f), _alpha);

            // A comet-tail highlight chasing around the ring of dots.
            float head = Mathf.Repeat(_t * 0.85f, 1f);
            for (int i = 0; i < _dots.Length; i++)
            {
                float d = Mathf.Repeat(head - i / (float)_dots.Length, 1f);
                float b = Mathf.Pow(1f - d, 3f); // bright at the head, fading along the tail
                _dots[i].color = WithAlpha(new Color(0.45f, 0.82f, 1.00f), _alpha * (0.12f + 0.88f * b));
            }
        }

        private void RefreshLabels()
        {
            if (Game == null)
            {
                return;
            }

            _title.text = string.IsNullOrEmpty(Game.LocationName) ? string.Empty : Game.LocationName;

            var loc = Game.Localizer;
            string type = Game.LoadingPlanetType ?? string.Empty;
            string typeKey = $"planet.{type}.name";
            _subtitle.text = loc != null && loc.Has(typeKey) ? loc.Get(typeKey) : string.Empty;

            // Boarding a station reads better as "entering" than "loading world".
            bool station = type == "orbital_station";
            string footerKey = station ? "ui.loading.station" : "ui.loading.world";
            _footerBase = loc != null ? loc.Get(footerKey) : (station ? "Entering station" : "Loading world");
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("World Loading");
            _canvas.sortingOrder = 75; // above HUD/menus/map; jumps use the warp (70) instead
            var root = _canvas.transform;

            _backdrop = FullScreen(root, "Veil", new Color(0.02f, 0.03f, 0.06f, 0f));

            // Centred column in the 1920×1080 reference space (top-left coords, y down).
            _title = UiKit.AddText(root, 360f, 420f, 1200f, 70f, string.Empty, 46,
                new Color(0.86f, 0.95f, 1f, 0f), TextAnchor.MiddleCenter, FontStyle.Bold);
            _subtitle = UiKit.AddText(root, 360f, 496f, 1200f, 40f, string.Empty, 24,
                new Color(0f, 0f, 0f, 0f), TextAnchor.MiddleCenter);
            _footer = UiKit.AddText(root, 360f, 666f, 1200f, 40f, string.Empty, 22,
                new Color(0f, 0f, 0f, 0f), TextAnchor.MiddleCenter, FontStyle.Bold);

            // Spinner: a ring of dots centred at (960, 600), highlight chasing around it.
            var ringGo = new GameObject("Spinner", typeof(RectTransform));
            ringGo.transform.SetParent(root, false);
            var ringRt = ringGo.GetComponent<RectTransform>();
            ringRt.anchorMin = ringRt.anchorMax = ringRt.pivot = new Vector2(0.5f, 0.5f);
            ringRt.anchoredPosition = new Vector2(0f, -28f); // below the title/subtitle, above the footer
            ringRt.sizeDelta = Vector2.zero;

            _dots = new Image[DotCount];
            for (int i = 0; i < DotCount; i++)
            {
                float ang = i / (float)DotCount * Mathf.PI * 2f;
                var go = new GameObject("Dot", typeof(RectTransform));
                go.transform.SetParent(ringRt, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(Mathf.Sin(ang) * Ring, Mathf.Cos(ang) * Ring);
                rt.sizeDelta = new Vector2(7f, 7f);
                var img = go.AddComponent<Image>();
                img.sprite = UiKit.SolidSprite;
                img.raycastTarget = false;
                _dots[i] = img;
            }

            _canvas.enabled = false;
        }

        private static Image FullScreen(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }
    }
}
