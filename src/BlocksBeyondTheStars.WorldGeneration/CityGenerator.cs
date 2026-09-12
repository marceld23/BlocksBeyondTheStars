// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The city composer (#1793): ONE gigantic walled city assembled from 32×32 district modules on a 7×7 grid
/// with 4-wide streets between them — ~256 blocks a side, an order of magnitude past the largest procedural
/// settlement. It is built for the G.D.S. desert world (a lava desert whose single city is the only cool,
/// green place on the planet), but it only knows a seed, a palette and a few zones to leave open: the
/// landing pad it is centred on and the wreck crash site inside its walls. Every module role is procedural
/// here; a hand-authored <c>data/settlement_templates.json</c> module with a matching role may replace it
/// later without touching this file.
/// <para>Modules by position: the centre module is the landing plaza (open ground around the pad, lamps and
/// guard posts ringing it), the ring around it holds the markets, the hall and two gardens, every third
/// outer module is a garden with a pool and trees, the four corners are watch towers, everything else is
/// housing. Houses are purple or red — the two G.D.S. colours — as a per-cell tint on the wall blocks, the
/// perimeter wall is purple with one red band, and the four gates open onto the axis streets.</para>
/// <para>Deterministic from the seed: the same body always composes the same city.</para>
/// </summary>
public static class CityGenerator
{
    public const string Tier = "metropolis";
    public const int Modules = 7;
    public const int ModuleSize = 32;
    public const int Street = 4;
    public const int Height = 20;
    public const int WallHeight = 5;
    public const int GateHalfWidth = 4;

    /// <summary>Dark purple — the chassis of the G.D.S. and the colour of most of the city's walls.</summary>
    public const int Purple = 0x3A1F5C;

    /// <summary>The G.D.S. red — the stripe on their machines, the band on the wall, every other house.</summary>
    public const int Red = 0x8A1C24;

    /// <summary>Blocks a side, streets included (7 × 32 + 8 × 4 = 256).</summary>
    public static int Footprint => Modules * ModuleSize + (Modules + 1) * Street;

