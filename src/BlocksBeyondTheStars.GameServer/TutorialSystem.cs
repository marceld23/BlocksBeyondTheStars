// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What <see cref="TutorialSystem"/> asks of the server beyond <see cref="IServerHost"/> (ADR 0019): the galaxy, the event
/// bus and the player's waiting steps (whether anyone listens, whether one of this player's steps waits, the step's
/// half-way line, the base checks of the building course) and whether the player stands in a station. Implemented
/// explicitly by <c>GameServer</c> in <c>GameServerTutorial.cs</c>, each member delegating to the server's own member of
/// the same name.
/// </summary>
internal interface ITutorialHost : IServerHost
{
    /// <summary>The galaxy (the server's <c>_galaxy</c>) — replaced when the server starts, so read it at use time.</summary>
    Galaxy Galaxy { get; }

    // The event bus and the steps that wait on it (GameServerGameEvents.cs, GameServerTutorialBuilding.cs, GameServerStoryPacks.cs)
    bool HasGameEventListener(string key);

    bool StepWaitsFor(PlayerSession s, string key);

    void SpeakStepMidLines(PlayerSession s, string eventKey);

    void TutorialBaseStateEvents(PlayerSession session);

    // Where the player stands (GameServerSpaceStations.cs)
    bool InStation(string playerId);
}

/// <summary>
/// The basic training's teaching core on the server (#2469, synthesis §3.3): the teaching verbs the gameplay handlers
/// raise only while a step waits for them (walk, jump, fly, walked back to the ship, a data fragment carried), the
/// client's UI teaching flags (<see cref="UiTutorialEvent"/>, tag 298), the tutorial's world predicates (plants and trees
/// in reach, a shaped block carried, knowledge for a blueprint, an unvisited world in the system) and the
/// <c>nearest_food_plant</c> marker probe.
///
/// <para>Extracted from the <c>GameServerTutorial.cs</c> partial (ADR 0019); that partial keeps one-line facades, the
/// official tutorial pack id, the "one teaching voice" hold over VEGA's hints and tips, the one-time migration of the old
/// onboarding chain (story-pack runtime) and the test seams.</para>
/// </summary>
internal sealed class TutorialSystem
{
    /// <summary>A move report further than this (blocks) from the last one is a teleport, a respawn or a streamed pose —
    /// never walking.</summary>
    private const double TutorialWalkMaxStep = 4.0;

    /// <summary>A ship pose further than this (flight units) from the last one is a jump, a warp or a fresh entry.</summary>
    private const double TutorialFlightMaxStep = 60.0;

    /// <summary>Upward speed (blocks/s) that reads as a jump.</summary>
    private const float TutorialJumpSpeed = 2.5f;

    /// <summary>The "walked back to the ship" check, in the metres the compass shows as "Ship N m" (#2550): far means the
    /// compass reads at least this …</summary>
    private const double TutorialShipFarDistance = 40.0;

    /// <summary>… and back means aboard or the compass reads at most this again.</summary>
    private const double TutorialShipNearDistance = 10.0;

    /// <summary>#2550: the compass rounds its distance with Unity's <c>Mathf.RoundToInt</c> — to the nearest whole metre, an
    /// exact .5 to the even one (39.5 reads 40, 10.5 reads 10). The server compares the exact flat distance with these edges,
    /// so the moment the compass reads "Ship 40 m" counts as far and "Ship 10 m" as back — never "the compass says 40 but
    /// the step disagrees" (both readings are even, so the .5 edge itself reads as the reading).</summary>
    private const double TutorialShipFarEdge = TutorialShipFarDistance - 0.5;

    /// <inheritdoc cref="TutorialShipFarEdge"/>
    private const double TutorialShipNearEdge = TutorialShipNearDistance + 0.5;

    /// <summary>Half-size (blocks, horizontal) of the probe behind <c>food_plants_near</c>, <c>trees_near</c> and the
    /// <c>nearest_food_plant</c> marker.</summary>
    private const int TutorialFloraProbeRadius = 20;

    /// <summary>Half-height (blocks) of that probe.</summary>
    private const int TutorialFloraProbeHeight = 10;

    /// <summary>Shortest gap between two accepted UI teaching flags of the same id from one player (server uptime).</summary>
    private const double UiTutorialEventGapSeconds = 0.25;

    /// <summary>The item a data fragment is.</summary>
    private const string DataFragmentItem = "data_fragment";

    private readonly ITutorialHost _host;
    private readonly GameContent _content;

    /// <summary>Stores the references only; built once in <c>GameServer.ConstructSystems</c> (ADR 0019).</summary>
    internal TutorialSystem(ITutorialHost host, GameContent content)
    {
        _host = host;
        _content = content;
    }

