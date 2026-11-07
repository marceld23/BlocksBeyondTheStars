// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Player chat. Sending requires a comm radio (server-enforced; the rejection shows as a toast); messages broadcast to
    /// all connected players, and server replies and local command answers land in the same history.
    /// <list type="bullet">
    /// <item><b>Closed:</b> incoming lines show in a compact holo lane at the left edge (<c>ChatUi.Lane.cs</c>, #1799) —
    /// they age out after a few seconds unless the player picked another <see cref="ChatVisibility"/>, the toggle key
    /// (default J) mutes the lane for the session, and the lane yields to VEGA's speech panel and objective chip.</item>
    /// <item><b>Open</b> (Enter, d-pad up, the touch CHAT button): a big centred, half-transparent window with the
    /// session's history (200 lines), a scrollbar, a title and hint row and the input row (<c>ChatUi.Window.cs</c>,
    /// <c>ChatUi.WindowView.cs</c>, #3264). The lane hides meanwhile and comes back without the lines read in the
    /// window.</item>
    /// </list>
    /// This file holds the history, the network events and the slash commands.
    /// </summary>
    public sealed partial class ChatUi : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>Live client settings, for the chat visibility mode. Null-safe: unassigned behaves like
        /// the default (auto-fade).</summary>
        public ClientSettings Settings;

        private readonly List<ChatLine> _lines = new();
        private Canvas _canvas;
        private bool _subscribed, _built, _hostAnnounced, _reportTipShown, _hiddenByKey, _capturing;
        private int _appended;          // lines appended this session — the lane's "already read" mark counts against it
        private const int MaxLog = 200; // #3264: the session's history (was 40); only the slice in view is ever laid out

        /// <summary>A history entry with the (unscaled) time it arrived, which is what the lane's fade reads.</summary>
        private readonly struct ChatLine
        {
            public ChatLine(string text, float time)
            {
                Text = text;
                Time = time;
            }

            public string Text { get; }

            public float Time { get; }
        }

        /// <summary>Chat visibility for THIS frame: the player's setting, unless the toggle key muted the
        /// lane for the session.</summary>
        private ChatVisibility Mode => _hiddenByKey
            ? ChatVisibility.Off
            : Settings?.ChatVisibility ?? ChatVisibility.Auto;

        private void Update()
        {
            if (Game?.Network == null)
            {
                return;
            }

            EnsureBuilt();
            Subscribe();
            AnnounceOnce();
            UpdateContextVisibility();
            UpdateWindowFrame(); // the big window: yields, refocus, scrolling, re-layout (ChatUi.Window.cs)
            UpdateLaneFrame();   // the compact lane: VEGA flips and the fade tick (ChatUi.Lane.cs)
            HandleKeys();
        }

        private void Subscribe()
        {
            if (!_subscribed)
            {
                Game.Network.ChatReceived += OnChat;
                // Server replies land in the history too (#642): a /tp target list arrives as a burst of
                // ServerMessages, and the single-line HUD toast (GameBootstrap.LastMessage) shows only
                // whichever line came last. The toast path stays — chat is the readable copy.
                Game.Network.ServerMessageReceived += OnServerMessage;
                Game.Network.ActionRejected += OnActionRejected;
                _subscribed = true;
            }

            HookHud(); // #3264: the damage cause goes into the open window's history (ChatUi.Window.cs)
        }

        /// <summary>The one-off lines of a session: the LAN address of an in-game host, and where the report paths are on
        /// an official hosted world.</summary>
        private void AnnounceOnce()
        {
            // In-game host: announce the LAN address once (chat history + toast) so the host
            // can tell friends where to join.
            if (!_hostAnnounced && !string.IsNullOrEmpty(Game.HostInfo) && Game.Localizer != null)
            {
                _hostAnnounced = true;
                string line = Game.Localizer.Get("ui.host.address_line").Replace("{addr}", Game.HostInfo);
                Append($"<color=#80E5D2>{ChatMarkup.RichSafe(line)}</color>");
                Game.ShowMessage(line);
            }

            // Official hosted world: point players at the report paths once — the report button hides
            // in the alliance tab and /report is invisible otherwise, so kids would never find them.
            if (!_reportTipShown && !string.IsNullOrEmpty(Game.HostedToken) && !string.IsNullOrEmpty(Game.PortalSession) && Game.Localizer != null)
            {
                _reportTipShown = true;
                Append($"<color=#80E5D2>{ChatMarkup.RichSafe(Game.Localizer.Get("ui.chat.report_tip"))}</color>");
            }
        }

        /// <summary>
        /// Context hiding, mirroring the HudUi rules: while piloting the ship the flight view draws its own overlay and the
        /// left lane belongs to it; EVA keeps the chat (you are still "on foot"). An open window overrides — the input must
        /// never vanish mid-sentence, however the state flips. The /bump capture guard: that path blanks the canvas itself
        /// for its end-of-frame screenshot, and this toggle must not fight it within the frame. A cutscene hides it too
        /// (#2461) — new lines keep arriving in the history and show again afterwards.
        /// </summary>
        private void UpdateContextVisibility()
        {
            bool hideForContext = ((Game.SpaceViewActive && !Game.InEva) || GameBootstrap.CutsceneActive) && !WindowOpen;
            if (_canvas != null && !_capturing && _canvas.enabled == hideForContext)
            {
                _canvas.enabled = !hideForContext;
            }
        }

        private void HandleKeys()
        {
            // Open the window with Enter — or, on a pad, with d-pad up: the chat box was the last thing on the HUD a
            // controller could not reach at all (#1211/#1220). KeypadEnter stays a raw second key rather than a binding:
            // it is the SAME key to a player, not a second one to configure. Never in the frame the window closed: the
            // Enter that sent a line must not open it again.
            if (!WindowOpen && Time.frameCount != _closedFrame
                && (InputMap.Down(InputAction.OpenChat) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                OpenInput();
            }

            // Mute/unmute the lane for this session (default J). The setting picks the default
            // behaviour; this is the in-the-moment "get it off my screen" without opening the menu.
            if (!WindowOpen && !Game.MenuOpen && InputMap.Down(InputAction.ToggleChat))
            {
                _hiddenByKey = !_hiddenByKey;
                Game.ShowMessage(HudUi.ExpandKeyTokens(Game.Localizer, L(_hiddenByKey ? "ui.chat.hidden" : "ui.chat.shown"))); // "press {key:ToggleChat} again" (#2502)
                RefreshLog();
            }
        }

        /// <summary>Appends a rendered line to the history, stamped with the moment it arrived (the lane's fade reads
        /// that), trims the buffer and re-renders whichever view is up.</summary>
        private void Append(string rendered)
        {
            _lines.Add(new ChatLine(rendered, Time.unscaledTime));
            _heights.Add(float.NaN); // measured when the window first lays it out
            _appended++;
            if (_lines.Count > MaxLog)
            {
                _lines.RemoveAt(0);
                _heights.RemoveAt(0);
            }

            OnHistoryLineAdded(); // the open window keeps a reader's place (ChatUi.Window.cs)
            RefreshLog();
        }

        private void OnDisable()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.ChatReceived -= OnChat;
                Game.Network.ServerMessageReceived -= OnServerMessage;
                Game.Network.ActionRejected -= OnActionRejected;
                _subscribed = false;
            }

            UnhookHud();
            DropWindowOnDisable();
        }

        private void OnChat(BlocksBeyondTheStars.Networking.Messages.ChatMessage m)
        {
            // A muted sender never reaches the scrollback (#1209). NPC radio calls are exempt: they are not
            // people, they carry the NPC's name, and nobody can put that on the list in the first place.
            if (!m.IsNpcCall && Settings != null && Settings.IsMuted(m.SenderId, m.Sender))
            {
                return;
            }

            // Sender and text are neutralised first — the log parses rich text, so an unescaped chat line
            // could otherwise recolour or resize everyone's scrollback.
            Append($"<b>{ChatMarkup.RichSafe(m.Sender)}:</b> {ChatMarkup.RichSafe(m.Text)}");
        }

        /// <summary>Server info lines (command replies, admin broadcasts) go into the scrollback as system
        /// lines. "@…" machine tokens are skipped — GameBootstrap localizes those into the HUD toast.</summary>
        private void OnServerMessage(BlocksBeyondTheStars.Networking.Messages.ServerMessage m)
        {
            if (!string.IsNullOrEmpty(m.Text) && m.Text[0] != '@')
            {
                LocalLine(m.Text);
            }
        }

        /// <summary>An admin-command rejection is the answer to something the player TYPED, so it must be
        /// readable where they typed it — the toast alone flashes past (#642: "/tp did nothing"). Other
        /// rejection kinds (mine/place/craft) are gameplay feedback and stay toast-only. The "@srv.*" tokens most
        /// rejections are since #822 are resolved into the player's language first — skipping them silently
        /// undid #642 ("/tp city" answered nothing, #1922).</summary>
        private void OnActionRejected(BlocksBeyondTheStars.Networking.Messages.ActionRejected m)
        {
            if (m.Action != "admin" || string.IsNullOrEmpty(m.Reason))
            {
                return;
            }

            string text = m.Reason[0] == '@' ? Game.ServerTokenText(m.Reason) : m.Reason;
            if (!string.IsNullOrEmpty(text) && text[0] != '@')
            {
                LocalLine(text);
            }
        }

        /// <summary>True for a slash command: after one the window stays open until Esc, so its answer can be read
        /// where it was asked (#3264 — replaces #1922's "the answer keeps the whole lane" trick).</summary>
        private static bool IsCommand(string text) => (text ?? string.Empty).TrimStart().StartsWith("/", System.StringComparison.Ordinal);

        /// <summary>Sends a finished chat line (also the pad keyboard and the touch/browser prompt paths).</summary>
        private void Submit(string text)
        {
            string t = (text ?? string.Empty).Trim();

            // Ordinary prose is capped at the server's 200-char chat limit anyway — trim it here so what
            // the player sends is what arrives. Share codes pass through untrimmed: the server answers
            // over-long ones with a readable hint instead of silent garbage (#1154).
            if (t.Length > 200 && !t.StartsWith("BBTS1-", System.StringComparison.Ordinal))
            {
                t = t.Substring(0, 200);
            }
            if (t.StartsWith("/bump", System.StringComparison.OrdinalIgnoreCase))
            {
                // Bug report: grab a screenshot first, then send it with the description. The capture
                // runs at the end of the frame with the chat canvas switched off, so the window stays out of the shot.
                string desc = t.Length > 5 ? t.Substring(5).Trim() : string.Empty;
                StartCoroutine(CaptureBumpAndSend(desc, t));
            }
            else if (BlocksBeyondTheStars.Client.Portal.ReportChatCommand.TryParse(t, out string reportedName, out string reportNote))
            {
                SubmitPlayerReport(reportedName, reportNote, t);
            }
            else if (TryMuteCommand(t))
            {
                // handled locally — never sent to the server (#1209)
            }
            else if (t.Length > 0 && !TryAdminCommand(t))
            {
                Game.Network.SendChat(t);
            }
        }

        /// <summary>Handles <c>/mute &lt;name&gt;</c> and <c>/unmute &lt;name&gt;</c>. Purely local: the list
        /// lives in the player's own settings and the server is never told, so nobody can tell they were
        /// muted (#1209). Returns true when the line was a mute command, so it is not relayed as chat.</summary>
        private bool TryMuteCommand(string t)
        {
            bool unmute = t.StartsWith("/unmute", System.StringComparison.OrdinalIgnoreCase);
            bool mute = !unmute && t.StartsWith("/mute", System.StringComparison.OrdinalIgnoreCase);
            if ((!mute && !unmute) || Settings == null)
            {
                return false;
            }

            string who = t.Substring(unmute ? "/unmute".Length : "/mute".Length).Trim();
            if (who.Length == 0)
            {
                LocalLine(L("ui.mute.usage"));
                return true;
            }

            bool changed = unmute ? Settings.Unmute(who) : Settings.Mute(who);
            if (changed)
            {
                Settings.Save();
            }

            LocalLine(L(unmute ? "ui.mute.unmuted" : "ui.mute.muted").Replace("{name}", who));
            return true;
        }

        /// <summary>
        /// Files a player report typed as <c>/report &lt;player&gt; [note]</c>. With a portal account it takes
        /// the portal path (the same one the alliance-tab report button uses, category "chat", with the
        /// reported player's recent chat lines quoted as evidence) and the outcome shows as a local-only chat
        /// line. Without one it hands the raw line to the SERVER, which files the report itself (#1222) — that
        /// is the arcade guest's, the LAN player's and the self-hosted world's route, and it used to be a dead
        /// end. Either way reports are reviewed by people and never auto-punish anyone.
        /// </summary>
        private async void SubmitPlayerReport(string reportedName, string note, string rawLine)
        {
            // No portal account — an arcade guest, a LAN player, anyone on a self-hosted world: send the line
            // to the SERVER, which files the report with the reported player's own recent chat as evidence
            // (#1222). This used to answer "not available here", which left exactly the people with no
            // account and no other channel with no recourse at all.
            if (string.IsNullOrEmpty(Game.HostedToken) || string.IsNullOrEmpty(Game.PortalSession))
            {
                Game.Network.SendChat(rawLine); // the server also answers the usage/unknown-name cases
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
            _capturing = true; // pause the Update context toggle so it can't re-enable the canvas mid-shot
            bool canvasWasOn = _canvas != null && _canvas.enabled;
            if (_canvas != null)
            {
                _canvas.enabled = false; // keep the chat overlay out of the shot; other HUD canvases remain
            }

            yield return new WaitForEndOfFrame();

            // Full composited frame (HUD kept), downscaled on the GPU to 1600 px and read back through GpuReadback
            // (#2390: no synchronous reads on WebGPU — the frame then lands a frame or two later).
            Texture2D shot = null;
            yield return GpuReadback.CaptureScreen(1600, TextureFormat.RGB24, t => shot = t);
            try
            {
                if (shot != null)
                {
                    jpg = ImageConversion.EncodeToJPG(shot, 70);
                }
                else
                {
                    Debug.LogWarning("Bump screenshot failed: readback returned nothing.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Bump screenshot failed: {e.Message}");
            }
            finally
            {
                if (shot != null)
                {
                    Destroy(shot);
                }
            }

            if (_canvas != null)
            {
                _canvas.enabled = canvasWasOn;
            }

            _capturing = false;

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

        /// <summary>#2993: "/pos" — the body and the canonical block coordinates the Quantum Teleporter takes.</summary>
        private string PositionLine()
        {
            if (Game.InSpace || Game.SpaceViewActive || Game.OnFootInSpace || Game.Aboard || !string.IsNullOrEmpty(Game.CurrentStationId))
            {
                return L("ui.chat.pos_none");
            }

            var p = Game.PlayerPosition;
            string body = string.IsNullOrEmpty(Game.LocationName) ? "—" : Game.ShownLocationName;
            return L("ui.chat.pos").Replace("{0}", body).Replace("{1}", CoordinateText.Format(p.x, p.y, p.z, Game.Circumference));
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

                // #2993: where am I — the body and the canonical block coordinates, for everyone, without the server.
                case "/pos":
                    LocalLine(PositionLine());
                    return true;

                case "/give":
                    if (p.Length < 2) { LocalLine(L("ui.cmd.usage_give")); return true; }
                    int count = p.Length >= 3 && int.TryParse(p[2], out var c) ? c : 1;
                    // The target is the LAST argument, so it takes the rest of the line — a name may hold
                    // spaces (#980) and only the item and the count are single tokens.
                    string who = p.Length >= 4 ? AdminChatCommand.PlayerArgument(t, 3) : null;
                    net.SendAdminCommand("give_item", stringArg: p[1], intArg: count, targetPlayer: who);
                    return true;

                case "/tp":
                    if (p.Length >= 4 && TryF(p[1], out var x) && TryF(p[2], out var y) && TryF(p[3], out var z))
                    {
                        net.SendAdminCommand("teleport_to_location", x: x, y: y, z: z);
                    }
                    else
                    {
                        // Anything that is not three numbers is a named target ("/tp village2"), and an empty
                        // "/tp" asks for the list. Both go to the server verbatim — it owns the target
                        // registry, and the client has nothing to check the word against.
                        net.SendAdminCommand("teleport_to_named", stringArg: t.Substring(p[0].Length).Trim());
                    }

                    return true;

                // Every command below whose argument is a PLAYER NAME takes the whole rest of the line, not
                // the next token: names contain spaces ("mincraft Fan"), and passing the first word alone
                // made the server answer "target player not found" (#980).
                case "/tpp":
                    if (p.Length < 2) { LocalLine(L("ui.cmd.usage_tpp")); return true; }
                    net.SendAdminCommand("teleport_to_player", targetPlayer: AdminChatCommand.PlayerArgument(t));
                    return true;

                case "/settime":
                    if (p.Length < 2) { LocalLine(L("ui.cmd.usage_settime")); return true; }
                    net.SendAdminCommand("set_time", stringArg: p[1]);
                    return true;

                case "/setweather":
                    if (p.Length < 2) { LocalLine(L("ui.cmd.usage_setweather")); return true; }
                    net.SendAdminCommand("set_weather", stringArg: p[1]);
                    return true;

                case "/giant": // #1998: summon this world's colossus or sandworm (testing)
                    if (p.Length < 2) { LocalLine(L("ui.cmd.usage_giant")); return true; }
                    net.SendAdminCommand("summon_giant", stringArg: p[1]);
                    return true;

                case "/arachnid": // #2009: summon this world's arachnid near you (testing)
                    net.SendAdminCommand("summon_arachnid");
                    return true;

                case "/biped": // #2081: summon a begging herd of this world's bipeds near you (testing)
                    net.SendAdminCommand("summon_biped");
                    return true;

                case "/hound": // #3326: summon this world's Schuppenhund near you (testing)
                    net.SendAdminCommand("summon_hound");
                    return true;

                case "/wanderer": // #2646: one of this world's wanderers comes by right now (testing)
                    net.SendAdminCommand("summon_wanderer");
                    return true;

                case "/fly": net.SendAdminCommand("fly"); return true;
                case "/god": net.SendAdminCommand("godmode"); return true;
                case "/instant": net.SendAdminCommand("instant_build"); return true;

                // Per-player mode override (#1121): "/mode <player…> <survival|creative|world>". The LAST
                // token is the mode, everything between is the player name — names contain spaces (#980).
                case "/mode":
                    if (p.Length == 2)
                    {
                        // "/mode sandbox" — one word is the whole world's mode (#1927); the server answers a word it
                        // does not know with both usages.
                        net.SendAdminCommand("set_world_mode", stringArg: p[1]);
                        return true;
                    }

                    if (p.Length < 3) { LocalLine(L("ui.cmd.usage_mode")); return true; }
                    net.SendAdminCommand("set_mode",
                        stringArg: p[p.Length - 1],
                        targetPlayer: t.Substring(p[0].Length, t.Length - p[0].Length - p[p.Length - 1].Length).Trim());
                    return true;

                // The whole world's mode (#1927): "/gamemode explorer|creative|sandbox", alone it names the current one.
                case "/gamemode":
                case "/modus":
                    net.SendAdminCommand("set_world_mode", stringArg: p.Length >= 2 ? p[1] : null);
                    return true;

                // ---- Fleet-admin observer + inspection (issues #487/#488) ----
                // Note these are NOT gated by the "admin cheats" world option server-side; the role is the gate.
                case "/players": net.SendAdminCommand("players"); return true;
                case "/basewalls": net.SendAdminCommand("basewalls"); return true; // enclosure-fill report (#1452)
                case "/creatures": net.SendAdminCommand("creatures"); return true; // creature-footing report (#1489)

                case "/builds":
                    // Optional player filter: "/builds Justus" lists only that player's structures.
                    net.SendAdminCommand("builds", stringArg: p.Length >= 2 ? AdminChatCommand.PlayerArgument(t) : null);
                    return true;

                case "/where":
                    if (p.Length < 2) { LocalLine(L("ui.cmd.usage_where")); return true; }
                    net.SendAdminCommand("where", stringArg: AdminChatCommand.PlayerArgument(t));
                    return true;

                // Not part of the name rule above: the argument here is on|off, never a player.
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
                // #2471: "/story [player]" — the world story, the player's track and the player packs (the caller's, or the
                // named player's: the whole rest of the line is the name).
                case "/story": net.SendAdminCommand("story_status", targetPlayer: AdminChatCommand.PlayerArgument(t)); return true;

                // #2471: tutorial controls — "/tutorial skip|restart [player]", "/tutorial chapter <id> [player]",
                // "/tutorial policy auto|courses|optin|off". The server owns the checks and answers with its usage line.
                case "/tutorial":
                    {
                        var (verb, tutorialTarget) = AdminChatCommand.Tutorial(t);
                        net.SendAdminCommand("tutorial", stringArg: verb,
                            targetPlayer: tutorialTarget.Length > 0 ? tutorialTarget : null);
                        return true;
                    }
                case "/advance":
                    net.SendAdminCommand("advance_story",
                        intArg: p.Length >= 2 && int.TryParse(p[1], out var steps) ? steps : 1);
                    return true;
                case "/revealfinale": net.SendAdminCommand("reveal_finale"); return true;
                case "/lore": net.SendAdminCommand("reveal_lore"); return true;
                case "/jumpdrive": net.SendAdminCommand("grant_module", stringArg: "jump_generator"); return true;
                case "/gotocore": net.SendAdminCommand("goto_core"); return true;

                // Cutscene preview (#2468): "/cutscene <pack> <id>" plays a story scene for this admin only (the server
                // checks the role and answers with the usage line when the words are missing).
                case "/cutscene":
                    net.SendAdminCommand("cutscene", stringArg: t.Substring(p[0].Length).Trim());
                    return true;

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

                        net.SendAdminCommand("kick", stringArg: AdminChatCommand.PlayerArgument(t));
                        return true;
                    }

                // Pause a player's chat for a few minutes (#1223) — the step between a word and a kick.
                // NOT "/mute": that is this player's own local mute list (#1209) and never leaves the client.
                case "/silence":
                    {
                        if (p.Length < 2)
                        {
                            LocalLine(L("ui.cmd.usage_silence"));
                            return true;
                        }

                        var (silenceName, minutes) = AdminChatCommand.NameAndMinutes(t);
                        net.SendAdminCommand("silence", stringArg: silenceName, intArg: minutes);
                        return true;
                    }

                case "/unsilence":
                    {
                        if (p.Length < 2)
                        {
                            LocalLine(L("ui.cmd.usage_unsilence"));
                            return true;
                        }

                        net.SendAdminCommand("unsilence", stringArg: AdminChatCommand.PlayerArgument(t));
                        return true;
                    }

                // ---- Paint moderation (#821): wipe a player's painted designs (or one by "#id") everywhere ----
                // ("/reportpaint" is NOT handled here on purpose: like /bump it travels as chat and the server
                // intercepts it, so reporting needs no admin role and no client plumbing. Plain "/report"
                // stays the portal player report, parsed above by ReportChatCommand.)
                case "/paintwipe":
                    {
                        if (p.Length < 2)
                        {
                            LocalLine(L("ui.cmd.usage_paintwipe"));
                            return true;
                        }

                        // "#42" (a design id) survives this untouched — only quotes and a leading '@' go.
                        net.SendAdminCommand("paintwipe", stringArg: AdminChatCommand.PlayerArgument(t));
                        return true;
                    }

                // ---- World textures (#1959): take a published texture back — one key, a player's, or all ----
                // ("/reporttexture <key>" travels as chat, like /reportpaint: any player may flag one.)
                case "/texturewipe":
                    {
                        if (p.Length < 2)
                        {
                            LocalLine(L("ui.cmd.usage_texturewipe"));
                            return true;
                        }

                        net.SendAdminCommand("texturewipe", stringArg: AdminChatCommand.PlayerArgument(t));
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
            LocalLine(L("ui.admin.help_mode"));
            LocalLine(L("ui.admin.help_gamemode"));
            LocalLine(L("ui.admin.help_teleport"));
            LocalLine(L("ui.admin.help_wilderness")); // #2646: /wanderer and /tp lodge
            LocalLine(L("ui.admin.help_inspect"));
            LocalLine(L("ui.admin.help_fleet"));
            LocalLine(L("ui.admin.help_story"));
            LocalLine(L("ui.admin.help_tutorial"));
            LocalLine(L("ui.admin.help_cutscene"));
            LocalLine(L("ui.admin.help_maintenance"));
            LocalLine(L("ui.admin.help_moderation"));
        }

        private static bool TryF(string s, out float v)
            => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

        /// <summary>Appends a local-only system line to the chat log (command help / usage errors).</summary>
        private void LocalLine(string s)
        {
            Append($"<color=#7fd4ff>{ChatMarkup.RichSafe(s)}</color>");
        }

        private void OnDestroy()
        {
            // A window fade still running would poke a destroyed CanvasGroup — drop them first.
            KillLaneTweens();
            KillWindowTweens();

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

            // Same canvas reference as the HUD (1536×864, #636): chat coordinates are then directly
            // comparable to HudUi's, the overlay follows the player's UI-scale setting instead of drifting
            // against the HUD at every scale step, and diegetic means it rides the visor pipeline with the
            // rest of the HUD rather than floating flat in front of the glass.
            _canvas = UiKit.CreateDiegeticCanvas("ChatUI", UiKit.HudRefW, UiKit.HudRefH);
            _canvas.sortingOrder = 25; // below menus (50) / map (60), above the HUD (10), VEGA (11) and the world
            BuildLane(_canvas.transform);
            BuildBigWindow(_canvas.transform);
            _built = true;
        }

        private string L(string k) => Game?.Localizer?.Get(k) ?? k;
    }
}
