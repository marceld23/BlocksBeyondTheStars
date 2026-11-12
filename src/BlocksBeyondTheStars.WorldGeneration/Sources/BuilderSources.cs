// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>
/// The editor's <see cref="IStructureCellSink"/> (#3522): a builder runs on flat ground — every column's surface at
/// <see cref="GroundY"/>, the ground of the request's surface block below it, air above — and the cells it writes are
/// collected into a <see cref="BuiltStructure"/> around everything it touched. A block write keeps the block (with its dye,
/// glow and shape); an air write that carves something (the ground or an earlier block) becomes a cavity cell, an air write
/// into air is nothing — exactly what it does to a world. Reads see the earlier writes over the flat ground. The ground row is
/// the surface row (<see cref="GroundY"/>); markers keep the builder's order.
/// </summary>
internal sealed class FlatGroundSink : IStructureCellSink
{
    /// <summary>The surface row of the flat ground (high enough that every builder's underground part fits above zero).</summary>
    public const int GroundY = 64;

    private readonly BlockId _ground;
    private readonly Dictionary<(int X, int Y, int Z), (BlockId Block, int Tint, int Glow, int Shape)> _cells = new();
    private readonly List<(string Type, int X, int Y, int Z, string Data)> _markers = new();

    public FlatGroundSink(BlockId ground) => _ground = ground;

    /// <summary>A sink over flat ground of the request's surface block (stone where the content lacks it).</summary>
    public static FlatGroundSink For(StructureBuildRequest r)
        => new((r.Content.GetBlock(r.Surface) ?? r.Content.GetBlock("stone"))?.NumericId ?? BlockId.Air);

    /// <summary>The surface row of every column — the builders' ground function on flat ground.</summary>
    public static int Ground(int x, int z) => GroundY;

    public void Set(int x, int y, int z, BlockId block, int tint, int glow, int shape)
    {
        if (block.IsAir && Get(x, y, z).IsAir)
        {
            return; // air into air carves nothing
        }

        _cells[(x, y, z)] = (block, tint, glow, shape);
    }

    public BlockId Get(int x, int y, int z)
        => _cells.TryGetValue((x, y, z), out var c) ? c.Block : y <= GroundY ? _ground : BlockId.Air;

    public void Mark(string type, int x, int y, int z, string data) => _markers.Add((type, x, y, z, data ?? string.Empty));

    /// <summary>The collected cells and markers as a structure of <paramref name="key"/>: the box around them, carved cells
    /// as cavity, the surface row as the ground row.</summary>
    public BuiltStructure ToBuilt(string key)
    {
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
        void Grow(int x, int y, int z)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            minZ = Math.Min(minZ, z);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            maxZ = Math.Max(maxZ, z);
        }

        foreach (var cell in _cells.Keys)
        {
            Grow(cell.X, cell.Y, cell.Z);
        }

        foreach (var m in _markers)
        {
            Grow(m.X, m.Y, m.Z);
        }

        if (minX == int.MaxValue)
        {
            return new BuiltStructure(key, 1, 1, 1); // the content lacked every block the builder needs
        }

        var b = new BuiltStructure(key, maxX - minX + 1, maxY - minY + 1, maxZ - minZ + 1, GroundY - minY);
        foreach (var kv in _cells)
        {
            int x = kv.Key.X - minX, y = kv.Key.Y - minY, z = kv.Key.Z - minZ;
            if (kv.Value.Block.IsAir)
            {
                b.Cavity.Add(b.Index(x, y, z));
            }
            else
            {
                b.Set(x, y, z, kv.Value.Block.Value, kv.Value.Tint, kv.Value.Glow, kv.Value.Shape);
            }
        }

        foreach (var m in _markers)
        {
            b.Markers.Add(new BuiltMarker(m.Type, m.X - minX, m.Y - minY, m.Z - minZ, m.Data));
        }

        return b;
    }
}

/// <summary>
/// A buried vault (<see cref="VaultBuilder"/>): the chamber sixteen below the surface, the shaft, the broken pillar ring (its
/// gaps and heights from the seed — the vault stream) and the loot spots; <c>wellhead</c> builds the well tube a vault under
/// water gets (<see cref="WellWaterDepth"/> of water over the seabed), <c>crystal</c> adds the crystal-vault puzzle (in a
/// world every third vault, by position).
/// </summary>
internal sealed class VaultSource : StructureSourceBase
{
    /// <summary>How deep the water over a wellhead vault's seabed stands on flat ground.</summary>
    public const int WellWaterDepth = 6;

