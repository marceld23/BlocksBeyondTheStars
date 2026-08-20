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
                Close(); // walking away is always allowed — the server just drops the abandoned walk
            }
        }

        private void LateUpdate()
        {
            Game?.SetMenuOwner(this, _state is not null); // dialogue happens on foot — take the cursor
        }

        private void OnDialog(NpcDialogState m)
        {
            _state = m;
            ClientAudio.Instance?.Cue("ui_open", 0.6f);
        }

        private void Choose(int index)
        {
            if (_state is null || _state.End || index >= _state.Choices.Length)
            {
                return;
            }

            Game.Network?.SendNpcDialogChoice(index);
        }

        private void Close()
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

        private static readonly TextGenerator Measurer = new TextGenerator();

        /// <summary>Sizes the speech row to its wrapped height and shifts the reply buttons + panel bottom
        /// with it (the bandit panel's reflow pattern — authored lines vary a lot in length).</summary>
        private void ReflowForLine()
        {
            var settings = new TextGenerationSettings
            {
                font = UiKit.Font,
                fontSize = 18,
                fontStyle = FontStyle.Italic,
                richText = false,
                scaleFactor = 1f,
                lineSpacing = 1f,
                horizontalOverflow = HorizontalWrapMode.Wrap,
                verticalOverflow = VerticalWrapMode.Overflow,
                generationExtents = new Vector2(RowW, 0f),
                textAnchor = TextAnchor.UpperLeft,
                pivot = new Vector2(0f, 1f),
                color = Color.white,
            };
            float lineH = Mathf.Max(LineBaseH, Measurer.GetPreferredHeight(_line.text, settings) + 4f);
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

            _panel = new GameObject("DialogPanel", typeof(RectTransform)).gameObject;
            _panel.transform.SetParent(root, false);
            var rt = _panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PanelW, PanelBaseH);
            rt.anchoredPosition = new Vector2(0f, 40f);
            var img = _panel.AddComponent<Image>();
            img.sprite = UiKit.PanelSprite;
            img.type = Image.Type.Sliced;
            img.color = UiKit.PanelFill;

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
        }
    }
}
