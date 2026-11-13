// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Terrain generation 40 (#3555, the geography package), the server's half:
/// <list type="bullet">
/// <item>the placement search tries its first-ring candidates best-first by the kind's site rules (#3564) — the candidates,
/// the random draws and every hard check stay the classic ones, only the order changes;</item>
/// <item>a fresh stamp reserves its ground (#3569): the footprint and its grade join the world's site flats before the stamp
/// writes, the generator levels and grades them from then on, the chunks that stood there already are regenerated, and the
/// list is pinned in the save, read back in the landing-pad stage on every later load;</item>
/// <item>a structure builds its foundation with the ground of the biome it stands on (#3565).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>True on a world of the geography generation.</summary>
    private bool GeographyWorld => _meta.Description.TerrainGeneration >= WorldDescription.GeographyGeneration;

    /// <summary>While true, a fresh stamp may reserve ground: set only for the boot's structures stage of a body that had no
    /// block edit at load (an admin placing a landmark in a live world never moves the ground under players).</summary>
    private bool _siteGroundOpen;

    /// <summary>The scores of the last ranked first ring, best first (tests): the chosen candidate and the field it was
    /// chosen from.</summary>
    internal List<double> LastSiteScoresForTest { get; } = new();

    /// <summary>The index of the candidate the last ranked first ring placed on, within <see cref="LastSiteScoresForTest"/>
    /// (−1 = none passed; the wider rings decided).</summary>
    internal int LastSitePickForTest { get; set; } = -1;

    /// <summary>The active world's reserved sites (tests).</summary>
    internal IReadOnlyList<SiteFlat> SiteFlatsForTest => _world.SiteFlats;

    /// <summary>A body's pinned site text in the save (tests), null when it has none.</summary>
    internal string? PinnedSitesForTest(string body) => _meta.BodySites.TryGetValue(body, out var text) ? text : null;

    /// <summary>How well a centre suits a kind's site rule on the active world (tests); 0 for a neutral kind.</summary>
    internal double SiteScoreForTest(string kind, int x, int z)
        => StructureType(kind).Site is { IsNeutral: false } rule ? SiteScore(rule, x, z) : 0.0;

    // ------------------------------------------------------------------ the pinned list

    /// <summary>Runs the boot's structures stage with fresh stamps allowed to reserve their ground (a body stamped for the
    /// first time only), and closes the gate again whatever happens.</summary>
    private void WithSiteGround(bool virgin, Action stage)
    {
        _siteGroundOpen = virgin && GeographyWorld;
        try
        {
            stage();
        }
        finally
        {
            _siteGroundOpen = false;
        }
    }

    /// <summary>Reads the body's pinned site flats into the world (the landing-pad stage, before any chunk generates).</summary>
    private void LoadPinnedSiteFlats()
    {
        var sites = _world.SiteFlats;
        sites.Clear();
        if (GeographyWorld && _meta.BodySites.TryGetValue(_world.LocationId, out var text))
        {
            sites.AddRange(SiteFlat.Parse(text, out int malformed));
            if (malformed > 0)
            {
                _log.Warn($"{malformed} malformed site flat(s) in the save for '{_world.LocationId}' were skipped.");
            }
        }
    }

    /// <summary>
    /// Reserves the ground of a structure about to be stamped for the first time (#3569): seats <c>flat</c>, <c>slope</c> and
    /// <c>shelf</c> of a kind with a grade, on a body stamped for the first time, during the boot's structures stage only. The
    /// site joins the world's list, the generator applies it, every resident chunk the grade reaches is dropped (it
    /// regenerates graded — the stamps' edits are in the repository already), and the list is pinned in the save.
    /// <paramref name="gradeOf"/> overrides the kind's grade (the city world's larger one).
    /// </summary>
    private void ReserveSiteGround(string kind, int width, int length, Vector3i origin, int groundY, string seat, StructureGradeRule? gradeOf = null)
    {
        if (!_siteGroundOpen || !GeographyWorld || !_worlds.Active.VirginAtLoad || seat is not ("flat" or "slope" or "shelf"))
        {
            return;
        }

        var grade = gradeOf ?? StructureType(kind).Grade;
        if (!grade.Reserves)
        {
            return;
        }

        var site = new SiteFlat(origin.X, origin.Z, width, length, groundY, grade.Radius, grade.MaxCut, grade.MaxFill);
        foreach (var had in _world.SiteFlats)
        {
            if (had.MinX == site.MinX && had.MinZ == site.MinZ && had.Width == site.Width && had.Length == site.Length)
            {
                return; // reserved already (a body whose only stamps are overlays reads as untouched on its next load)
            }
        }

        _world.SiteFlats.Add(site);
        _meta.BodySites[_world.LocationId] = SiteFlat.Format(_world.SiteFlats);
        _world.ApplyGeneratorMode();

        int r = site.Radius + 1;
        _world.ForgetChunksIn(new Vector3i(origin.X - r, MinBuildY, origin.Z - r),
            new Vector3i(origin.X + width + r, MaxBuildY, origin.Z + length + r));
        _worlds.Active.FarColumnBands.Clear(); // the streamed band of a column reads its surface
        _worlds.Active.WaterColumns.Clear();   // the water classes read the ground beside the water
    }

    /// <summary>The grade the city world reserves: the settlement's, half as far again and cutting deeper (a city's footprint
    /// spans a whole landscape).</summary>
    private StructureGradeRule CityGrade()
    {
        var g = StructureType(StructureTypeRules.Settlement).Grade;
        return new StructureGradeRule { Radius = g.Radius * 3 / 2, MaxCut = g.MaxCut * 4 / 3, MaxFill = g.MaxFill * 4 / 3 };
    }

    /// <summary>Whether the structure at <paramref name="origin"/> stands on reserved ground (its stamp skips the apron).</summary>
    private bool SiteGraded(Vector3i origin)
    {
        foreach (var s in _world.SiteFlats)
        {
            if (s.MinX == origin.X && s.MinZ == origin.Z)
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------ the site-biome ground (#3565)

    /// <summary>The surface and sub-surface block keys a structure's foundation is built with: from generation 40 the biome at
    /// the footprint's centre, before it the given surface and the first pool biome's sub-surface (the classic stamp).</summary>
    private (string Surface, string Sub) FoundationGround(Vector3i origin, int width, int length, string classicSurface)
    {
        var planet = _world.Planet;
        string classicSub = planet.Biomes.Count > 0 ? planet.Biomes[0].SubSurfaceBlock : planet.SubSurfaceBlock;
        if (!GeographyWorld)
        {
            return (classicSurface, classicSub);
        }

        int cx = origin.X + (width / 2), cz = origin.Z + (length / 2);
        string? surface = _generator.BiomeSurfaceKeyAt(planet, cx, cz);
        string? sub = _generator.BiomeSubSurfaceKeyAt(planet, cx, cz);
        return (surface ?? classicSurface, sub ?? classicSub);
    }

    // ------------------------------------------------------------------ the ranked first ring (#3564)

    /// <summary>The kind's site rule when the search should rank by it: a generation-40 world and a rule that prefers
    /// something; null keeps the classic first-fit order.</summary>
    private StructureSiteRule? SiteRuleFor(string? kind)
        => kind is not null && GeographyWorld && StructureType(kind).Site is { IsNeutral: false } rule ? rule : null;

    /// <summary>Orders the first ring's candidates best-first by the site rule (stable: equal scores keep their draw order).</summary>
    private void RankBySite((int Cx, int Cz)[] spots, StructureSiteRule rule)
    {
        var scores = new double[spots.Length];
        var order = new int[spots.Length];
        for (int i = 0; i < spots.Length; i++)
        {
            scores[i] = SiteScore(rule, spots[i].Cx, spots[i].Cz);
            order[i] = i;
        }

        Array.Sort(order, (a, b) => scores[a] != scores[b] ? scores[b].CompareTo(scores[a]) : a.CompareTo(b));
        var ranked = new (int Cx, int Cz)[spots.Length];
        LastSiteScoresForTest.Clear();
        for (int i = 0; i < order.Length; i++)
        {
            ranked[i] = spots[order[i]];
            LastSiteScoresForTest.Add(scores[order[i]]);
        }

        Array.Copy(ranked, spots, spots.Length);
        LastSitePickForTest = -1;
    }

    /// <summary>How well a candidate centre suits a site rule, 0 … 1: the weighted mean of the terms the rule names and the
    /// world can answer (no river on the world → the water term is left out, not failed).</summary>
    internal double SiteScore(StructureSiteRule rule, int cx, int cz)
    {
        var planet = _world.Planet;
        var g = _generator;
        double sum = 0.0, weights = 0.0;
        void Term(string name, double? score)
        {
            double w = rule.WeightOf(name);
            if (score is { } s && w > 0.0)
            {
                sum += w * s;
                weights += w;
            }
        }

        int hc = g.SurfaceHeight(planet, cx, cz);
        if (rule.Water.Count == 2)
        {
            int d = g.FreshWaterDistance(planet, cx, cz);
            Term("water", d == RiverGeography.None ? null : Band(d, rule.Water[0], rule.Water[1]));
        }

        if (rule.Coast.Count == 2)
        {
            int d = g.CoastDistance(planet, cx, cz);
            Term("coast", d == RiverGeography.None ? null : Band(d, rule.Coast[0], rule.Coast[1]));
        }

        if (rule.Prominence.Length > 0)
        {
            double prominence = hc - RingMean(cx, cz, 48);
            Term("prominence", Clamp01((rule.Prominence == "high" ? prominence : -prominence) / 24.0));
        }

        if (rule.Relief.Length > 0)
        {
            int relief = RingSpread(cx, cz, hc, 16);
            Term("relief", rule.Relief == "flat" ? 1.0 - Clamp01(relief / 16.0) : Clamp01(relief / 24.0));
        }

        if (rule.Altitude.Count == 2)
        {
            double q = g.AltitudeQuantile(planet, hc);
            Term("altitude", q >= rule.Altitude[0] && q <= rule.Altitude[1] ? 1.0
                : 1.0 - Clamp01((q < rule.Altitude[0] ? rule.Altitude[0] - q : q - rule.Altitude[1]) / 0.25));
        }

        if (rule.Ground.Count > 0)
        {
            string? ground = g.BiomeSurfaceKeyAt(planet, cx, cz);
            Term("ground", ground is not null && rule.Ground.Contains(ground) ? 1.0 : 0.0);
        }

        return weights > 0.0 ? sum / weights : 0.0;
    }

    private static readonly (int Dx, int Dz)[] RingDirections =
        { (1000, 0), (-1000, 0), (0, 1000), (0, -1000), (707, 707), (707, -707), (-707, 707), (-707, -707) };

    /// <summary>The mean ground height on a ring of eight samples <paramref name="radius"/> blocks out.</summary>
    private double RingMean(int cx, int cz, int radius)
    {
        var planet = _world.Planet;
        long sum = 0;
        foreach (var (dx, dz) in RingDirections)
        {
            sum += _generator.SurfaceHeight(planet, cx + (dx * radius / 1000), cz + (dz * radius / 1000));
        }

        return sum / (double)RingDirections.Length;
    }

    /// <summary>The height spread (highest minus lowest) over the centre and a ring of eight samples.</summary>
    private int RingSpread(int cx, int cz, int hc, int radius)
    {
        var planet = _world.Planet;
        int lo = hc, hi = hc;
        foreach (var (dx, dz) in RingDirections)
        {
            int h = _generator.SurfaceHeight(planet, cx + (dx * radius / 1000), cz + (dz * radius / 1000));
            lo = Math.Min(lo, h);
            hi = Math.Max(hi, h);
        }

        return hi - lo;
    }

    /// <summary>1 inside the band, falling to 0 over half the band's width (at least 32 blocks) outside it.</summary>
    internal static double Band(int value, int low, int high)
    {
        if (value >= low && value <= high)
        {
            return 1.0;
        }

        double span = Math.Max(32.0, (high - low) / 2.0);
        double outside = value < low ? low - value : value - high;
        return Math.Max(0.0, 1.0 - (outside / span));
    }

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
}
