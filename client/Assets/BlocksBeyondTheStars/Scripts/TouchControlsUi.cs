// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// On-screen touch controls for the tablet / browser build (P3+P4): a left virtual joystick, a
    /// full-screen right-side look pad, and per-context action buttons. Three button clusters cover the
    /// game's control contexts — <b>on foot</b> (jump / mine / place / use / down / chat), <b>flight + EVA</b>
    /// (fire / land / ship / autopilot / view / up / down / use) and <b>speeder</b> (boost / hop / exit /
    /// refuel) — with the stick, look pad, hotbar ◄► and menu button shared. State is reported to
    /// <see cref="TouchInputSource"/>, which feeds it into <see cref="InputMap"/>'s combined read alongside
    /// keyboard/mouse and gamepad.
    ///
    /// Built on uGUI pointer handlers (not raw <c>Input.touches</c>) so the canvas scaler and multitouch
    /// routing are handled by the EventSystem. It is **inert on desktop**: the UI is only built when the
    /// device actually has touch (<see cref="ShouldShow"/>), and while hidden the source reads zero — so the
    /// shipped keyboard/mouse + pad experience cannot regress. The whole layer hides while a menu is open
    /// (menus are tap-navigable via the EventSystem directly). Geometry + feel need an on-device pass
    /// (playtest issue #202); the UI builds lazily on the first Update so labels can use the localizer.
    /// </summary>
    [DefaultExecutionOrder(-100)] // resolve control state before gameplay reads it this frame
    public sealed class TouchControlsUi : MonoBehaviour
    {
        public static TouchControlsUi Active { get; private set; }

        public GameBootstrap Game;
        public GameMenu Menu;
        public ChatUi Chat;

        private GameObject _rootPanel;
        private GameObject _onFootCluster, _flightCluster, _speederCluster;
        private TouchStick _stick;
        private TouchLookPad _lookPad;
        private TouchButton _jump, _mine, _place, _descend, _chat;      // on foot
        private TouchButton _fire, _flightUp, _flightDown;              // flight + EVA
        private TouchButton _boost, _hop;                               // speeder
        private TouchButton _prev, _next, _menu;                        // shared
        private readonly List<(InputAction Action, TouchButton Button)> _actions = new();
        private readonly List<(InputAction Action, TouchButton Button)> _heldActions = new();
        private bool _built;

        /// <summary>True on a touch device (tablet / touch-capable browser). Desktop mouse rigs report false,
        /// so the whole feature stays dormant there. WebGL on a desktop browser also reports false → the
        /// browser build uses keyboard/mouse or a pad, exactly as intended.</summary>
        public static bool ShouldShow() => Application.isMobilePlatform || Input.touchSupported;

        /// <summary>Whether the controls are currently live (built AND visible). The source reads zero when false.</summary>
        public bool Visible => _built && _rootPanel != null && _rootPanel.activeSelf;

        public Vector2 Move => Visible && _stick != null ? _stick.Value : Vector2.zero;
        public Vector2 LookDelta => Visible && _lookPad != null ? _lookPad.Delta : Vector2.zero;

        // The movement/interaction verbs OR across clusters — inactive clusters can't be pressed, so only the
        // context's own buttons contribute (TouchButton clears its state on disable).
        public bool JumpHeld => Visible && (Pressed(_jump) || Pressed(_flightUp) || Pressed(_hop));
        public bool JumpDown => Visible && (Down(_jump) || Down(_flightUp) || Down(_hop));
        public bool MineHeld => Visible && (Pressed(_mine) || Pressed(_fire));
        public bool MineDown => Visible && (Down(_mine) || Down(_fire));
        public bool PlaceDown => Visible && Down(_place);
        public bool DescendHeld => Visible && (Pressed(_descend) || Pressed(_flightDown));

        private static bool Pressed(TouchButton b) => b != null && b.Pressed;
        private static bool Down(TouchButton b) => b != null && b.DownThisFrame;

        /// <summary>Press edge for a discrete rebindable action (USE / LAND / SHIP / AUTO / VIEW / EXIT / FUEL
        /// buttons). Buttons in hidden clusters are inactive and report false.</summary>
        public bool ActionDownFor(InputAction action)
        {
            if (!Visible)
            {
                return false;
            }

            for (int i = 0; i < _actions.Count; i++)
            {
                if (_actions[i].Action == action && _actions[i].Button.DownThisFrame)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Hold state for a discrete rebindable action (the speeder BOOST button).</summary>
        public bool ActionHeldFor(InputAction action)
        {
            if (!Visible)
            {
                return false;
            }

            for (int i = 0; i < _heldActions.Count; i++)
            {
                if (_heldActions[i].Action == action && _heldActions[i].Button.Pressed)
                {
                    return true;
                }
            }

            return false;
        }

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

        private void Awake() => Active = this;

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
                // Lazy build on the first frame: WorldRig assigns Game/Menu/Chat after AddComponent (so Awake
                // is too early), and the localizer must be up for the button labels.
                if (!ShouldShow() || Game == null)
                {
                    return;
                }

                Build();
            }

            // Hide everything while a menu is open so taps reach the menu.
            bool show = Game != null && !Game.MenuOpen;
            if (_rootPanel.activeSelf != show)
            {
                _rootPanel.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            // Pick the cluster for the current control context.
            bool flight = Game.SpaceViewActive;
            bool speeder = !flight && Game.DrivenSpeeder != null;
            SetActive(_flightCluster, flight);
            SetActive(_speederCluster, speeder);
            SetActive(_onFootCluster, !flight && !speeder);

            // The menu button toggles the gameplay menu (equivalent to Tab / the pad's Start).
            if (_menu != null && _menu.DownThisFrame && Menu != null)
            {
                Menu.SetMenuOpen(!Game.MenuOpen);
            }

            // The chat button opens the chat input (tablets have no Enter key).
            if (_chat != null && _chat.DownThisFrame && Chat != null)
            {
                Chat.OpenInput();
            }
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active)
            {
                go.SetActive(active);
            }
        }

        private void LateUpdate()
        {
            // Clear the per-frame look accumulation after gameplay has read it this frame.
            _lookPad?.ResetDelta();
        }

        /// <summary>Localized short label for a touch button (falls back to the English text pre-localizer).</summary>
        private string L(string key, string fallback)
        {
            var loc = Game != null ? Game.Localizer : null;
            return loc != null ? loc.Get(key) : fallback;
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

            // Shared: hotbar cycle (bottom-centre, above the hotbar) + menu (top-right).
            _prev = MakeButton(canvas.transform, new Vector2(0.5f, 0f), new Vector2(-360f, 130f), 90f, "◄");
            _next = MakeButton(canvas.transform, new Vector2(0.5f, 0f), new Vector2(360f, 130f), 90f, "►");
            _menu = MakeButton(canvas.transform, new Vector2(1f, 1f), new Vector2(-90f, -90f), 96f, "≡");

            // ---- On-foot cluster (bottom-right) --------------------------------------------------------
            _onFootCluster = MakeCluster(canvas.transform, "OnFoot");
            var foot = _onFootCluster.transform;
            _jump = MakeButton(foot, new Vector2(1f, 0f), new Vector2(-140f, 150f), 120f, L("ui.touch.jump", "JUMP"));
            _mine = MakeButton(foot, new Vector2(1f, 0f), new Vector2(-280f, 260f), 120f, L("ui.touch.mine", "MINE"));
            _place = MakeButton(foot, new Vector2(1f, 0f), new Vector2(-140f, 300f), 120f, L("ui.touch.place", "PLACE"));
            var use = MakeButton(foot, new Vector2(1f, 0f), new Vector2(-300f, 130f), 110f, L("ui.touch.use", "USE"));
            _descend = MakeButton(foot, new Vector2(1f, 0f), new Vector2(-430f, 170f), 96f, L("ui.touch.down", "DOWN"));
            _chat = MakeButton(foot, new Vector2(1f, 1f), new Vector2(-200f, -90f), 88f, L("ui.touch.chat", "CHAT"));
            _actions.Add((InputAction.Interact, use));

            // ---- Flight + EVA cluster ------------------------------------------------------------------
            _flightCluster = MakeCluster(canvas.transform, "Flight");
            var fly = _flightCluster.transform;
            _fire = MakeButton(fly, new Vector2(1f, 0f), new Vector2(-140f, 150f), 130f, L("ui.touch.fire", "FIRE"));
            var flyUse = MakeButton(fly, new Vector2(1f, 0f), new Vector2(-300f, 130f), 110f, L("ui.touch.use", "USE"));
            var land = MakeButton(fly, new Vector2(1f, 0f), new Vector2(-290f, 265f), 110f, L("ui.touch.land", "LAND"));
            var shipIn = MakeButton(fly, new Vector2(1f, 0f), new Vector2(-150f, 305f), 100f, L("ui.touch.ship", "SHIP"));
            var auto = MakeButton(fly, new Vector2(1f, 0f), new Vector2(-420f, 190f), 92f, L("ui.touch.auto", "AUTO"));
            var view = MakeButton(fly, new Vector2(1f, 1f), new Vector2(-200f, -90f), 88f, L("ui.touch.view", "VIEW"));
            _flightUp = MakeButton(fly, new Vector2(0f, 0f), new Vector2(430f, 300f), 96f, L("ui.touch.up", "UP"));
            _flightDown = MakeButton(fly, new Vector2(0f, 0f), new Vector2(430f, 190f), 96f, L("ui.touch.down", "DOWN"));
            _actions.Add((InputAction.Interact, flyUse));
            _actions.Add((InputAction.FlightPadChooser, land));
            _actions.Add((InputAction.FlightEnterInterior, shipIn));
            _actions.Add((InputAction.FlightAutopilot, auto));
            _actions.Add((InputAction.ToggleThirdPerson, view));

            // ---- Speeder cluster -----------------------------------------------------------------------
            _speederCluster = MakeCluster(canvas.transform, "Speeder");
            var spd = _speederCluster.transform;
            _boost = MakeButton(spd, new Vector2(1f, 0f), new Vector2(-140f, 150f), 130f, L("ui.touch.boost", "BOOST"));
            _hop = MakeButton(spd, new Vector2(1f, 0f), new Vector2(-300f, 130f), 110f, L("ui.touch.jump", "JUMP"));
            var exit = MakeButton(spd, new Vector2(1f, 0f), new Vector2(-290f, 265f), 110f, L("ui.touch.exit", "EXIT"));
            var fuel = MakeButton(spd, new Vector2(1f, 0f), new Vector2(-150f, 305f), 100f, L("ui.touch.fuel", "FUEL"));
            _actions.Add((InputAction.SpeederExit, exit));
            _actions.Add((InputAction.SpeederRefuel, fuel));
            _heldActions.Add((InputAction.SpeederBoost, _boost));

            // Only the on-foot cluster starts visible; Update swaps clusters with the control context.
            _flightCluster.SetActive(false);
            _speederCluster.SetActive(false);

            _built = true;
        }

        // ---- Widget builders ------------------------------------------------------------------------------

        private static GameObject MakeCluster(Transform parent, string name)
        {
            var go = new GameObject("TouchCluster_" + name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one; // stretch, so children anchor to screen corners as usual
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

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

            var text = UiKit.AddText(rt, 0f, 0f, size, size, label, Mathf.RoundToInt(size * 0.24f),
                new Color(0.9f, 0.97f, 1f, 0.95f), TextAnchor.MiddleCenter, FontStyle.Bold);
            text.resizeTextForBestFit = true; // localized labels vary in length (e.g. DE "Tanken")
            text.resizeTextMinSize = 10;
            text.resizeTextMaxSize = Mathf.RoundToInt(size * 0.26f);

            return go.GetComponent<TouchButton>();
        }
    }

    /// <summary>A momentary/hold touch button. <see cref="Pressed"/> is the current hold; <see cref="DownThisFrame"/>
    /// is the press edge, and — like <c>Input.GetKeyDown</c> — it is **idempotent within a frame**: every read
    /// during the same frame returns the same value, and it is true on exactly one frame per press. That matters
    /// because a single action (e.g. Interact) is polled at more than one call site per frame; a consume-on-read
    /// latch would let the first site eat the edge. The edge is computed in an early Update (execution order
    /// −100) from the pointer state the EventSystem set, so gameplay (default order) reads a stable value.
    /// All state clears on disable, so a button can't stay "held" across a cluster/context switch.</summary>
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

        private void OnDisable()
        {
            // A hidden button gets no OnPointerUp — clear everything so it can't stay held.
            Pressed = false;
            DownThisFrame = false;
            _pressedLast = false;
        }
    }

    /// <summary>Left virtual joystick: outputs a −1..1 vector from the drag offset, clamped to the base radius,
    /// and drives the thumb visual. Snaps back to centre on release (and on disable).</summary>
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

        public void OnPointerUp(PointerEventData e) => Release();

        private void OnDisable() => Release();

        private void Release()
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

        private void OnDisable() => Delta = Vector2.zero;
    }
}
