// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// The built-in forms a player may choose for a material, in picker order, with their localized name keys —
/// the one list behind the in-game Shape action, the hotbar's form menu and the build editors' form picker
/// (#1975). The two bed halves are deliberately absent: the server stamps them on a placed bed, they are never
/// chosen (#1846). The bench sits at the top of the index range, so it comes last.
/// </summary>
public static class BuiltInForms
{
    /// <summary>One pickable form: its shape index and the <c>ui.shape.*</c> key of its name.</summary>
    public readonly struct Option
    {
        public Option(int shape, string locKey)
        {
            Shape = shape;
            LocKey = locKey;
        }

        public int Shape { get; }

        public string LocKey { get; }
    }

    /// <summary>Every pickable built-in form, cube first.</summary>
    public static readonly IReadOnlyList<Option> Options = new[]
    {
        new Option((int)BlockShape.Cube, "ui.shape.cube"),
        new Option((int)BlockShape.Slab, "ui.shape.slab"),
        new Option((int)BlockShape.Pyramid, "ui.shape.pyramid"),
        new Option((int)BlockShape.Dome, "ui.shape.dome"),
        new Option((int)BlockShape.Sphere, "ui.shape.sphere"),
        new Option((int)BlockShape.Ramp, "ui.shape.ramp"),
        new Option((int)BlockShape.Stairs, "ui.shape.stairs"),
        new Option((int)BlockShape.Cone, "ui.shape.cone"),
        new Option((int)BlockShape.Cylinder, "ui.shape.cylinder"),
        new Option((int)BlockShape.Panel, "ui.shape.panel"),
        new Option((int)BlockShape.Post, "ui.shape.post"),
        new Option((int)BlockShape.Beam, "ui.shape.beam"),
        new Option((int)BlockShape.LowRamp, "ui.shape.lowramp"),
        new Option((int)BlockShape.QuarterCube, "ui.shape.quartercube"),
        new Option((int)BlockShape.Table, "ui.shape.table"),
        new Option((int)BlockShape.Chair, "ui.shape.chair"),
        new Option((int)BlockShape.Fence, "ui.shape.fence"),
        new Option((int)BlockShape.Sheet, "ui.shape.sheet"),
        new Option((int)BlockShape.Pot, "ui.shape.pot"),
        new Option((int)BlockShape.Bench, "ui.shape.bench"),
    };

    /// <summary>The name key of a form, or null when the form is not one a player picks (the bed halves, a
    /// player-designed form).</summary>
    public static string? LocKeyOf(int shape)
    {
        for (int i = 0; i < Options.Count; i++)
        {
            if (Options[i].Shape == shape)
            {
                return Options[i].LocKey;
            }
        }

        return null;
    }
}
