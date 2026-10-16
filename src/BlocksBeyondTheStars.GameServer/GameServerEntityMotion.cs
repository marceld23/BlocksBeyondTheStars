// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// An entity's real velocity over the last server tick (#2665), measured from where it stood at the previous end-of-tick
/// sample — whatever moved it (the locomotion controller, a jump, a giant's own rules, a knock-back). The client uses it
/// as the tangent of its interpolation curve. A move no planet entity can run (a teleport, an eviction, a relocation)
/// reads as standing still, so the client's teleport reset is not fed a huge tangent. Runtime only, never persisted.
/// </summary>
public struct TickVelocity
{
    /// <summary>Blocks/s beyond which a per-tick move is a teleport, not motion.</summary>
    public const float MaxPlausibleSpeed = 40f;

    private Vector3f _lastPos;
    private double _lastAt;
    private bool _has;

    /// <summary>The measured velocity, blocks/s.</summary>
    public Vector3f Velocity { get; private set; }

    /// <summary>Takes this tick's sample. A second sample at the same time (two calls in one tick) keeps the first result.</summary>
    public void Observe(Vector3f pos, double now, int circumference)
    {
        if (_has && now <= _lastAt)
        {
            return;
        }

        if (_has)
        {
            double dt = now - _lastAt;
            float dx = (float)WorldConstants.WrapDeltaX((double)pos.X - _lastPos.X, circumference);
            float dy = pos.Y - _lastPos.Y;
            float dz = (float)WorldConstants.WrapDeltaZ((double)pos.Z - _lastPos.Z, circumference);
            double dist = System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            Velocity = dist > (MaxPlausibleSpeed * dt) + 0.5
                ? Vector3f.Zero
                : new Vector3f((float)(dx / dt), (float)(dy / dt), (float)(dz / dt));
        }
        else
        {
            Velocity = Vector3f.Zero;
        }

        _lastPos = pos;
        _lastAt = now;
        _has = true;
    }
}

