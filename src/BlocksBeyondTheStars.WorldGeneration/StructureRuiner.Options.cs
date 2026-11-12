// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The steps and settings the options add (#3515): weathering, scorch marks, the rubble palette.</summary>
internal sealed partial class StructureRuiner
{
    // ---------------------------------------------------------------- 4b. weathering

    /// <summary>Each weathering rule turns its block into another with its chance, per cell (the first rule of a block
    /// that hits wins). The cell keeps its shape and loses tint and glow; an empty target takes the block away. Never in
    /// a kept cell or a protected row.</summary>
    private void Weather()
    {
        if (_t.Weathering.Count == 0)
        {
            return;
        }

        for (int x = 0; x < _g.W; x++)
        {
            for (int y = 0; y < _g.H; y++)
            {
                if (Protected(y))
                {
                    continue;
                }

                for (int z = 0; z < _g.L; z++)
                {
                    int i = _g.Index(x, y, z);
                    ushort b = _g.Blocks[i];
                    if (b != 0 && !_g.Keep[i])
                    {
                        WeatherCell(x, y, z, b);
                    }
                }
            }
        }
    }

    private void WeatherCell(int x, int y, int z, ushort block)
    {
        for (int k = 0; k < _t.Weathering.Count; k++)
        {
            var (from, to, chance) = _t.Weathering[k];
            if (from == block && U(LaneWeather + k, x, y, z) < chance)
            {
                _g.Set(x, y, z, to, shape: to != 0 ? _g.ShapeAt(x, y, z) : 0);
                return;
            }
        }
    }

    // ---------------------------------------------------------------- 4c. scorch marks

    /// <summary>Scorch or rust marks: a plain block (no glass, light or ladder) that touches a cell the decay took out is
    /// marked with the near chance, any other with the far one. Never in a kept cell or a protected row.</summary>
    private void ScorchMarks()
    {
        if (_t.Scorch is not { } scorch || _breach is null)
        {
            return;
        }

        for (int x = 0; x < _g.W; x++)
        {
            for (int y = 0; y < _g.H; y++)
            {
                if (Protected(y))
                {
                    continue;
                }

                for (int z = 0; z < _g.L; z++)
                {
                    int i = _g.Index(x, y, z);
                    ushort b = _g.Blocks[i];
                    if (b == 0 || _g.Keep[i] || KindOf(b) != 0)
                    {
                        continue;
                    }

                    double chance = NearBreach(x, y, z) ? scorch.Near : scorch.Far;
                    if (chance > 0 && U(LaneScorch, x, y, z) < chance)
                    {
                        int glow = scorch.Block == 0 && _g.Mods.TryGetValue(i, out var m) ? m.Glow : 0;
                        _g.Set(x, y, z, scorch.Block != 0 ? scorch.Block : b, scorch.Tint, glow, _g.ShapeAt(x, y, z));
                    }
                }
            }
        }
    }

    /// <summary>Whether a cell touching this one (faces, edges or corners) was taken out by the decay.</summary>
    private bool NearBreach(int x, int y, int z)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (_g.In(x + dx, y + dy, z + dz) && _breach![_g.Index(x + dx, y + dy, z + dz)])
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- the rubble palette

    /// <summary>A block of the options' rubble palette for one rubble cell, by weight.</summary>
    private ushort PickRubble(int x, int y, int z)
    {
        int at = (int)(U(LaneRubblePick, x, y, z) * _t.RubbleTotal);
        foreach (var (block, weight) in _t.Rubble)
        {
            at -= weight;
            if (at < 0)
            {
                return block;
            }
        }

        return _t.Rubble[_t.Rubble.Count - 1].Block;
    }

    /// <summary>The options resolved against the content: block ids instead of keys, unknown keys dropped (the default
    /// applies), weights summed.</summary>
    private sealed class Tuning
    {
        public Func<int, int, double>? StrengthAt;
        public int ProtectRows = -1;
        public bool ErodeShaped;
        public ushort Flora;
        public ushort LightOff;
        public List<(ushort Block, int Weight)> Rubble = new();
        public int RubbleTotal;
        public List<(ushort From, ushort To, double Chance)> Weathering = new();
        public (ushort Block, int Tint, double Near, double Far)? Scorch;

        public static Tuning From(RuinOptions o, GameContent content, string surfaceBlock)
        {
            ushort B(string? key) => string.IsNullOrEmpty(key) ? (ushort)0 : content.GetBlock(key!)?.NumericId.Value ?? 0;
            var t = new Tuning
            {
                StrengthAt = o.StrengthAt,
                ProtectRows = System.Math.Max(-1, o.ProtectRowsUpTo),
                ErodeShaped = o.ErodeShaped,
                LightOff = B(o.LightOffBlock),
            };
            ushort fixedFlora = B(o.OvergrowthBlock);
            t.Flora = !o.Overgrowth ? (ushort)0 : fixedFlora != 0 ? fixedFlora : B(SettlementGenerator.BiomeFloraKey(surfaceBlock));
            foreach (var w in o.RubblePalette ?? Array.Empty<RuinWeightedBlock>())
            {
                ushort id = B(w.Block);
                if (id != 0 && w.Weight > 0)
                {
                    t.Rubble.Add((id, w.Weight));
                    t.RubbleTotal += w.Weight;
                }
            }

            foreach (var s in o.Substitutions ?? Array.Empty<RuinSubstitution>())
            {
                ushort from = B(s.From), to = B(s.To);
                if (from != 0 && (to != 0 || s.To.Length == 0) && s.Chance > 0)
                {
                    t.Weathering.Add((from, to, s.Chance));
                }
            }

            if (o.Scorch is { } sc && (B(sc.Block) != 0 || sc.Tint != 0))
            {
                t.Scorch = (B(sc.Block), sc.Tint, sc.NearBreachChance, sc.ElsewhereChance);
            }

            return t;
        }
    }
}
