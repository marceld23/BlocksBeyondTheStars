// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A civilization site in the living world (#3642) — see GameServerCivilizations.cs. The landmark's world parts carry it
/// (GameServerLandmarkWorld.cs): its chests and sound emitters, the streaming lift; a site on land also its far silhouette
/// (a sunken one casts none — the sea hides it). What is its own:
/// <list type="bullet">
/// <item><b>The map.</b> On everyone's map (type <see cref="CivilizationMapType"/>, the site's name) once a player on foot or
/// swimming comes within <see cref="CivilizationRevealRange"/> blocks of its footprint.</item>
/// <item><b>The discovery.</b> The first time a player comes that near a site of a civilization — or scans one of its sites or
/// one of its tagged blocks (<see cref="BlockDefinition.Civilization"/>) — the civilization goes into the player's discovery
/// ledger (<see cref="CivilizationRules.DiscoveryKey"/>, <c>civ:&lt;key&gt;</c>), worth a relic's knowledge once. The Codex
/// groups it under its own heading and shows the civilization file's <c>codex</c> title and text.</item>
/// <item><b>VEGA</b> speaks each line of the civilization file's <c>vega</c> list once per player
/// (<see cref="CivilizationVegaMilestone"/>), from the 1 Hz advisor pass, never aboard — an advisor hint: the hints switch and
/// a basic training that holds hints (id <see cref="CivilizationHintId"/>) keep it for later. The triggers read the site's
/// layout, never the world: <c>first</c> within the reveal range of a site, <c>inside</c> its box, <c>block</c> within
/// <see cref="CivilizationBlockRange"/> blocks of a cell of the named block (a trailing <c>*</c> = any key with that prefix),
/// <c>prop</c> within <see cref="CivilizationPropRange"/> blocks of a prop marker of the named pool, <c>air</c> with the head in
/// one of its air pockets.</item>
/// <item><b>The scanner.</b> Any block of a site reads as the site (kind <see cref="CivilizationMapType"/>, its name, the info
/// line <see cref="CivilizationScanInfo"/>); the scan's ledger is the civilization's discovery.</item>
/// <item><b>Rumours.</b> A settler on a world that is no home of a civilization (terrain generation 43 or later, the
/// civilization at home somewhere) sometimes tells one of its <c>rumours</c> — each once per player.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The map/POI type of a civilization site (also the kind of its scan readout and of its silhouette).</summary>
    internal const string CivilizationMapType = "civilization";

    /// <summary>How near (blocks, to the footprint) a player must come for a civilization site to go on the map — and for its
    /// civilization to be discovered and VEGA's <c>first</c> line to be spoken.</summary>
    private const int CivilizationRevealRange = 80;

    /// <summary>How near (blocks) a cell of the named block brings VEGA's <c>block</c> line.</summary>
    internal const int CivilizationBlockRange = 6;

    /// <summary>How near (blocks across, and as many up or down) a prop marker of the named pool brings VEGA's <c>prop</c> line.</summary>
    internal const int CivilizationPropRange = 8;

    /// <summary>The hint id a basic training names in <c>gates.holdHints</c> to hold every civilization line.</summary>
    internal const string CivilizationHintId = "civilization";

    /// <summary>The scan readout's info line of a civilization site.</summary>
    internal const string CivilizationScanInfo = "ui.scan.civilization";

    /// <summary>The prefix of a civilization site's scan subject (<c>civ_&lt;civilization key&gt;</c>): the client names the
    /// people from it.</summary>
    internal const string CivilizationSubjectPrefix = "civ_";

    /// <summary>The prefix of a told rumour's once-flag (<c>rumour:civ:&lt;rumour key&gt;</c>).</summary>
    internal const string CivilizationRumourPrefix = "rumour:civ:";

    /// <summary>The chance per hint that a settler tells a rumour rather than something local (a forced roll skips it).</summary>
    private const double CivilizationRumourChance = 0.5;

    /// <summary>The player's once-flag of one VEGA line of a civilization: <c>vega:civ:&lt;civilization&gt;:&lt;trigger&gt;</c>
    /// and <c>:&lt;value&gt;</c> when the line names one (not under <c>vega:hint:</c>: the tips log does not list it).</summary>
    internal static string CivilizationVegaMilestone(string civilization, CivilizationVegaLine line)
        => "vega:civ:" + civilization + ":" + line.Trigger + (string.IsNullOrEmpty(line.Value) ? string.Empty : ":" + line.Value);

    /// <summary>The numeric ids of the blocks a VEGA <c>block</c> line names, per value (the content never changes at run time).</summary>
    private readonly Dictionary<string, HashSet<ushort>> _civBlockIds = new(System.StringComparer.Ordinal);

    /// <summary>The <c>RevealedPois</c> key of a civilization site (per body, civilization and site).</summary>
    private string CivilizationRevealKey(LandmarkInstance site)
        => _world.LocationId + "|" + CivilizationRules.RecordKind(site.Civilization) + ":"
           + site.CivilizationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The 1 Hz reveal (from <see cref="TickLandmarks"/>): a site goes on the map when a player comes near, and every
    /// player that near discovers its civilization.</summary>
    private void RevealCivilizationSites(List<PlayerSession> players)
    {
        foreach (var site in _civSites)
        {
            var civ = _content.GetCivilization(site.Civilization);
            foreach (var s in players)
            {
                if (LandmarkGap(site, s.State.Position) > CivilizationRevealRange)
                {
                    continue;
                }

                if (!_meta.RevealedPois.Contains(site.RevealKey))
                {
                    RevealPoi(site.RevealKey);
                }

                if (civ is not null)
                {
                    DiscoverCivilization(s, civ);
                }
            }
        }
    }

    /// <summary>The giant sites somebody found, appended to the POI list: the giant wrecks, then the civilization sites.</summary>
    private void AppendGiantSitePois(PlayerSession session, List<NetPoi> pois)
    {
        AppendGiantWreckPois(session, pois);
        foreach (var site in _civSites)
        {
            if (_meta.RevealedPois.Contains(site.RevealKey))
            {
                var (cx, cz, _, _) = site.Rect;
                pois.Add(new NetPoi { Type = CivilizationMapType, Name = CivilizationSiteName(site, session.Locale), X = cx, Z = cz });
            }
        }
    }

    /// <summary>A civilization site's name in a player's language: its pool's map name when the civilization gives one, else
    /// the template's.</summary>
    private string CivilizationSiteName(LandmarkInstance site, string locale)
    {
        if (site.SiteNameKey.Length > 0)
        {
            string text = Localize(locale, site.SiteNameKey);
            if (text != site.SiteNameKey && !text.StartsWith("[", System.StringComparison.Ordinal))
            {
                return text;
            }
        }

        return TemplateDisplayName(site.Template, locale);
    }

    /// <summary>A civilization's name in a player's language, else its key.</summary>
    private string CivilizationName(CivilizationDefinition civ, string locale)
    {
        string text = civ.NameKey.Length > 0 ? Localize(locale, civ.NameKey) : string.Empty;
        return text.Length == 0 || text == civ.NameKey || text.StartsWith("[", System.StringComparison.Ordinal) ? civ.Key : text;
    }

    // ---------------------------------------------------------------- the discovery

    /// <summary>Puts the civilization into the player's discovery ledger (<c>civ:&lt;key&gt;</c>) the first time: a relic's
    /// knowledge, the Codex entry (named after the people, found where the player stands) and the new knowledge total. False
    /// when the player knew it already.</summary>
    private bool DiscoverCivilization(PlayerSession session, CivilizationDefinition civ)
    {
        var p = session.State;
        string key = CivilizationRules.DiscoveryKey(civ.Key);
        if (!p.Scanned.Add(key))
        {
            return false;
        }

        p.KnowledgePoints += (int)System.Math.Round(KnowledgeMonument * ScanMultiplier(p));
        string name = CivilizationName(civ, session.Locale);
        p.ScannedNames[key] = name;
        var where = SiteFor(session);
        if (where is not null)
        {
            p.ScannedWhere[key] = where;
        }

        Send(session, DiscoveryDelta(key, name, where));
        SendInventory(session); // the knowledge total just changed
        _repo.SavePlayer(p);
        return true;
    }

    /// <summary>A scan of a block tagged with a civilization (<see cref="BlockDefinition.Civilization"/>) discovers that
    /// civilization — only when the aimed cell really holds the block and lies within reach, like every counted scan.</summary>
    private void DiscoverCivilizationOfScannedBlock(PlayerSession session, string subjectType, string blockKey)
    {
        if (subjectType != "block" || _scanCell is not { } cell || _content.CivilizationOfBlock(blockKey) is not { } civ
            || _content.GetBlock(blockKey) is not { } def || _world.GetBlockIfLoaded(cell).Value != def.NumericId.Value
            || !WithinReach(session.State, cell))
        {
            return;
        }

        DiscoverCivilization(session, civ);
    }

    // ---------------------------------------------------------------- the scanner

    /// <summary>The civilization site a scan of <paramref name="cell"/> reads: the one whose box holds the aimed cell, when that
    /// cell is one of its blocks.</summary>
    private LandmarkInstance? CivilizationSiteForScan(PlayerSession session, Vector3i? cell)
        => _civSites.Count == 0 || session.CurrentLocationId != _worlds.Active.LocationId || cell is not { } aimed
            ? null
            : CivilizationSiteAt(aimed) is { } site && site.Structure.Get(LocalX(site, aimed), aimed.Y - site.Origin.Y, aimed.Z - site.Origin.Z) != 0
                ? site
                : null;

    /// <summary>The scan readout of a civilization site (filled into the scanner's readout by <c>ScanSubject</c>): kind,
    /// subject (<c>civ_&lt;key&gt;</c>), the localized site name, the info line, the ledger key (the civilization's discovery)
    /// and the name the ledger records (the people's).</summary>
    private (string Kind, string SubjectKey, string Display, string InfoKey, string LedgerKey, string LedgerName) CivilizationScan(
        LandmarkInstance site, string locale)
    {
        var civ = _content.GetCivilization(site.Civilization);
        string name = civ is null ? site.Civilization : CivilizationName(civ, locale);
        return (CivilizationMapType, CivilizationSubjectPrefix + site.Civilization, CivilizationSiteName(site, locale), CivilizationScanInfo,
            CivilizationRules.DiscoveryKey(site.Civilization), name);
    }

    // ---------------------------------------------------------------- VEGA

    /// <summary>The 1 Hz advisor pass of the giant sites (never aboard): the giant wreck's line, then the civilizations'.</summary>
    private void TickGiantSiteHints(PlayerSession session)
    {
        TickGiantWreckHint(session);
        TickCivilizationHints(session);
    }

    /// <summary>VEGA's civilization lines (from the 1 Hz advisor pass, never aboard): for each site within the reveal range,
    /// the first line of its civilization this player has not heard whose trigger holds — one line a pass. Muted hints and a
    /// basic training that holds them keep it for later.</summary>
    private void TickCivilizationHints(PlayerSession session)
    {
        var p = session.State;
        if (_civSites.Count == 0 || p.AboardShip)
        {
            return;
        }

        foreach (var site in _civSites)
        {
            if (LandmarkGap(site, p.Position) > CivilizationRevealRange || _content.GetCivilization(site.Civilization) is not { } civ)
            {
                continue;
            }

            foreach (var line in civ.Vega)
            {
                string milestone = CivilizationVegaMilestone(civ.Key, line);
                if (p.Milestones.Contains(milestone) || !CivilizationVegaTriggerHolds(site, line, p.Position))
                {
                    continue;
                }

                if (HintMutedFor(session, 1) || TutorialHoldsHint(session, CivilizationHintId))
                {
                    return;
                }

                p.Milestones.Add(milestone);
                _repo.SavePlayer(p);
                SendVegaLine(session, line.Key, 1, CivilizationSiteName(site, session.Locale));
                return;
            }
        }
    }

    /// <summary>Whether a VEGA line's trigger holds for a player at <paramref name="pos"/> near <paramref name="site"/> (the
    /// caller asks only within the reveal range, which is all <c>first</c> needs). Reads the site's layout, never the world.</summary>
    private bool CivilizationVegaTriggerHolds(LandmarkInstance site, CivilizationVegaLine line, Vector3f pos)
    {
        string trigger = line.Trigger;
        if (string.Equals(trigger, CivilizationRules.VegaFirst, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        int x = WorldConstants.WrapDeltaX((int)System.Math.Floor(pos.X) - site.Origin.X, _world.Circumference);
        int y = (int)System.Math.Floor(pos.Y) - site.Origin.Y, z = (int)System.Math.Floor(pos.Z) - site.Origin.Z;
        if (string.Equals(trigger, CivilizationRules.VegaInside, System.StringComparison.OrdinalIgnoreCase))
        {
            return site.Structure.InBounds(x, y, z);
        }

        if (string.Equals(trigger, CivilizationRules.VegaAir, System.StringComparison.OrdinalIgnoreCase))
        {
            int head = (int)System.Math.Floor(pos.Y + 1.5f) - site.Origin.Y; // the head cell the oxygen check reads
            return site.Layout.IsAirPocket(x, head, z);
        }

        if (string.Equals(trigger, CivilizationRules.VegaBlock, System.StringComparison.OrdinalIgnoreCase))
        {
            return NearCivilizationBlock(site, x, y, z, CivilizationBlockIds(line.Value));
        }

        return string.Equals(trigger, CivilizationRules.VegaProp, System.StringComparison.OrdinalIgnoreCase)
               && NearCivilizationProp(site, x, y, z, line.Value);
    }

    /// <summary>Whether a cell of the site's layout within <see cref="CivilizationBlockRange"/> blocks of the local cell holds
    /// one of <paramref name="ids"/> — a 13³ walk at most, and only while the line is unheard.</summary>
    private static bool NearCivilizationBlock(LandmarkInstance site, int x, int y, int z, HashSet<ushort> ids)
    {
        var s = site.Structure;
        const int R = CivilizationBlockRange;
        if (ids.Count == 0 || x < -R || y < -R || z < -R || x >= s.Width + R || y >= s.Height + R || z >= s.Length + R)
        {
            return false;
        }

        for (int bx = System.Math.Max(0, x - R); bx <= System.Math.Min(s.Width - 1, x + R); bx++)
        {
            for (int by = System.Math.Max(0, y - R); by <= System.Math.Min(s.Height - 1, y + R); by++)
            {
                for (int bz = System.Math.Max(0, z - R); bz <= System.Math.Min(s.Length - 1, z + R); bz++)
                {
                    int dx = bx - x, dy = by - y, dz = bz - z;
                    if ((dx * dx) + (dy * dy) + (dz * dz) <= R * R && ids.Contains(s.Get(bx, by, bz)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>Whether a prop marker of the pool <paramref name="pool"/> (settings <c>pool=&lt;pool&gt;</c>; the prop markers
    /// stay in the layout) stands within <see cref="CivilizationPropRange"/> blocks across and as many up or down.</summary>
    private static bool NearCivilizationProp(LandmarkInstance site, int x, int y, int z, string pool)
    {
        const int R = CivilizationPropRange;
        foreach (var m in site.Structure.Markers)
        {
            int dx = m.LocalPos.X - x, dz = m.LocalPos.Z - z;
            if (m.Type == CivilizationRules.PropMarker && (dx * dx) + (dz * dz) <= R * R && System.Math.Abs(m.LocalPos.Y - y) <= R
                && string.Equals(MarkerSettings.Parse(m.Data).Get("pool"), pool, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The numeric ids of the blocks a VEGA <c>block</c> value names: one key, or every key with the prefix before a
    /// trailing <c>*</c>. Cached per value.</summary>
    private HashSet<ushort> CivilizationBlockIds(string value)
    {
        if (_civBlockIds.TryGetValue(value, out var ids))
        {
            return ids;
        }

        ids = new HashSet<ushort>();
        bool prefix = value.EndsWith("*", System.StringComparison.Ordinal);
        string stem = prefix ? value.Substring(0, value.Length - 1) : value;
        foreach (var b in _content.Blocks.Values)
        {
            if (stem.Length > 0 && (prefix ? b.Key.StartsWith(stem, System.StringComparison.Ordinal) : b.Key == stem))
            {
                ids.Add(b.NumericId.Value);
            }
        }

        _civBlockIds[value] = ids;
        return ids;
    }

    // ---------------------------------------------------------------- rumours

    /// <summary>A settler's rumour of a lost civilization (from the NPC hint, GameServerNpcHints.cs): on a world of terrain
    /// generation 43 or later, one of the rumours of a civilization that is at home somewhere but not on this body, not yet told
    /// to this player — its once-flag set, the line in the player's language. Empty when none is left, or (unless
    /// <paramref name="forceRoll"/>) when the <see cref="CivilizationRumourChance"/> roll says "not this time".</summary>
    private string TryEmitCivilizationRumour(PlayerSession session, bool forceRoll)
    {
        if (_meta.Description.TerrainGeneration < WorldDescription.CivilizationsGeneration)
        {
            return string.Empty;
        }

        var p = session.State;
        var untold = new List<string>();
        foreach (var civ in _content.Civilizations)
        {
            if (!_meta.CivilizationHomes.TryGetValue(civ.Key, out var homes) || homes.Count == 0 || homes.Contains(_world.LocationId))
            {
                continue; // no home in this galaxy, or this body is one: here it is no rumour
            }

            untold.AddRange(civ.Rumours.Where(r => r.Length > 0 && !p.Milestones.Contains(CivilizationRumourPrefix + r)));
        }

        if (untold.Count == 0 || (!forceRoll && _hintRng.NextDouble() >= CivilizationRumourChance))
        {
            return string.Empty; // the roll only once there is something to tell: an older world's hints keep their dice
        }

        string pick = untold[_hintRng.Next(untold.Count)];
        p.Milestones.Add(CivilizationRumourPrefix + pick);
        return Localize(session.Locale, pick);
    }

    /// <summary>Test seam: a settler's rumour for the player right now, the roll forced (see <see cref="TryEmitCivilizationRumour"/>).</summary>
    internal string CivilizationRumourForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s ? TryEmitCivilizationRumour(s, forceRoll: true) : string.Empty;
}