    public VaultSource()
        : base("vault", StructureSourceKind.Underground)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content) => new[]
    {
        StructureParam.Bool("wellhead", false),
        StructureParam.Bool("crystal", false),
    };

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var sink = FlatGroundSink.For(r);
        const int surfaceY = FlatGroundSink.GroundY;
        int? waterTop = p.Bool("wellhead") ? surfaceY + WellWaterDepth : null;
        VaultBuilder.Build(sink, r.Content, 0, 0, surfaceY, waterTop, InstanceRng(r.Seed), FlatGroundSink.Ground);
        if (p.Bool("crystal"))
        {
            VaultBuilder.BuildCrystalPuzzle(sink, r.Content, 0, 0, VaultBuilder.FloorY(surfaceY));
        }

        return sink.ToBuilt(Key);
    }
}

/// <summary>The Singing Shrine (<see cref="UniqueSiteBuilder.BuildShrine"/>) — one per galaxy in a world; no parameters,
/// the same for every seed.</summary>
internal sealed class SingingShrineSource : StructureSourceBase
{
    public SingingShrineSource()
        : base("singing_shrine", StructureSourceKind.Surface)
    {
    }

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var sink = FlatGroundSink.For(r);
        UniqueSiteBuilder.BuildShrine(sink, r.Content, 0, FlatGroundSink.GroundY, 0);
        return sink.ToBuilt(Key);
    }
}

/// <summary>The Sealed Observatory (<see cref="UniqueSiteBuilder.BuildObservatory"/>) — one per galaxy in a world; no
/// parameters, the same for every seed.</summary>
internal sealed class SealedObservatorySource : StructureSourceBase
{
    public SealedObservatorySource()
        : base("sealed_observatory", StructureSourceKind.Surface)
    {
    }

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var sink = FlatGroundSink.For(r);
        UniqueSiteBuilder.BuildObservatory(sink, r.Content, 0, FlatGroundSink.GroundY, 0);
        return sink.ToBuilt(Key);
    }
}

/// <summary>One of Soaniri's three places (<see cref="SoaniriBuilder"/>): <c>mirun</c>, <c>ornasa</c> or <c>choir_tower</c>.
/// Their broken heights and gaps hash the body's id in a world; here the seed stands in for it. The flat ground is the
/// request's surface: the Choir Tower's barren ring (chime moss turned to scree) shows on a surface of <c>chime_moss</c>.</summary>
internal sealed class SoaniriSiteSource : StructureSourceBase
{
    private readonly string _site;

    public SoaniriSiteSource(string site)
        : base(site, StructureSourceKind.Surface) => _site = site;

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var sink = FlatGroundSink.For(r);
        string location = r.Seed.ToString(CultureInfo.InvariantCulture);
        new SoaniriBuilder(sink, r.Content, location, FlatGroundSink.Ground).Build(_site, 0, FlatGroundSink.GroundY, 0);
        return sink.ToBuilt(Key);
    }
}

/// <summary>The finale's Guardian-core chamber (<see cref="GuardianCoreBuilder"/>) with its aperture shaft up to the surface;
/// no parameters, the same for every seed.</summary>
internal sealed class GuardianCoreSource : StructureSourceBase
{
    public GuardianCoreSource()
        : base("guardian_core", StructureSourceKind.Underground)
    {
    }

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var sink = FlatGroundSink.For(r);
        const int surfaceY = FlatGroundSink.GroundY;
        GuardianCoreBuilder.Build(sink, r.Content, 0, 0, GuardianCoreBuilder.FloorY(surfaceY), surfaceY, FlatGroundSink.Ground);
        return sink.ToBuilt(Key);
    }
}

/// <summary>A hunter shuttle's camp (<see cref="HunterCampBuilder"/>): the landed hull and its robot docks — two or three, by
/// the seed.</summary>
internal sealed class HunterCampSource : StructureSourceBase
{
    public HunterCampSource()
        : base("hunter_camp", StructureSourceKind.Surface)
    {
    }

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var sink = FlatGroundSink.For(r);
        const int feet = FlatGroundSink.GroundY + 1;
        HunterCampBuilder.Build(sink, r.Content, r.Surface, 0, feet, 0, InstanceRng(r.Seed), (_, _) => feet, (_, _, _) => true);
        return sink.ToBuilt(Key);
    }
}
