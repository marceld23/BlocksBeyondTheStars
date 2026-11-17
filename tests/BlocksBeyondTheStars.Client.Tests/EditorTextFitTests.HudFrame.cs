// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The catalogue: the character editor's HUD-frame tab (#3318; a tab of the shell's appearance screen since #3693),
/// the template rows of its two library cards (#3320, #3321) — boxes as <c>FaceEditor.HudMode.cs</c> and
/// <c>FaceEditor.Shelf.cs</c> lay them out in the preview card (500 wide) and the outfits card (440 wide). Its Submit is the
/// shell's header button.</summary>
public sealed partial class EditorTextFitTests
{
    private static IEnumerable<FitSite> HudFrameSites() => new[]
    {
        Label("Paint editor HUD tile labels", 112f, 13, "ui.hudframe.tile.*"),
        Label("Paint editor HUD active tile", 512f, 16, "ui.hudframe.tile.*"),
        Button("Paint editor HUD frame on / off", 320f, 44f, "ui.hudframe.on", "ui.hudframe.off"),
        FitLine("Paint editor HUD size / opacity kickers (upper case)", 320f * FaceKickerShare, 13, 16, "ui.hudframe.size", "ui.hudframe.opacity"),
        Button("Paint editor HUD sizes", 104f, 44f, "ui.hudframe.size_small", "ui.hudframe.size_medium", "ui.hudframe.size_large"),
        FitLine("Paint editor library / template kickers (upper case)", 392f * FaceKickerShare, 13, 16, "ui.hudframe.library",
            "ui.hudframe.templates"),
        Input("Paint editor HUD frame name", 392f, 18, "ui.hudframe.name"),
        Button("Paint editor HUD save frame", 192f, 44f, "ui.hudframe.save"),
        Wrap("Paint editor no frames (392 × 120)", 392f, 6, 16, "ui.hudframe.none"),
        Wrap("Paint editor frame / skin status (the shell's status bar)", FaceStatusW, 2, 16, "ui.hudframe.saved", "ui.hudframe.updated",
            "ui.hudframe.renamed", "ui.hudframe.deleted", "ui.hudframe.limit", "ui.hudframe.select_first", "ui.hudframe.name_taken",
            "ui.hudframe.need_name", "ui.hudframe.empty", "ui.hudframe.worn", "ui.hudframe.template_worn", "ui.avatar.skin_worn", "ui.avatar.skin_worn_face")
            with { Sample = "Lichterkette" },
        Button("Paint editor template rows (392)", 392f, 44f, "hudframe.*.name", "skin.*.name"),
        Input("Submit dialog name field (a frame's default name)", 828f, 18, "ui.hudframe.default_name"),
    };
}
