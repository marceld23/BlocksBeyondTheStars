// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// A fallen guardian giant on the ground (#3680, terrain generation 44): the lying block body of
/// <see cref="GuardianGiantBody.Lying"/> around its anchor, emitted through an <see cref="IStructureCellSink"/> — the server's
/// world sink writes it as ordinary block edits (mineable, unprotected), the editor's flat-ground sink collects it.
/// <para><b>How it lies.</b> The anchor stands <see cref="AnchorAboveGround"/> rows over the ground top it was seated on, so
/// the back (<see cref="GuardianGiantBody.LyingBottom"/>: the torso's back plates, the reactor, the shoulder plates) is sunk one
/// row into the ground and the arms, legs and head rest on it — it fell there long ago. Every column of the body reaches down
/// to that bottom row or the one above it, so on the levelled bed the server reserves nothing hangs in the air.</para>
/// <para><b>Never floating.</b> Where the ground under a column dips (no reserved bed, an old cave mouth), the cells under the
/// column's lowest cell are filled with <c>groundBlock</c> through air, fluids and plants until the first solid cell, at most
/// <see cref="MaxFill"/> deep. A short plant left standing on top of a column (the upper half of a tall flower over a horn, air
/// above it) is cleared.
/// Order: the body cells in <see cref="GuardianGiantBody.Lying"/> order, then the fill, then the clearing — the columns by
/// their x offset, then their z offset.</para>
/// </summary>
public static class GuardianGiantBuilder
{
    /// <summary>The anchor's height over the ground top it lies on: the bottom row (<see cref="GuardianGiantBody.LyingBottom"/>)
    /// replaces the ground's top cell, the row above it rests on the ground.</summary>
    public const int AnchorAboveGround = -GuardianGiantBody.LyingBottom;

    /// <summary>How far under a column's lowest cell the ground is filled where it dips.</summary>
    public const int MaxFill = 4;

    /// <summary>
    /// Emits one lying giant. <paramref name="ax"/>, <paramref name="ay"/>, <paramref name="az"/> is the anchor (the record's
    /// <c>X</c>/<c>Y</c>/<c>Z</c>), <paramref name="seed"/> picks the rusted plates, <paramref name="facing"/> the quarter turn,
    /// <paramref name="lit"/> the lights' block; <paramref name="groundBlock"/> is the fill under a dipping column (stone when
    /// the content lacks it). Returns how many cells were written.
    /// </summary>
    public static int Build(IStructureCellSink sink, GameContent content, string groundBlock, int ax, int ay, int az, int seed, int facing,
        bool lit)
    {
        var columns = new SortedDictionary<(int Dx, int Dz), (int Bottom, int Top)>();
        var ids = new Dictionary<string, BlockId>();
        int written = 0;
        foreach (var (dx, dy, dz, key, _) in GuardianGiantBody.Lying(seed, lit, facing))
        {
            if (!ids.TryGetValue(key, out var id))
            {
                id = content.GetBlock(key)?.NumericId ?? BlockId.Air;
                ids[key] = id;
            }

            if (id.IsAir)
            {
                continue; // a content without the block: the cell stays as it is
            }

            sink.Put(ax + dx, ay + dy, az + dz, id);
            written++;
            columns[(dx, dz)] = columns.TryGetValue((dx, dz), out var c)
                ? (System.Math.Min(c.Bottom, dy), System.Math.Max(c.Top, dy))
                : (dy, dy);
        }

        var cells = new CellKinds(content);
        var ground = (content.GetBlock(groundBlock) ?? content.GetBlock("stone"))?.NumericId ?? BlockId.Air;
        foreach (var kv in columns)
        {
            written += FillUnder(sink, cells, ground, ax + kv.Key.Dx, ay + kv.Value.Bottom, az + kv.Key.Dz);
        }

        foreach (var kv in columns)
        {
            int x = ax + kv.Key.Dx, y = ay + kv.Value.Top + 1, z = az + kv.Key.Dz;
            if (cells.IsPlant(sink.Get(x, y, z)) && sink.Get(x, y + 1, z).IsAir) // a short plant, never a trunk or a canopy
            {
                sink.Put(x, y, z, BlockId.Air);
            }
        }

        return written;
    }

    /// <summary>The cells of a lying giant: its anchor plus every offset of <see cref="GuardianGiantBody.Lying"/>, with the block
    /// key — the cells <see cref="Build"/> writes before the fill under them (not wrapped at the seam).</summary>
    public static IEnumerable<(int X, int Y, int Z, string Block)> Cells(int ax, int ay, int az, int seed, int facing, bool lit)
    {
        foreach (var (dx, dy, dz, key, _) in GuardianGiantBody.Lying(seed, lit, facing))
        {
            yield return (ax + dx, ay + dy, az + dz, key);
        }
    }

