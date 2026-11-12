// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Admin;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The direct chat between an admin and a player in the chat window (#3507): a line to or from this player shows
    /// highlighted with its direction — "Admin Name → you", "You → Admin Name" (<see cref="AdminPanelText.DirectLinePrefix"/>);
    /// an admin's line also raises a short notice. The answer is <c>/r Text</c> (the server routes it): the first time the chat
    /// opens after a line came in, the input already holds <c>/r </c>. Conversations between other admins and players, which an
    /// admin's panel mirrors, stay in the panel.
    /// </summary>
    public sealed partial class ChatUi
    {
        private const string ReplyPrefix = "/r ";

        private bool _replyPrefill;       // a direct line came in since the chat last opened
        private bool _replyCaretPending;  // the field holds the prefix and wants its caret after it once it has focus

        private void OnDirectChat(DirectChatMessage m)
        {
            if (Game == null || m == null)
            {
                return;
            }

            // A muted player's answer stays out like their chat (#1209); an admin's line always gets through.
            if (!m.FromAdmin && Settings != null && Settings.IsMuted(m.FromId, m.FromName))
            {
                return;
            }

            string prefix = AdminPanelText.DirectLinePrefix(m, Game.LocalPlayerId, L);
            if (prefix == null)
            {
                return; // another admin's conversation — the admin panel shows it
            }

            Append($"<color=#FFD27A><b>{ChatMarkup.RichSafe(prefix)}:</b> {ChatMarkup.RichSafe(m.Text)}</color>");
            if (m.FromId != Game.LocalPlayerId)
            {
                _replyPrefill = true;
                if (m.FromAdmin)
                {
                    Game.ShowMessage(HudUi.ExpandKeyTokens(Game.Localizer, L("ui.chat.dm_notice")));
                }
            }
        }

        /// <summary>The reply prefix for the field the chat opens with, once per incoming direct line (empty otherwise).</summary>
        private string TakeReplyPrefill()
        {
            if (!_replyPrefill)
            {
                return string.Empty;
            }

            _replyPrefill = false;
            return Game?.Network != null && Game.Network.DirectChats.ReplyTargetId.Length > 0 ? ReplyPrefix : string.Empty;
        }

        /// <summary>The keyboard window: puts the prefix into the field it just focused.</summary>
        private void PrefillReply()
        {
            string prefix = TakeReplyPrefill();
            if (prefix.Length == 0 || _input == null)
            {
                return;
            }

            _input.text = prefix;
            _replyCaretPending = true;
        }

        /// <summary>The field's focus selects its whole text (uGUI) — once it has focus, the caret goes after the prefix, so
        /// typing adds to it instead of replacing it.</summary>
        private void UpdateReplyCaret()
        {
            if (!_replyCaretPending || _input == null)
            {
                return;
            }

            if (!WindowOpen)
            {
                _replyCaretPending = false;
                return;
            }

            if (_input.isFocused)
            {
                _replyCaretPending = false;
                _input.caretPosition = _input.text.Length;
            }
        }
    }
}
