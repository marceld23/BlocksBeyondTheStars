// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Bakes a <see cref="SettlementPlan"/> into the settlement structure every stamper takes (#3584): the roads and the plaza
/// on their own floor rows, every lot's module turned and stamped on its height (furnished, its markers carried), the
/// dressing (lamps, gardens, the plaza's well, the green's trees, the squares), the markers the server reads (the plaza, the
/// road exits, fields, pens and their product crates) and — on a ruin verdict — the shared ruin pass. Every draw comes from
/// the instance seed, so the first stamp and every replay bake the same blocks. The structure carries the plan's column map
/// (<see cref="SettlementStructure.ColumnFloors"/>): the stamper, the protection and the creature zone follow it.
/// </summary>
public static class SettlementPlanBaker
{
    /// <summary>The marker at the plaza's centre (the festival's square, #3594).</summary>
    public const string PlazaMarker = "plaza";

    /// <summary>The marker where a road leaves the box (data <c>side=0..3</c>: −Z, +Z, −X, +X) — the intercity station docks
    /// there (#3594).</summary>
    public const string RoadExitMarker = "road_exit";

    /// <summary>A field's west / north corner (data <c>w=…;l=…</c>) — its crops regrow slower (#3596).</summary>
    public const string FieldMarker = "field";

    /// <summary>A pen's inner corner (data <c>w=…;l=…</c>) — its animals stay inside (#3597).</summary>
    public const string PenMarker = "pen";

    /// <summary>A pen's product crate (#3597).</summary>
    public const string PenBoxMarker = "pen_box";

    /// <summary>Bakes the plan. <paramref name="pool"/> resolves the lots' modules by key; a module that is gone leaves its
    /// lot an empty yard (warned).</summary>
    public static SettlementStructure Bake(SettlementPlan plan, string tier, bool ruined, long seed, string surface, GameContent content,
        IReadOnlyList<StructureTemplate> pool, System.Action<string>? warn = null)
        => new PlanBake(plan, tier, ruined, seed, surface, content, pool, warn).Run();

    /// <summary><see cref="Bake(SettlementPlan, string, bool, long, string, GameContent, IReadOnlyList{StructureTemplate}, System.Action{string}?)"/>
    /// with <paramref name="plinths"/>: every raised column is filled down to row 0 with the surface block — for the editor,
    /// which stands a structure on flat ground (in a world the stamper lays the plinth).</summary>
    public static SettlementStructure Bake(SettlementPlan plan, string tier, bool ruined, long seed, string surface, GameContent content,
        IReadOnlyList<StructureTemplate> pool, bool plinths)
    {
        var bake = new PlanBake(plan, tier, ruined, seed, surface, content, pool, null);
        var s = bake.Run();
        if (plinths)
        {
            bake.FillPlinths();
        }

        return s;
    }
}

/// <summary>One bake of a plan (SettlementPlanBaker).</summary>
internal sealed partial class PlanBake
{
    private readonly SettlementPlan _plan;
    private readonly string _tier;
    private readonly bool _ruined;
    private readonly bool _alien;
    private readonly bool _town;
    private readonly long _seed;
    private readonly string _surface;
    private readonly GameContent _content;
    private readonly IReadOnlyList<StructureTemplate> _pool;
    private readonly System.Action<string>? _warn;
    private readonly ModuleMaterials _materials;
    private readonly int _w, _h, _l;
    private readonly ushort[] _blocks;
    private readonly Dictionary<int, (int Tint, int Glow)> _mods = new();
    private readonly Dictionary<int, int> _shapes = new();
    private readonly RoomFurnisher.CellSink _sink;
    private readonly sbyte[] _floors;
    private readonly List<SettlementMarker> _markers = new();
    private readonly List<int> _keep = new();
    private int _buildings;

    public PlanBake(SettlementPlan plan, string tier, bool ruined, long seed, string surface, GameContent content,
        IReadOnlyList<StructureTemplate> pool, System.Action<string>? warn)
    {
        _plan = plan;
        _tier = tier;
        _ruined = ruined;
        _alien = SettlementPlanner.IsAlien(seed);
        _town = StructureRoles.IsTownStyleTier(tier);
        _seed = seed;
        _surface = surface;
        _content = content;
        _pool = pool;
        _warn = warn;
        _materials = ModuleMaterials.ForSettlement(tier, surface, _alien, content);
        _w = plan.Width;
        _l = plan.Length;
        _h = plan.MaxHeight() + TallestModule() + 4;
        _blocks = new ushort[_w * _h * _l];
        _sink = SettlementGenerator.SinkFor(_blocks, _w, _h, _l, _mods, _shapes);
        _floors = new sbyte[_w * _l];
    }

    private ushort B(string key, ushort fallback = 0) => _content.GetBlock(key)?.NumericId.Value ?? fallback;

    private ushort Get(int x, int y, int z)
        => x < 0 || y < 0 || z < 0 || x >= _w || y >= _h || z >= _l ? (ushort)0 : _blocks[(((x * _h) + y) * _l) + z];

