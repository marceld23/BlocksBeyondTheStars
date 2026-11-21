// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Persistence;

/// <summary>
/// One sound block of the other side (#3907) as it is stored: its location, its world cell, its glow colour (0xRRGGBB — the
/// tone it was shaped with, <c>OtherSideRules.ToneGlow</c>) and the player who shaped it. The sound layer has a table of its
/// own (<c>sound_edit</c>), apart from the real world's block edits: a sound block is never a row of <c>block_edit</c>, so
/// no system that reads the real world can meet one. Also the row of the in-memory repository's snapshot.
/// </summary>
public sealed class StoredSoundCell
{
    public string Planet { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Glow { get; set; }
    public string Owner { get; set; } = string.Empty;
}
