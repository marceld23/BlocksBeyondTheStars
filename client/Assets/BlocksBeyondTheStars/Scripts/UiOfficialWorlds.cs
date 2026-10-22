// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
#if !UNITY_WEBGL || UNITY_EDITOR
using System.Collections.Generic;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Client.Portal;
using BlocksBeyondTheStars.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The main menu's Official-worlds overlay (native only; HOSTED_WORLDS.md: the browser NEVER picks servers).
    /// Full portal parity (#268-#270): sign up (incl. in-game rules acceptance), sign in, create,
    /// join and manage your hosted worlds, save backups, feedback and account deletion — the web
    /// portal is optional for desktop players. Joining threads the grant through shell.HostedToken.
    /// <para>
    /// Built once per menu by <see cref="UiMainMenu"/>, right after its connect dialog: the constructor adds the
    /// overlay (hidden) to the menu canvas, and <see cref="Overlay"/> is what the menu's button shows. The class is
    /// split by dialog family — this file holds the frame, the session, the notices, joining and the world list;
    /// <c>.Rules.cs</c> the community-rules screen, <c>.Account.cs</c> signup/recovery/feedback/account and
    /// <c>.Worlds.cs</c> creating and managing a world. Every portal request awaits <c>Task.Run</c>, and its
    /// continuation returns early when <see cref="_overlay"/> (or the dialog's warn label) was destroyed meanwhile.
    /// </para>
    /// </summary>
    internal sealed partial class UiOfficialWorlds
    {
        // Secondary portal dialogs (#268-#270).
        private static readonly Color WarnCol = new Color(1f, 0.55f, 0.4f);

        private readonly AppShell _shell;

        // The menu's name gate (true when a player name is set; commits it to the shell and the settings) and the
        // write-back of a replacement name into the menu's name value — the join retry after "name reserved".
        private readonly System.Func<bool> _commitName;
        private readonly System.Action<string> _setMenuName;

        private readonly GameObject _overlay;
        private readonly Text _status;
        private readonly Transform _content;
        private readonly List<PortalWorldInfo> _ownWorlds = new();      // the signed-in account's own worlds
        private readonly List<PortalWorldInfo> _publicWorlds = new();   // public worlds shared by OTHERS (own ones filtered out)
        private readonly List<PortalWorldInfo> _operatorWorlds = new(); // fleet-operator view: EVERY world (#495); empty for normal accounts

        // Session-scoped world passwords (#250): entered once per protected world, reused on re-joins,
        // never persisted. The prompt is error-driven — open worlds join without ever seeing it.
        private readonly Dictionary<string, string> _joinPasswords = new();

        private readonly string _saveBackupDir = System.IO.Path.Combine(AppPaths.Root, "portal_saves");

        // Dialog slots: one for the form dialogs (signup / new world / manage / feedback / account — only one open),
        // one for the rules screen (may overlay the signup form without destroying its typed state),
        // one for the error-driven join-password prompt (#250).
        private GameObject _portalModal;
        private GameObject _rulesModal;
        private GameObject _passwordPrompt;

        private bool _banScreenShown; // see ShowNotices
        private PortalTermsResult _cachedTerms; // rules text+version change per deployment — fetch once
        private string _cachedTermsLang;        // …but re-fetch after the player switched language (#970)

        /// <summary>The overlay's full-screen scrim (hidden until the menu's Official-worlds button shows it).</summary>
        public GameObject Overlay => _overlay;

        /// <param name="commitName">The menu's name gate (<c>CommitName</c>): true when a name is set, warns otherwise.</param>
        /// <param name="setMenuName">Writes a replacement player name back into the menu's name value.</param>
        public UiOfficialWorlds(AppShell shell, Transform root, System.Func<bool> commitName, System.Action<string> setMenuName)
        {
            _shell = shell;
            _commitName = commitName;
            _setMenuName = setMenuName;

            var odim = UiKit.AddModalDim(root);
            _overlay = odim.gameObject;
            // Wide panel (1100) so the world rows have a roomy status column — a "starting…"/"running"
            // status used to sit behind the Play/Manage buttons on the old 700-wide layout.
            var odlg = UiKit.AddDialogPanel(_overlay.transform, 410f, 180f, 1100f, 720f);
            UiKit.AddText(odlg, 30f, 24f, 1040f, 30f, _shell.L("ui.portal.title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            // The same notices the portal shows (client parity): the beta warning, the one-line rules
            // summary, and a button opening the full rules page in the browser. The long notices MUST
            // wrap — AddText defaults to overflow, which ran them under the button and off the panel.
            var betaLine = UiKit.AddText(odlg, 30f, 58f, 1040f, 40f, _shell.L("ui.portal.beta"), 13,
                new Color(1f, 0.72f, 0.35f), TextAnchor.UpperLeft);
            betaLine.horizontalOverflow = HorizontalWrapMode.Wrap;
            var rulesLine = UiKit.AddText(odlg, 30f, 100f, 820f, 44f, _shell.L("ui.portal.rules_line"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
            rulesLine.horizontalOverflow = HorizontalWrapMode.Wrap;
            // Right column (x=874, w=195): "Regeln ansehen" here, "Abmelden"/"Konto"/per-world "Verwalten"
            // below all share this column so the whole right edge lines up.
            UiKit.AddButton(odlg, 874f, 98f, 195f, 44f, _shell.L("ui.portal.view_rules"),
                () => Application.OpenURL(PortalBase() + "/rules"), "btn_credits");

            _status = UiKit.AddText(odlg, 66f, 592f, 1004f, 48f, "", 14,
                new Color(1f, 0.55f, 0.4f), TextAnchor.UpperLeft, FontStyle.Bold);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap; // localized errors can be long
            // Spinner shown automatically while a status message is in progress (its text contains "…").
            UiKit.AddSpinner(odlg, 32f, 590f, 26f, _status);
            UiKit.AddButton(odlg, 415f, 648f, 270f, 54f, _shell.L("ui.menu.back"), () =>
            {
                CloseAllPortalModals(); // leaving the overlay must not park stale dialogs behind it
                _overlay.SetActive(false);
            }, "btn_exit");

            // Content area rebuilt on every state change (signed out ↔ signed in ↔ fresh world list).
            _content = UiKit.AddPanel(odlg, 0f, 150f, 1100f, 440f, new Color(0f, 0f, 0f, 0f)).transform;

            RebuildPortal();
            if (SignedIn())
            {
                DoRefresh(); // stay signed in across launches: populate the list right away
            }

            _overlay.SetActive(false);
        }

        private string PortalBase() => string.IsNullOrWhiteSpace(_shell.Settings.PortalUrl)
            ? PortalClient.DefaultPortalUrl
            : _shell.Settings.PortalUrl;

        // WorldHost errors carry a stable machine code → show the player's language when we have a
        // translation ('ui.portal.err_<code>'); otherwise fall back to the API's English text. Ban
        // reasons are operator-written free text, so 'banned' keeps the original message.
        private string PortalErr(string code, string error)
        {
            if (string.IsNullOrEmpty(code) || code == "banned")
            {
                return error;
            }

            // Localizer.Get returns "[key]" on a miss (never the bare key), so ask Has() — comparing
            // against the key could never match and leaked the raw "[ui.portal.err_x]" (#428).
            string key = "ui.portal.err_" + code;
            return _shell.Localizer != null && _shell.Localizer.Has(key) ? _shell.L(key) : error;
        }

        private bool SignedIn() => !string.IsNullOrEmpty(_shell.Settings.PortalSessionToken);

        private void SignOut(bool forgetName = false)
        {
            _shell.Settings.PortalSessionToken = "";
            // The account name stays: it prefills the sign-in form with exactly the value the player
            // will need again. Blanking it caused real lockouts — players typed their PLAYER name
            // into the empty field and concluded their account was gone. Forgotten only when the
            // account itself is (deletion).
            if (forgetName)
            {
                _shell.Settings.PortalAccountName = "";
            }

            _shell.Settings.Save();
            _status.text = "";
            RebuildPortal();
        }

        private async void DoRefresh()
        {
            _status.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            var r = await Task.Run(() => portal.ListWorlds(session));
            if (_overlay == null) { return; } // menu was torn down while the request ran
            if (!r.Ok)
            {
                if (r.Code == "unauthorized" || r.Error == "unauthorized") { SignOut(); return; } // session expired → back to sign-in
                _status.text = PortalErr(r.Code, r.Error);
                return;
            }

            // A ban (or a deleted world) can land while the player is signed in — sessions outlive it by
            // weeks, so the login answer alone would never reach them. Polled here, where the player is
            // looking at their worlds anyway; a failure is silent, the world list matters more (#496).
            var state = await Task.Run(() => portal.GetNotices(session));
            if (_overlay == null) { return; }
            if (state.Ok && _portalModal == null)
            {
                // Only when nothing else is open: the login path already showed these, and stealing a
                // dialog the player is filling in would be worse than telling them a refresh later.
                ShowNotices(state);
            }

            // Also pull the public worlds so both lists show in one window. A failure here is non-fatal:
            // the public section just stays empty rather than blocking the player's own worlds.
            var pub = await Task.Run(() => portal.ListPublicWorlds(session));
            if (_overlay == null) { return; }

            // Operator probe (#495): only developer accounts get an answer here — a 403 simply means
            // "not an operator" and the section stays hidden, so this costs normal players nothing.
            var all = await Task.Run(() => portal.ListAllWorldsOperator(session));
            if (_overlay == null) { return; }

            _status.text = "";
            _ownWorlds.Clear();
            _ownWorlds.AddRange(r.Worlds);
            _publicWorlds.Clear();
            if (pub.Ok)
            {
                // Hide worlds the player already owns (they appear in "My worlds" above).
                foreach (var p in pub.Worlds)
                {
                    if (!_ownWorlds.Exists(o => o.Id == p.Id))
                    {
                        _publicWorlds.Add(p);
                    }
                }
            }

            _operatorWorlds.Clear();
            if (all.Ok)
            {
                // The operator list shows OTHER people's worlds — own ones sit in "My worlds" already.
                foreach (var w in all.Worlds)
                {
                    if (!_ownWorlds.Exists(o => o.Id == w.Id))
                    {
                        _operatorWorlds.Add(w);
                    }
                }
            }

            RebuildPortal();
        }

        private async void DoLogin(string account, string password)
        {
            _status.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            var r = await Task.Run(() => portal.Login(account, password));
            if (_overlay == null) { return; }
            if (!r.Ok)
            {
                _status.text = r.Code.Length > 0
                    ? PortalErr(r.Code, r.Error)
                    : _shell.L("ui.portal.login_failed") + (r.Error.Length > 0 ? " (" + r.Error + ")" : "");
                return;
            }

            _shell.Settings.PortalSessionToken = r.SessionToken; // session only — the password is never stored
            _shell.Settings.PortalAccountName = r.AccountName.Length > 0 ? r.AccountName : account;
            _shell.Settings.Save();
            _status.text = "";
            RebuildPortal();
            DoRefresh();
            if (r.MustChangePassword)
            {
                // An operator handed out a temp password — land the player directly in front of the
                // change form instead of hoping they find the Account button on their own.
                OpenAccount(_shell.L("ui.portal.must_change_password"));
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
        private void ShowNotices(PortalNoticesResult state)
        {
            if (state == null || (!state.Banned && state.Notices.Count == 0))
            {
                _banScreenShown = false; // unbanned in the meantime — a later ban must show up again
                return;
            }

            if (state.Banned && _banScreenShown && state.Notices.Count == 0)
            {
                return; // already told them this refresh cycle; the status line keeps the hint visible
            }

            _banScreenShown = state.Banned;
            var nDlg = OpenModalPanel(510f, 200f, 900f, 680f);
            bool banned = state.Banned;
            UiKit.AddText(nDlg, 30f, 24f, 840f, 32f,
                _shell.L(banned ? "ui.notice.banned_title" : "ui.notice.title"), 24,
                banned ? UiKit.Warn : UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            var body = new System.Text.StringBuilder();
            if (banned)
            {
                if (state.BannedAtUnix > 0)
                {
                    body.AppendLine(string.Format(_shell.L("ui.notice.banned_since"), LocalDay(state.BannedAtUnix)));
                }

                body.AppendLine(state.BannedUntilUnix > 0
                    ? string.Format(_shell.L("ui.notice.banned_until"), LocalDay(state.BannedUntilUnix))
                    : _shell.L("ui.notice.banned_perm"));
                string reason = BanReasonText(state.BanReasonCode, state.BanReason);
                if (reason.Length > 0)
                {
                    body.AppendLine();
                    body.AppendLine(_shell.L("ui.notice.reason") + " " + reason);
                }

                body.AppendLine();
                body.AppendLine(_shell.L("ui.notice.appeal"));
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
                        body.AppendLine(string.Format(_shell.L("ui.notice.world_deleted"), n.Subject));
                        if (n.Reason.Length > 0)
                        {
                            body.AppendLine(_shell.L("ui.notice.reason") + " " + n.Reason);
                        }

                        break;
                    case PortalNotice.KindUnbanned:
                        body.AppendLine(_shell.L("ui.notice.unbanned"));
                        break;
                    default:
                        body.AppendLine(BanReasonText(n.ReasonCode, n.Reason));
                        break;
                }
            }

            var nBody = UiKit.AddText(nDlg, 44f, 76f, 812f, 480f, body.ToString().TrimEnd(), 17, UiKit.TextCol, TextAnchor.UpperLeft);
            nBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(nDlg, 280f, 590f, 340f, 54f, _shell.L("ui.notice.ok"), () =>
            {
                DoAckNotices();
                CloseModal();
            }, "btn_join");
        }

        private async void DoAckNotices()
        {
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            await Task.Run(() => portal.AckNotices(session)); // best effort: unread again next time is harmless
        }

        // Canned reason codes are ours and get translated; the operator's own words are shown exactly
        // as written (like every other ban reason in this UI).
        private string BanReasonText(string code, string reason)
        {
            string translated = string.Empty;
            if (!string.IsNullOrEmpty(code))
            {
                string key = "ui.notice.reason_" + code;
                translated = _shell.Localizer != null && _shell.Localizer.Has(key) ? _shell.L(key) : code;
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return translated;
            }

            return translated.Length > 0 ? translated + " — " + reason : reason;
        }

        private static string LocalDay(long unix)
            => System.DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("dd.MM.yyyy");

        private void CloseModal()
        {
            if (_portalModal != null)
            {
                Object.Destroy(_portalModal);
                _portalModal = null;
            }
        }

        private void CloseRules()
        {
            if (_rulesModal != null)
            {
                Object.Destroy(_rulesModal);
                _rulesModal = null;
            }
        }

        private void CloseAllPortalModals()
        {
            CloseRules();
            CloseModal();
            if (_passwordPrompt != null)
            {
                Object.Destroy(_passwordPrompt);
                _passwordPrompt = null;
            }
        }

        // Opens a fresh form dialog: full-screen scrim + an OPAQUE dialog panel — the overlay's form
        // must never shine through and blend with the dialog (user acceptance feedback).
        private Transform OpenModalPanel(float x, float y, float w, float h)
        {
            CloseModal();
            var mDim = UiKit.AddModalDim(_overlay.transform);
            _portalModal = mDim.gameObject;
            return UiKit.AddDialogPanel(_portalModal.transform, x, y, w, h);
        }

        private void PromptWorldPassword(string worldId, bool wrongBefore)
        {
            if (_passwordPrompt != null)
            {
                Object.Destroy(_passwordPrompt);
            }

            var pwDim = UiKit.AddModalDim(_overlay.transform);
            _passwordPrompt = pwDim.gameObject;
            var pwDlg = UiKit.AddDialogPanel(_passwordPrompt.transform, 660f, 390f, 600f, 300f);
            UiKit.AddText(pwDlg, 30f, 24f, 540f, 30f, _shell.L("ui.portal.world_password"), 20, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (wrongBefore)
            {
                UiKit.AddText(pwDlg, 30f, 62f, 540f, 24f, _shell.L("ui.portal.err_wrong_password"), 14,
                    new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleCenter, FontStyle.Bold);
            }

            string[] pw = { "" };
            var pwField = UiKit.AddInput(pwDlg, 30f, 100f, 540f, 38f, pw[0], v => pw[0] = v);
            pwField.contentType = InputField.ContentType.Password;
            UiKit.AddButton(pwDlg, 30f, 170f, 260f, 54f, _shell.L("ui.portal.play"), () =>
            {
                if (string.IsNullOrEmpty(pw[0]))
                {
                    return;
                }

                _joinPasswords[worldId] = pw[0];
                Object.Destroy(_passwordPrompt);
                _passwordPrompt = null;
                DoJoinWorld(worldId);
            }, "btn_join");
            UiKit.AddButton(pwDlg, 310f, 170f, 260f, 54f, _shell.L("ui.menu.back"), () =>
            {
                Object.Destroy(_passwordPrompt);
                _passwordPrompt = null;
                _status.text = "";
            }, "btn_exit");
        }

        // Error-driven player-name prompt: the world join was refused because the current player name is
        // reserved/not allowed. Let the player type another name (writing it back to the menu's name field
        // so CommitName picks it up), then retry the same join.
        private void PromptPlayerName(string worldId, string code)
        {
            if (_passwordPrompt != null)
            {
                Object.Destroy(_passwordPrompt);
            }

            var dim = UiKit.AddModalDim(_overlay.transform);
            _passwordPrompt = dim.gameObject;
            var dlg = UiKit.AddDialogPanel(_passwordPrompt.transform, 610f, 350f, 700f, 360f);
            UiKit.AddText(dlg, 30f, 24f, 640f, 30f, _shell.L("ui.menu.connect_name"), 20, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            var hint = UiKit.AddText(dlg, 30f, 64f, 640f, 60f,
                _shell.L(code == "name_reserved" ? "ui.portal.err_name_reserved_hint" : "ui.portal.err_name_blocked_hint"),
                14, new Color(1f, 0.55f, 0.4f), TextAnchor.UpperLeft, FontStyle.Bold);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;

            string[] nm = { string.Empty };
            UiKit.AddInput(dlg, 30f, 132f, 640f, 40f, nm[0], v => nm[0] = v, maxLength: Protocol.MaxPlayerNameLength);
            UiKit.AddButton(dlg, 30f, 192f, 320f, 54f, _shell.L("ui.portal.play"), () =>
            {
                if (string.IsNullOrWhiteSpace(nm[0]))
                {
                    return;
                }

                _setMenuName(nm[0].Trim()); // CommitName (in DoJoinWorld) reads the menu field, not shell.PlayerName
                Object.Destroy(_passwordPrompt);
                _passwordPrompt = null;
                DoJoinWorld(worldId);
            }, "btn_join");
            UiKit.AddButton(dlg, 370f, 192f, 300f, 54f, _shell.L("ui.menu.back"), () =>
            {
                Object.Destroy(_passwordPrompt);
                _passwordPrompt = null;
                _status.text = string.Empty;
            }, "btn_exit");
        }

        private async void DoJoinWorld(string worldId)
        {
            if (!_commitName())
            {
                _status.text = _shell.L("ui.webgl.need_name");
                return;
            }

            _status.text = _shell.L("ui.portal.waking"); // waking a sleeping world can take a moment
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            string playerName = _shell.PlayerName;
            _joinPasswords.TryGetValue(worldId, out string worldPassword);
            var r = await Task.Run(() => portal.JoinWorld(session, worldId, playerName, worldPassword));
            if (_overlay == null) { return; }
            if (!r.Ok)
            {
                if (r.Code == "password_required" || r.Code == "wrong_password")
                {
                    _joinPasswords.Remove(worldId); // a cached one that failed is stale
                    _status.text = "";
                    PromptWorldPassword(worldId, wrongBefore: r.Code == "wrong_password");
                    return;
                }

                // The chosen player name is reserved/not allowed — let the player pick another one right
                // here and retry, instead of a dead-end error they can only fix back on the main menu.
                if (r.Code == "name_reserved" || r.Code == "name_blocked")
                {
                    _status.text = "";
                    PromptPlayerName(worldId, r.Code);
                    return;
                }

                _status.text = PortalErr(r.Code, r.Error);
                return;
            }

            _shell.Host = r.NativeHost;
            _shell.Port = r.NativePort.ToString();
            _shell.Password = "";
            _shell.HostedToken = r.JoinToken; // the grant the server-side token gate verifies
            _shell.HostedWorldId = worldId; // attached to in-game player reports
            _shell.ArcadeNameToken = ""; // portal join: the browser-local PlayerToken IS the identity
            _shell.StartJoin();
        }

        private void RebuildPortal()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(_content.GetChild(i).gameObject);
            }

            if (!SignedIn())
            {
                // First-contact explainer: the hosted-worlds model (own world + password + optional
                // public listing, no open servers) is not obvious — say it before asking for an account.
                var intro = UiKit.AddText(_content, 30f, 18f, 1040f, 48f, _shell.L("ui.portal.intro"), 14, UiKit.Cyan, TextAnchor.UpperLeft);
                intro.horizontalOverflow = HorizontalWrapMode.Wrap;
                string[] acc = { _shell.Settings.PortalAccountName };
                string[] pw = { "" };
                UiKit.AddText(_content, 30f, 76f, 1040f, 22f, _shell.L("ui.portal.login_name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.AddInput(_content, 30f, 102f, 1040f, 38f, acc[0], v => acc[0] = v);
                // Own key, not ui.menu.connect_password: that one reads as the WORLD password in
                // this overlay's context — this field asks for the ACCOUNT password.
                UiKit.AddText(_content, 30f, 156f, 1040f, 22f, _shell.L("ui.portal.login_password"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                var pwInput = UiKit.AddInput(_content, 30f, 182f, 1040f, 38f, pw[0], v => pw[0] = v);
                pwInput.contentType = InputField.ContentType.Password;
                UiKit.AddButton(_content, 30f, 240f, 320f, 54f, _shell.L("ui.portal.login"), () => DoLogin(acc[0].Trim(), pw[0]), "btn_join");
                UiKit.AddButton(_content, 370f, 240f, 320f, 54f, _shell.L("ui.portal.signup"), OpenSignup, "btn_credits");
                UiKit.AddButton(_content, 710f, 240f, 320f, 54f, _shell.L("ui.portal.forgot"), OpenRecovery, "btn_settings");
                var signupHere = UiKit.AddText(_content, 30f, 316f, 1040f, 44f, _shell.L("ui.portal.signup_here"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
                signupHere.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddText(_content, 30f, 366f, 1040f, 24f, PortalBase(), 15, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                return;
            }

            UiKit.AddText(_content, 30f, 18f, 640f, 26f,
                _shell.L("ui.portal.signed_in") + " " + _shell.Settings.PortalAccountName, 16, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(_content, 874f, 8f, 195f, 44f, _shell.L("ui.portal.logout"), () => SignOut(), "btn_exit");

            // Entry points in a uniform column grid (equal width + gap). Columns are reused by the world
            // rows below: Play sits in the "Feedback" column (663), Manage in the "Konto" column (874),
            // so every button shares a column edge with the row above it. Public worlds are no longer a
            // separate window — they live in the same scroll area below (see "Öffentliche Welten" section).
            const float colW = 195f;
            float[] col = { 30f, 241f, 452f, 663f, 874f };
            UiKit.AddButton(_content, col[0], 54f, colW, 44f, _shell.L("ui.portal.refresh"), DoRefresh, "btn_settings");
            UiKit.AddButton(_content, col[1], 54f, colW, 44f, _shell.L("ui.portal.new_world"), OpenCreateWorld, "btn_join");
            UiKit.AddButton(_content, col[3], 54f, colW, 44f, _shell.L("ui.portal.feedback"), OpenFeedback, "btn_credits");
            UiKit.AddButton(_content, col[4], 54f, colW, 44f, _shell.L("ui.portal.account_btn"), () => OpenAccount(), "btn_settings");

            // Both lists live in ONE scrollable area with section headers + a divider — no second window,
            // and a clear visual split between the player's own worlds and the public ones. The viewport's
            // near-transparent hit graphic catches wheel/drag over empty areas.
            var list = UiKit.AddScrollViewport(_content, "WorldsScroll", 0f, 108f, 1076f, 322f, out var scroll);

            float ry = 0f;

            void WorldRow(PortalWorldInfo world, bool owned)
            {
                var w = world; // capture per row (Play joins; Manage is owner-only)
                UiKit.AddText(list, 30f, ry + 8f, 380f, 26f, w.Name, 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                // #2500: a world whose save the server refused as written by a newer build says "needs a server update" in
                // the warn colour instead of a plain "stopped" (as the web portal does); Play stays enabled.
                string rowStatus = w.StatusLabel(_shell.L);
                string status = owned
                    ? rowStatus + (w.HasPassword ? " [PW]" : "") + (w.IsPublic ? " [PUB]" : "")
                    : rowStatus + " [PW]"; // public worlds are always password-gated
                var st = UiKit.AddText(list, 420f, ry + 8f, 220f, 26f, status, 13, w.NeedsNewerServer ? UiKit.Warn : UiKit.CyanDim, TextAnchor.MiddleLeft);
                st.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddButton(list, col[3], ry, colW, 44f, _shell.L("ui.portal.play"), () => DoJoinWorld(w.Id), "btn_join");
                if (owned)
                {
                    UiKit.AddButton(list, col[4], ry, colW, 44f, _shell.L("ui.portal.manage"), () => OpenManage(w));
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
            SectionHeader(_shell.L("ui.portal.my_worlds"));
            if (_ownWorlds.Count == 0) { EmptyHint(_shell.L("ui.portal.no_worlds")); }
            else { foreach (var world in _ownWorlds) { WorldRow(world, owned: true); } }

            // Divider between the two sections.
            ry += 12f;
            UiKit.AddImage(list, 30f, ry, 1016f, 2f, UiKit.SolidSprite, new Color(UiKit.Cyan.r, UiKit.Cyan.g, UiKit.Cyan.b, 0.28f));
            ry += 18f;

            // Section 2 — public worlds shared by other players. The intro line always renders:
            // joiners must learn here (not via a failed Play click) that every listed world needs
            // the creator's password.
            SectionHeader(_shell.L("ui.portal.public_browse_title"));
            EmptyHint(_shell.L("ui.portal.public_intro"));
            if (_publicWorlds.Count == 0) { EmptyHint(_shell.L("ui.portal.no_public")); }
            else { foreach (var world in _publicWorlds) { WorldRow(world, owned: false); } }

            // Section 3 — fleet operator only (#495): every world on the fleet, private and
            // password-protected ones included. The list is simply empty for normal accounts (the
            // server answers 403 to the probe), so nothing here ever renders for players. Joining
            // bypasses the world password server-side; the owner name is shown because moderation
            // starts with "whose world is this".
            if (_operatorWorlds.Count > 0)
            {
                ry += 12f;
                UiKit.AddImage(list, 30f, ry, 1016f, 2f, UiKit.SolidSprite, new Color(UiKit.Warn.r, UiKit.Warn.g, UiKit.Warn.b, 0.35f));
                ry += 18f;
                SectionHeader(_shell.L("ui.portal.operator_title"));
                EmptyHint(_shell.L("ui.portal.operator_intro"));
                foreach (var world in _operatorWorlds)
                {
                    var w = world;
                    UiKit.AddText(list, 30f, ry + 8f, 380f, 26f, w.Name, 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    string flags = w.StatusLabel(_shell.L) // #2500: "needs a server update" (warn colour) instead of the status
                        + (w.HasPassword ? " [PW]" : "")
                        + (w.IsPublic ? " [PUB]" : " [PRIV]")
                        + (string.IsNullOrEmpty(w.Owner) ? "" : " · " + w.Owner);
                    var st = UiKit.AddText(list, 420f, ry + 8f, 230f, 26f, flags, 13, w.NeedsNewerServer ? UiKit.Warn : UiKit.CyanDim, TextAnchor.MiddleLeft);
                    st.horizontalOverflow = HorizontalWrapMode.Wrap;
                    UiKit.AddButton(list, col[3], ry, colW, 44f, _shell.L("ui.portal.play"), () => DoJoinWorld(w.Id), "btn_join");
                    ry += 52f;
                }
            }

            list.sizeDelta = new Vector2(0f, Mathf.Max(322f, ry + 8f));
            UiKit.AddVerticalScrollbar(_content, scroll, 1080f, 108f, 14f, 322f);
        }
    }
}
#endif
