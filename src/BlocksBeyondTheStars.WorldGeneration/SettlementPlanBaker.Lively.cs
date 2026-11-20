// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The street furniture of a lively place (plan revision 2, docs/developer/LIVELY_PLACES.md) — what makes a baked plan a
/// place people live in: lanterns that cast light, market stalls and benches on the plaza, a well, a fire place in a hamlet
/// or a village, crates, barrels and flower pots beside the doors, an awning in the settlement's colour over the door of
/// every building that offers something. Built from blocks and forms the game has (no new block). Every piece also leaves
/// a marker (<see cref="SettlementPlanBaker.FurnitureMarkers"/>): the server reads them as the places a villager can go
/// to, the client as the points it dresses with effects and the doors it hangs a sign over.
/// <para>Like the rest of the dressing every draw is a hash of the seed and the column, never a stream; how much is set
/// out follows the plan's pinned <see cref="SettlementPlan.Dressing"/>. Nothing stands in a door lane or on a road.</para>
/// </summary>
internal sealed partial class PlanBake
{
    /// <summary>Whether this bake is a lively place: a revision-2 plan of a lived-in settlement (a ruin keeps the classic
    /// dressing — it is knocked down right after).</summary>
    private bool Lively => _plan.IsLively && !_ruined;

    /// <summary>The cells of every door lane (the doorway and the rows in front of it), <c>x * _l + z</c> — nothing of the
    /// furniture may stand there.</summary>
    private HashSet<int>? _laneCells;

    private ushort Lantern => _alien ? B("crystal", B("lantern")) : B("lantern", B("torch"));

    /// <summary>What a lantern stands on and what the benches, counters and awnings are made of.</summary>
    private ushort PostBlock => _alien ? B("crystal", B("stone")) : _town ? B("iron_wall", B("stone")) : B("wood_log", B("stone"));

    private ushort Timber => _town ? B("wood_planks_mahogany", B("wood_planks", B("stone"))) : B("wood_planks", B("wood_log", B("stone")));

    private static int Shaped(BlockShape shape, int yaw = 0) => ShapeCode.Pack(shape, yaw);

    private void SetTinted(int x, int y, int z, ushort b, int shape, int tint) => _sink(x, y, z, b, shape, tint, 0);

    /// <summary>The columns somebody is meant to stand on (in front of a stall or a bench, beside the well) — nothing is set
    /// down there afterwards.</summary>
    private readonly HashSet<int> _standCells = new();

    private void Mark(string type, int x, int y, int z, string data = "")
    {
        _markers.Add(new SettlementMarker(type, new Vector3i(x, y, z), data: data));
        if (type is SettlementPlanBaker.StallMarker or SettlementPlanBaker.BenchMarker or SettlementPlanBaker.WellMarker)
        {
            _standCells.Add((x * _l) + z);
        }
    }

    private bool InLane(int x, int z)
    {
        if (_laneCells is null)
        {
            _laneCells = new HashSet<int>();
            var doors = new List<Vector3i>();
            foreach (var m in _markers)
            {
                if (RoomFurnisher.IsDoorMarker(m.Type))
                {
                    doors.Add(m.LocalPos);
                }
            }

            foreach (var lane in RoomFurnisher.DoorLanes(Get, _w, _h, _l, doors))
            {
                foreach (var (lx, lz) in lane.Clear)
                {
                    _laneCells.Add((lx * _l) + lz);
                }
            }
        }

        return _laneCells.Contains((x * _l) + z);
    }

    /// <summary>A free plaza cell: paved plaza, nothing standing on it, not in a door lane.</summary>
    private bool FreeOnPlaza(int x, int z, int y)
        => _plan.KindAt(x, z) == SettlementPlan.Plaza && Get(x, y, z) == 0 && Get(x, y + 1, z) == 0 && !InLane(x, z)
           && !_standCells.Contains((x * _l) + z);

