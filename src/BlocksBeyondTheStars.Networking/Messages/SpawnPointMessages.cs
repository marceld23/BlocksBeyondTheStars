// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Player presses E on a placed heal tank to make it their custom spawn point (client → server,
/// issue #461). The cell is the aimed heal-tank block; the server validates block + reach and stores
/// a body-qualified spawn that the death flow offers as a respawn option (issue #462).
/// </summary>
public sealed class SetSpawnPointIntent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}
