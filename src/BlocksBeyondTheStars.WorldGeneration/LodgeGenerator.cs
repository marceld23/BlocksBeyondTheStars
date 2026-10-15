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
/// Builds a <b>lodge</b> deterministically from a seed (#2640): a small inhabited hut out in the open land, about
/// <see cref="Size"/>×<see cref="Size"/>, in the <see cref="SettlementStructure"/> container so the settlement
/// placement and stamp pipeline applies unchanged (like <see cref="BanditCampGenerator"/>).
/// <list type="bullet">
/// <item><b>The house</b> (local x 1..6, z 1..6): a log frame — corner posts, the top beam, the roof — with walls of the
/// biome's material (<see cref="ModuleMaterials"/>, alien trim like the village houses), windows, a two-wide hinge
/// doorway toward +Z, one or two two-cell beds, a torch and the resident's store crate.</item>
/// <item><b>In front of it</b>: a campfire on a stone ring and a log bench facing it — the evening seats.</item>
/// <item><b>The yard</b> (local x 8..12), by profession: a fenced crop plot (gatherer), a rock face of the planet's own
/// tier-1 ores in stone (miner), a woodpile beside two trees (woodcutter), a drying frame (hunter).</item>
/// </list>
/// Markers: <c>door_hinge</c>, <c>bed</c> (bed heads), <c>npc</c> (a resting spot per bed), <c>store</c>, <c>seat</c>
/// (the bench cells), <c>campfire</c>, <c>yard</c> (the work anchor), and per yard <c>plot</c> (crop cells),
/// <c>quarry</c> (face cells), <c>grove</c> (trunk bases) or <c>hunt</c>. No new block. The grove's trees are the
/// sapling tree's shape (<c>wood_log</c> + <c>tree_leaves</c>), so a felled one grows back the same.
/// </summary>
public static class LodgeGenerator
{
    /// <summary>The square footprint.</summary>
    public const int Size = 13;

    /// <summary>The structure height (the foundation row, three storeys of wall, the roof and the grove's crowns).</summary>
    public const int Height = 9;

    public const string MarkerDoor = "door_hinge";
    public const string MarkerBed = "bed";
    public const string MarkerNpc = "npc";
    public const string MarkerStore = "store";
    public const string MarkerSeat = "seat";
    public const string MarkerCampfire = "campfire";
    public const string MarkerYard = "yard";
    public const string MarkerPlot = "plot";
    public const string MarkerQuarry = "quarry";
    public const string MarkerGrove = "grove";
    public const string MarkerHunt = "hunt";

    /// <summary>The areas players may not change (local, inclusive): the house with its fire and bench, and the yard of
    /// this kind — the plot, the quarry face, the woodpile (the grove beside it stays free to fell) or the frame.</summary>
    public static IReadOnlyList<(int MinX, int MinZ, int MaxX, int MaxZ)> ProtectedAreas(string yard)
    {
        var areas = new List<(int, int, int, int)> { (0, 0, 7, Size - 1) };
        switch (yard)
        {
            case LodgeRules.YardPlot:
                areas.Add((8, 2, 12, 7));
                break;
            case LodgeRules.YardQuarry:
                areas.Add((8, 2, 12, 9));
                break;
            case LodgeRules.YardWoodpile:
                areas.Add((8, 0, 8, 3));
                break;
            case LodgeRules.YardDryingFrame:
                areas.Add((8, 2, 12, 6));
                break;
        }

        return areas;
    }