/// <summary>
/// #2665/#2666/#2670: smooth planet-entity motion on the wire. Every end of a world tick samples the real velocity of each
/// animal, machine, bandit and NPC (<see cref="TickVelocity"/>); the entity lists carry it plus the server clock of the tick,
/// and every second tick each player gets a compact <see cref="CreatureMotionFrame"/> of the animals near them — the 2 Hz
/// <see cref="CreatureList"/> alone aliased every quick move into straight segments.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far around a player the 7.5 Hz creature motion stream reaches (blocks). Farther animals ride the 2 Hz list.</summary>
    internal const float CreatureMotionRange = 48f;

    /// <summary>The motion stream's beat (seconds): every second tick at the default 15 Hz.</summary>
    internal const double CreatureMotionInterval = 0.125;

    /// <summary>The length of the tick being simulated (set at the start of <c>Tick</c>).</summary>
    private double _tickSeconds;

    /// <summary>The server time the entity positions of this tick belong to: its END. <c>_uptime</c> only advances after the
    /// world loop, so stamping and velocity-sampling with it would divide this tick's movement by the previous tick's length
    /// (wrong by up to the dt cap after a hitch).</summary>
    private double SnapshotTime => _uptime + _tickSeconds;

    private readonly List<string> _motionIdsScratch = new();
    private readonly List<int> _motionDataScratch = new();

    /// <summary>Test seam: the number of creature motion frames sent so far.</summary>
    internal int CreatureMotionFramesSent { get; private set; }

    /// <summary>The end-of-tick velocity sample of everything that moves on this world's surface.</summary>
    private void SampleEntityVelocities()
    {
        int circ = _world.Circumference;
        foreach (var c in _creatures)
        {
            c.TickVel.Observe(c.Position, SnapshotTime, circ);
        }

        foreach (var e in _planetEnemies)
        {
            e.TickVel.Observe(e.Position, SnapshotTime, circ);
        }

        foreach (var b in _bandits)
        {
            b.TickVel.Observe(b.Position, SnapshotTime, circ);
        }

        foreach (var n in _npcs)
        {
            n.TickVel.Observe(n.Pos, SnapshotTime, circ);
        }
    }

    /// <summary>Sends each player the motion of the animals within <see cref="CreatureMotionRange"/>, at most every
    /// <see cref="CreatureMotionInterval"/>. Players in flight get none (they see no fauna).</summary>
    private void SendCreatureMotionFrames()
    {
        var w = _worlds.Active;
        if (_creatures.Count == 0 || SnapshotTime - w.CreatureMotionSentAt < CreatureMotionInterval)
        {
            return;
        }

        w.CreatureMotionSentAt = SnapshotTime;
        double rangeSq = (double)CreatureMotionRange * CreatureMotionRange;
        foreach (var session in JoinedInActiveWorld())
        {
            if (InSpace(session.State.PlayerId))
            {
                continue;
            }

            _motionIdsScratch.Clear();
            _motionDataScratch.Clear();
            foreach (var c in _creatures)
            {
                if (WrapDistSq(session.State.Position, c.Position) > rangeSq)
                {
                    continue;
                }

                _speciesById.TryGetValue(c.SpeciesId, out var sp);
                var (airborne, perched) = CreatureAirState(c, MotionClassOf(c, sp));
                var v = c.TickVel.Velocity;
                _motionIdsScratch.Add(c.Id);
                _motionDataScratch.Add(CreatureMotionFrame.ToFixed(c.Position.X, CreatureMotionFrame.PositionScale));
                _motionDataScratch.Add(CreatureMotionFrame.ToFixed(c.Position.Y, CreatureMotionFrame.PositionScale));
                _motionDataScratch.Add(CreatureMotionFrame.ToFixed(c.Position.Z, CreatureMotionFrame.PositionScale));
                _motionDataScratch.Add(CreatureMotionFrame.ToFixed(v.X, CreatureMotionFrame.VelocityScale));
                _motionDataScratch.Add(CreatureMotionFrame.ToFixed(v.Y, CreatureMotionFrame.VelocityScale));
                _motionDataScratch.Add(CreatureMotionFrame.ToFixed(v.Z, CreatureMotionFrame.VelocityScale));
                _motionDataScratch.Add(CreatureMotionFrame.ToFixed(CreatureFacing(c), CreatureMotionFrame.FacingScale));
                _motionDataScratch.Add((airborne ? CreatureMotionFrame.FlagAirborne : 0) | (perched ? CreatureMotionFrame.FlagPerched : 0));
            }

            if (_motionIdsScratch.Count == 0)
            {
                continue;
            }

            Send(session, new CreatureMotionFrame
            {
                ServerTime = SnapshotTime,
                Ids = _motionIdsScratch.ToArray(),
                Data = _motionDataScratch.ToArray(),
            });
            CreatureMotionFramesSent++;
        }
    }

    /// <summary>The heading a creature is drawn with when it stands (radians, dirX = cos, dirZ = sin): a giant's own, else
    /// the locomotion heading.</summary>
    private static float CreatureFacing(CombatEntity e) => e.Giant is { } g ? g.Facing : e.Loco.Heading;

    /// <summary>The motion class in effect for a creature right now (an amphibian's depends on the water it is in).</summary>
    private static MotionClass MotionClassOf(CombatEntity e, CreatureSpecies? sp)
        => sp is null ? MotionClass.Walker : CreatureMotion.EffectiveClass(sp, e.Vert.InWater);

    /// <summary>The vertical state the wire reports (#1333/#1368): a walker is airborne mid-jump/fall, a flier unless it sits
    /// on its perch (a perched flier whose perch was dug away is falling, so airborne), a hoverer always, a swimmer never.</summary>
    private static (bool Airborne, bool Perched) CreatureAirState(CombatEntity e, MotionClass motion)
    {
        bool perchedFalling = motion == MotionClass.Flier && e.Vert.Flight == FlightPhase.Perched && e.Vert.Airborne;
        bool airborne = motion switch
        {
            MotionClass.Flier => e.Vert.Flight != FlightPhase.Perched || perchedFalling,
            MotionClass.Hoverer => true,
            MotionClass.Swimmer => false,
            _ => e.Vert.Airborne,
        };
        bool perched = motion == MotionClass.Flier && e.Vert.Flight == FlightPhase.Perched && !perchedFalling;
        return (airborne, perched);
    }
}
