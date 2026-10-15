// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// CONTRACT STUB (#2639 package, wave 1) — the lodges (#2640) and their residents (#2641). The real implementation lives in
/// <c>GameServerLodges*.cs</c>; whoever writes it deletes this file.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Boot stage "structures", after the bandit camps: stamps this body's lodges once (generation 25+) and
    /// re-derives their instances and residents on every load.</summary>
    private void StampLodges()
    {
    }

    /// <summary>Per-world 1 Hz pass: lodges revealed to players who come near, the residents' store crates.</summary>
    private void TickLodges(double dt)
    {
    }
}
