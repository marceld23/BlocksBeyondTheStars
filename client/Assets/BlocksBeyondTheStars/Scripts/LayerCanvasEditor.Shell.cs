// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Client.Playtests;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The layer-canvas editors on the editor shell (#3693, epic #3696; the approved mock's "Formen &amp; Werkzeuge"
    /// view): the shell's header (Back, the title with the work's name, Undo / Redo, the editor's optional New, the ONE
    /// primary Save with its subline), three opaque cards — the list on the left, the layer canvas in the centre, the
    /// preview and the name on the right — and the status bar. This part is the <see cref="IEditorDocument"/> the shell
    /// asks (unsaved? undo? the draft?), the card geometry, the centre card's common parts (the layer line with ▲ / ▼,
    /// the layer strip, the Place | Remove switch) and the drafts' envelope (<see cref="CanvasDraftFormat"/>).
    /// </summary>
    public abstract partial class LayerCanvasEditor : IEditorDocument, IPlaytestWorkSource
    {
        // The mock's cards (1920 × 1080 reference): list, canvas, preview.
        protected const float LeftX = 24f, CentreX = 400f, RightX = 1276f, CardY = 120f, CardH = 900f;
        protected const float LeftW = 360f, CentreW = 860f, RightW = 620f;

        /// <summary>The inner margin of a card.</summary>
        protected const float Inset = 24f;

        /// <summary>The height of a button row (the shell's minimum control size) and the gap between rows.</summary>
        protected const float RowH = 44f, RowGap = 8f;

        /// <summary>The draft layout this base writes (<see cref="CanvasDraftFormat"/>).</summary>
        private const int DraftVersion = 1;

        /// <summary>The editor shell around the canvas (header, status bar, drafts, dialogs).</summary>
        protected EditorShell Frame;

        /// <summary>The centre card's layer line ("Layer 3 of 8").</summary>
        protected Text LayerLabel;

        /// <summary>The drafts folder EditMode tests set (by reflection) before Start; null = the player's drafts folder.</summary>
        private string _draftRootForTests = null;

        private UiKit.Segmented _toolSwitch;
        private RectTransform _layerStrip;
        private readonly List<Button> _layerButtons = new List<Button>();
        private int _stripLayers = -1;

        /// <summary>True while the Remove tool is chosen: the left mouse button / A removes (#3693 — removing without a
        /// middle click; the right mouse button and pad X keep removing in either tool).</summary>
        protected bool Removing { get; private set; }

        /// <summary>The draft slot and the draft header's kind (<c>form</c>, <c>tool_looks</c>, <c>shape</c>).</summary>
        protected abstract string DraftKind { get; }

        /// <summary>The work's own name for the header and the draft question.</summary>
        public abstract string DocumentName { get; }

        /// <summary>Unsaved changes besides the canvas (a renamed form).</summary>
        protected virtual bool ExtraDirty => false;

        /// <summary>The editor's own payload of a draft (the canvas plus whatever the editor needs to bring it back);
        /// null when there is nothing worth keeping.</summary>
        protected abstract string WriteDraftPayload();

        /// <summary>Brings a draft's payload back (the work stays unsaved); false when it cannot be read.</summary>
        protected abstract bool ReadDraftPayload(string payload);

        /// <summary>Closes the editor's own topmost layer (a picker, a panel); false when none is open.</summary>
        protected virtual bool CloseOwnLayer() => false;

        // ---------------------------------------------------------------- IEditorDocument

        public bool IsDirty => Layers.Dirty || ExtraDirty;

        public bool CanUndo => Layers.CanUndo;

        public bool CanRedo => Layers.CanRedo;

        public void Undo() => DoUndo();

        public void Redo() => DoRedo();

        public string SaveDraft() => CanvasDraftFormat.Wrap(DraftKind, DraftVersion, WriteDraftPayload());

        /// <summary>The canvas for a playtest report ("send my work along"): its draft — exactly what is on the screen,
        /// whether it could be saved or not, so the developers can bring it back the way a draft comes back.</summary>
        public FeedbackAttachment WorkForPlaytest() => PlaytestWork.OfFields(DraftKind, new[]
        {
            new KeyValuePair<string, string>("name", DocumentName),
            new KeyValuePair<string, string>("draft", SaveDraft()),
        });

        public bool LoadDraft(string draft)
        {
            if (!CanvasDraftFormat.TryUnwrap(draft, DraftKind, DraftVersion, out _, out string payload))
            {
                return false;
            }

            try
            {
                return ReadDraftPayload(payload);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[{DraftKind}] The draft could not be read: {e.Message}");
                return false;
            }
        }

        /// <summary>Esc / pad B: the editor's own layer first; the Remove tool is not a layer (it stays chosen).</summary>
        public bool CloseTopLayer() => CloseOwnLayer();

        // ---------------------------------------------------------------- building the frame

        /// <summary>
        /// The editor's canvas with the shell on it; returns the work area the cards go into. <paramref name="inWorld"/>:
        /// the in-game shape designer sits above the console (sorting order 60) on an opaque backdrop, so neither the world
        /// nor the console shows through. Undo is always on; the drafts folder is the player's (a throwaway one while the
        /// editors are being photographed).
        /// </summary>
        protected Transform BuildFrame(string canvasName, EditorShellOptions options, bool inWorld)
        {
            Ui = UiKit.CreateCanvas(canvasName);
            Ui.sortingOrder = inWorld ? 60 : 5;
            options.EditorKey = DraftKind;
            options.HasUndo = true;
            options.DraftRoot = _draftRootForTests ?? CanvasEditorDrafts.DraftRoot;
            if (options.Localize == null)
            {
                options.Localize = L;
            }
            Frame = EditorShell.Build(Ui, this, options);
            if (inWorld)
            {
                // The work area's first child: the header and the status bar are drawn over it.
                var backdrop = UiKit.AddImage(Frame.WorkArea, 0f, 0f, 1920f, 1080f, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.09f, 1f));
                backdrop.raycastTarget = true;
                backdrop.name = "LayerEditorBackdrop";
            }

            return Frame.WorkArea;
        }

        /// <summary>The last step of building: the pad's canvas focus, its first stop (never Back), the editor's first hint
        /// and the draft question when one is left from last time.</summary>
        protected void FinishFrame(Selectable firstFocus, string firstStatusKey)
        {
            Pad = new PadCanvasFocus(Ui.gameObject, CanvasRt);
            if (firstFocus != null)
            {
                Frame.PreferFocus(firstFocus);
            }

            if (!string.IsNullOrEmpty(firstStatusKey))
            {
                SetStatus(L(firstStatusKey), EditorStatusSeverity.Info);
            }

            CanvasEditorDrafts.Offer(Frame);
        }

        /// <summary>The editor changed its work: the shell's draft becomes due after the next rest.</summary>
        protected void Changed() => Frame?.NotifyChanged();

        /// <summary>True while a dialog of the shell or the submit dialog covers the canvas — no strokes, no keys.</summary>
        protected bool InputBlocked => (Frame != null && Frame.IsDialogOpen) || CreationSubmitDialog.OwnsCancel;

        /// <summary>A section title of a card: upper case, cyan, never under 13 px.</summary>
        protected Text Kicker(Transform card, float x, float y, float w, string key)
        {
            var label = UiKit.AddKicker(card, x, y, w, L(key));
            UiKit.FitLabelSingleLine(label, w, UiKit.EditorMinFontSize, 16);
            return label;
        }

        /// <summary>A button of a tier, its label fitted the editors' way (one line ≥ 14 px, else two lines ≥ 13 px); a
        /// glyph (▲, ✕, ←) is centred over a narrow one.</summary>
        protected static Button TierButton(Transform parent, float x, float y, float w, float h, string label, System.Action onClick,
            ButtonTier tier = ButtonTier.Quiet)
        {
            var button = UiKit.AddTierButton(parent, x, y, w, h, label, onClick, tier);
            var text = button.GetComponentInChildren<Text>(true);
            if (text != null && w < 70f)
            {
                UiKit.Place(text.gameObject, 2f, 0f, w - 4f, h);
                text.alignment = TextAnchor.MiddleCenter;
            }

            UiKit.FitButtonLabel(text);
            return button;
        }

        // ---------------------------------------------------------------- the centre card's common parts

        /// <summary>The layer line ("Layer 3 of 8") with ▲ / ▼ at the card's top right.</summary>
        protected void BuildLayerHeader(Transform card, float cardW)
        {
            const float arrowW = 56f;
            float arrowsX = cardW - Inset - (2f * arrowW) - RowGap;
            LayerLabel = UiKit.AddText(card, Inset, 14f, arrowsX - Inset - 12f, 36f, string.Empty, 18, UiKit.Cyan, TextAnchor.MiddleLeft,
                FontStyle.Bold);
            TierButton(card, arrowsX, 10f, arrowW, RowH, "▲", () => StepLayer(1));
            TierButton(card, arrowsX + arrowW + RowGap, 10f, arrowW, RowH, "▼", () => StepLayer(-1));
        }

        /// <summary>Writes the layer line (the derived editor's <see cref="RefreshLabels"/> calls it).</summary>
        protected void ShowLayerLine(int layer, int layers)
        {
            if (LayerLabel != null)
            {
                LayerLabel.text = string.Format(L("ui.shape.custom.layer"), layer + 1, layers);
                UiKit.FitLabelSingleLine(LayerLabel, LayerLabel.rectTransform.sizeDelta.x, UiKit.EditorMinFontSize, 18);
            }
        }

        /// <summary>The Place | Remove switch (#3693): what the left mouse button / A does on the canvas. E switches it
        /// too; the right mouse button and pad X always remove.</summary>
        protected UiKit.Segmented BuildToolSwitch(Transform card, float x, float y, float w)
        {
            _toolSwitch = UiKit.AddSegmented(card, x, y, w, RowH, new[] { L("ui.ed.tool_place"), L("ui.ed.tool_remove") }, 0,
                i => SetRemoving(i == 1));
            foreach (var segment in _toolSwitch.Buttons)
            {
                UiKit.FitButtonLabel(segment.GetComponentInChildren<Text>(true));
            }

            return _toolSwitch;
        }

        /// <summary>The colour-slot palette's buttons (null for an editor without one).</summary>
        protected Button[] SlotButtons { get; private set; }

        /// <summary>The colour-slot palette (#3941, <see cref="ShapeSlotPalette"/>): picking a slot paints with it and switches to
        /// Place. Its chosen slot wears the primary look like the tool switch's chosen tool — a choice, not an action.</summary>
        protected void BuildSlotButtons(Transform card, float x, float y, float w, System.Action<int> onPick)
            => SlotButtons = ShapeSlotPalette.Build(card, x, y, w, RowH, L, slot =>
            {
                onPick(slot);
                SetRemoving(false);
            });

        /// <summary>Chooses the Place or the Remove tool (the switch, the E key).</summary>
        protected void SetRemoving(bool removing)
        {
            Removing = removing;
            _toolSwitch?.Select(removing ? 1 : 0);
        }

        /// <summary>A column of numbered layer buttons beside the canvas, the top layer at the top (the mock's "layers"
        /// list): a click shows that layer. It scrolls when a form is several blocks high.</summary>
        protected void BuildLayerStrip(Transform card, float x, float y, float w, float h)
        {
            _layerStrip = UiKit.ScrollList(card, x, y, w, h, 4f);
            _stripLayers = -1;
            RefreshLayerStrip();
        }

        /// <summary>Rebuilds the strip when the number of layers changed; marks the shown layer.</summary>
        protected void RefreshLayerStrip()
        {
            if (_layerStrip == null)
            {
                return;
            }

            int layers = Mathf.Max(1, LayerCount);
            if (layers != _stripLayers)
            {
                RebuildLayerStrip(layers);
            }

            for (int i = 0; i < _layerButtons.Count; i++)
            {
                int layer = layers - 1 - i;
                if (_layerButtons[i] != null)
                {
                    UiKit.StyleTier(_layerButtons[i], layer == Layers.Layer ? ButtonTier.Secondary : ButtonTier.Quiet);
                }
            }
        }

        private void RebuildLayerStrip(int layers)
        {
            for (int i = _layerStrip.childCount - 1; i >= 0; i--)
            {
                EditorSceneKit.Discard(_layerStrip.GetChild(i).gameObject);
            }

            _layerButtons.Clear();
            _stripLayers = layers;
            float rowW = _layerStrip.parent is RectTransform view ? view.sizeDelta.x - 16f : 60f;
            for (int layer = layers - 1; layer >= 0; layer--)
            {
                int target = layer;
                var row = AddListRow(_layerStrip, rowW);
                var button = TierButton(row, 0f, 0f, rowW, RowH, (layer + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    () => ShowLayer(target));
                UiKit.NoPadStop(button); // the pad steps layers with the d-pad on the canvas, not through 24 buttons
                _layerButtons.Add(button);
            }
        }
    }
}
