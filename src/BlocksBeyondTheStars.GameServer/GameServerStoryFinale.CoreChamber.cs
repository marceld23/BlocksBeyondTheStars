// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The finale's stage 2 set piece (see <c>GameServerStoryFinale.cs</c>): the buried Guardian-core chamber on the finale body and
/// its two routes in — written into the world once per world (#3513), its runtime state re-derived on every load.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The stamped-feature key of the Guardian-core chamber's blocks (#3513).</summary>
    private const string GuardianCoreFeature = "guardiancore";

    /// <summary>Stamps the buried Guardian-core chamber on the finale body (idempotent per world). The chamber
    /// sits ~24 blocks down with an iron shell and a glowing red core column at its centre (the terminal you
    /// breach). Two routes reach it: <b>Route A</b> drops down a pre-carved 3×3 <b>aperture shaft</b> from the
    /// surface (ringed by plating so the maw is visible); <b>Route B</b> simply mines down through the shell.
    /// The finale body carries no other structures (see the per-world init), so nothing collides with it.</summary>
    private void StampGuardianCoreChamber()
    {
        var aw = _worlds.Active;
        if (aw.GuardianCoreStamped)
        {
            return;
        }

        aw.GuardianCoreStamped = true;
        var planet = _world.Planet;
        if (planet.Void)
        {
            return;
        }

        int ax = WorldConstants.WrapX(48, _world.Circumference);
        int az = 24;
        int surfaceY = _generator.SurfaceHeight(planet, ax, az);
        if (surfaceY < 40)
        {
            surfaceY = 64; // the finale core must bury cleanly even over low terrain
        }

        int floorY = surfaceY - (CoreShaftDepth + 1); // 24 down; the column's floor cell (the centre) 23 below the surface
        aw.CoreChamberCenter = new Vector3i(ax, floorY + 1, az);
        aw.HasCoreChamber = true;

        // #3513: the blocks go in ONCE per world. The in-memory guard above dies with the process, so whatever a player mined
        // or built in the chamber was overwritten on every server start; the stamped-feature registry is the persisted guard.
        // An older save has no mark, but every earlier load of this body wrote the chamber: its blocks are edits in the
        // chamber's chunk already, so the mark is set without writing again (a player's changes there stay). The runtime state
        // above is re-derived on every load like every other structure's.
        if (FindPlacementRecord(GuardianCoreRecordKind, 0) is null)
        {
            RecordPlacement(GuardianCoreRecordKind, 0, new Vector3i(ax, floorY, az), floorY, onIsland: false, "buried", GuardianCoreRecordKind);
        }

        if (!FeatureStamped(GuardianCoreFeature) && _repo.LoadChunkEdits(_world.LocationId, WorldConstants.WorldToChunk(new Vector3i(ax, floorY, az))).Count > 0)
        {
            MarkFeatureStamped(GuardianCoreFeature);
            _log.Info($"The Guardian core chamber on '{_world.LocationId}' stands from an older save — marked as stamped, not written again.");
        }

        if (FeatureStamped(GuardianCoreFeature))
        {
            SavePlacementRecords();
            return;
        }

        // The geometry is GuardianCoreBuilder's (#3522): the chamber, then the aperture's shaft and the plating ring on the
        // natural surface of each column; its core marker is the runtime state above.
        // One transaction: a crash mid-stamp leaves no edit behind, so the next load stamps again instead of taking a half
        // chamber for an older save's.
        _repo.RunInTransaction(() => GuardianCoreBuilder.Build(new WorldCellSink(_world), _content, ax, az, floorY, surfaceY, WrappedSurfaceHeight()));
        MarkFeatureStamped(GuardianCoreFeature); // after the writes: never half-marks (saves the record too)
        _log.Info($"Stamped the Guardian core chamber on '{_world.LocationId}' at ({ax},{floorY},{az}).");
    }
}
