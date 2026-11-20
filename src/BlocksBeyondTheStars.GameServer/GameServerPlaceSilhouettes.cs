// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places from afar (docs/developer/LIVELY_PLACES.md): a settlement's coarse silhouette rides in the landmark
/// list, so the client draws its roofs and its tower beyond the streamed chunks — where a village used to be a bump in
/// the far terrain, one now sees that somebody lives there. The model is the settlement's own baked layout sampled down
/// (<see cref="LandmarkSilhouette"/>), built once per settlement and world load, only from what stands ABOVE each
/// column's floor: the paving and the plinth are the far terrain's, and drawing them twice would only flicker.
/// <para>Render-only like every silhouette. A settlement without a column map (a hand-made single template, a ruin
/// field of an old world) sends none; the budget below keeps a metropolis from growing the world-entry message.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The most model cells one settlement's silhouette may have; a larger one is sampled twice as coarsely.</summary>
    private const int PlaceSilhouetteCells = 24_000;

    /// <summary>What stands above the floor of every column of a settlement's layout, as silhouette cells.</summary>
    private static IEnumerable<(int X, int Y, int Z, ushort Block, int Tint)> CellsAboveTheFloor(SettlementStructure s, sbyte[] floors)
    {
        for (int x = 0; x < s.Width; x++)
        {
            for (int z = 0; z < s.Length; z++)
            {
                int floor = floors[(x * s.Length) + z];
                for (int y = System.Math.Max(0, floor + 1); y < s.Height; y++)
                {
                    ushort block = s.Get(x, y, z);
                    if (block != 0)
                    {
                        yield return (x, y, z, block, s.GetModifier(x, y, z).Tint);
                    }
                }
            }
        }
    }

    /// <summary>The silhouette of a settlement, or null when it has no column map or nothing stands on it.</summary>
    private static LandmarkSilhouette? PlaceSilhouetteOf(SettlementInstance s)
    {
        if (s.SilhouetteBuilt)
        {
            return s.Silhouette;
        }

        s.SilhouetteBuilt = true;
        if (s.Layout is not { ColumnFloors: { } floors } layout)
        {
            return null;
        }

        int step = LandmarkSilhouette.DefaultStep;
        while ((long)((layout.Width / step) + 1) * ((layout.Height / step) + 1) * ((layout.Length / step) + 1) > PlaceSilhouetteCells)
        {
            step *= 2;
        }

        s.Silhouette = LandmarkSilhouette.FromCells(layout.Width, layout.Height, layout.Length, CellsAboveTheFloor(layout, floors), step);
        return s.Silhouette;
    }

    /// <summary>Test seam: gives a settlement registered with <c>RegisterTestSettlementForTest</c> a baked layout (its
    /// silhouette is built from it on the next list).</summary>
    internal void SetSettlementLayoutForTest(string settlementName, SettlementStructure layout)
    {
        foreach (var s in _settlements)
        {
            if (s.Name == settlementName)
            {
                s.Layout = layout;
                s.Silhouette = null;
                s.SilhouetteBuilt = false;
            }
        }
    }

    /// <summary>Test seam: marks a registered settlement a ruin (or lived-in again).</summary>
    internal void SetSettlementRuinedForTest(string settlementName, bool ruined)
    {
        foreach (var s in _settlements)
        {
            if (s.Name == settlementName)
            {
                s.Ruined = ruined;
            }
        }
    }

    /// <summary>The settlements of the active world as far silhouettes (the landmark list's tail).</summary>
    private IEnumerable<NetLandmark> PlaceSilhouettesOfActiveWorld()
    {
        foreach (var s in _settlements)
        {
            if (PlaceSilhouetteOf(s) is not { Blocks.Length: > 0 } model)
            {
                continue;
            }

            var corner = WorldConstants.CanonicalBlock(new Vector3i(s.Min.X, s.GroundY, s.Min.Z), _world.Circumference);
            yield return new NetLandmark
            {
                Key = SettlementPlaceKey(s),
                Kind = PlaceKinds.Settlement,
                Name = s.Name,
                X = corner.X,
                Y = corner.Y,
                Z = corner.Z,
                Step = model.Step,
                Width = model.Width,
                Height = model.Height,
                Length = model.Length,
                Cells = model.Cells,
                Blocks = model.Blocks,
                Tints = model.Tints,
                Ruined = s.Ruined,
            };
        }
    }
}
