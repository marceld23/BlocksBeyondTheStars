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
    /// The uGUI main menu, on the menu template (#3663): the title top left over the button column, ONE filled primary
    /// action on top — Continue with the last world when there is one, else Singleplayer — then Singleplayer, Multiplayer
    /// (one entry for official worlds, joining and hosting) and Editors, with Settings / Credits / Quit as a smaller row.
    /// The player name is a profile chip top right that every play mode uses. The right column carries real content —
    /// the newest "What's new?" release and the festival card — where a decorative SYSTEM CHECK and a static
    /// "world / server info" panel (which always claimed a "Survival" mode the game does not offer) used to be.
    /// The community row at the bottom — Feedback (#3715), Join in, Website — sits on the grid of the small row above it.
    /// Shown over the animated <see cref="MenuBackground"/>; AppShell spawns it on the MainMenu phase.
    /// <para>
    /// The screen is assembled by the nested <see cref="Builder"/> — one method per visual sub-block, called by
    /// <see cref="Build"/> in draw order. That order is behaviour: <see cref="UiKit.BootScreen"/> staggers the canvas's
    /// direct children by sibling index, and draw, raycast and gamepad-focus order follow the hierarchy (pinned by the
    /// EditMode golden <c>MainMenuHierarchyEditModeTests</c>). Official worlds and joining a server live on their own
    /// screen now (<c>UiMultiplayer</c>, #3665).
    /// </para>
    /// </summary>
    public static partial class UiMainMenu
    {
        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("MainMenuUI");
            UiNav.Enable(canvas.gameObject); // let a gamepad drive the menu (inert on keyboard/mouse)

            var b = new Builder(shell, canvas.transform);
            b.BuildTitle();
            b.BuildNotice();
#if UNITY_WEBGL && !UNITY_EDITOR
            b.BuildBrowserMenu();
#else
            b.BuildProfileChip();
            b.BuildNativeMenu();
#endif
            b.BuildWhatsNewCard();
            UiFestivalCard.Build(shell, canvas.transform); // #3003: today's or the next festival (nothing without content or a festival near)
            b.BuildBottomBar();
            // #3021: the card's "More festivals" opens "My festivals" at the END of the canvas when pressed, so neither the
            // card's button nor the bottom bar draws over the open list or stays clickable through its scrim (#3017).

#if UNITY_WEBGL && !UNITY_EDITOR
            b.BuildConnectDialog(); // the browser keeps its manual connect dialog; the desktop joins from UiMultiplayer
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
            // --- Menu buttons --- the column's x, width, button height and row pitch, and the name panel's top (browser menu).
            private const float bx = UiKit.ShellColumnX, bw = UiKit.ShellColumnW, bh = 54f, gap = 62f;
            private const float by = 322f;

            // --- The native column (#3663): left edge, width, the big Continue row and the entries under it.
            private const float ColX = UiKit.MenuLeft, ColW = 560f, ContinueH = 108f, EntryH = 78f, EntryPitch = 90f;

            // --- The column's two rows of three small buttons — Settings / Credits / Quit and the community row at the
            // bottom — share one grid, so their edges line up (#3716).
            private const float SideGap = 12f, SideW = (ColW - 2f * SideGap) / 3f;

            // --- The right column: the What's-new card on top, the festival card (UiFestivalCard) under it.
            private const float CardX = 1300f, CardW = 530f;

            private readonly AppShell _shell;
            private readonly Transform _root;

#if UNITY_WEBGL && !UNITY_EDITOR
            // Connect-to-server dialog (browser only; built by BuildConnectDialog). Read by the button that reveals it.
            // _dlgName mirrors the dialog's name input so openers can carry the menu's name field over.
            private GameObject _connect;
            private InputField _dlgName;
#endif

            // The participate / "Join in" overlay (built by BuildParticipateOverlay); the bottom-right button reveals it.
            private GameObject _participate;

            public Builder(AppShell shell, Transform root)
            {
                _shell = shell;
                _root = root;
            }

            // --- Title (top left, over the button column — one axis for the whole screen, #3663) + the tagline under it. The
            // title is the game's official logo (rocket icon + wordmark, #3706), the same everywhere; the 3D motif fills the free middle (#3705). ---
            public void BuildTitle()
            {
                UiKit.AddGameLogo(_root, ColX, 22f, ColW, 140f);
                UiKit.AddText(_root, ColX + 4f, 164f, 1100f, 30f, _shell.L("ui.splash.tagline"), 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
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
                    UiKit.AddText(_root, ColX, 198f, 1180f, 28f, _shell.MenuNotice, 17,
                        new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
                }
            }

            // --- "What's new?" card (top of the right column, #3663) — the newest release's title and a teaser ---
            public void BuildWhatsNewCard()
            {
                const float y = UiKit.MenuContentTop, h = 300f;
                var card = UiKit.AddCard(_root, CardX, y, CardW, h);
                var kicker = UiKit.AddKicker(card, 24f, 18f, CardW - 48f, _shell.L("ui.menu.whatsnew"));
                var title = UiKit.AddText(card, 24f, 50f, CardW - 48f, 64f, "", 23, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
                title.horizontalOverflow = HorizontalWrapMode.Wrap;
                var teaser = UiKit.AddText(card, 24f, 120f, CardW - 48f, 112f, _shell.L("ui.portal.working"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                teaser.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddLinkButton(card, 24f, 246f, 300f, 36f, _shell.L("ui.menu.whatsnew_more"), _shell.OpenWhatsNew);
                card.gameObject.AddComponent<WhatsNewTeaser>().Init(_shell, kicker, title, teaser, CardW - 48f);
            }

            // --- Bottom bar: the community links as real buttons (#3663), on the grid of the Settings / Credits / Quit row
            // so the row ends where the column ends (#3716) — the label sits on its own line above the buttons. Feedback
            // comes first (#3715): the menu's visible way into the form the feedback hotkey opens. ---
            public void BuildBottomBar()
            {
                const float y = 1000f, h = 50f;
                UiKit.AddKicker(_root, ColX, y - 32f, ColW, _shell.L("ui.menu.community"));
                UiKit.AddTierButton(_root, ColX, y, SideW, h, _shell.L("ui.menu.feedback"), FeedbackUi.OpenFromMenu, ButtonTier.Quiet, "btn_feedback");
                var contribute = UiKit.AddTierButton(_root, ColX + SideW + SideGap, y, SideW, h, _shell.L("ui.menu.contribute"),
                    () => { if (_participate != null) _participate.SetActive(true); }, ButtonTier.Quiet, "btn_multiplayer");
                if (PlaytestHub.Instance != null && PlaytestHub.Instance.Tracker.NewCount > 0)
                {
                    UiKit.AddBadge(contribute, SideW); // a new playtest task waits behind "Join in"
                }
                UiKit.AddTierButton(_root, ColX + 2f * (SideW + SideGap), y, SideW, h, _shell.L("ui.menu.website"), OpenWebsite, ButtonTier.Quiet, "btn_singleplayer");
                UiKit.AddText(_root, CardX, y, CardW, h, "VER. " + AppShell.Version, 15, UiKit.CyanDim, TextAnchor.MiddleRight);
            }

            // The game website follows the UI language (the German site is the root, English lives under /en).
            private void OpenWebsite()
                => Application.OpenURL(_shell.Settings.Language == "de"
                    ? "https://www.blocksbeyondthestars.com/"
                    : "https://www.blocksbeyondthestars.com/en");

#if UNITY_WEBGL && !UNITY_EDITOR
            // --- Connect-to-server dialog (browser only; added last so it draws on top; hidden until opened) ---
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
                UiKit.AddTierButton(dlg, 310f, 432f, 260f, 54f, _shell.L("ui.menu.back"), () => _connect.SetActive(false), ButtonTier.Quiet, "btn_back");
                _connect.SetActive(false);
            }
#endif

            // --- Participate / "Join in" overlay (added last so it draws on top; hidden until "Mach mit") ---
            public void BuildParticipateOverlay()
            {
                var pdim = UiKit.AddModalDim(_root);
                _participate = pdim.gameObject;
                // The Test Pilot page (desktop builds) is the first way to join in: one more row under the intro.
                var hub = PlaytestHub.Instance;
                float o = hub != null ? 84f : 0f;
                var pdlg = UiKit.AddDialogPanel(_participate.transform, 560f, 250f - (o / 2f), 800f, 580f + o);
                UiKit.AddText(pdlg, 40f, 26f, 720f, 36f, _shell.L("ui.contribute.title"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

                Text Para(float y, float h, string text, int size, Color col)
                {
                    var t = UiKit.AddText(pdlg, 40f, y + o, 720f, h, text, size, col, TextAnchor.UpperLeft);
                    t.horizontalOverflow = HorizontalWrapMode.Wrap;
                    return t;
                }

                Para(82f - o, 44f, _shell.L("ui.contribute.intro"), 18, UiKit.TextCol);
                if (hub != null)
                {
                    int fresh = hub.Tracker.NewCount;
                    string label = fresh > 0
                        ? _shell.L("ui.testpilot.title") + " — " + string.Format(_shell.L("ui.testpilot.menu_new"), fresh)
                        : _shell.L("ui.testpilot.title");
                    UiKit.AddTierButton(pdlg, 40f, 136f, 720f, 60f, label, () =>
                    {
                        _participate.SetActive(false);
                        _shell.OpenTestPilot();
                    }, ButtonTier.Primary, "btn_testpilot");
                }

                // Player feedback first (for everyone, in-game) — highlighted; then play, then the GitHub paths.
                Para(138f, 70f, "1.  " + _shell.L("ui.contribute.feedback").Replace("{feedback_key}", FeedbackUi.HotkeyName), 17, UiKit.Ok);
                Para(212f, 50f, "2.  " + _shell.L("ui.contribute.play"), 17, UiKit.TextCol);
                Para(266f, 70f, "3.  " + _shell.L("ui.contribute.bugs"), 17, UiKit.TextCol);
                Para(340f, 50f, "4.  " + _shell.L("ui.contribute.dev"), 17, UiKit.TextCol);
                // Real buttons, not link-styled text (#544): the GitHub line used to be a dead AddText that
                // merely LOOKED clickable. GitHub + the game website open in the system browser; the website
                // follows the UI language (the German site is the root, English lives under /en).
                UiKit.AddTierButton(pdlg, 60f, 414f + o, 380f, 46f, _shell.L("ui.contribute.github"),
                    () => Application.OpenURL("https://github.com/marceld23/BlocksBeyondTheStars"), ButtonTier.Secondary, "btn_editors");
                UiKit.AddTierButton(pdlg, 460f, 414f + o, 280f, 46f, _shell.L("ui.contribute.website"), OpenWebsite, ButtonTier.Secondary, "btn_singleplayer");
                UiKit.AddTierButton(pdlg, 270f, 500f + o, 260f, 52f, _shell.L("ui.menu.back"), () => _participate.SetActive(false), ButtonTier.Quiet, "btn_back");
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
                }, "btn_new");

                if (onGlitch)
                {
                    // glitch.fun: a guest's world lives only in this browser's storage, and every new
                    // deployment starts that storage empty (#1177) — logging in on Glitch is the one thing
                    // that carries the world across updates and devices (Cloud Save), so say so right next
                    // to the buttons that start it (#1178). The rest of the row is free; wrap to two lines.
                    // 490 wide: it ends left of the What's-new card (x 1300, #3663); three lines at 14 still fit the row.
                    var hint = UiKit.AddText(_root, bx + bw + 252f, spY, 490f, bh, _shell.L("ui.webgl.glitch_save_hint"), 14,
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
                UiKit.AddTierButton(_root, bx, nextY + gap, bw, bh, _shell.L("ui.menu.settings"), _shell.OpenSettings, ButtonTier.Quiet, "btn_settings");
                UiKit.AddTierButton(_root, bx, nextY + gap * 2f, bw, bh, _shell.L("ui.menu.credits"), () => _shell.GoTo(ShellPhase.Credits), ButtonTier.Quiet, "btn_credits");
                StyleBrowserLead(onGlitch || bareMenu ? _shell.L(onGlitch ? "ui.menu.singleplayer_glitch" : "ui.menu.singleplayer") : _shell.L("ui.menu.play"));
            }

            // The browser menu's one primary action (#3662): the button on top — Play for a portal deep-link, Singleplayer
            // on glitch.fun and on a bare /play page.
            private void StyleBrowserLead(string label)
            {
                foreach (var button in _root.GetComponentsInChildren<Button>(true))
                {
                    var text = button.GetComponentInChildren<Text>();
                    if (text != null && text.text == label)
                    {
                        UiKit.StyleTier(button, ButtonTier.Primary);
                        return;
                    }
                }
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
                // Danger tier (#3662): it deletes the browser's world — and a pad's first focus in the dialog skips it.
                UiKit.AddTierButton(newWorldPanel, 30f, 240f, 260f, 56f, _shell.L("ui.webgl.new_world_confirm"), () =>
                {
                    newWorldOverlay.SetActive(false);
                    _shell.StartNewBrowserWorld();
                }, ButtonTier.Danger, "btn_delete");
                UiKit.AddTierButton(newWorldPanel, 310f, 240f, 260f, 56f, _shell.L("ui.action.cancel"), () => newWorldOverlay.SetActive(false), ButtonTier.Quiet, "btn_back");
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
                    }, "btn_continue");
                    UiKit.AddTierButton(namePanel, 310f, 270f, 260f, 56f, _shell.L("ui.action.cancel"), () =>
                    {
                        _shell.BrowserNamePromptShowing = false;
                        nameOverlay.SetActive(false);
                    }, ButtonTier.Quiet, "btn_back");
                    nameInput.ActivateInputField();
                }
            }
#else
            // The menu's pilot name (#221) and the warning line under it — both read by CommitName — and the name's
            // input field in the profile chip.
            private string _natName;
            private Text _natWarn;

            // --- Profile chip (top right, #3663): the player name every play mode uses, edited in place ---
            public void BuildProfileChip()
            {
                // The name is the gate for every play action (#221): the chip sits where players look for "who am I",
                // the warning under it says what is missing when a play button is pressed without a name.
                _natName = _shell.PlayerName;
                const float x = 1430f, y = 34f, w = 400f, h = 98f;
                var chip = UiKit.AddCard(_root, x, y, w, h);
                UiKit.AddIcon(chip, 18f, 25f, 48f, "cat_suit");
                UiKit.AddText(chip, 80f, 10f, w - 100f, 24f, _shell.L("ui.menu.connect_name"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddInput(chip, 80f, 38f, w - 100f, 46f, _natName, v => _natName = v,
                    maxLength: Protocol.MaxPlayerNameLength); // the server's join cap (#1368)
                _natWarn = UiKit.AddText(_root, x, y + h + 6f, w, 24f, "", 15,
                    new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleRight, FontStyle.Bold);
            }

            public void BuildNativeMenu()
            {
                float y = UiKit.MenuContentTop;
                var last = LastWorld.Find(_shell.Settings.LastWorld);
                if (last != null)
                {
                    // Continue (#3663): the most common way into the game in one click — the world, its mode, how long ago, and
                    // the picture of where the player left it when the world has one (WorldPreviewCapture).
                    AddEntry(ColX, y, ColW, ContinueH, _shell.L("ui.menu.continue"), last.Line(_shell.L), "btn_continue",
                        ButtonTier.Primary, () => { if (CommitName()) { _shell.StartSingleplayerWorld(last.Name); } }, last.PicturePath);
                    y += ContinueH + 14f;
                }

                AddEntry(ColX, y, ColW, EntryH, _shell.L("ui.menu.singleplayer"), _shell.L("ui.menu.singleplayer_sub"), "btn_singleplayer",
                    last == null ? ButtonTier.Primary : ButtonTier.Secondary, () => { if (CommitName()) { _shell.StartSingleplayer(); } });
                y += EntryPitch;
                AddEntry(ColX, y, ColW, EntryH, _shell.L("ui.menu.multiplayer"), _shell.L("ui.menu.multiplayer_sub"), "btn_multiplayer",
                    ButtonTier.Secondary, () => { if (CommitName()) { _shell.OpenMultiplayer(0); } });
                y += EntryPitch;
                AddEntry(ColX, y, ColW, EntryH, _shell.L("ui.menu.editors"), _shell.L("ui.menu.editors_sub"), "btn_editors",
                    ButtonTier.Secondary, () => _shell.GoTo(ShellPhase.Editors));
                y += EntryPitch + 6f;

                // Settings / Credits / Quit: side actions, smaller and quieter — the power icon belongs to Quit alone.
                UiKit.AddImage(_root, ColX, y, ColW, 1.5f, UiKit.SolidSprite, new Color(UiKit.CyanDim.r, UiKit.CyanDim.g, UiKit.CyanDim.b, 0.6f));
                y += 16f;
                UiKit.AddTierButton(_root, ColX, y, SideW, 56f, _shell.L("ui.menu.settings"), _shell.OpenSettings, ButtonTier.Quiet, "btn_settings");
                UiKit.AddTierButton(_root, ColX + SideW + SideGap, y, SideW, 56f, _shell.L("ui.menu.credits"), () => _shell.GoTo(ShellPhase.Credits), ButtonTier.Quiet, "btn_credits");
                UiKit.AddTierButton(_root, ColX + 2f * (SideW + SideGap), y, SideW, 56f, _shell.L("ui.menu.quit"), _shell.Quit, ButtonTier.Quiet, "btn_exit");
            }

            // A two-line menu entry: the action in big letters, what it leads to under it; a larger icon on the left — or, when
            // picturePath holds a picture (the Continue card's world), that picture in the icon's place.
            private void AddEntry(float x, float y, float w, float h, string title, string sub, string icon, ButtonTier tier, System.Action onClick,
                string picturePath = null)
            {
                var button = UiKit.AddTierButton(_root, x, y, w, h, title, onClick, tier, icon);
                float iconSize = h >= ContinueH ? 52f : 40f;
                foreach (var image in button.GetComponentsInChildren<Image>(true))
                {
                    if (image != button.targetGraphic)
                    {
                        UiKit.Place(image.gameObject, 20f, (h - iconSize) / 2f, iconSize, iconSize);
                    }
                }

                var label = button.GetComponentInChildren<Text>();
                float textX = AddEntryPicture(button, h, picturePath) ?? iconSize + 40f;
                UiKit.Place(label.gameObject, textX, h * 0.12f, w - textX - 16f, h * 0.46f);
                label.fontSize = h >= ContinueH ? 30 : 24;
                label.resizeTextForBestFit = false;
                UiKit.FitLabelSingleLine(label, w - textX - 16f, 14, label.fontSize);
                var line = UiKit.AddText(button.transform, textX, h * 0.56f, w - textX - 16f, h * 0.32f, sub, h >= ContinueH ? 17 : 15,
                    tier == ButtonTier.Primary ? new Color(0.05f, 0.22f, 0.33f) : UiKit.CyanDim, TextAnchor.MiddleLeft);
                UiKit.FitLabelSingleLine(line, w - textX - 16f, 11, line.fontSize);
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

#endif
        }
    }
}
