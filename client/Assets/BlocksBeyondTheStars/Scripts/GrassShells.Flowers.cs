// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The meadow flowers (#3056): a ground-scatter kind of the High preset on about one plain-grass top in nine
    /// (<see cref="MeadowField.IsFlowerCell"/>, hashed on the canonical world cell, so every client sees the same flowers).
    /// Each wears one of the four floral species' colours of this world (the same per-world tints the real flower blocks
    /// wear), and the season decides how many bloom — all in spring, fewer in autumn, none in winter. Purely visual: a real
    /// flower block always wins its cell (it is not air, so its cell is never a carpet top), and these cannot be picked.
    /// </summary>
    public sealed partial class GrassShells
    {
        private static readonly int FlowerColorId = Shader.PropertyToID("_FlowerColor");
        private static readonly int FlowerInfoId = Shader.PropertyToID("_FlowerInfo");
        private static readonly int FlowerDensityId = Shader.PropertyToID("_FlowerDensity");
        private static readonly int SeasonId = Shader.PropertyToID("_Sc_Season");
        private static readonly Color[] Palette = new Color[MeadowField.FlowerSpeciesKeys.Length];
        private static int _grassId = -1;
        private static int _paletteVersion;

        private readonly List<Batch> _flowers = new List<Batch>();
        private int _flowerBatches;
        private int _flowersPaletteVersion = -1;
        private bool _flowersDirty = true;

        /// <summary>The world's flower colours, one per <see cref="MeadowField.FlowerSpeciesKeys"/> (shader-ready, i.e. already
        /// linear in a linear project), and the block id of plain grass — the only top that flowers (-1 = none). Set with the
        /// world's flora tints; every chunk re-bakes its flowers on its next draw.</summary>
        public static void SetFlowerPalette(int grassId, Color[] colours)
        {
            _grassId = grassId;
            for (int i = 0; i < Palette.Length; i++)
            {
                Palette[i] = colours != null && i < colours.Length ? colours[i] : Color.white;
            }

            _paletteVersion++;
        }

        /// <summary>The season's share of flowers in bloom, from the eased season look Sky hands the shaders
        /// (<c>_Sc_Season</c>), so the meadow fills and empties with the plants' colours.</summary>
        private static void UpdateFlowerDensity()
        {
            if (!HighDetails.Flowers)
            {
                return;
            }

            var material = MeadowAssets.FlowerMaterial;
            if (material != null)
            {
                var season = Shader.GetGlobalVector(SeasonId);
                material.SetFloat(FlowerDensityId, MeadowField.FlowerDensity(season.x, season.y, season.z));
            }
        }

        private void DrawFlowers()
        {
            var material = MeadowAssets.FlowerMaterial;
            if (material == null || _grassId < 0)
            {
                return;
            }

            if (_flowersDirty || _flowersPaletteVersion != _paletteVersion)
            {
                BuildFlowers();
            }

            var mesh = MeadowAssets.FlowerMesh;
            for (int i = 0; i < _flowerBatches; i++)
            {
                _flowers[i].Draw(mesh, material);
            }
        }

        /// <summary>One instance per flower cell: the flower's own yaw, height and offset, its species colour + bloom rank,
        /// and the light of its cell.</summary>
        private void BuildFlowers()
        {
            _flowersDirty = false;
            _flowersPaletteVersion = _paletteVersion;
            _flowerBatches = 0;
            Batch batch = null;
            for (int i = 0; i < _tops.Count; i++)
            {
                var p = _tops[i];
                int id = (int)p.w;
                if (id != _grassId)
                {
                    continue;
                }

                var cell = WorldCell(p);
                if (!MeadowField.IsFlowerCell(cell.x, cell.y, cell.z))
                {
                    continue;
                }

                if (batch == null || batch.Count == BatchSize)
                {
                    batch?.Commit(FlowerColorId, FlowerInfoId, null);
                    batch = NextBatch(_flowers, ref _flowerBatches, Mathf.Min(BatchSize, _tops.Count - i));
                }

                var f = MeadowField.Flower(cell.x, cell.y, cell.z);
                var at = _bakedPos + new Vector3(p.x + f.OffsetX, p.y, p.z + f.OffsetZ);
                var colour = Palette[f.Species];
                var bl = i < _light.Count ? _light[i] : Vector3.zero;
                batch.Add(Matrix4x4.TRS(at, Quaternion.Euler(0f, f.Yaw, 0f), Vector3.one * f.Height),
                    new Vector4(colour.r, colour.g, colour.b, f.Rank),
                    new Vector4(p.w - id, bl.x, bl.y, bl.z),
                    default);
            }

            batch?.Commit(FlowerColorId, FlowerInfoId, null);
        }
    }
}
