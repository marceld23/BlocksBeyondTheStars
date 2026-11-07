// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.HudFrames;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The HUD mode of the character editor (#3318): a "Character | HUD frame" switch beside the part tabs turns the same
    /// editor — palette, colour wheel, fill, eyedropper, undo/redo — onto the player's HUD frame (#3317). Its four tiles
    /// (corner, top edge, side edge, bottom edge) are the four regions of ONE canvas, the tiles column beside it picks the
    /// one to paint, exactly like the faces of a body part. In place of the figure, its base colours and the outfits it
    /// shows a little 16:9 screen with a hinted HUD and the live frame, the frame's switches (on/off, size, opacity) and
    /// its own library column: the player's saved frames (save, rename, delete, at most eight) and the templates on offer
    /// (free ones everywhere, a calendar one only in a world that unlocked it).
    /// <para>
    /// Each mode keeps its own subjects and its own undo history; switching commits the canvas being left (like switching
    /// a part tab does) and parks the other mode untouched, so both histories survive any number of switches. The frame is
    /// the player's own client setting in both hosts: committing it writes <see cref="ClientSettings.HudFrameDesign"/>.
    /// </para>
    /// </summary>
    public sealed partial class FaceEditor
    {
        /// <summary>The HUD mode's host (#3318). Null = no HUD mode (the block-paint tool).</summary>
        public HudFrameEditorHost HudHost;

        /// <summary>One mode's editing state, parked while the other mode is on the canvas.</summary>
        private sealed class ModeState
        {
            public Subject[] Subjects;
            public int Active;
            public PixelEditHistory History;
            public int Region;
        }

        /// <summary>What shows through an unpainted frame pixel: a dim view of the world.</summary>
        private static readonly Color HudCanvasBackdrop = new Color(0.16f, 0.21f, 0.28f, 1f);

        private static readonly string[] HudTileKeys =
        {
            "ui.hudframe.tile.corner", "ui.hudframe.tile.top", "ui.hudframe.tile.side", "ui.hudframe.tile.bottom",
        };

        private static readonly string[] HudSizeKeys = { "ui.hudframe.size_small", "ui.hudframe.size_medium", "ui.hudframe.size_large" };
        private static readonly float[] HudOpacitySteps = { 0.6f, 0.8f, 1f };

        // Right of the region tiles: the mode switch on the tab row, the preview screen and the switches under the wheel.
        private const float ModeButtonsX = 904f, ModeButtonW = 168f, HudColumnX = 904f, HudPreviewW = 320f, HudPreviewH = 180f;
        private const float HudSettingsY = 510f, HudChoiceW = 104f, HudChoicePitch = 108f;

        private bool _hudMode;
        private ModeState _parked;
        private RectTransform _charLayer, _hudLayer;
        private Image _charModeButton, _hudModeButton;
        private Text _charModeLabel, _hudModeLabel;
        private HudFrameDesign _hudBase = HudFrameDesign.Empty; // the twinkle of the design on the canvas
        private string _hudTemplateKey = string.Empty;          // the template the canvas started from (a submission's basedOn)
        private Shelf _hudShelf;
        private HudFrameView _hudPreview;
        private Text _onOffLabel;
        private readonly Image[] _sizeButtons = new Image[3];
        private readonly Text[] _sizeLabels = new Text[3];
        private readonly Image[] _opacityButtons = new Image[3];
        private readonly Text[] _opacityLabels = new Text[3];

        /// <summary>The character mode's own widgets (part tabs, base colours, the figure, the outfits) live in one layer
        /// the HUD mode hides; without a HUD host they go straight onto the panel, as before.</summary>
        private Transform CharacterLayer(Transform panel)
        {
            if (HudHost == null)
            {
                return panel;
            }

            if (_charLayer == null)
            {
                _charLayer = AddLayer(panel, "CharacterLayer");
            }

            return _charLayer;
        }

        /// <summary>A layer covering the whole panel, so its children use the panel's own coordinates.</summary>
        private static RectTransform AddLayer(Transform panel, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>What BuildUi adds after the shared buttons: the Submit button (#3319) and — on the appearance screen of a
        /// host with a HUD host — the mode switch and the HUD mode's layer.</summary>
        private void BuildModeExtras(Transform panel, float panelW, float buttonsY)
        {
            BuildSubmitButton(panel, buttonsY);
            if (HudHost == null || _subjects.Count < 2)
            {
                return;
            }

            var charButton = UiKit.AddFitButton(panel, ModeButtonsX, 50f, ModeButtonW, 36f, L("ui.hudframe.mode_character"), EnterCharacterMode);
            _charModeButton = charButton.image;
            _charModeLabel = charButton.GetComponentInChildren<Text>();
            var hudButton = UiKit.AddFitButton(panel, ModeButtonsX + ModeButtonW + 8f, 50f, ModeButtonW, 36f, L("ui.hudframe.mode_hud"), EnterHudMode);
            _hudModeButton = hudButton.image;
            _hudModeLabel = hudButton.GetComponentInChildren<Text>();

            _hudLayer = AddLayer(panel, "HudLayer");
            BuildHudPreview(_hudLayer);
            BuildHudSettings(_hudLayer);
            _hudShelf = HudShelf();
            BuildShelf(_hudLayer, panelW - 250f, _hudShelf);
            ShowModeLayers();
        }

        private void EnterHudMode() => SwitchMode(true);

        private void EnterCharacterMode() => SwitchMode(false);

        /// <summary>Commits the canvas of the mode being left, parks that mode (subjects, active part, region, history) and
        /// brings the other one back exactly as it was left.</summary>
        private void SwitchMode(bool hud)
        {
            if (hud == _hudMode || HudHost == null || _hudLayer == null)
            {
                return;
            }

            EndStroke();
            CommitActive();
            var leaving = new ModeState { Subjects = _subjects.ToArray(), Active = _active, History = _history, Region = _activeRegion };
            var entering = _parked ?? new ModeState { Subjects = new[] { HudSubject() }, Active = 0, History = new PixelEditHistory(), Region = 0 };
            _parked = leaving;
            _subjects.Clear();
            _subjects.AddRange(entering.Subjects);
            _history = entering.History;
            _hudMode = hud;

            LoadSubject(entering.Active);
            BuildSubjectArea(24f, 100f);
            if (RegionMode)
            {
                SetActiveRegion(entering.Region);
            }

            ShowModeLayers();
            _title.text = L(Current.TitleKey ?? Current.LabelKey);
            SetHint(HintKeyNow());
            HighlightTabs();
            UpdateBaseSwatch();
            RefreshHistoryButtons();
            RefreshPreview();
            RefreshHudControls();
        }

        /// <summary>The HUD frame as one subject: the four tiles side by side, each a 32 × 32 region.</summary>
        private Subject HudSubject() => new Subject
        {
            LabelKey = "ui.hudframe.mode_hud",
            TitleKey = "ui.hudframe.title",
            HintKey = "ui.hudframe.hint",
            GridW = HudFrameDesign.EditorGridW,
            GridH = HudFrameDesign.EditorGridH,
            Pixels = HudHost.Design,
            Decode = payload =>
            {
                _hudBase = HudFrameDesign.Parse(payload); // the twinkle rides along with whatever is loaded
                return _hudBase.ToEditorGrid();
            },
            Encode = grid => _hudBase.WithEditorGrid(grid).Encode(),
            ColumnLabelKeys = HudTileKeys,
            OnApply = payload => HudHost.Design = payload,
            GetBaseColor = () => HudCanvasBackdrop,
            Fetch = () => HudHost.Design,
        };

        private void ShowModeLayers()
        {
            if (_charLayer != null)
            {
                _charLayer.gameObject.SetActive(!_hudMode);
            }

            if (_hudLayer != null)
            {
                _hudLayer.gameObject.SetActive(_hudMode);
            }

            if (_preview != null)
            {
                _preview.SetActive(!_hudMode); // the figure's camera rests while it cannot be seen
            }

            Arm(_charModeButton, _charModeLabel, !_hudMode);
            Arm(_hudModeButton, _hudModeLabel, _hudMode);
        }

        // ── the HUD library column ───────────────────────────────────────────────────────────────

        private Shelf HudShelf() => new Shelf
        {
            ListName = "HudFrameList",
            TitleKey = "ui.hudframe.library",
            NameKey = "ui.hudframe.name",
            SaveKey = "ui.hudframe.save",
            RenameKey = "ui.avatar.outfit_rename",
            EmptyKey = "ui.hudframe.none",
            TemplatesKey = "ui.hudframe.templates",
            Names = HudHost.DesignNames,
            OnSave = SaveHudDesign,
            OnWear = WearHudDesign,
            OnDelete = HudHost.DeleteDesign,
            OnRename = HudHost.RenameDesign,
            TemplateNames = () => AppearanceTemplates.Names(HudHost.OfferedTemplates(), t => t.NameKey, L),
            OnWearTemplate = WearHudTemplate,
        };

        /// <summary>Saves the canvas as it is now (committed first) under the typed name.</summary>
        private string SaveHudDesign(string name)
        {
            CommitActive();
            return HudFrameDesign.Parse(HudHost.Design).IsEmpty ? L("ui.hudframe.empty") : HudHost.SaveDesign(name, HudHost.Design);
        }

        private string WearHudDesign(int index)
        {
            var entry = HudHost.DesignAt(index);
            if (entry == null)
            {
                return string.Empty;
            }

            PutOnHudDesign(entry.Design);
            _hudTemplateKey = string.Empty;
            return L("ui.hudframe.worn").Replace("{name}", entry.Name ?? string.Empty);
        }

        /// <summary>Copies a template into the editable frame — from then on it is the player's own.</summary>
        private string WearHudTemplate(int index)
        {
            var templates = HudHost.OfferedTemplates();
            if (index < 0 || index >= templates.Count || !HudFrameDesign.TryFromTemplate(templates[index], out var design))
            {
                return string.Empty;
            }

            PutOnHudDesign(design.Encode());
            _hudTemplateKey = templates[index].Key;
            return L("ui.hudframe.template_worn").Replace("{name}", L(templates[index].NameKey));
        }

        /// <summary>Loads a design onto the canvas as ONE undoable step and wears it at once.</summary>
        private void PutOnHudDesign(string payload)
        {
            if (!_hudMode)
            {
                return;
            }

            BeginGridEdit();
            LoadFrom(payload ?? string.Empty);
            _dirty = true;
            CommitGridEdit();
            CommitActive();
            RefreshPreview();
        }

        // ── preview screen and switches ──────────────────────────────────────────────────────────

        /// <summary>A little 16:9 screen: sky, ground, the live frame and — over it, as in the game — a hint of the HUD
        /// (location, compass, vitals, hotbar, crosshair), so the player sees what the frame leaves free.</summary>
        private void BuildHudPreview(Transform layer)
        {
            var box = AddArea(layer, "HudPreview", HudColumnX, 100f, HudPreviewW, HudPreviewH);
            UiKit.AddImage(box, 0f, 0f, HudPreviewW, HudPreviewH, UiKit.SolidSprite, new Color(0.10f, 0.16f, 0.30f, 1f));
            UiKit.AddImage(box, 0f, HudPreviewH * 0.62f, HudPreviewW, HudPreviewH * 0.38f, UiKit.SolidSprite, new Color(0.13f, 0.24f, 0.16f, 1f));
            _hudPreview = HudFrameView.Create(box, "PreviewFrame", FilterMode.Bilinear);

            var hint = new Color(UiKit.Cyan.r, UiKit.Cyan.g, UiKit.Cyan.b, 0.6f);
            UiKit.AddImage(box, 4f, 4f, 46f, 9f, UiKit.SolidSprite, hint);                           // location
            UiKit.AddImage(box, 110f, 5f, 100f, 5f, UiKit.SolidSprite, hint);                        // compass
            UiKit.AddImage(box, 158f, 88f, 4f, 4f, UiKit.SolidSprite, Color.white);                  // crosshair
            UiKit.AddImage(box, 8f, 158f, 56f, 4f, UiKit.SolidSprite, new Color(0.35f, 0.95f, 0.55f, 0.8f)); // vitals
            UiKit.AddImage(box, 8f, 165f, 56f, 4f, UiKit.SolidSprite, hint);
            const float slot = 11f, gap = 2f;
            float hotbarX = (HudPreviewW - (9f * slot) - (8f * gap)) / 2f;
            for (int i = 0; i < 9; i++)
            {
                UiKit.AddImage(box, hotbarX + (i * (slot + gap)), HudPreviewH - slot - 5f, slot, slot, UiKit.SolidSprite, hint); // hotbar
            }
        }

        /// <summary>Frame on/off, its size and its opacity — they apply at once and are saved.</summary>
        private void BuildHudSettings(Transform layer)
        {
            float y = HudSettingsY;
            var onOff = UiKit.AddFitButton(layer, HudColumnX, y, HudPreviewW, 40f, L("ui.hudframe.on"), ToggleFrameOn);
            _onOffLabel = onOff.GetComponentInChildren<Text>();

            UiKit.AddText(layer, HudColumnX, y + 50f, HudPreviewW, 22f, L("ui.hudframe.size"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            for (int i = 0; i < HudSizeKeys.Length; i++)
            {
                int level = i;
                var button = UiKit.AddFitButton(layer, HudColumnX + (i * HudChoicePitch), y + 76f, HudChoiceW, 40f, L(HudSizeKeys[i]), () => SetFrameSize(level));
                _sizeButtons[i] = button.image;
                _sizeLabels[i] = button.GetComponentInChildren<Text>();
            }

            UiKit.AddText(layer, HudColumnX, y + 126f, HudPreviewW, 22f, L("ui.hudframe.opacity"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            for (int i = 0; i < HudOpacitySteps.Length; i++)
            {
                float opacity = HudOpacitySteps[i];
                string label = Mathf.RoundToInt(opacity * 100f) + " %"; // a number, the same in every language
                var button = UiKit.AddFitButton(layer, HudColumnX + (i * HudChoicePitch), y + 152f, HudChoiceW, 40f, label, () => SetFrameOpacity(opacity));
                _opacityButtons[i] = button.image;
                _opacityLabels[i] = button.GetComponentInChildren<Text>();
            }
        }

        private void ToggleFrameOn()
        {
            if (HudHost?.Settings == null)
            {
                return;
            }

            HudHost.Settings.HudFrameOn = !HudHost.Settings.HudFrameOn;
            HudHost.Settings.Save();
            RefreshHudControls();
        }

        private void SetFrameSize(int level)
        {
            if (HudHost?.Settings == null)
            {
                return;
            }

            HudHost.Settings.HudFrameSize = Mathf.Clamp(level, 0, 2);
            HudHost.Settings.Save();
            RefreshHudControls();
        }

        private void SetFrameOpacity(float opacity)
        {
            if (HudHost?.Settings == null)
            {
                return;
            }

            HudHost.Settings.HudFrameOpacity = Mathf.Clamp(opacity, ClientSettings.MinHudFrameOpacity, 1f);
            HudHost.Settings.Save();
            RefreshHudControls();
        }

        /// <summary>Lights the chosen switches and redraws the preview.</summary>
        private void RefreshHudControls()
        {
            var settings = HudHost?.Settings;
            if (settings == null || _hudLayer == null)
            {
                return;
            }

            if (_onOffLabel != null)
            {
                _onOffLabel.text = L(settings.HudFrameOn ? "ui.hudframe.on" : "ui.hudframe.off");
            }

            for (int i = 0; i < _sizeButtons.Length; i++)
            {
                Arm(_sizeButtons[i], _sizeLabels[i], i == settings.HudFrameSizeLevel);
            }

            int nearest = 0;
            for (int i = 1; i < HudOpacitySteps.Length; i++)
            {
                if (Mathf.Abs(HudOpacitySteps[i] - settings.HudFrameAlpha) < Mathf.Abs(HudOpacitySteps[nearest] - settings.HudFrameAlpha))
                {
                    nearest = i;
                }
            }

            for (int i = 0; i < _opacityButtons.Length; i++)
            {
                Arm(_opacityButtons[i], _opacityLabels[i], i == nearest);
            }

            RefreshHudPreview();
        }

        /// <summary>The preview screen shows the canvas as it is right now (uncommitted strokes too), at the chosen size and
        /// opacity — faint when the frame is switched off, so there is still something to judge.</summary>
        private void RefreshHudPreview()
        {
            if (_hudPreview == null || HudHost == null)
            {
                return;
            }

            bool onCanvas = _hudMode && _grid != null && _grid.Length == HudFrameDesign.EditorGridW * HudFrameDesign.EditorGridH;
            _hudPreview.SetDesign(onCanvas ? _hudBase.WithEditorGrid(_grid) : HudFrameDesign.Parse(HudHost.Design));
            var settings = HudHost.Settings;
            int size = settings?.HudFrameSizeLevel ?? HudFrameLayout.SizeMedium;
            float tile = HudFrameLayout.TilePixels(1920, 1080, size) * (HudPreviewW / 1920f); // a 1080p screen, scaled down
            _hudPreview.Layout(HudPreviewW, HudPreviewH, tile, 0f);
            _hudPreview.SetAlpha(settings == null ? 1f : settings.HudFrameOn ? settings.HudFrameAlpha : 0.25f);
            _hudPreview.Still = settings != null && settings.ReducedEffects;
        }
    }
}
