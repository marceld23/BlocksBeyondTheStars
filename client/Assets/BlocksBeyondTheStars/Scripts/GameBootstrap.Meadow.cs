// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's ground detail: the render-only decoration of a chunk's open ground after each upload — the T0
    /// tufts (<see cref="GroundScatter"/>, Medium and up) and the High preset's meadow (<see cref="GrassShells"/>,
    /// #3056: the grass carpet and the flowers, desktop clients only) — and the world's flower colours.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>(Re)builds a chunk's ground detail from a fresh mesh build. Both components copy what they need, so the
        /// pooled mesh buffers can go back right after; a chunk without points disables them. The browser and phones never
        /// get a meadow component at all (<see cref="HighDetails.DesktopClass"/>); a desktop keeps the tops on every preset,
        /// so switching to High needs no remesh.</summary>
        private void ApplyGroundDetail(ChunkView view, ChunkCoord coord, ChunkMeshData data)
        {
            ApplyScatter(view, data);
            ApplyShells(view, coord, data);

            // Grass clumps: dense clumps of blades on the same carpet tops, with a fringe over open edges.
            if (GameLook.GrassClumps && HighDetails.DesktopClass)
            {
                ApplyGrassClumps(view, coord, data);
            }

            // Plant bodies: the small plants the mesher recorded, drawn as bodies near the camera.
            if (GameLook.PlantBodies && HighDetails.DesktopClass)
            {
                ApplyPlantBodies(view, data);
            }

            // Staged crowns: the crown cells the mesher recorded, drawn as micro-block clusters near the camera.
            if (GameLook.StagedCrowns)
            {
                ApplyCrownClusters(view, coord, data);
            }

            TrackDetail(view); // #4046: in or out of the per-frame detail pass (GameBootstrap.Detail.cs)
        }

        /// <summary>The T0 tufts of a chunk's open ground (Medium and up), or none.</summary>
        private static void ApplyScatter(ChunkView view, ChunkMeshData data)
        {
            if (data.Scatter != null && data.Scatter.Count > 0)
            {
                if (view.Scatter == null)
                {
                    view.Scatter = view.Go.AddComponent<GroundScatter>();
                }

                view.Scatter.Setup(data.Scatter, data.ScatterLight);
            }
            else if (view.Scatter != null)
            {
                view.Scatter.Setup(null);
            }
        }

        /// <summary>The grass carpet and its flowers on a chunk's grass tops (desktop clients only), or none.</summary>
        private void ApplyShells(ChunkView view, ChunkCoord coord, ChunkMeshData data)
        {
            if (HighDetails.DesktopClass && data.GrassTops != null && data.GrassTops.Count > 0)
            {
                if (view.Shells == null)
                {
                    view.Shells = view.Go.AddComponent<GrassShells>();
                }

                GrassShells.UseAtlas(Atlas);
                var origin = WorldConstants.ChunkOrigin(coord);
                view.Shells.Setup(data.GrassTops, new Vector3Int(origin.X, origin.Y, origin.Z), data.GrassLight);
            }
            else if (view.Shells != null)
            {
                view.Shells.Setup(null, default);
            }
        }

        /// <summary>The dense grass clumps on the carpet tops and the ground cover of bare ground, or none.</summary>
        private void ApplyGrassClumps(ChunkView view, ChunkCoord coord, ChunkMeshData data)
        {
            if ((data.GrassTops != null && data.GrassTops.Count > 0) || data.ScrubTops.Count > 0)
            {
                if (view.Clumps == null)
                {
                    view.Clumps = view.Go.AddComponent<GrassClumps>();
                }

                GrassClumps.UseAtlas(Atlas);
                var clumpOrigin = WorldConstants.ChunkOrigin(coord);
                view.Clumps.Setup(data.GrassTops, data.GrassLight, data.GrassEdges,
                    new Vector3Int(clumpOrigin.X, clumpOrigin.Y, clumpOrigin.Z), data.ScrubTops, data.ScrubLight);
            }
            else if (view.Clumps != null)
            {
                view.Clumps.Setup(null, null, null, default);
            }
        }

        /// <summary>The bodies of the small plants the mesher recorded, or none.</summary>
        private void ApplyPlantBodies(ChunkView view, ChunkMeshData data)
        {
            if (data.PlantCells.Count > 0)
            {
                if (view.Plants == null)
                {
                    view.Plants = view.Go.AddComponent<PlantBodies>();
                }

                PlantBodies.UseAtlas(Atlas);
                view.Plants.Setup(data.PlantCells);
            }
            else if (view.Plants != null)
            {
                view.Plants.Setup(null);
            }
        }

        /// <summary>The micro-block clusters of the crown cells the mesher recorded, or none.</summary>
        private static void ApplyCrownClusters(ChunkView view, ChunkCoord coord, ChunkMeshData data)
        {
            if (data.CrownCells.Count > 0)
            {
                if (view.Crowns == null)
                {
                    view.Crowns = view.Go.AddComponent<CrownClusters>();
                }

                var crownOrigin = WorldConstants.ChunkOrigin(coord);
                view.Crowns.Setup(data.CrownCells, new Vector3Int(crownOrigin.X, crownOrigin.Y, crownOrigin.Z));
            }
            else if (view.Crowns != null)
            {
                view.Crowns.Setup(null, default);
            }
        }

        /// <summary>#3056: the meadow flowers bloom in this world's floral colours — the per-world tints of
        /// <see cref="MeadowField.FlowerSpeciesKeys"/>, the same the real flower blocks wear — and grow on plain grass only.
        /// Runs with the flora tints whenever the world or the location changes.</summary>
        private void ApplyMeadowPalette()
        {
            if (Content == null)
            {
                return;
            }

            var keys = MeadowField.FlowerSpeciesKeys;
            var colours = new Color[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                var (r, g, b) = FloraTints.For(_worldSeed, LocationName, keys[i], _floraPlanet, _floraHarmony); // #3423: the type's palette; generation 48: the world's harmony
                colours[i] = ShaderColor.Srgb(new Color(r, g, b));
            }

            var grass = Content.GetBlock("grass");
            GrassShells.SetFlowerPalette(grass != null ? grass.NumericId.Value : -1, colours);
            GrassClumps.SetPalette(colours); // The same flower colours are the grass clumps' accent slot
        }
    }
}
