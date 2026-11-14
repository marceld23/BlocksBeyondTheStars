// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Tools;

/// <summary>The giant ship a <c>giantship --out</c> run writes: intact, or the planet / space wreck the server derives with
/// <see cref="GiantShipGenerator"/> (ruin revision 3) — always as a compact template, so the landmark preview renders it.</summary>
internal static class GiantShipWreckDump
{
    public static StructureTemplate Build(StructureTemplate t, GameContent content, string setting, long seed, int turns, bool atmosphere)
    {
        switch (setting)
        {
            case "intact":
                return t;

            case "planet":
                {
                    var layout = GiantShipGenerator.BuildPlanet(t, seed, turns, content, "grass", atmosphere);
                    var s = layout.Structure;
                    return ToTemplate(t, content, setting, seed, s.Width, s.Height, s.Length, s.Markers,
                        (x, y, z) => (s.Get(x, y, z), s.GetModifier(x, y, z), s.GetShape(x, y, z), layout.IsCavity(x, y, z)), layout.GroundRow);
                }

            case "space":
                {
                    var s = GiantShipGenerator.BuildSpace(t, seed, content);
                    return ToTemplate(t, content, setting, seed, s.Width, s.Height, s.Length, s.Markers,
                        (x, y, z) => (s.Get(x, y, z), s.GetModifier(x, y, z), s.GetShape(x, y, z), false), 0);
                }

            default:
                throw new ArgumentException($"Unknown setting '{setting}' (intact | planet | space).", nameof(setting));
        }
    }

    private static StructureTemplate ToTemplate(StructureTemplate source, GameContent content, string setting, long seed,
        int w, int h, int l, IReadOnlyList<SettlementMarker> markers,
        Func<int, int, int, (ushort Block, (int Tint, int Glow) Mod, int Shape, bool Cavity)> cell, int groundRow)
    {
        var t = new StructureTemplate
        {
            Key = $"{source.Key}_{setting}_{seed}",
            Name = $"{source.Name} ({setting} wreck, seed {seed})",
            GiantShip = true,
            Width = w,
            Height = h,
            Length = l,
            GroundRow = groundRow,
            Style = source.Style,
            PinOnly = true,
        };
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                for (int z = 0; z < l; z++)
                {
                    var (block, mod, shape, cavity) = cell(x, y, z);
                    if (block != 0 && content.BlockById(new BlockId(block)) is { } def)
                    {
                        t.Cells.Add(new TemplateCell { X = x, Y = y, Z = z, Kind = "block", Id = def.Key, Tint = mod.Tint, Glow = mod.Glow, Shape = shape });
                    }
                    else if (cavity)
                    {
                        t.Cells.Add(new TemplateCell { X = x, Y = y, Z = z, Kind = "cavity" });
                    }
                }
            }
        }

        foreach (var m in markers)
        {
            t.Cells.Add(new TemplateCell { X = m.LocalPos.X, Y = m.LocalPos.Y, Z = m.LocalPos.Z, Kind = "marker", Id = m.Type, Config = m.Data });
        }

        TemplateBody.Compact(t);
        return t;
    }
}
