// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// "My tools" (#1963): give your drill, your pistol, your blade, your scanner or (#2837) a gadget — the binoculars,
    /// the camera, the medkit … — a look of your own (every kind <see cref="HeldItemShapes.IsShapedKind"/> lists). Like the
    /// pixel face and the body paint the look belongs to YOU — it is chosen here in the main menu, stored in the
    /// settings, announced when you enter a world and shown to everyone who sees you. It changes nothing about what
    /// the tool does, and it is not handed over with the tool.
    ///
    /// A look is a small coloured voxel model, painted one horizontal layer at a time (the form editor's way of
    /// working) from a fixed palette of fifteen colours, each of which can glow. The canvas looks down on the tool:
    /// the bottom edge is at the hand, the tool points up the canvas.
    ///
    /// The model (<see cref="ToolLookCanvasModel"/>) is Unity-free and unit-tested; this class is its screen on the editor
    /// shell (#3693, <see cref="LayerCanvasEditor"/>): Save uses the look for the tool, "back to the standard look" asks
    /// first, picking another tool over unsaved work asks too, and unsaved work is kept as a draft. The cards are built in
    /// <c>ToolLookEditor.Ui.cs</c>.
    /// </summary>
    public sealed partial class ToolLookEditor : LayerCanvasEditor
    {
        private readonly ToolLookCanvasModel _model = new ToolLookCanvasModel();
        private readonly List<ItemDefinition> _tools = new List<ItemDefinition>();
        private GameContent _content;
        private ItemDefinition _tool;
        private int _color = 3;
        private GameObject _previewModel;

        protected override ILayerCanvas Layers => _model;

        protected override int CellsX => ToolLook.SizeX;

        protected override int CellsZ => ToolLook.SizeZ;

        protected override int LayerCount => ToolLook.SizeY;

        protected override float SpinDegreesPerSecond => 26f;

        protected override string HintKeyMouse => "ui.toollook.hint";

        protected override string HintKeyPadCanvas => "ui.toollook.hint_pad";

        protected override string HintKeyPadPanel => "ui.form.hint_pad_panel";

        protected override string DraftKind => "tool_looks";

        public override string DocumentName => _tool != null ? ToolName(_tool) : string.Empty;

        /// <summary>Place paints the chosen colour, Remove (or the right button / X) erases.</summary>
        protected override bool PaintCell(int x, int z, bool primary, bool secondary) => _model.Paint(x, z, secondary ? 0 : _color);

        /// <summary>Y on the canvas picks the colour under the cursor.</summary>
        protected override void OnPadCanvasFrame()
        {
            if (InputMap.PadDown(PadButton.Y))
            {
                int picked = _model.Get(Pad.CellX, _model.Layer, ToolLook.SizeZ - 1 - Pad.CellY);
                if (picked != 0)
                {
                    SetColor(picked);
                }
            }
        }

        /// <summary>The labels follow once a stroke ends, not per painted cell.</summary>
        protected override void OnStrokeEnded() => RefreshLabels();

        protected override void AfterCanvasEdit() => RefreshAll();

        // ---------------------------------------------------------------- life cycle

        private void Start()
        {
            _content = Shell != null ? Shell.Content : null;
            if (_content != null)
            {
                foreach (var item in _content.Items.Values)
                {
                    if (item.Tool != null && HeldItemShapes.IsShapedKind(HeldItem.For(_content, item.Key).kind.ToString()))
                    {
                        _tools.Add(item);
                    }
                }

                _tools.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            }

            Tex = new Texture2D(ToolLook.SizeX, ToolLook.SizeZ, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            var firstFocus = BuildUi();
            BuildStage();
            Open(_tools.Count > 0 ? _tools[0] : null);
            FinishFrame(firstFocus, _tools.Count > 0 ? "ui.form.tip_remove" : "ui.toollook.no_tools");
        }

        /// <summary>Opens a tool's look on the canvas (its stored look, else an empty canvas) with a fresh history.</summary>
        private void Open(ItemDefinition tool)
        {
            _tool = tool;
            _model.New();
            if (tool != null && Shell?.Settings != null)
            {
                _model.Load(Shell.Settings.GetToolLook(tool.Key)); // nothing stored → an empty canvas
            }

            Stroking = false;
            RefreshAll();
            RebuildToolList();
        }

        /// <summary>A tool of the list over unsaved work: the shell asks first.</summary>
        private void PickTool(ItemDefinition tool) => Frame?.ConfirmDiscard(() =>
        {
            Open(tool);
            Frame?.ClearStatus();
        });

        // ---------------------------------------------------------------- commands

        private void SetColor(int color)
        {
            _color = Mathf.Clamp(color, 1, ToolLook.Palette.Count - 1);
            SetRemoving(false); // a colour is chosen to paint with
            RefreshLabels();
        }

        /// <summary>Save (the shell's primary button): this look for this tool, stored in the settings.</summary>
        private bool Save()
        {
            if (_tool == null || Shell?.Settings == null)
            {
                SetStatus(L("ui.toollook.no_tools"), EditorStatusSeverity.Warning);
                return false;
            }

            string look = _model.Encode();
            if (look.Length == 0)
            {
                SetStatus(L(_model.IsEmpty ? "ui.toollook.problem_empty" : "ui.toollook.problem_parts"), EditorStatusSeverity.Warning);
                return false;
            }

            if (!Shell.Settings.SetToolLook(_tool.Key, look))
            {
                SetStatus(string.Format(L("ui.toollook.limit"), ToolLook.MaxLooksPerPlayer), EditorStatusSeverity.Warning);
                return false;
            }

            Shell.Settings.Save();
            _model.MarkSaved();
            RebuildToolList();
            SetStatus(L("ui.toollook.saved"), EditorStatusSeverity.Success);
            return true;
        }

        /// <summary>"Back to the standard look" removes the stored look for good — so it asks first (#3690).</summary>
        private void AskUseStandard()
        {
            if (_tool == null || Shell?.Settings == null)
            {
                return;
            }

            Frame?.Confirm(L("ui.toollook.confirm_standard"), L("ui.toollook.use_standard"), UseStandard);
        }

        private void UseStandard()
        {
            if (_tool == null || Shell?.Settings == null)
            {
                return;
            }

            Shell.Settings.SetToolLook(_tool.Key, string.Empty);
            Shell.Settings.Save();
            _model.New();
            RefreshAll();
            RebuildToolList();
            SetStatus(L("ui.toollook.standard_again"), EditorStatusSeverity.Info);
        }

        private void CopyCode()
        {
            string look = _model.Encode();
            if (look.Length == 0 || _tool == null)
            {
                SetStatus(L(_model.IsEmpty ? "ui.toollook.problem_empty" : "ui.toollook.problem_parts"), EditorStatusSeverity.Warning);
                return;
            }

            GUIUtility.systemCopyBuffer = ShareCode.EncodeToolLook(look, _tool.Key);
            SetStatus(L("ui.tex.code_copied"), EditorStatusSeverity.Success);
        }

        /// <summary>A pasted code lands on the canvas as ONE undoable step; Save makes it this tool's look.</summary>
        private void PasteCode()
        {
            if (ShareCode.TryDecodeToolLook(GUIUtility.systemCopyBuffer, out string look, out _) && ToolLook.IsValid(look)
                && _model.ReplaceRaw(look))
            {
                RefreshAll();
                Changed();
                SetStatus(L("ui.toollook.pasted"), EditorStatusSeverity.Info);
            }
            else
            {
                SetStatus(L("ui.shape.custom.import_failed"), EditorStatusSeverity.Warning);
            }
        }

        private void Close()
        {
            if (Shell != null)
            {
                Shell.CloseToolLookEditor();
            }
        }

        // ---------------------------------------------------------------- drafts (#3689)

        [Serializable]
        private sealed class LookDraft
        {
            public string tool;
            public string raw;
        }

        /// <summary>The tool and its canvas as it stands (also empty or over budget — an emptied canvas is work too when
        /// the tool had a look).</summary>
        protected override string WriteDraftPayload()
            => _tool == null ? null : JsonUtility.ToJson(new LookDraft { tool = _tool.Key, raw = _model.Raw() });

        protected override bool ReadDraftPayload(string payload)
        {
            var draft = JsonUtility.FromJson<LookDraft>(payload);
            var tool = draft == null ? null : _tools.Find(t => t.Key == draft.tool);
            if (tool == null)
            {
                return false; // a tool this game no longer has
            }

            Open(tool);
            if (!_model.ReplaceRaw(draft.raw))
            {
                return false;
            }

            RefreshAll();
            return true;
        }

        // ---------------------------------------------------------------- rendering

        private static Color PaletteColor(int index)
            => HeldModelPart.TryParseColor(ToolLook.Palette[index], out float r, out float g, out float b) ? new Color(r, g, b, 1f) : Color.magenta;

        protected override void RenderLayer()
        {
            var background = FacePalette.EditorBackground;
            for (int z = 0; z < ToolLook.SizeZ; z++)
            {
                for (int x = 0; x < ToolLook.SizeX; x++)
                {
                    int here = _model.Get(x, _model.Layer, z);
                    Color c;
                    if (here != 0)
                    {
                        c = PaletteColor(here);
                    }
                    else
                    {
                        int below = _model.Layer > 0 ? _model.Get(x, _model.Layer - 1, z) : 0;
                        c = below != 0 ? Color.Lerp(background, PaletteColor(below), 0.28f) : background;
                    }

                    Tex.SetPixel(x, z, c); // texture row 0 = the hand end, drawn at the bottom
                }
            }

            Tex.Apply();
        }

        private void BuildStage()
        {
            var target = BeginPreviewStage("ToolLookPreviewRT", "ToolLookStage", -8000f);
            AddPreviewCamera(target, "ToolLookCam", 12f);
            PreviewCamera.orthographicSize = 0.42f;
            PreviewCamera.transform.localPosition = new Vector3(1.6f, 1.1f, -1.6f);
            PreviewCamera.transform.LookAt(Stage.transform.position);
        }

        /// <summary>The preview is built by the SAME code that puts the tool into a hand in the world
        /// (<see cref="HeldItem.Build"/>), so what turns here is what the others will see. While the canvas is empty
        /// it shows the standard model — the thing the look would replace.</summary>
        protected override void RebuildPreview()
        {
            PreviewDirty = false;
            if (Spin == null)
            {
                return;
            }

            if (_previewModel != null)
            {
                Destroy(_previewModel);
                _previewModel = null;
            }

            if (_tool == null)
            {
                return;
            }

            var parts = new List<HeldModelPart>();
            foreach (var box in _model.PreviewBoxes())
            {
                float w = (box.X1 - box.X0) * ToolLook.Voxel, h = (box.Y1 - box.Y0) * ToolLook.Voxel, d = (box.Z1 - box.Z0) * ToolLook.Voxel;
                parts.Add(new HeldModelPart
                {
                    P = new[] { ToolLook.OriginX + (box.X0 * ToolLook.Voxel) + (w / 2f), ToolLook.OriginY + (box.Y0 * ToolLook.Voxel) + (h / 2f), ToolLook.OriginZ + (box.Z0 * ToolLook.Voxel) + (d / 2f) },
                    S = new[] { w, h, d },
                    C = ToolLook.Palette[box.Color],
                    G = _model.Glows(box.Color),
                });
            }

            var (kind, tint, _) = HeldItem.For(_content, _tool.Key);
            var model = parts.Count > 0 ? parts : (IReadOnlyList<HeldModelPart>)_tool.HeldModel; // empty canvas → the standard look
            _previewModel = HeldItem.Build(Spin, kind, tint, null, _tool.Key, model);
            if (_previewModel != null)
            {
                _previewModel.transform.localPosition = new Vector3(0f, 0.04f, -0.2f); // centre the length of the tool on the turntable
            }
        }

        private string ToolName(ItemDefinition tool)
        {
            string name = Shell != null && !string.IsNullOrEmpty(tool.NameKey) ? Shell.L(tool.NameKey) : tool.Key;
            return string.IsNullOrEmpty(name) || name == tool.NameKey ? tool.Key : name;
        }
    }
}
