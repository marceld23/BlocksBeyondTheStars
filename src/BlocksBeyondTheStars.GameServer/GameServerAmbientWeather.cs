// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Weather;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Ambient weather (#2173): every body of a star system with a player in it — on a surface, aboard a station or in
/// space — carries a live <see cref="WeatherSim"/>, not only the loaded worlds. A body whose world is loaded ticks
/// its sim through <c>TickWeather</c> as before; every other body is advanced here at a coarse 1 Hz. Both are the
/// SAME object: loading a world adopts its body's ambient sim (<c>InitWeather</c>), so the storm seen from orbit is
/// the storm you land in, and unloading hands it back without a reset.
/// <para>The clients get the system's weather as one <see cref="SystemWeather"/> snapshot (orbit cloud shells, map
/// layers, the time of day each sphere turns to) and the weather at every landing pad with the pad list. Runtime
/// only, like the shared orbital clock: a restart starts every body fresh.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds between two advances of the bodies nobody stands on (cheap: one sim step per body).</summary>
    private const double AmbientWeatherStepSeconds = 1.0;

    /// <summary>Seconds between two <see cref="SystemWeather"/> heartbeats to every joined player.</summary>
    private const double SystemWeatherHeartbeatSeconds = 10.0;

    /// <summary>One body's ambient weather and the per-world inputs its sim needs while no world is loaded.</summary>
    internal sealed class BodyWeather
    {
        public string BodyId = string.Empty;
        public string SystemId = string.Empty;
        public PlanetType Planet = null!;
        public int Circumference;
        public WeatherSim Sim = null!;
        public double AtmosphereDensity;
        public bool Airless;
        public bool Toxic;
    }

    private readonly Dictionary<string, BodyWeather> _bodyWeather = new(StringComparer.Ordinal);
    private readonly HashSet<string> _weatherTicked = new(StringComparer.Ordinal); // worlds TickWeather advances this tick
    private double _sinceAmbientWeather;
    private double _sinceSystemWeather;

    /// <summary>Test seam: the ambient weather of a body (null when the body has none yet).</summary>
    public WeatherSim? BodyWeatherSimForTest(string bodyId)
        => _bodyWeather.TryGetValue(bodyId, out var bw) ? bw.Sim : null;

    /// <summary>Test seam: the weather the active world reports at a position (the shared per-position formula).</summary>
    public (string State, float Intensity) WeatherAtForTest(BlocksBeyondTheStars.Shared.Geometry.Vector3f pos) => BiomeWeatherAt(pos);

    /// <summary>Test seam: the active world's cloud tint (0xRRGGBB).</summary>
    public int CloudColorForTest => _cloudColor;

    /// <summary>Test seam: whether a body's world is resident.</summary>
    public bool HasWorldLoadedForTest(string bodyId) => _worlds.IsLoaded(bodyId);

    /// <summary>Test seam: the shared orbital clock (system-days).</summary>
    public double SystemTimeDaysForTest => _systemTimeDays;

    /// <summary>Advances the ambient weather of every body in an occupied system whose world is not loaded, and sends
    /// the heartbeat snapshot. Runs once per server tick (after the per-world loop).</summary>
    private void TickAmbientWeather(double dt)
    {
        _sinceAmbientWeather += dt;
        if (_sinceAmbientWeather >= AmbientWeatherStepSeconds)
        {
            double step = _sinceAmbientWeather;
            _sinceAmbientWeather = 0;
            foreach (var system in OccupiedSystems())
            {
                foreach (var body in system.Bodies)
                {
                    var bw = EnsureBodyWeather(body);
                    if (bw is null || _weatherTicked.Contains(body.Id))
                    {
                        continue; // no weather here, or its world's TickWeather advances this very sim itself
                    }

                    bw.Sim.Advance(step, AmbientWeatherCtx(bw));
                }
            }
        }

        _sinceSystemWeather += dt;
        if (_sinceSystemWeather >= SystemWeatherHeartbeatSeconds)
        {
            _sinceSystemWeather = 0;
            BroadcastSystemWeather();
        }
    }

    /// <summary>The star systems a joined player is in (via their location's body; a station world resolves to the
    /// body it orbits).</summary>
    private List<StarSystem> OccupiedSystems()
    {
        var result = new List<StarSystem>();
        foreach (var s in _sessions.Values)
        {
            if (!s.Joined)
            {
                continue;
            }

            var system = SystemOf(s);
            if (system is not null && !result.Contains(system))
            {
                result.Add(system);
            }
        }

        return result;
    }

    private StarSystem? SystemOf(PlayerSession session)
    {
        var body = ResolveLocationBody(session.CurrentLocationId);
        if (body is null || string.IsNullOrEmpty(body.SystemId))
        {
            return null;
        }

        foreach (var sys in _galaxy.Systems)
        {
            if (string.Equals(sys.Id, body.SystemId, StringComparison.Ordinal))
            {
                return sys;
            }
        }

        return null;
    }

    /// <summary>The ambient weather of a body, created on first use with exactly the seed and set-up a world load
    /// would give it. Null for bodies without a surface world (stations, wrecks) or with a fixed-weather type.</summary>
    private BodyWeather? EnsureBodyWeather(CelestialBody body)
    {
        if (_bodyWeather.TryGetValue(body.Id, out var existing))
        {
            return existing;
        }

        if (body.Kind is not (CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField))
        {
            return null;
        }

        var planet = _content.GetPlanet(body.PlanetType ?? string.Empty);
        if (planet is null || planet.Void)
        {
            return null;
        }

        // A loaded world already owns a sim — adopt THAT one (a body visited before the ambient table knew it).
        var loaded = _worlds.Find(body.Id);
        bool airless = planet.IsAirless || planet.SpaceSky;
        var bw = new BodyWeather
        {
            BodyId = body.Id,
            SystemId = body.SystemId,
            Planet = planet,
            Circumference = WorldConstants.CircumferenceFor(body.Id, WorldConstants.SizeClassFor(body.Kind, planet.Key), body.SizeBias),
            AtmosphereDensity = AtmosphereDensityFor(planet, airless, body.Id),
            Airless = airless,
            Toxic = !airless && !string.Equals(planet.Atmosphere, "breathable", StringComparison.OrdinalIgnoreCase),
        };

        if (loaded is not null && string.Equals(loaded.PlanetType, planet.Key, StringComparison.Ordinal))
        {
            bw.Sim = loaded.Weather;
        }
        else
        {
            bw.Sim = NewWeatherSim(body.Id, planet, airless);
            bw.Sim.Start(AmbientWeatherCtx(bw));
        }

        _bodyWeather[body.Id] = bw;
        return bw;
    }

    /// <summary>The weather model's inputs for a body nobody stands on: the type's data, the seeded air density and
    /// the arrival clock (a world loads at <see cref="InitialDayFraction"/>, so that is the hour it waits at).</summary>
    private WeatherContext AmbientWeatherCtx(BodyWeather bw) => new()
    {
        StormChance = bw.Planet.StormChance,
        AtmosphereDensity = bw.AtmosphereDensity,
        DayFraction = InitialDayFraction,
        SystemTimeDays = _systemTimeDays,
        Airless = bw.Airless,
        Toxic = bw.Toxic,
        BaseTemperature = bw.Planet.BaseTemperature,
        PlanetKey = bw.Planet.Key,
        EventWeights = bw.Planet.WeatherEvents,
        Circumference = bw.Circumference,
        Dynamic = true,
    };

    /// <summary>The snapshot of one system's weather: every body with weather, loaded or ambient.</summary>
    private SystemWeather BuildSystemWeather(StarSystem system)
    {
        var bodies = new List<NetBodyWeather>(system.Bodies.Count);
        foreach (var body in system.Bodies)
        {
            var bw = EnsureBodyWeather(body);
            if (bw is null)
            {
                continue;
            }

            var loaded = _worlds.Find(body.Id);
            var sim = loaded?.Weather ?? bw.Sim;
            var fronts = new NetWeatherFront[sim.Fronts.Count];
            for (int i = 0; i < fronts.Length; i++)
            {
                var f = sim.Fronts[i];
                fronts[i] = new NetWeatherFront
                {
                    CenterX = (float)f.CenterX,
                    HalfWidth = (float)f.HalfWidth,
                    Drift = (float)f.Drift,
                    Boost = f.Boost,
                };
            }

            bodies.Add(new NetBodyWeather
            {
                BodyId = body.Id,
                State = sim.State,
                LadderSeverity = sim.LadderSeverity,
                LadderFloor = sim.LadderFloor,
                LadderCeiling = sim.LadderCeiling,
                Peak = sim.Peak,
                Intensity = sim.Intensity,
                Precip = sim.Precip,
                WindSpeed = sim.WindSpeed,
                WindDirection = sim.WindDirection,
                Dynamic = loaded is null || loaded.PlanetWeatherMode == "dynamic",
                TimeOfDay = (float)(loaded?.DayFraction ?? InitialDayFraction),
                ClockRunning = loaded is not null,
                Fronts = fronts,
            });
        }

        return new SystemWeather { SystemId = system.Id, SystemTimeDays = _systemTimeDays, Bodies = bodies.ToArray() };
    }

    /// <summary>Sends a player the weather of the system they are in (no-op outside any system).</summary>
    private void SendSystemWeather(PlayerSession session)
    {
        var system = SystemOf(session);
        if (system is not null)
        {
            Send(session, BuildSystemWeather(system));
        }
    }

    /// <summary>The heartbeat: each occupied system's snapshot is built once and sent to everyone in it.</summary>
    private void BroadcastSystemWeather()
    {
        var built = new Dictionary<string, SystemWeather>(StringComparer.Ordinal);
        foreach (var s in _sessions.Values)
        {
            if (!s.Joined)
            {
                continue;
            }

            var system = SystemOf(s);
            if (system is null)
            {
                continue;
            }

            if (!built.TryGetValue(system.Id, out var msg))
            {
                msg = BuildSystemWeather(system);
                built[system.Id] = msg;
            }

            Send(s, msg);
        }
    }

    /// <summary>Fills each pad's weather (#2173): the state and precipitation the surface would report standing on
    /// it — the shared per-position formula on the body's live weather, with the body's OWN terrain (the shared
    /// generator is switched to the body for the queries and restored afterwards, as the pad search does).</summary>
    private void FillPadWeather(CelestialBody body, PlanetType planet, int circ, List<LandingPad> computed, NetLandingPad[] pads)
    {
        var bw = EnsureBodyWeather(body);
        if (bw is null)
        {
            return;
        }

        var loaded = _worlds.Find(body.Id);
        var sim = loaded?.Weather ?? bw.Sim;
        bool dynamic = loaded is null || loaded.PlanetWeatherMode == "dynamic";
        var episode = EpisodeOf(sim, dynamic);
        double timeOfDay = BodyArrivalTimeOfDay(body.Id);
        double cloudLine = WeatherProjection.CloudLineY(planet);
        bool breathable = string.Equals(planet.Atmosphere, "breathable", StringComparison.OrdinalIgnoreCase);
        bool ladder = WeatherCatalog.Find(sim.State)?.IsLadder ?? true;

        bool airlessMoon = body.Kind == CelestialKind.Moon
            && string.Equals(planet.Atmosphere, "none", StringComparison.OrdinalIgnoreCase);
        // A generator of its own for that body (#2235), with the levelled pads the loaded world will have.
        var previousOverride = _bodyGeneratorOverride;
        _bodyGeneratorOverride = BodyGenerator(circ, airlessMoon, PadFlats(computed), body.Id);
        try
        {
            for (int i = 0; i < computed.Count && i < pads.Length; i++)
            {
                var p = computed[i];
                int standY = p.CenterY + 1;
                int offset = 0, boost = 0;
                bool summit = false;
                if (dynamic && ladder)
                {
                    int biome = _generator.BiomeIndexAt(planet, p.CenterX, p.CenterZ);
                    offset = WeatherProjection.BiomeOffset(_meta.Seed, biome, _systemTimeDays);
                    boost = sim.FrontBoostAt(p.CenterX, circ);
                    summit = standY > cloudLine;
                }

                var (state, _) = WeatherProjection.At(episode, offset, boost, summit);
                float temp = TemperatureAt(planet, sim, state, timeOfDay, breathable,
                    new BlocksBeyondTheStars.Shared.Geometry.Vector3f(p.CenterX, standY, p.CenterZ));
                pads[i].Weather = state;
                pads[i].Precipitation = WeatherProjection.Precipitation(state, planet, temp, sim.Precip);
            }
        }
        finally
        {
            _bodyGeneratorOverride = previousOverride;
        }
    }

    /// <summary>A live sim reduced to the projection's episode.</summary>
    private static WeatherEpisode EpisodeOf(WeatherSim sim, bool dynamic)
        => new(sim.State, sim.LadderSeverity, sim.LadderFloor, sim.LadderCeiling, sim.Peak, sim.Intensity, dynamic);
}
