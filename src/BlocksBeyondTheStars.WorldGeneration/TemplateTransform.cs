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

    // ------------------------------------------------------------------ mirroring (plan revision 2)

    /// <summary>
    /// Whether <see cref="MirrorX"/> can mirror the template without changing how any of its cells looks or works: every
    /// shaped block is a built-in form that is its own mirror image (every form but the corner cube, whose corner has no
    /// mirror in the descriptor), carries no paint design and is no cell of a larger form, a form that points somewhere
    /// (stairs, a chair, a bed half …) stands upright, and no cell is a docking port (a station module docks by its ports'
    /// faces). A template that fails is simply never mirrored.
    /// </summary>
    public static bool CanMirror(StructureTemplate t)
    {
        foreach (var c in t.Cells)
        {
            if (c.Kind != "block")
            {
                continue;
            }

            if (c.Port.Length > 0)
            {
                return false;
            }

            if (c.Shape != 0 && !CanMirrorShape(c.Shape))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether one shape descriptor has a mirror image <see cref="MirrorShape"/> can name.</summary>
    public static bool CanMirrorShape(int descriptor)
    {
        if (descriptor == 0)
        {
            return true;
        }

        int shape = ShapeCode.ShapeOf(descriptor);
        if (!ShapeCode.IsBuiltIn(shape) || shape == (int)BlockShape.QuarterCube
            || ShapeCode.DesignOf(descriptor) != 0 || ShapeCode.CellOf(descriptor) != 0)
        {
            return false;
        }

        // A form that points along its yaw keeps a simple mirror only while it stands upright: tipped onto a wall or the
        // ceiling its yaw turns about another axis, and the mirror of that is not a yaw of the same descriptor.
        return !PointsAlongYaw(shape) || ShapeCode.UpFaceOf(descriptor) == ShapeCode.UpPlusY;
    }

    /// <summary>The forms whose look depends on the yaw (the others are the same from every side).</summary>
    private static bool PointsAlongYaw(int shape) => (BlockShape)shape is BlockShape.Ramp or BlockShape.Stairs or BlockShape.Beam
        or BlockShape.LowRamp or BlockShape.Chair or BlockShape.Fence or BlockShape.BedFoot or BlockShape.BedHead or BlockShape.Bench;

    /// <summary>The shape descriptor mirrored left to right (x → −x): a yaw toward −X points toward +X and back, an up-face
    /// (or a cube's front) on ±X changes sides; everything else stays. Only for descriptors <see cref="CanMirrorShape"/>
    /// accepts.</summary>
    public static int MirrorShape(int descriptor)
    {
        if (descriptor == 0)
        {
            return 0;
        }

        int yaw = ShapeCode.OrientationOf(descriptor);
        yaw = yaw switch { 1 => 3, 3 => 1, _ => yaw };
        int up = ShapeCode.UpFaceOf(descriptor);
        up = up switch { 2 => 3, 3 => 2, _ => up };
        return ShapeCode.Pack(ShapeCode.ShapeOf(descriptor), yaw, up);
    }

    /// <summary>
    /// A copy of <paramref name="t"/> mirrored left to right: a cell (x, z) → (W − 1 − x, z), the footprint and the front
    /// (the −Z wall with its door) stay where they are — the same building with its door, its stairs and its rooms on the
    /// other side. Shapes are mirrored with <see cref="MirrorShape"/>, markers ride along. Mirroring twice gives the input
    /// back. Call it only for a template <see cref="CanMirror"/> accepts.
    /// </summary>
    public static StructureTemplate MirrorX(StructureTemplate t)
    {
        var r = Header(t, t.Width, t.Length);
        foreach (var c in t.Cells)
        {
            r.Cells.Add(new TemplateCell
            {
                X = t.Width - 1 - c.X,
                Y = c.Y,
                Z = c.Z,
                Kind = c.Kind,
                Id = c.Id,
                Tint = c.Tint,
                Glow = c.Glow,
                Shape = c.Kind == "block" ? MirrorShape(c.Shape) : c.Shape,
                Port = c.Port,
                Mode = c.Mode,
                Config = c.Config,
                Label = c.Label,
                Keep = c.Keep,
            });
        }

        return r;
    }

    /// <summary>The header of a transformed copy: everything a composer reads from a module, with the new footprint.</summary>
    private static StructureTemplate Header(StructureTemplate t, int width, int length) => new()
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
        Width = width,
        Height = t.Height,
        Length = length,
    };

    private static StructureTemplate QuarterTurn(StructureTemplate t)
    {
        int w = t.Width, l = t.Length;
        var r = Header(t, l, w);

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
                Mode = c.Mode,     // #2260: a pre-built device keeps its settings; its horizontal direction turns with the
                Config = c.Config, // front in Shape, an up / down direction (yaw=4/5 in Config) needs no turn
                Label = c.Label,
                Keep = c.Keep,     // #3519: a turned settlement template keeps what its ruin pass must leave standing
            });
        }

        return r;
    }
}
