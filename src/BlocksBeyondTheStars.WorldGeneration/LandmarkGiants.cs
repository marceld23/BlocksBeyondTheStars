// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>One fallen guardian giant a landmark's layout carries (#3986): its place among the layout's giant markers, the
/// lying body's anchor in layout cells (<see cref="GuardianGiantBuilder"/>'s anchor, not yet moved to the world), its facing in
/// the layout's frame and whether it can still wake.</summary>
public readonly struct LandmarkGiant
{
    public LandmarkGiant(int ordinal, Vector3i anchor, int facing, bool canWake)
    {
        Ordinal = ordinal;
        Anchor = anchor;
        Facing = facing;
        CanWake = canWake;
    }

    /// <summary>Its place among the layout's giant markers (0, 1, …).</summary>
    public int Ordinal { get; }

    /// <summary>The lying body's anchor in layout cells.</summary>
    public Vector3i Anchor { get; }

    /// <summary>The lying body's facing (0–3) in the layout's frame.</summary>
    public int Facing { get; }

    /// <summary>Whether it can still wake.</summary>
    public bool CanWake { get; }
}

/// <summary>
/// The fallen guardian giants of a landmark (#3986): every <see cref="LandmarkRules.GuardianGiantMarker"/> of a built
/// <see cref="LandmarkLayout"/>, read into the anchor and facing <see cref="GuardianGiantBuilder"/> lays the body with. Pure:
/// the stamp writes the bodies from it and every later load re-derives the giants' records from it.
/// <para><b>The marker</b> stands on the floor the body lies on (like a chest, so the ruin pass seats it on what is left); the
/// anchor lies <see cref="GuardianGiantBuilder.AnchorAboveGround"/> rows over that floor. Its <c>facing</c> is the body's
/// facing in template coordinates.</para>
/// <para><b>The turn.</b> A landmark's quarter turn takes +Z to −X (<see cref="LandmarkGenerator.Turn"/>), a giant's facing
/// turns +Z to +X (<see cref="GuardianGiantBody.Face"/>), so the facing in the turned layout is the template's minus the
/// landmark's turns. The body turns about its anchor's corner while the layout turns cells, so the turned anchor sits one
/// cell over from the turned marker cell along the axes the turn reversed — every cell of the body lands exactly where turning
/// the unturned landmark's body would put it.</para>
/// </summary>
public static class LandmarkGiants
{
    /// <summary>The most giants one landmark carries (the server numbers their records within this many per landmark); further
    /// markers are ignored.</summary>
    public const int MaxPerLandmark = 16;

    /// <summary>The giants of a built layout, in marker order (at most <see cref="MaxPerLandmark"/>).</summary>
    public static List<LandmarkGiant> Of(LandmarkLayout layout)
    {
        var giants = new List<LandmarkGiant>();
        int turns = ((layout.Turns % 4) + 4) % 4;
        var (cx, cz) = CornerShift(turns);
        foreach (var m in layout.Structure.Markers)
        {
            if (m.Type != LandmarkRules.GuardianGiantMarker)
            {
                continue;
            }

            if (giants.Count >= MaxPerLandmark)
            {
                break;
            }

            var settings = MarkerSettings.Parse(m.Data);
            int facing = settings.GetInt(LandmarkRules.GuardianGiantFacingSetting, 0) - turns;
            var anchor = new Vector3i(m.LocalPos.X + cx, m.LocalPos.Y - 1 + GuardianGiantBuilder.AnchorAboveGround, m.LocalPos.Z + cz);
            giants.Add(new LandmarkGiant(giants.Count, anchor, ((facing % 4) + 4) % 4,
                settings.GetBool(LandmarkRules.GuardianGiantWakeSetting)));
        }

        return giants;
    }

    /// <summary>Where the low corner of a template cell lands after <paramref name="turns"/> quarter turns, relative to the low
    /// corner of the turned cell: one turn moves it to the cell's +X side, two to the +X +Z corner, three to the +Z side.</summary>
    private static (int X, int Z) CornerShift(int turns) => turns switch
    {
        1 => (1, 0),
        2 => (1, 1),
        3 => (0, 1),
        _ => (0, 0),
    };
}
