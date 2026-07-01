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
/// Packs/unpacks the per-voxel SHAPE DESCRIPTOR: a single int carrying the shape index in the high bits and
/// the yaw orientation (0..3 = the four cardinal facings) in the low two bits. Stored in the chunk modifier
/// store, persisted, and sent over the wire as one value, mirroring how the dye tint travels as one int.
/// Symmetric shapes (sphere, dome, pyramid, cone, cylinder) ignore the orientation; ramps + stairs use it.
/// </summary>
public static class ShapeCode
{
    /// <summary>Number of distinct <see cref="BlockShape"/> forms (including <see cref="BlockShape.Cube"/>).</summary>
    public const int Count = 13;

    /// <summary>Packs a shape index (0..63) + a yaw orientation (0..3) into one stored descriptor.</summary>
    public static int Pack(int shape, int orientation) => ((shape & 0x3F) << 2) | (orientation & 0x3);

    /// <summary>Packs a shape + a yaw orientation (0..3) into one stored descriptor.</summary>
    public static int Pack(BlockShape shape, int orientation) => Pack((int)shape, orientation);

    /// <summary>The shape index (0 = cube) encoded in a packed descriptor.</summary>
    public static int ShapeOf(int descriptor) => (descriptor >> 2) & 0x3F;

    /// <summary>The yaw orientation (0..3) encoded in a packed descriptor.</summary>
    public static int OrientationOf(int descriptor) => descriptor & 0x3;

    /// <summary>True when the descriptor is an ordinary full cube (no custom geometry).</summary>
    public static bool IsCube(int descriptor) => ShapeOf(descriptor) == 0;

    /// <summary>True when <paramref name="shapeIndex"/> names a real (non-cube) shape we can build.</summary>
    public static bool IsValidShape(int shapeIndex) => shapeIndex > 0 && shapeIndex < Count;
}
