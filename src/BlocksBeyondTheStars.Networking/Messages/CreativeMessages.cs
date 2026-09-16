// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client → server (#1930): take an item from the Sandbox "All items" catalog. Honoured only while the player
/// plays the Creative game mode; the count is clamped to the item's stack size.</summary>
public sealed class CreativeTakeItemIntent
{
    public string ItemKey { get; set; } = string.Empty;

    public int Count { get; set; } = 1;
}
