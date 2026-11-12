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
    /// The admin panel's Chat tab (#3507): the session's direct conversations on the left (unread lines counted), the picked
    /// one's lines on the right, and a line to send (<see cref="NetworkClient.SendDirectChat"/>). A conversation starts from
    /// the Players tab's Chat button or a report's "Chat with reporter" — before the first line there is no thread yet, so the
    /// panel keeps the partner itself. Showing a thread marks its lines read.
    /// </summary>
    public sealed partial class AdminPanelUi
    {
        private const float ThreadsW = 360f, ThreadRowH = 52f, ChatRight = ThreadsW + 20f, InputH = 52f, SendW = 170f;

        private string _chatPartnerId = string.Empty;
        private string _chatPartnerName = string.Empty;
        private RectTransform _threadList;
        private RectTransform _lineList;
        private ScrollRect _lineScroll;
        private Text _chatHeader;
        private InputField _chatInput;

        /// <summary>Opens the Chat tab on the conversation with <paramref name="playerId"/> (the Players tab's Chat, a
        /// report's "Chat with reporter").</summary>
        private void OpenConversation(string playerId, string name)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return;
            }

            _chatPartnerId = playerId;
            _chatPartnerName = name ?? string.Empty;
            if (_tab == Tab.Chat)
            {
                Invalidate();
            }
            else
            {
                SwitchTab(Tab.Chat);
            }
        }

        private void ForgetChatWidgets()
        {
            _threadList = null;
            _lineList = null;
            _lineScroll = null;
            _chatHeader = null;
            _chatInput = null;
        }

        private void BuildChatTab()
        {
            _threadList = UiKit.AddScrollViewport(_tabRoot, "AdminThreads", 0f, 0f, ThreadsW, ContentH, out var threadScroll);
            UiKit.AddInlineScrollbar(threadScroll);

            float rightW = ContentW - ChatRight;
            _chatHeader = AddDataText(_tabRoot, ChatRight, 0f, rightW, 34f, string.Empty, 21, UiKit.Cyan, FontStyle.Bold);
            var note = UiKit.AddText(_tabRoot, ChatRight, 36f, rightW, 26f, L("ui.admin.chat_note"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelLines(note, 15, 13, 13, 1);

            float linesTop = 70f, linesH = ContentH - linesTop - InputH - 14f;
            _lineList = UiKit.AddScrollViewport(_tabRoot, "AdminLines", ChatRight, linesTop, rightW, linesH, out _lineScroll);
            UiKit.AddInlineScrollbar(_lineScroll);

            _chatInput = UiKit.AddInput(_tabRoot, ChatRight, ContentH - InputH, rightW - SendW - 12f, InputH, string.Empty, null,
                L("ui.admin.chat_placeholder"), 200);
            _chatInput.onEndEdit.AddListener(OnChatEndEdit);
            UiKit.AddFitButton(_tabRoot, ContentW - SendW, ContentH - InputH, SendW, InputH, L("ui.admin.send"), SendChatLine);
        }

        /// <summary>Enter in the field sends (a click elsewhere only leaves it).</summary>
        private void OnChatEndEdit(string _)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                SendChatLine();
            }
        }

        private void SendChatLine()
        {
            var net = Game?.Network;
            string text = _chatInput != null ? (_chatInput.text ?? string.Empty).Trim() : string.Empty;
            if (net == null || text.Length == 0 || _chatPartnerId.Length == 0)
            {
                return;
            }

            net.SendDirectChat(_chatPartnerId, text); // the server echoes it back into the thread, screened
            _chatInput.text = string.Empty;
            if (InputMap.ActiveDevice == InputDeviceKind.KeyboardMouse)
            {
                _chatInput.ActivateInputField(); // keep typing
            }
        }

        private void RefreshChat(NetworkClient net)
        {
            var threads = net.DirectChats.Threads;
            if (_chatPartnerId.Length == 0 && threads.Count > 0)
            {
                _chatPartnerId = threads[0].PartnerId;
                _chatPartnerName = threads[0].PartnerName;
            }

            ClearRows(_threadList);
            float y = 0f;
            foreach (var t in threads)
            {
                string id = t.PartnerId, name = t.PartnerName;
                string label = t.Unread > 0 && id != _chatPartnerId ? $"{name} ({t.Unread})" : name;
                var button = UiKit.AddFitButton(_threadList, 0f, y, ThreadsW - 16f, ThreadRowH - 6f, label, () => OpenConversation(id, name));
                button.GetComponentInChildren<Text>().supportRichText = false;
                if (id == _chatPartnerId)
                {
                    button.GetComponent<Image>().color = new Color(0.55f, 0.88f, 1f);
                }

                y += ThreadRowH;
            }

            _threadList.sizeDelta = new Vector2(0f, y);
            RefreshChatLines(net);
        }

        private void RefreshChatLines(NetworkClient net)
        {
            ClearRows(_lineList);
            bool none = _chatPartnerId.Length == 0;
            _chatHeader.text = none
                ? L(net.DirectChats.Threads.Count == 0 ? "ui.admin.chat_none" : "ui.admin.chat_pick")
                : AdminPanelText.Fill(L("ui.admin.chat_with"), _chatPartnerName);
            _chatInput.interactable = !none;

            var thread = none ? null : net.DirectChats.Find(_chatPartnerId);
            float y = 6f, width = ContentW - ChatRight - 30f;
            if (thread != null)
            {
                foreach (var line in thread.Lines)
                {
                    y += AddChatLine(line, y, width) + 6f;
                }

                net.DirectChats.MarkRead(_chatPartnerId);
            }

            _lineList.sizeDelta = new Vector2(0f, y);
            _lineScroll.verticalNormalizedPosition = 0f; // the newest line in view
        }

        /// <summary>One line: "HH:mm:ss  Name: text" — the admin side in the panel's blue. Returns its height.</summary>
        private float AddChatLine(DirectChatMessage line, float y, float width)
        {
            string text = $"{AdminPanelText.LocalClock(line.UnixTime)}  {line.FromName}: {line.Text}";
            var label = UiKit.AddMeasuredText(_lineList, 8f, y, width, text, 17, line.FromAdmin ? UiKit.Cyan : UiKit.TextCol, out float h);
            label.supportRichText = false;
            return h;
        }
    }
}
