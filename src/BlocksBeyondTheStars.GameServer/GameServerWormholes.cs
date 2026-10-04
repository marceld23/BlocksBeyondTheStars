// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Wormholes (#2242): rarely, at the edge of a star system, a tear in space-time opens whose twin sits in another
/// system. A pilot who flies up to it and presses [E] comes out at the twin — without a jump generator, and always
/// back the same way. The ends are placed by <see cref="WormholePlacer"/> (seed-pure, the fixed procedural systems
/// only); here they enter the flight instance as a <see cref="CombatEntityKind.Wormhole"/> entity at their system
/// position, the transit is checked in full before anyone moves, the ship scanner reads where one leads, and the star
/// map tells each player which pairs they know.
/// <para>A wormhole never leads to — or sits in — a story system: the placer only picks <c>sys*</c> systems, and every
/// transit checks <see cref="IsStoryLockedSystem"/> again, so even an edited save cannot open a way to the Guardian.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Knowledge for reading where a wormhole leads (first time per wormhole).</summary>
    private const int KnowledgeWormhole = 8;

    /// <summary>Player → uptime until which they cannot fly through a wormhole again (just came out of one).</summary>
    private readonly Dictionary<string, double> _wormholeLockUntil = new();

    /// <summary>True for a system no wormhole may lead into or sit in: the story's finale system and anything that is
    /// not a procedural <c>sys*</c> system.</summary>
    internal static bool IsStoryLockedSystem(string? systemId)
        => string.IsNullOrEmpty(systemId) || systemId == GuardianFinaleSystemId || !WormholePlacer.MayHoldWormhole(systemId!);

    /// <summary>The wormhole end of the anchor's system as a flight-instance entity, at its system position in the
    /// flight frame (the client's layout transform, like the wrecks). Never hostile, never a target.</summary>
    private void AddSpaceWormholes(SpaceInstance instance, CelestialBody? anchor)
    {
        if (anchor is null || _galaxy is null)
        {
            return;
        }

        foreach (var end in _galaxy.Wormholes)
        {
            if (end.SystemId != anchor.SystemId || IsStoryLockedSystem(end.SystemId) || instance.Entities.Any(e => e.Id == end.Id))
            {
                continue;
            }

            instance.Entities.Add(new CombatEntity
            {
                Id = end.Id,
                Kind = CombatEntityKind.Wormhole,
                Name = string.Empty, // the client names it (and "???" or the twin's system once known)
                Hostile = false,
                Hull = 1f,
                HullMax = 1f,
                Position = WormholeFlightPosition(end, anchor),
            });
        }
    }

    /// <summary>A wormhole end in the flight frame of an instance anchored on <paramref name="anchor"/>.</summary>
    private static Vector3f WormholeFlightPosition(Wormhole end, CelestialBody anchor)
        => new(
            (end.SystemX - anchor.SystemX) * SystemBodyLayout.FlightViewScale,
            end.SystemY * SystemBodyLayout.FlightViewScale,
            (end.SystemZ - anchor.SystemZ) * SystemBodyLayout.FlightViewScale);

    private void HandleWormholeTransit(PlayerSession session, WormholeTransitIntent intent)
        => TraverseWormhole(session.State.PlayerId, intent.WormholeId);

    /// <summary>Flies the pilot through a wormhole (#2242). Every gate first — in flight, aboard and not on an EVA, the
    /// rift in this instance and in range, a twin outside the story's systems, no arrival lock — and only then is the
    /// pilot moved: out of this flight, into the twin system's, a little in front of the twin rift.</summary>
    public void TraverseWormhole(string playerId, string wormholeId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        if (!_playerInstance.TryGetValue(playerId, out var instanceId) || !_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            RejectSpace(session, "@srv.wormhole.not_in_flight");
            return;
        }

        if (session.State.InEva)
        {
            RejectSpace(session, "@srv.wormhole.eva");
            return;
        }

        var entity = instance.Entities.FirstOrDefault(e => e.Id == wormholeId && e.Kind == CombatEntityKind.Wormhole);
        var end = _galaxy?.FindWormhole(wormholeId);
        var twin = end is null ? null : _galaxy!.FindWormhole(end.LinkedId);
        if (entity is null || end is null || twin is null)
        {
            RejectSpace(session, "@srv.wormhole.unknown");
            return;
        }

        float reach = _content.Wormholes.TransitRange * 1.6f; // the client offers the prompt at TransitRange; allow lag
        if (entity.Position.DistanceSquared(PilotPositionIn(instance, playerId)) > reach * reach)
        {
            RejectSpace(session, "@srv.wormhole.too_far");
            return;
        }

        // The rule of the feature: never into (or out of) a story system — checked again here, whatever the galaxy says.
        if (IsStoryLockedSystem(twin.SystemId) || IsStoryLockedSystem(end.SystemId))
        {
            RejectSpace(session, "@srv.wormhole.unknown");
            return;
        }

        if (_wormholeLockUntil.TryGetValue(playerId, out double until) && _uptime < until)
        {
            return; // just came out of one — the rift is still settling; no message, the client knows
        }

        var system = _galaxy!.Systems.FirstOrDefault(s => s.Id == twin.SystemId);
        var anchor = system?.Bodies.FirstOrDefault(b => !string.IsNullOrEmpty(b.PlanetType)) ?? system?.Bodies.FirstOrDefault();
        if (system is null || anchor is null)
        {
            RejectSpace(session, "@srv.wormhole.unknown");
            return;
        }

        Serve(session);
        if (ShipLaunchProblem() is { } problem)
        {
            RejectSpace(session, problem); // a ship that cannot fly cannot fly through either
            return;
        }

        // Everything checked — now move. The others out here see the ship vanish into the rift.
        var departure = PilotPositionIn(instance, playerId);
        foreach (string other in instance.Players)
        {
            if (other != playerId && FindSessionByPlayerId(other) is { } watcher)
            {
                Send(watcher, new SpaceWarpFx { X = departure.X, Y = departure.Y, Z = departure.Z, Arriving = false, Style = "wormhole" });
            }
        }

        LeaveSpace(playerId);
        session.CurrentLocationId = anchor.Id;
        session.AssignedPadIndex = -1; // no pad claim carries across systems (#1679), like a hyperjump
        SetCurrent(session);
        _ship.CurrentLocationId = anchor.Id;
        session.State.AboardShip = true;
        session.State.InEva = false;

        // Both ends are known now: the pair shows on this player's star chart from here on.
        var p = session.State;
        bool first = p.Scanned.Add(WormholeScanKey(end.Id));
        p.Scanned.Add(WormholeScanKey(twin.Id));
        MarkSystemKnown(session, system.Id);
        Advance(session, BlocksBeyondTheStars.Shared.Definitions.AchievementCounters.Wormhole);
        _wormholeLockUntil[playerId] = _uptime + _content.Wormholes.ArrivalLockSeconds;

        // Out a little in front of the twin, nose pointing away from it — into its system.
        var twinPos = WormholeFlightPosition(twin, anchor);
        float dx = -twinPos.X, dz = -twinPos.Z;
        float len = (float)System.Math.Sqrt(dx * dx + dz * dz);
        if (len < 0.001f)
        {
            dx = 0f;
            dz = 1f;
            len = 1f;
        }

        dx /= len;
        dz /= len;
        float offset = _content.Wormholes.ArrivalOffset;
        var exit = new Vector3f(twinPos.X + dx * offset, twinPos.Y, twinPos.Z + dz * offset);
        float yaw = (float)(System.Math.Atan2(dx, dz) * 180.0 / System.Math.PI);

        EnterSpace(playerId, skipLaunch: true, resume: new SpacePlayerPose(exit, yaw, false), wormhole: true);
        SendStarMap(session);

        if (_playerInstance.TryGetValue(playerId, out var newInstanceId) && _spaceInstances.TryGetValue(newInstanceId, out var arrived))
        {
            foreach (string other in arrived.Players)
            {
                if (other != playerId && FindSessionByPlayerId(other) is { } watcher)
                {
                    Send(watcher, new SpaceWarpFx { X = exit.X, Y = exit.Y, Z = exit.Z, Arriving = true, Style = "wormhole" });
                }
            }
        }

        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.wormhole.traversed").Replace("{system}", system.Name),
        });
        if (first)
        {
            SendInventory(session);
        }

        _log.Info($"Player '{p.Name}' flew through wormhole '{end.Id}' into system '{system.Name}'.");
    }

    /// <summary>The ship scanner reads a wormhole (#2242): where it leads. The pair is known to the player from then
    /// on — the star chart shows it — and the first reading of each wormhole pays knowledge.</summary>
    private ScanResult ScanWormhole(PlayerSession session, CombatEntity target)
    {
        var end = _galaxy?.FindWormhole(target.Id);
        var twin = end is null ? null : _galaxy!.FindWormhole(end.LinkedId);
        var twinSystem = twin is null ? null : _galaxy!.Systems.FirstOrDefault(s => s.Id == twin.SystemId);
        var readout = new ScanReadout
        {
            Kind = "wormhole",
            SubjectKey = "wormhole",
            Display = twinSystem?.Name ?? string.Empty,
            InfoKey = twinSystem is null ? "ui.scan.wormhole_unstable" : "ui.scan.wormhole",
            TraitKeys = new[] { "ui.scan.trait.two_way" },
            LegacyInfo = "A tear in space-time — it leads to another star system and back.",
        };
        var result = Award(session, WormholeScanKey(target.Id), readout, KnowledgeWormhole);
        if (twin is not null)
        {
            session.State.Scanned.Add(WormholeScanKey(twin.Id));
            SendStarMap(session); // the line on the star chart
        }

        if (_playerInstance.TryGetValue(session.State.PlayerId, out var iid) && _spaceInstances.TryGetValue(iid, out var instance))
        {
            SendSpaceState(session, instance); // the rift's label now names the twin system (ScannedIds)
        }

        return result;
    }

    /// <summary>The wormhole ends a player sees on the star map (#2242): every end in a system they know or are in,
    /// with the twin's system only when they know that wormhole.</summary>
    private NetWormhole[] WormholesFor(PlayerSession session, string? currentSystemId)
    {
        if (_galaxy is null || _galaxy.Wormholes.Count == 0)
        {
            return System.Array.Empty<NetWormhole>();
        }

        var known = session.State.KnownSystems;
        var list = new List<NetWormhole>();
        foreach (var end in _galaxy.Wormholes)
        {
            if (IsStoryLockedSystem(end.SystemId) || (end.SystemId != currentSystemId && !known.Contains(end.SystemId)))
            {
                continue;
            }

            var twin = _galaxy.FindWormhole(end.LinkedId);
            bool knows = session.State.Scanned.Contains(WormholeScanKey(end.Id));
            list.Add(new NetWormhole
            {
                Id = end.Id,
                SystemId = end.SystemId,
                SystemX = end.SystemX,
                SystemY = end.SystemY,
                SystemZ = end.SystemZ,
                LinkedSystemId = knows && twin is not null ? twin.SystemId : string.Empty,
            });
        }

        return list.ToArray();
    }

    /// <summary>Test seam: lifts the arrival lock, so a test can fly straight back through.</summary>
    internal void ResetWormholeLockForTest(string playerId) => _wormholeLockUntil.Remove(playerId);
}
