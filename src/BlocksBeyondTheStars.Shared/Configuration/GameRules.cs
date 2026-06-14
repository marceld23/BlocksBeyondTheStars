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

    /// <summary>Whether the player gets hungry and must eat (survival need); off in Creative.</summary>
    public bool Hunger { get; set; } = true;

    public DeathPenalty DeathPenalty { get; set; } = DeathPenalty.Light;
    public bool KeepInventoryOnDeath { get; set; }
    public bool KeepShipOnDeath { get; set; } = true;

    public StructureDamageMode AllowPlayerStructureDamage { get; set; } = StructureDamageMode.Off;
    public StructureDamageMode ShipDamageByPlayers { get; set; } = StructureDamageMode.Off;

    public bool AdminCheats { get; set; }
    public bool AllowCheatsInSurvival { get; set; }
    public bool AllowCheatsInCreative { get; set; } = true;

    // --- Space flight / combat / enemies / docking / landing zones ---

    public bool FreeSpaceFlight { get; set; }
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

    /// <summary>Oxygen drain per second derived from the configured rate. Drastically softened (item: oxygen on
    /// planets was far too punishing) — at Normal a full tank now lasts ~200s on foot instead of ~50s. The carried
    /// oxygen-tank upgrade (item.oxygen_tank_2, oxygenBonus) still adds meaningful survival time on top.</summary>
    public float OxygenDrainPerSecond => OxygenConsumption switch
    {
        OxygenConsumption.Slow => 0.25f,
        OxygenConsumption.Normal => 0.5f,
        OxygenConsumption.Fast => 1f,
        _ => 0f,
    };

    /// <summary>Whether the player's hunger drains given the mode and setting.</summary>
    public bool HungerEnabled => GameMode != GameMode.Creative && Hunger;

    /// <summary>Hunger lost per second outside the ship (a full bar lasts a few minutes).</summary>
    public float HungerDrainPerSecond => 0.5f;

    /// <summary>Whether admin cheats may be used at all, given mode + toggles.</summary>
    public bool CheatsAllowed => AdminCheats &&
        (GameMode == GameMode.Creative ? AllowCheatsInCreative : AllowCheatsInSurvival);

    public GameRules Clone() => (GameRules)MemberwiseClone();
}
