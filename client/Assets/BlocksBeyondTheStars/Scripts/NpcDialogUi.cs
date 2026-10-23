// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The NPC dialogue panel (#1127): the NPC's line plus up to three reply buttons ([1]/[2]/[3] keys work
    /// too), or a single "Leave" on the closing line. Text arrives RESOLVED from the server (per-player
    /// content); the client only renders and sends the picked index back — every branch and consequence is
    /// server-authoritative. Modeled on the bandit hold-up panel (reflow for long lines, UiNav for pads,
    /// SetMenuOwner cursor arbitration); no countdown — a conversation waits, Esc simply walks away.
    /// <para>#2616: closing the panel while it still offers replies tells the server ("leave" — the NPC goes back to its
    /// day); the server closes it the other way with <see cref="NpcDialogState.ActionClose"/> when the conversation ended
    /// on its side (walked away, another world, the NPC gone) — that close sends nothing back.</para>
    /// </summary>
    public sealed class NpcDialogUi : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int MaxChoices = 3;
        private const float PanelW = 560f, PanelBaseH = 130f, RowW = 520f, LineBaseH = 48f;
        private const float ChoicesY = 104f, ChoiceH = 44f, ChoiceGap = 8f;

        private Canvas _canvas;
        private GameObject _panel;
        private Text _title, _line;
        private readonly Button[] _choiceButtons = new Button[MaxChoices];
        private readonly Text[] _choiceLabels = new Text[MaxChoices];
        private Button _leave;
        private Text _leaveLabel;

        private NpcDialogState _state; // the live dialogue step (null = closed)
        private bool _subscribed;
        private string _laidOutLine;

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (!_subscribed && Game.Network != null)
            {
                Game.Network.NpcDialogReceived += OnDialog;
                _subscribed = true;
            }

            if (_state is null || Game.Localizer is null)
            {
                return;
            }

            EnsureBuilt();
            Refresh();

            for (int i = 0; i < MaxChoices && i < _state.Choices.Length; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    Choose(i);
                    return;
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape) || (_state.End && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.E))))
            {
                Close(); // walking away is always allowed — the server ends the conversation (#2616)
            }
        }

        private void LateUpdate()
        {
            Game?.SetMenuOwner(this, _state is not null); // dialogue happens on foot — take the cursor
        }

        private void OnDialog(NpcDialogState m)
        {
            // #2616: the server ended the conversation (walked away, another world, the NPC gone, a new talk) — the panel
            // just goes, with no leave sent back. A close for another NPC's panel than the one shown changes nothing.
            if (m.Action == NpcDialogState.ActionClose)
            {
                if (_state is not null && _state.NpcId == m.NpcId)
                {
                    Hide();
                }

                return;
            }

            // #2592: the NPC speaks every line it says here in its own wordless voice — the conversation's first line as
            // its greeting, every later one (an answer) with a nod.
            bool opening = _state is null || _state.NpcId != m.NpcId;
            _state = m;
            ClientAudio.Instance?.Cue("ui_open", 0.6f);
            if (!string.IsNullOrEmpty(m.Text) && Game?.Views.Npcs != null)
            {
                Game.Views.Npcs.DialogLine(m.NpcId, m.Text, opening);
            }

            // 2026-09 professions: the streamer's photo and the reporter's interview follow the closing line.
            if (m.Action == "photo")
            {
                StartCoroutine(PhotoAfterLine());
            }
            else if (m.Action == "interview")
            {
                StartCoroutine(InterviewAfterLine(m.NpcId));
            }
            else if (m.Action == "heal")
            {
                ClientAudio.Instance?.Cue("heal"); // #2628: the doctor treated you (the server already filled your health)
            }
        }

        private System.Collections.IEnumerator PhotoAfterLine()
        {
            yield return new WaitForSecondsRealtime(1.2f); // "say cheese" — then the panel goes and the camera clicks
            Close();
            yield return null;
            FindAnyObjectByType<PlayerController>()?.TakePhoto();
        }

        private System.Collections.IEnumerator InterviewAfterLine(int npcId)
        {
            yield return new WaitForSecondsRealtime(0.8f);
            Close();
            Game?.Context.Panel<InterviewUi>()?.Open(npcId);
        }

        private void Choose(int index)
        {
            if (_state is null || _state.End || index >= _state.Choices.Length)
            {
                return;
            }

            Game.Network?.SendNpcDialogChoice(index, _state.NpcId);
        }

        /// <summary>The player closes the panel (Esc, Leave, the photo/interview hand-off). #2616: while it still offered
        /// replies the server holds the conversation (and the NPC stands still) — tell it the player left.</summary>
        private void Close()
        {
            if (_state is { End: false })
            {
                Game?.Network?.SendNpcDialogLeave(_state.NpcId);
            }

            Hide();
        }

        /// <summary>Takes the panel down without telling the server (its own close, #2616).</summary>
        private void Hide()
        {
            _state = null;
            _panel?.SetActive(false);
        }

        private void Refresh()
        {
            _panel.SetActive(true);
            _title.text = _state.Name;

            _line.text = "“" + _state.Text + "”";
            if (_line.text != _laidOutLine)
            {
                ReflowForLine();
            }

            for (int i = 0; i < MaxChoices; i++)
            {
                bool used = !_state.End && i < _state.Choices.Length;
                _choiceButtons[i].gameObject.SetActive(used);
                if (used)
                {
                    _choiceLabels[i].text = $"[{i + 1}] " + _state.Choices[i];
                }
            }

            _leave.gameObject.SetActive(_state.End);
            _leaveLabel.text = L("ui.dialog.leave");
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        /// <summary>Sizes the speech row to its wrapped height and shifts the reply buttons + panel bottom
        /// with it (the bandit panel's reflow pattern — authored lines vary a lot in length).</summary>
        private void ReflowForLine()
        {
            float lineH = Mathf.Max(LineBaseH, UiKit.MeasureHeight(_line.text, RowW, 18, FontStyle.Italic, richText: false) + 4f);
            float delta = lineH - LineBaseH;

            _line.rectTransform.sizeDelta = new Vector2(RowW, lineH);
            int rows = _state.End ? 1 : Mathf.Min(MaxChoices, Mathf.Max(1, _state.Choices.Length));
            for (int i = 0; i < MaxChoices; i++)
            {
                ((RectTransform)_choiceButtons[i].transform).anchoredPosition =
                    new Vector2(20f, -(ChoicesY + delta + i * (ChoiceH + ChoiceGap)));
            }

            ((RectTransform)_leave.transform).anchoredPosition = new Vector2(20f, -(ChoicesY + delta));
            ((RectTransform)_panel.transform).sizeDelta =
                new Vector2(PanelW, PanelBaseH + delta + rows * (ChoiceH + ChoiceGap));
            _laidOutLine = _line.text;
        }

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("NpcDialogUi");
            _canvas.sortingOrder = 24; // the bandit panel's tier — above dock/trade, below menus
            UiNav.Enable(_canvas.gameObject); // pad: stick walks the replies, A picks — number keys stay
            var root = _canvas.transform;

            _panel = UiKit.AddCenteredPanel(root, "DialogPanel", PanelW, PanelBaseH, 40f);

            var t = _panel.transform;
            _title = UiKit.AddText(t, 20f, 12f, RowW, 26f, string.Empty, 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _line = UiKit.AddText(t, 20f, 44f, RowW, LineBaseH, string.Empty, 18, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Italic);
            _line.horizontalOverflow = HorizontalWrapMode.Wrap;

            for (int i = 0; i < MaxChoices; i++)
            {
                int index = i;
                _choiceButtons[i] = UiKit.AddButton(t, 20f, ChoicesY + i * (ChoiceH + ChoiceGap), RowW, ChoiceH,
                    string.Empty, () => Choose(index));
                _choiceLabels[i] = _choiceButtons[i].GetComponentInChildren<Text>();
                _choiceLabels[i].alignment = TextAnchor.MiddleLeft;
            }

            _leave = UiKit.AddButton(t, 20f, ChoicesY, 200f, 40f, string.Empty, Close);
            _leaveLabel = _leave.GetComponentInChildren<Text>();

            _panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.NpcDialogReceived -= OnDialog;
            }

            // A scene-root canvas — it goes with the rig, or an open conversation stays over the main menu (#2936).
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
