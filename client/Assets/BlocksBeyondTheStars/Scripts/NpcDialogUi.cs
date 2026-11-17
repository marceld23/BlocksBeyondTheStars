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

        /// <summary>#3089: the in-game menu — the quartermaster's "Show me the jobs" opens its Missions tab.</summary>
        public GameMenu Menu;

        private const int MaxChoices = 4; // #3405: the robot's talk offers four (people offer up to three)
        private const float PanelW = 560f, PanelBaseH = 130f, RowW = 520f, LineBaseH = 48f;
        private const float ChoicesY = 104f, ChoiceH = 44f, ChoiceGap = 8f;

        /// <summary>How far the panel's centre sits above the screen's (1920×1080 units). At 70 the tallest panel (four
        /// answers under a four-line speech, ~390 high) ends near y 665 — above VEGA's speech panel (HUD y 546 ≈ 682 here),
        /// which on a tablet moves right of the move stick, under this panel's left half.</summary>
        public const float PanelRaise = 70f;

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

            // Esc and pad B (the menu verb — B did nothing here before, though the pad's hint strip names it "back").
            if (InputMap.Down(InputAction.UiCancel) || (_state.End && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.E))))
            {
                Game.MarkMenuInputHandled(); // this Esc ends the talk — it must not also open the pause menu
                Close(); // walking away is always allowed — the server ends the conversation (#2616)
            }
        }

        /// <summary>"[1] " in front of an answer — the number key that picks it — only while a keyboard is in hand: a pad
        /// picks with A and a tablet taps, and the bracketed digits read as noise there.</summary>
        public static string KeyPrefix(int number)
            => InputMap.ActiveDevice == InputDeviceKind.KeyboardMouse ? "[" + number + "] " : string.Empty;

        private void LateUpdate()
        {
            Game?.SetMenuOwner(this, _state is not null); // dialogue happens on foot — take the cursor
            if (_state is not null)
            {
                MusicDuck.Speech(); // the music makes room while the conversation is open
            }
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
            _missionsOnClose = m.Action == NpcDialogState.ActionMissions; // #3089: a later line or talk cancels it
            ClientAudio.Instance?.Cue("ui_open", 0.6f);
            // #3405: the robot is no NPC — it speaks in the panel only (no person to show a bubble over).
            if (!string.IsNullOrEmpty(m.Text) && Game?.Views.Npcs != null && !RobotTalkPanel.IsRobot(m.NpcId))
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
            else if (m.Action == NpcDialogState.ActionMissions)
            {
                StartCoroutine(MissionsAfterLine()); // #3089: the quartermaster shows the jobs
            }
        }

        /// <summary>#3089: the quartermaster's "Show me the jobs" was answered — the Missions tab opens as the panel closes,
        /// whether the player closes it or <see cref="MissionsAfterLine"/> does.</summary>
        private bool _missionsOnClose;

        /// <summary>#3089: after the quartermaster's "Here you go" the panel goes by itself and the Missions tab opens on the
        /// jobs on offer (the server lists the board the player is at); closing it sooner opens the tab at once.</summary>
        private System.Collections.IEnumerator MissionsAfterLine()
        {
            var line = _state;
            yield return new WaitForSecondsRealtime(1.4f); // long enough to read the short line
            if (ReferenceEquals(_state, line))
            {
                Close();
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

            // #3347: a sung reply is played in the Sounder voice as it is given (locally — the server owns what it does).
            string melody = DialogMelody.MelodyOf(_state.ChoiceMelodies, index);
            if (melody.Length > 0)
            {
                StartCoroutine(SingMelody(DialogMelody.Notes(melody)));
            }

            Game.Network?.SendNpcDialogChoice(index, _state.NpcId);
        }

        /// <summary>#3347: plays a sung reply note by note (instrument 4, <c>note_4_&lt;tone&gt;</c>; a rest is a short silence).</summary>
        private System.Collections.IEnumerator SingMelody(int[] notes)
        {
            foreach (int tone in notes)
            {
                string id = DialogMelody.NoteId(tone);
                if (id != null)
                {
                    ClientAudio.Instance?.PlayOwn(id, 0.8f);
                }

                yield return new WaitForSecondsRealtime(DialogMelody.NoteSeconds);
            }
        }

        /// <summary>The player closes the panel (Esc, Leave, the photo/interview hand-off). #2616: while it still offered
        /// replies the server holds the conversation (and the NPC stands still) — tell it the player left.</summary>
        private void Close()
        {
            if (_state is { End: false })
            {
                Game?.Network?.SendNpcDialogLeave(_state.NpcId);
            }

            bool missions = _missionsOnClose;
            Hide();
            if (missions)
            {
                Menu?.OpenMissionBoard(); // #3089: the jobs the quartermaster offered
            }
        }

        /// <summary>Takes the panel down without telling the server (its own close, #2616).</summary>
        private void Hide()
        {
            _state = null;
            _missionsOnClose = false;
            _panel?.SetActive(false);
        }

        private void Refresh()
        {
            _panel.SetActive(true);
            if (_title.text != _state.Name)
            {
                _title.text = _state.Name;
                UiKit.FitLabelSingleLine(_title, RowW, 14, 22); // a long settler name with its profession stays on its row
            }

            _title.color = RobotTalkPanel.TitleRgb(_state.NpcId) is int rgb ? RgbColor(rgb) : UiKit.Cyan; // #3405: the robot's light blue

            _line.text = "“" + _state.Text + "”";
            if (_line.text != _laidOutLine)
            {
                ReflowForLine();
            }

            var toneNames = DialogMelody.ToneNames(L("ui.note.names")); // #3347: a sung reply shows its notes
            for (int i = 0; i < MaxChoices; i++)
            {
                bool used = !_state.End && i < _state.Choices.Length;
                _choiceButtons[i].gameObject.SetActive(used);
                if (used)
                {
                    _choiceLabels[i].text = KeyPrefix(i + 1) + DialogMelody.ChoiceLabel(_state.Choices[i], DialogMelody.MelodyOf(_state.ChoiceMelodies, i), toneNames);
                }
            }

            _leave.gameObject.SetActive(_state.End);
            _leaveLabel.text = L("ui.dialog.leave");
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        /// <summary>0xRRGGBB → an opaque colour (#3405: the robot's name in its visor's light blue).</summary>
        private static Color RgbColor(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

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

            // #3668: no scrim and clear of the comms zone — VEGA's lesson on answering (tutorial c11.2, "Answers matter…")
            // comes WHILE this panel is open, and her speech panel (bottom left; right of the move stick on a tablet) must
            // stay readable beside it. Raised a little above the centre, so even the tallest conversation (four answers
            // under a long line) ends above her panel on a tablet too. Opaque like every text-carrying panel (#3662).
            _panel = UiKit.AddCenteredPanel(root, "DialogPanel", PanelW, PanelBaseH, PanelRaise);
            _panel.GetComponent<Image>().sprite = UiKit.DialogSprite;

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
                UiKit.FitButtonLabel(_choiceLabels[i]); // a long German answer wraps onto a second line instead of shrinking to 11 px
            }

            // Leave closes the conversation: the close icon, quiet, a full 44 px touch target (#3662).
            _leave = UiKit.AddTierButton(t, 20f, ChoicesY, 220f, ChoiceH, string.Empty, Close, ButtonTier.Quiet, "btn_close");
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
