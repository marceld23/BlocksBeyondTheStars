// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Textures;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Material designer — a developer tool of the Editors menu. You paint a 64×64 block texture on a live canvas (or point
    /// at a source PNG the merge tool decodes), set its mining mechanics (hardness, tool), its look (gloss / metal / glow /
    /// base colour) and where it spawns (frequency + depth band + which kind of planet). <b>Save</b> writes a material
    /// bundle (<c>material_exports/&lt;key&gt;/material.json</c> + <c>texture.bytes</c>) a developer folds into the game with
    /// <c>tools/merge_material.py</c> (→ data/blocks.json, items.json, planets.json, the bundled texture + locale
    /// placeholders). Client-only; nothing reaches the current game.
    /// <para>
    /// On the <see cref="EditorShell"/> since #3694 (epic #3696), as the approved mock draws it: Back, the title with a
    /// "DEVELOPER" badge, Undo / Redo, New and the primary Save ("export for the game") in the header; a texture card with the
    /// texture editor's paint tools (brush, fill, pick a colour, eraser — <see cref="PixelCanvasModel"/>) and a properties
    /// card of <c>‹ value ›</c> cycles; errors in red in the status bar with what to do. Undo covers strokes, fills and every
    /// property: the snapshot (and the draft) is the definition's JSON with the pixels in it.
    /// </para>
    /// </summary>
    public sealed partial class MaterialEditor : MonoBehaviour, IEditorDocument
    {
        public AppShell Shell;

        /// <summary>Tests: the drafts folder (null = the player's, or the capture run's throwaway one).</summary>
        public string DraftRoot;

        /// <summary>Tests: where <c>material_exports</c> goes (null = <see cref="AppPaths.Root"/>).</summary>
        public string ExportRoot;

        /// <summary>The draft slot and the draft header's kind.</summary>
        public const string EditorKey = "material";

        /// <summary>The draft layout this build writes and reads (<see cref="CanvasDraftFormat"/>).</summary>
        public const int DraftVersion = 1;

        private const int Tile = PixelCanvasModel.Size; // 64, the block atlas's tile size

        // What a block can require to be MINED. data/blocks.json only ever uses these two — a scanner cannot
        // break anything, so offering it produced a block nobody could mine (#511).
        private static readonly string[] Tools = { "none", "drill" };
        // Palette/UI grouping, mirroring the categories every shipped block carries (#510).
        private static readonly string[] BlockCategories = { "ore", "terrain", "building", "machine", "light", "door", "flora" };
        // Where the ore vein gets added by the merge tool: any planet, only airless / only with an
        // atmosphere, only single-biome worlds or only multi-biome worlds.
        private static readonly string[] WorldTypes = { "any", "airless", "atmosphere", "single_biome", "multi_biome" };

        private readonly UndoHistory<string> _history = new UndoHistory<string>();
        private readonly HashSet<string> _exportedKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly PixelCanvasModel _model = new PixelCanvasModel(TextureAlphaMode.Opaque);
        private MaterialBundle _b;
        private Canvas _ui;
        private EditorShell _shell;
        private DevEditorForm _form;
        private UnityEngine.UI.InputField _keyField;
        private bool _overwriteConfirmed;
        private string _shownCheck;

        /// <summary>The shell this editor is built on — for tests.</summary>
        public EditorShell EditorShellForTests => _shell;

        /// <summary>The key field (the pad's first focus) — for tests.</summary>
        public UnityEngine.UI.InputField KeyFieldForTests => _keyField;

        /// <summary>The painted tile (raw RGBA32, rows bottom-up) — for tests.</summary>
        public byte[] PixelsForTests => _model.Frames[0];

        private void Start()
        {
            _b = Defaults();
            _model.Load(new[] { BaseFrame(true) }, 0);
            _history.Reset();
            BuildUi();
        }

        private void OnDestroy()
        {
            EndStroke();
            _pad?.Release();
            if (_ui != null)
            {
                DevEditorForm.Discard(_ui.gameObject); // the shell on it keeps unsaved work as a draft
            }

            DevEditorForm.Discard(_tex);
        }

        private void BuildUi()
        {
            _ui = UiKit.CreateCanvas("Material Editor UI");
            _ui.sortingOrder = 5;
            _shell = EditorShell.Build(_ui, this, new EditorShellOptions
            {
                EditorKey = EditorKey,
                TitleKey = "ui.menu.material_editor",
                SaveSublineKey = "ui.editor.save_sub.material",
                OnBack = () => Shell?.CloseMaterialEditor(),
                OnSave = Save,
                OnNew = StartNew,
                HasUndo = true,
                DraftRoot = DraftRoot ?? CanvasEditorDrafts.DraftRoot,
                Localize = L,
            });
            DevEditorForm.AddBadge(_shell, L("ui.editor.badge_dev"));
            _form = new DevEditorForm(_history.EndMerge);
            BuildTextureCard();
            BuildPropsCard();
            _form.OnRefresh(RefreshPaintUi);
            _shell.PreferFocus(_keyField);
            _pad = new PadCanvasFocus(_ui.gameObject, _canvasRt);
            RunChecks();
            CanvasEditorDrafts.Offer(_shell);
        }

        private string L(string key) => Shell?.L(key) ?? key;

        // ---- the edited definition ---------------------------------------------------------------------------------------

        private MaterialBundle Defaults() => new MaterialBundle
        {
            key = "my_material",
            name = L("ui.ed.default.material"),
            desc = L("ui.ed.default.material_desc"),
            requiredTool = Tools[0],
            worldType = WorldTypes[0],
            category = BlockCategories[0],
            sourceImage = string.Empty,
            hardness = 3f,
            gloss = 0.1f,
            frequency = 0.06f,
            // The shipped veins all run to 2048 — the world is that deep since the worldgen overhaul, and a band
            // that stops at 256 would leave the material out of most of the crust (#509).
            minDepth = 4,
            maxDepth = 2048,
            colorRgb = (140 << 16) | (140 << 8) | 145,
            tintable = true, // may the player dye/re-shape it (plain materials should)
        };

        /// <summary>The definition with its pixels as JSON — one undo step's state, and the draft's payload.</summary>
        private string Snapshot() => JsonUtility.ToJson(new MaterialState { material = _b, pixels = Convert.ToBase64String(_model.Frames[0]) });

        /// <summary>Reads a snapshot back; false when it is not one (a broken draft).</summary>
        private static bool TryParse(string json, out MaterialBundle bundle, out byte[] pixels)
        {
            bundle = null;
            pixels = null;
            try
            {
                var state = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<MaterialState>(json);
                if (state?.material == null || string.IsNullOrEmpty(state.pixels))
                {
                    return false;
                }

                pixels = Convert.FromBase64String(state.pixels);
                bundle = state.material;
            }
            catch (Exception)
            {
                return false; // not JSON / not base64: a broken draft, never thrown at the shell
            }

            if (pixels.Length != TextureTiles.BytesPerFrame)
            {
                return false;
            }

            bundle.key ??= string.Empty;
            bundle.name ??= string.Empty;
            bundle.desc ??= string.Empty;
            bundle.sourceImage ??= string.Empty;
            return true;
        }

        /// <summary>One change as ONE undo step (see <see cref="ContentEditor"/>); a <paramref name="mergeKey"/> joins the
        /// letters typed into one field.</summary>
        private void Change(Action apply, string mergeKey = null)
        {
            string before = Snapshot();
            apply();
            string after = Snapshot();
            bool step = mergeKey == null ? _history.Record(before, after) : _history.Merge(before, after, mergeKey);
            if (step)
            {
                AfterChange();
            }
        }

        private void AfterChange()
        {
            Upload();
            _form.Refresh();
            _shell.NotifyChanged();
            RunChecks();
        }

        /// <summary>Shows a state everywhere (undo, redo, a draft, New).</summary>
        private void Show(MaterialBundle bundle, byte[] pixels)
        {
            _b = bundle;
            _model.Load(new[] { pixels }, 0);
            Upload();
            _form.Refresh();
            RunChecks();
        }

        private void ShowSnapshot(string state)
        {
            if (TryParse(state, out var bundle, out var pixels))
            {
                Show(bundle, pixels);
            }
        }

        // ---- IEditorDocument ---------------------------------------------------------------------------------------------

        public string DocumentName => ContentKeyRules.Clean(_b?.key);

        public bool IsDirty => _history.IsDirty;

        public bool CanUndo => _history.CanUndo;

        public bool CanRedo => _history.CanRedo;

        public void Undo()
        {
            EndStroke();
            if (_history.TryUndo(Snapshot(), out string state))
            {
                ShowSnapshot(state);
            }
        }

        public void Redo()
        {
            EndStroke();
            if (_history.TryRedo(Snapshot(), out string state))
            {
                ShowSnapshot(state);
            }
        }

        public string SaveDraft() => CanvasDraftFormat.Wrap(EditorKey, DraftVersion, Snapshot());

        public bool LoadDraft(string draft)
        {
            if (!CanvasDraftFormat.TryUnwrap(draft, EditorKey, DraftVersion, out _, out string payload)
                || !TryParse(payload, out var bundle, out var pixels))
            {
                return false;
            }

            string before = Snapshot();
            _history.Reset();
            _history.Record(before); // a restored draft is unsaved work; Undo goes back to the fresh material
            Show(bundle, pixels);
            return true;
        }

        /// <summary>Esc / pad B: the pad's painting mode is this editor's only layer — it closes first; the editor never.</summary>
        public bool CloseTopLayer()
        {
            if (_pad != null && _pad.CanvasMode)
            {
                _pad.Release();
                return true;
            }

            return false;
        }

        // ---- header actions ----------------------------------------------------------------------------------------------

        /// <summary>New: a fresh material (the shell asks first over unsaved work).</summary>
        private void StartNew()
        {
            EndStroke();
            _history.Reset();
            _b = Defaults(); // BaseFrame reads the base colour
            Show(_b, BaseFrame(true));
            _shell.ClearStatus();
            RunChecks();
        }

        private string ExportDir(string key) => Path.Combine(ExportRoot ?? AppPaths.Root, "material_exports", key);

        /// <summary>Save: checks, then writes the bundle; an export of the same key from an earlier session is replaced only
        /// after a question.</summary>
        private bool Save()
        {
            EndStroke();
            var (error, _) = FirstProblem();
            if (error != null)
            {
                _shell.SetStatus(error, EditorStatusSeverity.Error);
                return false;
            }

            string key = ContentKeyRules.Clean(_b.key);
            string dir = ExportDir(key);
            if (!_overwriteConfirmed && !_exportedKeys.Contains(key) && Directory.Exists(dir))
            {
                string question = L("ui.ed.confirm_overwrite_export").Replace("{key}", key);
                _shell.Confirm(question, L("ui.editor.confirm_replace_yes"), SaveOverExport, "btn_save");
                _shell.SetStatus(question, EditorStatusSeverity.Warning);
                return false;
            }

            return Export(key, dir);
        }

        private void SaveOverExport()
        {
            _overwriteConfirmed = true;
            try
            {
                _shell.Save(); // through the shell, so a written export also removes the draft
            }
            finally
            {
                _overwriteConfirmed = false;
            }
        }

        private bool Export(string key, string dir)
        {
            var b = JsonUtility.FromJson<MaterialBundle>(JsonUtility.ToJson(_b));
            b.key = key;
            b.sourceImage = string.IsNullOrWhiteSpace(_b.sourceImage) ? null : _b.sourceImage.Trim();
            b.maxDepth = Mathf.Max(_b.minDepth, _b.maxDepth);
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "material.json"), JsonUtility.ToJson(b, true));
                // Raw RGBA32 tile in the exact layout BlockTextureAtlas.LoadRawTextureData expects.
                File.WriteAllBytes(Path.Combine(dir, "texture.bytes"), _model.Frames[0]);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MaterialEditor] Export failed: {e}");
                _shell.SetStatus(L("ui.ed.export_failed_hint"), EditorStatusSeverity.Error); // the exception goes to the log
                return false;
            }

            _exportedKeys.Add(key);
            _history.MarkSaved();
            _shell.SetStatus(L("ui.material.exported").Replace("{key}", key), EditorStatusSeverity.Success);
            return true;
        }

        // ---- checks ------------------------------------------------------------------------------------------------------

        /// <summary>The first error (key, name) or else the first warning (the block exists already: the merge only changes
        /// its look) — the material editor has no check card, so both go to the status bar.</summary>
        private (string Error, string Warning) FirstProblem()
        {
            string error = ContentEditor.KeyProblemText(L, _b.key);
            if (error == null && string.IsNullOrWhiteSpace(_b.name))
            {
                error = L("ui.ed.check.need_name");
            }

            string key = ContentKeyRules.Clean(_b.key);
            var content = Shell != null ? Shell.Content : null;
            string warning = error == null && content != null && content.GetBlock(key) != null
                ? L("ui.material.check.key_taken").Replace("{key}", key)
                : null;
            return (error, warning);
        }

        /// <summary>A problem stands in the status bar (red error, else amber warning); once it is fixed, the line it left
        /// there goes again — a message of something else ("Exported") stays.</summary>
        private void RunChecks()
        {
            if (_shell == null || _b == null)
            {
                return;
            }

            var (error, warning) = FirstProblem();
            bool ours = _shownCheck != null && _shell.StatusText.text == _shownCheck;
            if (error != null || warning != null)
            {
                _shell.SetStatus(error ?? warning, error != null ? EditorStatusSeverity.Error : EditorStatusSeverity.Warning);
                _shownCheck = _shell.StatusText.text;
            }
            else if (ours)
            {
                _shell.ClearStatus();
                _shownCheck = null;
            }
        }

        /// <summary>The current problem's text and severity — for tests.</summary>
        public (string Error, string Warning) ProblemForTests => FirstProblem();

        // ---- the bundle ----------------------------------------------------------------------------------------------------

        /// <summary>The material as merge_material.py reads it — also the editor's state.</summary>
        [Serializable]
        private sealed class MaterialBundle
        {
            public string key, name, desc, requiredTool, worldType, sourceImage, category;
            public float hardness, gloss, metal, emission, frequency;
            public int minToolTier, minDepth, maxDepth, colorRgb;
            public bool tintable;
        }

        /// <summary>A snapshot / draft: the bundle and the tile's pixels (base64 of the raw RGBA32 rows).</summary>
        [Serializable]
        private sealed class MaterialState
        {
            public MaterialBundle material;
            public string pixels;
        }
    }
}
