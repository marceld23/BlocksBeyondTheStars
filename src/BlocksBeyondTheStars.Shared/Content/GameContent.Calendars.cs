// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Festivals;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>The festival day calendars (#3315) and the cosmetic templates their doors unlock (#3320 HUD frames, #3321
/// skins). Each file is optional: absent → no calendars / no templates.</summary>
public sealed partial class GameContent
{
    private IReadOnlyList<FestivalCalendarDefinition> _festivalCalendars = new List<FestivalCalendarDefinition>();
    private IReadOnlyList<HudFrameDefinition> _hudFrames = new List<HudFrameDefinition>();
    private IReadOnlyList<SkinDefinition> _skins = new List<SkinDefinition>();

    /// <summary>The festival day calendars, in the file's order.</summary>
    public IReadOnlyList<FestivalCalendarDefinition> FestivalCalendars => _festivalCalendars;

    /// <summary>The HUD frame templates, in the file's order.</summary>
    public IReadOnlyList<HudFrameDefinition> HudFrames => _hudFrames;

    /// <summary>The character skin templates, in the file's order.</summary>
    public IReadOnlyList<SkinDefinition> Skins => _skins;

    /// <summary>The calendar <paramref name="key"/>, or null.</summary>
    public FestivalCalendarDefinition? FindFestivalCalendar(string? key)
        => FindByKey(_festivalCalendars, key, c => c.Key);

    /// <summary>The HUD frame template <paramref name="key"/> (without the <c>hudframe:</c> prefix), or null.</summary>
    public HudFrameDefinition? FindHudFrame(string? key) => FindByKey(_hudFrames, key, f => f.Key);

    /// <summary>The skin template <paramref name="key"/> (without the <c>skin:</c> prefix), or null.</summary>
    public SkinDefinition? FindSkin(string? key) => FindByKey(_skins, key, s => s.Key);

    public void SetFestivalCalendars(FestivalCalendarCatalog? catalog)
    {
        var list = new List<FestivalCalendarDefinition>();
        foreach (var c in catalog?.Calendars ?? new List<FestivalCalendarDefinition>())
        {
            if (c == null || string.IsNullOrWhiteSpace(c.Key))
            {
                continue;
            }

            c.Pool ??= new List<CalendarPoolEntry>();
            c.Specials ??= new List<CalendarSpecial>();
            foreach (var s in c.Specials)
            {
                s.Items ??= new List<ItemAmount>();
                s.Cosmetic ??= string.Empty;
            }

            c.Layout ??= Array.Empty<int>();
            list.Add(c);
        }

        _festivalCalendars = list;
    }

    public void SetHudFrames(HudFrameCatalog? catalog)
    {
        var list = new List<HudFrameDefinition>();
        foreach (var f in catalog?.Frames ?? new List<HudFrameDefinition>())
        {
            if (f != null && !string.IsNullOrWhiteSpace(f.Key))
            {
                f.Twinkle ??= Array.Empty<int>();
                f.TwinkleColors ??= Array.Empty<string>();
                list.Add(f);
            }
        }

        _hudFrames = list;
    }

    public void SetSkins(SkinCatalog? catalog)
    {
        var list = new List<SkinDefinition>();
        foreach (var s in catalog?.Skins ?? new List<SkinDefinition>())
        {
            if (s != null && !string.IsNullOrWhiteSpace(s.Key))
            {
                s.Colors ??= Array.Empty<string>();
                s.Paint ??= Array.Empty<string>();
                list.Add(s);
            }
        }

        _skins = list;
    }

    private static T? FindByKey<T>(IReadOnlyList<T> list, string? key, Func<T, string> keyOf)
        where T : class
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        foreach (var item in list)
        {
            if (string.Equals(keyOf(item), key, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }
}
