// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// UNDERWATER CIVILIZATION SITES (#3640): a sunken site sits on the sea floor (<see cref="LandmarkRules.SeatSeabed"/>) and is
/// written in the water mode instead of the landmark's air mode:
/// <list type="bullet">
/// <item><b>The seat</b>: a footprint whose every sampled column lies under liquid water, the one needing the least digging
/// (then the flattest floor, then the one with the least water to spare — a site in a deep trench is never found) among a fixed number of draws from the site's seed, clear of the pads and every footprint. #3700: "digging" is the
/// CUT — how far the site's ground lies below the highest natural floor of the footprint, the wall the dig leaves (a deep
/// sea over a flat floor cuts nothing; a sandbank or one trench under a bumpy floor cuts deep) — and a footprint cutting at
/// most <see cref="SunkenPreferredCut"/> blocks always beats one cutting more (<see cref="SeabedScore"/>). The
/// ground row rests on the deepest floor of the footprint; when the sea is too shallow for the site plus
/// <see cref="SunkenHeadroom"/> blocks of water over its top, the ground row sinks into the floor (at most
/// <see cref="SunkenMaxDig"/> blocks) — the site lies in a basin.</item>
/// <item><b>The crater rim</b> (#3700): a site dug in no longer stands in a shaft with sheer walls. Around its footprint the
/// natural sea floor is lowered to a slope — one block down per block toward the footprint, from the site's ground up —
/// out to <see cref="LandmarkInstance.SunkenRim"/> blocks (<see cref="SunkenRimFor"/>, pinned in the record). Only columns whose
/// natural floor lies under the water are lowered (an island is never carved), never another structure's footprint or a
/// pad; the rim's top keeps the floor's own surface block, the flora that stood on the old floor goes, and the rim's reach
/// is registered with the footprint so what is placed later keeps clear of the slope.</item>
/// <item><b>The cells</b>: every column of the footprint is written from the ground row up to the higher of its top and the
/// natural sea floor (the basin is dug), the site's blocks as they are, every other cell WATER at and below the water top —
/// never air, so no dry pocket opens in the sea — except the template's AIR POCKETS (<see cref="CivilizationRules.AirKind"/>),
/// which stay air: a diving bell a player can breathe in. Under the ground row its blocks, and its cavities flooded. No
/// stone seal against water.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Water left over a sunken site's top (blocks) — the shared value the editor's stamping panel names too.</summary>
    internal const int SunkenHeadroom = CivilizationRules.SunkenHeadroom;

    /// <summary>How deep a sunken site may dig into the sea floor below its deepest natural point.</summary>
    internal const int SunkenMaxDig = CivilizationRules.SunkenMaxDig;

    /// <summary>How many footprints a sunken site's seat search draws (#3700: twice as many as before, so a deep enough spot
    /// is found where the body has one).</summary>
    private const int SunkenSeatDraws = 480;

    /// <summary>#3700: a footprint whose cut (see the class summary) is at most this deep always beats a deeper one.</summary>
    internal const int SunkenPreferredCut = 10;

    /// <summary>#3700: how far past the highest sampled sea floor of the footprint the crater rim reaches, and its cap.</summary>
    private const int SunkenRimPad = 4;
    internal const int SunkenRimMax = 48;

    /// <summary>Seats a site of the civilization: the sea floor for an underwater one, else the guaranteed search with the
    /// footprint registry (like a landmark; one with an underground part needs dry, even ground).</summary>
    private bool TrySeatCivilizationSite(CivilizationDefinition civ, LandmarkPlan plan)
    {
        if (civ.Scope.Underwater)
        {
            return TrySeatSeabed(plan);
        }

        var layout = plan.Layout!;
        bool buried = layout.GroundRow > 0;
        var policy = new SeatPolicy(AllowStilts: false, AllowLava: false, PreferRugged: false);
        // Only a graded seat: on stilts (over water) the site grade levels nothing, and the writer clears a column only up to
        // its top block — a maze's passages (their top is the paving) would stay full of the land under them.
        bool SeatOk(Vector3i origin, string seat) => buried ? seat is "flat" or "slope" : seat is "flat" or "slope" or "shelf";
        if (!TryPlaceStructureGuaranteed(layout.Structure, RngFor(plan.Seed, "search"),
                KeepClearFor(StructureTypeRules.Civilization, LandmarkReserved()), wantIsland: false, policy,
                avoidPlayerEdits: false, out var origin, out int groundY, out _, out string seatStyle, seatOk: SeatOk,
                siteKind: StructureTypeRules.Civilization))
        {
            return false;
        }

        (plan.OriginX, plan.OriginZ, plan.GroundY, plan.Seat) = (origin.X, origin.Z, groundY, seatStyle);
        return true;
    }

    /// <summary>The sea-floor seat (see the class summary). False when no draw lies fully under water.</summary>
    private bool TrySeatSeabed(LandmarkPlan plan)
    {
        var s = plan.Layout!.Structure;
        int w = s.Width, l = s.Length, above = s.Height - plan.Layout.GroundRow;
        int latBand = System.Math.Max(8, (WorldConstants.LatitudePeriodFor(_world.Circumference) / 2) - (System.Math.Max(w, l) / 2) - 16);
        var reserved = KeepClearFor(StructureTypeRules.Civilization, LandmarkReserved());
        var rng = RngFor(plan.Seed, "seabed");
        (int Score, int Ox, int Oz, int Top, int Bed, int BedMax)? best = null;
        for (int draw = 0; draw < SunkenSeatDraws; draw++)
        {
            int cx = rng.Next(0, _world.Circumference), cz = rng.Next(-latBand, latBand + 1);
            if (OverlapsFootprint(cx, cz, (w / 2) + 1, (l / 2) + 1, reserved, SettlementCollisionMargin))
            {
                continue;
            }

            int ox = cx - (w / 2), oz = cz - (l / 2);
            if (!SeabedUnder(ox, oz, w, l, out int top, out int bedMin, out int bedMax))
            {
                continue;
            }

            int need = above + SunkenHeadroom, depth = top - bedMin;
            int ground = SunkenGround(top, bedMin, above);
            int cut = System.Math.Max(0, bedMax + 1 - ground), excess = System.Math.Max(0, depth - need);
            int score = SeabedScore(cut, excess);
            if (best is null || score > best.Value.Score)
            {
                best = (score, ox, oz, top, bedMin, bedMax);
            }
        }

        if (best is not { } pick)
        {
            return false;
        }

        int groundY = SunkenGround(pick.Top, pick.Bed, above);
        (plan.OriginX, plan.OriginZ, plan.GroundY, plan.Seat, plan.WaterTop) = (pick.Ox, pick.Oz, groundY, LandmarkRules.SeatSeabed, pick.Top);
        plan.SunkenRim = SunkenRimFor(pick.BedMax, groundY);
        return true;
    }

    /// <summary>The ground row's world Y on a footprint with the water top <paramref name="top"/> and the deepest floor
    /// <paramref name="bedMin"/>: on that floor, sunk further when the sea is too shallow for the site's
    /// <paramref name="above"/> rows plus the headroom — at most <see cref="SunkenMaxDig"/> blocks.</summary>
    private static int SunkenGround(int top, int bedMin, int above)
        => System.Math.Max(System.Math.Min(bedMin + 1, top - above - SunkenHeadroom + 1), bedMin + 1 - SunkenMaxDig);

    /// <summary>#3700: a sea-floor footprint's score (higher is better): the shallowest cut through the natural floor, then
    /// the least water to spare (a site in a deep trench is never found) — and a footprint cutting deeper than
    /// <see cref="SunkenPreferredCut"/> below every one that cuts less.</summary>
    internal static int SeabedScore(int cut, int excess)
        => (cut > SunkenPreferredCut ? -1_000_000 : 0) - ((cut * 2) + (excess / 4));

    /// <summary>#3700: how far the crater rim reaches around a sunken site whose ground lies at <paramref name="groundY"/>, the
    /// highest sampled natural floor of its footprint being <paramref name="bedMax"/>: as far as the slope needs to climb back
    /// to that floor, plus <see cref="SunkenRimPad"/>, at most <see cref="SunkenRimMax"/>.</summary>
    internal static int SunkenRimFor(int bedMax, int groundY)
        => System.Math.Clamp(bedMax - groundY + SunkenRimPad, 0, SunkenRimMax);

    /// <summary>Whether every sampled column of the footprint lies under liquid water: the lowest water top and the lowest and
    /// highest sea floor of the samples.</summary>
    private bool SeabedUnder(int ox, int oz, int w, int l, out int top, out int bedMin, out int bedMax)
    {
        top = int.MaxValue;
        bedMin = int.MaxValue;
        bedMax = int.MinValue;
        var planet = _world.Planet;
        foreach (var (x, z) in FootprintSamples(ox, oz, w, l))
        {
            if (!_generator.TryGetWaterSurface(planet, x, z, out int waterTop, out int bed))
            {
                return false;
            }

            top = System.Math.Min(top, waterTop);
            bedMin = System.Math.Min(bedMin, bed);
            bedMax = System.Math.Max(bedMax, bed);
        }

        return top != int.MaxValue && top > bedMax;
    }

    /// <summary>Writes a sunken site in the water mode (see the class summary). Returns the cells written.</summary>
    private int WriteSunkenLandmark(IStructureCellSink sink, LandmarkInstance lm, int waterTop)
    {
        var water = _content.GetBlock("water")?.NumericId ?? new BlockId(0);
        int n = 0;
        for (int x = 0; x < lm.Structure.Width; x++)
        {
            for (int z = 0; z < lm.Structure.Length; z++)
            {
                n += WriteSunkenColumn(sink, lm, x, z, waterTop, water);
            }
        }

        return n + WriteSunkenUnderground(sink, lm, water) + WriteSunkenRim(sink, lm, waterTop, water);
    }

    /// <summary>One column from the ground row up to the higher of its top and the natural sea floor (the basin), then the
    /// plinth under a standing foot.</summary>
    private int WriteSunkenColumn(IStructureCellSink sink, LandmarkInstance lm, int x, int z, int waterTop, BlockId water)
    {
        var s = lm.Structure;
        var layout = lm.Layout;
        int g = layout.GroundRow, top = -1;
        for (int y = s.Height - 1; y >= g && top < 0; y--)
        {
            if (s.Get(x, y, z) != 0 || layout.IsCavity(x, y, z))
            {
                top = y;
            }
        }

        int wx = lm.Origin.X + x, wz = lm.Origin.Z + z;
        int bed = _generator.SurfaceHeight(_world.Planet, wx, wz);
        int highest = System.Math.Max(top < 0 ? int.MinValue : lm.Origin.Y + top, bed);
        int n = 0;
        for (int wy = lm.Origin.Y + g; wy <= highest; wy++, n++)
        {
            int y = wy - lm.Origin.Y;
            if (y < s.Height && s.Get(x, y, z) != 0)
            {
                SetLandmarkCell(sink, lm, x, y, z);
            }
            else
            {
                bool air = (y < s.Height && layout.IsAirPocket(x, y, z)) || wy > waterTop;
                sink.Put(wx, wy, wz, air ? new BlockId(0) : water);
            }
        }

        n += highest >= lm.Origin.Y + g ? FloodFloatingFlora(sink, wx, highest + 1, wz, waterTop, water) : 0;
        if (s.Get(x, g, z) != 0)
        {
            n += WriteLandmarkPlinth(sink, lm, x, z, LandmarkPlinthDepth);
        }

        return n;
    }

    /// <summary>The sea floor's own flora (coral, kelp) that stood on the floor the column just dug away or covered would float
    /// in the water above it: from <paramref name="fromY"/> up, every cell that is neither water nor air nor solid becomes
    /// water, until the first water or air cell (or the water top). Returns the cells written.</summary>
    private int FloodFloatingFlora(IStructureCellSink sink, int wx, int fromY, int wz, int waterTop, BlockId water)
    {
        int n = 0;
        for (int wy = fromY; wy <= waterTop; wy++, n++)
        {
            var here = sink.Get(wx, wy, wz);
            if (here.Value == 0 || here.Value == water.Value || _content.BlockById(here) is not { Solid: false })
            {
                break; // the open water above, or something that stands on its own
            }

            sink.Put(wx, wy, wz, water);
        }

        return n;
    }

    /// <summary>The rows under the ground row: the site's blocks, its cavities flooded (an air pocket stays air); the planet's
    /// own rock everywhere else. No seals — water against water needs none.</summary>
    private int WriteSunkenUnderground(IStructureCellSink sink, LandmarkInstance lm, BlockId water)
    {
        var s = lm.Structure;
        var layout = lm.Layout;
        int n = 0;
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < layout.GroundRow; y++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    if (s.Get(x, y, z) != 0)
                    {
                        SetLandmarkCell(sink, lm, x, y, z);
                        n++;
                    }
                    else if (layout.IsCavity(x, y, z))
                    {
                        var cell = lm.Cell(new Vector3i(x, y, z));
                        sink.Put(cell.X, cell.Y, cell.Z, layout.IsAirPocket(x, y, z) ? new BlockId(0) : water);
                        n++;
                    }
                }
            }
        }

        return n;
    }
}
