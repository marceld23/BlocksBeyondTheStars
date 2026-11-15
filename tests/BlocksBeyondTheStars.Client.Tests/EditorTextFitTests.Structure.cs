// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The catalogue: the Station/Village editor on the editor shell (#3692, <c>StructureEditor.Ui.cs</c> /
/// <c>.Advanced.cs</c>) — the palette card (400 px, <c>EditorPaletteKit.AddPaletteCard</c>) with the kits below it, the right
/// card (380 px: the children's rows at 16 px and up, the developer sections behind "Advanced", their rows on the 348 px column
/// of #3263), the shell's status bar and key line, the function picker and the kit panel.</summary>
public sealed partial class EditorTextFitTests
{
    // The palette card: 400 wide, its inner width 368, a tile's name 304 (the ship editor's card: EditorTextFitTests.Ship.cs).
    private const float StructurePaletteInnerW = 368f, StructureTileNameW = 304f;

    // The right card's content column; a cycle's value (348 − 2 × 44 − 8, as a button 28 px wider for its insets).
    private const float StructureColumnW = 348f, StructureCycleButtonW = 280f;

    private static IEnumerable<FitSite> StructureEditorSites() => new[]
    {
        // The palette card (the tool switch and the search are the shared card's, EditorTextFitTests.Ship.cs).
        FitLine("StructureEditor palette card title (368)", StructurePaletteInnerW, 13, 16, "ui.struct.palette"),
        Label("StructureEditor palette sections (352)", StructurePaletteInnerW - 16f, 16, "ui.pal.markers"),
        Lines("StructureEditor palette tiles (304×68, 16 px, three lines)", StructureTileNameW, 68f, 18, 16, 16, "block.*.name",
            "ui.marker.arms_dealer", "ui.marker.blockfarmer", "ui.marker.cabin", "ui.marker.chest", "ui.marker.console", "ui.marker.data_terminal",
            "ui.marker.doctor", "ui.marker.door_energy", "ui.marker.door_hinge", "ui.marker.door_slide", "ui.marker.greenhouse", "ui.marker.grocer",
            "ui.marker.guard_post", "ui.marker.hangar", "ui.marker.heal_tank", "ui.marker.loot", "ui.marker.lounge", "ui.marker.mission_board",
            "ui.marker.npc", "ui.marker.quarters", "ui.marker.reporter", "ui.marker.room", "ui.marker.sage", "ui.marker.spawn", "ui.marker.streamer",
            "ui.marker.tamer", "ui.marker.tavern", "ui.marker.vendor", "ui.marker.workshop", "ui.marker.device", "ui.marker.entrance",
            "ui.marker.sound", "ui.marker.den", "ui.marker.keep", "ui.marker.cavity", "ui.marker.settings", "ui.marker.zone",
            "ui.marker.relic_cache", "ui.marker.air_pocket", "ui.marker.prop", "ui.ed.giantship.entrance", "ui.ed.airlock_block",
            "ui.marker.port_door", "ui.marker.port_wide", "ui.marker.port_ladder", "ui.token.*") with { MaxLines = 3 },
        Button("StructureEditor Kits… (palette card foot, 368×44, icon)", StructurePaletteInnerW, 44f, "ui.struct.kits") with { TextX = 56f },

        // The right card's head and the children's rows.
        FitLine("StructureEditor card kicker (196)", 196f, 13, 16, "ui.struct.title_station", "ui.struct.title_settlement"),
        FitLine("StructureEditor Advanced label (88)", 88f, 13, 16, "ui.struct.advanced"),
        FitLine("StructureEditor children's row labels (348)", StructureColumnW, 13, 16, "ui.struct.name", "ui.struct.tier", "ui.struct.where",
            "ui.struct.shape"),
        Input("StructureEditor default name in the name field", StructureColumnW, 18, "ui.ed.default.station", "ui.ed.default.settlement"),
        Button("StructureEditor size / where cycle values (252)", StructureCycleButtonW, 44f, TierValueKeys.Concat(new[]
        {
            "ui.struct.where.surface", "ui.struct.where.underground",
        }).ToArray()),
        Lines("StructureEditor size hint (348×42)", StructureColumnW, 42f, 16, 13, 13, "ui.struct.size_station", "ui.struct.size_settlement",
            "ui.struct.size_module"),
        Lines("StructureEditor 'Show as a ruin' switch label (276×44)", 276f, 44f, 18, 13, 13, "ui.ed.landmark.preview_on"),
        Lines("StructureEditor form value (154×44)", 154f, 44f, 16, 13, 13, FormKeys.Append("ui.ed.form_auto_short").ToArray()),
        Button("StructureEditor form pick (140×44)", 140f, 44f, "ui.ed.form_pick"),
        FitLine("StructureEditor orientation label (120)", 120f, 13, 16, "ui.struct.orient"),
        Label("StructureEditor placed", StructureColumnW, 16, "ui.ed.placed") with { Sample = "88888" },
        Wrap("StructureEditor 'the developer parts are under Advanced'", StructureColumnW, 4, 16, "ui.struct.advanced_hint"),

        // Advanced: the amber line, the section headers (title · summary), the hints of a section the design is not in.
        FitLine("StructureEditor Advanced title (348)", StructureColumnW, 13, 16, "ui.struct.advanced_title"),
        Button("StructureEditor section headers (348×44)", StructureColumnW, 44f, "ui.struct.sec.entry", "ui.struct.sec.ports", "ui.struct.sec.sources",
            "ui.ed.landmark.title", "ui.ed.giantship.title", "ui.struct.sec.civ", "ui.struct.brush") with { Prefix = "▾ ", Suffix = "  ·  FFAA00" },
        Button("StructureEditor entry section header with its 'use as'", StructureColumnW, 44f, "ui.use.whole", "ui.use.module", "ui.use.landmark",
            "ui.use.entry", "ui.use.giant_ship") with { Prefix = "▾ Generator-Eintrag  ·  " },
        Button("StructureEditor section summaries", StructureColumnW, 44f, "ui.struct.sec.on", "ui.struct.sec.off", "ui.struct.sec.none")
            with { Prefix = "▾ Riesenschiff  ·  " },
        Wrap("StructureEditor section hints (348)", StructureColumnW, 4, 16, "ui.struct.sec.ports_hint", "ui.struct.sec.landmark_hint",
            "ui.struct.sec.giant_ship_hint", "ui.struct.sec.civ_hint"),

        // The developer rows (unchanged boxes of #3263 on the 348 px column).
        FitLine("StructureEditor field labels (348)", StructureColumnW, 13, 15, "ui.struct.key", "ui.struct.pack", "ui.struct.surface",
            "ui.struct.planet_types", "ui.ed.landmark.name_key", "ui.ed.giantship.show_wreck"),
        Lines("StructureEditor row labels (140×36)", 140f, 36f, 16, 13, 13, "ui.struct.role", "ui.struct.function",
            "ui.struct.port_door", "ui.struct.style", "ui.struct.weight", "ui.ed.landmark.profile", "ui.ed.landmark.strength",
            "ui.ed.landmark.ground_row", "ui.ed.landmark.seat", "ui.ed.protection", "ui.ed.giantship.style"),
        Lines("StructureEditor values (150×36)", 150f, 36f, 16, 13, 13, TierValueKeys.Concat(new[]
        {
            "ui.use.whole", "ui.use.module", "ui.use.landmark", "ui.function.*", "ui.role_short.*", "ui.door_opt.*", "ui.style.human",
            "ui.style.alien", "ui.ed.landmark.profile.*", "ui.ed.landmark.seat.*", "ui.ed.protection.*", "ui.ed.zone.profile_own",
            "ui.use.giant_ship", "ui.ed.giantship.style.*",
        }).ToArray()),
        Button("StructureEditor landmark toggles", StructureColumnW, 34f, "ui.ed.landmark.ruin", "ui.ed.landmark.atmosphere", "ui.ed.landmark.inhabited")
            with { Prefix = "☐ " },
        Wrap("StructureEditor protection line (measured, 348)", StructureColumnW, 3, 13, "ui.stamp.protection.*"),
        FitLine("StructureEditor ruin zones title", StructureColumnW, 13, 16, "ui.ed.zone.title"),
        Button("StructureEditor add a ruin zone", StructureColumnW, 34f, "ui.ed.zone.add"),
        Wrap("StructureEditor ruin zone hint (measured)", StructureColumnW, 8, 13, "ui.ed.zone.hint"),
        FitLine("StructureEditor ruin zone name (200)", 200f, 13, 16, "ui.ed.zone.name"),
        Button("StructureEditor ruin zone remove", 120f, 30f, "ui.ed.zone.remove"),
        FitLine("StructureEditor ruin zone corner labels (60)", 60f, 13, 15, "ui.ed.zone.from", "ui.ed.zone.to"),
        Button("StructureEditor ruin zone: two clicks set it", StructureColumnW, 34f, "ui.ed.zone.select") with { Prefix = "○ " },
        Label("StructureEditor landmark number value (90)", 90f, 16, "ui.ed.landmark.strength_auto"),
        Button("StructureEditor ruin preview on / off", StructureColumnW, 34f, "ui.ed.landmark.preview_on", "ui.ed.landmark.preview_off"),
        FitLine("StructureEditor kit label", 70f, 13, 15, "ui.struct.kit"),
        Button("StructureEditor Kits… (module row)", 124f, 30f, "ui.struct.kits"),
        Button("StructureEditor check seal / assemble", 168f, 34f, "ui.struct.check_seal", "ui.struct.assemble"),
        FitLine("StructureEditor section titles", StructureColumnW, 13, 16, "ui.struct.generate_title", "ui.ed.landmark.title",
            "ui.ed.giantship.title", "ui.ed.civ.title"),
        Wrap("StructureEditor giant ship hint (measured)", StructureColumnW, 8, 13, "ui.ed.giantship.hint"),
        Button("StructureEditor wreck preview: planet / space", 168f, 34f, "ui.ed.giantship.on_planet", "ui.ed.giantship.in_space"),
        FitLine("StructureEditor seed label", 60f, 13, 15, "ui.struct.seed"),
        Button("StructureEditor reroll", 168f, 30f, "ui.struct.reroll"),
        Button("StructureEditor generate", StructureColumnW, 34f, "ui.struct.generate"),
        FitLine("StructureEditor dye/glow labels", 70f, 13, 15, "ui.struct.dye", "ui.struct.glow"),
        Button("StructureEditor brush None", 114f, 30f, "ui.struct.brush_none"),

        // The shell: the Save subline, the status bar (1140, line breaks become spaces), the key line (680) and the flying line.
        FitLine("StructureEditor Save subline (350)", 350f, 16, 16, "ui.editor.save_sub.station", "ui.editor.save_sub.settlement"),
        Wrap("StructureEditor status (shell status bar, 1140)", 1140f, 3, 16, "ui.ed.saved_structure", "ui.ed.need_key", "ui.ed.export_failed_hint",
            "ui.ed.load_failed_hint", "ui.ed.assemble_failed_hint", "ui.ed.seal_ok", "ui.ed.seal_leaks", "ui.ed.port_errors",
            "ui.ed.door_lanes_blocked", "ui.ed.door_needs_wall", "ui.ed.bed_needs_room", "ui.ed.need_kit", "ui.ed.assembled", "ui.ed.generated",
            "ui.ed.not_found", "ui.ed.copy_hint", "ui.ed.loaded", "ui.ed.assemble_failed", "ui.ed.device.none", "ui.ed.device.saved",
            "ui.ed.skipped", "ui.ed.landmark.preview_locked", "ui.ed.landmark.preview_back", "ui.ed.landmark.preview_shown",
            "ui.ed.landmark.kept", "ui.ed.landmark.unkept", "ui.ed.landmark.problem", "ui.ed.landmark.saved", "ui.ed.landmark.no_submit",
            "ui.ed.marker.none", "ui.ed.marker.applied", "ui.ed.giantship.preview_shown", "ui.ed.giantship.saved", "ui.ed.giantship.no_submit",
            "ui.ed.giantship.door_unclear", "ui.ed.civ.saved", "ui.struct.new_done", "ui.ed.zone.corner", "ui.ed.zone.set", "ui.ed.zone.full",
            "ui.ed.zone.not_ruin", "ui.ed.zone.outside") with { Sample = "my_town" },
        Wrap("StructureEditor status: a check's problem (shell status bar, 1140)", 1140f, 2, 16, "ui.problem.*") with { Sample = "my_town" },
        Lines("StructureEditor key line (shell status bar, 680×44)", 680f, 44f, 16, 16, 16, "ui.struct.hint_mouse", "ui.struct.hint_pad_build",
            "ui.struct.hint_pad_panels"),
        Lines("StructureEditor flying line (the 3D view's foot, 1076×36)", 1076f, 36f, 16, 13, 13, "ui.struct.view_hint"),

        // The function picker (#3600, centred since #3692).
        FitLine("Function picker title (280)", 280f, 13, 20, "ui.struct.function"),
        Button("Function picker rows (420×44)", 420f, 44f, "ui.role_short.*", "ui.function.*") with { Prefix = "▶ " },
        Button("Function picker close (150×44, icon)", 150f, 44f, "ui.kit.close") with { TextX = 56f },
    };

