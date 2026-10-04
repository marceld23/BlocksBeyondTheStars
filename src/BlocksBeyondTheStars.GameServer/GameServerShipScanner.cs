// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Weather;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The ship scanner (#2237–#2240). Every ship carries one: the mandatory cockpit holds tier 1, the Deep scanner (the
/// former planet scanner module) tier 2 and the Quantum scanner tier 3 — the fitted module with the highest
/// <c>scanner_strength</c> decides range and scan time. The flight hotbar selects it like a weapon; holding the fire
/// button on a target sends <see cref="ScanEntityIntent"/> (a space object) or <see cref="PlanetScanIntent"/> (a body).
/// The server checks range and a short cooldown and answers with locale keys only. Scanning is free — no energy.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Minimum seconds between two ship scans of the SAME target by one pilot — the hold ring already paces a
    /// real player; this only stops a scripted trigger from hammering one object.</summary>
    private const double ShipScanCooldownSeconds = 0.3;

    private readonly Dictionary<string, double> _shipScanReadyAt = new();

    /// <summary>Knowledge for the first scan of each space object kind (pod, station, machine kind, raiders).</summary>
    private const int KnowledgeSpaceObject = 3;

    /// <summary>Knowledge for the first overview of a body (#2239).</summary>
    private const int KnowledgeBodyOverview = 2;

    /// <summary>The ship's scanner — the fitted module with the highest <c>scanner_strength</c> (#2240), the rule the
    /// client shares (<see cref="ShipScannerRules"/>).</summary>
    internal ShipScannerSpec ShipScanner(ShipState ship) => ShipScannerRules.For(ship.Modules, _content.GetShipModule);

    /// <summary>The per-wormhole scan-ledger key (#2242): a wormhole the player flew through or scanned — its
    /// destination is known to them from then on.</summary>
    private static string WormholeScanKey(string wormholeId) => "wormhole:" + wormholeId;

    /// <summary>Whether a pilot may scan this target again now; arms the cooldown when they may.</summary>
    private bool TakeShipScanTurn(string playerId, string targetId)
    {
        string key = playerId + "|" + targetId;
        if (_shipScanReadyAt.TryGetValue(key, out double readyAt) && _uptime < readyAt)
        {
            return false;
        }

        _shipScanReadyAt[key] = _uptime + ShipScanCooldownSeconds;
        return true;
    }

    /// <summary>The space object kinds the scanner reads (#2238). Resource drops and the planet-side kinds are not
    /// space objects worth a readout.</summary>
    private static bool IsScannableSpaceObject(CombatEntityKind kind) => kind is CombatEntityKind.Asteroid
        or CombatEntityKind.Anomaly or CombatEntityKind.Wreck or CombatEntityKind.EscapePod
        or CombatEntityKind.SpaceStation or CombatEntityKind.Drone or CombatEntityKind.Ufo
        or CombatEntityKind.Cruiser or CombatEntityKind.BanditShip or CombatEntityKind.Wormhole;

    /// <summary>The readouts the scanner adds for the kinds that had none (#2238): a life pod, a station, the
    /// Guardian machines and the raiders. Knowledge once per kind, through the shared <see cref="Award"/> (missions,
    /// Codex, achievements). Null for kinds handled elsewhere.</summary>
    private ScanResult? ScanNewSpaceObject(PlayerSession session, CombatEntity target)
    {
        ScanReadout readout;
        string ledger;
        switch (target.Kind)
        {
            case CombatEntityKind.EscapePod:
                readout = new ScanReadout
                {
                    Kind = "pod",
                    SubjectKey = "escape_pod",
                    Display = target.Name,
                    InfoKey = "ui.scan.pod",
                    TraitKeys = new[] { "ui.scan.trait.life_sign" },
                    LegacyInfo = "One life sign inside. Fly close to take them aboard.",
                };
                ledger = "space:pod";
                break;

            case CombatEntityKind.SpaceStation:
                bool playerOwned = IsPlayerStationId(target.Id);
                var station = _stationsById.TryGetValue(target.Id, out var st) ? st : null;
                var traits = new List<string> { playerOwned ? "ui.scan.trait.station_player" : "ui.scan.trait.station_trade" };
                if (station is not null && !string.IsNullOrEmpty(station.SizeTier))
                {
                    traits.Add("ui.scan.trait.station_size_" + station.SizeTier);
                }

                readout = new ScanReadout
                {
                    Kind = "station",
                    SubjectKey = "space_station",
                    Display = target.Name,
                    InfoKey = "ui.scan.station",
                    TraitKeys = traits.ToArray(),
                    LegacyInfo = "A space station — fly to its hangar to dock.",
                };
                ledger = "space:station";
                break;

            case CombatEntityKind.Drone or CombatEntityKind.Ufo or CombatEntityKind.Cruiser:
                string machine = target.Kind.ToString().ToLowerInvariant();
                readout = new ScanReadout
                {
                    Kind = "machine",
                    SubjectKey = machine,
                    Display = string.Empty,
                    ThreatKey = target.Hostile ? "ui.scan.threat.hostile" : string.Empty,
                    InfoKey = "ui.scan.machine." + machine,
                    LegacyInfo = "A Guardian machine.",
                    LegacyThreat = target.Hostile ? "Hostile" : "—",
                };
                ledger = "space:machine:" + machine;
                break;

            case CombatEntityKind.BanditShip:
                readout = new ScanReadout
                {
                    Kind = "bandit",
                    SubjectKey = "bandit_ship",
                    Display = target.Name,
                    ThreatKey = target.Hostile ? "ui.scan.threat.hostile" : "ui.scan.threat.provokable",
                    InfoKey = "ui.scan.bandit",
                    LegacyInfo = "Raiders — they demand cargo and open fire when refused.",
                };
                ledger = "space:bandit";
                break;

            default:
                return null;
        }

        return Award(session, ledger, readout, KnowledgeSpaceObject);
    }

    /// <summary>The ids of this instance's entities the player has already read (#2238) — the kinds whose scan the
    /// server remembers per object (anomalies, wrecks, wormholes), for the lock-on label and the calm anomaly.</summary>
    private string[] ScannedIdsIn(SpaceInstance instance, PlayerSession session)
    {
        var scanned = session.State.Scanned;
        var ids = new List<string>();
        foreach (var e in instance.Entities)
        {
            string? key = e.Kind switch
            {
                CombatEntityKind.Anomaly => AnomalyScanKey(e.Id),
                CombatEntityKind.Wreck => "wreck:" + e.Id,
                CombatEntityKind.Wormhole => WormholeScanKey(e.Id),
                _ => null,
            };
            if (key != null && scanned.Contains(key))
            {
                ids.Add(e.Id);
            }
        }

        return ids.ToArray();
    }

    /// <summary>The per-anomaly scan-ledger key (#2238): knowledge for every NEW anomaly. Their number per galaxy is
    /// fixed by the seed (the encounter roll is per launch body), so there is nothing to farm.</summary>
    private static string AnomalyScanKey(string anomalyId) => "anomaly:" + anomalyId;

    // ---------------- #2239: the planet overview card ----------------

    /// <summary>The overview rows of a body for a scanner tier — built from the planet type's data and the body's
    /// seed-pure values (gravity, roster, live weather, frontier tier). No world is loaded for it.</summary>
    private (NetOverviewRow[] Rows, byte Danger) BuildBodyOverview(CelestialBody body, PlanetType planet, int tier)
    {
        var rows = new List<NetOverviewRow>();
        bool airlessMoon = body.Kind == CelestialKind.Moon
                           && string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);

        // Air.
        string atmosphere = planet.Atmosphere.ToLowerInvariant();
        var air = atmosphere switch
        {
            "breathable" => Row("air", "ui.overview.air.breathable", 0),
            "toxic" => Row("air", planet.CorrosiveAirChance > 0 ? "ui.overview.air.corrosive" : "ui.overview.air.toxic", 2),
            _ => Row("air", "ui.overview.air.none", 2),
        };
        if (tier >= 2 && atmosphere != "none")
        {
            air.DetailKey = planet.OxygenExtractability >= 0.5 ? "ui.overview.air.oxygen_much"
                : planet.OxygenExtractability > 0 ? "ui.overview.air.oxygen_little" : "ui.overview.air.oxygen_none";
        }

        rows.Add(air);

        // Temperature.
        double temp = planet.BaseTemperature;
        var temperature = temp <= -60 ? Row("temperature", "ui.overview.temperature.freezing", 2)
            : temp <= -10 ? Row("temperature", "ui.overview.temperature.cold", 1)
            : temp < 35 ? Row("temperature", "ui.overview.temperature.mild", 0)
            : temp < 70 ? Row("temperature", "ui.overview.temperature.hot", 1)
            : Row("temperature", "ui.overview.temperature.scorching", 2);
        if (planet.ExposureMinutesCold > 0 || planet.ExposureMinutesHot > 0)
        {
            temperature.DetailKey = "ui.overview.temperature.protection";
            temperature.Level = System.Math.Max(temperature.Level, (byte)1);
        }
        else if (tier >= 2 && planet.HotZoneShare > 0)
        {
            temperature.DetailKey = "ui.overview.temperature.hot_zones";
        }

        rows.Add(temperature);

        // Gravity — the same seeded band the world uses once it is loaded.
        var sizeClass = WorldConstants.SizeClassFor(body.Kind, planet.Key);
        float gravity = GravityFor(sizeClass, unchecked((uint)(StableStringHash(body.Id) ^ (int)_meta.Seed)));
        var gravityRow = gravity < 0.7f ? Row("gravity", "ui.overview.gravity.light", 0)
            : gravity <= 1.2f ? Row("gravity", "ui.overview.gravity.normal", 0)
            : Row("gravity", "ui.overview.gravity.heavy", 1);
        if (tier >= 2)
        {
            gravityRow.Extra = gravity.ToString("0.0", CultureInfo.InvariantCulture) + " g";
        }

        rows.Add(gravityRow);

        // Weather — what it is doing down there right now.
        if (!planet.IsAirless)
        {
            var loaded = _worlds.Find(body.Id);
            var sim = loaded?.Weather ?? EnsureBodyWeather(body)?.Sim;
            string state = sim?.State ?? "clear";
            int severity = WeatherCatalog.Find(state)?.Severity ?? 0;
            var weather = Row("weather", "weather." + state, (byte)(severity >= 3 ? 2 : severity >= 2 ? 1 : 0));
            if (tier >= 2)
            {
                weather.DetailKey = planet.StormChance >= 0.5 ? "ui.overview.weather.storms_often"
                    : planet.StormChance > 0.15 ? "ui.overview.weather.storms_sometimes" : "ui.overview.weather.storms_rare";
            }

            rows.Add(weather);
        }

        // Water and lava.
        if (planet.IsGasWorld)
        {
            rows.Add(Row("water", "ui.overview.water.gas_sea", 2));
        }
        else
        {
            double water = planet.WaterAbundance ?? 0.0;
            var waterRow = water <= 0.0 ? Row("water", "ui.overview.water.none", 3)
                : water < 0.35 ? Row("water", "ui.overview.water.little", 0)
                : Row("water", "ui.overview.water.lots", 0);
            if (planet.WaterDamagePerSecond > 0 && water > 0)
            {
                waterRow.DetailKey = "ui.overview.water.hurts";
                waterRow.Level = 2;
            }

            rows.Add(waterRow);
        }

        double lava = planet.LavaAbundance ?? 0.0;
        if (lava > 0)
        {
            rows.Add(lava < 0.4 ? Row("lava", "ui.overview.lava.little", 1) : Row("lava", "ui.overview.lava.lots", 2));
        }

        // Plants.
        rows.Add(!planet.HasLife ? Row("plants", "ui.overview.plants.none", 3)
            : planet.DeadForests ? Row("plants", "ui.overview.plants.dead", 3)
            : planet.FloraDensity < 0.6 ? Row("plants", "ui.overview.plants.sparse", 0)
            : Row("plants", "ui.overview.plants.dense", 0));

        // Animals — the roster this body rolls, the same one the world will spawn.
        var roster = CreatureGenerator.GenerateRoster(planet, WorldGenerator.RosterSeedFor(_meta.Seed, body.Id),
            _meta.Description.TerrainGeneration, _content.AuthoredCreaturesFor(planet));
        bool dangerous = PlanetEnemiesActive && !planet.PeacefulFauna && roster.Any(sp => sp.Hostile);
        var animals = roster.Count == 0 ? Row("animals", "ui.overview.animals.none", 3)
            : Row("animals", roster.Count >= 5 ? "ui.overview.animals.many" : "ui.overview.animals.few", (byte)(dangerous ? 2 : 0));
        if (roster.Count > 0)
        {
            animals.DetailKey = dangerous ? "ui.overview.animals.dangerous" : "ui.overview.animals.peaceful";
            if (tier >= 2)
            {
                animals.Extra = roster.Count.ToString(CultureInfo.InvariantCulture);
            }
        }

        rows.Add(animals);

        // Guardian machines.
        double enemies = PlanetEnemiesActive ? planet.EnemyDensity : 0.0;
        rows.Add(enemies <= 0 ? Row("machines", "ui.overview.machines.none", 0)
            : enemies < 1.0 ? Row("machines", "ui.overview.machines.few", 1)
            : Row("machines", "ui.overview.machines.many", 2));

        // Terrain.
        rows.Add(planet.IsGasWorld ? Row("terrain", "ui.overview.terrain.no_ground", 2)
            : planet.FloatingIslands ? Row("terrain", "ui.overview.terrain.floating_islands", 3)
            : planet.SandSeaShare > 0 ? Row("terrain", "ui.overview.terrain.sand_sea", 1)
            : planet.Cratered || airlessMoon ? Row("terrain", "ui.overview.terrain.craters", 3)
            : planet.CalmTerrain ? Row("terrain", "ui.overview.terrain.flat", 3)
            : planet.Amplitude >= 20 ? Row("terrain", "ui.overview.terrain.mountains", 3)
            : Row("terrain", "ui.overview.terrain.hills", 3));

        // Structures.
        if (!string.IsNullOrEmpty(planet.CityWorld))
        {
            rows.Add(Row("structures", "ui.overview.structures.city", 3));
        }
        else if (!string.IsNullOrEmpty(planet.FixedName))
        {
            rows.Add(Row("structures", "ui.overview.structures.landmark", 3));
        }
        else if (tier < 2)
        {
            rows.Add(Row("structures", "ui.overview.deeper_scanner", 3));
        }
        else
        {
            var structures = Row("structures", planet.RuinsBias >= 1.0 || planet.FactoriesBias >= 1.0
                ? "ui.overview.structures.ruins_likely" : "ui.overview.structures.ruins_few", 3);
            if (tier >= 3 && planet.DataCacheRarity > 0)
            {
                structures.DetailKey = "ui.overview.structures.data_caches";
            }

            rows.Add(structures);
        }

        // How far out — the frontier tier (richer, and rougher).
        int frontier = FrontierTierForBody(body.Id);
        rows.Add(frontier <= 0 ? Row("frontier", "ui.overview.frontier.home", 0)
            : frontier == 1 ? Row("frontier", "ui.overview.frontier.frontier", 1)
            : Row("frontier", "ui.overview.frontier.deep", 2));

        byte danger = 0;
        foreach (var row in rows)
        {
            if (row.Topic is "air" or "temperature" or "water" or "lava" or "animals" or "machines" or "terrain" && row.Level <= 2)
            {
                danger = System.Math.Max(danger, row.Level);
            }
        }

        return (rows.ToArray(), danger);
    }

    private static NetOverviewRow Row(string topic, string valueKey, byte level)
        => new() { Topic = topic, ValueKey = valueKey, Level = level };

    // ---------------- Test hooks ----------------

    /// <summary>Test seam: the scanner of a player's active ship.</summary>
    internal ShipScannerSpec ShipScannerForTest(string playerId)
    {
        var session = FindSessionByPlayerId(playerId)!;
        Serve(session);
        return ShipScanner(ShipOf(session));
    }

    /// <summary>Test seam: puts an entity into the player's flight instance (a second anomaly, a station contact …).</summary>
    internal void AddSpaceEntityForTest(string playerId, CombatEntity entity)
    {
        if (_playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var instance))
        {
            instance.Entities.Add(entity);
        }
    }

    /// <summary>Test seam: lets the next ship scan through at once (the cooldown is a real-time pacing guard).</summary>
    internal void ResetShipScanCooldownForTest() => _shipScanReadyAt.Clear();

    /// <summary>Test seam: the per-player "already scanned" ids the next space state would carry.</summary>
    internal string[] ScannedIdsForTest(string playerId)
        => _playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var instance)
           && FindSessionByPlayerId(playerId) is { } session
            ? ScannedIdsIn(instance, session)
            : System.Array.Empty<string>();
}
