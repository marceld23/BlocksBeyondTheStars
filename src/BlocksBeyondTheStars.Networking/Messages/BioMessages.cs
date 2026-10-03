// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>One running status effect on the wire (#2202), inside <see cref="PlayerStateUpdate"/>. The client counts
/// <see cref="SecondsLeft"/> down by itself between updates.</summary>
public sealed class NetEffect
{
    /// <summary><c>BioEffect</c> as its number.</summary>
    public int Effect { get; set; }
    public int Level { get; set; }
    public float SecondsLeft { get; set; }

    /// <summary><c>BioSideEffect</c> as its number, and its level 0..3.</summary>
    public int Side { get; set; }
    public int SideLevel { get; set; }

    /// <summary><c>BioThermal</c> as its number.</summary>
    public int Thermal { get; set; }
}

/// <summary>
/// One species of the save's register as a player knows it (#2201, #2203). Carries what the client needs to derive the
/// substance profile itself (the seed and the context, or the parents of a cross) — the profile is a pure function in
/// <c>Shared</c>, so nothing about it has to be sent or kept in step.
/// </summary>
public sealed class NetBioSpecies
{
    public uint Seed { get; set; }

    /// <summary><c>BioKind</c> as its number: 0 plant, 1 animal, 2 mineral.</summary>
    public int Kind { get; set; }

    /// <summary>The coined species name; empty for a deposit (its material names it).</summary>
    public string Name { get; set; } = string.Empty;
    public string OriginBodyId { get; set; } = string.Empty;
    public string OriginBodyName { get; set; } = string.Empty;

    // The context of a wild species or a deposit (BioContext). HasContext is false for a cross.
    public bool HasContext { get; set; }
    public ulong Tags { get; set; }
    public int RarityPoints { get; set; }
    public int Toxicity { get; set; }
    public int Carrier { get; set; }

    // The parents of a cross (0 for a wild species). Their entries ride in the same book.
    public uint ParentA { get; set; }
    public uint ParentB { get; set; }
    public bool DifferentWorlds { get; set; }
    public int Generation { get; set; }

    /// <summary>A deposit's material item key.</summary>
    public string MaterialItem { get; set; } = string.Empty;

    /// <summary>A plant's form blocks (empty for anything that is not a single plant) — the lab shows them, and their
    /// presence says a seedling can be made.</summary>
    public string BodyBlock { get; set; } = string.Empty;
    public string CrownBlock { get; set; } = string.Empty;

    /// <summary>An animal a clone tank may grow: never a hostile one, never a giant.</summary>
    public bool Cloneable { get; set; }

    /// <summary>The player has analysed this species in the lab: the full profile may be shown.</summary>
    public bool Analysed { get; set; }
}

/// <summary>
/// Server → client: the player's research book (#2203) — the species they hold samples of or have analysed, and the
/// mixes they have tried. <see cref="Full"/> replaces the client's book (sent on join); otherwise the entries are merged.
/// </summary>
public sealed class BioBook
{
    public bool Full { get; set; }
    public NetBioSpecies[] Species { get; set; } = System.Array.Empty<NetBioSpecies>();

    /// <summary>Signatures of the mixes this player has run (<c>Synthesis.Signature</c>): for these the lab shows the
    /// result before mixing. A mix a detoxifier washed is its own entry (the signature ends in <c>/w</c>).</summary>
    public string[] Reactions { get; set; } = System.Array.Empty<string>();

    /// <summary>Item keys of the tools and gear this player has changed, as "base key|payload" — the Materials chapter.</summary>
    public string[] Changes { get; set; } = System.Array.Empty<string>();
}

/// <summary>Client → server: something done at a bio lab (#2203–#2206, #2209). The player must stand at a lab.</summary>
public sealed class BioLabIntent
{
    public const int Analyse = 0;   // Sample (+ SampleMineral)
    public const int Mix = 1;       // Sample, Carrier, Stabiliser, Modifier
    public const int Change = 2;    // TargetItem, Stabiliser (the mineral sample), CoatingItem
    public const int WashOff = 3;   // TargetItem
    public const int Seedling = 4;  // Sample

    public int Action { get; set; }

    /// <summary>The species seed of the sample (the active substance of a mix, the species to analyse or to raise).</summary>
    public uint Sample { get; set; }

    /// <summary>True when <see cref="Sample"/> names a mineral sample (analyse only).</summary>
    public bool SampleMineral { get; set; }

    /// <summary>The carrier item key of a mix; empty = a plain extract (an injector made with nothing but the sample).</summary>
    public string Carrier { get; set; } = string.Empty;

    /// <summary>The deposit seed of the stabilising mineral sample (0 = none) — and of the material when changing an item.</summary>
    public uint Stabiliser { get; set; }

    /// <summary>Instead of a mineral sample: a plain material with lab traits from the backpack (an ingot, an alloy,
    /// synthesised ore). It has no origin and acts with exactly its fixed traits. Used when <see cref="Stabiliser"/> is 0.</summary>
    public string MaterialItem { get; set; } = string.Empty;

    /// <summary>The species seed of the modifier sample (0 = none).</summary>
    public uint Modifier { get; set; }

    /// <summary>The full item key of the tool or gear to change or wash, as it lies in the backpack.</summary>
    public string TargetItem { get; set; } = string.Empty;

    /// <summary>The full item key of the coating to use when changing an item; empty = none.</summary>
    public string CoatingItem { get; set; } = string.Empty;
}

/// <summary>Server → client: what the lab did.</summary>
public sealed class BioLabResult
{
    public int Action { get; set; }
    public bool Success { get; set; }

    /// <summary>A locale key (without the leading "@") that says what happened or why not.</summary>
    public string MessageKey { get; set; } = string.Empty;

    /// <summary>The item the lab made (a preparation, a seedling, the changed tool); empty when nothing was made.</summary>
    public string ItemKey { get; set; } = string.Empty;

    /// <summary>A mix: its stability 0..100 and whether it fell apart.</summary>
    public int Stability { get; set; }
    public bool Failed { get; set; }

    /// <summary>A mix: true when a detoxifier washed its toxic sample(s) — the result (and the entry in the research book,
    /// <see cref="BioBook.Reactions"/>) is that of the washed mix. An older server never sets it.</summary>
    public bool Washed { get; set; }

    /// <summary>Knowledge gained by a first analysis.</summary>
    public int Knowledge { get; set; }
}
