// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The two developer editors on the editor shell (#3694): the item &amp; recipe editor and the material editor built for
    /// real (edit mode runs no Start / Update, so the tests start them by reflection, without an AppShell — every text is
    /// its locale key). Pins the header (Back, Undo / Redo, New, ONE primary Save, the developer badge, no Load / Submit),
    /// the pad's first focus on the key field, Esc closing the top layer and never the editor, undo / redo of a field, a
    /// cycle, a list row and a paint stroke, the draft round trip, the key check in red and Save's export and overwrite
    /// question.
    /// </summary>
    public sealed class DevEditorsEditModeTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private bool _hadEventSystem, _hadUiSound;
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null;
            _hadUiSound = GameObject.Find("UiSound") != null;
            _root = Path.Combine(Path.GetTempPath(), "bbs-dev-editors-" + Guid.NewGuid().ToString("N"));
            EditorShell.CancelOwnedElsewhere = () => false; // static flags of other screens are not this test's subject
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
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<EditorShell>(FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            }

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

            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        // ---- building ------------------------------------------------------------------------------------------------------

        private T Start<T>() where T : MonoBehaviour
        {
            var go = new GameObject(typeof(T).Name + "UnderTest");
            _spawned.Add(go);
            var editor = go.AddComponent<T>();
            typeof(T).GetField("DraftRoot").SetValue(editor, Path.Combine(_root, "drafts"));
            typeof(T).GetField("ExportRoot").SetValue(editor, Path.Combine(_root, "exports"));
            typeof(T).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor, null);
            return editor;
        }

        private static EditorShell ShellOf(MonoBehaviour editor)
            => (EditorShell)editor.GetType().GetProperty("EditorShellForTests").GetValue(editor);

        private static IEditorDocument Doc(MonoBehaviour editor) => (IEditorDocument)editor;

        /// <summary>Primary-tier buttons — the paint tool bar's selected segment aside: a tab drawn the template's way (the
        /// mock's bar too), a state rather than an action.</summary>
        private static int CountPrimary(Component root)
            => root.GetComponentsInChildren<Button>(true).Count(b => b.targetGraphic is Image i && i.sprite == UiKit.PrimarySprite
                                                                    && !b.name.StartsWith(MaterialEditor.PaintToolName, StringComparison.Ordinal));

        /// <summary>The value text of the <c>‹ value ›</c> cycle whose label (above or left of it) says <paramref name="labelKey"/>.</summary>
        private static Text CycleNear(EditorShell shell, string labelKey)
        {
            var label = shell.WorkArea.GetComponentsInChildren<Text>(true).First(t => t.text == labelKey);
            var labelPos = ((RectTransform)label.transform).anchoredPosition;
            return label.transform.parent.GetComponentsInChildren<Button>(true)
                .Where(b => b.GetComponent<StepOnMove>() != null && b.GetComponentInChildren<Text>(true) != null)
                .Select(b => (Button: b, Pos: ((RectTransform)b.transform).anchoredPosition))
                .Where(x => x.Pos.y <= labelPos.y + 1f && x.Pos.y >= labelPos.y - 60f && x.Pos.x >= labelPos.x - 1f)
                .OrderBy(x => labelPos.y - x.Pos.y).ThenBy(x => x.Pos.x)
                .Select(x => x.Button.GetComponentInChildren<Text>(true))
                .First();
        }

        private static void StepCycle(Text cycleValue, int dir)
            => cycleValue.GetComponentInParent<Button>().GetComponent<StepOnMove>().OnMove(new AxisEventData(EventSystem.current)
            {
                moveDir = dir < 0 ? MoveDirection.Left : MoveDirection.Right,
            });

        // ---- the header ----------------------------------------------------------------------------------------------------

        [TestCase(typeof(ContentEditor), "ui.menu.content_editor", "ui.editor.save_sub.content")]
        [TestCase(typeof(MaterialEditor), "ui.menu.material_editor", "ui.editor.save_sub.material")]
        public void Header_HasBackUndoRedoNewAndOnePrimarySave_WithTheDeveloperBadge(Type type, string title, string subline)
        {
            var editor = (MonoBehaviour)typeof(DevEditorsEditModeTests).GetMethod(nameof(Start), BindingFlags.NonPublic | BindingFlags.Instance)
                .MakeGenericMethod(type).Invoke(this, null);
            var shell = ShellOf(editor);
            Assert.IsNotNull(shell.BackButton);
            Assert.IsNotNull(shell.UndoButton, "undo / redo over the whole definition");
            Assert.IsNotNull(shell.RedoButton);
            Assert.IsNotNull(shell.NewButton);
            Assert.IsNull(shell.LoadButton, "the developer editors have nothing to load");
            Assert.IsNull(shell.SubmitButton, "nothing to submit to the developers — they are the developers");
            Assert.AreEqual(1, CountPrimary(shell.Canvas), "Save is the one primary button");
            Assert.AreEqual("ui.editor.save", shell.SaveButton.GetComponentInChildren<Text>().text);
            var texts = shell.GetComponentsInChildren<Text>(true);
            Assert.AreEqual(title, texts.Single(t => t.name == EditorShell.TitleName).text, "the title is the start card's name");
            Assert.AreEqual(subline, texts.Single(t => t.name == EditorShell.SublineName).text);
            var badge = shell.Canvas.transform.Find(EditorShell.HeaderName + "/" + DevEditorForm.BadgeName);
            Assert.IsNotNull(badge, "the DEVELOPER badge sits in the header");
            Assert.AreEqual("UI.EDITOR.BADGE_DEV", badge.GetComponentInChildren<Text>().text);
            Assert.AreSame(shell.BackButton.transform, shell.BackButton.transform.parent.GetChild(shell.BackButton.transform.parent.childCount - 1),
                "Back stays the header's last control");
            Assert.IsFalse(shell.GetComponentsInChildren<Text>(true).Any(t => t.text == "ui.content.save" || t.text == "ui.material.save"),
                "the old SAVE / EXPORT button is gone");
        }

        [Test]
        public void Material_TextureCard_HasTheTextureEditorsPaintTools_InTheMocksOrder()
        {
            var shell = ShellOf(Start<MaterialEditor>());
            var tools = Enumerable.Range(0, 4)
                .Select(i => shell.WorkArea.GetComponentsInChildren<Button>(true).Single(b => b.name == MaterialEditor.PaintToolName + i))
                .Select(b => b.GetComponentInChildren<Text>(true).text)
                .ToArray();
            CollectionAssert.AreEqual(new[] { "ui.tex.tool.brush", "ui.tex.tool.fill", "ui.tex.tool.pipette", "ui.tex.tool.eraser" }, tools,
                "brush, fill, pick a colour, eraser — the texture editor's words");
        }

        [Test]
        public void PadFirstFocus_IsTheKeyField_NeverBack()
        {
            foreach (var editor in new MonoBehaviour[] { Start<ContentEditor>(), Start<MaterialEditor>() })
            {
                var shell = ShellOf(editor);
                var key = (InputField)editor.GetType().GetProperty("KeyFieldForTests").GetValue(editor);
                var focus = shell.Canvas.GetComponent<UiNavFocus>().FocusTarget();
                Assert.AreSame(key, focus, $"{editor.GetType().Name}: the first field gets the first focus");
                Assert.AreNotSame(shell.BackButton, focus);
            }
        }

        [Test]
        public void CyclesAndSteppers_AreOnePadStopEach_TheArrowsAreNoStops()
        {
            var shell = ShellOf(Start<ContentEditor>());
            var buttons = shell.WorkArea.GetComponentsInChildren<Button>(true);
            var arrows = buttons.Where(b => b.GetComponentInChildren<Text>(true)?.text is "‹" or "›" or "−" or "+").ToList();
            Assert.IsNotEmpty(arrows);
            Assert.IsTrue(arrows.All(b => b.navigation.mode == Navigation.Mode.None), "‹ › − + are mouse targets, not pad stops");
            var stops = buttons.Where(b => b.GetComponent<StepOnMove>() != null).ToList();
            Assert.GreaterOrEqual(stops.Count, 20, "every cycle and stepper has its one stop");
            Assert.IsTrue(stops.All(b => b.navigation.mode == Navigation.Mode.Vertical), "Left / Right step the value, Up / Down leave it");
            foreach (var b in buttons.Where(b => b.gameObject.activeInHierarchy))
            {
                var size = ((RectTransform)b.transform).sizeDelta;
                Assert.GreaterOrEqual(Mathf.Min(size.x, size.y), 44f, $"{b.name} is at least 44 px");
            }
        }

        // ---- Esc / pad B ---------------------------------------------------------------------------------------------------

        [Test]
        public void Esc_ClosesTheShellsDialogFirst_ThenPointsAtBack_AndNeverLeaves()
        {
            var editor = Start<ContentEditor>();
            var shell = ShellOf(editor);
            CycleNear(shell, "ui.content.category"); // the form is there
            StepCycle(CycleNear(shell, "ui.content.category"), +1);
            Assert.IsTrue(Doc(editor).IsDirty);
            shell.NewButton.onClick.Invoke();
            Assert.IsTrue(shell.IsDialogOpen, "New over unsaved work asks first");

            Assert.AreEqual(EditorCancelResult.ClosedDialog, shell.HandleCancel(false), "the question closes first (Cancel)");
            Assert.IsTrue(Doc(editor).IsDirty, "Esc on the question keeps the work");
            Assert.AreEqual(EditorCancelResult.PointedAtBack, shell.HandleCancel(false), "the editor has no layer of its own");
            Assert.IsTrue(editor != null && shell != null, "the editor is still open");
            Assert.AreEqual("ui.editor.leave_hint", shell.StatusText.text);

            var material = Start<MaterialEditor>();
            Assert.IsFalse(Doc(material).CloseTopLayer(), "outside the pad's painting mode there is nothing to close");
        }

        // ---- undo / redo ---------------------------------------------------------------------------------------------------

        [Test]
        public void Content_UndoRedo_CoverAFieldACycleAndAListRow()
        {
            var editor = Start<ContentEditor>();
            var shell = ShellOf(editor);
            var key = editor.KeyFieldForTests;
            key.text = "pick";
            key.text = "pickaxe"; // typing in one field is one step
            key.onEndEdit.Invoke(key.text);
            var category = CycleNear(shell, "ui.content.category");
            string before = category.text;
            StepCycle(category, +1);
            Assert.AreNotEqual(before, category.text);
            int rows = ActiveRows(shell);
            shell.WorkArea.GetComponentsInChildren<Button>(true).First(b => b.GetComponentInChildren<Text>(true)?.text == "ui.content.add")
                .onClick.Invoke();
            Assert.AreEqual(rows + 1, ActiveRows(shell));

            shell.Undo();
            Assert.AreEqual(rows, ActiveRows(shell), "the added row goes again");
            shell.Undo();
            Assert.AreEqual(before, CycleNear(shell, "ui.content.category").text, "the category steps back");
            shell.Undo();
            Assert.AreEqual("my_item", key.text, "the typed key is one step");
            Assert.IsFalse(Doc(editor).IsDirty, "undone back to the start: nothing unsaved");
            Assert.IsFalse(Doc(editor).CanUndo);

            shell.Redo();
            Assert.AreEqual("pickaxe", key.text);
            Assert.IsTrue(Doc(editor).IsDirty);
        }

        private static int ActiveRows(EditorShell shell)
            => shell.WorkArea.GetComponentsInChildren<LayoutElement>(false).Count(e => e.name == "Row");

        [Test]
        public void Material_UndoRedo_CoverAStrokeAndAProperty()
        {
            var editor = Start<MaterialEditor>();
            var shell = ShellOf(editor);
            byte[] start = (byte[])editor.PixelsForTests.Clone();
            editor.PaintStrokeForTests(10, 10, 20, 10);
            Assert.IsFalse(start.SequenceEqual(editor.PixelsForTests), "the stroke painted");
            var hardness = CycleNear(shell, "ui.material.hardness");
            string hard = hardness.text;
            StepCycle(hardness, +1);

            shell.Undo();
            Assert.AreEqual(hard, CycleNear(shell, "ui.material.hardness").text);
            shell.Undo();
            Assert.IsTrue(start.SequenceEqual(editor.PixelsForTests), "the whole stroke is one step");
            Assert.IsFalse(Doc(editor).IsDirty);
            shell.Redo();
            Assert.IsFalse(start.SequenceEqual(editor.PixelsForTests));
        }

        // ---- drafts ----------------------------------------------------------------------------------------------------------

        [Test]
        public void Content_Draft_RoundTrip_ThroughTheShell()
        {
            var first = Start<ContentEditor>();
            first.KeyFieldForTests.text = "space_pick";
            StepCycle(CycleNear(ShellOf(first), "ui.content.station"), +1);
            string station = CycleNear(ShellOf(first), "ui.content.station").text;
            string draft = Doc(first).SaveDraft();
            StringAssert.StartsWith(CanvasDraftFormat.Magic + " content v" + ContentEditor.DraftVersion + "\n", draft, "version-prefixed");
            ShellOf(first).Leave(); // Back keeps the work as a draft

            var second = Start<ContentEditor>();
            var shell = ShellOf(second);
            Assert.IsTrue(shell.IsDialogOpen, "the next opening offers the draft");
            shell.OpenDialogNow.Yes.onClick.Invoke();
            Assert.AreEqual("space_pick", second.KeyFieldForTests.text);
            Assert.AreEqual(station, CycleNear(shell, "ui.content.station").text);
            Assert.IsTrue(Doc(second).IsDirty, "a restored draft is unsaved work");
            Assert.IsFalse(Doc(second).LoadDraft(CanvasDraftFormat.Wrap("material", 1, "{}")), "another editor's draft is refused");
            Assert.IsFalse(Doc(second).LoadDraft(CanvasDraftFormat.Wrap("content", 1, "not json")), "a broken one too");
        }

        [Test]
        public void Material_Draft_RoundTrip_KeepsPixelsAndProperties()
        {
            var first = Start<MaterialEditor>();
            first.PaintStrokeForTests(5, 5, 30, 40);
            StepCycle(CycleNear(ShellOf(first), "ui.material.gloss"), +1);
            string gloss = CycleNear(ShellOf(first), "ui.material.gloss").text;
            byte[] pixels = (byte[])first.PixelsForTests.Clone();
            string draft = Doc(first).SaveDraft();
            StringAssert.StartsWith(CanvasDraftFormat.Magic + " material v" + MaterialEditor.DraftVersion + "\n", draft);

            var second = Start<MaterialEditor>();
            Assert.IsTrue(Doc(second).LoadDraft(draft));
            Assert.IsTrue(pixels.SequenceEqual(second.PixelsForTests), "every pixel comes back");
            Assert.AreEqual(gloss, CycleNear(ShellOf(second), "ui.material.gloss").text);
            Assert.IsTrue(Doc(second).IsDirty);
            Assert.IsFalse(Doc(second).LoadDraft(CanvasDraftFormat.Wrap("material", 1, "{\"material\":{},\"pixels\":\"AAAA\"}")),
                "a tile of the wrong size is refused");
        }

        // ---- checks and Save -------------------------------------------------------------------------------------------------

        [Test]
        public void KeyWithASpace_IsARedStatusWithWhatToDo_AndSaveWaits()
        {
            var editor = Start<MaterialEditor>();
            var shell = ShellOf(editor);
            editor.KeyFieldForTests.text = "mein material";
            Assert.AreEqual(EditorStatusSeverity.Error, shell.StatusSeverity);
            Assert.AreEqual("ui.ed.check.key_space", shell.StatusText.text, "the space gets its own message");
            Assert.AreEqual(UiKit.DangerCol, shell.StatusText.color, "red, never green");
            shell.Save();
            Assert.IsFalse(Directory.Exists(Path.Combine(_root, "exports")), "nothing is written");
            Assert.AreEqual(EditorStatusSeverity.Error, shell.StatusSeverity);

            editor.KeyFieldForTests.text = "mein_material";
            Assert.AreEqual(string.Empty, shell.StatusText.text, "fixed: the error goes again");
        }

        [Test]
        public void Content_CheckCard_ListsErrorsAndTheAllSetLine()
        {
            var editor = Start<ContentEditor>();
            Assert.AreEqual(EditorStatusSeverity.Success, editor.ChecksForTests[0].Severity, "the fresh item has every required field");
            editor.KeyFieldForTests.text = "My-Item";
            var lines = editor.ChecksForTests;
            Assert.AreEqual((EditorStatusSeverity.Error, "ui.ed.check.key_chars"), lines[0]);
            Assert.IsFalse(lines.Any(l => l.Severity == EditorStatusSeverity.Success), "no \"all set\" next to an error");
            Assert.AreEqual(EditorStatusSeverity.Error, ShellOf(editor).StatusSeverity);
        }

        [Test]
        public void Save_ExportsWithoutAPathInTheStatus_AndAsksBeforeReplacingAnOlderExport()
        {
            var first = Start<ContentEditor>();
            var shell = ShellOf(first);
            first.KeyFieldForTests.text = "space_pick";
            shell.Save();
            string file = Path.Combine(_root, "exports", "content_exports", "space_pick", "content.json");
            Assert.IsTrue(File.Exists(file));
            Assert.AreEqual(EditorStatusSeverity.Success, shell.StatusSeverity);
            StringAssert.DoesNotContain(_root, shell.StatusText.text, "no file paths in the status bar");
            Assert.IsFalse(Doc(first).IsDirty);
            StringAssert.Contains("\"key\": \"space_pick\"", File.ReadAllText(file));

            shell.Save();
            Assert.IsFalse(shell.IsDialogOpen, "saving again in the same session needs no question");

            var second = Start<ContentEditor>();
            var shell2 = ShellOf(second);
            second.KeyFieldForTests.text = "space_pick";
            File.WriteAllText(file, "older");
            shell2.Save();
            Assert.IsTrue(shell2.IsDialogOpen, "an export from another session is replaced only after a question");
            Assert.AreEqual("older", File.ReadAllText(file));
            Assert.IsTrue(UiKit.IsDangerButton(shell2.OpenDialogNow.Yes));
            shell2.OpenDialogNow.Yes.onClick.Invoke();
            StringAssert.Contains("space_pick", File.ReadAllText(file));
            Assert.AreEqual(EditorStatusSeverity.Success, shell2.StatusSeverity);
            Assert.IsFalse(shell2.DraftStore.HasDraft, "saved work needs no draft");
        }

        [Test]
        public void Material_Save_WritesTheBundleAndTheTile()
        {
            var editor = Start<MaterialEditor>();
            editor.PaintStrokeForTests(0, 0, 63, 63);
            ShellOf(editor).Save();
            string dir = Path.Combine(_root, "exports", "material_exports", "my_material");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "material.json")));
            CollectionAssert.AreEqual(editor.PixelsForTests, File.ReadAllBytes(Path.Combine(dir, "texture.bytes")),
                "the raw tile the merge tool reads");
            Assert.AreEqual(EditorStatusSeverity.Success, ShellOf(editor).StatusSeverity);
        }
    }
}
