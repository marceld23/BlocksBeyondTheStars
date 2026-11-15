// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
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
    /// The Station/Village editor on the editor shell (#3692): built for real (its Start run by reflection, as edit mode runs
    /// none; an AppShell without content, so texts are their keys and the palette has the markers, tools and material tokens).
    /// Pins the header (every action, Save the one primary button beside the palette's tool switch), Esc / pad B (the kit panel
    /// closes first, the editor never), the draft (version-prefixed, the cells, the name and the mode come back, another
    /// editor's draft is refused), undo / redo of a placed cell, the remembered Advanced switch and the kit panel (centred, its
    /// delete asks first).
    /// </summary>
    public sealed class StructureEditorShellEditModeTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private bool _hadEventSystem;
        private string _draftRoot;
        private object _loadPref, _storePref;
        private bool _stored;

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null;
            _draftRoot = Path.Combine(Path.GetTempPath(), "bbs-structure-editor-" + Guid.NewGuid().ToString("N"));
            EditorShell.CancelOwnedElsewhere = () => false;
            _loadPref = StaticField("LoadAdvancedPref").GetValue(null);
            _storePref = StaticField("StoreAdvancedPref").GetValue(null);
            StaticField("LoadAdvancedPref").SetValue(null, (Func<bool>)(() => false)); // never the developer's PlayerPrefs
            StaticField("StoreAdvancedPref").SetValue(null, (Action<bool>)(on => _stored = on));
            LogAssert.ignoreFailingMessages = true; // a content-less scene logs (no atlas, no blocks): not this test's subject
        }

        [TearDown]
        public void TearDown()
        {
            StaticField("LoadAdvancedPref").SetValue(null, _loadPref);
            StaticField("StoreAdvancedPref").SetValue(null, _storePref);
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

            LogAssert.ignoreFailingMessages = false;
            if (Directory.Exists(_draftRoot))
            {
                Directory.Delete(_draftRoot, recursive: true);
            }
        }

        // ---------------------------------------------------------------- helpers

        private static FieldInfo StaticField(string name) => typeof(StructureEditor).GetField(name, Any);

        private StructureEditor Open(StructureEditor.Mode mode)
        {
            var shellGo = new GameObject("ShellUnderTest");
            _spawned.Add(shellGo);
            var app = shellGo.AddComponent<AppShell>();
            app.SetSettingsForTest(new ClientSettings());
            var go = new GameObject("StructureEditorUnderTest");
            _spawned.Add(go);
            var editor = go.AddComponent<StructureEditor>();
            editor.Shell = app;
            editor.EditorMode = mode;
            Set(editor, "DraftRoot", _draftRoot);
            Call(editor, "Start");
            _spawned.Add(Get<Canvas>(editor, "_canvas").gameObject); // edit mode runs no OnDestroy
            Call(editor, "TickForTest"); // the form's signature, as the first frame takes it
            return editor;
        }

        private static EditorShell ShellOf(StructureEditor editor) => Get<EditorShell>(editor, "_shell");

        private static T Get<T>(object target, string name)
        {
            var type = target.GetType();
            var field = type.GetField(name, Any);
            if (field != null)
            {
                return (T)field.GetValue(target);
            }

            return (T)type.GetProperty(name, Any).GetValue(target);
        }

        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Any).SetValue(target, value);

        private static object Call(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethods(Any).First(m => m.Name == name && m.GetParameters().Length == args.Length);
            return method.Invoke(target, args);
        }

        private static bool Place(StructureEditor editor, int x, int y, int z, string id = "@wall", string kind = "block")
            => (bool)Call(editor, "PlaceForTest", new Vector3i(x, y, z), id, kind);

        private static bool Has(StructureEditor editor, int x, int y, int z) => (bool)Call(editor, "HasCellForTest", new Vector3i(x, y, z));

        private static bool IsPrimary(Button b) => b.targetGraphic is Image i && i.sprite == UiKit.PrimarySprite;

        // ---------------------------------------------------------------- the header

        [Test]
        public void Header_HasEveryAction_AndSaveIsTheOnePrimaryButton()
        {
            var editor = Open(StructureEditor.Mode.Settlement);
            var shell = ShellOf(editor);
            Assert.IsNotNull(shell, "the editor runs in the editor shell");
            Assert.IsNotNull(shell.UndoButton);
            Assert.IsNotNull(shell.RedoButton);
            Assert.IsNotNull(shell.LoadButton);
            Assert.IsNotNull(shell.NewButton);
            Assert.IsNotNull(shell.SubmitButton);
            Assert.IsTrue(shell.SubmitButton.gameObject.activeSelf, "a whole village can be submitted");
            Assert.IsTrue(IsPrimary(shell.SaveButton));
            var tools = Get<object>(editor, "_paletteCard");
            var place = Get<Button>(tools, "PlaceButton");
            var remove = Get<Button>(tools, "RemoveButton");
            var primaries = shell.Canvas.GetComponentsInChildren<Button>(true)
                .Where(IsPrimary).Where(b => b != place && b != remove).ToList();
            Assert.AreEqual(1, primaries.Count, "only Save is primary (the tool switch draws its selected tab that way): "
                                                + string.Join(", ", primaries.Select(b => b.name)));
            Assert.AreEqual("ui.menu.settlement_editor", shell.GetComponentsInChildren<Text>(true).Single(t => t.name == EditorShell.TitleName).text,
                "the title is the start card's name");
        }

        [Test]
        public void StationEditor_HasItsOwnTitleAndDraftSlot()
        {
            var editor = Open(StructureEditor.Mode.Station);
            var shell = ShellOf(editor);
            Assert.AreEqual("ui.menu.station_editor", shell.GetComponentsInChildren<Text>(true).Single(t => t.name == EditorShell.TitleName).text);
            Assert.AreEqual("station", shell.Options.EditorKey);
            Assert.AreEqual("ui.editor.save_sub.station", shell.Options.SaveSublineKey);
        }

        // ---------------------------------------------------------------- Esc / pad B

        [Test]
        public void Esc_ClosesTheKitPanelFirst_AndNeverTheEditor()
        {
            var editor = Open(StructureEditor.Mode.Settlement);
            var shell = ShellOf(editor);
            Call(editor, "OpenKitPanel");
            Assert.IsNotNull(Get<object>(editor, "_kitPanel"));
            Assert.AreEqual(EditorCancelResult.ClosedLayer, shell.HandleCancel(false));
            Assert.IsNull(Get<object>(editor, "_kitPanel"), "Esc closed the kit panel");
            Assert.AreEqual(EditorCancelResult.PointedAtBack, shell.HandleCancel(false), "with nothing open the editor stays");
            Assert.IsTrue(editor != null && editor.gameObject.activeSelf);
        }

        [Test]
        public void KitPanel_OpensCentred_AndItsDeleteAsksFirst()
        {
            var editor = Open(StructureEditor.Mode.Settlement);
            var shell = ShellOf(editor);
            Call(editor, "OpenKitPanel");
            var kits = Get<object>(editor, "_kitPanel");
            var overlay = Get<GameObject>(kits, "Overlay");
            var panel = overlay.GetComponentsInChildren<RectTransform>(true).First(rt => rt.sizeDelta == new Vector2(1100f, 900f));
            Assert.AreEqual(new Vector2((1920f - 1100f) / 2f, -(1080f - 900f) / 2f), panel.anchoredPosition, "centred (it opened at 0, 0)");

            Call(kits, "NewKit");
            int before = Get<System.Collections.IList>(kits, "_kits").Count;
            Call(Get<object>(editor, "_kitPanel"), "Delete");
            Assert.IsTrue(shell.IsDialogOpen, "deleting a kit asks first");
            Assert.AreEqual(before, Get<System.Collections.IList>(Get<object>(editor, "_kitPanel"), "_kits").Count, "nothing deleted yet");
            shell.OpenDialogNow.Choose(false);
            Assert.AreEqual(before, Get<System.Collections.IList>(Get<object>(editor, "_kitPanel"), "_kits").Count, "Cancel keeps the kit");
        }

        // ---------------------------------------------------------------- undo / redo

        [Test]
        public void UndoRedo_TakesAPlacedCellBack_AndPutsItBack()
        {
            var editor = Open(StructureEditor.Mode.Settlement);
            Assert.IsFalse(editor.IsDirty, "a fresh editor has nothing unsaved");
            Assert.IsTrue(Place(editor, 4, 0, 4));
            Assert.IsTrue(editor.CanUndo);
            Assert.IsTrue(editor.IsDirty);

            ShellOf(editor).Undo();
            Assert.IsFalse(Has(editor, 4, 0, 4), "undo took the cell back");
            Assert.IsTrue(editor.CanRedo);
            Assert.IsFalse(editor.IsDirty, "undone back to the opened state: nothing unsaved");

            ShellOf(editor).Redo();
            Assert.IsTrue(Has(editor, 4, 0, 4), "redo put it back");
            Assert.IsTrue(editor.IsDirty);
        }

        [Test]
        public void Undo_OfAMarkerOverABlock_KeepsEachStepApart()
        {
            var editor = Open(StructureEditor.Mode.Settlement);
            Place(editor, 1, 0, 1);
            Place(editor, 2, 0, 1, "vendor", "marker");
            Assert.AreEqual(2, Get<int>(editor, "UndoSteps"));
            editor.Undo();
            Assert.IsFalse(Has(editor, 2, 0, 1));
            Assert.IsTrue(Has(editor, 1, 0, 1), "one click is one step");
        }

        // ---------------------------------------------------------------- the draft

        [Test]
        public void Draft_RoundTrip_BringsTheCellsTheNameAndTheModeBack()
        {
            var first = Open(StructureEditor.Mode.Settlement);
            Set(first, "_name", "Mein Dorf");
            Place(first, 3, 0, 3);
            Place(first, 3, 1, 3);
            string draft = first.SaveDraft();
            StringAssert.StartsWith("bbs-structure-draft/1\n", draft, "the draft names its format and version");

            var second = Open(StructureEditor.Mode.Settlement);
            Assert.IsTrue(second.LoadDraft(draft));
            Assert.AreEqual("Mein Dorf", second.DocumentName);
            Assert.IsTrue(Has(second, 3, 0, 3) && Has(second, 3, 1, 3), "the cells came back");
            Assert.IsTrue(second.IsDirty, "a restored draft is unsaved work");
        }

        [Test]
        public void Draft_OfAnotherEditor_OrBroken_IsRefused()
        {
            var station = Open(StructureEditor.Mode.Station);
            Place(station, 2, 0, 2);
            string stationDraft = station.SaveDraft();

            var village = Open(StructureEditor.Mode.Settlement);
            Assert.IsFalse(village.LoadDraft(stationDraft), "a station draft never opens in the village editor");
            Assert.IsFalse(village.LoadDraft("not a draft"));
            Assert.IsFalse(village.LoadDraft("bbs-structure-draft/1\n{broken"));
        }

        [Test]
        public void Draft_OfAnUntouchedEmptyRoom_IsNothing()
        {
            var editor = Open(StructureEditor.Mode.Settlement);
            Assert.IsNull(editor.SaveDraft(), "an empty room with an untouched form keeps no draft");
        }

        // ---------------------------------------------------------------- the Advanced switch

        [Test]
        public void AdvancedSwitch_ShowsTheDeveloperSections_AndIsRemembered()
        {
            var editor = Open(StructureEditor.Mode.Settlement);
            var canvas = Get<Canvas>(editor, "_canvas");
            Assert.IsFalse(canvas.GetComponentsInChildren<Button>(false).Any(b => b.name == "Section_entry"),
                "the children's view shows only the building parts");

            Call(editor, "SetAdvanced", true);
            Assert.IsTrue(_stored, "the switch is remembered");
            Assert.IsTrue(canvas.GetComponentsInChildren<Button>(false).Any(b => b.name == "Section_entry"), "Advanced shows the generator entry");
            Assert.IsTrue(canvas.GetComponentsInChildren<Button>(false).Any(b => b.name == "Section_giant_ship"), "… and the giant ship");
        }

        [Test]
        public void DeveloperDesign_KeepsItsSectionsInView_WithTheSwitchOff()
        {
            var go = new GameObject("StructureEditorUnderTest");
            _spawned.Add(go);
            var shellGo = new GameObject("ShellUnderTest");
            _spawned.Add(shellGo);
            var app = shellGo.AddComponent<AppShell>();
            app.SetSettingsForTest(new ClientSettings());
            var editor = go.AddComponent<StructureEditor>();
            editor.Shell = app;
            editor.EditorMode = StructureEditor.Mode.Station;
            Set(editor, "DraftRoot", _draftRoot);
            Set(editor, "_moduleMode", true);
            Set(editor, "_function", "hub");
            Call(editor, "Start");
            var canvas = Get<Canvas>(editor, "_canvas");
            _spawned.Add(canvas.gameObject);
            Assert.IsTrue(canvas.GetComponentsInChildren<Button>(false).Any(b => b.name == "Section_ports"), "a module shows its ports");
        }
    }
}
