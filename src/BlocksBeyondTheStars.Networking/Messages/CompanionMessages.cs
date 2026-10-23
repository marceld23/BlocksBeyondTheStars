// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client → server: the player's response in an in-progress taming ritual on a creature. The
/// ritual itself is started/refreshed by right-clicking the <c>creature_translator</c> gadget (which sends
/// the usual <see cref="UseGadgetIntent"/>); this carries the chosen response to the creature's current
/// need. <see cref="Response"/> ∈ "feed" | "calm" | "approach" | "space" | "cancel".</summary>
public sealed class TameRespondIntent
{
    public string CreatureId { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
}

/// <summary>Server → client: the live state of a taming attempt — the creature's decoded mood + what it
/// wants now, plus the trust earned so far. Drives the taming HUD prompt. All text fields are localization
/// keys the client resolves.</summary>
public sealed class TameProgress
{
    public string CreatureId { get; set; } = string.Empty;
    public string CreatureName { get; set; } = string.Empty; // coined species name (the animal's "kind")
    public bool Active { get; set; }                          // false = no/ended attempt (client closes the prompt)
    public string MoodKey { get; set; } = string.Empty;      // creature.tame.mood.*
    public string NeedKey { get; set; } = string.Empty;      // creature.tame.need.* (the response it wants now)
    public string BaitKey { get; set; } = string.Empty;      // creature.tame.bait.* when the need is "feed" (else empty)
    public string BaitItem { get; set; } = string.Empty;     // the exact bait item key it wants (when need is "feed")
    public string MessageKey { get; set; } = string.Empty;   // creature.tame.msg.* feedback line for this step
    public int Trust { get; set; }
    public int Required { get; set; }
}

/// <summary>Server → client: a taming attempt finished (success or failure). On success it carries the new
/// companion id + the knowledge awarded.</summary>
public sealed class TameResult
{
    public string CreatureId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string CompanionId { get; set; } = string.Empty;
    public string CompanionName { get; set; } = string.Empty;
    public string MessageKey { get; set; } = string.Empty;   // creature.tame.msg.*
    public int KnowledgeGained { get; set; }
    public int KnowledgeTotal { get; set; }
}

/// <summary>Client → server: open the Companions menu tab (asks for the player's full companion roster).</summary>
public sealed class RequestCompanionsIntent
{
}

/// <summary>Client → server: rename a companion you own.</summary>
public sealed class SetCompanionNameIntent
{
    public string CompanionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Client → server: release a companion (untame it — it returns to the wild).</summary>
public sealed class ReleaseCompanionIntent
{
    public string CompanionId { get; set; } = string.Empty;
}

/// <summary>Feeds a companion one bait item to raise its bond (#1225). The server picks whichever bait the
/// player is carrying and answers with a refreshed companion list.</summary>
public sealed class FeedCompanionIntent
{
    public string CompanionId { get; set; } = string.Empty;
}

/// <summary>A tamed companion as shown in the Companions menu — enough of its descriptor to draw a small
/// procedural portrait, plus its name, home world and bond.</summary>
public sealed class NetCompanion
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SpeciesName { get; set; } = string.Empty;  // coined species name
    public string HomeBodyId { get; set; } = string.Empty;
    public string HomeBodyName { get; set; } = string.Empty;
    public bool Present { get; set; }                         // currently materialised near the player
    public int Bond { get; set; }

    /// <summary>Whether feeding would do something right now (#1225): the player carries bait, the bond is
    /// not already full and the animal is not still chewing the last one. Additive field — the button is
    /// dimmed rather than failing silently, and an older server simply leaves it false.</summary>
    public bool CanFeed { get; set; }
    public string Temperament { get; set; } = "Passive";
    public string Habitat { get; set; } = "Land";

    // Parametric appearance for the menu portrait (mirrors NetCreature's renderer inputs).
    public float Size { get; set; } = 1f;
    public int Legs { get; set; } = 4;
    public bool HasWings { get; set; }
    public bool HasTail { get; set; }
    public int BodySegments { get; set; } = 1;
    public int ColorRgb { get; set; } = 0xFFFFFF;
    public int BellyRgb { get; set; } = 0xFFFFFF;
    public bool Glows { get; set; }
    public int Eyes { get; set; } = 2;
    public int EyeRgb { get; set; } // #2069: the iris colour, 0 = classic (additive)
    public int Horns { get; set; }
    public bool HasCrest { get; set; }
    public int Tentacles { get; set; }
    public bool EyeStalks { get; set; }
    public bool HasGasSac { get; set; }
    public bool HasFins { get; set; }
    public string BodyPlan { get; set; } = "Standard"; // #637/#638 — the portrait renders the real plan
    public int NeckLength { get; set; }
    public bool HasTrunk { get; set; }
    public string HeadShape { get; set; } = "Box"; // #2009 — the portrait shows the pyramid an arachnid companion wears
    public int Arms { get; set; }                   // #2081 — a biped companion keeps its arms and its big head
    public float HeadRatio { get; set; } = 1f;
    public int Heads { get; set; } = 1;     // #1780-#1782 (generation 6) — the portrait shows every head, wing pair and fin pair
    public int WingPairs { get; set; } = 1;
    public int FinPairs { get; set; } = 1;
}

/// <summary>Server → client: the player's full companion roster (for the Companions menu tab).</summary>
public sealed class CompanionList
{
    public NetCompanion[] Companions { get; set; } = System.Array.Empty<NetCompanion>();
}