    // ---------------------------------------------------------------------------------------------
    // Teaching verbs raised only while a step waits (synthesis G13: no tick scans otherwise).
    // ---------------------------------------------------------------------------------------------

    /// <summary>From <c>HandleMove</c>: walked metres (<c>move:walked</c>), a jump or the start of a crouch
    /// (<c>move:jump</c>). Measures nothing unless a step listens.</summary>
    internal void TutorialMovementEvents(PlayerSession session, Vector3f before, Vector3f now, bool onSurface, bool crouching)
    {
        bool wasCrouching = session.Tutorial.TutorialWasCrouching;
        session.Tutorial.TutorialWasCrouching = crouching;
        if (!onSurface || session.State.InSpeeder.Length > 0 || session.State.InTrain.Length > 0)
        {
            return;
        }

        // #2482: metres climbed up a wall in the climbing pose (the "On the move" course) — the server sees the pose, the
        // client owns the motion; a report over 4 blocks from the last one never counts.
        double rise = now.Y - before.Y;
        if (session.State.Climbing && rise > 0 && rise <= TutorialWalkMaxStep && _host.HasGameEventListener(GameEventKeys.MoveClimbed))
        {
            session.Tutorial.TutorialClimbAccum += rise;
            int metres = (int)session.Tutorial.TutorialClimbAccum;
            if (metres >= 1)
            {
                session.Tutorial.TutorialClimbAccum -= metres;
                _host.RaiseGameEvent(session, GameEventKeys.MoveClimbed, string.Empty, metres);
            }
        }

        if (_host.HasGameEventListener(GameEventKeys.MoveWalked))
        {
            double dx = now.X - before.X;
            double dz = now.Z - before.Z;
            int circ = _host.World.Circumference;
            if (circ > 0)
            {
                dx -= Math.Round(dx / circ) * circ; // across the seam of a round world
            }

            double step = Math.Sqrt((dx * dx) + (dz * dz));
            if (step > 0 && step <= TutorialWalkMaxStep)
            {
                session.Tutorial.TutorialWalkAccum += step;
                int whole = (int)session.Tutorial.TutorialWalkAccum;
                if (whole >= 1)
                {
                    session.Tutorial.TutorialWalkAccum -= whole;
                    _host.RaiseGameEvent(session, GameEventKeys.MoveWalked, string.Empty, whole);
                }
            }
        }

        if (_host.HasGameEventListener(GameEventKeys.MoveJump) && _host.Uptime - session.Tutorial.TutorialJumpAt >= 1.0)
        {
            bool jumped = session.VerticalSpeed > TutorialJumpSpeed && !session.State.Climbing;
            bool crouched = crouching && !wasCrouching;
            if (jumped || crouched)
            {
                session.Tutorial.TutorialJumpAt = _host.Uptime;
                _host.RaiseGameEvent(session, GameEventKeys.MoveJump, jumped ? GameEventKeys.MoveJumpArg : GameEventKeys.MoveCrouchArg);
            }
        }
    }

    /// <summary>From <c>ShipMove</c>: flight units flown (<c>flight:flown</c>). Measures nothing unless a step listens.</summary>
    internal void TutorialFlightEvents(PlayerSession pilot, Vector3f pos)
    {
        var last = pilot.Tutorial.TutorialFlightLast;
        pilot.Tutorial.TutorialFlightLast = pos;
        if (last is not { } from || !_host.HasGameEventListener(GameEventKeys.FlightFlown))
        {
            return;
        }

        double dx = pos.X - from.X, dy = pos.Y - from.Y, dz = pos.Z - from.Z;
        double step = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (step <= 0 || step > TutorialFlightMaxStep)
        {
            return;
        }

        pilot.Tutorial.TutorialFlightAccum += step;
        int whole = (int)pilot.Tutorial.TutorialFlightAccum;
        if (whole >= 1)
        {
            pilot.Tutorial.TutorialFlightAccum -= whole;
            _host.RaiseGameEvent(pilot, GameEventKeys.FlightFlown, string.Empty, whole);
        }
    }

