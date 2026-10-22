// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The boat (#1215) on the server: its launch, waterline and ashore rules live in the boat section of
/// <see cref="VehicleSystem"/> (ADR 0019). This partial keeps the first-join water-world check, the vehicle-kind helper
/// the test seams read, and the boat test seams.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Vehicle kind of a persisted record — empty (pre-#1215 save) reads as a speeder.</summary>
    private static string VehicleKind(DeployedSpeeder rec) => VehicleSystem.VehicleKind(rec);

    /// <summary>True when the start planet is a water world (the <c>ocean</c> type: water abundance ≥ 1), where a
    /// fresh pilot is handed a boat on first join — a shore with no way across it would be a bad first hour.</summary>
    private bool StartBodyIsWaterWorld
        => (_content.GetPlanet(_meta.DefaultPlanetType)?.WaterAbundance ?? 0.0) >= 1.0;

    // ---------------------------------------------------------------------------------------------
    // Test hooks.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test/util: the persisted vehicle kind of a live vehicle ("speeder" / "boat"), or "" if unknown.</summary>
    public string VehicleKindForTest(string vehicleId)
        => _speeders.Find(v => v.Id == vehicleId) is { } s ? VehicleKind(s.Rec) : string.Empty;

    /// <summary>Test/util: the last pose a live boat was judged to be floating at (the ashore snap-back target),
    /// and whether one is known at all.</summary>
    public (bool Known, Vector3f Pos) BoatWaterPosForTest(string vehicleId)
        => _speeders.Find(v => v.Id == vehicleId) is { } s ? (s.HasWaterPos, s.LastWaterPos) : (false, default);

    /// <summary>Test/util: whether the start planet hands out a boat on first join.</summary>
    public bool StartBodyIsWaterWorldForTest => StartBodyIsWaterWorld;
}
