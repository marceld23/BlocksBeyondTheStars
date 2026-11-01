// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's ground detail: the render-only decoration of a chunk's open ground after each upload — the T0
    /// tufts and pebbles (<see cref="GroundScatter"/>, Medium and up) and the High preset's meadow (<see cref="GrassShells"/>,
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
            var scatter = go.GetComponent<GroundScatter>();
            if (data.Scatter != null && data.Scatter.Count > 0)
            {
                if (scatter == null)
                {
                    scatter = go.AddComponent<GroundScatter>();
                }

                scatter.Setup(data.Scatter);
            }
            else if (scatter != null)
            {
                scatter.Setup(null);
            }

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
                var (r, g, b) = FloraTints.For(_worldSeed, LocationName, keys[i]);
                colours[i] = ShaderColor.Srgb(new Color(r, g, b));
            }

            var grass = Content.GetBlock("grass");
            GrassShells.SetFlowerPalette(grass != null ? grass.NumericId.Value : -1, colours);
        }
    }
}
