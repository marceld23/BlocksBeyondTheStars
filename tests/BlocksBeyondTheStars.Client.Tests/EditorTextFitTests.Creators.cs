// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The catalogue: the Avatar Designer and the paint (appearance) editor on the editor shell (#3693) — boxes as
/// <c>FaceEditor.Layout.cs</c>, <c>FaceEditor.Shelf.cs</c> and <c>AvatarEditor.Figure.cs</c> lay them out; the form,
/// tool-look and shape editors are in <c>EditorTextFitTests.Forms.cs</c>.</summary>
public sealed partial class EditorTextFitTests
{
    // Card titles (UiKit.AddKicker) are drawn in upper case, which this headless measure does not do: they get 80 % of
    // their room.
    private const float FaceKickerShare = 0.8f;

    // The shell header's title with Undo, Submit and Save (EditorShellLayout): 1062 + 12 − 24 − 210.
    private const float FaceHeaderTitleW = 840f;

    // The shell's status bar text: 1140 wide, 16–18 px, two lines in its 48 px.
    private const float FaceStatusW = 1140f;

    private static IEnumerable<FitSite> AvatarSites() => new[]
    {
        FitLine("AvatarEditor header title", FaceHeaderTitleW, 20, 30, "ui.menu.avatar_editor"),
        FitLine("AvatarEditor figure kickers (upper case)", 600f * FaceKickerShare, 13, 16, "ui.avatar.colors", "ui.avatar.gear_preview",
            "ui.avatar.name"),
        Label("AvatarEditor colour rows", 220f, 18, "ui.settings.skin", "ui.settings.torso", "ui.settings.arms", "ui.settings.legs"),
        Label("AvatarEditor gear rows", 300f, 18, "ui.avatar.helmet", "ui.avatar.chest", "ui.avatar.legs", "ui.avatar.pack", "ui.avatar.lamp"),
        Input("AvatarEditor default name in the name field", 600f, 18, "ui.ed.default.skin"),
        Button("AvatarEditor export", 300f, 44f, "ui.avatar.export"),
        Wrap("AvatarEditor status (the shell's status bar)", FaceStatusW, 2, 16, "ui.avatar.need_name",
            "ui.ed.export_failed_hint", "ui.avatar.outfit_deleted", "ui.avatar.outfit_limit", "ui.avatar.outfit_loaded",
            "ui.avatar.outfit_name_taken", "ui.avatar.outfit_renamed", "ui.avatar.outfit_saved", "ui.avatar.outfit_select_first",
            "ui.avatar.outfit_updated", "ui.avatar.outfit_worn", "ui.avatar.skin_worn") with { Sample = "Mein Skin" },
    }.Concat(WhenMerged(
        Button("AvatarEditor Figure tab", 170f, 52f, "ui.avatar.figure_tab"),
        Wrap("AvatarEditor export done (the shell's status bar)", FaceStatusW, 2, 16, "ui.avatar.exported_folder")));

    private static IEnumerable<FitSite> PaintSites() => new[]
    {
        FitLine("Paint editor header title", FaceHeaderTitleW, 20, 30, "ui.face.title", "ui.paint.title", "ui.appearance.title"),
        Button("Paint editor part tabs (170×52)", 170f, 52f, "ui.paint.tab.*", "ui.hudframe.mode_hud"),
        Button("Paint editor tool bar: fill / pick (four segments of 532)", 127f, 48f, "ui.face.fill", "ui.face.pick"),
        FitLine("Paint editor palette kicker (upper case)", 532f * FaceKickerShare, 13, 16, "ui.face.palette"),
        Button("Paint editor fill everything / clear", 262f, 44f, "ui.face.fill_all", "ui.face.clear"),
        Lines("Paint editor pad tools hint (the shell's hint, 680×48)", 680f, 48f, 16, 16, 16, "ui.face.hint_pad_panel"),
        FitLine("Paint editor library kicker (upper case)", 312f * FaceKickerShare, 13, 16, "ui.paint.library"),
        Input("Paint editor design name", 312f, 18, "ui.paint.name"),
        Button("Paint editor copy / paste code", 152f, 44f, "ui.shape.custom.export", "ui.shape.custom.import"),
        Label("Paint editor active region", 512f, 16, "ui.paint.body.front", "ui.paint.body.left", "ui.paint.body.back", "ui.paint.body.right",
            "ui.paint.body.chin", "ui.paint.body.top", "ui.paint.body.outer", "ui.paint.body.inner"),
        FitLine("Paint editor wheel label", 230f, 13, 16, "ui.face.wheel"),
        FitLine("Paint editor wheel label on the base colour", 230f, 13, 16, "ui.paint.base_color") with { Prefix = "Farbrad → " },
        FitLine("Paint editor base colour kicker (upper case)", 300f * FaceKickerShare, 13, 16, "ui.paint.base_color"),
        Label("Paint editor limb headers", 136f, 14, "ui.paint.row.left", "ui.paint.row.right"),
        Label("Paint editor region labels", 136f, 13, "ui.paint.body.front", "ui.paint.body.left", "ui.paint.body.back",
            "ui.paint.body.right", "ui.paint.body.chin", "ui.paint.body.top", "ui.paint.body.outer", "ui.paint.body.inner"),
        FitLine("Paint editor outfits kicker (upper case)", 392f * FaceKickerShare, 13, 16, "ui.avatar.outfits", "ui.avatar.skin_templates"),
        Input("Paint editor outfit name", 392f, 18, "ui.avatar.outfit_name"),
        Button("Paint editor outfit save / rename", 192f, 44f, "ui.avatar.outfit_save", "ui.avatar.outfit_rename"),
        Wrap("Paint editor no outfits (392 × 120)", 392f, 6, 16, "ui.avatar.outfit_none"),
    }.Concat(WhenMerged(
        Button("Paint editor tool bar: pen / eraser", 127f, 48f, "ui.face.tool.pen", "ui.face.tool.eraser"),
        Lines("Paint editor controls hint (the shell's hint, 680×48)", 680f, 48f, 16, 16, 16, "ui.face.hint_short", "ui.face.hint_fill_short",
            "ui.face.hint_pick_short", "ui.face.hint_pad_short"),
        Wrap("Paint editor tab notes and library lines (the shell's status bar)", FaceStatusW, 2, 16, "ui.paint.body.helmet_about",
            "ui.paint.about", "ui.hudframe.about", "ui.paint.kept", "ui.paint.keep_empty", "ui.paint.code_copied", "ui.paint.imported",
            "ui.paint.import_failed"),
        IconButton("Paint editor keep in my designs", 312f, 44f, "ui.paint.keep"),
        FitLine("Paint editor preview kicker (upper case)", 452f * FaceKickerShare, 13, 16, "ui.face.preview"),
        Button("Paint editor outfit row: wear", 140f, 44f, "ui.avatar.wear")));

    /// <summary>
    /// Sites of the keys the editor shell migration (#3693) added. They were skipped until their locale fragment was merged;
    /// now merged, every one is checked — a missing key fails <see cref="EveryCataloguedKey_Exists"/> like any other.
    /// </summary>
    private static IEnumerable<FitSite> WhenMerged(params FitSite[] sites) => sites;
}
