// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
#if !UNITY_WEBGL || UNITY_EDITOR
using BlocksBeyondTheStars.Client.Portal;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    // The Official-worlds tab's two views (#3665), rebuilt on every state change: signed out — a narrow sign-in card and
    // the "How online play works" card; signed in — the account row and one scrolling list of the player's own worlds,
    // the public ones and (fleet operators only) every world. Coordinates are the menu's 1920×1080 reference space.
    internal sealed partial class UiOfficialWorlds
    {
        /// <summary>The beta banner's height (it sits at <see cref="UiMultiplayer.PaneTop"/>).</summary>
        private const float BannerH = 44f;

        /// <summary>Where the cards under the banner start, and how tall they are (down to the content bottom).</summary>
        private const float CardsTop = UiMultiplayer.PaneTop + BannerH + 20f;
        private const float CardsH = UiKit.MenuContentBottom - CardsTop;

        /// <summary>The sign-in card's width; the explanation card takes the rest of the row.</summary>
        private const float SignInW = 620f, CardGap = 40f;

        private const float SpinnerSize = 26f;

        // World list columns, inside the list's scroll content.
        private const float ColName = 24f, ColNameW = 600f, ColStatus = 650f, ColStatusW = 460f;
        private const float ColPlay = 1150f, ColManage = 1406f, ColBtnW = 240f, RowH = 48f, RowPitch = 60f;

        private static readonly Color DividerCol = new Color(0.40f, 0.82f, 1.00f, 0.28f);

        private void RebuildPortal()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(_content.GetChild(i).gameObject);
            }

            if (!SignedIn())
            {
                BuildSignInCard();
                BuildHowItWorksCard();
                return;
            }

            BuildAccountRow();
            BuildWorldList(CardsTop + RowH + 20f);
            // Signed in, the status (refreshing, waking a world, errors) sits in the footer row under the list.
            PlaceStatus(UiKit.MenuLeft, UiKit.MenuFooterY, UiKit.MenuWidth, UiKit.MenuFooterH);
        }

        // Moves the status line (and its spinner) to where the current view's actions are.
        private void PlaceStatus(float x, float y, float w, float h)
        {
            _spinner.anchoredPosition = new Vector2(x + SpinnerSize / 2f, -(y + 4f + SpinnerSize / 2f));
            UiKit.Place(_status.gameObject, x + SpinnerSize + 10f, y, w - SpinnerSize - 10f, h);
        }

        private void BuildSignInCard()
        {
            const float x = 32f, w = SignInW - 2f * x;
            var card = UiKit.AddCard(_content, UiKit.MenuLeft, CardsTop, SignInW, CardsH);
            UiKit.AddKicker(card, x, 22f, w, _shell.L("ui.portal.login"));
            string[] acc = { _shell.Settings.PortalAccountName };
            string[] pw = { "" };
            // "Account name (not your player name!)": the two are different identities (the player name is chosen on the
            // main menu), and mixing them up at this form is THE classic lockout.
            FieldLabel(card, x, 58f, w, "ui.portal.login_name");
            UiKit.AddInput(card, x, 84f, w, 46f, acc[0], v => acc[0] = v);
            // Own key, not ui.menu.connect_password: that one reads as the WORLD password in this context — this field
            // asks for the ACCOUNT password.
            FieldLabel(card, x, 142f, w, "ui.portal.login_password");
            var pwInput = UiKit.AddInput(card, x, 168f, w, 46f, pw[0], v => pw[0] = v);
            pwInput.contentType = InputField.ContentType.Password;
            UiKit.AddLinkButton(card, x, 220f, 360f, 32f, _shell.L("ui.portal.forgot"), OpenRecovery, 17);
            UiKit.AddTierButton(card, x, 262f, w, 58f, _shell.L("ui.portal.login"), () => DoLogin(acc[0].Trim(), pw[0]),
                ButtonTier.Primary, "btn_continue");
            PlaceStatus(UiKit.MenuLeft + x, CardsTop + 328f, w, 44f); // sign-in errors right under the button

            Divider(card, x, 386f, w, _shell.L("ui.portal.no_account"));
            UiKit.AddTierButton(card, x, 408f, w, 54f, _shell.L("ui.portal.signup"), OpenSignup, ButtonTier.Secondary, "btn_new");
            var note = UiKit.AddText(card, x, 472f, w, 66f, _shell.L("ui.portal.signup_note"), 15, UiKit.CyanDim, TextAnchor.UpperLeft);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            // Which portal this is (a self-hosted WorldHost shows its own address) — small, at the card's foot.
            var url = UiKit.AddText(card, x, CardsH - 40f, w, 24f, PortalBase(), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(url, w, 10, 14);
        }

        // First-contact explainer: the hosted-worlds model (own world + password + optional public listing, no open
        // servers) is not obvious — the three steps say it before asking for an account. Under them the family rules.
        private void BuildHowItWorksCard()
        {
            const float cx = UiKit.MenuLeft + SignInW + CardGap, cw = UiKit.MenuRight - cx, x = 40f, w = cw - 2f * x;
            var card = UiKit.AddCard(_content, cx, CardsTop, cw, CardsH);
            UiKit.AddKicker(card, x, 22f, w, _shell.L("ui.portal.how_title"));
            UiMultiplayer.AddStep(card, x, 66f, w, 1, _shell.L("ui.portal.step_create"), _shell.L("ui.portal.step_create_desc"));
            UiMultiplayer.AddStep(card, x, 166f, w, 2, _shell.L("ui.portal.step_list"), _shell.L("ui.portal.step_list_desc"));
            UiMultiplayer.AddStep(card, x, 266f, w, 3, _shell.L("ui.portal.step_share"), _shell.L("ui.portal.step_share_desc"));
            UiKit.AddImage(card, x, 384f, w, 1.5f, UiKit.SolidSprite, DividerCol);
            var rules = UiKit.AddText(card, x, 404f, w, 52f, _shell.L("ui.portal.rules_line"), 17, UiKit.TextCol, TextAnchor.UpperLeft);
            rules.horizontalOverflow = HorizontalWrapMode.Wrap;
            // The in-game rules screen as a plain viewer (no accept button) — no browser needed.
            UiKit.AddTierButton(card, x, 466f, 320f, 50f, _shell.L("ui.portal.view_rules"), () => ShowRules(null),
                ButtonTier.Quiet, "cat_mission");
        }

        private Text FieldLabel(Transform parent, float x, float y, float w, string key)
        {
            var label = UiKit.AddText(parent, x, y, w, 24f, _shell.L(key), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(label, w, 12, 16);
            return label;
        }

        // A thin line with a centred label ("— No account yet? —") between two groups of a card.
        private static void Divider(Transform parent, float x, float y, float w, string label)
        {
            float labelW = Mathf.Min(w, UiKit.MeasureWidth(label, 15) + 28f);
            float side = Mathf.Max(0f, (w - labelW) / 2f);
            UiKit.AddImage(parent, x, y, side, 1.5f, UiKit.SolidSprite, DividerCol);
            UiKit.AddText(parent, x + side, y - 12f, labelW, 24f, label, 15, UiKit.CyanDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddImage(parent, x + side + labelW, y, side, 1.5f, UiKit.SolidSprite, DividerCol);
        }

        // Signed in: who (with a "Sign out" link), then New world · Refresh · Feedback · Account on the right. New world is
        // the screen's one primary action while the player has no world of their own; afterwards Play on a row is.
        private void BuildAccountRow()
        {
            const float bw = 230f, gap = 16f, bx = UiKit.MenuRight - 4f * bw - 3f * gap;
            const float linkW = 200f, whoW = bx - UiKit.MenuLeft - 2f * linkW - 48f;
            var who = UiKit.AddText(_content, UiKit.MenuLeft, CardsTop, whoW, RowH,
                _shell.L("ui.portal.signed_in") + " " + _shell.Settings.PortalAccountName, 19, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(who, whoW, 13, 19);
            float linkX = UiKit.MenuLeft + Mathf.Min(whoW, UiKit.MeasureWidth(who.text, who.fontSize)) + 24f;
            UiKit.AddLinkButton(_content, linkX, CardsTop + 8f, linkW, 32f, _shell.L("ui.portal.logout"), () => SignOut(), 17);
            // The family rules stay readable once signed in (the signed-out card has the same viewer).
            UiKit.AddLinkButton(_content, linkX + linkW + 8f, CardsTop + 8f, linkW, 32f, _shell.L("ui.portal.view_rules"),
                () => ShowRules(null), 17);

            UiKit.AddTierButton(_content, bx, CardsTop, bw, RowH, _shell.L("ui.portal.new_world"), OpenCreateWorld,
                _ownWorlds.Count == 0 ? ButtonTier.Primary : ButtonTier.Secondary, "btn_new");
            UiKit.AddTierButton(_content, bx + (bw + gap), CardsTop, bw, RowH, _shell.L("ui.portal.refresh"), DoRefresh,
                ButtonTier.Quiet, "sys_comms");
            UiKit.AddTierButton(_content, bx + 2f * (bw + gap), CardsTop, bw, RowH, _shell.L("ui.portal.feedback"), OpenFeedback,
                ButtonTier.Quiet, "btn_feedback");
            UiKit.AddTierButton(_content, bx + 3f * (bw + gap), CardsTop, bw, RowH, _shell.L("ui.portal.account_btn"),
                () => OpenAccount(), ButtonTier.Quiet, "map_player");
        }

        // Both lists live in ONE scrollable card with section headers + a divider — no second window, and a clear split
        // between the player's own worlds and the public ones. The viewport's near-transparent hit graphic catches
        // wheel/drag over empty areas.
        private void BuildWorldList(float top)
        {
            float cardH = UiKit.MenuContentBottom - top, viewH = cardH - 32f, viewW = UiKit.MenuWidth - 54f;
            var card = UiKit.AddCard(_content, UiKit.MenuLeft, top, UiKit.MenuWidth, cardH);
            var list = UiKit.AddScrollViewport(card, "WorldsScroll", 16f, 16f, viewW, viewH, out var scroll);
            float ry = 8f;

            // Section 1 — the player's own worlds. The first row's Play is the primary action (see BuildAccountRow).
            SectionHeader(list, ref ry, _shell.L("ui.portal.my_worlds"));
            if (_ownWorlds.Count == 0) { EmptyHint(list, ref ry, _shell.L("ui.portal.no_worlds")); }
            for (int i = 0; i < _ownWorlds.Count; i++) { WorldRow(list, ref ry, _ownWorlds[i], owned: true, primary: i == 0); }

            // Section 2 — public worlds shared by other players. The intro line always renders: joiners must learn here
            // (not via a failed Play click) that every listed world needs the creator's password.
            SectionDivider(list, ref ry, DividerCol);
            SectionHeader(list, ref ry, _shell.L("ui.portal.public_browse_title"));
            EmptyHint(list, ref ry, _shell.L("ui.portal.public_intro"));
            if (_publicWorlds.Count == 0) { EmptyHint(list, ref ry, _shell.L("ui.portal.no_public")); }
            foreach (var world in _publicWorlds) { WorldRow(list, ref ry, world, owned: false, primary: false); }

            // Section 3 — fleet operator only (#495): every world on the fleet, private and password-protected ones
            // included. The list is simply empty for normal accounts (the server answers 403 to the probe), so nothing
            // here ever renders for players. Joining bypasses the world password server-side; the owner name is shown
            // because moderation starts with "whose world is this".
            if (_operatorWorlds.Count > 0)
            {
                SectionDivider(list, ref ry, new Color(UiKit.Warn.r, UiKit.Warn.g, UiKit.Warn.b, 0.35f));
                SectionHeader(list, ref ry, _shell.L("ui.portal.operator_title"));
                EmptyHint(list, ref ry, _shell.L("ui.portal.operator_intro"));
                foreach (var world in _operatorWorlds) { OperatorRow(list, ref ry, world); }
            }

            list.sizeDelta = new Vector2(0f, Mathf.Max(viewH, ry + 8f));
            UiKit.AddVerticalScrollbar(card, scroll, 16f + viewW + 8f, 16f, 14f, viewH);
        }

        private void WorldRow(Transform list, ref float ry, PortalWorldInfo world, bool owned, bool primary)
        {
            var w = world; // capture per row (Play joins; Manage is owner-only)
            var name = UiKit.AddText(list, ColName, ry + 10f, ColNameW, 28f, w.Name, 19, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(name, ColNameW, 13, 19);
            // #2500: a world whose save the server refused as written by a newer build says "needs a server update" in
            // the warn colour instead of a plain "stopped" (as the web portal does); Play stays enabled.
            string rowStatus = w.StatusLabel(_shell.L);
            string status = owned
                ? rowStatus + (w.HasPassword ? " [PW]" : "") + (w.IsPublic ? " [PUB]" : "")
                : rowStatus + " [PW]"; // public worlds are always password-gated
            var st = UiKit.AddText(list, ColStatus, ry + 10f, ColStatusW, 28f, status, 15, w.NeedsNewerServer ? UiKit.Warn : UiKit.CyanDim,
                TextAnchor.MiddleLeft);
            st.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddTierButton(list, ColPlay, ry, ColBtnW, RowH, _shell.L("ui.portal.play"), () => DoJoinWorld(w.Id),
                primary ? ButtonTier.Primary : ButtonTier.Secondary, "btn_continue");
            if (owned)
            {
                UiKit.AddTierButton(list, ColManage, ry, ColBtnW, RowH, _shell.L("ui.portal.manage"), () => OpenManage(w),
                    ButtonTier.Quiet, "btn_settings");
            }

            ry += RowPitch;
        }

        private void OperatorRow(Transform list, ref float ry, PortalWorldInfo world)
        {
            var w = world;
            var name = UiKit.AddText(list, ColName, ry + 10f, ColNameW, 28f, w.Name, 19, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(name, ColNameW, 13, 19);
            string flags = w.StatusLabel(_shell.L) // #2500: "needs a server update" (warn colour) instead of the status
                + (w.HasPassword ? " [PW]" : "")
                + (w.IsPublic ? " [PUB]" : " [PRIV]")
                + (string.IsNullOrEmpty(w.Owner) ? "" : " · " + w.Owner);
            var st = UiKit.AddText(list, ColStatus, ry + 10f, ColStatusW, 28f, flags, 15, w.NeedsNewerServer ? UiKit.Warn : UiKit.CyanDim,
                TextAnchor.MiddleLeft);
            st.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddTierButton(list, ColPlay, ry, ColBtnW, RowH, _shell.L("ui.portal.play"), () => DoJoinWorld(w.Id),
                ButtonTier.Secondary, "btn_continue");
            ry += RowPitch;
        }

        private static void SectionHeader(Transform list, ref float ry, string label)
        {
            UiKit.AddKicker(list, ColName, ry, 900f, label);
            ry += 34f;
        }

        private static void EmptyHint(Transform list, ref float ry, string label)
        {
            var t = UiKit.AddText(list, ColName, ry, 1560f, 44f, label, 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            ry += 44f;
        }

        private static void SectionDivider(Transform list, ref float ry, Color color)
        {
            ry += 12f;
            UiKit.AddImage(list, ColName, ry, ColManage + ColBtnW - ColName, 2f, UiKit.SolidSprite, color);
            ry += 18f;
        }
    }
}
#endif
