// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
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
    public static Role RoleAt(int gx, int gz) => RoleAtFor(gx, gz, Modules, null);

    /// <summary>The district role for any odd grid (#1876): the centre is the plaza, the corners towers, the ring
    /// around the plaza markets / hall / gardens / housing, every third outer district a garden — unless a kit's
    /// district map (<paramref name="roleMap"/>: one string per row, letters P O M H G T R) says otherwise.</summary>
    public static Role RoleAtFor(int gx, int gz, int grid, IReadOnlyList<string>? roleMap)
    {
        if (roleMap != null && gz < roleMap.Count && gx < roleMap[gz].Length)
        {
            switch (char.ToUpperInvariant(roleMap[gz][gx]))
            {
                case 'P': return Role.Plaza;
                case 'O': return Role.Open;
                case 'M': return Role.Market;
                case 'H': return Role.Hall;
                case 'G': return Role.Garden;
                case 'T': return Role.Tower;
                case 'R': return Role.Housing;
            }
        }

        int c = grid / 2;
        if (gx == c && gz == c)
        {
            return Role.Plaza;
        }

        bool corner = (gx == 0 || gx == grid - 1) && (gz == 0 || gz == grid - 1);
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
    public static (int X, int Z) ModuleOrigin(int gx, int gz) => ModuleOriginFor(gx, gz, ModuleSize, Street);

    /// <summary>The district origin for a kit layout's district size and street width (#1876).</summary>
    public static (int X, int Z) ModuleOriginFor(int gx, int gz, int size, int street)
        => (street + gx * (size + street), street + gz * (size + street));

    /// <summary>The kit function a district of <paramref name="role"/> takes ("" for the plaza / open ground).</summary>
    public static string CityRoleName(Role role) => role switch
    {
        Role.Market => StructureRoles.CityMarket,
        Role.Hall => StructureRoles.CityHall,
        Role.Garden => StructureRoles.CityGarden,
        Role.Tower => StructureRoles.CityTower,
        Role.Housing => StructureRoles.CityHousing,
        _ => string.Empty,
    };

    /// <summary>Composes the city. <paramref name="modules"/> (#1827) are the authored district modules the
    /// world allows (tier <see cref="StructureRoles.MetropolisTier"/>, role <c>city_*</c>); with
    /// <paramref name="moduleChance"/> per district, a hash of seed + grid cell decides whether a district is
    /// stamped from a module of its role instead of the procedural one. Plaza and open districts never are.</summary>
    public static SettlementStructure Generate(long seed, GameContent content, IReadOnlyList<OpenZone> openZones,
        IReadOnlyList<StructureTemplate>? modules = null, double moduleChance = 0.0,
        IList<string>? composition = null, System.Action<string>? warn = null,
        CityLayoutSpec? layout = null, StructureKit? kit = null, IReadOnlyList<StructureTemplate>? kitModules = null)
    {
        // #1876: a kit city takes its grid from the layout spec (districts per side, district size, street width, height, map).
        var spec = layout ?? CityLayoutSpec.Default;
        int grid = spec.Grid, size = spec.DistrictSize, streetW = spec.Street;
        int w = spec.Footprint, l = spec.Footprint, h = spec.Height;

        // #1872: the module per district is pinned (index = gx * grid + gz): a non-empty list replays, an empty
        // one records, null neither — see SettlementGenerator.Generate.
        bool replay = composition is { Count: > 0 };
        bool record = composition is { Count: 0 };
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
        var shapes = new Dictionary<int, int>();
        var markers = new List<SettlementMarker>();
        int buildings = 0;
        int tint = 0; // the tint the Set closure applies to wall/accent cells while a building is stamped

        // Interiors (#1828): town-style furniture; the houses light their rooms from the deck (#1808), so no
        // floor lamps.
        var furniture = RoomFurnisher.PaletteFor(RoomFurnisher.Style.Town, content);
        furniture.CeilingLit = true;
        var materials = ModuleMaterials.ForCity(content); // #1885: the district modules' material tokens

        ushort Get(int x, int y, int z) => x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= l ? (ushort)0 : blocks[(x * h + y) * l + z];

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
            shapes.Remove(idx); // a plain stamp is a cube
            if (tint != 0 && b != 0 && (b == wall || b == stone || b == paving))
            {
                mods[idx] = (tint, 0);
            }
            else
            {
                mods.Remove(idx); // air, an untinted stamp or a light over a tinted cell: no tint survives
            }
        }

        // The cell sink for modules + furniture: an explicit shape / tint / glow per cell, the district tint
        // never applied (a module brings its own colours), the open zones honoured like everything else.
        void SetCell(int x, int y, int z, ushort b, int shape, int cellTint, int glow)
        {
            if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= l || (y > 0 && Open(x, z)))
            {
                return;
            }

            int idx = (x * h + y) * l + z;
            blocks[idx] = b;
            if (b != 0 && (cellTint != 0 || glow != 0)) mods[idx] = (cellTint, glow); else mods.Remove(idx);
            if (b != 0 && shape != 0) shapes[idx] = shape; else shapes.Remove(idx);
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

        // #1876: a kit fills the districts from its entries (required first, then weighted draws) — decided once,
        // replayed from the record afterwards. The plaza and open ground never take a module.
        string[]? assigned = kit != null && !replay
            ? SettlementGenerator.AssignKitModules(kit, kitModules, grid * grid,
                d => CityRoleName(RoleAtFor(d / grid, d % grid, grid, spec.RoleMap)),
                m => m.Tier == StructureRoles.MetropolisTier && !m.IsAlienStyle && m.Width <= size && m.Height <= h - 1 && m.Length <= size,
                seed)
            : null;

        // 1) Streets: the grid lanes, dark paving.
        for (int i = 0; i <= grid; i++)
        {
            int s0 = i * (size + streetW);
            Fill(s0, 0, 0, s0 + streetW - 1, 0, l - 1, street);
            Fill(0, 0, s0, w - 1, 0, s0 + streetW - 1, street);
        }

        // 2) Modules.
        for (int gx = 0; gx < grid; gx++)
            for (int gz = 0; gz < grid; gz++)
            {
                var (mx, mz) = ModuleOriginFor(gx, gz, size, streetW);
                var role = RoleAtFor(gx, gz, grid, spec.RoleMap);
                bool blocked = false;
                foreach (var zone in openZones)
                {
                    if (role != Role.Plaza && zone.Intersects(mx, mz, mx + size - 1, mz + size - 1))
                    {
                        blocked = true;
                    }
                }

                if (blocked)
                {
                    role = Role.Open;
                }

                tint = 0;
                Fill(mx, 0, mz, mx + size - 1, 0, mz + size - 1, paving); // module floor

                // #1827: an authored district of this role? Stamped centred on the paved floor; its own
                // markers (residents, vendors, doors, rooms) replace the procedural ones.
                StructureTemplate? authored = null;
                if (role != Role.Plaza && role != Role.Open)
                {
                    string moduleRole = role switch
                    {
                        Role.Market => StructureRoles.CityMarket,
                        Role.Hall => StructureRoles.CityHall,
                        Role.Garden => StructureRoles.CityGarden,
                        Role.Tower => StructureRoles.CityTower,
                        _ => StructureRoles.CityHousing,
                    };
                    int district = gx * grid + gz;
                    bool StyleOk(StructureTemplate m) => m.Tier == StructureRoles.MetropolisTier;
                    var pool = kitModules ?? modules;
                    authored = replay
                        ? SettlementGenerator.ModuleByKey(pool, district < composition!.Count ? composition[district] : string.Empty, moduleRole, StyleOk, size, h - 1, size, warn)
                        : assigned != null
                            ? SettlementGenerator.ModuleByKey(pool, assigned[district], moduleRole, StyleOk, size, h - 1, size, warn)
                            : SettlementGenerator.PickModule(modules, moduleChance, $"citymodule:{seed}:{gx}:{gz}", moduleRole, StyleOk, size, h - 1, size);
                }

                if (record)
                {
                    composition!.Add(authored?.Key ?? string.Empty); // #1872: plaza / open districts pin "" too
                }

                if (authored != null)
                {
                    var moduleMarkers = new List<SettlementMarker>();
                    SettlementGenerator.StampModule(authored, mx + (size - authored.Width) / 2, 0, mz + (size - authored.Length) / 2,
                        content, Get, SetCell, moduleMarkers, furniture, WorldGenerator.StableHash($"furnish:city:{seed}:{gx}:{gz}"), materials);
                    foreach (var m in moduleMarkers)
                    {
                        markers.Add(m);
                        if (m.Type == "npc" || m.Type == "vendor") buildings++;
                    }

                    continue;
                }

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
        return new SettlementStructure(w, h, l, Tier, ruined: false, inhabitant: "human", blocks, markers, System.Math.Max(1, buildings), mods, shapes);

        // ------------------------------------------------------------------ modules

        void House(int ox, int oz, int fp, int storeys, int doorSide, bool red, RoomFurnisher.RoomRole groundRole = RoomFurnisher.RoomRole.House)
        {
            tint = red ? Red : Purple;
            SettlementGenerator.StampBuilding(Set, ox, oz, fp, storeys, wall, wall, glass, ladder, doorSide, 0, rng, ruined: false, ceilingLight: lamp,
                furnish: furniture, setCell: SetCell, furnishSeed: WorldGenerator.StableHash($"furnish:city:{seed}:{ox}:{oz}"), groundRole: groundRole);
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
            int inset = (size - plots * SettlementGenerator.Plot - 1) / 2;
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
            House(ox0, oz0, fp, 2, 1, false, RoomFurnisher.RoomRole.Market);
            markers.Add(new SettlementMarker("vendor", new Vector3i(ox0 + fp / 2, 1, oz0 + fp / 2)));
            DoorMarker(ox0, oz0, fp, 1);

            var (ox1, oz1) = (mx + size - 3 - fp, mz + 3);
            House(ox1, oz1, fp, 2, 1, true, RoomFurnisher.RoomRole.Market);
            markers.Add(new SettlementMarker("vendor", new Vector3i(ox1 + fp / 2, 1, oz1 + fp / 2)));
            DoorMarker(ox1, oz1, fp, 1);

            var (ox2, oz2) = (mx + size / 2 - fp / 2, mz + size - 3 - fp);
            House(ox2, oz2, fp, 2, 0, false, RoomFurnisher.RoomRole.Board);
            markers.Add(new SettlementMarker("mission_board", new Vector3i(ox2 + fp / 2, 1, oz2 + fp / 2)));
            DoorMarker(ox2, oz2, fp, 0);

            // Market stalls: a glass canopy on four posts, an NPC minding each.
            tint = 0;
            for (int i = 0; i < 3; i++)
            {
                int sx = mx + 4 + i * 8, sz = mz + size / 2 - 1;
                Fill(sx, 1, sz, sx, 2, sz, stone);
                Fill(sx + 3, 1, sz, sx + 3, 2, sz, stone);
                Fill(sx, 1, sz + 3, sx, 2, sz + 3, stone);
                Fill(sx + 3, 1, sz + 3, sx + 3, 2, sz + 3, stone);
                Fill(sx, 3, sz, sx + 3, 3, sz + 3, glass);
                markers.Add(new SettlementMarker("npc", new Vector3i(sx + 1, 1, sz + 1)));
            }

            LampPost(mx + 1, mz + 1);
            LampPost(mx + size - 2, mz + 1);
            LampPost(mx + 1, mz + size - 2);
            LampPost(mx + size - 2, mz + size - 2);
        }

        void StampHall(int mx, int mz)
        {
            // The G.D.S. hall: one 14-wide, two-storey building of purple stone with a red crown, four
            // residents inside, a guard at each corner of the forecourt.
            int fp = 14;
            int ox = mx + (size - fp) / 2, oz = mz + (size - fp) / 2;
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
            LampPost(mx + size - 2, mz + size - 2);
        }

        void StampGarden(int mx, int mz)
        {
            // A green square: grass, an 8×8 pool in the middle with a stone rim, six trees, ferns and bushes,
            // a lamp at each corner and two strollers.
            tint = 0;
            Fill(mx + 1, 0, mz + 1, mx + size - 2, 0, mz + size - 2, grass);
            int cx = mx + size / 2, cz = mz + size / 2;
            Fill(cx - 5, 0, cz - 5, cx + 4, 0, cz + 4, stone);
            if (water != 0)
            {
                Fill(cx - 4, 0, cz - 4, cx + 3, 0, cz + 3, water);
            }

            var spots = new (int X, int Z)[] { (4, 4), (size - 6, 4), (4, size - 6), (size - 6, size - 6), (size / 2 - 1, 3), (size / 2 - 1, size - 5) };
            foreach (var (sx, sz) in spots)
            {
                Tree(mx + sx, mz + sz, 4 + rng.Next(0, 2));
            }

            for (int i = 0; i < 24; i++)
            {
                int fx = mx + 1 + rng.Next(0, size - 2), fz = mz + 1 + rng.Next(0, size - 2);
                if (System.Math.Abs(fx - cx) > 6 || System.Math.Abs(fz - cz) > 6)
                {
                    Set(fx, 1, fz, rng.NextDouble() < 0.7 ? fern : bush);
                }
            }

            LampPost(mx + 1, mz + 1);
            LampPost(mx + size - 2, mz + 1);
            LampPost(mx + 1, mz + size - 2);
            LampPost(mx + size - 2, mz + size - 2);
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
            int ox = west ? mx + 2 : mx + size - 2 - fp;
            int oz = north ? mz + 2 : mz + size - 2 - fp;
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
            int cx = mx + size / 2, cz = mz + size / 2;
            for (int x = mx; x < mx + size; x++)
                for (int z = mz; z < mz + size; z++)
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
            LampPost(mx + size - 2, mz + 1);
            LampPost(mx + 1, mz + size - 2);
            LampPost(mx + size - 2, mz + size - 2);
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
