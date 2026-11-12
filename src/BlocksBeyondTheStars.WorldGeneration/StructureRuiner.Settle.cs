// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The cave-ins, the settle step and what lies on the ground afterwards (#3487).</summary>
internal sealed partial class StructureRuiner
{
    /// <summary>How far below the ground row a cave-in may start — the way in stays open.</summary>
    private const int CaveInClearance = 5;

    // ---------------------------------------------------------------- 5. cave-ins

    /// <summary>Profile <c>underground</c> only: a few stretches of the tunnels and caverns fill with rubble. The rock
    /// around them is the planet's own and stays; a marker's cell is never filled.</summary>
    private void CaveIns()
    {
        if (_profile != LandmarkRules.ProfileUnderground || _rock == 0)
        {
            return;
        }

        var cells = new List<int>();
        int layer = _g.H * _g.L;
        for (int i = 0; i < _g.Blocks.Length; i++)
        {
            if (_g.Cavity[i] && (i % layer) / _g.L <= _ground - CaveInClearance)
            {
                cells.Add(i);
            }
        }

        if (cells.Count == 0)
        {
            return;
        }

        int spots = System.Math.Clamp((int)System.Math.Round(_s * 8 * cells.Count / 4000.0), 1, 16);
        for (int k = 0; k < spots; k++)
        {
            int i = cells[(int)(U(LaneCaveIn, k, 0, 0) * cells.Count)];
            int r = 2 + (int)(U(LaneCaveIn, k, 1, 0) * 3);
            FillBall(i / layer, (i % layer) / _g.L, i % _g.L, r);
        }
    }

