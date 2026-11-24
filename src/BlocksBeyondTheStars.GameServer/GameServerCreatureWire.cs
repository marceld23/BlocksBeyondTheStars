// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The creature wire: the creature list broadcast and its <see cref="NetCreature"/> mapping.
/// </summary>
public sealed partial class GameServer
{
    private void BroadcastCreatures() => _worlds.Active.CreatureListDirty = true; // #1530: flushed once per tick

    private void SendCreatureList() => SendCreatureListBySide(); // #3905: the animals of the other side only for who is over there

    private void SendCreatures(PlayerSession session)
        => Send(session, new CreatureList { Creatures = CreaturesFor(session), ServerTime = SnapshotTime }); // #3905

    private NetCreature ToNetCreature(CombatEntity e) => WithRobotGear(WithGiantWire(ToNetCreatureCore(e), e), e); // #1998; #3398/#3399

    private NetCreature ToNetCreatureCore(CombatEntity e)
    {
        _speciesById.TryGetValue(e.SpeciesId, out var sp);
        bool asleep = sp != null && !SpeciesActive(sp, e.Position) && e.AwakeOverrideTimer <= 0 && !e.IsCompanion // roused or companion → not asleep
            && !StrandedOutOfWater(e, sp); // #3552: nor a fish ashore

        // Motion class + vertical state on the wire (#1333, additive): a walker is airborne mid-jump/fall (with
        // its velocity so the client can integrate the arc between updates), a flier is airborne unless perched,
        // a hoverer always is; swimmers never.
        var motion = MotionClassOf(e, sp);
        // A perched flier whose perch was dug away is falling under plain gravity (#1367): its phase is still
        // Perched, but the wire reports the ACTUAL vertical state — airborne with its fall velocity — so the
        // client unfolds the wings and integrates the drop instead of drawing a folded bird sliding down (#1368).
        bool perchedFalling = motion == MotionClass.Flier && e.Vert.Flight == FlightPhase.Perched && e.Vert.Airborne;
        var (airborne, perched) = CreatureAirState(e, motion);
        var vel = e.TickVel.Velocity; // #2665: the client's interpolation tangent
        var n = new NetCreature
        {
            Motion = CreatureMotion.Key(motion),
            Airborne = airborne,
            Perched = perched,
            VertVel = (CreatureMotion.IsGroundBound(motion) || perchedFalling) && e.Vert.Airborne ? e.Vert.VertVel : 0f,
            Glides = sp is not null && CreatureMotion.Glides(sp), // the client's glide factor follows the server's rule (#1368)
            VelX = vel.X,
            VelY = vel.Y,
            VelZ = vel.Z,
            Facing = CreatureFacing(e), // #2665: every creature's heading (a giant's is set again by WithGiantWire)

            Id = e.Id,
            SpeciesId = e.SpeciesId,
            AnimalSeed = e.AnimalSeed, // #3972: this animal's own seed
            Hostile = !e.IsCompanion && (e.Hostile || e.ProvokeTimer > 0), // provoked creatures read as hostile (red tint); companions never
            Asleep = asleep || SoundelingCurled(e), // #3366: a Soundeling hiding in the dark by day is curled up like a sleeper
            CarriesLight = e.Soundeling is { Item.Length: > 0 }, // #3366: the light a Soundeling carries to a dark spot
            Frozen = e.FrozenTimer > 0, // held in stasis (item 36) — client tints it icy blue
            Staggered = IsStaggered(e), // #2278: dazed by a shock-glove push — client draws circling stars
            OwnerId = e.OwnerId,        // tamed companion → client draws friendly tint + nameplate
            CustomName = e.CustomName,
            NameTag = e.IsCompanion && e.NameTag, // #3035: a hologram name
            Alerting = e.IsCompanion && _uptime < e.AlertUntil, // #1210: growling at a hostile in sight

            Hull = e.Hull,
            HullMax = e.HullMax,
            X = e.Position.X,
            Y = e.Position.Y,
            Z = e.Position.Z,
            Lurking = e.Lurking,                                              // #2009: an ambusher sitting in wait
            Begging = e.BegPhase is BegPhase.Beg or BegPhase.Rush or BegPhase.Squabble, // #2018: the pose + the fast calls
        };

        // #3971: the species traits through the one species→wire mapping; ActiveFaunaLook is cached per world (#2868) — a
        // species that carries no home look wears this one.
        CreatureWireMapping.WriteSpecies(n, sp, ActiveFaunaLook(), e.AnimalSeed);
        n.Size = n.SpeciesSize * e.SizeScale; // species size × this individual's own variance (cosmetic)
        return n;
    }
}
