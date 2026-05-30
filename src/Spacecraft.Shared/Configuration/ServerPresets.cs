namespace Spacecraft.Shared.Configuration;

/// <summary>
/// Predefined rule profiles (technical requirements / `anf_admin_einstellungen.md` §4) so
/// admins don't have to set every rule individually.
/// </summary>
public static class ServerPresets
{
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "peaceful-creative", "family", "coop-survival", "dangerous", "pvp",
    };

    public static GameRules? Get(string name) => name?.Trim().ToLowerInvariant() switch
    {
        "peaceful-creative" => new GameRules
        {
            GameMode = GameMode.Creative,
            AggressiveAliens = AlienActivity.Off,
            PassiveCreatures = true,
            Pvp = PvpMode.Off,
            WeaponMode = WeaponMode.None,
            EnvironmentalHazards = HazardLevel.Off,
            OxygenConsumption = OxygenConsumption.Off,
            DeathPenalty = DeathPenalty.None,
            KeepInventoryOnDeath = true,
            AllowPlayerStructureDamage = StructureDamageMode.Off,
            ShipDamageByPlayers = StructureDamageMode.Off,
            AdminCheats = true,
            AllowCheatsInCreative = true,
        },
        "family" => new GameRules
        {
            GameMode = GameMode.Survival,
            AggressiveAliens = AlienActivity.Off,
            Pvp = PvpMode.Off,
            WeaponMode = WeaponMode.ToolsOnly,
            EnvironmentalHazards = HazardLevel.Light,
            OxygenConsumption = OxygenConsumption.Slow,
            DeathPenalty = DeathPenalty.None,
            KeepInventoryOnDeath = true,
            ShipDamageByPlayers = StructureDamageMode.Off,
        },
        "coop-survival" => new GameRules
        {
            GameMode = GameMode.Survival,
            AggressiveAliens = AlienActivity.Normal,
            Pvp = PvpMode.Off,
            WeaponMode = WeaponMode.Lasers,
            EnvironmentalHazards = HazardLevel.Normal,
            OxygenConsumption = OxygenConsumption.Normal,
            DeathPenalty = DeathPenalty.Light,
            AllowPlayerStructureDamage = StructureDamageMode.WithRights,
            ShipDamageByPlayers = StructureDamageMode.Off,
        },
        "dangerous" => new GameRules
        {
            GameMode = GameMode.Survival,
            AggressiveAliens = AlienActivity.Frequent,
            Pvp = PvpMode.Off,
            WeaponMode = WeaponMode.Lasers,
            EnvironmentalHazards = HazardLevel.Hard,
            OxygenConsumption = OxygenConsumption.Normal,
            DeathPenalty = DeathPenalty.Normal,
            KeepInventoryOnDeath = false,
            AllowPlayerStructureDamage = StructureDamageMode.WithRights,
            ShipDamageByPlayers = StructureDamageMode.Off,
        },
        "pvp" => new GameRules
        {
            GameMode = GameMode.Survival,
            AggressiveAliens = AlienActivity.Normal,
            Pvp = PvpMode.On,
            FriendlyFire = true,
            WeaponMode = WeaponMode.All,
            EnvironmentalHazards = HazardLevel.Normal,
            DeathPenalty = DeathPenalty.Normal,
            KeepInventoryOnDeath = false,
            AllowPlayerStructureDamage = StructureDamageMode.On,
            ShipDamageByPlayers = StructureDamageMode.On,
        },
        _ => null,
    };
}
