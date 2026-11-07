// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Festivals;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>The cross-checks of the festival day calendars (#3315) and the cosmetic templates their doors unlock (#3320 HUD
/// frames, #3321 skins). Whether the <c>calendar.&lt;key&gt;.name|vega</c> texts exist in English and German is checked by
/// <c>FestivalCalendarContentTests</c> (the locale tables load lazily).</summary>
public sealed partial class GameContent
{
    /// <summary>Most doors a calendar may have — the opened doors travel as one <c>int</c> bit mask.</summary>
    public const int MaxCalendarDoors = 31;

    /// <summary>The body plans only an authored companion wears (#3295 the robot, #3322 Rudolpho) — never a wild species, so
    /// an authored creature file must not use them.</summary>
    public static bool IsCompanionOnlyBodyPlan(CreatureBodyPlan plan) => plan is CreatureBodyPlan.Robot or CreatureBodyPlan.Reindeer;

    private void ValidateFestivalCalendars(List<string> problems)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in _festivalCalendars)
        {
            string ctx = $"Festival calendar '{c.Key}'";
            if (!IsSnakeCaseKey(c.Key) || !keys.Add(c.Key))
            {
                problems.Add($"{ctx}: the key must be unique lower-case letters, digits and underscores.");
            }

            if (FindFestival(c.Festival) is null)
            {
                problems.Add($"{ctx} belongs to unknown festival '{c.Festival}'.");
            }

            ValidateCalendarDates(problems, ctx, c);
            ValidateCalendarPool(problems, ctx, c);
            ValidateCalendarSpecials(problems, ctx, c);
            if (c.Layout.Length > 0 && (c.Layout.Length != c.Doors || !c.Layout.OrderBy(d => d).SequenceEqual(Enumerable.Range(1, Math.Max(0, c.Doors)))))
            {
                problems.Add($"{ctx}: the layout must be empty or list every door 1…{c.Doors} exactly once.");
            }
        }

        ValidateHudFrames(problems);
        ValidateSkins(problems);
    }

    private static void ValidateCalendarDates(List<string> problems, string ctx, FestivalCalendarDefinition c)
    {
        if (c.Doors < 1 || c.Doors > MaxCalendarDoors)
        {
            problems.Add($"{ctx}: doors must be 1…{MaxCalendarDoors}.");
        }

        if (!IsDateOfEveryYear(c.StartMonth, c.StartDay))
        {
            problems.Add($"{ctx}: start {c.StartMonth}/{c.StartDay} is not a date of every year.");
            return;
        }

        if (!IsDateOfEveryYear(c.CatchUpMonth, c.CatchUpDay))
        {
            problems.Add($"{ctx}: catch-up {c.CatchUpMonth}/{c.CatchUpDay} is not a date of every year.");
            return;
        }

        // A non-leap year: the last door's day must not lie after the catch-up end.
        if (FestivalCalendarRules.TryStartOf(c, 2001, out var start) && FestivalCalendarRules.TryCatchUpEndOf(c, 2001, out var end)
            && start.AddDays(Math.Max(1, c.Doors) - 1) > end)
        {
            problems.Add($"{ctx}: the catch-up end comes before the last door's day.");
        }
    }

    private void ValidateCalendarPool(List<string> problems, string ctx, FestivalCalendarDefinition c)
    {
        var items = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in c.Pool)
        {
            if (entry is null)
            {
                problems.Add($"{ctx}: the pool has an empty entry.");
                continue;
            }

            if (!_items.ContainsKey(entry.Item ?? string.Empty))
            {
                problems.Add($"{ctx} pool references unknown item '{entry.Item}'.");
            }
            else if (!items.Add(entry.Item!))
            {
                problems.Add($"{ctx} pool lists '{entry.Item}' twice.");
            }

            if (entry.Min < 1 || entry.Min > entry.Max || entry.Weight <= 0)
            {
                problems.Add($"{ctx} pool entry '{entry.Item}' needs 1 ≤ min ≤ max and a weight above 0.");
            }
        }

        bool everyDoorSpecial = c.Doors > 0 && Enumerable.Range(1, c.Doors).All(d => FestivalCalendarRules.SpecialOf(c, d) is not null);
        if (!everyDoorSpecial && (c.PicksPerDoor < 1 || c.PicksPerDoor > c.Pool.Count))
        {
            problems.Add($"{ctx}: picksPerDoor must be 1…{c.Pool.Count} (the pool's size).");
        }
    }

    private void ValidateCalendarSpecials(List<string> problems, string ctx, FestivalCalendarDefinition c)
    {
        var doors = new HashSet<int>();
        foreach (var s in c.Specials)
        {
            if (s is null)
            {
                problems.Add($"{ctx}: a special is empty.");
                continue;
            }

            if (s.Door < 1 || s.Door > c.Doors || !doors.Add(s.Door))
            {
                problems.Add($"{ctx}: special door {s.Door} must be one of 1…{c.Doors} and appear once.");
            }

            if (string.IsNullOrEmpty(s.Cosmetic) && s.Items.Count == 0)
            {
                problems.Add($"{ctx}: special door {s.Door} holds neither a cosmetic nor items.");
            }

            if (!string.IsNullOrEmpty(s.Cosmetic) && !CosmeticExists(s.Cosmetic))
            {
                problems.Add($"{ctx}: special door {s.Door} unlocks unknown cosmetic '{s.Cosmetic}' (hudframe:<key> in hud_frames.json or skin:<key> in skins.json).");
            }

            foreach (var item in s.Items)
            {
                if (item is null || !_items.ContainsKey(item.Item ?? string.Empty) || item.Count < 1)
                {
                    problems.Add($"{ctx}: special door {s.Door} gives unknown item '{item?.Item}' or an amount below 1.");
                }
            }
        }
    }

    /// <summary>Whether a <see cref="CosmeticKeys"/> key names a template that exists.</summary>
    public bool CosmeticExists(string? key)
    {
        if (!CosmeticKeys.IsCosmetic(key))
        {
            return false;
        }

        return key!.StartsWith(CosmeticKeys.HudFramePrefix, StringComparison.Ordinal)
            ? FindHudFrame(key.Substring(CosmeticKeys.HudFramePrefix.Length)) is not null
            : FindSkin(key.Substring(CosmeticKeys.SkinPrefix.Length)) is not null;
    }

    private void ValidateHudFrames(List<string> problems)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in _hudFrames)
        {
            string ctx = $"HUD frame '{f.Key}'";
            ValidateCosmeticHeader(problems, ctx, f.Key, f.Unlock, keys);
            foreach (var (name, tile) in new[] { ("corner", f.Corner), ("top", f.Top), ("side", f.Side), ("bottom", f.Bottom) })
            {
                if (!string.IsNullOrEmpty(tile) && (tile.Length != BodyPaint.ChunkPixels || !IsPixelAlphabet(tile)))
                {
                    problems.Add($"{ctx}: the {name} tile must be empty or {BodyPaint.ChunkPixels} pixel-alphabet characters (0-9a-v).");
                }
            }

            if (f.Twinkle.Any(i => i < 1 || i >= PaintPayload.PaletteSize))
            {
                problems.Add($"{ctx}: twinkle indices must be palette indices 1…{PaintPayload.PaletteSize - 1}.");
            }

            if (f.TwinkleColors.Any(color => !IsHexColour(color)))
            {
                problems.Add($"{ctx}: twinkle colours must be #rrggbb.");
            }
        }
    }

    private void ValidateSkins(List<string> problems)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in _skins)
        {
            string ctx = $"Skin '{s.Key}'";
            ValidateCosmeticHeader(problems, ctx, s.Key, s.Unlock, keys);
            if (s.Colors.Length is not (0 or 4) || s.Colors.Any(color => !string.IsNullOrEmpty(color) && !IsHexColour(color)))
            {
                problems.Add($"{ctx}: colors must be empty or four entries, each \"\" (keep the player's own) or #rrggbb.");
            }

            if (s.Paint.Length is not (0 or BodyPaint.PartCount))
            {
                problems.Add($"{ctx}: paint must be empty or {BodyPaint.PartCount} entries (torso, arms, legs, helmet).");
                continue;
            }

            for (int part = 0; part < s.Paint.Length; part++)
            {
                string pixels = s.Paint[part] ?? string.Empty;
                if (pixels.Length > 0 && (pixels.Length != BodyPaint.ExpectedLength(part) || !IsPixelAlphabet(pixels)))
                {
                    problems.Add($"{ctx}: paint part {part} must be empty or {BodyPaint.ExpectedLength(part)} pixel-alphabet characters.");
                }
            }
        }
    }

    private static void ValidateCosmeticHeader(List<string> problems, string ctx, string key, string unlock, HashSet<string> keys)
    {
        if (!IsSnakeCaseKey(key) || !keys.Add(key))
        {
            problems.Add($"{ctx}: the key must be unique lower-case letters, digits and underscores.");
        }

        if (!string.IsNullOrEmpty(unlock) && unlock != "calendar")
        {
            problems.Add($"{ctx}: unlock must be \"\" (free) or \"calendar\".");
        }
    }

    private static bool IsDateOfEveryYear(int month, int day)
        => month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(2001, month);

    private static bool IsHexColour(string? text)
        => text is { Length: 7 } && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit);

    /// <summary>The pixel editor's alphabet exactly as the client writes it (<c>FacePalette.Alphabet</c>, base32 <c>0-9a-v</c>,
    /// lower case) — a shipped template is data, so the upper case a player payload may carry is not accepted here.</summary>
    private static bool IsPixelAlphabet(string pixels)
        => pixels.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'v'));
}
