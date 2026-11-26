// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class GameServer
{
    /// <summary>
    /// #4024: the other side in the <c>/bump</c> snapshot's player block. A report filed over there ("do enemies and NPCs
    /// look at me?") could only be read as one from nearby over-there creatures; this names it directly: whether the
    /// player is over there, whether their client can show it, how they got to the side they are on (the reason of the
    /// last change), the entry gate's world and centre, the spot they crossed from, and their sound blocks.
    /// </summary>
    private object BumpOtherSideSnapshot(PlayerSession s)
    {
        int used = 0, cap = 0;
        FillSoundBlockCount(s, ref used, ref cap);
        var p = s.State;
        bool gate = p.OtherSideGateLocation.Length > 0;
        return new
        {
            over = s.OtherSide,
            clientShowsOtherSide = s.SupportsOtherSide,
            lastChange = s.OtherSideLastChange,
            entryGateWorld = p.OtherSideGateLocation,
            entryGate = gate ? new { x = p.OtherSideGate.X, y = p.OtherSideGate.Y, z = p.OtherSideGate.Z } : null,
            crossedFrom = gate ? new { x = p.OtherSideEntry.X, y = p.OtherSideEntry.Y, z = p.OtherSideEntry.Z } : null,
            soundBlocks = used,
            soundBlockCap = cap,
        };
    }

    /// <summary>The other-side block of a <c>/bump</c> snapshot as JSON (tests).</summary>
    public string BumpOtherSideSnapshotJsonForTest(PlayerSession s) => JsonSerializer.Serialize(BumpOtherSideSnapshot(s));
}