    /// <summary>A village green of a lively place (it has no paved plaza): a hamlet's fire and a village's place to draw
    /// water stand beside its pond — set before the green's flowers and trees, which grow around them.</summary>
    private void DressLivelyGreen()
    {
        int cx = _plan.PlazaX, cz = _plan.PlazaZ;
        int y = _plan.HeightAt(cx, cz) + 1;
        if (_tier == "hamlet" && _plan.KindAt(cx + 2, cz) == SettlementPlan.Green && _plan.HeightAt(cx + 2, cz) == y - 1)
        {
            FirePlace(cx + 2, y, cz);
        }
        else if (_tier != "hamlet" && TryPondWithJetty(cx, cz, y))
        {
            // #3872: a real pond with a plank jetty and a lantern — its jetty is where somebody draws water
        }
        else if (_plan.KindAt(cx + 1, cz + 1) == SettlementPlan.Green && _plan.HeightAt(cx + 1, cz + 1) == y - 1)
        {
            Mark(SettlementPlanBaker.WellMarker, cx + 1, y, cz + 1); // the pond is the green's well
        }
    }

    private const int PondHalfW = SettlementPlanBaker.PondHalfW, PondHalfL = SettlementPlanBaker.PondHalfL;

    /// <summary>Set once a lively green got its pond — the classic single water block in its middle is left out then.</summary>
    private bool _pondBuilt;

    /// <summary>
    /// #3872: the pond of a lively green village (the plan names a ninth landmark, a jetty with a lantern; the water a plan
    /// owns is this pond — a river lies beyond the settlement's box, where a jetty could end on dry land). Three by five
    /// blocks of water where the green is level, a plank jetty from the east bank to the middle, a lantern on a post at
    /// its end (a <c>lamp</c> marker), and the jetty is where somebody draws water (the <c>well</c> marker). Nothing grows
    /// on the pond. False (nothing built) where the green is not level or not wide enough.
    /// </summary>
    private bool TryPondWithJetty(int cx, int cz, int y)
    {
        for (int x = cx - PondHalfW; x <= cx + PondHalfW + 1; x++)
        {
            for (int z = cz - PondHalfL; z <= cz + PondHalfL; z++)
            {
                if (_plan.KindAt(x, z) != SettlementPlan.Green || _plan.HeightAt(x, z) != y - 1 || Get(x, y, z) != 0)
                {
                    return false; // the bank (x = cx + 2) must be level green too
                }
            }
        }

        ushort water = B("water"), planks = B("wood_planks", B("wood_log"));
        for (int x = cx - PondHalfW; x <= cx + PondHalfW; x++)
        {
            for (int z = cz - PondHalfL; z <= cz + PondHalfL; z++)
            {
                Set(x, y - 1, z, water);
                _standCells.Add((x * _l) + z); // nothing grows or stands on the water
            }
        }

        Set(cx + 1, y - 1, cz, planks); // the jetty, flush with the bank, out to the middle
        Set(cx, y - 1, cz, planks);
        LanternPost(cx, y, cz);
        Mark(SettlementPlanBaker.WellMarker, cx + 1, y, cz); // on the jetty, the water on both sides
        _standCells.Add(((cx + 2) * _l) + cz); // the bank where the jetty starts stays free to step onto it
        _pondBuilt = true;
        return true;
    }

    /// <summary>A lantern on a post (the classic lamp was two glowing blocks that cast no light).</summary>
    private void LanternPost(int x, int y, int z)
    {
        SetShaped(x, y, z, PostBlock, Shaped(BlockShape.Post));
        Set(x, y + 1, z, Lantern);
        Mark(SettlementPlanBaker.LampMarker, x, y, z);
    }

    /// <summary>The furniture of a lively place, after the classic dressing: what stands on the plaza, then what stands at
    /// the houses.</summary>
    private void DressLively()
    {
        int cx = _plan.PlazaX, cz = _plan.PlazaZ;
        if (_plan.KindAt(cx, cz) == SettlementPlan.Plaza && _plan.Dressing > 1)
        {
            int y = _plan.HeightAt(cx, cz) + 1;
            int half = PlazaHalf(cx, cz);
            PlaceStalls(cx, cz, y, half);
            PlaceBenches(cx, cz, y, half);
            PlazaCornerLanterns(cx, cz, y, half);
        }

        if (_plan.Dressing > 1)
        {
            foreach (var lot in _plan.Lots)
            {
                if (lot.Module.Length > 0)
                {
                    DressFront(lot);
                }
            }
        }
    }

