// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Builds a <b>monument</b> deterministically from a seed — a single eroded relic of a vanished
/// civilisation, far smaller than a settlement: a half-collapsed arcade of arches, a free-standing gate,
/// a ring of standing stones, an obelisk or a rune altar. Unlike the settlement generator (which decays a
/// whole town statistically) these are authored silhouettes: the shape is built intact, then an erosion
/// pass takes pieces away while one element is deliberately spared so the outline still reads from afar.
///
/// Every monument carries <b>runes</b> — cells swapped to the rune material and given an emissive glow
/// colour drawn per instance, so they are readable at night and give the scanner something to identify
/// (see <c>GameServerScanning</c>). At least one rune always survives the erosion pass.
///
/// Reuses the <see cref="SettlementStructure"/> container so the placement pipeline applies unchanged, and
/// is the first procedural generator to populate its per-cell <b>shape</b> and <b>glow</b> modifiers —
/// arches, columns and lintels come from <see cref="BlockShape"/> forms, not from new geometry.
/// </summary>
public static class MonumentGenerator
{
    /// <summary>The monument silhouettes. One instance of a body's monuments is generated per archetype,
    /// so a body never shows the same kind twice.</summary>
    public static readonly string[] Archetypes = { "arcade", "gate", "circle", "obelisk", "altar" };

    /// <summary>Rune glow colours (0xRRGGBB) — one is drawn per monument, so a whole relic glows in one hue.</summary>
    private static readonly int[] RuneGlows = { 0x3FD8E8, 0xA870F0, 0xF0A03C, 0x5FE08A };

