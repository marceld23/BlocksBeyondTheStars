// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A civilization site in the living world (#3642) — see GameServerCivilizations.cs. The landmark's world parts carry it
/// (GameServerLandmarkWorld.cs): its chests and sound emitters, the streaming lift; a site on land also its far silhouette
/// (a sunken one casts none — the sea hides it). What is its own:
/// <list type="bullet">
/// <item><b>The map.</b> On everyone's map (type <see cref="CivilizationMapType"/>, the site's name) once a player on foot or
/// swimming comes within <see cref="CivilizationRevealRange"/> blocks of its footprint.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The map/POI type of a civilization site.</summary>
    internal const string CivilizationMapType = "civilization";

    /// <summary>How near (blocks, to the footprint) a player must come for a civilization site to go on the map.</summary>
    private const int CivilizationRevealRange = 80;

    /// <summary>The <c>RevealedPois</c> key of a civilization site (per body, civilization and site).</summary>
    private string CivilizationRevealKey(LandmarkInstance site)
        => _world.LocationId + "|" + CivilizationRules.RecordKind(site.Civilization) + ":"
           + site.CivilizationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The 1 Hz reveal (from <see cref="TickLandmarks"/>): a site goes on the map when a player comes near.</summary>
    private void RevealCivilizationSites(List<PlayerSession> players)
    {
        foreach (var site in _civSites)
        {
            if (!_meta.RevealedPois.Contains(site.RevealKey) && players.Any(s => LandmarkGap(site, s.State.Position) <= CivilizationRevealRange))
            {
                RevealPoi(site.RevealKey);
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

    /// <summary>The 1 Hz advisor pass of the giant sites (never aboard): the giant wreck's line, then the civilizations'.</summary>
    private void TickGiantSiteHints(PlayerSession session)
    {
        TickGiantWreckHint(session);
    }
}
