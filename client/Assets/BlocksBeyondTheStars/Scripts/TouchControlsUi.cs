// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// On-screen touch controls for the tablet / browser build (P3): a left virtual joystick (move), a
    /// full-screen right-side look pad (drag to look), and action buttons (jump, mine-hold, place, use,
    /// descend, hotbar ◄►, menu). It reports state to <see cref="TouchInputSource"/>, which feeds it into
    /// <see cref="InputMap"/>'s combined read alongside keyboard/mouse and gamepad.
    ///
    /// Built on uGUI pointer handlers (not raw <c>Input.touches</c>) so the canvas scaler and multitouch
    /// routing are handled by the EventSystem. It is **inert on desktop**: the UI is only built when the
    /// device actually has touch (<see cref="ShouldShow"/>), and while hidden the source reads zero — so the
    /// shipped keyboard/mouse + pad experience cannot regress. Scoped to **on-foot**: the controls hide
    /// during flight/EVA and while a menu is open (menus are tap-navigable via the EventSystem directly).
    /// Flight/speeder/EVA touch layouts, contextual labels and text-input are follow-up (issue #197).
    /// Geometry + feel need an on-device pass (CI can't test touch).
    /// </summary>
    [DefaultExecutionOrder(-100)] // resolve control state before gameplay reads it this frame
    public sealed class TouchControlsUi : MonoBehaviour
    {
        public static TouchControlsUi Active { get; private set; }

        public GameBootstrap Game;
        public GameMenu Menu;

        private GameObject _rootPanel;
        private TouchStick _stick;
        private TouchLookPad _lookPad;
        private TouchButton _jump, _mine, _place, _use, _descend, _prev, _next, _menu;
        private bool _built;

        /// <summary>True on a touch device (tablet / touch-capable browser). Desktop mouse rigs report false,
        /// so the whole feature stays dormant there. WebGL on a desktop browser also reports false → the
        /// browser build uses keyboard/mouse or a pad, exactly as intended.</summary>
        public static bool ShouldShow() => Application.isMobilePlatform || Input.touchSupported;

        /// <summary>Whether the controls are currently live (built AND visible). The source reads zero when false.</summary>
        public bool Visible => _built && _rootPanel != null && _rootPanel.activeSelf;

        public Vector2 Move => Visible && _stick != null ? _stick.Value : Vector2.zero;
        public Vector2 LookDelta => Visible && _lookPad != null ? _lookPad.Delta : Vector2.zero;
        public bool JumpHeld => Visible && _jump != null && _jump.Pressed;
        public bool JumpDown => Visible && _jump != null && _jump.DownThisFrame;
        public bool MineHeld => Visible && _mine != null && _mine.Pressed;
        public bool MineDown => Visible && _mine != null && _mine.DownThisFrame;
        public bool PlaceDown => Visible && _place != null && _place.DownThisFrame;
        public bool UseDown => Visible && _use != null && _use.DownThisFrame;
        public bool DescendHeld => Visible && _descend != null && _descend.Pressed;

        /// <summary>Hotbar step for this frame: &gt;0 = previous slot, &lt;0 = next (mirrors mouse-wheel sign).
        /// Idempotent within the frame (reads the buttons' frame-stable edge).</summary>
        public float HotbarStep()
        {
            if (!Visible)
            {
                return 0f;
            }

            if (_prev != null && _prev.DownThisFrame)
            {
                return 1f;
            }

            if (_next != null && _next.DownThisFrame)
            {
                return -1f;
            }

            return 0f;
        }

        private void Awake()
        {
            Active = this;
            if (ShouldShow())
            {
                Build();
            }
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        private void Update()
        {
            if (!_built)
            {
                return;
            }

            // On-foot only: hide while a menu is open (so taps reach the menu) or during flight/EVA.
            bool show = Game != null && !Game.MenuOpen && !Game.SpaceViewActive;
            if (_rootPanel.activeSelf != show)
            {
                _rootPanel.SetActive(show);
            }

            // The menu button toggles the gameplay menu (equivalent to Tab / the pad's Start).
            if (show && _menu != null && _menu.DownThisFrame && Menu != null)
            {
                Menu.SetMenuOpen(!Game.MenuOpen);
            }
        }

        private void LateUpdate()
        {
            // Clear the per-frame look accumulation after gameplay has read it this frame.
            _lookPad?.ResetDelta();
        }

        private void Build()
        {
            var canvas = UiKit.CreateCanvas("TouchControls");
            canvas.sortingOrder = 100; // above the HUD; menus hide these controls so no fight there
            _rootPanel = canvas.gameObject;

            // Full-screen look pad FIRST (bottom sibling) so buttons/stick placed after sit on top of it and
            // win the touch; empty-area drags fall through to the look pad.
            _lookPad = MakeLookPad(canvas.transform);

            // Left virtual joystick.
            _stick = MakeStick(canvas.transform);

            // Right-hand action cluster (anchored bottom-right).
            _jump = MakeButton(canvas.transform, new Vector2(1f, 0f), new Vector2(-140f, 150f), 120f, "JUMP");
            _mine = MakeButton(canvas.transform, new Vector2(1f, 0f), new Vector2(-280f, 260f), 120f, "MINE");
            _place = MakeButton(canvas.transform, new Vector2(1f, 0f), new Vector2(-140f, 300f), 120f, "PLACE");
            _use = MakeButton(canvas.transform, new Vector2(1f, 0f), new Vector2(-300f, 130f), 110f, "USE");
            _descend = MakeButton(canvas.transform, new Vector2(1f, 0f), new Vector2(-430f, 170f), 96f, "DOWN");

            // Hotbar cycle, bottom-centre above the hotbar.
            _prev = MakeButton(canvas.transform, new Vector2(0.5f, 0f), new Vector2(-360f, 130f), 90f, "◄");
            _next = MakeButton(canvas.transform, new Vector2(0.5f, 0f), new Vector2(360f, 130f), 90f, "►");

            // Menu button, top-right.
            _menu = MakeButton(canvas.transform, new Vector2(1f, 1f), new Vector2(-90f, -90f), 96f, "≡");

            _built = true;
        }

        // ---- Widget builders ------------------------------------------------------------------------------

        private static TouchLookPad MakeLookPad(Transform parent)
        {
            var go = new GameObject("LookPad", typeof(RectTransform), typeof(Image), typeof(TouchLookPad));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one; // full screen
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // invisible but raycast-catching
            return go.GetComponent<TouchLookPad>();
        }

        private static TouchStick MakeStick(Transform parent)
        {
            const float baseSize = 320f;
            var baseGo = new GameObject("MoveStick", typeof(RectTransform), typeof(Image), typeof(TouchStick));
            var rt = (RectTransform)baseGo.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f); // bottom-left
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(baseSize, baseSize);
            rt.anchoredPosition = new Vector2(230f, 230f);
            var baseImg = baseGo.GetComponent<Image>();
            baseImg.sprite = UiKit.ButtonSprite;
            baseImg.type = Image.Type.Sliced;
            baseImg.color = new Color(0.4f, 0.7f, 0.9f, 0.18f);

            var thumbGo = new GameObject("Thumb", typeof(RectTransform), typeof(Image));
            var trt = (RectTransform)thumbGo.transform;
            trt.SetParent(rt, false);
            trt.sizeDelta = new Vector2(baseSize * 0.42f, baseSize * 0.42f);
            trt.anchoredPosition = Vector2.zero;
            var thumbImg = thumbGo.GetComponent<Image>();
            thumbImg.sprite = UiKit.ButtonSprite;
            thumbImg.color = new Color(0.5f, 0.85f, 1f, 0.45f);
            thumbImg.raycastTarget = false;

            var stick = baseGo.GetComponent<TouchStick>();
            stick.Init(rt, trt);
            return stick;
        }

        private static TouchButton MakeButton(Transform parent, Vector2 anchor, Vector2 anchoredPos, float size, string label)
        {
            var go = new GameObject("TouchBtn_" + label, typeof(RectTransform), typeof(Image), typeof(TouchButton));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = anchoredPos;
            var img = go.GetComponent<Image>();
            img.sprite = UiKit.ButtonSprite;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.30f, 0.55f, 0.80f, 0.40f);

            UiKit.AddText(rt, 0f, 0f, size, size, label, Mathf.RoundToInt(size * 0.28f),
                new Color(0.9f, 0.97f, 1f, 0.95f), TextAnchor.MiddleCenter, FontStyle.Bold);

            return go.GetComponent<TouchButton>();
        }
    }

    /// <summary>A momentary/hold touch button. <see cref="Pressed"/> is the current hold; <see cref="DownThisFrame"/>
    /// is the press edge, and — like <c>Input.GetKeyDown</c> — it is **idempotent within a frame**: every read
    /// during the same frame returns the same value, and it is true on exactly one frame per press. That matters
    /// because a single action (e.g. Interact) is polled at more than one call site per frame; a consume-on-read
    /// latch would let the first site eat the edge. The edge is computed in an early Update (execution order
    /// −100) from the pointer state the EventSystem set, so gameplay (default order) reads a stable value.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public bool Pressed { get; private set; }
        public bool DownThisFrame { get; private set; }
        private bool _pressedLast;

        public void OnPointerDown(PointerEventData e) => Pressed = true;

        public void OnPointerUp(PointerEventData e) => Pressed = false;

        private void Update()
        {
            DownThisFrame = Pressed && !_pressedLast;
            _pressedLast = Pressed;
        }
    }

    /// <summary>Left virtual joystick: outputs a −1..1 vector from the drag offset, clamped to the base radius,
    /// and drives the thumb visual. Snaps back to centre on release.</summary>
    public sealed class TouchStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Vector2 Value { get; private set; }
        private RectTransform _baseRect;
        private RectTransform _thumb;

        public void Init(RectTransform baseRect, RectTransform thumb)
        {
            _baseRect = baseRect;
            _thumb = thumb;
        }

        public void OnPointerDown(PointerEventData e) => UpdateFrom(e);

        public void OnDrag(PointerEventData e) => UpdateFrom(e);

        public void OnPointerUp(PointerEventData e)
        {
            Value = Vector2.zero;
            if (_thumb != null)
            {
                _thumb.anchoredPosition = Vector2.zero;
            }
        }

        private void UpdateFrom(PointerEventData e)
        {
            if (_baseRect == null)
            {
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_baseRect, e.position, e.pressEventCamera, out var local);
            float radius = _baseRect.rect.width * 0.5f;
            Vector2 clamped = Vector2.ClampMagnitude(local, radius);
            Value = radius > 0.01f ? clamped / radius : Vector2.zero;
            if (_thumb != null)
            {
                _thumb.anchoredPosition = clamped;
            }
        }
    }

    /// <summary>Full-screen look area: accumulates drag delta (pixels) for the current frame. Owner clears it in
    /// LateUpdate after gameplay has read it, so each frame reports only that frame's movement.</summary>
    public sealed class TouchLookPad : MonoBehaviour, IDragHandler
    {
        public Vector2 Delta { get; private set; }

        public void OnDrag(PointerEventData e) => Delta += e.delta;

        public void ResetDelta() => Delta = Vector2.zero;
    }
}
