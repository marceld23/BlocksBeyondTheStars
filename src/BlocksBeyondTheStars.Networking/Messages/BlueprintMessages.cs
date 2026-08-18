// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client → server (#1117): copy the placed blocks between two corners (region ≤ 16³) into a
/// build share code. The server reads the cells, serialises them and answers with <see cref="BuildCodeResult"/>.</summary>
public sealed class CopyBuildIntent
{
    public int X1 { get; set; }
    public int Y1 { get; set; }
    public int Z1 { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }
    public int Z2 { get; set; }

    /// <summary>Player-chosen name for the build, embedded in the code (sanitised server-side).</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>Server → client (#1117): the requested build share code, or a localized rejection token.</summary>
public sealed class BuildCodeResult
{
    public bool Success { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Client → server (#1117): paste a build share code with its minimum corner at (X,Y,Z). The
/// server re-validates every cell like a hand-placed block and pays materials from the player's inventory
/// (free in creative/instant-build).</summary>
public sealed class PasteBuildIntent
{
    public string Code { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

/// <summary>Server → client (#1117): what a paste actually did — placed count plus the skip tallies the
/// client folds into one honest toast, and the blueprint author's name for the credit line.</summary>
public sealed class BuildPasteResult
{
    public bool Success { get; set; }
    public int Placed { get; set; }
    public int SkippedMaterials { get; set; }
    public int SkippedProtected { get; set; }
    public int SkippedSpecial { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
