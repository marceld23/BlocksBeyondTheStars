// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>A procedural space station of a size tier (<see cref="StationGenerator.Generate"/>); the seed is the
/// station's seed.</summary>
internal sealed class StationSource : StructureSourceBase
{
    public static readonly string[] Tiers = { "small", "medium", "large", "huge", "colossal" };

    public StationSource()
        : base("station", StructureSourceKind.Space)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content) => new[]
    {
        StructureParam.Choice("tier", Tiers, "medium"),
    };

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
        => BuiltConvert.FromStation(Key, StationGenerator.Generate(p.Get("tier"), r.Seed, r.Content));
}

/// <summary>A space station composed from a station kit (<see cref="StationKitComposer.Compose"/>), its modules looked up
/// by key among the station templates as the server does.</summary>
internal sealed class StationKitSource : StructureSourceBase
{
    public StationKitSource()
        : base("station_kit", StructureSourceKind.Space)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content) => new[]
    {
        StructureParam.Choice("kit", SourceOptions.KitKeys(content.KitsFor(StructureKit.KindStation, null, null, null), withNone: false)),
    };

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var content = r.Content;
        var kit = content.KitByKey(p.Get("kit")) ?? throw new InvalidOperationException("The content holds no station kit.");
        var s = StationKitComposer.Compose(kit, key => content.TemplateByKey(StructureKit.KindStation, key), r.Seed, content, out _,
                    out string? failure)
                ?? throw new InvalidOperationException($"Station kit '{kit.Key}' cannot be assembled: {failure}");
        return BuiltConvert.FromStation(Key, s);
    }
}

/// <summary>
/// A derelict station (#2632): every new one is the wreck of a beta station (<see cref="DerelictGenerator.Generate"/>), one a
/// save stored before #2674 keeps its legacy layout (<see cref="DerelictGenerator.GenerateLegacy"/>; <c>guaranteed</c> = the
/// start system's medium module station). Its spots become markers: <c>door</c> (data <c>x</c>/<c>z</c>, the leaf's
/// axis), <c>arrival</c>, <c>crate</c>, <c>guardian_spawner</c> and <c>trader_post</c>.
/// </summary>
internal sealed class DerelictSource : StructureSourceBase
{
    public const string VariantBeta = "beta";
    public const string VariantLegacy = "legacy";

    public DerelictSource()
        : base("derelict", StructureSourceKind.Space)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content) => new[]
    {
        StructureParam.Choice("variant", new[] { VariantBeta, VariantLegacy }, VariantBeta),
        StructureParam.Bool("guaranteed", false),
    };

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var layout = p.Get("variant") == VariantLegacy
            ? DerelictGenerator.GenerateLegacy(r.Seed, r.Content, proceduralOnly: p.Bool("guaranteed"))
            : DerelictGenerator.Generate(r.Seed, r.Content);
        var b = new BuiltStructure(Key, layout.Width, layout.Height, layout.Length);
        BuiltConvert.CopyGrid(b, layout.Width, layout.Height, layout.Length, 0, layout.Get, layout.GetModifier, layout.GetShape);
        foreach (var d in layout.Doors)
        {
            b.Markers.Add(new BuiltMarker("door", d.Cell.X, d.Cell.Y, d.Cell.Z, d.AxisX ? "x" : "z"));
        }

        b.Markers.Add(new BuiltMarker("arrival", layout.Arrival.X, layout.Arrival.Y, layout.Arrival.Z));
        foreach (var c in layout.CrateSpots)
        {
            b.Markers.Add(new BuiltMarker("crate", c.X, c.Y, c.Z));
        }

        if (layout.SpawnerSpot is { } spawner)
        {
            b.Markers.Add(new BuiltMarker("guardian_spawner", spawner.X, spawner.Y, spawner.Z));
        }

        foreach (var t in layout.TraderPosts)
        {
            b.Markers.Add(new BuiltMarker("trader_post", t.X, t.Y, t.Z));
        }

        return b;
    }
}
