// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The dressing of a baked plan (#3587): lamps at the junctions, gardens behind the houses, the plaza's well, the
/// village green's trees, the squares. Every draw is a hash of the seed and the column, never a stream.</summary>
internal sealed partial class PlanBake
{
    private void Dress()
    {
        DressPlaza();
        DressGreen();
        PlaceLamps();
        PlaceGardens();
    }

    private double Hash(string key) => StableHashing.Unit(key, _seed);

    /// <summary>A well, a monument or a planted bed at the plaza's centre (a village green dresses itself).</summary>
    private void DressPlaza()
    {
        int cx = _plan.PlazaX, cz = _plan.PlazaZ;
        if (_plan.KindAt(cx, cz) != SettlementPlan.Plaza)
        {
            return;
        }

        int y = _plan.HeightAt(cx, cz) + 1;
        ushort accent = _materials.Accent;
        switch (StableHashing.Index("plan:plaza", _seed, 3))
        {
            case 0:
                Ring(cx, y, cz, accent);
                Set(cx, y, cz, B("water"));
                break;
            case 1:
                Set(cx, y, cz, accent);
                Set(cx, y + 1, cz, accent);
                Set(cx, y + 2, cz, Lamp);
                break;
            default:
                Set(cx, y, cz, Flora);
                Set(cx - 1, y, cz - 1, Lamp);
                Set(cx + 1, y, cz + 1, Lamp);
                break;
        }
    }

