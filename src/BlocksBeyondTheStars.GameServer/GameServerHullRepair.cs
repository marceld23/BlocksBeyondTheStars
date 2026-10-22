// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The one-time repair of self-built ship hulls an earlier block-adding update left in old block ids (#2751) on the
/// server: the facade of <see cref="HullRepairSystem"/> (ADR 0019), which holds the whole pass. The pass runs once, from
/// <see cref="Start"/>, and keeps no state, so it is built at that one use instead of in a <c>ConstructSystems</c> slot.
/// This partial keeps the entry point and the two constants the tests read — same names, each one line delegating — the
/// launch-problem test seam, and the explicit <see cref="IHullRepairHost"/> implementation.
/// </summary>
public sealed partial class GameServer : IHullRepairHost
{
    /// <summary>The <see cref="WorldMetadata.StampedFeatures"/> entry that marks the hull repair as done for this save.</summary>
    internal const string HullRepairMarker = HullRepairSystem.HullRepairMarker;

    /// <summary>The first terrain generation no build without the hull remap ever created a world with (see
    /// <see cref="HullRepairSystem.FirstTerrainGenerationAfterHullRemap"/>).</summary>
    internal const int FirstTerrainGenerationAfterHullRemap = HullRepairSystem.FirstTerrainGenerationAfterHullRemap;

    /// <summary>Runs the repair once per save (from <see cref="Start"/>, after the palette is current and the metadata is
    /// loaded; the caller persists the metadata with the marker). A backup that fails leaves the save untouched and the
    /// pass due again at the next start.</summary>
    private void RepairLegacyShipHulls() => new HullRepairSystem(this, _content, _repo, _log, _config).RepairLegacyShipHulls();

    /// <summary>Test seam (#2751): the launch problem of a ship's hull (a locale key), or null when it may fly.</summary>
    internal string? CustomShipLaunchProblemForTest(ShipState ship) => CustomShipLaunchProblem(ship);

    // IHullRepairHost: each member delegates to the server's own member of the same name (never to the interface
    // member, which would recurse).
    WorldMetadata IHullRepairHost.Metadata => _meta;

    DateTime IHullRepairHost.BackupUtcNow => BackupUtcNow;
}
