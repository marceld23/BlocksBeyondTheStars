// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BlocksBeyondTheStars.Shared.Festivals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The pixel editor on the editor shell (#3693, #3689, #3690): the appearance screen, the Avatar Designer's host
    /// settings and the block paint tool built for real (edit mode runs no Start, so the editor is started by reflection).
    /// Pins the header (Undo / Redo, no Load / New, the ONE primary Save — the selected tab and tool are segments, not
    /// actions), Esc closing the armed tool and never the editor, the drafts of a live host and of the designer's scratch
    /// look, undo / redo through the shell (back to the saved state is "saved" again), Save reaching the host, and the
    /// question before an outfit is deleted.
    /// </summary>
    public sealed class FaceEditorShellEditModeTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private bool _hadEventSystem;
        private string _dir;
        private string _face = string.Empty;
        private readonly string[] _paints = new string[4];
        private readonly Color[] _colors = { Color.white, Color.gray, Color.gray, Color.gray };

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = Object.FindAnyObjectByType<EventSystem>() != null;
            _face = string.Empty; // NUnit keeps one fixture instance: every test starts from a blank look
            Array.Clear(_paints, 0, _paints.Length);
            _colors[0] = Color.white;
            _colors[1] = _colors[2] = _colors[3] = Color.gray;
            _dir = Path.Combine(Path.GetTempPath(), "bbts_faceshell_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ClientSettings.StorageDirOverride = _dir; // a committed HUD frame saves the settings — never the real file
            EditorShell.CancelOwnedElsewhere = () => false; // the submit dialog's / canvas mode's static flags are not this test's
        }

        [TearDown]
        public void TearDown()
        {
            EditorShell.CancelOwnedElsewhere = EditorShell.DefaultCancelOwner;
            ClientSettings.StorageDirOverride = null;
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == "Face Editor UI")
                {
                    Object.DestroyImmediate(root);
                }
            }

            foreach (var go in _spawned.Where(go => go != null))
            {
                Object.DestroyImmediate(go);
            }

            _spawned.Clear();
            LogAssert.ignoreFailingMessages = false;
            var es = Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                Object.DestroyImmediate(es.gameObject);
            }

            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
                // best effort: a temp folder
            }
        }

        // ---------------------------------------------------------------- building

        /// <summary>The appearance screen as the in-game console builds it (no live figure: its rig needs a running game).</summary>
        private FaceEditor Appearance(bool keepsScratch = false, Func<bool> onSaveAll = null, List<string> outfits = null,
            Func<int, string> onDelete = null)
        {
            var editor = NewEditor("AppearanceUnderTest");
            editor.Subjects = AppearanceSubjects.Build(
                () => _face, f => _face = f ?? string.Empty,
                part => _paints[part] ?? string.Empty, (part, p) => _paints[part] = p ?? string.Empty,
                which => _colors[which], (which, c) => _colors[which] = c);
            editor.OutfitNames = () => outfits ?? new List<string>();
            editor.OnDeleteOutfit = onDelete;
            editor.HudHost = new HudFrameEditorHost { Settings = new ClientSettings(), Templates = () => new List<HudFrameDefinition>() };
            editor.KeepsScratch = keepsScratch;
            editor.OnSaveAll = onSaveAll;
            return Start(editor);
        }

        /// <summary>The block paint tool's editor: one 32 × 32 canvas, a live host (Save paints the block).</summary>
        private FaceEditor PaintTool(Action<string> onApply = null)
        {
            var editor = NewEditor("PaintUnderTest");
            editor.EditorKey = "paint";
            editor.OnApply = onApply;
            return Start(editor);
        }

        private FaceEditor NewEditor(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            var editor = go.AddComponent<FaceEditor>();
            editor.DraftRoot = Path.Combine(_dir, "drafts");
            return editor;
        }

        private static FaceEditor Start(FaceEditor editor)
        {
            LogAssert.ignoreFailingMessages = true; // edit mode refuses Destroy; the editor's rebuilds log it
            Call(editor, "Start");
            Assert.IsNotNull(editor.Shell, "the editor built its shell frame");
            return editor;
        }

        private static object Call(object target, string method, params object[] args)
            => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Invoke(target, args);

        private static T Get<T>(object target, string field)
            => (T)target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        private static void Set(object target, string field, object value)
            => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static object Tool(string name)
            => Enum.Parse(typeof(FaceEditor).GetNestedType("DrawTool", BindingFlags.NonPublic), name);

        /// <summary>Paints one cell the way a stroke does: an undo step, the cell, the dirty flag.</summary>
        private static void Paint(FaceEditor editor, int cell, int index)
        {
            Call(editor, "BeginGridEdit");
            Get<int[]>(editor, "_grid")[cell] = index;
            Set(editor, "_dirty", true);
            Call(editor, "CommitGridEdit");
        }

        /// <summary>Primary-styled buttons that are actions — the selected tab and the selected tool are segments of a bar,
        /// drawn the template's way, not a second main action.</summary>
        private static int CountPrimaryActions(FaceEditor editor)
        {
            var segments = new HashSet<Button>();
            foreach (string bar in new[] { "_tabs", "_toolBar" })
            {
                if (Get<UiKit.Segmented>(editor, bar) is { } segmented)
                {
                    segments.UnionWith(segmented.Buttons);
                }
            }

            return editor.Shell.Canvas.GetComponentsInChildren<Button>(true)
                .Count(b => !segments.Contains(b) && b.targetGraphic is Image i && i.sprite == UiKit.PrimarySprite);
        }

        // ---------------------------------------------------------------- header

        [Test]
        public void Header_HasUndoRedo_NoLoadNoNew_AndSaveIsTheOnePrimary()
        {
            var editor = Appearance();
            var shell = editor.Shell;
            LogAssert.ignoreFailingMessages = true;

            Assert.IsNotNull(shell.UndoButton, "Undo in the header");
            Assert.IsNotNull(shell.RedoButton, "Redo in the header");
            Assert.IsNull(shell.LoadButton, "the outfits card is the loader — no Load button that duplicates it");
            Assert.IsNull(shell.NewButton);
            Assert.IsNull(shell.SubmitButton, "no Submit without settings and a figure to send");
            Assert.AreEqual(1, CountPrimaryActions(editor), "exactly one primary action: Save");
            Assert.AreSame(UiKit.PrimarySprite, ((Image)shell.SaveButton.targetGraphic).sprite);
            Assert.IsFalse(shell.UndoButton.interactable, "nothing to undo yet");
        }

        [Test]
        public void Submit_IsInTheHeader_OnlyForAHostThatCanSend()
        {
            var editor = NewEditor("OptionsUnderTest");
            var plain = (EditorShellOptions)Call(editor, "ShellOptions");
            Assert.IsNull(plain.OnSubmit, "the block paint tool has nothing to submit");

            editor.SubmitSettings = new ClientSettings();
            editor.PreviewState = () => new FaceEditor.AppearanceSnapshot();
            var avatar = (EditorShellOptions)Call(editor, "ShellOptions");
            Assert.IsNotNull(avatar.OnSubmit, "both avatar hosts send a skin or a frame");
            Assert.IsTrue(avatar.HasUndo);
        }

        // ---------------------------------------------------------------- Esc / pad B

        [Test]
        public void Esc_DisarmsTheArmedTool_ThenPointsAtBack_AndNeverClosesTheEditor()
        {
            var editor = PaintTool();
            LogAssert.ignoreFailingMessages = true;
            Call(editor, "SelectTool", Tool("Fill"));
            Assert.IsTrue(Get<bool>(editor, "_filling"));

            Assert.AreEqual(EditorCancelResult.ClosedLayer, editor.Shell.HandleCancel(false));
            Assert.IsFalse(Get<bool>(editor, "_filling"), "the armed fill went back to the pen");
            Assert.AreEqual(Tool("Pen"), Get<object>(editor, "_tool"));

            Assert.AreEqual(EditorCancelResult.PointedAtBack, editor.Shell.HandleCancel(false));
            Assert.IsTrue(editor != null && editor.Shell != null, "the editor is still there — only Back leaves it");
            Assert.AreEqual(EditorCancelResult.TextField, editor.Shell.HandleCancel(true), "an Esc that left a text field closes nothing");
        }

        // ---------------------------------------------------------------- undo / redo / save

        [Test]
        public void UndoRedo_ThroughTheShell_AndUndoBackToTheSavedStateIsSavedAgain()
        {
            var editor = PaintTool();
            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(editor.IsDirty);

            Paint(editor, 5, 12);
            Assert.IsTrue(editor.IsDirty);
            Assert.IsTrue(editor.CanUndo);

            editor.Shell.Undo();
            Assert.AreEqual(0, Get<int[]>(editor, "_grid")[5], "the stroke was taken back");
            Assert.IsTrue(editor.CanRedo);
            Assert.IsFalse(editor.IsDirty, "back at the opened state: nothing unsaved");

            editor.Shell.Redo();
            Assert.AreEqual(12, Get<int[]>(editor, "_grid")[5]);
            Assert.IsTrue(editor.IsDirty);
        }

        [Test]
        public void Undo_OfAStepOnAnotherTab_BringsThatTabBack()
        {
            var editor = Appearance();
            LogAssert.ignoreFailingMessages = true;
            Paint(editor, 0, 12);         // the face
            Call(editor, "OnTabSelected", 1); // the torso — the face is committed on the way
            Assert.AreEqual(1, Get<int>(editor, "_active"));
            Assert.AreEqual('c', _face[0], "leaving the face tab committed it");

            editor.Shell.Undo();

            Assert.AreEqual(0, Get<int>(editor, "_active"), "the face tab came back with its step");
            Assert.AreEqual(0, Get<int[]>(editor, "_grid")[0]);
            Assert.AreEqual(0, Get<UiKit.Segmented>(editor, "_tabs").Selected, "and the tab bar shows it");
        }

        [Test]
        public void Save_InALiveHost_ReachesTheHost_AndTheWorkIsSaved()
        {
            string applied = null;
            var editor = PaintTool(pixels => applied = pixels);
            LogAssert.ignoreFailingMessages = true;
            Paint(editor, 3, 9);

            editor.Shell.Save();

            Assert.IsNotNull(applied, "Save painted the block (the host's OnApply)");
            Assert.IsFalse(editor.IsDirty);
            Assert.AreEqual(EditorStatusSeverity.Success, editor.Shell.StatusSeverity);
        }

        [Test]
        public void TheDesigner_IsSavedOnlyByItsSave_NotByATabSwitch()
        {
            int saves = 0;
            var editor = Appearance(keepsScratch: true, onSaveAll: () => { saves++; return true; });
            LogAssert.ignoreFailingMessages = true;
            Paint(editor, 0, 12);
            Call(editor, "OnTabSelected", 1);
            Assert.IsTrue(editor.IsDirty, "the face reached the scratch look only — still unsaved");

            editor.Shell.Save();

            Assert.AreEqual(1, saves, "the host persisted the scratch look");
            Assert.IsFalse(editor.IsDirty);
        }

        // ---------------------------------------------------------------- drafts

        [Test]
        public void Draft_OfTheBlockPaintTool_BringsTheUnsavedCanvasBack()
        {
            var first = PaintTool();
            LogAssert.ignoreFailingMessages = true;
            Paint(first, 7, 21);
            string draft = first.SaveDraft();
            StringAssert.StartsWith("bbts-draft paint v1\n", draft);

            var second = PaintTool();
            Assert.IsTrue(second.LoadDraft(draft));
            Assert.AreEqual(21, Get<int[]>(second, "_grid")[7]);
            Assert.IsTrue(second.IsDirty, "a restored draft is unsaved work");
            Assert.IsFalse(second.LoadDraft(draft.Replace("bbts-draft paint", "bbts-draft form")), "another editor's draft");
            Assert.IsFalse(second.LoadDraft("bbts-draft paint v1\n{ not json"), "a broken draft is refused, not thrown");
        }

        [Test]
        public void Draft_OfTheDesigner_KeepsTheWholeScratchLook()
        {
            var first = Appearance(keepsScratch: true);
            LogAssert.ignoreFailingMessages = true;
            first.EditorKey = "avatar";
            Paint(first, 0, 12);
            Call(first, "OnTabSelected", 1); // the face is in the scratch look now, the torso is on the canvas
            Paint(first, 0, 5);
            string draft = first.SaveDraft();
            StringAssert.StartsWith("bbts-draft avatar v1\n", draft);

            _face = string.Empty; // a fresh designer, opened on the settings' look
            _paints[0] = string.Empty;
            var second = Appearance(keepsScratch: true);
            second.EditorKey = "avatar";
            Assert.IsTrue(second.LoadDraft(draft));

            Assert.AreEqual('c', _face[0], "the face of the draft went back into the scratch look");
            Assert.AreEqual(1, Get<int>(second, "_active"), "the torso tab is open again");
            Assert.AreEqual(5, Get<int[]>(second, "_grid")[0], "with its unsaved paint on the canvas");
            Assert.IsTrue(second.IsDirty);
        }

        // ---------------------------------------------------------------- the in-world host

        /// <summary>The console's appearance screen as GameMenu builds it (no AvatarEditor around it): the same frame as the
        /// menu's designer — tab bar with Figure and HUD frame, its own canvas above the console, an opaque backdrop — and
        /// Save reaching the subjects' OnApply, which in a world sends the look on.</summary>
        [Test]
        public void InWorldHost_HasTheDesignersFrame_AboveTheConsole_AndSaveReachesTheLook()
        {
            var editor = NewEditor("InWorldAppearanceUnderTest");
            editor.Subjects = AppearanceSubjects.Build(
                () => _face, f => _face = f ?? string.Empty,
                part => _paints[part] ?? string.Empty, (part, p) => _paints[part] = p ?? string.Empty,
                which => _colors[which], (which, c) => _colors[which] = c);
            editor.OutfitNames = () => new List<string>();
            editor.HudHost = new HudFrameEditorHost { Settings = new ClientSettings(), Templates = () => new List<HudFrameDefinition>() };
            editor.EditorKey = "appearance";
            editor.ShellTitleKey = "ui.appearance.title";
            editor.SaveSublineKey = "ui.editor.save_sub.appearance";
            editor.FigureTabKey = "ui.avatar.figure_tab";
            Start(editor);
            LogAssert.ignoreFailingMessages = true;

            var tabs = Get<UiKit.Segmented>(editor, "_tabs");
            Assert.AreEqual(7, tabs.Buttons.Length, "face, torso, arms, legs, helmet, Figure, HUD frame — one bar");
            Assert.AreEqual(5, Get<int>(editor, "_figureTab"));
            Assert.AreEqual(6, Get<int>(editor, "_hudTab"));
            Assert.Greater(editor.Shell.Canvas.sortingOrder, 55, "above the console (50), the Codex (55) and VEGA over it (52)");
            Assert.Less(editor.Shell.Canvas.sortingOrder, 60, "under the F1 form (60) and the console's own dialogs (61)");
            var backdrop = editor.Shell.WorkArea.Find("EditorBackdrop")?.GetComponent<Image>();
            Assert.IsNotNull(backdrop, "an opaque backdrop: the world and the console stay behind");
            Assert.AreEqual(1f, backdrop.color.a);
            Assert.IsTrue(backdrop.raycastTarget);

            Paint(editor, 0, 12);
            editor.Shell.Save();
            Assert.AreEqual('c', _face[0], "Save handed the face to the host (GameMenu.ApplyFace sends it on)");
            Assert.IsFalse(editor.IsDirty);
        }

        [Test]
        public void FigureTab_StepsABaseColour_AsAnUndoableStep_AndStaysOnTheTab()
        {
            var editor = NewEditor("FigureUnderTest");
            editor.Subjects = AppearanceSubjects.Build(
                () => _face, f => _face = f ?? string.Empty,
                part => _paints[part] ?? string.Empty, (part, p) => _paints[part] = p ?? string.Empty,
                which => _colors[which], (which, c) => _colors[which] = c);
            editor.FigureTabKey = "ui.avatar.figure_tab";
            Start(editor);
            LogAssert.ignoreFailingMessages = true;
            Call(editor, "OnTabSelected", Get<int>(editor, "_figureTab"));
            Assert.IsTrue(Get<bool>(editor, "_figureShown"));
            Assert.AreEqual(4, Get<System.Collections.ICollection>(editor, "_figureSwatches").Count, "skin, torso, arms, legs");

            Color before = _colors[2];
            Call(editor, "StepFigureColor", 2, 1); // arms →
            Assert.AreNotEqual(before, _colors[2]);
            Assert.IsTrue(editor.CanUndo);

            editor.Shell.Undo();
            Assert.AreEqual(before, _colors[2], "the colour came back");
            Assert.IsTrue(Get<bool>(editor, "_figureShown"), "and the Figure tab stayed up: its rows show the change");
        }

        // ---------------------------------------------------------------- confirmations

        [Test]
        public void DeletingAnOutfit_AsksFirst()
        {
            int deleted = -1;
            var editor = Appearance(outfits: new List<string> { "Explorer" }, onDelete: i => { deleted = i; return "gone"; });
            LogAssert.ignoreFailingMessages = true;
            var shelf = Get<object>(editor, "_outfitShelf");

            Call(editor, "DeleteShelfPressed", shelf, 0);

            Assert.AreEqual(-1, deleted, "nothing is deleted before the answer");
            var dialog = editor.Shell.OpenDialogNow;
            Assert.IsNotNull(dialog, "the question is up");
            Assert.IsTrue(UiKit.IsDangerButton(dialog.Yes), "deleting is the red button; the pad starts on Cancel");

            dialog.Choose(true);
            Assert.AreEqual(0, deleted);
        }
    }
}
