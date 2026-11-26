// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #4026: the battleship's and the ark's rugs stood in their cabins as full carpet cubes — the ship scripts wrote
/// <c>Mat("rug")</c> without a shape, and the landmark stamp places a cell's shape exactly as the template gives it (it
/// never applies <see cref="PropShapes.DefaultPlaceShape"/>, which a player's placement does). This test reads the shipped
/// giant ships and landmarks: a prop with a placement form of its own (rug → sheet, flower pot → pot, stretcher → table, …)
/// never sits in a template as a plain cube, except for the few cells listed in <see cref="Exceptions"/>.
/// </summary>
public sealed class TemplatePropShapeTests
{
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    /// <summary>The (template key, block key) pairs allowed to carry a prop as a plain cube on purpose.</summary>
    private static readonly HashSet<(string Template, string Block)> Exceptions = new()
    {
        // The carrier's rec room lays its rug INTO the floor row (tools/giant_ships/carrier.py `_rec`): an inlaid
        // carpet, flush with the floor around it — a sheet there would leave a hole under a thin plate.
        ("carrier", "rug"),
    };

    /// <summary>Block keys exempt in every template: a cube bed is the giant wrecks' authored "short bed" marker, which the
    /// abandoned-bed pass (<c>AbandonedBeds.Apply</c>, docs/developer/GIANT_SHIPS.md §4) turns into a two-cell bed laid out
    /// in the room — the script cannot know which way the foot fits. Old worlds keep the cube on purpose (AbandonedBedTests).
    /// A cube ladder is drawn by its key: the mesher hangs its plate on the first wall beside it (#803,
    /// <see cref="PropShapes.DeriveLadderMount"/>'s heuristic), so a template ladder never shows as a cube.</summary>
    private static readonly HashSet<string> AnyTemplate = new(StringComparer.Ordinal) { "bed", "ladder" };

    private static IEnumerable<StructureTemplate> Templates()
        => Shipped.Value.GiantShips.Concat(Shipped.Value.Landmarks);

    [Fact]
    public void NoShippedGiantShipOrLandmark_PlacesAPropWithAFormOfItsOwn_AsAPlainCube()
    {
        var offenders = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int props = 0;
        foreach (var t in Templates())
        {
            TemplateBody.ForEachPlain(t, p =>
            {
                if (p.Cavity || p.Air || string.IsNullOrEmpty(p.Id) || PropShapes.DefaultPlaceShape(p.Id) == 0)
                {
                    return;
                }

                props++;
                if (ShapeCode.ShapeOf(p.Shape) == 0 && !AnyTemplate.Contains(p.Id) && !Exceptions.Contains((t.Key, p.Id)))
                {
                    string key = $"{t.Key}: {p.Id}";
                    offenders[key] = offenders.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            });
        }

        Assert.True(props > 0, "no prop at all in the shipped templates — the scan reads nothing");
        Assert.True(offenders.Count == 0,
            "props stamped as plain cubes (give them their place shape in the generator script and regenerate): "
            + string.Join(", ", offenders.Select(kv => $"{kv.Key} ×{kv.Value}")));
    }

    [Fact]
    public void TheBattleshipsAndTheArksRugs_LieFlat_AndTheirFlowerPotsArePots()
    {
        foreach (string key in new[] { "battleship", "ark" })
        {
            var ship = Shipped.Value.GetGiantShip(key);
            Assert.NotNull(ship);
            var rugs = new List<int>();
            TemplateBody.ForEachPlain(ship!, p =>
            {
                if (p.Id == "rug")
                {
                    rugs.Add(ShapeCode.ShapeOf(p.Shape));
                }
            });
            Assert.NotEmpty(rugs);
            Assert.All(rugs, s => Assert.Equal((int)BlockShape.Sheet, s));
        }

        var pots = new List<int>();
        TemplateBody.ForEachPlain(Shipped.Value.GetGiantShip("ark")!, p =>
        {
            if (p.Id == "flower_pot")
            {
                pots.Add(ShapeCode.ShapeOf(p.Shape));
            }
        });
        Assert.NotEmpty(pots);
        Assert.All(pots, s => Assert.Equal((int)BlockShape.Pot, s));
    }

    [Fact]
    public void TheExceptionList_StillNamesSomethingReal()
    {
        foreach (var (template, block) in Exceptions)
        {
            var t = Templates().FirstOrDefault(x => x.Key == template);
            Assert.NotNull(t);
            bool found = false;
            TemplateBody.ForEachPlain(t!, p => found |= p.Id == block && ShapeCode.ShapeOf(p.Shape) == 0);
            Assert.True(found, $"exception ({template}, {block}) no longer matches any cube cell — drop it from the list");
        }
    }
}
