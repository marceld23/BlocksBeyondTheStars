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
    // The Official-worlds overlay's community-rules screen: loading the rules, showing them, and (re-)accepting them.
    internal sealed partial class UiOfficialWorlds
    {
        private async void LoadTerms(System.Action<PortalTermsResult> done)
        {
            string lang = _shell.Settings.Language;
            if (_cachedTerms != null && _cachedTerms.Ok && _cachedTermsLang == lang)
            {
                done(_cachedTerms);
                return;
            }

            var portal = new PortalClient(PortalBase());
            var r = await Task.Run(() => portal.GetTerms(lang));
            if (_overlay == null) { return; }
            _cachedTerms = r;
            _cachedTermsLang = lang;
            done(r);
        }

        // The in-game community-rules screen (text from GET /api/terms, localized DE/EN). With an
        // accept action it doubles as the consent step for signup and for re-acceptance after a
        // rules change; without one it is a plain viewer.
        private void ShowRules(System.Action<PortalTermsResult> onAccept)
        {
            CloseRules();
            var rDim = UiKit.AddModalDim(_overlay.transform);
            _rulesModal = rDim.gameObject;
            var rDlg = UiKit.AddDialogPanel(_rulesModal.transform, 160f, 80f, 1600f, 920f);
            var rTitle = UiKit.AddText(rDlg, 40f, 22f, 1520f, 32f, _shell.L("ui.portal.rules_title"), 24, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            var rBody = UiKit.AddText(rDlg, 60f, 76f, 1480f, 730f, "", 17, UiKit.TextCol, TextAnchor.UpperLeft);
            rBody.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Fetches (or re-fetches) the rules into the body — a friendly message instead of a raw
            // "http_404" when the portal is unreachable or too old to know /api/terms yet.
            void ApplyTerms()
            {
                rBody.text = _shell.L("ui.portal.working");
                LoadTerms(terms =>
                {
                    if (_rulesModal == null) { return; }
                    if (!terms.Ok)
                    {
                        string msg = PortalErr(terms.Code, terms.Error);
                        rBody.text = msg.StartsWith("http_", System.StringComparison.Ordinal) ? _shell.L("ui.portal.err_offline") : msg;
                        return;
                    }

                    rTitle.text = _shell.L("ui.portal.rules_title") + " (v" + terms.Version + ")";
                    // `Text` is the portal's answer in the player's own language (#970); a portal
                    // that predates it leaves the field empty and we fall back to the DE/EN pair.
                    rBody.text = !string.IsNullOrEmpty(terms.Text)
                        ? terms.Text
                        : (_shell.Settings.Language == "de" ? terms.TextDe : terms.TextEn);
                });
            }

            if (onAccept != null)
            {
                UiKit.AddButton(rDlg, 430f, 836f, 380f, 54f, _shell.L("ui.portal.rules_accept"), () =>
                {
                    if (_cachedTerms != null && _cachedTerms.Ok)
                    {
                        onAccept(_cachedTerms);
                    }
                    else
                    {
                        ApplyTerms(); // nothing loaded to consent to — retry instead of ignoring the click
                    }
                }, "btn_join");
            }

            UiKit.AddButton(rDlg, 830f, 836f, 340f, 54f, _shell.L("ui.menu.back"), CloseRules, "btn_exit");
            ApplyTerms();
        }

        private async void DoAcceptTerms()
        {
            _status.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            var r = await Task.Run(() => portal.AcceptTerms(session));
            if (_overlay == null) { return; }
            CloseRules();
            if (!r.Ok)
            {
                _status.text = PortalErr(r.Code, r.Error);
                return;
            }

            _status.text = "";
            DoRefresh();
        }

        // The rules changed since this account accepted them (terms_outdated): world actions are
        // blocked until the player re-reads and re-accepts — same flow as the portal's login gate.
        private void PromptReaccept()
        {
            _status.text = _shell.L("ui.portal.terms_outdated");
            ShowRules(_ => DoAcceptTerms());
        }
    }
}
#endif
