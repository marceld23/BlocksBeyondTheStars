// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>#3344: a companion's selection state on its live entity (server-only, never persisted — the record is
/// <see cref="TamedCreature"/>).</summary>
public sealed partial class CombatEntity
{
    /// <summary>A planet pet that stays home: it potters about <see cref="HomeSpot"/> instead of following its owner, and
    /// gives none of the follow payoffs (fetch, growl, bandit stall and ward, machine ward, scouting).</summary>
    public bool StaysHome { get; set; }

    /// <summary>Where a pet that <see cref="StaysHome"/> lives.</summary>
    public Vector3f HomeSpot { get; set; }

    /// <summary>A travelling companion (robot, Rudolpho, the Soundi): it takes no pet slot and is nothing to sample.</summary>
    public bool CompanionRoams { get; set; }

    /// <summary>The companion's <see cref="TamedCreature.Kind"/> (empty for a tamed animal) — the story Soundi sings.</summary>
    public string CompanionKind { get; set; } = string.Empty;
}

/// <summary>
/// Companion selection (#3344): the player picks who follows. Every companion record carries
/// <see cref="TamedCreature.Following"/> (true by default, so an old save behaves exactly as before):
/// <list type="bullet">
/// <item>A <b>planet pet</b> that does not follow <b>stays home</b>: while its owner is on its world it appears at its home
/// spot (<see cref="TamedCreature.HasHomeSpot"/>: where it was tamed, or where "set home here" put it), wanders within
/// <see cref="CompanionHomeWanderRange"/> blocks and is leashed to home instead of to its owner.</item>
/// <item>A <b>travelling companion</b> (robot, Rudolpho, the Soundi) that does not follow <b>waits on board</b> — it is not
/// spawned anywhere; the Companions tab brings it back with one click.</item>
/// <item>The follow payoffs (fetch, the growl and the robber stall, the bandit and machine wards, scouting, the robot's
/// guarding) come only from followers; the at-home effects (the produce drop and its sample) keep working.</item>
/// </list>
/// The caps are unchanged: six planet pets per world (a pet at home is one of them), travelling companions apart.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A pet at home potters about within this many blocks of its home spot.</summary>
    internal const float CompanionHomeWanderRange = 8f;

    /// <summary>A pet at home farther than this from its home spot is put back there (the home leash).</summary>
    internal const float CompanionHomeLeashRange = 16f;

    /// <summary>A pet at home holds still while its owner is farther than this from its home — nobody sees it there, and
    /// stepping it would read terrain far from every player.</summary>
    private const float CompanionHomeIdleRange = 96f;

    /// <summary>Whether a companion record stays home on its world: a planet pet the player does not take along.</summary>
    internal static bool CompanionStaysHome(TamedCreature tc) => !tc.Following && !tc.Roams;

    /// <summary>Whether a companion record waits on board: a travelling companion the player does not take along.</summary>
    internal static bool CompanionOnBoard(TamedCreature tc) => !tc.Following && tc.Roams;

    /// <summary>Whether a live companion follows its owner — the follow payoffs come only from these.</summary>
    private static bool CompanionFollowsOwner(CombatEntity c) => c.IsCompanion && !c.StaysHome;

    private static Vector3f HomeSpotOf(TamedCreature tc) => new(tc.HomeX, tc.HomeY, tc.HomeZ);

    private static void SetHomeSpot(TamedCreature tc, Vector3f at)
    {
        tc.HasHomeSpot = true;
        tc.HomeX = at.X;
        tc.HomeY = at.Y;
        tc.HomeZ = at.Z;
    }

    /// <summary>The spot a companion entity appears near: a pet at home at its home spot (when it has one), everyone else
    /// beside the owner.</summary>
    private static Vector3f CompanionSpawnAnchor(TamedCreature tc, Vector3f nearOwner)
        => CompanionStaysHome(tc) && tc.HasHomeSpot ? HomeSpotOf(tc) : nearOwner;

    /// <summary>Stamps the selection state of a freshly spawned companion entity. A pet at home without a home spot yet (a
    /// record from before #3344) makes the spot it appeared on its home.</summary>
    private static void StampCompanionSelection(CombatEntity entity, TamedCreature tc)
    {
        entity.CompanionRoams = tc.Roams;
        entity.CompanionKind = tc.Kind ?? string.Empty;
        StampRobotGear(entity, tc); // #3398/#3399: the robot's weapon and jetpack
        entity.StaysHome = CompanionStaysHome(tc);
        if (!entity.StaysHome)
        {
            return;
        }

        if (!tc.HasHomeSpot)
        {
            SetHomeSpot(tc, entity.Position);
        }

        entity.HomeSpot = HomeSpotOf(tc);
    }

    // ---------------- The intent ----------------

    /// <summary>The Companions tab's choice: one companion or all of them follow or not, or a present planet pet gets the
    /// spot the player stands on as its new home. The records change at once; the next creature tick spawns a companion
    /// that comes along again and takes one that waits on board away.</summary>
    private void HandleSetCompanionFollow(PlayerSession session, SetCompanionFollowIntent intent)
    {
        var p = session.State;
        if (intent.SetHomeHere)
        {
            SetCompanionHomeHere(session, p.TamedCreatures.FirstOrDefault(t => t.Id == intent.CompanionId));
            return;
        }

        var chosen = intent.All
            ? p.TamedCreatures.ToList()
            : p.TamedCreatures.Where(t => t.Id == intent.CompanionId).ToList();
        int changed = 0;
        foreach (var tc in chosen)
        {
            if (tc.Following != intent.Following)
            {
                ApplyFollowChoice(session, tc, intent.Following);
                changed++;
            }
        }

        if (changed > 0)
        {
            _repo.SavePlayer(p);
            BroadcastCreatures();
            Send(session, new ServerMessage { Text = FollowChoiceLine(intent, chosen) });
        }

        SendCompanions(session);
    }

    /// <summary>What the player is told: the one companion's new state, or the whole group's.</summary>
    private static string FollowChoiceLine(SetCompanionFollowIntent intent, List<TamedCreature> chosen)
    {
        if (intent.All)
        {
            return intent.Following ? "@srv.companion.all_follow" : "@srv.companion.all_stay";
        }

        var tc = chosen[0];
        string what = tc.Following ? "follows" : tc.Roams ? "on_board" : "stays_home";
        return "@srv.companion." + what + ":" + CompanionDisplayName(tc);
    }

    /// <summary>One companion's follow choice, record and live entity: a pet at home starts pottering about its home spot
    /// (the spot it stands on, if it has none yet), a follower trots back to its owner, one that waits on board leaves the
    /// world and one that comes along again appears beside its owner.</summary>
    private void ApplyFollowChoice(PlayerSession session, TamedCreature tc, bool following)
    {
        tc.Following = following;
        var live = _creatures.FirstOrDefault(c => c.CompanionId == tc.Id && c.OwnerId == session.State.PlayerId);
        if (CompanionOnBoard(tc))
        {
            _creatures.RemoveAll(c => c.CompanionId == tc.Id);
            return;
        }

        if (live is not null)
        {
            StampCompanionSelection(live, tc);
            return;
        }

        if (tc.Roams && session.CurrentLocationId == _world.LocationId)
        {
            SpawnCompanionsForSession(session); // back from on board: it steps out beside its owner (never in space)
        }
    }

    /// <summary>"Set home here": a planet pet's home becomes the spot where its owner stands — on the pet's own world, on
    /// foot. A travelling companion has no home to set.</summary>
    private void SetCompanionHomeHere(PlayerSession session, TamedCreature? tc)
    {
        if (tc is null)
        {
            return;
        }

        var p = session.State;
        string name = CompanionDisplayName(tc);
        if (tc.Roams)
        {
            Reject(session, "companion", "@srv.companion.home_travels:" + name);
            return;
        }

        if (tc.HomeBodyId != _world.LocationId || session.CurrentLocationId != _world.LocationId || InSpace(p.PlayerId)
            || p.AboardShip || InShipInterior(p.PlayerId))
        {
            Reject(session, "companion", "@srv.companion.home_away:" + name);
            return;
        }

        SetHomeSpot(tc, p.Position);
        foreach (var c in _creatures.Where(c => c.CompanionId == tc.Id))
        {
            c.HomeSpot = p.Position;
        }

        _repo.SavePlayer(p);
        Send(session, new ServerMessage { Text = "@srv.companion.home_set:" + name });
        SendCompanions(session);
    }

    // ---------------- Movement at home ----------------

    /// <summary>A pet at home: it potters about within <see cref="CompanionHomeWanderRange"/> of its home spot and is put
    /// back there when it ends up farther than <see cref="CompanionHomeLeashRange"/> (pushed, fell, called away). It holds
    /// still while its owner is far from home.</summary>
    private void MoveHomeCompanion(CombatEntity c, CreatureSpecies sp, Vector3f ownerPos, double moveDt)
    {
        var home = c.HomeSpot;
        if (WrapDistSq(ownerPos, home) > CompanionHomeIdleRange * CompanionHomeIdleRange)
        {
            return;
        }

        if (WrapDistSq(c.Position, home) > CompanionHomeLeashRange * CompanionHomeLeashRange)
        {
            c.Position = CompanionSpotNear(sp, c.Id, home);
            return;
        }

        var profile = ProfileFor(c.SpeciesId);
        var res = LocomotionController.FollowStep(
            c.Loco, profile, c.Position, home, CompanionHomeWanderRange, moveDt, unchecked((uint)StableHashing.Seed32(Hash(c.Id, "wander"))));
        c.Loco = res.State;
        ApplyCreatureStep(c, sp, EffectiveMotion(c, sp), res.Position, res.VertWave, profile, moveDt,
            res.State.Mode == MoveMode.Seek ? MoveMode.Seek : MoveMode.Roam, res.Moving, terrainGates: false);
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: sends the Companions tab's follow choice for one companion (or all of them) as the intent would.</summary>
    public void SetCompanionFollowForTest(string playerId, string companionId, bool following, bool all = false)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleSetCompanionFollow(s, new SetCompanionFollowIntent { CompanionId = companionId, Following = following, All = all });
        }
    }

    /// <summary>Test seam: "set home here" for a companion, as the intent would.</summary>
    public void SetCompanionHomeHereForTest(string playerId, string companionId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleSetCompanionFollow(s, new SetCompanionFollowIntent { CompanionId = companionId, SetHomeHere = true });
        }
    }

    /// <summary>Test seam: one companion movement step for every companion on the active world.</summary>
    public void MoveCompanionsForTest(double moveDt)
    {
        foreach (var c in _creatures.Where(c => c.IsCompanion).ToList())
        {
            MoveCompanion(c, moveDt);
        }
    }

    /// <summary>Test seam: whether a companion is fetching a packet at a spot for its owner (#1210, follow payoff).</summary>
    public bool CompanionFetchesForTest(string playerId, Vector3f packetCenter)
        => FindSessionByPlayerId(playerId) is { } s && CompanionFetches(s, packetCenter);
}
