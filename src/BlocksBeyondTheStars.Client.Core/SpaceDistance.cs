// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// How the cockpit instruments (space radar, system chart) print a flight-scene distance (#1599).
/// A flight-scene unit is not a metre — the ship flies at half size, a 6 km-circumference planet is a
/// ~35-unit ball — so the readouts label it as <b>kilometres at 10 km per unit</b>: "830 km" to the next
/// planet instead of "83 m". The EVA prompt deliberately keeps metres (suit scale: board range 11 units),
/// and every on-foot distance (compass, world map, beam) is a real voxel metre and untouched.
/// </summary>
public static class SpaceDistance
{
    /// <summary>Kilometres one flight-scene unit stands for on the instruments.</summary>
    public const float KmPerUnit = 10f;

    /// <summary>The format used when the locale table has no <c>ui.space.km_fmt</c> (or hands back the key).</summary>
    public const string FallbackFormat = "{0} km";

    /// <summary>Whole kilometres for a flight-scene distance (never negative).</summary>
    public static int Km(float units) => (int)System.Math.Round(System.Math.Max(0f, units) * KmPerUnit);

    /// <summary>Digits grouped in threes with a plain space ("1 660") — the SI style, readable in every
    /// locale the game ships and free of culture lookups on the HUD path.</summary>
    public static string Group(int value)
    {
        string digits = System.Math.Abs(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (digits.Length <= 3)
        {
            return value < 0 ? "-" + digits : digits;
        }

        var sb = new StringBuilder(digits.Length + digits.Length / 3 + 1);
        if (value < 0)
        {
            sb.Append('-');
        }

        int lead = digits.Length % 3;
        if (lead > 0)
        {
            sb.Append(digits, 0, lead);
        }

        for (int i = lead; i < digits.Length; i += 3)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(digits, i, 3);
        }

        return sb.ToString();
    }

    /// <summary>The readout text for a flight-scene distance: <paramref name="format"/> is the localized
    /// <c>ui.space.km_fmt</c> ("{0} km"); anything without a <c>{0}</c> slot falls back to the default.</summary>
    public static string Label(float units, string? format)
    {
        string fmt = !string.IsNullOrEmpty(format) && format!.Contains("{0}") ? format : FallbackFormat;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, fmt, Group(Km(units)));
    }
}
