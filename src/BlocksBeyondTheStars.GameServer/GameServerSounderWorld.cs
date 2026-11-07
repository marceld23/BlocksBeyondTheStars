// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Soaniri, the Sounder home world (#3358, the Sounders story pack): the three places the server stamps on every world of the
/// <c>sounder_world</c> type — the village <b>Mirun</b> near the landing pad (huts of stone and reed around a bell post, the
/// last living Sounders and the Eldest), the ruin city <b>Ornasa</b> further out (broken obsidian walls, a rune amphitheatre,
/// three sealed sound chambers and the bell-bloom trail) and the <b>Choir Tower</b> furthest away (an obsidian-and-crystal
/// tower with eight pipes on its top platform, standing in a barren ring). Each is stamped ONCE like the unique sites — the
/// placement record <c>sounder:&lt;key&gt;</c> pins where it stands, the blocks are written once and live on as edits, and
/// every later load only re-derives the transient part (the villagers, the melody and pack sites, the chambers and the
/// trail) from the record. Deterministic: the search is a pure function of the world seed and the body. The world's rune
/// monuments (4–6) and its singing meadows come from the classic stamper and the world generator.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The village of the last Sounders (also its pack-site id).</summary>
    internal const string MirunSiteKey = "mirun";

    /// <summary>The ruin city of the old Sounders (also its pack-site id).</summary>
    internal const string OrnasaSiteKey = "ornasa";

    /// <summary>The Choir Tower — the finale stage, the melody site and pack site <c>choir_tower</c>.</summary>
    internal const string ChoirTowerSiteKey = "choir_tower";

    /// <summary>The placement-record kind prefix of a Soaniri site.</summary>
    internal const string SounderSiteRecordPrefix = "sounder:";

    /// <summary>How many seeded spots the site search weighs per site.</summary>
    private const int SounderSiteAttempts = 90;

    /// <summary>A Soaniri site: its key, its footprint radius (what it levels and reserves), how far from the home landing pad
    /// it may stand, and the singing zone it prefers (<c>WorldGenerator.SingingZoneAt</c>).</summary>
    internal sealed record SounderSiteSpec(string Key, int Radius, int MinDistance, int MaxDistance, string PreferZone);

    /// <summary>The three sites, in stamping order (each one keeps clear of the ones before it).</summary>
    internal static readonly SounderSiteSpec[] SounderSites =
    {
        new(MirunSiteKey, MirunRadius, 48, 110, "marsh"),
        new(OrnasaSiteKey, OrnasaRadius, 140, 240, "meadow"),
        new(ChoirTowerSiteKey, ChoirBarrenRadius, 220, 330, "slope"),
    };

    /// <summary>Whether the active world is a Sounder world (the <c>sounder_world</c> type).</summary>
    private bool IsSounderWorld => string.Equals(_world.Planet?.Key, SoundersIds.PlanetType, StringComparison.Ordinal);

    /// <summary>Stamps (or re-derives) Soaniri's village, ruin city and Choir Tower — on a Sounder world only. Runs in the
    /// structures stage right after the text tablets, before the monuments (which reserve around the three).</summary>
    private void StampSounderWorld()
    {
        if (!IsSounderWorld)
        {
            return;
        }

        var placed = new List<(int X, int Z, int Radius)>();
        foreach (var spec in SounderSites)
        {
            if (StampSounderSite(spec, placed) is { } at)
            {
                placed.Add((at.X, at.Z, spec.Radius));
            }
        }
    }

    /// <summary>The once-per-world placement of one site (the unique-site pattern): replay a pinned record, skip on a world
    /// stamped before the feature, else search, write the blocks once and pin it. The transient part runs every load.</summary>
    private (int X, int Z)? StampSounderSite(SounderSiteSpec spec, List<(int X, int Z, int Radius)> placed)
    {
        string kind = SounderSiteRecordPrefix + spec.Key;
        var record = FindPlacementRecord(kind, 0);
        if (record is not null)
        {
            if (!record.Placed)
            {
                return null;
            }

            AfterSounderSite(spec.Key, record.X, record.GroundY, record.Z);
            return (record.X, record.Z);
        }

        if (!_worlds.Active.VirginAtLoad || !TrySeatSounderSite(spec, placed, out int cx, out int cz, out int floorY))
        {
            RecordPlacementSkip(kind, 0); // built-in ground (never on a fresh Soaniri) or no dry spot at all
            SavePlacementRecords();
            ReportStamp(kind, 1, 0);
            return null;
        }

        _repo.RunInTransaction(() => WriteSounderSite(spec.Key, cx, floorY, cz));
        RecordPlacement(kind, 0, new Vector3i(cx, floorY, cz), floorY, onIsland: false, "flat", spec.Key);
        SavePlacementRecords();
        ReportStamp(kind, 1, 1);
        _log.Info($"Soaniri site '{spec.Key}' stamped on {_world.LocationId} at ({cx},{floorY},{cz}).");
        AfterSounderSite(spec.Key, cx, floorY, cz);
        return (cx, cz);
    }

    private void WriteSounderSite(string key, int cx, int floorY, int cz)
    {
        var b = new SounderBlocks(this);
        switch (key)
        {
            case MirunSiteKey:
                WriteMirun(b, cx, floorY, cz);
                break;
            case OrnasaSiteKey:
                WriteOrnasa(b, cx, floorY, cz);
                break;
            case ChoirTowerSiteKey:
                WriteChoirTower(b, cx, floorY, cz);
                break;
        }
    }

    /// <summary>Every load (and right after the stamp): what a site re-derives from its record.</summary>
    private void AfterSounderSite(string key, int cx, int floorY, int cz)
    {
        string loc = _world.LocationId;
        RegisterPackSite(SoundersIds.PackId, key, loc, new Vector3i(cx, floorY + 1, cz));
        switch (key)
        {
            case MirunSiteKey:
                SpawnMirunVillagers(cx, floorY, cz);
                break;
            case OrnasaSiteKey:
                AfterOrnasa(cx, floorY, cz);
                break;
            case ChoirTowerSiteKey:
                RegisterChoirTowerSite(cx, floorY, cz);
                break;
        }
    }

    // ---------------- The site search ----------------

    /// <summary>The best of <see cref="SounderSiteAttempts"/> seeded spots within the spec's distance band of the home pad: dry
    /// ground, clear of the pads and of the sites placed before, scored by how flat its footprint is (the spread of thirteen
    /// ground samples) with a bonus for its preferred zone. The floor is the median sample, so the levelling cuts and fills
    /// about as much as it must.</summary>
    private bool TrySeatSounderSite(SounderSiteSpec spec, List<(int X, int Z, int Radius)> placed, out int cx, out int cz, out int floorY)
    {
        long seed = _meta.Seed ^ WorldGenerator.StableHash("sounder-site:" + spec.Key + ":" + _world.LocationId);
        var rng = new Random(unchecked((int)(seed ^ (seed >> 32))));
        int pad0X = _landingPads.Count > 0 ? _landingPads[0].CenterX : 0;
        int pad0Z = _landingPads.Count > 0 ? _landingPads[0].CenterZ : 0;
        int best = int.MaxValue;
        cx = cz = floorY = 0;
        for (int attempt = 0; attempt < SounderSiteAttempts * 2 && !(attempt == SounderSiteAttempts && best < int.MaxValue); attempt++)
        {
            // A second round, three times as far out, only when the first found no dry spot (a pad on a lake shore).
            int maxDistance = attempt < SounderSiteAttempts ? spec.MaxDistance : spec.MaxDistance * 3;
            double angle = rng.NextDouble() * Math.PI * 2.0;
            int dist = spec.MinDistance + rng.Next(maxDistance - spec.MinDistance + 1);
            int x = WorldConstants.WrapX(pad0X + (int)Math.Round(Math.Cos(angle) * dist), _world.Circumference);
            int z = pad0Z + (int)Math.Round(Math.Sin(angle) * dist);
            if (!SounderSiteClear(spec, placed, x, z))
            {
                continue;
            }

            int score = SounderSiteScore(spec, x, z, out int floor);
            if (score < best)
            {
                best = score;
                cx = x;
                cz = z;
                floorY = floor;
            }
        }

        return best < int.MaxValue;
    }

    /// <summary>Clear of every landing pad and earlier site, and no water, lava or sea ice under the centre or the eight rim
    /// samples at 70 % of the radius.</summary>
    private bool SounderSiteClear(SounderSiteSpec spec, List<(int X, int Z, int Radius)> placed, int x, int z)
    {
        foreach (var pad in _landingPads)
        {
            if (SounderDistance(x, z, pad.CenterX, pad.CenterZ) < pad.Radius + spec.Radius + 6)
            {
                return false;
            }
        }

        foreach (var other in placed)
        {
            if (SounderDistance(x, z, other.X, other.Z) < other.Radius + spec.Radius + 10)
            {
                return false;
            }
        }

        var planet = _world.Planet;
        double rim = spec.Radius * 0.7;
        for (int i = -1; i < 8; i++)
        {
            int sx = i < 0 ? x : x + (int)Math.Round(Math.Cos(i * Math.PI / 4.0) * rim);
            int sz = i < 0 ? z : z + (int)Math.Round(Math.Sin(i * Math.PI / 4.0) * rim);
            if (_generator.IsSurfaceLava(planet, sx, sz) || _generator.TryGetWaterSurface(planet, sx, sz, out _, out _)
                || _generator.SurfaceIceThickness(planet, sx, sz) > 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Lower is better: ten per block of height spread over thirteen samples, plus 25 off the preferred zone.</summary>
    private int SounderSiteScore(SounderSiteSpec spec, int x, int z, out int floorY)
    {
        var planet = _world.Planet;
        var heights = new List<int>(13) { _generator.SurfaceHeight(planet, x, z) };
        for (int i = 0; i < 12; i++)
        {
            double r = (i < 4 ? 0.35 : 0.8) * spec.Radius;
            double a = i < 4 ? i * Math.PI / 2.0 : (i - 4) * Math.PI / 4.0;
            heights.Add(_generator.SurfaceHeight(planet, x + (int)Math.Round(Math.Cos(a) * r), z + (int)Math.Round(Math.Sin(a) * r)));
        }

        heights.Sort();
        floorY = heights[heights.Count / 2];
        int spread = heights[heights.Count - 1] - heights[0];
        bool preferred = string.Equals(_generator.SingingZoneAt(planet, x, z), spec.PreferZone, StringComparison.Ordinal);
        return (spread * 10) + (preferred ? 0 : 25);
    }

    /// <summary>Horizontal distance across the world's seams.</summary>
    private double SounderDistance(int ax, int az, int bx, int bz)
    {
        int circ = _world.Circumference;
        double dx = WorldConstants.WrapDeltaX(ax - bx, circ), dz = WorldConstants.WrapDeltaZ(az - bz, circ);
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    // ---------------- Reservations, map, records ----------------

    /// <summary>The placed Soaniri sites of the active world (key, centre, floor), from their records.</summary>
    private IEnumerable<(SounderSiteSpec Spec, int X, int FloorY, int Z)> PlacedSounderSites()
    {
        if (!IsSounderWorld)
        {
            yield break;
        }

        foreach (var spec in SounderSites)
        {
            if (FindPlacementRecord(SounderSiteRecordPrefix + spec.Key, 0) is { Placed: true } record)
            {
                yield return (spec, record.X, record.GroundY, record.Z);
            }
        }
    }

    /// <summary>Keeps the rune monuments clear of the three sites (a footprint plus a margin).</summary>
    private void AppendSounderWorldReservations(List<(int Cx, int Cz, int Hw, int Hl)> reserved)
    {
        foreach (var (spec, x, _, z) in PlacedSounderSites())
        {
            reserved.Add((x, z, spec.Radius + 4, spec.Radius + 4));
        }
    }

    /// <summary>The three sites on the world map of a Sounder world — landmarks you see from afar, so always listed.</summary>
    private void AppendSounderWorldPois(PlayerSession session, List<NetPoi> pois)
    {
        foreach (var (spec, x, _, z) in PlacedSounderSites())
        {
            pois.Add(new NetPoi { Type = spec.Key, Name = Localize(session.Locale, "poi." + spec.Key), X = x + 0.5f, Z = z + 0.5f });
        }
    }

    /// <summary>The monument count a type names (#3358, <c>monumentCount</c> [min, max]): its own hash of the body, so the
    /// classic roll before it stays untouched.</summary>
    private static int TypeMonumentCount(int min, int max, long monumentSeed)
    {
        uint h = unchecked((uint)WorldGenerator.StableHash("monument-count:" + monumentSeed.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return min + (int)(h % (uint)(max - min + 1));
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/inspection: the placed Soaniri sites of the active world (key, centre, floor).</summary>
    internal IReadOnlyList<(string Key, int X, int FloorY, int Z)> SounderSitesForTest()
    {
        var list = new List<(string, int, int, int)>();
        foreach (var (spec, x, y, z) in PlacedSounderSites())
        {
            list.Add((spec.Key, x, y, z));
        }

        return list;
    }
}
