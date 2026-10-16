// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Builds a <b>pioneer outpost</b> deterministically from a seed (#2681) — the lodge variant a player-like NPC lives in: a
/// small base the way a player builds one, in the <see cref="SettlementStructure"/> container so the lodge placement and
/// stamp pipeline applies unchanged (like <see cref="LodgeGenerator"/>).
/// <list type="bullet">
/// <item><b>The room</b> (local x/z 1..9): fully closed — floor, four walls with glass windows, a flat roof — in one wall
/// material per outpost (<see cref="WallKinds"/>: stone, the biome's earth, iron or steel), a two-wide hinge door toward
/// +Z.</item>
/// <item><b>Inside</b>: a <c>base_core</c> in the middle (decoration only — the server registers no base for it), 2–4
/// crates along the back wall, a bed, a table with a chair and a torch.</item>
/// <item><b>The tunnel</b>: a ladder shaft in the front-right corner down to a small cellar with one wooden box. Either
/// visible — a closet with its own door, the ladder going down through the floor — or hidden: the shaft lies under a
/// planter of loose soil with a plant on it, the one cell of the outpost a visitor may dig (<see cref="MarkerSoil"/>).</item>
/// </list>
/// The cellar lies below the structure's floor row, so it travels beside the structure (<see cref="Outpost.Below"/>) and
/// the server writes it after the stamp. A decorative rowboat (<see cref="Boat"/>) is stamped on the water when the
/// outpost stands by it. No new block — everything here exists.
/// </summary>
public static class PioneerOutpostGenerator
{
    /// <summary>The square footprint.</summary>
    public const int Size = 15;

    /// <summary>The structure height (the floor row, three rows of wall, the roof and one row of headroom).</summary>
    public const int Height = 6;

    /// <summary>How far below the floor row the cellar's own floor lies.</summary>
    public const int CellarDepth = 5;

    public const string MarkerDoor = LodgeGenerator.MarkerDoor;
    public const string MarkerBed = LodgeGenerator.MarkerBed;
    public const string MarkerNpc = LodgeGenerator.MarkerNpc;
    public const string MarkerStore = LodgeGenerator.MarkerStore;
    public const string MarkerSeat = LodgeGenerator.MarkerSeat;
    public const string MarkerYard = LodgeGenerator.MarkerYard;

    /// <summary>The decorative base core.</summary>
    public const string MarkerCore = "core";

    /// <summary>The shaft's top cell in the house floor (a ladder, or the planter soil over a hidden shaft).</summary>
    public const string MarkerHatch = "hatch";

    /// <summary>The planter soil over a hidden shaft — the one cell of an outpost a visitor may dig.</summary>
    public const string MarkerSoil = "soil";

    /// <summary>The cellar's wooden box.</summary>
    public const string MarkerCellarStore = "cellar_store";

    /// <summary>A standing spot in the cellar.</summary>
    public const string MarkerCellar = "cellar";

    public const string WallStone = "stone";
    public const string WallEarth = "earth";
    public const string WallIron = "iron";
    public const string WallSteel = "steel";

    /// <summary>The wall materials an outpost is built from, one per outpost.</summary>
    public static readonly IReadOnlyList<string> WallKinds = new[] { WallStone, WallEarth, WallIron, WallSteel };

    /// <summary>The fewest and most crates in the room.</summary>
    public const int MinChests = 2;

    public const int MaxChests = 4;

    /// <summary>The shaft's column (local x/z) — the front-right corner of the room, above the cellar's corner.</summary>
    public const int ShaftX = 8;

    public const int ShaftZ = 8;

    /// <summary>One built outpost: the structure (floor row and up), the cellar cells below it and what was rolled.</summary>
    public sealed class Outpost
    {
        public Outpost(SettlementStructure structure, IReadOnlyList<(Vector3i Local, ushort Block, int Shape)> below,
            IReadOnlyList<SettlementMarker> belowMarkers, string wallKind, ushort wallBlock, bool hiddenShaft, int chests)
        {
            Structure = structure;
            Below = below;
            BelowMarkers = belowMarkers;
            WallKind = wallKind;
            WallBlock = wallBlock;
            HiddenShaft = hiddenShaft;
            Chests = chests;
        }

        public SettlementStructure Structure { get; }

