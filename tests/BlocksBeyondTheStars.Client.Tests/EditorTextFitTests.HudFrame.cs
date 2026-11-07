// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The catalogue: the character editor's HUD mode (#3318), the template rows of its two library columns (#3320,
/// #3321) and its Submit button (#3319) — boxes as <c>FaceEditor.HudMode.cs</c>, <c>FaceEditor.Shelf.cs</c> and
/// <c>FaceEditor.Submit.cs</c> lay them out on the 1500 px appearance panel.</summary>
public sealed partial class EditorTextFitTests
{
    private static IEnumerable<FitSite> HudFrameSites() => new[]
    {
        Button("Paint editor mode switch", 168f, 36f, "ui.hudframe.mode_character", "ui.hudframe.mode_hud"),
        Label("Paint editor HUD title (wide panel)", 1452f, 22, "ui.hudframe.title"),
        Lines("Paint editor HUD hint (wide panel, three lines)", 1452f, 52f, 14, 13, 13, "ui.hudframe.hint") with { MaxLines = 3 },
        Label("Paint editor HUD tile labels", 112f, 13, "ui.hudframe.tile.*"),
        Label("Paint editor HUD active tile", 512f, 15, "ui.hudframe.tile.*"),
        Button("Paint editor HUD frame on / off", 320f, 40f, "ui.hudframe.on", "ui.hudframe.off"),
        Label("Paint editor HUD size / opacity titles", 320f, 15, "ui.hudframe.size", "ui.hudframe.opacity"),
        Button("Paint editor HUD sizes", 104f, 40f, "ui.hudframe.size_small", "ui.hudframe.size_medium", "ui.hudframe.size_large"),
        Label("Paint editor library / template titles", 226f, 15, "ui.hudframe.library", "ui.hudframe.templates", "ui.avatar.skin_templates"),
        Input("Paint editor HUD frame name", 226f, 16, "ui.hudframe.name"),
        Button("Paint editor HUD save frame", 226f, 44f, "ui.hudframe.save"),
        Wrap("Paint editor no frames (226×90)", 226f, 5, 13, "ui.hudframe.none"),
        Wrap("Paint editor frame / skin status (226×90)", 226f, 5, 13, "ui.hudframe.saved", "ui.hudframe.updated", "ui.hudframe.renamed",
            "ui.hudframe.deleted", "ui.hudframe.limit", "ui.hudframe.select_first", "ui.hudframe.name_taken", "ui.hudframe.need_name",
            "ui.hudframe.empty", "ui.hudframe.worn", "ui.hudframe.template_worn", "ui.avatar.skin_worn") with { Sample = "Lichterkette" },
        Button("Paint editor template rows", 226f, 40f, "hudframe.*.name", "skin.*.name"),
        Button("Paint editor submit", 180f, 56f, "ui.submit.button"),
        Input("Submit dialog name field (a frame's default name)", 828f, 18, "ui.hudframe.default_name"),
    };
}
