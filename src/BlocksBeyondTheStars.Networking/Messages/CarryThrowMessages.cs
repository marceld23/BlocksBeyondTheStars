// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>#3766: the player lifts one block of the selected hotbar stack over the head with both hands
/// (<see cref="Carry"/> true), or puts it back. A stance, not a transfer — the block stays in the stack until it is
/// thrown. The server checks that the slot holds a block and that the player is on foot on a world, and shows the pose
/// to the others (a bit of the presence's gear mask).</summary>
public sealed class CarryBlockIntent
{
    public bool Carry { get; set; }
}

/// <summary>#3766: the player throws the carried block along <see cref="DirX"/>/<see cref="DirY"/>/<see cref="DirZ"/>
/// (the view direction; the server normalises it and flies the block itself). The server takes one block from the
/// selected stack, books a hit on a machine, a robber or a wild animal by the block's material, and lays the block on
/// the ground where it comes down — as an item, never placed.</summary>
public sealed class ThrowBlockIntent
{
    public float DirX { get; set; }
    public float DirY { get; set; }
    public float DirZ { get; set; }
}
