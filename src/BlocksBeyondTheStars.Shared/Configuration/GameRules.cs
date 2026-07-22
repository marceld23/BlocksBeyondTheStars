// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Configuration;

/// <summary>
/// How much the optional AI mission backend may do (technical requirements /
/// `anf_mission_editor.md` §11.2). The game always works with AI off.
/// </summary>
public enum AiLevel
{
    Off,
    TextOnly,
    Suggest,
    Auto,
}

/// <summary>Primary game mode for a world.</summary>
public enum GameMode
{
    Survival,
    Creative,
}

public enum PvpMode
{
    Off,
    DuelsOnly,
    GroupBased,
    On,
}

public enum AlienActivity
{
    Off,
    Rare,
    Normal,
    Frequent,
    Extreme,
}

public enum WeaponMode
{
    None,
    ToolsOnly,
    NonLethal,
    Lasers,
    All,
}

public enum HazardLevel
{
    Off,
    Light,
    Normal,
    Hard,
}

public enum OxygenConsumption
{
    Off,
    Slow,
    Normal,
    Fast,
}

public enum HungerConsumption
{
    Off,
    Slow,
    Normal,
    Fast,
}

public enum DeathPenalty
{
    None,
    Light,
    Normal,
    Hard,
}

public enum StructureDamageMode
{
    Off,
    WithRights,
    On,
}

// --- Space flight / combat / enemy settings (anf_space_flight.md §13) ---

public enum SpaceCombatMode { Off, PvE, Pvp, Both }

public enum ShipWeaponMode { Off, MiningOnly, NpcsOnly, PvpAllowed, All }

public enum AsteroidDestructionMode { Off, MiningOnly, WeaponsAllowed }

public enum DockingMode { Off, FriendsOnly, RequestRequired, Free }

public enum LandingZoneProtection { Off, StartZoneOnly, All }

/// <summary>How fast the active story unfolds (P8 world option): scales the progress score, so a denser
/// setting reveals beats + the finale sooner without changing the pack. <see cref="Normal"/> is the pack's
/// authored pacing.</summary>
public enum StoryDensity { Sparse, Normal, Dense }

/// <summary>
/// Authoritative world rules (technical requirements / `anf_admin_einstellungen.md`).
/// The admin sets these; the server enforces them; clients are told the active set on join.
/// </summary>
public sealed class GameRules
{
    public GameMode GameMode { get; set; } = GameMode.Survival;

    public PvpMode Pvp { get; set; } = PvpMode.Off;
    public bool FriendlyFire { get; set; }

    public AlienActivity AggressiveAliens { get; set; } = AlienActivity.Normal;
    public bool PassiveCreatures { get; set; } = true;

    /// <summary>Passive-fauna abundance (world options): scales each world's live-creature cap —
    /// Off = lifeless, Extreme ≈ double the normal population. Live-editable by the world admin.</summary>
    public AlienActivity CreatureAbundance { get; set; } = AlienActivity.Normal;

    public WeaponMode WeaponMode { get; set; } = WeaponMode.ToolsOnly;

    public HazardLevel EnvironmentalHazards { get; set; } = HazardLevel.Normal;
    public OxygenConsumption OxygenConsumption { get; set; } = OxygenConsumption.Normal;

    /// <summary>How fast the player gets hungry (survival need); Off disables it entirely (as in Creative).
    /// A difficulty tier mirroring <see cref="OxygenConsumption"/> so admins can soften or sharpen it.</summary>
    public HungerConsumption HungerConsumption { get; set; } = HungerConsumption.Normal;

    public DeathPenalty DeathPenalty { get; set; } = DeathPenalty.Light;
    public bool KeepInventoryOnDeath { get; set; }
    public bool KeepShipOnDeath { get; set; } = true;

    public StructureDamageMode AllowPlayerStructureDamage { get; set; } = StructureDamageMode.Off;
    public StructureDamageMode ShipDamageByPlayers { get; set; } = StructureDamageMode.Off;

    public bool AdminCheats { get; set; }
    public bool AllowCheatsInSurvival { get; set; }
    public bool AllowCheatsInCreative { get; set; } = true;

    // --- Space flight / combat / enemies / docking / landing zones ---

    public bool FreeSpaceFlight { get; set; } = true;
    public SpaceCombatMode SpaceCombat { get; set; } = SpaceCombatMode.Off;
    public ShipWeaponMode ShipWeapons { get; set; } = ShipWeaponMode.Off;
    public AlienActivity SpaceNpcEnemies { get; set; } = AlienActivity.Off;
    public bool NeutralNpcShips { get; set; } = true;
    public AlienActivity AlienUfos { get; set; } = AlienActivity.Off;
    public AlienActivity PlanetEnemies { get; set; } = AlienActivity.Normal;

