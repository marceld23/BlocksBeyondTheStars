// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection environment-hazard bookkeeping of a <see cref="PlayerSession"/>
/// (RAM only, never persisted).</summary>
public sealed class PlayerSessionHazards
{
    // --- Heal-tank regen field (base/station life support, issue #460) ---

    /// <summary>Countdown to the next heal-tank proximity rescan (the regen itself applies every tick).</summary>
    public double HealTankScanIn { get; set; }

    /// <summary>Cached result of the last heal-tank proximity scan.</summary>
    public bool NearHealTank { get; set; }

    /// <summary>Cached result of the last bed proximity scan (#804) — same cadence as the tank scan.</summary>
    public bool NearBed { get; set; }

    // --- Temperature hazard (#666): the effective-temperature scan is ~1 Hz, the drain applies every tick ---

    /// <summary>Countdown to the next effective-temperature rescan (block probe + shelter check are the
    /// expensive part; the cached severity is applied every tick in between).</summary>
    public double TemperatureScanIn { get; set; }

    /// <summary>Cached severity (°C beyond the comfort band, shelter applied) from the last scan.</summary>
    public float TemperatureSeverity { get; set; }

    /// <summary>Cached effective temperature (°C) from the last scan — tells VEGA (and the freeze-vs-
    /// overheat hint pick) WHICH extreme is stressing the suit.</summary>
    public float EffectiveTemperatureC { get; set; } = 15f;

    /// <summary>The temperature (°C) the player's status effects feel (#2218): the cabin's comfort aboard a ship or on a
    /// station, the real temperature outside everywhere else — also where the hazard scan above never runs (hazards
    /// off, god mode, the Creative game mode). Read through <c>GameServer.AmbientTemperature</c>, which renews it.</summary>
    public float AmbientTemperatureC { get; set; } = 15f;

    /// <summary>Server uptime of that reading — by the hazard scan or by the effects' own look. Never read yet = −∞.</summary>
    public double AmbientTemperatureReadAt { get; set; } = double.NegativeInfinity;

    /// <summary>Countdown to the next correction of the client's effect countdown while an effect runs out faster in the
    /// heat or the cold (#2218) — the client counts at plain speed between two updates.</summary>
    public double EffectSyncIn { get; set; }

    // --- Exposure meter (2026-09, Titas): scanned at ~1 Hz with the temperature, applied every tick ---

    /// <summary>True while the meter runs (on foot outside on a timed-exposure type) — sent to the HUD.</summary>
    public bool ExposureActive { get; set; }

    /// <summary>The last scan found the player in a hot zone (the heat timer applies).</summary>
    public bool ExposureHot { get; set; }

    /// <summary>The last scan found a roof overhead (the meter fills at half speed).</summary>
    public bool ExposureRoofed { get; set; }

    /// <summary>The last scan found warmth: base air, a campfire, the ground far below the surface.</summary>
    public bool ExposureSheltered { get; set; }

    /// <summary>Highest VEGA warning already given this episode (0 none, 1 = 50 %, 2 = 75 %, 3 = 90 %).</summary>
    public int ExposureWarned { get; set; }

    /// <summary>Seconds the meter has stood at full — the damage rises with it.</summary>
    public double ExposureFullSeconds { get; set; }

    /// <summary>Seconds in a toxic type's water (the damage starts after a grace).</summary>
    public double ToxicWaterSeconds { get; set; }

    /// <summary>The death line of the environment hazard that hurt the player this tick (null = the generic one).</summary>
    public string? HazardDeathReason { get; set; }

    public float LastSentExposure;

    // --- Valuma's mood (2026-09): time on the world, the watched line, the fog ---
    public string MoodLocationId { get; set; } = string.Empty;
    public double MoodSeconds { get; set; }
    public bool MoodWatchedTold { get; set; }
    public bool MoodUneasy { get; set; }
}
