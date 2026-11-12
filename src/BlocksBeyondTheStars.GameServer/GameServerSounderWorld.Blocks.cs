// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3358: a Soaniri block by key for the live changes on a Sounder world (a bell bloom opening, a chamber's seal). The block
/// writing of the three sites' stamp — the palette, the levelling of a round footprint, the walls — is
/// <c>WorldGeneration.SoaniriBuilder</c>'s since #3522.
/// </summary>
public sealed partial class GameServer
{
    private BlockId SounderBlock(string key) => _content.GetBlock(key)?.NumericId ?? BlockId.Air;
}
