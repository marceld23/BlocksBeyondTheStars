// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// #3676: the state of one guardian giant fight, sent to every player near it — when the move changes, twice a second while
/// it is awake, and once with <see cref="Move"/> 0 (<c>GuardianGiantMove.None</c>) when the fight is over (it lay down or its
/// collapse finished). The giant's position, heading and hull for targeting arrive like every planet machine's in
/// <see cref="PlanetEnemyList"/> (kind <c>GuardianGiant</c>, id <see cref="EntityId"/>); this message adds what only a boss
/// needs: the move and its clock (the client animates from it), the aim point of the current attack (the landing shadow, the
/// beam, the rock), the rage phase, the boss bar's numbers, and the lying pose it rises from or lies down into.
/// </summary>
public sealed class GuardianGiantStatus
{
    /// <summary>The giant's record key (<c>GuardianGiantRecord.SiteId</c>).</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>The awake giant's planet-enemy id; empty when the fight is over.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>The current <c>GuardianGiantMove</c> (byte on the wire).</summary>
    public byte Move { get; set; }

    /// <summary>Seconds since the move started, at <see cref="ServerTime"/>.</summary>
    public float MoveElapsed { get; set; }

    /// <summary>The server clock of this status (the world time the planet-enemy lists carry).</summary>
    public double ServerTime { get; set; }

    /// <summary>The current attack's aim point (world, blocks): the jump's landing, the beam's end, the rock's impact.</summary>
    public float AimX { get; set; }

    /// <summary>See <see cref="AimX"/>.</summary>
    public float AimY { get; set; }

    /// <summary>See <see cref="AimX"/>.</summary>
    public float AimZ { get; set; }

    /// <summary>The rage phase (from half hull on).</summary>
    public bool Rage { get; set; }

    /// <summary>The boss bar: hull left and the full hull.</summary>
    public float Hull { get; set; }

    /// <summary>See <see cref="Hull"/>.</summary>
    public float HullMax { get; set; }

    /// <summary>The lying pose it rises from (<c>Rise</c>) or lies down / falls into (<c>LieDown</c>, <c>Collapse</c>):
    /// the anchor cell and the facing of <c>GuardianGiantBody</c>.</summary>
    public int AnchorX { get; set; }

    /// <summary>See <see cref="AnchorX"/>.</summary>
    public int AnchorY { get; set; }

    /// <summary>See <see cref="AnchorX"/>.</summary>
    public int AnchorZ { get; set; }

    /// <summary>See <see cref="AnchorX"/>.</summary>
    public byte Facing { get; set; }

    /// <summary>The body's seed (<c>GuardianGiantBody.Standing</c>): which plates rusted.</summary>
    public int Seed { get; set; }
}
