// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client docks with and boards a nearby space station from the current space instance.</summary>
public sealed class BoardStationIntent
{
    public string StationId { get; set; } = string.Empty;
}

/// <summary>Client leaves the currently boarded station and returns to the ship.</summary>
public sealed class LeaveStationIntent { }

/// <summary>Client → server (#1842): the boarder of a player-built station switches zero-g construction mode on
/// (<c>Enabled</c> = true) or off for THEMSELVES — the suit floats everywhere on the station, so the outer hull
/// can be built from any side without stepping off the deck. Per player, session-only (never persisted); the
/// server ignores it while the player is not on a player station.</summary>
public sealed class SetStationZeroGIntent
{
    public bool Enabled { get; set; }
}

/// <summary>Client repairs one damaged/missing wreck hull cell with a matching block item.</summary>
public sealed class RepairWreckIntent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string ItemKey { get; set; } = string.Empty;
}

/// <summary>Client claims a fully repaired wreck into the owned ship fleet.</summary>
public sealed class ClaimWreckIntent { }

/// <summary>The player's own ship as a voxel structure to render in the flight view (item 20, S1).
/// Seeded server-side from the ship-editor design and sent on entering space (and on ship switch); the
/// client meshes it 1:1 with the chunk mesher and uses it in place of the hand-built cube ship. The block
/// grid is sparse — parallel arrays of cell coordinates + the block id at each (only non-air cells).</summary>
public sealed class SpaceShipDesign
{
    /// <summary>Structure id (e.g. "ship:&lt;playerId&gt;" or an asteroid's entity id).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Structure kind: "ship" (the player's own ship — rides the live flight pose) or "asteroid" (a
    /// static voxel body at <see cref="PosX"/>/<see cref="PosY"/>/<see cref="PosZ"/>) — item 20 S3.</summary>
    public string Kind { get; set; } = "ship";

    /// <summary>World position in the flight scene for a static structure (asteroid). Ignored for the own ship.</summary>
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }

    /// <summary>Design bounding-box size in blocks (for centring the mesh on the structure pivot).</summary>
    public int Width { get; set; }
    public int Height { get; set; }
    public int Length { get; set; }

    /// <summary>Per-cell block coordinates (design-local) and the block id placed there. Same length.</summary>
    public int[] X { get; set; } = System.Array.Empty<int>();
    public int[] Y { get; set; } = System.Array.Empty<int>();
    public int[] Z { get; set; } = System.Array.Empty<int>();
    public ushort[] Block { get; set; } = System.Array.Empty<ushort>();

    /// <summary>Per-cell dye/glow colour (0xRRGGBB; 0 = none) and packed shape+orientation (0 = cube),
    /// parallel to <see cref="Block"/>. Empty = no authored cell carries a modifier (the common case).</summary>
    public int[] Tint { get; set; } = System.Array.Empty<int>();
    public int[] Glow { get; set; } = System.Array.Empty<int>();
    public int[] Shape { get; set; } = System.Array.Empty<int>();

    /// <summary>#1917: a generated station hull's hangar mouth — its centre in design cell units (the same space as
    /// <see cref="X"/>/<see cref="Y"/>/<see cref="Z"/>; cell (x,y,z) spans x..x+1) and the horizontal direction pointing
    /// out of it. The flight view docks there. Contractless-additive: older payloads leave <see cref="HasDock"/> false.</summary>
    public bool HasDock { get; set; }
    public float DockX { get; set; }
    public float DockY { get; set; }
    public float DockZ { get; set; }
    public int DockOutX { get; set; }
    public int DockOutZ { get; set; }

    /// <summary>#2119: the doors the owner built into this ship (design-local doorway base cells, parallel arrays):
    /// the kind ("slide"/"hinge"/"wood"/"energy") and whether the leaf runs along X. The flight view draws a closed
    /// door there — the cell itself is air, like every doorway; a closed energy door shows its blue field in space
    /// (#2607). #2607: a player station lists the doors built into it the same way (on a spacewalk or aboard). The
    /// design's own doors are not listed (the hatch keeps its energy field, see <see cref="HatchX"/>).
    /// Contractless-additive: older payloads carry none.</summary>
    public int[] DoorX { get; set; } = System.Array.Empty<int>();
    public int[] DoorY { get; set; } = System.Array.Empty<int>();
    public int[] DoorZ { get; set; } = System.Array.Empty<int>();
    public string[] DoorKind { get; set; } = System.Array.Empty<string>();
    public bool[] DoorAxisX { get; set; } = System.Array.Empty<bool>();

    /// <summary>#2587: the doorways of the design's OWN doors that open to space — an authored ship's rear hatch — as
    /// design-local cells (parallel arrays): every air cell such a door fills, its width × three high. The flight view
    /// caps exactly these with the hatch's energy panel instead of guessing the opening from the stern's cells, which
    /// leaves and blocks built around the hull used to throw off. The doors the owner built are in <see cref="DoorX"/>,
    /// never here. Contractless-additive: an older server sends none (the client then guesses), an older client ignores them.</summary>
    public int[] HatchX { get; set; } = System.Array.Empty<int>();
    public int[] HatchY { get; set; } = System.Array.Empty<int>();
    public int[] HatchZ { get; set; } = System.Array.Empty<int>();

    /// <summary>#2587: true when the server named the hatch cells above, so EMPTY hatch arrays mean "no doorway opens to
    /// space" (a room built behind the stern closed it in) and the client caps nothing — while an older server (false,
    /// the default of a payload without the field) leaves the client to guess. Contractless-additive.</summary>
    public bool HatchKnown { get; set; }

    /// <summary>#2607: the structure's owner — a player station's builder, a ship's pilot; empty for generated structures.
    /// The spacewalk's door preview shows only where the server lets the suit build (the owner and their allies).
    /// Contractless-additive: an older server sends none.</summary>
    public string OwnerId { get; set; } = string.Empty;
}

