// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Server → client (#2173): the live weather of every body in the receiver's star system — the input the orbit view
/// and the map layers project onto each planet with the shared formula (<c>Shared.Weather.WeatherProjection</c>).
/// The server keeps an ambient weather simulation for every body of an occupied system, so this is the weather you
/// will land in. Sent on entering space, after a hyperjump, with every landing-pad list and as a 10 s heartbeat.
/// </summary>
public sealed class SystemWeather
{
    /// <summary>The star system the bodies belong to.</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>The shared monotonic system clock (system-days) — rotates the per-biome weather offsets.</summary>
    public double SystemTimeDays { get; set; }

    /// <summary>One entry per body with weather.</summary>
    public NetBodyWeather[] Bodies { get; set; } = System.Array.Empty<NetBodyWeather>();
}

/// <summary>One body's world-level weather episode in a <see cref="SystemWeather"/>.</summary>
public sealed class NetBodyWeather
{
    public string BodyId { get; set; } = string.Empty;

    /// <summary>The world-level state key (a ladder state or an event, see <c>Shared.Weather.WeatherCatalog</c>).</summary>
    public string State { get; set; } = "clear";

    /// <summary>Where the rain ladder stands (0 clear .. 3 storm), kept across events.</summary>
    public int LadderSeverity { get; set; }

    /// <summary>The world's ladder band: lowest severity (an overcast world sits at 1).</summary>
    public int LadderFloor { get; set; }

    /// <summary>The world's ladder band: highest severity (airless bodies are pinned at 0).</summary>
    public int LadderCeiling { get; set; } = 3;

    /// <summary>This episode's rolled peak intensity.</summary>
    public float Peak { get; set; }

    /// <summary>The envelope-shaped intensity right now (0..1).</summary>
    public float Intensity { get; set; }

    /// <summary>The episode's rolled precipitation form ("none" when nothing falls).</summary>
    public string Precip { get; set; } = "none";

    /// <summary>Wind strength 0..1.</summary>
    public float WindSpeed { get; set; }

    /// <summary>Wind direction in radians.</summary>
    public float WindDirection { get; set; }

    /// <summary>False on fixed-weather worlds: no position shifts apply.</summary>
    public bool Dynamic { get; set; } = true;

    /// <summary>The body's day fraction (0..1): the live clock of a loaded world, else the arrival time a landing
    /// would start at — so the orbit sphere's lit side is the side you would land on in daylight (#2177).</summary>
    public float TimeOfDay { get; set; } = 0.35f;

    /// <summary>True when the body's world is loaded (its clock is running; the client may extrapolate it).</summary>
    public bool ClockRunning { get; set; }

    /// <summary>The drifting fronts (0..2).</summary>
    public NetWeatherFront[] Fronts { get; set; } = System.Array.Empty<NetWeatherFront>();
}

/// <summary>A drifting weather front: a north–south band along the world's east–west wrap.</summary>
public sealed class NetWeatherFront
{
    /// <summary>Longitude (world X) of the centre.</summary>
    public float CenterX { get; set; }

    /// <summary>True half-width in blocks (the gameplay width; the views may draw it wider).</summary>
    public float HalfWidth { get; set; }

    /// <summary>Drift in blocks per second (signed).</summary>
    public float Drift { get; set; }

    /// <summary>Ladder steps the front adds where it covers (1 or 2).</summary>
    public int Boost { get; set; }
}
