// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Runtime.CompilerServices;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Terrain generation 18 (#2106) — the oil pockets, partial of <see cref="WorldGenerator"/>. Justus asked for oil; Marcel's
/// rule: it lies underground on the LIVING worlds only (<see cref="PlanetType.HasLife"/> — plants and animals, oil is old
/// life), in pockets of its own beside the caves so that no cave is ever flooded. A pocket is a flattened ellipsoid
/// 40–120 below the base height, wrapped in a shell of tar (the block the tar pits already use) and filled with oil — the
/// same self-sealing shape as the crystal geode (#1646): its branch in the column's y-loop runs before the tunnels and the
/// blob caves, and a column that only grazes the ellipsoid is all shell, so the carvers never open it. The mega-cavern
/// runs earlier and wins where the two overlap; that exposes still oil in a cavern wall, which is a find, not a flood —
/// the oil block is <see cref="BlockDefinition.Liquid"/>: a finite deposit the automaton never moves.
/// Deterministic: a pure function of the hotspot cell, and gated on <see cref="WonderProfile.OilPockets"/>, which no
/// world below generation 18 ever sets. Kept in its own file behind non-inlined helpers (#1740). Generation 22 gives the
/// pockets their designed height and lets some of them seep (<c>WorldGenerator.OilSeepsGen22.cs</c>).
/// </summary>
public sealed partial class WorldGenerator
{
    // One hotspot cell of 600 columns rolls a pocket with chance 0.25 — a living world of the default size (50 cells)
    // carries about a dozen (7–21 measured), ~600 blocks apart, so each is a find. Radii are in blocks; the depth band
    // starts below every topsoil.
    private const double OilCellSize = 600.0;
    private const double OilChance = 0.25;
    private const double OilMinRadius = 6.0;
    private const double OilMaxRadius = 14.0;
    private const double OilMinHalfHeight = 3.0;
    private const double OilMaxHalfHeight = 7.0;
    private const double OilShell = 1.6;   // the tar rim, thick enough that an axis neighbour's outer span covers the inner one
    private const int OilTopClearance = 4; // the pocket's shell never rises closer than this to the local ground

    /// <summary>The salt of the pocket hotspot grid (the pocket function and the oil echo share it).</summary>
    private const long OilHotspotSalt = 0x01A5EED;

    /// <summary>Whether this world carries oil pockets at all: a living world (plants and animals) with a real
    /// underground — not void, not a cratered body.</summary>
    private bool HasOilPockets(PlanetType planet) => planet.HasLife && !planet.Void && !planet.Cratered && !_crateredWorld;

    /// <summary>Test seam: the oil pocket spans at a column of this world (the ground taken as the column's surface
    /// height), false where none covers it or the world carries no pockets.</summary>
    public bool TryGetOilPocketSpanForTest(PlanetType planet, int worldX, int worldZ,
        out int yLo, out int yHi, out int innerLo, out int innerHi)
    {
        var w = WonderFor(planet);
        yLo = yHi = 0;
        innerLo = 1;
        innerHi = 0;
        return w.OilPockets && HasOilPockets(planet)
            && TryGetOilPocketSpan(planet, w, worldX, worldZ, SurfaceHeight(planet, worldX, worldZ), out yLo, out yHi, out innerLo, out innerHi);
    }

    /// <summary>The oil pocket covering this column: the full ellipsoid span (tar shell) and the inner span (oil), both
    /// clamped under the local ground so a valley never opens the pocket. False when no pocket covers the column or the
    /// clamped shell would have no room. Empty inner span (Lo &gt; Hi) = the column only grazes the shell.</summary>
    private bool TryGetOilPocketSpan(PlanetType planet, WonderProfile w, int worldX, int worldZ, int seabedY,
        out int yLo, out int yHi, out int innerLo, out int innerHi)
        => TryGetOilPocketSpan(planet, w, worldX, worldZ, seabedY, out yLo, out yHi, out innerLo, out innerHi, out _, out _);

    /// <summary>As above, plus the pocket's seep on this column (generation 22, #2371): every cell from
    /// <paramref name="seepLo"/> up to the ground is the seep's tar (int.MaxValue = no seep cell here), and
    /// <paramref name="seepPuddle"/> marks the columns whose ground cell is the oil puddle when no water stands over it.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryGetOilPocketSpan(PlanetType planet, WonderProfile w, int worldX, int worldZ, int seabedY,
        out int yLo, out int yHi, out int innerLo, out int innerHi, out int seepLo, out bool seepPuddle)
    {
        yLo = yHi = 0;
        innerLo = 1;
        innerHi = 0;
        seepLo = int.MaxValue;
        seepPuddle = false;
        if (!TryGetHotspot(w.Seed ^ OilHotspotSalt, OilCellSize, OilChance, OilMaxRadius + 4.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double rx = OilMinRadius + ((h >> 16) & 0x3FF) / 1023.0 * (OilMaxRadius - OilMinRadius);
        double ry = OilMinHalfHeight + OilHalfHeightByte(w, h) / 255.0 * (OilMaxHalfHeight - OilMinHalfHeight);
        double q = 1.0 - (dx * dx + dz * dz) / (rx * rx);
        if (q <= 0.0)
        {
            return false;
        }

        int cy = OilPocketCentreY(planet, h);
        double half = ry * System.Math.Sqrt(q);
        yLo = cy - (int)half;
        yHi = System.Math.Min(cy + (int)half, seabedY - OilTopClearance);
        if (yHi < yLo)
        {
            return false; // the ground dips below the pocket here — no room for a sealed shell
        }

        double rxi = rx - OilShell, ryi = ry - OilShell;
        if (rxi > 0.0 && ryi > 0.0)
        {
            double qi = 1.0 - (dx * dx + dz * dz) / (rxi * rxi);
            if (qi > 0.0)
            {
                double hi = ryi * System.Math.Sqrt(qi);
                innerLo = cy - (int)hi;
                innerHi = System.Math.Min(cy + (int)hi, yHi - (int)System.Math.Ceiling(OilShell));
            }
        }

        if (w.OilSeeps)
        {
            OilSeepSpan(h, dx, dz, yHi, seabedY, out seepLo, out seepPuddle);
        }

        return true;
    }

    /// <summary>The byte the half-height is scaled from. Below generation 22 it is bits 8–15 — but the hotspot keeps a cell
    /// only when <c>(h &amp; 0xFFFF) &lt; 0x4000</c>, so bits 14 and 15 are always clear and the half-height never left
    /// 3.0–3.99 (#2370). Generation 22 reads bits 56–63, which no other roll of the pocket uses (0–15 chance, 16–25 radius,
    /// 26–32 depth, 33–35 the seep, 36–55 the hotspot offset).</summary>
    private static ulong OilHalfHeightByte(WonderProfile w, ulong h) => w.OilFullHeight ? (h >> 56) & 0xFF : (h >> 8) & 0xFF;

    /// <summary>The pocket's centre height: 40..120 below the planet's base height.</summary>
    private static int OilPocketCentreY(PlanetType planet, ulong h) => planet.BaseHeight - 40 - (int)((h >> 26) & 0x7F) % 81;
}
