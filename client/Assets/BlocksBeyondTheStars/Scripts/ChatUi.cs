// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
        private bool _typing, _subscribed, _built, _hostAnnounced, _reportTipShown;
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
                _lines.Add($"<color=#80E5D2>{ChatMarkup.RichSafe(line)}</color>");
                RefreshLog();
                Game.ShowMessage(line);
            }

            // Official hosted world: point players at the report paths once — the report button hides
            // in the alliance tab and /report is invisible otherwise, so kids would never find them.
            if (!_reportTipShown && !string.IsNullOrEmpty(Game.HostedToken) && !string.IsNullOrEmpty(Game.PortalSession) && Game.Localizer != null)
            {
                _reportTipShown = true;
                _lines.Add($"<color=#80E5D2>{ChatMarkup.RichSafe(Game.Localizer.Get("ui.chat.report_tip"))}</color>");
                RefreshLog();
            }

            // Open the input with Enter (when no other panel is open).
            if (!_typing && !Game.MenuOpen && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                OpenInput();
            }
        }

        /// <summary>Opens the chat input box (the same path as pressing Enter). Public so the touch layer's
        /// CHAT button can open it — tablets have no Enter key. On WebGL-touch (no soft keyboard in the
        /// browser) it skips the dead InputField and goes straight through the browser prompt.</summary>
        public void OpenInput()
        {
            if (_typing || Game == null || Game.MenuOpen)
            {
                return;
            }

            if (TouchTextEntry.NeedsPrompt)
            {
                string label = Game.Localizer != null ? Game.Localizer.Get("ui.chat.hint") : "Chat";
                Submit(TouchTextEntry.Prompt(label, string.Empty));
                return;
            }

            _typing = true;
            _openFrame = Time.frameCount;
            Game.ChatTyping = true;
            _inputRow.gameObject.SetActive(true);
            _input.text = string.Empty;
            _input.ActivateInputField();
            RefreshLog();
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
            // Sender and text are neutralised first — the log parses rich text, so an unescaped chat line
            // could otherwise recolour or resize everyone's scrollback.
            _lines.Add($"<b>{ChatMarkup.RichSafe(m.Sender)}:</b> {ChatMarkup.RichSafe(m.Text)}");
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
                Submit(text);
            }

            _typing = false;
            Game.ChatTyping = false;
            _input.text = string.Empty;
            _inputRow.gameObject.SetActive(false);
            RefreshLog();
        }

        /// <summary>Sends a finished chat line (also the touch/browser prompt path, which has no Enter key).</summary>
        private void Submit(string text)
        {
            string t = (text ?? string.Empty).Trim();
            if (t.StartsWith("/bump", System.StringComparison.OrdinalIgnoreCase))
            {
                // Bug report: grab a screenshot first, then send it with the description. The capture
                // runs next frame (after the chat box closes), so the input box stays out of the shot.
                string desc = t.Length > 5 ? t.Substring(5).Trim() : string.Empty;
                StartCoroutine(CaptureBumpAndSend(desc, t));
            }
            else if (BlocksBeyondTheStars.Client.Portal.ReportChatCommand.TryParse(t, out string reportedName, out string reportNote))
            {
                SubmitPlayerReport(reportedName, reportNote);
            }
            else if (t.Length > 0 && !TryAdminCommand(t))
            {
                Game.Network.SendChat(t);
            }
        }

        /// <summary>
        /// Files a player report typed as <c>/report &lt;player&gt; [note]</c> — official hosted worlds only
        /// (same portal path as the alliance-tab report button, but with category "chat" and the reported
        /// player's recent chat lines quoted as evidence). The outcome shows as a local-only chat line;
        /// reports are reviewed by the operators and never auto-punish anyone.
        /// </summary>
        private async void SubmitPlayerReport(string reportedName, string note)
        {
            if (string.IsNullOrEmpty(Game.HostedToken) || string.IsNullOrEmpty(Game.PortalSession))
            {
                LocalLine(L("ui.chat.report_unavailable"));
                return;
            }

            if (string.IsNullOrEmpty(reportedName))
            {
                LocalLine(L("ui.chat.report_usage"));
                return;
            }

            // Snapshot the shared chat feed on the main thread; composing quotes the reported player.
            var feed = Game.RecentChat;
            var recent = new List<(string Sender, string Text)>(feed?.Count ?? 0);
            if (feed != null)
            {
                foreach (var m in feed)
                {
                    recent.Add((m.Sender, m.Text));
                }
            }

            string message = BlocksBeyondTheStars.Client.Portal.ReportChatCommand.ComposeMessage(note, recent, reportedName);
            var portal = new BlocksBeyondTheStars.Client.Portal.PortalClient(Game.PortalUrl);
            string session = Game.PortalSession;
            string worldId = Game.HostedWorldId;
            var result = await System.Threading.Tasks.Task.Run(() => portal.Report(session, reportedName, "chat", message, worldId));
            if (_log == null)
            {
                return; // chat UI was torn down while the request ran
            }

            LocalLine(result.Ok
                ? L("ui.chat.report_sent").Replace("{name}", reportedName)
                : L("ui.chat.report_failed"));
        }

        /// <summary>Captures an in-game screenshot (downscaled JPG) at the end of the frame — with the chat
        /// overlay hidden, HUD kept — and sends it to the server as a <c>/bump</c> bug report. Falls back to a
        /// plain text <c>/bump</c> (JSON-only snapshot) when the capture fails.</summary>
        private System.Collections.IEnumerator CaptureBumpAndSend(string description, string rawCommand)
        {
            byte[] jpg = null;
            bool canvasWasOn = _canvas != null && _canvas.enabled;
            if (_canvas != null)
            {
                _canvas.enabled = false; // keep the chat overlay out of the shot; other HUD canvases remain
            }

            yield return new WaitForEndOfFrame();

            try
            {
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    jpg = EncodeDownscaledJpg(shot, 1600, 70);
                }
                finally
                {
                    Destroy(shot);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Bump screenshot failed: {e.Message}");
            }

            if (_canvas != null)
            {
                _canvas.enabled = canvasWasOn;
            }

            if (Game?.Network == null)
            {
                yield break;
            }

            if (jpg != null && jpg.Length > 0)
            {
                Game.Network.SendBumpReport(description, jpg, AppShell.Version);
            }
            else
            {
                Game.Network.SendChat(rawCommand); // fallback: server still writes the snapshot, just no image
            }
        }

        /// <summary>JPG-encodes a screenshot, downscaled so its longest side is at most <paramref name="maxDim"/>
        /// (keeping packet/disk size modest). Returns the encoded bytes.</summary>
        private static byte[] EncodeDownscaledJpg(Texture2D src, int maxDim, int quality)
        {
            int w = src.width, h = src.height;
            float scale = Mathf.Min(1f, (float)maxDim / Mathf.Max(w, h));
            int tw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            int th = Mathf.Max(1, Mathf.RoundToInt(h * scale));

            if (tw == w && th == h)
            {
                return ImageConversion.EncodeToJPG(src, quality);
            }

            var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var small = new Texture2D(tw, th, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
            small.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            byte[] jpg = ImageConversion.EncodeToJPG(small, quality);
            UnityEngine.Object.Destroy(small);
            return jpg;
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
                // Plain "/help" is the player help — two short lines. The admin block is a wall of ~30
                // commands a normal player may not even run, so it hides behind "/help admin" (the old
                // "/admin" spelling still lands there) instead of flooding everyone's scrollback.
                case "/help":
                    if (p.Length >= 2 && p[1].Equals("admin", System.StringComparison.OrdinalIgnoreCase))
                    {
                        AdminHelp();
                        return true;
                    }

                    LocalLine(L("ui.chat.help_player"));
                    LocalLine(L("ui.chat.help_admin_hint"));
                    return true;

                case "/admin":
                    AdminHelp();
                    return true;

                case "/give":
                    if (p.Length < 2) { LocalLine(L("ui.cmd.usage_give")); return true; }
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
                        LocalLine(L("ui.cmd.usage_tp"));
                    }

                    return true;

                case "/tpp":
                    if (p.Length < 2) { LocalLine(L("ui.cmd.usage_tpp")); return true; }
                    net.SendAdminCommand("teleport_to_player", targetPlayer: p[1]);
                    return true;

                case "/settime":
                    if (p.Length < 2) { LocalLine(L("ui.cmd.usage_settime")); return true; }
                    net.SendAdminCommand("set_time", stringArg: p[1]);
                    return true;

                case "/setweather":
                    if (p.Length < 2) { LocalLine(L("ui.cmd.usage_setweather")); return true; }
                    net.SendAdminCommand("set_weather", stringArg: p[1]);
                    return true;

                case "/fly": net.SendAdminCommand("fly"); return true;
                case "/god": net.SendAdminCommand("godmode"); return true;
                case "/instant": net.SendAdminCommand("instant_build"); return true;

                // ---- Fleet-admin observer + inspection (issues #487/#488) ----
                // Note these are NOT gated by the "admin cheats" world option server-side; the role is the gate.
                case "/players": net.SendAdminCommand("players"); return true;

                case "/builds":
                    // Optional player filter: "/builds Justus" lists only that player's structures.
                    net.SendAdminCommand("builds", stringArg: p.Length >= 2 ? p[1].TrimStart('@') : null);
                    return true;

                case "/where":
                    if (p.Length < 2) { LocalLine(L("ui.cmd.usage_where")); return true; }
                    net.SendAdminCommand("where", stringArg: p[1].TrimStart('@'));
                    return true;

                case "/spectate":
                case "/observe":
                    net.SendAdminCommand("spectate", stringArg: p.Length >= 2 ? p[1] : null);
                    return true;

                case "/goto":
                    if (p.Length < 2)
                    {
                        LocalLine(L("ui.cmd.usage_goto"));
                        return true;
                    }

                    // Everything after the verb goes to the server verbatim — it owns resolving names, build
                    // kinds and coordinates, and the client has no registry to check them against anyway.
                    net.SendAdminCommand("goto", stringArg: t.Substring(p[0].Length).Trim());
                    return true;

                // ---- Story finale QA ----
                case "/story": net.SendAdminCommand("story_status"); return true;
                case "/advance":
                    net.SendAdminCommand("advance_story",
                        intArg: p.Length >= 2 && int.TryParse(p[1], out var steps) ? steps : 1);
                    return true;
                case "/revealfinale": net.SendAdminCommand("reveal_finale"); return true;
                case "/lore": net.SendAdminCommand("reveal_lore"); return true;
                case "/jumpdrive": net.SendAdminCommand("grant_module", stringArg: "jump_generator"); return true;
                case "/gotocore": net.SendAdminCommand("goto_core"); return true;

                case "/ai":
                case "/ai_mission":
                    net.SendAdminCommand("ai_mission", stringArg: t.Substring(p[0].Length).Trim());
                    return true;

                // ---- Maintenance announcements (#249; allowed even when cheats are off) ----
                case "/announce":
                    {
                        string msg = t.Substring(p[0].Length).Trim();
                        if (msg.Length == 0) { LocalLine(L("ui.cmd.usage_announce")); return true; }
                        net.SendAdminCommand("announce", stringArg: msg);
                        return true;
                    }

                case "/restart":
                case "/schedule_restart":
                    {
                        if (p.Length < 2 || !int.TryParse(p[1], out var minutes))
                        {
                            LocalLine(L("ui.cmd.usage_restart"));
                            return true;
                        }

                        string msg = t.Substring(p[0].Length).Trim();
                        msg = msg.Substring(p[1].Length).Trim(); // drop the minutes token, keep the rest
                        net.SendAdminCommand("schedule_restart", stringArg: msg, intArg: minutes);
                        return true;
                    }

                case "/cancelrestart":
                case "/cancel_restart":
                    net.SendAdminCommand("cancel_restart");
                    return true;

                // ---- Moderation (#497): momentary — a lasting block is the owner's ban list on the portal ----
                case "/kick":
                    {
                        if (p.Length < 2)
                        {
                            LocalLine(L("ui.cmd.usage_kick"));
                            return true;
                        }

                        net.SendAdminCommand("kick", stringArg: p[1].TrimStart('@'));
                        return true;
                    }

                default:
                    return false; // not an admin command (e.g. /bump) → send as normal chat
            }
        }

        /// <summary>Prints the admin command reference, one short line per group — reachable via
        /// <c>/help admin</c> or <c>/admin</c>. The server still decides what actually runs: most of these
        /// need the admin role (or the world's "admin cheats" option), so a curious player gets nothing but
        /// a rejection toast out of them.</summary>
        private void AdminHelp()
        {
            LocalLine(L("ui.admin.help_cheats"));
            LocalLine(L("ui.admin.help_inspect"));
            LocalLine(L("ui.admin.help_fleet"));
            LocalLine(L("ui.admin.help_story"));
            LocalLine(L("ui.admin.help_maintenance"));
        }

        private static bool TryF(string s, out float v)
            => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

        /// <summary>Appends a local-only system line to the chat log (command help / usage errors).</summary>
        private void LocalLine(string s)
        {
            _lines.Add($"<color=#7fd4ff>{ChatMarkup.RichSafe(s)}</color>");
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
