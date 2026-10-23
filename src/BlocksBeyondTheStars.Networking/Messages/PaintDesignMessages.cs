// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client paints (or clears) a pixel design onto a placed world block. The server dedups the
/// bitmap into the save-global paint-design registry and stamps the resulting design id into the block's
/// shape descriptor (bits 11+), which travels through the ordinary <see cref="BlockChanged"/> path — the
/// bitmap itself never repeats per block. Mirrors <see cref="SetFaceIntent"/>: the payload is a palette-index
/// hex string (32×32 = 1024 chars) the client owns, validated + throttled server-side.</summary>
public sealed class PaintBlockIntent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    /// <summary>The design as a 32×32 palette-index hex string; empty clears the paint from the block.</summary>
    public string Pixels { get; set; } = string.Empty;
}

/// <summary>Client crafts a player-designed FORM out of a held building material (#843). The paint sibling
/// of <see cref="ShapeCraftIntent"/>: the micro-voxel bitmap rides along, the server dedups it into the
/// save-global form registry and answers with the ordinary craft result — the crafted item then carries the
/// registered shape index in its key like any built-in form. Free 1:1, any shapeable material.</summary>
public sealed class CustomShapeCraftIntent
{
    /// <summary>Source material to re-form (base key, or an already coloured/shaped key).</summary>
    public string SourceItemKey { get; set; } = string.Empty;

    /// <summary>The form as a micro-voxel hex string (4³ = 64 or 8³ = 512 chars; see <c>CustomShape</c>).</summary>
    public string Voxels { get; set; } = string.Empty;

    /// <summary>The player's name for the form; only used when this bitmap is registered for the first time.</summary>
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; } = 1;

    /// <summary>Preferred output slot (hotbar action), or -1 for the legacy first-free-slot behaviour.</summary>
    public int Slot { get; set; } = -1;
}

/// <summary>
/// The "own texture" crafting action (hotbar Farbe → Meine Muster): apply a saved 32×32 paint design to a
/// held building material as an ITEM modifier, the item-key sibling of <see cref="PaintBlockIntent"/> (which
/// paints a placed block). The bitmap rides along; the server dedups it into the same save-global paint
/// registry and hands back the material carrying <c>p&lt;designId&gt;</c> in its key. Placing the item stamps
/// the design into the cell's shape descriptor; mining recovers it — the promise dye and form already make.
/// Free 1:1, tintable materials only. Empty pixels strip the design (back to the unpainted material).
/// </summary>
public sealed class PaintCraftIntent
{
    /// <summary>Source material (base key, or an already modified key — colour/shape are preserved).</summary>
    public string SourceItemKey { get; set; } = string.Empty;

    /// <summary>The design as a 32×32 palette-index hex string; empty removes the design from the item.</summary>
    public string Pixels { get; set; } = string.Empty;

    public int Count { get; set; } = 1;

    /// <summary>Preferred output slot (hotbar action), or -1 for the legacy first-free-slot behaviour.</summary>
    public int Slot { get; set; } = -1;
}

/// <summary>One paint design's bitmap, keyed by its save-global id (see <see cref="PaintBlockIntent"/>).
/// Sent to everyone when a design is first registered — before the <see cref="BlockChanged"/> that
/// references it, so the mesher never sees an unknown id from the live path. An EMPTY <c>Pixels</c> is a
/// moderation wipe: the client blanks the design everywhere it is placed.</summary>
public sealed class PaintDesignData
{
    public int Id { get; set; }

    /// <summary>The design as a 32×32 palette-index hex string; empty = wiped (render nothing).</summary>
    public string Pixels { get; set; } = string.Empty;

    /// <summary>Display name of whoever registered the design — shown when a player copies it off a block
    /// into their own library, so borrowed art keeps its attribution (#846).</summary>
    public string Owner { get; set; } = string.Empty;
}

/// <summary>Every registered paint design, pushed once on join BEFORE chunk streaming starts so painted
/// blocks in the first chunks resolve immediately. Parallel arrays, one entry per design.</summary>
public sealed class PaintDesignList
{
    public int[] Ids { get; set; } = System.Array.Empty<int>();
    public string[] Pixels { get; set; } = System.Array.Empty<string>();
    public string[] Owners { get; set; } = System.Array.Empty<string>();
}

/// <summary>One player-designed form, keyed by its save-global shape index (#843). Broadcast when a form is
/// first registered — before any block or item can reference it — so the mesher never meets an unknown form
/// on the live path. EMPTY <c>Voxels</c> is a moderation wipe: the id is released and every block holding it
/// falls back to a plain cube.</summary>
public sealed class CustomShapeData
{
    /// <summary>The shape index this form was registered under (19..63).</summary>
    public int Id { get; set; }

    /// <summary>The form as a micro-voxel hex string; empty = wiped.</summary>
    public string Voxels { get; set; } = string.Empty;

    /// <summary>The designer's name for the form (shown in the crafting menu and the tooltip).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Display name of whoever registered it, for attribution when someone copies it.</summary>
    public string Owner { get; set; } = string.Empty;
}

/// <summary>Every registered player-designed form, pushed once on join BEFORE chunk streaming starts so
/// blocks carrying a custom form in the first chunks mesh immediately. Parallel arrays.</summary>
public sealed class CustomShapeList
{
    public int[] Ids { get; set; } = System.Array.Empty<int>();
    public string[] Voxels { get; set; } = System.Array.Empty<string>();
    public string[] Names { get; set; } = System.Array.Empty<string>();
    public string[] Owners { get; set; } = System.Array.Empty<string>();
}