    /// <summary>Fills the dip under one column: down from the cell under its lowest cell through air, fluids and plants until
    /// the first solid cell, at most <see cref="MaxFill"/> cells.</summary>
    private static int FillUnder(IStructureCellSink sink, CellKinds cells, BlockId ground, int x, int bottomY, int z)
    {
        if (ground.IsAir)
        {
            return 0;
        }

        int filled = 0;
        for (int d = 1; d <= MaxFill; d++)
        {
            var id = sink.Get(x, bottomY - d, z);
            if (!id.IsAir && !cells.IsPlant(id) && !cells.IsFluid(id))
            {
                break;
            }

            sink.Put(x, bottomY - d, z, ground);
            filled++;
        }

        return filled;
    }

    /// <summary>What a cell under or over the body is: a plant (the <c>flora</c> category — a tuft, a flower, leaves) or a fluid
    /// (water, lava — the fluid automaton's two, by key — or a still liquid such as oil).</summary>
    private sealed class CellKinds
    {
        private readonly GameContent _content;
        private readonly ushort _water;
        private readonly ushort _lava;

        public CellKinds(GameContent content)
        {
            _content = content;
            _water = content.GetBlock("water")?.NumericId.Value ?? 0;
            _lava = content.GetBlock("lava")?.NumericId.Value ?? 0;
        }

        public bool IsPlant(BlockId id) => !id.IsAir && _content.BlockById(id) is { Category: "flora" };

        public bool IsFluid(BlockId id)
            => !id.IsAir && (id.Value == _water || id.Value == _lava || _content.BlockById(id) is { Liquid: true });
    }
}

/// <summary>
/// The seeded decisions of the guardian giants on one body (#3680) — pure functions of the world seed, the body and the giant's
/// index, each on a lane of its own, so the server, the tests and any later reader agree: how many giants the body rolls
/// (<see cref="Count"/>), each one's quarter turn, body seed, seat-search seed and whether it can still wake (about
/// <see cref="GuardianGiantRules.WakeShare"/> of them).
/// </summary>
public static class GuardianGiantSites
{
    /// <summary>
    /// How many giants a body rolls: a first one below <paramref name="firstChance"/>, then each further one below its entry of
    /// <paramref name="further"/> (the second giant the first entry, …) as long as the one before came — at most
    /// <paramref name="cap"/> (0 = as many as <paramref name="further"/> allows).
    /// </summary>
    public static int Count(long worldSeed, string locationId, double firstChance, IReadOnlyList<double> further, int cap)
    {
        int limit = 1 + further.Count;
        if (cap > 0)
        {
            limit = System.Math.Min(limit, cap);
        }

        if (limit <= 0 || Roll(worldSeed, locationId, 0) >= System.Math.Max(0.0, System.Math.Min(1.0, firstChance)))
        {
            return 0;
        }

        int count = 1;
        while (count < limit && Roll(worldSeed, locationId, count) < further[count - 1])
        {
            count++;
        }

        return count;
    }

    /// <summary>The quarter turn (0–3) of the body's <paramref name="index"/>-th giant.</summary>
    public static int Facing(long worldSeed, string locationId, int index)
        => Shared.World.StableHashing.Index(Lane("facing", locationId, index), worldSeed, 4);

    /// <summary>The body seed of the giant (<see cref="GuardianGiantBody.Standing"/>'s rust, the loot roll).</summary>
    public static int BodySeed(long worldSeed, string locationId, int index)
        => Shared.World.StableHashing.Seed32(Lane("seed", locationId, index), worldSeed);

    /// <summary>The seed of the giant's seat search.</summary>
    public static long SearchSeed(long worldSeed, string locationId, int index)
        => unchecked((long)Shared.World.StableHashing.Mixed(Lane("search", locationId, index), worldSeed));

    /// <summary>Whether the giant can still wake: a roll below <see cref="GuardianGiantRules.WakeShare"/>.</summary>
    public static bool CanWake(long worldSeed, string locationId, int index)
        => Shared.World.StableHashing.Unit(Lane("wake", locationId, index), worldSeed) < GuardianGiantRules.WakeShare;

    /// <summary>The count roll of the body's <paramref name="index"/>-th giant, uniform in [0, 1).</summary>
    private static double Roll(long worldSeed, string locationId, int index)
        => Shared.World.StableHashing.Unit(Lane("count", locationId, index), worldSeed);

    private static string Lane(string what, string locationId, int index)
        => "guardiangiant:" + what + ":" + locationId + "#" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
