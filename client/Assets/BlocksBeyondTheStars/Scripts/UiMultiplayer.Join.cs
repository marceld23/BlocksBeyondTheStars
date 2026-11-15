// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    // The Multiplayer screen's Join tab (#3665): the old connect dialog as a card (without its name field — the name is
    // chosen on the main menu) and the servers joined before, one click each.
    public sealed partial class UiMultiplayer
    {
        /// <summary>The Join tab's two cards: "Connect directly" on the left, "Recently joined" on the right.</summary>
        public const float ConnectCardW = 820f, JoinCardH = 560f, CardGap = 40f;

        private const float RecentX = UiKit.MenuLeft + ConnectCardW + CardGap;
        private const float RecentW = UiKit.MenuRight - RecentX;
        private const float RecentTop = 64f, RecentPitch = 92f;

        private static readonly Color WarnCol = new Color(1f, 0.55f, 0.4f);

        // What the connect card's fields hold (prefilled from the last manual join, #978), and the fields themselves — a
        // recent server's Connect writes its address into them first, so a refused join leaves it there to fix.
        private string _joinHost = "", _joinPort = "", _joinPass = "";
        private InputField _hostInput, _portInput;
        private Text _joinWarn;
        private Transform _recentList;

        private void BuildJoinPane(Transform pane)
        {
            BuildConnectCard(pane);
            var recent = UiKit.AddCard(pane, RecentX, PaneTop, RecentW, JoinCardH);
            UiKit.AddKicker(recent, 40f, 24f, RecentW - 80f, L("ui.mp.recent_title"));
            _recentList = UiKit.AddStretchChild(recent, "RecentServers").transform;
            FillRecent();
        }

        private void BuildConnectCard(Transform pane)
        {
            _joinHost = _shell.ManualJoinHost;
            _joinPort = _shell.ManualJoinPort;
            _joinPass = "";
            const float x = 40f, w = ConnectCardW - 80f;
            var card = UiKit.AddCard(pane, UiKit.MenuLeft, PaneTop, ConnectCardW, JoinCardH);
            UiKit.AddKicker(card, x, 24f, w, L("ui.mp.connect_direct"));
            FieldLabel(card, x, 64f, w, "ui.menu.connect_host");
            _hostInput = UiKit.AddInput(card, x, 90f, w, 48f, _joinHost, v => _joinHost = v);
            FieldLabel(card, x, 156f, 220f, "ui.menu.connect_port");
            _portInput = UiKit.AddInput(card, x, 182f, 220f, 48f, _joinPort, v => _joinPort = v);
            // The field prefills the port a friend's hosted world uses (#978) — the one a player types by hand here.
            // Official worlds bring their own host and port, so the dedicated-server port no longer needs a mention.
            var hint = UiKit.AddText(card, x + 240f, 182f, w - 240f, 48f,
                L("ui.mp.port_hint").Replace("{hosted}", LocalServerLauncher.DefaultPort.ToString(CultureInfo.InvariantCulture)),
                15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            FieldLabel(card, x, 248f, w, "ui.menu.connect_password");
            var pass = UiKit.AddInput(card, x, 274f, w, 48f, _joinPass, v => _joinPass = v);
            pass.contentType = InputField.ContentType.Password; // masked like the portal's password fields
            _joinWarn = UiKit.AddText(card, x, 330f, w, 30f, "", 15, WarnCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddTierButton(card, x, 368f, w, 60f, L("ui.menu.connect"), Connect, ButtonTier.Primary, "btn_join");
            var tip = UiKit.AddText(card, x, 448f, w, 72f, L("ui.mp.connect_hint"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            tip.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        // Dials what the connect card holds — the old connect dialog's Connect, without its name field, and remembered for
        // the "Recently joined" list.
        private void Connect()
        {
            // A name is required (#221): joining anonymously fell back to a server-assigned "player_N" nobody recognizes.
            if (!CommitName())
            {
                _joinWarn.text = L("ui.webgl.need_name");
                return;
            }

            _joinWarn.text = "";
            // Remember what was typed for the next time this tab opens, then dial it. The prefill is kept apart from the
            // live join target (#978) — the official-worlds join overwrites the latter.
            _shell.ManualJoinHost = string.IsNullOrWhiteSpace(_joinHost) ? "127.0.0.1" : _joinHost.Trim();
            _shell.ManualJoinPort = string.IsNullOrWhiteSpace(_joinPort) ? _shell.ManualJoinPort : _joinPort.Trim();
            _shell.Host = _shell.ManualJoinHost;
            _shell.Port = _shell.ManualJoinPort;
            _shell.Password = _joinPass ?? "";
            _shell.Settings.RememberServer(_shell.Host, _shell.Port);
            _shell.Settings.Save();
            _shell.StartJoin();
        }

        private void ConnectTo(RecentServer server)
        {
            _joinHost = server.Host;
            _joinPort = server.Port;
            _hostInput.SetTextWithoutNotify(server.Host);
            _portInput.SetTextWithoutNotify(server.Port);
            Connect();
        }

        private void Forget(RecentServer server)
        {
            _shell.Settings.RecentServers?.Remove(server);
            _shell.Settings.Save();
            FillRecent();
        }

        // (Re)builds the "Recently joined" rows, newest first, at most ClientSettings.MaxRecentServers.
        private void FillRecent()
        {
            for (int i = _recentList.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(_recentList.GetChild(i).gameObject);
            }

            int shown = 0;
            foreach (var server in _shell.Settings.RecentServers ?? new System.Collections.Generic.List<RecentServer>())
            {
                if (server == null || string.IsNullOrWhiteSpace(server.Host) || shown >= ClientSettings.MaxRecentServers)
                {
                    continue;
                }

                AddRecentRow(server, RecentTop + shown * RecentPitch);
                shown++;
            }

            if (shown == 0)
            {
                var empty = UiKit.AddText(_recentList, 40f, RecentTop, RecentW - 80f, 72f, L("ui.mp.recent_empty"), 17,
                    UiKit.CyanDim, TextAnchor.UpperLeft);
                empty.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
        }

        private void AddRecentRow(RecentServer server, float y)
        {
            const float textW = 440f, connectX = 500f, connectW = 240f, forgetW = 64f, h = 54f;
            string address = string.IsNullOrEmpty(server.Port) ? server.Host : server.Host + ":" + server.Port;
            var name = UiKit.AddText(_recentList, 40f, y, textW, 30f, address, 20, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(name, textW, 12, 20);
            UiKit.AddText(_recentList, 40f, y + 32f, textW, 24f, AgeText(_shell.L, server.LastJoinedUnix), 15, UiKit.CyanDim);
            var s = server; // capture per row
            UiKit.AddTierButton(_recentList, connectX, y + 2f, connectW, h, L("ui.menu.connect"), () => ConnectTo(s),
                ButtonTier.Secondary, "btn_join");
            var forget = UiKit.AddTierButton(_recentList, connectX + connectW + 16f, y + 2f, forgetW, h, "", () => Forget(s),
                ButtonTier.Quiet, "btn_delete");
            CenterIcon(forget, forgetW, h);
        }

        // An icon-only button: AddButton leaves the icon at the left, where the label would follow — centre it instead.
        private static void CenterIcon(Button button, float w, float h)
        {
            foreach (var image in button.GetComponentsInChildren<Image>(true))
            {
                if (image != button.targetGraphic)
                {
                    UiKit.Place(image.gameObject, (w - 30f) / 2f, (h - 30f) / 2f, 30f, 30f);
                }
            }
        }

        // The last second DateTimeOffset can hold (9999-12-31) — a hand-edited settings file must not throw here.
        private const long MaxUnixSeconds = 253402300799L;

        /// <summary>"5 min ago" / "3 days ago" for a server's last join — the wording of the world list
        /// (<see cref="UiKit.TimeAgo"/>); empty when the time is unknown (0) or out of range.</summary>
        public static string AgeText(System.Func<string, string> localize, long lastJoinedUnix)
        {
            if (lastJoinedUnix <= 0 || lastJoinedUnix > MaxUnixSeconds)
            {
                return "";
            }

            return UiKit.TimeAgo(localize, System.DateTimeOffset.FromUnixTimeSeconds(lastJoinedUnix).UtcDateTime);
        }
    }
}
