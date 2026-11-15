// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The ship editor's per-frame input: look (right mouse / right stick), fly (WASD + Q/E / left stick), place and remove
    /// (left click with the card's tool, Delete or X, the middle mouse button; A / X on the pad), turn the brush (R / Y), and
    /// the pad's two focus modes (#1198). Esc / pad B is not read here: <c>AppShell</c> hands it to
    /// <see cref="EditorShell.HandleCancel"/>, which comes back through <see cref="CloseTopLayer"/> (#3690) — B in the build
    /// view leaves the view, never the editor.
    /// </summary>
    public sealed partial class ShipEditor
    {
        // Gamepad focus (#1198): false = the side cards own the sticks, true = the build viewport does.
        // Cards first, so a pad player lands on the tools and picks a block before flying anywhere.
        private bool _padViewport;
        private GameObject _reticle;
        private string _hintKeyShown;

        // Stick look is already a per-frame delta (GamepadInputSource scales by deltaTime); this puts it
        // in the same range as the mouse path's 2.6 multiplier so both feel alike.
        private const float PadLookScale = 2.6f;

        /// <summary>True while a picker or one of the shell's dialogs is open — it owns the keys and the mouse.</summary>
        private bool ModalOpen => (_shell != null && _shell.IsDialogOpen) || _loadPicker != null || _formPicker != null;

        private void Update()
        {
            if (_cam == null)
            {
                return;
            }

            if (CreationSubmitDialog.IsAnyOpen || ModalOpen)
            {
                // The submit dialog (#3262), a picker or a question owns the keys and the mouse; the pad walks its buttons.
                _padViewport = false;
                SetPadSuspended(false);
                Look(false, false);
                _ghost?.Update(false, default, false);
                _view.Flush();
                return;
            }

            bool pad = InputMap.ActiveDevice == InputDeviceKind.Gamepad;
            UpdatePadFocus(pad);
            bool padViewport = pad && _padViewport;
            bool flying = Input.GetMouseButton(1);
            Look(flying, padViewport);

            // Keystrokes belong to a focused text field (the palette search, the form inputs) — typing
            // "sand" must not fly the camera away or turn the brush (#1388).
            bool typing = UiKit.TextFieldFocused();
            bool fast = Input.GetKey(KeyCode.LeftShift) || (padViewport && InputMap.PadHeld(PadButton.L3));
            Fly(typing, padViewport, fast);

            // The pad has no pointer, so in viewport mode the cards can never be 'under' the aim and the reticle always picks.
            _mouseOverUi = !padViewport && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!_mouseOverUi)
            {
                EditorSceneKit.WheelDolly(_cam.transform, fast); // wheel over a card scrolls the card (#1390)
            }

            RefreshPlacedCount();
            UpdateGhost(flying || _mouseOverUi);
            HandleBuildInput(typing, flying, padViewport);
            RefreshPadChrome(pad, padViewport);
            _view.Flush(); // upload any chunk meshes touched by this frame's edits
        }

        /// <summary>The mouse looks while RMB is held; the pad looks whenever the viewport has focus. Holding a
        /// button to look would cost a face button the editor needs for placing.</summary>
        private void Look(bool flying, bool padViewport)
        {
            Cursor.lockState = flying ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !flying;
            if (flying)
            {
                _yaw += Input.GetAxis("Mouse X") * 2.6f;
                _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * 2.6f, -89f, 89f);
                _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }
            else if (padViewport)
            {
                _yaw += InputMap.PadLookX() * PadLookScale;
                _pitch = Mathf.Clamp(_pitch - InputMap.PadLookY() * PadLookScale, -89f, 89f);
                _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }
        }

        /// <summary>WASD + Q/E (Space / Left Ctrl) fly, Shift faster, F frames the build; the left stick and LB/RB on the pad.
        /// With Ctrl held the letters stay still: Ctrl+Z / Ctrl+S are the shell's shortcuts, not a flight backwards.</summary>
        private void Fly(bool typing, bool padViewport, bool fast)
        {
            var t = _cam.transform;
            var move = Vector3.zero;
            if (!typing)
            {
                if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl)) move += Vector3.down;
                if (!CtrlHeld())
                {
                    if (Input.GetKey(KeyCode.W)) move += t.forward;
                    if (Input.GetKey(KeyCode.S)) move -= t.forward;
                    if (Input.GetKey(KeyCode.D)) move += t.right;
                    if (Input.GetKey(KeyCode.A)) move -= t.right;
                    if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) move += Vector3.up;
                    if (Input.GetKeyDown(KeyCode.F)) FrameCamera();
                }
            }

            if (padViewport)
            {
                move += t.forward * InputMap.PadStickY();
                move += t.right * InputMap.PadStickX();
                if (InputMap.PadHeld(PadButton.Rb)) move += Vector3.up;
                if (InputMap.PadHeld(PadButton.Lb)) move += Vector3.down;
            }

            t.position += move * ((fast ? 30f : 14f) * Time.deltaTime);
        }

        private static bool CtrlHeld() => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        /// <summary>Place / remove / turn. Mouse: a left click uses the card's tool (#3691), the middle button removes as
        /// before, Delete or X remove the block under the pointer, R turns the brush. Pad in the viewport: A uses the tool,
        /// X removes, Y turns, the d-pad steps the palette.</summary>
        private void HandleBuildInput(bool typing, bool flying, bool padViewport)
        {
            if (padViewport)
            {
                if (InputMap.PadDown(PadButton.A))
                {
                    UseTool();
                }
                else if (InputMap.PadDown(PadButton.X))
                {
                    TryRemove();
                }

                StepPalette(InputMap.PadDpadStep());
            }
            else if (!flying && !_mouseOverUi)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    UseTool();
                }
                else if (Input.GetMouseButtonDown(2) || EditorPaletteKit.RemoveKeyDown())
                {
                    TryRemove();
                }
            }

            // Rotate the shape brush (matches the in-game place-orientation control); Y on the pad.
            bool turnKey = !typing && !_mouseOverUi && !CtrlHeld() && Input.GetKeyDown(KeyCode.R);
            if (turnKey || (padViewport && InputMap.PadDown(PadButton.Y)))
            {
                SetOrient(_brushOrient + 1);
            }
        }

        /// <summary>Start swaps the pad between the side cards and the build viewport; B leaves the viewport through
        /// <see cref="CloseTopLayer"/>. Exactly one of the two owns the sticks at a time, so walking a list and flying the
        /// camera never fight over the same axis (#1198).</summary>
        private void UpdatePadFocus(bool pad)
        {
            if (_canvas == null)
            {
                return; // BuildUi has not run yet
            }

            if (!pad)
            {
                SetPadSuspended(false); // mouse in hand — the cards are always live
                return;
            }

            if (InputMap.Down(InputAction.UiMenu))
            {
                _padViewport = !_padViewport;
            }

            SetPadSuspended(_padViewport);
        }

        private void SetPadSuspended(bool suspended)
        {
            if (_canvas != null)
            {
                UiNav.SetSuspended(_canvas.gameObject, suspended);
            }
        }

        /// <summary>Esc / pad B's part of <see cref="CloseTopLayer"/>: leaves the pad's build view (back to the cards).
        /// Only while a pad flies it — with a mouse in hand the mode is not on screen.</summary>
        private bool LeavePadViewport()
        {
            if (!_padViewport || InputMap.ActiveDevice != InputDeviceKind.Gamepad)
            {
                return false;
            }

            _padViewport = false;
            SetPadSuspended(false);
            return true;
        }

        /// <summary>Moves the palette selection by one repeat-gated d-pad step (the pad's stand-in for
        /// scrolling the list, which needs a pointer).</summary>
        private void StepPalette(float step)
        {
            var list = _paletteCard?.List;
            if (Mathf.Abs(step) < 0.5f || list == null || _palette == null || _palette.Length == 0)
            {
                return;
            }

            int next = list.Selected + (step > 0f ? -1 : 1); // >0 = left = previous, matching the hotbar
            list.Select(Mathf.Clamp(next, 0, _palette.Length - 1));
        }

        /// <summary>Shows the centre reticle only while the pad flies the viewport — with a mouse the pointer IS the aim,
        /// and a crosshair would just be clutter — and names the keys of the moment in the status bar (at most four).</summary>
        private void RefreshPadChrome(bool pad, bool padViewport)
        {
            if (_reticle != null && _reticle.activeSelf != padViewport)
            {
                _reticle.SetActive(padViewport);
            }

            string key = HintKey(pad, padViewport, Tool);
            if (_shell != null && key != _hintKeyShown)
            {
                _hintKeyShown = key;
                _shell.SetHint(L(key));
            }
        }

        /// <summary>The status bar's key line (at most four keys, <see cref="EditorShell.SetHint"/>): for the mouse with the
        /// Place or the Remove tool, for the pad flying the build view, for the pad on the cards.</summary>
        internal static string HintKey(bool pad, bool padInView, EditorBuildTool tool)
            => pad
                ? (padInView ? "ui.ship.hint_pad" : "ui.ship.hint_pad_panel")
                : (tool == EditorBuildTool.Remove ? "ui.ship.hint_remove" : "ui.ship.hint");

        /// <summary>Where the picking ray starts on screen: the mouse pointer, or the centre reticle while
        /// the pad owns the viewport — a pad has no pointer, and a screen-centre crosshair is how every
        /// pad-driven builder aims (#1198).</summary>
        private Vector3 PickPoint() => _padViewport && InputMap.ActiveDevice == InputDeviceKind.Gamepad
            ? new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f)
            : Input.mousePosition;

        /// <summary>The pad's aiming crosshair: two thin bars at the exact screen centre, which is where
        /// <see cref="PickPoint"/> casts from. Hidden until the pad takes the viewport.</summary>
        private void BuildReticle(Transform root)
        {
            _reticle = new GameObject("Reticle", typeof(RectTransform));
            _reticle.transform.SetParent(root, false);
            var rt = (RectTransform)_reticle.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(22f, 22f);
            rt.anchoredPosition = Vector2.zero;
            Bar(rt, new Vector2(22f, 2f));
            Bar(rt, new Vector2(2f, 22f));
            _reticle.SetActive(false);

            static void Bar(RectTransform parent, Vector2 size)
            {
                var go = new GameObject("Bar", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var brt = (RectTransform)go.transform;
                brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
                brt.sizeDelta = size;
                brt.anchoredPosition = Vector2.zero;
                var img = go.AddComponent<Image>();
                img.sprite = UiKit.SolidSprite;
                img.color = new Color(0.45f, 0.92f, 1f, 0.85f);
                img.raycastTarget = false;
            }
        }
    }
}
