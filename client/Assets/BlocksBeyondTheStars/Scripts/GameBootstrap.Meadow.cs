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
        private void ApplyGroundDetail(GameObject go, ChunkCoord coord, ChunkMeshData data)
        {
            ApplyScatter(go, data);
            ApplyShells(go, coord, data);

            // Grass clumps: dense clumps of blades on the same carpet tops, with a fringe over open edges.
            if (GameLook.GrassClumps && HighDetails.DesktopClass)
            {
                ApplyGrassClumps(go, coord, data);
            }

            // Plant bodies: the small plants the mesher recorded, drawn as bodies near the camera.
            if (GameLook.PlantBodies && HighDetails.DesktopClass)
            {
                ApplyPlantBodies(go, data);
            }

            // Staged crowns: the crown cells the mesher recorded, drawn as micro-block clusters near the camera.
            if (GameLook.StagedCrowns)
            {
                ApplyCrownClusters(go, coord, data);
            }
        }

        /// <summary>The T0 tufts of a chunk's open ground (Medium and up), or none.</summary>
        private static void ApplyScatter(GameObject go, ChunkMeshData data)
        {
            var scatter = go.GetComponent<GroundScatter>();
            if (data.Scatter != null && data.Scatter.Count > 0)
            {
                if (scatter == null)
                {
                    scatter = go.AddComponent<GroundScatter>();
                }

                scatter.Setup(data.Scatter, data.ScatterLight);
            }
            else if (scatter != null)
            {
                scatter.Setup(null);
            }
        }

        /// <summary>The grass carpet and its flowers on a chunk's grass tops (desktop clients only), or none.</summary>
        private void ApplyShells(GameObject go, ChunkCoord coord, ChunkMeshData data)
        {
            var shells = go.GetComponent<GrassShells>();
            if (HighDetails.DesktopClass && data.GrassTops != null && data.GrassTops.Count > 0)
            {
                if (shells == null)
                {
                    shells = go.AddComponent<GrassShells>();
                }

                GrassShells.UseAtlas(Atlas);
                var origin = WorldConstants.ChunkOrigin(coord);
                shells.Setup(data.GrassTops, new Vector3Int(origin.X, origin.Y, origin.Z), data.GrassLight);
            }
            else if (shells != null)
            {
                shells.Setup(null, default);
            }
        }

        /// <summary>The dense grass clumps on the carpet tops and the ground cover of bare ground, or none.</summary>
        private void ApplyGrassClumps(GameObject go, ChunkCoord coord, ChunkMeshData data)
        {
            var clumps = go.GetComponent<GrassClumps>();
            if ((data.GrassTops != null && data.GrassTops.Count > 0) || data.ScrubTops.Count > 0)
            {
                if (clumps == null)
                {
                    clumps = go.AddComponent<GrassClumps>();
                }

                GrassClumps.UseAtlas(Atlas);
                var clumpOrigin = WorldConstants.ChunkOrigin(coord);
                clumps.Setup(data.GrassTops, data.GrassLight, data.GrassEdges,
                    new Vector3Int(clumpOrigin.X, clumpOrigin.Y, clumpOrigin.Z), data.ScrubTops, data.ScrubLight);
            }
            else if (clumps != null)
            {
                clumps.Setup(null, null, null, default);
            }
        }

        /// <summary>The bodies of the small plants the mesher recorded, or none.</summary>
        private void ApplyPlantBodies(GameObject go, ChunkMeshData data)
        {
            var plants = go.GetComponent<PlantBodies>();
            if (data.PlantCells.Count > 0)
            {
                if (plants == null)
                {
                    plants = go.AddComponent<PlantBodies>();
                }

                PlantBodies.UseAtlas(Atlas);
                plants.Setup(data.PlantCells);
            }
            else if (plants != null)
            {
                plants.Setup(null);
            }
        }

        /// <summary>The micro-block clusters of the crown cells the mesher recorded, or none.</summary>
        private static void ApplyCrownClusters(GameObject go, ChunkCoord coord, ChunkMeshData data)
        {
            var crowns = go.GetComponent<CrownClusters>();
            if (data.CrownCells.Count > 0)
            {
                if (crowns == null)
                {
                    crowns = go.AddComponent<CrownClusters>();
                }

                var crownOrigin = WorldConstants.ChunkOrigin(coord);
                crowns.Setup(data.CrownCells, new Vector3Int(crownOrigin.X, crownOrigin.Y, crownOrigin.Z));
            }
            else if (crowns != null)
            {
                crowns.Setup(null, default);
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
