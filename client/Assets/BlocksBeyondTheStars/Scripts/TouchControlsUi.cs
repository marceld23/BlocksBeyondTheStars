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
    /// game's control contexts — <b>on foot</b> (jump / mine / place / use / down / chat / view / map, plus
    /// ROTATE while a rotatable block is held and ATTACK while a weapon is), <b>flight + EVA</b> (fire /
    /// land / ship / autopilot / map / view / up / down / use at the helm; PLACE + DEPLOY replace land / ship /
    /// autopilot in EVA) and <b>speeder</b> (boost / hop / exit / refuel) — with the stick, look pad, hotbar
    /// ◄►, slot-actions "…" (#940), the context-actions ACT list (#1042), VEGA's NEXT (#1041) and the menu
    /// button shared. State is reported to <see cref="TouchInputSource"/>, which feeds it into
    /// <see cref="InputMap"/>'s combined read alongside keyboard/mouse and gamepad.
    ///
    /// Buttons for verbs that only sometimes apply are shown only then (rotate, attack, next, act): a
    /// tablet has room for a handful of thumb targets, and a button that does nothing reads as broken.
    /// The long tail of verbs (trade / dock / loot / lamp / …) lives behind ACT — see
    /// <see cref="ContextActionsUi"/>.
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
        public PlayerController Player; // applicability probes for the contextual buttons (#1042)
        public VegaPanel Vega;          // NEXT shows only while a line is up (#1041)

        private GameObject _rootPanel;
        private GameObject _onFootCluster, _flightCluster, _speederCluster;
        private TouchStick _stick;
        private TouchLookPad _lookPad;
        private TouchButton _jump, _mine, _place, _descend, _chat;      // on foot
        private TouchButton _rotate, _attack;                           // on foot, contextual (#1042)
        private TouchButton _fire, _flightUp, _flightDown;              // flight + EVA
        private TouchButton _land, _shipIn, _auto, _flightMap;          // helm only — swapped out in EVA
        private TouchButton _evaPlace, _evaDeploy;                      // EVA only (#1042)
        private TouchButton _boost, _hop;                               // speeder
        private TouchButton _prev, _next, _menu;                        // shared
        private TouchButton _slotActions;                               // opens the hotbar slot-action pie (#940)
        private TouchButton _contextActions;                            // opens the context-actions list (#1042)
        private TouchButton _vegaNext;                                  // VEGA continue (#1041)
        private readonly List<(InputAction Action, TouchButton Button)> _actions = new();
        private readonly List<(InputAction Action, TouchButton Button)> _heldActions = new();
        private readonly List<TouchButton> _all = new();                // every button — for HadActivityThisFrame
        private bool _built;

        // Input.touchSupported is capability, not usage: Windows ≥8 standalone and desktop browsers
        // report it without any touchscreen (the OS/browser merely exposes the touch API), which popped
        // the overlay on plain desktops (#219). Evidence latch instead: the first REAL touch flips it.
        private static bool s_touchSeen;

        /// <summary>True on a device where touch is in use: mobile platforms immediately, everything else
        /// (desktop, WebGL in a desktop browser) only once an actual touch has been seen — so mouse-only
        /// rigs stay dormant while touch laptops and iPad-Safari's desktop UA get controls on first tap.</summary>
        public static bool ShouldShow() => Application.isMobilePlatform || s_touchSeen;

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
        public bool PlaceDown => Visible && (Down(_place) || Down(_evaPlace)); // EVA builds through its own PLACE (#1042)
        public bool DescendHeld => Visible && (Pressed(_descend) || Pressed(_flightDown));

        /// <summary>Any on-screen button held this frame — flips the HUD into touch glyph mode on ANY tap, not
        /// only on the movement verbs (#1042).</summary>
        public bool AnyButtonPressed
        {
            get
            {
                if (!Visible)
                {
                    return false;
                }

                for (int i = 0; i < _all.Count; i++)
                {
                    if (_all[i] != null && _all[i].Pressed)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

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
            if (!s_touchSeen && Input.touchCount > 0)
            {
                s_touchSeen = true;
            }

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
            bool onFoot = !flight && !speeder;
            SetActive(_flightCluster, flight);
            SetActive(_speederCluster, speeder);
            SetActive(_onFootCluster, onFoot);

            // Contextual buttons (#1042): shown only while their verb applies, so a tablet never carries a
            // dead target. On foot: ROTATE with a rotatable block in hand, ATTACK with a weapon (or on the
            // Guardian core, where the same hold breaches it). In flight the helm-only verbs (land / ship /
            // autopilot / map) give way to the EVA pair (place / deploy) — SpaceView polls each set only in
            // its own state, so a swapped-out button would be inert anyway.
            if (onFoot)
            {
                SetActive(_rotate?.gameObject, Player != null && Player.HeldRotatable);
                var finale = FinaleView.Instance;
                SetActive(_attack?.gameObject, (Player != null && Player.HoldsWeapon) || (finale != null && finale.BreachAvailable));
            }

            if (flight)
            {
                bool eva = Game.InEva;
                SetActive(_land?.gameObject, !eva);
                SetActive(_shipIn?.gameObject, !eva);
                SetActive(_auto?.gameObject, !eva);
                SetActive(_flightMap?.gameObject, !eva);
                SetActive(_evaPlace?.gameObject, eva);
                SetActive(_evaDeploy?.gameObject, eva);
            }

            // NEXT advances VEGA (#1041) — visible only while a line is on screen; the panel itself stays
            // non-modal, so this sits beside the other shared controls rather than covering the world.
            SetActive(_vegaNext?.gameObject, Vega != null && Vega.LineShowing);

            // ACT opens the context-actions list (#1042) — every verb that applies right now, one tap away.
            // Like the slot pie, opening registers a menu owner (hides this layer); closing brings it back.
            var ctx = ContextActionsUi.Instance;
            if (_contextActions != null)
            {
                bool canCtx = ctx != null && ctx.CanOpen;
                SetActive(_contextActions.gameObject, canCtx);
                if (canCtx && _contextActions.DownThisFrame)
                {
                    ctx.Toggle();
                }
            }

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

            // The "…" button opens the slot-action pie on the selected hotbar slot (#940). Shown only while
            // the pie could actually open (same hidden-hotbar gates; EVA allowed). Opening registers a menu
            // owner, which hides this whole layer — so taps reach the pie, and closing it brings us back.
            var pie = HotbarActionUi.Instance;
            if (_slotActions != null)
            {
                bool canAct = pie != null && pie.CanOpen;
                SetActive(_slotActions.gameObject, canAct);
                if (canAct && _slotActions.DownThisFrame)
                {
                    pie.Toggle();
                }
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

            // Slot-action pie opener (#940), tucked beside the ► arrow: touch has no key to press, so this
            // button IS the feature's input path here. Symbol label like ◄►≡, so no locale key is needed;
            // Update shows it only while the pie could actually open.
            _slotActions = MakeButton(canvas.transform, new Vector2(0.5f, 0f), new Vector2(480f, 130f), 90f, "…");

            // Context-actions list opener (#1042), mirrored on the ◄ side: every verb that applies right now
            // (trade / dock / loot / lamp / …), one tap away. Update shows it only while the list could open.
            _contextActions = MakeButton(canvas.transform, new Vector2(0.5f, 0f), new Vector2(-480f, 130f), 90f, L("ui.touch.actions", "ACT"));

            // VEGA continue (#1041), top-centre: the speech panel is non-modal and keyboard-only advanced
            // before this — on a tablet a line (and every page after it) simply stayed forever.
            _vegaNext = MakeButton(canvas.transform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), 96f, L("ui.touch.next", "NEXT ▶"));
            _actions.Add((InputAction.VegaContinue, _vegaNext));

            // ---- On-foot cluster (bottom-right) --------------------------------------------------------
            _onFootCluster = MakeCluster(canvas.transform, "OnFoot");
            var foot = _onFootCluster.transform;
            _jump = MakeButton(foot, new Vector2(1f, 0f), new Vector2(-140f, 150f), 120f, L("ui.touch.jump", "JUMP"));
            _mine = MakeButton(foot, new Vector2(1f, 0f), new Vector2(-280f, 260f), 120f, L("ui.touch.mine", "MINE"));
            _place = MakeButton(foot, new Vector2(1f, 0f), new Vector2(-140f, 300f), 120f, L("ui.touch.place", "PLACE"));
            var use = MakeButton(foot, new Vector2(1f, 0f), new Vector2(-300f, 130f), 110f, L("ui.touch.use", "USE"));
            _descend = MakeButton(foot, new Vector2(1f, 0f), new Vector2(-430f, 170f), 96f, L("ui.touch.down", "DOWN"));
            _chat = MakeButton(foot, new Vector2(1f, 1f), new Vector2(-200f, -90f), 88f, L("ui.touch.chat", "CHAT"));
            var footView = MakeButton(foot, new Vector2(1f, 1f), new Vector2(-310f, -90f), 88f, L("ui.touch.view", "VIEW"));
            var footMap = MakeButton(foot, new Vector2(1f, 1f), new Vector2(-420f, -90f), 88f, L("ui.touch.map", "MAP"));
            // Contextual (#1042): ROTATE beside PLACE while a rotatable block is held; ATTACK is a hold-capable
            // button (weapon swing on tap; the Guardian-core breach channels while held).
            _rotate = MakeButton(foot, new Vector2(1f, 0f), new Vector2(-420f, 300f), 96f, L("ui.touch.rotate", "ROTATE"));
            _attack = MakeButton(foot, new Vector2(1f, 0f), new Vector2(-560f, 240f), 100f, L("ui.touch.attack", "ATTACK"));
            _actions.Add((InputAction.Interact, use));
            _actions.Add((InputAction.ToggleThirdPerson, footView));
            _actions.Add((InputAction.PlanetMap, footMap));
            _actions.Add((InputAction.RotateShape, _rotate));
            _actions.Add((InputAction.PrimaryFire, _attack));
            _heldActions.Add((InputAction.PrimaryFire, _attack));

            // ---- Flight + EVA cluster ------------------------------------------------------------------
            _flightCluster = MakeCluster(canvas.transform, "Flight");
            var fly = _flightCluster.transform;
            _fire = MakeButton(fly, new Vector2(1f, 0f), new Vector2(-140f, 150f), 130f, L("ui.touch.fire", "FIRE"));
            var flyUse = MakeButton(fly, new Vector2(1f, 0f), new Vector2(-300f, 130f), 110f, L("ui.touch.use", "USE"));
            _land = MakeButton(fly, new Vector2(1f, 0f), new Vector2(-290f, 265f), 110f, L("ui.touch.land", "LAND"));
            _shipIn = MakeButton(fly, new Vector2(1f, 0f), new Vector2(-150f, 305f), 100f, L("ui.touch.ship", "SHIP"));
            _auto = MakeButton(fly, new Vector2(1f, 0f), new Vector2(-420f, 190f), 92f, L("ui.touch.auto", "AUTO"));
            var view = MakeButton(fly, new Vector2(1f, 1f), new Vector2(-200f, -90f), 88f, L("ui.touch.view", "VIEW"));
            _flightMap = MakeButton(fly, new Vector2(1f, 1f), new Vector2(-310f, -90f), 88f, L("ui.touch.map", "MAP"));
            _flightUp = MakeButton(fly, new Vector2(0f, 0f), new Vector2(430f, 300f), 96f, L("ui.touch.up", "UP"));
            _flightDown = MakeButton(fly, new Vector2(0f, 0f), new Vector2(430f, 190f), 96f, L("ui.touch.down", "DOWN"));
            // EVA (#1042): PLACE + DEPLOY take the helm-only LAND / SHIP spots — Update swaps them with Game.InEva.
            _evaPlace = MakeButton(fly, new Vector2(1f, 0f), new Vector2(-290f, 265f), 110f, L("ui.touch.place", "PLACE"));
            _evaDeploy = MakeButton(fly, new Vector2(1f, 0f), new Vector2(-150f, 305f), 100f, L("ui.touch.deploy", "DEPLOY"));
            _actions.Add((InputAction.Interact, flyUse));
            _actions.Add((InputAction.FlightPadChooser, _land));
            _actions.Add((InputAction.FlightEnterInterior, _shipIn));
            _actions.Add((InputAction.FlightAutopilot, _auto));
            _actions.Add((InputAction.ToggleThirdPerson, view));
            _actions.Add((InputAction.FlightMap, _flightMap));
            _actions.Add((InputAction.EvaDeployStation, _evaDeploy));

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

            // Only the on-foot cluster starts visible; Update swaps clusters with the control context. The
            // contextual buttons start hidden too — Update shows each while its verb applies.
            _flightCluster.SetActive(false);
            _speederCluster.SetActive(false);
            foreach (var b in new[] { _rotate, _attack, _evaPlace, _evaDeploy, _vegaNext, _contextActions })
            {
                b.gameObject.SetActive(false);
            }

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

        private TouchButton MakeButton(Transform parent, Vector2 anchor, Vector2 anchoredPos, float size, string label)
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
            // Localized labels vary in length (e.g. DE "Tanken") — shrink to fit rather than spill.
            UiKit.FitLabel(text, 10, Mathf.RoundToInt(size * 0.26f));

            var button = go.GetComponent<TouchButton>();
            _all.Add(button); // every button feeds HadActivityThisFrame
            return button;
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
