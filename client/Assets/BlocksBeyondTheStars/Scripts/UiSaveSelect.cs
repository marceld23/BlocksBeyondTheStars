// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// "My worlds" (#3664): ONE master-detail screen for playing and hosting local worlds, on the menu template. The list
    /// of saves sits on the left (<see cref="WorldListView"/>, UiSaveSelect.WorldList.cs: name, mode, last played, playtime,
    /// read in the background and built in slices, #3259); the right pane shows the selected world with Play as the screen's
    /// one primary action and Delete set apart — or, after "+ New world", the creation form (UiSaveSelect.Create.cs). Under
    /// both, the switch "Open to friends" with the player cap, the join password and the LAN address replaces the separate
    /// "Host a world" screen that used to repeat this whole picker for one extra panel; the main menu's Multiplayer screen
    /// opens this screen with the switch already on (<see cref="AppShell.HostMode"/>).
    /// </summary>
    public static partial class UiSaveSelect
    {
        // The world list (left card): a clipped viewport under the toolbar, rows at a fixed pitch.
        private const float ListX = 90f, ListW = 700f, PaneY = UiKit.MenuContentTop, PaneH = 730f;
        private const float ListY = 76f;
        private const float ListH = 630f;
        private const float RowPitch = 84f;
        private const float RowH = 76f;

        // The right pane: the selected world or the creation form above, the hosting row and the footer below.
        private const float PaneX = 820f, PaneW = 1010f;
        private const float LanY = 520f, FooterY = 664f;

        /// <summary>
        /// The list's scroll offset (content y, px from the top) carried across the rebuild a delete triggers.
        /// <see cref="AppShell.RefreshSaveSelect"/> destroys the whole screen so the deleted world drops off the
        /// list, and without this the list jumped back to the top after every delete — clearing out old worlds
        /// far down a long list meant scrolling back down each time. Consumed (reset) by every
        /// <see cref="Build"/>, so only the rebuild right after a delete restores it.
        /// </summary>
        private static float _restoreScrollY;

        /// <summary>Compact playtime for a save-list row: "12 h 30 min", "45 min", or "&lt;1 min" (h/min are
        /// understood in both German and English, so the figure needs no localization).</summary>
        private static string FormatPlaytime(long totalSeconds)
        {
            long minutes = totalSeconds / 60;
            if (minutes <= 0) return "<1 min";
            long h = minutes / 60, m = minutes % 60;
            return h > 0 ? $"{h} h {m} min" : $"{m} min";
        }

        /// <summary>
        /// Characters a world name may not contain. Deliberately the WINDOWS invalid-filename set on every
        /// platform rather than <c>Path.GetInvalidFileNameChars()</c>, which on Linux is only <c>/</c>: a save
        /// folder should mean the same thing wherever the game runs, and a <c>"</c> would additionally break
        /// the quoted <c>--world "…"</c> argument the launcher builds.
        /// <para>
        /// A player typed <c>Minecraft Wo bin ich?:(</c> and reported that he "cannot create a world with
        /// THIS name" — nothing rejected it, nothing warned him; the name was quietly mangled into
        /// <c>Minecraft Wo bin ich__(</c> deep inside the save layer, and the picker then listed a world he
        /// never named. Strip it here, in front of him, where he can see what he is getting.
        /// </para>
        /// </summary>
        private static readonly char[] IllegalNameChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

        /// <summary>The world name as it will actually be stored: illegal characters dropped, whitespace
        /// collapsed and trimmed. Empty when nothing usable is left.</summary>
        public static string SanitizeWorldName(string raw) => SanitizeWorldName(raw, trimEnd: true);

        /// <summary>
        /// <paramref name="trimEnd"/> is false while the player is still TYPING. A trailing space is illegal
        /// in a save name, but stripping it on every keystroke means the space between two words vanishes the
        /// moment you press it and the name can never grow past one word. Trailing whitespace is therefore
        /// only removed when the name is actually used.
        /// </summary>
        public static string SanitizeWorldName(string raw, bool trimEnd)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder(raw.Length);
            bool lastWasSpace = false;
            foreach (char c in raw)
            {
                if (char.IsControl(c) || System.Array.IndexOf(IllegalNameChars, c) >= 0)
                {
                    continue;
                }

                bool space = c == ' ';
                if (space && (lastWasSpace || sb.Length == 0))
                {
                    continue; // no leading or doubled spaces
                }

                lastWasSpace = space;
                sb.Append(c);
            }

            // A trailing dot or space names an unopenable file on Windows, so they go — at use time.
            return trimEnd ? sb.ToString().TrimEnd(' ', '.') : sb.ToString();
        }

        /// <summary>The world list's clipped, vertically scrolling viewport inside the left card
        /// (<see cref="ListY"/>..+<see cref="ListH"/>); <paramref name="content"/> takes the rows, placed absolutely
        /// (top-left) at <see cref="RowPitch"/>. The caller sizes the content's height from the world count.</summary>
        private static ScrollRect BuildWorldListViewport(Transform panel, out RectTransform content)
        {
            // The viewport's near-transparent hit graphic gives the wheel/drag something to hit between the rows.
            content = UiKit.AddScrollViewport(panel, "WorldListScroll", 20f, ListY, ListW - 40f, ListH, out var scroll);
            content.sizeDelta = Vector2.zero;
            return scroll;
        }

        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("SaveSelectUI");
            UiNav.Enable(canvas.gameObject); // gamepad can pick a world (inert on KB/mouse)
            s_screen = new WorldsScreen(shell, canvas);
            s_screen.Build();
            return canvas.gameObject;
        }

        /// <summary>The screen on show (its canvas is gone once the shell left "My worlds").</summary>
        private static WorldsScreen s_screen;

        /// <summary>
        /// Esc / pad B on "My worlds": closes the topmost layer — the delete question, then the world options (a sub-page
        /// back to their main page first), then the New-world form (back to the selected world) — and says whether there
        /// was one. False means nothing is open and the shell may leave the screen.
        /// </summary>
        internal static bool CloseTopLayer()
        {
            var screen = s_screen;
            return screen != null && screen.IsShown && screen.CloseTopLayer();
        }

        /// <summary>The screen's parts and the state they share: the selected world, the open view, the hosting row.</summary>
        private sealed partial class WorldsScreen
        {
            private readonly AppShell _shell;
            private readonly Canvas _canvas;
            private readonly Transform _root;

            // Hosting ("Open to friends", #3664): on when the Multiplayer screen sent the player here to host.
            private bool _lan;
            private int _maxPlayers = 12;
            private string _hostPass = "";
            private GameObject _lanOptions;

            private WorldListView _list;
            private string _selected;
            private GameObject _detail, _empty, _create, _deleteButton;
            private Button _play, _emptyNew; // the pad's first focus once the list is there (with a world / without one)
            private Text _detailName, _detailMeta, _factsValues;

            // Delete confirmation (built last so it draws on top).
            private GameObject _confirm;
            private Text _confirmText;

            public WorldsScreen(AppShell shell, Canvas canvas)
            {
                _shell = shell;
                _canvas = canvas;
                _root = canvas.transform;
                _lan = shell.HostMode;
            }

            public void Build()
            {
                var back = UiKit.AddScreenHeader(_root, _shell.L("ui.save.title"), _shell.L("ui.save.subtitle"),
                    () => _shell.GoTo(ShellPhase.MainMenu), _shell.L("ui.menu.back"));
                BuildList();
                var pane = UiKit.AddCard(_root, PaneX, PaneY, PaneW, PaneH);
                BuildDetail(pane);
                BuildCreate(pane);
                BuildLan(pane);
                // Back after the cards: a pad's first focus is the first control in hierarchy order, and A on arrival must
                // not leave the screen. The delete confirmation stays last, on top of everything.
                back?.transform.SetAsLastSibling();
                BuildConfirm();
                ShowDetail(null);
            }

            // ── Left: the saved worlds ─────────────────────────────────────────────────────────
            private void BuildList()
            {
                var left = UiKit.AddCard(_root, ListX, PaneY, ListW, PaneH);
                UiKit.AddKicker(left, 24f, 24f, ListW - 300f, _shell.L("ui.save.existing"));
                UiKit.AddTierButton(left, ListW - 264f, 14f, 240f, 48f, _shell.L("ui.save.new_world"), ShowCreate, ButtonTier.Secondary, "btn_new");

                // #3259: the rows come from the world list read in the background and arrive a slice per frame — the
                // screen opens at once whatever the number of worlds.
                float restoreScrollY = _restoreScrollY;
                _restoreScrollY = 0f;
                _list = WorldListView.Attach(_canvas.gameObject, _shell, left, restoreScrollY, Select, Arrived);
            }

            /// <summary>The list is there: preselect the last-played world — unless the player opened the New-world form while
            /// the list was loading, which must not vanish under their typing. A pad then lands on Play (or, without a world,
            /// on "New world") instead of the "+ New world" button it found first while the list was still loading — unless
            /// the player has moved the focus meanwhile.</summary>
            private void Arrived(SavedWorld world)
            {
                if (!_create.activeSelf)
                {
                    Select(world);
                    UiNav.Prefer(_canvas.gameObject, world != null ? _play : _emptyNew, takeOver: true);
                }
            }

            /// <summary>True while the screen's canvas is up.</summary>
            public bool IsShown => _canvas != null && _canvas.gameObject.activeInHierarchy && _canvas.enabled;

            /// <summary>See <see cref="UiSaveSelect.CloseTopLayer"/>.</summary>
            public bool CloseTopLayer()
            {
                if (_confirm != null && _confirm.activeSelf)
                {
                    _confirm.SetActive(false);
                    return true;
                }

                if (_optionsOverlay != null && _optionsOverlay.activeSelf)
                {
                    UiWorldOptions.Back(_optionsOverlay);
                    return true;
                }

                if (_create != null && _create.activeSelf)
                {
                    CancelCreate();
                    return true;
                }

                return false;
            }

            /// <summary>A row was picked (or the list arrived and picked the last-played world): show it on the right.</summary>
            private void Select(SavedWorld world)
            {
                _list.MarkSelected(world?.Name);
                ShowDetail(world);
            }

            // ── Right: the selected world ──────────────────────────────────────────────────────
            private void BuildDetail(Transform pane)
            {
                _detail = UiKit.AddStretchChild(pane, "WorldDetail");
                _detailName = UiKit.AddText(_detail.transform, 32f, 26f, PaneW - 64f, 50f, "", 36, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                _detailMeta = UiKit.AddText(_detail.transform, 34f, 80f, PaneW - 64f, 28f, "", 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
                _play = UiKit.AddTierButton(_detail.transform, 32f, 128f, 460f, 76f, _shell.L("ui.save.play"),
                    () => { if (_selected != null) { Launch(_selected); } }, ButtonTier.Primary, "btn_continue");
                _play.GetComponentInChildren<Text>().fontSize = 28;

                // A small facts grid: what the list row says, larger and labelled.
                UiKit.AddText(_detail.transform, 32f, 240f, 220f, 96f,
                    _shell.L("ui.save.mode") + "\n" + _shell.L("ui.save.last_played") + "\n" + _shell.L("ui.save.playtime"),
                    17, UiKit.CyanDim, TextAnchor.UpperLeft).lineSpacing = 1.25f;
                _factsValues = UiKit.AddText(_detail.transform, 260f, 240f, 600f, 96f, "", 17, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
                _factsValues.lineSpacing = 1.25f;

                _deleteButton = UiKit.AddTierButton(pane, PaneW - 32f - 240f, FooterY, 240f, 50f, _shell.L("ui.save.delete_yes"),
                    () => { if (_selected != null) { AskDelete(_selected); } }, ButtonTier.Danger, "btn_delete").gameObject;

                _empty = UiKit.AddStretchChild(pane, "NoWorldSelected");
                var hint = UiKit.AddText(_empty.transform, 32f, 40f, PaneW - 64f, 60f, _shell.L("ui.save.pick_or_new"), 20, UiKit.TextCol, TextAnchor.UpperLeft);
                hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                _emptyNew = UiKit.AddTierButton(_empty.transform, 32f, 120f, 420f, 70f, _shell.L("ui.save.new_world"), ShowCreate,
                    ButtonTier.Primary, "btn_new");
            }

            private void ShowDetail(SavedWorld world)
            {
                _selected = world?.Name;
                _create.SetActive(false);
                _detail.SetActive(world != null);
                _deleteButton.SetActive(world != null);
                _empty.SetActive(world == null);
                if (world == null)
                {
                    return;
                }

                string mode = world.ModeKey.Length > 0 ? _shell.L(world.ModeKey) : "—";
                string ago = UiKit.TimeAgo(_shell.L, world.LastWriteUtc);
                string played = world.PlaytimeSeconds >= 0 ? FormatPlaytime(world.PlaytimeSeconds) : "—";
                _detailName.text = world.Name;
                UiKit.FitLabelSingleLine(_detailName, PaneW - 64f, 20, 36);
                _detailMeta.text = mode + "  ·  " + ago;
                _factsValues.text = mode + "\n" + ago + "\n" + played;
            }

            // seed 0 = the server derives it from the name; "Random world" under a typed name passes its own (#2522).
            private void Launch(string world, bool unlockAll = false, bool allShips = false, bool kit = false, bool sandbox = false,
                WorldCreationOptions options = null, bool flight = false, long seed = 0)
            {
                if (_lan)
                {
                    // #3708: friends dial in, so Windows Firewall has to allow the server — checked (and explained) first.
                    HostThroughFirewall(_shell, _root,
                        () => _shell.StartHostWorld(world, _maxPlayers, _hostPass, seed, unlockAll, allShips, kit, sandbox, options, flight));
                }
                else
                {
                    _shell.StartSingleplayerWorld(world, seed, unlockAll, allShips, kit, sandbox, options, flight);
                }
            }

            // ── Delete (confirmation on top) ───────────────────────────────────────────────────
            private void AskDelete(string world)
            {
                _confirmText.text = _shell.L("ui.save.delete_confirm").Replace("{world}", world);
                _confirm.SetActive(true);
                UiKit.OpenModal(_confirm);
            }

            private void BuildConfirm()
            {
                var (overlay, panel) = UiKit.AddModalOverlay(_root, 610f, 420f, 700f, 250f);
                _confirm = overlay;
                _confirmText = UiKit.AddText(panel, 30f, 30f, 640f, 80f, string.Empty, 20, UiKit.TextCol, TextAnchor.MiddleCenter);
                _confirmText.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddTierButton(panel, 40f, 160f, 290f, 58f, _shell.L("ui.save.delete_yes"), () =>
                {
                    if (!string.IsNullOrEmpty(_selected))
                    {
                        LocalServerLauncher.DeleteWorld(_selected);
                    }

                    _restoreScrollY = _list.ScrollY; // the rebuild keeps the list where it was
                    _shell.RefreshSaveSelect(); // force a rebuild so the deleted world drops off the list (B59)
                }, ButtonTier.Danger, "btn_delete");
                UiKit.AddTierButton(panel, 370f, 160f, 290f, 58f, _shell.L("ui.save.delete_no"), () => _confirm.SetActive(false),
                    ButtonTier.Quiet, "btn_back");
                _confirm.SetActive(false);
            }
        }
    }
}