    /// <summary>1 Hz from <c>TickShipAi</c> (the player's world is the active cursor there): walked back to the landed
    /// ship after having been far from it (<c>ship:returned</c> <c>walk</c>), a data fragment carried (<c>data:found</c>
    /// <c>fragment</c>), and since #2480 a base's sealed room or wall ring (<c>base:sealed</c>, <c>base:walled</c>). Each
    /// check only while a step waits for it — the walk back only while one of THIS player's steps waits for it (#2550:
    /// the event bus is shared, so another player's open step must not let this one's walk count early).</summary>
    internal void TutorialStateEvents(PlayerSession session)
    {
        var p = session.State;
        if (_host.HasGameEventListener(GameEventKeys.ShipReturned))
        {
            var landed = _host.Worlds.Active.LandedFor(p.PlayerId);
            if (!_host.StepWaitsFor(session, GameEventKeys.ShipReturned))
            {
                session.Tutorial.TutorialFarFromShip = false; // #2550: a walk only counts while the player's own step waits for it
            }
            else if (landed.Placed && !_host.InSpace(p.PlayerId) && !_host.InStation(p.PlayerId))
            {
                // #2550: what the compass shows ("Ship N m", rounded — see TutorialShipFarEdge), not the heal tank in 3-D.
                double d2 = ShipCompassDistSq(p.Position, landed);
                bool onFoot = !p.AboardShip && !p.InEva && !_host.ShipInteriorContains(p.Position);
                if (onFoot && d2 >= TutorialShipFarEdge * TutorialShipFarEdge)
                {
                    if (!session.Tutorial.TutorialFarFromShip)
                    {
                        session.Tutorial.TutorialFarFromShip = true;
                        _host.SpeakStepMidLines(session, GameEventKeys.ShipReturned); // #2550: "far enough — now find your way back"
                    }
                }
                else if (session.Tutorial.TutorialFarFromShip && (p.AboardShip || d2 <= TutorialShipNearEdge * TutorialShipNearEdge))
                {
                    session.Tutorial.TutorialFarFromShip = false;
                    _host.RaiseGameEvent(session, GameEventKeys.ShipReturned, "walk");
                }
            }
        }

        if (_host.HasGameEventListener(GameEventKeys.DataFound)
            && (p.Inventory.Has(DataFragmentItem, 1)
                || (session.Ships.TryGetValue(session.ActiveShipId, out var ship) && ship.Cargo.Has(DataFragmentItem, 1))))
        {
            _host.RaiseGameEvent(session, GameEventKeys.DataFound, "fragment");
        }

        _host.TutorialBaseStateEvents(session); // #2480: a sealed room, a closed wall ring — only while this player's step waits
    }

    /// <summary>#2550: the squared distance the compass shows as "Ship N m" — flat (height does not count) and the short way
    /// round the world's seams, from <paramref name="pos"/> to the landed ship's anchor (the floor centre, at the block's
    /// middle — exactly the point <see cref="GameServer.SendShipPlacement"/> gives the client's compass). The walk-back step measures
    /// with it, so the player can read their progress off the compass.</summary>
    internal double ShipCompassDistSq(Vector3f pos, LandedShip landed)
    {
        var anchor = GameServer.AnchorOf(landed);
        return WorldConstants.WrapDistanceSquared(
            new Vector3f(pos.X, 0f, pos.Z), new Vector3f(anchor.X + 0.5f, 0f, anchor.Z + 0.5f), _host.World.Circumference);
    }

