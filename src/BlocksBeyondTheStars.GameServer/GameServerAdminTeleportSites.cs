// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The <c>/tp</c> targets of the lost civilizations' sites (#3654) and of the giant wrecks (#3655): a civilization site of the
/// current body — on land or under water on the seabed —, the giant wrecks on the current body named after their ship, and
/// the giant wrecks in space of the player's current system, which a teleport BOARDS directly (the station travel path).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>#3654: every civilization site of the current body, in stamp order, named in the player's language — the
    /// target stands in front of the footprint (like a landmark's), on the seabed for a site under water.</summary>
    private void AppendCivilizationTargets(List<TeleportTarget> list, PlayerSession session)
    {
        for (int i = 0; i < _civSites.Count; i++)
        {
            var site = _civSites[i];
            var (cx, _, _, _) = site.Rect;
            list.Add(new TeleportTarget("civ", i + 1, Named("civ", i + 1, LandmarkName(site, session.Locale)),
                StandSpot(WorldConstants.WrapX(cx, _world.Circumference), site.Origin.Z - 3)));
        }
    }

    /// <summary>#3655: the giant wrecks on the current body (named after their ship) and then those in space of the player's
    /// current system — a space wreck carries its station id, so the teleport boards it instead of moving the player on this
    /// body.</summary>
    private void AppendGiantWreckTargets(List<TeleportTarget> list, PlayerSession session)
    {
        int n = 0;
        for (int i = 0; i < _giantWrecks.Count; i++)
        {
            var (cx, _, _, _) = _giantWrecks[i].Rect;
            n++;
            list.Add(new TeleportTarget("giantwreck", n, Named("giantwreck", n, LandmarkName(_giantWrecks[i], session.Locale)),
                SurfaceSpot(WorldConstants.WrapX(cx, _world.Circumference), _giantWrecks[i].Origin.Z - 3)));
        }

        foreach (var (id, label) in SpaceGiantWrecksOfSystem(session))
        {
            n++;
            list.Add(new TeleportTarget("giantwreck", n, Named("giantwreck", n, label), default, id));
        }
    }

    /// <summary>The giant wrecks in space of the player's current system: (station id, "name · ship"). One that had to stay away
    /// is left out; listing builds none (that happens on the way aboard, where <see cref="TravelToStation"/> checks it).</summary>
    private List<(string Id, string Label)> SpaceGiantWrecksOfSystem(PlayerSession session)
    {
        var result = new List<(string, string)>();
        string? system = SystemOf(session)?.Id;
        if (system is null)
        {
            return result;
        }

        foreach (var site in _giantDerelicts.Values.Where(s => s.SystemId == system).OrderBy(s => s.Index))
        {
            if (!site.Retired)
            {
                result.Add((site.Id, site.Body.Name + " · " + TemplateDisplayName(site.Template, session.Locale)));
            }
        }

        return result;
    }

    /// <summary>The targets <c>/tp</c> offers while the player is flying: only the giant wrecks in space of the system.</summary>
    private List<TeleportTarget> SpaceTeleportTargets(PlayerSession session)
    {
        var list = new List<TeleportTarget>();
        int n = 0;
        foreach (var (id, label) in SpaceGiantWrecksOfSystem(session))
        {
            n++;
            list.Add(new TeleportTarget("giantwreck", n, Named("giantwreck", n, label), default, id));
        }

        return list;
    }

    /// <summary>A standing spot over column (x, z): from the generated ground up to the first two cells a body fits in — a
    /// liquid counts as passable, so under water this is the seabed, on land the ground (#3654).</summary>
    private Vector3f StandSpot(int x, int z)
    {
        int y = _generator.SurfaceHeight(_world.Planet, x, z) + 1;
        for (int i = 0; i < TeleportClearProbeHeight; i++, y++)
        {
            if (!IsBodyBlockingCell(x, y, z) && !IsBodyBlockingCell(x, y + 1, z))
            {
                break;
            }
        }

        return new Vector3f(x + 0.5f, y, z + 0.5f);
    }
}