    /// <summary>
    /// Builds one lodge. <paramref name="yard"/> is a <see cref="LodgeRules"/> yard kind, <paramref name="surface"/> the
    /// biome's surface block, <paramref name="ores"/> the planet's own tier-1 ore blocks (the quarry face uses only
    /// these; none → a face of plain stone), <paramref name="alien"/> the alien trim.
    /// </summary>
    public static SettlementStructure Generate(long seed, string yard, string surface, IReadOnlyList<string> ores, bool alien, GameContent content)
    {
        var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32))));
        int w = Size, h = Height, l = Size;
        var blocks = new ushort[w * h * l];
        var shapes = new Dictionary<int, int>();
        var markers = new List<SettlementMarker>();

        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        var mats = ModuleMaterials.ForSettlement("hamlet", surface, alien, content);
        ushort ground = B(surface, B("dirt"));
        ushort stone = B("stone", ground);
        ushort log = B("wood_log", stone);
        ushort leaves = B("tree_leaves");
        ushort glass = B("glass", mats.Wall);
        ushort wall = mats.Wall != 0 ? mats.Wall : stone;
        ushort trim = mats.Accent != 0 ? mats.Accent : wall;
        ushort roof = alien ? trim : log;
        ushort floor = mats.Floor != 0 ? mats.Floor : wall;
        ushort path = mats.Path != 0 ? mats.Path : stone;
        ushort bed = B("bed");
        ushort crate = B("wood_crate", B("crate"));
        ushort torch = B("torch");
        ushort campfire = B("campfire");
        ushort dirt = B("dirt", ground);

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

        ushort Get(int x, int y, int z) => x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= l ? (ushort)0 : blocks[(x * h + y) * l + z];

        // Trampled ground: the whole footprint's foundation row (the stamper sinks y0 to ground level).
        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < l; z++)
            {
                Set(x, 0, z, ground);
            }
        }

        // ---- the house ----
        const int hx0 = 1, hx1 = 6, hz0 = 1, hz1 = 6;
        for (int x = hx0; x <= hx1; x++)
        {
            for (int z = hz0; z <= hz1; z++)
            {
                Set(x, 0, z, floor);
                bool shell = x == hx0 || x == hx1 || z == hz0 || z == hz1;
                if (!shell)
                {
                    continue;
                }

                bool corner = (x == hx0 || x == hx1) && (z == hz0 || z == hz1);
                for (int y = 1; y <= 3; y++)
                {
                    bool window = y == 2 && !corner
                        && (((x == hx0 || x == hx1) && z is 3 or 4) || (z == hz0 && x is 3 or 4));
                    ushort id = corner || y == 3 ? log : window ? glass : y == 1 ? trim : wall;
                    Set(x, y, z, id);
                }
            }
        }

        // The doorway: two wide, three tall, in the front (+Z) wall — the village house's opening, a hinge door fills it.
        for (int x = 3; x <= 4; x++)
        {
            for (int y = 1; y <= 3; y++)
            {
                Set(x, y, hz1, 0);
            }
        }

        markers.Add(new SettlementMarker(MarkerDoor, new Vector3i(3, 1, hz1), DoorWall.AlongX));

        // The roof: one row of overhang all round, a ridge along X on top.
        for (int x = hx0 - 1; x <= hx1 + 1; x++)
        {
            for (int z = hz0 - 1; z <= hz1 + 1; z++)
            {
                Set(x, 4, z, roof);
            }
        }

        for (int x = hx0; x <= hx1; x++)
        {
            Set(x, 5, 3, roof);
            Set(x, 5, 4, roof);
        }

        // Inside: one or two two-cell beds along the back wall (head at the wall, foot toward the door), the store crate
        // between them, a torch; with one bed a small log table takes the second corner.
        int beds = rng.NextDouble() < 0.55 ? 2 : 1;
        int bedHead = ShapeCode.Pack(BlockShape.BedHead, ShapeCode.YawToward(0, 1));
        if (bed != 0)
        {
            Set(2, 1, 2, bed, bedHead);
            Set(2, 1, 3, bed, FurnitureShapes.BedPartnerDescriptor(bedHead));
            markers.Add(new SettlementMarker(MarkerBed, new Vector3i(2, 1, 2)));
            markers.Add(new SettlementMarker(MarkerNpc, new Vector3i(3, 1, 4)));
            if (beds == 2)
            {
                Set(5, 1, 2, bed, bedHead);
                Set(5, 1, 3, bed, FurnitureShapes.BedPartnerDescriptor(bedHead));
                markers.Add(new SettlementMarker(MarkerBed, new Vector3i(5, 1, 2)));
                markers.Add(new SettlementMarker(MarkerNpc, new Vector3i(4, 1, 4)));
            }
            else
            {
                Set(5, 1, 2, log, ShapeCode.Pack(BlockShape.Table, 0));
            }
        }

        if (crate != 0)
        {
            Set(3, 1, 2, crate);
            markers.Add(new SettlementMarker(MarkerStore, new Vector3i(3, 1, 2)));
        }

        if (torch != 0)
        {
            Set(4, 1, 2, torch);
        }

        // ---- in front of the house: a path, the fire on its stone ring, the log bench facing it ----
        for (int z = hz1 + 1; z <= hz1 + 2; z++)
        {
            Set(3, 0, z, path);
            Set(4, 0, z, path);
        }

        for (int x = 1; x <= 3; x++)
        {
            for (int z = 8; z <= 10; z++)
            {
                Set(x, 0, z, stone);
            }
        }

        if (campfire != 0)
        {
            Set(2, 1, 9, campfire);
            markers.Add(new SettlementMarker(MarkerCampfire, new Vector3i(2, 1, 9)));
        }

        int bench = ShapeCode.Pack(BlockShape.Bench, 0); // backrest toward +Z: whoever sits faces the fire (−Z)
        for (int x = 1; x <= 3; x++)
        {
            Set(x, 1, 11, log, bench);
            markers.Add(new SettlementMarker(MarkerSeat, new Vector3i(x, 1, 11)));
        }

        // ---- the yard ----
        switch (yard)
        {
            case LodgeRules.YardPlot:
                StampPlot();
                break;
            case LodgeRules.YardQuarry:
                StampQuarry();
                break;
            case LodgeRules.YardWoodpile:
                StampWoodpile();
                break;
            default:
                StampDryingFrame();
                break;
        }

        var structure = new SettlementStructure(w, h, l, "lodge", ruined: false, inhabitant: alien ? "alien" : "human",
            blocks, markers, buildingCount: 1, mods: null, shapes: shapes);
        return structure;

        // A small fenced crop plot: berries and grain on tilled soil, the gate toward the house.
        void StampPlot()
        {
            ushort berry = B("flora_cropberry");
            ushort grain = B("flora_cropgrain", berry);
            int railX = ShapeCode.Pack(BlockShape.Fence, 0);
            int railZ = ShapeCode.Pack(BlockShape.Fence, 1);
            for (int x = 8; x <= 12; x++)
            {
                for (int z = 2; z <= 7; z++)
                {
                    bool rim = x == 8 || x == 12 || z == 2 || z == 7;
                    if (!rim)
                    {
                        Set(x, 0, z, dirt);
                        if (berry != 0)
                        {
                            Set(x, 1, z, rng.NextDouble() < 0.6 ? berry : grain);
                            markers.Add(new SettlementMarker(MarkerPlot, new Vector3i(x, 1, z)));
                        }

                        continue;
                    }

                    bool gate = x == 8 && z is 4 or 5;
                    if (!gate)
                    {
                        Set(x, 1, z, log, z == 2 || z == 7 ? railX : railZ);
                    }
                }
            }

            markers.Add(new SettlementMarker(MarkerYard, new Vector3i(8, 1, 4)));
        }

        // A rock face in stone: four wide, three tall, its front cells partly the planet's own tier-1 ores.
        void StampQuarry()
        {
            for (int x = 8; x <= 10; x++)
            {
                for (int z = 3; z <= 8; z++)
                {
                    Set(x, 0, z, stone);
                }
            }

            for (int z = 3; z <= 8; z++)
            {
                for (int y = 1; y <= 4; y++)
                {
                    Set(11, y, z, stone);
                    Set(12, y, z, stone);
                }
            }

            var face = new List<Vector3i>();
            for (int z = 4; z <= 7; z++)
            {
                for (int y = 1; y <= 3; y++)
                {
                    face.Add(new Vector3i(11, y, z));
                }
            }

            int oreCells = ores.Count == 0 ? 0 : 4 + rng.Next(5); // 4..8
            for (int i = face.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (face[i], face[j]) = (face[j], face[i]);
            }

            int marked = 0;
            for (int i = 0; i < face.Count; i++)
            {
                var c = face[i];
                if (i < oreCells)
                {
                    ushort ore = B(ores[rng.Next(ores.Count)], stone);
                    Set(c.X, c.Y, c.Z, ore);
                    markers.Add(new SettlementMarker(MarkerQuarry, c));
                    marked++;
                }
            }

            if (marked == 0)
            {
                // No ores of its own on this world: the miner breaks plain stone from four cells of the face.
                for (int i = 0; i < 4 && i < face.Count; i++)
                {
                    markers.Add(new SettlementMarker(MarkerQuarry, face[i]));
                }
            }

            markers.Add(new SettlementMarker(MarkerYard, new Vector3i(9, 1, 6)));
        }

        // A woodpile by the house, two trees beside it (the grove a woodcutter fells and replants).
        void StampWoodpile()
        {
            for (int z = 1; z <= 2; z++)
            {
                for (int y = 1; y <= 2; y++)
                {
                    Set(8, y, z, log);
                }
            }

            // Two trees, seven cells apart: a woodcutter's felling takes every leaf within three cells of the trunk, so
            // the crowns (radius 2) must keep clear of the other trunk's reach. Each stands on soil a sapling takes
            // (grass or dirt) whatever the biome's surface is, so the replanted sapling grows back into the same tree.
            ushort soil = surface is "grass" or "dirt" or "mud" ? ground : dirt;
            var trunks = new List<(int X, int Z)> { (11, 3), (11, 10) };
            foreach (var (tx, tz) in trunks)
            {
                if (log == 0 || leaves == 0)
                {
                    break;
                }

                Set(tx, 0, tz, soil);
                int height = 4 + (System.Math.Abs(tx * 31 + tz * 17 + (int)(seed & 0xFF)) % 2); // 4 or 5
                for (int y = 1; y <= height; y++)
                {
                    Set(tx, y, tz, log);
                }

                int top = height;
                for (int dy = -1; dy <= 2; dy++)
                {
                    int r = dy <= 0 ? 2 : dy == 1 ? 1 : 0;
                    for (int dx = -r; dx <= r; dx++)
                    {
                        for (int dz = -r; dz <= r; dz++)
                        {
                            if (r == 2 && System.Math.Abs(dx) == 2 && System.Math.Abs(dz) == 2)
                            {
                                continue; // clipped corners keep the crown round (the sapling tree's shape)
                            }

                            if (Get(tx + dx, top + dy, tz + dz) == 0)
                            {
                                Set(tx + dx, top + dy, tz + dz, leaves);
                            }
                        }
                    }
                }

                markers.Add(new SettlementMarker(MarkerGrove, new Vector3i(tx, 1, tz)));
            }

            markers.Add(new SettlementMarker(MarkerYard, new Vector3i(9, 1, 8)));
        }

        // A drying frame: two log posts and a rail between them.
        void StampDryingFrame()
        {
            int post = ShapeCode.Pack(BlockShape.Post, 0);
            int rail = ShapeCode.Pack(BlockShape.Fence, 0);
            foreach (int x in new[] { 9, 12 })
            {
                Set(x, 1, 3, log, post);
                Set(x, 2, 3, log, post);
            }

            Set(10, 2, 3, log, rail);
            Set(11, 2, 3, log, rail);
            markers.Add(new SettlementMarker(MarkerHunt, new Vector3i(10, 1, 5)));
            markers.Add(new SettlementMarker(MarkerYard, new Vector3i(10, 1, 5)));
        }
    }
}
