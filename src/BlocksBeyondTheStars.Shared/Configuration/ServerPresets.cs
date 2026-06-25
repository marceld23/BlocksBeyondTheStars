// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Configuration;

/// <summary>
/// Predefined rule profiles (technical requirements / `anf_admin_einstellungen.md` §4 and
/// `anf_space_flight.md` §13.2) so admins don't have to set every rule individually.
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
            FreeSpaceFlight = true,
            SpaceCombat = SpaceCombatMode.Off,
            ShipWeapons = ShipWeaponMode.Off,
            SpaceNpcEnemies = AlienActivity.Off,
            AlienUfos = AlienActivity.Off,
            PlanetEnemies = AlienActivity.Off,
            AsteroidDestruction = AsteroidDestructionMode.MiningOnly,
            ShipDocking = DockingMode.RequestRequired,
            PersonalLandingZones = true,
            PersonalLandingZoneProtection = LandingZoneProtection.All,
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
            FreeSpaceFlight = true,
            SpaceCombat = SpaceCombatMode.Off,
            ShipWeapons = ShipWeaponMode.Off,
            SpaceNpcEnemies = AlienActivity.Off,
            AlienUfos = AlienActivity.Off,
            PlanetEnemies = AlienActivity.Off,
            AsteroidDestruction = AsteroidDestructionMode.MiningOnly,
            PersonalLandingZones = true,
            PersonalLandingZoneProtection = LandingZoneProtection.All,
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
            FreeSpaceFlight = true,
            SpaceCombat = SpaceCombatMode.PvE,
            ShipWeapons = ShipWeaponMode.NpcsOnly,
            SpaceNpcEnemies = AlienActivity.Normal,
            AlienUfos = AlienActivity.Rare,
            PlanetEnemies = AlienActivity.Normal,
            AsteroidDestruction = AsteroidDestructionMode.WeaponsAllowed,
            ShipDocking = DockingMode.RequestRequired,
            PersonalLandingZones = true,
            PersonalLandingZoneProtection = LandingZoneProtection.StartZoneOnly,
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
            FreeSpaceFlight = true,
            SpaceCombat = SpaceCombatMode.PvE,
            ShipWeapons = ShipWeaponMode.NpcsOnly,
            SpaceNpcEnemies = AlienActivity.Frequent,
            AlienUfos = AlienActivity.Normal,
            PlanetEnemies = AlienActivity.Frequent,
            AsteroidDestruction = AsteroidDestructionMode.WeaponsAllowed,
            ShipDocking = DockingMode.RequestRequired,
            PersonalLandingZones = true,
            PersonalLandingZoneProtection = LandingZoneProtection.StartZoneOnly,
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
            FreeSpaceFlight = true,
            SpaceCombat = SpaceCombatMode.Both,
            ShipWeapons = ShipWeaponMode.PvpAllowed,
            SpaceNpcEnemies = AlienActivity.Normal,
            AlienUfos = AlienActivity.Normal,
            PlanetEnemies = AlienActivity.Normal,
            AsteroidDestruction = AsteroidDestructionMode.WeaponsAllowed,
            ShipDocking = DockingMode.RequestRequired,
            PersonalLandingZones = true,
            PersonalLandingZoneProtection = LandingZoneProtection.StartZoneOnly,
        },
        _ => null,
    };
}
