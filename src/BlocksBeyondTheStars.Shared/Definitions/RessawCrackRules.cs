// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// #3336: Ressaw blocks crack and leak when a player lingers next to them (Justus' "Alte Städte" report: "If you stand next
/// to the glowing white blocks called Ressaw blocks for too long, they get cracks and the glowing white liquid Ressaw runs
/// out"). A <see cref="BlockKey"/> cell a player stays near for <see cref="CrackSeconds"/> turns into
/// <see cref="CrackedKey"/>; staying on for <see cref="LeakSeconds"/> more breaks it — the cell becomes the liquid
/// <see cref="LiquidKey"/>, and the Ressaw flows out of it (#3433, maintainer decision 2026-11-08 — it used to appear as a
/// fixed still puddle): one step every <see cref="FlowSeconds"/>, sideways across the floor in a fixed face order and down
/// wherever there is air below, at most <see cref="MaxPuddleCells"/> cells besides the broken one. The generated Ressaw
/// pools of the ruins stay still. The server runs it (<c>GameServerRessawBlocks.cs</c>); these are its numbers.
/// </summary>
public static class RessawCrackRules
{
    /// <summary>The intact Ressaw block.</summary>
    public const string BlockKey = "ressaw_block";

    /// <summary>The cracked stage.</summary>
    public const string CrackedKey = "ressaw_block_cracked";

    /// <summary>The still liquid a broken block leaks.</summary>
    public const string LiquidKey = "ressaw";

    /// <summary>The sound cue when a block cracks (a clip in the client's audio bank).</summary>
    public const string CrackSound = "ressaw_crack";

    /// <summary>The sound cue when a cracked block breaks and leaks.</summary>
    public const string LeakSound = "ressaw_leak";

    /// <summary>Seconds between two looks at the cells around the players — slow on purpose: a crack takes seconds.</summary>
    public const double BeatSeconds = 0.5;

    /// <summary>Seconds a player must stay near an intact block, without a break, before it cracks. Walking past takes about a
    /// second, so it never cracks anything.</summary>
    public const double CrackSeconds = 5.0;

    /// <summary>Seconds a player must stay on near a cracked block before it breaks and leaks.</summary>
    public const double LeakSeconds = 4.0;

    /// <summary>"Near": at most this many cells away from the player's feet column, sideways (a 5 × 5 square).</summary>
    public const int Reach = 2;

    /// <summary>"Near", up and down: from the floor under the feet (−1) to the cell above the head (+2).</summary>
    public const int ReachBelow = 1;

    /// <summary>See <see cref="ReachBelow"/>.</summary>
    public const int ReachAbove = 2;

    /// <summary>The most cells one leak pours out besides the broken block's own cell.</summary>
    public const int MaxPuddleCells = 5;

    /// <summary>#3433: seconds between two steps of a leak's flow — a cell further across the floor, or one cell further
    /// down a fall, per step.</summary>
    public const double FlowSeconds = 0.25;

    /// <summary>#3433: the most steps a leak flows (16 s). A drop still falling then — down a bottomless shaft, or above
    /// somebody who never steps away — runs dry.</summary>
    public const int MaxLeakSteps = 64;

    /// <summary>The most cracks and leaks one world makes per beat — a player beside a whole Ressaw wall cracks it a few
    /// blocks at a time, not in one burst of edits. The rest keep their time and follow on the next beats.</summary>
    public const int MaxChangesPerBeat = 4;

    /// <summary>The order a leak spreads in across a floor: the four sideways faces, never up (it falls down on its own).</summary>
    public static readonly Vector3i[] SpillFaces =
    {
        new(1, 0, 0),
        new(-1, 0, 0),
        new(0, 0, 1),
        new(0, 0, -1),
    };
}
