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
    QuarterCube = 13, // small 0.5³ cube in a cell corner (micro-detail; full orientation reaches all 8 corners)

    // Furniture forms (#805): every shapeable material can be formed into these, so a wooden and an
    // iron table are the SAME shape on different materials — no per-material block ids needed.
    Table = 14,   // full-cell top plate on four corner legs
    Chair = 15,   // seat + backrest (backrest toward +Z, yaw-oriented like Ramp/Stairs)
    Fence = 16,   // two posts + two full-width rails along X (yaw-oriented like Beam); rails meet across cells
    Sheet = 17,   // ultra-thin 1/16 plate (rugs, veneers) — the Panel's little sibling
    Pot = 18,     // small centred planter box with a rim (bowls, pots)
}

/// <summary>How much of the orientation a prop's rotate-key cycle may reach (#909). The cycle the player
/// walks must promise exactly what the placement will honour — offering a tip the server pins back to +Y,
/// or four quarter turns of a square plate nobody can tell apart, is worse than offering nothing.</summary>
public enum PropOrientation : byte
{
    /// <summary>Not a stamped prop — the item places a plain cube (or no block at all).</summary>
    None = 0,

    /// <summary>Quarter turns only; the up-face stays +Y (bed/campfire/rug/pot — a tipped bed would break
    /// its sit/heal/warmth checks).</summary>
    YawOnly = 1,

    /// <summary>The full 24 orientations, exactly like a shaped building block (the crafted staircase).</summary>
    Full = 2,

    /// <summary>The ladder's own five states: the four walls it can hug, plus free-standing. Yaw is
    /// meaningless (its plate is a square <see cref="BlockShape.Panel"/>) and the two vertical up-faces are
    /// the two a ladder has no use for.</summary>
    LadderMount = 3,
}

/// <summary>
/// The default FORM a prop block is stamped with on placement (#804/#807/#809, #909), so it reads as its
/// silhouette without the player using the Shape action. Shared between the server (stamp on place, strip
/// from the mined drop) and the client (the rotate key + placement ghost must treat these items as
/// rotatable even though their item key carries no shape suffix).
/// </summary>
public static class PropShapes
{
    /// <summary>The default shape index for a prop block key, or 0 (plain cube) for everything else.
    /// The ladder's default is its wall plate; see <see cref="LadderFreeStanding"/> for the other form it
    /// can be stamped with.</summary>
    public static int DefaultPlaceShape(string blockKey) => blockKey switch
    {
        "bed" => (int)BlockShape.Slab,
        "campfire" => (int)BlockShape.Slab,
        "rug" => (int)BlockShape.Sheet,
        "flower_pot" => (int)BlockShape.Pot,
        "ladder" => (int)BlockShape.Panel,     // thin plate hugging a wall (#803 meshed this, #909 stores it)
        "stairs" => (int)BlockShape.Stairs,    // the crafted staircase used to place as a full cube (#909)
        _ => 0,
    };

    /// <summary>The form a ladder takes when it hugs no wall: a slim pole through the cell. The mesher has
    /// always drawn a free-standing ladder this way; since #909 the choice can also be stored.</summary>
    public const int LadderFreeStanding = (int)BlockShape.Post;

    /// <summary>How far this prop's placement orientation may be steered.</summary>
    public static PropOrientation OrientationOf(string blockKey) => blockKey switch
    {
        "ladder" => PropOrientation.LadderMount,
        "stairs" => PropOrientation.Full,
        _ => DefaultPlaceShape(blockKey) != 0 ? PropOrientation.YawOnly : PropOrientation.None,
    };

    /// <summary>True when <paramref name="shape"/> is a form the SERVER stamps on this block key rather than
    /// player data. Such a form is stripped from the mined drop so it stacks with freshly crafted items —
    /// which matters most for the ladder, whose two forms (wall plate / free-standing pole) would otherwise
    /// split the stack into two item keys that both place as a plate anyway.</summary>
    public static bool IsStampedForm(string blockKey, int shape)
        => shape != 0
           && (shape == DefaultPlaceShape(blockKey)
               || (shape == LadderFreeStanding && blockKey == "ladder"));

    /// <summary>The form + up-face a ladder is stamped with for a chosen mount face: an up-face pointing away
    /// from one of the four walls keeps the plate, anything else (no wall to hug) becomes the pole. Server and
    /// client run this same function so the placement ghost cannot promise the wrong silhouette.</summary>
    public static (int Shape, int UpFace) LadderForm(int upFace)
        => upFace >= 2 && upFace <= 5
            ? (DefaultPlaceShape("ladder"), upFace)
            : (LadderFreeStanding, ShapeCode.UpPlusY);

    /// <summary>
    /// Picks the wall a ladder hugs when the player let placement decide: the wall they aimed at wins
    /// (<paramref name="clickedFace"/>, -1 = none), otherwise the first one in <see cref="ShapeCode.WallFaces"/>
    /// order — the heuristic the mesher has used since #803, kept so a ladder placed by an old client, by
    /// worldgen or inside a ship layout lands exactly where it always did. Returns <see cref="ShapeCode.UpPlusY"/>
    /// when there is no wall at all, which <see cref="LadderForm"/> turns into the free-standing pole.
    /// <paramref name="hasWall"/> answers "is the cell on the far side of this up-face a wall the plate can
    /// hang on" for one of the four horizontal up-faces.
    /// </summary>
    public static int DeriveLadderMount(System.Func<int, bool> hasWall, int clickedFace)
    {
        if (hasWall is null)
        {
            return ShapeCode.UpPlusY;
        }

        if (clickedFace >= 2 && clickedFace <= 5 && hasWall(clickedFace))
        {
            return clickedFace;
        }

        foreach (int face in ShapeCode.WallFaces)
        {
            if (hasWall(face))
            {
                return face;
            }
        }

        return ShapeCode.UpPlusY;
    }
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
    public const int Count = 19;

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

