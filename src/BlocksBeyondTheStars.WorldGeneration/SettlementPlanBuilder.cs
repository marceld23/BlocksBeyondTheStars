// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// One planning run of <see cref="SettlementPlanner"/> (#3586, #3587, #3590): the road skeleton (SettlementPlanBuilder.Roads.cs
/// for the village forms, SettlementPlanBuilder.Grid.cs for the grown grid), the growth of the buildings along the roads
/// (SettlementPlanBuilder.Growth.cs) and the heights that follow the ground (SettlementPlanBuilder.Heights.cs).
/// </summary>
internal sealed partial class SettlementPlanBuilder
{
    private readonly SettlementPlanRequest _r;
    private readonly System.Random _rng;
    private readonly int _e;           // the box side
    private readonly int _mainWidth;   // the main road's width
    private readonly int _spacing;     // the largest setback
    private readonly bool _grid;

    /// <summary>Columns the heights keep level with the plaza (the plaza itself and a village green).</summary>
    private readonly bool[] _level;

    /// <summary>The road column in front of each placed lot (its height becomes the lot's), by lot index.</summary>
    private readonly List<(int X, int Z)> _frontage = new();

    /// <summary>The plaza's centre column.</summary>
    private int _px, _pz;

    public SettlementPlanBuilder(SettlementPlanRequest request)
    {
        _r = request;
        _e = request.Extent > 0 ? System.Math.Clamp(request.Extent, 24, 128) : SettlementPlanner.ExtentFor(request.Kit, request.Tier);
        _rng = new System.Random(StableHashing.Seed32("settlement:plan", request.Seed));
        _mainWidth = request.Kit.MainStreet > 0 ? System.Math.Clamp(request.Kit.MainStreet, 1, 4) : 2;
        _spacing = request.Kit.Spacing > 0 ? System.Math.Clamp(request.Kit.Spacing, 1, 4) : 3;
        _grid = request.Kit.Layout == StructureKit.LayoutGrid;
        Plan = new SettlementPlan(_e, _e) { Layout = _grid ? StructureKit.LayoutGrid : StructureKit.LayoutGrown };
        _level = new bool[_e * _e];
        _px = _pz = _e / 2;
    }

    public SettlementPlan Plan { get; }

    private int TierRank => _r.Tier switch { "hamlet" => 0, "town" => 2, "city" => 3, _ => 1 };

    public void Build()
    {
        bool alien = SettlementPlanner.IsAlien(_r.Seed);
        var modules = SettlementPlanner.PickModules(_r, alien, _rng);
        if (_grid)
        {
            GridSkeleton();
        }
        else
        {
            Plan.Form = PickForm();
            FormSkeleton(Plan.Form);
        }

        GrowAll(modules);
        AddSquares(TierRank switch { 0 => 0, 1 => 1, _ => 2 });
        AssignHeights();
        ClaimYards();
        Plan.PlazaX = _px;
        Plan.PlazaZ = _pz;
    }

    /// <summary>The form of a grown settlement: the request's when it names one, else a draw from the kit's forms (every form
    /// when the kit lists none); the bank village only beside fresh water, the street village standing in for it elsewhere.</summary>
    private string PickForm()
    {
        bool water = _r.WaterSide >= 0;
        if (System.Array.IndexOf(StructureKit.AllForms, _r.Form) >= 0)
        {
            return _r.Form == "bank" && !water ? "street" : _r.Form;
        }

        var forms = new List<string>();
        foreach (var f in _r.Kit.Forms.Count > 0 ? (IEnumerable<string>)_r.Kit.Forms : StructureKit.AllForms)
        {
            if (System.Array.IndexOf(StructureKit.AllForms, f) >= 0 && (f != "bank" || water))
            {
                forms.Add(f);
            }
        }

        return forms.Count == 0 ? "street" : forms[_rng.Next(forms.Count)];
    }

    // ------------------------------------------------------------------ painting

    private bool In(int x, int z) => x >= 0 && z >= 0 && x < _e && z < _e;

    /// <summary>Paints one column with a road kind: a plaza or a green is never painted over, a main road never turns into a
    /// lane, a lane may widen into the main road.</summary>
    private void Paint(int x, int z, byte kind)
    {
        if (!In(x, z))
        {
            return;
        }

        byte had = Plan.KindAt(x, z);
        if (had == SettlementPlan.Plaza || had == SettlementPlan.Green || (had == SettlementPlan.MainRoad && kind == SettlementPlan.Lane))
        {
            return;
        }

        Plan.SetKind(x, z, kind);
    }

    /// <summary>A square of the plaza (paved, level) centred on (<paramref name="cx"/>, <paramref name="cz"/>).</summary>
    private void PaintPlaza(int cx, int cz, int size)
    {
        int h0 = size / 2;
        for (int x = cx - h0; x < cx - h0 + size; x++)
        {
            for (int z = cz - h0; z < cz - h0 + size; z++)
            {
                if (In(x, z))
                {
                    Plan.SetKind(x, z, SettlementPlan.Plaza);
                    _level[(x * _e) + z] = true;
                }
            }
        }

        _px = cx;
        _pz = cz;
    }

    /// <summary>The plaza's side by tier.</summary>
    private int PlazaSize => TierRank switch { 0 => 5, 1 => 5, 2 => 7, _ => 7 };
}