/// <summary>Server → client: a player's ship parked on the current world as a placed voxel OBJECT
/// (ship-as-object — the hull is no longer stamped into the world grid). Sent on world join (one per
/// parked ship), on landing/ship-switch (placed/replaced) and on launch (<see cref="Removed"/>). Cell
/// edits afterwards ride the existing <see cref="StructureBlockChanged"/> keyed by <see cref="StructureId"/>.</summary>
public sealed class LandedShipState
{
    /// <summary>The owning player.</summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>Structure id ("ship:&lt;playerId&gt;") — the same id structure edits use.</summary>
    public string StructureId { get; set; } = string.Empty;

    /// <summary>True = the ship left (launch/owner logout); the client despawns the object.</summary>
    public bool Removed { get; set; }

    /// <summary>World position of the structure-local origin (cell 0,0,0).</summary>
    public int OriginX { get; set; }
    public int OriginY { get; set; }
    public int OriginZ { get; set; }

    /// <summary>The owner's hull paint colour (0xRRGGBB; 0 = default steel).</summary>
    public int Hull { get; set; }

    /// <summary>Design bounding-box size in blocks.</summary>
    public int Width { get; set; }
    public int Height { get; set; }
    public int Length { get; set; }

    /// <summary>Per-cell block coordinates (structure-local) and the block id placed there. Same length.
    /// Empty when <see cref="Removed"/>.</summary>
    public int[] X { get; set; } = System.Array.Empty<int>();
    public int[] Y { get; set; } = System.Array.Empty<int>();
    public int[] Z { get; set; } = System.Array.Empty<int>();
    public ushort[] Block { get; set; } = System.Array.Empty<ushort>();

    /// <summary>Per-cell dye/glow colour (0xRRGGBB; 0 = none) and packed shape+orientation (0 = cube),
    /// parallel to <see cref="Block"/>. Empty array = no cell carries a modifier (the common case).</summary>
    public int[] Tint { get; set; } = System.Array.Empty<int>();
    public int[] Glow { get; set; } = System.Array.Empty<int>();
    public int[] Shape { get; set; } = System.Array.Empty<int>();
}

/// <summary>Client → server: place or mine one block on a voxel <see cref="SpaceShipDesign"/> structure while
/// on an EVA spacewalk (item 20, S2). Coordinates are design-local cells (the same space the structure was sent
/// in). The free-space analogue of <see cref="PlaceBlockIntent"/>/<see cref="MineBlockIntent"/>.</summary>
public sealed class StructureEditIntent
{
    public string StructureId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    /// <summary>True = mine the cell; false = place <see cref="ItemKey"/>'s block at it.</summary>
    public bool Mine { get; set; }

    /// <summary>The hotbar item whose block to place (ignored when mining).</summary>
    public string ItemKey { get; set; } = string.Empty;

    /// <summary>#1943: the orientation the placement ghost showed, exactly like <see cref="PlaceBlockIntent"/> —
    /// up-face 0..5 and yaw 0..3, −1 = "let the server decide". Added to an existing contractless MessagePack
    /// message: an older client leaves both at 0, so the server treats an unset pair as "decide" (see
    /// <c>StampStructurePropShape</c>). Without it every prop built into a ship stamped as a plain cube.</summary>
    public int UpFace { get; set; } = -1;
    public int Yaw { get; set; } = -1;

    /// <summary>#2268 (additive): a directional Crystal Net device's direction aboard, as <see cref="PlaceBlockIntent.DeviceDir"/>.</summary>
    public int DeviceDir { get; set; } = -1;
}

/// <summary>Client → server: deploy a station core in front of the suit to start a player-built station (item
/// 20, S4). No payload — the server places it from the suit's pose.</summary>
public sealed class DeployStationCoreIntent
{
}

/// <summary>Server → client: one cell of a voxel structure changed (item 20, S2) — the free-space analogue of
/// <see cref="BlockChanged"/>. Broadcast to everyone in the space instance so they re-mesh the structure.</summary>
public sealed class StructureBlockChanged
{
    public string StructureId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public ushort Block { get; set; } // 0 = air

    /// <summary>Per-cell dye / glow colour (0xRRGGBB; 0 = none) and packed shape+orientation (0 = cube),
    /// matching <see cref="BlockChanged"/>. Lets authored/edited ship cells carry colour + form.</summary>
    public int Tint { get; set; }
    public int Glow { get; set; }
    public int Shape { get; set; }
}

/// <summary>Server → client: mining progress on one cell of a voxel structure (#685) — the structure-space
/// analogue of <see cref="MiningProgress"/>. EVA asteroid mining obeys block hardness like planet mining, so
/// a hard ore block needs several drill hits; Fraction is 0..1 of the way to breaking. The cell stays until
/// it breaks (<see cref="StructureBlockChanged"/>). Coordinates are structure-local cells.</summary>
public sealed class StructureMiningProgress
{
    public string StructureId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public float Fraction { get; set; }
}

/// <summary>The player boarded a space station and is now walking inside its voxel interior.</summary>
public sealed class StationBoarded
{
    public string StationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

/// <summary>Repair progress for the currently stamped wreck.</summary>
public sealed class WreckRepairStatus
{
    public string WreckName { get; set; } = string.Empty;
    public string ShipType { get; set; } = string.Empty;
    public int Remaining { get; set; }
    public int Total { get; set; }
    public bool Claimable { get; set; }
    public bool Claimed { get; set; }

    /// <summary>Comma-separated distinct block keys still needed to repair the wreck (for the HUD hint).</summary>
    public string Needs { get; set; } = string.Empty;
}
