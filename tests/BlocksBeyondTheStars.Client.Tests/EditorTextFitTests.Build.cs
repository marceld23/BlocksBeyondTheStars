// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The catalogue: the Station/Town editor (<c>StructureEditor.Ui.cs</c>), its kit panel, the Ship Designer, and the
/// panels both build editors share (palette, form picker, load picker, device panel).</summary>
public sealed partial class EditorTextFitTests
{
    /// <summary>The built-in forms' names (<c>BuiltInForms.Options</c>) — the brush's form value and the picker's captions.</summary>
    private static readonly string[] FormKeys =
    {
        "ui.shape.beam", "ui.shape.bedfoot", "ui.shape.bedhead", "ui.shape.bench", "ui.shape.chair", "ui.shape.cone", "ui.shape.cube",
        "ui.shape.cylinder", "ui.shape.dome", "ui.shape.fence", "ui.shape.lowramp", "ui.shape.panel", "ui.shape.post", "ui.shape.pot",
        "ui.shape.pyramid", "ui.shape.quartercube", "ui.shape.ramp", "ui.shape.sheet", "ui.shape.slab", "ui.shape.sphere", "ui.shape.stairs",
        "ui.shape.table",
    };

    /// <summary>The size tiers as a compact value (the metropolis by its short name).</summary>
    private static readonly string[] TierValueKeys =
    {
        "ui.tier.small", "ui.tier.medium", "ui.tier.large", "ui.tier.huge", "ui.tier.colossal", "ui.tier.hamlet", "ui.tier.village",
        "ui.tier.town", "ui.tier.city", "ui.tier_short.metropolis",
    };

    /// <summary>The Station/Village editor's own sites (EditorTextFitTests.Structure.cs, #3692), then the panels both build editors
    /// share (form picker, load picker, device panel, marker panel).</summary>
    private static IEnumerable<FitSite> StructureSites() => StructureEditorSites().Concat(new[]
    {
        Label("Form picker title", 600f, 18, "ui.ed.form_title"),
        // "Automatic (the block's own form)" where it fits, else "Auto" (EditorFormPicker.AutoCaption) — the short one must fit.
        Lines("Form picker captions", 136f, 34f, 15, 13, 13, FormKeys.Append("ui.ed.form_auto_short").ToArray()),
        Button("Form / load picker back", 600f, 44f, "ui.menu.back"),
        Label("Load picker title", 600f, 18, "ui.editor.load"),
        Label("Load picker empty", 560f, 15, "ui.ed.none"),
        Label("Load picker sections", 560f, 13, "ui.ed.sec_builtin_ships", "ui.ed.sec_builtin_templates", "ui.ed.sec_user_templates",
            "ui.ed.sec_user_ships", "ui.ed.sec_exports"),
        Label("Load picker cell count", 252f, 13, "ui.ed.cells") with { Sample = "88888" },
        // A picker without an editor shell asks itself, in the shell's words (an editor on the shell asks through it).
        Wrap("Load picker confirmation", 600f, 5, 16, "ui.editor.confirm_replace"),
        Button("Load picker confirm buttons", 294f, 44f, "ui.editor.confirm_replace_yes", "ui.menu.back"),

        Label("Device panel title", 916f, 22, "ui.ed.device.title") with { Sample = "Crystal Lamp" },
        Button("Device panel mode cells", 220f, 48f, "ui.crystal.mode.*.*", "ui.crystal.output_on", "ui.crystal.output_off"),
        Label("Device panel row labels", 360f, 18, "ui.ed.device.direction", "ui.crystal.name", "ui.ed.device.settings"),
        Button("Device panel direction", 300f, 48f, "ui.ed.device.dir.*", "ui.ed.device.dir_front"),
        Label("Device panel settings hint", 520f, 14, "ui.ed.device.settings_hint"),
        Button("Device panel apply", 300f, 48f, "ui.ed.device.apply"),
        Button("Device panel close", 220f, 48f, "ui.crystal.close"),

        // #3493 the marker settings panel (EditorMarkerPanel.cs, 980 wide): chest, vendor, sound, den.
        Label("Marker panel title", 916f, 22, "ui.ed.marker.title") with { Sample = "Animal den" },
        Lines("Marker panel row labels (360×48)", 360f, 48f, 18, 13, 13, "ui.ed.marker.items", "ui.ed.marker.table", "ui.ed.marker.chance",
            "ui.ed.marker.block", "ui.ed.marker.theme", "ui.ed.marker.name", "ui.ed.marker.sound", "ui.ed.marker.sound_pick",
            "ui.ed.marker.radius", "ui.ed.marker.volume", "ui.ed.marker.species", "ui.ed.marker.count",
            "ui.ed.marker.prop_pool", "ui.ed.marker.prop_template"), // #3646: a prop marker
        Button("Marker panel stepper values", 520f, 48f, "ui.ed.marker.table_default", "ui.ed.marker.species_native") with { Suffix = "  →" },
        Button("Marker panel toggles", 888f, 48f, "ui.ed.marker.hidden", "ui.ed.marker.inside") with { Prefix = "☐ " },
        Label("Marker panel settings line", 916f, 14, "ui.ed.marker.defaults"),
        Button("Marker panel apply", 300f, 48f, "ui.ed.device.apply"),
        Button("Device / marker panel close (icon)", 220f, 48f, "ui.crystal.close") with { TextX = 56f },
    });
}