    private static IEnumerable<FitSite> KitPanelSites() => new[]
    {
        Label("Kit panel title", 600f, 22, "ui.kit.title"),
        Button("Kit panel close (150×44, icon)", 150f, 44f, "ui.kit.close") with { TextX = 56f },
        Label("Kit panel list title", 240f, 15, "ui.kit.list"),
        Button("Kit panel new / use (250×44, icon)", 250f, 44f, "ui.kit.new", "ui.kit.use") with { TextX = 56f },
        Input("Kit panel default name in the name field", 228f, 14, "ui.ed.default.kit") with { Sample = "8" },
        Button("Kit panel list rows", 240f, 36f, "ui.ed.default.kit") with { Prefix = "▶ ", Suffix = "  ·  Station / Huge", Sample = "8" },
        FitLine("Kit panel field labels (140)", 140f, 13, 14, "ui.kit.key", "ui.kit.name", "ui.kit.kind", "ui.kit.tier", "ui.kit.pack",
            "ui.kit.weight", "ui.kit.planet_types", "ui.kit.modules_min", "ui.kit.modules_max", "ui.kit.start", "ui.kit.max_extent",
            "ui.kit.solar_wings", "ui.kit.antennas", "ui.kit.domes", "ui.kit.grid", "ui.kit.district", "ui.kit.street", "ui.kit.height",
            "ui.kit.role_map", "ui.kit.cols_min", "ui.kit.cols_max", "ui.kit.rows_min", "ui.kit.rows_max", "ui.kit.plot_stride",
            "ui.kit.building", "ui.kit.storeys"),
        Lines("Kit panel kind / tier values", 166f, 30f, 14, 13, 13, TierValueKeys.Append("ui.kit.kind.*").ToArray()),
        Button("Kit panel modules only / underground (#3333)", 192f, 30f, "ui.kit.modules_only", "ui.kit.underground") with { Prefix = "☐ " },
        FitLine("Kit panel layout field labels (140, #3600)", 140f, 13, 14, "ui.kit.layout", "ui.kit.main_street", "ui.kit.spacing",
            "ui.kit.extent", "ui.kit.forms", "ui.kit.suburb_kit"),
        Lines("Kit panel layout values (166×30, #3600)", 166f, 30f, 14, 13, 13, "ui.kit.layout.*"),
        Button("Kit panel flat / grown city (#3600)", 192f, 30f, "ui.kit.flat", "ui.kit.grown_city") with { Prefix = "☐ " },
        Button("Kit panel village forms (#3600)", 192f, 30f, "ui.kit.form.*") with { Prefix = "☐ " },
        Wrap("Kit panel district map legend (390, #3600)", 390f, 12, 13, "ui.kit.map_legend"),
        Label("Kit panel column: module", 220f, 13, "ui.kit.col_module"),
        Label("Kit panel columns: min / max", 50f, 13, "ui.kit.col_min", "ui.kit.col_max"),
        Label("Kit panel column: required", 90f, 13, "ui.kit.col_required"),
        Label("Kit panel column: weight", 60f, 13, "ui.kit.col_weight"),
        Label("Kit panel column: rotate", 80f, 13, "ui.kit.col_rotate"),
        Button("Kit panel remove", 110f, 30f, "ui.kit.remove"),
        Button("Kit panel + entry / save kit / delete kit (220×44, icon)", 220f, 44f, "ui.kit.add_entry", "ui.kit.save", "ui.kit.delete")
            with { TextX = 56f },
        Wrap("Kit panel status (796, 2 lines)", 796f, 2, 14, "ui.kit.saved", "ui.ed.need_key", "ui.ed.export_failed_hint",
            "ui.ed.delete_failed_hint") with { Sample = "my_kit_8" },
        Wrap("Kit panel delete question (the shell's, 688)", 688f, 5, 20, "ui.editor.confirm_delete") with { Sample = "Hamlet Default 1" },
        Button("Kit panel delete question's buttons (332×58, icon)", 332f, 58f, "ui.editor.delete", "ui.action.cancel") with { TextX = 56f },
        Label("Kit picker title", 500f, 18, "ui.kit.pick_module"),
        Label("Kit picker filter", 80f, 14, "ui.kit.filter"),
        Button("Kit picker close (150×44, icon)", 150f, 44f, "ui.kit.close") with { TextX = 56f },
    };
}
