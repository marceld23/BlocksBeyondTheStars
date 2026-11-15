// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The pad's way through the menus after the UI-layout overhaul (#3674 follow-up): a dialog takes the focus, keeps it
    /// away from the controls under its scrim and hands it back to the control that opened it; a destructive button is
    /// never the first focus; a <c>‹ value ›</c> cycle is one stop whose value Left / Right step; and no screen's first
    /// focus is its Back / Close / Leave button (A on arrival must not leave the screen). CI has no pad, so these pin the
    /// rule <see cref="UiNavFocus.FocusTarget"/> applies — the pad feel itself stays a manual pass.
    /// </summary>
    public sealed class PadMenuFocusEditModeTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private bool _hadEventSystem;

        [SetUp]
        public void SetUp() => _hadEventSystem = Object.FindAnyObjectByType<EventSystem>() != null;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();

            // CreateCanvas makes an EventSystem when none exists — leave the scene as we found it.
            var es = Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                Object.DestroyImmediate(es.gameObject);
            }
        }

        private T Track<T>(T obj) where T : Object
        {
            _spawned.Add(obj is Component c ? c.gameObject : obj as GameObject);
            return obj;
        }

        private GameObject NewScreen(string name)
        {
            var go = Track(new GameObject(name, typeof(RectTransform)));
            go.AddComponent<Canvas>();
            return go;
        }

        private static Button AddButton(GameObject parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<Image>();
            return go.AddComponent<Button>();
        }

        private AppShell NewShell()
        {
            var shell = Track(new GameObject("ShellUnderTest").AddComponent<AppShell>());
            shell.SetSettingsForTest(new ClientSettings { LastWorld = "" });
            shell.PlayerName = "";
            return shell;
        }

        private static string Label(Selectable control)
        {
            var text = control != null ? control.GetComponentInChildren<Text>(true) : null;
            return text != null ? text.text : null;
        }

        // ---- dialogs ------------------------------------------------------------------------------------------

        [Test]
        public void Dialog_TakesTheFocus_KeepsItFromTheScreen_AndHandsItBackToTheOpener()
        {
            var screen = NewScreen("Screen");
            var opener = AddButton(screen, "Delete");
            var other = AddButton(screen, "Play");
            var back = AddButton(screen, "Back"); // last on purpose — the clamped position used to land here
            var nav = screen.AddComponent<UiNavFocus>();
            nav.NoteSelection(opener.gameObject);
            Assert.AreSame(opener, nav.FocusTarget());

            var (overlay, panel) = UiKit.AddModalOverlay(screen.transform, 610f, 420f, 700f, 250f);
            var yes = UiKit.AddTierButton(panel, 40f, 160f, 290f, 58f, "yes", null, ButtonTier.Danger, "btn_delete");
            var no = UiKit.AddTierButton(panel, 370f, 160f, 290f, 58f, "no", null, ButtonTier.Quiet, "btn_back");

            Assert.AreSame(no, nav.FocusTarget(), "the dialog takes the focus — on Cancel, never on the destructive choice");
            Assert.IsTrue(nav.IsCovered(opener), "the opener lies under the scrim");
            Assert.IsTrue(nav.IsCovered(other), "the stick must not reach Play under the delete question");
            Assert.IsTrue(nav.IsCovered(back));
            Assert.IsFalse(nav.IsCovered(yes));

            nav.NoteSelection(yes.gameObject);
            nav.NoteSelection(other.gameObject); // a control under the scrim is no place to come back to
            Assert.AreSame(yes, nav.FocusTarget(), "inside the dialog the focus is remembered");

            overlay.SetActive(false);
            Assert.AreSame(opener, nav.FocusTarget(), "closed: back to the opener, not to the screen's last control");
            Assert.IsFalse(nav.IsCovered(other));
        }

        [Test]
        public void Dialog_AControlDrawnAboveIt_StaysReachable()
        {
            var screen = NewScreen("Screen");
            AddButton(screen, "Under");
            var (_, panel) = UiKit.AddModalOverlay(screen.transform, 610f, 420f, 700f, 250f);
            var ok = UiKit.AddTierButton(panel, 40f, 160f, 290f, 58f, "ok", null, ButtonTier.Primary);
            var above = AddButton(screen, "Above"); // built after the dialog: drawn over its scrim
            var nav = screen.AddComponent<UiNavFocus>();

            Assert.AreSame(ok, nav.FocusTarget());
            Assert.IsFalse(nav.IsCovered(above));
        }

        [Test]
        public void Backdrop_WithoutControls_IsNoDialog()
        {
            // The settings screen and the on-screen keyboard put a scrim BEHIND their own controls — a backdrop, not a
            // dialog: it must not hide the screen's controls from the pad.
            var screen = NewScreen("Settings");
            UiKit.AddModalDim(screen.transform);
            var first = AddButton(screen, "Graphics");
            AddButton(screen, "Audio");
            var nav = screen.AddComponent<UiNavFocus>();

            Assert.AreSame(first, nav.FocusTarget());
            Assert.IsFalse(nav.IsCovered(first));
        }

        [Test]
        public void Prefer_NamesTheFirstFocus_ButARememberedPositionWins()
        {
            var screen = NewScreen("Screen");
            var first = AddButton(screen, "First");
            var shown = AddButton(screen, "ShownCategory");
            var nav = screen.AddComponent<UiNavFocus>();

            UiNav.Prefer(screen, shown);
            Assert.AreSame(shown, nav.FocusTarget(), "a screen names its first focus");

            nav.NoteSelection(first.gameObject);
            Assert.AreSame(first, nav.FocusTarget(), "a rebuild restores where the player was");
        }

        // ---- the cycle control --------------------------------------------------------------------------------

        [Test]
        public void Cycle_LeftAndRight_StepTheValue_AndTheValueIsTheOnlyStop()
        {
            var screen = NewScreen("CycleScreen");
            int at = 0;
            var text = UiKit.AddCycle(screen.transform, 0f, 0f, 400f, 48f, "0",
                dir => (at += dir).ToString(CultureInfo.InvariantCulture));
            var value = text.GetComponentInParent<Button>();
            var step = value.GetComponent<StepOnMove>();
            Assert.IsNotNull(step, "the value takes Left / Right");

            step.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Left });
            Assert.AreEqual("-1", text.text);
            step.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Right });
            var right = new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Right };
            step.OnMove(right);
            Assert.AreEqual("1", text.text);
            Assert.IsTrue(right.used, "the step consumes the move");

            var up = new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Up };
            step.OnMove(up);
            Assert.AreEqual("1", text.text, "Up / Down leave the row instead");
            Assert.IsFalse(up.used);

            Assert.AreEqual(Navigation.Mode.Vertical, value.navigation.mode, "Left / Right never navigate away from the value");
            foreach (var button in screen.GetComponentsInChildren<Button>(true))
            {
                if (button != value)
                {
                    Assert.AreEqual(Navigation.Mode.None, button.navigation.mode, "the arrows are mouse / touch targets, no pad stops");
                }
            }

            var nav = screen.AddComponent<UiNavFocus>();
            Assert.AreSame(value, nav.FocusTarget(), "one focus stop per cycle");
        }

        // ---- first focus on the screens -----------------------------------------------------------------------

        [Test]
        public void MyWorlds_FirstFocus_IsNotBack()
        {
            var root = Track(UiSaveSelect.Build(NewShell())).transform;
            var target = root.GetComponent<UiNavFocus>().FocusTarget();

            Assert.IsNotNull(target);
            Assert.AreNotEqual("ui.menu.back", Label(target), "A on arrival must not leave the screen");
            Assert.AreEqual("ui.save.new_world", Label(target), "while the list loads: + New world (Play once it is there)");
        }

        [TestCase(UiMultiplayer.TabOfficial, "ui.mp.tab_official")]
        [TestCase(UiMultiplayer.TabJoin, "ui.mp.tab_join")]
        [TestCase(UiMultiplayer.TabHost, "ui.mp.tab_host")]
        public void Multiplayer_FirstFocus_IsTheOpenTab(int tab, string label)
        {
            var shell = NewShell();
            shell.MultiplayerTab = tab;
            var root = Track(UiMultiplayer.Build(shell)).transform;

            Assert.AreEqual(label, Label(root.GetComponent<UiNavFocus>().FocusTarget()));
        }

        [Test]
        public void Settings_FirstFocus_IsTheShownCategory()
        {
            var root = Track(UiSettings.Build(NewShell())).transform;
            var target = root.GetComponent<UiNavFocus>().FocusTarget();

            Assert.IsNotNull(target);
            Assert.AreNotEqual("ui.menu.back", Label(target));
            Assert.AreSame(UiKit.PrimarySprite, ((Image)target.targetGraphic).sprite,
                "the category the screen reopened on (drawn primary), not always Graphics");
        }

        [Test]
        public void Codex_FirstFocus_IsTheOpenChapter()
        {
            var wiki = Track(new GameObject("WikiUnderTest").AddComponent<WikiUI>());
            wiki.Show("blocks");
            Track(wiki.CanvasForTest.gameObject);
            var target = wiki.CanvasForTest.GetComponent<UiNavFocus>().FocusTarget();

            Assert.AreEqual("ui.wiki.blocks", Label(target), "not the header's Back to the menu");
        }

        [Test]
        public void AdminMenu_FirstFocus_IsTheOpenTab_NotLeave()
        {
            // Opening for real needs a server in admin mode; the frame itself is built the same way.
            var admin = Track(new GameObject("AdminUnderTest").AddComponent<AdminPanelUi>());
            Call(admin, "ShowCanvas");
            Track(admin.CanvasForTest.gameObject);
            Call(admin, "BuildFrame");
            var target = admin.CanvasForTest.GetComponent<UiNavFocus>().FocusTarget();

            Assert.AreEqual("ui.admin.tab_players", Label(target));
        }

        /// <summary>Calls a non-public instance method declared on <paramref name="target"/>'s type or a base type.</summary>
        private static void Call(object target, string method)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var info = type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, System.Type.EmptyTypes, null);
                if (info != null)
                {
                    info.Invoke(target, null);
                    return;
                }
            }

            Assert.Fail($"{target.GetType().Name}.{method}() not found — the test needs updating");
        }
    }
}