    /// <summary>Story P4: when on, a fraction of the planet-enemy population spawns as the black flying
    /// <b>scan-drone</b> variant (hovering) instead of the walking three-eyed ground robot — so planets carry
    /// both machine types. Toggles the mix only; the total planet-enemy count stays governed by
    /// <see cref="PlanetEnemies"/>. Live-editable.</summary>
    public bool PlanetDrones { get; set; } = true;

    /// <summary>Count-neutral machine/wreck coupling (story P5): when on, planet machines bias their spawn
    /// position toward a nearby wreck (clustering there) and hit harder there — without changing HOW MANY
    /// spawn (the frequency sliders + cap are untouched). Off restores uniform spawning. Live-editable.</summary>
    public bool MachineWreckCoupling { get; set; } = true;

    /// <summary>The active story pack for a fresh save (P8 world option): a pack id (e.g. "vega_protocol"),
    /// "none" to play sandbox with no story, or empty to use the built-in default pack. Only consulted when a
    /// save has no persisted story state yet; thereafter the admin switches packs live (resets progress).</summary>
    public string StoryId { get; set; } = string.Empty;

    /// <summary>Story pacing speed (P8 world option): how fast beats + the finale unfold. Live-editable.</summary>
    public StoryDensity StoryDensity { get; set; } = StoryDensity.Normal;

    /// <summary>Multiplier applied to the story-progress score from <see cref="StoryDensity"/> (Dense reveals
    /// the arc sooner, Sparse later). Normal is 1.0 — the pack's authored pacing.</summary>
    public float StoryProgressScale => StoryDensity switch
    {
        StoryDensity.Sparse => 0.65f,
        StoryDensity.Dense => 1.5f,
        _ => 1f,
    };
    public AsteroidDestructionMode AsteroidDestruction { get; set; } = AsteroidDestructionMode.MiningOnly;
    public DockingMode ShipDocking { get; set; } = DockingMode.RequestRequired;
    public bool PersonalLandingZones { get; set; } = true;
    public LandingZoneProtection PersonalLandingZoneProtection { get; set; } = LandingZoneProtection.StartZoneOnly;

    /// <summary>Instant Travel (world option, default OFF): when ON, the travel screen can quick-travel to
    /// any world/station, even ones never visited. When OFF, quick-travel is limited to bodies the player
    /// has already physically landed on — a new world must be reached by flying there and landing (and a
    /// never-visited star system must be reached by a hyperjump into its flight space). Live-editable by the
    /// world admin.</summary>
    public bool InstantTravel { get; set; }

    /// <summary>Whether crafting consumes materials / needs stations (false in Creative).</summary>
    public bool CraftingCostsMaterials => GameMode != GameMode.Creative;

    /// <summary>Whether the suit consumes oxygen given the mode and setting.</summary>
    public bool OxygenEnabled => GameMode != GameMode.Creative && OxygenConsumption != OxygenConsumption.Off;

    /// <summary>Oxygen drain per second derived from the configured rate. Softened again (Severin playtest —
    /// oxygen still felt punishing) — at Normal a full tank now lasts ~285s on foot (was ~200s, originally ~50s).
    /// The carried oxygen-tank upgrade (item.oxygen_tank_2, oxygenBonus) still adds meaningful time on top.</summary>
    public float OxygenDrainPerSecond => OxygenConsumption switch
    {
        OxygenConsumption.Slow => 0.18f,
        OxygenConsumption.Normal => 0.35f,
        OxygenConsumption.Fast => 0.7f,
        _ => 0f,
    };

    /// <summary>Whether the player's hunger drains given the mode and setting.</summary>
    public bool HungerEnabled => GameMode != GameMode.Creative && HungerConsumption != HungerConsumption.Off;

    /// <summary>Hunger lost per second outside the ship, derived from the configured tier. Softened and tiered
    /// after Severin playtest #2 (nearly starved twice in the first minutes): at Normal a full bar now lasts
    /// ~333s on foot (was a flat ~200s), with Slow/Fast difficulty tiers mirroring <see cref="OxygenDrainPerSecond"/>.
    /// Aboard the ship or in a station hunger still refills, so a short mining trip no longer means starvation.</summary>
    public float HungerDrainPerSecond => HungerConsumption switch
    {
        HungerConsumption.Slow => 0.18f,
        HungerConsumption.Normal => 0.3f,
        HungerConsumption.Fast => 0.5f,
        _ => 0f,
    };

    /// <summary>Whether admin cheats may be used at all, given mode + toggles.</summary>
    public bool CheatsAllowed => AdminCheats &&
        (GameMode == GameMode.Creative ? AllowCheatsInCreative : AllowCheatsInSurvival);

    public GameRules Clone() => (GameRules)MemberwiseClone();
}
