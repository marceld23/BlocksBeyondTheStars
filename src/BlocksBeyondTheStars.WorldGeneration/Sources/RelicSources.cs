// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>
/// A crashed ship of a shipped design — or of a user ship that allows wrecks. The generator draws its origin (human or
/// alien hull) from the seed. As it stands in a world it is breached and scorched; intact it is the repair mask — the hull
/// a full repair restores. #3521: for terrain generation 38 and later a ship with a layout is built from that layout
/// (<see cref="WreckGenerator.GenerateFromLayout"/>, sunk like a planet wreck: its ground row is the row at the surface);
/// before it, and for a ship without a layout, it is the classic box (<see cref="WreckGenerator.Generate"/>).
/// </summary>
internal sealed class WreckSource : StructureSourceBase
{
    public WreckSource()
        : base("wreck", StructureSourceKind.Surface, supportsIntact: true)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content)
    {
        var ships = new List<string>(content.Ships.Keys);
        ships.Sort(StringComparer.Ordinal);
        foreach (var user in content.UserShipsFor(Shared.Definitions.UserShipUses.Wreck))
        {
            ships.Add(user.Key); // #3521: after the shipped ones, so the default stays a shipped ship
        }

        return new[] { StructureParam.Choice("ship", ships) };
    }

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var design = r.Content.GetShip(p.Get("ship")) ?? throw new InvalidOperationException("The content holds no ship design.");
        var layout = r.Generation >= Shared.World.WorldDescription.StructurePipelineGeneration ? r.Content.GetShipLayout(design.Layout) : null;
        var hull = layout is { Cells.Count: > 0 }
            ? WreckGenerator.GenerateFromLayout(design, layout, r.Seed, r.Content)
            : WreckGenerator.Generate(design, r.Seed, r.Content);
        var b = new BuiltStructure(Key, hull.Width, hull.Height, hull.Length, hull.FromLayout ? WreckGenerator.PlanetGroundRow : 0);
        BuiltConvert.CopyGrid(b, hull.Width, hull.Height, hull.Length, 0, r.AsInWorld ? hull.Get : hull.IntactAt,
            r.AsInWorld ? hull.ModifierAt : hull.IntactModifierAt, r.AsInWorld ? hull.ShapeAt : hull.IntactShapeAt);
        foreach (var m in hull.Markers)
        {
            b.Markers.Add(new BuiltMarker(m.Type, m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z));
        }

        return b;
    }
}

/// <summary>
/// A stone text tablet (<see cref="TextMonumentGenerator.Build"/>) in the server's blocks: a basalt slab, concrete letters,
/// gold for an accent (stone where a block is missing). Lines are separated by <c>/</c>. The lamp cells of the front step
/// (the stamper lets them glow) are <c>lamp</c> markers. The frame: x along the text, y up, z away from the reader.
/// </summary>
internal sealed class TextTabletSource : StructureSourceBase
{
    public const char LineSeparator = '/';

    public const string LampMarker = "lamp";

    private const string DefaultLines = "Blocks/Beyond";

    public TextTabletSource()
        : base("text_tablet", StructureSourceKind.Surface)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content) => new[]
    {
        StructureParam.Text("lines", DefaultLines),
    };

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var lines = SplitLines(p.Get("lines"));
        if (lines.Count == 0)
        {
            lines = SplitLines(DefaultLines);
        }

        ushort B(string key, string fallback) => (r.Content.GetBlock(key) ?? r.Content.GetBlock(fallback))?.NumericId.Value ?? 1;
        var tablet = TextMonumentGenerator.Build(lines, B("basalt", "stone"), B("concrete", "stone"), B("gold_block", "concrete"));
        var b = new BuiltStructure(Key, tablet.Width, tablet.Height, tablet.Depth);
        BuiltConvert.CopyGrid(b, tablet.Width, tablet.Height, tablet.Depth, 0, tablet.Get);
        for (int x = 0; x < tablet.Width; x++)
        {
            for (int y = 0; y < tablet.Height; y++)
            {
                for (int z = 0; z < tablet.Depth; z++)
                {
                    if (tablet.IsLamp(x, y, z))
                    {
                        b.Markers.Add(new BuiltMarker(LampMarker, x, y, z));
                    }
                }
            }
        }

        return b;
    }

    internal static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        foreach (string line in (text ?? string.Empty).Split(LineSeparator))
        {
            if (line.Trim().Length > 0)
            {
                lines.Add(line.Trim());
            }
        }

        return lines;
    }
}

/// <summary>
/// A giant building (<see cref="LandmarkGenerator.Build"/>) from a landmark template, turned by quarter turns. As it stands
/// in a world it is a ruin when its template says so (or as the request forces it); intact it is never ruined. The ground
/// row is the template's; the cells its underground part carves are the cavity.
/// </summary>
internal sealed class LandmarkSource : StructureSourceBase
{
    public LandmarkSource()
        : base("landmark", StructureSourceKind.Surface, supportsIntact: true)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content)
    {
        var templates = new List<string>();
        foreach (var t in content.Landmarks)
        {
            if (!t.IsSource)
            {
                templates.Add(t.Key); // #3518: a generator entry is built by its own source, never through this one
            }
        }

        return new[]
        {
            StructureParam.Choice("template", templates),
            StructureParam.Choice("ruin", SourceOptions.Tri, SourceOptions.Auto),
            StructureParam.Int("turns", 0, 0, 3),
        };
    }

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var template = r.Content.LandmarkByKey(p.Get("template"))
                       ?? throw new InvalidOperationException("The content holds no landmark template (data/landmarks/).");
        bool? ruin = r.AsInWorld ? p.Tri("ruin") : false;
        var layout = LandmarkGenerator.Build(template, r.Seed, p.Int("turns"), r.Content, r.Surface, ruin);
        var s = layout.Structure;
        var b = new BuiltStructure(Key, s.Width, s.Height, s.Length, layout.GroundRow);
        BuiltConvert.CopyGrid(b, s.Width, s.Height, s.Length, 0, s.Get, s.GetModifier, s.GetShape);
        BuiltConvert.AddMarkers(b, s.Markers);
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < s.Height; y++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    if (layout.IsCavity(x, y, z))
                    {
                        b.Cavity.Add(b.Index(x, y, z));
                    }
                }
            }
        }

        return b;
    }
}
