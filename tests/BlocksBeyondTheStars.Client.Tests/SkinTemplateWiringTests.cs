// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3770: a skin template may carry a face (a player's submitted skin is a whole look, adopted as a template for everybody).
/// The data rule is the server suite's (<c>FestivalCalendarContentTests</c>); here the Unity wiring is guarded as source
/// text (the scripts cannot load headless — the <see cref="ClientQuirksWiringTests"/> pattern): putting a template on
/// takes its face along, in the main menu's designer and in a world, and the status line says which face is worn.
/// </summary>
public sealed class SkinTemplateWiringTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    [Fact]
    public void PuttingATemplateOn_TakesItsFaceAlong()
    {
        string templates = Script("AppearanceTemplates.cs");
        Assert.Contains("if (skin.HasFace)", templates);
        Assert.Contains("look.FacePixels = skin.Face;", templates);
        Assert.Contains("skin.HasFace ? \"ui.avatar.skin_worn_face\" : \"ui.avatar.skin_worn\"", templates);

        // In a world the face is its own message (the paintings go through the paced queue, the face through ApplyFace).
        string world = Script("GameMenu.Templates.cs");
        Assert.Contains("ApplyFace(look.FacePixels);", world);
        Assert.Contains("L(AppearanceTemplates.WornKey(skins[index]))", world);

        // The main menu's designer puts the whole scratch look on — face included.
        Assert.Contains("AppearanceTemplates.ApplySkin(skins[index], look);", Script("AvatarEditor.Templates.cs"));
    }

    [Fact]
    public void BothStatusLines_ExistInEnglishAndGerman_AndTheFreeTemplateHasAName()
    {
        foreach (string code in new[] { "en", "de" })
        {
            var table = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json")));
            foreach (string key in new[] { "ui.avatar.skin_worn", "ui.avatar.skin_worn_face", "skin.green_spacesuit.name" })
            {
                Assert.True(table.TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text), code + ": " + key);
            }

            Assert.Contains("{name}", table["ui.avatar.skin_worn_face"]);
        }
    }
}
