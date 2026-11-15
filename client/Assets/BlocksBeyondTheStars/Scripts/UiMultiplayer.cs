// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Multiplayer screen (#3665): one menu entry instead of the main menu's three (Host game, Join server, Official
    /// worlds), laid out on the menu template (<see cref="UiKit.AddScreenHeader"/>) with a tab bar under the header —
    /// <b>Official worlds</b> (the hosted-worlds portal, <see cref="UiOfficialWorlds"/>, now a tab instead of an overlay),
    /// <b>Join a server</b> (address, port, password and the servers joined before) and <b>Host on your network</b>
    /// (a pointer to "My worlds", where hosting lives since #3664).
    /// <para>
    /// The screen opens on <see cref="AppShell.MultiplayerTab"/>; Back, Esc and pad B return to the main menu. A tab's
    /// pane is built the first time it is shown and then only hidden and shown again, so the official worlds' sign-in
    /// state, a typed address or a running portal request survive switching tabs. The player name is chosen on the main
    /// menu, so this screen only checks that one is set.
    /// </para>
    /// <para>
    /// The browser build never opens this screen: its main menu keeps its own connect dialog (<c>UiMainMenu</c>), the
    /// browser never picks official worlds and cannot host (HOSTED_WORLDS.md) — so the Official-worlds and Host panes are
    /// compiled for the desktop only. This file holds the frame and the tabs (LB / RB step them on a pad); <c>.Join.cs</c>
    /// the Join tab and <c>.Host.cs</c> the Host tab.
    /// </para>
    /// </summary>
    public sealed partial class UiMultiplayer
    {
        /// <summary>The tab indices <see cref="AppShell.MultiplayerTab"/> uses.</summary>
        public const int TabOfficial = 0, TabJoin = 1, TabHost = 2;

        /// <summary>The tab bar under the header (1920×1080 reference space).</summary>
        public const float TabBarW = 900f, TabBarH = 56f;

        /// <summary>Where a tab's content starts: under the tab bar.</summary>
        public const float PaneTop = UiKit.MenuContentTop + TabBarH + 24f;

        private static readonly string[] TabLabelKeys = { "ui.mp.tab_official", "ui.mp.tab_join", "ui.mp.tab_host" };
        private static readonly string[] TabIcons = { "info_multiplayer", "btn_join", "btn_host" };
        private static readonly string[] PaneNames = { "OfficialWorldsPane", "JoinPane", "HostPane" };

        private static readonly int[] Tabs = { TabOfficial, TabJoin, TabHost };

        private readonly AppShell _shell;
        private readonly Transform _root;
        private readonly GameObject[] _panes = new GameObject[3];

        private UiMultiplayer(AppShell shell, Transform root)
        {
            _shell = shell;
            _root = root;
        }

        /// <summary>Builds the screen on its own canvas and returns the canvas root (<c>AppShell</c> boots and destroys
        /// it with the <see cref="ShellPhase.Multiplayer"/> phase).</summary>
        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("MultiplayerUI");
            UiNav.Enable(canvas.gameObject); // let a gamepad drive the screen (inert on keyboard/mouse)
            new UiMultiplayer(shell, canvas.transform).BuildFrame();
            return canvas.gameObject;
        }

        private string L(string key) => _shell.L(key);

        private void BuildFrame()
        {
            var back = UiKit.AddScreenHeader(_root, L("ui.mp.title"), L("ui.mp.subtitle"), () => _shell.GoTo(ShellPhase.MainMenu),
                L("ui.menu.back"));

            int start = System.Array.IndexOf(Tabs, _shell.MultiplayerTab);
            if (start < 0)
            {
                start = 0;
            }

            if (Tabs.Length > 1)
            {
                var labels = new List<string>();
                var icons = new List<string>();
                foreach (int tab in Tabs)
                {
                    labels.Add(L(TabLabelKeys[tab]));
                    icons.Add(TabIcons[tab]);
                }

                var bar = UiKit.AddSegmented(_root, UiKit.MenuLeft, UiKit.MenuContentTop, TabBarW, TabBarH, labels, start,
                    index => ShowTab(Tabs[index]), icons);
                bar.StepWithShoulders(); // LB / RB, as on the console
                UiNav.Prefer(_root.gameObject, bar.Buttons[start]); // a pad lands on the tab the screen opened on
            }

            ShowTab(Tabs[start]);

            // Back after the tab bar and the first pane: a pad's first focus is the first control in hierarchy order, and A
            // on arrival must not leave the screen. Dialogs open later on the root, so they still draw over it.
            back?.transform.SetAsLastSibling();
        }

        // Shows one tab's pane (building it on first use) and hides the others. The shell remembers the tab, so a rebuild
        // of the screen (a language change) reopens where the player was.
        private void ShowTab(int tab)
        {
            _shell.MultiplayerTab = tab;
            for (int i = 0; i < _panes.Length; i++)
            {
                if (_panes[i] != null)
                {
                    _panes[i].SetActive(i == tab);
                }
            }

            if (_panes[tab] == null)
            {
                _panes[tab] = BuildPane(tab);
            }
        }

        private GameObject BuildPane(int tab)
        {
            var pane = UiKit.AddStretchChild(_root, PaneNames[tab]);
            switch (tab)
            {
                case TabJoin:
                    BuildJoinPane(pane.transform);
                    break;
#if !UNITY_WEBGL || UNITY_EDITOR
                case TabOfficial:
                    // Its dialogs open on the screen root, so they cover the header and the tab bar too. The instance lives
                    // on in its buttons' handlers and its running portal requests — as long as the pane does.
                    new UiOfficialWorlds(_shell, _root, pane, CommitName, SetPlayerName);
                    break;
                case TabHost:
                    BuildHostPane(pane.transform);
                    break;
#endif
            }

            return pane;
        }

        /// <summary>
        /// The name gate of every join on this screen (#221): false when the main menu has no player name yet (the caller
        /// shows <c>ui.webgl.need_name</c>); otherwise trims it and clears the official-worlds grant of an earlier join, so
        /// it never leaks into a join by address — the official-worlds join sets its own right afterwards.
        /// </summary>
        private bool CommitName()
        {
            if (string.IsNullOrWhiteSpace(_shell.PlayerName))
            {
                return false;
            }

            _shell.PlayerName = _shell.PlayerName.Trim();
            _shell.HostedToken = "";
            _shell.HostedWorldId = "";
            _shell.ArcadeNameToken = "";
            return true;
        }

        // The official worlds' "name reserved" retry: the replacement name becomes the player's name everywhere.
        private void SetPlayerName(string name)
        {
            _shell.PlayerName = name;
            _shell.Settings.PlayerName = name; // remember the identity across sessions
            _shell.Settings.Save();
        }

        /// <summary>
        /// One numbered step of an explanation ("1 Create your own world …"): a cyan disc with the number, a bold title and
        /// an optional dim line under it (two lines wrap). Used by the Host tab and the official worlds' "How online play
        /// works" card. <paramref name="x"/>/<paramref name="y"/> are the disc's top-left corner, <paramref name="w"/> the
        /// whole step's width.
        /// </summary>
        internal static void AddStep(Transform parent, float x, float y, float w, int number, string title, string body)
        {
            const float disc = 48f, gap = 20f;
            UiKit.AddImage(parent, x, y, disc, disc, UiKit.DiscSprite, UiKit.Cyan);
            UiKit.AddText(parent, x, y, disc, disc, number.ToString(CultureInfo.InvariantCulture), 24, UiKit.PrimaryText,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            float textX = x + disc + gap, textW = w - disc - gap;
            var head = UiKit.AddText(parent, textX, y, textW, 30f, title, 21, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(head, textW, 14, 21);
            if (!string.IsNullOrEmpty(body))
            {
                var line = UiKit.AddText(parent, textX, y + 32f, textW, 52f, body, 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                line.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
        }

        // A field's label line above its input.
        private Text FieldLabel(Transform parent, float x, float y, float w, string key)
        {
            var label = UiKit.AddText(parent, x, y, w, 24f, L(key), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(label, w, 12, 16);
            return label;
        }
    }
}
