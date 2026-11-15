// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The ship editor on the editor shell (#3691, #3690, #3695), built for real without an <c>AppShell</c> (texts are their
    /// keys, the palette holds the ship parts only). Edit mode runs no Start / Update / OnDestroy, so the tests start the
    /// editor by reflection, place cells through its own <c>PlaceAt</c> / <c>RemoveAt</c> and tear the canvases down
    /// themselves. Pins: every header action and Save as the one primary button, the pad's first focus on the Place tool,
    /// the cards leaving the 3D view free with ≥ 44 px tiles, undo / redo of placed and removed blocks with the right
    /// "unsaved" answer, the draft round trip (and its question on reopening), Esc closing the pickers but never the
    /// editor, and New starting from scratch.
    /// </summary>
    public sealed class ShipEditorShellEditModeTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private bool _hadEventSystem, _hadUiSound;
        private string _draftRoot;

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null;
            _hadUiSound = GameObject.Find("UiSound") != null;
            _draftRoot = Path.Combine(Path.GetTempPath(), "bbs-ship-editor-" + Guid.NewGuid().ToString("N"));
            EditorShell.CancelOwnedElsewhere = () => false; // static flags of other screens are not this test's subject
            LogAssert.ignoreFailingMessages = true;          // scene set-up logs (no content, no atlas) are not either
        }

        [TearDown]
        public void TearDown()
        {
            EditorShell.CancelOwnedElsewhere = EditorShell.DefaultCancelOwner;
            foreach (var go in _spawned.Where(go => go != null))
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            _spawned.Clear();
            var es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                UnityEngine.Object.DestroyImmediate(es.gameObject);
            }

            var sound = GameObject.Find("UiSound");
            if (!_hadUiSound && sound != null)
            {
                UnityEngine.Object.DestroyImmediate(sound); // a button click plays the UI blip
            }

            LogAssert.ignoreFailingMessages = false;
            if (Directory.Exists(_draftRoot))
            {
                Directory.Delete(_draftRoot, recursive: true);
            }
        }

        // ---- helpers -------------------------------------------------------------------------------------------------

        /// <summary>A ship editor as AppShell opens it, its drafts in the test's temp folder.</summary>
        private ShipEditor StartEditor()
        {
            var go = new GameObject("ShipEditorUnderTest");
            _spawned.Add(go);
            var editor = go.AddComponent<ShipEditor>();
            editor.DraftRoot = _draftRoot;
            Call(editor, "Start");
            _spawned.Add(editor.EditorFrame.Canvas.gameObject); // OnDestroy does not run in edit mode
            return editor;
        }

        private static object Call(object target, string method, params object[] args)
            => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.Invoke(target, args);

        private static object Field(object target, string name)
            => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);

        private static int Cells(ShipEditor editor) => ((ICollection)Field(editor, "_design")).Count;

        private static bool Place(ShipEditor editor, int x, int y, int z) => (bool)Call(editor, "PlaceAt", new Vector3i(x, y, z), -1);

        private static bool Remove(ShipEditor editor, int x, int y, int z) => (bool)Call(editor, "RemoveAt", new Vector3i(x, y, z));

        private static string Label(Selectable control) => control.GetComponentInChildren<Text>(true)?.text;

        private static Button ButtonLabelled(ShipEditor editor, string label)
            => editor.EditorFrame.Canvas.GetComponentsInChildren<Button>(true).First(b => Label(b) == label);

        // ---- the frame -----------------------------------------------------------------------------------------------

        [Test]
        public void Header_HasEveryShipAction_AndSaveIsTheOnlyPrimaryButton()
        {
            var shell = StartEditor().EditorFrame;
            foreach (var button in new[] { shell.BackButton, shell.UndoButton, shell.RedoButton, shell.LoadButton, shell.NewButton,
                         shell.SubmitButton, shell.SaveButton })
            {
                Assert.IsNotNull(button, "Back, Undo / Redo, Load, New, Submit and Save sit in the header");
            }

            var texts = shell.GetComponentsInChildren<Text>(true);
            Assert.AreEqual("ui.menu.ship_editor", texts.Single(t => t.name == EditorShell.TitleName).text, "the start card's name");
            Assert.AreEqual("ui.editor.save_sub.ship", texts.Single(t => t.name == EditorShell.SublineName).text);

            var primaries = shell.Canvas.GetComponentsInChildren<Button>(true)
                .Where(b => b.targetGraphic is Image i && i.sprite == UiKit.PrimarySprite).ToList();
            CollectionAssert.Contains(primaries, shell.SaveButton);
            foreach (var other in primaries.Where(b => b != shell.SaveButton))
            {
                Assert.AreEqual("ui.ed.tool_place", Label(other), "besides Save only the selected tool segment is lit (a selected tab)");
            }

            Assert.AreEqual(0, shell.GetComponentsInChildren<Text>(true).Count(t => t.text == "ui.struct.save" || t.text == "ui.struct.load"),
                "the old SAVE / EXPORT and LOAD buttons are gone");
        }

        [Test]
        public void PadFirstFocus_IsThePlaceTool_NeverBack()
        {
            var shell = StartEditor().EditorFrame;
            var first = shell.Canvas.GetComponent<UiNavFocus>().FocusTarget();
            Assert.AreNotSame(shell.BackButton, first);
            Assert.AreEqual("ui.ed.tool_place", Label(first), "a pad starts on the first tool");
        }

        [Test]
        public void Cards_LeaveThe3DViewFree_AndTheTilesAreChildSized()
        {
            var shell = StartEditor().EditorFrame;
            foreach (Transform child in shell.WorkArea)
            {
                if (!child.gameObject.activeSelf || child.name == "Reticle")
                {
                    continue;
                }

                var rt = (RectTransform)child;
                float left = rt.anchoredPosition.x, right = left + rt.sizeDelta.x;
                Assert.IsTrue(right <= 440f || left >= 1500f, $"{child.name} ({left:0}–{right:0}) stays out of the 3D view");
            }

            var light = shell.Canvas.GetComponentsInChildren<Text>(true).First(t => t.text == "ui.part.light");
            Assert.GreaterOrEqual(((RectTransform)light.transform.parent).sizeDelta.y, UiKit.MinTapSize, "a palette tile is a tap target");
            Assert.GreaterOrEqual(light.fontSize, 16, "a tile's name is at least 16 px");
        }

        [Test]
        public void RemoveTool_IsASwitchInThePaletteCard()
        {
            var editor = StartEditor();
            Assert.AreEqual("Place", Prop(editor, "Tool").ToString());
            ButtonLabelled(editor, "ui.ed.tool_remove").onClick.Invoke();
            Assert.AreEqual("Remove", Prop(editor, "Tool").ToString(), "a click removes now — no middle mouse button needed");
        }

        private static object Prop(object target, string name)
            => target.GetType().GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);

        // ---- undo / redo ---------------------------------------------------------------------------------------------

        [Test]
        public void Undo_TakesPlacedBlocksBack_AndRedoPutsThemBack_WithTheRightUnsavedAnswer()
        {
            var editor = StartEditor();
            Assert.IsFalse(editor.IsDirty);
            Assert.IsTrue(Place(editor, 10, 0, 10));
            Assert.IsTrue(Place(editor, 11, 0, 10));
            Assert.IsFalse(Place(editor, 11, 0, 10), "an occupied cell is no step");
            Assert.AreEqual(2, Cells(editor));
            Assert.IsTrue(editor.IsDirty);

            editor.EditorFrame.Undo();
            Assert.AreEqual(1, Cells(editor), "one placed block per step");
            editor.EditorFrame.Undo();
            Assert.AreEqual(0, Cells(editor));
            Assert.IsFalse(editor.IsDirty, "undone back to the opened state: nothing unsaved");
            Assert.IsFalse(editor.CanUndo);
            Assert.IsTrue(editor.CanRedo);

            editor.EditorFrame.Redo();
            Assert.AreEqual(1, Cells(editor));
            Assert.IsTrue(editor.IsDirty);
            editor.EditorFrame.Tick(0);
            Assert.IsTrue(editor.EditorFrame.UndoButton.interactable, "the header follows the history");
        }

        [Test]
        public void Undo_BringsARemovedBlockBack()
        {
            var editor = StartEditor();
            Place(editor, 10, 0, 10);
            Assert.IsTrue(Remove(editor, 10, 0, 10));
            Assert.IsFalse(Remove(editor, 10, 0, 10), "nothing there: no step");
            Assert.AreEqual(0, Cells(editor));
            editor.Undo();
            Assert.AreEqual(1, Cells(editor));
        }

        // ---- drafts --------------------------------------------------------------------------------------------------

        [Test]
        public void Draft_RoundTrip_RestoresCellsAndName_AsUnsavedWork()
        {
            var first = StartEditor();
            Place(first, 10, 0, 10);
            Place(first, 10, 1, 10);
            first.GetType().GetField("_shipName", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(first, "Blitz");
            string draft = first.SaveDraft();
            StringAssert.StartsWith("bbs-ship-draft/1\n", draft, "a version-prefixed draft");

            var second = StartEditor();
            Assert.IsTrue(second.LoadDraft(draft));
            Assert.AreEqual(2, Cells(second));
            Assert.AreEqual("Blitz", second.DocumentName);
            Assert.IsTrue(second.IsDirty, "a restored draft is unsaved work");
            Assert.IsFalse(second.CanUndo, "a draft starts its own history");

            Assert.IsFalse(second.LoadDraft("{\"layout\":{}}"), "no version line: not a ship draft");
            Assert.IsFalse(second.LoadDraft("bbs-ship-draft/1\nnot json"), "unreadable: refused, never thrown");
            Assert.IsNull(StartEditor().SaveDraft(), "an empty room is nothing worth keeping");
        }

        [Test]
        public void Draft_IsWrittenWhileUnsaved_AndOfferedWhenTheEditorOpensAgain()
        {
            var first = StartEditor();
            Place(first, 10, 0, 10); // announces the change on the shell's clock (Time.unscaledTimeAsDouble)
            first.EditorFrame.Tick(Time.unscaledTimeAsDouble + EditorDraftStore.DefaultQuietSeconds + 0.5);
            Assert.IsTrue(first.EditorFrame.DraftStore.HasDraft, "unsaved work is kept as a draft");

            var second = StartEditor();
            Assert.IsTrue(second.EditorFrame.IsDialogOpen, "reopening asks: continue your draft?");
            second.EditorFrame.OpenDialogNow.Yes.onClick.Invoke();
            Assert.AreEqual(1, Cells(second), "Continue brings the ship back");
            Assert.IsTrue(second.IsDirty);
        }

        // ---- Esc / pad B ---------------------------------------------------------------------------------------------

        [Test]
        public void Esc_ClosesThePickersOneByOne_AndNeverTheEditor()
        {
            var editor = StartEditor();
            var shell = editor.EditorFrame;
            Call(editor, "OpenLoadPicker");
            Assert.IsNotNull(Field(editor, "_loadPicker"));
            Assert.AreEqual(EditorCancelResult.ClosedLayer, shell.HandleCancel(false), "the load picker closes first");
            Assert.IsNull(Field(editor, "_loadPicker"));

            Call(editor, "OpenFormPicker");
            Assert.AreEqual(EditorCancelResult.ClosedLayer, shell.HandleCancel(false), "then the form picker");
            Assert.IsNull(Field(editor, "_formPicker"));

            Assert.AreEqual(EditorCancelResult.PointedAtBack, shell.HandleCancel(false), "nothing open: the editor stays");
            Assert.IsTrue(editor != null && editor.isActiveAndEnabled, "Esc / B never leave the ship editor — only Back does");
        }

        // ---- New -----------------------------------------------------------------------------------------------------

        [Test]
        public void New_OverUnsavedWork_AsksFirst_ThenStartsFromScratch()
        {
            var editor = StartEditor();
            Place(editor, 10, 0, 10);
            editor.EditorFrame.NewButton.onClick.Invoke();
            Assert.AreEqual(1, Cells(editor), "not before the question");
            editor.EditorFrame.OpenDialogNow.Yes.onClick.Invoke();
            Assert.AreEqual(0, Cells(editor));
            Assert.IsFalse(editor.IsDirty);
            Assert.IsFalse(editor.CanUndo, "a new ship starts a new history");
        }
    }
}
