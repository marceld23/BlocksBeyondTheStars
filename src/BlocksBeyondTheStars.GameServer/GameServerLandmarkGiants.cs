// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// FALLEN GUARDIAN GIANTS IN A LANDMARK (#3986) — see GameServerLandmarks.cs and GameServerGuardianGiantStamp.cs. A landmark
/// template may carry <see cref="LandmarkRules.GuardianGiantMarker"/>s (the SPS headquarters' great hall has one); each one is
/// a giant like every fallen giant the structure stamp lays in the wilderness, so the scanner, the map marker, the story and
/// the boss logic treat it alike:
/// <list type="bullet">
/// <item><b>The body</b> (<see cref="GuardianGiantBuilder"/>, lights out) is the last part of the landmark's cell list
/// (<see cref="WriteLandmarkCells"/>): it is written once with the landmark — into its structure overlay on a world of
/// generation 39+ — and lies in the landmark's box, so a protected landmark protects it too.</item>
/// <item><b>The record</b> (<see cref="WorldMetadata.GuardianGiants"/>, state <see cref="GuardianGiantRecord.Dormant"/>,
/// <see cref="GuardianGiantRecord.CanWake"/> as the marker says — by default never) is pinned when the landmark is committed —
/// on the stamp and on every later load, so one that went missing comes back; an existing record is never touched (its state
/// is the boss logic's). Its index is <see cref="LandmarkGiantIndexBase"/> + the landmark record's index ×
/// <see cref="LandmarkGiants.MaxPerLandmark"/> + the marker's place — clear of the structure stamp's (0, 1, …) and the admin's
/// (1000 up). Its body seed is the giant sites' hash of that index.</item>
/// <item>Only a landmark proper carries giants (<see cref="LandmarkInstance.RecordIndex"/>); the other kinds on the landmark
/// stack refuse the marker in their own template checks.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The first guardian giant record index of a landmark's giants.</summary>
    internal const int LandmarkGiantIndexBase = 100_000;

    /// <summary>A landmark's giant in the world: its record index, anchor (world cell, X wrapped), facing, body seed and
    /// whether it can wake.</summary>
    private readonly record struct PlacedLandmarkGiant(int Index, Vector3i Anchor, int Facing, int Seed, bool CanWake);

    /// <summary>The guardian giant record index of the <paramref name="ordinal"/>-th giant of landmark record
    /// <paramref name="landmarkIndex"/>.</summary>
    internal static int LandmarkGiantIndex(int landmarkIndex, int ordinal)
        => LandmarkGiantIndexBase + (landmarkIndex * LandmarkGiants.MaxPerLandmark) + ordinal;

    /// <summary>The fallen giants of a landmark proper in world cells — none for any other kind on the landmark stack.</summary>
    private List<PlacedLandmarkGiant> LandmarkGiantsOf(LandmarkInstance lm)
    {
        var placed = new List<PlacedLandmarkGiant>();
        if (lm.RecordIndex is not int landmarkIndex)
        {
            return placed;
        }

        foreach (var g in LandmarkGiants.Of(lm.Layout))
        {
            int index = LandmarkGiantIndex(landmarkIndex, g.Ordinal);
            var cell = lm.Cell(g.Anchor);
            var anchor = new Vector3i(WorldConstants.WrapX(cell.X, _world.Circumference), cell.Y, cell.Z);
            placed.Add(new PlacedLandmarkGiant(index, anchor, g.Facing,
                GuardianGiantSites.BodySeed(_meta.Seed, _world.LocationId, index), g.CanWake));
        }

        return placed;
    }

    /// <summary>The lying bodies of the landmark's giants, lights out, through the landmark's sink. Returns the cells written.</summary>
    private int WriteLandmarkGiants(IStructureCellSink sink, LandmarkInstance lm)
    {
        int cells = 0;
        foreach (var g in LandmarkGiantsOf(lm))
        {
            cells += GuardianGiantBuilder.Build(sink, _content, LandmarkSurfaceBlock(), g.Anchor.X, g.Anchor.Y, g.Anchor.Z, g.Seed,
                g.Facing, lit: false);
        }

        return cells;
    }

    /// <summary>Pins the record of every giant of the landmark that has none yet (dormant, lying where the stamp laid it); an
    /// existing record keeps its state.</summary>
    private void PinLandmarkGiants(LandmarkInstance lm)
    {
        bool added = false;
        foreach (var g in LandmarkGiantsOf(lm))
        {
            string key = GuardianGiantRecord.Key(_world.LocationId, g.Index);
            if (_meta.GuardianGiants.ContainsKey(key))
            {
                continue;
            }

            _meta.GuardianGiants[key] = NewGuardianGiantRecord(g.Index, g.Anchor, g.Facing, g.Seed, g.CanWake);
            _log.Info($"Landmark '{lm.Key}' carries guardian giant '{key}' at {g.Anchor.X},{g.Anchor.Y},{g.Anchor.Z} (facing {g.Facing}" +
                      $"{(g.CanWake ? ", can wake" : string.Empty)}).");
            added = true;
        }

        if (added)
        {
            _placementRecordsDirty = true;
            SavePlacementRecords();
        }
    }
}