    /// <summary>#2550: a step that waits for <c>ship:returned</c> activates (also in a replay): the walk starts afresh, so a
    /// "far" noted before — under another step, or before a skip — never completes it at the ship without a walk.</summary>
    internal static void ResetTutorialWalk(PlayerSession s, GameServer.CompiledStep step)
    {
        if (step.Event.Length > 0 && GameEventKeys.Matches(step.Event, GameEventKeys.ShipReturned))
        {
            s.Tutorial.TutorialFarFromShip = false;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // UI teaching flags (tag 298).
    // ---------------------------------------------------------------------------------------------

    /// <summary>A UI moment the client reports (<see cref="UiTutorialEvent"/>): a known id becomes <c>ui:&lt;id&gt;</c> on the
    /// event bus, where only a step waiting for exactly that flag listens. Teaching only — it ticks the sender's own lesson
    /// off and nothing else (never pays, unlocks or counts for the story); an unknown id is ignored, the same id at most
    /// every <see cref="UiTutorialEventGapSeconds"/>.</summary>
    internal void HandleUiTutorialEvent(PlayerSession session, UiTutorialEvent message)
    {
        string id = message.Id ?? string.Empty;
        if (!GameEventKeys.IsKnownUiEventId(id))
        {
            return;
        }

        if (session.Tutorial.UiTutorialEventAt.TryGetValue(id, out double at) && _host.Uptime - at < UiTutorialEventGapSeconds && _host.Uptime >= at)
        {
            return;
        }

        session.Tutorial.UiTutorialEventAt[id] = _host.Uptime;
        _host.RaiseGameEvent(session, GameEventKeys.UiPrefix + id);
    }

    // ---------------------------------------------------------------------------------------------
    // World predicates of the tutorial (registered in GameServerStoryConditions).
    // ---------------------------------------------------------------------------------------------

    private HashSet<ushort>? _foodPlantBlocks;
    private HashSet<ushort>? _treeBlocks;

    /// <summary>Flora whose harvest is food (a drop with hunger value) — berries and the like.</summary>
    internal HashSet<ushort> FoodPlantBlocks => _foodPlantBlocks ??= BlocksDropping(item => _content.GetItem(item)?.ConsumeHunger > 0f, floraOnly: true);

    /// <summary>Blocks that drop wood (tree trunks) — the log and every item grouped with it (#2958: a birch drops its
    /// own pale log and is a tree all the same).</summary>
    internal HashSet<ushort> TreeBlocks => _treeBlocks ??= BlocksDropping(IsWood, floraOnly: false);

    private bool IsWood(string item)
        => item == "wood_log" || (_content.ItemGroupOf(item) is { Length: > 0 } group && group == _content.ItemGroupOf("wood_log"));

    private HashSet<ushort> BlocksDropping(Func<string, bool> drop, bool floraOnly)
    {
        var set = new HashSet<ushort>();
        foreach (var block in _content.Blocks.Values)
        {
            if ((!floraOnly || string.Equals(block.Category, "flora", StringComparison.OrdinalIgnoreCase))
                && block.Drops.Any(d => drop(d.Item)))
            {
                set.Add(block.NumericId.Value);
            }
        }

        return set;
    }

    /// <summary>The nearest block of <paramref name="blocks"/> around <paramref name="who"/> on their own body (a bounded probe
    /// of the loaded blocks), or null — also when the player is in space or their world is not loaded.</summary>
    internal Vector3i? NearestBlockOf(PlayerSession who, HashSet<ushort> blocks)
    {
        if (blocks.Count == 0 || _host.InSpace(who.State.PlayerId) || _host.Worlds.Find(who.CurrentLocationId)?.World is not { } world)
        {
            return null;
        }

        var pos = who.State.Position;
        var origin = WorldConstants.CanonicalBlock(pos.ToBlock(), world.Circumference);
        int r = TutorialFloraProbeRadius;
        int h = TutorialFloraProbeHeight;
        int bestDist = int.MaxValue;
        Vector3i? found = null;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dy = -h; dy <= h; dy++)
                {
                    int d = (dx * dx) + (dy * dy) + (dz * dz);
                    if (d >= bestDist)
                    {
                        continue;
                    }

                    var cell = WorldConstants.CanonicalBlock(new Vector3i(origin.X + dx, origin.Y + dy, origin.Z + dz), world.Circumference);
                    if (blocks.Contains(world.GetBlockIfLoaded(cell).Value))
                    {
                        bestDist = d;
                        found = cell;
                    }
                }
            }
        }

        return found;
    }

    /// <summary><c>world:shape_block_carried</c>: a shaped building material (a form from the shape tool) or a prop block
    /// (a crafted staircase, furniture) the rotate key turns.</summary>
    internal bool ShapeBlockCarried(PlayerSession who)
    {
        foreach (var stack in who.State.Inventory.Slots)
        {
            if (stack is not { IsEmpty: false })
            {
                continue;
            }

            string block = _content.GetItem(ItemKey.Base(stack.Item))?.PlacesBlock ?? string.Empty;
            if (block.Length == 0)
            {
                continue;
            }

            if ((ItemKey.Shape(stack.Item) != 0 && _content.GetBlock(block)?.Shapeable == true) || PropShapes.DefaultPlaceShape(block) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>world:research_ready</c>: the player's knowledge covers a blueprint they could research now —
    /// prerequisites unlocked, not owned, no data fragment in its cost.</summary>
    internal bool ResearchReady(PlayerSession who)
    {
        var p = who.State;
        foreach (var bp in _content.Blueprints.Values)
        {
            if (!p.UnlockedBlueprints.Contains(bp.Key)
                && bp.KnowledgeCost <= p.KnowledgePoints
                && bp.Prerequisites.All(pr => p.UnlockedBlueprints.Contains(pr))
                && bp.UnlockCost.All(c => c.Item != DataFragmentItem))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>world:new_world_in_system</c>: a planet, moon or asteroid in the player's system they never landed on.</summary>
    internal bool NewWorldInSystem(PlayerSession who)
    {
        string systemId = _host.Galaxy?.FindBody(who.CurrentLocationId)?.SystemId ?? string.Empty;
        var system = systemId.Length > 0 ? _host.Galaxy!.Systems.FirstOrDefault(sys => sys.Id == systemId) : null;
        return system is not null
               && system.Bodies.Any(b => (b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField)
                                         && !who.State.LandedBodies.Contains(b.Id));
    }
}
