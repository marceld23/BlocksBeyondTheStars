// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// Everything salvage in space pays out (#2352, <c>data/space_salvage.json</c>): the debris fields — their size, the
/// shield taps, their themes with fragment and capsule loot — the combat debris a destroyed hostile leaves, and the
/// space wreck's payout that used to be a hard-coded method. Optional content: a data folder without the file gets
/// these defaults, which match the numbers that shipped before the file existed.
/// </summary>
public sealed class SpaceSalvageDefinition
{
    public DebrisFieldSettings Fields { get; set; } = new();

    /// <summary>Theme key → theme. A field rolls one theme from the weights (the system archetype tilts them).</summary>
    public Dictionary<string, DebrisThemeDefinition> Themes { get; set; } = new();

    public CombatDebrisDefinition CombatDebris { get; set; } = new();

    public WreckSalvageDefinition Wreck { get; set; } = new();
}

/// <summary>The debris field itself: how many pieces, how wide, and how the drifting rubble taps the shield.</summary>
public sealed class DebrisFieldSettings
{
    public int FragmentsMin { get; set; } = 8;
    public int FragmentsMax { get; set; } = 12;
    public int CapsulesMin { get; set; } = 1;
    public int CapsulesMax { get; set; } = 2;

    /// <summary>Flight units: the fragments scatter inside this radius around the field's centre.</summary>
    public float Radius { get; set; } = 28f;

    /// <summary>Flight units within which a pilot "visits" the field — the flight recorder is read on approach.</summary>
    public float ApproachRange { get; set; } = 45f;

    /// <summary>Shield points one tap of drifting debris takes. Shield only — the hull is never touched.</summary>
    public float BumpShield { get; set; } = 3f;

    /// <summary>Seconds between two taps on the same pilot while they fly inside the field.</summary>
    public double BumpIntervalSeconds { get; set; } = 7.0;

    /// <summary>Flight units per second a ship must at least move for the rubble to tap it — a parked ship is left alone.</summary>
    public float BumpMinSpeed { get; set; } = 0.5f;

    /// <summary>System archetype name → multiplier on the world option's odds that a system holds a field
    /// (Belt / Desolate / PirateHaven more, Hub less; absent = 1).</summary>
    public Dictionary<string, double> ArchetypeOdds { get; set; } = new()
    {
        ["Belt"] = 1.5,
        ["Desolate"] = 1.5,
        ["PirateHaven"] = 2.0,
        ["Hub"] = 0.5,
    };
}

/// <summary>One kind of debris field: what it was before it broke, which blocks its pieces are, what they pay.</summary>
public sealed class DebrisThemeDefinition
{
    /// <summary>Roll weight among the themes (0 = never).</summary>
    public int Weight { get; set; } = 1;

    /// <summary>System archetype name → extra weight multiplier for this theme (absent = 1).</summary>
    public Dictionary<string, double> ArchetypeWeights { get; set; } = new();

    /// <summary>The fragments' main block (plating), the second block mixed in, and the scorch block.</summary>
    public string Hull { get; set; } = "iron_wall";
    public string Accent { get; set; } = "steel_wall";
    public string Scorch { get; set; } = "carbon";

    /// <summary>Share of fragment cells that are scorched (0..1).</summary>
    public double ScorchShare { get; set; } = 0.3;

    /// <summary>What one carved fragment pays.</summary>
    public List<SalvageRoll> FragmentLoot { get; set; } = new();

    /// <summary>What one salvage capsule holds (rolled once per capsule, paid once per galaxy).</summary>
    public List<SalvageRoll> CapsuleLoot { get; set; } = new();

    /// <summary>The lore site key the flight recorder reveals a text of (<c>lore_sites.json</c>).</summary>
    public string Lore { get; set; } = "debris";
}

/// <summary>One loot row: <see cref="Chance"/> to appear at all, then a count in <see cref="Min"/>..<see cref="Max"/>.</summary>
public sealed class SalvageRoll
{
    public string Item { get; set; } = string.Empty;
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 1;
    public double Chance { get; set; } = 1.0;
}

/// <summary>What a destroyed drone, saucer, cruiser or raider leaves behind.</summary>
public sealed class CombatDebrisDefinition
{
    public int FragmentsMin { get; set; } = 2;
    public int FragmentsMax { get; set; } = 3;

    /// <summary>At most this many combat fragments per flight instance — the oldest goes when the cap is hit.</summary>
    public int Cap { get; set; } = 12;

    public List<SalvageRoll> Loot { get; set; } = new()
    {
        new SalvageRoll { Item = "scrap_metal", Min = 1, Max = 2 },
        new SalvageRoll { Item = "iron_plate", Min = 1, Max = 1, Chance = 0.4 },
    };
}

/// <summary>The space wreck's payout once it is salvaged down to nothing, by hull origin.</summary>
public sealed class WreckSalvageDefinition
{
    public List<ScaledSalvageRoll> Human { get; set; } = new()
    {
        new ScaledSalvageRoll { Item = "iron_plate", Base = 3, PerCells = 12 },
        new ScaledSalvageRoll { Item = "cable", Base = 2, PerCells = 30 },
        new ScaledSalvageRoll { Item = "titanium_plate", Base = 1, PerCells = 60 },
        new ScaledSalvageRoll { Item = "data_fragment", Base = 1, ExtraMax = 1, Chance = 0.45 },
        new ScaledSalvageRoll { Item = "ai_memory_fragment", Base = 1, Chance = 0.25 },
    };

    public List<ScaledSalvageRoll> Alien { get; set; } = new()
    {
        new ScaledSalvageRoll { Item = "iron_plate", Base = 3, PerCells = 12 },
        new ScaledSalvageRoll { Item = "cable", Base = 2, PerCells = 30 },
        new ScaledSalvageRoll { Item = "crystal", Base = 1, PerCells = 60 },
        new ScaledSalvageRoll { Item = "data_fragment", Base = 1, ExtraMax = 1, Chance = 0.45 },
        new ScaledSalvageRoll { Item = "ai_memory_fragment", Base = 1, Chance = 0.25 },
    };
}

/// <summary>A payout row that grows with the hull: <c>Base + cells / PerCells + rng(0..ExtraMax)</c>, with
/// <see cref="Chance"/> to appear at all.</summary>
public sealed class ScaledSalvageRoll
{
    public string Item { get; set; } = string.Empty;
    public int Base { get; set; } = 1;

    /// <summary>One more per this many hull cells (0 = no scaling).</summary>
    public int PerCells { get; set; }

    /// <summary>A random extra of 0..this on top.</summary>
    public int ExtraMax { get; set; }

    public double Chance { get; set; } = 1.0;
}