    private ushort SafeGet(int x, int y, int z)
        => x < 0 || y < 0 || z < 0 || x >= _w || y >= _h || z >= _l ? (ushort)1 : _blocks[(((x * _h) + y) * _l) + z];

    private void Set(int x, int y, int z, ushort b) => _sink(x, y, z, b, 0, 0, 0);

    private void SetShaped(int x, int y, int z, ushort b, int shape) => _sink(x, y, z, b, shape, 0, 0);

    public SettlementStructure Run()
    {
        for (int i = 0; i < _floors.Length; i++)
        {
            _floors[i] = _plan.Kinds[i] == SettlementPlan.Unclaimed ? (sbyte)-1 : (sbyte)System.Math.Min(_plan.Heights[i], sbyte.MaxValue);
        }

        LayGround();
        LaySteps();
        foreach (var lot in _plan.Lots)
        {
            StampLot(lot);
        }

        Dress();
        AddPlanMarkers();
        if (_ruined)
        {
            Fall();
        }
        else
        {
            SettlementGenerator.ClearDecorationFromDoorLanes(Get, Set, _w, _h, _l, _markers, 0, Flora, Lamp);
        }

        return new SettlementStructure(_w, _h, _l, _tier, _ruined, _ruined ? string.Empty : _alien ? "alien" : "human", _blocks, _markers,
            System.Math.Max(1, _buildings), _mods, _shapes) { ColumnFloors = _floors };
    }

    /// <summary>Fills every claimed column from row 0 up to its floor with the surface (the editor's preview plinths); the
    /// structure shares the block array, so this lands in it.</summary>
    public void FillPlinths()
    {
        ushort fill = B(_surface, B("dirt"));
        for (int x = 0; x < _w; x++)
        {
            for (int z = 0; z < _l; z++)
            {
                for (int y = 0; y <= _floors[(x * _l) + z]; y++)
                {
                    if (Get(x, y, z) == 0)
                    {
                        Set(x, y, z, fill);
                    }
                }
            }
        }
    }

    /// <summary>The tallest module a lot names (the structure's head-room above its highest floor).</summary>
    private int TallestModule()
    {
        int tallest = 6;
        foreach (var lot in _plan.Lots)
        {
            if (SettlementPlanner.ModuleByKey(_pool, lot.Module) is { } m)
            {
                tallest = System.Math.Max(tallest, m.Height);
            }
        }

        return tallest;
    }

    // ------------------------------------------------------------------ materials

    private ushort Path => _materials.Path;

    private ushort PlazaPaving => _town ? B("steel_floor", Path) : B("sandstone", Path);

    private ushort StepBlock => _town ? B("carbon", B("stone")) : B("stone");

    private ushort Lamp => B("data_cache", B("glass"));

    private ushort Flora => _alien ? B("flora_crystal", B("flora_plant")) : B(SettlementGenerator.BiomeFloraKey(_surface), B("flora_plant"));

    // ------------------------------------------------------------------ the ground

    /// <summary>Paves the roads, the footpaths and the plaza on their own floor rows (yards, lots and greens keep the
    /// ground the stamper lays — the biome's surface).</summary>
    private void LayGround()
    {
        for (int x = 0; x < _w; x++)
        {
            for (int z = 0; z < _l; z++)
            {
                byte kind = _plan.KindAt(x, z);
                ushort b = kind switch
                {
                    SettlementPlan.Plaza => PlazaPaving,
                    SettlementPlan.MainRoad or SettlementPlan.Lane or SettlementPlan.Footpath => Path,
                    _ => (ushort)0,
                };
                if (b != 0)
                {
                    Set(x, _plan.HeightAt(x, z), z, b);
                }
            }
        }
    }

    /// <summary>
    /// Half steps on the roads (#3590): where a walkable column has a neighbour one block higher and none lower, a slab on it
    /// halves the step for a walker. An NPC reads the slab as a full block, which keeps every step it takes at one block.
    /// </summary>
    private void LaySteps()
    {
        int slab = ShapeCode.Pack((int)BlockShape.Slab, 0);
        for (int x = 0; x < _w; x++)
        {
            for (int z = 0; z < _l; z++)
            {
                byte kind = _plan.KindAt(x, z);
                if (kind != SettlementPlan.Lane && kind != SettlementPlan.MainRoad)
                {
                    continue; // never on the plaza, never in front of a door
                }

                int f = _plan.HeightAt(x, z);
                bool higher = false, lower = false;
                foreach (var (nx, nz) in new[] { (x - 1, z), (x + 1, z), (x, z - 1), (x, z + 1) })
                {
                    if (SettlementPlan.IsWalkable(_plan.KindAt(nx, nz)))
                    {
                        int g = _plan.HeightAt(nx, nz);
                        higher |= g == f + 1;
                        lower |= g < f;
                    }
                }

                if (higher && !lower && f + 1 < _h)
                {
                    SetShaped(x, f + 1, z, StepBlock, slab);
                }
            }
        }
    }
}
