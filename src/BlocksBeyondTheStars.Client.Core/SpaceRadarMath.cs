// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// The space radar's geometry and the flight readouts that carry height (#1880, #1881). Flight is full 3-D, but the
/// radar is a flat disc — so the disc is turned with the pilot's HEADING (the view direction flattened onto the
/// flight plane) and height is reported separately as an ▲/▼ cue with its own distance. Projecting onto the tilted
/// chase camera instead let a wreck 229 units overhead sit dead centre and read as "behind you".
/// </summary>
public static class SpaceRadarMath
{
    /// <summary>Flight units a target must sit above or below the pilot before it counts as "not level" — the
    /// threshold the nearest-station readout has used since it got its arrow.</summary>
    public const float VerticalCueUnits = 10f;

    public const string UpGlyph = "▲";
    public const string DownGlyph = "▼";

    /// <summary>Radar-disc offset of a target in the heading frame: <c>Right</c> along the pilot's right, <c>Ahead</c>
    /// along the heading. <paramref name="viewX"/>/<paramref name="viewZ"/> are the horizontal components of the
    /// view direction; when those vanish (looking straight up or down) <paramref name="fallbackX"/>/
    /// <paramref name="fallbackZ"/> — the last good heading — turn the disc instead. Height is deliberately ignored.</summary>
    public static (float Right, float Ahead) Project(float dx, float dz, float viewX, float viewZ, float fallbackX = 0f, float fallbackZ = 1f)
    {
        Heading(viewX, viewZ, fallbackX, fallbackZ, out float fx, out float fz);
        // right = up × forward = (fz, 0, -fx) in the flight scene's left-handed frame.
        return (dx * fz - dz * fx, dx * fx + dz * fz);
    }

    /// <summary>The unit heading on the flight plane for a view direction, or the (normalised) fallback when the
    /// view has no horizontal component worth trusting.</summary>
    public static void Heading(float viewX, float viewZ, float fallbackX, float fallbackZ, out float x, out float z)
    {
        float len = (float)System.Math.Sqrt(viewX * viewX + viewZ * viewZ);
        if (len < 1e-3f)
        {
            viewX = fallbackX;
            viewZ = fallbackZ;
            len = (float)System.Math.Sqrt(viewX * viewX + viewZ * viewZ);
            if (len < 1e-3f)
            {
                viewX = 0f;
                viewZ = 1f;
                len = 1f;
            }
        }

        x = viewX / len;
        z = viewZ / len;
    }

    /// <summary>+1 when the target is above the pilot, −1 below, 0 roughly level.</summary>
    public static int VerticalState(float dy) => dy > VerticalCueUnits ? 1 : dy < -VerticalCueUnits ? -1 : 0;

    /// <summary>The ▲ / ▼ glyph for a <see cref="VerticalState"/>; empty when level.</summary>
    public static string Glyph(int state) => state > 0 ? UpGlyph : state < 0 ? DownGlyph : string.Empty;

    /// <summary>A distance readout with the height folded in: "2 300 km · ▲ 2 290 km" when the target is off level,
    /// just "2 300 km" when it is level. <paramref name="kmFormat"/> is the localized <c>ui.space.km_fmt</c>.</summary>
    public static string DistanceWithHeight(float distance, float dy, string? kmFormat)
    {
        string label = SpaceDistance.Label(distance, kmFormat);
        int state = VerticalState(dy);
        return state == 0 ? label : label + " · " + Glyph(state) + " " + SpaceDistance.Label(System.Math.Abs(dy), kmFormat);
    }

    /// <summary>Signed whole kilometres of a height (the instruments' change detector keys on it).</summary>
    public static int SignedKm(float y) => y < 0f ? -SpaceDistance.Km(-y) : SpaceDistance.Km(y);

    /// <summary>The instruments' ALT value (#1881): the pilot's height over the flight plane (y = 0, where the
    /// system's planets, stations and wrecks sit), signed, in instrument kilometres — "+120 km", "-2 170 km", "0 km".</summary>
    public static string Altitude(float y, string? kmFormat)
    {
        int km = SignedKm(y);
        string label = SpaceDistance.Label(System.Math.Abs(y), kmFormat);
        return km > 0 ? "+" + label : km < 0 ? "-" + label : label;
    }
}
