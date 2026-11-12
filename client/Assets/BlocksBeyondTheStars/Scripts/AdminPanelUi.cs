// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Admin;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The in-game admin view (#3509, epic #3502): a modal panel with four tabs — <b>Players</b> (who plays where, with Jump,
    /// Follow, Chat, Silence and Kick), <b>Log</b> (the world's event log with a filter per category), <b>Chat</b> (the direct
    /// conversations with players) and <b>Reports</b> (every report filed in this world). The header names the mode — the
    /// invisible admin mode, or on an official hosted world the visible variant (no jump, no follow) — and leaves it.
    /// <para>F8 (<see cref="InputAction.AdminMode"/>) or the pause menu's Admin button enter admin mode and open the panel once
    /// the server confirms it; in admin mode F8 toggles the panel, and closing it (F8, Esc, Close) keeps the mode — the admin
    /// flies and watches. Everything shown comes from the server's models in <see cref="NetworkClient"/> (the roster, the log,
    /// the threads, the reports); every button sends an intent or an existing admin command, and the server decides.</para>
    /// <para>Shape B2 of <see cref="UiPanel"/>: the canvas once, an overlay per open; inside it the active tab's content is
    /// rebuilt when its model changes (the Players tab only rewrites its rows' texts while nobody joins, leaves or moves to
    /// another body, so the pad keeps its focus). The tabs live in <c>AdminPanelUi.*.cs</c>.</para>
    /// </summary>
    public sealed partial class AdminPanelUi : UiPanel
    {
        private enum Tab { Players, Log, Chat, Reports }

        private const float W = 1560f, H = 920f;
        private const float ContentX = 28f, ContentY = 176f, ContentW = W - 56f, ContentH = H - ContentY - 24f;
        private const float PendingSeconds = 5f;    // how long an F8 waits for the server to confirm admin mode
        private const float RefreshInterval = 0.5f; // the most often a tab's rows are rebuilt for model changes

        private static readonly string[] TabKeys = { "ui.admin.tab_players", "ui.admin.tab_log", "ui.admin.tab_chat", "ui.admin.tab_reports" };

        /// <summary>The local player (wired by WorldRig): Follow steers its observer flight.</summary>
        public PlayerController Player;

        private Tab _tab = Tab.Players;
        private Transform _panel;
        private Transform _tabRoot;
        private readonly Button[] _tabButtons = new Button[4];
        private readonly Text[] _tabLabels = new Text[4];
        private readonly int[] _tabCounts = { -1, -1, -1, -1 };
        private System.Func<string, string> _loc;
        private NetworkClient _net;
        private PlayerController _followHooked;
        private float _openPendingUntil;
        private float _nextRefresh;
        private int _builtVersion = int.MinValue;
        private bool _fieldFocusedPrev;
        private int _newReportNotices;
        private long _localEventId; // the follow-ended lines this client adds to the log count down from -1

        private int _countsRoster = -1, _countsChat = -1, _countsReports = -1, _countsNotices = -1;

        /// <summary>Open reports for the HUD's admin badge: the last list's open rows plus the "new report" notices that came
        /// since (the list itself only streams while the panel is open).</summary>
        public int OpenReports { get; private set; }

        /// <summary>Direct lines not read in the Chat tab yet, for the HUD's admin badge.</summary>
        public int UnreadDirect { get; private set; }

        /// <summary>The followed player's name, or empty (the HUD's admin badge names it).</summary>
        public string FollowingName => Player != null ? Player.AdminFollowTargetName : string.Empty;

        protected override string CanvasName => "AdminPanelUI";

        protected override int CanvasSortingOrder => 58;

        private bool Invisible => Game?.Network != null && Game.Network.AdminMode == AdminModes.Invisible;

        private void Awake() => _loc = L;

        /// <summary>Opens the admin view — F8 and the pause menu's Admin button. Out of admin mode it asks the server first and
        /// opens once the mode is confirmed (a refusal arrives as a toast instead).</summary>
        public void RequestOpen()
        {
            var net = Game?.Network;
            if (net == null || !net.HasAdminView || Shown)
            {
                return;
            }

            if (net.AdminMode != AdminModes.Off)
            {
                Open();
                return;
            }

            net.SendAdminMode(true);
            _openPendingUntil = Time.realtimeSinceStartup + PendingSeconds;
        }

        private void Open()
        {
            _openPendingUntil = 0f;
            EnsureCanvas();
            ShowCanvas();
            BuildFrame();
            PlayOpenEffect();
            SetMenuOwned(true);
            Game.Network.SendAdminPanel(true); // the server sends the roster, the backlog and the reports at once
        }

        /// <summary>Closes the panel and keeps admin mode (fly and watch).</summary>
        public void Close() => CloseCore(notifyServer: true);

        /// <summary>Takes the panel down; <paramref name="notifyServer"/> false when the server already ended admin mode.</summary>
        private void CloseCore(bool notifyServer)
        {
            if (!Shown)
            {
                return;
            }

            UiKit.ReleaseTextFieldFocus(Canvas != null ? Canvas.transform : null);
            DestroyOverlay();
            HideCanvas();
            SetMenuOwned(false);
            ForgetTabWidgets();
            _panel = null;
            if (notifyServer)
            {
                Game?.Network?.SendAdminPanel(false); // the server stops the roster stream
            }
        }

        private void LeaveAdminMode()
        {
            Player?.StopAdminFollow();
            Close();
            Game?.Network?.SendAdminMode(false); // the server puts the admin back where admin mode started
        }

        private void Update()
        {
            var net = Game?.Network;
            if (net == null)
            {
                return;
            }

            Hook(net);
            UpdateCounts(net);
            if (_openPendingUntil > 0f)
            {
                if (net.AdminMode != AdminModes.Off)
                {
                    Open();
                }
                else if (Time.realtimeSinceStartup > _openPendingUntil)
                {
                    _openPendingUntil = 0f; // refused (the server said why) or lost — F8 asks again
                }
            }

            if (Shown)
            {
                UpdateOpen(net);
            }
            else if (net.HasAdminView && InputMap.Down(InputAction.AdminMode) && !Game.MenuOpen && !Game.ChatTyping
                     && !GameBootstrap.CutsceneActive)
            {
                RequestOpen();
            }
        }

        private void UpdateOpen(NetworkClient net)
        {
            // Esc while typing only leaves the field (#413 N5); the field may clear its focus before this runs, so the
            // previous frame's focus counts too. F8 never reaches here while typing (InputMap's text-entry gate).
            bool typing = UiKit.TextFieldFocused();
            bool typingRecent = typing || _fieldFocusedPrev;
            _fieldFocusedPrev = typing;
            if ((CancelPressed() && !typingRecent) || (Time.frameCount != OpenFrame && InputMap.Down(InputAction.AdminMode)))
            {
                ConsumeMenuInput();
                Close();
                return;
            }

            UpdateKickArm();
            RefreshTabLabels();
            int version = TabVersion(net);
            bool due = Time.unscaledTime >= _nextRefresh || _builtVersion == int.MinValue;
            if (version != _builtVersion && due && _tabRoot != null)
            {
                RefreshTab(net);
                _builtVersion = TabVersion(net); // after the refresh: showing a thread marks it read
                _nextRefresh = Time.unscaledTime + RefreshInterval;
            }
        }

        /// <summary>The active tab's model version: its rows are rebuilt (or rewritten) when it changes.</summary>
        private int TabVersion(NetworkClient net) => _tab switch
        {
            Tab.Players => net.AdminPlayers.Version,
            Tab.Log => net.AdminLog.Version,
            Tab.Chat => net.DirectChats.Version,
            _ => net.AdminReports.Version,
        };

        /// <summary>Something the rows show changed outside the model (a click, a toggle): refresh on this frame.</summary>
        private void Invalidate() => _builtVersion = int.MinValue;

        private void RefreshTab(NetworkClient net)
        {
            switch (_tab)
            {
                case Tab.Players:
                    RefreshPlayers(net);
                    break;
                case Tab.Log:
                    RefreshLog(net);
                    break;
                case Tab.Chat:
                    RefreshChat(net);
                    break;
                default:
                    RefreshReports(net);
                    break;
            }
        }

        /// <summary>The header (title, the mode line, Leave and Close) and the tab row; then the active tab.</summary>
        private void BuildFrame()
        {
            _panel = AddModal((1920f - W) * 0.5f, (1080f - H) * 0.5f, W, H);
            var title = UiKit.AddText(_panel, 28f, 18f, 640f, 40f, L("ui.admin.title"), 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(title);
            UiKit.FitLabelLines(title, 26, 16, 14, 1);

            // The mode line: the invisible mode names its key; the visible variant of a hosted world says what it lacks.
            string mode = Invisible ? HudUi.ExpandKeyTokens(Game.Localizer, L("ui.admin.mode_invisible")) : L("ui.admin.mode_visible");
            var modeLine = UiKit.AddText(_panel, 28f, 62f, 1000f, 46f, mode, 16, Invisible ? UiKit.CyanDim : UiKit.Warn, TextAnchor.UpperLeft);
            UiKit.FitLabelLines(modeLine, 16, 14, 13);

            UiKit.AddFitButton(_panel, W - 28f - 160f - 12f - 300f, 18f, 300f, 46f, L("ui.admin.leave"), LeaveAdminMode);
            UiKit.AddFitButton(_panel, W - 28f - 160f, 18f, 160f, 46f, L("ui.admin.close"), Close);

            for (int i = 0; i < TabKeys.Length; i++)
            {
                var tab = (Tab)i;
                _tabButtons[i] = UiKit.AddFitButton(_panel, 28f + (i * 244f), 118f, 232f, 46f, L(TabKeys[i]), () => SwitchTab(tab));
                _tabLabels[i] = _tabButtons[i].GetComponentInChildren<Text>();
                _tabCounts[i] = -1;
            }

            BuildTab();
        }

        private void SwitchTab(Tab tab)
        {
            if (_tab == tab && _tabRoot != null)
            {
                return;
            }

            _tab = tab;
            BuildTab();
        }

        /// <summary>A fresh content root for the active tab (the old one goes at the end of the frame), its static part,
        /// then the rows on this frame.</summary>
        private void BuildTab()
        {
            if (_panel == null)
            {
                return;
            }

            if (_tabRoot != null)
            {
                UiKit.ReleaseTextFieldFocus(_tabRoot);
                Destroy(_tabRoot.gameObject);
            }

            ForgetTabWidgets();
            var root = new GameObject("AdminTab", typeof(RectTransform));
            root.transform.SetParent(_panel, false);
            UiKit.Place(root, ContentX, ContentY, ContentW, ContentH);
            _tabRoot = root.transform;
            switch (_tab)
            {
                case Tab.Players:
                    BuildPlayersTab();
                    break;
                case Tab.Log:
                    BuildLogTab();
                    break;
                case Tab.Chat:
                    BuildChatTab();
                    break;
                default:
                    BuildReportsTab();
                    break;
            }

            StyleTabs();
            Invalidate();
        }

        private void StyleTabs()
        {
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] != null)
                {
                    _tabButtons[i].GetComponent<Image>().color = i == (int)_tab ? new Color(0.55f, 0.88f, 1f) : UiKit.PanelFill;
                }
            }
        }

        /// <summary>The tab labels carry their counts — players online, unread lines, open reports — rewritten only when one
        /// changes.</summary>
        private void RefreshTabLabels()
        {
            var net = Game.Network;
            SetTabCount(0, net.AdminPlayers.Players.Count);
            SetTabCount(1, 0);
            SetTabCount(2, UnreadDirect);
            SetTabCount(3, OpenReports);
        }

        private void SetTabCount(int index, int count)
        {
            if (_tabLabels[index] == null || _tabCounts[index] == count)
            {
                return;
            }

            _tabCounts[index] = count;
            _tabLabels[index].text = count > 0 ? $"{L(TabKeys[index])} ({count})" : L(TabKeys[index]);
        }

        /// <summary>Drops the references into the tab content that is going away.</summary>
        private void ForgetTabWidgets()
        {
            _tabRoot = null;
            _list = null;
            _scroll = null;
            _rosterLayout = null;
            _playerRows.Clear();
            ForgetChatWidgets();
        }

        // ---- Network hooks and the badge counts ---------------------------------------------------------------------------

        private void Hook(NetworkClient net)
        {
            if (!ReferenceEquals(_net, net))
            {
                Unhook();
                _net = net;
                net.AdminStateChanged += OnAdminState;
                net.ServerMessageReceived += OnServerMessage;
                net.AdminReportsReceived += OnReports;
            }

            if (!ReferenceEquals(_followHooked, Player) && Player != null)
            {
                if (_followHooked != null)
                {
                    _followHooked.AdminFollowLost -= OnFollowLost;
                }

                _followHooked = Player;
                Player.AdminFollowLost += OnFollowLost;
            }
        }

        private void Unhook()
        {
            if (_net != null)
            {
                _net.AdminStateChanged -= OnAdminState;
                _net.ServerMessageReceived -= OnServerMessage;
                _net.AdminReportsReceived -= OnReports;
                _net = null;
            }

            if (_followHooked != null)
            {
                _followHooked.AdminFollowLost -= OnFollowLost;
                _followHooked = null;
            }
        }

        /// <summary>The server changed the role or the mode: out of admin mode the panel closes and following ends; a mode
        /// switch while it is open redraws the header (the visible variant has no jump or follow).</summary>
        private void OnAdminState()
        {
            var net = Game?.Network;
            if (net == null || this == null)
            {
                return;
            }

            if (net.AdminMode == AdminModes.Off)
            {
                Player?.StopAdminFollow();
                CloseCore(notifyServer: false);
            }
            else if (Shown)
            {
                ForgetTabWidgets();
                DestroyOverlay();
                BuildFrame();
            }
        }

        /// <summary>A report arrived while the panel was closed: the server sent a notice instead of the list.</summary>
        private void OnServerMessage(ServerMessage m)
        {
            if (m != null && !Shown && m.Text == "@srv.admin.new_report")
            {
                _newReportNotices++;
            }
        }

        private void OnReports(AdminReportList _) => _newReportNotices = 0;

        /// <summary>Following ended because the player left this body: a line in the log and a toast.</summary>
        private void OnFollowLost(string name)
        {
            var net = Game?.Network;
            if (net == null)
            {
                return;
            }

            net.AdminLog.Add(new NetAdminEvent
            {
                Id = --_localEventId, // local: below every server id
                UnixTime = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Category = AdminEventCategories.Admin,
                TargetName = name,
                TextKey = "admin.ev.follow_lost",
                Args = new[] { name },
            });
            Game.ShowMessage(AdminEventLogModel.Fill(L("admin.ev.follow_lost"), new[] { name }, _loc));
        }

        /// <summary>Keeps the HUD badge's counts current — recounted only when one of the models changed.</summary>
        private void UpdateCounts(NetworkClient net)
        {
            if (_countsRoster == net.AdminPlayers.Version && _countsChat == net.DirectChats.Version
                && _countsReports == net.AdminReports.Version && _countsNotices == _newReportNotices)
            {
                return;
            }

            _countsRoster = net.AdminPlayers.Version;
            _countsChat = net.DirectChats.Version;
            _countsReports = net.AdminReports.Version;
            _countsNotices = _newReportNotices;
            OpenReports = net.AdminReports.OpenCount + _newReportNotices;
            UnreadDirect = net.HasAdminView ? net.DirectChats.UnreadTotal : 0;
        }

        /// <summary>Gives the menu back if the panel was open; the base drops the registration and the canvas.</summary>
        protected override void OnDestroy()
        {
            if (Shown)
            {
                Shown = false;
                SetMenuOwned(false);
            }

            Unhook();
            base.OnDestroy();
        }

        // ---- Shared row helpers --------------------------------------------------------------------------------------------

        private RectTransform _list;
        private ScrollRect _scroll;

        /// <summary>A scrolling list filling the tab below <paramref name="top"/>.</summary>
        private RectTransform AddList(string name, float top)
        {
            var list = UiKit.AddScrollViewport(_tabRoot, name, 0f, top, ContentW, ContentH - top, out _scroll);
            UiKit.AddInlineScrollbar(_scroll);
            return list;
        }

        /// <summary>Removes a list's rows (their GameObjects go at the end of the frame).</summary>
        private static void ClearRows(Transform list)
        {
            if (list == null)
            {
                return;
            }

            for (int i = list.childCount - 1; i >= 0; i--)
            {
                var child = list.GetChild(i).gameObject;
                child.SetActive(false); // out of the layout and the pad's reach at once
                Destroy(child);
            }
        }

        /// <summary>A note in an empty list.</summary>
        private void AddEmptyNote(Transform list, string key)
        {
            var note = UiKit.AddText(list, 12f, 12f, ContentW - 40f, 40f, L(key), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelLines(note, 18, 14, 13);
        }

        /// <summary>A data line (a name, a chat line): plain text, never rich text, wrapped into its box.</summary>
        private static Text AddDataText(Transform parent, float x, float y, float w, float h, string text, int size, Color color,
            FontStyle style = FontStyle.Normal)
        {
            var t = UiKit.AddText(parent, x, y, w, h, text, size, color, TextAnchor.MiddleLeft, style);
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        /// <summary>Jumps (the invisible admin mode) and closes the panel, so the admin sees where they landed.</summary>
        private void JumpToPlayer(string playerId)
        {
            Player?.StopAdminFollow();
            Game?.Network?.SendAdminJumpToPlayer(playerId);
            Close();
        }

        private void JumpToPlace(string locationId, float x, float y, float z)
        {
            Player?.StopAdminFollow();
            Game?.Network?.SendAdminJumpToPlace(locationId, x, y, z);
            Close();
        }
    }
}
