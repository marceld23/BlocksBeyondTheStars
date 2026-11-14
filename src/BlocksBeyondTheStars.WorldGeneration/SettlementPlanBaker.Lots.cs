// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The lots of a baked plan (#3584, #3585, #3595): each module turned and stamped on its floor, its markers.</summary>
internal sealed partial class PlanBake
{
    private readonly Dictionary<(string Key, int Turns), StructureTemplate> _turned = new();

    private void StampLot(PlanLot lot)
    {
        if (lot.Module.Length == 0)
        {
            DressSquare(lot);
            return;
        }

        if (SettlementPlanner.ModuleByKey(_pool, lot.Module) is not { } module)
        {
            _warn?.Invoke($"Planned module '{lot.Module}' is missing from the pool — its lot stays an empty yard.");
            return;
        }

        if (!_turned.TryGetValue((lot.Module, lot.Turns), out var turned))
        {
            turned = TemplateTransform.RotateY(module, lot.Turns);
            _turned[(lot.Module, lot.Turns)] = turned;
        }

        var local = new List<SettlementMarker>();
        long furnish = WorldGenerator.StableHash($"furnish:plan:{_seed}:{lot.X}:{lot.Z}");
        SettlementGenerator.StampModule(turned, lot.X, lot.Y, lot.Z, _content, Get, _sink, local,
            RoomFurnisher.PaletteFor(RoomFurnisher.StyleFor(_tier, _alien), _content), furnish, _materials, markerSettings: true,
            keep: (x, y, z) => _keep.Add((((x * _h) + y) * _l) + z));
        _buildings++;
        AddLotMarkers(lot, turned, local);
    }

    /// <summary>A lot's markers: the module's own (a ruin keeps only the ones with settings and gets a loot cache instead of
    /// its people), the role marker a module lacks (the vendor, the notice board, a resident), and the farm markers.</summary>
    private void AddLotMarkers(PlanLot lot, StructureTemplate turned, List<SettlementMarker> local)
    {
        string function = lot.Function;
        var centre = new Vector3i(lot.X + (lot.W / 2), lot.Y + 1, lot.Z + (lot.L / 2));
        if (_ruined)
        {
            _markers.AddRange(local.FindAll(m => SettlementGenerator.IsSettingsMarker(m.Type)));
            if (_buildings == 1 || StableHashing.Unit($"plan:loot:{lot.X}:{lot.Z}", _seed) < 0.6)
            {
                _markers.Add(new SettlementMarker("loot", centre));
            }

            return;
        }

        _markers.AddRange(local);
        string? role = function switch
        {
            StructureRoles.Market => "vendor",
            StructureRoles.Board => "mission_board",
            StructureRoles.Field or StructureRoles.Garden or StructureRoles.Pen => null,
            _ => "npc",
        };
        if (role != null && !local.Exists(m => m.Type == role))
        {
            _markers.Add(new SettlementMarker(role, new Vector3i(centre.X, SettlementGenerator.FreeCellAbove((x, y, z) => Get(x, y, z), _h, centre.X, centre.Z), centre.Z)));
        }

        if (function == StructureRoles.Greenhouse && !local.Exists(m => m.Type == "greenhouse"))
        {
            _markers.Add(new SettlementMarker("greenhouse", centre));
        }

        AddFarmMarkers(lot, turned);
    }

    /// <summary>A field's corner and size; a pen's inner corner and size and a marker on each of its crates.</summary>
    private void AddFarmMarkers(PlanLot lot, StructureTemplate turned)
    {
        if (lot.Function == StructureRoles.Field)
        {
            _markers.Add(new SettlementMarker(SettlementPlanBaker.FieldMarker, new Vector3i(lot.X, lot.Y + 1, lot.Z),
                data: $"w={lot.W};l={lot.L}"));
        }

        if (lot.Function != StructureRoles.Pen)
        {
            return;
        }

        _markers.Add(new SettlementMarker(SettlementPlanBaker.PenMarker, new Vector3i(lot.X + 1, lot.Y + 1, lot.Z + 1),
            data: $"w={System.Math.Max(1, lot.W - 2)};l={System.Math.Max(1, lot.L - 2)}"));
        foreach (var c in turned.Cells)
        {
            if (c.Kind == "block" && c.Id == "crate")
            {
                _markers.Add(new SettlementMarker(SettlementPlanBaker.PenBoxMarker, new Vector3i(lot.X + c.X, lot.Y + c.Y, lot.Z + c.Z)));
            }
        }
    }

    /// <summary>The plaza marker and a marker where each road leaves the box.</summary>
    private void AddPlanMarkers()
    {
        _markers.Add(new SettlementMarker(SettlementPlanBaker.PlazaMarker,
            new Vector3i(_plan.PlazaX, _plan.HeightAt(_plan.PlazaX, _plan.PlazaZ) + 1, _plan.PlazaZ)));
        for (int side = 0; side < 4; side++)
        {
            int run = 0;
            for (int i = 0; i <= Edge(side); i++)
            {
                var (x, z) = EdgeCell(side, i);
                bool road = i < Edge(side) && (_plan.KindAt(x, z) is SettlementPlan.MainRoad or SettlementPlan.Lane);
                if (road)
                {
                    run++;
                    continue;
                }

                if (run > 0)
                {
                    var (mx, mz) = EdgeCell(side, i - 1 - (run / 2));
                    _markers.Add(new SettlementMarker(SettlementPlanBaker.RoadExitMarker, new Vector3i(mx, _plan.HeightAt(mx, mz) + 1, mz),
                        data: $"side={side}"));
                }

                run = 0;
            }
        }
    }

    private int Edge(int side) => side < 2 ? _w : _l;

    private (int X, int Z) EdgeCell(int side, int i) => side switch
    {
        0 => (i, 0),
        1 => (i, _l - 1),
        2 => (0, i),
        _ => (_w - 1, i),
    };

    /// <summary>
    /// The ruin of a planned settlement (#3593): the columns under every raised floor are filled with support (kept), so the
    /// shared ruin pass — which knows one ground row — sees the buildings standing on solid ground; one lot is spared; the
    /// support is taken out again afterwards (the stamper lays the plinth under each floor).
    /// </summary>
    private void Fall()
    {
        var grid = VoxelGrid.Over(_w, _h, _l, _blocks, _mods, _shapes);
        ushort support = B("stone");
        var filled = new List<int>();
        for (int x = 0; x < _w; x++)
        {
            for (int z = 0; z < _l; z++)
            {
                for (int y = 0; y < _floors[(x * _l) + z]; y++)
                {
                    int i = (((x * _h) + y) * _l) + z;
                    if (_blocks[i] == 0)
                    {
                        _blocks[i] = support;
                        filled.Add(i);
                    }
                }
            }
        }

        filled.ForEach(i => grid.Keep[i] = true);
        _keep.ForEach(i => grid.Keep[i] = true);
        SettlementGenerator.RuinPass(grid, _markers, SparedLot(), _tier, _seed, _surface, _content);
        filled.ForEach(i => _blocks[i] = 0);
    }

    /// <summary>The lot that is spared the worst (a hash of the seed), grown by its own width.</summary>
    private (int MinX, int MinZ, int MaxX, int MaxZ) SparedLot()
    {
        var built = _plan.Lots.FindAll(l => l.Module.Length > 0);
        if (built.Count == 0)
        {
            return (0, 0, -1, -1);
        }

        var lot = built[StableHashing.Index("plan:spared", _seed, built.Count)];
        return (lot.X - 1, lot.Z - 1, lot.X + lot.W, lot.Z + lot.L);
    }
}