    private void FillBall(int cx, int cy, int cz, int r)
    {
        for (int x = cx - r; x <= cx + r; x++)
        {
            for (int y = cy - r; y <= cy + r; y++)
            {
                for (int z = cz - r; z <= cz + r; z++)
                {
                    if (!_g.In(x, y, z) || ((x - cx) * (x - cx)) + ((y - cy) * (y - cy)) + ((z - cz) * (z - cz)) > r * r)
                    {
                        continue;
                    }

                    int i = _g.Index(x, y, z);
                    if (_g.Cavity[i] && !_markerCell[i] && U(LaneCaveFill, x, y, z) < 0.8 && !Protected(y) && !Spared(x, y, z))
                    {
                        _g.Cavity[i] = false;
                        _g.Set(x, y, z, _rock);
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- 6. settle

    /// <summary>The 18 neighbours a block may hang on: the six faces and the twelve edges — a stepped arch and a
    /// diagonal lattice member hold, a stone that only touches a corner does not.</summary>
    private static readonly (int X, int Y, int Z)[] Neighbours = BuildNeighbours();

    private static (int X, int Y, int Z)[] BuildNeighbours()
    {
        var list = new List<(int, int, int)>();
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    int axes = System.Math.Abs(x) + System.Math.Abs(y) + System.Math.Abs(z);
                    if (axes == 1 || axes == 2)
                    {
                        list.Add((x, y, z));
                    }
                }
            }
        }

        return list.ToArray();
    }

    /// <summary>Whatever no longer hangs together with the ground comes down: a flood fill from the anchors — every
    /// block at or below the ground row (it sits in the rock), in a protected row and every kept cell; for a hull, which
    /// has no ground, also the largest piece that is left — through touching blocks; the rest falls.</summary>
    private void Settle()
    {
        var held = new BitArray(_g.Blocks.Length);
        var open = new Stack<int>();
        int layer = _g.H * _g.L;
        int anchoredUpTo = System.Math.Max(_ground, _t.ProtectRows);
        for (int i = 0; i < _g.Blocks.Length; i++)
        {
            if (_g.Blocks[i] != 0 && ((i % layer) / _g.L <= anchoredUpTo || _g.Keep[i]))
            {
                held[i] = true;
                open.Push(i);
            }
        }

        if (_hull)
        {
            AnchorLargestPiece(held, open);
        }

        Spread(held, open);
        for (int i = 0; i < _g.Blocks.Length; i++)
        {
            if (_g.Blocks[i] != 0 && !held[i])
            {
                Fell(i / layer, (i % layer) / _g.L, i % _g.L);
            }
        }
    }

    /// <summary>Marks every block that touches a marked one (faces and edges), from the cells on the stack, until
    /// nothing is left to mark. Returns how many it marked.</summary>
    private int Spread(BitArray marked, Stack<int> open)
    {
        int layer = _g.H * _g.L, count = 0;
        while (open.Count > 0)
        {
            int i = open.Pop();
            int x = i / layer, y = (i % layer) / _g.L, z = i % _g.L;
            foreach (var (dx, dy, dz) in Neighbours)
            {
                int nx = x + dx, ny = y + dy, nz = z + dz;
                if (!_g.In(nx, ny, nz))
                {
                    continue;
                }

                int n = _g.Index(nx, ny, nz);
                if (_g.Blocks[n] != 0 && !marked[n])
                {
                    marked[n] = true;
                    open.Push(n);
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>A hull (#3515) has no ground to hang on: the largest piece of touching blocks that is left (the first in
    /// cell order on a tie) holds, like a kept cell.</summary>
    private void AnchorLargestPiece(BitArray held, Stack<int> open)
    {
        var seen = new BitArray(_g.Blocks.Length);
        var stack = new Stack<int>();
        int best = -1, bestSize = 0;
        for (int i = 0; i < _g.Blocks.Length; i++)
        {
            if (_g.Blocks[i] == 0 || seen[i])
            {
                continue;
            }

            seen[i] = true;
            stack.Push(i);
            int size = 1 + Spread(seen, stack);
            if (size > bestSize)
            {
                (best, bestSize) = (i, size);
            }
        }

        if (best >= 0 && !held[best])
        {
            held[best] = true;
            open.Push(best);
        }
    }

    // ---------------------------------------------------------------- 7. the ground

    /// <summary>How far above the ground row rubble may come to lie (on a low wall stump, not on a standing wall).</summary>
    private const int RubbleReach = 3;

    /// <summary>Rubble where something came down: one or two blocks of what fell (the planet's rock when only glass
    /// and fittings fell) or of the options' palette, on the lowest free cell of the column near the ground. Never in a
    /// marker's cell or a protected row.</summary>
    private void Rubble()
    {
        for (int x = 0; x < _g.W; x++)
        {
            for (int z = 0; z < _g.L; z++)
            {
                int c = Column(x, z);
                ushort block = _fallenBlock[c] != 0 ? _fallenBlock[c] : _rock;
                if (_fallen[c] == 0 || (block == 0 && _t.RubbleTotal == 0) || U(LaneRubble, x, 0, z) >= 0.25 + (0.45 * S(x, z)))
                {
                    continue;
                }

                int y = _ground;
                while (y <= _ground + RubbleReach && _g.Solid(x, y, z))
                {
                    y++;
                }

                int pile = _fallen[c] >= 24 && U(LaneRubble, x, 1, z) < 0.5 ? 2 : 1;
                for (int k = 0; k < pile && y + k <= _ground + RubbleReach && y + k < _g.H; k++)
                {
                    int i = _g.Index(x, y + k, z);
                    if (!_markerCell[i] && !_g.Cavity[i] && !Protected(y + k)) // a marker's cell and the mouth of a shaft stay free
                    {
                        _g.Set(x, y + k, z, _t.RubbleTotal > 0 ? PickRubble(x, y + k, z) : block);
                    }
                }
            }
        }
    }

    /// <summary>The biome's flora (or the options' flora) pushes through the floor: on a share of the free cells right
    /// above a block of the ground row. Never in a marker's cell or a protected row.</summary>
    private void Overgrowth()
    {
        if (_t.Flora == 0 || _ground + 1 >= _g.H || Protected(_ground + 1))
        {
            return;
        }

        for (int x = 0; x < _g.W; x++)
        {
            for (int z = 0; z < _g.L; z++)
            {
                if (_g.Solid(x, _ground, z) && !_g.Solid(x, _ground + 1, z)
                    && !_markerCell[_g.Index(x, _ground + 1, z)] && U(LaneFlora, x, 0, z) < 0.04 + (0.08 * S(x, z)))
                {
                    _g.Set(x, _ground + 1, z, _t.Flora);
                }
            }
        }
    }
}
