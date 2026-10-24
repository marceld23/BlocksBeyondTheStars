// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Portal;
using BlocksBeyondTheStars.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The uGUI main menu (M27 UI rework): the sci-fi mockup look built in code via <see cref="UiKit"/>
    /// — a SYSTEM CHECK panel, the BLOCKS BEYOND THE STARS title, framed cyan menu buttons wired to the shell, a
    /// tagline and the version. Shown over the animated <see cref="MenuBackground"/>. AppShell spawns
    /// it on the MainMenu phase and destroys it on leaving. Decorative panels (world/server info,
    /// community bar) + editable host/port land in a follow-up.
    /// <para>
    /// The screen is assembled by the nested <see cref="Builder"/> — one method per visual sub-block, called by
    /// <see cref="Build"/> in draw order. That order is behaviour: <see cref="UiKit.BootScreen"/> staggers the canvas's
    /// direct children by sibling index, and draw, raycast and gamepad-focus order follow the hierarchy (pinned by the
    /// EditMode golden <c>MainMenuHierarchyEditModeTests</c>). The native Official-worlds overlay lives in
    /// <c>UiOfficialWorlds</c>.
    /// </para>
    /// </summary>
    public static class UiMainMenu
    {
        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("MainMenuUI");
            UiNav.Enable(canvas.gameObject); // let a gamepad drive the menu (inert on keyboard/mouse)

            var b = new Builder(shell, canvas.transform);
            b.BuildSystemCheck();
            b.BuildTitle();
            b.BuildNotice();
#if UNITY_WEBGL && !UNITY_EDITOR
            b.BuildBrowserMenu();
#else
            b.BuildNativeMenu();
#endif
            b.BuildWorldInfoPanel();
            b.BuildBottomBar();
            b.BuildConnectDialog();
#if !UNITY_WEBGL || UNITY_EDITOR
            b.BuildOfficialWorlds();
#endif
            b.BuildParticipateOverlay();
#if UNITY_WEBGL && !UNITY_EDITOR
            // The browser's two dialogs come last, after every other panel and overlay, so they draw on top (#1181,
            // #1322). They used to follow the browser menu directly and sat under the later overlays (#2926).
            b.BuildBrowserNewWorldDialog();
            b.BuildBrowserNamePrompt();
#endif
            return canvas.gameObject;
        }

        /// <summary>
        /// Builds the menu's sub-blocks onto one canvas and holds what they share: the dialogs a button opens are
        /// built after the button, so the button's click handler reads the dialog's field at click time (as the
        /// one-method <c>Build</c> read its captured locals). Fields are only ever ASSIGNED by the method that builds
        /// their widget — never re-declared as a local there, which would leave the field null.
        /// </summary>
        private sealed class Builder
        {
            // --- Menu buttons --- the column's x, width, button height and row pitch, and the name panel's top.
            private const float bx = UiKit.ShellColumnX, bw = 440f, bh = 54f, gap = 62f;
            private const float by = 322f;

            private readonly AppShell _shell;
            private readonly Transform _root;

            // Connect-to-server dialog (built by BuildConnectDialog; the JOIN button reveals it). Read by the button.
            // _dlgName mirrors the dialog's name input so openers can carry the menu's name field over.
            private GameObject _connect;
            private InputField _dlgName;

            // The participate / "Join in" overlay (built by BuildParticipateOverlay); the bottom-right button reveals it.
            private GameObject _participate;

            public Builder(AppShell shell, Transform root)
            {
                _shell = shell;
                _root = root;
            }

            // --- SYSTEM CHECK panel (decorative flavour) --- its left edge on the menu column's line (#2953).
            public void BuildSystemCheck()
            {
                const float sx = bx;
                UiHolo.AddPanel(_root, sx, 40f, UiKit.SystemCheckWidth, 220f, UiKit.Panel, 18f, 1.5f, 1f); // holo frame — wipes on with the screen boot (#1796)
                UiKit.AddText(_root, sx + 20f, 54f, 250f, 22f, _shell.L("ui.menu.system_check"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                string[] sysKeys = { "ui.sys.engines", "ui.sys.shields", "ui.sys.life_support", "ui.sys.comms", "ui.sys.navigation" };
                string[] sysIcons = { "sys_engines", "sys_shields", "sys_life", "sys_comms", "sys_nav" };
                for (int i = 0; i < sysKeys.Length; i++)
                {
                    float yy = 92f + i * 30f;
                    UiKit.AddIcon(_root, sx + 6f, yy, 18f, sysIcons[i]);
                    UiKit.AddText(_root, sx + 32f, yy, 178f, 22f, _shell.L(sysKeys[i]), 16, UiKit.TextCol);
                    UiKit.AddText(_root, sx + 210f, yy, 50f, 22f, _shell.L("ui.sys.ok"), 16, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);
                }
            }

            // --- Title ---
            public void BuildTitle()
            {
                UiKit.AddLogo(_root, UiKit.ShellTitleX, 70f, 1200f, 96f, "BLOCKS BEYOND THE STARS", 64);
                UiKit.AddText(_root, 1700f, 44f, 180f, 24f, "VER. " + AppShell.Version, 16, UiKit.CyanDim, TextAnchor.MiddleRight);
            }

            // --- One-shot notice (e.g. why the last join was refused) ---
            public void BuildNotice()
            {
                // A settings-recovery incident (#410) is claimed here rather than at load time: ClientSettings.Load
                // runs before the localizer exists, so it can only stash the locale KEY of the notice.
                if (string.IsNullOrEmpty(_shell.MenuNotice) && !string.IsNullOrEmpty(ClientSettings.LoadNoticeKey))
                {
                    _shell.MenuNotice = _shell.L(ClientSettings.LoadNoticeKey);
                    ClientSettings.LoadNoticeKey = "";
                }

                if (!string.IsNullOrEmpty(_shell.MenuNotice))
                {
                    UiKit.AddText(_root, 90f, 286f, 1200f, 28f, _shell.MenuNotice, 17,
                        new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
                }
            }

            // --- World / server info panel (bottom-right, decorative) ---
            public void BuildWorldInfoPanel()
            {
                // Its bottom edge (672+250=922) lines up with the menu column's last entry ("Quit" ends
                // at 434+62*7+54=922), so the two columns close on one shared baseline.
                UiHolo.AddPanel(_root, 1290f, 672f, 590f, 250f, UiKit.Panel, 18f, 1.5f, 1f);
                UiKit.AddText(_root, 1314f, 688f, 540f, 24f, _shell.L("ui.menu.world_info"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                AddInfo(_root, 728f, "info_mode", _shell.L("ui.info.mode_title"), _shell.L("ui.info.mode_desc"));
                AddInfo(_root, 792f, "info_multiplayer", _shell.L("ui.info.mp_title"), _shell.L("ui.info.mp_desc"));
                AddInfo(_root, 856f, "info_procedural", _shell.L("ui.info.proc_title"), _shell.L("ui.info.proc_desc"));
            }

            // --- Bottom bar ---
            public void BuildBottomBar()
            {
                UiKit.AddText(_root, 90f, 1030f, 500f, 26f, _shell.L("ui.menu.community"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddText(_root, 660f, 1030f, 600f, 26f, _shell.L("ui.splash.tagline"), 18, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                // "What's new?" (#543, the devblog release notes in-game — also auto-opens once after an
                // update) + "Mach mit" (opens the open-source participate panel; replaced the old "Wishlist
                // on Steam" line). The pair spans exactly the world-info panel above (1290..1880), so the
                // right column closes flush.
                UiKit.AddButton(_root, 1290f, 1018f, 285f, 48f, _shell.L("ui.menu.whatsnew"),
                    _shell.OpenWhatsNew, "btn_credits");
                UiKit.AddButton(_root, 1595f, 1018f, 285f, 48f, _shell.L("ui.menu.contribute"),
                    () => { if (_participate != null) _participate.SetActive(true); }, "btn_credits");
            }

            // --- Connect-to-server dialog (added last so it draws on top; hidden until JOIN is pressed) ---
            public void BuildConnectDialog()
            {
                string[] name = { _shell.PlayerName };
                string[] host = { _shell.ManualJoinHost };
                string[] port = { _shell.ManualJoinPort };
                string[] pass = { "" };
                var (connectOverlay, dlg) = UiKit.AddModalOverlay(_root, 660f, 280f, 600f, 520f);
                _connect = connectOverlay;
                UiKit.AddText(dlg, 30f, 24f, 540f, 30f, _shell.L("ui.menu.connect_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.AddText(dlg, 30f, 80f, 540f, 22f, _shell.L("ui.menu.connect_name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                _dlgName = UiKit.AddInput(dlg, 30f, 106f, 540f, 38f, name[0], v => name[0] = v, maxLength: Protocol.MaxPlayerNameLength);
                UiKit.AddText(dlg, 30f, 160f, 540f, 22f, _shell.L("ui.menu.connect_host"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.AddInput(dlg, 30f, 186f, 540f, 38f, host[0], v => host[0] = v);
                UiKit.AddText(dlg, 30f, 240f, 540f, 22f, _shell.L("ui.menu.connect_port"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.AddInput(dlg, 30f, 266f, 260f, 38f, port[0], v => port[0] = v);
                // Both well-known defaults next to the port field. The field itself prefills the "Host Game"
                // port (#978) because that is what this dialog is for — official worlds arrive with their own
                // host + port from the portal — and the hint still names the dedicated-server port for anyone
                // typing in a server address by hand (#960).
                UiKit.AddText(dlg, 306f, 258f, 264f, 54f,
                    _shell.L("ui.menu.connect_port_hint")
                        .Replace("{official}", AppShell.DefaultServerPort.ToString())
                        .Replace("{hosted}", LocalServerLauncher.DefaultPort.ToString()),
                    13, UiKit.CyanDim, TextAnchor.MiddleLeft);
                UiKit.AddText(dlg, 30f, 320f, 540f, 22f, _shell.L("ui.menu.connect_password"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                var passInput = UiKit.AddInput(dlg, 30f, 346f, 540f, 38f, pass[0], v => pass[0] = v);
                passInput.contentType = InputField.ContentType.Password; // mask it like the portal login field
                var dlgWarn = UiKit.AddText(dlg, 30f, 396f, 540f, 22f, "", 14,
                    new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddButton(dlg, 30f, 432f, 270f, 54f, _shell.L("ui.menu.connect"), () =>
                {
                    // A name is required (#221): joining anonymously fell back to a server-assigned
                    // "player_N" identity nobody recognizes, and shared "Pilot" names collide.
                    if (string.IsNullOrWhiteSpace(name[0]))
                    {
                        dlgWarn.text = _shell.L("ui.webgl.need_name");
                        return;
                    }

                    dlgWarn.text = "";
                    _shell.PlayerName = name[0].Trim();
                    _shell.Settings.PlayerName = _shell.PlayerName; // remember the identity across sessions
                    _shell.Settings.Save();

                    // Remember what was typed for the next time this dialog opens, then dial it. The prefill is
                    // kept apart from the live join target (#978) — the portal join overwrites the latter.
                    _shell.ManualJoinHost = string.IsNullOrWhiteSpace(host[0]) ? "127.0.0.1" : host[0].Trim();
                    _shell.ManualJoinPort = string.IsNullOrWhiteSpace(port[0]) ? _shell.ManualJoinPort : port[0].Trim();
                    _shell.Host = _shell.ManualJoinHost;
                    _shell.Port = _shell.ManualJoinPort;
                    _shell.Password = pass[0] ?? "";
                    _shell.HostedToken = ""; // manual join: no official-worlds grant
                    _shell.HostedWorldId = "";
                    _shell.ArcadeNameToken = "";
                    _shell.StartJoin();
                }, "btn_join");
                UiKit.AddButton(dlg, 310f, 432f, 260f, 54f, _shell.L("ui.menu.back"), () => _connect.SetActive(false), "btn_exit");
                _connect.SetActive(false);
            }

            // --- Participate / "Join in" overlay (added last so it draws on top; hidden until "Mach mit") ---
            public void BuildParticipateOverlay()
            {
                var pdim = UiKit.AddModalDim(_root);
                _participate = pdim.gameObject;
                var pdlg = UiKit.AddDialogPanel(_participate.transform, 560f, 250f, 800f, 580f);
                UiKit.AddText(pdlg, 40f, 26f, 720f, 36f, _shell.L("ui.contribute.title"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

                Text Para(float y, float h, string text, int size, Color col)
                {
                    var t = UiKit.AddText(pdlg, 40f, y, 720f, h, text, size, col, TextAnchor.UpperLeft);
                    t.horizontalOverflow = HorizontalWrapMode.Wrap;
                    return t;
                }

                Para(82f, 44f, _shell.L("ui.contribute.intro"), 18, UiKit.TextCol);
                // Player feedback first (for everyone, in-game) — highlighted; then play, then the GitHub paths.
                Para(138f, 70f, "1.  " + _shell.L("ui.contribute.feedback").Replace("{feedback_key}", FeedbackUi.HotkeyName), 17, UiKit.Ok);
                Para(212f, 50f, "2.  " + _shell.L("ui.contribute.play"), 17, UiKit.TextCol);
                Para(266f, 70f, "3.  " + _shell.L("ui.contribute.bugs"), 17, UiKit.TextCol);
                Para(340f, 50f, "4.  " + _shell.L("ui.contribute.dev"), 17, UiKit.TextCol);
                // Real buttons, not link-styled text (#544): the GitHub line used to be a dead AddText that
                // merely LOOKED clickable. GitHub + the game website open in the system browser; the website
                // follows the UI language (the German site is the root, English lives under /en).
                UiKit.AddButton(pdlg, 60f, 414f, 380f, 46f, _shell.L("ui.contribute.github"),
                    () => Application.OpenURL("https://github.com/marceld23/BlocksBeyondTheStars"), "btn_credits");
                UiKit.AddButton(pdlg, 460f, 414f, 280f, 46f, _shell.L("ui.contribute.website"),
                    () => Application.OpenURL(_shell.Settings.Language == "de"
                        ? "https://www.blocksbeyondthestars.com/"
                        : "https://www.blocksbeyondthestars.com/en"), "btn_credits");
                UiKit.AddButton(pdlg, 270f, 500f, 260f, 52f, _shell.L("ui.menu.back"), () => _participate.SetActive(false), "btn_exit");
                _participate.SetActive(false);
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // The browser menu's name (the menu field, mirrored by the name prompt), the field itself, and the
            // "New world?" confirmation the New-world button opens.
            private string _webName;
            private InputField _webNameInput;
            private GameObject _newWorldDlg;

            public void BuildBrowserMenu()
            {
                // Browser build: a slimmed "enter your name and play" screen. No host, editors or quit in the
                // browser (no bundled server process, and quitting a tab is meaningless) — but singleplayer
                // exists: the REAL server runs in-process (BrowserLocalServer). Which button leads depends on
                // how the page was opened: a portal deep-link preconfigures a server to join, glitch.fun offers
                // its arcade, and a bare /play page has nothing to join, so Singleplayer takes the top (#1322).
                // "Connect to a server…" stays as the manual fallback. A name is required so players never
                // join the public realm anonymously. The whole block is guarded so the native client (the
                // #else below) is byte-for-byte unchanged.
                _webName = _shell.PlayerName;
                if (string.IsNullOrWhiteSpace(_webName))
                {
                    // No name in the settings (fresh browser, settings lost) but a world on disk: its one player
                    // IS this player — the name is the save key — so offer it instead of an empty field (#1322).
                    _webName = BrowserLocalServer.PeekSavedPlayerName() ?? "";
                }
                // Accented like the native menu: the name gates every play action (#221); panel edges flush
                // with the button column, content inset — see the native branch below.
                UiHolo.AddPanel(_root, bx, by - 10f, bw, 100f, new Color(0.12f, 0.45f, 0.62f, 0.22f), 14f, 1.2f, 0.8f);
                UiKit.AddText(_root, bx + 16f, by, bw - 32f, 24f, _shell.L("ui.menu.connect_name"), 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                // Capped at the server's join limit (#1368): a longer name was truncated server-side, so the settings
                // remembered a name that was not the player's actual id.
                _webNameInput = UiKit.AddInput(_root, bx + 16f, by + 30f, bw - 32f, 46f, _webName, v => _webName = v, maxLength: Protocol.MaxPlayerNameLength);
                var webWarn = UiKit.AddText(_root, bx, by + 80f, bw, 22f, "", 14,
                    new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
                float wby = by + 112f;

                // glitch.fun context (install_id in the URL): the player NEVER picks a server here — the
                // arcade auto-join runs on page load, so the menu only appears when it failed. Play then
                // re-requests an arcade session instead of dialing the meaningless default host (which
                // built an empty, serverless world rig), and the manual server picker stays hidden.
                bool onGlitch = GlitchIntegration.ArcadeInstallId.Length > 0 && GlitchIntegration.PortalUrl.Length > 0;

                // A bare /play page (no deep-linked server, not glitch.fun) has nothing for "Play" to dial —
                // it used to aim at 127.0.0.1 and fail — so that button is not built there at all (#1322).
                bool bareMenu = !onGlitch && !GlitchIntegration.TryGetConfiguredServer(out _, out _, out _);

                // On glitch.fun the singleplayer entry leads and the arcade drops to second place, labeled
                // "Multiplayer (Arcade)": store visitors should discover the in-browser world first (first
                // live feedback said the arcade hid that singleplayer exists). The bare /play menu leads with
                // singleplayer for the same reason. Portal /play deep-links keep the join button on top —
                // there the player explicitly chose a server to join.
                float joinY = onGlitch ? wby + gap : wby;
                float spY = onGlitch || bareMenu ? wby : wby + gap;
                if (!bareMenu)
                {
                    UiKit.AddButton(_root, bx, joinY, bw, bh, _shell.L(onGlitch ? "ui.menu.arcade" : "ui.menu.play"), () =>
                    {
                        if (string.IsNullOrWhiteSpace(_webName))
                        {
                            webWarn.text = _shell.L("ui.webgl.need_name");
                            return;
                        }

                        _shell.PlayerName = _webName.Trim();
                        _shell.Settings.PlayerName = _shell.PlayerName; // remember the identity across sessions
                        _shell.Settings.Save();
                        if (onGlitch)
                        {
                            _shell.RetryArcadeJoin();
                        }
                        else
                        {
                            _shell.StartJoin();
                        }
                    }, "btn_join");
                }

                // In-browser singleplayer: the REAL authoritative server runs in-process (LoopbackTransport,
                // MemoryWorldRepository) — one persistent world per browser, synced to the Glitch cloud when
                // the build runs on glitch.fun with a logged-in account. A name is still required: it is the
                // player identity the save keys on. The glitch label is the plain "Singleplayer" so the pair
                // reads Singleplayer / Multiplayer (Arcade); elsewhere the "/ Local World" suffix stays.
                UiKit.AddButton(_root, bx, spY, bw, bh, _shell.L(onGlitch ? "ui.menu.singleplayer_glitch" : "ui.menu.singleplayer"), () =>
                {
                    if (string.IsNullOrWhiteSpace(_webName))
                    {
                        webWarn.text = _shell.L("ui.webgl.need_name");
                        return;
                    }

                    _shell.PlayerName = _webName.Trim();
                    _shell.Settings.PlayerName = _shell.PlayerName;
                    _shell.Settings.Save();
                    _shell.StartBrowserSingleplayer();
                }, "btn_singleplayer");

                // "New world…" (#1181): the one-world browser save can be thrown away and started over. The
                // confirmation dialog is built at the END of this block (so it draws above everything) and
                // captured here; the button only opens it. Same name gate as Singleplayer — the fresh world
                // keys its player on the name too.
                UiKit.AddButton(_root, bx + bw + 16f, spY, 220f, bh, _shell.L("ui.menu.new_world"), () =>
                {
                    if (string.IsNullOrWhiteSpace(_webName))
                    {
                        webWarn.text = _shell.L("ui.webgl.need_name");
                        return;
                    }

                    _shell.PlayerName = _webName.Trim();
                    _shell.Settings.PlayerName = _shell.PlayerName;
                    _shell.Settings.Save();
                    if (_newWorldDlg != null)
                    {
                        _newWorldDlg.SetActive(true);
                    }
                }, "btn_settings");

                if (onGlitch)
                {
                    // glitch.fun: a guest's world lives only in this browser's storage, and every new
                    // deployment starts that storage empty (#1177) — logging in on Glitch is the one thing
                    // that carries the world across updates and devices (Cloud Save), so say so right next
                    // to the buttons that start it (#1178). The rest of the row is free; wrap to two lines.
                    var hint = UiKit.AddText(_root, bx + bw + 252f, spY, 560f, bh, _shell.L("ui.webgl.glitch_save_hint"), 14,
                        UiKit.CyanDim, TextAnchor.MiddleLeft);
                    hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                }

                // The manual server picker only helps when /play was opened WITHOUT a deep-linked server —
                // players arriving through the portal already have host/port preconfigured, and on
                // glitch.fun there is nothing to pick at all (#221). On the bare menu it takes the row the
                // join button would have had.
                float nextY = wby + (bareMenu ? gap : gap * 2f);
                if (bareMenu)
                {
                    UiKit.AddButton(_root, bx, nextY, bw, bh, _shell.L("ui.menu.connect_manual"), () =>
                    {
                        if (_connect != null)
                        {
                            _dlgName.text = _webName; // carry the menu's name over (fires the input's onChange)
                            _connect.SetActive(true);
                        }
                    }, "btn_join");
                    nextY += gap;
                }

                // "My Worlds / Account" (#272): one click back to the worlds portal the game was served
                // from — same origin, so a self-hosted WorldHost links to its own portal, never to ours.
                // The portal's Play button deep-links back into /play, which closes the round-trip; the
                // portal page itself stays the browser home for signup/create/manage (HOSTED_WORLDS.md:
                // the WebGL menu never grows a server picker).
                UiKit.AddButton(_root, bx, nextY, bw, bh, _shell.L("ui.menu.my_worlds"), () =>
                {
                    // Prefer the baked portal origin: on glitch.fun the page origin is play.glitch.fun,
                    // where /worlds does not exist — and pointing arcade players at OUR portal is exactly
                    // the intended "create your own world with friends" funnel.
                    string portalUrl = GlitchIntegration.PortalUrl.Length > 0
                        ? GlitchIntegration.PortalUrl
                        : System.Uri.TryCreate(Application.absoluteURL, System.UriKind.Absolute, out var page)
                            && page.Scheme != System.Uri.UriSchemeFile
                            ? page.GetLeftPart(System.UriPartial.Authority)
                            : PortalClient.DefaultPortalUrl; // local file test builds → the official portal
                    Application.OpenURL(portalUrl + "/worlds");
                }, "btn_credits");
                UiKit.AddButton(_root, bx, nextY + gap, bw, bh, _shell.L("ui.menu.settings"), _shell.OpenSettings, "btn_settings");
                UiKit.AddButton(_root, bx, nextY + gap * 2f, bw, bh, _shell.L("ui.menu.credits"), () => _shell.GoTo(ShellPhase.Credits), "btn_credits");
            }

            // --- "New world?" confirmation (#1181) — added last so it draws on top; hidden until opened ---
            public void BuildBrowserNewWorldDialog()
            {
                // Final for the world saved in this browser (and the cloud copy follows on the next save), so it
                // spells that out and puts the destructive choice on the left, Cancel on the right.
                var (newWorldOverlay, newWorldPanel) = UiKit.AddModalOverlay(_root, 660f, 360f, 600f, 330f);
                _newWorldDlg = newWorldOverlay;
                UiKit.AddText(newWorldPanel, 30f, 24f, 540f, 36f, _shell.L("ui.webgl.new_world_title"), 24, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                var newWorldBody = UiKit.AddText(newWorldPanel, 30f, 78f, 540f, 140f, _shell.L("ui.webgl.new_world_body"), 16, UiKit.TextCol, TextAnchor.UpperLeft);
                newWorldBody.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddButton(newWorldPanel, 30f, 240f, 260f, 56f, _shell.L("ui.webgl.new_world_confirm"), () =>
                {
                    newWorldOverlay.SetActive(false);
                    _shell.StartNewBrowserWorld();
                }, "btn_exit");
                UiKit.AddButton(newWorldPanel, 310f, 240f, 260f, 56f, _shell.L("ui.action.cancel"), () => newWorldOverlay.SetActive(false), "btn_settings");
                newWorldOverlay.SetActive(false);
            }

            // --- "What is your name?" (#1322) — a deep-linked singleplayer start that found no name anywhere
            // (URL, settings, saved world). One field, one button, shown once; replaces the old silent
            // "Explorer" fallback that baked a placeholder identity into the browser's one world. Built
            // last so it draws above everything; the shell's flag is consumed here so a later menu rebuild
            // (settings → back) does not re-open it. While the What's-new modal is up the prompt waits (#1368):
            // the flag stays set and the shell rebuilds the menu — prompt included — once that modal closes,
            // instead of both opening in the same menu frame with the prompt buried underneath.
            public void BuildBrowserNamePrompt()
            {
                if (_shell.BrowserNamePromptPending && !_shell.WhatsNewOpen)
                {
                    _shell.BrowserNamePromptPending = false;
                    _shell.BrowserNamePromptShowing = true;
                    var (nameOverlay, namePanel) = UiKit.AddModalOverlay(_root, 660f, 340f, 600f, 360f);
                    UiKit.AddText(namePanel, 30f, 24f, 540f, 36f, _shell.L("ui.webgl.name_prompt_title"), 24, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                    var nameBody = UiKit.AddText(namePanel, 30f, 72f, 540f, 90f, _shell.L("ui.webgl.name_prompt_body"), 16, UiKit.TextCol, TextAnchor.UpperLeft);
                    nameBody.horizontalOverflow = HorizontalWrapMode.Wrap;
                    // The prompt edits the SAME name the menu field shows (#1368): every keystroke is mirrored into the
                    // field, so Cancel leaves the menu showing exactly the name Singleplayer would start with.
                    var nameInput = UiKit.AddInput(namePanel, 30f, 170f, 540f, 46f, _webName, v =>
                    {
                        _webName = v;
                        _webNameInput.SetTextWithoutNotify(v);
                    }, _shell.L("ui.menu.connect_name"), maxLength: Protocol.MaxPlayerNameLength);
                    var nameWarn = UiKit.AddText(namePanel, 30f, 222f, 540f, 22f, "", 14,
                        new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
                    UiKit.AddButton(namePanel, 30f, 270f, 260f, 56f, _shell.L("ui.webgl.name_prompt_go"), () =>
                    {
                        if (string.IsNullOrWhiteSpace(_webName))
                        {
                            nameWarn.text = _shell.L("ui.webgl.need_name");
                            return;
                        }

                        _shell.PlayerName = _webName.Trim();
                        _shell.Settings.PlayerName = _shell.PlayerName;
                        _shell.Settings.Save();
                        _shell.BrowserNamePromptShowing = false;
                        nameOverlay.SetActive(false);
                        _shell.StartBrowserSingleplayer();
                    }, "btn_singleplayer");
                    UiKit.AddButton(namePanel, 310f, 270f, 260f, 56f, _shell.L("ui.action.cancel"), () =>
                    {
                        _shell.BrowserNamePromptShowing = false;
                        nameOverlay.SetActive(false);
                    }, "btn_settings");
                    nameInput.ActivateInputField();
                }
            }
#else
            // The menu's pilot name (#221) and the warning line under it — both read by CommitName — and the name's
            // input field, which SetMenuName keeps showing the value.
            private string _natName;
            private Text _natWarn;
            private InputField _natNameInput;

            // Official-worlds overlay (native only; built by BuildOfficialWorlds). Read by its menu button.
            private GameObject _official;

            public void BuildNativeMenu()
            {
                // Pilot name on the menu itself (#221): play actions require a chosen name — the old silent
                // "Pilot" default meant nobody ever picked one and multiplayer names collided. The value is
                // persisted on use; the connect dialog's own name field stays as a per-join override.
                _natName = _shell.PlayerName;
                // The name is the gate for every play action (#221) — make the field read as step one, not
                // as a side note: an accented backdrop + bold cyan label instead of the plain grey line.
                // The panel's outer edges sit exactly on the button column (bx..bx+bw); content insets instead.
                UiHolo.AddPanel(_root, bx, by - 10f, bw, 100f, new Color(0.12f, 0.45f, 0.62f, 0.22f), 14f, 1.2f, 0.8f);
                UiKit.AddText(_root, bx + 16f, by, bw - 32f, 24f, _shell.L("ui.menu.connect_name"), 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                _natNameInput = UiKit.AddInput(_root, bx + 16f, by + 30f, bw - 32f, 46f, _natName, v => _natName = v, maxLength: Protocol.MaxPlayerNameLength); // the server's join cap (#1368)
                _natWarn = UiKit.AddText(_root, bx, by + 80f, bw, 22f, "", 14,
                    new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
                float nby = by + 112f;

                UiKit.AddButton(_root, bx, nby, bw, bh, _shell.L("ui.menu.singleplayer"),
                    () => { if (CommitName()) { _shell.StartSingleplayer(); } }, "btn_singleplayer");
                UiKit.AddButton(_root, bx, nby + gap, bw, bh, _shell.L("ui.menu.host"),
                    () => { if (CommitName()) { _shell.StartHost(); } }, "btn_join");
                UiKit.AddButton(_root, bx, nby + gap * 2f, bw, bh, _shell.L("ui.menu.join"), () =>
                {
                    if (CommitName() && _connect != null)
                    {
                        _dlgName.text = _shell.PlayerName; // carry the menu's name over (fires the input's onChange)
                        _connect.SetActive(true);
                    }
                }, "btn_join");
                UiKit.AddButton(_root, bx, nby + gap * 3f, bw, bh, _shell.L("ui.menu.official"), () =>
                {
                    if (CommitName() && _official != null)
                    {
                        _official.SetActive(true);
                    }
                }, "btn_join");
                UiKit.AddButton(_root, bx, nby + gap * 4f, bw, bh, _shell.L("ui.menu.editors"), () => _shell.GoTo(ShellPhase.Editors), "btn_singleplayer");
                UiKit.AddButton(_root, bx, nby + gap * 5f, bw, bh, _shell.L("ui.menu.settings"), _shell.OpenSettings, "btn_settings");
                UiKit.AddButton(_root, bx, nby + gap * 6f, bw, bh, _shell.L("ui.menu.credits"), () => _shell.GoTo(ShellPhase.Credits), "btn_credits");
                UiKit.AddButton(_root, bx, nby + gap * 7f, bw, bh, _shell.L("ui.menu.quit"), _shell.Quit, "btn_exit");
            }

            // True when a name is present (warns + blocks otherwise); commits it to the shell + settings.
            private bool CommitName()
            {
                if (string.IsNullOrWhiteSpace(_natName))
                {
                    _natWarn.text = _shell.L("ui.webgl.need_name");
                    return false;
                }

                _natWarn.text = "";
                _shell.PlayerName = _natName.Trim();
                _shell.Settings.PlayerName = _shell.PlayerName; // remember the identity across sessions
                _shell.Settings.Save();
                _shell.HostedToken = ""; // never let an official-worlds grant leak into SP/LAN/manual joins
                _shell.HostedWorldId = "";
                _shell.ArcadeNameToken = "";
                return true;
            }

            // Writes a replacement name into the menu: the value CommitName reads AND the field that shows it — the
            // field used to keep showing the refused name (#2926). Without notify: the value is set right here.
            private void SetMenuName(string name)
            {
                _natName = name;
                _natNameInput.SetTextWithoutNotify(name);
            }

            // --- Official-worlds overlay (native only; HOSTED_WORLDS.md: the browser NEVER picks servers) ---
            // Its name gate is CommitName, and a replacement name from its "name reserved" retry goes through SetMenuName.
            public void BuildOfficialWorlds()
            {
                _official = new UiOfficialWorlds(_shell, _root, CommitName, SetMenuName).Overlay;
            }
#endif
        }

        private static void AddInfo(Transform root, float y, string icon, string title, string desc)
        {
            UiKit.AddIcon(root, 1314f, y + 4f, 32f, icon);
            UiKit.AddText(root, 1356f, y, 500f, 22f, title, 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(root, 1356f, y + 24f, 500f, 22f, desc, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
        }
    }
}
