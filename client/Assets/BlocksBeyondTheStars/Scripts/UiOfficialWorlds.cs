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
    /// The Official-worlds tab of the Multiplayer screen (#3665; native only — HOSTED_WORLDS.md: the browser NEVER picks
    /// servers). Full portal parity (#268-#270): sign up (incl. in-game rules acceptance), sign in, create,
    /// join and manage your hosted worlds, save backups, feedback and account deletion — the web
    /// portal is optional for desktop players. Joining threads the grant through shell.HostedToken.
    /// <para>
    /// Built by <see cref="UiMultiplayer"/> the first time its tab is shown, into that tab's pane: the beta banner across
    /// the content width, then either the sign-in card and the "How online play works" card (signed out) or the account
    /// row and the world list (signed in). Until #3665 it was an overlay with its own scrim over the main menu. Its
    /// dialogs open on the screen's root, so they cover the header and the tab bar too. The class is
    /// split by dialog family — this file holds the frame, the session, the notices and joining; <c>.Views.cs</c> the
    /// signed-out and signed-in views and the world list, <c>.Rules.cs</c> the community-rules screen, <c>.Account.cs</c>
    /// signup/recovery/feedback/account and <c>.Worlds.cs</c> creating and managing a world. Every portal request awaits
    /// <c>Task.Run</c>, and its continuation returns early when <see cref="_pane"/> (or the dialog's warn label) was
    /// destroyed meanwhile — the player left the Multiplayer screen.
    /// </para>
    /// </summary>
    internal sealed partial class UiOfficialWorlds
    {
        // Secondary portal dialogs (#268-#270).
        private static readonly Color WarnCol = new Color(1f, 0.55f, 0.4f);

        private readonly AppShell _shell;

        // The screen's name gate (true when the main menu set a player name; clears an earlier official-worlds grant) and
        // the write-back of a replacement name — the join retry after "name reserved".
        private readonly System.Func<bool> _commitName;
        private readonly System.Action<string> _setMenuName;

        // The tab pane this view lives in (destroyed with the Multiplayer screen) and the screen root the dialogs open on.
        private readonly GameObject _pane;
        private readonly Transform _modalRoot;
        private readonly Text _status;
        private readonly RectTransform _spinner;
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
        private GameObject _stickyModal; // the rescue-codes dialog: Esc must not close it (its codes are shown only once)

        private bool _banScreenShown; // see ShowNotices
        private PortalTermsResult _cachedTerms; // rules text+version change per deployment — fetch once
        private string _cachedTermsLang;        // …but re-fetch after the player switched language (#970)

        /// <param name="screenRoot">The Multiplayer screen's canvas root — the dialogs open there, above everything.</param>
        /// <param name="pane">The tab's pane: the view is built into it, and its destruction ends every pending request.</param>
        /// <param name="commitName">The screen's name gate: true when a player name is set (the caller warns otherwise).</param>
        /// <param name="setMenuName">Makes a replacement player name the player's name (shell + settings).</param>
        public UiOfficialWorlds(AppShell shell, Transform screenRoot, GameObject pane, System.Func<bool> commitName,
            System.Action<string> setMenuName)
        {
            _shell = shell;
            _modalRoot = screenRoot;
            _pane = pane;
            _commitName = commitName;
            _setMenuName = setMenuName;
            Current = this;

            // The same notice the portal shows (client parity): the beta warning, one line across the content width.
            var banner = UiKit.AddCard(pane.transform, UiKit.MenuLeft, UiMultiplayer.PaneTop, UiKit.MenuWidth, BannerH);
            var beta = UiKit.AddText(banner, 24f, 0f, UiKit.MenuWidth - 48f, BannerH, _shell.L("ui.portal.beta"), 17,
                new Color(1f, 0.72f, 0.35f), TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(beta, UiKit.MenuWidth - 48f, 12, 17);

            // Content area rebuilt on every state change (signed out ↔ signed in ↔ fresh world list); the status line
            // after it so it draws on top — each view places it where its actions are (PlaceStatus).
            _content = UiKit.AddStretchChild(pane.transform, "PortalContent").transform;
            _status = UiKit.AddText(pane.transform, 0f, 0f, 100f, 48f, "", 15, WarnCol, TextAnchor.UpperLeft, FontStyle.Bold);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap; // localized errors can be long
            // Spinner shown automatically while a status message is in progress (its text contains "…").
            _spinner = (RectTransform)UiKit.AddSpinner(pane.transform, 0f, 0f, SpinnerSize, _status).transform;

            RebuildPortal();
            if (SignedIn())
            {
                DoRefresh(); // stay signed in across launches: populate the list right away
            }
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
            if (_pane == null) { return; } // menu was torn down while the request ran
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
            if (_pane == null) { return; }
            if (state.Ok && NoPortalDialogOpen())
            {
                // Only when nothing else is open: the login path already showed these, and stealing a
                // dialog the player is filling in would be worse than telling them a refresh later.
                ShowNotices(state);
            }

            // Also pull the public worlds so both lists show in one window. A failure here is non-fatal:
            // the public section just stays empty rather than blocking the player's own worlds.
            var pub = await Task.Run(() => portal.ListPublicWorlds(session));
            if (_pane == null) { return; }

            // Operator probe (#495): only developer accounts get an answer here — a 403 simply means
            // "not an operator" and the section stays hidden, so this costs normal players nothing.
            var all = await Task.Run(() => portal.ListAllWorldsOperator(session));
            if (_pane == null) { return; }

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
            if (_pane == null) { return; }
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
            UiKit.AddTierButton(nDlg, 280f, 590f, 340f, 54f, _shell.L("ui.notice.ok"), () =>
            {
                DoAckNotices();
                CloseModal();
            }, ButtonTier.Primary, "btn_continue");
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

        // No dialog of any of the three slots is open — the gate for the notices a refresh polls: they open in the form
        // slot, on top of whatever is up, so the rules screen and the password/name prompt hold them off too (#2926).
        private bool NoPortalDialogOpen() => _portalModal == null && _rulesModal == null && _passwordPrompt == null;

        /// <summary>The Multiplayer screen's official-worlds tab, while it exists (#3665) — AppShell asks it to close a dialog
        /// before Esc / pad B leaves the whole screen.</summary>
        internal static UiOfficialWorlds Current;

        /// <summary>Closes the topmost open portal dialog — the one drawn last: the rules opened from the signup form sit on
        /// that form — and says whether there was one, so Esc closes the dialog first instead of leaving the screen with it
        /// (#3665). The rescue-codes dialog swallows the Esc without closing: its codes are shown only this once and it
        /// closes only through its "I wrote them down" button.</summary>
        internal static bool CloseTopDialog()
        {
            var tab = Current;
            if (tab == null || tab.NoPortalDialogOpen())
            {
                return false;
            }

            var top = Topmost(tab._passwordPrompt, Topmost(tab._portalModal, tab._rulesModal));
            if (top == null || top == tab._stickyModal)
            {
                return true;
            }

            if (top == tab._passwordPrompt)
            {
                Object.Destroy(tab._passwordPrompt);
                tab._passwordPrompt = null;
            }
            else if (top == tab._portalModal)
            {
                tab.CloseModal();
            }
            else
            {
                tab.CloseRules();
            }

            return true;
        }

        // The one of two dialogs drawn later (higher sibling index under the screen root); null when neither is open.
        private static GameObject Topmost(GameObject a, GameObject b)
        {
            if (a == null || b == null)
            {
                return a != null ? a : b;
            }

            return a.transform.GetSiblingIndex() >= b.transform.GetSiblingIndex() ? a : b;
        }

        // Opens a fresh form dialog: full-screen scrim + an OPAQUE dialog panel — the tab behind it
        // must never shine through and blend with the dialog (user acceptance feedback).
        private Transform OpenModalPanel(float x, float y, float w, float h)
        {
            CloseModal();
            var mDim = UiKit.AddModalDim(_modalRoot);
            _portalModal = mDim.gameObject;
            return UiKit.AddDialogPanel(_portalModal.transform, x, y, w, h);
        }

        private void PromptWorldPassword(string worldId, bool wrongBefore)
        {
            if (_passwordPrompt != null)
            {
                Object.Destroy(_passwordPrompt);
            }

            var pwDim = UiKit.AddModalDim(_modalRoot);
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
            UiKit.AddTierButton(pwDlg, 30f, 170f, 260f, 54f, _shell.L("ui.portal.play"), () =>
            {
                if (string.IsNullOrEmpty(pw[0]))
                {
                    return;
                }

                _joinPasswords[worldId] = pw[0];
                Object.Destroy(_passwordPrompt);
                _passwordPrompt = null;
                DoJoinWorld(worldId);
            }, ButtonTier.Primary, "btn_continue");
            UiKit.AddTierButton(pwDlg, 310f, 170f, 260f, 54f, _shell.L("ui.menu.back"), () =>
            {
                Object.Destroy(_passwordPrompt);
                _passwordPrompt = null;
                _status.text = "";
            }, ButtonTier.Quiet, "btn_back");
        }

        // Error-driven player-name prompt: the world join was refused because the current player name is
        // reserved/not allowed. Let the player type another name (it becomes the player name, so the name gate and
        // the retry use it), then retry the same join.
        private void PromptPlayerName(string worldId, string code)
        {
            if (_passwordPrompt != null)
            {
                Object.Destroy(_passwordPrompt);
            }

            var dim = UiKit.AddModalDim(_modalRoot);
            _passwordPrompt = dim.gameObject;
            var dlg = UiKit.AddDialogPanel(_passwordPrompt.transform, 610f, 350f, 700f, 360f);
            UiKit.AddText(dlg, 30f, 24f, 640f, 30f, _shell.L("ui.menu.connect_name"), 20, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            var hint = UiKit.AddText(dlg, 30f, 64f, 640f, 60f,
                _shell.L(code == "name_reserved" ? "ui.portal.err_name_reserved_hint" : "ui.portal.err_name_blocked_hint"),
                14, new Color(1f, 0.55f, 0.4f), TextAnchor.UpperLeft, FontStyle.Bold);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;

            string[] nm = { string.Empty };
            UiKit.AddInput(dlg, 30f, 132f, 640f, 40f, nm[0], v => nm[0] = v, maxLength: Protocol.MaxPlayerNameLength);
            UiKit.AddTierButton(dlg, 30f, 192f, 320f, 54f, _shell.L("ui.portal.play"), () =>
            {
                if (string.IsNullOrWhiteSpace(nm[0]))
                {
                    return;
                }

                _setMenuName(nm[0].Trim()); // the shell's PlayerName — what the name gate and the join (DoJoinWorld) read
                Object.Destroy(_passwordPrompt);
                _passwordPrompt = null;
                DoJoinWorld(worldId);
            }, ButtonTier.Primary, "btn_continue");
            UiKit.AddTierButton(dlg, 370f, 192f, 300f, 54f, _shell.L("ui.menu.back"), () =>
            {
                Object.Destroy(_passwordPrompt);
                _passwordPrompt = null;
                _status.text = string.Empty;
            }, ButtonTier.Quiet, "btn_back");
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
            if (_pane == null) { return; }
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
    }
}
#endif
