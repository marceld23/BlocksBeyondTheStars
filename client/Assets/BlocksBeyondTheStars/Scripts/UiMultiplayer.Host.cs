// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
#if !UNITY_WEBGL || UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    // The Multiplayer screen's Host tab (native only — a browser cannot run a server for others): hosting lives in
    // "My worlds" since #3664 (pick a world, switch on "Open to friends"), so this tab explains that, leads there, and
    // shows the address friends on the same network will type.
    public sealed partial class UiMultiplayer
    {
        /// <summary>The Host tab's explanation card; the address card fills the rest of the row.</summary>
        public const float HostCardW = 1060f;

        private void BuildHostPane(Transform pane)
        {
            const float x = 40f, w = HostCardW - 80f;
            var card = UiKit.AddCard(pane, UiKit.MenuLeft, PaneTop, HostCardW, JoinCardH);
            UiKit.AddIcon(card, x, 30f, 56f, "btn_host");
            var title = UiKit.AddText(card, x + 76f, 30f, w - 76f, 56f, L("ui.mp.host_title"), 28, UiKit.TextCol,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(title, w - 76f, 18, 28);
            AddStep(card, x, 118f, w, 1, L("ui.mp.host_step1"), L("ui.mp.host_step1_desc"));
            AddStep(card, x, 214f, w, 2, L("ui.mp.host_step2"), L("ui.mp.host_step2_desc"));
            AddStep(card, x, 310f, w, 3, L("ui.mp.host_step3"), L("ui.mp.host_step3_desc"));
            UiKit.AddTierButton(card, x, 440f, 520f, 60f, L("ui.mp.host_go"), _shell.OpenMyWorldsForHosting,
                ButtonTier.Primary, "btn_continue");
            BuildAddressCard(pane);
        }

        // The LAN address for reference (#984): resolved on build from the live interface list, as the host bar of "My
        // worlds" does — so it is the address friends will type once a world is open to them.
        private void BuildAddressCard(Transform pane)
        {
            const float ax = UiKit.MenuLeft + HostCardW + CardGap, aw = UiKit.MenuRight - ax, x = 40f, w = aw - 80f;
            var card = UiKit.AddCard(pane, ax, PaneTop, aw, JoinCardH);
            UiKit.AddKicker(card, x, 24f, w, L("ui.host.your_address"));
            string address = AppShell.LanJoinAddress();
            var shown = UiKit.AddText(card, x, 60f, w, 48f, address, 30, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(shown, w, 16, 30);
            Text hint = null;
            UiKit.AddTierButton(card, x, 124f, 240f, 52f, L("ui.host.copy"), () =>
            {
                GUIUtility.systemCopyBuffer = address;
                // The hint line doubles as the confirmation — a clipboard copy is otherwise invisible.
                hint.text = L("ui.host.copied");
                hint.color = UiKit.Cyan;
            }, ButtonTier.Quiet, "btn_join");
            hint = UiKit.AddText(card, x, 196f, w, 120f, L("ui.mp.host_address_hint"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
        }
    }
}
#endif
