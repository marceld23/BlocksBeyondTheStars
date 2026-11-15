// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Station/Village editor on the <see cref="EditorShell"/> (#3692, epic #3696): the header (Back, the start card's title
    /// with the template's name, Undo / Redo, Load, New, Submit and the one primary Save with its subline), the two side cards
    /// and the status bar coloured by severity. The editor is the shell's <see cref="IEditorDocument"/>: its name, its unsaved
    /// state (cell edits through the undo history, form fields through a signature — StructureEditor.Undo.cs), its draft
    /// (StructureEditor.Draft.cs) and Esc / pad B, which closes the topmost picker, panel or the pad's build view and never the
    /// editor (<see cref="CloseTopLayer"/>).
    /// </summary>
    public sealed partial class StructureEditor : IEditorDocument
    {
        private EditorShell _shell;
        private Transform _leftCard, _rightCard, _leftContent, _rightContent;
        private string _status = string.Empty;
        private EditorStatusSeverity _statusSeverity;

        /// <summary>The draft folder; null = the shell's own (tests point it at a temp folder).</summary>
        internal string DraftRoot;

        /// <summary>The shell this editor runs in — for tests.</summary>
        internal EditorShell EditorShellForTest => _shell;

        /// <summary>The canvas, the shell, the two cards and their content; the draft question last.</summary>
        private void BuildUi()
        {
            _canvas = UiKit.CreateCanvas("Structure Editor UI");
            _canvas.sortingOrder = 5;
            _shell = EditorShell.Build(_canvas, this, new EditorShellOptions
            {
                EditorKey = ModeName,
                TitleKey = EditorMode == Mode.Station ? "ui.menu.station_editor" : "ui.menu.settlement_editor",
                SaveSublineKey = "ui.editor.save_sub." + ModeName,
                OnBack = () => Shell?.CloseStructureEditor(),
                OnSave = SaveFromShell,
                OnLoad = OpenLoadPicker,
                OnNew = NewDesign,
                OnSubmit = Submit,
                HasUndo = true,
                DraftRoot = DraftRoot,
                Localize = L,
            });
            _leftCard = _shell.AddSideCard(false, LeftCardW);
            _rightCard = _shell.AddSideCard(true, RightCardW);
            _leftContent = NewCardContent(_leftCard);
            _rightContent = NewCardContent(_rightCard);
            BuildCards();
            _shell.SetSubmitAvailable(SubmitFits);
            _shell.PreferFocus(_paletteCard?.PlaceButton); // the pad starts on the first tool, never on Back
            RefreshHint(InputMap.ActiveDevice == InputDeviceKind.Gamepad, false);
            BuildReticle(_shell.WorkArea);
            BuildViewHint();
            if (!ScreenshotDirector.CapturingEditors)
            {
                _shell.OfferDraft(); // "Continue your draft?" — after everything can take it
            }
        }

        private static Transform NewCardContent(Transform card) => UiKit.AddStretchChild(card, "CardContent").transform;

        /// <summary>The flying controls in one line at the foot of the 3D view, between the cards (the status bar's right side
        /// carries the placing keys).</summary>
        private void BuildViewHint()
        {
            float x = EditorShellLayout.Margin + LeftCardW + 16f;
            float w = EditorShellLayout.CanvasW - x - RightCardW - EditorShellLayout.Margin - 16f;
            float y = EditorShell.WorkRect.Y + EditorShell.WorkRect.H - 44f;
            var hint = UiKit.AddText(_shell.WorkArea, x, y, w, 36f, L("ui.struct.view_hint"), 16, UiKit.CyanDim, TextAnchor.MiddleCenter);
            UiKit.FitLabelLines(hint, 16, UiKit.EditorMinFontSize, UiKit.EditorMinFontSize);
            hint.raycastTarget = false;
        }

        /// <summary>Rebuilds both cards so the fields reflect the design (a load, a toggled mode). The canvas stays — the header,
        /// the status line, the draft timer and the pickers' parent; the placed cells live under the editor transform.</summary>
        private void RebuildUi()
        {
            _loadPicker?.Close(); // a load closes its picker
            _loadPicker = null;
            _kitPanel?.Close();
            _kitPanel = null;
            _formPicker?.Close();
            _formPicker = null;
            _sourcePicker?.Close(); // #3523
            _sourcePicker = null;
            CloseFunctionPicker();
            if (_shell == null)
            {
                return; // not built yet (a load before Start)
            }

            var focus = FocusPath();
            _leftContent = ReplaceContent(_leftCard, _leftContent);
            _rightContent = ReplaceContent(_rightCard, _rightContent);
            BuildCards();
            _shell.SetSubmitAvailable(SubmitFits);
            _shell.SetStatus(_status, _statusSeverity);
            RestoreFocus(focus);
        }

        /// <summary>Retires a card's content (hidden at once, so neither the pad nor a lookup finds it again) and gives a new one.</summary>
        private static Transform ReplaceContent(Transform card, Transform old)
        {
            if (old != null)
            {
                old.gameObject.SetActive(false);
                old.name = "CardContent (old)";
                if (Application.isPlaying)
                {
                    EditorSceneKit.Discard(old.gameObject);
                }
                else
                {
                    DestroyImmediate(old.gameObject);
                }
            }

            return NewCardContent(card);
        }

        /// <summary>The pad's focus as "card/index/index…", so a rebuild puts it back on the same row.</summary>
        private string FocusPath()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null)
            {
                return null;
            }

            var root = selected.transform.IsChildOf(_leftContent) ? _leftContent : selected.transform.IsChildOf(_rightContent) ? _rightContent : null;
            if (root == null)
            {
                return null;
            }

            var path = new List<string>();
            for (var t = selected.transform; t != root; t = t.parent)
            {
                path.Insert(0, t.GetSiblingIndex().ToString());
            }

            return (root == _leftContent ? "L/" : "R/") + string.Join("/", path);
        }

        private void RestoreFocus(string path)
        {
            if (string.IsNullOrEmpty(path) || EventSystem.current == null)
            {
                return;
            }

            var t = path[0] == 'L' ? _leftContent : _rightContent;
            foreach (string part in path.Substring(2).Split('/'))
            {
                if (!int.TryParse(part, out int i) || i >= t.childCount)
                {
                    return;
                }

                t = t.GetChild(i);
            }

            if (t.GetComponent<Selectable>() is { } control && control.IsInteractable())
            {
                EventSystem.current.SetSelectedGameObject(control.gameObject);
            }
        }

        /// <summary>A message in the status bar: light (info), green (saved), amber (a check's warning), red (refused).</summary>
        private void SetStatus(string s, EditorStatusSeverity severity = EditorStatusSeverity.Info)
        {
            _status = s ?? string.Empty;
            _statusSeverity = severity;
            _shell?.SetStatus(_status, severity);
        }

        /// <summary>Submit exists where the work can be submitted: a whole structure or a kit module — not a generator entry, a
        /// landmark or a giant ship (their files are no submission bundle yet).</summary>
        private bool SubmitFits => !_entryMode && !_landmarkMode && !_giantShipMode;

        /// <summary>Save (the header's primary button, Ctrl+S): the editor's own export; true when it wrote the files.</summary>
        private bool SaveFromShell()
        {
            _savedNow = false;
            Export();
            if (_savedNow)
            {
                MarkSaved(); // StructureEditor.Undo.cs
            }

            return _savedNow;
        }

        /// <summary>New (header): an empty room and the form as a fresh editor opens it. The shell has asked over unsaved work.</summary>
        private void NewDesign()
        {
            ResetForm();
            ApplyCells(new List<CellJson>(), new List<string>());
            NewDocument();
            EditorSceneKit.Frame(_cam.transform, ref _pitch, _yaw, _design.Keys, MaxW, MaxL);
            SetStatus(L("ui.struct.new_done"));
            RebuildUi();
        }

        /// <summary>Every form field back to a fresh editor's value.</summary>
        private void ResetForm()
        {
            DropPreview();
            _key = EditorMode == Mode.Station ? "my_station" : "my_settlement";
            _name = L(EditorMode == Mode.Station ? "ui.ed.default.station" : "ui.ed.default.settlement");
            _pack = "default";
            _weight = 1;
            _tier = 0;
            _role = _kit = _function = _style = _planetTypes = _basedOn = _loadedFrom = string.Empty;
            _moduleMode = _landmarkMode = _entryMode = _underground = false;
            _protection = LandmarkRules.ProtectionAuto;
            ResetLandmarkExtras();
            ResetGiantShipExtras();
            _entryExtras = null;
            _entrySourceInvalid = false;
            _zoneCorner = null;
        }

        // ---------------------------------------------------------------- IEditorDocument

        /// <summary>The template's name under the title.</summary>
        public string DocumentName => _name;

        /// <inheritdoc/>
        public bool CloseTopLayer()
        {
            if (_kitPanel != null && _kitPanel.CloseTopLayer())
            {
                return true; // the kit panel's module picker, then the panel itself
            }

            if (CloseFirst(ref _markerPanel) || CloseFirst(ref _devicePanel))
            {
                return true;
            }

            if (_functionPicker != null)
            {
                CloseFunctionPicker();
                return true;
            }

            if (ClosePicker(ref _formPicker) || ClosePicker(ref _sourcePicker) || ClosePicker(ref _loadPicker))
            {
                return true;
            }

            if (_zoneCorner != null)
            {
                _zoneCorner = null; // a zone tool click waiting for its second corner
                SyncZoneBoxes();
                return true;
            }

            return LeavePadViewport(); // StructureEditor.Pad.cs
        }

        private static bool CloseFirst(ref EditorMarkerPanel panel)
        {
            if (panel == null)
            {
                return false;
            }

            var p = panel;
            panel = null;
            p.Close();
            return true;
        }

        private static bool CloseFirst(ref EditorDevicePanel panel)
        {
            if (panel == null)
            {
                return false;
            }

            var p = panel;
            panel = null;
            p.Close();
            return true;
        }

        private static bool ClosePicker(ref EditorFormPicker picker)
        {
            if (picker == null)
            {
                return false;
            }

            var p = picker;
            picker = null;
            p.Close();
            return true;
        }

        private static bool ClosePicker(ref EditorLoadPicker picker)
        {
            if (picker == null)
            {
                return false;
            }

            var p = picker;
            picker = null;
            p.Close();
            return true;
        }
    }
}