    /// <summary>A lantern on a post in every corner of the plaza the stalls left free (a hamlet keeps its fire). Plain
    /// lanterns like the street's: they burn round the clock and belong to no circuit.</summary>
    private void PlazaCornerLanterns(int cx, int cz, int y, int half)
    {
        if (half < 2 || _tier == "hamlet" || y + 1 >= _h)
        {
            return;
        }

        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            int x = cx + (sx * half), z = cz + (sz * half);
            if (FreeOnPlaza(x, z, y) && _plan.HeightAt(x, z) == y - 1)
            {
                LanternPost(x, y, z);
            }
        }
    }

    /// <summary>How far the plaza reaches from its centre (2 for a plaza of five, 3 for one of seven).</summary>
    private int PlazaHalf(int cx, int cz)
    {
        int half = 0;
        while (half < 4 && _plan.KindAt(cx + half + 1, cz) == SettlementPlan.Plaza && _plan.KindAt(cx, cz + half + 1) == SettlementPlan.Plaza)
        {
            half++;
        }

        return half;
    }

    /// <summary>
    /// Market stalls in the plaza's corners: a counter (a table of planks) and a crate beside it under an awning in the
    /// settlement's colour, carried by a post on the crate. The customer's place is the cell in front of the counter.
    /// </summary>
    private void PlaceStalls(int cx, int cz, int y, int half)
    {
        if (half < 2)
        {
            return;
        }

        int wanted = (_tier == "hamlet" ? 1 : _town ? 3 : 2) + (_plan.Dressing >= 4 ? 1 : 0) - (_plan.Dressing <= 2 ? 1 : 0);
        var corners = new[] { (-1, -1), (1, 1), (1, -1), (-1, 1) };
        int first = StableHashing.Index("plan:stall", _seed, corners.Length);
        int tint = _plan.Colour;
        for (int i = 0; i < corners.Length && wanted > 0; i++)
        {
            var (sx, sz) = corners[(first + i) % corners.Length];
            int ax = cx + (sx * half), az = cz + (sz * half); // the counter, in the corner
            int bx = ax - sx, bz = az;                        // the crate, one cell along the edge toward the middle
            int fx = ax, fz = az - sz;                        // the customer, in front of the counter
            if (!FreeOnPlaza(ax, az, y) || !FreeOnPlaza(bx, bz, y) || !FreeOnPlaza(fx, fz, y) || y + 2 >= _h)
            {
                continue;
            }

            SetShaped(ax, y, az, Timber, Shaped(BlockShape.Table));
            Set(bx, y, bz, B("wood_crate", B("crate")));
            SetShaped(bx, y + 1, bz, PostBlock, Shaped(BlockShape.Post));
            SetTinted(ax, y + 2, az, Timber, Shaped(BlockShape.Slab), tint);
            SetTinted(bx, y + 2, bz, Timber, Shaped(BlockShape.Slab), tint);
            Mark(SettlementPlanBaker.StallMarker, fx, y, fz);
            wanted--;
        }
    }

    /// <summary>Benches on the plaza's edge, their backs to the outside: two in a hamlet, more with the size and the
    /// dressing. The marker is the cell in front of the bench.</summary>
    private void PlaceBenches(int cx, int cz, int y, int half)
    {
        if (half < 2)
        {
            return;
        }

        int wanted = (_tier == "hamlet" ? 2 : _town ? 4 : 3) + (_plan.Dressing >= 4 ? 2 : 0) - (_plan.Dressing <= 2 ? 1 : 0);
        // The four edges: (dx, dz) outward, the bench one cell beside the edge's middle (the middle is where a road comes in).
        var edges = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        int first = StableHashing.Index("plan:bench", _seed, edges.Length);
        for (int round = 0; round < 2 && wanted > 0; round++)
        {
            for (int i = 0; i < edges.Length && wanted > 0; i++)
            {
                var (ox, oz) = edges[(first + i) % edges.Length];
                int along = round == 0 ? 1 : -1;
                int x = cx + (ox * half) + (oz != 0 ? along : 0);
                int z = cz + (oz * half) + (ox != 0 ? along : 0);
                int fx = x - ox, fz = z - oz; // in front of the bench, toward the middle
                if (!FreeOnPlaza(x, z, y) || !FreeOnPlaza(fx, fz, y))
                {
                    continue;
                }

                SetShaped(x, y, z, Timber, Shaped(BlockShape.Bench, ShapeCode.YawToward(ox, oz))); // the backrest toward the outside
                Mark(SettlementPlanBaker.BenchMarker, fx, y, fz);
                wanted--;
            }
        }
    }

    /// <summary>The plaza's centre of a lively place: a hamlet gathers round a fire, a village has its well, a town and a
    /// city keep the classic three (a well, a column with a lantern, a planted bed).</summary>
    private void DressLivelyPlazaCentre(int cx, int cz, int y)
    {
        if (_tier == "hamlet")
        {
            FirePlace(cx, y, cz);
        }
        else if (!_town)
        {
            Well(cx, y, cz);
        }
        else
        {
            switch (StableHashing.Index("plan:plaza", _seed, 3))
            {
                case 0:
                    Well(cx, y, cz);
                    break;
                case 1:
                    Set(cx, y, cz, _materials.Accent);
                    Set(cx, y + 1, cz, _materials.Accent);
                    Set(cx, y + 2, cz, Lantern);
                    Mark(SettlementPlanBaker.LampMarker, cx + 1, y, cz);
                    break;
                default:
                    Set(cx, y, cz, Flora);
                    Set(cx - 1, y, cz - 1, Lantern);
                    Set(cx + 1, y, cz + 1, Lantern);
                    break;
            }
        }
    }

    private void Well(int cx, int y, int cz)
    {
        Ring(cx, y, cz, _materials.Accent);
        Set(cx, y, cz, B("water"));
        Mark(SettlementPlanBaker.WellMarker, cx + 2, y, cz + 1); // beside the ring, where somebody stands to draw water
    }

    /// <summary>A camp fire with the marker beside it (the client gives a lit camp fire its sparks, smoke and crackle).</summary>
    private void FirePlace(int cx, int y, int cz)
    {
        ushort fire = B("campfire");
        if (fire == 0)
        {
            Well(cx, y, cz);
            return;
        }

        SetShaped(cx, y, cz, fire, ShapeCode.Pack(PropShapes.DefaultPlaceShape("campfire"), 0)); // its own low form, as a player's
        Mark(SettlementPlanBaker.FireMarker, cx, y, cz);
    }

    /// <summary>A square of a lively place: a village's is its fire place; a town's first is a well when the plaza has none.</summary>
    private void DressLivelySquare(PlanLot lot, int cx, int y, int cz)
    {
        bool plazaHasWell = !_town || StableHashing.Index("plan:plaza", _seed, 3) == 0;
        if (!_town)
        {
            FirePlace(cx, y, cz);
            SetShaped(cx - 2, y, cz, Timber, Shaped(BlockShape.Bench, ShapeCode.YawToward(-1, 0)));
            SetShaped(cx + 2, y, cz, Timber, Shaped(BlockShape.Bench, ShapeCode.YawToward(1, 0)));
            Mark(SettlementPlanBaker.BenchMarker, cx - 1, y, cz + 1);
            Mark(SettlementPlanBaker.BenchMarker, cx + 1, y, cz - 1);
        }
        else if (!plazaHasWell && Hash($"plan:square:{lot.X}:{lot.Z}") < 0.7)
        {
            Well(cx, y, cz);
        }
        else
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    Set(cx + dx, y, cz + dz, Flora);
                }
            }

            LanternPost(cx, y, cz);
        }
    }

    /// <summary>The wall a lot's front (its door) faces after its turns: 0 −Z, 1 +Z, 2 −X, 3 +X.</summary>
    private static int FrontOf(PlanLot lot) => lot.Turns switch { 0 => 0, 1 => 3, 2 => 1, _ => 2 };

    private static (int Dx, int Dz) Outward(int front) => front switch { 0 => (0, -1), 1 => (0, 1), 2 => (-1, 0), _ => (1, 0) };

    /// <summary>The functions whose building gets a sign and an awning (a home has neither; a farm has no door).</summary>
    private static bool HasSign(string function)
        => function.Length > 0 && function is not (StructureRoles.House or StructureRoles.Field or StructureRoles.Garden or StructureRoles.Pen);

    /// <summary>
    /// The front of a building: the sign marker at its door and an awning over it for a building that offers something; a
    /// crate, a barrel or a flower pot at the corners of the facade (never in the door's lane).
    /// </summary>
    private void DressFront(PlanLot lot)
    {
        int front = FrontOf(lot);
        var (ox, oz) = Outward(front);
        if (HasSign(lot.Function) && FrontDoorOf(lot, front, out bool onFacade) is { } door)
        {
            Mark(SettlementPlanBaker.SignMarker, door.X, door.Y, door.Z, $"fn={lot.Function};side={front}");
            if (onFacade)
            {
                Awning(door, front, ox, oz); // a shell with a porch or an awning strip brings its own
            }
        }

        if (_plan.Dressing < 3 && !HasSign(lot.Function))
        {
            return;
        }

        // The two cells just outside the facade's corners.
        int y = lot.Y + 1;
        for (int end = 0; end < 2; end++)
        {
            int x = front < 2 ? (end == 0 ? lot.X : lot.X + lot.W - 1) : (front == 2 ? lot.X - 1 : lot.X + lot.W);
            int z = front < 2 ? (front == 0 ? lot.Z - 1 : lot.Z + lot.L) : (end == 0 ? lot.Z : lot.Z + lot.L - 1);
            double r = Hash($"plan:front:{lot.X}:{lot.Z}:{end}");
            if (r > (_plan.Dressing >= 4 ? 0.85 : 0.55) || !_plan.InBox(x, z) || _plan.KindAt(x, z) != SettlementPlan.Yard
                || _plan.HeightAt(x, z) != lot.Y || Get(x, y, z) != 0 || InLane(x, z))
            {
                continue;
            }

            FrontProp(lot.Function, x, y, z, r);
        }
    }

    /// <summary>What stands at a facade's corner, by what the building is.</summary>
    private void FrontProp(string function, int x, int y, int z, double r)
    {
        bool goods = function is StructureRoles.Market or StructureRoles.Shop or StructureRoles.Workshop or StructureRoles.Depot
            or StructureRoles.Armory or StructureRoles.Quarry or StructureRoles.Tavern;
        if (goods && r < 0.3)
        {
            Set(x, y, z, B("wood_crate", B("crate")));
        }
        else if (goods)
        {
            SetShaped(x, y, z, Timber, Shaped(BlockShape.Cylinder)); // a barrel
        }
        else
        {
            ushort pot = B("flower_pot");
            if (pot != 0)
            {
                SetShaped(x, y, z, pot, ShapeCode.Pack(PropShapes.DefaultPlaceShape("flower_pot"), 0));
            }
        }
    }

    /// <summary>How far behind a lot's road edge a front door may stand (a shell's porch, awning strip or front yard).</summary>
    private const int FrontDoorDepth = 3;

    /// <summary>The lower cell of a lot's front door: the door marker nearest the lot's road edge (at most
    /// <see cref="FrontDoorDepth"/> cells in — a shell may set its door back behind a porch or a yard), the lowest one of
    /// those; null when the lot has none there. <paramref name="onFacade"/> says whether it stands on the edge itself.</summary>
    private Vector3i? FrontDoorOf(PlanLot lot, int front, out bool onFacade)
    {
        Vector3i? best = null;
        int bestDepth = int.MaxValue;
        foreach (var m in _markers)
        {
            if (!RoomFurnisher.IsDoorMarker(m.Type) || !lot.Covers(m.LocalPos.X, m.LocalPos.Z))
            {
                continue;
            }

            int depth = front switch
            {
                0 => m.LocalPos.Z - lot.Z,
                1 => lot.Z + lot.L - 1 - m.LocalPos.Z,
                2 => m.LocalPos.X - lot.X,
                _ => lot.X + lot.W - 1 - m.LocalPos.X,
            };
            if (depth > FrontDoorDepth || m.LocalPos.Y > lot.Y + 1)
            {
                continue; // an inner door, or one upstairs
            }

            if (depth < bestDepth)
            {
                bestDepth = depth;
                best = m.LocalPos;
            }
        }

        onFacade = bestDepth == 0;
        return best;
    }

    /// <summary>An awning over a door: a row of slabs in the settlement's colour, one step out from the wall above the
    /// doorway (three cells high) and a cell to each side of it.</summary>
    private void Awning(Vector3i door, int front, int ox, int oz)
    {
        int y = door.Y + 3;
        if (y >= _h || _plan.Colour == 0)
        {
            return;
        }

        // The doorway is two wide: the marker sits on its lower-coordinate column (SettlementModuleShapes' convention).
        bool alongX = front < 2;
        for (int k = -1; k <= 2; k++)
        {
            int x = door.X + ox + (alongX ? k : 0);
            int z = door.Z + oz + (alongX ? 0 : k);
            if (_plan.InBox(x, z) && Get(x, y, z) == 0)
            {
                SetTinted(x, y, z, Timber, Shaped(BlockShape.Slab), _plan.Colour);
            }
        }
    }
}
