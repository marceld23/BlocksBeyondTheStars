// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Melody sites (#3346): eight pillars around a centre, pillar <c>i</c> = tone <c>i</c>, clockwise from north
/// (<see cref="MelodySiteGeometry"/>). Striking a pillar with the use key while the player plays a puzzle there feeds that
/// tone into it (<see cref="TryStrikeMelodyPillar"/>); with no puzzle running, a registered starter
/// (<see cref="RegisterMelodySiteStarter"/>) may start one, else the stone simply sings its rune word. Sites live per world
/// and in memory only — they are re-derived, never saved:
/// <list type="bullet">
/// <item>the Singing Shrine's eight rune pillars, registered on first use from the shrine's placement record (the same
/// cells <c>WriteShrine</c> wrote) — id <see cref="GameServer.ShrineSiteKey"/>, kind <see cref="MelodySiteKindShrine"/>;</item>
/// <item>a player-built <b>ring</b> — eight <c>rune_stone</c> columns 2–5 blocks around an <c>ancient_brick</c> or
/// <c>song_spindle</c> centre, one per 45° sector — found when one of its stones is struck (the fallback for worlds without
/// a shrine); id <c>ring:x,y,z</c> of the centre, kind <see cref="MelodySiteKindRing"/>; dropped once its centre or a stone
/// is gone;</item>
/// <item>anything other code registers (<see cref="RegisterMelodySite(string, IReadOnlyList{Vector3i})"/>, kind
/// <see cref="MelodySiteKindCustom"/> — the Choir Tower's pipes, say).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    internal const string MelodySiteKindShrine = "shrine";
    internal const string MelodySiteKindRing = "ring";
    internal const string MelodySiteKindCustom = "custom";

    /// <summary>A struck stone that is in no ring is not searched again for this long (seconds of the melody clock).</summary>
    private const double MelodyRingMissSeconds = 5;

    private readonly Dictionary<string, Dictionary<string, MelodySite>> _melodySites = new(StringComparer.Ordinal);
    private readonly HashSet<string> _melodyShrineChecked = new(StringComparer.Ordinal);
    private readonly List<Func<PlayerSession, MelodySite, MelodyPuzzleSpec?>> _melodySiteStarters = new();
    private readonly Dictionary<Vector3i, double> _melodyRingMisses = new();

    /// <summary>A melody site of one world: canonical cells; pillar <c>i</c> (its lowest stone) is tone <c>i</c>.</summary>
    internal sealed class MelodySite
    {
        public MelodySite(string id, string kind, string locationId, Vector3i centre, Vector3i[] pillars)
        {
            Id = id;
            Kind = kind;
            LocationId = locationId;
            Centre = centre;
            Pillars = pillars;
        }

        public string Id { get; }

        /// <summary><see cref="MelodySiteKindShrine"/>, <see cref="MelodySiteKindRing"/> or <see cref="MelodySiteKindCustom"/>.</summary>
        public string Kind { get; }

        public string LocationId { get; }

        /// <summary>The centre cell (the shrine's plinth centre, the ring's centre block).</summary>
        public Vector3i Centre { get; }

        public IReadOnlyList<Vector3i> Pillars { get; }

        /// <summary>The middle of the site at standing height — where prompts sound and the leave distance counts from.</summary>
        public Vector3f CentreF => new(Centre.X + 0.5f, Centre.Y + 1f, Centre.Z + 0.5f);
    }

    /// <summary>Registers (or replaces) a melody site in the ACTIVE world: <paramref name="pillars"/> are eight cells, pillar
    /// <c>i</c> = tone <c>i</c>, ordered clockwise from north around the centre (the average of the pillars); a pillar counts
    /// from its cell up to three cells above it. Null for anything but eight pillars.</summary>
    internal MelodySite? RegisterMelodySite(string siteId, IReadOnlyList<Vector3i> pillars)
        => pillars is { Count: MelodySiteGeometry.Pillars } ? RegisterMelodySite(siteId, pillars, CentreOf(pillars), MelodySiteKindCustom) : null;

    /// <summary>Removes a site of the active world (the Choir Tower torn down). False when there was none.</summary>
    internal bool UnregisterMelodySite(string siteId)
        => _melodySites.TryGetValue(_world.LocationId, out var sites) && sites.Remove(siteId);

    /// <summary>A registered site of <paramref name="locationId"/> by id (the shrine of the active world is found on demand).</summary>
    internal MelodySite? FindMelodySite(string locationId, string siteId)
    {
        if (locationId == _world.LocationId && siteId == ShrineSiteKey)
        {
            return ShrineMelodySite();
        }

        return _melodySites.TryGetValue(locationId, out var sites) && sites.TryGetValue(siteId, out var site) ? site : null;
    }

    /// <summary>The registered site of the active world whose centre is nearest to <paramref name="pos"/> within
    /// <paramref name="range"/> blocks, or null (the shrine is included once this world has one).</summary>
    internal MelodySite? NearestMelodySite(Vector3f pos, float range)
    {
        ShrineMelodySite();
        if (!_melodySites.TryGetValue(_world.LocationId, out var sites))
        {
            return null;
        }

        MelodySite? best = null;
        double bestSq = (double)range * range;
        foreach (var site in sites.Values)
        {
            double d = WrapDistSq(pos, site.CentreF);
            if (d <= bestSq)
            {
                bestSq = d;
                best = site;
            }
        }

        return best;
    }

    /// <summary>
    /// Asks <paramref name="starter"/> whenever a player strikes a pillar of a site while no puzzle runs for them: a spec
    /// starts that puzzle (its <see cref="MelodyPuzzleSpec.SiteId"/> defaults to the struck site), null passes to the next
    /// starter — and when every starter passes, the stone sings its rune word. The story wires its chapters here (the
    /// shrine's echo puzzle while chapter 2 runs, the Song of Return in chapter 6).
    /// </summary>
    internal void RegisterMelodySiteStarter(Func<PlayerSession, MelodySite, MelodyPuzzleSpec?> starter)
    {
        if (starter is not null)
        {
            _melodySiteStarters.Add(starter);
        }
    }

    /// <summary>
    /// The use key on <paramref name="cell"/> as a pillar strike: true when the cell is a pillar of a melody site of the
    /// active world and the strike fed the player's running puzzle there (or started one through a starter). False leaves
    /// the use to the block's own handler (a rune stone sings its word). A rune stone's handler calls this first; another
    /// pillar block's handler (the Choir Tower's resonance crystals) can do the same.
    /// </summary>
    internal bool TryStrikeMelodyPillar(PlayerSession session, Vector3i cell)
    {
        var site = MelodySiteAt(cell, out int pillar);
        if (site is null || pillar < 0)
        {
            return false;
        }

        if (_melodyRuns.TryGetValue(session.State.PlayerId, out var run))
        {
            string runSite = run.Puzzle.Spec.SiteId;
            if (runSite.Length > 0 && !string.Equals(runSite, site.Id, StringComparison.Ordinal))
            {
                return false; // the puzzle belongs to another site: here the stone is just a rune
            }

            FeedMelodyRun(session, run, pillar, site.Pillars[pillar]);
            return true;
        }

        return TryStartSitePuzzle(session, site);
    }

    private bool TryStartSitePuzzle(PlayerSession session, MelodySite site)
    {
        foreach (var starter in _melodySiteStarters.ToArray())
        {
            MelodyPuzzleSpec? spec;
            try
            {
                spec = starter(session, site);
            }
            catch (Exception ex)
            {
                _log.Error($"Melody site starter failed at '{site.Id}' for '{session.State.Name}': {ex.Message}");
                continue;
            }

            if (spec is null)
            {
                continue;
            }

            if (spec.SiteId.Length == 0)
            {
                spec = spec.Clone();
                spec.SiteId = site.Id;
            }

            if (StartMelodyPuzzle(session, spec))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The site of the active world <paramref name="cell"/> is a pillar of (and which pillar), the shrine and a ring
    /// around the cell found on demand. Null and -1 when none.</summary>
    internal MelodySite? MelodySiteAt(Vector3i cell, out int pillar)
    {
        var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        ShrineMelodySite();
        if (_melodySites.TryGetValue(_world.LocationId, out var sites))
        {
            MelodySite? fallen = null;
            foreach (var site in sites.Values)
            {
                int i = MelodySiteGeometry.PillarIndex(site.Pillars, c);
                if (i < 0)
                {
                    continue;
                }

                if (site.Kind == MelodySiteKindRing && !RingStillStands(site))
                {
                    fallen = site;
                    break;
                }

                pillar = i;
                return site;
            }

            if (fallen is not null)
            {
                sites.Remove(fallen.Id); // a stone or the centre was mined: look again below
            }
        }

        var ring = DetectMelodyRing(c);
        pillar = ring is null ? -1 : MelodySiteGeometry.PillarIndex(ring.Pillars, c);
        return ring;
    }

    /// <summary>The Singing Shrine's site, registered on first need when the active world carries the shrine.</summary>
    private MelodySite? ShrineMelodySite()
    {
        string loc = _world.LocationId;
        if (_melodySites.TryGetValue(loc, out var sites) && sites.TryGetValue(ShrineSiteKey, out var shrine))
        {
            return shrine;
        }

        if (!_melodyShrineChecked.Add(loc) || loc != ShrineBodyId())
        {
            return null;
        }

        var record = FindPlacementRecord("unique:" + ShrineSiteKey, 0);
        return record is { Placed: true } ? RegisterShrineMelodySite(record.X, record.GroundY, record.Z) : null;
    }

    /// <summary>Registers the shrine stamped at (<paramref name="cx"/>, <paramref name="groundY"/>, <paramref name="cz"/>) in
    /// the active world — its eight rune pillars as <c>WriteShrine</c> wrote them (also the test seam).</summary>
    internal MelodySite? RegisterShrineMelodySite(int cx, int groundY, int cz)
        => RegisterMelodySite(ShrineSiteKey, MelodySiteGeometry.ShrinePillars(cx, groundY, cz), new Vector3i(cx, groundY, cz),
            MelodySiteKindShrine);

    private MelodySite? RegisterMelodySite(string siteId, IReadOnlyList<Vector3i> pillars, Vector3i centre, string kind)
    {
        if (string.IsNullOrEmpty(siteId) || pillars.Count != MelodySiteGeometry.Pillars)
        {
            return null;
        }

        int circ = _world.Circumference;
        var canonical = new Vector3i[pillars.Count];
        for (int i = 0; i < canonical.Length; i++)
        {
            canonical[i] = WorldConstants.CanonicalBlock(pillars[i], circ);
        }

        var site = new MelodySite(siteId, kind, _world.LocationId, WorldConstants.CanonicalBlock(centre, circ), canonical);
        if (!_melodySites.TryGetValue(site.LocationId, out var sites))
        {
            _melodySites[site.LocationId] = sites = new Dictionary<string, MelodySite>(StringComparer.Ordinal);
        }

        sites[siteId] = site;
        return site;
    }

    /// <summary>The middle of eight pillars (seam-aware), one cell below the lowest — the ground they stand on.</summary>
    private Vector3i CentreOf(IReadOnlyList<Vector3i> pillars)
    {
        int circ = _world.Circumference;
        var first = pillars[0];
        double sx = 0, sz = 0;
        int minY = int.MaxValue;
        foreach (var p in pillars)
        {
            sx += WorldConstants.WrapDeltaX(p.X - first.X, circ);
            sz += WorldConstants.WrapDeltaZ(p.Z - first.Z, circ);
            minY = Math.Min(minY, p.Y);
        }

        return new Vector3i(first.X + (int)Math.Round(sx / pillars.Count), minY - 1, first.Z + (int)Math.Round(sz / pillars.Count));
    }

    // ---------------------------------------------------------------------------------------------
    // The player-built ring.
    // ---------------------------------------------------------------------------------------------

    /// <summary>A ring around the struck stone <paramref name="stone"/> (canonical): a centre block 2–5 blocks away with eight
    /// stone columns around it, the struck one among them. Registered when found.</summary>
    private MelodySite? DetectMelodyRing(Vector3i stone)
    {
        BlockId rune = MelodyBlockId("rune_stone");
        if (rune.IsAir || _world.GetBlockIfLoaded(stone).Value != rune.Value
            || (_melodyRingMisses.TryGetValue(stone, out double until) && _melodyClock < until))
        {
            return null;
        }

        int r = MelodySiteGeometry.RingSearchRadius;
        for (int dy = -3; dy <= 1; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    var centre = new Vector3i(stone.X + dx, stone.Y + dy, stone.Z + dz);
                    if (RingAround(centre, stone, rune) is { } ring)
                    {
                        return RegisterMelodySite($"ring:{ring.Centre.X},{ring.Centre.Y},{ring.Centre.Z}", ring.Pillars,
                            ring.Centre, MelodySiteKindRing);
                    }
                }
            }
        }

        if (_melodyRingMisses.Count > 512)
        {
            _melodyRingMisses.Clear();
        }

        _melodyRingMisses[stone] = _melodyClock + MelodyRingMissSeconds;
        return null;
    }

    /// <summary>The ring around <paramref name="centre"/> that <paramref name="stone"/> belongs to, or null.</summary>
    private (Vector3i Centre, Vector3i[] Pillars)? RingAround(Vector3i centre, Vector3i stone, BlockId rune)
    {
        int dx = stone.X - centre.X, dz = stone.Z - centre.Z;
        double d = Math.Sqrt((dx * dx) + (dz * dz));
        if (d < MelodySiteGeometry.RingMinDistance || d > MelodySiteGeometry.RingMaxDistance || !IsRingCentre(centre)
            || !StoneColumnNear(centre.X - dx, centre.Y, centre.Z - dz, rune))
        {
            return null; // no centre block here, or nothing standing opposite the struck stone
        }

        var ring = MelodySiteGeometry.FindRing(centre,
            (ox, oy, oz) => _world.GetBlockIfLoaded(new Vector3i(centre.X + ox, centre.Y + oy, centre.Z + oz)).Value == rune.Value);
        return ring is not null && MelodySiteGeometry.PillarIndex(ring, stone) >= 0 ? (centre, ring) : null;
    }

    /// <summary>Whether a ring may stand around this cell: an ancient brick or a Song Spindle.</summary>
    private bool IsRingCentre(Vector3i cell)
    {
        ushort id = _world.GetBlockIfLoaded(cell).Value;
        if (id == BlockId.AirValue)
        {
            return false;
        }

        BlockId brick = MelodyBlockId("ancient_brick"), spindle = MelodyBlockId(SoundersIds.SongSpindle);
        return (!brick.IsAir && id == brick.Value) || (!spindle.IsAir && id == spindle.Value);
    }

    /// <summary>A stone in the 3×3 columns around (<paramref name="x"/>, <paramref name="z"/>), one below to three above
    /// <paramref name="y"/> — the quick check that something stands opposite the struck stone.</summary>
    private bool StoneColumnNear(int x, int y, int z, BlockId rune)
    {
        for (int ox = -1; ox <= 1; ox++)
        {
            for (int oz = -1; oz <= 1; oz++)
            {
                for (int oy = -1; oy <= 3; oy++)
                {
                    if (_world.GetBlockIfLoaded(new Vector3i(x + ox, y + oy, z + oz)).Value == rune.Value)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>Whether a registered ring still has its centre block and a stone in every pillar.</summary>
    private bool RingStillStands(MelodySite site)
    {
        BlockId rune = MelodyBlockId("rune_stone");
        if (!IsRingCentre(site.Centre))
        {
            return false;
        }

        foreach (var p in site.Pillars)
        {
            bool stone = false;
            for (int dy = 0; dy <= 3 && !stone; dy++)
            {
                stone = _world.GetBlockIfLoaded(new Vector3i(p.X, p.Y + dy, p.Z)).Value == rune.Value;
            }

            if (!stone)
            {
                return false;
            }
        }

        return true;
    }

    private BlockId MelodyBlockId(string key) => _content.GetBlock(key)?.NumericId ?? BlockId.Air;
}
