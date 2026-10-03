// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Bio;

/// <summary>
/// What a bred plant looks like and yields. A wild species has one form block for body and crown and the plain layout;
/// a cross takes its body from one parent and its crown from the other and gets a layout, a size, a colour and maybe a
/// glow of its own — a plant that looks like neither parent, built from the tiles the game already has.
/// </summary>
public sealed class FloraGenome
{
    /// <summary>The <c>flora_*</c> block the body is drawn from; it also decides what the plant yields.</summary>
    public string BodyBlock { get; set; } = string.Empty;

    /// <summary>The <c>flora_*</c> block the crown is drawn from (the body's own for a wild species).</summary>
    public string CrownBlock { get; set; } = string.Empty;

    /// <summary>0 plain, 1 star, 2 tiered, 3 fan (<see cref="FloraForm"/>).</summary>
    public int Layout { get; set; }

    /// <summary>0 small .. 3 giant.</summary>
    public int Size { get; set; } = 1;

    /// <summary>The colour, 0xRRGGBB.</summary>
    public int TintRgb { get; set; }

    /// <summary>0 none .. 3 bright: how much of its colour the plant casts as light.</summary>
    public int Glow { get; set; }

    public bool Toxic { get; set; }

    /// <summary>The genome of a cross: always the same for the same pair.</summary>
    public static FloraGenome Cross(FloraGenome a, FloraGenome b, uint childSeed)
    {
        bool bodyFromA = (BioHash.Draw(childSeed, 20) & 1) == 0;
        var body = bodyFromA ? a : b;
        var crown = bodyFromA ? b : a;
        ulong roll = BioHash.Draw(childSeed, 21);

        // The colour is the parents' mixed; one cross in four shifts it to a colour of its own.
        int tint = Mix(a.TintRgb, b.TintRgb);
        if ((roll & 0x3) == 0)
        {
            tint = RotateHue(tint);
        }

        // A glowing parent passes the glow on half the time; one cross in eight starts to glow by itself.
        int glow = Math.Max(a.Glow, b.Glow);
        if (glow > 0 && ((roll >> 2) & 1) == 0)
        {
            glow = 0;
        }
        else if (glow == 0 && ((roll >> 3) & 0x7) == 0)
        {
            glow = 1;
        }

        return new FloraGenome
        {
            BodyBlock = body.BodyBlock,
            CrownBlock = crown.CrownBlock,
            Layout = 1 + (int)((roll >> 8) % 3),     // a cross is never the plain layout of a wild plant
            Size = (int)((roll >> 12) % 4),
            TintRgb = tint,
            Glow = glow,
            Toxic = a.Toxic && b.Toxic,              // a cross breeds the poison out unless both carry it
        };
    }

    private static int Mix(int rgbA, int rgbB)
    {
        int r = (((rgbA >> 16) & 0xFF) + ((rgbB >> 16) & 0xFF)) / 2;
        int g = (((rgbA >> 8) & 0xFF) + ((rgbB >> 8) & 0xFF)) / 2;
        int b = ((rgbA & 0xFF) + (rgbB & 0xFF)) / 2;
        int mixed = (r << 16) | (g << 8) | b;
        return mixed == 0 ? 0x010101 : mixed; // 0 means "no tint" on a voxel
    }

    private static int RotateHue(int rgb) => ((rgb & 0xFF) << 16) | ((rgb >> 8) & 0xFFFF);
}

/// <summary>
/// The form of a bred plant as it rides on its voxel: 24 bits in the cell's glow channel, which a flora block has no
/// other use for (a bred plant's light is one of the fields). Bit 23 is always set, so a form is never 0 = "none".
/// The catalog indices are stable: wild species are only ever appended to <see cref="FloraCatalog.All"/>.
/// </summary>
public static class FloraForm
{
    /// <summary>The block key of every bred plant.</summary>
    public const string BlockKey = "flora_hybrid";

    public const int LayoutPlain = 0;
    public const int LayoutStar = 1;
    public const int LayoutTiered = 2;
    public const int LayoutFan = 3;

    private const int Marker = 1 << 23;

    /// <summary>Packs a genome into the 24 bits of a voxel's glow channel; 0 when a form block is not in the catalog.</summary>
    public static int Pack(FloraGenome genome)
    {
        int body = IndexOf(genome.BodyBlock);
        int crown = IndexOf(genome.CrownBlock);
        if (body < 0 || crown < 0)
        {
            return 0;
        }

        return Marker | body | (crown << 6) | ((genome.Layout & 0x3) << 12) | ((genome.Size & 0x3) << 14) | ((genome.Glow & 0x3) << 16);
    }

    /// <summary>Whether a glow value is a packed form.</summary>
    public static bool IsForm(int packed) => (packed & Marker) != 0;

    public static string BodyBlock(int packed) => KeyAt(packed & 0x3F);

