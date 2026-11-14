// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>
/// #3645: a maze ruin of the Labyrinth Builders (<see cref="MazeRuinPlan"/>, <see cref="MazeRuinGenerator"/>) — built intact;
/// as a landmark generator entry with <c>ruin: always</c> it falls through the shared ruin pass, its lowest wall rows kept.
/// Parameters: <c>size</c> (S / M / L ≈ 80 / 120 / 180 blocks across), <c>height</c> (the walls above the floor),
/// <c>braid</c> (percent of the dead ends opened into loops), <c>halls</c> (2 × 2 halls, as many as fit), <c>statues</c>
/// (percent of the roomy dead ends with a statue) and the three blocks <c>wall</c>, <c>accent</c> and <c>floor</c> by key —
/// a source builds block ids, not material tokens, so the civilization's materials travel as these parameters (an unknown
/// key builds with the dark default). Everything comes from the instance seed.
/// </summary>
internal sealed class MazeRuinSource : StructureSourceBase
{
    public const string DefaultWall = "basalt";
    public const string DefaultAccent = "carved_obsidian";
    public const string DefaultFloor = "deepslate";

    /// <summary>The block every missing one falls back to.</summary>
    private const string LastResort = "stone";

    private static readonly StructureParam[] Params =
    {
        StructureParam.Choice("size", MazeRuinPlan.Sizes, MazeRuinPlan.Medium),
        StructureParam.Int("height", 20, 6, 40),
        StructureParam.Int("braid", 15, 0, 100),
        StructureParam.Int("halls", 3, 0, 12),
        StructureParam.Int("statues", 40, 0, 100),
        StructureParam.Text("wall", DefaultWall),
        StructureParam.Text("accent", DefaultAccent),
        StructureParam.Text("floor", DefaultFloor),
    };

    public MazeRuinSource()
        : base("maze_ruin", StructureSourceKind.Surface)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content) => Params;

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        ushort stone = BlockOf(r.Content, LastResort, 0);
        ushort wall = BlockOf(r.Content, p.Get("wall"), BlockOf(r.Content, DefaultWall, stone));
        if (wall == 0)
        {
            throw new InvalidOperationException("The content holds none of the maze ruin's wall blocks.");
        }

        ushort accent = BlockOf(r.Content, p.Get("accent"), BlockOf(r.Content, DefaultAccent, wall));
        ushort floor = BlockOf(r.Content, p.Get("floor"), BlockOf(r.Content, DefaultFloor, wall));
        var plan = MazeRuinPlan.Create(r.Seed, p.Get("size"), p.Int("braid"), p.Int("halls"));
        return MazeRuinGenerator.Build(Key, plan, r.Seed, new MazeRuinLook(p.Int("height"), p.Int("statues"), wall, accent, floor));
    }

    /// <summary>The numeric id of the block <paramref name="key"/>, or <paramref name="fallback"/> when the content lacks it.</summary>
    private static ushort BlockOf(GameContent content, string key, ushort fallback)
        => content.GetBlock((key ?? string.Empty).Trim())?.NumericId.Value ?? fallback;
}
