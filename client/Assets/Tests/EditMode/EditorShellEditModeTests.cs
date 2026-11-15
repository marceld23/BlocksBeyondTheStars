// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The editor shell (#3688, #3689, #3690) built for real on a test canvas with a fake document (edit mode runs no
    /// Update, so the tests call <see cref="EditorShell.Tick"/> with their own clock). Pins the header (order, the ONE
    /// primary Save, optional buttons only when provided, sizes), the pad's first focus (never Back), the status colours
    /// (errors never green, no paths), the draft question and the Esc order (topmost layer first, never the editor).
    /// </summary>
    public sealed class EditorShellEditModeTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private bool _hadEventSystem, _hadUiSound;
        private string _draftRoot;

        /// <summary>A document the tests steer by hand.</summary>
        private sealed class FakeDocument : IEditorDocument
        {
            public string Name = "My cruiser";
            public bool Dirty;
            public bool Undoable, Redoable;
            public int Undos, Redos, LayersOpen;
            public string Work = "cells:12";
            public string Loaded;
            public bool LoadWorks = true;

            public string DocumentName => Name;

            public bool IsDirty => Dirty;

            public bool CanUndo => Undoable;

            public bool CanRedo => Redoable;

            public void Undo() => Undos++;

            public void Redo() => Redos++;

            public string SaveDraft() => Work;

            public bool LoadDraft(string draft)
            {
                Loaded = draft;
                Dirty = LoadWorks;
                return LoadWorks;
            }

            public bool CloseTopLayer()
            {
                if (LayersOpen == 0)
                {
                    return false;
                }

                LayersOpen--;
                return true;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null;
            _hadUiSound = GameObject.Find("UiSound") != null;
            _draftRoot = Path.Combine(Path.GetTempPath(), "bbs-editor-shell-" + Guid.NewGuid().ToString("N"));
            // The submit dialog's and the canvas mode's static cancel flags may be left set by other tests (edit mode does
            // not advance frames): the Esc order is pinned here on its own.
            EditorShell.CancelOwnedElsewhere = () => false;
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
                UnityEngine.Object.DestroyImmediate(sound);
            }

            if (Directory.Exists(_draftRoot))
            {
                Directory.Delete(_draftRoot, recursive: true);
            }
        }

        private EditorShell Build(FakeDocument doc, bool all = true, Action onBack = null, Func<bool> onSave = null, Action onNew = null)
        {
            var canvas = UiKit.CreateCanvas("EditorShellTest");
            _spawned.Add(canvas.gameObject);
            var options = new EditorShellOptions
            {
                EditorKey = "test",
                TitleKey = "ui.menu.ship_editor",
                SaveSublineKey = "ui.editor.save_sub.ship",
                OnBack = onBack ?? (() => { }),
                OnSave = onSave ?? (() => true),
                HasUndo = all,
                OnLoad = all ? () => { } : null,
                OnNew = all ? onNew ?? (() => { }) : null,
                OnSubmit = all ? () => { } : null,
                DraftRoot = _draftRoot,
                Localize = key => key,
            };
            return EditorShell.Build(canvas, doc, options);
        }

        private static float X(Component c) => ((RectTransform)c.transform).anchoredPosition.x;

        private static int CountPrimary(Component root)
            => root.GetComponentsInChildren<Button>(true).Count(b => b.targetGraphic is Image i && i.sprite == UiKit.PrimarySprite);

        // ---- header --------------------------------------------------------------------------------------------------

        [Test]
        public void Header_HasBackTitleActionsAndSave_LeftToRight_WithOnePrimarySave()
        {
            var shell = Build(new FakeDocument());
            var order = new Component[]
            {
                shell.BackButton, shell.UndoButton, shell.RedoButton, shell.LoadButton, shell.NewButton, shell.SubmitButton,
                shell.SaveButton,
            };
            for (int i = 1; i < order.Length; i++)
            {
                Assert.Less(X(order[i - 1]), X(order[i]), $"{order[i - 1].name} sits left of {order[i].name}");
            }

            Assert.AreEqual(1, CountPrimary(shell.Canvas), "exactly one primary button on the editor: Save");
            Assert.AreSame(UiKit.PrimarySprite, ((Image)shell.SaveButton.targetGraphic).sprite);
            Assert.AreEqual("ui.editor.save", shell.SaveButton.GetComponentInChildren<Text>().text);
            Assert.AreEqual("ui.menu.back", shell.BackButton.GetComponentInChildren<Text>().text, "Back reuses the menus' word");
            var subline = shell.GetComponentsInChildren<Text>(true).Single(t => t.name == EditorShell.SublineName);
            Assert.AreEqual("ui.editor.save_sub.ship", subline.text, "Save carries the line that says what it does");
        }

        [Test]
        public void Header_OptionalButtons_OnlyWhenTheEditorProvidesThem()
        {
            var shell = Build(new FakeDocument(), all: false);
            Assert.IsNull(shell.UndoButton);
            Assert.IsNull(shell.RedoButton);
            Assert.IsNull(shell.LoadButton);
            Assert.IsNull(shell.NewButton);
            Assert.IsNull(shell.SubmitButton);
            var names = shell.GetComponentsInChildren<Button>(true).Select(b => b.name).ToList();
            CollectionAssert.DoesNotContain(names, EditorShell.UndoName);
            CollectionAssert.DoesNotContain(names, EditorShell.LoadName);
            Assert.IsNotNull(shell.SaveButton);
            Assert.IsNotNull(shell.BackButton);
        }

        [Test]
        public void Header_ControlsAndTexts_MeetTheMinimumSizes()
        {
            var shell = Build(new FakeDocument { Dirty = true });
            shell.SetStatus("A message", EditorStatusSeverity.Info);
            foreach (var button in shell.GetComponentsInChildren<Button>(true))
            {
                var size = ((RectTransform)button.transform).sizeDelta;
                Assert.GreaterOrEqual(size.y, EditorShellLayout.MinControl, $"{button.name} is at least 44 high");
                Assert.GreaterOrEqual(size.x, EditorShellLayout.MinControl, $"{button.name} is at least 44 wide");
            }

            foreach (var text in shell.GetComponentsInChildren<Text>(true))
            {
                int smallest = text.resizeTextForBestFit ? text.resizeTextMinSize : text.fontSize;
                Assert.GreaterOrEqual(smallest, EditorShellLayout.MinTextSize, $"'{text.name}' ({text.text}) is at least 16 px");
            }
        }

        [Test]
        public void Undo_FollowsTheDocument_AndCallsIt()
        {
            var doc = new FakeDocument();
            var shell = Build(doc);
            Assert.IsFalse(shell.UndoButton.interactable, "nothing to undo: greyed");
            doc.Undoable = true;
            shell.Tick(0);
            Assert.IsTrue(shell.UndoButton.interactable);
            Assert.IsFalse(shell.RedoButton.interactable);
            shell.UndoButton.onClick.Invoke();
            Assert.AreEqual(1, doc.Undos);
        }

        [Test]
        public void UnsavedMarker_ShowsOnlyWhileDirty()
        {
            var doc = new FakeDocument();
            var shell = Build(doc);
            var unsaved = shell.GetComponentsInChildren<Text>(true).Single(t => t.name == EditorShell.UnsavedName);
            Assert.IsFalse(unsaved.enabled);
            doc.Dirty = true;
            shell.Tick(0);
            Assert.IsTrue(unsaved.enabled, "unsaved work says so under the title");
        }

        // ---- pad -----------------------------------------------------------------------------------------------------

        [Test]
        public void PadFirstFocus_IsNeverBack()
        {
            var shell = Build(new FakeDocument());
            var nav = shell.Canvas.GetComponent<UiNavFocus>();
            Assert.IsNotNull(nav, "the shell enables pad navigation on its canvas");
            Assert.AreNotSame(shell.BackButton, nav.FocusTarget(), "A on arrival must not leave the editor");
            Assert.AreSame(shell.BackButton.transform.parent.GetChild(shell.BackButton.transform.parent.childCount - 1),
                shell.BackButton.transform, "Back is the header's last control");
        }

        [Test]
        public void PadFirstFocus_IsTheEditorsOwnContent()
        {
            var shell = Build(new FakeDocument());
            var tool = UiKit.AddButton(shell.AddSideCard(false, 300f), 16f, 16f, 200f, 48f, "Brush", null);
            Assert.AreSame(tool, shell.Canvas.GetComponent<UiNavFocus>().FocusTarget(),
                "the work area sits before the header: the editor's first control is the first focus");
        }

        // ---- status --------------------------------------------------------------------------------------------------

        [TestCase(EditorStatusSeverity.Info)]
        [TestCase(EditorStatusSeverity.Success)]
        [TestCase(EditorStatusSeverity.Warning)]
        [TestCase(EditorStatusSeverity.Error)]
        public void Status_IsColouredBySeverity(EditorStatusSeverity severity)
        {
            var shell = Build(new FakeDocument());
            shell.SetStatus("Something happened", severity);
            Assert.AreEqual(EditorShell.SeverityColor(severity), shell.StatusText.color);
            Assert.AreEqual(severity, shell.StatusSeverity);
            if (severity != EditorStatusSeverity.Success)
            {
                Assert.AreNotEqual(UiKit.Ok, shell.StatusText.color, "only success is green");
            }
        }

        [Test]
        public void Status_ShowsNoFilePaths()
        {
            var shell = Build(new FakeDocument());
            shell.SetStatus("Saved 'Cruiser' to:\nC:\\Users\\Kid Name\\AppData\\LocalLow\\JuMaVe\\ships\\cruiser.json\nFor everyone?",
                EditorStatusSeverity.Success);
            StringAssert.DoesNotContain("Users", shell.StatusText.text);
            StringAssert.DoesNotContain("\n", shell.StatusText.text);
            StringAssert.Contains("cruiser.json", shell.StatusText.text);
        }

        // ---- drafts --------------------------------------------------------------------------------------------------

        [Test]
        public void Draft_IsWrittenAfterARest_AndOfferedOnTheNextOpening()
        {
            var doc = new FakeDocument { Dirty = true };
            var shell = Build(doc);
            shell.NotifyChanged(10.0);
            shell.Tick(10.5);
            Assert.IsFalse(shell.DraftStore.HasDraft, "not while the work is still moving");
            shell.Tick(10.0 + EditorDraftStore.DefaultQuietSeconds + 0.1);
            Assert.IsTrue(shell.DraftStore.HasDraft, "written after a rest");

            var next = new FakeDocument { Name = string.Empty, Work = string.Empty };
            var reopened = Build(next);
            Assert.IsTrue(reopened.OfferDraft(), "the next opening offers it");
            var dialog = reopened.OpenDialogNow;
            Assert.IsNotNull(dialog);
            Assert.AreSame(dialog.Yes, reopened.Canvas.GetComponent<UiNavFocus>().FocusTarget(),
                "the pad lands on Continue — the safe choice");
            Assert.AreSame(UiKit.PrimarySprite, ((Image)dialog.Yes.targetGraphic).sprite);
            dialog.Yes.onClick.Invoke();
            Assert.AreEqual("cells:12", next.Loaded, "Continue gives the document its draft");
            Assert.IsTrue(next.IsDirty, "a restored draft is unsaved work");
            Assert.AreEqual(EditorStatusSeverity.Info, reopened.StatusSeverity);
        }

        [Test]
        public void Draft_AnEditorThatNeverAnnouncesChanges_StillGetsItsDraft_AndLosesItWhenTheWorkIsCleanAgain()
        {
            var doc = new FakeDocument { Dirty = true };
            var shell = Build(doc);
            shell.Tick(0);
            shell.Tick(EditorDraftStore.DefaultQuietSeconds + 0.1);
            Assert.IsTrue(shell.DraftStore.HasDraft, "dirty work is re-checked even without NotifyChanged");

            doc.Dirty = false; // undone back to the saved state
            shell.Tick(5);
            Assert.IsFalse(shell.DraftStore.HasDraft, "a draft of stale changes is not kept");
        }

        [Test]
        public void Draft_StartFresh_LoadsNothing_AndKeepsTheFileUntilNewWorkReplacesIt()
        {
            Build(new FakeDocument { Dirty = true }).Leave();
            var next = new FakeDocument();
            var shell = Build(next);
            Assert.IsTrue(shell.OfferDraft());
            shell.OpenDialogNow.No.onClick.Invoke();
            Assert.IsNull(next.Loaded);
            Assert.IsTrue(shell.DraftStore.HasDraft, "one mis-click does not destroy the old draft");
            shell.Tick(100);
            Assert.IsTrue(shell.DraftStore.HasDraft, "untouched fresh work does not remove it either");
        }

        [Test]
        public void Draft_EscOnTheQuestion_Continues()
        {
            Build(new FakeDocument { Dirty = true }).Leave();
            var next = new FakeDocument();
            var shell = Build(next);
            shell.OfferDraft();
            Assert.AreEqual(EditorCancelResult.ClosedDialog, shell.HandleCancel(false));
            Assert.AreEqual("cells:12", next.Loaded, "a stray Esc / B keeps the work");
        }

        [Test]
        public void Draft_Unreadable_IsSetAside_AndNeverBlocksTheEditor()
        {
            Directory.CreateDirectory(_draftRoot);
            File.WriteAllText(Path.Combine(_draftRoot, "test.draft"), "not a draft at all");
            var shell = Build(new FakeDocument());
            Assert.IsFalse(shell.OfferDraft(), "no question for a broken draft");
            Assert.IsFalse(shell.IsDialogOpen);
            Assert.IsFalse(shell.DraftStore.HasDraft);
            Assert.IsTrue(File.Exists(Path.Combine(_draftRoot, "test.draft.broken")), "set aside, not destroyed");
        }

        [Test]
        public void Draft_TheDocumentCannotRestoreIt_StartsFreshWithAWarning()
        {
            Build(new FakeDocument { Dirty = true }).Leave();
            var next = new FakeDocument { LoadWorks = false };
            var shell = Build(next);
            shell.OfferDraft();
            shell.OpenDialogNow.Yes.onClick.Invoke();
            Assert.AreEqual(EditorStatusSeverity.Warning, shell.StatusSeverity);
            Assert.IsFalse(shell.DraftStore.HasDraft, "not offered again");
        }

        [Test]
        public void Save_RemovesTheDraft_AndSaysSaved()
        {
            var doc = new FakeDocument { Dirty = true };
            var shell = Build(doc, onSave: () =>
            {
                doc.Dirty = false;
                return true;
            });
            shell.NotifyChanged(0);
            shell.Tick(60);
            Assert.IsTrue(shell.DraftStore.HasDraft);
            shell.SaveButton.onClick.Invoke();
            Assert.IsFalse(shell.DraftStore.HasDraft, "saved work needs no draft");
            Assert.AreEqual(EditorStatusSeverity.Success, shell.StatusSeverity);
            Assert.AreEqual("ui.editor.saved", shell.StatusText.text);
        }

        [Test]
        public void Save_Failing_IsAnError_NotGreen()
        {
            var shell = Build(new FakeDocument { Dirty = true }, onSave: () => false);
            shell.Save();
            Assert.AreEqual(EditorStatusSeverity.Error, shell.StatusSeverity);
            Assert.AreEqual(UiKit.DangerCol, shell.StatusText.color);
        }

        [Test]
        public void Back_WithUnsavedWork_KeepsADraft_AndLeavesWithoutAQuestion()
        {
            int left = 0;
            var shell = Build(new FakeDocument { Dirty = true }, onBack: () => left++);
            shell.BackButton.onClick.Invoke();
            Assert.AreEqual(1, left, "Back leaves at once");
            Assert.IsFalse(shell.IsDialogOpen);
            Assert.IsTrue(shell.DraftStore.HasDraft, "the work waits as a draft");
        }

        // ---- Esc / B and confirmations -------------------------------------------------------------------------------

        [Test]
        public void Esc_ClosesTheTopmostLayerFirst_AndNeverTheEditor()
        {
            int left = 0;
            var doc = new FakeDocument { LayersOpen = 1 };
            var shell = Build(doc, onBack: () => left++);
            shell.ConfirmDelete("Kit 1", () => Assert.Fail("Esc must not confirm a delete"));

            Assert.AreEqual(EditorCancelResult.TextField, shell.HandleCancel(textFieldRecent: true), "typing: nothing closes");
            Assert.IsTrue(shell.IsDialogOpen);
            Assert.AreEqual(EditorCancelResult.ClosedDialog, shell.HandleCancel(false), "the shell's dialog first");
            Assert.AreEqual(1, doc.LayersOpen);
            Assert.AreEqual(EditorCancelResult.ClosedLayer, shell.HandleCancel(false), "then the editor's picker / panel");
            Assert.AreEqual(0, doc.LayersOpen);
            Assert.AreEqual(EditorCancelResult.PointedAtBack, shell.HandleCancel(false), "then nothing closes");
            Assert.AreEqual(EditorCancelResult.PointedAtBack, shell.HandleCancel(false));
            Assert.AreEqual(0, left, "Esc / B never leave the editor — only Back does");
            Assert.AreEqual("ui.editor.leave_hint", shell.StatusText.text, "the status bar says how to leave");
        }

        [Test]
        public void ConfirmDelete_IsDangerAndCancel_AndOpensOnCancel()
        {
            bool deleted = false;
            var shell = Build(new FakeDocument());
            var dialog = shell.ConfirmDelete("Kit 1", () => deleted = true);
            Assert.IsTrue(UiKit.IsDangerButton(dialog.Yes), "the permanent action is red");
            Assert.AreSame(dialog.No, shell.Canvas.GetComponent<UiNavFocus>().FocusTarget(), "the pad lands on Cancel");
            Assert.AreEqual(shell.Canvas.transform.childCount - 1, dialog.Overlay.transform.GetSiblingIndex(),
                "drawn above every widget of the editor");
            foreach (var text in dialog.Overlay.GetComponentsInChildren<Text>(true))
            {
                int smallest = text.resizeTextForBestFit ? text.resizeTextMinSize : text.fontSize;
                Assert.GreaterOrEqual(smallest, EditorShellLayout.MinTextSize, $"'{text.text}' is at least 16 px");
            }

            dialog.Yes.onClick.Invoke();
            Assert.IsTrue(deleted);
            Assert.IsFalse(shell.IsDialogOpen);
        }

        [Test]
        public void New_OverUnsavedWork_AsksFirst()
        {
            int fresh = 0;
            var doc = new FakeDocument { Dirty = true };
            var shell = Build(doc, onNew: () => fresh++);
            shell.NewButton.onClick.Invoke();
            Assert.AreEqual(0, fresh, "not before the question");
            Assert.IsTrue(shell.IsDialogOpen);
            shell.OpenDialogNow.Yes.onClick.Invoke();
            Assert.AreEqual(1, fresh);

            doc.Dirty = false;
            shell.NewButton.onClick.Invoke();
            Assert.AreEqual(2, fresh, "nothing to lose: no question");
        }
    }
}
