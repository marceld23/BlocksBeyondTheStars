using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
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
        private bool _typing, _subscribed, _built, _hostAnnounced;
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

            // In-game host: announce the LAN address once (chat scrollback + toast) so the host
            // can tell friends where to join.
            if (!_hostAnnounced && !string.IsNullOrEmpty(Game.HostInfo) && Game.Localizer != null)
            {
                _hostAnnounced = true;
                string line = Game.Localizer.Get("ui.host.address_line").Replace("{addr}", Game.HostInfo);
                _lines.Add($"<color=#80E5D2>{line}</color>");
                RefreshLog();
                Game.ShowMessage(line);
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

        private void OnChat(BlocksBeyondTheStars.Networking.Messages.ChatMessage m)
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
                if (t.Length > 0 && !TryAdminCommand(t))
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

        /// <summary>
        /// Parses an admin/cheat slash-command typed in chat and sends it as an <c>AdminCommandIntent</c>
        /// (the server validates the player is an admin and that cheats are allowed, then replies with a
        /// toast). Returns true if the text was a command (so it isn't also broadcast as chat). <c>/bump</c>
        /// is intentionally NOT handled here — it stays a chat message the server intercepts.
        /// </summary>
        private bool TryAdminCommand(string t)
        {
            if (t.Length == 0 || t[0] != '/')
            {
                return false;
            }

            var p = t.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            var net = Game.Network;
            switch (p[0].ToLowerInvariant())
            {
                case "/help":
                case "/admin":
                    LocalLine(L("ui.admin.help"));
                    return true;

                case "/give":
                    if (p.Length < 2) { LocalLine("usage: /give <item> [count] [player]"); return true; }
                    int count = p.Length >= 3 && int.TryParse(p[2], out var c) ? c : 1;
                    string who = p.Length >= 4 ? p[3].TrimStart('@') : null;
                    net.SendAdminCommand("give_item", stringArg: p[1], intArg: count, targetPlayer: who);
                    return true;

                case "/tp":
                    if (p.Length >= 4 && TryF(p[1], out var x) && TryF(p[2], out var y) && TryF(p[3], out var z))
                    {
                        net.SendAdminCommand("teleport_to_location", x: x, y: y, z: z);
                    }
                    else
                    {
                        LocalLine("usage: /tp <x> <y> <z>");
                    }

                    return true;

                case "/tpp":
                    if (p.Length < 2) { LocalLine("usage: /tpp <player>"); return true; }
                    net.SendAdminCommand("teleport_to_player", targetPlayer: p[1]);
                    return true;

                case "/settime":
                    if (p.Length < 2) { LocalLine("usage: /settime <day|night|dawn|dusk>"); return true; }
                    net.SendAdminCommand("set_time", stringArg: p[1]);
                    return true;

                case "/setweather":
                    if (p.Length < 2) { LocalLine("usage: /setweather <clear|cloudy|storm>"); return true; }
                    net.SendAdminCommand("set_weather", stringArg: p[1]);
                    return true;

                case "/fly": net.SendAdminCommand("fly"); return true;
                case "/god": net.SendAdminCommand("godmode"); return true;
                case "/instant": net.SendAdminCommand("instant_build"); return true;

                case "/ai":
                case "/ai_mission":
                    net.SendAdminCommand("ai_mission", stringArg: t.Substring(p[0].Length).Trim());
                    return true;

                default:
                    return false; // not an admin command (e.g. /bump) → send as normal chat
            }
        }

        private static bool TryF(string s, out float v)
            => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

        /// <summary>Appends a local-only system line to the chat log (command help / usage errors).</summary>
        private void LocalLine(string s)
        {
            _lines.Add($"<color=#7fd4ff>{s}</color>");
            if (_lines.Count > MaxLog)
            {
                _lines.RemoveAt(0);
            }

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
