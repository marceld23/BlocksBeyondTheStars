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

    private static IEnumerable<FitSite> StructureSites() => new[]
    {
        Label("Palette title", 268f, 18, "ui.struct.palette", "ui.ship.palette"),
        Input("Palette search", 276f, 18, "ui.pal.search"),
        Label("Palette section headers", 250f, 13, "ui.cat.*", "ui.pal.parts", "ui.pal.markers"),
        Lines("Palette rows (230×36, two lines at 13)", 230f, 36f, 15, 13, 13, "block.*.name", "ui.marker.arms_dealer", "ui.marker.blockfarmer", "ui.marker.cabin",
            "ui.marker.chest", "ui.marker.console", "ui.marker.data_terminal", "ui.marker.doctor", "ui.marker.door_energy", "ui.marker.door_hinge",
            "ui.marker.door_slide", "ui.marker.greenhouse", "ui.marker.grocer", "ui.marker.guard_post", "ui.marker.hangar", "ui.marker.heal_tank",
            "ui.marker.loot", "ui.marker.lounge", "ui.marker.mission_board", "ui.marker.npc", "ui.marker.quarters", "ui.marker.reporter",
            "ui.marker.room", "ui.marker.sage", "ui.marker.spawn", "ui.marker.streamer", "ui.marker.tamer", "ui.marker.tavern",
            "ui.marker.vendor", "ui.marker.workshop", "ui.marker.device",
            "ui.marker.sound", "ui.marker.den", "ui.marker.keep", "ui.marker.cavity", "ui.marker.settings"), // #3492/#3493: the landmark tools
        Lines("Palette rows: an airlock block (a 52 px row when it needs three lines)", 230f, 52f, 15, 13, 13, "ui.ed.airlock_block")
            with { Sample = "Hull Airlock", MaxLines = 3 },
        Label("StructureEditor title", 348f, 18, "ui.struct.title_station", "ui.struct.title_settlement"),
        FitLine("StructureEditor field labels (348)", 348f, 13, 15, "ui.struct.key", "ui.struct.name", "ui.struct.pack", "ui.struct.surface",
            "ui.struct.planet_types", "ui.ed.landmark.name_key"),
        Lines("StructureEditor row labels (140×36)", 140f, 36f, 16, 13, 13, "ui.struct.tier", "ui.struct.role", "ui.struct.function",
            "ui.struct.port_door", "ui.struct.style", "ui.struct.weight", "ui.ed.landmark.profile", "ui.ed.landmark.strength",
            "ui.ed.landmark.ground_row"),
        Lines("StructureEditor values (150×36)", 150f, 36f, 16, 13, 13, TierValueKeys.Concat(new[]
        {
            "ui.use.whole", "ui.use.module", "ui.use.landmark", "ui.function.*", "ui.role_short.*", "ui.door_opt.*", "ui.style.human",
            "ui.style.alien", "ui.ed.landmark.profile.*",
        }).ToArray()),
        // #3492 the landmark form (StructureEditor.Landmark.cs): four toggles, the number rows' "Auto", the preview button.
        Button("StructureEditor landmark toggles", 348f, 34f, "ui.ed.landmark.ruin", "ui.ed.landmark.atmosphere", "ui.ed.landmark.inhabited",
            "ui.ed.landmark.protected") with { Prefix = "☐ " },
        Label("StructureEditor landmark number value (90)", 90f, 16, "ui.ed.landmark.strength_auto"),
        Button("StructureEditor ruin preview on / off", 348f, 34f, "ui.ed.landmark.preview_on", "ui.ed.landmark.preview_off"),
        Wrap("StructureEditor size hint (2 lines)", 348f, 2, 13, "ui.struct.size_station", "ui.struct.size_settlement", "ui.struct.size_module"),
        FitLine("StructureEditor kit label", 70f, 13, 15, "ui.struct.kit"),
        Button("StructureEditor Kits… (module row)", 124f, 30f, "ui.struct.kits"),
        Button("StructureEditor check seal / assemble / Kits… (whole)", 168f, 34f, "ui.struct.check_seal", "ui.struct.assemble", "ui.struct.kits"),
        Button("StructureEditor underground toggle (whole settlement, #3333)", 348f, 34f, "ui.struct.underground") with { Prefix = "☐ " },
        FitLine("StructureEditor section titles", 348f, 13, 16, "ui.struct.generate_title", "ui.struct.brush", "ui.ed.landmark.title"),
        FitLine("StructureEditor seed label", 60f, 13, 15, "ui.struct.seed"),
        Button("StructureEditor reroll", 168f, 30f, "ui.struct.reroll"),
        Button("StructureEditor generate", 348f, 34f, "ui.struct.generate"),
        FitLine("StructureEditor dye/glow labels", 70f, 13, 15, "ui.struct.dye", "ui.struct.glow"),
        Button("StructureEditor brush None", 114f, 30f, "ui.struct.brush_none"),
        FitLine("StructureEditor shape label", 80f, 13, 15, "ui.struct.shape"),
        Lines("StructureEditor form value (112×36)", 112f, 36f, 15, 13, 13, FormKeys.Append("ui.ed.form_auto_short").ToArray()),
        Button("StructureEditor form pick", 114f, 30f, "ui.ed.form_pick"),
        FitLine("StructureEditor orientation label", 120f, 13, 15, "ui.struct.orient"),
        Label("StructureEditor placed", 348f, 15, "ui.ed.placed") with { Sample = "88888" },
        Wrap("StructureEditor status (348×84, 5 lines)", 348f, 5, 13, "ui.ed.saved_structure", "ui.ed.need_key", "ui.ed.export_failed_hint",
            "ui.ed.load_failed_hint", "ui.ed.assemble_failed_hint", "ui.ed.seal_ok", "ui.ed.seal_leaks", "ui.ed.port_errors",
            "ui.ed.door_lanes_blocked", "ui.ed.door_needs_wall", "ui.ed.bed_needs_room", "ui.ed.need_kit", "ui.ed.assembled", "ui.ed.generated",
            "ui.ed.not_found", "ui.ed.copy_hint", "ui.ed.loaded", "ui.ed.assemble_failed", "ui.ed.device.none", "ui.ed.device.saved",
            "ui.ed.skipped", "ui.ed.landmark.preview_locked", "ui.ed.landmark.preview_back", "ui.ed.landmark.preview_shown",
            "ui.ed.landmark.kept", "ui.ed.landmark.unkept", "ui.ed.landmark.problem", "ui.ed.landmark.saved", "ui.ed.landmark.no_submit",
            "ui.ed.marker.none") with { Sample = "my_town" },
        Input("StructureEditor default name in the name field", 348f, 18, "ui.ed.default.station", "ui.ed.default.settlement"),
        Button("StructureEditor save / submit", 168f, 42f, "ui.struct.save", "ui.submit.button"),
        Button("StructureEditor load / back", 168f, 40f, "ui.struct.load", "ui.menu.back"),
        Lines("Build editors' bottom hint (between the panels)", 1104f, 36f, 16, 13, 13, "ui.struct.hint", "ui.struct.hint_pad",
            "ui.struct.hint_pad_panel"),

        Label("Form picker title", 600f, 18, "ui.ed.form_title"),
        // "Automatic (the block's own form)" where it fits, else "Auto" (EditorFormPicker.AutoCaption) — the short one must fit.
        Lines("Form picker captions", 136f, 34f, 15, 13, 13, FormKeys.Append("ui.ed.form_auto_short").ToArray()),
        Button("Form / load picker back", 600f, 40f, "ui.menu.back"),
        Label("Load picker title", 600f, 18, "ui.struct.load"),
        Label("Load picker empty", 560f, 15, "ui.ed.none"),
        Label("Load picker sections", 560f, 13, "ui.ed.sec_builtin_ships", "ui.ed.sec_builtin_templates", "ui.ed.sec_user_templates",
            "ui.ed.sec_user_ships", "ui.ed.sec_exports"),
        Label("Load picker cell count", 252f, 13, "ui.ed.cells") with { Sample = "88888" },
        Wrap("Load picker confirmation", 600f, 5, 18, "ui.ed.confirm_replace") with { Sample = "Hammerhead 2" },
        Button("Load picker confirm buttons", 294f, 42f, "ui.ed.confirm_load", "ui.menu.back"),

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
            "ui.ed.marker.radius", "ui.ed.marker.volume", "ui.ed.marker.species", "ui.ed.marker.count"),
        Button("Marker panel stepper values", 520f, 48f, "ui.ed.marker.table_default", "ui.ed.marker.species_native") with { Suffix = "  →" },
        Button("Marker panel toggles", 888f, 48f, "ui.ed.marker.hidden", "ui.ed.marker.inside") with { Prefix = "☐ " },
        Label("Marker panel settings line", 916f, 14, "ui.ed.marker.defaults"),
        Button("Marker panel apply", 300f, 48f, "ui.ed.device.apply"),
    };

    private static IEnumerable<FitSite> KitPanelSites() => new[]
    {
        Label("Kit panel title", 600f, 20, "ui.kit.title"),
        Button("Kit panel close", 100f, 30f, "ui.kit.close"),
        Label("Kit panel list title", 240f, 15, "ui.kit.list"),
        Button("Kit panel new / use", 250f, 34f, "ui.kit.new", "ui.kit.use"),
        Input("Kit panel default name in the name field", 228f, 14, "ui.ed.default.kit") with { Sample = "8" },
        Button("Kit panel list rows", 240f, 36f, "ui.ed.default.kit") with { Prefix = "▶ ", Suffix = "  ·  Station / Huge", Sample = "8" },
        FitLine("Kit panel field labels (140)", 140f, 13, 14, "ui.kit.key", "ui.kit.name", "ui.kit.kind", "ui.kit.tier", "ui.kit.pack",
            "ui.kit.weight", "ui.kit.planet_types", "ui.kit.modules_min", "ui.kit.modules_max", "ui.kit.start", "ui.kit.max_extent",
            "ui.kit.solar_wings", "ui.kit.antennas", "ui.kit.domes", "ui.kit.grid", "ui.kit.district", "ui.kit.street", "ui.kit.height",
            "ui.kit.role_map", "ui.kit.cols_min", "ui.kit.cols_max", "ui.kit.rows_min", "ui.kit.rows_max", "ui.kit.plot_stride",
            "ui.kit.building", "ui.kit.storeys"),
        Lines("Kit panel kind / tier values", 166f, 30f, 14, 13, 13, TierValueKeys.Append("ui.kit.kind.*").ToArray()),
        Button("Kit panel modules only / underground (#3333)", 192f, 30f, "ui.kit.modules_only", "ui.kit.underground") with { Prefix = "☐ " },
        Label("Kit panel column: module", 220f, 13, "ui.kit.col_module"),
        Label("Kit panel columns: min / max", 50f, 13, "ui.kit.col_min", "ui.kit.col_max"),
        Label("Kit panel column: required", 90f, 13, "ui.kit.col_required"),
        Label("Kit panel column: weight", 60f, 13, "ui.kit.col_weight"),
        Label("Kit panel column: rotate", 80f, 13, "ui.kit.col_rotate"),
        Button("Kit panel remove", 110f, 30f, "ui.kit.remove"),
        Button("Kit panel add / save / delete", 160f, 34f, "ui.kit.add_entry", "ui.kit.save", "ui.kit.delete"),
        Wrap("Kit panel status (256×54, 3 lines)", 256f, 3, 13, "ui.kit.saved", "ui.ed.need_key", "ui.ed.export_failed_hint",
            "ui.ed.delete_failed_hint") with { Sample = "my_kit_8" },
        Label("Kit picker title", 500f, 18, "ui.kit.pick_module"),
        Label("Kit picker filter", 80f, 14, "ui.kit.filter"),
    };

    private static IEnumerable<FitSite> ShipSites() => new[]
    {
        Label("ShipEditor title", 348f, 18, "ui.ship.title"),
        Label("ShipEditor form labels", 352f, 15, "ui.struct.key", "ui.struct.name", "ui.ship.desc", "ui.ship.blueprint", "ui.ship.dye_hex",
            "ui.ship.glow_hex"),
        Label("ShipEditor placed", 352f, 15, "ui.ed.placed") with { Sample = "88888" },
        Label("ShipEditor form headers", 352f, 16, "ui.ship.stats", "ui.ship.interior", "ui.ship.brush"),
        FitLine("ShipEditor stepper labels (204)", 204f, 13, 15, "ui.ship.hull", "ui.ship.shield", "ui.ship.speed", "ui.ship.handling",
            "ui.ship.cargo", "ui.ship.int_w", "ui.ship.int_l", "ui.ship.int_h", "ui.struct.orient"),
        Wrap("ShipEditor interior hint (measured row)", 352f, 6, 13, "ui.ship.interior_hint"),
        FitLine("ShipEditor shape label", 86f, 13, 15, "ui.struct.shape"),
        Lines("ShipEditor form value (114×36)", 114f, 36f, 15, 13, 13, FormKeys.Append("ui.ed.form_auto_short").ToArray()),
        Button("ShipEditor form pick", 112f, 28f, "ui.ed.form_pick"),
        FitLine("ShipEditor cost title", 216f, 13, 16, "ui.ship.cost"),
        Button("ShipEditor cost add", 132f, 28f, "ui.ship.cost_add"),
        Input("ShipEditor cost item", 200f, 18, "ui.ship.cost_item"),
        Wrap("ShipEditor status (356×86, 5 lines)", 356f, 5, 13, "ui.ed.saved_ship", "ui.ed.need_key", "ui.ed.export_failed_hint",
            "ui.ed.load_failed_hint", "ui.ed.loaded", "ui.ed.not_found", "ui.ed.skipped", "ui.ed.user_ship_renamed") with { Sample = "my_ship" },
        Input("ShipEditor default name / description in their fields", 352f, 18, "ui.ed.default.ship", "ui.ed.default.ship_desc"),
        Button("ShipEditor save / submit", 172f, 42f, "ui.struct.save", "ui.submit.button"),
        Button("ShipEditor load / back", 172f, 38f, "ui.struct.load", "ui.menu.back"),
    };
}
