// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client.Admin;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The in-game admin view (epic #3502) on the client: the player's own role and admin mode (from
    /// <see cref="PlayerStateUpdate"/>), the four Unity-free models the panel draws (<see cref="AdminPlayers"/>,
    /// <see cref="AdminLog"/>, <see cref="DirectChats"/>, <see cref="AdminReports"/>) and the intents it sends. Everything here is
    /// filled by the server — the client never decides who is an admin.
    /// </summary>
    public sealed partial class NetworkClient
    {
        private string _selfPlayerId = string.Empty;

        /// <summary>The player's own role as the server last said (<c>Shared.State.PlayerRole</c> as a byte: 0 Player,
        /// 1 Moderator, 2 Admin, 3 WorldAdmin).</summary>
        public byte Role { get; private set; }

        /// <summary>The admin mode the player is in (<see cref="AdminModes"/>: 0 off, 1 invisible, 2 visible).</summary>
        public byte AdminMode { get; private set; }

        /// <summary>The admin mode the player would get (<see cref="AdminModes"/>); 0 = no admin view (hide F8 and the pause
        /// menu's Admin button), 2 = the visible variant of an official hosted world's owner (show its note).</summary>
        public byte AdminModeAvailable { get; private set; }

        /// <summary>Whether this player may open the admin view at all.</summary>
        public bool HasAdminView => AdminModeAvailable != AdminModes.Off;

        /// <summary>#3508: the server files "Call an admin" (<see cref="Networking.Messages.JoinAccepted.AdminCallSupported"/>). False before the
        /// join and against an older server, which would drop the call.</summary>
        public bool AdminCallSupported { get; private set; }

        /// <summary>Whether this client offers "Call an admin" — the pause menu's button and the chat's <c>/admin Text</c>: the
        /// server must file it, and the player must not be an admin themselves (an admin has the admin view instead; a
        /// singleplayer world's player is its admin, and a call would only reach themselves).</summary>
        public bool OffersCallAdmin => AdminCallSupported && !HasAdminView;

        /// <summary>The Players tab: who plays where (#3505).</summary>
        public AdminRosterModel AdminPlayers { get; } = new AdminRosterModel();

        /// <summary>The Log tab (#3506).</summary>
        public AdminEventLogModel AdminLog { get; } = new AdminEventLogModel();

        /// <summary>The direct-chat threads — on every client, a player's too (the admin's lines and the answers, #3507).</summary>
        public DirectChatThreads DirectChats { get; } = new DirectChatThreads();

        /// <summary>The Reports tab (#3508).</summary>
        public AdminReportsModel AdminReports { get; } = new AdminReportsModel();

        /// <summary><see cref="Role"/>, <see cref="AdminMode"/> or <see cref="AdminModeAvailable"/> changed.</summary>
        public event Action? AdminStateChanged;

        /// <summary>A roster arrived (<see cref="AdminPlayers"/> is updated first).</summary>
        public event Action<AdminRoster>? AdminRosterReceived;

        /// <summary>The event log changed — a backlog or one live event (<see cref="AdminLog"/> is updated first).</summary>
        public event Action? AdminLogChanged;

        /// <summary>A direct line arrived (<see cref="DirectChats"/> is updated first). The chat window shows a line to this
        /// player highlighted ("Admin → you") with a short notice.</summary>
        public event Action<DirectChatMessage>? DirectChatReceived;

        /// <summary>The report list arrived (<see cref="AdminReports"/> is updated first).</summary>
        public event Action<AdminReportList>? AdminReportsReceived;

        /// <summary>Enters (true) or leaves admin mode.</summary>
        public void SendAdminMode(bool on) => Send(new AdminModeIntent { On = on });

        /// <summary>The admin panel opened (the server then streams the roster, the log and the reports) or closed.</summary>
        public void SendAdminPanel(bool open) => Send(new AdminPanelIntent { Open = open });

        /// <summary>Jump to a player (the invisible admin mode only).</summary>
        public void SendAdminJumpToPlayer(string playerId) => Send(new AdminJumpIntent { PlayerId = playerId ?? string.Empty });

        /// <summary>Jump to a place — a log row or a report with a position (the invisible admin mode only).</summary>
        public void SendAdminJumpToPlace(string locationId, float x, float y, float z)
            => Send(new AdminJumpIntent { LocationId = locationId ?? string.Empty, HasPosition = true, X = x, Y = y, Z = z });

        /// <summary>A direct line to <paramref name="toPlayerId"/> (an admin to anyone; a player only as an answer).</summary>
        public void SendDirectChat(string toPlayerId, string text)
            => Send(new DirectChatIntent { ToPlayerId = toPlayerId ?? string.Empty, Text = text ?? string.Empty });

        /// <summary>Answers whoever wrote last (the Reply button; <c>/r Text</c> in chat goes to the server as a chat line and
        /// lands in the same place). False when nobody wrote yet.</summary>
        public bool SendDirectReply(string text)
        {
            if (DirectChats.ReplyTargetId.Length == 0)
            {
                return false;
            }

            SendDirectChat(DirectChats.ReplyTargetId, text);
            return true;
        }

        /// <summary>Marks a report done (or open again).</summary>
        public void SendAdminReportAction(long id, bool done)
        {
            AdminReports.MarkLocally(id, done);
            Send(new AdminReportAction { Id = id, Done = done });
        }

        /// <summary>"Call an admin" — the pause menu's button and the chat's <c>/admin Text</c>.</summary>
        public void SendCallAdmin(string text) => Send(new CallAdminIntent { Text = text ?? string.Empty });

        /// <summary>The chat's <c>/admin Text</c>: sends the call when this client offers it (<see cref="OffersCallAdmin"/>)
        /// and there is text. False otherwise — the chat then shows the admin help, the command's old meaning.</summary>
        public bool TrySendCallAdmin(string? text)
        {
            string t = (text ?? string.Empty).Trim();
            if (!OffersCallAdmin || t.Length == 0)
            {
                return false;
            }

            SendCallAdmin(t);
            return true;
        }

        /// <summary>A player report by name. <paramref name="alsoFiledWithPortal"/>: the report already went to the portal
        /// (the official-world route of <c>/report</c> and the Alliances tab's Report button) — the server files it in the world's
        /// own list for its admins without forwarding it again.</summary>
        public void SendPlayerReport(string targetName, string note, bool alsoFiledWithPortal)
            => Send(new PlayerReportIntent
            {
                TargetName = targetName ?? string.Empty,
                Note = note ?? string.Empty,
                AlsoFiledWithPortal = alsoFiledWithPortal,
            });

        private void HandlePlayerStateUpdate(PlayerStateUpdate m)
        {
            // The update is the own player's by protocol; the id check only keeps a shared test connection (local players
            // beside the joined one) from handing this client somebody else's role.
            bool own = _selfPlayerId.Length == 0 || string.IsNullOrEmpty(m.PlayerId) || m.PlayerId == _selfPlayerId;
            if (!own)
            {
                PlayerStateUpdated?.Invoke(m);
                return;
            }

            if (!string.IsNullOrEmpty(m.PlayerId))
            {
                _selfPlayerId = m.PlayerId;
            }

            bool changed = m.Role != Role || m.AdminMode != AdminMode || m.AdminModeAvailable != AdminModeAvailable;
            byte leftMode = AdminMode;
            Role = m.Role;
            AdminMode = m.AdminMode;
            AdminModeAvailable = m.AdminModeAvailable;
            if (leftMode != AdminModes.Off && AdminMode == AdminModes.Off)
            {
                AdminPlayers.Clear(); // out of admin mode the panel is closed and the server stops the stream
            }

            PlayerStateUpdated?.Invoke(m);
            if (changed)
            {
                AdminStateChanged?.Invoke();
            }
        }

        private void HandleAdminRoster(AdminRoster m)
        {
            AdminPlayers.Apply(m);
            AdminRosterReceived?.Invoke(m);
        }

        private void HandleAdminEventBacklog(AdminEventBacklog m)
        {
            AdminLog.ApplyBacklog(m.Events);
            AdminLogChanged?.Invoke();
        }

        private void HandleAdminEvent(AdminEvent m)
        {
            if (AdminLog.Add(m.Event))
            {
                AdminLogChanged?.Invoke();
            }
        }

        private void HandleDirectChat(DirectChatMessage m)
        {
            DirectChats.Add(m, _selfPlayerId);
            DirectChatReceived?.Invoke(m);
        }

        private void HandleAdminReports(AdminReportList m)
        {
            AdminReports.Apply(m);
            AdminReportsReceived?.Invoke(m);
        }

        /// <summary>A fresh session starts without admin state — the server sends the role with the first player state; whether
        /// it files "Call an admin" comes with the join itself.</summary>
        private void ResetAdminState(string selfPlayerId, bool adminCallSupported)
        {
            _selfPlayerId = selfPlayerId ?? string.Empty;
            AdminCallSupported = adminCallSupported;
            Role = 0;
            AdminMode = AdminModes.Off;
            AdminModeAvailable = AdminModes.Off;
            AdminPlayers.Clear();
            AdminLog.Clear();
            DirectChats.Clear();
            AdminReports.Clear();
        }
    }
}
