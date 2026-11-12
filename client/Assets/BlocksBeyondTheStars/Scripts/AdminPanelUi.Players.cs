// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Admin;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The admin panel's Players tab (#3505): every online player of the world, grouped by body — name, role, state, the
    /// position, health, duel / protected / chat-paused and idle time — with <b>Jump</b> and <b>Follow</b> (the invisible
    /// admin mode only), <b>Chat</b> (the Chat tab on that conversation), <b>Silence</b> (the existing <c>/silence</c> /
    /// <c>/unsilence</c> admin commands) and <b>Kick</b> (<c>/kick</c>, after a second click). The roster arrives once a
    /// second: the rows are rebuilt only when somebody joins, leaves or changes body (<see cref="AdminPanelText.RosterLayout"/>)
    /// and otherwise only their texts are rewritten.
    /// </summary>
    public sealed partial class AdminPanelUi
    {
        private const float GroupH = 40f, RowH = 90f, RowGap = 6f, ButtonH = 46f, ButtonGap = 8f;
        private const float JumpW = 130f, FollowW = 210f, ChatW = 120f, SilenceW = 160f, KickW = 190f;
        private const float KickArmSeconds = 4f;

        /// <summary>One drawn player row — what a roster without layout changes rewrites.</summary>
        private sealed class PlayerRow
        {
            public string Id;
            public Text Headline, Detail, Follow, Silence, Kick;
        }

        private readonly List<PlayerRow> _playerRows = new List<PlayerRow>();
        private string _rosterLayout;
        private string _kickArmedId = string.Empty;
        private float _kickArmedUntil;

        /// <summary>The width the buttons of a row take, from its right edge.</summary>
        private static float ButtonsWidth => JumpW + FollowW + ChatW + SilenceW + KickW + (4f * ButtonGap);

        private void BuildPlayersTab()
        {
            _list = AddList("AdminPlayers", 0f);
        }

        private void RefreshPlayers(NetworkClient net)
        {
            var groups = net.AdminPlayers.ByBody();
            // The mode is part of the layout: the visible variant has no Jump or Follow buttons.
            string layout = AdminPanelText.RosterLayout(groups) + (Invisible ? "#i" : "#v");
            if (layout != _rosterLayout)
            {
                _rosterLayout = layout;
                RebuildPlayerRows(groups);
            }

            foreach (var row in _playerRows)
            {
                if (net.AdminPlayers.Find(row.Id) is { } p)
                {
                    WritePlayerRow(row, p);
                }
            }
        }

        private void RebuildPlayerRows(IReadOnlyList<AdminRosterGroup> groups)
        {
            ClearRows(_list);
            _playerRows.Clear();
            if (groups.Count == 0)
            {
                AddEmptyNote(_list, "ui.admin.players_none");
                _list.sizeDelta = new Vector2(0f, 64f);
                return;
            }

            float y = 0f;
            float width = ContentW - 16f; // the inline scrollbar's strip
            foreach (var g in groups)
            {
                string body = string.IsNullOrEmpty(g.LocationId) ? L("ui.admin.body_unknown") : g.LocationName;
                AddDataText(_list, 8f, y + 6f, width - 16f, GroupH - 8f, $"{body}  ({g.Players.Count})", 21, UiKit.Cyan, FontStyle.Bold);
                y += GroupH;
                foreach (var p in g.Players)
                {
                    _playerRows.Add(AddPlayerRow(p, y, width));
                    y += RowH + RowGap;
                }
            }

            _list.sizeDelta = new Vector2(0f, y + 8f);
        }

        private PlayerRow AddPlayerRow(NetAdminPlayer p, float y, float width)
        {
            UiKit.AddPanel(_list, 0f, y, width, RowH, new Color(0.10f, 0.20f, 0.34f, 0.55f));
            float textW = width - ButtonsWidth - 36f;
            var row = new PlayerRow
            {
                Id = p.PlayerId,
                Headline = AddDataText(_list, 14f, y + 8f, textW, 30f, string.Empty, 20, UiKit.TextCol, FontStyle.Bold),
                Detail = AddDataText(_list, 14f, y + 40f, textW, 44f, string.Empty, 15, UiKit.CyanDim),
            };

            bool self = p.PlayerId == Game.LocalPlayerId;
            if (self)
            {
                return row; // the admin's own row: nothing to do with oneself
            }

            string id = p.PlayerId, name = p.Name;
            float bx = width - ButtonsWidth - 12f, by = y + ((RowH - ButtonH) * 0.5f);
            if (Invisible)
            {
                UiKit.AddFitButton(_list, bx, by, JumpW, ButtonH, L("ui.admin.jump"), () => JumpToPlayer(id));
                row.Follow = UiKit.AddFitButton(_list, bx + JumpW + ButtonGap, by, FollowW, ButtonH, L("ui.admin.follow"), () => ToggleFollow(id, name))
                    .GetComponentInChildren<Text>();
            }

            bx += JumpW + FollowW + (2f * ButtonGap);
            UiKit.AddFitButton(_list, bx, by, ChatW, ButtonH, L("ui.admin.chat"), () => OpenConversation(id, name));
            bx += ChatW + ButtonGap;
            row.Silence = UiKit.AddFitButton(_list, bx, by, SilenceW, ButtonH, L("ui.admin.silence"), () => ToggleSilence(id, name))
                .GetComponentInChildren<Text>();
            bx += SilenceW + ButtonGap;
            row.Kick = UiKit.AddFitButton(_list, bx, by, KickW, ButtonH, L("ui.admin.kick"), () => Kick(id, name)).GetComponentInChildren<Text>();
            return row;
        }

        /// <summary>A row's texts and its toggling button labels, each written only when it changed.</summary>
        private void WritePlayerRow(PlayerRow row, NetAdminPlayer p)
        {
            string headline = AdminPanelText.PlayerHeadline(p, _loc);
            if (p.PlayerId == Game.LocalPlayerId)
            {
                headline += "  " + L("ui.admin.you");
            }

            SetText(row.Headline, headline);
            SetText(row.Detail, AdminPanelText.PlayerDetail(p, _loc));
            SetText(row.Follow, L(Player != null && Player.AdminFollowTargetId == p.PlayerId ? "ui.admin.unfollow" : "ui.admin.follow"));
            SetText(row.Silence, L(p.Silenced ? "ui.admin.unsilence" : "ui.admin.silence"));
            SetText(row.Kick, L(_kickArmedId == p.PlayerId ? "ui.admin.kick_confirm" : "ui.admin.kick"));
        }

        private static void SetText(Text label, string text)
        {
            if (label != null && label.text != text)
            {
                label.text = text;
            }
        }

        /// <summary>Follow: jump to the player, then keep behind them (the panel closes so the admin watches). Again on the
        /// followed player: stop.</summary>
        private void ToggleFollow(string playerId, string name)
        {
            if (Player == null)
            {
                return;
            }

            if (Player.AdminFollowTargetId == playerId)
            {
                Player.StopAdminFollow();
                Invalidate();
                return;
            }

            Game?.Network?.SendAdminJumpToPlayer(playerId);
            Player.StartAdminFollow(playerId, name);
            Close();
        }

        /// <summary>The existing chat commands' path (<c>/silence Name</c> with the server's default minutes, or
        /// <c>/unsilence Name</c>), by the state the roster shows right now.</summary>
        private void ToggleSilence(string playerId, string name)
        {
            var net = Game?.Network;
            if (net == null)
            {
                return;
            }

            if (net.AdminPlayers.Find(playerId) is { Silenced: true })
            {
                net.SendAdminCommand("unsilence", stringArg: name);
            }
            else
            {
                net.SendAdminCommand("silence", stringArg: name, intArg: 0);
            }
        }

        /// <summary>Kick needs a second click within a few seconds — the first only arms the button.</summary>
        private void Kick(string playerId, string name)
        {
            if (_kickArmedId == playerId && Time.realtimeSinceStartup < _kickArmedUntil)
            {
                _kickArmedId = string.Empty;
                Game?.Network?.SendAdminCommand("kick", stringArg: name);
            }
            else
            {
                _kickArmedId = playerId;
                _kickArmedUntil = Time.realtimeSinceStartup + KickArmSeconds;
            }

            Invalidate();
        }

        /// <summary>An armed Kick falls back after a few seconds.</summary>
        private void UpdateKickArm()
        {
            if (_kickArmedId.Length > 0 && Time.realtimeSinceStartup >= _kickArmedUntil)
            {
                _kickArmedId = string.Empty;
                Invalidate();
            }
        }
    }
}
