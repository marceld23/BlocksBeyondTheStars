// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The way from a player to one of the galaxy's unique sites (#3415) — one rule for VEGA's chapter leads
/// (<see cref="ResolveUniqueSite"/>) and for a pack step's <c>unique_site:&lt;kind&gt;</c> marker
/// (<see cref="MarkUniqueSiteReveal"/>, the Sounders' Singing Shrine): which body the site is on, whether it exists in this
/// save and the player can get there, its spot while the player stands on its world, and whether the player is in the
/// site's system. The lead turns that into its marker or space waypoint; the pack marker also names the way from afar,
/// which a lead never did (it is only offered where it can be followed).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The narrator line of a <c>unique_site:&lt;kind&gt;</c> marker while the player is in another system ({0} = the
    /// site's world, {1} = its system).</summary>
    internal const string StepMarkedWorldLine = "vega.step.marked_world";

    /// <summary>A unique site as seen from one player. <c>Target</c> is the site's body (the derelict: its station id),
    /// <c>NameBodyId</c> the body that names it (the derelict: its host body), <c>GroundSpot</c> the site's spot while the
    /// player is on its world (else null), <c>InSiteSystem</c> whether the player is in the site's system.</summary>
    private readonly record struct UniqueSiteRoute(string Target, string NameBodyId, bool Exists, bool Followable, Vector3f? GroundSpot, bool InSiteSystem);

    /// <summary>The route to a unique site for <paramref name="s"/> (<paramref name="world"/> = the world they stand on, null in
    /// flight), or null when the galaxy has no such site. Followable is the lead's rule (<see cref="UniqueSiteFollowable"/>):
    /// the site exists in this save and the player can reach it.</summary>
    private UniqueSiteRoute? RouteToUniqueSite(PlayerSession s, string kind, LoadedWorld? world)
    {
        if (UniqueSiteTarget(kind) is not { } target)
        {
            return null;
        }

        bool exists = UniqueSiteExists(kind, target);
        string? hereSystem = ResolveLocationHostBody(s.CurrentLocationId)?.SystemId;
        return new UniqueSiteRoute(
            target,
            kind == "derelict" ? UniqueSiteTarget("derelict_host") ?? target : target,
            exists,
            exists && CanReachBody(s, target),
            world is not null && world.LocationId == target ? UniqueSiteGroundSpot(kind, target) : null,
            string.Equals(hereSystem, _galaxy?.FindBody(target)?.SystemId, StringComparison.Ordinal));
    }

    /// <summary>The <c>unique_site:&lt;kind&gt;</c> marker of a pack step, wherever the player is (#3415). On the site's world
    /// the ground marker with <see cref="StepMarkedLine"/> — the marker is spent. For the shrine (#3416) the nearest complete
    /// rune shrine of the player's world comes first: any singing shrine answers the Sounders, so its altar is marked instead
    /// of sending the player to the one-of-a-kind shrine (on that shrine's own world the nearer of the two). Anywhere else
    /// nothing is spent (the ground marker still comes on arrival: a new place probes at once), but the narrator names the
    /// way (<see cref="SpeakUniqueSiteWay"/>). Nothing for a site this save does not have. The one place a step's site marker
    /// is chosen. Returns whether the marker went up.</summary>
    private bool MarkUniqueSiteReveal(PlayerSession s, CompiledStep step, string kind)
    {
        bool inSpace = InSpace(s.State.PlayerId);
        var world = inSpace ? null : _worlds.Find(s.CurrentLocationId ?? string.Empty);
        var route = RouteToUniqueSite(s, kind, world);
        if (ShrineGroundSpot(s, kind, route?.Exists == true ? route.Value.GroundSpot : null, inSpace) is { } spot)
        {
            if (!RaiseStoryMarker(s, step, spot))
            {
                return false;
            }

            SendPackLine(s, step.Pack, StepMarkedLine);
            return true;
        }

        if (route is not { Exists: true } site || !s.Story.StepEscalation.TryGetValue(step.ProgressKey, out var esc))
        {
            return false;
        }

        SpeakUniqueSiteWay(s, step, site, esc, inSpace);
        return false;
    }

    /// <summary>#3416: where a marker goes on the player's world. For the shrine: the altar of the nearest complete rune shrine
    /// (<see cref="NearestRuneShrine"/>, read once per probe — the generator reads the active world, so only while the player
    /// stands on it), unless the one-of-a-kind site's own spot <paramref name="siteSpot"/> is nearer. Otherwise
    /// <paramref name="siteSpot"/> (null off the site's world).</summary>
    private Vector3f? ShrineGroundSpot(PlayerSession s, string kind, Vector3f? siteSpot, bool inSpace)
    {
        if (kind != "shrine" || inSpace || RuneShrineSpot(s) is not { } rune)
        {
            return siteSpot;
        }

        if (siteSpot is not { } unique)
        {
            return rune;
        }

        var at = s.State.Position;
        int circ = _world.Circumference;
        return WorldConstants.WrapDistanceSquared(unique, at, circ) <= WorldConstants.WrapDistanceSquared(rune, at, circ) ? unique : rune;
    }

    /// <summary>#3416: the marker spot on the altar of the nearest complete rune shrine of the player's world
    /// (<see cref="NearestRuneShrine"/>), or null — in flight, aboard a station, on a world that is not the active cursor (the
    /// generator reads the active world), or none in reach. The <see cref="StoryRevealProviders.NearestRuneShrine"/> provider
    /// and the shrine's <c>unique_site</c> marker read it once per probe.</summary>
    private Vector3f? RuneShrineSpot(PlayerSession s)
    {
        if (InSpace(s.State.PlayerId) || InStation(s.State.PlayerId) || string.IsNullOrEmpty(s.CurrentLocationId)
            || s.CurrentLocationId != _world.LocationId || NearestRuneShrine(s.State.Position) is not { } altar)
        {
            return null;
        }

        return new Vector3f(altar.X + 0.5f, altar.Y + 1f, altar.Z + 0.5f);
    }

    /// <summary>The way to a unique site the player is not on (#3415), each stage once per step activation: in the site's
    /// system the site's world becomes the space waypoint (<see cref="StoryNarratorLines.StepMarkedStation"/>, the dock
    /// lesson's line); from another system <see cref="StepMarkedWorldLine"/> names the world and its system.</summary>
    private void SpeakUniqueSiteWay(PlayerSession s, CompiledStep step, UniqueSiteRoute route, StepEscalationState esc, bool inSpace)
    {
        string worldName = GalaxyBodyName(route.NameBodyId);
        if (worldName.Length == 0)
        {
            return;
        }

        if (route.InSiteSystem)
        {
            if (!esc.SiteNearLineSent && (inSpace || s.CurrentLocationId != route.Target))
            {
                esc.SiteNearLineSent = true;
                SendPackLine(s, step.Pack, StoryNarratorLines.StepMarkedStation, worldName, waypointId: route.Target);
            }
        }
        else if (!esc.SiteFarLineSent && GalaxySystemName(s, route.Target) is { Length: > 0 } systemName)
        {
            esc.SiteFarLineSent = true;
            SendPackLine(s, step.Pack, StepMarkedWorldLine, worldName + LineText.ArgSeparator + systemName);
        }
    }

    /// <summary>The star-map name of the system a body belongs to (<see cref="StarMapSystemName"/>), or empty.</summary>
    private string GalaxySystemName(PlayerSession s, string bodyId)
    {
        string? systemId = _galaxy.FindBody(bodyId)?.SystemId;
        var system = systemId is null ? null : _galaxy.Systems.FirstOrDefault(x => x.Id == systemId);
        return system is null ? string.Empty : StarMapSystemName(s, system);
    }
}
