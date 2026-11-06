// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The catalogue: the editors hub (<c>UiEditors.cs</c>) and the texture editor (<c>TextureEditor.Ui.cs</c>).</summary>
public sealed partial class EditorTextFitTests
{
    private static IEnumerable<FitSite> HubSites() => new[]
    {
        Label("UiEditors title (logo)", 800f, 60, "ui.editors.title"),
        Label("UiEditors subtitle", 900f, 18, "ui.editors.subtitle"),
        FitLine("UiEditors group headers", 520f, 13, 15, "ui.editors.group.creator", "ui.editors.group.dev"),
        IconButton("UiEditors tool buttons", 520f, 54f, "ui.menu.ship_editor", "ui.menu.avatar_editor", "ui.menu.station_editor",
            "ui.menu.settlement_editor", "ui.menu.texture_editor", "ui.menu.form_editor", "ui.menu.tool_looks", "ui.menu.content_editor",
            "ui.menu.material_editor", "ui.menu.back"),
        Label("UiEditors info title", 520f, 17, "ui.editors.info_title"),
        Wrap("UiEditors info body (520×250)", 520f, 12, 16, "ui.editors.info_body"),
        Wrap("UiEditors developer note (520×90)", 520f, 5, 14, "ui.editors.dev.note"),
    };

    private static IEnumerable<FitSite> TextureSites() => new[]
    {
        Label("TextureEditor title", 1400f, 17, "ui.tex.title"),
        Label("TextureEditor browser title", 330f, 16, "ui.tex.browser"),
        Input("TextureEditor search", 332f, 15, "ui.tex.search"),
        Label("TextureEditor layer (left part)", 380f, 13, "ui.tex.layer_*") with { Suffix = " (Name)  · " },
        Label("TextureEditor layer (alpha part)", 250f, 13, "ui.tex.alpha_opaque", "ui.tex.alpha_cutout"),
        Label("TextureEditor slot labels on the canvas", 320f, 13, "ui.tex.part.*", "ui.tex.side.*") with { Suffix = " · Oben" },
        Label("TextureEditor frame label", 640f, 13, "ui.tex.frames_animated", "ui.tex.frames_still") with { Sample = "8" },
        Button("TextureEditor frame buttons", 150f, 32f, "ui.tex.frame_add", "ui.tex.frame_del", "ui.tex.fps"),
        Button("TextureEditor tool rows (3 per row)", 208f, 40f, "ui.tex.tool.*", "ui.tex.mirror_x", "ui.tex.mirror_y", "ui.tex.slots",
            "ui.tex.play", "ui.tex.fill_all"),
        Label("TextureEditor brush size", 136f, 14, "ui.tex.brush_size") with { Sample = "8" },
        Button("TextureEditor edit column (2 per row)", 150f, 40f, "ui.tex.undo", "ui.tex.redo", "ui.tex.flip_h", "ui.tex.flip_v",
            "ui.tex.brighter", "ui.tex.darker", "ui.tex.contrast_up", "ui.tex.contrast_down", "ui.tex.saturate", "ui.tex.desaturate"),
        Label("TextureEditor preview title", 360f, 15, "ui.tex.preview"),
        Label("TextureEditor tiled title", 380f, 15, "ui.tex.tiled"),
        Label("TextureEditor colour title", 300f, 15, "ui.tex.color"),
        Wrap("TextureEditor alpha hint (measured height)", 456f, 2, 13, "ui.tex.alpha_bar"),
        Button("TextureEditor swap", 120f, 32f, "ui.tex.swap"),
        Label("TextureEditor tile colours", 220f, 13, "ui.tex.tile_colors"),
        Label("TextureEditor save title", 770f, 15, "ui.tex.save_title"),
        Button("TextureEditor save row", 250f, 38f, "ui.tex.save_local", "ui.tex.remove_local"),
        Button("TextureEditor export", 256f, 38f, "ui.tex.export"),
        Button("TextureEditor reset/before/folder", 250f, 34f, "ui.tex.reset", "ui.tex.before", "ui.tex.open_folder", "ui.tex.reload_folder"),
        Button("TextureEditor code/publish", 126f, 34f, "ui.tex.copy_code", "ui.tex.paste_code", "ui.tex.publish", "ui.tex.unpublish"),
        Wrap("TextureEditor status (772 wide, 2 lines)", 772f, 2, 13, "ui.tex.saved_local", "ui.tex.saved_local_world_wins", "ui.tex.save_failed",
            "ui.tex.removed_local", "ui.tex.none_local", "ui.tex.reset_done", "ui.tex.no_official", "ui.tex.reloaded", "ui.tex.publish_sent",
            "ui.tex.code_pasted", "ui.tex.code_copied", "ui.tex.code_bad", "ui.tex.icon_one_frame", "ui.tex.frame_limit",
            "ui.ed.export_failed_hint", "ui.ed.open_folder_failed") with { Sample = "stone_bricks" },
        Wrap("TextureEditor status: exported + folder", 772f, 1, 13, "ui.tex.exported"),
        Button("TextureEditor back", 160f, 40f, "ui.menu.back"),
        IconButton("TextureEditor submit", 222f, 40f, "ui.tex.submit.open"),
        Label("TextureEditor hint line", 1880f, 14, "ui.tex.hint", "ui.tex.hint_pad", "ui.tex.hint_pad_panel"),
    };
}
