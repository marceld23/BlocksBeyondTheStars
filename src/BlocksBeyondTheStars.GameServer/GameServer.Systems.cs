// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The extracted subsystems (ADR 0019): one field and one construction line each, in alphabetical slots, plus the one
/// shared <see cref="IServerHost"/> implementation. Every subsystem extraction fills its own pre-seeded slot line in
/// both blocks below and touches nothing else here, so parallel extractions meet in this one predictable file.
/// </summary>
public sealed partial class GameServer : IServerHost
{
    // One field per extracted subsystem (alphabetical slots; a package replaces its own slot line only). Not readonly:
    // they are assigned in ConstructSystems, not in the constructor body.
    private AiLinesSystem _aiLinesSystem = null!;
    private BioLabSystem _bioLabSystem = null!;
    private ContainerSystem _containerSystem = null!;
    private CrewSystem _crewSystem = null!;
    private EquipmentSystem _equipmentSystem = null!;
    private FarTerrainSystem _farTerrainSystem = null!;
    // HullRepair: a one-shot pass, built where Start runs it (GameServerHullRepair.cs) — no field.
    private JournalSystem _journalSystem = null!;
    private MarkerSystem _markerSystem = null!;
    private NpcWorkSystem _npcWorkSystem = null!;
    private ObserverSystem _observerSystem = null!;
    // ShipExtension: ShipExtensionGeometry is a static class (the glue stays in GameServerShipExtension.cs) — no field.
    private TemperatureSystem _temperatureSystem = null!;
    private TutorialSystem _tutorialSystem = null!;
    private VehicleSystem _vehicleSystem = null!;

    /// <summary>Builds every extracted subsystem; called once from the constructor, just before the tick schedules
    /// (GameServerTickSchedule.cs) are built. A subsystem's constructor only stores references — <c>_worlds</c>,
    /// <c>_meta</c>, the world and the galaxy are null or change until <see cref="Start"/>, so a subsystem reads them
    /// through its host at use time.</summary>
    private void ConstructSystems()
    {
        _aiLinesSystem = new AiLinesSystem(this, _config);
        _bioLabSystem = new BioLabSystem(this, _content);
        _containerSystem = new ContainerSystem(this, _content, _repo);
        _crewSystem = new CrewSystem(this, _repo, _log);
        _equipmentSystem = new EquipmentSystem(this, _content, _log);
        _farTerrainSystem = new FarTerrainSystem(this, _repo);
        // HullRepair: a one-shot pass, built where Start runs it (GameServerHullRepair.cs).
        _journalSystem = new JournalSystem(this, _content);
        _markerSystem = new MarkerSystem(this, _repo, _log);
        _npcWorkSystem = new NpcWorkSystem(this);
        _observerSystem = new ObserverSystem(this, _repo, _config);
        // ShipExtension: ShipExtensionGeometry is static — nothing to build.
        _temperatureSystem = new TemperatureSystem(this, _content);
        _tutorialSystem = new TutorialSystem(this, _content);
        _vehicleSystem = new VehicleSystem(this, _content, _repo);
    }

    // IServerHost: each member delegates to the server's own member of the same name (never to the interface member,
    // which would recurse).
    void IServerHost.Send(PlayerSession session, object message) => Send(session, message);

    void IServerHost.Reject(PlayerSession session, string action, string reason) => Reject(session, action, reason);

    void IServerHost.RaiseGameEvent(PlayerSession? actor, string key, string arg, int amount) => RaiseGameEvent(actor, key, arg, amount);

    PlayerSession? IServerHost.FindSessionByPlayerId(string playerId) => FindSessionByPlayerId(playerId);

    ServerWorld IServerHost.World => _world;

    double IServerHost.Uptime => _uptime;

    WorldManager IServerHost.Worlds => _worlds;

    void IServerHost.SendPlayerState(PlayerSession session) => SendPlayerState(session);

    void IServerHost.SendInventory(PlayerSession session) => SendInventory(session);

    IReadOnlyDictionary<int, PlayerSession> IServerHost.Sessions => _sessions;

    GameRules IServerHost.Rules => Rules;

    bool IServerHost.InSpace(string playerId) => InSpace(playerId);

    void IServerHost.BroadcastToWorld(object message) => BroadcastToWorld(message);

    double IServerHost.WrapDistSq(Vector3f a, Vector3f b) => WrapDistSq(a, b);

    bool IServerHost.ShipInteriorContains(Vector3f p) => ShipInteriorContains(p);
}
