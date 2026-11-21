// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Held-beam weapons (#3898, the Guardian laser). A tap of the fire control is the weapon's ordinary shot and never
/// comes through here; while the control stays HELD the client sends one <see cref="BeamWeaponIntent"/> per beam tick
/// (the pattern of the drill's hold loop) and this file decides what each one does:
/// <list type="bullet">
/// <item><b>Cadence.</b> At most one tick per the weapon's <c>beamTick</c>, whatever the client sends — a schedule per
/// player that moves on by whole ticks and survives a stop and a restart, so neither a flood of ticks nor a
/// release/press flutter resolves hits faster.</item>
/// <item><b>A tick is a shot.</b> The tick runs down the very path a shot takes (<see cref="AttackEntity"/>: the
/// untouchable and companion refusals, the PvP rules, reach, line of fire, the aim, <c>LandHit</c> with its loot, story
/// credit and hit reactions) with the tick's own numbers — <see cref="BeamWeaponRules.TickProfile"/> stands in for the
/// held tool for exactly that call. No second damage path, no new damage type.</item>
/// <item><b>Energy.</b> Every resolved tick drains <c>beamEnergyPerSecond × tick</c>, whether it met anything or not.
/// The beam stops before the suit goes below <see cref="BeamWeaponRules.ReserveFraction"/> of its maximum and says so
/// (<c>srv.attack.beam_reserve</c>, paced) — nobody is stranded without a jetpack.</item>
/// <item><b>It ends</b> when the control is released, at the reserve, when the item leaves the hand, when the player
/// sits down, boards, dies, changes world or crosses to the other side, and when no tick arrived for
/// <see cref="BeamWeaponRules.TimeoutSeconds"/>.</item>
/// <item><b>Terrain.</b> A tick that ends on a block sets it alight like the weapon's shot does
/// (<see cref="ToolProperties.Ignites"/>, the fire's own guards), at most once per tick — a shot costs less energy than
/// a tick, so the beam never lights more than shots could.</item>
/// <item><b>The others see it.</b> Each resolved tick goes to the players near as an <see cref="ActionFx"/> of kind
/// <see cref="FxActionKinds.WeaponBeam"/> along the line the server validated, and the end as
/// <see cref="FxActionKinds.WeaponBeamEnd"/> (to the shooter too, whose client then lets go of its own beam).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds before the same refusal ("out of reach", "no clear line" …) is told again while a beam is held —
    /// a shot's refusal is one toast per press, a beam would say it four times a second.</summary>
    internal const double BeamRefusalLineSeconds = 3.0;

    /// <summary>One held beam.</summary>
    private sealed class HeldBeam
    {
        public HeldBeam(PlayerSession session, string itemKey, ToolProperties tool)
        {
            Session = session;
            ItemKey = itemKey;
            Tool = tool;
            Profile = BeamWeaponRules.TickProfile(tool);
            LocationId = session.CurrentLocationId;
            Aboard = session.State.AboardShip;
        }

        public PlayerSession Session { get; }

        /// <summary>The held item's key when the beam started (a changed tool's key carries its changes).</summary>
        public string ItemKey { get; }

        /// <summary>The held weapon's own values.</summary>
        public ToolProperties Tool { get; }

        /// <summary>One tick as a weapon the shot's path resolves.</summary>
        public ToolProperties Profile { get; }

        public string LocationId { get; }

        public bool Aboard { get; }

        public double LastHeardAt { get; set; }

        /// <summary>Where the beam last started and ended (for the end's effect); <see cref="Drawn"/> once a tick resolved.</summary>
        public Vector3f From { get; set; }

        public Vector3f To { get; set; }

        public bool Drawn { get; set; }

        public string ToldReason { get; set; } = string.Empty;

        public double ToldAt { get; set; } = double.NegativeInfinity;
    }

    private readonly Dictionary<string, HeldBeam> _heldBeams = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _beamNextTickAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _beamReserveLineAt = new(StringComparer.Ordinal);
    private readonly HashSet<string> _beamRefusalTold = new(StringComparer.Ordinal);
    private readonly List<string> _beamScratch = new();

    /// <summary>The beam tick being resolved right now — set only for the one <see cref="AttackEntity"/> call of
    /// <see cref="ResolveBeamTick"/>; null at every other time.</summary>
    private HeldBeam? _beamTick;

    /// <summary>During a beam tick the held weapon's values are the tick's (<see cref="ActiveTool"/> asks).</summary>
    private ToolProperties? BeamTickTool(Shared.State.PlayerState player)
        => _beamTick is { } beam && ReferenceEquals(beam.Session.State, player) ? beam.Profile : null;

    /// <summary>A refusal raised for the shooter while a beam tick is resolved (<see cref="Reject"/> asks): the same
    /// reason is passed on to the player only every <see cref="BeamRefusalLineSeconds"/>. True = swallow it.</summary>
    private bool BeamTickRefused(PlayerSession session, string reason)
    {
        if (_beamTick is not { } beam || !ReferenceEquals(beam.Session, session))
        {
            return false;
        }

        if (beam.ToldReason == reason && _uptime < beam.ToldAt + BeamRefusalLineSeconds)
        {
            return true;
        }

        beam.ToldReason = reason;
        beam.ToldAt = _uptime;
        return false;
    }

    private void HandleBeamWeapon(PlayerSession session, BeamWeaponIntent intent)
    {
        string id = session.State.PlayerId;
        if (!intent.Active)
        {
            _beamRefusalTold.Remove(id);
            StopHeldBeam(id, tellShooter: false); // released: the shooter's client already let go
            return;
        }

        var dir = new Vector3f(intent.DirX, intent.DirY, intent.DirZ);
        if (!float.IsFinite(dir.X) || !float.IsFinite(dir.Y) || !float.IsFinite(dir.Z)
            || (dir.X * dir.X) + (dir.Y * dir.Y) + (dir.Z * dir.Z) < 0.0001f)
        {
            return; // a beam needs an aim; garbage no client sends
        }

        var p = session.State;
        var tool = ActiveTool(p);
        if (!BeamWeaponRules.IsBeamWeapon(tool) || HeldBeamBlocked(session))
        {
            RefuseHeldBeam(session);
            return;
        }

        if (!BeamWeaponRules.CanPayTick(tool, p.SuitEnergy, MaxSuitEnergy(p)))
        {
            TellBeamReserve(session);
            RefuseHeldBeam(session);
            return;
        }

        string held = HeldItemKey(p);
        if (!_heldBeams.TryGetValue(id, out var beam) || beam.ItemKey != held || beam.LocationId != session.CurrentLocationId)
        {
            StopHeldBeam(id, tellShooter: false); // another beam weapon took the hand
            beam = new HeldBeam(session, held, tool);
            _heldBeams[id] = beam;
            _beamRefusalTold.Remove(id);
            StartleNpcsNear(session, p.Position); // #3769: the people around jump once, as at a shot
        }

        beam.LastHeardAt = _uptime;
        double next = _beamNextTickAt.TryGetValue(id, out var at) ? at : double.NegativeInfinity;
        if (_uptime < next - BeamWeaponRules.JitterToleranceSeconds)
        {
            return; // too soon — the beam stays on, this tick resolves nothing
        }

        _beamNextTickAt[id] = Math.Max(next, _uptime) + BeamWeaponRules.Tick(tool);
        ResolveBeamTick(session, beam, intent, dir);
    }

    /// <summary>The server will not fire this player's beam (the reserve, a seat, another item …): a beam that is on ends,
    /// and the shooter's client is told once to let go of its own — once per hold, however many ticks it still sends
    /// (the next release or the next beam that does start clears it).</summary>
    private void RefuseHeldBeam(PlayerSession session)
    {
        string id = session.State.PlayerId;
        if (_heldBeams.ContainsKey(id))
        {
            StopHeldBeam(id, tellShooter: true);
            return;
        }

        if (_beamRefusalTold.Add(id))
        {
            var at = session.State.Position;
            Send(session, NewActionFx(session, FxActionKinds.WeaponBeamEnd, HeldItemKey(session.State), at, at, hit: false, outcome: false));
        }
    }

    /// <summary>What ends a beam whatever the client sends: not on foot in the world, down, or over there.</summary>
    private bool HeldBeamBlocked(PlayerSession session)
    {
        var p = session.State;
        return !session.Joined || session.Watching || session.OtherSide || p.Health <= 0f || session.Respawn.ChoicePending
            || p.Seated || p.InSpeeder.Length > 0 || p.InTrain.Length > 0 || p.InEva || InSpace(p.PlayerId);
    }

    /// <summary>One resolved tick: the hit on the claimed entity down the shot's own path, else the tick's energy for a
    /// beam that met nothing alive, the terrain it ended on, and the line the others see.</summary>
    private void ResolveBeamTick(PlayerSession session, HeldBeam beam, BeamWeaponIntent intent, Vector3f dir)
    {
        var p = session.State;
        float range = BeamWeaponRules.Range(beam.Tool);
        float len = (float)Math.Sqrt((dir.X * dir.X) + (dir.Y * dir.Y) + (dir.Z * dir.Z));
        var aim = new Vector3f(dir.X / len, dir.Y / len, dir.Z / len);
        var eye = new Vector3f(p.Position.X, p.Position.Y + SightEyeHeight, p.Position.Z);
        float reach = range; // how far along the aim the beam runs
        bool landed = false;

        string target = intent.EntityId ?? string.Empty;
        if (target.Length is > 0 and <= 96 && BeamTargetPoint(target, p.Position) is { } body)
        {
            float before = p.SuitEnergy;
            _beamTick = beam;
            try
            {
                AttackEntity(p.PlayerId, target, dir.X, dir.Y, dir.Z); // the shot's path, with the tick's numbers
            }
            finally
            {
                _beamTick = null;
            }

            // The shot's path takes the energy as its last check, right before the hit: paid means landed. (Whatever the
            // hit sets off afterwards — loot, a respawn — cannot make it count as a miss and be charged twice.)
            landed = p.SuitEnergy < before;
            if (landed)
            {
                reach = Math.Clamp(Along(eye, aim, body), 0.5f, range);
            }
        }

        if (!landed)
        {
            p.SuitEnergy = Math.Max(0f, p.SuitEnergy - beam.Profile.EnergyPerUse); // the beam burns whether it meets anything or not
            MarkPlayerStateDirty(session);
        }

        bool terrain = !landed && intent.HasCell && BeamOnTerrain(session, beam, new Vector3i(intent.X, intent.Y, intent.Z), eye, aim, range, ref reach);
        beam.From = eye;
        beam.To = new Vector3f(eye.X + (aim.X * reach), eye.Y + (aim.Y * reach), eye.Z + (aim.Z * reach));
        beam.Drawn = true;
        var fx = NewActionFx(session, FxActionKinds.WeaponBeam, beam.ItemKey, beam.From, beam.To, landed || terrain, outcome: false);
        SendFxNearby(fx, beam.From, session, includeActor: false, DeliveryMode.Unreliable);
    }

    /// <summary>The beam ended on a block: the cell must lie inside the beam's reach (the check a terrain shot gets), and
    /// an igniting weapon sets it alight under the fire's own guards — never as a guest in a friend's ship (#2723).</summary>
    private bool BeamOnTerrain(PlayerSession session, HeldBeam beam, Vector3i cell, Vector3f eye, Vector3f aim, float range, ref float reach)
    {
        var pos = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        if (!WithinBuildHeight(pos.Y) || WrapDistSq(session.State.Position, pos) > (range + 1.5f) * (range + 1.5f))
        {
            return false;
        }

        var centre = Unwrapped(eye, new Vector3f(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f));
        reach = Math.Clamp(Along(eye, aim, centre) - 0.5f, 0.5f, range);
        if (beam.Tool.Ignites && _fireId != 0 && !_shipVisits.ContainsKey(session.State.PlayerId)
            && IsFlammable(_world.GetBlock(pos).Value))
        {
            Ignite(pos);
        }

        return true;
    }

    /// <summary>Where the claimed entity's body is (seen from <paramref name="from"/>, the short way round the seam), or
    /// null when there is no such machine, creature, robber or player in this world.</summary>
    private Vector3f? BeamTargetPoint(string entityId, Vector3f from)
    {
        Vector3f? feet = null;
        if (PvpRules.TryPlayerId(entityId, out var victim))
        {
            feet = FindSessionByPlayerId(victim) is { Joined: true } other ? other.State.Position : null;
        }
        else if ((_planetEnemies.FirstOrDefault(e => e.Id == entityId) ?? _creatures.FirstOrDefault(e => e.Id == entityId)
                     ?? _bandits.FirstOrDefault(e => e.Id == entityId)) is { } entity)
        {
            feet = entity.Position;
        }

        if (feet is not { } at)
        {
            return null;
        }

        var near = Unwrapped(from, at);
        return new Vector3f(near.X, near.Y + 0.9f, near.Z);
    }

    /// <summary>How far along <paramref name="aim"/> (unit length) the point lies from <paramref name="eye"/>.</summary>
    private static float Along(Vector3f eye, Vector3f aim, Vector3f point)
        => ((point.X - eye.X) * aim.X) + ((point.Y - eye.Y) * aim.Y) + ((point.Z - eye.Z) * aim.Z);

    /// <summary>"The beam stops so you are never stranded" — at most every <see cref="BeamWeaponRules.ReserveLineSeconds"/>.</summary>
    private void TellBeamReserve(PlayerSession session)
    {
        string id = session.State.PlayerId;
        if (_beamReserveLineAt.TryGetValue(id, out var at) && _uptime < at + BeamWeaponRules.ReserveLineSeconds)
        {
            return;
        }

        _beamReserveLineAt[id] = _uptime;
        Send(session, new ServerMessage { Text = "@srv.attack.beam_reserve" });
    }

    /// <summary>Ends the player's beam, if one is on: the others near hear and see it end, and — with
    /// <paramref name="tellShooter"/> — so does the shooter's own client, which then lets go of its beam.</summary>
    private void StopHeldBeam(string playerId, bool tellShooter)
    {
        if (!_heldBeams.Remove(playerId, out var beam))
        {
            return;
        }

        var session = beam.Session;
        if (!session.Joined || session.CurrentLocationId != (_worlds.Active?.LocationId ?? string.Empty))
        {
            return; // gone, or in another world by now: nobody there saw this beam
        }

        if (tellShooter)
        {
            _beamRefusalTold.Add(playerId); // the shooter hears this end; a tick still on its way says nothing more
        }

        var from = beam.Drawn ? beam.From : session.State.Position;
        var to = beam.Drawn ? beam.To : from;
        var fx = NewActionFx(session, FxActionKinds.WeaponBeamEnd, beam.ItemKey, from, to, hit: false, outcome: false);
        SendFxNearby(fx, from, session, includeActor: tellShooter, DeliveryMode.ReliableOrdered);
    }

    /// <summary>Every tick, per occupied world: a beam whose shooter is in this world ends when no tick arrived for
    /// <see cref="BeamWeaponRules.TimeoutSeconds"/>, the item left the hand, or the player sat down, boarded, went down,
    /// changed world or crossed over. Beams of players who left are dropped without a word.</summary>
    private void TickBeamWeapons()
    {
        if (_heldBeams.Count == 0)
        {
            return;
        }

        string here = _worlds.Active?.LocationId ?? string.Empty;
        _beamScratch.Clear();
        foreach (var (id, beam) in _heldBeams)
        {
            var s = beam.Session;
            if (!s.Joined || !ReferenceEquals(FindSessionByPlayerId(id), s))
            {
                _beamScratch.Add(id); // left: StopHeldBeam sends nothing for a session that is gone
                _beamNextTickAt.Remove(id);
                _beamReserveLineAt.Remove(id);
                _beamRefusalTold.Remove(id);
                continue;
            }

            if (s.CurrentLocationId != here)
            {
                continue; // that world's own pass looks at it
            }

            if (_uptime - beam.LastHeardAt > BeamWeaponRules.TimeoutSeconds || beam.LocationId != here
                || beam.Aboard != s.State.AboardShip || HeldBeamBlocked(s) || HeldItemKey(s.State) != beam.ItemKey)
            {
                _beamScratch.Add(id);
            }
        }

        foreach (var id in _beamScratch)
        {
            StopHeldBeam(id, tellShooter: true);
        }
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: a beam intent of this player, through the dispatch gates (the other side, the pause …).</summary>
    public void BeamWeaponForTest(string playerId, BeamWeaponIntent intent)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            Serve(s);
            Dispatch(s, intent);
        }
    }

    /// <summary>Test seam: whether this player's beam is on.</summary>
    public bool HeldBeamActiveForTest(string playerId) => _heldBeams.ContainsKey(playerId);

    /// <summary>Test seam: the per-world beam pass, with this player's world under the cursor.</summary>
    public void TickBeamWeaponsForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            Serve(s);
        }

        TickBeamWeapons();
    }
}
