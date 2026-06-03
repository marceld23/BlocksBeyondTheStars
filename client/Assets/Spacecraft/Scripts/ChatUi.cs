using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Spacecraft.Client
{
    /// <summary>
    /// Player chat overlay (bottom-left), built in the modern uGUI design: a scrollback of recent lines
    /// plus an input box opened with <b>Enter</b> (Esc cancels). Sending requires a comm radio
    /// (server-enforced; the rejection shows as a toast). Messages broadcast to all connected players.
    /// </summary>
    public sealed class ChatUi : MonoBehaviour
    {
        public GameBootstrap Game;

        private readonly List<string> _lines = new();
        private Canvas _canvas;
        private Text _log;
        private RectTransform _inputRow;
        private InputField _input;
        private bool _typing, _subscribed, _built;
        private int _openFrame = -1;
        private const int MaxLog = 40;

        private void Update()
        {
            if (Game?.Network == null)
            {
                return;
            }

            EnsureBuilt();

            if (!_subscribed)
            {
                Game.Network.ChatReceived += OnChat;
                _subscribed = true;
            }

            // Open the input with Enter (when no other panel is open).
            if (!_typing && !Game.MenuOpen && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                _typing = true;
                _openFrame = Time.frameCount;
                Game.ChatTyping = true;
                _inputRow.gameObject.SetActive(true);
                _input.text = string.Empty;
                _input.ActivateInputField();
                RefreshLog();
            }
        }

        private void OnDisable()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.ChatReceived -= OnChat;
            }

            if (Game != null && _typing)
            {
                Game.ChatTyping = false;
            }
        }

        private void OnChat(Spacecraft.Networking.Messages.ChatMessage m)
        {
            _lines.Add($"<b>{m.Sender}:</b> {m.Text}");
            if (_lines.Count > MaxLog)
            {
                _lines.RemoveAt(0);
            }

            RefreshLog();
        }

        private void OnEndEdit(string text)
        {
            if (Time.frameCount == _openFrame)
            {
                return; // ignore the Enter that opened the box
            }

            // Enter submits; clicking away / Esc cancels.
            bool enter = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);
            if (enter)
            {
                string t = text.Trim();
                if (t.Length > 0)
                {
                    Game.Network.SendChat(t);
                }
            }

            _typing = false;
            Game.ChatTyping = false;
            _input.text = string.Empty;
            _inputRow.gameObject.SetActive(false);
            RefreshLog();
        }

        private void RefreshLog()
        {
            if (_log == null)
            {
                return;
            }

            int from = Mathf.Max(0, _lines.Count - 10);
            var sb = new System.Text.StringBuilder();
            for (int i = from; i < _lines.Count; i++)
            {
                sb.AppendLine(_lines[i]);
            }

            _log.text = sb.ToString();
            _log.color = new Color(0.86f, 0.93f, 1f, _typing ? 1f : 0.8f);
        }

        private void OnDestroy()
        {
            // Top-level canvas — destroy it with the component so chat doesn't linger on the menu.
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("ChatUI");
            _canvas.sortingOrder = 25; // below menus (50) / map (60), above the world
            var root = _canvas.transform;

            // Scrollback (bottom-left, growing upward).
            _log = UiKit.AddText(root, 16, 1080 - 320, 620, 250, string.Empty, 18, new Color(0.86f, 0.93f, 1f, 0.8f), TextAnchor.LowerLeft);
            _log.horizontalOverflow = HorizontalWrapMode.Wrap;
            _log.verticalOverflow = VerticalWrapMode.Overflow;
            _log.supportRichText = true;

            // Input row (hidden until typing).
            _inputRow = UiKit.AddPanel(root, 16, 1080 - 60, 620, 44, UiKit.Panel).rectTransform;
            var inputGo = new GameObject("ChatInput", typeof(RectTransform));
            inputGo.transform.SetParent(_inputRow, false);
            UiKit.Place(inputGo, 10, 6, 600, 32);
            var img = inputGo.AddComponent<Image>();
            img.color = new Color(0.04f, 0.09f, 0.18f, 0.95f);
            _input = inputGo.AddComponent<InputField>();
            var txt = UiKit.AddText(inputGo.transform, 8, 0, 584, 32, string.Empty, 20, UiKit.TextCol, TextAnchor.MiddleLeft);
            txt.supportRichText = false;
            var ph = UiKit.AddText(inputGo.transform, 8, 0, 584, 32, L("ui.chat.send_hint"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Italic);
            _input.textComponent = txt;
            _input.placeholder = ph;
            _input.characterLimit = 200;
            _input.lineType = InputField.LineType.SingleLine;
            _input.onEndEdit.AddListener(OnEndEdit);
            _inputRow.gameObject.SetActive(false);

            _built = true;
        }

        private string L(string k) => Game?.Localizer?.Get(k) ?? k;
    }
}
