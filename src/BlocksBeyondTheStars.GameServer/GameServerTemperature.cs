// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Temperature survival hazard (#666–#668) on the server: the facade of <see cref="TemperatureSystem"/> (ADR 0019), which
/// owns the comfort band, the suit drain and the exposure meter, the temperature the status effects feel (#2218) and the
/// local-source, city-shelter and roof probes. This partial keeps the members sibling partials, the bio lab and equipment
/// hosts, the tick and the tests call — same names, same signatures, each one line delegating — and the explicit
/// <see cref="ITemperatureHost"/> implementation.
/// </summary>
public sealed partial class GameServer : ITemperatureHost
{
    // The comfort band's cold edge and the cabin reading, read by sibling partials (the values live in TemperatureSystem).
    private const float ComfortLowC = TemperatureSystem.ComfortLowC;

    /// <summary>The air of a cabin: aboard the ship, inside its hull, on a station — the same reading a void world (a ship
    /// interior, a station deck) gives.</summary>
    internal const float CabinComfortC = TemperatureSystem.CabinComfortC;

    /// <summary>Sun-dependent vacuum reading (#668): ≈ +120 °C in full sunlight down to ≈ −150 °C in
    /// shadow, following the world's day curve — the HUD shows it during EVA instead of "—", and the
    /// hazard treats it like any other temperature (the severity cap bounds both extremes).</summary>
    public static float VacuumTemperature(double timeOfDay) => TemperatureSystem.VacuumTemperature(timeOfDay);

    /// <summary>°C beyond the comfort band (0 = comfortable) for an effective temperature — the pure
    /// band math, exposed for tests.</summary>
    public static float TemperatureSeverityFor(float temperatureC) => TemperatureSystem.TemperatureSeverityFor(temperatureC);

    private void TickTemperature(PlayerSession session, double dt) => _temperatureSystem.TickTemperature(session, dt);

    private float AmbientTemperature(PlayerSession session) => _temperatureSystem.AmbientTemperature(session);

    private float ApplyLocalSources(Shared.Geometry.Vector3f pos, float t) => _temperatureSystem.ApplyLocalSources(pos, t);

    private bool RoofedAt(Shared.Geometry.Vector3f pos) => _temperatureSystem.RoofedAt(pos);

    // ---------------- Test seams ----------------

    /// <summary>Test seam (#2218): the temperature a player's status effects feel right now.</summary>
    public float AmbientTemperatureForTest(PlayerSession session) => AmbientTemperature(session);

    /// <summary>The seconds a bare meter takes to fill here (before the roof halving) — exposed for tests.</summary>
    public double ExposureSecondsForTest(string playerId, bool hot) => _temperatureSystem.ExposureSecondsForTest(playerId, hot);

    /// <summary>Test/util: expose the local-source override (mirrors <see cref="NearHealTankForTest"/>).</summary>
    public float ApplyLocalSourcesForTest(Shared.Geometry.Vector3f pos, float ambientC) => ApplyLocalSources(pos, ambientC);

    /// <summary>Test seam (#1793).</summary>
    public bool InCityShelterForTest(Shared.Geometry.Vector3f pos) => _temperatureSystem.InCityShelterForTest(pos);

    // ITemperatureHost: each member delegates to the server's own member of the same name (never to the interface member,
    // which would recurse).
    bool ITemperatureHost.InStation(string playerId) => InStation(playerId);

    bool ITemperatureHost.InSpsLab(Vector3f pos) => InSpsLab(pos);

    bool ITemperatureHost.InAnyBaseZone(Vector3i cell) => InAnyBaseZone(cell);

    bool ITemperatureHost.InSealedBaseRoom(Vector3i cell) => InSealedBaseRoom(cell);

    double ITemperatureHost.DayFraction => _dayFraction;

    (string State, float Intensity) ITemperatureHost.BiomeWeatherAt(Vector3f pos) => BiomeWeatherAt(pos);

    float ITemperatureHost.CurrentTemperature(string weather, double timeOfDay, Vector3f pos) => CurrentTemperature(weather, timeOfDay, pos);

    WorldGenerator ITemperatureHost.Generator => _generator;

    ushort ITemperatureHost.LavaId => _lavaId;

    ushort ITemperatureHost.FireId => _fireId;

    float ITemperatureHost.ThermalInsulation(PlayerState p) => ThermalInsulation(p);

    float ITemperatureHost.DamagePlayer(PlayerState p, float amount) => DamagePlayer(p, amount);

    void ITemperatureHost.ShipAiHintOnce(PlayerSession session, string hintId) => ShipAiHintOnce(session, hintId);

    void ITemperatureHost.SendVegaLine(PlayerSession session, string lineKey, byte kind) => SendVegaLine(session, lineKey, kind);
}
