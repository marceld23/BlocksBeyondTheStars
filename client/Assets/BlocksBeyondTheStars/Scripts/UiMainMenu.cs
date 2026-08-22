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
            // A settings-recovery incident (#410) is claimed here rather than at load time: ClientSettings.Load
            // runs before the localizer exists, so it can only stash the locale KEY of the notice.
            if (string.IsNullOrEmpty(shell.MenuNotice) && !string.IsNullOrEmpty(ClientSettings.LoadNoticeKey))
            {
                shell.MenuNotice = shell.L(ClientSettings.LoadNoticeKey);
                ClientSettings.LoadNoticeKey = "";
            }

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
            // Accented like the native menu: the name gates every play action (#221); panel edges flush
            // with the button column, content inset — see the native branch below.
            UiKit.AddPanel(root, bx, by - 10f, bw, 100f, new Color(0.12f, 0.45f, 0.62f, 0.22f));
            UiKit.AddText(root, bx + 16f, by, bw - 32f, 24f, shell.L("ui.menu.connect_name"), 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddInput(root, bx + 16f, by + 30f, bw - 32f, 46f, webName[0], v => webName[0] = v);
            var webWarn = UiKit.AddText(root, bx, by + 80f, bw, 22f, "", 14,
                new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            float wby = by + 112f;

            // glitch.fun context (install_id in the URL): the player NEVER picks a server here — the
            // arcade auto-join runs on page load, so the menu only appears when it failed. Play then
            // re-requests an arcade session instead of dialing the meaningless default host (which
            // built an empty, serverless world rig), and the manual server picker stays hidden.
            bool onGlitch = GlitchIntegration.ArcadeInstallId.Length > 0 && GlitchIntegration.PortalUrl.Length > 0;

            // On glitch.fun the singleplayer entry leads and the arcade drops to second place, labeled
            // "Multiplayer (Arcade)": store visitors should discover the in-browser world first (first
            // live feedback said the arcade hid that singleplayer exists). Portal /play deep-links keep
            // the join button on top — there the player explicitly chose a server to join.
            float joinY = onGlitch ? wby + gap : wby;
            float spY = onGlitch ? wby : wby + gap;
            UiKit.AddButton(root, bx, joinY, bw, bh, shell.L(onGlitch ? "ui.menu.arcade" : "ui.menu.play"), () =>
            {
                if (string.IsNullOrWhiteSpace(webName[0]))
                {
                    webWarn.text = shell.L("ui.webgl.need_name");
                    return;
                }

                shell.PlayerName = webName[0].Trim();
                shell.Settings.PlayerName = shell.PlayerName; // remember the identity across sessions
                shell.Settings.Save();
                if (onGlitch)
                {
                    shell.RetryArcadeJoin();
                }
                else
                {
                    shell.StartJoin();
                }
            }, "btn_join");

            // In-browser singleplayer: the REAL authoritative server runs in-process (LoopbackTransport,
            // MemoryWorldRepository) — one persistent world per browser, synced to the Glitch cloud when
            // the build runs on glitch.fun with a logged-in account. A name is still required: it is the
            // player identity the save keys on. The glitch label is the plain "Singleplayer" so the pair
            // reads Singleplayer / Multiplayer (Arcade); elsewhere the "/ Local World" suffix stays.
            UiKit.AddButton(root, bx, spY, bw, bh, shell.L(onGlitch ? "ui.menu.singleplayer_glitch" : "ui.menu.singleplayer"), () =>
            {
                if (string.IsNullOrWhiteSpace(webName[0]))
                {
                    webWarn.text = shell.L("ui.webgl.need_name");
                    return;
                }

                shell.PlayerName = webName[0].Trim();
                shell.Settings.PlayerName = shell.PlayerName;
                shell.Settings.Save();
                shell.StartBrowserSingleplayer();
            }, "btn_singleplayer");

            if (onGlitch)
            {
                // glitch.fun: a guest's world lives only in this browser's storage, and every new
                // deployment starts that storage empty (#1177) — logging in on Glitch is the one thing
                // that carries the world across updates and devices (Cloud Save), so say so right next
                // to the button that starts it (#1178). The column to the right of the buttons is free.
                UiKit.AddText(root, bx + bw + 20f, spY, 760f, bh, shell.L("ui.webgl.glitch_save_hint"), 14,
                    UiKit.CyanDim, TextAnchor.MiddleLeft);
            }

            // The manual server picker only helps when /play was opened WITHOUT a deep-linked server —
            // players arriving through the portal already have host/port preconfigured, and on
            // glitch.fun there is nothing to pick at all (#221).
            float wextra = 0f;
            if (!onGlitch && !GlitchIntegration.TryGetConfiguredServer(out _, out _, out _))
            {
                UiKit.AddButton(root, bx, wby + gap * 2f, bw, bh, shell.L("ui.menu.connect_manual"), () =>
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
            UiKit.AddButton(root, bx, wby + wextra + gap * 2f, bw, bh, shell.L("ui.menu.my_worlds"), () =>
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
            UiKit.AddButton(root, bx, wby + wextra + gap * 3f, bw, bh, shell.L("ui.menu.settings"), shell.OpenSettings, "btn_settings");
            UiKit.AddButton(root, bx, wby + wextra + gap * 4f, bw, bh, shell.L("ui.menu.credits"), () => shell.GoTo(ShellPhase.Credits), "btn_credits");
#else
            // Pilot name on the menu itself (#221): play actions require a chosen name — the old silent
            // "Pilot" default meant nobody ever picked one and multiplayer names collided. The value is
            // persisted on use; the connect dialog's own name field stays as a per-join override.
            string[] natName = { shell.PlayerName };
            // The name is the gate for every play action (#221) — make the field read as step one, not
            // as a side note: an accented backdrop + bold cyan label instead of the plain grey line.
            // The panel's outer edges sit exactly on the button column (bx..bx+bw); content insets instead.
            UiKit.AddPanel(root, bx, by - 10f, bw, 100f, new Color(0.12f, 0.45f, 0.62f, 0.22f));
            UiKit.AddText(root, bx + 16f, by, bw - 32f, 24f, shell.L("ui.menu.connect_name"), 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddInput(root, bx + 16f, by + 30f, bw - 32f, 46f, natName[0], v => natName[0] = v);
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
                shell.ArcadeNameToken = "";
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
            // Its bottom edge (672+250=922) lines up with the menu column's last entry ("Quit" ends
            // at 434+62*7+54=922), so the two columns close on one shared baseline.
            UiKit.AddPanel(root, 1290f, 672f, 590f, 250f, UiKit.PanelFill);
            UiKit.AddText(root, 1314f, 688f, 540f, 24f, shell.L("ui.menu.world_info"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddInfo(root, 728f, "info_mode", shell.L("ui.info.mode_title"), shell.L("ui.info.mode_desc"));
            AddInfo(root, 792f, "info_multiplayer", shell.L("ui.info.mp_title"), shell.L("ui.info.mp_desc"));
            AddInfo(root, 856f, "info_procedural", shell.L("ui.info.proc_title"), shell.L("ui.info.proc_desc"));

            // --- Bottom bar ---
            // The participate / "Join in" overlay (built below); the bottom-right button reveals it.
            GameObject participate = null;
            UiKit.AddText(root, 90f, 1030f, 500f, 26f, shell.L("ui.menu.community"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(root, 660f, 1030f, 600f, 26f, shell.L("ui.splash.tagline"), 18, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            // "What's new?" (#543, the devblog release notes in-game — also auto-opens once after an
            // update) + "Mach mit" (opens the open-source participate panel; replaced the old "Wishlist
            // on Steam" line). The pair spans exactly the world-info panel above (1290..1880), so the
            // right column closes flush.
            UiKit.AddButton(root, 1290f, 1018f, 285f, 48f, shell.L("ui.menu.whatsnew"),
                shell.OpenWhatsNew, "btn_credits");
            UiKit.AddButton(root, 1595f, 1018f, 285f, 48f, shell.L("ui.menu.contribute"),
                () => { if (participate != null) participate.SetActive(true); }, "btn_credits");

            // --- Connect-to-server dialog (added last so it draws on top; hidden until JOIN is pressed) ---
            string[] name = { shell.PlayerName };
            string[] host = { shell.ManualJoinHost };
            string[] port = { shell.ManualJoinPort };
            string[] pass = { "" };
            var (connectOverlay, dlg) = UiKit.AddModalOverlay(root, 660f, 280f, 600f, 520f);
            connect = connectOverlay;
            UiKit.AddText(dlg, 30f, 24f, 540f, 30f, shell.L("ui.menu.connect_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddText(dlg, 30f, 80f, 540f, 22f, shell.L("ui.menu.connect_name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            dlgName = UiKit.AddInput(dlg, 30f, 106f, 540f, 38f, name[0], v => name[0] = v);
            UiKit.AddText(dlg, 30f, 160f, 540f, 22f, shell.L("ui.menu.connect_host"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(dlg, 30f, 186f, 540f, 38f, host[0], v => host[0] = v);
            UiKit.AddText(dlg, 30f, 240f, 540f, 22f, shell.L("ui.menu.connect_port"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(dlg, 30f, 266f, 260f, 38f, port[0], v => port[0] = v);
            // Both well-known defaults next to the port field. The field itself prefills the "Host Game"
            // port (#978) because that is what this dialog is for — official worlds arrive with their own
            // host + port from the portal — and the hint still names the dedicated-server port for anyone
            // typing in a server address by hand (#960).
            UiKit.AddText(dlg, 306f, 258f, 264f, 54f,
                shell.L("ui.menu.connect_port_hint")
                    .Replace("{official}", AppShell.DefaultServerPort.ToString())
                    .Replace("{hosted}", LocalServerLauncher.DefaultPort.ToString()),
                13, UiKit.CyanDim, TextAnchor.MiddleLeft);
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

                // Remember what was typed for the next time this dialog opens, then dial it. The prefill is
                // kept apart from the live join target (#978) — the portal join overwrites the latter.
                shell.ManualJoinHost = string.IsNullOrWhiteSpace(host[0]) ? "127.0.0.1" : host[0].Trim();
                shell.ManualJoinPort = string.IsNullOrWhiteSpace(port[0]) ? shell.ManualJoinPort : port[0].Trim();
                shell.Host = shell.ManualJoinHost;
                shell.Port = shell.ManualJoinPort;
                shell.Password = pass[0] ?? "";
                shell.HostedToken = ""; // manual join: no official-worlds grant
                shell.HostedWorldId = "";
                shell.ArcadeNameToken = "";
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
            var oWorlds = new List<PortalWorldInfo>();  // the signed-in account's own worlds
            var oPublic = new List<PortalWorldInfo>();  // public worlds shared by OTHERS (own ones filtered out)
            var oOperator = new List<PortalWorldInfo>(); // fleet-operator view: EVERY world (#495); empty for normal accounts

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

                // Localizer.Get returns "[key]" on a miss (never the bare key), so ask Has() — comparing
                // against the key could never match and leaked the raw "[ui.portal.err_x]" (#428).
                string key = "ui.portal.err_" + code;
                return shell.Localizer != null && shell.Localizer.Has(key) ? shell.L(key) : error;
            }

            bool SignedIn() => !string.IsNullOrEmpty(shell.Settings.PortalSessionToken);

            void SignOut(bool forgetName = false)
            {
                shell.Settings.PortalSessionToken = "";
                // The account name stays: it prefills the sign-in form with exactly the value the player
                // will need again. Blanking it caused real lockouts — players typed their PLAYER name
                // into the empty field and concluded their account was gone. Forgotten only when the
                // account itself is (deletion).
                if (forgetName)
                {
                    shell.Settings.PortalAccountName = "";
                }

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

                // A ban (or a deleted world) can land while the player is signed in — sessions outlive it by
                // weeks, so the login answer alone would never reach them. Polled here, where the player is
                // looking at their worlds anyway; a failure is silent, the world list matters more (#496).
                var state = await Task.Run(() => portal.GetNotices(session));
                if (official == null) { return; }
                if (state.Ok && portalModal == null)
                {
                    // Only when nothing else is open: the login path already showed these, and stealing a
                    // dialog the player is filling in would be worse than telling them a refresh later.
                    ShowNotices(state);
                }

                // Also pull the public worlds so both lists show in one window. A failure here is non-fatal:
                // the public section just stays empty rather than blocking the player's own worlds.
                var pub = await Task.Run(() => portal.ListPublicWorlds(session));
                if (official == null) { return; }

                // Operator probe (#495): only developer accounts get an answer here — a 403 simply means
                // "not an operator" and the section stays hidden, so this costs normal players nothing.
                var all = await Task.Run(() => portal.ListAllWorldsOperator(session));
                if (official == null) { return; }

                oStatus.text = "";
                oWorlds.Clear();
                oWorlds.AddRange(r.Worlds);
                oPublic.Clear();
                if (pub.Ok)
                {
                    // Hide worlds the player already owns (they appear in "My worlds" above).
                    foreach (var p in pub.Worlds)
                    {
                        if (!oWorlds.Exists(o => o.Id == p.Id))
                        {
                            oPublic.Add(p);
                        }
                    }
                }

                oOperator.Clear();
                if (all.Ok)
                {
                    // The operator list shows OTHER people's worlds — own ones sit in "My worlds" already.
                    foreach (var w in all.Worlds)
                    {
                        if (!oWorlds.Exists(o => o.Id == w.Id))
                        {
                            oOperator.Add(w);
                        }
                    }
                }

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
                shell.Settings.PortalAccountName = r.AccountName.Length > 0 ? r.AccountName : account;
                shell.Settings.Save();
                oStatus.text = "";
                RebuildPortal();
                DoRefresh();
                if (r.MustChangePassword)
                {
                    // An operator handed out a temp password — land the player directly in front of the
                    // change form instead of hoping they find the Account button on their own.
                    OpenAccount(shell.L("ui.portal.must_change_password"));
                }

                if (r.TermsOutdated)
                {
                    PromptReaccept(); // rules changed since the last visit — re-accept in-game (#268)
                }
                else
                {
                    // Being banned used to be invisible here: the login succeeded, the world list loaded,
                    // and the wall only appeared at "create world". Now the login itself carries the state
                    // and the unread messages (#496) — DoRefresh polls too, this is the fast path.
                    ShowNotices(r.State);
                }
            }

            // Dialog for the fleet's messages to this player: the ban screen (reason, since, until, what to
            // do) and the notices behind it — a deleted world leaves no other trace. Shown once per state:
            // acknowledging clears the unread flag server-side, and _banScreenShown keeps the poll from
            // re-opening the ban screen every refresh.
            bool banScreenShown = false;

            void ShowNotices(PortalNoticesResult state)
            {
                if (state == null || (!state.Banned && state.Notices.Count == 0))
                {
                    banScreenShown = false; // unbanned in the meantime — a later ban must show up again
                    return;
                }

                if (state.Banned && banScreenShown && state.Notices.Count == 0)
                {
                    return; // already told them this refresh cycle; the status line keeps the hint visible
                }

                banScreenShown = state.Banned;
                var nDlg = OpenModalPanel(510f, 200f, 900f, 680f);
                bool banned = state.Banned;
                UiKit.AddText(nDlg, 30f, 24f, 840f, 32f,
                    shell.L(banned ? "ui.notice.banned_title" : "ui.notice.title"), 24,
                    banned ? UiKit.Warn : UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

                var body = new System.Text.StringBuilder();
                if (banned)
                {
                    if (state.BannedAtUnix > 0)
                    {
                        body.AppendLine(string.Format(shell.L("ui.notice.banned_since"), LocalDay(state.BannedAtUnix)));
                    }

                    body.AppendLine(state.BannedUntilUnix > 0
                        ? string.Format(shell.L("ui.notice.banned_until"), LocalDay(state.BannedUntilUnix))
                        : shell.L("ui.notice.banned_perm"));
                    string reason = BanReasonText(state.BanReasonCode, state.BanReason);
                    if (reason.Length > 0)
                    {
                        body.AppendLine();
                        body.AppendLine(shell.L("ui.notice.reason") + " " + reason);
                    }

                    body.AppendLine();
                    body.AppendLine(shell.L("ui.notice.appeal"));
                }

                foreach (var n in state.Notices)
                {
                    // The ban itself is already spelled out above — its notice would only repeat it.
                    if (n.Kind == PortalNotice.KindBanned && banned)
                    {
                        continue;
                    }

                    if (body.Length > 0)
                    {
                        body.AppendLine();
                    }

                    switch (n.Kind)
                    {
                        case PortalNotice.KindWorldDeleted:
                            body.AppendLine(string.Format(shell.L("ui.notice.world_deleted"), n.Subject));
                            if (n.Reason.Length > 0)
                            {
                                body.AppendLine(shell.L("ui.notice.reason") + " " + n.Reason);
                            }

                            break;
                        case PortalNotice.KindUnbanned:
                            body.AppendLine(shell.L("ui.notice.unbanned"));
                            break;
                        default:
                            body.AppendLine(BanReasonText(n.ReasonCode, n.Reason));
                            break;
                    }
                }

                var nBody = UiKit.AddText(nDlg, 44f, 76f, 812f, 480f, body.ToString().TrimEnd(), 17, UiKit.TextCol, TextAnchor.UpperLeft);
                nBody.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddButton(nDlg, 280f, 590f, 340f, 54f, shell.L("ui.notice.ok"), () =>
                {
                    DoAckNotices();
                    CloseModal();
                }, "btn_join");
            }

            async void DoAckNotices()
            {
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                await Task.Run(() => portal.AckNotices(session)); // best effort: unread again next time is harmless
            }

            // Canned reason codes are ours and get translated; the operator's own words are shown exactly
            // as written (like every other ban reason in this UI).
            string BanReasonText(string code, string reason)
            {
                string translated = string.Empty;
                if (!string.IsNullOrEmpty(code))
                {
                    string key = "ui.notice.reason_" + code;
                    translated = shell.Localizer != null && shell.Localizer.Has(key) ? shell.L(key) : code;
                }

                if (string.IsNullOrWhiteSpace(reason))
                {
                    return translated;
                }

                return translated.Length > 0 ? translated + " — " + reason : reason;
            }

            static string LocalDay(long unix)
                => System.DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("dd.MM.yyyy");

            // Session-scoped world passwords (#250): entered once per protected world, reused on re-joins,
            // never persisted. The prompt is error-driven — open worlds join without ever seeing it.
            var joinPasswords = new Dictionary<string, string>();

            // Secondary portal dialogs (#268-#270).
            PortalTermsResult cachedTerms = null; // rules text+version change per deployment — fetch once
            string cachedTermsLang = null;        // …but re-fetch after the player switched language (#970)
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
                var mDim = UiKit.AddModalDim(official.transform);
                portalModal = mDim.gameObject;
                return UiKit.AddDialogPanel(portalModal.transform, x, y, w, h);
            }

            async void LoadTerms(System.Action<PortalTermsResult> done)
            {
                string lang = shell.Settings.Language;
                if (cachedTerms != null && cachedTerms.Ok && cachedTermsLang == lang)
                {
                    done(cachedTerms);
                    return;
                }

                var portal = new PortalClient(PortalBase());
                var r = await Task.Run(() => portal.GetTerms(lang));
                if (official == null) { return; }
                cachedTerms = r;
                cachedTermsLang = lang;
                done(r);
            }

            // The in-game community-rules screen (text from GET /api/terms, localized DE/EN). With an
            // accept action it doubles as the consent step for signup and for re-acceptance after a
            // rules change; without one it is a plain viewer.
            void ShowRules(System.Action<PortalTermsResult> onAccept)
            {
                CloseRules();
                var rDim = UiKit.AddModalDim(official.transform);
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
                        // `Text` is the portal's answer in the player's own language (#970); a portal
                        // that predates it leaves the field empty and we fall back to the DE/EN pair.
                        rBody.text = !string.IsNullOrEmpty(terms.Text)
                            ? terms.Text
                            : (shell.Settings.Language == "de" ? terms.TextDe : terms.TextEn);
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
                oStatus.text = "";
                RebuildPortal();
                DoRefresh();
                if (r.RecoveryCodes.Count > 0)
                {
                    ShowRecoveryCodes(r.RecoveryCodes); // replaces the signup dialog; OK closes it
                }
                else
                {
                    CloseModal();
                }
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
                // Deliberately labelled "account name, NOT your player name": the two are different
                // identities (the player name stays freely changeable in the main menu) and mixing
                // them up at the sign-in form later is THE classic lockout.
                UiKit.AddText(sDlg, 40f, 70f, 720f, 22f, shell.L("ui.portal.signup_name"), 15, UiKit.TextCol);
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
                // In-flow pointer to the friends path (password now, list publicly later via Manage) —
                // the Manage dialog is the only other place that explains it, and first-timers skip it.
                var friends = UiKit.AddText(cDlg, 40f, 264f, 720f, 40f, shell.L("ui.portal.create_friends_hint"), 13, UiKit.Cyan, TextAnchor.UpperLeft);
                friends.horizontalOverflow = HorizontalWrapMode.Wrap;
                var cWarn = UiKit.AddText(cDlg, 40f, 310f, 720f, 44f, "", 14, warnCol, TextAnchor.UpperLeft, FontStyle.Bold);
                cWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddButton(cDlg, 40f, 366f, 340f, 54f, shell.L("ui.portal.create"), () =>
                {
                    if (string.IsNullOrWhiteSpace(worldName[0])) { cWarn.text = shell.L("ui.portal.err_world_name_invalid"); return; }
                    if (pw1[0] != pw2[0]) { cWarn.text = shell.L("ui.portal.err_password_mismatch"); return; }
                    DoCreateWorld(worldName[0].Trim(), pw1[0], cWarn);
                }, "btn_join");
                UiKit.AddButton(cDlg, 420f, 366f, 340f, 54f, shell.L("ui.menu.back"), CloseModal, "btn_exit");
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

                // Owner moderation (#497): the world owner's own ban list, the counterpart to the operator's
                // fleet ban. Its own dialog — this one is full, and blocking someone deserves room to read.
                UiKit.AddButton(mDlg, 150f, 700f, 340f, 54f, shell.L("ui.portal.moderation"), () => OpenWorldBans(world), "btn_settings");
                UiKit.AddButton(mDlg, 510f, 700f, 340f, 54f, shell.L("ui.menu.back"), CloseModal, "btn_exit");
            }

            // The owner's ban list for one world: who is blocked (with unblock), and who has played here
            // (with block/kick). Names come from the join grants the portal issued — the client has no
            // other way to know who visited, and typing names by hand is how you ban the wrong kid.
            void OpenWorldBans(PortalWorldInfo world)
            {
                var bDlg = OpenModalPanel(460f, 150f, 1000f, 800f);
                UiKit.AddText(bDlg, 30f, 24f, 940f, 30f, shell.L("ui.portal.bans_title") + " — " + world.Name, 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                var bHint = UiKit.AddText(bDlg, 40f, 62f, 920f, 44f, shell.L("ui.portal.bans_hint"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
                bHint.horizontalOverflow = HorizontalWrapMode.Wrap;
                var bWarn = UiKit.AddText(bDlg, 76f, 110f, 884f, 30f, "", 14, warnCol, TextAnchor.UpperLeft, FontStyle.Bold);
                UiKit.AddSpinner(bDlg, 42f, 110f, 26f, bWarn);
                string[] reason = { "" };
                var listRoot = UiKit.AddPanel(bDlg, 30f, 148f, 940f, 560f, new Color(0f, 0f, 0f, 0f)).transform;
                UiKit.AddButton(bDlg, 330f, 716f, 340f, 54f, shell.L("ui.menu.back"), () => OpenManage(world), "btn_exit");

                async void Load()
                {
                    bWarn.color = warnCol;
                    bWarn.text = shell.L("ui.portal.working");
                    var portal = new PortalClient(PortalBase());
                    string session = shell.Settings.PortalSessionToken;
                    var r = await Task.Run(() => portal.ListWorldBans(session, world.Id));
                    if (official == null || listRoot == null) { return; }
                    if (!r.Ok)
                    {
                        bWarn.text = PortalErr(r.Code, r.Error);
                        return;
                    }

                    bWarn.text = "";
                    for (int i = listRoot.childCount - 1; i >= 0; i--)
                    {
                        Object.Destroy(listRoot.GetChild(i).gameObject);
                    }

                    float y = 0f;
                    UiKit.AddText(listRoot, 10f, y, 900f, 24f, shell.L("ui.portal.bans_title"), 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    y += 30f;
                    if (r.Bans.Count == 0)
                    {
                        UiKit.AddText(listRoot, 10f, y, 900f, 24f, shell.L("ui.portal.bans_none"), 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
                        y += 30f;
                    }

                    foreach (var ban in r.Bans)
                    {
                        long banId = ban.Id;
                        string label = ban.PlayerName + (ban.Reason.Length > 0 ? "  —  " + ban.Reason : string.Empty);
                        UiKit.AddText(listRoot, 10f, y, 700f, 34f, label, 14, UiKit.TextCol, TextAnchor.MiddleLeft);
                        UiKit.AddButton(listRoot, 730f, y, 190f, 34f, shell.L("ui.portal.unblock"), () => DoWorldUnban(world, banId, bWarn, Load), "btn_join");
                        y += 40f;
                    }

                    y += 16f;
                    UiKit.AddText(listRoot, 10f, y, 900f, 24f, shell.L("ui.portal.bans_visitors"), 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    y += 30f;
                    var blocked = new System.Collections.Generic.HashSet<string>();
                    foreach (var ban in r.Bans)
                    {
                        blocked.Add(ban.PlayerName.ToLowerInvariant());
                    }

                    int shown = 0;
                    foreach (var v in r.Visitors)
                    {
                        if (blocked.Contains(v.PlayerName.ToLowerInvariant()) || y > 470f)
                        {
                            continue; // already blocked, or the dialog is full — the list is newest-first
                        }

                        string playerName = v.PlayerName;
                        string accountId = v.AccountId;
                        UiKit.AddText(listRoot, 10f, y, 520f, 34f, playerName, 14, UiKit.TextCol, TextAnchor.MiddleLeft);
                        UiKit.AddButton(listRoot, 540f, y, 180f, 34f, shell.L("ui.portal.kick"),
                            () => DoWorldKick(world, playerName, bWarn), "btn_settings");
                        UiKit.AddButton(listRoot, 730f, y, 190f, 34f, shell.L("ui.portal.block"),
                            () => DoWorldBan(world, playerName, accountId, reason[0], bWarn, Load), "btn_exit");
                        y += 40f;
                        shown++;
                    }

                    if (shown == 0 && r.Visitors.Count == 0)
                    {
                        UiKit.AddText(listRoot, 10f, y, 900f, 24f, shell.L("ui.portal.bans_no_visitors"), 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
                        y += 30f;
                    }

                    y += 16f;
                    UiKit.AddInput(listRoot, 10f, y, 910f, 38f, reason[0], v => reason[0] = v, shell.L("ui.portal.block_reason"));
                }

                Load();
            }

            async void DoWorldBan(PortalWorldInfo world, string playerName, string accountId, string reason, Text warn, System.Action done)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.AddWorldBan(session, world.Id, playerName, accountId, reason ?? ""));
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                // The block always holds; the kick behind it only reaches someone who is in the world right
                // now (#502). Saying "blocked ✓" alone would imply they were thrown out — they weren't.
                warn.color = UiKit.Ok;
                warn.text = r.Kicked
                    ? shell.L("ui.portal.blocked_ok")
                    : shell.L("ui.portal.blocked_ok") + " " + shell.L("ui.portal.kick_not_online");
                done?.Invoke();
            }

            async void DoWorldUnban(PortalWorldInfo world, long banId, Text warn, System.Action done)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.RemoveWorldBan(session, world.Id, banId));
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                warn.text = "";
                done?.Invoke();
            }

            async void DoWorldKick(PortalWorldInfo world, string playerName, Text warn)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.KickFromWorld(session, world.Id, playerName));
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                // A kick that reached nobody is not a success story — the player may be offline, the world
                // asleep, or the instance still on an image without the kick endpoint (#502).
                warn.color = r.Kicked ? UiKit.Ok : warnCol;
                warn.text = shell.L(r.Kicked ? "ui.portal.kicked_ok" : "ui.portal.kick_not_online");
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
                SignOut(forgetName: true); // wipes the (now dead) session + account name and rebuilds the sign-in view
                oStatus.text = shell.L("ui.portal.account_deleted");
            }

            // The one moment rescue-code plaintexts exist (signup / re-issue) — the dialog's whole job
            // is "write these on paper NOW"; the server keeps only hashes and can never show them again.
            void ShowRecoveryCodes(System.Collections.Generic.List<string> codes)
            {
                var cDlg = OpenModalPanel(560f, 220f, 800f, 600f);
                UiKit.AddText(cDlg, 30f, 24f, 740f, 30f, shell.L("ui.portal.codes_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                var hint = UiKit.AddText(cDlg, 40f, 70f, 720f, 110f, shell.L("ui.portal.codes_hint"), 15, UiKit.Warn, TextAnchor.UpperLeft);
                hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                for (int i = 0; i < codes.Count; i++)
                {
                    UiKit.AddText(cDlg, 40f, 196f + i * 66f, 720f, 56f, codes[i], 34, UiKit.Ok, TextAnchor.MiddleCenter, FontStyle.Bold);
                }

                UiKit.AddButton(cDlg, 230f, 430f, 340f, 54f, shell.L("ui.portal.codes_written"), CloseModal, "btn_join");
            }

            async void DoRecover(string accName, string code, string newPw, Text warn)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                var r = await Task.Run(() => portal.Recover(accName, code, newPw));
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    warn.color = warnCol;
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                // A successful rescue IS a sign-in (fresh session; every old session is dead anyway).
                shell.Settings.PortalSessionToken = r.SessionToken;
                shell.Settings.PortalAccountName = r.AccountName.Length > 0 ? r.AccountName : accName;
                shell.Settings.Save();
                CloseModal();
                oStatus.text = shell.L("ui.portal.recovery_done");
                RebuildPortal();
                DoRefresh();
            }

            void OpenRecovery()
            {
                var rDlg = OpenModalPanel(560f, 200f, 800f, 640f);
                UiKit.AddText(rDlg, 30f, 24f, 740f, 30f, shell.L("ui.portal.recovery_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                var hint = UiKit.AddText(rDlg, 40f, 66f, 720f, 66f, shell.L("ui.portal.recovery_hint"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
                hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                string[] accName = { shell.Settings.PortalAccountName };
                string[] code = { "" };
                string[] pw1 = { "" };
                string[] pw2 = { "" };
                UiKit.AddText(rDlg, 40f, 140f, 340f, 22f, shell.L("ui.portal.login_name"), 15, UiKit.TextCol);
                UiKit.AddInput(rDlg, 40f, 164f, 340f, 38f, accName[0], v => accName[0] = v);
                UiKit.AddText(rDlg, 420f, 140f, 340f, 22f, shell.L("ui.portal.recovery_code"), 15, UiKit.TextCol);
                UiKit.AddInput(rDlg, 420f, 164f, 340f, 38f, code[0], v => code[0] = v);
                UiKit.AddText(rDlg, 40f, 218f, 340f, 22f, shell.L("ui.portal.password_new"), 15, UiKit.TextCol);
                var rp1 = UiKit.AddInput(rDlg, 40f, 242f, 340f, 38f, pw1[0], v => pw1[0] = v);
                rp1.contentType = InputField.ContentType.Password;
                UiKit.AddText(rDlg, 420f, 218f, 340f, 22f, shell.L("ui.portal.password_repeat"), 15, UiKit.TextCol);
                var rp2 = UiKit.AddInput(rDlg, 420f, 242f, 340f, 38f, pw2[0], v => pw2[0] = v);
                rp2.contentType = InputField.ContentType.Password;
                var rWarn = UiKit.AddText(rDlg, 40f, 300f, 720f, 44f, "", 14, warnCol, TextAnchor.UpperLeft, FontStyle.Bold);
                rWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddButton(rDlg, 40f, 362f, 340f, 54f, shell.L("ui.portal.recovery_submit"), () =>
                {
                    if (string.IsNullOrWhiteSpace(accName[0]) || string.IsNullOrWhiteSpace(code[0])) { rWarn.text = shell.L("ui.portal.err_recover_failed"); return; }
                    if (pw1[0] != pw2[0]) { rWarn.text = shell.L("ui.portal.err_password_mismatch"); return; }
                    if (pw1[0].Length < 8) { rWarn.text = shell.L("ui.portal.err_password_short"); return; }
                    DoRecover(accName[0].Trim(), code[0].Trim(), pw1[0], rWarn);
                }, "btn_join");
                UiKit.AddButton(rDlg, 420f, 362f, 340f, 54f, shell.L("ui.menu.back"), CloseModal, "btn_exit");
            }

            async void DoRegenCodes(string password, Text warn)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.RegenerateRecoveryCodes(session, password));
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    warn.color = warnCol;
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                ShowRecoveryCodes(r.RecoveryCodes); // replaces the account dialog — the codes are the point now
            }

            async void DoChangePassword(string oldPw, string newPw, Text warn)
            {
                warn.color = warnCol;
                warn.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.ChangePassword(session, oldPw, newPw));
                if (official == null || warn == null) { return; }
                if (!r.Ok)
                {
                    warn.color = warnCol;
                    warn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                warn.color = UiKit.Ok;
                warn.text = shell.L("ui.portal.password_changed");
            }

            void OpenAccount(string notice = null)
            {
                var aDlg = OpenModalPanel(510f, 160f, 900f, 770f);
                UiKit.AddText(aDlg, 30f, 24f, 840f, 30f, shell.L("ui.portal.account_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.AddText(aDlg, 40f, 70f, 820f, 26f,
                    shell.L("ui.portal.signed_in") + " " + shell.Settings.PortalAccountName, 16, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);

                // Password change (the account panel is the one place a signed-in player manages their
                // access): current password required, new one typed twice — same rules as signup.
                UiKit.AddText(aDlg, 40f, 112f, 820f, 24f, shell.L("ui.portal.change_password"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                string[] oldPw = { "" };
                string[] newPw1 = { "" };
                string[] newPw2 = { "" };
                UiKit.AddText(aDlg, 40f, 144f, 260f, 22f, shell.L("ui.portal.password_old"), 14, UiKit.TextCol);
                var apOld = UiKit.AddInput(aDlg, 40f, 168f, 260f, 38f, oldPw[0], v => oldPw[0] = v);
                apOld.contentType = InputField.ContentType.Password;
                UiKit.AddText(aDlg, 320f, 144f, 260f, 22f, shell.L("ui.portal.password_new"), 14, UiKit.TextCol);
                var apNew1 = UiKit.AddInput(aDlg, 320f, 168f, 260f, 38f, newPw1[0], v => newPw1[0] = v);
                apNew1.contentType = InputField.ContentType.Password;
                UiKit.AddText(aDlg, 600f, 144f, 260f, 22f, shell.L("ui.portal.password_repeat"), 14, UiKit.TextCol);
                var apNew2 = UiKit.AddInput(aDlg, 600f, 168f, 260f, 38f, newPw2[0], v => newPw2[0] = v);
                apNew2.contentType = InputField.ContentType.Password;
                var pwWarn = UiKit.AddText(aDlg, 40f, 216f, 820f, 40f, "", 14, warnCol, TextAnchor.UpperLeft, FontStyle.Bold);
                pwWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
                if (!string.IsNullOrEmpty(notice))
                {
                    pwWarn.text = notice; // e.g. "your password was reset — pick your own now" (login flow)
                }

                UiKit.AddButton(aDlg, 40f, 262f, 340f, 46f, shell.L("ui.portal.change_password"), () =>
                {
                    // Same cheap pre-checks as signup, so typos don't burn the shared attempt budget.
                    if (newPw1[0] != newPw2[0]) { pwWarn.color = warnCol; pwWarn.text = shell.L("ui.portal.err_password_mismatch"); return; }
                    if (newPw1[0].Length < 8) { pwWarn.color = warnCol; pwWarn.text = shell.L("ui.portal.err_password_short"); return; }
                    DoChangePassword(oldPw[0], newPw1[0], pwWarn);
                }, "btn_join");

                // Rescue codes are re-issuable, gated on the SAME "current password" field as the change
                // button — the one secret a stolen session does not have.
                UiKit.AddButton(aDlg, 420f, 262f, 340f, 46f, shell.L("ui.portal.codes_regen"), () =>
                {
                    if (oldPw[0].Length == 0) { pwWarn.color = warnCol; pwWarn.text = shell.L("ui.portal.codes_need_password"); return; }
                    DoRegenCodes(oldPw[0], pwWarn);
                }, "btn_credits");

                var delWarn = UiKit.AddText(aDlg, 40f, 336f, 820f, 110f, shell.L("ui.portal.delete_account_warn"), 14, UiKit.Warn, TextAnchor.UpperLeft);
                delWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddText(aDlg, 40f, 458f, 820f, 22f, shell.L("ui.portal.delete_confirm_name"), 14, UiKit.TextCol);
                string[] typed = { "" };
                UiKit.AddInput(aDlg, 40f, 484f, 480f, 38f, typed[0], v => typed[0] = v, shell.Settings.PortalAccountName);
                var aWarn = UiKit.AddText(aDlg, 40f, 542f, 820f, 44f, "", 14, warnCol, TextAnchor.UpperLeft, FontStyle.Bold);
                aWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddButton(aDlg, 40f, 604f, 480f, 50f, shell.L("ui.portal.delete_account"), () =>
                {
                    if (typed[0].Trim() != shell.Settings.PortalAccountName)
                    {
                        aWarn.color = warnCol;
                        aWarn.text = shell.L("ui.portal.err_delete_name_mismatch");
                        return;
                    }

                    DoDeleteAccount(aWarn);
                }, "btn_exit");
                UiKit.AddButton(aDlg, 540f, 604f, 320f, 50f, shell.L("ui.menu.back"), CloseModal, "btn_exit");
            }

            void PromptWorldPassword(string worldId, bool wrongBefore)
            {
                if (passwordPrompt != null)
                {
                    Object.Destroy(passwordPrompt);
                }

                var pwDim = UiKit.AddModalDim(official.transform);
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

                var dim = UiKit.AddModalDim(official.transform);
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
                shell.ArcadeNameToken = ""; // portal join: the browser-local PlayerToken IS the identity
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
                    // First-contact explainer: the hosted-worlds model (own world + password + optional
                    // public listing, no open servers) is not obvious — say it before asking for an account.
                    var intro = UiKit.AddText(oContent, 30f, 18f, 1040f, 48f, shell.L("ui.portal.intro"), 14, UiKit.Cyan, TextAnchor.UpperLeft);
                    intro.horizontalOverflow = HorizontalWrapMode.Wrap;
                    string[] acc = { shell.Settings.PortalAccountName };
                    string[] pw = { "" };
                    UiKit.AddText(oContent, 30f, 76f, 1040f, 22f, shell.L("ui.portal.login_name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                    UiKit.AddInput(oContent, 30f, 102f, 1040f, 38f, acc[0], v => acc[0] = v);
                    // Own key, not ui.menu.connect_password: that one reads as the WORLD password in
                    // this overlay's context — this field asks for the ACCOUNT password.
                    UiKit.AddText(oContent, 30f, 156f, 1040f, 22f, shell.L("ui.portal.login_password"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                    var pwInput = UiKit.AddInput(oContent, 30f, 182f, 1040f, 38f, pw[0], v => pw[0] = v);
                    pwInput.contentType = InputField.ContentType.Password;
                    UiKit.AddButton(oContent, 30f, 240f, 320f, 54f, shell.L("ui.portal.login"), () => DoLogin(acc[0].Trim(), pw[0]), "btn_join");
                    UiKit.AddButton(oContent, 370f, 240f, 320f, 54f, shell.L("ui.portal.signup"), OpenSignup, "btn_credits");
                    UiKit.AddButton(oContent, 710f, 240f, 320f, 54f, shell.L("ui.portal.forgot"), OpenRecovery, "btn_settings");
                    var signupHere = UiKit.AddText(oContent, 30f, 316f, 1040f, 44f, shell.L("ui.portal.signup_here"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
                    signupHere.horizontalOverflow = HorizontalWrapMode.Wrap;
                    UiKit.AddText(oContent, 30f, 366f, 1040f, 24f, PortalBase(), 15, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                    return;
                }

                UiKit.AddText(oContent, 30f, 18f, 640f, 26f,
                    shell.L("ui.portal.signed_in") + " " + shell.Settings.PortalAccountName, 16, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddButton(oContent, 874f, 8f, 195f, 44f, shell.L("ui.portal.logout"), () => SignOut(), "btn_exit");

                // Entry points in a uniform column grid (equal width + gap). Columns are reused by the world
                // rows below: Play sits in the "Feedback" column (663), Manage in the "Konto" column (874),
                // so every button shares a column edge with the row above it. Public worlds are no longer a
                // separate window — they live in the same scroll area below (see "Öffentliche Welten" section).
                const float colW = 195f;
                float[] col = { 30f, 241f, 452f, 663f, 874f };
                UiKit.AddButton(oContent, col[0], 54f, colW, 44f, shell.L("ui.portal.refresh"), DoRefresh, "btn_settings");
                UiKit.AddButton(oContent, col[1], 54f, colW, 44f, shell.L("ui.portal.new_world"), OpenCreateWorld, "btn_join");
                UiKit.AddButton(oContent, col[3], 54f, colW, 44f, shell.L("ui.portal.feedback"), OpenFeedback, "btn_credits");
                UiKit.AddButton(oContent, col[4], 54f, colW, 44f, shell.L("ui.portal.account_btn"), () => OpenAccount(), "btn_settings");

                // Both lists live in ONE scrollable area with section headers + a divider — no second window,
                // and a clear visual split between the player's own worlds and the public ones.
                var scrollGo = new GameObject("WorldsScroll", typeof(RectTransform));
                scrollGo.transform.SetParent(oContent, false);
                UiKit.Place(scrollGo, 0f, 108f, 1076f, 322f);
                var scroll = scrollGo.AddComponent<ScrollRect>();
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 28f;
                scrollGo.AddComponent<RectMask2D>();
                var hit = scrollGo.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0.001f); // catches wheel/drag over empty areas

                var listGo = new GameObject("Content", typeof(RectTransform));
                listGo.transform.SetParent(scrollGo.transform, false);
                var list = listGo.GetComponent<RectTransform>();
                list.anchorMin = new Vector2(0f, 1f);
                list.anchorMax = new Vector2(1f, 1f);
                list.pivot = new Vector2(0.5f, 1f);
                list.anchoredPosition = Vector2.zero;
                scroll.viewport = (RectTransform)scrollGo.transform;
                scroll.content = list;

                float ry = 0f;

                void WorldRow(PortalWorldInfo world, bool owned)
                {
                    var w = world; // capture per row (Play joins; Manage is owner-only)
                    UiKit.AddText(list, 30f, ry + 8f, 380f, 26f, w.Name, 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    string status = owned
                        ? w.Status + (w.HasPassword ? " [PW]" : "") + (w.IsPublic ? " [PUB]" : "")
                        : w.Status + " [PW]"; // public worlds are always password-gated
                    var st = UiKit.AddText(list, 420f, ry + 8f, 220f, 26f, status, 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
                    st.horizontalOverflow = HorizontalWrapMode.Wrap;
                    UiKit.AddButton(list, col[3], ry, colW, 44f, shell.L("ui.portal.play"), () => DoJoinWorld(w.Id), "btn_join");
                    if (owned)
                    {
                        UiKit.AddButton(list, col[4], ry, colW, 44f, shell.L("ui.portal.manage"), () => OpenManage(w));
                    }

                    ry += 52f;
                }

                void SectionHeader(string label)
                {
                    UiKit.AddText(list, 30f, ry, 600f, 26f, label, 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                    ry += 34f;
                }

                void EmptyHint(string label)
                {
                    var t = UiKit.AddText(list, 30f, ry, 1000f, 40f, label, 14, UiKit.CyanDim, TextAnchor.UpperLeft);
                    t.horizontalOverflow = HorizontalWrapMode.Wrap;
                    ry += 40f;
                }

                // Section 1 — the player's own worlds.
                SectionHeader(shell.L("ui.portal.my_worlds"));
                if (oWorlds.Count == 0) { EmptyHint(shell.L("ui.portal.no_worlds")); }
                else { foreach (var world in oWorlds) { WorldRow(world, owned: true); } }

                // Divider between the two sections.
                ry += 12f;
                UiKit.AddImage(list, 30f, ry, 1016f, 2f, UiKit.SolidSprite, new Color(UiKit.Cyan.r, UiKit.Cyan.g, UiKit.Cyan.b, 0.28f));
                ry += 18f;

                // Section 2 — public worlds shared by other players. The intro line always renders:
                // joiners must learn here (not via a failed Play click) that every listed world needs
                // the creator's password.
                SectionHeader(shell.L("ui.portal.public_browse_title"));
                EmptyHint(shell.L("ui.portal.public_intro"));
                if (oPublic.Count == 0) { EmptyHint(shell.L("ui.portal.no_public")); }
                else { foreach (var world in oPublic) { WorldRow(world, owned: false); } }

                // Section 3 — fleet operator only (#495): every world on the fleet, private and
                // password-protected ones included. The list is simply empty for normal accounts (the
                // server answers 403 to the probe), so nothing here ever renders for players. Joining
                // bypasses the world password server-side; the owner name is shown because moderation
                // starts with "whose world is this".
                if (oOperator.Count > 0)
                {
                    ry += 12f;
                    UiKit.AddImage(list, 30f, ry, 1016f, 2f, UiKit.SolidSprite, new Color(UiKit.Warn.r, UiKit.Warn.g, UiKit.Warn.b, 0.35f));
                    ry += 18f;
                    SectionHeader(shell.L("ui.portal.operator_title"));
                    EmptyHint(shell.L("ui.portal.operator_intro"));
                    foreach (var world in oOperator)
                    {
                        var w = world;
                        UiKit.AddText(list, 30f, ry + 8f, 380f, 26f, w.Name, 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                        string flags = w.Status
                            + (w.HasPassword ? " [PW]" : "")
                            + (w.IsPublic ? " [PUB]" : " [PRIV]")
                            + (string.IsNullOrEmpty(w.Owner) ? "" : " · " + w.Owner);
                        var st = UiKit.AddText(list, 420f, ry + 8f, 230f, 26f, flags, 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
                        st.horizontalOverflow = HorizontalWrapMode.Wrap;
                        UiKit.AddButton(list, col[3], ry, colW, 44f, shell.L("ui.portal.play"), () => DoJoinWorld(w.Id), "btn_join");
                        ry += 52f;
                    }
                }

                list.sizeDelta = new Vector2(0f, Mathf.Max(322f, ry + 8f));
                UiKit.AddVerticalScrollbar(oContent, scroll, 1080f, 108f, 14f, 322f);
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
            Para(138f, 70f, "1.  " + shell.L("ui.contribute.feedback").Replace("{feedback_key}", FeedbackUi.HotkeyName), 17, UiKit.Ok);
            Para(212f, 50f, "2.  " + shell.L("ui.contribute.play"), 17, UiKit.TextCol);
            Para(266f, 70f, "3.  " + shell.L("ui.contribute.bugs"), 17, UiKit.TextCol);
            Para(340f, 50f, "4.  " + shell.L("ui.contribute.dev"), 17, UiKit.TextCol);
            // Real buttons, not link-styled text (#544): the GitHub line used to be a dead AddText that
            // merely LOOKED clickable. GitHub + the game website open in the system browser; the website
            // follows the UI language (the German site is the root, English lives under /en).
            UiKit.AddButton(pdlg, 60f, 414f, 380f, 46f, shell.L("ui.contribute.github"),
                () => Application.OpenURL("https://github.com/marceld23/BlocksBeyondTheStars"), "btn_credits");
            UiKit.AddButton(pdlg, 460f, 414f, 280f, 46f, shell.L("ui.contribute.website"),
                () => Application.OpenURL(shell.Settings.Language == "de"
                    ? "https://www.blocksbeyondthestars.com/"
                    : "https://www.blocksbeyondthestars.com/en"), "btn_credits");
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
