// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// Pins the Multiplayer screen (#3665) <see cref="UiMultiplayer.Build"/> assembles: the tab bar, the tab the shell asks
    /// for, tab switching that keeps a built pane, Back to the main menu, the Join tab's name gate and its "Recently joined"
    /// rows, and one primary action per tab. The shell never ran Awake, so every text is its locale key and its settings
    /// are a fresh <see cref="ClientSettings"/> (signed out of the portal). Nothing here reaches
    /// <c>ClientSettings.Save()</c> or a join — the Connect click is made without a player name.
    /// </summary>
    public sealed class MultiplayerScreenEditModeTests
    {
        private const string NeedName = "ui.webgl.need_name";
        private static readonly string[] PaneNames = { "OfficialWorldsPane", "JoinPane", "HostPane" };

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private bool _hadEventSystem;

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = Object.FindAnyObjectByType<EventSystem>() != null;
        }

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

        private AppShell NewShell(int tab, ClientSettings settings = null)
        {
            var shell = Track(new GameObject("ShellUnderTest").AddComponent<AppShell>());
            shell.SetSettingsForTest(settings ?? new ClientSettings());
            shell.PlayerName = "";
            shell.MultiplayerTab = tab;
            return shell;
        }

        private Transform Build(AppShell shell) => Track(UiMultiplayer.Build(shell)).transform;

        [Test]
        public void Screen_HasTheHeaderAndOneTabPerWay()
        {
            var root = Build(NewShell(UiMultiplayer.TabOfficial));

            Assert.AreEqual("MultiplayerUI", root.name);
            ButtonIn(root, "ui.menu.back");
            ButtonIn(root, "ui.mp.tab_official");
            ButtonIn(root, "ui.mp.tab_join");
            ButtonIn(root, "ui.mp.tab_host");
            Assert.IsNotNull(TextShowing(root, "ui.mp.title"), "the header's title");
        }

        [TestCase(UiMultiplayer.TabOfficial)]
        [TestCase(UiMultiplayer.TabJoin)]
        [TestCase(UiMultiplayer.TabHost)]
        public void Screen_OpensOnTheShellsTab_AndBuildsNoOtherPane(int tab)
        {
            var root = Build(NewShell(tab));

            for (int i = 0; i < PaneNames.Length; i++)
            {
                var pane = root.Find(PaneNames[i]);
                if (i == tab)
                {
                    Assert.IsNotNull(pane, $"{PaneNames[i]} is built");
                    Assert.IsTrue(pane.gameObject.activeSelf, $"{PaneNames[i]} is shown");
                }
                else
                {
                    Assert.IsNull(pane, $"{PaneNames[i]} waits until its tab is opened");
                }
            }
        }

        [Test]
        public void TabBar_SwitchesPanes_AndKeepsABuiltPane()
        {
            var shell = NewShell(UiMultiplayer.TabJoin);
            var root = Build(shell);
            var join = root.Find("JoinPane").gameObject;

            Click(ButtonIn(root, "ui.mp.tab_host"));
            Assert.IsTrue(root.Find("HostPane").gameObject.activeSelf, "the Host tab shows its pane");
            Assert.IsFalse(join.activeSelf, "the Join pane is hidden, not destroyed");
            Assert.AreEqual(UiMultiplayer.TabHost, shell.MultiplayerTab, "the shell remembers the tab");

            Click(ButtonIn(root, "ui.mp.tab_join"));
            Assert.AreSame(join, root.Find("JoinPane").gameObject, "the same Join pane comes back");
            Assert.IsTrue(join.activeSelf);
        }

        [Test]
        public void BackButton_ReturnsToTheMainMenu()
        {
            var shell = NewShell(UiMultiplayer.TabJoin);
            var root = Build(shell);

            Click(ButtonIn(root, "ui.menu.back"));

            Assert.AreEqual(ShellPhase.MainMenu, shell.Phase);
        }

        [Test]
        public void Connect_WithoutAName_WarnsAndDialsNothing()
        {
            var shell = NewShell(UiMultiplayer.TabJoin);
            var phase = shell.Phase;
            var root = Build(shell);
            Assert.IsNull(TextShowing(root, NeedName), "no warning before the click");

            Click(ButtonIn(root, "ui.menu.connect"));

            Assert.IsNotNull(TextShowing(root, NeedName), "Connect without a player name shows the name warning");
            Assert.AreEqual(phase, shell.Phase, "nothing was dialled");
            Assert.AreEqual(0, shell.Settings.RecentServers.Count, "nothing was remembered");
        }

        [Test]
        public void RecentServers_GetOneConnectRowEach_AndAnEmptyLineWithout()
        {
            var settings = new ClientSettings();
            settings.RecentServers.Add(new RecentServer { Host = "192.168.0.7", Port = "31550", LastJoinedUnix = 1 });
            settings.RecentServers.Add(new RecentServer { Host = "games.example", Port = "31415", LastJoinedUnix = 0 });
            var root = Build(NewShell(UiMultiplayer.TabJoin, settings));

            Assert.IsNotNull(TextShowing(root, "192.168.0.7:31550"));
            Assert.IsNotNull(TextShowing(root, "games.example:31415"));
            Assert.AreEqual(3, ButtonsIn(root, "ui.menu.connect").Count, "the connect card's Connect plus one per server");
            Assert.IsNull(TextShowing(root, "ui.mp.recent_empty"));

            var empty = Build(NewShell(UiMultiplayer.TabJoin));
            Assert.IsNotNull(TextShowing(empty, "ui.mp.recent_empty"), "no servers yet: the empty line says what will show up");
        }

        [TestCase(UiMultiplayer.TabOfficial)]
        [TestCase(UiMultiplayer.TabJoin)]
        [TestCase(UiMultiplayer.TabHost)]
        public void EveryTab_HasExactlyOnePrimaryAction(int tab)
        {
            var root = Build(NewShell(tab));
            var pane = root.Find(PaneNames[tab]);

            var primaries = pane.GetComponentsInChildren<Button>(true)
                .Where(b => b.targetGraphic is Image frame && frame.sprite == UiKit.PrimarySprite)
                .ToList();

            Assert.AreEqual(1, primaries.Count, $"{PaneNames[tab]} has one filled next step");
        }

        [Test]
        public void OfficialWorlds_SignedOut_OffersSignInSignupAndRecovery_InTheTab()
        {
            var root = Build(NewShell(UiMultiplayer.TabOfficial));
            var pane = root.Find("OfficialWorldsPane");

            ButtonIn(pane, "ui.portal.login");
            ButtonIn(pane, "ui.portal.signup");
            ButtonIn(pane, "ui.portal.forgot");
            ButtonIn(pane, "ui.portal.view_rules");
            Assert.IsNotNull(TextShowing(pane, "ui.portal.beta"), "the beta banner");
            // Every dialog's scrim is a UiModalScope (UiKit.AddModalDim); the header's Back is the root's last child since
            // it moved behind the tabs for the pad's first focus.
            Assert.IsEmpty(root.GetComponentsInChildren<UiModalScope>(false), "no dialog or scrim of its own is open over the screen");
        }

        [Test]
        public void AgeText_IsEmptyForAnUnknownTime_AndUsesTheTimeAgoWording()
        {
            string Key(string key) => key;
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            Assert.AreEqual("", UiMultiplayer.AgeText(Key, 0));
            Assert.AreEqual("", UiMultiplayer.AgeText(Key, long.MaxValue));
            Assert.AreEqual("ui.time.ago_now", UiMultiplayer.AgeText(Key, now - 10));
            Assert.AreEqual("ui.time.ago_days", UiMultiplayer.AgeText(Key, now - 3 * 86400));
        }

        private static List<Button> ButtonsIn(Transform scope, string label)
            => scope.GetComponentsInChildren<Button>(true)
                .Where(b => b.GetComponentsInChildren<Text>(true).Any(t => t.text == label))
                .ToList();

        /// <summary>The one button under <paramref name="scope"/> whose label reads <paramref name="label"/>.</summary>
        private static Button ButtonIn(Transform scope, string label)
        {
            var matches = ButtonsIn(scope, label);
            Assert.AreEqual(1, matches.Count, $"exactly one '{label}' button under {scope.name}");
            return matches[0];
        }

        private static void Click(Button button)
        {
            Assert.IsTrue(button.interactable, $"'{button.gameObject.name}' can be clicked");
            button.onClick.Invoke();
        }

        private static Text TextShowing(Transform root, string text)
            => root.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.text == text);
    }
}
