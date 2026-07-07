// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Client.Portal;
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
    /// </summary>
    public static class UiMainMenu
    {
        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("MainMenuUI");
            var root = canvas.transform;
            UiNav.Enable(canvas.gameObject); // let a gamepad drive the menu (inert on keyboard/mouse)

            // --- SYSTEM CHECK panel (decorative flavour) ---
            UiKit.AddPanel(root, 40f, 40f, 280f, 220f, UiKit.PanelFill);
            UiKit.AddText(root, 60f, 54f, 250f, 22f, shell.L("ui.menu.system_check"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            string[] sysKeys = { "ui.sys.engines", "ui.sys.shields", "ui.sys.life_support", "ui.sys.comms", "ui.sys.navigation" };
            string[] sysIcons = { "sys_engines", "sys_shields", "sys_life", "sys_comms", "sys_nav" };
            for (int i = 0; i < sysKeys.Length; i++)
            {
                float yy = 92f + i * 30f;
                UiKit.AddIcon(root, 46f, yy, 18f, sysIcons[i]);
                UiKit.AddText(root, 72f, yy, 178f, 22f, shell.L(sysKeys[i]), 16, UiKit.TextCol);
                UiKit.AddText(root, 250f, yy, 50f, 22f, shell.L("ui.sys.ok"), 16, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);
            }

            // --- Title ---
            UiKit.AddLogo(root, 360f, 70f, 1200f, 96f, "BLOCKS BEYOND THE STARS", 64);
            UiKit.AddText(root, 1700f, 44f, 180f, 24f, "VER. " + AppShell.Version, 16, UiKit.CyanDim, TextAnchor.MiddleRight);

            // Connect-to-server dialog (built below; the JOIN button reveals it). Captured by the button.
            // dlgName mirrors the dialog's name input so openers can carry the menu's name field over.
            GameObject connect = null;
            InputField dlgName = null;

            // Official-worlds overlay (native only; built below). Captured by its menu button.
            GameObject official = null;

            // --- One-shot notice (e.g. why the last join was refused) ---
            if (!string.IsNullOrEmpty(shell.MenuNotice))
            {
                UiKit.AddText(root, 90f, 286f, 1200f, 28f, shell.MenuNotice, 17,
                    new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            }

            // --- Menu buttons ---
            const float bx = 90f, bw = 440f, bh = 54f, gap = 62f;
            float by = 322f;
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browser build: a slimmed "enter your name and play" screen. There is no singleplayer,
            // host, editors or quit in the browser (no local filesystem, no bundled server, and quitting
            // a browser tab is meaningless). The server is preconfigured via Glitch/URL params, so the
            // primary action just joins it; "Connect to a server…" stays as a manual fallback. A name is
            // required so players never join the public realm anonymously. The whole block is guarded so
            // the native client (the #else below) is byte-for-byte unchanged.
            string[] webName = { shell.PlayerName };
            UiKit.AddText(root, bx, by, bw, 22f, shell.L("ui.menu.connect_name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(root, bx, by + 28f, bw, 44f, webName[0], v => webName[0] = v);
            var webWarn = UiKit.AddText(root, bx, by + 80f, bw, 22f, "", 14,
                new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            float wby = by + 112f;
            UiKit.AddButton(root, bx, wby, bw, bh, shell.L("ui.menu.play"), () =>
            {
                if (string.IsNullOrWhiteSpace(webName[0]))
                {
                    webWarn.text = shell.L("ui.webgl.need_name");
                    return;
                }

                shell.PlayerName = webName[0].Trim();
                shell.Settings.PlayerName = shell.PlayerName; // remember the identity across sessions
                shell.Settings.Save();
                shell.StartJoin();
            }, "btn_join");

            // The manual server picker only helps when /play was opened WITHOUT a deep-linked server —
            // players arriving through the portal already have host/port preconfigured, so the extra
            // choice is just noise for them (#221).
            float wextra = 0f;
            if (!GlitchIntegration.TryGetConfiguredServer(out _, out _, out _))
            {
                UiKit.AddButton(root, bx, wby + gap, bw, bh, shell.L("ui.menu.connect_manual"), () =>
                {
                    if (connect != null)
                    {
                        dlgName.text = webName[0]; // carry the menu's name over (fires the input's onChange)
                        connect.SetActive(true);
                    }
                }, "btn_join");
                wextra = gap;
            }

            // "My Worlds / Account" (#272): one click back to the worlds portal the game was served
            // from — same origin, so a self-hosted WorldHost links to its own portal, never to ours.
            // The portal's Play button deep-links back into /play, which closes the round-trip; the
            // portal page itself stays the browser home for signup/create/manage (HOSTED_WORLDS.md:
            // the WebGL menu never grows a server picker).
            UiKit.AddButton(root, bx, wby + wextra + gap, bw, bh, shell.L("ui.menu.my_worlds"), () =>
            {
                string portalUrl = System.Uri.TryCreate(Application.absoluteURL, System.UriKind.Absolute, out var page)
                    && page.Scheme != System.Uri.UriSchemeFile
                    ? page.GetLeftPart(System.UriPartial.Authority)
                    : PortalClient.DefaultPortalUrl; // local file test builds → the official portal
                Application.OpenURL(portalUrl + "/worlds");
            }, "btn_credits");
            UiKit.AddButton(root, bx, wby + wextra + gap * 2f, bw, bh, shell.L("ui.menu.settings"), shell.OpenSettings, "btn_settings");
            UiKit.AddButton(root, bx, wby + wextra + gap * 3f, bw, bh, shell.L("ui.menu.credits"), () => shell.GoTo(ShellPhase.Credits), "btn_credits");
#else
            // Pilot name on the menu itself (#221): play actions require a chosen name — the old silent
            // "Pilot" default meant nobody ever picked one and multiplayer names collided. The value is
            // persisted on use; the connect dialog's own name field stays as a per-join override.
            string[] natName = { shell.PlayerName };
            UiKit.AddText(root, bx, by, bw, 22f, shell.L("ui.menu.connect_name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(root, bx, by + 28f, bw, 44f, natName[0], v => natName[0] = v);
            var natWarn = UiKit.AddText(root, bx, by + 80f, bw, 22f, "", 14,
                new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            float nby = by + 112f;

            // True when a name is present (warns + blocks otherwise); commits it to the shell + settings.
            bool CommitName()
            {
                if (string.IsNullOrWhiteSpace(natName[0]))
                {
                    natWarn.text = shell.L("ui.webgl.need_name");
                    return false;
                }

                natWarn.text = "";
                shell.PlayerName = natName[0].Trim();
                shell.Settings.PlayerName = shell.PlayerName; // remember the identity across sessions
                shell.Settings.Save();
                shell.HostedToken = ""; // never let an official-worlds grant leak into SP/LAN/manual joins
                shell.HostedWorldId = "";
                return true;
            }

            UiKit.AddButton(root, bx, nby, bw, bh, shell.L("ui.menu.singleplayer"),
                () => { if (CommitName()) { shell.StartSingleplayer(); } }, "btn_singleplayer");
            UiKit.AddButton(root, bx, nby + gap, bw, bh, shell.L("ui.menu.host"),
                () => { if (CommitName()) { shell.StartHost(); } }, "btn_join");
            UiKit.AddButton(root, bx, nby + gap * 2f, bw, bh, shell.L("ui.menu.join"), () =>
            {
                if (CommitName() && connect != null)
                {
                    dlgName.text = shell.PlayerName; // carry the menu's name over (fires the input's onChange)
                    connect.SetActive(true);
                }
            }, "btn_join");
            UiKit.AddButton(root, bx, nby + gap * 3f, bw, bh, shell.L("ui.menu.official"), () =>
            {
                if (CommitName() && official != null)
                {
                    official.SetActive(true);
                }
            }, "btn_join");
            UiKit.AddButton(root, bx, nby + gap * 4f, bw, bh, shell.L("ui.menu.editors"), () => shell.GoTo(ShellPhase.Editors), "btn_singleplayer");
            UiKit.AddButton(root, bx, nby + gap * 5f, bw, bh, shell.L("ui.menu.settings"), shell.OpenSettings, "btn_settings");
            UiKit.AddButton(root, bx, nby + gap * 6f, bw, bh, shell.L("ui.menu.credits"), () => shell.GoTo(ShellPhase.Credits), "btn_credits");
            UiKit.AddButton(root, bx, nby + gap * 7f, bw, bh, shell.L("ui.menu.quit"), shell.Quit, "btn_exit");
#endif

            // --- World / server info panel (bottom-right, decorative) ---
            UiKit.AddPanel(root, 1290f, 650f, 590f, 250f, UiKit.PanelFill);
            UiKit.AddText(root, 1314f, 666f, 540f, 24f, shell.L("ui.menu.world_info"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddInfo(root, 706f, "info_mode", shell.L("ui.info.mode_title"), shell.L("ui.info.mode_desc"));
            AddInfo(root, 770f, "info_multiplayer", shell.L("ui.info.mp_title"), shell.L("ui.info.mp_desc"));
            AddInfo(root, 834f, "info_procedural", shell.L("ui.info.proc_title"), shell.L("ui.info.proc_desc"));

            // --- Bottom bar ---
            // The participate / "Join in" overlay (built below); the bottom-right button reveals it.
            GameObject participate = null;
            UiKit.AddText(root, 90f, 1030f, 500f, 26f, shell.L("ui.menu.community"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(root, 660f, 1030f, 600f, 26f, shell.L("ui.splash.tagline"), 18, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            // "Mach mit" — replaces the old "Wishlist on Steam" line; opens the open-source participate panel.
            UiKit.AddButton(root, 1620f, 1018f, 260f, 48f, shell.L("ui.menu.contribute"),
                () => { if (participate != null) participate.SetActive(true); }, "btn_credits");

            // --- Connect-to-server dialog (added last so it draws on top; hidden until JOIN is pressed) ---
            string[] name = { shell.PlayerName };
            string[] host = { shell.Host };
            string[] port = { shell.Port };
            string[] pass = { "" };
            var dim = UiKit.AddImage(root, 0f, 0f, 1920f, 1080f, UiKit.SolidSprite, new Color(0f, 0f, 0f, 0.6f));
            connect = dim.gameObject;
            dim.raycastTarget = true; // swallow clicks behind the dialog
            var dlg = UiKit.AddDialogPanel(connect.transform, 660f, 280f, 600f, 520f);
            UiKit.AddText(dlg, 30f, 24f, 540f, 30f, shell.L("ui.menu.connect_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddText(dlg, 30f, 80f, 540f, 22f, shell.L("ui.menu.connect_name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            dlgName = UiKit.AddInput(dlg, 30f, 106f, 540f, 38f, name[0], v => name[0] = v);
            UiKit.AddText(dlg, 30f, 160f, 540f, 22f, shell.L("ui.menu.connect_host"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(dlg, 30f, 186f, 540f, 38f, host[0], v => host[0] = v);
            UiKit.AddText(dlg, 30f, 240f, 540f, 22f, shell.L("ui.menu.connect_port"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(dlg, 30f, 266f, 260f, 38f, port[0], v => port[0] = v);
            UiKit.AddText(dlg, 30f, 320f, 540f, 22f, shell.L("ui.menu.connect_password"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            var passInput = UiKit.AddInput(dlg, 30f, 346f, 540f, 38f, pass[0], v => pass[0] = v);
            passInput.contentType = InputField.ContentType.Password; // mask it like the portal login field
            var dlgWarn = UiKit.AddText(dlg, 30f, 396f, 540f, 22f, "", 14,
                new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(dlg, 30f, 432f, 270f, 54f, shell.L("ui.menu.connect"), () =>
            {
                // A name is required (#221): joining anonymously fell back to a server-assigned
                // "player_N" identity nobody recognizes, and shared "Pilot" names collide.
                if (string.IsNullOrWhiteSpace(name[0]))
                {
                    dlgWarn.text = shell.L("ui.webgl.need_name");
                    return;
                }

                dlgWarn.text = "";
                shell.PlayerName = name[0].Trim();
                shell.Settings.PlayerName = shell.PlayerName; // remember the identity across sessions
                shell.Settings.Save();

                shell.Host = string.IsNullOrWhiteSpace(host[0]) ? "127.0.0.1" : host[0].Trim();
                shell.Port = string.IsNullOrWhiteSpace(port[0]) ? shell.Port : port[0].Trim();
                shell.Password = pass[0] ?? "";
                shell.HostedToken = ""; // manual join: no official-worlds grant
                shell.HostedWorldId = "";
                shell.StartJoin();
            }, "btn_join");
            UiKit.AddButton(dlg, 310f, 432f, 260f, 54f, shell.L("ui.menu.back"), () => connect.SetActive(false), "btn_exit");
            connect.SetActive(false);

#if !UNITY_WEBGL || UNITY_EDITOR
            // --- Official-worlds overlay (native only; HOSTED_WORLDS.md: the browser NEVER picks servers).
            // Full portal parity (#268-#270): sign up (incl. in-game rules acceptance), sign in, create,
            // join and manage your hosted worlds, save backups, feedback and account deletion — the web
            // portal is optional for desktop players. Joining threads the grant through shell.HostedToken.

            // Dialog slots, declared BEFORE any button lambda that may close them (definite assignment):
            // one for the form dialogs (signup / new world / manage / feedback / account — only one open),
            // one for the rules screen (may overlay the signup form without destroying its typed state),
            // one for the error-driven join-password prompt (#250).
            GameObject portalModal = null;
            GameObject rulesModal = null;
            GameObject passwordPrompt = null;

            var odim = UiKit.AddModalDim(root);
            official = odim.gameObject;
            // Wide panel (1100) so the world rows have a roomy status column — a "starting…"/"running"
            // status used to sit behind the Play/Manage buttons on the old 700-wide layout.
            var odlg = UiKit.AddDialogPanel(official.transform, 410f, 180f, 1100f, 720f);
            UiKit.AddText(odlg, 30f, 24f, 1040f, 30f, shell.L("ui.portal.title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            // The same notices the portal shows (client parity): the beta warning, the one-line rules
            // summary, and a button opening the full rules page in the browser. The long notices MUST
            // wrap — AddText defaults to overflow, which ran them under the button and off the panel.
            var betaLine = UiKit.AddText(odlg, 30f, 58f, 1040f, 40f, shell.L("ui.portal.beta"), 13,
                new Color(1f, 0.72f, 0.35f), TextAnchor.UpperLeft);
            betaLine.horizontalOverflow = HorizontalWrapMode.Wrap;
            var rulesLine = UiKit.AddText(odlg, 30f, 100f, 820f, 44f, shell.L("ui.portal.rules_line"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
            rulesLine.horizontalOverflow = HorizontalWrapMode.Wrap;
            // Right column (x=874, w=195): "Regeln ansehen" here, "Abmelden"/"Konto"/per-world "Verwalten"
            // below all share this column so the whole right edge lines up.
            UiKit.AddButton(odlg, 874f, 98f, 195f, 44f, shell.L("ui.portal.view_rules"),
                () => Application.OpenURL(PortalBase() + "/rules"), "btn_credits");

            var oStatus = UiKit.AddText(odlg, 66f, 592f, 1004f, 48f, "", 14,
                new Color(1f, 0.55f, 0.4f), TextAnchor.UpperLeft, FontStyle.Bold);
            oStatus.horizontalOverflow = HorizontalWrapMode.Wrap; // localized errors can be long
            // Spinner shown automatically while a status message is in progress (its text contains "…").
            UiKit.AddSpinner(odlg, 32f, 590f, 26f, oStatus);
            UiKit.AddButton(odlg, 415f, 648f, 270f, 54f, shell.L("ui.menu.back"), () =>
            {
                CloseAllPortalModals(); // leaving the overlay must not park stale dialogs behind it
                official.SetActive(false);
            }, "btn_exit");

            // Content area rebuilt on every state change (signed out ↔ signed in ↔ fresh world list).
            var oContent = UiKit.AddPanel(odlg, 0f, 150f, 1100f, 440f, new Color(0f, 0f, 0f, 0f)).transform;
            var oWorlds = new List<PortalWorldInfo>();

            string PortalBase() => string.IsNullOrWhiteSpace(shell.Settings.PortalUrl)
                ? PortalClient.DefaultPortalUrl
                : shell.Settings.PortalUrl;

            // WorldHost errors carry a stable machine code → show the player's language when we have a
            // translation ('ui.portal.err_<code>'); otherwise fall back to the API's English text. Ban
            // reasons are operator-written free text, so 'banned' keeps the original message.
            string PortalErr(string code, string error)
            {
                if (string.IsNullOrEmpty(code) || code == "banned")
                {
                    return error;
                }

                string key = "ui.portal.err_" + code;
                string localized = shell.L(key);
                return localized == key ? error : localized;
            }

            bool SignedIn() => !string.IsNullOrEmpty(shell.Settings.PortalSessionToken);

            void SignOut()
            {
                shell.Settings.PortalSessionToken = "";
                shell.Settings.PortalAccountName = "";
                shell.Settings.Save();
                oStatus.text = "";
                RebuildPortal();
            }

            async void DoRefresh()
            {
                oStatus.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.ListWorlds(session));
                if (official == null) { return; } // menu was torn down while the request ran
                if (!r.Ok)
                {
                    if (r.Code == "unauthorized" || r.Error == "unauthorized") { SignOut(); return; } // session expired → back to sign-in
                    oStatus.text = PortalErr(r.Code, r.Error);
                    return;
                }

                oStatus.text = "";
                oWorlds.Clear();
                oWorlds.AddRange(r.Worlds);
                RebuildPortal();
            }

            async void DoLogin(string account, string password)
            {
                oStatus.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                var r = await Task.Run(() => portal.Login(account, password));
                if (official == null) { return; }
                if (!r.Ok)
                {
                    oStatus.text = r.Code.Length > 0
                        ? PortalErr(r.Code, r.Error)
                        : shell.L("ui.portal.login_failed") + (r.Error.Length > 0 ? " (" + r.Error + ")" : "");
                    return;
                }

                shell.Settings.PortalSessionToken = r.SessionToken; // session only — the password is never stored
                shell.Settings.PortalAccountName = account;
                shell.Settings.Save();
                oStatus.text = "";
                RebuildPortal();
                DoRefresh();
                if (r.TermsOutdated)
                {
                    PromptReaccept(); // rules changed since the last visit — re-accept in-game (#268)
                }
            }

            // Session-scoped world passwords (#250): entered once per protected world, reused on re-joins,
            // never persisted. The prompt is error-driven — open worlds join without ever seeing it.
            var joinPasswords = new Dictionary<string, string>();

            // Secondary portal dialogs (#268-#270).
            PortalTermsResult cachedTerms = null; // rules text+version change per deployment — fetch once
            string saveBackupDir = System.IO.Path.Combine(Application.persistentDataPath, "portal_saves");
            var warnCol = new Color(1f, 0.55f, 0.4f);

            void CloseModal()
            {
                if (portalModal != null)
                {
                    Object.Destroy(portalModal);
                    portalModal = null;
                }
            }

            void CloseRules()
            {
                if (rulesModal != null)
                {
                    Object.Destroy(rulesModal);
                    rulesModal = null;
                }
            }

            void CloseAllPortalModals()
            {
                CloseRules();
                CloseModal();
                if (passwordPrompt != null)
                {
                    Object.Destroy(passwordPrompt);
                    passwordPrompt = null;
                }
            }

            // Opens a fresh form dialog: full-screen scrim + an OPAQUE dialog panel — the overlay's form
            // must never shine through and blend with the dialog (user acceptance feedback).
            Transform OpenModalPanel(float x, float y, float w, float h)
            {
                CloseModal();
                var mDim = UiKit.AddModalDim(official.transform, 0.9f);
                portalModal = mDim.gameObject;
                return UiKit.AddDialogPanel(portalModal.transform, x, y, w, h);
            }

            async void LoadTerms(System.Action<PortalTermsResult> done)
            {
                if (cachedTerms != null && cachedTerms.Ok)
                {
                    done(cachedTerms);
                    return;
                }

                var portal = new PortalClient(PortalBase());
                var r = await Task.Run(() => portal.GetTerms());
                if (official == null) { return; }
                cachedTerms = r;
                done(r);
            }

            // The in-game community-rules screen (text from GET /api/terms, localized DE/EN). With an
            // accept action it doubles as the consent step for signup and for re-acceptance after a
            // rules change; without one it is a plain viewer.
            void ShowRules(System.Action<PortalTermsResult> onAccept)
            {
                CloseRules();
                var rDim = UiKit.AddModalDim(official.transform, 0.92f);
                rulesModal = rDim.gameObject;
                var rDlg = UiKit.AddDialogPanel(rulesModal.transform, 160f, 80f, 1600f, 920f);
                var rTitle = UiKit.AddText(rDlg, 40f, 22f, 1520f, 32f, shell.L("ui.portal.rules_title"), 24, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                var rBody = UiKit.AddText(rDlg, 60f, 76f, 1480f, 730f, "", 17, UiKit.TextCol, TextAnchor.UpperLeft);
                rBody.horizontalOverflow = HorizontalWrapMode.Wrap;

                // Fetches (or re-fetches) the rules into the body — a friendly message instead of a raw
                // "http_404" when the portal is unreachable or too old to know /api/terms yet.
                void ApplyTerms()
                {
                    rBody.text = shell.L("ui.portal.working");
                    LoadTerms(terms =>
                    {
                        if (rulesModal == null) { return; }
                        if (!terms.Ok)
                        {
                            string msg = PortalErr(terms.Code, terms.Error);
                            rBody.text = msg.StartsWith("http_", System.StringComparison.Ordinal) ? shell.L("ui.portal.err_offline") : msg;
                            return;
                        }

                        rTitle.text = shell.L("ui.portal.rules_title") + " (v" + terms.Version + ")";
                        rBody.text = shell.Settings.Language == "de" ? terms.TextDe : terms.TextEn;
                    });
                }

                if (onAccept != null)
                {
                    UiKit.AddButton(rDlg, 430f, 836f, 380f, 54f, shell.L("ui.portal.rules_accept"), () =>
                    {
                        if (cachedTerms != null && cachedTerms.Ok)
                        {
                            onAccept(cachedTerms);
                        }
                        else
                        {
                            ApplyTerms(); // nothing loaded to consent to — retry instead of ignoring the click
                        }
                    }, "btn_join");
                }

                UiKit.AddButton(rDlg, 830f, 836f, 340f, 54f, shell.L("ui.menu.back"), CloseRules, "btn_exit");
                ApplyTerms();
            }

            async void DoAcceptTerms()
            {
                oStatus.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.AcceptTerms(session));
                if (official == null) { return; }
                CloseRules();
                if (!r.Ok)
                {
                    oStatus.text = PortalErr(r.Code, r.Error);
                    return;
                }

                oStatus.text = "";
                DoRefresh();
            }

            // The rules changed since this account accepted them (terms_outdated): world actions are
            // blocked until the player re-reads and re-accepts — same flow as the portal's login gate.
            void PromptReaccept()
            {
                oStatus.text = shell.L("ui.portal.terms_outdated");
                ShowRules(_ => DoAcceptTerms());
            }

            async void DoSignup(string accName, string password, int termsVersion, Text warn)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                var r = await Task.Run(() => portal.Signup(accName, password, termsVersion));
                if (official == null || warn == null) { return; } // overlay/dialog closed while the request ran
                if (!r.Ok)
                {
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                shell.Settings.PortalSessionToken = r.SessionToken; // a signup IS a sign-in (fresh session)
                shell.Settings.PortalAccountName = accName;
                shell.Settings.Save();
                CloseModal();
                oStatus.text = "";
                RebuildPortal();
                DoRefresh();
            }

            void OpenSignup()
            {
                var sDlg = OpenModalPanel(560f, 220f, 800f, 600f);
                UiKit.AddText(sDlg, 30f, 24f, 740f, 30f, shell.L("ui.portal.signup_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                string[] accName = { "" };
                string[] pw1 = { "" };
                string[] pw2 = { "" };
                bool[] accepted = { false };
                int[] termsVersion = { 0 };
                UiKit.AddText(sDlg, 40f, 70f, 720f, 22f, shell.L("ui.portal.account"), 15, UiKit.TextCol);
                UiKit.AddInput(sDlg, 40f, 94f, 720f, 38f, accName[0], v => accName[0] = v);
                UiKit.AddText(sDlg, 40f, 146f, 340f, 22f, shell.L("ui.portal.password_label"), 15, UiKit.TextCol);
                var sp1 = UiKit.AddInput(sDlg, 40f, 170f, 340f, 38f, pw1[0], v => pw1[0] = v);
                sp1.contentType = InputField.ContentType.Password;
                UiKit.AddText(sDlg, 420f, 146f, 340f, 22f, shell.L("ui.portal.password_repeat"), 15, UiKit.TextCol);
                var sp2 = UiKit.AddInput(sDlg, 420f, 170f, 340f, 38f, pw2[0], v => pw2[0] = v);
                sp2.contentType = InputField.ContentType.Password;
                var parents = UiKit.AddText(sDlg, 40f, 224f, 720f, 40f, shell.L("ui.portal.signup_parents"), 13, UiKit.Warn, TextAnchor.UpperLeft);
                parents.horizontalOverflow = HorizontalWrapMode.Wrap;

                // Consent is deliberate: the rules must be OPENED to accept them (the accept button lives
                // on the rules screen itself), mirroring the portal's required signup checkbox.
                var rulesState = UiKit.AddText(sDlg, 40f, 282f, 360f, 44f, shell.L("ui.portal.rules_state_open"), 14, warnCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddButton(sDlg, 420f, 280f, 340f, 46f, shell.L("ui.portal.rules_read"), () => ShowRules(terms =>
                {
                    accepted[0] = true;
                    termsVersion[0] = terms.Version;
                    CloseRules();
                    rulesState.text = shell.L("ui.portal.rules_state_ok");
                    rulesState.color = UiKit.Ok;
                }), "btn_credits");

                var sWarn = UiKit.AddText(sDlg, 40f, 344f, 720f, 44f, "", 14, warnCol, TextAnchor.UpperLeft, FontStyle.Bold);
                sWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddButton(sDlg, 40f, 406f, 340f, 54f, shell.L("ui.portal.signup"), () =>
                {
                    // Client-side pre-checks mirror the server's cheap rules, so typos don't burn the
                    // 5/hour signup rate limit; everything else (blocked/reserved/taken) is the server's call.
                    if (string.IsNullOrWhiteSpace(accName[0])) { sWarn.text = shell.L("ui.portal.err_name_invalid"); return; }
                    if (pw1[0] != pw2[0]) { sWarn.text = shell.L("ui.portal.err_password_mismatch"); return; }
                    if (pw1[0].Length < 8) { sWarn.text = shell.L("ui.portal.err_password_short"); return; }
                    if (!accepted[0]) { sWarn.text = shell.L("ui.portal.err_accept_rules"); return; }
                    DoSignup(accName[0].Trim(), pw1[0], termsVersion[0], sWarn);
                }, "btn_join");
                UiKit.AddButton(sDlg, 420f, 406f, 340f, 54f, shell.L("ui.menu.back"), CloseModal, "btn_exit");
            }

            async void DoCreateWorld(string worldName, string password, Text warn)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.CreateWorld(session, worldName, string.IsNullOrEmpty(password) ? null : password));
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    if (r.Code == "terms_outdated") { CloseModal(); PromptReaccept(); return; }
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                CloseModal();
                oStatus.text = "";
                DoRefresh();
            }

            void OpenCreateWorld()
            {
                var cDlg = OpenModalPanel(560f, 270f, 800f, 500f);
                UiKit.AddText(cDlg, 30f, 24f, 740f, 30f, shell.L("ui.portal.new_world_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                string[] worldName = { "" };
                string[] pw1 = { "" };
                string[] pw2 = { "" };
                UiKit.AddText(cDlg, 40f, 70f, 720f, 22f, shell.L("ui.portal.world_name"), 15, UiKit.TextCol);
                UiKit.AddInput(cDlg, 40f, 94f, 720f, 38f, worldName[0], v => worldName[0] = v);
                UiKit.AddText(cDlg, 40f, 146f, 340f, 22f, shell.L("ui.portal.world_password"), 15, UiKit.TextCol);
                var cp1 = UiKit.AddInput(cDlg, 40f, 170f, 340f, 38f, pw1[0], v => pw1[0] = v);
                cp1.contentType = InputField.ContentType.Password;
                UiKit.AddText(cDlg, 420f, 146f, 340f, 22f, shell.L("ui.portal.password_repeat"), 15, UiKit.TextCol);
                var cp2 = UiKit.AddInput(cDlg, 420f, 170f, 340f, 38f, pw2[0], v => pw2[0] = v);
                cp2.contentType = InputField.ContentType.Password;
                var optional = UiKit.AddText(cDlg, 40f, 222f, 720f, 40f, shell.L("ui.portal.world_password_optional"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
                optional.horizontalOverflow = HorizontalWrapMode.Wrap;
                var cWarn = UiKit.AddText(cDlg, 40f, 274f, 720f, 44f, "", 14, warnCol, TextAnchor.UpperLeft, FontStyle.Bold);
                cWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddButton(cDlg, 40f, 336f, 340f, 54f, shell.L("ui.portal.create"), () =>
                {
                    if (string.IsNullOrWhiteSpace(worldName[0])) { cWarn.text = shell.L("ui.portal.err_world_name_invalid"); return; }
                    if (pw1[0] != pw2[0]) { cWarn.text = shell.L("ui.portal.err_password_mismatch"); return; }
                    DoCreateWorld(worldName[0].Trim(), pw1[0], cWarn);
                }, "btn_join");
                UiKit.AddButton(cDlg, 420f, 336f, 340f, 54f, shell.L("ui.menu.back"), CloseModal, "btn_exit");
            }

            async void DoSetWorldPassword(string worldId, string password, Text warn)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.SetWorldPassword(session, worldId, password));
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                warn.color = UiKit.Ok;
                warn.text = shell.L("ui.portal.pw_updated");
                DoRefresh(); // the list's [PW] marker must follow
            }

            async void DoSetVisibility(string worldId, bool makePublic, Text warn)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.SetWorldVisibility(session, worldId, makePublic));
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                warn.color = UiKit.Ok;
                warn.text = shell.L(makePublic ? "ui.portal.public_on" : "ui.portal.public_off");
                DoRefresh(); // the list's [PUB] marker + the manage toggle must follow on reopen
            }

            async void DoRefreshPublic(Transform list, Text status)
            {
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.ListPublicWorlds(session));
                if (official == null || list == null) { return; }
                if (!r.Ok)
                {
                    if (r.Code == "unauthorized" || r.Error == "unauthorized") { CloseModal(); SignOut(); return; }
                    status.text = PortalErr(r.Code, r.Error);
                    return;
                }

                for (int i = list.childCount - 1; i >= 0; i--)
                {
                    Object.Destroy(list.GetChild(i).gameObject);
                }

                if (r.Worlds.Count == 0)
                {
                    status.text = shell.L("ui.portal.no_public");
                    return;
                }

                status.text = "";
                float ry = 0f;
                foreach (var world in r.Worlds)
                {
                    var w = world; // capture per row for the Play lambda
                    UiKit.AddText(list, 10f, ry + 10f, 400f, 26f, w.Name + "  [PW]", 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    UiKit.AddText(list, 420f, ry + 10f, 130f, 26f, w.Status, 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
                    UiKit.AddButton(list, 560f, ry, 160f, 46f, shell.L("ui.portal.play"), () => DoJoinWorld(w.Id), "btn_join");
                    ry += 56f;
                    if (ry > 470f) { break; } // dialog list area caps the visible rows
                }
            }

            void OpenPublicBrowse()
            {
                var pDlg = OpenModalPanel(560f, 180f, 800f, 720f);
                UiKit.AddText(pDlg, 30f, 24f, 740f, 30f, shell.L("ui.portal.public_browse_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                var pIntro = UiKit.AddText(pDlg, 40f, 62f, 720f, 44f, shell.L("ui.portal.public_intro"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
                pIntro.horizontalOverflow = HorizontalWrapMode.Wrap;
                var pStatus = UiKit.AddText(pDlg, 40f, 112f, 720f, 24f, shell.L("ui.portal.working"), 14, warnCol, TextAnchor.UpperLeft, FontStyle.Bold);
                pStatus.horizontalOverflow = HorizontalWrapMode.Wrap;
                var pList = UiKit.AddPanel(pDlg, 30f, 144f, 740f, 476f, new Color(0f, 0f, 0f, 0f)).transform;
                UiKit.AddButton(pDlg, 250f, 640f, 300f, 54f, shell.L("ui.menu.back"), CloseModal, "btn_exit");
                DoRefreshPublic(pList, pStatus);
            }

            async void DoStopWorld(string worldId, Text warn, Text statusLabel)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.StopWorld(session, worldId));
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                warn.color = UiKit.Ok;
                warn.text = shell.L("ui.portal.stopped");
                if (statusLabel != null) { statusLabel.text = "stopped"; }
                DoRefresh();
            }

            async void DoDeleteWorld(PortalWorldInfo world, string typedName, Text warn)
            {
                if (typedName.Trim() != world.Name)
                {
                    warn.color = warnCol;
                    warn.text = shell.L("ui.portal.err_delete_name_mismatch");
                    return;
                }

                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.DeleteWorld(session, world.Id));
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                CloseModal();
                oStatus.text = shell.L("ui.portal.deleted");
                DoRefresh();
            }

            async void DoDownloadSave(string worldId, Text warn)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                string path = System.IO.Path.Combine(saveBackupDir, worldId + "-world.db");
                var r = await Task.Run(() =>
                {
                    var dl = portal.DownloadSave(session, worldId);
                    if (dl.Ok)
                    {
                        try
                        {
                            System.IO.Directory.CreateDirectory(saveBackupDir);
                            System.IO.File.WriteAllBytes(path, dl.Bytes);
                        }
                        catch (System.Exception ex)
                        {
                            return new PortalSaveResult { Error = ex.Message };
                        }
                    }

                    return dl;
                });
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                warn.color = UiKit.Ok;
                warn.text = shell.L("ui.portal.save_downloaded");
            }

            async void DoUploadSave(string worldId, Text warn)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                string path = System.IO.Path.Combine(saveBackupDir, worldId + "-world.db");
                var r = await Task.Run(() =>
                {
                    try
                    {
                        if (!System.IO.File.Exists(path))
                        {
                            return new PortalSimpleResult { Code = "save_file_missing", Error = "save_file_missing" };
                        }

                        return portal.UploadSave(session, worldId, System.IO.File.ReadAllBytes(path));
                    }
                    catch (System.Exception ex)
                    {
                        return new PortalSimpleResult { Error = ex.Message };
                    }
                });
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    if (r.Code == "terms_outdated") { CloseModal(); PromptReaccept(); return; }
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                warn.color = UiKit.Ok;
                warn.text = shell.L("ui.portal.save_uploaded");
            }

            void OpenSaveFolder()
            {
                System.IO.Directory.CreateDirectory(saveBackupDir);
                Application.OpenURL("file:///" + saveBackupDir.Replace('\\', '/'));
            }

            void OpenManage(PortalWorldInfo world)
            {
                var mDlg = OpenModalPanel(460f, 150f, 1000f, 780f);
                UiKit.AddText(mDlg, 30f, 24f, 940f, 30f, world.Name, 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                var mStatus = UiKit.AddText(mDlg, 40f, 58f, 920f, 24f, world.Status + (world.HasPassword ? "  [PW]" : ""), 14, UiKit.CyanDim, TextAnchor.MiddleCenter);
                var mWarn = UiKit.AddText(mDlg, 76f, 596f, 884f, 56f, "", 14, warnCol, TextAnchor.UpperLeft, FontStyle.Bold);
                mWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddSpinner(mDlg, 42f, 596f, 26f, mWarn); // spins while a manage action is in progress

                // World join password (owner-only; empty remove keeps the world open).
                string[] mp1 = { "" };
                string[] mp2 = { "" };
                UiKit.AddText(mDlg, 40f, 100f, 920f, 22f, shell.L("ui.portal.world_password"), 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                var mpi1 = UiKit.AddInput(mDlg, 40f, 126f, 300f, 38f, mp1[0], v => mp1[0] = v);
                mpi1.contentType = InputField.ContentType.Password;
                var mpi2 = UiKit.AddInput(mDlg, 352f, 126f, 300f, 38f, mp2[0], v => mp2[0] = v);
                mpi2.contentType = InputField.ContentType.Password;
                UiKit.AddButton(mDlg, 664f, 122f, 155f, 44f, shell.L("ui.portal.pw_set"), () =>
                {
                    if (mp1[0] != mp2[0]) { mWarn.color = warnCol; mWarn.text = shell.L("ui.portal.err_password_mismatch"); return; }
                    if (string.IsNullOrEmpty(mp1[0])) { mWarn.color = warnCol; mWarn.text = shell.L("ui.portal.err_world_password_invalid"); return; }
                    DoSetWorldPassword(world.Id, mp1[0], mWarn);
                }, "btn_join");
                UiKit.AddButton(mDlg, 827f, 122f, 133f, 44f, shell.L("ui.portal.pw_remove"), () => DoSetWorldPassword(world.Id, "", mWarn), "btn_exit");

                // Lifecycle: stopping is also the precondition for save download/upload.
                UiKit.AddButton(mDlg, 40f, 192f, 300f, 50f, shell.L("ui.portal.stop"), () => DoStopWorld(world.Id, mWarn, mStatus), "btn_settings");
                var stopHint = UiKit.AddText(mDlg, 360f, 200f, 600f, 40f, shell.L("ui.portal.stop_hint"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
                stopHint.horizontalOverflow = HorizontalWrapMode.Wrap;

                // Save backup round-trip (browser-free): download to / upload from a fixed local folder.
                UiKit.AddText(mDlg, 40f, 262f, 920f, 22f, shell.L("ui.portal.save_title"), 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                var saveHint = UiKit.AddText(mDlg, 40f, 288f, 920f, 40f, shell.L("ui.portal.save_hint"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
                saveHint.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddButton(mDlg, 40f, 338f, 300f, 50f, shell.L("ui.portal.save_download"), () => DoDownloadSave(world.Id, mWarn), "btn_join");
                UiKit.AddButton(mDlg, 352f, 338f, 300f, 50f, shell.L("ui.portal.save_upload"), () => DoUploadSave(world.Id, mWarn), "btn_join");
                UiKit.AddButton(mDlg, 664f, 338f, 296f, 50f, shell.L("ui.portal.save_folder"), OpenSaveFolder, "btn_credits");

                // Danger zone: deletion needs the world's name typed back — a click can't do it by accident.
                UiKit.AddText(mDlg, 40f, 412f, 920f, 22f, shell.L("ui.portal.delete_world"), 15, UiKit.Warn, TextAnchor.MiddleLeft, FontStyle.Bold);
                var delHint = UiKit.AddText(mDlg, 40f, 438f, 920f, 24f, shell.L("ui.portal.delete_world_hint"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
                delHint.horizontalOverflow = HorizontalWrapMode.Wrap;
                string[] typed = { "" };
                UiKit.AddInput(mDlg, 40f, 468f, 480f, 38f, typed[0], v => typed[0] = v, world.Name);
                UiKit.AddButton(mDlg, 552f, 464f, 408f, 46f, shell.L("ui.portal.delete_world"), () => DoDeleteWorld(world, typed[0], mWarn), "btn_exit");

                // Public listing (opt-in). Requires a join password — public worlds stay password-gated, so
                // strangers who find the world in the browser still need the owner-shared password to join.
                UiKit.AddText(mDlg, 40f, 522f, 600f, 22f, shell.L("ui.portal.public_title"), 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                var pubHint = UiKit.AddText(mDlg, 40f, 548f, 600f, 44f,
                    shell.L(world.HasPassword ? "ui.portal.public_hint" : "ui.portal.public_needs_pw"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
                pubHint.horizontalOverflow = HorizontalWrapMode.Wrap;
                if (world.HasPassword)
                {
                    UiKit.AddButton(mDlg, 664f, 540f, 296f, 46f,
                        shell.L(world.IsPublic ? "ui.portal.make_private" : "ui.portal.make_public"),
                        () => DoSetVisibility(world.Id, !world.IsPublic, mWarn), world.IsPublic ? "btn_exit" : "btn_join");
                }

                UiKit.AddButton(mDlg, 330f, 700f, 340f, 54f, shell.L("ui.menu.back"), CloseModal, "btn_exit");
            }

            async void DoFeedbackSend(string message, Text warn)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.Report(session, "", "feedback", message));
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                warn.color = UiKit.Ok;
                warn.text = shell.L("ui.portal.feedback_thanks");
            }

            void OpenFeedback()
            {
                var fDlg = OpenModalPanel(560f, 280f, 800f, 500f);
                UiKit.AddText(fDlg, 30f, 24f, 740f, 30f, shell.L("ui.portal.feedback_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                var fHint = UiKit.AddText(fDlg, 40f, 66f, 720f, 44f, shell.L("ui.portal.feedback_hint"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
                fHint.horizontalOverflow = HorizontalWrapMode.Wrap;
                string[] msg = { "" };
                var msgInput = UiKit.AddInput(fDlg, 40f, 120f, 720f, 170f, msg[0], v => msg[0] = v);
                msgInput.lineType = InputField.LineType.MultiLineNewline; // feedback wants sentences, not one line
                msgInput.textComponent.alignment = TextAnchor.UpperLeft;
                var fWarn = UiKit.AddText(fDlg, 40f, 302f, 720f, 40f, "", 14, warnCol, TextAnchor.UpperLeft, FontStyle.Bold);
                fWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddButton(fDlg, 40f, 356f, 340f, 54f, shell.L("ui.portal.send"), () =>
                {
                    if (string.IsNullOrWhiteSpace(msg[0])) { fWarn.color = warnCol; fWarn.text = shell.L("ui.portal.feedback_hint"); return; }
                    DoFeedbackSend(msg[0].Trim(), fWarn);
                }, "btn_join");
                UiKit.AddButton(fDlg, 420f, 356f, 340f, 54f, shell.L("ui.menu.back"), CloseModal, "btn_exit");
            }

            async void DoDeleteAccount(Text warn)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.DeleteAccount(session));
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                CloseModal();
                SignOut(); // wipes the (now dead) session + account name and rebuilds the sign-in view
                oStatus.text = shell.L("ui.portal.account_deleted");
            }

            void OpenAccount()
            {
                var aDlg = OpenModalPanel(510f, 260f, 900f, 560f);
                UiKit.AddText(aDlg, 30f, 24f, 840f, 30f, shell.L("ui.portal.account_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.AddText(aDlg, 40f, 70f, 820f, 26f,
                    shell.L("ui.portal.signed_in") + " " + shell.Settings.PortalAccountName, 16, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);
                var delWarn = UiKit.AddText(aDlg, 40f, 112f, 820f, 110f, shell.L("ui.portal.delete_account_warn"), 14, UiKit.Warn, TextAnchor.UpperLeft);
                delWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddText(aDlg, 40f, 234f, 820f, 22f, shell.L("ui.portal.delete_confirm_name"), 14, UiKit.TextCol);
                string[] typed = { "" };
                UiKit.AddInput(aDlg, 40f, 260f, 480f, 38f, typed[0], v => typed[0] = v, shell.Settings.PortalAccountName);
                var aWarn = UiKit.AddText(aDlg, 40f, 318f, 820f, 44f, "", 14, warnCol, TextAnchor.UpperLeft, FontStyle.Bold);
                aWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddButton(aDlg, 40f, 380f, 480f, 50f, shell.L("ui.portal.delete_account"), () =>
                {
                    if (typed[0].Trim() != shell.Settings.PortalAccountName)
                    {
                        aWarn.color = warnCol;
                        aWarn.text = shell.L("ui.portal.err_delete_name_mismatch");
                        return;
                    }

                    DoDeleteAccount(aWarn);
                }, "btn_exit");
                UiKit.AddButton(aDlg, 540f, 380f, 320f, 50f, shell.L("ui.menu.back"), CloseModal, "btn_exit");
            }

            void PromptWorldPassword(string worldId, bool wrongBefore)
            {
                if (passwordPrompt != null)
                {
                    Object.Destroy(passwordPrompt);
                }

                var pwDim = UiKit.AddModalDim(official.transform, 0.9f);
                passwordPrompt = pwDim.gameObject;
                var pwDlg = UiKit.AddDialogPanel(passwordPrompt.transform, 660f, 390f, 600f, 300f);
                UiKit.AddText(pwDlg, 30f, 24f, 540f, 30f, shell.L("ui.portal.world_password"), 20, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                if (wrongBefore)
                {
                    UiKit.AddText(pwDlg, 30f, 62f, 540f, 24f, shell.L("ui.portal.err_wrong_password"), 14,
                        new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleCenter, FontStyle.Bold);
                }

                string[] pw = { "" };
                var pwField = UiKit.AddInput(pwDlg, 30f, 100f, 540f, 38f, pw[0], v => pw[0] = v);
                pwField.contentType = InputField.ContentType.Password;
                UiKit.AddButton(pwDlg, 30f, 170f, 260f, 54f, shell.L("ui.portal.play"), () =>
                {
                    if (string.IsNullOrEmpty(pw[0]))
                    {
                        return;
                    }

                    joinPasswords[worldId] = pw[0];
                    Object.Destroy(passwordPrompt);
                    passwordPrompt = null;
                    DoJoinWorld(worldId);
                }, "btn_join");
                UiKit.AddButton(pwDlg, 310f, 170f, 260f, 54f, shell.L("ui.menu.back"), () =>
                {
                    Object.Destroy(passwordPrompt);
                    passwordPrompt = null;
                    oStatus.text = "";
                }, "btn_exit");
            }

            // Error-driven player-name prompt: the world join was refused because the current player name is
            // reserved/not allowed. Let the player type another name (writing it back to the menu's name field
            // so CommitName picks it up), then retry the same join.
            void PromptPlayerName(string worldId, string code)
            {
                if (passwordPrompt != null)
                {
                    Object.Destroy(passwordPrompt);
                }

                var dim = UiKit.AddModalDim(official.transform, 0.9f);
                passwordPrompt = dim.gameObject;
                var dlg = UiKit.AddDialogPanel(passwordPrompt.transform, 610f, 350f, 700f, 360f);
                UiKit.AddText(dlg, 30f, 24f, 640f, 30f, shell.L("ui.menu.connect_name"), 20, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                var hint = UiKit.AddText(dlg, 30f, 64f, 640f, 60f,
                    shell.L(code == "name_reserved" ? "ui.portal.err_name_reserved_hint" : "ui.portal.err_name_blocked_hint"),
                    14, new Color(1f, 0.55f, 0.4f), TextAnchor.UpperLeft, FontStyle.Bold);
                hint.horizontalOverflow = HorizontalWrapMode.Wrap;

                string[] nm = { string.Empty };
                UiKit.AddInput(dlg, 30f, 132f, 640f, 40f, nm[0], v => nm[0] = v);
                UiKit.AddButton(dlg, 30f, 192f, 320f, 54f, shell.L("ui.portal.play"), () =>
                {
                    if (string.IsNullOrWhiteSpace(nm[0]))
                    {
                        return;
                    }

                    natName[0] = nm[0].Trim(); // CommitName (in DoJoinWorld) reads the menu field, not shell.PlayerName
                    Object.Destroy(passwordPrompt);
                    passwordPrompt = null;
                    DoJoinWorld(worldId);
                }, "btn_join");
                UiKit.AddButton(dlg, 370f, 192f, 300f, 54f, shell.L("ui.menu.back"), () =>
                {
                    Object.Destroy(passwordPrompt);
                    passwordPrompt = null;
                    oStatus.text = string.Empty;
                }, "btn_exit");
            }

            async void DoJoinWorld(string worldId)
            {
                if (!CommitName())
                {
                    oStatus.text = shell.L("ui.webgl.need_name");
                    return;
                }

                oStatus.text = shell.L("ui.portal.waking"); // waking a sleeping world can take a moment
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                string playerName = shell.PlayerName;
                joinPasswords.TryGetValue(worldId, out string worldPassword);
                var r = await Task.Run(() => portal.JoinWorld(session, worldId, playerName, worldPassword));
                if (official == null) { return; }
                if (!r.Ok)
                {
                    if (r.Code == "password_required" || r.Code == "wrong_password")
                    {
                        joinPasswords.Remove(worldId); // a cached one that failed is stale
                        oStatus.text = "";
                        PromptWorldPassword(worldId, wrongBefore: r.Code == "wrong_password");
                        return;
                    }

                    // The chosen player name is reserved/not allowed — let the player pick another one right
                    // here and retry, instead of a dead-end error they can only fix back on the main menu.
                    if (r.Code == "name_reserved" || r.Code == "name_blocked")
                    {
                        oStatus.text = "";
                        PromptPlayerName(worldId, r.Code);
                        return;
                    }

                    oStatus.text = PortalErr(r.Code, r.Error);
                    return;
                }

                shell.Host = r.NativeHost;
                shell.Port = r.NativePort.ToString();
                shell.Password = "";
                shell.HostedToken = r.JoinToken; // the grant the server-side token gate verifies
                shell.HostedWorldId = worldId; // attached to in-game player reports
                shell.StartJoin();
            }

            void RebuildPortal()
            {
                for (int i = oContent.childCount - 1; i >= 0; i--)
                {
                    Object.Destroy(oContent.GetChild(i).gameObject);
                }

                if (!SignedIn())
                {
                    string[] acc = { shell.Settings.PortalAccountName };
                    string[] pw = { "" };
                    UiKit.AddText(oContent, 30f, 20f, 1040f, 22f, shell.L("ui.portal.account"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                    UiKit.AddInput(oContent, 30f, 46f, 1040f, 38f, acc[0], v => acc[0] = v);
                    UiKit.AddText(oContent, 30f, 100f, 1040f, 22f, shell.L("ui.menu.connect_password"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                    var pwInput = UiKit.AddInput(oContent, 30f, 126f, 1040f, 38f, pw[0], v => pw[0] = v);
                    pwInput.contentType = InputField.ContentType.Password;
                    UiKit.AddButton(oContent, 30f, 184f, 320f, 54f, shell.L("ui.portal.login"), () => DoLogin(acc[0].Trim(), pw[0]), "btn_join");
                    UiKit.AddButton(oContent, 370f, 184f, 320f, 54f, shell.L("ui.portal.signup"), OpenSignup, "btn_credits");
                    var signupHere = UiKit.AddText(oContent, 30f, 260f, 1040f, 44f, shell.L("ui.portal.signup_here"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
                    signupHere.horizontalOverflow = HorizontalWrapMode.Wrap;
                    UiKit.AddText(oContent, 30f, 310f, 1040f, 24f, PortalBase(), 15, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                    return;
                }

                UiKit.AddText(oContent, 30f, 18f, 640f, 26f,
                    shell.L("ui.portal.signed_in") + " " + shell.Settings.PortalAccountName, 16, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddButton(oContent, 874f, 8f, 195f, 44f, shell.L("ui.portal.logout"), SignOut, "btn_exit");

                // Entry points in one uniform 5-column grid (equal width + gap) so the row lines up cleanly.
                // Columns are reused below: Play sits in the "Feedback" column (663), Manage in the "Konto"
                // column (874) — so every button shares a column edge with the row above it.
                const float colW = 195f;
                float[] col = { 30f, 241f, 452f, 663f, 874f };
                UiKit.AddButton(oContent, col[0], 54f, colW, 44f, shell.L("ui.portal.refresh"), DoRefresh, "btn_settings");
                UiKit.AddButton(oContent, col[1], 54f, colW, 44f, shell.L("ui.portal.new_world"), OpenCreateWorld, "btn_join");
                UiKit.AddButton(oContent, col[2], 54f, colW, 44f, shell.L("ui.portal.public_browse"), OpenPublicBrowse, "btn_credits");
                UiKit.AddButton(oContent, col[3], 54f, colW, 44f, shell.L("ui.portal.feedback"), OpenFeedback, "btn_credits");
                UiKit.AddButton(oContent, col[4], 54f, colW, 44f, shell.L("ui.portal.account_btn"), OpenAccount, "btn_settings");

                if (oWorlds.Count == 0)
                {
                    var noWorlds = UiKit.AddText(oContent, 30f, 116f, 1040f, 48f, shell.L("ui.portal.no_worlds"), 15, UiKit.TextCol, TextAnchor.UpperLeft);
                    noWorlds.horizontalOverflow = HorizontalWrapMode.Wrap;
                    return;
                }

                float ry = 112f;
                foreach (var world in oWorlds)
                {
                    var w = world; // capture per row (Play joins, Manage opens the owner dialog)
                    UiKit.AddText(oContent, 30f, ry + 10f, 380f, 26f, w.Name, 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    // Status gets its own wide column (420..640) so "starting…" / "running [PW] [PUB]" never
                    // hides behind the buttons; Play/Manage align with the Feedback/Konto columns above.
                    var st = UiKit.AddText(oContent, 420f, ry + 10f, 220f, 26f, w.Status + (w.HasPassword ? " [PW]" : "") + (w.IsPublic ? " [PUB]" : ""), 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
                    st.horizontalOverflow = HorizontalWrapMode.Wrap;
                    UiKit.AddButton(oContent, col[3], ry, colW, 44f, shell.L("ui.portal.play"), () => DoJoinWorld(w.Id), "btn_join");
                    UiKit.AddButton(oContent, col[4], ry, colW, 44f, shell.L("ui.portal.manage"), () => OpenManage(w));
                    ry += 56f;
                    if (ry > 384f) { break; } // quota keeps this short; guard against overflow anyway
                }
            }

            RebuildPortal();
            if (SignedIn())
            {
                DoRefresh(); // stay signed in across launches: populate the list right away
            }

            official.SetActive(false);
#endif

            // --- Participate / "Join in" overlay (added last so it draws on top; hidden until "Mach mit") ---
            var pdim = UiKit.AddModalDim(root);
            participate = pdim.gameObject;
            var pdlg = UiKit.AddDialogPanel(participate.transform, 560f, 250f, 800f, 580f);
            UiKit.AddText(pdlg, 40f, 26f, 720f, 36f, shell.L("ui.contribute.title"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            Text Para(float y, float h, string text, int size, Color col)
            {
                var t = UiKit.AddText(pdlg, 40f, y, 720f, h, text, size, col, TextAnchor.UpperLeft);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                return t;
            }

            Para(82f, 44f, shell.L("ui.contribute.intro"), 18, UiKit.TextCol);
            // Player feedback first (for everyone, in-game) — highlighted; then play, then the GitHub paths.
            Para(138f, 70f, "1.  " + shell.L("ui.contribute.feedback"), 17, UiKit.Ok);
            Para(212f, 50f, "2.  " + shell.L("ui.contribute.play"), 17, UiKit.TextCol);
            Para(266f, 70f, "3.  " + shell.L("ui.contribute.bugs"), 17, UiKit.TextCol);
            Para(340f, 50f, "4.  " + shell.L("ui.contribute.dev"), 17, UiKit.TextCol);
            UiKit.AddText(pdlg, 40f, 424f, 720f, 26f, shell.L("ui.contribute.github"), 17, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(pdlg, 270f, 500f, 260f, 52f, shell.L("ui.menu.back"), () => participate.SetActive(false), "btn_exit");
            participate.SetActive(false);

            return canvas.gameObject;
        }

        private static void AddInfo(Transform root, float y, string icon, string title, string desc)
        {
            UiKit.AddIcon(root, 1314f, y + 4f, 32f, icon);
            UiKit.AddText(root, 1356f, y, 500f, 22f, title, 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(root, 1356f, y + 24f, 500f, 22f, desc, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
        }
    }
}
