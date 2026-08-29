// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.ReportHost;

// Bug-report inbox ("ReportHost"): receives the game's player feedback (F1) and automatic crash
// reports on the SAME wire contract as the original Wix/Velo endpoint, stores them in SQLite +
// screenshot files, and serves them back through a keyed read API (pull scripts / CI) and a
// Basic-Auth admin UI. Independent of the game/WorldHost deployment — one small container, one
// volume. See docs/developer/REPORT_HOST.md. The routes live in ReportHostApp so the test suite can
// start the same app in-process (#1352); this file only reads the environment and runs it.

var config = ReportHostConfig.FromEnvironment();
using var store = new ReportStore(config);

// Operator push notifications (#938): one fire-and-forget ping per stored report, so the operator no
// longer has to poll the admin UI to learn something arrived. Off by default (empty NOTIFY_URL). Note
// the known double-ping for in-game F1 feedback: those reports arrive twice by design (client-direct
// POST + the server /bump forward) — see ReportDuplicateGroupingTests.
var notifier = new BlocksBeyondTheStars.Shared.Notifications.AdminNotifier(config.NotifyUrl, "reports");

await using var app = ReportHostApp.Create(config, store, notifier, args);
app.Run();
