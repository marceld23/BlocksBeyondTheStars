// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Item + recipe designer — a developer tool of the Editors menu. A data form to define a new item (key / name /
    /// category / stats), its crafting recipe (station, ingredients, output count) and optional blueprint gating (knowledge
    /// + unlock cost). <b>Save</b> writes a content bundle (<c>content_exports/&lt;key&gt;/content.json</c>) a developer folds
    /// into the game with <c>tools/merge_recipe.py</c> (→ data/items.json, recipes.json, blueprints.json + locale
    /// placeholders). Client-only; nothing reaches the current game.
    /// <para>
    /// On the <see cref="EditorShell"/> since #3694 (epic #3696), as the approved mock draws it: Back, the title with a
    /// "DEVELOPER" badge, Undo / Redo, New and the primary Save ("export for the game") in the header; the item, the recipe and
    /// a check card in the work area; messages in the status bar by severity and without file paths. The whole definition is
    /// one <see cref="ContentBundle"/> — the undo snapshots and the draft are its JSON, so every field, row and switch is
    /// undoable and survives in the draft.
    /// </para>
    /// </summary>
    public sealed partial class ContentEditor : MonoBehaviour, IEditorDocument
    {
        public AppShell Shell;

        /// <summary>Tests: the drafts folder (null = the player's, or the capture run's throwaway one).</summary>
        public string DraftRoot;

        /// <summary>Tests: where <c>content_exports</c> goes (null = <see cref="AppPaths.Root"/>).</summary>
        public string ExportRoot;

        /// <summary>The draft slot and the draft header's kind.</summary>
        public const string EditorKey = "content";

        /// <summary>The draft layout this build writes and reads (<see cref="CanvasDraftFormat"/>).</summary>
        public const int DraftVersion = 1;

        // The pickers are derived from the real enums (in their JSON camelCase spelling), so the editor can
        // never again offer a value the game refuses to load — it used to list "lab"/"machineRoom", which do
        // not exist, and to omit transmuter/factory/algaeTank, which do (#508).
        private static readonly string[] Categories = EnumOptions<ItemCategory>();
        private static readonly string[] ToolKinds = EnumOptions<ToolKind>();
        private static readonly string[] Stations = EnumOptions<CraftingStation>();

        private readonly UndoHistory<string> _history = new UndoHistory<string>();
        private readonly HashSet<string> _exportedKeys = new HashSet<string>(StringComparer.Ordinal);
        private ContentBundle _b;
        private Canvas _canvas;
        private EditorShell _shell;
        private DevEditorForm _form;
        private InputField _keyField;
        private bool _overwriteConfirmed;
        private bool? _hintForPad;

        /// <summary>An enum's members in declaration order, spelled the way
        /// <c>ContentLoader.JsonOptions</c>' camelCase <c>JsonStringEnumConverter</c> reads and writes them.</summary>
        private static string[] EnumOptions<T>() where T : struct, Enum
        {
            var names = Enum.GetNames(typeof(T));
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = char.ToLowerInvariant(names[i][0]) + names[i].Substring(1);
            }

            return names;
        }

        /// <summary>The shell this editor is built on — for tests.</summary>
        public EditorShell EditorShellForTests => _shell;

        /// <summary>The key field (the pad's first focus) — for tests.</summary>
        public InputField KeyFieldForTests => _keyField;

        private void Start()
        {
            _b = Defaults();
            _history.Reset();
            BuildUi();
        }

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                DevEditorForm.Discard(_canvas.gameObject); // the shell on it keeps unsaved work as a draft
            }
        }

        private void Update()
        {
            if (_shell == null)
            {
                return;
            }

            if (!_shell.IsDialogOpen)
            {
                _form.TickTab();
            }

            bool pad = InputMap.ActiveDevice == InputDeviceKind.Gamepad;
            if (_hintForPad != pad)
            {
                _hintForPad = pad;
                _shell.SetHint(L(pad ? "ui.ed.hint_pad_form" : "ui.content.keys_hint"));
            }
        }

        private void BuildUi()
        {
            _canvas = UiKit.CreateCanvas("Content Editor UI");
            _canvas.sortingOrder = 5;
            _shell = EditorShell.Build(_canvas, this, new EditorShellOptions
            {
                EditorKey = EditorKey,
                TitleKey = "ui.menu.content_editor",
                SaveSublineKey = "ui.editor.save_sub.content",
                OnBack = () => Shell?.CloseContentEditor(),
                OnSave = Save,
                OnNew = StartNew,
                HasUndo = true,
                DraftRoot = DraftRoot ?? CanvasEditorDrafts.DraftRoot,
                Localize = L,
            });
            DevEditorForm.AddBadge(_shell, L("ui.editor.badge_dev"));
            _form = new DevEditorForm(_history.EndMerge);
            BuildItemCard();
            BuildRecipeCard();
            BuildCheckCard();
            _shell.PreferFocus(_keyField);
            RunChecks();
            CanvasEditorDrafts.Offer(_shell);
        }

        private string L(string key) => Shell?.L(key) ?? key;

        // ---- the edited definition ---------------------------------------------------------------------------------------

        /// <summary>A fresh item: the defaults the editor always opened with.</summary>
        private ContentBundle Defaults() => new ContentBundle
        {
            key = "my_item",
            name = L("ui.ed.default.item"),
            desc = L("ui.ed.default.item_desc"),
            category = Categories[0],
            toolKind = ToolKinds[0],
            station = Array.IndexOf(Stations, "workshop") >= 0 ? "workshop" : Stations[0],
            placesBlock = string.Empty,
            marketTheme = string.Empty,
            maxStack = ItemDefinition.DefaultMaxStack,
            tier = 1,
            outputCount = 1,
            miningPower = 1f,
            scan = 1f,
            inputs = new List<AmountJson> { new AmountJson { item = "iron_plate", count = 1 } },
            unlockCost = new List<AmountJson>(),
        };

        /// <summary>The definition as JSON — one undo step's state, and the draft's payload.</summary>
        private string Snapshot() => JsonUtility.ToJson(_b);

        /// <summary>Reads a snapshot back; null when it is not one (a broken draft).</summary>
        private static ContentBundle Parse(string json)
        {
            ContentBundle b;
            try
            {
                b = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<ContentBundle>(json);
            }
            catch (Exception)
            {
                return null; // not JSON: a broken draft, never thrown at the shell
            }

            if (b == null)
            {
                return null;
            }

            b.key ??= string.Empty;
            b.name ??= string.Empty;
            b.desc ??= string.Empty;
            b.placesBlock ??= string.Empty;
            b.marketTheme ??= string.Empty;
            b.inputs ??= new List<AmountJson>();
            b.unlockCost ??= new List<AmountJson>();
            return b;
        }

        /// <summary>One change of the definition as ONE undo step: the state before and after <paramref name="apply"/>.
        /// With a <paramref name="mergeKey"/> a run of changes (the letters typed into one field) is one step until the field
        /// lets go of the keyboard.</summary>
        private void Change(Action apply, string mergeKey = null)
        {
            string before = Snapshot();
            apply();
            string after = Snapshot();
            bool step = mergeKey == null ? _history.Record(before, after) : _history.Merge(before, after, mergeKey);
            if (step)
            {
                _shell.NotifyChanged();
                RunChecks();
            }
        }

        /// <summary>Shows <paramref name="b"/> everywhere (undo, redo, a draft, New).</summary>
        private void Show(ContentBundle b)
        {
            _b = b;
            _form.Refresh();
            RefreshRows();
            RunChecks();
        }

        // ---- IEditorDocument ---------------------------------------------------------------------------------------------

        public string DocumentName => ContentKeyRules.Clean(_b?.key);

        public bool IsDirty => _history.IsDirty;

        public bool CanUndo => _history.CanUndo;

        public bool CanRedo => _history.CanRedo;

        public void Undo()
        {
            if (_history.TryUndo(Snapshot(), out string state))
            {
                Show(Parse(state) ?? _b);
            }
        }

        public void Redo()
        {
            if (_history.TryRedo(Snapshot(), out string state))
            {
                Show(Parse(state) ?? _b);
            }
        }

        public string SaveDraft() => CanvasDraftFormat.Wrap(EditorKey, DraftVersion, Snapshot());

        public bool LoadDraft(string draft)
        {
            if (!CanvasDraftFormat.TryUnwrap(draft, EditorKey, DraftVersion, out _, out string payload))
            {
                return false;
            }

            var restored = Parse(payload);
            if (restored == null)
            {
                return false;
            }

            string before = Snapshot();
            _history.Reset();
            _history.Record(before); // a restored draft is unsaved work; Undo goes back to the fresh item
            Show(restored);
            return true;
        }

        /// <summary>Nothing of this editor's own sits over the form: Esc / pad B close the shell's dialogs, never the
        /// editor.</summary>
        public bool CloseTopLayer() => false;

        // ---- header actions ----------------------------------------------------------------------------------------------

        /// <summary>New: a fresh item (the shell asks first over unsaved work).</summary>
        private void StartNew()
        {
            _history.Reset();
            Show(Defaults());
            _shell.ClearStatus();
        }

        private string ExportDir(string key) => Path.Combine(ExportRoot ?? AppPaths.Root, "content_exports", key);

        /// <summary>Save: checks, then writes the bundle. An export of the same key from an earlier session is replaced only
        /// after a question (it may be someone's finished work).</summary>
        private bool Save()
        {
            string error = FirstError();
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
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "content.json"), JsonUtility.ToJson(ExportBundle(key), true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ContentEditor] Export failed: {e}");
                _shell.SetStatus(L("ui.ed.export_failed_hint"), EditorStatusSeverity.Error); // the exception goes to the log
                return false;
            }

            _exportedKeys.Add(key);
            _history.MarkSaved();
            _shell.SetStatus(L("ui.content.exported").Replace("{key}", key), EditorStatusSeverity.Success);
            return true;
        }

        /// <summary>The bundle as merge_recipe.py reads it: the key cleaned, blank optional texts dropped, ingredient rows
        /// without an item or count left out.</summary>
        private ContentBundle ExportBundle(string key)
        {
            var b = Parse(Snapshot());
            b.key = key;
            b.placesBlock = string.IsNullOrWhiteSpace(_b.placesBlock) ? null : _b.placesBlock.Trim();
            b.marketTheme = string.IsNullOrWhiteSpace(_b.marketTheme) ? null : _b.marketTheme.Trim();
            b.inputs = CleanAmounts(_b.inputs);
            b.unlockCost = CleanAmounts(_b.unlockCost);
            return b;
        }

        private static List<AmountJson> CleanAmounts(List<AmountJson> rows)
        {
            var clean = new List<AmountJson>();
            foreach (var a in rows)
            {
                if (a != null && !string.IsNullOrWhiteSpace(a.item) && a.count > 0)
                {
                    clean.Add(new AmountJson { item = a.item.Trim(), count = a.count });
                }
            }

            return clean;
        }

        // ---- the bundle ----------------------------------------------------------------------------------------------------

        [Serializable]
        private sealed class AmountJson
        {
            public string item;
            public int count;
        }

        /// <summary>The whole definition — the editor's state, the undo snapshot, the draft and (cleaned) the export.</summary>
        [Serializable]
        private sealed class ContentBundle
        {
            public string key, name, desc, category, placesBlock, toolKind, station, marketTheme;
            public int maxStack, tier, outputCount, knowledgeCost, miningRadius;
            public float miningPower, damage, range, energy, cooldownSeconds;
            public float consumeHealth, consumeHunger, armor, oxygen, scan;
            public bool hasBlueprint;
            public List<AmountJson> inputs = new();
            public List<AmountJson> unlockCost = new();
        }
    }
}
