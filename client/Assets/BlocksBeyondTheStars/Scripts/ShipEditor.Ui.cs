// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The ship editor on the <see cref="EditorShell"/> (#3691), as the approved mock's "Ship" view draws it: the shell's
    /// header (Back, "Ship Editor" with the ship's name and the unsaved mark, Undo / Redo, Load, New, Submit, the primary Save
    /// "for your new worlds") and status bar; the palette card on the left (search, <b>Place | Remove</b>, tiles of at least
    /// 52 px with 16 px names); the ship card on the right (name, description, values, interior, the selected part with its
    /// form and turn, the placed count, and — closed at first, remembered — "Advanced": key, blueprint, dye / glow colours and
    /// the craft cost). The 3D view between the cards stays free.
    /// </summary>
    public sealed partial class ShipEditor
    {
        /// <summary>The widths of the palette card (left) and the ship card (right).</summary>
        internal const float PaletteCardW = 400f, FormCardW = 380f;

        /// <summary>A form row's usable width inside the ship card's scroll list (364 wide, 4 + 12 padding, a margin).</summary>
        internal const float RowW = 340f;

        /// <summary>Inputs, steppers and buttons of the ship card: a tap target.</summary>
        internal const float RowH = 44f;

        /// <summary>The label column of a stepper row (#3263) — −, the value and + take the rest of the row.</summary>
        internal const float StepperLabelW = 176f;

        /// <summary>The ship card's text sizes: 18 where it fits, never under 16.</summary>
        private const int FormText = 16, FormTextMax = 18;

        private Canvas _canvas;
        private EditorShell _shell;
        private PaletteCard _paletteCard;
        private RectTransform _form;
        private Text _placedLabel;
        private int _lastPlaced = -1;
        private readonly List<CostUi> _costPool = new();
        private readonly List<GameObject> _advancedRows = new();
        private bool _advancedOpen, _buildingAdvanced;
        private Button _advancedButton;
        private Image _partIcon;
        private Text _partName;
        private RawImage _formIcon;
        private Text _formLabel;
        private Text _orientValue;
        private InputField _keyInput;
        private EditorFormPicker _formPicker;

        private sealed class CostUi
        {
            public GameObject Go;
            public InputField Item;
            public InputField Count;
            public CostRow Bound;
        }

        /// <summary>The editor's shell (header, status bar, drafts) — for tests and the screenshot run.</summary>
        public EditorShell EditorFrame => _shell;

        /// <summary>The tool a click into the 3D view uses (the palette card's switch).</summary>
        private EditorBuildTool Tool => _paletteCard != null ? _paletteCard.Tool : EditorBuildTool.Place;

        private void BuildUi()
        {
            _canvas = UiKit.CreateCanvas("Ship Editor UI");
            _canvas.sortingOrder = 5;
            _shell = EditorShell.Build(_canvas, this, new EditorShellOptions
            {
                EditorKey = "ship",
                TitleKey = "ui.menu.ship_editor",
                SaveSublineKey = "ui.editor.save_sub.ship",
                OnBack = () => Shell?.CloseShipEditor(),
                OnSave = Export,
                OnLoad = OpenLoadPicker,
                OnNew = NewDesign,
                OnSubmit = Submit,
                HasUndo = true,
                DraftRoot = DraftRoot,
                Localize = L,
            });

            _advancedOpen = EditorSceneKit.RememberedFlag(EditorSceneKit.FlagKey("ship", "advanced"), false);
            BuildPaletteCard();
            BuildFormCard();
            BuildReticle(_shell.WorkArea);
            _shell.PreferFocus(_paletteCard.PlaceButton); // the pad starts on the first tool, never on Back
            RefreshPadChrome(InputMap.ActiveDevice == InputDeviceKind.Gamepad, false);
            if (!ScreenshotDirector.CapturingEditors)
            {
                _shell.OfferDraft(); // last: "Continue your draft?" when the last session left unsaved work
            }
        }

        /// <summary>The left card: kicker, search, Place | Remove and the palette tiles (<see cref="EditorPaletteKit.AddPaletteCard"/>).</summary>
        private void BuildPaletteCard()
        {
            var rect = EditorShellLayout.SideCard(false, PaletteCardW);
            var card = _shell.AddSideCard(false, PaletteCardW);
            _paletteCard = EditorPaletteKit.AddPaletteCard(Shell, card, rect.W, rect.H, new PaletteCardSpec
            {
                Title = L("ui.ship.palette"),
                Palette = _palette,
                Selected = _selected,
                Search = _search,
                OnSelected = OnPaletteSelected,
                OnSearchChanged = v => _search = v ?? string.Empty,
            });
        }

        /// <summary>The right card: the ship's kicker over the scrolling form.</summary>
        private void BuildFormCard()
        {
            var rect = EditorShellLayout.SideCard(true, FormCardW);
            var card = _shell.AddSideCard(true, FormCardW);
            UiKit.AddKicker(card, 16f, 14f, FormCardW - 32f, L("ui.ship.title"));
            _form = UiKit.ScrollList(card, 8f, 46f, FormCardW - 16f, rect.H - 54f, 4f);
            BuildForm();
        }

        private void BuildForm()
        {
            BuildIdentity();
            BuildValues();
            BuildSelectedPart();
            BuildAdvanced();
        }

        /// <summary>Name and description — what the mock's ship card opens with.</summary>
        private void BuildIdentity()
        {
            FormLabel(L("ui.struct.name"));
            InputRow(_shipName, OnNameChanged, RowH);
            FormLabel(L("ui.ship.desc"));
            InputRow(_desc, v =>
            {
                _desc = v;
                MetaChanged();
            }, 80f, multiline: true);
        }

        /// <summary>The ship's values and its interior (cabin) frame: the volume the server walks, floors and roofs (#1396).
        /// The cyan box in the room follows the interior steppers; exterior cells may sit anywhere outside it.</summary>
        private void BuildValues()
        {
            FormHeader(L("ui.ship.stats"));
            Stepper(L("ui.ship.hull"), () => _hull, v => _hull = v, 20f, 500f, 10f, "0", meta: true);
            Stepper(L("ui.ship.shield"), () => _shield, v => _shield = v, 0f, 300f, 10f, "0", meta: true);
            Stepper(L("ui.ship.speed"), () => _flightSpeed, v => _flightSpeed = v, 0.4f, 2.5f, 0.05f, "0.00", meta: true);
            Stepper(L("ui.ship.handling"), () => _handling, v => _handling = v, 0.4f, 2.5f, 0.05f, "0.00", meta: true);
            Stepper(L("ui.ship.cargo"), () => _cargo, v => _cargo = Mathf.RoundToInt(v), 12f, 240f, 4f, "0", meta: true);

            FormHeader(L("ui.ship.interior"));
            Stepper(L("ui.ship.int_w"), () => _intW, v => { _intW = Mathf.RoundToInt(v); RebuildFrame(); }, 3f, MaxW - Origin.X, 1f, "0", meta: true);
            Stepper(L("ui.ship.int_l"), () => _intL, v => { _intL = Mathf.RoundToInt(v); RebuildFrame(); }, 3f, MaxL - Origin.Z, 1f, "0", meta: true);
            Stepper(L("ui.ship.int_h"), () => _intH, v => { _intH = Mathf.RoundToInt(v); RebuildFrame(); }, 2f, MaxH - 1, 1f, "0", meta: true);
            WrappedFormLabel(L("ui.ship.interior_hint"));
        }

        /// <summary>The selected part (the mock's last section): its icon and name, the form it is placed in, its turn (R /
        /// pad Y), and how many cells the ship has.</summary>
        private void BuildSelectedPart()
        {
            FormHeader(L("ui.ship.selected_part"));
            var row = Row(_form, PaletteListUi.TileTallH);
            var entry = SelectedEntry();
            _partIcon = PaletteListUi.AddEntryIcon(row, 4f, (PaletteListUi.TileTallH - PaletteListUi.TileIcon) / 2f, PaletteListUi.TileIcon, entry);
            _partName = UiKit.AddText(row, 50f, 2f, RowW - 46f, PaletteListUi.TileTallH - 4f, entry.Label ?? string.Empty, FormTextMax, UiKit.TextCol);
            UiKit.FitLabelLines(_partName, FormTextMax, FormText, FormText, 3);
            FormRow();
            _orientValue = Stepper(L("ui.ship.turn_r"), () => _brushOrient, v => SetOrient(Mathf.RoundToInt(v)), 0f, 3f, 1f, "0",
                v => (Mathf.RoundToInt(v) * 90) + "°");
            _lastPlaced = _design.Count;
            _placedLabel = FormLabel(string.Format(L("ui.ed.placed"), _design.Count));
        }

        /// <summary>"Advanced" (closed at first, remembered): the key, the blueprint, the brush's dye and glow colours as hex
        /// and the craft cost — what a developer needs and a child does not. The cost is the last section, so its rows can
        /// simply append to the form.</summary>
        private void BuildAdvanced()
        {
            var toggleRow = Row(_form, RowH + 10f);
            _advancedButton = UiKit.AddTierButton(toggleRow, 4f, 6f, RowW, RowH, AdvancedLabel(), ToggleAdvanced, ButtonTier.Quiet);
            _advancedRows.Clear();
            _buildingAdvanced = true;
            FormLabel(L("ui.struct.key"));
            _keyInput = InputRow(_key, v =>
            {
                _key = v;
                _keyEdited = true;
                MetaChanged();
            }, RowH);
            FormLabel(L("ui.ship.blueprint"));
            InputRow(_requiredBlueprint, v =>
            {
                _requiredBlueprint = v;
                MetaChanged();
            }, RowH);
            FormLabel(L("ui.ship.dye_hex"));
            InputRow(HexOf(_brushTint), v => _brushTint = ParseHex(v), RowH);
            FormLabel(L("ui.ship.glow_hex"));
            InputRow(HexOf(_brushGlow), v => _brushGlow = ParseHex(v), RowH);
            BuildCostHeader();
            _buildingAdvanced = false;
            ShowAdvanced(_advancedOpen); // also lays out the cost rows (they follow the section's state)
        }

        /// <summary>The craft cost's header over the whole row ("ВАРТІСТЬ РЕМОНТУ/СТВОРЕННЯ" needs it), "+ add" under it, the
        /// cost rows after that.</summary>
        private void BuildCostHeader()
        {
            FormHeader(L("ui.ship.cost"));
            var addRow = Row(_form, RowH + 4f);
            UiKit.AddFitButton(addRow, 4f, 2f, 160f, RowH, L("ui.ship.cost_add"), () =>
            {
                _craftCost.Add(new CostRow { Item = "iron_plate", Count = 1 });
                MetaChanged();
                RefreshCostRows();
            });
        }

        private string AdvancedLabel() => (_advancedOpen ? "▾ " : "▸ ") + L("ui.ship.advanced");

        private void ToggleAdvanced() => ShowAdvanced(!_advancedOpen, remember: true);

        /// <summary>Opens or closes "Advanced" (Save opens it when the key is missing).</summary>
        private void ShowAdvanced(bool open, bool remember = false)
        {
            _advancedOpen = open;
            if (remember)
            {
                EditorSceneKit.RememberFlag(EditorSceneKit.FlagKey("ship", "advanced"), open);
            }

            foreach (var row in _advancedRows)
            {
                if (row != null)
                {
                    row.SetActive(open);
                }
            }

            if (_advancedButton != null)
            {
                _advancedButton.GetComponentInChildren<Text>(true).text = AdvancedLabel();
            }

            RefreshCostRows();
        }

        /// <summary>The name follows into the key while the key was never typed or loaded (a new design).</summary>
        private void OnNameChanged(string v)
        {
            _shipName = v;
            if (!_keyEdited)
            {
                _key = Slug(v);
                _keyInput?.SetTextWithoutNotify(_key);
            }

            MetaChanged();
        }

        private EditorPaletteKit.Entry SelectedEntry()
            => _palette != null && _selected >= 0 && _selected < _palette.Length ? _palette[_selected] : default;

        private void OnPaletteSelected(int index)
        {
            _selected = index;
            RefreshSelectedPart();
        }

        /// <summary>The selected part's icon, name and form icon follow the palette selection.</summary>
        private void RefreshSelectedPart()
        {
            var entry = SelectedEntry();
            PaletteListUi.ShowEntryIcon(_partIcon, entry);
            if (_partName != null)
            {
                _partName.text = entry.Label ?? string.Empty;
            }

            EditorFormPicker.PaintIcon(_formIcon, _atlas, BrushTile(), _brushShape);
        }

        /// <summary>Turns the brush (R, pad Y, the stepper) and shows the new turn in the stepper.</summary>
        private void SetOrient(int orient)
        {
            _brushOrient = orient & 3;
            if (_orientValue != null)
            {
                _orientValue.text = (_brushOrient * 90) + "°";
            }
        }

        private void RefreshPlacedCount()
        {
            if (_placedLabel != null && _lastPlaced != _design.Count)
            {
                _lastPlaced = _design.Count;
                _placedLabel.text = string.Format(L("ui.ed.placed"), _design.Count);
            }
        }

        /// <summary>The brush's form: a label row, then its icon, its name and "Change…", which opens the in-game form grid (#1975).</summary>
        private void FormRow()
        {
            FormLabel(L("ui.struct.shape"));
            var row = Row(_form, RowH + 4f);
            var iconGo = new GameObject("FormIcon", typeof(RectTransform));
            iconGo.transform.SetParent(row, false);
            UiKit.Place(iconGo, 4f, 4f, 36f, 36f);
            _formIcon = iconGo.AddComponent<RawImage>();
            _formIcon.raycastTarget = false;
            EditorFormPicker.PaintIcon(_formIcon, _atlas, BrushTile(), _brushShape);
            _formLabel = UiKit.AddText(row, 48f, 0f, 164f, RowH + 4f, FormName(_brushShape), FormTextMax, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelLines(_formLabel, FormTextMax, FormText, FormText);
            UiKit.AddFitButton(row, RowW - 122f, 2f, 122f, RowH, L("ui.ed.form_pick"), OpenFormPicker); // "Изменить…" at 16 px
        }

        private void OpenFormPicker()
        {
            _formPicker?.Close();
            _formPicker = EditorFormPicker.Show(Shell, _canvas.transform, _atlas, BrushTile(), _brushShape, shape =>
            {
                _brushShape = shape;
                if (_formLabel != null)
                {
                    _formLabel.text = FormName(shape);
                }

                EditorFormPicker.PaintIcon(_formIcon, _atlas, BrushTile(), shape);
            }, () => _formPicker = null);
        }

        private static int ParseHex(string s) => EditorSceneKit.ParseHex(s);

        private static string HexOf(int rgb) => EditorSceneKit.HexOf(rgb);

        private void RefreshCostRows()
        {
            int i = 0;
            foreach (var c in _craftCost)
            {
                var ui = i < _costPool.Count ? _costPool[i] : MakeCostRow();
                ui.Bound = null; // suppress notify while we set the displayed text
                ui.Item.SetTextWithoutNotify(c.Item);
                ui.Count.SetTextWithoutNotify(c.Count.ToString());
                ui.Bound = c;
                ui.Go.SetActive(_advancedOpen);
                i++;
            }

            for (; i < _costPool.Count; i++)
            {
                _costPool[i].Go.SetActive(false);
            }
        }

        private CostUi MakeCostRow()
        {
            var row = Row(_form, RowH + 4f);
            var ui = new CostUi { Go = row.gameObject };
            ui.Item = UiKit.AddInput(row, 4f, 2f, 190f, RowH, string.Empty, null, L("ui.ship.cost_item"), 0, FormText);
            ui.Count = UiKit.AddInput(row, 200f, 2f, 84f, RowH, string.Empty, null, string.Empty, 0, FormTextMax);
            ui.Count.contentType = InputField.ContentType.IntegerNumber;
            UiKit.AddFitButton(row, 292f, 2f, 48f, RowH, "×", () =>
            {
                if (ui.Bound != null)
                {
                    _craftCost.Remove(ui.Bound); // a line of the unsaved form, not saved work: no question
                    MetaChanged();
                    RefreshCostRows();
                }
            });

            ui.Item.onValueChanged.AddListener(v =>
            {
                if (ui.Bound != null)
                {
                    ui.Bound.Item = v;
                    MetaChanged();
                }
            });
            ui.Count.onValueChanged.AddListener(v =>
            {
                if (ui.Bound != null && int.TryParse(v, out var c))
                {
                    ui.Bound.Count = Mathf.Max(0, c);
                    MetaChanged();
                }
            });
            _costPool.Add(ui);
            return ui;
        }

        /// <summary>Rebuilds the ship card (name, values, …) so it reflects a freshly loaded design.</summary>
        private void RebuildForm()
        {
            _formPicker?.Close();
            _formPicker = null;
            if (_form == null)
            {
                return;
            }

            for (int i = _form.childCount - 1; i >= 0; i--)
            {
                EditorSceneKit.Discard(_form.GetChild(i).gameObject);
            }

            _costPool.Clear(); // its rows went with the form — reusing them made the cost rows vanish after a load
            _advancedRows.Clear();
            BuildForm();
        }

        // --- small uGUI form builders ---

        /// <summary>A form row of <paramref name="height"/>. Under <see cref="UiKit.ScrollList"/> the row's own rect height is
        /// what counts (its layout group does not control heights, #1386) — without it every row was 100 px tall. Rows built
        /// while "Advanced" is being built belong to it.</summary>
        private RectTransform Row(Transform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(0f, height);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            if (_buildingAdvanced)
            {
                _advancedRows.Add(go);
            }

            return rt;
        }

        /// <summary>A field label at 16 px in a row as tall as its wrapped text (a long label takes a second line).</summary>
        private Text FormLabel(string text)
        {
            float h = Mathf.Max(24f, Mathf.Ceil(UiKit.MeasureHeight(text, RowW, FormText, FontStyle.Normal, false)) + 4f);
            var row = Row(_form, h);
            var label = UiKit.AddText(row, 4f, 0f, RowW, h, text, FormText, UiKit.TextCol, TextAnchor.LowerLeft);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }

        /// <summary>A form row as tall as its wrapped text at 16 px (<see cref="UiKit.MeasureHeight"/>) — the interior hint
        /// was one clipped 12 px line, cut in every language (#3263).</summary>
        private void WrappedFormLabel(string text)
        {
            float h = Mathf.Ceil(UiKit.MeasureHeight(text, RowW, FormText, FontStyle.Normal, false)) + 4f;
            var row = Row(_form, h);
            var label = UiKit.AddText(row, 4f, 2f, RowW, h - 4f, text, FormText, UiKit.CyanDim, TextAnchor.UpperLeft);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private void FormHeader(string text)
        {
            Row(_form, 10f); // spacer
            var row = Row(_form, 28f);
            var label = UiKit.AddText(row, 4f, 0f, RowW, 28f, text, FormText, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(label, RowW, FormText, FormText);
        }

        private InputField InputRow(string value, Action<string> onChange, float height, bool multiline = false)
        {
            var row = Row(_form, height + 4f);
            return UiKit.AddInput(row, 4f, 2f, RowW, height, value, onChange, string.Empty, 0, FormTextMax, multiline);
        }

        /// <summary>A − value + row, 44 px high: the label (18 px, down to 16 on two lines), − and + as 44 px buttons and the
        /// value as the pad's ONE stop between them (Left / Right step it, <see cref="UiKit.AddStepperValue"/>). A
        /// <paramref name="meta"/> stepper changes the ship (unsaved), the turn only the brush. Returns the value text.</summary>
        private Text Stepper(string label, Func<float> get, Action<float> set, float min, float max, float step, string fmt,
            Func<float, string> display = null, bool meta = false)
        {
            string Show() => display != null ? display(get()) : get().ToString(fmt);
            var row = Row(_form, RowH);
            var text = UiKit.AddText(row, 4f, 0f, StepperLabelW, RowH, label, FormTextMax, UiKit.TextCol);
            UiKit.FitLabelLines(text, FormTextMax, FormText, FormText);
            Text value = null;
            void Step(int dir)
            {
                set(Mathf.Clamp(get() + dir * step, min, max));
                value.text = Show();
                if (meta)
                {
                    MetaChanged();
                }
            }

            var minus = UiKit.AddFitButton(row, StepperLabelW + 8f, 0f, RowH, RowH, "−", () => Step(-1));
            UiKit.AddStepperValue(row, StepperLabelW + 56f, 0f, RowW - StepperLabelW - 56f - RowH - 4f, RowH, Step);
            value = UiKit.AddText(row, StepperLabelW + 56f, 0f, RowW - StepperLabelW - 56f - RowH - 4f, RowH, Show(), FormTextMax,
                UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            value.raycastTarget = false;
            var plus = UiKit.AddFitButton(row, RowW - RowH, 0f, RowH, RowH, "+", () => Step(+1));
            UiKit.NoPadStop(minus);
            UiKit.NoPadStop(plus);
            return value;
        }
    }
}