    public static string CrownBlock(int packed) => KeyAt((packed >> 6) & 0x3F);

    public static int Layout(int packed) => (packed >> 12) & 0x3;

    public static int Size(int packed) => (packed >> 14) & 0x3;

    public static int Glow(int packed) => (packed >> 16) & 0x3;

    /// <summary>How tall a size class draws the plant, relative to a plain one.</summary>
    public static float HeightOf(int size) => size switch { 0 => 0.65f, 1 => 1f, 2 => 1.45f, _ => 1.9f };

    /// <summary>The share of its colour a glowing plant casts as light.</summary>
    public static float LightOf(int glow) => glow switch { 1 => 0.3f, 2 => 0.45f, 3 => 0.6f, _ => 0f };

    /// <summary>Whether a catalog species can be bred at all: a single-cell wild or authored plant. Fruit hangs on a
    /// tree, a crop is a crop, and the sapling becomes a tree — none of them is a plant to clone.</summary>
    public static bool Breedable(string blockKey)
        => FloraCatalog.Find(blockKey) is { } sp && !sp.Fruit && !sp.Cultivated;

    private static int IndexOf(string blockKey)
    {
        var all = FloraCatalog.All;
        for (int i = 0; i < all.Count && i < 64; i++)
        {
            if (all[i].Key == blockKey)
            {
                return i;
            }
        }

        return -1;
    }

    private static string KeyAt(int index)
    {
        var all = FloraCatalog.All;
        return index >= 0 && index < all.Count ? all[index].Key : string.Empty;
    }
}

/// <summary>
/// One species in a save's register: everything cloning, crossing and the lab need to know about it besides its seed.
/// A wild species stores its <see cref="Context"/> (the profile is derived, never stored); a cross stores its parents.
/// </summary>
public sealed class BioSpeciesEntry
{
    public uint Seed { get; set; }
    public BioKind Kind { get; set; }

    /// <summary>The coined species name (animals, plants). Empty for a deposit — its name is its material's.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The body it was first sampled on (its own id and its display name at that time).</summary>
    public string OriginBodyId { get; set; } = string.Empty;
    public string OriginBodyName { get; set; } = string.Empty;

    /// <summary>Where and how it lives — a wild species or a deposit. Null for a cross.</summary>
    public BioContext? Context { get; set; }

    /// <summary>The parents of a cross (0 for a wild species), and whether they came from different worlds.</summary>
    public uint ParentA { get; set; }
    public uint ParentB { get; set; }
    public bool DifferentWorlds { get; set; }

    /// <summary>0 for a wild species, 1.. for a cross (<see cref="BioRules.MaxCrossGeneration"/>).</summary>
    public int Generation { get; set; }

    /// <summary>An animal's species snapshot — what a clone tank grows.</summary>
    public CreatureSpecies? Creature { get; set; }

    /// <summary>A plant's genome — what a seedling grows into. Null for a tree, whose trunk and leaves are no single plant.</summary>
    public FloraGenome? Flora { get; set; }

    /// <summary>A deposit's material (the item key its fixed traits are read from).</summary>
    public string MaterialItem { get; set; } = string.Empty;

    public bool IsCross => ParentA != 0 && ParentB != 0;
}

/// <summary>Resolves profiles from a register: a wild species from its context, a cross from its parents.</summary>
public static class BioSpeciesProfiles
{
    /// <summary>The substance profile of a register entry, or null for a deposit or a cross whose parents are unknown.
    /// <paramref name="lookup"/> finds an entry by seed.</summary>
    public static BioProfile? ProfileOf(BioSpeciesEntry? entry, Func<uint, BioSpeciesEntry?> lookup, int depth = 0)
    {
        if (entry is null || entry.Kind == BioKind.Mineral || depth > BioRules.MaxCrossGeneration)
        {
            return null;
        }

        if (!entry.IsCross)
        {
            return entry.Context is null ? null : BioProfiles.Derive(entry.Seed, entry.Context);
        }

        var a = ProfileOf(lookup(entry.ParentA), lookup, depth + 1);
        var b = ProfileOf(lookup(entry.ParentB), lookup, depth + 1);
        return a is null || b is null ? null : BioProfiles.Cross(a, b, entry.Seed, entry.DifferentWorlds);
    }

    /// <summary>The material profile of a deposit entry, from its material's fixed traits.</summary>
    public static MaterialProfile? MaterialOf(BioSpeciesEntry? entry, Func<string, IReadOnlyDictionary<string, int>?> traitsOf)
    {
        if (entry is not { Kind: BioKind.Mineral } || entry.Context is null)
        {
            return null;
        }

        return MaterialProfiles.Derive(entry.Seed, MaterialProfiles.BaseLevels(traitsOf(entry.MaterialItem)), entry.Context);
    }
}