    /// <summary>True when <paramref name="shapeIndex"/> names a real (non-cube) BUILT-IN shape we can build.
    /// Player-designed forms live above this range — see <see cref="IsCustomShape"/>.</summary>
    public static bool IsValidShape(int shapeIndex) => shapeIndex > 0 && shapeIndex < Count;

    // --- Player-designed forms (#842) ---
    // The shape field is 6 bits (0..63) and only 19 values are built-in, so the free indices ABOVE the enum
    // are handed out to player-designed forms registered per save (see CustomShape + the server registry).
    // A custom form therefore rides through crafting, the item key, placing, persistence and mining with no
    // format change whatsoever: it is just another shape index. Descriptor bits 27..31 stay reserved zero as
    // the escape hatch if 45 slots per save ever prove too few (widening them is additive, not a migration).

    /// <summary>First shape index handed out to player-designed forms (one past the built-in enum).</summary>
    public const int FirstCustom = Count;

    /// <summary>Last shape index the 6-bit descriptor field can hold.</summary>
    public const int LastCustom = 63;

    /// <summary>How many player-designed forms one save can hold at a time.</summary>
    public const int MaxCustomShapes = LastCustom - FirstCustom + 1;

    /// <summary>True when the index names a player-designed form rather than a built-in one. Says nothing
    /// about whether that form is actually REGISTERED in this save — ask the registry for that.</summary>
    public static bool IsCustomShape(int shapeIndex) => shapeIndex >= FirstCustom && shapeIndex <= LastCustom;

    /// <summary>True when the packed descriptor carries a player-designed form.</summary>
    public static bool IsCustomDescriptor(int descriptor) => IsCustomShape(ShapeOf(descriptor));

    /// <summary>True when <paramref name="shapeIndex"/> can be placed/crafted right now: either a built-in
    /// form, or a custom form that <paramref name="registered"/> confirms exists in this save. Passing a
    /// null predicate rejects every custom id (callers with no registry at hand see built-ins only).</summary>
    public static bool IsPlaceableShape(int shapeIndex, System.Func<int, bool>? registered)
        => IsValidShape(shapeIndex) || (IsCustomShape(shapeIndex) && registered is not null && registered(shapeIndex));

    /// <summary>True when <paramref name="upFace"/> is a valid up-face index (0..5).</summary>
    public static bool IsValidUpFace(int upFace) => upFace is >= 0 and <= 5;

    /// <summary>The unit direction an up-face points in — which is also the direction AWAY from the surface
    /// the shape's base rests on, so the supporting neighbour always sits at the opposite offset.</summary>
    public static (int X, int Y, int Z) FaceDirection(int upFace) => upFace switch
    {
        1 => (0, -1, 0),
        2 => (1, 0, 0),
        3 => (-1, 0, 0),
        4 => (0, 0, 1),
        5 => (0, 0, -1),
        _ => (0, 1, 0),
    };

    /// <summary>The up-face pointing along a unit direction, or -1 for anything that is not one of the six.
    /// Placement uses it to turn "the block face the player clicked" (the step from the aimed cell to the
    /// cell being filled) into the up-face that rests the shape on exactly that face.</summary>
    public static int FaceFromDirection(int dx, int dy, int dz) => (dx, dy, dz) switch
    {
        (0, 1, 0) => 0,
        (0, -1, 0) => 1,
        (1, 0, 0) => 2,
        (-1, 0, 0) => 3,
        (0, 0, 1) => 4,
        (0, 0, -1) => 5,
        _ => -1,
    };

    /// <summary>The four up-faces that lean a shape against a WALL, in the order placement falls back on
    /// when the player expressed no preference (unchanged from the ladder heuristic #803 shipped with).</summary>
    public static System.Collections.Generic.IReadOnlyList<int> WallFaces => WallFaceOrder;

    private static readonly int[] WallFaceOrder = { 2, 3, 4, 5 };

    // --- Paint design reference (bits 11..26) ---
    // Like the up-face field, the design id was added on top of the existing descriptor: bits 11+ were
    // always written as 0, so old saves/wire values read design 0 = "unpainted" → no migration. The id
    // references a world-level paint design (a 32×32 pixel bitmap in the paint_design registry); the
    // bitmap itself never travels per block.

    /// <summary>Largest paint design id that fits the descriptor field (16 bits).</summary>
    public const int MaxDesignId = 0xFFFF;

    /// <summary>The paint design id (0 = unpainted) encoded in a packed descriptor.</summary>
    public static int DesignOf(int descriptor) => (descriptor >> 11) & 0xFFFF;

    /// <summary>Returns the descriptor with its paint design id replaced (0 clears the paint).</summary>
    public static int WithDesign(int descriptor, int designId)
        => (descriptor & ~(0xFFFF << 11)) | ((designId & 0xFFFF) << 11);

    /// <summary>The descriptor with any paint design stripped (what a mined block's drop keeps).</summary>
    public static int WithoutDesign(int descriptor) => WithDesign(descriptor, 0);
}
