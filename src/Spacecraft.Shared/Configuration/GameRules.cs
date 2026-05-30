namespace Spacecraft.Shared.Configuration;

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

    public WeaponMode WeaponMode { get; set; } = WeaponMode.ToolsOnly;

    public HazardLevel EnvironmentalHazards { get; set; } = HazardLevel.Normal;
    public OxygenConsumption OxygenConsumption { get; set; } = OxygenConsumption.Normal;

    public DeathPenalty DeathPenalty { get; set; } = DeathPenalty.Light;
    public bool KeepInventoryOnDeath { get; set; }
    public bool KeepShipOnDeath { get; set; } = true;

    public StructureDamageMode AllowPlayerStructureDamage { get; set; } = StructureDamageMode.Off;
    public StructureDamageMode ShipDamageByPlayers { get; set; } = StructureDamageMode.Off;

    public bool AdminCheats { get; set; }
    public bool AllowCheatsInSurvival { get; set; }
    public bool AllowCheatsInCreative { get; set; } = true;

    /// <summary>Whether crafting consumes materials / needs stations (false in Creative).</summary>
    public bool CraftingCostsMaterials => GameMode != GameMode.Creative;

    /// <summary>Whether the suit consumes oxygen given the mode and setting.</summary>
    public bool OxygenEnabled => GameMode != GameMode.Creative && OxygenConsumption != OxygenConsumption.Off;

    /// <summary>Oxygen drain per second derived from the configured rate.</summary>
    public float OxygenDrainPerSecond => OxygenConsumption switch
    {
        OxygenConsumption.Slow => 1f,
        OxygenConsumption.Normal => 2f,
        OxygenConsumption.Fast => 4f,
        _ => 0f,
    };

    /// <summary>Whether admin cheats may be used at all, given mode + toggles.</summary>
    public bool CheatsAllowed => AdminCheats &&
        (GameMode == GameMode.Creative ? AllowCheatsInCreative : AllowCheatsInSurvival);

    public GameRules Clone() => (GameRules)MemberwiseClone();
}
