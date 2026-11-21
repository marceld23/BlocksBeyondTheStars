// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Which site of the world a rune gate belongs to (#3909) — the identity a mended crack is kept by.
/// <para>A gate's middle is the mean of the rune stones around whoever looks, so it moves when a player places stones
/// nearby or stands somewhere else; a mend keyed by the middle alone could be played again at the same monument — a
/// second bloom, a second gift, and one monument counting as "three different gates". The real side's answer and the
/// "different gate" count therefore go by the SITE: the generated structure whose stones make the gate a world gate.</para>
/// <list type="bullet">
/// <item>The Singing Shrine: <c>shrine</c>.</item>
/// <item>A stamped structure (a monument, a ruin, a unique site, a site of the Sounders' world, a settlement …): the
/// box it registered in the footprint registry, <c>&lt;kind&gt;@&lt;x&gt;:&lt;z&gt;</c>.</item>
/// <item>A rune shrine of the terrain (seed arithmetic): <c>rune_shrine@&lt;x&gt;:&lt;z&gt;</c>.</item>
/// <item>World stones that belong to none of these (terrain stones of an old world): <c>stones@&lt;x&gt;:&lt;y&gt;:&lt;z&gt;</c>, the
/// first cell of the whole group of world stones — grown from the stones found until nothing joins, so it does not
/// depend on which part of the group the look started at.</item>
/// </list>
/// Stones a player placed never count (<see cref="RuneSitePlaced"/>): a gate of placed stones has no site, keeps the
/// position key and gets no answer from the real side.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The site id of the Singing Shrine's gate.</summary>
    private const string ShrineGateSite = "shrine";

    /// <summary>How often a group of world stones outside every known structure is grown before its anchor is taken.</summary>
    private const int RuneGroupGrowRounds = 2;

    /// <summary>
    /// The stable id of the world site whose stones make the gate at <paramref name="gate"/> (the active world) a world
    /// gate, or null when fewer than <see cref="EchoRuneGroupSize"/> of the rune stones around it are the world's own —
    /// a gate a player built. The site is the one most of the world's stones there belong to. Loaded chunks only.
    /// </summary>
    internal string? WorldGateSite(Vector3f gate)
    {
        if (_content.GetBlock("rune_stone") is not { } rune)
        {
            return null;
        }

        var centre = new Vector3i((int)Math.Floor(gate.X), (int)Math.Floor(gate.Y), (int)Math.Floor(gate.Z));
        int r = EchoRuneScanRadius;
        var own = WorldRuneStonesIn(new Vector3i(centre.X - r, centre.Y - r, centre.Z - r), new Vector3i(centre.X + r, centre.Y + r, centre.Z + r),
            rune.NumericId.Value);
        if (own.Count < EchoRuneGroupSize)
        {
            return null;
        }

        var tally = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var stone in own)
        {
            string id = RuneStoneStructure(WorldConstants.CanonicalBlock(stone, _world.Circumference)) ?? string.Empty;
            tally[id] = tally.TryGetValue(id, out int n) ? n + 1 : 1;
        }

        string best = string.Empty;
        int bestCount = -1;
        foreach (var (id, count) in tally)
        {
            if (count > bestCount || (count == bestCount && string.CompareOrdinal(id, best) < 0))
            {
                best = id;
                bestCount = count;
            }
        }

        return best.Length > 0 ? best : RuneGroupSite(own, rune.NumericId.Value);
    }

    /// <summary>The world's own rune stones in a box of cells (as scanned, not canonical): rune stones no player placed.</summary>
    private List<Vector3i> WorldRuneStonesIn(Vector3i lo, Vector3i hi, ushort rune)
    {
        var found = new List<Vector3i>();
        for (int x = lo.X; x <= hi.X; x++)
        {
            for (int y = lo.Y; y <= hi.Y; y++)
            {
                for (int z = lo.Z; z <= hi.Z; z++)
                {
                    var cell = new Vector3i(x, y, z);
                    if (_world.GetBlockIfLoaded(cell).Value == rune
                        && !string.Equals(RuneSiteKind(cell), RuneSitePlaced, StringComparison.Ordinal))
                    {
                        found.Add(cell);
                    }
                }
            }
        }

        return found;
    }

    /// <summary>The structure a world stone (canonical cell) belongs to, or null for a stone of the open terrain.</summary>
    private string? RuneStoneStructure(Vector3i c)
    {
        if (ShrineMelodySite() is { } shrine && NearShrine(shrine, c))
        {
            return ShrineGateSite;
        }

        if (Footprints.Covering(c.X, c.Z, static kind => kind is StructureTypeRules.Pad or StructureTypeRules.WreckSite) is { } box)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}@{1}:{2}", box.Kind, box.Cx, box.Cz);
        }

        var shrines = _generator.RuneShrinesNear(_world.Planet, c.X, c.Z, RuneShrineHalfExtent + 3); // nearest first
        return shrines.Count > 0
            ? string.Format(CultureInfo.InvariantCulture, "rune_shrine@{0}:{1}", shrines[0].X, shrines[0].Z)
            : null;
    }

    /// <summary>The id of a group of world stones that stands in no known structure: its first cell (canonical, by X, Y,
    /// Z) after the group was grown — every world stone within the scan radius of the group joins, at most
    /// <see cref="RuneGroupGrowRounds"/> times — so the same group has the same id wherever the look began.</summary>
    private string RuneGroupSite(List<Vector3i> group, ushort rune)
    {
        for (int round = 0; round < RuneGroupGrowRounds; round++)
        {
            Vector3i lo = group[0], hi = group[0];
            foreach (var s in group)
            {
                lo = new Vector3i(Math.Min(lo.X, s.X), Math.Min(lo.Y, s.Y), Math.Min(lo.Z, s.Z));
                hi = new Vector3i(Math.Max(hi.X, s.X), Math.Max(hi.Y, s.Y), Math.Max(hi.Z, s.Z));
            }

            int r = EchoRuneScanRadius;
            var grown = WorldRuneStonesIn(new Vector3i(lo.X - r, lo.Y - r, lo.Z - r), new Vector3i(hi.X + r, hi.Y + r, hi.Z + r), rune);
            if (grown.Count <= group.Count)
            {
                break;
            }

            group = grown;
        }

        var first = WorldConstants.CanonicalBlock(group[0], _world.Circumference);
        foreach (var s in group)
        {
            var c = WorldConstants.CanonicalBlock(s, _world.Circumference);
            if (c.X < first.X || (c.X == first.X && (c.Y < first.Y || (c.Y == first.Y && c.Z < first.Z))))
            {
                first = c;
            }
        }

        return string.Format(CultureInfo.InvariantCulture, "stones@{0}:{1}:{2}", first.X, first.Y, first.Z);
    }

    /// <summary>Whether the site <paramref name="site"/> of <paramref name="locationId"/> was mended already.</summary>
    private bool IsSiteMended(string locationId, string site)
    {
        foreach (var crack in MendedCracks())
        {
            if (crack.Site.Length > 0 && string.Equals(crack.Site, site, StringComparison.Ordinal)
                && string.Equals(crack.LocationId, locationId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Test seam: <see cref="WorldGateSite"/> on the active world.</summary>
    public string? WorldGateSiteForTest(Vector3f gate) => WorldGateSite(gate);
}
