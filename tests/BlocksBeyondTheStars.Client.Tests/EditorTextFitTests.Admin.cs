// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The catalogue: the admin view (#3509, <c>AdminPanelUi*.cs</c>) and the pause menu's admin rows with the "Call an
/// admin" box (<c>AppShell.Admin.cs</c>) in their real boxes — a translation that runs out of its button fails here.</summary>
public sealed partial class EditorTextFitTests
{
    private const float AdminChatW = 1504f - 380f; // AdminPanelUi: ContentW − ChatRight

    private static IEnumerable<FitSite> AdminSites() => new[]
    {
        Lines("Admin view title (640×40)", 640f, 40f, 26, 16, 14, "ui.admin.title"),
        Lines("Admin view mode line (1000×46)", 1000f, 46f, 16, 14, 13, "ui.admin.mode_invisible", "ui.admin.mode_visible"),
        Button("Admin view leave", 300f, 46f, "ui.admin.leave"),
        Button("Admin view close", 160f, 46f, "ui.admin.close"),
        Button("Admin view tabs with a count", 232f, 46f, "ui.admin.tab_players", "ui.admin.tab_log", "ui.admin.tab_chat", "ui.admin.tab_reports")
            with { Suffix = " (88)" },
        Lines("Admin view empty notes (1464×40)", 1464f, 40f, 18, 14, 13, "ui.admin.players_none", "ui.admin.log_none", "ui.admin.reports_none"),
        Button("Admin players: jump", 130f, 46f, "ui.admin.jump"),
        Button("Admin players: follow / stop", 210f, 46f, "ui.admin.follow", "ui.admin.unfollow"),
        Button("Admin players: chat", 120f, 46f, "ui.admin.chat"),
        Button("Admin players: silence / unsilence", 160f, 46f, "ui.admin.silence", "ui.admin.unsilence"),
        Button("Admin players: kick / confirm", 190f, 46f, "ui.admin.kick", "ui.admin.kick_confirm"),
        Button("Admin log: category chips", 236f, 44f, "ui.admin.cat_*"),
        Button("Admin log: jump", 140f, 36f, "ui.admin.jump"),
        Label("Admin chat header (21 px)", AdminChatW, 21, "ui.admin.chat_with", "ui.admin.chat_pick", "ui.admin.chat_none"),
        Lines("Admin chat note (one line)", AdminChatW, 26f, 15, 13, 13, "ui.admin.chat_note"),
        Input("Admin chat field", AdminChatW - 170f - 12f, 18, "ui.admin.chat_placeholder"),
        Button("Admin chat send", 170f, 52f, "ui.admin.send"),
        Button("Admin reports: done / reopen", 180f, 46f, "ui.admin.done", "ui.admin.reopen"),
        Button("Admin reports: jump", 180f, 46f, "ui.admin.jump", "ui.admin.jump_there"),
        Button("Admin reports: chat with reporter", 240f, 46f, "ui.admin.chat_reporter"),
        Button("Admin reports: chat lines", 300f, 40f, "ui.admin.evidence", "ui.admin.evidence_hide"),
        Button("Pause menu: admin rows", 300f, 56f, "ui.pause.admin", "ui.pause.call_admin"),
        Lines("Call an admin: title (584×44)", 584f, 44f, 24, 16, 14, "ui.pause.call_admin_title"),
        Input("Call an admin: field", 584f, 18, "ui.pause.call_admin_placeholder"),
        Button("Call an admin: send", 300f, 52f, "ui.pause.call_admin_send"),
        Button("Call an admin: cancel", 240f, 52f, "ui.pause.call_admin_cancel"),
    };
}
