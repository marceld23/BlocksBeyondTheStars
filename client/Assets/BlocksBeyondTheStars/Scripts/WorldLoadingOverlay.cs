// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
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
    // Run after the gameplay views (default order 0), so when SpaceView ends its landing descent and clears
    // SpaceViewActive in its own Update, this overlay sees it the SAME frame and raises the (opaque) veil
    // before that frame renders — without this the surface could flash for one frame on a landing.
    [DefaultExecutionOrder(100)]
    public sealed class WorldLoadingOverlay : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int DotCount = 12;       // spinner dots around the ring
        private const float Ring = 30f;        // spinner radius (reference units)
        private const float MinShow = 3.0f;    // always hold the screen long enough to read it (planet + station)
        private const float MaxShow = 25f;     // hard safety: drop the veil even if "ready" never arrives
        private const float ConfirmTimeout = 2.5f; // pre-raised veil: drop it if no world load confirms by here
        private const float FadeOut = 0.55f;        // only the reveal fades; the veil snaps to opaque on raise

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
        private bool _awaitingConfirm; // veil pre-raised at intent time; waiting for the load to confirm (B34)
        private float _confirmTimer;   // seconds since the pre-raise, to time out an unconfirmed transition
        private bool _initial;  // the initial world-entry veil: pre-raised opaque before the first frame, bypasses the in-space hold
        private bool _joinSeen; // a WorldLoadStarted has arrived since priming — gates the initial veil's fade-out

        // WorldRig sets Game right after AddComponent, so subscribe in Start (not OnEnable, which would
        // run during AddComponent while Game is still null).
        private void Start()
        {
            if (Game != null)
            {
                Game.WorldLoadStarted += OnWorldLoadStarted;
                Game.WorldTransitionStarted += OnTransitionStarted;
            }
        }

        private void OnDestroy()
        {
            if (Game != null)
            {
                Game.WorldLoadStarted -= OnWorldLoadStarted;
                Game.WorldTransitionStarted -= OnTransitionStarted;
            }

            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        private void OnWorldLoadStarted()
        {
            // The join confirmed: an initial-entry veil (pre-raised before the first frame) may now honour
            // WorldReady for its fade-out. Until this arrives WorldReady defaults true, so the gate below
            // keeps the curtain up through the brief connect/join window.
            _joinSeen = true;

            // A descent-less transition pre-raised the veil already — just confirm it (so the timeout doesn't
            // drop it) and refresh the title now that the destination is known; don't reset the on-screen timer.
            if (_active)
            {
                _awaitingConfirm = false;
                RefreshLabels();
                return;
            }

            // Arm now, but don't raise the veil yet: a planet landing first plays the ship's descent in the
            // space view (the surface build-up is hidden behind the space scene anyway). We only veil once
            // we're back on the surface and the world still isn't ready — see Update.
            _armed = true;
        }

        /// <summary>The client sent a descent-less world-changing intent (board a station, enter the ship) —
        /// veil the screen immediately so the old view never flashes (B34). If no world load confirms within
        /// <see cref="ConfirmTimeout"/> (e.g. the action was rejected), the veil drops itself again.</summary>
        private void OnTransitionStarted()
        {
            _armed = false;
            _awaitingConfirm = true;
            _confirmTimer = 0f;
            Raise();
        }

        /// <summary>Brings the veil on screen opaque immediately (or restarts its on-screen timer if already up).
        /// The raise is a hard cut — never a fade-in — so the view it's hiding (the just-finished space descent,
        /// or the on-foot view on a board/enter) can't bleed through while it ramps up. Only the reveal fades.</summary>
        private void Raise()
        {
            EnsureBuilt();
            RefreshLabels();
            _t = 0f;
            _fadingOut = false;
            _initial = false; // an in-game landing/boarding veil, not the initial-entry curtain
            if (!_active)
            {
                _alpha = 1f; // snap to opaque this frame so the surface/old view never flashes behind a fade-in
                _active = true;
                _canvas.enabled = true;
                _backdrop.raycastTarget = true; // swallow clicks while the world loads
            }
        }

        /// <summary>
        /// Raises the veil fully opaque BEFORE the first in-game frame renders, so the freshly-built world
        /// rig never flashes its raw scene (the space view / bare surface) during entry. Called synchronously
        /// by <see cref="WorldRig"/> right after the rig is built — the cut from the already-opaque shell
        /// loading screen is seamless. Marks the veil as the initial-load curtain, which bypasses the "hold
        /// through the in-space descent" rule (so the star system never flashes) and keeps it up until the
        /// join confirms and the world is ready — the normal fade-out path then takes over.
        /// </summary>
        public void PrimeForInitialLoad()
        {
            EnsureBuilt();
            RefreshLabels();
            _initial = true;
            _armed = false;
            _awaitingConfirm = false;
            _joinSeen = false;
            _t = 0f;
            _fadingOut = false;
            _alpha = 1f; // opaque immediately — no fade-in, so the very first frame is covered
            _active = true;
            _canvas.enabled = true;
            _backdrop.raycastTarget = true; // swallow clicks while the world loads
            Apply(); // paint it opaque this frame, before the in-game cameras render
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

                // Always raise the veil (even if the surface already streamed in during the descent) so the
                // landing/boarding screen is reliably shown + readable for ~MinShow seconds, not skipped on a
                // fast/cached load. The minimum-on-screen time below holds it; WorldReady only gates the fade-out.
                Raise();
            }

            if (!_active)
            {
                return;
            }

            // A pre-raised veil whose transition never confirmed (a rejected board/enter) drops itself, so the
            // screen can't get stuck behind the veil for a non-event.
            if (_awaitingConfirm)
            {
                _confirmTimer += Time.deltaTime;
                if (_confirmTimer > ConfirmTimeout)
                {
                    _awaitingConfirm = false;
                    _fadingOut = true;
                }
            }

            _t += Time.deltaTime;

            // Begin the fade-out once the world is ready (and we've shown it long enough), or after the
            // safety timeout so a stuck load can never trap the player behind the veil. The initial-entry
            // veil additionally waits for the join to confirm — WorldReady defaults true, so without this
            // gate the curtain could drop in the brief window before JoinAccepted arrives.
            bool ready = Game != null && Game.WorldReady && (!_initial || _joinSeen);
            if (!_fadingOut && ((ready && _t >= MinShow) || _t >= MaxShow))
            {
                _fadingOut = true;
            }

            // The veil is opaque the instant it's raised (Raise/PrimeForInitialLoad set alpha = 1); only the
            // reveal at the end fades, so nothing behind the curtain ever flashes while it ramps up.
            _alpha = _fadingOut ? Mathf.MoveTowards(_alpha, 0f, Time.deltaTime / FadeOut) : 1f;

            Apply();

            if (_fadingOut && _alpha <= 0.001f)
            {
                _active = false;
                _awaitingConfirm = false;
                _initial = false; // the entry curtain is done; later landings/boardings use the normal path
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
