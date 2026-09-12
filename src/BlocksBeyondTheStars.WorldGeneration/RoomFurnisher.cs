// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Procedural interiors (#1828): puts furniture into a room — a bed, a table with a chair, a crate, a plant,
/// a light, a market counter, a terminal — along the walls of a floor region, leaving the centre, the NPC's
/// spot and the door lane walkable. Works on any floor region: the rectangular interior of a procedural
/// building (<see cref="SettlementGenerator.StampBuilding"/>) or the flood-filled floor of an authored room
/// (a <c>room</c> marker in a template, <see cref="FloodRoom"/>). The pieces are the existing prop blocks
/// (bed, crate, flower pot, torch, lights …) and the #805 furniture SHAPES (table, chair, slab counter) on
/// the style's material, so a village sits at a log table and a town at a steel one.
/// <para>Deterministic from the <see cref="System.Random"/> handed in — callers seed it from a hash so the
/// main generator stream stays untouched and a room always gets the same furniture.</para>
/// </summary>
public static class RoomFurnisher
{
    /// <summary>The look of a room: timber-and-stone village, iron-and-steel town/city, crystal-lit alien.</summary>
    public enum Style
    {
        Village,
        Town,
        Alien,
    }

    /// <summary>What a room is for — drives which pieces it gets.</summary>
    public enum RoomRole
    {
        House,
        Market,
        Board,
        Upper,
    }

    /// <summary>Writes one cell: block id + packed shape (0 = cube) + tint + glow.</summary>
    public delegate void CellSink(int x, int y, int z, ushort block, int shape, int tint, int glow);

    /// <summary>The blocks a style furnishes with. 0 for a piece the content lacks (it is then skipped).</summary>
    public sealed class Palette
    {
        public ushort Bed;
        public int BedShape;
        public ushort TableMaterial;
        public ushort ChairMaterial;
        public ushort Storage;
        public ushort Light;
        public ushort Plant;
        public int PlantShape;
        public ushort PlantAlt;
        public double PlantAltChance;
        public ushort Counter;
        public ushort Terminal;
        public ushort TerminalAlt;
        public double TerminalAltChance;

        /// <summary>True when the building lights its rooms itself (a lamp in the deck, #1808) — no floor light then.</summary>
        public bool CeilingLit;
    }

    /// <summary>Style of a settlement tier: town/city/metropolis build town-style rooms, alien settlements
    /// their own, everything else a village room.</summary>
    public static Style StyleFor(string tier, bool alien)
        => alien ? Style.Alien
            : StructureRoles.IsTownStyleTier(tier) || tier == StructureRoles.MetropolisTier || tier == CityGenerator.Tier ? Style.Town
            : Style.Village;

    /// <summary>The palette of a style from the loaded content — a missing block leaves its piece out.</summary>
    public static Palette PaletteFor(Style style, GameContent content)
    {
        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;

        var p = new Palette
        {
            Bed = B("bed"),
            BedShape = ShapeCode.Pack(PropShapes.DefaultPlaceShape("bed"), 0),
            Plant = B("flower_pot"),
            PlantShape = ShapeCode.Pack(PropShapes.DefaultPlaceShape("flower_pot"), 0),
        };

        switch (style)
        {
            case Style.Town:
                p.TableMaterial = B("steel_floor", B("iron_wall"));
                p.ChairMaterial = B("steel_floor", B("iron_wall"));
                p.Storage = B("crate", B("wood_crate"));
                p.Light = B("light_white", B("data_cache"));
                p.PlantAlt = B("algae_tank");
                p.PlantAltChance = 0.34;
                p.Counter = B("steel_floor", B("iron_wall"));
                p.Terminal = B("data_cache");
                p.TerminalAlt = B("gaming_pc");
                p.TerminalAltChance = 0.25;
                break;
            case Style.Alien:
                p.TableMaterial = B("iron_wall", B("stone"));
                p.ChairMaterial = B("iron_wall", B("stone"));
                p.Storage = B("crate", B("wood_crate"));
                p.Light = B("data_cache", B("torch"));
                p.Counter = B("iron_wall", B("stone"));
                p.Terminal = B("data_cache");
                break;
            default:
                p.TableMaterial = B("wood_log", B("stone"));
                p.ChairMaterial = B("wood_log", B("stone"));
                p.Storage = B("wood_crate", B("crate"));
                p.Light = B("torch");
                p.Counter = B("wood_log", B("stone"));
                p.Terminal = B("data_cache");
                break;
        }

        return p;
    }

