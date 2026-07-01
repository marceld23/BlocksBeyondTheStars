// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// The geometric FORM a placed building block renders + collides as. Like the dye tint, a shape is a
/// per-voxel modifier (stored alongside the block id), not a separate block type — the same material can
/// be placed as a cube, a sphere, a ramp, … The crafted item carries only the shape index in its key
/// (e.g. <c>"stone#s05"</c>); the placement ORIENTATION is decided from the player's facing at place time
/// and stored together with the shape in the per-voxel descriptor (see <see cref="ShapeCode"/>).
/// <see cref="Cube"/> (0) is the default — an unshaped, ordinary full block.
/// </summary>
public enum BlockShape : byte
{
    Cube = 0,
    Slab = 1,     // bottom half-height box
    Pyramid = 2,  // square base tapering to an apex (flat side down)
    Dome = 3,     // half-sphere (flat side down)
    Sphere = 4,   // full ball centred in the cell
    Ramp = 5,     // wedge: a sloped face from floor on one edge up to full height on the opposite edge
    Stairs = 6,   // two-step staircase rising along the facing axis
    Cone = 7,     // circular base tapering to an apex
    Cylinder = 8, // circular column, full height
    Panel = 9,    // thin quarter-height plate (floor/ceiling trim)
    Post = 10,    // slim square column centred in the cell (pillars, railings)
    Beam = 11,    // horizontal square bar spanning the cell (structural frames), yaw-oriented
    LowRamp = 12, // half-height wedge — a gentle incline (yaw-oriented like Ramp)
}

/// <summary>
/// Packs/unpacks the per-voxel SHAPE DESCRIPTOR: a single int carrying the yaw orientation in bits 0..1,
/// the shape index in bits 2..7, and the "up-face" (which cube face the shape's local +Y points to, 0..5) in
/// bits 8..10. Stored in the chunk modifier store, persisted, and sent over the wire as one value, mirroring
/// how the dye tint travels as one int. Together up-face × yaw give the full 24 cube orientations.
/// BACKWARD-COMPATIBLE: the up-face field was added on top, so descriptors written before it read up-face 0 =
/// <see cref="UpPlusY"/> = the original +Y-up behaviour → no save/wire migration. Symmetric shapes (sphere,
/// dome, cube) ignore orientation entirely; ramps/stairs/wedges/corners use it.
/// </summary>
public static class ShapeCode
{
    /// <summary>Number of distinct <see cref="BlockShape"/> forms (including <see cref="BlockShape.Cube"/>).</summary>
    public const int Count = 13;

    /// <summary>The default up-face (local +Y points to world +Y): the original, pre-orientation behaviour.</summary>
    public const int UpPlusY = 0;

    /// <summary>Packs a shape index (0..63) + yaw (0..3) + up-face (0..5) into one stored descriptor.</summary>
    public static int Pack(int shape, int yaw, int upFace) => ((upFace & 0x7) << 8) | ((shape & 0x3F) << 2) | (yaw & 0x3);

    /// <summary>Packs a shape index (0..63) + a yaw orientation (0..3), up-face defaulting to +Y (compat overload).</summary>
    public static int Pack(int shape, int orientation) => Pack(shape, orientation, UpPlusY);

    /// <summary>Packs a shape + a yaw orientation (0..3), up-face +Y (compat overload).</summary>
    public static int Pack(BlockShape shape, int orientation) => Pack((int)shape, orientation, UpPlusY);

    /// <summary>Packs a shape + yaw + up-face.</summary>
    public static int Pack(BlockShape shape, int yaw, int upFace) => Pack((int)shape, yaw, upFace);

    /// <summary>The shape index (0 = cube) encoded in a packed descriptor.</summary>
    public static int ShapeOf(int descriptor) => (descriptor >> 2) & 0x3F;

    /// <summary>The yaw orientation (0..3) encoded in a packed descriptor.</summary>
    public static int OrientationOf(int descriptor) => descriptor & 0x3;

    /// <summary>The up-face (0..5, which world face local +Y points to) encoded in a packed descriptor.
    /// 0 = +Y (default), 1 = -Y, 2 = +X, 3 = -X, 4 = +Z, 5 = -Z — matching the mesher's face order.</summary>
    public static int UpFaceOf(int descriptor) => (descriptor >> 8) & 0x7;

    /// <summary>True when the descriptor is an ordinary full cube (no custom geometry).</summary>
    public static bool IsCube(int descriptor) => ShapeOf(descriptor) == 0;

    /// <summary>True when <paramref name="shapeIndex"/> names a real (non-cube) shape we can build.</summary>
    public static bool IsValidShape(int shapeIndex) => shapeIndex > 0 && shapeIndex < Count;

    /// <summary>True when <paramref name="upFace"/> is a valid up-face index (0..5).</summary>
    public static bool IsValidUpFace(int upFace) => upFace is >= 0 and <= 5;
}
