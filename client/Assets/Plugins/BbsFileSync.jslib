// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
//
// WebGL persistence bridge: Unity's Application.persistentDataPath lives on an in-memory Emscripten
// FS backed by IndexedDB (IDBFS) — writes only become durable after an explicit FS.syncfs. The
// browser singleplayer calls this after every world-save so the save blob survives a tab close.
mergeInto(LibraryManager.library, {
    BbsSyncIndexedDb: function () {
        if (typeof FS !== 'undefined' && typeof FS.syncfs === 'function') {
            FS.syncfs(false, function (err) {
                if (err) {
                    console.warn('[BBS] IndexedDB sync failed:', err);
                }
            });
        }
    }
});