    private void Ring(int cx, int y, int cz, ushort block)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx != 0 || dz != 0)
                {
                    Set(cx + dx, y, cz + dz, block);
                }
            }
        }
    }

    /// <summary>A village green: a few trees and flowers on the grass, a pond in the middle of a long one.</summary>
    private void DressGreen()
    {
        if (_plan.Form != "green")
        {
            return;
        }

        for (int x = 1; x < _w - 1; x++)
        {
            for (int z = 1; z < _l - 1; z++)
            {
                if (_plan.KindAt(x, z) != SettlementPlan.Green || IsSquare(x, z))
                {
                    continue;
                }

                int y = _plan.HeightAt(x, z) + 1;
                double r = Hash($"plan:green:{x}:{z}");
                if (r < 0.035 && Get(x, y, z) == 0)
                {
                    Tree(x, y, z);
                }
                else if (r < 0.2 && Get(x, y, z) == 0)
                {
                    Set(x, y, z, Flora);
                }
            }
        }

        Set(_plan.PlazaX, _plan.HeightAt(_plan.PlazaX, _plan.PlazaZ), _plan.PlazaZ, B("water"));
    }

    private bool IsSquare(int x, int z)
    {
        foreach (var lot in _plan.Lots)
        {
            if (lot.Module.Length == 0 && lot.Covers(x, z))
            {
                return true;
            }
        }

        return false;
    }

    private void Tree(int x, int y, int z)
    {
        ushort log = B("wood_log"), leaves = B("tree_leaves");
        if (log == 0 || leaves == 0 || y + 5 >= _h)
        {
            return;
        }

        for (int k = 0; k < 3; k++)
        {
            Set(x, y + k, z, log);
        }

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                Set(x + dx, y + 3, z + dz, leaves);
            }
        }

        Set(x, y + 4, z, leaves);
    }

    /// <summary>A square (an empty lot of a lived-in place): a well or a planted bed in its middle.</summary>
    private void DressSquare(PlanLot lot)
    {
        int cx = lot.X + (lot.W / 2), cz = lot.Z + (lot.L / 2), y = lot.Y + 1;
        if (Hash($"plan:square:{lot.X}:{lot.Z}") < 0.5)
        {
            Ring(cx, y, cz, _materials.Accent);
            Set(cx, y, cz, B("water"));
            return;
        }

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                Set(cx + dx, y, cz + dz, Flora);
            }
        }
    }

    /// <summary>Lamp posts beside the road junctions and bends, on yard or open ground that is no footpath's neighbour — the
    /// column is claimed for it at the road's height. At most a handful per tier.</summary>
    private void PlaceLamps()
    {
        int budget = _tier switch { "hamlet" => 3, "town" => 10, "city" => 14, _ => 6 };
        for (int x = 1; x < _w - 1 && budget > 0; x++)
        {
            for (int z = 1; z < _l - 1 && budget > 0; z++)
            {
                if (!SettlementPlan.IsRoad(_plan.KindAt(x, z)) || _plan.KindAt(x, z) == SettlementPlan.Plaza || RoadNeighbours(x, z) < 3
                    || Hash($"plan:lamp:{x}:{z}") > 0.5)
                {
                    continue;
                }

                if (TryLampBeside(x, z))
                {
                    budget--;
                }
            }
        }
    }

    private int RoadNeighbours(int x, int z)
    {
        int n = 0;
        foreach (var (nx, nz) in new[] { (x - 1, z), (x + 1, z), (x, z - 1), (x, z + 1) })
        {
            n += SettlementPlan.IsRoad(_plan.KindAt(nx, nz)) ? 1 : 0;
        }

        return n;
    }

    private bool TryLampBeside(int x, int z)
    {
        foreach (var (nx, nz) in new[] { (x - 1, z - 1), (x + 1, z - 1), (x - 1, z + 1), (x + 1, z + 1) })
        {
            byte kind = _plan.KindAt(nx, nz);
            if (!_plan.InBox(nx, nz) || (kind != SettlementPlan.Unclaimed && kind != SettlementPlan.Yard) || NextToFootpath(nx, nz))
            {
                continue;
            }

            int y = _plan.HeightAt(x, z);
            if (kind == SettlementPlan.Unclaimed)
            {
                _floors[(nx * _l) + nz] = (sbyte)y;
            }
            else if (_plan.HeightAt(nx, nz) != y)
            {
                continue;
            }

            Set(nx, y + 1, nz, Lamp);
            Set(nx, y + 2, nz, Lamp);
            return true;
        }

        return false;
    }

    private bool NextToFootpath(int x, int z)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (_plan.KindAt(x + dx, z + dz) is SettlementPlan.Footpath or SettlementPlan.LotColumn)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>A few planted cells in the yard behind every lived-in building (the side away from its road).</summary>
    private void PlaceGardens()
    {
        foreach (var lot in _plan.Lots)
        {
            if (lot.Module.Length == 0 || StructureRoles.Ring(lot.Function) == 2)
            {
                continue;
            }

            var (bx, bz) = BackRow(lot);
            for (int k = 0; k < (_alien ? 4 : 2); k++)
            {
                int i = StableHashing.Index($"plan:garden:{lot.X}:{lot.Z}:{k}", _seed, System.Math.Max(1, bx.Length));
                int x = bx[i], z = bz[i];
                if (_plan.KindAt(x, z) == SettlementPlan.Yard && Get(x, lot.Y + 1, z) == 0)
                {
                    Set(x, lot.Y + 1, z, Flora);
                }
            }
        }
    }

    /// <summary>The yard row behind a lot: the side opposite its front, read from the turns (a module's front faces −Z
    /// before turning).</summary>
    private static (int[] Xs, int[] Zs) BackRow(PlanLot lot)
    {
        int front = lot.Turns switch { 0 => 0, 1 => 3, 2 => 1, _ => 2 };
        int n = front < 2 ? lot.W : lot.L;
        var xs = new int[n];
        var zs = new int[n];
        for (int i = 0; i < n; i++)
        {
            (xs[i], zs[i]) = front switch
            {
                0 => (lot.X + i, lot.Z + lot.L),
                1 => (lot.X + i, lot.Z - 1),
                2 => (lot.X + lot.W, lot.Z + i),
                _ => (lot.X - 1, lot.Z + i),
            };
        }

        return (xs, zs);
    }
}