        /// <summary>The cellar and the shaft below the floor row (local y &lt; 0): every cell of the cellar's box, air
        /// included, so a stamp carves the room out of whatever the ground holds there.</summary>
        public IReadOnlyList<(Vector3i Local, ushort Block, int Shape)> Below { get; }

        /// <summary>The cellar's markers (local y &lt; 0): <see cref="MarkerCellarStore"/>, <see cref="MarkerCellar"/>.</summary>
        public IReadOnlyList<SettlementMarker> BelowMarkers { get; }

        /// <summary>One of <see cref="WallKinds"/>.</summary>
        public string WallKind { get; }

        /// <summary>The wall block (the earth kind resolves to the biome's, <see cref="EarthBlockKey"/>).</summary>
        public ushort WallBlock { get; }

        /// <summary>The shaft hides under a planter (no closet, no door).</summary>
        public bool HiddenShaft { get; }

        /// <summary>The crates in the room (<see cref="MinChests"/>..<see cref="MaxChests"/>).</summary>
        public int Chests { get; }
    }

    /// <summary>The block an "earth" wall is built of on a biome surface: its own soil, never a block that settles (sand,
    /// snow and ash would slump out of a wall) and never a grass top.</summary>
    public static string EarthBlockKey(string surface) => surface switch
    {
        "grass" or "alien_grass" or "mycelium" or "tainted_soil" or "peat" or "" => "dirt",
        "sand" or "black_sand" => "sandstone",
        "snow" => "ice",
        "ash" => "basalt",
        "scree" => "stone",
        _ => surface,
    };

    /// <summary>The block key of a wall kind on a biome surface.</summary>
    public static string WallBlockKey(string wallKind, string surface) => wallKind switch
    {
        WallIron => "iron_wall",
        WallSteel => "steel_wall",
        WallEarth => EarthBlockKey(surface),
        _ => "stone",
    };

