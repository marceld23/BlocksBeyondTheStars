// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The in-game form designer (#845): sculpt a block out of micro cubes, ONE LAYER AT A TIME — the canvas is a single
    /// horizontal slice painted like a pixel grid, the layer strip walks up and down the block, and a turning 3-D preview
    /// shows the finished form. It is opened by the shape tool (<see cref="ShapeToolUi"/>) and by the crafting tab, which
    /// crafts the form out of the selected material.
    /// <para>
    /// Since #3693 it is a <see cref="LayerCanvasEditor"/> over the main menu form editor's model
    /// (<see cref="FormCanvasModel"/>, one block only) on the editor shell — the same frame as the form editor and "My
    /// tools": header with Back, Undo / Redo and the primary Save (keep the form in "My forms"), the library on the left,
    /// the canvas with the Place | Remove switch in the centre, the preview and the name on the right, and a gamepad. "Clear
    /// all" and "clear layer" became undoable, a library row over unsaved work asks first, and unsaved work is kept as a
    /// draft. A form over several blocks belongs to the menu's form editor and is not opened here.
    /// </para>
    /// </summary>
    public sealed partial class ShapeEditor : LayerCanvasEditor
    {
        /// <summary>The crafting tab's <see cref="ApplyLabelKey"/>: that host crafts the form (a "Craft it" button beside
        /// Save); any other value is a host that only keeps forms.</summary>
        public const string CraftLabelKey = "ui.shape.custom.craft";

        // Host-supplied hooks (the shape tool, the crafting tab, the editor screenshots set them).
        public string InitialVoxels;                     // form to preload (empty = start from scratch)
        public string InitialName;                       // its name, when editing/copying an existing form
        public Func<string, string> Localizer;
        public Action<string, string> OnApply;           // (voxels, name) — the host crafts or keeps it
        public string ApplyLabelKey = CraftLabelKey;     // hosts differ: craft from the menu, keep from the tool
        public Action<string, string> OnSaveDesign;      // (voxels, name) — save to the local library
        public Func<List<(string Name, string Voxels)>> LibraryProvider;
        public Action OnClosed;
        public GameBootstrap Game;                       // the host world (may be null — the editor screenshots)

        private readonly FormCanvasModel _model = new FormCanvasModel();
        private string _name = string.Empty;
        private string _savedName = string.Empty;
        private Mesh _previewMesh;
        private bool _openedMulti;

        /// <summary>True when the host crafts the form (the crafting tab).</summary>
        private bool HostCrafts => ApplyLabelKey == CraftLabelKey;

        protected override ILayerCanvas Layers => _model;

        protected override int CellsX => _model.SizeX;

        protected override int CellsZ => _model.SizeZ;

        protected override int LayerCount => _model.SizeY;

        protected override float SpinDegreesPerSecond => 28f;

        protected override string HintKeyMouse => "ui.shape.custom.hint";

        protected override string HintKeyPadCanvas => "ui.form.hint_pad";

        protected override string HintKeyPadPanel => "ui.form.hint_pad_panel";

        protected override string DraftKind => "shape";

        public override string DocumentName => (_name ?? string.Empty).Trim();

        protected override bool ExtraDirty => !string.Equals(DocumentName, _savedName, StringComparison.Ordinal);

        protected override string Localize(string key) => Localizer != null ? Localizer(key) : UiKit.L(key);

        protected override bool PaintCell(int x, int z, bool primary, bool secondary) => _model.Paint(x, z, primary && !secondary);

        protected override void OnCellPainted() => RefreshLabels();

        protected override void AfterCanvasEdit() => AfterStructuralChange();

        // ---------------------------------------------------------------- life cycle

        private void Start()
        {
            LoadInitial(InitialVoxels, InitialName);
            var firstFocus = BuildUi();
            BuildStage();
            AfterStructuralChange();
            FinishFrame(firstFocus, "ui.form.tip_remove");
            if (_openedMulti)
            {
                SetStatus(L("ui.shape.custom.multi_in_menu"), EditorStatusSeverity.Warning);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Destroy(_previewMesh);
            OnClosed?.Invoke();
        }

        /// <summary>The form the host opened the editor on — a form over several blocks cannot be edited here (it opens
        /// empty and says where it can be).</summary>
        private void LoadInitial(string voxels, string name)
        {
            _openedMulti = CustomShape.IsMulti(voxels);
            if (_openedMulti || string.IsNullOrEmpty(voxels) || !_model.Load(voxels))
            {
                _model.New();
            }

            _name = _savedName = _openedMulti ? string.Empty : name ?? string.Empty;
        }

        private void AfterStructuralChange()
        {
            RebuildCanvasTexture();
            RefreshAll();
        }

        private void ToggleGrid()
        {
            if (_model.SetGrid(_model.Grid == CustomShape.GridLarge ? CustomShape.GridSmall : CustomShape.GridLarge))
            {
                AfterStructuralChange();
                Changed();
            }
        }

        // ---------------------------------------------------------------- save, craft, library

        /// <summary>The form as the game stores it, or null with the reason in the status bar.</summary>
        private string ValidForm()
        {
            string voxels = _model.Encode();
            if (voxels.Length > 0)
            {
                return voxels;
            }

            SetStatus(ProblemText(), EditorStatusSeverity.Warning);
            return null;
        }

        /// <summary>Save (the shell's primary button): keeps the form in "My forms" — through the shape tool's keep hook,
        /// else the host's library save.</summary>
        private bool Save()
        {
            string voxels = ValidForm();
            if (voxels == null)
            {
                return false;
            }

            if (!HostCrafts && OnApply != null)
            {
                OnApply(voxels, DocumentName);
            }
            else if (OnSaveDesign != null)
            {
                OnSaveDesign(voxels, DocumentName);
            }
            else
            {
                CustomShapeLibrary.Save(voxels, DocumentName);
            }

            _savedName = DocumentName;
            _model.MarkSaved();
            RebuildLibraryList();
            SetStatus(L("ui.shape.custom.saved"), EditorStatusSeverity.Success);
            return true;
        }

        /// <summary>"Craft it" (the crafting tab only): the host crafts the form out of the selected material and keeps it
        /// in the library; the designer closes, as before.</summary>
        private void Craft()
        {
            string voxels = ValidForm();
            if (voxels == null || OnApply == null)
            {
                return;
            }

            OnApply(voxels, DocumentName);
            _savedName = DocumentName;
            _model.MarkSaved(); // crafting keeps it in the library: nothing is left unsaved
            Close();
        }

        private void LoadEntry(string name, string voxels)
        {
            bool multi = CustomShape.IsMulti(voxels);
            if (multi || !_model.Load(voxels))
            {
                SetStatus(L(multi ? "ui.shape.custom.multi_in_menu" : "ui.form.load_failed"), EditorStatusSeverity.Warning);
                return;
            }

            _name = _savedName = name ?? string.Empty;
            _nameInput?.SetTextWithoutNotify(_name);
            AfterStructuralChange();
            Frame?.ClearStatus();
        }

        /// <summary>A library row over unsaved work: the shell asks first.</summary>
        private void PickEntry(string name, string voxels) => Frame?.ConfirmDiscard(() => LoadEntry(name, voxels));

        /// <summary>Puts the current form on the clipboard as a share code (#846) — desktop only.</summary>
        private void ExportCode()
        {
            string voxels = ValidForm();
            if (voxels == null)
            {
                return;
            }

            GUIUtility.systemCopyBuffer = ShareCode.EncodeForm(voxels, DocumentName);
            SetStatus(L("ui.shape.custom.exported"), EditorStatusSeverity.Success);
        }

        /// <summary>Takes a share code off the clipboard — validated exactly like the server validates a form — into the
        /// library and, for a one-block form, onto the canvas as ONE undoable step.</summary>
        private void ImportCode()
        {
            if (!ShareCode.TryDecodeForm(GUIUtility.systemCopyBuffer, out string voxels, out string name))
            {
                SetStatus(L("ui.shape.custom.import_failed"), EditorStatusSeverity.Warning);
                return;
            }

            CustomShapeLibrary.Save(voxels, name);
            if (!CustomShape.IsMulti(voxels) && _model.Replace(voxels))
            {
                _name = name ?? string.Empty;
                _nameInput?.SetTextWithoutNotify(_name);
                AfterStructuralChange();
                Changed();
            }

            RebuildLibraryList();
            SetStatus(L("ui.shape.custom.imported"), EditorStatusSeverity.Success);
        }

        private void Close() => Destroy(gameObject);

        private string ProblemText()
        {
            switch (_model.Problem(out _))
            {
                case FormProblem.Empty: return L("ui.form.problem_empty");
                case FormProblem.SolidCubes: return L("ui.form.problem_solid");
                case FormProblem.TooDetailed: return string.Format(L("ui.form.problem_detailed"), 1, CustomShape.MaxBoxes);
                default: return string.Empty;
            }
        }

        // ---------------------------------------------------------------- drafts (#3689)

        [Serializable]
        private sealed class ShapeDraft
        {
            public string name;
            public string raw;
        }

        protected override string WriteDraftPayload()
            => _model.Problem(out _) == FormProblem.Empty ? null : JsonUtility.ToJson(new ShapeDraft { name = _name ?? string.Empty, raw = _model.Raw() });

        protected override bool ReadDraftPayload(string payload)
        {
            var draft = JsonUtility.FromJson<ShapeDraft>(payload);
            _model.New();
            if (draft == null || !_model.ReplaceRaw(draft.raw) || _model.Cells != 1)
            {
                _model.New();
                AfterStructuralChange();
                return false;
            }

            _name = draft.name ?? string.Empty;
            _savedName = string.Empty;
            _nameInput?.SetTextWithoutNotify(_name);
            AfterStructuralChange();
            return true;
        }
    }

    /// <summary>
    /// The client-local form library (#845): one small JSON per saved form under
    /// <c>persistentDataPath/custom_shapes/</c> — the <c>paint_designs</c> pattern, with the player-chosen
    /// NAME that library never had. World-independent by construction: only the voxels travel, the registered
    /// shape index belongs to whichever save it was crafted in.
    /// </summary>
    internal static class CustomShapeLibrary
    {
        [Serializable]
        private sealed class Entry
        {
            public string name;
            public string voxels;
        }

        private static string Dir => Path.Combine(AppPaths.Root, "custom_shapes");

        /// <summary>Saves a form under the player's name for it (a nameless save falls back to "Form N").
        /// Saving the same NAME again overwrites that entry — editing a form and saving it should not leave
        /// a trail of near-duplicates.</summary>
        public static void Save(string voxels, string name)
        {
            if (!CustomShape.IsValidVoxels(voxels))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Dir);
                string clean = Sanitize(name);
                string file = Path.Combine(Dir, FileNameFor(clean));
                if (string.IsNullOrEmpty(clean))
                {
                    int n = 1;
                    while (File.Exists(Path.Combine(Dir, $"form-{n:00}.json")))
                    {
                        n++;
                    }

                    clean = $"Form {n:00}";
                    file = Path.Combine(Dir, $"form-{n:00}.json");
                }

                File.WriteAllText(file, JsonUtility.ToJson(new Entry { name = clean, voxels = voxels }));
                WebGlStorage.Sync(); // in the browser a write lives in memory until synced (#1179) — no-op elsewhere
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Forms] Could not save the form: {ex.Message}");
            }
        }

        /// <summary>All saved forms, name-sorted. Corrupt or malformed files are skipped, never thrown.</summary>
        public static List<(string Name, string Voxels)> List()
        {
            var result = new List<(string, string)>();
            try
            {
                if (!Directory.Exists(Dir))
                {
                    return result;
                }

                foreach (var file in Directory.GetFiles(Dir, "*.json").OrderBy(f => f))
                {
                    try
                    {
                        var entry = JsonUtility.FromJson<Entry>(File.ReadAllText(file));
                        if (entry != null && CustomShape.IsValidVoxels(entry.voxels))
                        {
                            result.Add((string.IsNullOrEmpty(entry.name) ? Path.GetFileNameWithoutExtension(file) : entry.name, entry.voxels));
                        }
                    }
                    catch
                    {
                        // skip corrupt entry
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Forms] Could not list the form library: {ex.Message}");
            }

            return result;
        }

        /// <summary>Removes a saved form by name (no-op when it is not there).</summary>
        public static void Delete(string name)
        {
            try
            {
                string file = Path.Combine(Dir, FileNameFor(Sanitize(name)));
                if (File.Exists(file))
                {
                    File.Delete(file);
                    WebGlStorage.Sync();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Forms] Could not delete the form: {ex.Message}");
            }
        }

        private static string Sanitize(string name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            return trimmed.Length > 24 ? trimmed.Substring(0, 24).Trim() : trimmed;
        }

        /// <summary>A file name derived from the form name — same name, same file, so re-saving overwrites.</summary>
        private static string FileNameFor(string name)
        {
            var safe = new string((name ?? string.Empty).Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
            return (string.IsNullOrEmpty(safe) ? "form" : safe) + ".json";
        }
    }
}