    /// <summary>A structure-local rectangle (inclusive) the composer keeps as open, paved ground: the landing
    /// pad at the centre, the wreck crash site — anything another stamper owns.</summary>
    public readonly struct OpenZone
    {
        public readonly int MinX, MinZ, MaxX, MaxZ;

        public OpenZone(int minX, int minZ, int maxX, int maxZ)
        {
            MinX = minX;
            MinZ = minZ;
            MaxX = maxX;
            MaxZ = maxZ;
        }

        public bool Contains(int x, int z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

        public bool Intersects(int x0, int z0, int x1, int z1) => x0 <= MaxX && x1 >= MinX && z0 <= MaxZ && z1 >= MinZ;
    }

    /// <summary>District roles, chosen per grid cell — see the class summary.</summary>
    public enum Role
    {
        Plaza,
        Open,
        Market,
        Hall,
        Garden,
        Tower,
        Housing,
    }

    /// <summary>The role of the module at grid cell (<paramref name="gx"/>, <paramref name="gz"/>) — public so
    /// tests (and a future template override) can ask the same question the composer answers.</summary>
    public static Role RoleAt(int gx, int gz)
    {
        int c = Modules / 2;
        if (gx == c && gz == c)
        {
            return Role.Plaza;
        }

        bool corner = (gx == 0 || gx == Modules - 1) && (gz == 0 || gz == Modules - 1);
        if (corner)
        {
            return Role.Tower;
        }

        int dx = System.Math.Abs(gx - c), dz = System.Math.Abs(gz - c);
        if (dx <= 1 && dz <= 1)
        {
            // The inner ring: markets east and west of the plaza, the hall north, a garden south, gardens on
            // two diagonals, housing on the other two.
            if (gz == c)
            {
                return Role.Market;
            }

            if (gx == c)
            {
                return gz < c ? Role.Hall : Role.Garden;
            }

            return (gx - c) == (gz - c) ? Role.Garden : Role.Housing;
        }

        return (gx + gz) % 3 == 0 ? Role.Garden : Role.Housing;
    }

    /// <summary>Structure-local origin of the module at grid cell (<paramref name="gx"/>, <paramref name="gz"/>).</summary>
    public static (int X, int Z) ModuleOrigin(int gx, int gz)
        => (Street + gx * (ModuleSize + Street), Street + gz * (ModuleSize + Street));

    public static SettlementStructure Generate(long seed, GameContent content, IReadOnlyList<OpenZone> openZones)
    {
        int w = Footprint, l = Footprint, h = Height;
        var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32)) ^ (int)WorldGenerator.StableHash("city:gds")));

        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        ushort stone = B("stone");
        ushort wall = B("iron_wall", stone);
        ushort glass = B("glass");
        ushort ladder = B("ladder");
        ushort street = B("carbon", stone);
        ushort paving = B("sandstone", stone);
        ushort lamp = B("strip_light_warm", B("torch", glass));
        ushort grass = B("grass", paving);
        ushort water = B("water");
        ushort log = B("wood_log", stone);
        ushort leaves = B("tree_leaves", B("giant_leaves"));
        ushort fern = B("flora_fern", B("flora_plant"));
        ushort bush = B("flora_bush", fern);
        ushort steelFloor = B("steel_floor", paving);

        var blocks = new ushort[w * h * l];
        var mods = new Dictionary<int, (int Tint, int Glow)>();
        var markers = new List<SettlementMarker>();
        int buildings = 0;
        int tint = 0; // the tint the Set closure applies to wall/accent cells while a building is stamped

        bool Open(int x, int z)
        {
            foreach (var zone in openZones)
            {
                if (zone.Contains(x, z))
                {
                    return true;
                }
            }

            return false;
        }

        void Set(int x, int y, int z, ushort b)
        {
            if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= l)
            {
                return;
            }

            if (y > 0 && Open(x, z))
            {
                return; // the pad and the crash site stay open ground, whatever a module wanted there
            }

            int idx = (x * h + y) * l + z;
            blocks[idx] = b;
            if (tint != 0 && b != 0 && (b == wall || b == stone || b == paving))
            {
                mods[idx] = (tint, 0);
            }
            else
            {
                mods.Remove(idx); // air, an untinted stamp or a light over a tinted cell: no tint survives
            }
        }

        void Fill(int x0, int y0, int z0, int x1, int y1, int z1, ushort b)
        {
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        Set(x, y, z, b);
                    }
        }

        void LampPost(int x, int z, int height = 3)
        {
            tint = 0;
            for (int y = 1; y < height; y++)
            {
                Set(x, y, z, stone);
            }

            Set(x, height, z, lamp);
        }

        // 1) Streets: the grid lanes, dark paving.
        for (int i = 0; i <= Modules; i++)
        {
            int s0 = i * (ModuleSize + Street);
            Fill(s0, 0, 0, s0 + Street - 1, 0, l - 1, street);
            Fill(0, 0, s0, w - 1, 0, s0 + Street - 1, street);
        }

        // 2) Modules.
        for (int gx = 0; gx < Modules; gx++)
            for (int gz = 0; gz < Modules; gz++)
            {
                var (mx, mz) = ModuleOrigin(gx, gz);
                var role = RoleAt(gx, gz);
                bool blocked = false;
                foreach (var zone in openZones)
                {
                    if (role != Role.Plaza && zone.Intersects(mx, mz, mx + ModuleSize - 1, mz + ModuleSize - 1))
                    {
                        blocked = true;
                    }
                }

                if (blocked)
                {
                    role = Role.Open;
                }

                tint = 0;
                Fill(mx, 0, mz, mx + ModuleSize - 1, 0, mz + ModuleSize - 1, paving); // module floor
                switch (role)
                {
                    case Role.Plaza:
                        StampPlaza(mx, mz);
                        break;
                    case Role.Open:
                        StampOpen(mx, mz);
                        break;
                    case Role.Market:
                        StampMarket(mx, mz);
                        break;
                    case Role.Hall:
                        StampHall(mx, mz);
                        break;
                    case Role.Garden:
                        StampGarden(mx, mz);
                        break;
                    case Role.Tower:
                        StampTower(mx, mz, gx == 0, gz == 0);
                        break;
                    default:
                        StampHousing(mx, mz);
                        break;
                }
            }

        // 3) The perimeter wall with its red band, four gates on the axis streets, guard posts at the gates.
        StampWall();

        tint = 0;
        return new SettlementStructure(w, h, l, Tier, ruined: false, inhabitant: "human", blocks, markers, System.Math.Max(1, buildings), mods);

        // ------------------------------------------------------------------ modules

        void House(int ox, int oz, int fp, int storeys, int doorSide, bool red)
        {
            tint = red ? Red : Purple;
            SettlementGenerator.StampBuilding(Set, ox, oz, fp, storeys, wall, wall, glass, ladder, doorSide, 0, rng, ruined: false, ceilingLight: lamp);
            tint = 0;
            SettlementGenerator.DecorateAround(Set, ox, oz, fp, doorSide, lamp, fern, false, rng);
            buildings++;
        }

        void DoorMarker(int ox, int oz, int fp, int doorSide)
        {
            int mid = fp / 2, w0 = System.Math.Max(1, mid - 1);
            var doorCell = doorSide switch
            {
                0 => new Vector3i(ox + w0, 1, oz),
                1 => new Vector3i(ox + w0, 1, oz + fp - 1),
                2 => new Vector3i(ox, 1, oz + w0),
                _ => new Vector3i(ox + fp - 1, 1, oz + w0),
            };
            markers.Add(new SettlementMarker("door_slide", doorCell));
        }

        void StampHousing(int mx, int mz)
        {
            // Three by three plots on the settlement generator's 8-block stride, inset so the module keeps a
            // 3-block lane along every edge; houses 4–6 wide, one to three storeys, purple or red by turns.
            const int plots = 3;
            int inset = (ModuleSize - plots * SettlementGenerator.Plot - 1) / 2;
            int plot = 0;
            for (int cx = 0; cx < plots; cx++)
                for (int cz = 0; cz < plots; cz++)
                {
                    int fp = 6 - rng.Next(0, 3);
                    int storeys = 1 + rng.Next(0, 3);
                    int doorSide = rng.Next(0, 4);
                    bool red = rng.NextDouble() < 0.4;
                    int off = (6 - fp) / 2;
                    int ox = mx + inset + cx * SettlementGenerator.Plot + 1 + off;
                    int oz = mz + inset + cz * SettlementGenerator.Plot + 1 + off;
                    House(ox, oz, fp, storeys, doorSide, red);
                    markers.Add(new SettlementMarker("npc", new Vector3i(ox + fp / 2, 1, oz + fp / 2)));
                    DoorMarker(ox, oz, fp, doorSide);
                    plot++;
                }

            // Lanes between the plots, lighter than the streets.
            tint = 0;
            for (int i = 0; i <= plots; i++)
            {
                int x = mx + inset + i * SettlementGenerator.Plot;
                int z = mz + inset + i * SettlementGenerator.Plot;
                Fill(x, 0, mz + inset, x, 0, mz + inset + plots * SettlementGenerator.Plot, stone);
                Fill(mx + inset, 0, z, mx + inset + plots * SettlementGenerator.Plot, 0, z, stone);
            }
        }

        void StampMarket(int mx, int mz)
        {
            // Two vendors and the mission board in the front row, stalls behind, a lamp at each corner.
            int fp = 6;
            var (ox0, oz0) = (mx + 3, mz + 3);
            House(ox0, oz0, fp, 2, 1, false);
            markers.Add(new SettlementMarker("vendor", new Vector3i(ox0 + fp / 2, 1, oz0 + fp / 2)));
            DoorMarker(ox0, oz0, fp, 1);

            var (ox1, oz1) = (mx + ModuleSize - 3 - fp, mz + 3);
            House(ox1, oz1, fp, 2, 1, true);
            markers.Add(new SettlementMarker("vendor", new Vector3i(ox1 + fp / 2, 1, oz1 + fp / 2)));
            DoorMarker(ox1, oz1, fp, 1);

            var (ox2, oz2) = (mx + ModuleSize / 2 - fp / 2, mz + ModuleSize - 3 - fp);
            House(ox2, oz2, fp, 2, 0, false);
            markers.Add(new SettlementMarker("mission_board", new Vector3i(ox2 + fp / 2, 1, oz2 + fp / 2)));
            DoorMarker(ox2, oz2, fp, 0);

            // Market stalls: a glass canopy on four posts, an NPC minding each.
            tint = 0;
            for (int i = 0; i < 3; i++)
            {
                int sx = mx + 4 + i * 8, sz = mz + ModuleSize / 2 - 1;
                Fill(sx, 1, sz, sx, 2, sz, stone);
                Fill(sx + 3, 1, sz, sx + 3, 2, sz, stone);
                Fill(sx, 1, sz + 3, sx, 2, sz + 3, stone);
                Fill(sx + 3, 1, sz + 3, sx + 3, 2, sz + 3, stone);
                Fill(sx, 3, sz, sx + 3, 3, sz + 3, glass);
                markers.Add(new SettlementMarker("npc", new Vector3i(sx + 1, 1, sz + 1)));
            }

            LampPost(mx + 1, mz + 1);
            LampPost(mx + ModuleSize - 2, mz + 1);
            LampPost(mx + 1, mz + ModuleSize - 2);
            LampPost(mx + ModuleSize - 2, mz + ModuleSize - 2);
        }

        void StampHall(int mx, int mz)
        {
            // The G.D.S. hall: one 14-wide, two-storey building of purple stone with a red crown, four
            // residents inside, a guard at each corner of the forecourt.
            int fp = 14;
            int ox = mx + (ModuleSize - fp) / 2, oz = mz + (ModuleSize - fp) / 2;
            House(ox, oz, fp, 2, 1, false);
            tint = Red;
            Fill(ox, 9, oz, ox + fp - 1, 9, oz + fp - 1, wall); // the red crown row
            tint = 0;
            for (int i = 0; i < 4; i++)
            {
                markers.Add(new SettlementMarker("npc", new Vector3i(ox + 3 + (i % 2) * (fp - 7), 1, oz + 3 + (i / 2) * (fp - 7))));
            }

            markers.Add(new SettlementMarker("guard_post", new Vector3i(ox - 2, 1, oz + fp + 2)));
            markers.Add(new SettlementMarker("guard_post", new Vector3i(ox + fp + 1, 1, oz + fp + 2)));
            DoorMarker(ox, oz, fp, 1);
            LampPost(mx + 1, mz + 1);
            LampPost(mx + ModuleSize - 2, mz + ModuleSize - 2);
        }

        void StampGarden(int mx, int mz)
        {
            // A green square: grass, an 8×8 pool in the middle with a stone rim, six trees, ferns and bushes,
            // a lamp at each corner and two strollers.
            tint = 0;
            Fill(mx + 1, 0, mz + 1, mx + ModuleSize - 2, 0, mz + ModuleSize - 2, grass);
            int cx = mx + ModuleSize / 2, cz = mz + ModuleSize / 2;
            Fill(cx - 5, 0, cz - 5, cx + 4, 0, cz + 4, stone);
            if (water != 0)
            {
                Fill(cx - 4, 0, cz - 4, cx + 3, 0, cz + 3, water);
            }

            var spots = new (int X, int Z)[] { (4, 4), (ModuleSize - 6, 4), (4, ModuleSize - 6), (ModuleSize - 6, ModuleSize - 6), (ModuleSize / 2 - 1, 3), (ModuleSize / 2 - 1, ModuleSize - 5) };
            foreach (var (sx, sz) in spots)
            {
                Tree(mx + sx, mz + sz, 4 + rng.Next(0, 2));
            }

            for (int i = 0; i < 24; i++)
            {
                int fx = mx + 1 + rng.Next(0, ModuleSize - 2), fz = mz + 1 + rng.Next(0, ModuleSize - 2);
                if (System.Math.Abs(fx - cx) > 6 || System.Math.Abs(fz - cz) > 6)
                {
                    Set(fx, 1, fz, rng.NextDouble() < 0.7 ? fern : bush);
                }
            }

            LampPost(mx + 1, mz + 1);
            LampPost(mx + ModuleSize - 2, mz + 1);
            LampPost(mx + 1, mz + ModuleSize - 2);
            LampPost(mx + ModuleSize - 2, mz + ModuleSize - 2);
            markers.Add(new SettlementMarker("npc", new Vector3i(cx - 7, 1, cz)));
            markers.Add(new SettlementMarker("npc", new Vector3i(cx + 7, 1, cz)));
        }

        void Tree(int x, int z, int trunk)
        {
            tint = 0;
            for (int y = 1; y <= trunk; y++)
            {
                Set(x, y, z, log);
            }

            if (leaves == 0)
            {
                return;
            }

            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        bool corner = System.Math.Abs(dx) == 2 && System.Math.Abs(dz) == 2;
                        bool rim = System.Math.Abs(dx) == 2 || System.Math.Abs(dz) == 2;
                        if (corner || (rim && dy != 0) || (dx == 0 && dz == 0 && dy <= 0))
                        {
                            continue;
                        }

                        Set(x + dx, trunk + dy, z + dz, leaves);
                    }

            Set(x, trunk + 2, z, leaves);
        }

        void StampTower(int mx, int mz, bool west, bool north)
        {
            // A 7×7 watch tower, 14 tall, purple with two red bands, in the corner that faces the wall; two
            // guards at its foot, a lamp on top and lights inside the shaft.
            int fp = 7, height = 14;
            int ox = west ? mx + 2 : mx + ModuleSize - 2 - fp;
            int oz = north ? mz + 2 : mz + ModuleSize - 2 - fp;
            tint = Purple;
            for (int y = 1; y <= height; y++)
            {
                for (int x = 0; x < fp; x++)
                    for (int z = 0; z < fp; z++)
                    {
                        bool shell = x == 0 || z == 0 || x == fp - 1 || z == fp - 1;
                        if (shell || y == height)
                        {
                            tint = y == 4 || y == 9 ? Red : Purple;
                            Set(ox + x, y, oz + z, wall);
                        }
                    }
            }

            tint = 0;
            // A doorway on the side that faces the city centre, and a ladder up the inside.
            int dx = west ? fp - 1 : 0, dz = north ? fp - 1 : 0;
            Set(ox + dx, 1, oz + 3, 0);
            Set(ox + dx, 2, oz + 3, 0);
            Set(ox + dx, 3, oz + 3, 0);
            for (int y = 1; y < height; y++)
            {
                Set(ox + (west ? 1 : fp - 2), y, oz + (north ? 1 : fp - 2), ladder);
            }

            Set(ox + fp / 2, height + 1, oz + fp / 2, lamp);
            // The shaft is lit too (#1808): a light set into the roof over the middle and one into each of the
            // two outer walls at every red band, so the climb never goes dark.
            Set(ox + fp / 2, height, oz + fp / 2, lamp);
            int wx = ox + (west ? 0 : fp - 1), wz = oz + (north ? 0 : fp - 1);
            Set(wx, 4, oz + 3, lamp);
            Set(ox + 3, 4, wz, lamp);
            Set(wx, 9, oz + 3, lamp);
            Set(ox + 3, 9, wz, lamp);
            markers.Add(new SettlementMarker("guard_post", new Vector3i(ox + dx + (west ? 2 : -2), 1, oz + 3)));
            markers.Add(new SettlementMarker("guard_post", new Vector3i(ox + 3, 1, oz + dz + (north ? 2 : -2))));
            buildings++;
        }

        void StampPlaza(int mx, int mz)
        {
            // The landing plaza: steel paving under the pad ring, a lamp every 45° at radius 13 and four
            // guard posts that watch the ship come down.
            tint = 0;
            int cx = mx + ModuleSize / 2, cz = mz + ModuleSize / 2;
            for (int x = mx; x < mx + ModuleSize; x++)
                for (int z = mz; z < mz + ModuleSize; z++)
                {
                    int ddx = x - cx, ddz = z - cz;
                    int d2 = ddx * ddx + ddz * ddz;
                    if (d2 <= 13 * 13)
                    {
                        Set(x, 0, z, d2 >= 11 * 11 ? street : steelFloor);
                    }
                }

            for (int i = 0; i < 8; i++)
            {
                double a = i * System.Math.PI / 4.0;
                LampPost(cx + (int)System.Math.Round(System.Math.Cos(a) * 13), cz + (int)System.Math.Round(System.Math.Sin(a) * 13), 4);
            }

            markers.Add(new SettlementMarker("guard_post", new Vector3i(cx - 14, 1, cz - 14)));
            markers.Add(new SettlementMarker("guard_post", new Vector3i(cx + 14, 1, cz - 14)));
            markers.Add(new SettlementMarker("guard_post", new Vector3i(cx - 14, 1, cz + 14)));
            markers.Add(new SettlementMarker("guard_post", new Vector3i(cx + 14, 1, cz + 14)));
        }

        void StampOpen(int mx, int mz)
        {
            // Open ground another stamper owns (the crash site): paving and four lamps, nothing in the way.
            LampPost(mx + 1, mz + 1);
            LampPost(mx + ModuleSize - 2, mz + 1);
            LampPost(mx + 1, mz + ModuleSize - 2);
            LampPost(mx + ModuleSize - 2, mz + ModuleSize - 2);
        }

        void StampWall()
        {
            int mid = w / 2;
            for (int i = 1; i < w - 1; i++)
            {
                bool gateX = System.Math.Abs(i - mid) < GateHalfWidth;
                for (int y = 1; y <= WallHeight; y++)
                {
                    tint = y == 3 ? Red : Purple;
                    bool gateRow = gateX && y <= 4;
                    if (!gateRow)
                    {
                        Set(i, y, 1, wall);
                        Set(i, y, l - 2, wall);
                        Set(1, y, i, wall);
                        Set(w - 2, y, i, wall);
                    }
                }
            }

            tint = 0;
            foreach (var (x, z) in new[] { (mid - 6, 3), (mid + 6, 3), (mid - 6, l - 4), (mid + 6, l - 4), (3, mid - 6), (3, mid + 6), (w - 4, mid - 6), (w - 4, mid + 6) })
            {
                markers.Add(new SettlementMarker("guard_post", new Vector3i(x, 1, z)));
            }

            // Sentinels outside the gates too — the G.D.S. patrol the ground around the city, not only inside.
            foreach (var (x, z) in new[] { (mid - 6, 0), (mid + 6, 0), (mid - 6, l - 1), (mid + 6, l - 1), (0, mid - 6), (0, mid + 6), (w - 1, mid - 6), (w - 1, mid + 6) })
            {
                markers.Add(new SettlementMarker("guard_post", new Vector3i(x, 1, z)));
            }
        }
    }
}
