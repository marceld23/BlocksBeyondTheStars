// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Server → client (#3245): the species of the world the player has just entered, sent once per world entry (the join,
/// a travel, a respawn onto another world, boarding a station or a ship interior), never per tick. The client only learns
/// about the animals near it (<see cref="CreatureList"/>); this roster lets its distant-fauna scheduler
/// (<c>Client.Core/Audio/DistantFauna</c>) have the world's OWN species call from far away, out of sight, so a world
/// sounds like its fauna even when no animal is close. Empty for a world without fauna (a station deck, a ship interior,
/// a dead world). A new tag with no protocol bump: an older client drops the unknown tag and hears no distant calls.
/// </summary>
public sealed class FaunaRoster
{
    public List<NetFaunaSpecies> Species { get; set; } = new();
}

/// <summary>
/// One species of a <see cref="FaunaRoster"/> (#3245): its id and exactly the traits the client's voice derivation
/// (<c>CreatureVoices.Derive(VoiceSeed, VoiceTraits)</c>) reads, so a distant call is the same voice the animal has up
/// close, plus <see cref="Activity"/> so the calls follow the hour. The string enums carry the enum names, like
/// <see cref="NetCreature"/> does.
/// </summary>
public sealed class NetFaunaSpecies
{
    /// <summary>The per-world species id ("sp0", …) — the same id a <see cref="NetCreature.SpeciesId"/> of this species
    /// carries, so the client can tell a species already within hearing.</summary>
    public string SpeciesId { get; set; } = string.Empty;

    /// <summary>The species' voice seed (<c>CreatureSpecies.VoiceSeed</c>, #907).</summary>
    public int VoiceSeed { get; set; }

    /// <summary>"Land" | "Water" | "Lava" | "Air" | "Cave" | "Amphibian".</summary>
    public string Habitat { get; set; } = "Land";

    /// <summary>"Passive" | "Skittish" | "Territorial" | "Aggressive" | "PackHunter".</summary>
    public string Temperament { get; set; } = "Passive";

    /// <summary>The body plan's enum name ("Standard", "Medusa", "Titan", …).</summary>
    public string BodyPlan { get; set; } = "Standard";

    public float Size { get; set; } = 1f;
    public int Legs { get; set; } = 4;
    public int Eyes { get; set; } = 2;
    public int Horns { get; set; }
    public int BodySegments { get; set; } = 1;
    public int Tentacles { get; set; }
    public bool HasGasSac { get; set; }
    public bool Glows { get; set; }

    /// <summary>"Diurnal" | "Nocturnal" | "Crepuscular" | "Cathemeral" — when the species is awake.</summary>
    public string Activity { get; set; } = "Diurnal";
}