    /// <summary>
    /// Builds one outpost. <paramref name="surface"/> is the biome's surface block. <paramref name="wallKind"/>,
    /// <paramref name="hidden"/> and <paramref name="chests"/> force what the seed would roll (tests); every roll is drawn
    /// anyway, so a forced value never shifts the others.
    /// </summary>
    public static Outpost Generate(long seed, string surface, GameContent content, string? wallKind = null, bool? hidden = null,
        int chests = 0)
    {
        var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32))));
        int wallRoll = rng.Next(WallKinds.Count);
        bool hiddenRoll = rng.NextDouble() < 0.5;
        int chestRoll = rng.Next(MinChests, MaxChests + 1);
        string kind = wallKind is not null && System.Linq.Enumerable.Contains(WallKinds, wallKind) ? wallKind : WallKinds[wallRoll];
        bool hiddenShaft = hidden ?? hiddenRoll;
        int chestCount = chests is >= MinChests and <= MaxChests ? chests : chestRoll;

        int w = Size, h = Height, l = Size;
        var blocks = new ushort[w * h * l];
        var shapes = new Dictionary<int, int>();
        var markers = new List<SettlementMarker>();

        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        ushort stone = B("stone");
        ushort dirt = B("dirt", stone);
        ushort ground = B(surface, dirt);
        ushort wall = B(WallBlockKey(kind, surface), stone);
        bool metal = kind is WallIron or WallSteel;
        ushort floor = metal ? B("steel_floor", wall) : wall;
        ushort glass = B("glass", wall);
        ushort path = metal ? B("steel_floor", stone) : stone;
        ushort log = B("wood_log", stone);
        ushort bed = B("bed");
        ushort crate = B("crate", B("wood_crate"));
        ushort box = B("wood_crate", crate);
        ushort core = B("base_core");
        ushort torch = B("torch");
        ushort lantern = B("lantern", torch);
        ushort ladder = B("ladder");
        ushort fern = B("flora_fern", B("flora_bush"));
        ushort bush = B("flora_bush", fern);

        void Set(int x, int y, int z, ushort id, int shape = 0)
        {
            if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= l)
            {
                return;
            }

            int idx = (x * h + y) * l + z;
            blocks[idx] = id;
            if (id != 0 && shape != 0)
            {
                shapes[idx] = shape;
            }
            else
            {
                shapes.Remove(idx);
            }
        }

        // Trampled ground under the whole footprint (the stamper sinks y0 to ground level).
        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < l; z++)
            {
                Set(x, 0, z, ground);
            }
        }

        // ---- the room: floor, walls, roof — closed all round ----
        const int hx0 = 1, hx1 = 9, hz0 = 1, hz1 = 9;
        for (int x = hx0; x <= hx1; x++)
        {
            for (int z = hz0; z <= hz1; z++)
            {
                Set(x, 0, z, floor);
                Set(x, 4, z, wall); // the flat roof
                bool shell = x == hx0 || x == hx1 || z == hz0 || z == hz1;
                if (!shell)
                {
                    continue;
                }

                bool corner = (x == hx0 || x == hx1) && (z == hz0 || z == hz1);
                for (int y = 1; y <= 3; y++)
                {
                    bool window = y == 2 && !corner
                        && ((z == hz0 && x is 3 or 7) || (x == hx0 && z is 4 or 6) || (x == hx1 && z is 3 or 4));
                    Set(x, y, z, window ? glass : wall);
                }
            }
        }

        // The front door: two wide, three tall (the door hangs in it).
        for (int x = 4; x <= 5; x++)
        {
            for (int y = 1; y <= 3; y++)
            {
                Set(x, y, hz1, 0);
            }
        }

        markers.Add(new SettlementMarker(MarkerDoor, new Vector3i(4, 1, hz1), DoorWall.AlongX));

        // ---- inside ----
        // The bed along the left of the back wall (head at the wall), a torch beside it.
        int bedHead = ShapeCode.Pack(BlockShape.BedHead, ShapeCode.YawToward(0, 1));
        if (bed != 0)
        {
            Set(2, 1, 2, bed, bedHead);
            Set(2, 1, 3, bed, FurnitureShapes.BedPartnerDescriptor(bedHead));
            markers.Add(new SettlementMarker(MarkerBed, new Vector3i(2, 1, 2)));
        }

        markers.Add(new SettlementMarker(MarkerNpc, new Vector3i(3, 1, 4)));
        if (torch != 0)
        {
            Set(3, 1, 2, torch);
        }

        // The crates along the back wall, the first one is where the work goes.
        for (int i = 0; i < chestCount && crate != 0; i++)
        {
            Set(4 + i, 1, 2, crate);
            markers.Add(new SettlementMarker(MarkerStore, new Vector3i(4 + i, 1, 2)));
        }

        // The base core in the middle — a player's base has one, this one founds nothing.
        if (core != 0)
        {
            Set(5, 1, 5, core);
            markers.Add(new SettlementMarker(MarkerCore, new Vector3i(5, 1, 5)));
        }

        // A table by the left wall's window and the chair facing it (the evening seat).
        Set(2, 1, 6, log, ShapeCode.Pack(BlockShape.Table, 0));
        Set(2, 1, 7, log, ShapeCode.Pack(BlockShape.Chair, 0)); // backrest toward +Z: whoever sits faces the table (−Z)
        markers.Add(new SettlementMarker(MarkerSeat, new Vector3i(2, 1, 7)));

        // ---- the shaft's top ----
        if (hiddenShaft)
        {
            // Two planters in the front-right corner; the shaft lies under the corner one. Its soil is loose (the one cell a
            // visitor may dig) — pick the plant, dig the soil, and the ladder is below.
            Set(ShaftX - 1, 0, ShaftZ, dirt);
            Set(ShaftX, 0, ShaftZ, dirt);
            Set(ShaftX - 1, 1, ShaftZ, bush);
            Set(ShaftX, 1, ShaftZ, fern);
            markers.Add(new SettlementMarker(MarkerSoil, new Vector3i(ShaftX, 0, ShaftZ)));
        }
        else
        {
            // A closet of its own in the corner, with a one-wide door; the ladder runs from its floor down.
            for (int y = 1; y <= 3; y++)
            {
                Set(6, y, 7, wall);
                Set(6, y, 8, wall);
                Set(6, y, 6, wall);
                Set(8, y, 6, wall);
            }

            markers.Add(new SettlementMarker(MarkerDoor, new Vector3i(7, 1, 6), DoorWall.AlongX));
            Set(ShaftX, 1, ShaftZ, ladder);
            Set(ShaftX, 0, ShaftZ, ladder);
        }

        markers.Add(new SettlementMarker(MarkerHatch, new Vector3i(ShaftX, 0, ShaftZ)));

        // ---- in front: a short path, the work anchor ----
        for (int z = hz1 + 1; z <= hz1 + 2; z++)
        {
            Set(4, 0, z, path);
            Set(5, 0, z, path);
        }

        markers.Add(new SettlementMarker(MarkerYard, new Vector3i(5, 1, hz1 + 3)));

        // ---- the cellar below the front-right of the room ----
        var below = new List<(Vector3i, ushort, int)>();
        var belowMarkers = new List<SettlementMarker>();
        const int cx0 = 4, cx1 = 9, cz0 = 4, cz1 = 9;
        int floorY = -CellarDepth;
        for (int x = cx0; x <= cx1; x++)
        {
            for (int z = cz0; z <= cz1; z++)
            {
                for (int y = floorY; y <= -1; y++)
                {
                    bool inside = x > cx0 && x < cx1 && z > cz0 && z < cz1 && y > floorY && y < -1;
                    bool shaft = x == ShaftX && z == ShaftZ && y > floorY;
                    ushort id = shaft ? ladder : inside ? (ushort)0 : stone;
                    below.Add((new Vector3i(x, y, z), id, 0));
                }
            }
        }

        // The wooden box in the far corner and a lantern beside it.
        int boxY = floorY + 1;
        Replace(below, new Vector3i(cx0 + 1, boxY, cz0 + 1), box);
        Replace(below, new Vector3i(cx0 + 2, boxY, cz0 + 1), lantern);
        belowMarkers.Add(new SettlementMarker(MarkerCellarStore, new Vector3i(cx0 + 1, boxY, cz0 + 1)));
        belowMarkers.Add(new SettlementMarker(MarkerCellar, new Vector3i(cx0 + 2, boxY, cz0 + 3)));

        var structure = new SettlementStructure(w, h, l, "outpost", ruined: false, inhabitant: "human",
            blocks, markers, buildingCount: 1, mods: null, shapes: shapes);
        return new Outpost(structure, below, belowMarkers, kind, wall, hiddenShaft, crate != 0 ? chestCount : 0);
    }

    private static void Replace(List<(Vector3i Local, ushort Block, int Shape)> cells, Vector3i at, ushort block)
    {
        int i = cells.FindIndex(c => c.Local.Equals(at));
        if (i >= 0)
        {
            cells[i] = (at, block, 0);
        }
    }

    /// <summary>The boat's length (along its axis) and width.</summary>
    public const int BoatLength = 6;

    public const int BoatWidth = 3;

    /// <summary>
    /// A small rowboat of logs (#2681), as cells relative to its corner at the water's top row (y 0 = the topmost water
    /// cell, so the hull sits in the water and rises one row above it): log sides, a pointed bow and a stern post, a deck of
    /// log slabs with a bench across, a lantern on the stern. <paramref name="alongX"/> lays it along X (else along Z, the
    /// bow at the low end). Decoration only — no vehicle.
    /// </summary>
    public static IReadOnlyList<(Vector3i Local, ushort Block, int Shape)> Boat(bool alongX, GameContent content)
    {
        ushort log = content.GetBlock("wood_log")?.NumericId.Value ?? 0;
        ushort lantern = content.GetBlock("lantern")?.NumericId.Value ?? 0;
        var cells = new List<(Vector3i, ushort, int)>();
        if (log == 0)
        {
            return cells;
        }

        int slab = ShapeCode.Pack(BlockShape.Slab, 0);
        int bench = ShapeCode.Pack(BlockShape.Bench, alongX ? ShapeCode.YawToward(1, 0) : 0);
        void Add(int across, int y, int along, ushort id, int shape = 0)
            => cells.Add((alongX ? new Vector3i(along, y, across) : new Vector3i(across, y, along), id, shape));

        for (int along = 0; along < BoatLength; along++)
        {
            bool bow = along == 0, stern = along == BoatLength - 1;
            for (int y = 0; y <= 1; y++)
            {
                if (bow)
                {
                    Add(1, y, along, log); // the pointed bow
                    continue;
                }

                Add(0, y, along, log);
                Add(2, y, along, log);
                if (stern)
                {
                    Add(1, y, along, log); // the stern post
                }
            }

            if (!bow && !stern)
            {
                Add(1, 0, along, log, along == 3 ? bench : slab); // the deck, a bench across it
            }
        }

        if (lantern != 0)
        {
            Add(1, 2, BoatLength - 1, lantern);
        }

        return cells;
    }
}