    private enum Piece
    {
        Bed,
        Light,
        Table,
        Storage,
        Plant,
        Counter,
        Terminal,
    }

    private static readonly Piece[] HousePieces = { Piece.Bed, Piece.Light, Piece.Table, Piece.Storage, Piece.Plant };
    private static readonly Piece[] MarketPieces = { Piece.Counter, Piece.Storage, Piece.Light, Piece.Storage, Piece.Table };
    private static readonly Piece[] BoardPieces = { Piece.Table, Piece.Terminal, Piece.Light, Piece.Storage };
    private static readonly Piece[] UpperPieces = { Piece.Bed, Piece.Storage, Piece.Light, Piece.Plant };

    /// <summary>
    /// Furnishes one floor region. <paramref name="region"/> = the walkable floor cells (x, z) at height
    /// <paramref name="floorY"/> (the cell the player stands IN, not the deck below), <paramref name="clearance"/>
    /// = air cells from the floor up (a room shorter than two is left alone), <paramref name="reserved"/> = cells
    /// that must stay air (the NPC marker, the door lane, the ladder). Pieces hug the walls — a wall is any
    /// side of a cell with no region cell beyond it — corners first, and never take more than a third of the
    /// room. Returns the number of cells written.
    /// </summary>
    public static int Furnish(CellSink set, IReadOnlyList<(int X, int Z)> region, int floorY, int clearance,
        Palette p, RoomRole role, ICollection<(int X, int Z)> reserved, System.Random rng)
    {
        if (region.Count == 0 || clearance < 2)
        {
            return 0;
        }

        var cells = new HashSet<(int X, int Z)>(region);
        var used = new HashSet<(int X, int Z)>();
        foreach (var r in reserved)
        {
            used.Add(r);
        }

        int WallSides((int X, int Z) c)
        {
            int n = 0;
            if (!cells.Contains((c.X + 1, c.Z))) n++;
            if (!cells.Contains((c.X - 1, c.Z))) n++;
            if (!cells.Contains((c.X, c.Z + 1))) n++;
            if (!cells.Contains((c.X, c.Z - 1))) n++;
            return n;
        }

        // Candidate cells: along the walls, not reserved. Shuffled once, then corners first (stable), so two
        // rooms of the same shape still differ in where the bed ends up.
        var candidates = new List<(int X, int Z)>();
        foreach (var c in region)
        {
            if (!used.Contains(c) && WallSides(c) > 0)
            {
                candidates.Add(c);
            }
        }

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        var corners = new List<(int X, int Z)>();
        var edges = new List<(int X, int Z)>();
        foreach (var c in candidates)
        {
            if (WallSides(c) >= 2) corners.Add(c); else edges.Add(c);
        }

        var order = new List<(int X, int Z)>(corners.Count + edges.Count);
        order.AddRange(corners);
        order.AddRange(edges);

        int budget = System.Math.Max(1, region.Count / 3);
        var pieces = role switch
        {
            RoomRole.Market => MarketPieces,
            RoomRole.Board => BoardPieces,
            RoomRole.Upper => UpperPieces,
            _ => HousePieces,
        };

        int written = 0;
        int placed = 0;

        (int X, int Z)? TakeFree()
        {
            foreach (var c in order)
            {
                if (!used.Contains(c))
                {
                    return c;
                }
            }

            return null;
        }

        (int X, int Z)? FreeNeighbour((int X, int Z) c, bool preferInside)
        {
            (int X, int Z)? fallback = null;
            foreach (var d in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
            {
                var n = (c.X + d.Item1, c.Z + d.Item2);
                if (!cells.Contains(n) || used.Contains(n))
                {
                    continue;
                }

                bool inside = WallSides(n) == 0;
                if (inside == preferInside)
                {
                    return n;
                }

                fallback ??= n;
            }

            return fallback;
        }

        // A bed lies along its wall; a chair faces its table. Yaw: 0 = +Z, 1 = +X, 2 = −Z, 3 = −X.
        int YawAlongWall((int X, int Z) c) => cells.Contains((c.X, c.Z + 1)) && cells.Contains((c.X, c.Z - 1)) ? 0 : 1;
        int YawToward(int dx, int dz) => dz > 0 ? 0 : dx > 0 ? 1 : dz < 0 ? 2 : 3;

        void Put((int X, int Z) c, ushort block, int shape)
        {
            set(c.X, floorY, c.Z, block, shape, 0, 0);
            used.Add(c);
            written++;
        }

        foreach (var piece in pieces)
        {
            if (placed >= budget)
            {
                break;
            }

            var cell = TakeFree();
            if (cell is null)
            {
                break;
            }

            var c = cell.Value;
            switch (piece)
            {
                case Piece.Bed:
                    if (p.Bed == 0) continue;
                    Put(c, p.Bed, ShapeCode.Pack(ShapeCode.ShapeOf(p.BedShape), YawAlongWall(c)));
                    break;

                case Piece.Light:
                    if (p.Light == 0 || p.CeilingLit) continue;
                    Put(c, p.Light, 0);
                    break;

                case Piece.Table:
                    if (p.TableMaterial == 0) continue;
                    Put(c, p.TableMaterial, ShapeCode.Pack(BlockShape.Table, 0));
                    if (p.ChairMaterial != 0)
                    {
                        var seat = FreeNeighbour(c, preferInside: true);
                        if (seat is { } s)
                        {
                            // The backrest points away from the table: the chair's +Z faces the table's side.
                            Put(s, p.ChairMaterial, ShapeCode.Pack(BlockShape.Chair, YawToward(c.X - s.X, c.Z - s.Z)));
                        }
                    }

                    break;

                case Piece.Storage:
                    if (p.Storage == 0) continue;
                    Put(c, p.Storage, 0);
                    break;

                case Piece.Plant:
                    if (p.PlantAlt != 0 && rng.NextDouble() < p.PlantAltChance)
                    {
                        Put(c, p.PlantAlt, 0);
                    }
                    else if (p.Plant != 0)
                    {
                        Put(c, p.Plant, p.PlantShape);
                    }
                    else
                    {
                        continue;
                    }

                    break;

                case Piece.Counter:
                    if (p.Counter == 0) continue;
                    Put(c, p.Counter, ShapeCode.Pack(BlockShape.Slab, 0));
                    var next = FreeNeighbour(c, preferInside: false);
                    if (next is { } n2 && WallSides(n2) > 0)
                    {
                        Put(n2, p.Counter, ShapeCode.Pack(BlockShape.Slab, 0));
                    }

                    break;

                case Piece.Terminal:
                    if (p.TerminalAlt != 0 && rng.NextDouble() < p.TerminalAltChance)
                    {
                        Put(c, p.TerminalAlt, 0);
                    }
                    else if (p.Terminal != 0)
                    {
                        Put(c, p.Terminal, 0);
                    }
                    else
                    {
                        continue;
                    }

                    break;
            }

            placed++;
        }

        return written;
    }

    /// <summary>
    /// The floor region of an authored room (#1828): every air cell reachable from (<paramref name="sx"/>,
    /// <paramref name="sy"/>, <paramref name="sz"/>) on that level through 4-connected air cells that stand on
    /// something solid, capped at <paramref name="cap"/> cells so a marker on open ground never floods the whole
    /// template. <paramref name="clearance"/> = the shortest run of air above any region cell (at most 4).
    /// Empty when the start cell is not a floor cell.
    /// </summary>
    public static List<(int X, int Z)> FloodRoom(System.Func<int, int, int, ushort> get, int w, int h, int l,
        int sx, int sy, int sz, int cap, out int clearance)
    {
        var region = new List<(int X, int Z)>();
        clearance = 0;
        if (sy <= 0 || sy >= h || sx < 0 || sz < 0 || sx >= w || sz >= l)
        {
            return region;
        }

        bool Floor(int x, int z) => x >= 0 && z >= 0 && x < w && z < l && get(x, sy, z) == 0 && get(x, sy - 1, z) != 0;
        if (!Floor(sx, sz))
        {
            return region;
        }

        var seen = new HashSet<(int X, int Z)> { (sx, sz) };
        var queue = new Queue<(int X, int Z)>();
        queue.Enqueue((sx, sz));
        clearance = 4;
        while (queue.Count > 0 && region.Count < cap)
        {
            var c = queue.Dequeue();
            region.Add(c);

            int run = 0;
            for (int y = sy; y < h && y < sy + 4 && get(c.X, y, c.Z) == 0; y++)
            {
                run++;
            }

            clearance = System.Math.Min(clearance, run);

            foreach (var d in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
            {
                var n = (c.X + d.Item1, c.Z + d.Item2);
                if (!seen.Contains(n) && Floor(n.Item1, n.Item2))
                {
                    seen.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        return region;
    }
}
