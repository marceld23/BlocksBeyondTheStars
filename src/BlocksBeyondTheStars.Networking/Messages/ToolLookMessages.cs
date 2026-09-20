// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

// Player tool looks (#1963) — modelled on SetBodyPaintIntent / PlayerBodyPaint: the look belongs to the PLAYER,
// the client sends it on join and on change, the server validates, stores it with the player and relays it to
// everyone on the same world. Extended tag ids (#1951).

/// <summary>Client sets (or clears) its own look for one tool.</summary>
public sealed class SetToolLookIntent
{
    /// <summary>The base item key the look is for (<c>titanium_drill</c>) — a tool that is built from parts.</summary>
    public string ItemKey { get; set; } = string.Empty;

    /// <summary>The look (<c>ToolLook</c> payload); empty = back to the standard model.</summary>
    public string Model { get; set; } = string.Empty;
}

/// <summary>Server tells the others what a player's tool looks like (empty model = the standard one again).</summary>
public sealed class PlayerToolLook
{
    public string PlayerId { get; set; } = string.Empty;

    public string ItemKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;
}
