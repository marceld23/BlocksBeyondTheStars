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
    // The Official-worlds overlay's account dialogs: signup, feedback, account deletion, rescue codes and password change.
    internal sealed partial class UiOfficialWorlds
    {
        private async void DoSignup(string accName, string password, int termsVersion, Text warn)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            var r = await Task.Run(() => portal.Signup(accName, password, termsVersion));
            if (_overlay == null || warn == null) { return; } // overlay/dialog closed while the request ran
            if (!r.Ok)
            {
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            _shell.Settings.PortalSessionToken = r.SessionToken; // a signup IS a sign-in (fresh session)
            _shell.Settings.PortalAccountName = accName;
            _shell.Settings.Save();
            _status.text = "";
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

        private void OpenSignup()
        {
            var sDlg = OpenModalPanel(560f, 220f, 800f, 600f);
            UiKit.AddText(sDlg, 30f, 24f, 740f, 30f, _shell.L("ui.portal.signup_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            string[] accName = { "" };
            string[] pw1 = { "" };
            string[] pw2 = { "" };
            bool[] accepted = { false };
            int[] termsVersion = { 0 };
            // Deliberately labelled "account name, NOT your player name": the two are different
            // identities (the player name stays freely changeable in the main menu) and mixing
            // them up at the sign-in form later is THE classic lockout.
            UiKit.AddText(sDlg, 40f, 70f, 720f, 22f, _shell.L("ui.portal.signup_name"), 15, UiKit.TextCol);
            UiKit.AddInput(sDlg, 40f, 94f, 720f, 38f, accName[0], v => accName[0] = v);
            UiKit.AddText(sDlg, 40f, 146f, 340f, 22f, _shell.L("ui.portal.password_label"), 15, UiKit.TextCol);
            var sp1 = UiKit.AddInput(sDlg, 40f, 170f, 340f, 38f, pw1[0], v => pw1[0] = v);
            sp1.contentType = InputField.ContentType.Password;
            UiKit.AddText(sDlg, 420f, 146f, 340f, 22f, _shell.L("ui.portal.password_repeat"), 15, UiKit.TextCol);
            var sp2 = UiKit.AddInput(sDlg, 420f, 170f, 340f, 38f, pw2[0], v => pw2[0] = v);
            sp2.contentType = InputField.ContentType.Password;
            var parents = UiKit.AddText(sDlg, 40f, 224f, 720f, 40f, _shell.L("ui.portal.signup_parents"), 13, UiKit.Warn, TextAnchor.UpperLeft);
            parents.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Consent is deliberate: the rules must be OPENED to accept them (the accept button lives
            // on the rules screen itself), mirroring the portal's required signup checkbox.
            var rulesState = UiKit.AddText(sDlg, 40f, 282f, 360f, 44f, _shell.L("ui.portal.rules_state_open"), 14, WarnCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(sDlg, 420f, 280f, 340f, 46f, _shell.L("ui.portal.rules_read"), () => ShowRules(terms =>
            {
                accepted[0] = true;
                termsVersion[0] = terms.Version;
                CloseRules();
                rulesState.text = _shell.L("ui.portal.rules_state_ok");
                rulesState.color = UiKit.Ok;
            }), "btn_credits");

            var sWarn = UiKit.AddText(sDlg, 40f, 344f, 720f, 44f, "", 14, WarnCol, TextAnchor.UpperLeft, FontStyle.Bold);
            sWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(sDlg, 40f, 406f, 340f, 54f, _shell.L("ui.portal.signup"), () =>
            {
                // Client-side pre-checks mirror the server's cheap rules, so typos don't burn the
                // 5/hour signup rate limit; everything else (blocked/reserved/taken) is the server's call.
                if (string.IsNullOrWhiteSpace(accName[0])) { sWarn.text = _shell.L("ui.portal.err_name_invalid"); return; }
                if (pw1[0] != pw2[0]) { sWarn.text = _shell.L("ui.portal.err_password_mismatch"); return; }
                if (pw1[0].Length < 8) { sWarn.text = _shell.L("ui.portal.err_password_short"); return; }
                if (!accepted[0]) { sWarn.text = _shell.L("ui.portal.err_accept_rules"); return; }
                DoSignup(accName[0].Trim(), pw1[0], termsVersion[0], sWarn);
            }, "btn_join");
            UiKit.AddButton(sDlg, 420f, 406f, 340f, 54f, _shell.L("ui.menu.back"), CloseModal, "btn_exit");
        }

        private async void DoFeedbackSend(string message, Text warn)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            var r = await Task.Run(() => portal.Report(session, "", "feedback", message));
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            warn.color = UiKit.Ok;
            warn.text = _shell.L("ui.portal.feedback_thanks");
        }

        private void OpenFeedback()
        {
            var fDlg = OpenModalPanel(560f, 280f, 800f, 500f);
            UiKit.AddText(fDlg, 30f, 24f, 740f, 30f, _shell.L("ui.portal.feedback_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            var fHint = UiKit.AddText(fDlg, 40f, 66f, 720f, 44f, _shell.L("ui.portal.feedback_hint"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
            fHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            string[] msg = { "" };
            var msgInput = UiKit.AddInput(fDlg, 40f, 120f, 720f, 170f, msg[0], v => msg[0] = v);
            msgInput.lineType = InputField.LineType.MultiLineNewline; // feedback wants sentences, not one line
            msgInput.textComponent.alignment = TextAnchor.UpperLeft;
            var fWarn = UiKit.AddText(fDlg, 40f, 302f, 720f, 40f, "", 14, WarnCol, TextAnchor.UpperLeft, FontStyle.Bold);
            fWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(fDlg, 40f, 356f, 340f, 54f, _shell.L("ui.portal.send"), () =>
            {
                if (string.IsNullOrWhiteSpace(msg[0])) { fWarn.color = WarnCol; fWarn.text = _shell.L("ui.portal.feedback_hint"); return; }
                DoFeedbackSend(msg[0].Trim(), fWarn);
            }, "btn_join");
            UiKit.AddButton(fDlg, 420f, 356f, 340f, 54f, _shell.L("ui.menu.back"), CloseModal, "btn_exit");
        }

        private async void DoDeleteAccount(Text warn)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            var r = await Task.Run(() => portal.DeleteAccount(session));
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            CloseModal();
            SignOut(forgetName: true); // wipes the (now dead) session + account name and rebuilds the sign-in view
            _status.text = _shell.L("ui.portal.account_deleted");
        }

        // The one moment rescue-code plaintexts exist (signup / re-issue) — the dialog's whole job
        // is "write these on paper NOW"; the server keeps only hashes and can never show them again.
        private void ShowRecoveryCodes(System.Collections.Generic.List<string> codes)
        {
            var cDlg = OpenModalPanel(560f, 220f, 800f, 600f);
            UiKit.AddText(cDlg, 30f, 24f, 740f, 30f, _shell.L("ui.portal.codes_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            var hint = UiKit.AddText(cDlg, 40f, 70f, 720f, 110f, _shell.L("ui.portal.codes_hint"), 15, UiKit.Warn, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            for (int i = 0; i < codes.Count; i++)
            {
                UiKit.AddText(cDlg, 40f, 196f + i * 66f, 720f, 56f, codes[i], 34, UiKit.Ok, TextAnchor.MiddleCenter, FontStyle.Bold);
            }

            UiKit.AddButton(cDlg, 230f, 430f, 340f, 54f, _shell.L("ui.portal.codes_written"), CloseModal, "btn_join");
        }

        private async void DoRecover(string accName, string code, string newPw, Text warn)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            var r = await Task.Run(() => portal.Recover(accName, code, newPw));
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                warn.color = WarnCol;
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            // A successful rescue IS a sign-in (fresh session; every old session is dead anyway).
            _shell.Settings.PortalSessionToken = r.SessionToken;
            _shell.Settings.PortalAccountName = r.AccountName.Length > 0 ? r.AccountName : accName;
            _shell.Settings.Save();
            CloseModal();
            _status.text = _shell.L("ui.portal.recovery_done");
            RebuildPortal();
            DoRefresh();
        }

        private void OpenRecovery()
        {
            var rDlg = OpenModalPanel(560f, 200f, 800f, 640f);
            UiKit.AddText(rDlg, 30f, 24f, 740f, 30f, _shell.L("ui.portal.recovery_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            var hint = UiKit.AddText(rDlg, 40f, 66f, 720f, 66f, _shell.L("ui.portal.recovery_hint"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            string[] accName = { _shell.Settings.PortalAccountName };
            string[] code = { "" };
            string[] pw1 = { "" };
            string[] pw2 = { "" };
            UiKit.AddText(rDlg, 40f, 140f, 340f, 22f, _shell.L("ui.portal.login_name"), 15, UiKit.TextCol);
            UiKit.AddInput(rDlg, 40f, 164f, 340f, 38f, accName[0], v => accName[0] = v);
            UiKit.AddText(rDlg, 420f, 140f, 340f, 22f, _shell.L("ui.portal.recovery_code"), 15, UiKit.TextCol);
            UiKit.AddInput(rDlg, 420f, 164f, 340f, 38f, code[0], v => code[0] = v);
            UiKit.AddText(rDlg, 40f, 218f, 340f, 22f, _shell.L("ui.portal.password_new"), 15, UiKit.TextCol);
            var rp1 = UiKit.AddInput(rDlg, 40f, 242f, 340f, 38f, pw1[0], v => pw1[0] = v);
            rp1.contentType = InputField.ContentType.Password;
            UiKit.AddText(rDlg, 420f, 218f, 340f, 22f, _shell.L("ui.portal.password_repeat"), 15, UiKit.TextCol);
            var rp2 = UiKit.AddInput(rDlg, 420f, 242f, 340f, 38f, pw2[0], v => pw2[0] = v);
            rp2.contentType = InputField.ContentType.Password;
            var rWarn = UiKit.AddText(rDlg, 40f, 300f, 720f, 44f, "", 14, WarnCol, TextAnchor.UpperLeft, FontStyle.Bold);
            rWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(rDlg, 40f, 362f, 340f, 54f, _shell.L("ui.portal.recovery_submit"), () =>
            {
                if (string.IsNullOrWhiteSpace(accName[0]) || string.IsNullOrWhiteSpace(code[0])) { rWarn.text = _shell.L("ui.portal.err_recover_failed"); return; }
                if (pw1[0] != pw2[0]) { rWarn.text = _shell.L("ui.portal.err_password_mismatch"); return; }
                if (pw1[0].Length < 8) { rWarn.text = _shell.L("ui.portal.err_password_short"); return; }
                DoRecover(accName[0].Trim(), code[0].Trim(), pw1[0], rWarn);
            }, "btn_join");
            UiKit.AddButton(rDlg, 420f, 362f, 340f, 54f, _shell.L("ui.menu.back"), CloseModal, "btn_exit");
        }

        private async void DoRegenCodes(string password, Text warn)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            var r = await Task.Run(() => portal.RegenerateRecoveryCodes(session, password));
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                warn.color = WarnCol;
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            ShowRecoveryCodes(r.RecoveryCodes); // replaces the account dialog — the codes are the point now
        }

        private async void DoChangePassword(string oldPw, string newPw, Text warn)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            var r = await Task.Run(() => portal.ChangePassword(session, oldPw, newPw));
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                warn.color = WarnCol;
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            warn.color = UiKit.Ok;
            warn.text = _shell.L("ui.portal.password_changed");
        }

        private void OpenAccount(string notice = null)
        {
            var aDlg = OpenModalPanel(510f, 160f, 900f, 770f);
            UiKit.AddText(aDlg, 30f, 24f, 840f, 30f, _shell.L("ui.portal.account_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddText(aDlg, 40f, 70f, 820f, 26f,
                _shell.L("ui.portal.signed_in") + " " + _shell.Settings.PortalAccountName, 16, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Password change (the account panel is the one place a signed-in player manages their
            // access): current password required, new one typed twice — same rules as signup.
            UiKit.AddText(aDlg, 40f, 112f, 820f, 24f, _shell.L("ui.portal.change_password"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            string[] oldPw = { "" };
            string[] newPw1 = { "" };
            string[] newPw2 = { "" };
            UiKit.AddText(aDlg, 40f, 144f, 260f, 22f, _shell.L("ui.portal.password_old"), 14, UiKit.TextCol);
            var apOld = UiKit.AddInput(aDlg, 40f, 168f, 260f, 38f, oldPw[0], v => oldPw[0] = v);
            apOld.contentType = InputField.ContentType.Password;
            UiKit.AddText(aDlg, 320f, 144f, 260f, 22f, _shell.L("ui.portal.password_new"), 14, UiKit.TextCol);
            var apNew1 = UiKit.AddInput(aDlg, 320f, 168f, 260f, 38f, newPw1[0], v => newPw1[0] = v);
            apNew1.contentType = InputField.ContentType.Password;
            UiKit.AddText(aDlg, 600f, 144f, 260f, 22f, _shell.L("ui.portal.password_repeat"), 14, UiKit.TextCol);
            var apNew2 = UiKit.AddInput(aDlg, 600f, 168f, 260f, 38f, newPw2[0], v => newPw2[0] = v);
            apNew2.contentType = InputField.ContentType.Password;
            var pwWarn = UiKit.AddText(aDlg, 40f, 216f, 820f, 40f, "", 14, WarnCol, TextAnchor.UpperLeft, FontStyle.Bold);
            pwWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (!string.IsNullOrEmpty(notice))
            {
                pwWarn.text = notice; // e.g. "your password was reset — pick your own now" (login flow)
            }

            UiKit.AddButton(aDlg, 40f, 262f, 340f, 46f, _shell.L("ui.portal.change_password"), () =>
            {
                // Same cheap pre-checks as signup, so typos don't burn the shared attempt budget.
                if (newPw1[0] != newPw2[0]) { pwWarn.color = WarnCol; pwWarn.text = _shell.L("ui.portal.err_password_mismatch"); return; }
                if (newPw1[0].Length < 8) { pwWarn.color = WarnCol; pwWarn.text = _shell.L("ui.portal.err_password_short"); return; }
                DoChangePassword(oldPw[0], newPw1[0], pwWarn);
            }, "btn_join");

            // Rescue codes are re-issuable, gated on the SAME "current password" field as the change
            // button — the one secret a stolen session does not have.
            UiKit.AddButton(aDlg, 420f, 262f, 340f, 46f, _shell.L("ui.portal.codes_regen"), () =>
            {
                if (oldPw[0].Length == 0) { pwWarn.color = WarnCol; pwWarn.text = _shell.L("ui.portal.codes_need_password"); return; }
                DoRegenCodes(oldPw[0], pwWarn);
            }, "btn_credits");

            var delWarn = UiKit.AddText(aDlg, 40f, 336f, 820f, 110f, _shell.L("ui.portal.delete_account_warn"), 14, UiKit.Warn, TextAnchor.UpperLeft);
            delWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddText(aDlg, 40f, 458f, 820f, 22f, _shell.L("ui.portal.delete_confirm_name"), 14, UiKit.TextCol);
            string[] typed = { "" };
            UiKit.AddInput(aDlg, 40f, 484f, 480f, 38f, typed[0], v => typed[0] = v, _shell.Settings.PortalAccountName);
            var aWarn = UiKit.AddText(aDlg, 40f, 542f, 820f, 44f, "", 14, WarnCol, TextAnchor.UpperLeft, FontStyle.Bold);
            aWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(aDlg, 40f, 604f, 480f, 50f, _shell.L("ui.portal.delete_account"), () =>
            {
                if (typed[0].Trim() != _shell.Settings.PortalAccountName)
                {
                    aWarn.color = WarnCol;
                    aWarn.text = _shell.L("ui.portal.err_delete_name_mismatch");
                    return;
                }

                DoDeleteAccount(aWarn);
            }, "btn_exit");
            UiKit.AddButton(aDlg, 540f, 604f, 320f, 50f, _shell.L("ui.menu.back"), CloseModal, "btn_exit");
        }
    }
}
#endif
