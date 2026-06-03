using System.Collections.Generic;

namespace Spacecraft.Shared.Definitions;

/// <summary>
/// A hand-designed station / settlement template, exported from the in-game structure editor and
/// merged into a pool (<c>data/station_templates.json</c> / <c>data/settlement_templates.json</c>).
/// World-gen may roll one of these instead of the procedural generator. Cells carry either a block
/// (<see cref="TemplateCell.Kind"/> = "block", <see cref="TemplateCell.Id"/> = a block key) or an
/// interaction marker ("marker", id = vendor / mission_board / hangar / heal_tank / quarters / npc …).
/// </summary>
public sealed class StructureTemplate
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = "medium";
    public string Kind { get; set; } = string.Empty; // "station" | "settlement" (informational)
    public int Width { get; set; }
    public int Height { get; set; }
    public int Length { get; set; }
    public List<TemplateCell> Cells { get; set; } = new();
}

/// <summary>One cell of a <see cref="StructureTemplate"/>: a block or an interaction marker.</summary>
public sealed class TemplateCell
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string Kind { get; set; } = "block"; // "block" | "marker"
    public string Id { get; set; } = string.Empty;
}
