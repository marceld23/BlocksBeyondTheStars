// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The gamepad in the Station/Village editor (#3692, V16 — it had none: pad B only left the editor). As in the ship editor
    /// (#1198) the pad has two places, and Menu swaps them: the CARDS (the shell's navigation walks the tools, the palette, the
    /// form and the header; the first focus is the first tool, never Back) and the BUILD VIEW — the left stick flies, the right
    /// stick looks, LB / RB sink and rise, a crosshair at the screen centre aims, A uses the tool (place or remove), X removes,
    /// Y turns the form, the d-pad steps through the palette. B leaves the build view through the shell's one Esc reader
    /// (<see cref="CloseTopLayer"/>), never the editor. The status bar's right side names the keys of the place the pad is in.
    /// </summary>
    public sealed partial class StructureEditor
    {
        /// <summary>The stick's look per frame (the stick is already a per-frame delta).</summary>
        private const float PadLookScale = 2.6f;

        private bool _padViewport;
        private GameObject _reticle;
        private string _hintKey;

        /// <summary>True while the pad flies the build view (the cards are suspended).</summary>
        internal bool PadInBuildView => _padViewport;

        /// <summary>Menu swaps the pad between the cards and the build view — not while a picker, a panel or a dialog is open.</summary>
        private void UpdatePadFocus(bool pad)
        {
            if (_canvas == null)
            {
                return;
            }

            if (!pad)
            {
                UiNav.SetSuspended(_canvas.gameObject, false); // mouse in hand — the cards are always live
                return;
            }

            if (InputMap.Down(InputAction.UiMenu) && !_shell.IsDialogOpen && !LayerOpen())
            {
                _padViewport = !_padViewport;
            }

            UiNav.SetSuspended(_canvas.gameObject, _padViewport);
        }

        /// <summary>Whether one of the editor's own pickers or panels is open (the pad stays in the cards then).</summary>
        private bool LayerOpen()
            => _kitPanel != null || _markerPanel != null || _devicePanel != null || _functionPicker != null || _formPicker != null
               || _sourcePicker != null || _loadPicker != null;

        /// <summary>B in the build view (through <see cref="CloseTopLayer"/>): back to the cards.</summary>
        private bool LeavePadViewport()
        {
            if (!_padViewport)
            {
                return false;
            }

            _padViewport = false;
            if (_canvas != null)
            {
                UiNav.SetSuspended(_canvas.gameObject, false);
            }

            return true;
        }

        /// <summary>The right stick's look while the pad flies.</summary>
        private void PadLook()
        {
            _yaw += InputMap.PadLookX() * PadLookScale;
            _pitch = Mathf.Clamp(_pitch - InputMap.PadLookY() * PadLookScale, -89f, 89f);
            _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        /// <summary>The left stick and LB / RB as a flight direction.</summary>
        private Vector3 PadMove()
        {
            var move = (_cam.transform.forward * InputMap.PadStickY()) + (_cam.transform.right * InputMap.PadStickX());
            if (InputMap.PadHeld(PadButton.Rb))
            {
                move += Vector3.up;
            }

            if (InputMap.PadHeld(PadButton.Lb))
            {
                move += Vector3.down;
            }

            return move;
        }

        /// <summary>A uses the tool, X removes, Y turns the form, the d-pad steps the palette.</summary>
        private void PadActions()
        {
            if (InputMap.PadDown(PadButton.A))
            {
                UseTool();
            }
            else if (InputMap.PadDown(PadButton.X))
            {
                TryRemove();
            }

            if (InputMap.PadDown(PadButton.Y))
            {
                TurnBrush();
            }

            StepPalette(InputMap.PadDpadStep());
        }

        /// <summary>The tool's click: Place puts the picked entry, Remove takes the cell under the aim away.</summary>
        private void UseTool()
        {
            if (_removeTool)
            {
                TryRemove();
            }
            else
            {
                TryPlace();
            }
        }

        /// <summary>One repeat-gated d-pad step through the shown palette (a pad cannot scroll the list).</summary>
        private void StepPalette(float step)
        {
            if (Mathf.Abs(step) < 0.5f || _palList == null || _shown.Length == 0)
            {
                return;
            }

            int next = _palList.Selected + (step > 0f ? -1 : 1); // >0 = left = previous, matching the hotbar
            _palList.Select(Mathf.Clamp(next, 0, _shown.Length - 1));
        }

        /// <summary>The crosshair while the pad flies, and the keys of the place the pad (or the mouse) is in.</summary>
        private void RefreshHint(bool pad, bool padViewport)
        {
            if (_reticle != null && _reticle.activeSelf != padViewport)
            {
                _reticle.SetActive(padViewport);
            }

            string key = !pad ? "ui.struct.hint_mouse" : padViewport ? "ui.struct.hint_pad_build" : "ui.struct.hint_pad_panels";
            if (key != _hintKey && _shell != null)
            {
                _hintKey = key;
                _shell.SetHint(L(key));
            }
        }

        /// <summary>Where the picking ray starts: the mouse, or the crosshair at the screen centre while the pad flies.</summary>
        private Vector3 PickPoint() => _padViewport && InputMap.ActiveDevice == InputDeviceKind.Gamepad
            ? new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f)
            : Input.mousePosition;

        /// <summary>The pad's crosshair: two thin bars at the screen centre, hidden until the pad takes the build view.</summary>
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
