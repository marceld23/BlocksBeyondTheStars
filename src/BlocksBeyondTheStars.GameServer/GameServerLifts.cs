// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lift (#2266): a 3×3 platform that rides a vertical shaft between stops (see docs/developer/MOVING_BLOCKS.md).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A signal (or a press) on a lift stop calls the platform there; on the motor it sends the platform on.</summary>
    private void LiftSignal(ServerCrystalCell c)
    {
        _ = c;
    }
}
