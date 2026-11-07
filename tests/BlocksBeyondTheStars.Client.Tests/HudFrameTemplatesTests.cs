// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;
using BlocksBeyondTheStars.Client.HudFrames;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Festivals;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The shipped HUD frame templates (<c>data/hud_frames.json</c>, written by <c>tools/gen_hud_frames.py</c>, #3320): every
/// one loads into an editable design, the Christmas lights wait for the calendar while at least one frame is free for
/// everybody, the lights twinkle, the bottom stays light (the hotbar lives there) and red stays an accent — it is the
/// enemies' colour in this game.
/// </summary>
public sealed class HudFrameTemplatesTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));

    private static IReadOnlyList<HudFrameDefinition> Templates => Content.Value.HudFrames;

    /// <summary>The palette's reds: red, dark red, light red.</summary>
    private static readonly int[] Reds = { 8, 22, 23 };

    [Fact]
    public void EveryTemplate_IsAValidFrame_WithAUniqueKey()
    {
        Assert.NotEmpty(Templates);
        Assert.Equal(Templates.Count, Templates.Select(t => t.Key).Distinct(StringComparer.Ordinal).Count());
        foreach (var template in Templates)
        {
            Assert.True(HudFrameDesign.TryFromTemplate(template, out var design), template.Key);
            Assert.False(design.IsEmpty, template.Key);
            Assert.True(template.Unlock is "" or "calendar", template.Key + ": unlock is empty or 'calendar'");
            Assert.Equal(design.Encode(), HudFrameDesign.Parse(design.Encode()).Encode());
        }

        Assert.Contains(Templates, t => t.Unlock.Length == 0); // the main menu always has something to start from
    }

    [Fact]
    public void TheChristmasLights_WaitForTheCalendar_Twinkle_AndKeepTheBottomLight()
    {
        var lights = Content.Value.FindHudFrame("christmas_lights");
        Assert.NotNull(lights);
        Assert.Equal("calendar", lights!.Unlock);
        Assert.True(HudFrameDesign.TryFromTemplate(lights, out var design));
        Assert.InRange(design.Twinkle.Count, 3, HudFrameDesign.MaxTwinkle);
        Assert.InRange(design.TwinkleColors.Count, 3, HudFrameDesign.MaxTwinkle);

        int lit = 0;
        foreach (HudFrameTile tile in Enum.GetValues(typeof(HudFrameTile)))
        {
            lit += design.TileGrid(tile).Count(i => design.TwinkleSlot(i) >= 0);
        }

        Assert.True(lit >= 8, "the garland carries a string of lights");
        Assert.True(Painted(design, HudFrameTile.Bottom) * 3 < Painted(design, HudFrameTile.Top), "the bottom edge stays light");
        Assert.All(design.Twinkle, index => Assert.DoesNotContain(index, Reds)); // a red light is a twinkle COLOUR, not a bulb
    }

    [Fact]
    public void EveryTemplate_KeepsTheMiddleOfTheViewClear_AndRedAnAccent()
    {
        foreach (var template in Templates)
        {
            Assert.True(HudFrameDesign.TryFromTemplate(template, out var design));
            int painted = 0, red = 0;
            foreach (HudFrameTile tile in Enum.GetValues(typeof(HudFrameTile)))
            {
                int[] grid = design.TileGrid(tile);
                painted += grid.Count(i => i != 0);
                red += grid.Count(i => Reds.Contains(i));
            }

            Assert.True(red * 10 <= painted, $"{template.Key}: red is an accent ({red} of {painted} pixels)");
            Assert.True(painted * 2 < HudFrameDesign.TilePixels * HudFrameDesign.TileCount, $"{template.Key}: the tiles are mostly transparent");
        }
    }

    [Fact]
    public void EveryTemplateName_IsLocalisedInEnglishAndGerman()
    {
        foreach (string locale in new[] { "en", "de" })
        {
            var table = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", locale + ".json"), Encoding.UTF8));
            Assert.All(Templates, t => Assert.True(table.ContainsKey(t.NameKey), $"{locale}.json lacks {t.NameKey}"));
        }
    }

    private static int Painted(HudFrameDesign design, HudFrameTile tile) => design.TileGrid(tile).Count(i => i != 0);
}
