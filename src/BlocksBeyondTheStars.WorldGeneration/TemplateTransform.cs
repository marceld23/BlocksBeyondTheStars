// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Turns a structure template about the vertical axis (#1873) so the station composer can dock a module whose
/// port faces the wrong way. One quarter turn maps a cell (x, z) → (L − 1 − z, x) and swaps width and length;
/// every shape descriptor turns with it — its yaw by one step (the mesher's own yaw order, see
/// <see cref="ShapeCode.YawDirection"/>: +Z → −X → −Z → +X) and a wall-mounted up-face the same way — so a bed
/// pair stays a pair, a chair keeps facing its table and a ladder keeps hugging its wall. Ports and markers ride
/// along. Pure and deterministic; four turns give the input back.
/// </summary>
public static class TemplateTransform
{
    /// <summary>A copy of <paramref name="t"/> turned by <paramref name="turns"/> quarter turns (0..3); 0 returns
    /// the same instance.</summary>
    public static StructureTemplate RotateY(StructureTemplate t, int turns)
    {
        turns = ((turns % 4) + 4) % 4;
        if (turns == 0)
        {
            return t;
        }

        var current = t;
        for (int i = 0; i < turns; i++)
        {
            current = QuarterTurn(current);
        }

        return current;
    }

    /// <summary>The up-face index after one quarter turn: +X → +Z → −X → −Z → +X; ±Y stay.</summary>
    public static int TurnUpFace(int upFace) => upFace switch
    {
        2 => 4, // +X → +Z
        4 => 3, // +Z → −X
        3 => 5, // −X → −Z
        5 => 2, // −Z → +X
        _ => upFace,
    };

    /// <summary>The shape descriptor after one quarter turn (0 stays 0).</summary>
    public static int TurnShape(int descriptor)
    {
        if (descriptor == 0)
        {
            return 0;
        }

        int shape = ShapeCode.ShapeOf(descriptor);
        int yaw = (ShapeCode.OrientationOf(descriptor) + 1) & 3;
        int up = TurnUpFace(ShapeCode.UpFaceOf(descriptor));
        return ShapeCode.Pack(shape, yaw, up);
    }

    private static StructureTemplate QuarterTurn(StructureTemplate t)
    {
        int w = t.Width, l = t.Length;
        var r = new StructureTemplate
        {
            Key = t.Key,
            Name = t.Name,
            Tier = t.Tier,
            Kind = t.Kind,
            Pack = t.Pack,
            Role = t.Role,
            Kit = t.Kit,
            Function = t.Function,
            PinOnly = t.PinOnly,
            Weight = t.Weight,
            PlanetTypes = new List<string>(t.PlanetTypes),
            LegacyPool = t.LegacyPool,
            Width = l,
            Height = t.Height,
            Length = w,
        };

        foreach (var c in t.Cells)
        {
            r.Cells.Add(new TemplateCell
            {
                X = l - 1 - c.Z,
                Y = c.Y,
                Z = c.X,
                Kind = c.Kind,
                Id = c.Id,
                Tint = c.Tint,
                Glow = c.Glow,
                Shape = c.Kind == "block" ? TurnShape(c.Shape) : c.Shape,
                Port = c.Port,
            });
        }

        return r;
    }
}
