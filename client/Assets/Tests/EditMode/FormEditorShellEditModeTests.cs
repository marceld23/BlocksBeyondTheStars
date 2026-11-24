// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Definitions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The layer-canvas editors on the editor shell (#3693, the mock's "Formen &amp; Werkzeuge" view): the form editor,
    /// "My tools" and the in-game shape designer. Pins the header's actions (New only where there is one, never Load or
    /// Submit), Save as the ONE primary button (the Place | Remove switch's selected segment is a tab, not an action), Esc
    /// closing the topmost layer and never the editor, undo / redo of an edit through the shell, the Place | Remove switch,
    /// the confirmations of permanent actions, the draft round trip and the shape designer's in-world frame and hosts.
    /// Edit mode runs no Start / Update, so the editors are started by reflection and the shell is driven directly.
    /// </summary>
    public sealed class FormEditorShellEditModeTests
    {
        /// <summary>A one-block 8³ form whose bottom layer is filled — valid (something filled, something empty).</summary>
        private static readonly string Floor = new string('1', 64) + new string('0', 512 - 64);

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private bool _hadEventSystem;
        private Func<string, string> _previousLocalize;
        private string _draftRoot, _settingsDir;

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null;
            LogAssert.ignoreFailingMessages = true; // edit mode refuses Destroy; the editors' rebuilds log it
            _previousLocalize = UiKit.Localize;
            UiKit.Localize = key => key;
            string id = Guid.NewGuid().ToString("N");
            _draftRoot = Path.Combine(Path.GetTempPath(), "bbts-layer-shell-" + id);
            _settingsDir = Path.Combine(Path.GetTempPath(), "bbts-layer-settings-" + id);
            Directory.CreateDirectory(_settingsDir);
            ClientSettings.StorageDirOverride = _settingsDir; // a saved tool look writes the settings — never the real file
            EditorShell.CancelOwnedElsewhere = () => false; // static flags of other tests must not own this Esc
        }

        [TearDown]
        public void TearDown()
        {
            EditorShell.CancelOwnedElsewhere = EditorShell.DefaultCancelOwner;
            ClientSettings.StorageDirOverride = null;
            UiKit.Localize = _previousLocalize;
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name.EndsWith(" Editor UI", StringComparison.Ordinal) || root.name.EndsWith("Stage", StringComparison.Ordinal)
                    || _spawned.Contains(root))
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            _spawned.Clear();
            var es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                UnityEngine.Object.DestroyImmediate(es.gameObject);
            }

            foreach (string dir in new[] { _draftRoot, _settingsDir })
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }

            LogAssert.ignoreFailingMessages = false;
        }

        // ---------------------------------------------------------------- helpers

        private T StartEditor<T>(Action<T> prepare = null) where T : LayerCanvasEditor
        {
            var shellObject = new GameObject("ShellUnderTest");
            _spawned.Add(shellObject);
            var appShell = shellObject.AddComponent<AppShell>();
            appShell.SetSettingsForTest(new ClientSettings());
            var host = new GameObject(typeof(T).Name + "UnderTest");
            _spawned.Add(host);
            var editor = host.AddComponent<T>();
            editor.Shell = appShell;
            prepare?.Invoke(editor);
            typeof(LayerCanvasEditor).GetField("_draftRootForTests", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(editor, _draftRoot);
            Call(editor, "Start");
            return editor;
        }

        private static EditorShell ShellOf(LayerCanvasEditor editor) => (EditorShell)BaseField(editor, "Frame");

        private static object BaseField(object target, string field)
            => typeof(LayerCanvasEditor).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);

        private static object Get(object target, string field)
            => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);

        private static object Call(object target, string method, params object[] args)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var m = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (m != null)
                {
                    return m.Invoke(target, args);
                }
            }

            throw new MissingMethodException(target.GetType().Name, method);
        }

        private static Button ButtonLabelled(Component root, string label)
            => root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.GetComponentInChildren<Text>(true)?.text == label);

        /// <summary>The primary buttons that are actions — a segmented switch's selected segment is a tab, not an action.</summary>
        private static List<Button> PrimaryActions(EditorShell shell, LayerCanvasEditor editor)
        {
            // The tool switch and the colour-slot palette (#3941) show their CHOICE in the primary look — not actions.
            var toolSwitch = (UiKit.Segmented)BaseField(editor, "_toolSwitch");
            var slots = (Button[])typeof(LayerCanvasEditor).GetProperty("SlotButtons", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(editor) ?? Array.Empty<Button>();
            return shell.Canvas.GetComponentsInChildren<Button>(true)
                .Where(b => b.targetGraphic is Image i && i.sprite == UiKit.PrimarySprite && !toolSwitch.Buttons.Contains(b)
                    && !slots.Contains(b))
                .ToList();
        }

        /// <summary>A helper action of the editor (mirror, fill, clear …): one undo step, announced to the shell.</summary>
        private static void Helper(LayerCanvasEditor editor, Action change) => Call(editor, "Do", change);

        private static void AssertHeaderSaveIsTheOnlyPrimary(LayerCanvasEditor editor)
        {
            var shell = ShellOf(editor);
            Assert.IsNotNull(shell.UndoButton, "Undo in the header");
            Assert.IsNotNull(shell.RedoButton, "Redo in the header");
            Assert.IsNull(shell.LoadButton, "the list is the loader — no Load button");
            Assert.IsNull(shell.SubmitButton, "nothing to submit");
            var primaries = PrimaryActions(shell, editor);
            Assert.AreEqual(1, primaries.Count, "exactly one primary action");
            Assert.AreSame(shell.SaveButton, primaries[0], "and it is Save");
        }

        // ---------------------------------------------------------------- form editor

        [Test]
        public void FormEditor_Header_HasNewButNoLoadOrSubmit_AndSaveIsTheOnlyPrimaryAction()
        {
            var editor = StartEditor<FormEditor>();

            AssertHeaderSaveIsTheOnlyPrimary(editor);
            Assert.IsNotNull(ShellOf(editor).NewButton, "New form is the header's New");
            Assert.AreEqual(5, ShellOf(editor).Canvas.sortingOrder, "a menu editor");
        }

        [Test]
        public void FormEditor_UndoAndRedo_RunThroughTheShell_AndUndoBackToTheSavedFormIsClean()
        {
            var editor = StartEditor<FormEditor>();
            var shell = ShellOf(editor);
            var model = (FormCanvasModel)Get(editor, "_model");
            Assert.IsFalse(editor.IsDirty);

            Helper(editor, model.FillLayer);
            Assert.IsTrue(model.Get(0, 0, 0));
            Assert.IsTrue(editor.IsDirty);
            Assert.IsTrue(editor.CanUndo);

            shell.Undo();
            Assert.IsFalse(model.Get(0, 0, 0), "Undo took the fill back");
            Assert.IsFalse(editor.IsDirty, "back at the state it was opened in — nothing unsaved");
            Assert.IsTrue(editor.CanRedo);

            shell.Redo();
            Assert.IsTrue(model.Get(0, 0, 0), "Redo put it back");
            Assert.IsTrue(editor.IsDirty);
        }

        [Test]
        public void FormEditor_Esc_ClosesTheTopLayerFirst_AndNeverTheEditor()
        {
            var editor = StartEditor<FormEditor>();
            var shell = ShellOf(editor);

            Call(editor, "AskDeleteEntry", "Stairs");
            Assert.IsTrue(shell.IsDialogOpen, "deleting a saved form asks first");
            Assert.IsTrue(UiKit.IsDangerButton(shell.OpenDialogNow.Yes), "and the delete button is red");

            Assert.AreEqual(EditorCancelResult.ClosedDialog, shell.HandleCancel(false));
            Assert.IsFalse(shell.IsDialogOpen);
            Assert.AreEqual(EditorCancelResult.PointedAtBack, shell.HandleCancel(false), "nothing open: the editor stays");
            Assert.IsTrue(editor != null && editor.gameObject.activeSelf, "the editor is still open");
            Assert.AreEqual(EditorCancelResult.TextField, shell.HandleCancel(true), "Esc that only left a text field does nothing");
        }

        [Test]
        public void FormEditor_New_OverUnsavedWork_AsksFirst_ThenStartsEmpty()
        {
            var editor = StartEditor<FormEditor>();
            var shell = ShellOf(editor);
            var model = (FormCanvasModel)Get(editor, "_model");
            Helper(editor, model.FillLayer);

            shell.NewButton.onClick.Invoke();
            Assert.IsTrue(shell.IsDialogOpen, "the shell asks before throwing unsaved work away");
            shell.OpenDialogNow.Choose(true);

            Assert.IsFalse(model.Get(0, 0, 0));
            Assert.IsFalse(editor.IsDirty);
            Assert.IsFalse(editor.CanUndo, "a new form starts a new history");
        }

        [Test]
        public void FormEditor_PlaceRemoveSwitch_ChoosesTheRemoveTool()
        {
            var editor = StartEditor<FormEditor>();
            var toolSwitch = (UiKit.Segmented)BaseField(editor, "_toolSwitch");
            bool Removing() => (bool)typeof(LayerCanvasEditor).GetProperty("Removing", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(editor);

            Assert.IsFalse(Removing(), "Place is the start tool");
            Assert.AreEqual(2, toolSwitch.Buttons.Length);
            toolSwitch.Buttons[1].onClick.Invoke();
            Assert.IsTrue(Removing(), "Remove: the left button removes — no middle click needed");
            Assert.AreEqual(1, toolSwitch.Selected);
            toolSwitch.Buttons[0].onClick.Invoke();
            Assert.IsFalse(Removing());
        }

        [Test]
        public void FormEditor_Draft_RoundTrips_AHalfDrawnFormAndItsName()
        {
            var first = StartEditor<FormEditor>();
            var model = (FormCanvasModel)Get(first, "_model");
            Helper(first, model.FillLayer); // a whole layer: half-drawn, not a valid form yet
            first.GetType().GetField("_name", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(first, "Table");
            string draft = first.SaveDraft();
            StringAssert.StartsWith("bbts-draft form v1\n", draft);

            var second = StartEditor<FormEditor>();
            Assert.IsTrue(second.LoadDraft(draft));
            var restored = (FormCanvasModel)Get(second, "_model");
            Assert.IsTrue(restored.Get(3, 0, 5));
            Assert.AreEqual("Table", second.DocumentName);
            Assert.IsTrue(second.IsDirty, "a restored draft is unsaved work");

            Assert.IsFalse(second.LoadDraft("bbts-draft shape v1\n{}"), "another editor's draft is refused");
            Assert.IsFalse(second.LoadDraft("bbts-draft form v1\n{\"raw\":\"9,9,9,8:1\"}"), "a broken canvas is refused");
            Assert.IsNull(StartEditor<FormEditor>().SaveDraft(), "an empty canvas is nothing worth keeping");
        }

        // ---------------------------------------------------------------- my tools

        private ToolLookEditor StartToolLooks()
        {
            var editor = StartEditor<ToolLookEditor>();
            var tool = new ItemDefinition { Key = "test_drill", NameKey = "item.test_drill" };
            ((List<ItemDefinition>)Get(editor, "_tools")).Add(tool);
            Call(editor, "Open", tool);
            return editor;
        }

        [Test]
        public void ToolLookEditor_Header_HasNoNewLoadOrSubmit_AndSaveIsTheOnlyPrimaryAction()
        {
            var editor = StartToolLooks();

            AssertHeaderSaveIsTheOnlyPrimary(editor);
            Assert.IsNull(ShellOf(editor).NewButton);
        }

        [Test]
        public void ToolLookEditor_BackToTheStandardLook_AsksFirst()
        {
            var editor = StartToolLooks();
            var shell = ShellOf(editor);

            ButtonLabelled(shell.Canvas, "ui.toollook.use_standard").onClick.Invoke();

            Assert.IsTrue(shell.IsDialogOpen, "deleting the tool's own look asks first");
            Assert.IsTrue(UiKit.IsDangerButton(shell.OpenDialogNow.Yes));
            shell.OpenDialogNow.Escape();
            Assert.IsFalse(shell.IsDialogOpen);
        }

        [Test]
        public void ToolLookEditor_UndoRedo_AndDraft_RoundTrip()
        {
            var editor = StartToolLooks();
            var shell = ShellOf(editor);
            var model = (ToolLookCanvasModel)Get(editor, "_model");
            Helper(editor, () => model.Paint(2, 3, 5));
            Assert.AreEqual(5, model.Get(2, model.Layer, 3));

            shell.Undo();
            Assert.AreEqual(0, model.Get(2, model.Layer, 3));
            shell.Redo();
            string draft = editor.SaveDraft();
            StringAssert.StartsWith("bbts-draft tool_looks v1\n", draft);

            var second = StartToolLooks();
            Assert.IsTrue(second.LoadDraft(draft));
            var restored = (ToolLookCanvasModel)Get(second, "_model");
            Assert.AreEqual(5, restored.Get(2, restored.Layer, 3));
            Assert.IsTrue(second.IsDirty);
            Assert.AreEqual("item.test_drill", ((ItemDefinition)Get(second, "_tool")).NameKey);
        }

        // ---------------------------------------------------------------- shape designer

        [Test]
        public void ShapeEditor_InAWorld_IsAnOpaqueFrameAboveTheConsole_WithCraftOnlyWhereTheHostCrafts()
        {
            var crafting = StartEditor<ShapeEditor>(e =>
            {
                e.Localizer = key => key;
                e.OnApply = (_, _) => { };
            });
            var shell = ShellOf(crafting);
            Assert.AreEqual(60, shell.Canvas.sortingOrder, "above the console (50)");
            Assert.AreEqual("LayerEditorBackdrop", shell.WorkArea.GetChild(0).name, "an opaque backdrop is the work area's first child");
            AssertHeaderSaveIsTheOnlyPrimary(crafting);
            Assert.IsNotNull(ButtonLabelled(shell.Canvas, ShapeEditor.CraftLabelKey), "the crafting tab crafts");

            var keeping = StartEditor<ShapeEditor>(e =>
            {
                e.Localizer = key => key;
                e.ApplyLabelKey = string.Empty;
                e.OnApply = (_, _) => { };
            });
            Assert.IsNull(ButtonLabelled(ShellOf(keeping).Canvas, ShapeEditor.CraftLabelKey), "the shape tool only keeps forms");
        }

        [Test]
        public void ShapeEditor_Save_KeepsTheFormThroughTheRightHook_AndClearAllIsUndoable()
        {
            int kept = 0, saved = 0;
            var editor = StartEditor<ShapeEditor>(e =>
            {
                e.Localizer = key => key;
                e.InitialVoxels = Floor;
                e.InitialName = "Plate";
                e.ApplyLabelKey = string.Empty;
                e.OnApply = (_, _) => kept++;
                e.OnSaveDesign = (_, _) => saved++;
            });
            var shell = ShellOf(editor);
            var model = (FormCanvasModel)Get(editor, "_model");
            Assert.IsTrue(model.Get(0, 0, 0));
            Assert.IsFalse(editor.IsDirty, "the opened form is the saved state");

            Helper(editor, model.ClearAll);
            Assert.IsFalse(model.Get(0, 0, 0));
            shell.Undo();
            Assert.IsTrue(model.Get(0, 0, 0), "clear all can be taken back now");

            Helper(editor, () => model.Mirror(alongX: true));
            Helper(editor, model.CopyLayerBelow);
            model.SetLayer(1);
            Helper(editor, model.CopyLayerBelow);
            shell.Save();
            Assert.AreEqual(1, kept, "the shape tool's host keeps the form");
            Assert.AreEqual(0, saved);
            Assert.IsFalse(editor.IsDirty);
            Assert.AreEqual(EditorStatusSeverity.Success, shell.StatusSeverity);
        }

        [Test]
        public void ShapeEditor_Draft_RoundTrips_AndAFormOverSeveralBlocksIsRefused()
        {
            var first = StartEditor<ShapeEditor>(e =>
            {
                e.Localizer = key => key;
                e.InitialVoxels = Floor;
                e.InitialName = "Plate";
            });
            var model = (FormCanvasModel)Get(first, "_model");
            model.SetLayer(2);
            Helper(first, model.FillLayer);
            string draft = first.SaveDraft();
            StringAssert.StartsWith("bbts-draft shape v1\n", draft);

            var second = StartEditor<ShapeEditor>(e => e.Localizer = key => key);
            Assert.IsTrue(second.LoadDraft(draft));
            var restored = (FormCanvasModel)Get(second, "_model");
            Assert.IsTrue(restored.Get(0, 0, 0) && restored.Get(0, 2, 0));
            Assert.AreEqual("Plate", second.DocumentName);
            Assert.IsTrue(second.IsDirty);

            string multi = "bbts-draft shape v1\n{\"name\":\"Wardrobe\",\"raw\":\"1,2,1,8:" + new string('1', 1024) + "\"}";
            Assert.IsFalse(second.LoadDraft(multi), "a form over several blocks belongs to the menu's form editor");
        }
    }
}
