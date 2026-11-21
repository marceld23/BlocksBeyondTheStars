// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The guide towers in the stories and in the settlers' mouths (#3895; the towers' life is GameServerSpsTowerLife.cs).
/// <list type="bullet">
/// <item><b>VEGA's lines by what the player's story knows</b> (<see cref="SpsTowerVegaKey"/>, the guardian giants' pattern):
/// at the first approach, at the first read of a console, at the lighting. Before the Service (B2) she is puzzled; knowing the
/// Service she names a guide tower, "the sky-lights"; knowing the verdict (B6) she names the night it turned red and whose
/// doing it was; after the finale "it may be blue again". The Guardian is never named before the player's own story heard
/// of it. Without the VEGA story the neutral lines of the engine's locale.</item>
/// <item><b>One insight</b> (<see cref="SpsTowerInsightEvent"/>) for the Journal's VEGA notes: what the sky-lights were.</item>
/// <item><b>The settlers</b>: a rumour that points at the tower of their world (<see cref="TryEmitSpsTowerRumour"/>, from the
/// NPC hint), and the world tags <see cref="SpsTowerRedTag"/> / <see cref="SpsTowerBlueTag"/> for the story's flavour lines
/// (fear and legend while a tower of their world shows red, "the light is blue again" once one is lit).</item>
/// <item><b>For the story steps</b>: the predicates <c>world:sps_tower_lit_near</c>, <c>world:sps_tower_known</c>,
/// <c>world:sps_guard_post_known</c> and <c>world:sps_guard_awake</c> (GameServerStoryConditions.cs), and the guide markers
/// <c>nearest_sps_tower</c> and <c>nearest_sps_guard</c> (<see cref="MarkSpsTowerReveal"/>).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The story insight a read or lit tower speaks (VEGA remembers the sky-lights; held until "The Service").</summary>
    internal const string SpsTowerInsightEvent = "sps_tower";

    /// <summary>The world tag of the flavour lines while a guide tower of the world still shows red.</summary>
    internal const string SpsTowerRedTag = "sps_tower_red";

    /// <summary>The world tag of the flavour lines once a guide tower of the world is lit blue.</summary>
    internal const string SpsTowerBlueTag = "sps_tower_blue";

    /// <summary>The VEGA beat after which a player's story knows the Service (B2, "The Service").</summary>
    private const int SpsTowerServiceBeat = 2;

    /// <summary>The VEGA beat after which a player's story knows the verdict (B6, "The Verdict").</summary>
    private const int SpsTowerVerdictBeat = 6;

    /// <summary>What a player's story knows for VEGA's tower lines.</summary>
    internal enum SpsTowerLore
    {
        /// <summary>No VEGA story here: the neutral lines.</summary>
        Neutral,

        /// <summary>The player's story has not reached the Service (B2): a broken tower with red lamps, no record of it.</summary>
        Puzzled,

        /// <summary>The Service is known (B2): a guide tower of the Service — the sky-lights.</summary>
        Service,

        /// <summary>The verdict is known (B6): the night it turned red — that was the Guardian.</summary>
        Verdict,

        /// <summary>The Guardian core is shut down: it may be blue again.</summary>
        Blue,
    }

    /// <summary>The moments VEGA speaks about a tower.</summary>
    internal enum SpsTowerVegaMoment
    {
        /// <summary>The player first comes near a tower.</summary>
        Found,

        /// <summary>The player first reads a tower's console.</summary>
        Read,

        /// <summary>A tower's lamps turn blue.</summary>
        Lit,
    }

    /// <summary>Whether the world's story is the VEGA data pack (its lore texts, its lines, the sleeper's dialog exist).</summary>
    internal bool SpsTowerStoryRuns
        => _story is not null && !ReferenceEquals(_story, StoryRegistry.Default)
           && string.Equals(_story.Id, StoryRegistry.DefaultStoryId, StringComparison.OrdinalIgnoreCase);

    /// <summary>What the player's story knows for the tower lines: the VEGA pack's beats this player heard, and the finale.</summary>
    internal SpsTowerLore SpsTowerLoreOf(PlayerSession s)
    {
        if (!SpsTowerStoryRuns)
        {
            return SpsTowerLore.Neutral;
        }

        var heard = s.State.Milestones;
        if (heard.Contains(StoryPackKeys.Beat(_story!.Id, SpsTowerVerdictBeat)))
        {
            return _storyState.GuardianDefeated ? SpsTowerLore.Blue : SpsTowerLore.Verdict;
        }

        return heard.Contains(StoryPackKeys.Beat(_story.Id, SpsTowerServiceBeat)) ? SpsTowerLore.Service : SpsTowerLore.Puzzled;
    }

    /// <summary>
    /// The locale key of VEGA's tower line for a moment: <c>story.vega.tower.&lt;found|read|lit&gt;.&lt;puzzled|service|verdict|blue&gt;</c>
    /// in the VEGA pack's locale by what THIS player's story knows, or the neutral <c>vega.tower.&lt;moment&gt;</c> of the
    /// engine's locale without the VEGA story.
    /// </summary>
    internal string SpsTowerVegaKey(PlayerSession s, SpsTowerVegaMoment moment)
    {
        var lore = SpsTowerLoreOf(s);
        string word = SpsTowerMomentWord(moment);
        return lore == SpsTowerLore.Neutral ? "vega.tower." + word : "story.vega.tower." + word + "." + lore.ToString().ToLowerInvariant();
    }

    /// <summary>A moment's word in the line keys.</summary>
    internal static string SpsTowerMomentWord(SpsTowerVegaMoment moment) => moment switch
    {
        SpsTowerVegaMoment.Found => "found",
        SpsTowerVegaMoment.Read => "read",
        _ => "lit",
    };

    /// <summary>1 Hz per player on foot: VEGA's words the first time they come within the found range of a tower — once per
    /// player and tower. A tower somebody lit already gets its own line (its lamps are blue).</summary>
    private void SpsTowerApproach(PlayerSession s)
    {
        if (SpsTowerInFoundRange(s) is not { } tower || !s.State.Milestones.Add(SpsTowerSeenKey(tower.SiteId)))
        {
            return;
        }

        _repo.SavePlayer(s.State);
        SendVegaLine(s, tower.Lit ? "vega.tower.found_blue" : SpsTowerVegaKey(s, SpsTowerVegaMoment.Found), 2);
    }

    /// <summary>VEGA's words about a tower's records — at the player's first read at that tower — and her memory of the
    /// sky-lights (the insight, held until the player's story knows the Service).</summary>
    private void SpeakSpsTowerRead(PlayerSession s, SpsTowerRecord tower)
    {
        if (s.State.Milestones.Add(SpsTowerReadKey(tower.SiteId)))
        {
            _repo.SavePlayer(s.State);
            SendVegaLine(s, SpsTowerVegaKey(s, SpsTowerVegaMoment.Read), 2);
        }

        RevealSpsTowerInsight(s);
    }

    /// <summary>VEGA remembers what the sky-lights were (the pack's insight <see cref="SpsTowerInsightEvent"/>, once per
    /// player) — only once the player's own story knows the Service.</summary>
    private void RevealSpsTowerInsight(PlayerSession s)
    {
        if (SpsTowerLoreOf(s) >= SpsTowerLore.Service)
        {
            RevealStoryInsight(s, SpsTowerInsightEvent);
        }
    }

    // ---------------------------------------------------------------- the settlers

    /// <summary>A settler's rumour of the old sky-light (from the NPC hint, GameServerNpcHints.cs): the nearest guide tower of
    /// this world that is not on the map yet goes on everyone's map, and the line gives its direction and distance. Empty
    /// when every tower of the world is on the map (or it has none).</summary>
    private string TryEmitSpsTowerRumour(PlayerSession session)
    {
        if (_meta.SpsTowers.Count == 0)
        {
            return string.Empty;
        }

        SpsTowerRecord? best = null;
        double bestSq = double.MaxValue;
        foreach (var tower in _meta.SpsTowers)
        {
            if (tower.LocationId != _world.LocationId || SpsTowerRevealed(tower))
            {
                continue;
            }

            double d = WrapDistSq(session.State.Position, SpsTowerCentre(tower));
            if (d < bestSq)
            {
                (best, bestSq) = (tower, d);
            }
        }

        if (best is null)
        {
            return string.Empty;
        }

        RevealSpsTower(best);
        var at = SpsTowerCentre(best);
        return HintLine(session, best.Lit ? "npc.hint.sps_tower_blue" : "npc.hint.sps_tower", at.X, at.Z);
    }

    /// <summary>Adds the tower tags of the active world to the flavour lines' world tags: <see cref="SpsTowerRedTag"/> while a
    /// tower of the world is unlit, <see cref="SpsTowerBlueTag"/> once one is lit.</summary>
    private void AddSpsTowerStoryTags(HashSet<string> tags)
    {
        if (_meta.SpsTowers.Count == 0 || _world is null)
        {
            return;
        }

        foreach (var tower in _meta.SpsTowers)
        {
            if (tower.LocationId == _world.LocationId)
            {
                tags.Add(tower.Lit ? SpsTowerBlueTag : SpsTowerRedTag);
            }
        }
    }

    // ---------------------------------------------------------------- the guide markers

    /// <summary>
    /// The guide markers <c>nearest_sps_tower</c> and (<paramref name="guard"/>) <c>nearest_sps_guard</c> of a story step.
    /// On a world that has one: a ground marker on the nearest tower's relay console — for the guard on the sleeper where he
    /// stands (his post or the settlement he went to), or on the sealed capsule tank of the nearest guard post — with the
    /// usual "marked" line; the marker is spent. From a world without one, when a tower (a guard post) stands on another
    /// body somebody visited: nothing is spent, but the narrator names that world and its system, or makes it the flight
    /// target inside its system — the way a unique site is named (<see cref="SpeakUniqueSiteWay"/>); the ground marker comes
    /// on arrival. While no tower is known anywhere: no marker and no line (the step's own text says where one will be:
    /// <c>world:sps_tower_known</c>). Returns whether a marker went up.
    /// </summary>
    private bool MarkSpsTowerReveal(PlayerSession s, CompiledStep step, bool guard)
    {
        if (_meta.SpsTowers.Count == 0)
        {
            return false;
        }

        bool inSpace = InSpace(s.State.PlayerId);
        string here = s.CurrentLocationId ?? string.Empty;
        if (!inSpace && SpsMarkerSpotOn(s, here, guard) is { } spot)
        {
            if (!RaiseStoryMarker(s, step, spot))
            {
                return false;
            }

            SendPackLine(s, step.Pack, StepMarkedLine);
            return true;
        }

        if (SpsMarkerBodyElsewhere(here, guard) is not { } body || !s.Story.StepEscalation.TryGetValue(step.ProgressKey, out var esc))
        {
            return false;
        }

        string? hereSystem = ResolveLocationHostBody(here)?.SystemId;
        bool sameSystem = hereSystem is not null && string.Equals(hereSystem, _galaxy?.FindBody(body)?.SystemId, StringComparison.Ordinal);
        SpeakUniqueSiteWay(s, step, new UniqueSiteRoute(body, body, true, CanReachBody(s, body), null, sameSystem), esc, inSpace);
        return false;
    }

    /// <summary>The marker's spot on body <paramref name="locationId"/> for the player standing there: the nearest tower's
    /// console (its footprint's middle without one); for the guard the sleeper of the nearest opened tank where he stands,
    /// else the nearest sealed tank. Null when the body has none.</summary>
    private Vector3f? SpsMarkerSpotOn(PlayerSession s, string locationId, bool guard)
    {
        if (!guard)
        {
            return NearestSpsTower(s.State.Position, locationId, out _) is { } tower ? SpsTowerMarkerSpot(tower) : null;
        }

        int circumference = _worlds.Find(locationId)?.World.Circumference ?? _world.Circumference;
        SpsTowerRecord? best = null;
        double bestSq = double.MaxValue;
        foreach (var tower in _meta.SpsTowers)
        {
            if (tower.LocationId != locationId || !tower.HasGuardPost)
            {
                continue;
            }

            double d = Shared.World.WorldConstants.WrapDistanceSquared(s.State.Position, SpsTowerCentre(tower), circumference);
            if (d < bestSq)
            {
                (best, bestSq) = (tower, d);
            }
        }

        if (best is null)
        {
            return null;
        }

        return best.TankOpened && SleeperPosition(best) is { } standing ? standing
            : best.Tank is { } tank ? new Vector3f(tank.X + 0.5f, tank.Y + 1f, tank.Z + 0.5f)
            : SpsTowerMarkerSpot(best);
    }

    /// <summary>Where a tower's marker points: its relay console, else the middle of its footprint.</summary>
    private static Vector3f SpsTowerMarkerSpot(SpsTowerRecord tower)
        => tower.Console is { } c ? new Vector3f(c.X + 0.5f, c.Y + 1f, c.Z + 0.5f) : SpsTowerCentre(tower);

    /// <summary>A visited body other than <paramref name="here"/> that holds a guide tower (with a guard post, for the guard):
    /// one in the player's own system first, else the first one recorded. Null when none is known.</summary>
    private string? SpsMarkerBodyElsewhere(string here, bool guard)
    {
        string? hereSystem = ResolveLocationHostBody(here)?.SystemId;
        string? first = null;
        foreach (var tower in _meta.SpsTowers)
        {
            if (tower.LocationId == here || (guard && !tower.HasGuardPost) || _galaxy?.FindBody(tower.LocationId) is not { } body)
            {
                continue;
            }

            if (hereSystem is not null && body.SystemId == hereSystem)
            {
                return tower.LocationId;
            }

            first ??= tower.LocationId;
        }

        return first;
    }

    // ---------------------------------------------------------------- test seams

    /// <summary>Test seam: VEGA's tower line key for a player and a moment.</summary>
    internal string SpsTowerVegaKeyForTest(PlayerSession s, SpsTowerVegaMoment moment) => SpsTowerVegaKey(s, moment);

    /// <summary>Test seam: where the guide marker <c>nearest_sps_tower</c> (or <c>nearest_sps_guard</c>) would stand for a player
    /// on their own body, and the other body it would name from afar.</summary>
    internal (Vector3f? Spot, string? Elsewhere) SpsMarkerForTest(PlayerSession s, bool guard = false)
    {
        string here = s.CurrentLocationId ?? string.Empty;
        var spot = InSpace(s.State.PlayerId) ? null : SpsMarkerSpotOn(s, here, guard);
        return (spot, spot is null ? SpsMarkerBodyElsewhere(here, guard) : null);
    }

    /// <summary>Test seam: a settler's tower rumour for the player right now.</summary>
    internal string SpsTowerRumourForTest(PlayerSession s) => TryEmitSpsTowerRumour(s);

    /// <summary>Test seam: the flavour lines' world tags of the active world.</summary>
    internal IReadOnlyCollection<string> WorldStoryTagsForTest() => WorldStoryTags();
}
