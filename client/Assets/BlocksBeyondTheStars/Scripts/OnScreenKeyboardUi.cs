// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The on-screen keyboard a gamepad types with (#1211). Before it, a pad could reach every screen but
    /// not a single text field: a world name, a join address, a beacon label and the chat box all needed a
    /// physical keyboard, so "finish a session without a mouse or keyboard" was simply not possible.
    ///
    /// It is a plain uGUI button grid on its own canvas above every dialog, driven by
    /// <see cref="OnScreenKeyboardLayout"/> — the layout and every text edit live in Client.Core so they are
    /// unit-tested headlessly; this file is buttons, colours and the modal rules:
    /// <list type="bullet">
    /// <item><see cref="InputMap.ModalCapture"/> is raised while it is open, so one press of B closes the
    /// keyboard and NOT the screen behind it — two MonoBehaviours must not race over one button press.</item>
    /// <item><see cref="UiKit.TextFieldFocused"/> reports true while it is open, which is the gate the rest
    /// of the game already uses for "the player is typing".</item>
    /// <item>It is rebuilt per open and destroyed on close: it is opened once in a while by a human hand,
    /// never per frame, and leaving no canvas behind keeps it out of every other screen's way.</item>
    /// </list>
    /// Native mobile keeps the OS keyboard and WebGL-touch keeps the browser prompt
    /// (<see cref="TouchTextEntry"/>) — this is the desktop/console gap only.
    /// </summary>
    public sealed class OnScreenKeyboardUi : MonoBehaviour
    {
        private const float KeyW = 92f;
        private const float KeyH = 72f;
        private const float Gap = 8f;
        private const float PanelW = 1160f;

        private static OnScreenKeyboardUi _instance;

        private Canvas _canvas;
        private Transform _root;
        private Text _preview;
        private string _title = string.Empty;
        private string _text = string.Empty;
        private int _maxLength;
        private bool _mask;                                          // password / PIN: preview shows bullets (#1289)
        private KeyboardContentKind _kind = KeyboardContentKind.Text; // number fields drop letters (#1289)
        private bool _shift;
        private KeyboardPage _page = KeyboardPage.Letters;
        private System.Action<string> _onDone;
        private System.Action _onCancel;
        private GameObject _restoreSelection; // the field that opened us; reselected on Done / Cancel (#1289)

        /// <summary>True while the keyboard owns the input. Read by <see cref="UiKit.TextFieldFocused"/>,
        /// so every existing "am I typing?" check covers it without knowing this class exists.</summary>
        public static bool IsOpen => _instance != null && _instance._canvas != null;

        /// <summary>Opens the keyboard over everything else. <paramref name="onDone"/> receives the finished
        /// text; <paramref name="onCancel"/> (optional) runs when the player backs out instead. Opening it a
        /// second time replaces the first — the last caller wins rather than stacking two keyboards.
        /// <paramref name="mask"/> shows bullets instead of the text (passwords, PINs); <paramref name="kind"/>
        /// filters the keys a number field will take; <paramref name="restoreSelection"/> is reselected when
        /// the keyboard closes through Done or Cancel, so the pad lands back on the field it came from (#1289).</summary>
        public static void Open(string title, string initial, int maxLength, System.Action<string> onDone,
            System.Action onCancel = null, bool mask = false, KeyboardContentKind kind = KeyboardContentKind.Text,
            GameObject restoreSelection = null)
        {
            if (_instance == null)
            {
                var go = new GameObject("OnScreenKeyboardUi");
                if (Application.isPlaying)
                {
                    DontDestroyOnLoad(go); // survives the menu/world scene swap like the rest of the shell
                }

                _instance = go.AddComponent<OnScreenKeyboardUi>();
            }

            _instance.Show(title, initial, maxLength, onDone, onCancel, mask, kind, restoreSelection);
        }

        /// <summary>Closes the keyboard without accepting the text (no callback runs).</summary>
        public static void Dismiss()
        {
            if (_instance != null)
            {
                _instance.Teardown();
            }
        }

        /// <summary>Whether a text field should route through this keyboard: a pad is the device in hand and
        /// the platform has no keyboard of its own to offer. The one place that rule is written down.</summary>
        public static bool WantedFor() =>
            !TouchTextEntry.NeedsPrompt
            && !Application.isMobilePlatform
            && InputMap.ActiveDevice == InputDeviceKind.Gamepad;

        private void Show(string title, string initial, int maxLength, System.Action<string> onDone,
            System.Action onCancel, bool mask, KeyboardContentKind kind, GameObject restoreSelection)
        {
            Teardown();
            _title = title ?? string.Empty;
            _text = initial ?? string.Empty;
            _maxLength = maxLength;
            _mask = mask;
            _kind = kind;
            _onDone = onDone;
            _onCancel = onCancel;
            _restoreSelection = restoreSelection;
            _shift = false;
            _page = KeyboardPage.Letters;
            InputMap.ModalCapture = true;

            // Drop the current selection BEFORE the grid exists: while the submitting field stayed selected,
            // the keyboard's UiNavFocus saw "a valid control is focused" and never claimed the first key, so
            // the sticks wandered over the dialog behind us and A re-opened the keyboard from the field (#1289).
            var es = EventSystem.current;
            if (es != null)
            {
                es.SetSelectedGameObject(null);
            }

            Build();
        }

        /// <summary>Destroys a piece of this screen the way the current context allows: Unity refuses
        /// <c>Object.Destroy</c> outside play mode, and the EditMode suite builds this keyboard for real.</summary>
        private static void DestroyUi(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(go);
            }
            else
            {
                DestroyImmediate(go);
            }
        }

        private void Teardown()
        {
            if (_canvas != null)
            {
                DestroyUi(_canvas.gameObject);
            }

            _canvas = null;
            _root = null;
            _preview = null;
            _onDone = null;
            _onCancel = null;
            _restoreSelection = null;
            InputMap.ModalCapture = false;
        }

        /// <summary>Hands the pad selection back to the field that opened the keyboard (Done / Cancel only —
        /// a bare Dismiss is another screen taking over, and it decides where focus goes). The field's own
        /// bridge keeps it deactivated on a pad, so this only positions the cursor; it does not start typing.</summary>
        private void RestoreSelection()
        {
            var target = _restoreSelection;
            var es = EventSystem.current;
            if (target != null && es != null && target.activeInHierarchy)
            {
                es.SetSelectedGameObject(target);
            }
        }

        private void Build()
        {
            _canvas = UiKit.CreateCanvas("OnScreenKeyboardCanvas");
            _canvas.sortingOrder = 5000; // above every dialog, including the modal scrims
            _root = _canvas.transform;
            UiNav.Enable(_canvas.gameObject); // the canvas is a scene root, so the component belongs on IT (#1198)
            BuildContent();
        }

        private void BuildContent()
        {
            string[] rows = OnScreenKeyboardLayout.Rows(_page, _shift);
            float panelH = 150f + (rows.Length + 1) * (KeyH + Gap) + 22f;
            float px = (1920f - PanelW) / 2f;
            float py = (1080f - panelH) / 2f;
            UiKit.AddModalDim(_root); // scrim first — uGUI draws in child order, so it must sit behind the panel
            UiKit.AddDialogPanel(_root, px, py, PanelW, panelH);

            UiKit.AddText(_root, px + 30f, py + 18f, PanelW - 60f, 34f, _title, 22, UiKit.Cyan, TextAnchor.MiddleLeft,
                FontStyle.Bold);

            // The line being edited, with a block caret so an empty field still shows where the text will go.
            UiKit.AddPanel(_root, px + 30f, py + 58f, PanelW - 60f, 56f, new Color(0.03f, 0.07f, 0.14f, 0.95f));
            _preview = UiKit.AddText(_root, px + 44f, py + 58f, PanelW - 88f, 56f, string.Empty, 24, UiKit.TextCol);
            RefreshPreview();

            float y = py + 132f;
            for (int r = 0; r < rows.Length; r++)
            {
                string row = rows[r];
                float rowW = row.Length * KeyW + (row.Length - 1) * Gap;
                float x = px + (PanelW - rowW) / 2f;
                foreach (char c in row)
                {
                    string key = c.ToString();
                    KeyButton(x, y, KeyW, key, () => Press(key));
                    x += KeyW + Gap;
                }

                y += KeyH + Gap;
            }

            BuildCommandRow(px, y);
        }

        /// <summary>The bottom row: case, page, space, backspace and the two ways out. Sized in one place so
        /// the row always ends flush with the letter grid above it.</summary>
        private void BuildCommandRow(float px, float y)
        {
            var cells = new (string Label, float W, System.Action OnClick)[]
            {
                (UiKit.L(_shift ? "ui.keyboard.lower" : "ui.keyboard.upper"), 150f, () => Press(OnScreenKeyboardLayout.Shift)),
                (UiKit.L(_page == KeyboardPage.Letters ? "ui.keyboard.symbols" : "ui.keyboard.letters"), 150f,
                    () => Press(OnScreenKeyboardLayout.Page)),
                (UiKit.L("ui.keyboard.space"), 300f, () => Press(OnScreenKeyboardLayout.Space)),
                (UiKit.L("ui.keyboard.back"), 150f, () => Press(OnScreenKeyboardLayout.Backspace)),
                (UiKit.L("ui.keyboard.cancel"), 170f, () => Press(OnScreenKeyboardLayout.Cancel)),
                (UiKit.L("ui.keyboard.done"), 170f, () => Press(OnScreenKeyboardLayout.Done)),
            };

            float total = -Gap;
            foreach (var cell in cells)
            {
                total += cell.W + Gap;
            }

            float x = px + (PanelW - total) / 2f;
            foreach (var cell in cells)
            {
                KeyButton(x, y, cell.W, cell.Label, cell.OnClick);
                x += cell.W + Gap;
            }
        }

        /// <summary>A themed key. <see cref="UiKit.AddButton"/> left-aligns its label for menu rows; a key
        /// wants it centred, which is the only thing adjusted here — colours, hover and click sound stay
        /// exactly the shared ones.</summary>
        private void KeyButton(float x, float y, float w, string label, System.Action onClick)
        {
            var button = UiKit.AddButton(_root, x, y, w, KeyH, label, onClick);
            var text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                UiKit.Place(text.gameObject, 0f, 0f, w, KeyH);
                text.alignment = TextAnchor.MiddleCenter;
            }
        }

        /// <summary>Redraws the grid after a case / page switch. Same shape as the settings screen: drop the
        /// children and lay the panel out again, so the keyboard keeps its canvas (and with it the pad's
        /// focus component) across the swap.</summary>
        private void Rebuild()
        {
            if (_root == null)
            {
                return;
            }

            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                DestroyUi(_root.GetChild(i).gameObject);
            }

            BuildContent();
        }

        /// <summary>Test seam: the text currently being edited. CI cannot click a key.</summary>
        public static string TextForTest => _instance != null ? _instance._text : null;

        /// <summary>Test seam: what the preview line actually SHOWS (bullets for a password field, #1289).</summary>
        public static string PreviewForTest => _instance != null && _instance._preview != null ? _instance._preview.text : null;

        /// <summary>Test seam: does exactly what pressing that key on the grid does, commands included —
        /// the same entry point the buttons use, so a test exercises the real wiring rather than a copy.</summary>
        public static void PressForTest(string key)
        {
            if (_instance != null)
            {
                _instance.Press(key);
            }
        }

        /// <summary>Applies one key press: the two commands that change the KEYBOARD are handled here, the
        /// two that end it call through, and everything else is a text edit the layout decides.</summary>
        private void Press(string key)
        {
            if (key == OnScreenKeyboardLayout.Done)
            {
                Accept();
                return;
            }

            if (key == OnScreenKeyboardLayout.Cancel)
            {
                Cancel();
                return;
            }

            if (key == OnScreenKeyboardLayout.Shift)
            {
                _shift = !_shift;
                Rebuild();
                return;
            }

            if (key == OnScreenKeyboardLayout.Page)
            {
                _page = _page == KeyboardPage.Letters ? KeyboardPage.Symbols : KeyboardPage.Letters;
                Rebuild();
                return;
            }

            _text = OnScreenKeyboardLayout.Apply(_text, key, _maxLength, _kind);
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (_preview != null)
            {
                _preview.text = OnScreenKeyboardLayout.Preview(_text, _mask) + "_";
            }
        }

        private void Accept()
        {
            var done = _onDone;
            string text = _text;
            RestoreSelection();
            Teardown();
            done?.Invoke(text);
        }

        private void Cancel()
        {
            var cancelled = _onCancel;
            RestoreSelection();
            Teardown();
            cancelled?.Invoke();
        }

        private void Update()
        {
            if (_canvas == null)
            {
                return;
            }

            // Read the physical controls, not the actions: ModalCapture deliberately blanks UiCancel/UiMenu
            // for everyone while this is open, and "everyone" includes this screen.
            if (Input.GetKeyDown(KeyCode.Escape) || InputMap.PadDown(PadButton.B))
            {
                Cancel();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                || InputMap.PadDown(PadButton.Start))
            {
                Accept();
                return;
            }

            // Backspace on a real keyboard, and X on the pad — the one edit worth a shortcut, because it is
            // the one a player repeats.
            if (Input.GetKeyDown(KeyCode.Backspace) || InputMap.PadDown(PadButton.X))
            {
                Press(OnScreenKeyboardLayout.Backspace);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                InputMap.ModalCapture = false;
                _instance = null;
            }
        }
    }

    /// <summary>
    /// Routes ONE uGUI text field through the on-screen keyboard while a gamepad is the active device
    /// (#1211). Attached to every field built by <see cref="UiKit.AddInput"/>, and completely inert on
    /// keyboard/mouse — the shipped desktop flow is not touched.
    ///
    /// It does two things. Submit (A on the field) opens the keyboard on the field's current text and
    /// writes the result back through <c>field.text</c>, so the field's own <c>onValueChanged</c> listeners
    /// fire exactly as they would after typing. And while a pad is in hand it keeps the field DEACTIVATED:
    /// a focused uGUI <see cref="InputField"/> swallows the navigation axes, which used to mean a pad
    /// player who landed on a field could not leave it again.
    /// </summary>
    [RequireComponent(typeof(InputField))]
    public sealed class PadTextEntryBridge : MonoBehaviour, ISubmitHandler
    {
        private InputField _field;
        private string _label = string.Empty;

        public void Init(InputField field, string label)
        {
            _field = field;
            _label = label ?? string.Empty;
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (_field == null || !OnScreenKeyboardUi.WantedFor())
            {
                return; // keyboard in hand: uGUI's own submit behaviour, unchanged
            }

            var field = _field;
            field.DeactivateInputField();
            OnScreenKeyboardUi.Open(
                string.IsNullOrEmpty(_label) ? UiKit.L("ui.keyboard.title") : _label,
                field.text,
                field.characterLimit,
                text =>
                {
                    if (field != null)
                    {
                        field.text = text;
                    }
                },
                mask: IsMasked(field),
                kind: KindOf(field),
                restoreSelection: field.gameObject);
        }

        /// <summary>Password and PIN fields must not echo on the big preview line (#1289).</summary>
        private static bool IsMasked(InputField field) =>
            field.contentType == InputField.ContentType.Password
            || field.contentType == InputField.ContentType.Pin
            || field.inputType == InputField.InputType.Password;

        /// <summary>The keyboard's content kind for a field: the <c>text</c> setter bypasses uGUI's own
        /// character validation, so the filter has to happen on the keyboard side (#1289).</summary>
        private static KeyboardContentKind KindOf(InputField field)
        {
            switch (field.contentType)
            {
                case InputField.ContentType.IntegerNumber:
                case InputField.ContentType.Pin:
                    return KeyboardContentKind.Integer;
                case InputField.ContentType.DecimalNumber:
                    return KeyboardContentKind.Decimal;
                default:
                    return KeyboardContentKind.Text;
            }
        }

        private void LateUpdate()
        {
            // uGUI activates a field the moment it is SELECTED, which is how navigating onto one used to
            // trap the pad. Undo that every frame while a pad is the device in hand: the field can be
            // selected and stepped over freely, and A is what actually starts typing.
            //
            // Every frame, not once: ActivateInputField arms a flag that the field re-reads on its next
            // Update, so a single Deactivate would be undone the moment after we did it.
            if (_field != null && _field.isFocused && OnScreenKeyboardUi.WantedFor())
            {
                _field.DeactivateInputField();
            }
        }
    }
}
