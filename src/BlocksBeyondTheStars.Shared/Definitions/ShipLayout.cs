using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// A voxel ship blueprint produced by the in-game ship editor (see docs/SHIP_TYPE_EDITOR_PLAN.md),
/// loaded from <c>data/ship_layouts/&lt;key&gt;.json</c>. A <see cref="ShipDefinition"/> may reference one
/// via <see cref="ShipDefinition.Layout"/>; when present the server stamps this exact design (hull,
/// viewports, stations, hatch, lights, engine) instead of the parametric box.
/// </summary>
public sealed class ShipLayout
{
    /// <summary>Set from the file name on load (matches the ship's <c>Layout</c> reference).</summary>
    public string Key { get; set; } = string.Empty;

    public int Width { get; set; }
    public int Height { get; set; }
    public int Length { get; set; }

    public List<ShipLayoutCell> Cells { get; set; } = new();
}

/// <summary>One placed cell: position + what it is (a block, a station marker, or a special element).</summary>
public sealed class ShipLayoutCell
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    /// <summary>"block", "station" or "element".</summary>
    public string Kind { get; set; } = "block";

    /// <summary>The palette id: a block key (iron_wall/glass), a station type (cockpit/medbay/…) or an
    /// element (hatch/light/engine).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Per-cell dye colour (0xRRGGBB; 0 = none). Applied to tintable blocks like in-game dye.</summary>
    public int Tint { get; set; }

    /// <summary>Per-cell glow colour (0xRRGGBB; 0 = none) — the coloured-light blocks.</summary>
    public int Glow { get; set; }

    /// <summary>Packed shape + orientation (<c>ShapeCode.Pack(shape, facing)</c>; 0 = plain cube).</summary>
    public int Shape { get; set; }
}
