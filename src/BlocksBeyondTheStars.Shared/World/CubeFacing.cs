// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// Which side of a CUBE block is its front (#2124) — the vending machine's screen, the forge's fire hole, the
/// watcher's eye. Pure data, shared by the server (placement stores it), the client mesher (which tile each face
/// gets) and the placement ghost (which way the front will look).
/// <para>
/// <b>Where it is stored.</b> A cube has no use for the descriptor's up-face field (a cube tipped over is still a
/// cube), so a block with a front keeps WHICH FACE is its front there: 2..5 = +X, −X, +Z, −Z — the
/// <see cref="ShapeCode.FaceDirection"/> / mesher face indices. The shape field stays 0, so every
/// <see cref="ShapeCode.IsCube"/> check keeps treating the cell as the plain cube it is (culling, collision, air,
/// support, the mined drop — which keeps only the shape index and so drops the front for free). A descriptor of 0
/// means "no front stored": world generation and every block placed before #2124 write 0, so nothing about a
/// generated world changes; the mesher derives a front for those from the neighbours instead
/// (<see cref="DeriveFront"/>). A stored yaw would not do: yaw 0 is a real direction and would be
/// indistinguishable from "none". Rotating a template (<c>TemplateTransform.TurnShape</c>) turns the up-face field,
/// so a stored front turns with the building.
/// </para>
/// </summary>
public static class CubeFacing
{
    /// <summary><see cref="Definitions.BlockDefinition.Facing"/>: the front looks back at the player who places it.</summary>
    public const string Toward = "toward";

    /// <summary><see cref="Definitions.BlockDefinition.Facing"/>: the front looks the way the player looked.</summary>
    public const string Away = "away";

    /// <summary>True for a valid <see cref="Definitions.BlockDefinition.Facing"/> value (null = no front).</summary>
    public static bool IsValidMode(string? facing) => facing is null or Toward or Away;

    /// <summary>True when <paramref name="face"/> is one of the four horizontal faces (2..5) a front can be.</summary>
    public static bool IsFrontFace(int face) => face is >= 2 and <= 5;

    /// <summary>The descriptor of a cube whose front is <paramref name="frontFace"/> (2..5); 0 for anything else.</summary>
    public static int Pack(int frontFace) => IsFrontFace(frontFace) ? ShapeCode.Pack(0, 0, frontFace) : 0;

    /// <summary>The front face (2..5) stored in a cube descriptor, or -1 when none is stored — a plain 0, a form, or
    /// an up-face that names no horizontal face.</summary>
    public static int FrontOf(int descriptor)
    {
        if (!ShapeCode.IsCube(descriptor))
        {
            return -1;
        }

        int face = ShapeCode.UpFaceOf(descriptor);
        return IsFrontFace(face) ? face : -1;
    }

    /// <summary>The face index (2..5) pointing along a placement heading — the quarter-turn index placement derives
    /// from the player's look (0 = +Z, 1 = +X, 2 = −Z, 3 = −X, the Unity euler convention).</summary>
    public static int FaceAlongHeading(int heading) => (heading & 3) switch
    {
        0 => 4,
        1 => 2,
        2 => 5,
        _ => 3,
    };

    /// <summary>The heading index (0 = +Z, 1 = +X, 2 = −Z, 3 = −X) a horizontal face points along, or -1.</summary>
    public static int HeadingOfFace(int face) => face switch
    {
        4 => 0,
        2 => 1,
        5 => 2,
        3 => 3,
        _ => -1,
    };

    /// <summary>The face opposite a horizontal face (+X ↔ −X, +Z ↔ −Z); anything else comes back unchanged.</summary>
    public static int Opposite(int face) => face switch
    {
        2 => 3,
        3 => 2,
        4 => 5,
        5 => 4,
        _ => face,
    };

    /// <summary>The heading index of a player's look yaw in degrees (the same rounding every placement uses).</summary>
    public static int HeadingOfYaw(float yawDegrees) => ((int)System.MathF.Round(yawDegrees / 90f)) & 3;

