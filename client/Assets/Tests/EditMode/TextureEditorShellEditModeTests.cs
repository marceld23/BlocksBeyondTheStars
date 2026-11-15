// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The texture editor on the editor shell (#3693, mock "Textur"): the header's actions (no Load / New — the texture
    /// list is the loader), Save as the ONE primary button (the tool bar's selected segment is a tab, not an action), Esc
    /// closing the topmost layer and never the editor, the draft round trip, undo / redo through the shell, a list pick over
    /// unsaved work asking first, the red permanent actions and the in-world backdrop. Edit mode runs no Start / Update, so
    /// the editor is started by reflection (as the text-fit test does) and the shell is driven directly.
    /// </summary>
    public sealed class TextureEditorShellEditModeTests
    {
        private readonly System.Collections.Generic.List<GameObject> _spawned = new System.Collections.Generic.List<GameObject>();
        private bool _hadEventSystem;
        private Func<string, string> _previousLocalize;
        private string _draftRoot;
        private int _closed;

        private sealed class FakeWorld : ITextureEditorWorldHost
        {
            public GameContent Content => null;

            public bool CanPublish => true;

            public void Publish(string key, byte[][] frames, int fps)
            {
            }

            public void Unpublish(string key)
            {
            }
        }

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null;
            LogAssert.ignoreFailingMessages = true; // edit mode refuses Destroy; the editor's rebuilds log it
            _previousLocalize = UiKit.Localize;
            UiKit.Localize = key => key;
            _draftRoot = Path.Combine(Path.GetTempPath(), "bbts-texture-shell-" + Guid.NewGuid().ToString("N"));
            EditorShell.CancelOwnedElsewhere = () => false; // static flags of other tests must not own this Esc
            _closed = 0;
        }

        [TearDown]
        public void TearDown()
        {
            EditorShell.CancelOwnedElsewhere = EditorShell.DefaultCancelOwner;
            UiKit.Localize = _previousLocalize;
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == "Texture Editor UI" || root.name == "TexturePreviewStage" || _spawned.Contains(root))
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            _spawned.Clear();
            var es =UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                UnityEngine.Object.DestroyImmediate(es.gameObject);
            }

            if (Directory.Exists(_draftRoot))
            {
                Directory.Delete(_draftRoot, recursive: true);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        private TextureEditor StartEditor(bool inWorld = false)
        {
            var shellObject = new GameObject("ShellUnderTest");
            _spawned.Add(shellObject);
            var appShell = shellObject.AddComponent<AppShell>();
            appShell.SetSettingsForTest(new ClientSettings());
            var host = new GameObject("TextureEditorUnderTest");
            _spawned.Add(host);
            var editor = host.AddComponent<TextureEditor>();
            editor.Shell = appShell;
            editor.OnClose = () => _closed++;
            if (inWorld)
            {
                editor.WorldHost = new FakeWorld();
            }

            Set(editor, "_draftRootForTests", _draftRoot);
            Call(editor, "Start");
            Assume.That(Entries(editor).Count, Is.GreaterThan(1), "the build bundles textures to open");
            return editor;
        }

        private static EditorShell ShellOf(TextureEditor editor) => (EditorShell)Get(editor, "_shell");

        private static PixelCanvasModel ModelOf(TextureEditor editor) => (PixelCanvasModel)Get(editor, "_model");

        private static IList Entries(TextureEditor editor) => (IList)Get(editor, "_entries");

        private static object Get(object target, string field)
            => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);

        private static void Set(object target, string field, object value)
            => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(target, value);

        private static object Call(object target, string method, params object[] args)
            => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.Invoke(target, args);

        private static Button ButtonLabelled(Component root, string label)
            => root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.GetComponentInChildren<Text>(true)?.text == label);

        /// <summary>Paints a red square onto the canvas as the fill tool would — one undo step, announced to the shell.</summary>
        private static void Paint(TextureEditor editor)
        {
            ModelOf(editor).Fill(3, 3, new TexelColor(255, 0, 0), everywhere: true);
            Call(editor, "AfterChange");
        }

        [Test]
        public void Header_HasBackUndoSubmitAndSave_ButNoLoadOrNew_AndSaveIsTheOnlyPrimaryAction()
        {
            var editor = StartEditor();
            var shell = ShellOf(editor);

            Assert.IsNotNull(shell.BackButton);
            Assert.IsNotNull(shell.UndoButton);
            Assert.IsNotNull(shell.RedoButton);
            Assert.IsNotNull(shell.SubmitButton, "the texture can be submitted to the developers");
            Assert.IsNull(shell.LoadButton, "the texture list on the left is the loader");
            Assert.IsNull(shell.NewButton, "a texture is opened, never made from nothing");
            Assert.AreEqual("ui.menu.texture_editor", shell.GetComponentsInChildren<Text>(true).Single(t => t.name == EditorShell.TitleName).text);
            Assert.AreEqual("ui.editor.save_sub.texture", shell.GetComponentsInChildren<Text>(true).Single(t => t.name == EditorShell.SublineName).text);

            var toolBar = (UiKit.Segmented)Get(editor, "_toolBar");
            var primaries = shell.Canvas.GetComponentsInChildren<Button>(true)
                .Where(b => b.targetGraphic is Image i && i.sprite == UiKit.PrimarySprite && !toolBar.Buttons.Contains(b))
                .ToList();
            Assert.AreEqual(1, primaries.Count, "one primary action: Save (the selected tool is a tab)");
            Assert.AreSame(shell.SaveButton, primaries[0]);
            Assert.AreSame(UiKit.PrimarySprite, ((Image)toolBar.Buttons[0].targetGraphic).sprite, "the pen is the selected tool");
            Assert.AreEqual(6, toolBar.Buttons.Length, "pen, fill, pick, eraser, line, box in ONE bar");
        }

        [Test]
        public void PermanentActions_AreRed_AndTheOldUndoAndSaveButtonsAreGone()
        {
            var editor = StartEditor();
            var shell = ShellOf(editor);

            Assert.IsTrue(UiKit.IsDangerButton(ButtonLabelled(shell.Canvas, "ui.tex.reset")), "back to the original asks and is red");
            Assert.IsTrue(UiKit.IsDangerButton(ButtonLabelled(shell.Canvas, "ui.tex.remove_local")), "remove mine is red");
            Assert.IsNull(ButtonLabelled(shell.Canvas, "ui.tex.undo"), "undo lives in the header");
            Assert.IsNull(ButtonLabelled(shell.Canvas, "ui.tex.save_local"), "Save lives in the header");
            Assert.IsNotNull(ButtonLabelled(shell.Canvas, "ui.tex.export"), "the desktop keeps its export");
        }

        [Test]
        public void Esc_ClosesTheTopmostLayer_AndNeverTheEditor()
        {
            var editor = StartEditor();
            var shell = ShellOf(editor);
            Paint(editor);
            Call(editor, "OnRowPicked", 1); // a pick over unsaved work asks first

            Assert.IsTrue(shell.IsDialogOpen);
            Assert.AreEqual(EditorCancelResult.ClosedDialog, shell.HandleCancel(false));
            Assert.IsFalse(shell.IsDialogOpen);
            Assert.AreEqual(EditorCancelResult.PointedAtBack, shell.HandleCancel(false), "nothing of its own open: the editor stays");
            Assert.AreEqual(EditorCancelResult.TextField, shell.HandleCancel(true), "an Esc that only left a text field does nothing");
            Assert.AreEqual(0, _closed, "only Back closes the editor");
            Assert.IsTrue(editor.IsDirty, "the work is still there");
        }

        [Test]
        public void ARowPick_OverUnsavedWork_AsksFirst_AndOpensOnlyOnYes()
        {
            var editor = StartEditor();
            var shell = ShellOf(editor);
            string before = editor.DocumentName;
            Paint(editor);

            Call(editor, "OnRowPicked", 1);
            Assert.AreEqual(before, editor.DocumentName, "nothing is thrown away before the answer");
            shell.OpenDialogNow.Choose(true);

            Assert.AreNotEqual(before, editor.DocumentName);
            Assert.IsFalse(editor.IsDirty, "a freshly opened texture is saved work");
        }

        [Test]
        public void UndoAndRedo_GoThroughTheShell_AndUndoingBackToTheOpenedStateIsSavedAgain()
        {
            var editor = StartEditor();
            var shell = ShellOf(editor);
            var model = ModelOf(editor);
            var before = model.Get(3, 3);
            Paint(editor);
            shell.Refresh();
            Assert.IsTrue(shell.UndoButton.interactable);

            shell.Undo();
            Assert.AreEqual(before, model.Get(3, 3));
            Assert.IsFalse(editor.IsDirty, "back to the opened state");
            Assert.IsTrue(shell.RedoButton.interactable);

            shell.Redo();
            Assert.AreEqual(new TexelColor(255, 0, 0), model.Get(3, 3));
            Assert.IsTrue(editor.IsDirty);
        }

        [Test]
        public void Draft_RoundTrip_BringsTheTextureAndItsPixelsBack_Unsaved()
        {
            var first = StartEditor();
            Call(first, "OnRowPicked", 1); // not dirty: opens at once
            Paint(first);
            string name = first.DocumentName;
            string draft = first.SaveDraft();
            StringAssert.StartsWith("bbts-draft texture v1\n", draft);
            UnityEngine.Object.DestroyImmediate(first.gameObject);
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == "Texture Editor UI")
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            var second = StartEditor();
            Assert.IsTrue(second.LoadDraft(draft));

            Assert.AreEqual(name, second.DocumentName, "the draft's texture is open again");
            Assert.AreEqual(new TexelColor(255, 0, 0), ModelOf(second).Get(3, 3));
            Assert.IsTrue(second.IsDirty, "a restored draft is unsaved work");
            Assert.IsTrue(second.CanUndo, "the restore itself can be taken back");
        }

        [Test]
        public void Draft_FromAnotherEditorOrBroken_IsRefused()
        {
            var editor = StartEditor();

            Assert.IsFalse(editor.LoadDraft("bbts-draft form v1\n{}"));
            Assert.IsFalse(editor.LoadDraft("bbts-draft texture v2\n{}"), "a newer layout than this editor reads");
            Assert.IsFalse(editor.LoadDraft("bbts-draft texture v1\n{\"key\":\"no_such_texture\",\"frames\":[\"AAAA\"]}"));
            Assert.IsFalse(editor.LoadDraft("not a draft"));
            Assert.IsNull(CanvasDraftFormat.Wrap("texture", 1, string.Empty), "empty work is no draft");
        }

        [Test]
        public void InAWorld_TheEditorDrawsAboveTheConsole_OnAnOpaqueBackdrop_AndRemovingAWorldTextureIsRed()
        {
            var editor = StartEditor(inWorld: true);
            var shell = ShellOf(editor);

            Assert.AreEqual(60, shell.Canvas.sortingOrder, "above the console (50)");
            Assert.AreEqual("TextureEditorBackdrop", shell.WorkArea.GetChild(0).name, "the backdrop is the work area's first child");
            var backdrop = shell.WorkArea.GetChild(0).GetComponent<Image>();
            Assert.AreEqual(1f, backdrop.color.a, 0.001f, "opaque: the world must not shine through");
            Assert.IsTrue(UiKit.IsDangerButton(ButtonLabelled(shell.Canvas, "ui.tex.unpublish")));
        }
    }
}
