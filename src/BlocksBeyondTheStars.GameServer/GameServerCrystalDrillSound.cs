// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3238: the auto-drill's working sound. The clip <c>auto_drill_loop</c> was bundled with the Crystal Net (#2058) and
/// listed in SOUND_DESIGN as "working (loop while it mines)", but nothing ever started it — a drill only thudded per mined
/// block. Now a held, unblocked drill runs the loop through the device-loop path every other machine uses
/// (<c>SetCrystalLoop</c>: one start, one stop, never re-sent per beat, inside the world's cap of 8 playing devices).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The drill's logic beat plus its working loop: on while its network holds it ON and it is not blocked (no
    /// crate beside it, or nothing left to mine), off otherwise.</summary>
    private void AutoDrillBeat(ServerCrystalCell drill, bool held)
    {
        bool working = held && !drill.Output && CrystalNetRules.DrillTierOf(drill.BlockKey) >= 0;
        SetCrystalLoop(drill, working, "auto_drill_loop");
        if (held && _uptime >= drill.NextBeat)
        {
            AutoDrillStep(drill);
        }
    }
}