    /// <summary>The front an automatic placement gives a block: <see cref="Toward"/> looks back at the player (the
    /// face opposite the look), <see cref="Away"/> the way the player looks.</summary>
    public static int FrontForHeading(string? facing, int heading)
        => facing == Away ? FaceAlongHeading(heading) : FaceAlongHeading(heading + 2);

    /// <summary>
    /// The front a placement stores. An explicit quarter turn in the intent (0..3 — the rotate key, or the client's own
    /// Auto answer) stands in for the player's look heading; -1 uses the look yaw the server has. Both then go through
    /// <see cref="FrontForHeading"/>. Treating the turn as a HEADING (0 = +Z, 1 = +X, …) rather than as a form's
    /// geometry yaw is deliberate: it is the convention the Crystal Net's gates and watchers already store
    /// (<c>CrystalNetRules.OutputFace</c>), so one number means the same direction to the drawn front and to the wire,
    /// and the ±X mirror between the two conventions (<see cref="ShapeCode.YawFacingForward"/>) never comes up.
    /// </summary>
    public static int FrontForPlacement(string? facing, int intentYaw, float playerYawDegrees)
        => FrontForHeading(facing, intentYaw is >= 0 and <= 3 ? intentYaw : HeadingOfYaw(playerYawDegrees));

    /// <summary>The heading the player "looked along" for a block with this front — the inverse of
    /// <see cref="FrontForHeading"/>. A Crystal Net gate or watcher keeps its signal direction in that convention
    /// (<c>CrystalNetRules.OutputFace</c>); the client sends it as the intent's quarter turn, and the ghost turns the
    /// front with it.</summary>
    public static int LookHeadingOf(string? facing, int frontFace)
    {
        int heading = HeadingOfFace(facing == Away ? frontFace : Opposite(frontFace));
        return heading < 0 ? 0 : heading;
    }

    /// <summary>Which texture side face <paramref name="face"/> (0..5, the mesher's order +Y, −Y, +X, −X, +Z, −Z) of a
    /// cube shows when its front is <paramref name="frontFace"/> (-1 = no front: all four sides alike).</summary>
    public static FaceSide SideOf(int face, int frontFace) => face switch
    {
        0 => FaceSide.Top,
        1 => FaceSide.Bottom,
        _ => face == frontFace ? FaceSide.Front : FaceSide.Side,
    };

    /// <summary>
    /// A front for a block that has none stored (world generation, blocks placed before #2124): the side that looks
    /// into the open, away from the wall the block stands against — a vendor against a station wall faces the room.
    /// A side that is open with a wall behind it wins, then any open side, in the fixed order +Z, +X, −Z, −X, so every
    /// client derives the same front; no open side at all (nothing to see) gives +Z. The flags say whether the cell
    /// beyond that face leaves the face visible (air, glass, a plant).
    /// </summary>
    public static int DeriveFront(bool openPlusX, bool openMinusX, bool openPlusZ, bool openMinusZ)
    {
        int best = 4, bestScore = -1;
        for (int heading = 0; heading < 4; heading++)
        {
            int face = FaceAlongHeading(heading);
            bool open = IsOpen(face), behind = IsOpen(Opposite(face));
            int score = open ? (behind ? 1 : 2) : 0;
            if (score > bestScore)
            {
                best = face;
                bestScore = score;
            }
        }

        return best;

        bool IsOpen(int face) => face switch
        {
            2 => openPlusX,
            3 => openMinusX,
            4 => openPlusZ,
            _ => openMinusZ,
        };
    }

    /// <summary>Quarter turns (the mesher's top-face UV rotation, 0..3) that stand a TOP picture upright for someone
    /// in front of the block: the picture's upper edge points away from the front. 0 when the front is −Z, which is
    /// how the unturned top face is mapped (upper edge toward +Z).</summary>
    public static int TopUvTurns(int frontFace) => frontFace switch
    {
        2 => 1,
        4 => 2,
        3 => 3,
        _ => 0,
    };
}
