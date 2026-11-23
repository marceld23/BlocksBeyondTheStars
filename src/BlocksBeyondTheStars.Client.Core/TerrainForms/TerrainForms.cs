// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Terrain forms (#3964, LOOK.md §17): natural terrain cells drawn as forms out of micro cubes (8 per block edge) —
    /// broken edges, small steps, strata grooves, niches, ledges, ceiling breakouts, and on flat ground surface patterns
    /// (flagstone joints, cracks, wind ripples, dips). Display only: the cell stays a cube for collision and every rule.
    /// This is the pure part the client mesher (<c>ChunkMesher.TerrainForms.cs</c>) runs: which cell gets which form (a
    /// deterministic hash of the world cell and the world seed, so every client draws the same), the recipes that carve a
    /// full cube into a form, and the merged faces of a carved cell given its neighbours. Integer geometry; the only floats
    /// are plain products and comparisons, identical on every platform.
    /// </summary>
    public static class TerrainForms
    {
        /// <summary>Micro cells per block edge.</summary>
        public const int G = 8;

        /// <summary>Micro cells per block.</summary>
        public const int Cells = G * G * G;

        /// <summary>The material families: none (never formed), rock, soft ground, grass.</summary>
        public const byte FamilyNone = 0, FamilyRock = 1, FamilySoft = 2, FamilyGrass = 3;

        /// <summary>The material family of a block key (0 = never formed).</summary>
        public static byte FamilyOf(string key) => key switch
        {
            "stone" or "basalt" or "granite" or "sandstone" or "moss_stone" or "deepslate" or "dark_sandstone"
                or "blackened_sandstone" or "coral_rock" or "sulfur_stone" or "tainted_stone" or "ice" => FamilyRock,
            "dirt" or "mud" or "peat" or "ash" or "salt" or "sand" or "black_sand" or "snow" => FamilySoft,
            "grass" or "alien_grass" or "mycelium" => FamilyGrass,
            _ => FamilyNone,
        };

        /// <summary>Index of a micro cell in a form (x fastest, then z, then y — the custom-shape order).</summary>
        public static int Index(int x, int y, int z) => (((y * G) + z) * G) + x;

        // ------------------------------------------------------------------ selection

        private const int SpotNone = -1, SpotEdge = 0, SpotWall = 1, SpotFlat = 2, SpotCeiling = 3;

        // Base chance per family (rows: none, rock, soft, grass) and spot (edge, wall, flat — unused: flat ground takes
        // the region patterns, ceiling).
        private static readonly float[,] BaseChance =
        {
            { 0f, 0f, 0f, 0f },
            { 0.65f, 0.55f, 0f, 0.45f },
            { 0.50f, 0.35f, 0f, 0f },
            { 0.55f, 0.30f, 0f, 0f },
        };

        /// <summary>
        /// Decides whether the cell gets a form and carves it into <paramref name="vox"/> (true = filled, at least
        /// <see cref="Cells"/> long). <paramref name="open"/> bit f = the neighbour across face f (mesher order +Y, −Y, +X,
        /// −X, +Z, −Z) is air. <paramref name="density"/> scales every chance (1 = the shipped look). False = stays a cube.
        /// <paramref name="topCarved"/> = the form broke the cell's top edge so far that ground details (grass carpet, tufts)
        /// would hang over a gap; the mesher leaves them off.
        /// </summary>
        public static bool TrySelect(byte family, int open, int wx, int wy, int wz, long salt, float density, bool[] vox,
            out bool topCarved)
        {
            topCarved = false;
            if (family == FamilyNone || density <= 0f)
            {
                return false;
            }

            int spot = SpotOf(open);
            if (spot == SpotNone)
            {
                return false;
            }

            ulong h = Hash(wx, wy, wz, salt);
            if (spot == SpotFlat)
            {
                // Flat ground: a surface pattern laid over whole regions (not cell by cell), so it reads as a surface.
                if (Zone(wx, wz, salt) * WorldStrength(salt) * density < 0.7f)
                {
                    return false;
                }

                var fr = new Rng(h >> 16);
                Fill(vox, true);
                return CarveSurface(family, wx, wz, salt, ref fr, vox);
            }

            float chance = BaseChance[family, spot] * Zone(wx, wz, salt) * WorldStrength(salt) * density;
            if (chance <= 0f || (h & 0xFFFF) / 65535f >= System.Math.Min(0.9f, chance))
            {
                return false;
            }

            var r = new Rng(h >> 16);
            Fill(vox, true);
            bool edgeTop = false;
            bool carved = spot switch
            {
                SpotEdge => CarveEdge(family, open, ref r, vox, out edgeTop),
                SpotWall => CarveWall(family, open, wy, wx, wz, salt, ref r, vox),
                _ => CarveCeiling(family, ref r, vox),
            };
            topCarved = carved && edgeTop;
            return carved;
        }

        private static int SpotOf(int open)
        {
            bool top = (open & 1) != 0, bottom = (open & 2) != 0;
            int sides = Bits(open >> 2);
            if (top)
            {
                return bottom ? SpotNone : sides == 0 ? SpotFlat : sides <= 2 ? SpotEdge : SpotNone;
            }

            if (sides >= 1)
            {
                return SpotWall;
            }

            return bottom ? SpotCeiling : SpotNone;
        }

        private static int Bits(int v)
        {
            int c = 0;
            for (; v != 0; v >>= 1)
            {
                c += v & 1;
            }

            return c;
        }

        /// <summary>Rough and calm regions: 0 on about a fifth of the ground, up to 2 in the heart of a rough region.</summary>
        private static float Zone(int wx, int wz, long salt)
        {
            const int Period = 24;
            int x0 = FloorDiv(wx, Period), z0 = FloorDiv(wz, Period);
            float tx = (wx - (x0 * Period)) / (float)Period, tz = (wz - (z0 * Period)) / (float)Period;
            tx = tx * tx * (3f - (2f * tx));
            tz = tz * tz * (3f - (2f * tz));
            float a = Lerp(Lattice(x0, z0, salt), Lattice(x0 + 1, z0, salt), tx);
            float b = Lerp(Lattice(x0, z0 + 1, salt), Lattice(x0 + 1, z0 + 1, salt), tx);
            float v = Lerp(a, b, tz);
            return System.Math.Max(0f, (v - 0.2f) / 0.8f) * 2f;
        }

        private static float WorldStrength(long salt) => 0.6f + (0.8f * ((Mix((ulong)salt ^ 0x51A7E5u) & 0xFFFF) / 65535f));

        private static float Lattice(int x, int z, long salt) => (Mix((ulong)salt ^ ((ulong)(uint)x * 0x9E3779B1u) ^ ((ulong)(uint)z << 32) ^ 0x2Bu) & 0xFFFF) / 65535f;

        private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;

        private static ulong Hash(int x, int y, int z, long salt)
            => Mix((ulong)salt ^ ((ulong)(uint)x * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)y * 0xC2B2AE3D27D4EB4FUL) ^ ((ulong)(uint)z * 0x165667B19E3779F9UL));

        private static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private struct Rng
        {
            private ulong _s;

            public Rng(ulong seed) => _s = seed | 1;

            /// <summary>An integer in [lo, hi] inclusive.</summary>
            public int Next(int lo, int hi)
            {
                _s = Mix(_s + 0x9E3779B97F4A7C15UL);
                return lo + (int)(_s % (ulong)(hi - lo + 1));
            }
        }

        // ------------------------------------------------------------------ recipes (local frame: the open side is +X)

        // For an open side (2..5) the second side that turns the local +Z (for the corner recipe).
        private static int SecondSide(int side) => side switch
        {
            2 => 4,
            3 => 5,
            4 => 3,
            _ => 2,
        };

        private static int PickSide(int open, ref Rng r)
        {
            int count = Bits((open >> 2) & 0xF);
            int pick = r.Next(0, count - 1);
            for (int f = 2; f < 6; f++)
            {
                if ((open & (1 << f)) != 0 && pick-- == 0)
                {
                    return f;
                }
            }

            return 2;
        }

        private static bool CarveEdge(byte family, int open, ref Rng r, bool[] vox, out bool topCarved)
        {
            topCarved = false;
            int side = PickSide(open, ref r);
            if (family == FamilyGrass && r.Next(0, 99) < 50)
            {
                // Grass edge broken off: the carpet stays off this cell (it would hang over the gap).
                topCarved = true;
                int gl = r.Next(4, 8), gz = r.Next(0, 8 - gl);
                Carve(vox, side, r.Next(5, 6), r.Next(5, 6), gz, 8, 8, gz + gl);
                return true;
            }

            if (family == FamilyGrass)
            {
                // K5 grass sod: the earth under the grass lip falls back; the top two layers stay as an overhang.
                Carve(vox, side, 7, 1, 0, 8, 6, 8);
                int len = r.Next(3, 5), z0 = r.Next(0, 8 - len);
                Carve(vox, side, 6, 1, z0, 8, 5, z0 + len);
                return true;
            }

            bool corner = (open & (1 << SecondSide(side))) != 0;
            int roll = r.Next(0, 99);
            if (corner && roll < 30)
            {
                // K4 corner gone
                Carve(vox, side, 6, r.Next(3, 6), 6, 8, 8, 8);
                return true;
            }

            if (family == FamilyRock && roll < 55)
            {
                Carve(vox, side, 6, 5, 0, 8, 8, 8); // K2 step
                return true;
            }

            if (family == FamilyRock && roll < 65)
            {
                Carve(vox, side, 6, 4, 0, 8, 8, 8); // K3 double step
                Carve(vox, side, 4, 6, 0, 6, 8, 8);
                return true;
            }

            // K1 broken edge
            int d = r.Next(2, 3), hgt = r.Next(2, 4), l = r.Next(4, 8), zs = r.Next(0, 8 - l);
            Carve(vox, side, 8 - d, 8 - hgt, zs, 8, 8, zs + l);
            return true;
        }

        private static bool CarveWall(byte family, int open, int wy, int wx, int wz, long salt, ref Rng r, bool[] vox)
        {
            int side = PickSide(open, ref r);
            int roll = r.Next(0, 99);
            if (family == FamilyRock && roll < 35)
            {
                // W1 strata groove: its height follows the world height and a coarse field, so neighbours line up.
                int band = (int)(Mix((ulong)salt ^ ((ulong)(uint)FloorDiv(wx, 16) * 0x9E3779B1u) ^ ((ulong)(uint)FloorDiv(wz, 16) << 32)) % 7);
                int y0 = (int)((uint)((wy * 3) + band) % 7u);
                Carve(vox, side, 6, y0, 0, 8, y0 + 1, 8);
                return true;
            }

            if (family == FamilyRock && roll < 55)
            {
                int h = r.Next(2, 3), y0 = r.Next(1, 7 - h), l = r.Next(2, 4), z0 = r.Next(0, 8 - l);
                Carve(vox, side, 6, y0, z0, 8, y0 + h, z0 + l); // W2 niche
                return true;
            }

            if (family != FamilyGrass && roll < 75)
            {
                int b = r.Next(2, 5); // W3 ledge: the face steps back except a band that stands out
                Carve(vox, side, 6, 0, 0, 8, b, 8);
                Carve(vox, side, 6, b + 2, 0, 8, 8, 8);
                return true;
            }

            int pits = r.Next(3, 5); // W4 chips
            for (int i = 0; i < pits; i++)
            {
                int sx = r.Next(1, 3), sz = r.Next(2, 3), y = r.Next(0, 6), z0 = r.Next(0, 8 - sz);
                Carve(vox, side, 8 - sx, y, z0, 8, y + 2, z0 + sz);
            }

            return true;
        }

        // ------------------------------------------------------------------ surface patterns (flat ground, top layer only)

        /// <summary>A surface pattern for a flat cell. The pattern is chosen per region (32 blocks) and family; flagstones
        /// and ripples run in world micro coordinates, so they continue across block borders. Only the top micro layer is
        /// touched (1/8 deep), so the ground stays walkable and the grass carpet and tufts keep their place.</summary>
        private static bool CarveSurface(byte family, int wx, int wz, long salt, ref Rng r, bool[] vox)
        {
            ulong region = Mix((ulong)salt ^ 0x5F1A7u ^ ((ulong)(uint)FloorDiv(wx, 32) * 0x9E3779B1u)
                ^ ((ulong)(uint)FloorDiv(wz, 32) << 32) ^ ((ulong)family << 56));
            int pattern = (int)(region % 2);
            if (family == FamilyGrass)
            {
                return Pits(ref r, vox, 1, 3, 25);
            }

            if (family == FamilyRock)
            {
                return pattern == 0 ? Flagstones(wx, wz, salt, region, vox) : Crack(ref r, vox);
            }

            return pattern == 0 ? Ripples(wx, wz, salt, region, vox) : Pits(ref r, vox, 1, 2, 30);
        }

        /// <summary>Flagstones: a running-bond net of 1-wide joints (rows of 10 micro cells, stones 12–15 long).</summary>
        private static bool Flagstones(int wx, int wz, long salt, ulong region, bool[] vox)
        {
            bool swap = ((region >> 8) & 1) != 0;
            int rowH = 16 + (int)((region >> 9) % 5), len = 20 + (int)((region >> 12) % 9);
            for (int z = 0; z < G; z++)
            {
                for (int x = 0; x < G; x++)
                {
                    long mx = ((long)wx * G) + x, mz = ((long)wz * G) + z;
                    long a = swap ? mz : mx, b = swap ? mx : mz;
                    long row = FloorDivL(b, rowH);
                    int offset = (int)(Mix((ulong)salt ^ (ulong)row ^ 0xF1A6u) % (ulong)len);
                    if (FloorModL(b, rowH) == 0 || FloorModL(a + offset, len) == 0)
                    {
                        vox[Index(x, G - 1, z)] = false;
                    }
                }
            }

            return true;
        }

        /// <summary>Wind ripples: parallel 1-wide grooves every 5–6 micro cells, along X, Z or a diagonal per region, with a
        /// small wobble per block so they are not ruled lines.</summary>
        private static bool Ripples(int wx, int wz, long salt, ulong region, bool[] vox)
        {
            int dir = (int)((region >> 8) % 2), period = 9 + (int)((region >> 10) % 4); // straight only: diagonals stair-step into many faces
            for (int z = 0; z < G; z++)
            {
                for (int x = 0; x < G; x++)
                {
                    long mx = ((long)wx * G) + x, mz = ((long)wz * G) + z;
                    long across = dir switch { 0 => mz, 1 => mx, 2 => mx + mz, _ => mx - mz };
                    long along = dir switch { 0 => mx, 1 => mz, 2 => mx - mz, _ => mx + mz };
                    int wobble = (int)(Mix((ulong)salt ^ (ulong)FloorDivL(along, 12) ^ 0x21Bu) % 2);
                    if (FloorModL(across + wobble, period) == 0)
                    {
                        vox[Index(x, G - 1, z)] = false;
                    }
                }
            }

            return true;
        }

        /// <summary>Small dips in the top layer: <paramref name="count"/> of them, side 2..<paramref name="maxSide"/>, on
        /// <paramref name="percent"/> % of the cells.</summary>
        private static bool Pits(ref Rng r, bool[] vox, int count, int maxSide, int percent)
        {
            if (r.Next(0, 99) >= percent)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                int a = r.Next(2, maxSide), c = r.Next(2, maxSide), x0 = r.Next(0, G - a), z0 = r.Next(0, G - c);
                Carve(vox, 2, x0, G - 1, z0, x0 + a, G, z0 + c);
            }

            return true;
        }

        /// <summary>A crack across the cell: a 1-wide line from one side to the other with one jog, on 60 % of the cells.</summary>
        private static bool Crack(ref Rng r, bool[] vox)
        {
            if (r.Next(0, 99) >= 25)
            {
                return false;
            }

            int side = r.Next(2, 5), z1 = r.Next(1, 6), z2 = r.Next(1, 6), bend = r.Next(2, 5);
            Carve(vox, side, 0, G - 1, z1, bend + 1, G, z1 + 1);
            Carve(vox, side, bend, G - 1, System.Math.Min(z1, z2), bend + 1, G, System.Math.Max(z1, z2) + 1);
            Carve(vox, side, bend, G - 1, z2, G, G, z2 + 1);
            return true;
        }

        private static long FloorDivL(long a, long b) => a >= 0 ? a / b : ((a + 1) / b) - 1;

        private static long FloorModL(long a, long b) => ((a % b) + b) % b;

        private static bool CarveCeiling(byte family, ref Rng r, bool[] vox)
        {
            if (family != FamilyRock)
            {
                return false;
            }

            int a = r.Next(3, 5), c = r.Next(3, 5), d = r.Next(1, 3), xs = r.Next(1, 7 - a), zs = r.Next(1, 7 - c);
            Carve(vox, 2, xs, 0, zs, xs + a, d, zs + c); // D1 breakout
            return true;
        }

        private static void Fill(bool[] vox, bool v)
        {
            for (int i = 0; i < Cells; i++)
            {
                vox[i] = v;
            }
        }

        /// <summary>Empties the local box [x0,x1)×[y0,y1)×[z0,z1) (local +X = the open <paramref name="side"/>).</summary>
        private static void Carve(bool[] vox, int side, int x0, int y0, int z0, int x1, int y1, int z1)
        {
            for (int y = y0; y < y1; y++)
            {
                for (int z = z0; z < z1; z++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        var (cx, cz) = side switch
                        {
                            3 => (7 - x, 7 - z),
                            4 => (7 - z, x),
                            5 => (z, 7 - x),
                            _ => (x, z),
                        };
                        vox[Index(cx, y, cz)] = false;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ faces

        /// <summary>What lies across one cube face of a formed cell.</summary>
        public const byte SideOpen = 0, SideSolid = 1, SideFormed = 2;

        /// <summary>One merged face: the plane rectangle in micro cells (the axis of <see cref="Dir"/> carries the plane
        /// coordinate in both corners); <see cref="Outer"/> = on the cell's surface toward open air.</summary>
        public readonly struct Rect3
        {
            public Rect3(int dir, bool outer, int x0, int y0, int z0, int x1, int y1, int z1)
            {
                Dir = dir;
                Outer = outer;
                X0 = x0;
                Y0 = y0;
                Z0 = z0;
                X1 = x1;
                Y1 = y1;
                Z1 = z1;
            }

            public readonly int Dir;
            public readonly bool Outer;
            public readonly int X0, Y0, Z0, X1, Y1, Z1;
        }

        private static readonly int[] Dx = { 0, 0, 1, -1, 0, 0 };
        private static readonly int[] Dy = { 1, -1, 0, 0, 0, 0 };
        private static readonly int[] Dz = { 0, 0, 0, 0, 1, -1 };

        [System.ThreadStatic] private static bool[]? _mask;

        /// <summary>
        /// The faces this cell draws. Between a filled micro cell <c>a</c> and an empty one <c>b</c> (one step along a face
        /// direction) a face points from a to b. The cell draws it when a is inside (toward its own space, toward open air or
        /// toward a formed neighbour's empty space), or when a lies in a solid unformed neighbour and b inside — the back wall
        /// that neighbour does not draw, because it takes this cell for a full cube. False when the faces exceed
        /// <paramref name="budget"/> (the cell then stays a cube).
        /// </summary>
        public static bool BuildFaces(bool[] vox, byte[] sideKind, bool[][] sideVox, List<Rect3> output, int budget)
        {
            output.Clear();
            var mask = _mask ??= new bool[G * G];
            for (int dir = 0; dir < 6; dir++)
            {
                for (int plane = 0; plane <= G; plane++)
                {
                    bool any = false;
                    for (int v = 0; v < G; v++)
                    {
                        for (int u = 0; u < G; u++)
                        {
                            bool m = Draws(vox, sideKind, sideVox, dir, plane, u, v, out _);
                            mask[(v * G) + u] = m;
                            any |= m;
                        }
                    }

                    if (any && !Merge(vox, sideKind, sideVox, mask, dir, plane, output, budget))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // The micro cell "before" the plane (a) and "after" it (b) along dir; u, v run over the other two axes.
        private static void Cells2(int dir, int plane, int u, int v, out int ax, out int ay, out int az, out int bx, out int by, out int bz)
        {
            int axis = dir <= 1 ? 1 : dir <= 3 ? 0 : 2;
            bool positive = (dir & 1) == 0;
            int before = positive ? plane - 1 : plane, after = positive ? plane : plane - 1;
            switch (axis)
            {
                case 0:
                    ax = before; bx = after; ay = by = v; az = bz = u;
                    break;
                case 1:
                    ay = before; by = after; ax = bx = u; az = bz = v;
                    break;
                default:
                    az = before; bz = after; ax = bx = u; ay = by = v;
                    break;
            }
        }

        private static bool Draws(bool[] vox, byte[] sideKind, bool[][] sideVox, int dir, int plane, int u, int v, out bool outer)
        {
            Cells2(dir, plane, u, v, out int ax, out int ay, out int az, out int bx, out int by, out int bz);
            outer = false;
            bool aIn = Inside(ax, ay, az), bIn = Inside(bx, by, bz);
            if (aIn)
            {
                if (!vox[Index(ax, ay, az)])
                {
                    return false;
                }

                if (bIn)
                {
                    return !vox[Index(bx, by, bz)];
                }

                byte k = sideKind[dir];
                if (k == SideOpen)
                {
                    outer = true;
                    return true;
                }

                return k == SideFormed && !sideVox[dir][Index(Wrap(bx), Wrap(by), Wrap(bz))];
            }

            if (!bIn || vox[Index(bx, by, bz)])
            {
                return false;
            }

            int from = dir ^ 1; // a lies across the face opposite to dir
            return sideKind[from] == SideSolid;
        }

        private static bool Inside(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < G && y < G && z < G;

        private static int Wrap(int c) => c < 0 ? c + G : c >= G ? c - G : c;

        private static bool Merge(bool[] vox, byte[] sideKind, bool[][] sideVox, bool[] mask, int dir, int plane, List<Rect3> output, int budget)
        {
            for (int v = 0; v < G; v++)
            {
                for (int u = 0; u < G; u++)
                {
                    if (!mask[(v * G) + u])
                    {
                        continue;
                    }

                    Draws(vox, sideKind, sideVox, dir, plane, u, v, out bool outer);
                    int u1 = u + 1;
                    while (u1 < G && mask[(v * G) + u1] && OuterAt(vox, sideKind, sideVox, dir, plane, u1, v) == outer)
                    {
                        u1++;
                    }

                    int v1 = v + 1;
                    while (v1 < G && RowSet(vox, sideKind, sideVox, mask, dir, plane, u, u1, v1, outer))
                    {
                        v1++;
                    }

                    for (int vv = v; vv < v1; vv++)
                    {
                        for (int uu = u; uu < u1; uu++)
                        {
                            mask[(vv * G) + uu] = false;
                        }
                    }

                    if (output.Count >= budget)
                    {
                        return false;
                    }

                    output.Add(ToRect(dir, plane, u, v, u1, v1, outer));
                }
            }

            return true;
        }

        private static bool OuterAt(bool[] vox, byte[] sideKind, bool[][] sideVox, int dir, int plane, int u, int v)
        {
            Draws(vox, sideKind, sideVox, dir, plane, u, v, out bool outer);
            return outer;
        }

        private static bool RowSet(bool[] vox, byte[] sideKind, bool[][] sideVox, bool[] mask, int dir, int plane, int u0, int u1, int v, bool outer)
        {
            for (int u = u0; u < u1; u++)
            {
                if (!mask[(v * G) + u] || OuterAt(vox, sideKind, sideVox, dir, plane, u, v) != outer)
                {
                    return false;
                }
            }

            return true;
        }

        private static Rect3 ToRect(int dir, int plane, int u0, int v0, int u1, int v1, bool outer)
        {
            int axis = dir <= 1 ? 1 : dir <= 3 ? 0 : 2;
            return axis switch
            {
                0 => new Rect3(dir, outer, plane, v0, u0, plane, v1, u1),
                1 => new Rect3(dir, outer, u0, plane, v0, u1, plane, v1),
                _ => new Rect3(dir, outer, u0, v0, plane, u1, v1, plane),
            };
        }
    }
}