    /// <summary>Builds the monument. <paramref name="withCache"/> adds a <c>relic_cache</c> loot marker
    /// (the stamper turns it into a lootable container).</summary>
    public static SettlementStructure Generate(string archetype, long seed, string biomeSurfaceBlock,
        GameContent content, bool withCache)
    {
        var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32)) ^ (int)WorldGenerator.StableHash(archetype)));

        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        ushort stone = B("stone");
        var mat = new Materials(
            masonry: B("ancient_brick", stone),
            rune: B("rune_stone", B("ancient_brick", stone)),
            rubble: B(biomeSurfaceBlock, stone),
            glow: RuneGlows[rng.Next(RuneGlows.Length)]);

        var c = archetype switch
        {
            "gate" => Gate(mat, rng),
            "circle" => Circle(mat, rng),
            "obelisk" => Obelisk(mat, rng),
            "altar" => Altar(mat, rng),
            _ => Arcade(mat, rng),
        };

        ScatterRunes(c, mat, rng);

        if (withCache)
        {
            AddCacheMarker(c, rng);
        }

        return c.ToStructure(archetype);
    }

    /// <summary>The material set a monument is built from, plus its rune glow colour.</summary>
    private readonly struct Materials
    {
        public readonly ushort Masonry;
        public readonly ushort Rune;
        public readonly ushort Rubble;
        public readonly int Glow;

        public Materials(ushort masonry, ushort rune, ushort rubble, int glow)
        {
            Masonry = masonry;
            Rune = rune;
            Rubble = rubble;
            Glow = glow;
        }
    }

    // ---------------- archetypes ----------------

    /// <summary>A colonnade of arches along X — some intact, some reduced to a springer, some to a stump with
    /// fallen column drums around it. The classic "there was a hall here once" silhouette.</summary>
    private static Canvas Arcade(Materials mat, System.Random rng)
    {
        const int Span = 5;   // pier-to-pier distance of one arch (opening ≈ 3 wide)
        const int Pier = 4;   // pier height before the arch springs
        int arches = 3 + rng.Next(3); // 3..5
        int w = arches * (Span - 1) + 3;
        var c = new Canvas(w, 11, 7);

        int cz = c.L / 2;
        int spared = rng.Next(arches); // one arch always keeps its full curve

        for (int a = 0; a < arches; a++)
        {
            int x0 = 1 + a * (Span - 1);
            // 0 = whole arch, 1 = one pier + a broken springer, 2 = a stump with drums on the ground.
            int state = a == spared ? 0 : rng.NextDouble() < 0.45 ? 0 : rng.NextDouble() < 0.6 ? 1 : 2;
            int leftTop = state == 2 ? 1 + rng.Next(2) : Pier;
            int rightTop = state == 0 ? Pier : state == 1 ? Pier : 1 + rng.Next(2);

            Column(c, x0, cz, leftTop, mat.Masonry);
            Column(c, x0 + Span - 1, cz, rightTop, mat.Masonry);

            if (state == 0)
            {
                Arch(c, x0, x0 + Span - 1, cz, Pier + 1, mat.Masonry);
            }
            else if (state == 1)
            {
                // Only the left haunch is left standing — the curve stops in mid-air.
                c.Set(x0, Pier + 1, cz, mat.Masonry);
                c.Set(x0 + 1, Pier + 2, cz, mat.Masonry, ShapeCode.Pack(BlockShape.Ramp, 1));
            }
            else
            {
                // Collapsed: drums from the fallen shaft rolled out to the sides.
                for (int d = 0; d < 2 + rng.Next(2); d++)
                {
                    int dx = x0 + rng.Next(Span);
                    int dz = cz + (rng.Next(2) == 0 ? -1 : 1) * (1 + rng.Next(2));
                    c.Set(dx, 1, dz, mat.Rubble, ShapeCode.Pack(BlockShape.Cylinder, 0));
                }
            }
        }

        // The stylobate the colonnade stood on — a paved strip under the piers, itself broken up.
        for (int x = 0; x < c.W; x++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (rng.NextDouble() < 0.72)
                {
                    c.Set(x, 0, cz + dz, mat.Masonry);
                }
            }
        }

        Erode(c, rng, baseP: 0.05, topP: 0.28, protectY: 2); // the lower shafts always survive — a colonnade must read as one
        return c;
    }

    /// <summary>One large free-standing gate: two massive piers carrying a stepped lintel, with a doorway
    /// wide and tall enough to walk (and fly a jetpack) through.</summary>
    private static Canvas Gate(Materials mat, System.Random rng)
    {
        var c = new Canvas(13, 14, 7);
        int cz = c.L / 2;
        int height = 8 + rng.Next(3); // pier height
        int gapMin = 5, gapMax = 7;   // the doorway columns (x), 3 wide

        // Two 3×3 piers.
        for (int p = 0; p < 2; p++)
        {
            int px = p == 0 ? 2 : 10;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int y = 0; y <= height; y++)
                    {
                        c.Set(px + dx, y, cz + dz, mat.Masonry);
                    }
                }
            }
        }

        // Lintel across the opening (two courses) + a cornice that oversails the piers.
        for (int x = 1; x <= 11; x++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                c.Set(x, height + 1, cz + dz, mat.Masonry);
                c.Set(x, height + 2, cz + dz, mat.Masonry);
            }
        }

        for (int x = 0; x <= 12; x++)
        {
            c.Set(x, height + 3, cz - 2, mat.Masonry, ShapeCode.Pack(BlockShape.Slab, 0));
            c.Set(x, height + 3, cz + 2, mat.Masonry, ShapeCode.Pack(BlockShape.Slab, 0));
            c.Set(x, height + 3, cz, mat.Masonry, ShapeCode.Pack(BlockShape.Slab, 0));
        }

        // The rune band: the whole front face of the lintel is inscribed — this is the gate's message.
        for (int x = gapMin - 1; x <= gapMax + 1; x++)
        {
            c.Set(x, height + 1, cz - 1, mat.Rune, glow: mat.Glow);
        }

        // Keep the doorway clear (3 wide, full pier height) so a player walks straight through.
        for (int x = gapMin; x <= gapMax; x++)
        {
            for (int y = 1; y <= height; y++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    c.Clear(x, y, cz + dz);
                }
            }
        }

        // Threshold paving through the gate.
        for (int x = gapMin - 1; x <= gapMax + 1; x++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                c.Set(x, 0, cz + dz, mat.Masonry);
            }
        }

        Erode(c, rng, baseP: 0.03, topP: 0.30, protectY: 1, protectDoor: (gapMin, gapMax, cz, height));
        return c;
    }

    /// <summary>A ring of standing stones: some carrying a lintel across their neighbour (a trilithon), some
    /// toppled and lying in the grass, an inscribed altar stone at the centre.</summary>
    private static Canvas Circle(Materials mat, System.Random rng)
    {
        var c = new Canvas(21, 9, 21);
        int cx = c.W / 2, cz = c.L / 2;
        double radius = 7.5 + rng.NextDouble();
        int stones = 8 + rng.Next(5); // 8..12

        var tops = new (int X, int Z, int Top, bool Standing)[stones];
        for (int i = 0; i < stones; i++)
        {
            double ang = System.Math.PI * 2.0 * i / stones;
            int sx = cx + (int)System.Math.Round(System.Math.Cos(ang) * radius);
            int sz = cz + (int)System.Math.Round(System.Math.Sin(ang) * radius);
            bool toppled = rng.NextDouble() < 0.22;
            int h = 3 + rng.Next(3); // 3..5

            if (toppled)
            {
                // Fallen outward, lying flat where it came down.
                int dx = System.Math.Sign(sx - cx), dz = System.Math.Sign(sz - cz);
                for (int s = 0; s < h; s++)
                {
                    c.Set(sx + dx * s, 1, sz + dz * s, mat.Masonry, ShapeCode.Pack(BlockShape.Slab, 0));
                }
            }
            else
            {
                for (int y = 0; y <= h; y++)
                {
                    c.Set(sx, y, sz, mat.Masonry);
                }
            }

            tops[i] = (sx, sz, h, !toppled);
        }

        // Trilithons: a lintel laid from one standing stone to the next, where both still stand.
        for (int i = 0; i < stones; i++)
        {
            var a = tops[i];
            var b = tops[(i + 1) % stones];
            if (!a.Standing || !b.Standing || rng.NextDouble() > 0.35)
            {
                continue;
            }

            int top = System.Math.Min(a.Top, b.Top);
            int steps = System.Math.Max(System.Math.Abs(b.X - a.X), System.Math.Abs(b.Z - a.Z));
            for (int s = 0; s <= steps; s++)
            {
                int lx = a.X + (b.X - a.X) * s / System.Math.Max(1, steps);
                int lz = a.Z + (b.Z - a.Z) * s / System.Math.Max(1, steps);
                c.Set(lx, top + 1, lz, mat.Masonry, ShapeCode.Pack(BlockShape.Slab, 0));
            }
        }

        // The centre stone: a low inscribed table on a small paved disc.
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                if (dx * dx + dz * dz <= 5 && rng.NextDouble() < 0.85)
                {
                    c.Set(cx + dx, 0, cz + dz, mat.Masonry);
                }
            }
        }

        c.Set(cx, 1, cz, mat.Rune, ShapeCode.Pack(BlockShape.Slab, 0), mat.Glow);
        c.Set(cx + 1, 1, cz, mat.Masonry, ShapeCode.Pack(BlockShape.Slab, 0));
        c.Set(cx - 1, 1, cz, mat.Masonry, ShapeCode.Pack(BlockShape.Slab, 0));

        Erode(c, rng, baseP: 0.04, topP: 0.22, protectY: 1); // every stone keeps a visible foot
        return c;
    }

    /// <summary>A single tapering monolith on a stepped base, its tip cracked off and lying at its foot.</summary>
    private static Canvas Obelisk(Materials mat, System.Random rng)
    {
        var c = new Canvas(9, 16, 9);
        int cx = c.W / 2, cz = c.L / 2;
        int shaft = 8 + rng.Next(4); // 8..11 above the base

        // Stepped base: 5×5, then 3×3.
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                c.Set(cx + dx, 0, cz + dz, mat.Masonry);
                if (System.Math.Abs(dx) <= 2 && System.Math.Abs(dz) <= 2 && (System.Math.Abs(dx) == 2 || System.Math.Abs(dz) == 2))
                {
                    c.Set(cx + dx, 1, cz + dz, mat.Masonry, ShapeCode.Pack(BlockShape.Slab, 0));
                }
            }
        }

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                c.Set(cx + dx, 1, cz + dz, mat.Masonry);
            }
        }

        // The shaft, with an inscribed course every third block.
        for (int y = 2; y <= shaft; y++)
        {
            bool band = (y - 2) % 3 == 2;
            c.Set(cx, y, cz, band ? mat.Rune : mat.Masonry, glow: band ? mat.Glow : 0);
        }

        bool cracked = rng.NextDouble() < 0.55;
        if (!cracked)
        {
            c.Set(cx, shaft + 1, cz, mat.Masonry, ShapeCode.Pack(BlockShape.Pyramid, 0));
        }
        else
        {
            // The tip came down: it lies on the base, broken in two.
            int dx = rng.Next(2) == 0 ? -1 : 1;
            c.Set(cx + dx * 2, 1, cz + 1, mat.Masonry, ShapeCode.Pack(BlockShape.Pyramid, 0));
            c.Set(cx + dx * 2, 1, cz, mat.Rubble);
        }

        // A smaller companion menhir, leaning out of the base.
        int mx = cx + (rng.Next(2) == 0 ? -3 : 3);
        int mz = cz + (rng.Next(2) == 0 ? -3 : 3);
        for (int y = 0; y <= 1 + rng.Next(3); y++)
        {
            c.Set(mx, y, mz, mat.Masonry, ShapeCode.Pack(BlockShape.Post, 0));
        }

        // Only the upper half of the shaft weathers — an obelisk that loses its base is just a plinth.
        Erode(c, rng, baseP: 0.0, topP: 0.35, protectY: 2 + shaft / 2);
        return c;
    }

    /// <summary>A low paved platform carrying an inscribed altar table, ringed by kneeling stones.</summary>
    private static Canvas Altar(Materials mat, System.Random rng)
    {
        var c = new Canvas(13, 7, 13);
        int cx = c.W / 2, cz = c.L / 2;

        // Paving: a 9×9 court, then a raised 5×5 dais.
        for (int dx = -4; dx <= 4; dx++)
        {
            for (int dz = -4; dz <= 4; dz++)
            {
                if (rng.NextDouble() < 0.88)
                {
                    c.Set(cx + dx, 0, cz + dz, mat.Masonry);
                }
            }
        }

        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                c.Set(cx + dx, 1, cz + dz, mat.Masonry, ShapeCode.Pack(BlockShape.Slab, 0));
            }
        }

        // The table: two legs and an inscribed slab across them.
        c.Set(cx - 1, 2, cz, mat.Masonry, ShapeCode.Pack(BlockShape.Post, 0));
        c.Set(cx + 1, 2, cz, mat.Masonry, ShapeCode.Pack(BlockShape.Post, 0));
        for (int dx = -1; dx <= 1; dx++)
        {
            c.Set(cx + dx, 3, cz, mat.Rune, ShapeCode.Pack(BlockShape.Slab, 0), mat.Glow);
        }

        // Kneeling stones on a ring around the dais.
        for (int i = 0; i < 6; i++)
        {
            double ang = System.Math.PI * 2.0 * i / 6 + rng.NextDouble() * 0.3;
            int sx = cx + (int)System.Math.Round(System.Math.Cos(ang) * 4.0);
            int sz = cz + (int)System.Math.Round(System.Math.Sin(ang) * 4.0);
            if (rng.NextDouble() < 0.8)
            {
                c.Set(sx, 1, sz, mat.Masonry, ShapeCode.Pack(BlockShape.Slab, 0));
            }
        }

        Erode(c, rng, baseP: 0.05, topP: 0.20, protectY: 0);
        return c;
    }

    // ---------------- building blocks ----------------

    /// <summary>A round column shaft of <paramref name="top"/> blocks with a square capital.</summary>
    private static void Column(Canvas c, int x, int z, int top, ushort masonry)
    {
        for (int y = 0; y <= top; y++)
        {
            c.Set(x, y, z, masonry, y == 0 || y == top ? 0 : ShapeCode.Pack(BlockShape.Cylinder, 0));
        }
    }

    /// <summary>An arch spanning two piers, corbelled the way stone actually carries: springers on the pier
    /// tops, wedge haunches stepping diagonally inwards over them, and an architrave course closing the span.
    /// Ramp yaw 1 is full-height at −X and yaw 3 at +X (see <c>BlockShapeGeometry.Ramp</c>), so each haunch
    /// leans over the opening instead of away from it.</summary>
    private static void Arch(Canvas c, int xLeft, int xRight, int z, int y, ushort masonry)
    {
        c.Set(xLeft, y, z, masonry);
        c.Set(xRight, y, z, masonry);
        c.Set(xLeft + 1, y + 1, z, masonry, ShapeCode.Pack(BlockShape.Ramp, 1));
        c.Set(xRight - 1, y + 1, z, masonry, ShapeCode.Pack(BlockShape.Ramp, 3));
        for (int x = xLeft; x <= xRight; x++)
        {
            c.Set(x, y + 2, z, masonry);
        }
    }

    /// <summary>Takes pieces away — probability rises with height so bases survive and crowns come down.
    /// One protected course (<paramref name="protectY"/> and below) keeps the footprint readable, and a gate's
    /// doorway jambs are never eroded so the opening stays walkable.</summary>
    private static void Erode(Canvas c, System.Random rng, double baseP, double topP, int protectY,
        (int MinX, int MaxX, int CZ, int Height)? protectDoor = null)
    {
        for (int x = 0; x < c.W; x++)
        {
            for (int y = 0; y < c.H; y++)
            {
                for (int z = 0; z < c.L; z++)
                {
                    if (c.Get(x, y, z) == 0 || y <= protectY)
                    {
                        continue;
                    }

                    if (protectDoor is { } d && y <= d.Height + 3
                        && x >= d.MinX - 2 && x <= d.MaxX + 2
                        && System.Math.Abs(z - d.CZ) <= 2)
                    {
                        continue; // the jambs and the lintel over the opening must not become a widow-maker
                    }

                    double frac = (double)y / System.Math.Max(1, c.H - 1);
                    if (rng.NextDouble() < baseP + (topP - baseP) * frac)
                    {
                        c.Clear(x, y, z);
                    }
                }
            }
        }

        Settle(c, protectY);
    }

    /// <summary>Swaps a seeded scatter of surviving masonry to the rune material and lights it with the
    /// monument's glow colour. Runs AFTER erosion (so no rune is eroded away) and always leaves at least one
    /// rune standing — the scanner needs something to read.</summary>
    private static void ScatterRunes(Canvas c, Materials mat, System.Random rng)
    {
        var candidates = new List<(int X, int Y, int Z)>();
        int existing = 0;
        for (int x = 0; x < c.W; x++)
        {
            for (int y = 0; y < c.H; y++)
            {
                for (int z = 0; z < c.L; z++)
                {
                    ushort id = c.Get(x, y, z);
                    if (id == mat.Rune)
                    {
                        existing++;
                    }
                    else if (id == mat.Masonry && y >= 1)
                    {
                        candidates.Add((x, y, z));
                    }
                }
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        // An archetype that carved its own inscription (gate lintel, altar table, obelisk bands) only gets a
        // light dusting on top; one without needs at least one rune so the monument is always scannable.
        int wanted = existing > 0
            ? candidates.Count / 24
            : System.Math.Max(1, candidates.Count / 12);
        for (int i = 0; i < wanted; i++)
        {
            var (x, y, z) = candidates[rng.Next(candidates.Count)];
            c.Set(x, y, z, mat.Rune, c.ShapeAt(x, y, z), mat.Glow);
        }
    }

    /// <summary>Brings down what erosion left hanging: a stone with nothing under it, nothing corbelled under
    /// its shoulder and nothing beside it to lean on falls. Spans (architraves, lintels, cornices) hold each
    /// other, and a corbelled arch is carried by the course diagonally below it — so silhouettes survive while
    /// orphaned column segments floating in mid-air do not.</summary>
    private static void Settle(Canvas c, int protectY)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            bool changed = false;
            for (int y = protectY + 1; y < c.H; y++)
            {
                for (int x = 0; x < c.W; x++)
                {
                    for (int z = 0; z < c.L; z++)
                    {
                        if (c.Get(x, y, z) == 0 || Supported(c, x, y, z))
                        {
                            continue;
                        }

                        c.Clear(x, y, z);
                        changed = true;
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    private static bool Supported(Canvas c, int x, int y, int z)
        => c.Get(x, y - 1, z) != 0                                              // straight down
        || c.Get(x - 1, y - 1, z) != 0 || c.Get(x + 1, y - 1, z) != 0           // corbelled shoulder
        || c.Get(x, y - 1, z - 1) != 0 || c.Get(x, y - 1, z + 1) != 0
        || c.Get(x - 1, y, z) != 0 || c.Get(x + 1, y, z) != 0                   // leaning on the span beside it
        || c.Get(x, y, z - 1) != 0 || c.Get(x, y, z + 1) != 0;

    /// <summary>Places the relic-cache marker on a free cell near the monument's centre.</summary>
    private static void AddCacheMarker(Canvas c, System.Random rng)
    {
        int cx = c.W / 2, cz = c.L / 2;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            int x = System.Math.Clamp(cx + rng.Next(-3, 4), 1, c.W - 2);
            int z = System.Math.Clamp(cz + rng.Next(-3, 4), 1, c.L - 2);
            if (c.Get(x, 1, z) == 0)
            {
                c.Markers.Add(new SettlementMarker("relic_cache", new Vector3i(x, 1, z)));
                return;
            }
        }

        c.Markers.Add(new SettlementMarker("relic_cache", new Vector3i(cx, 1, cz)));
    }

    // ---------------- canvas ----------------

    /// <summary>A local voxel canvas with per-cell shape + glow, converted to a <see cref="SettlementStructure"/>
    /// at the end. Out-of-bounds writes are ignored, so an archetype may draw past its own edge safely.</summary>
    private sealed class Canvas
    {
        public readonly int W;
        public readonly int H;
        public readonly int L;
        public readonly List<SettlementMarker> Markers = new();

        private readonly ushort[] _blocks;
        private readonly Dictionary<int, (int Tint, int Glow)> _mods = new();
        private readonly Dictionary<int, int> _shapes = new();

        public Canvas(int w, int h, int l)
        {
            W = w;
            H = h;
            L = l;
            _blocks = new ushort[w * h * l];
        }

        public ushort Get(int x, int y, int z) => In(x, y, z) ? _blocks[Index(x, y, z)] : (ushort)0;

        public int ShapeAt(int x, int y, int z)
            => In(x, y, z) && _shapes.TryGetValue(Index(x, y, z), out var s) ? s : 0;

        public void Set(int x, int y, int z, ushort id, int shape = 0, int glow = 0)
        {
            if (!In(x, y, z) || id == 0)
            {
                return;
            }

            int i = Index(x, y, z);
            _blocks[i] = id;
            if (shape != 0)
            {
                _shapes[i] = shape;
            }
            else
            {
                _shapes.Remove(i);
            }

            if (glow != 0)
            {
                _mods[i] = (0, glow);
            }
            else
            {
                _mods.Remove(i);
            }
        }

        public void Clear(int x, int y, int z)
        {
            if (!In(x, y, z))
            {
                return;
            }

            int i = Index(x, y, z);
            _blocks[i] = 0;
            _shapes.Remove(i);
            _mods.Remove(i);
        }

        public SettlementStructure ToStructure(string archetype)
            => new(W, H, L, "monument:" + archetype, ruined: true, inhabitant: string.Empty,
                _blocks, Markers, buildingCount: 1, _mods, _shapes);

        private bool In(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < W && y < H && z < L;

        private int Index(int x, int y, int z) => (x * H + y) * L + z;
    }
}
