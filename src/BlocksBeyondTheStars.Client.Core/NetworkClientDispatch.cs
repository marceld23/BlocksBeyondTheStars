// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The server → client message table behind <c>Dispatch</c> (#2894): every state message the client acts on and the
    /// typed event it raises, registered once per process. <c>Dispatch</c> looks the message's exact type up here; an
    /// unknown tag (decoded to null) or a type without an entry is ignored, as the former type-pattern <c>switch</c> did.
    /// </summary>
    public sealed partial class NetworkClient
    {
        /// <summary>The enemies this player has met (#2971), for the Codex's "Enemies" chapter: the full list on join, then a
        /// one-key delta per first meeting.</summary>
        public event Action<EnemyLog>? EnemyLogReceived;

        /// <summary>
        /// Server → client message type → the typed event it raises, built once per process. No reflection, so IL2CPP
        /// and managed stripping in the Unity player cannot remove anything. The lookup is by the message's exact type,
        /// which equals the former type-pattern switch because every registered message class is sealed and none
        /// derives from another.
        /// </summary>
        private static readonly Dictionary<Type, Action<NetworkClient, object>> MessageHandlers = BuildMessageHandlers();

        /// <summary>Registers one typed handler. <c>Add</c>, not the indexer: a type registered twice throws at type
        /// initialisation instead of silently replacing the first handler.</summary>
        private static void On<T>(Dictionary<Type, Action<NetworkClient, object>> map, Action<NetworkClient, T> handler)
            where T : class
            => map.Add(typeof(T), (client, message) => handler(client, (T)message));

        /// <summary>One registration per handled message, in the order the switch listed them.</summary>
        private static Dictionary<Type, Action<NetworkClient, object>> BuildMessageHandlers()
        {
            var map = new Dictionary<Type, Action<NetworkClient, object>>(140);
            On<JoinAccepted>(map, (c, m) => c.HandleJoinAccepted(m));
            On<JoinRejected>(map, (c, m) => c.JoinRejected?.Invoke(m));
            On<ChunkDataMessage>(map, (c, m) =>
            {
                if (c.AcceptWorldStream(m.WorldId, m))
                {
                    c.ChunkReceived?.Invoke(m);
                }
            });
            On<BlockChanged>(map, (c, m) =>
            {
                if (c.AcceptWorldStream(m.WorldId, m))
                {
                    c.BlockChanged?.Invoke(m);
                }
            });
            On<FarTerrainWorldInfo>(map, (c, m) => c.FarTerrainWorldInfoReceived?.Invoke(m)); // #1820
            On<FarTerrainTile>(map, (c, m) =>
            {
                // #1821: another world's tile is simply dropped — the far view re-asks per world
                if (m.WorldId == 0 || m.WorldId == c.CurrentWorldId)
                {
                    c.FarTerrainTileReceived?.Invoke(m);
                }
            });
            On<InventoryUpdate>(map, (c, m) => c.InventoryUpdated?.Invoke(m));
            On<PlayerStateUpdate>(map, (c, m) => c.PlayerStateUpdated?.Invoke(m));
            On<CraftResult>(map, (c, m) => c.CraftCompleted?.Invoke(m));
            On<ActionRejected>(map, (c, m) => c.ActionRejected?.Invoke(m));
            On<ServerMessage>(map, (c, m) => c.ServerMessageReceived?.Invoke(m));
            On<MaintenanceNotice>(map, (c, m) => c.MaintenanceNoticeReceived?.Invoke(m));
            On<DockRequestNotice>(map, (c, m) => c.DockRequested?.Invoke(m));
            On<TradeRequestNotice>(map, (c, m) => c.TradeRequested?.Invoke(m));
            On<DockStatus>(map, (c, m) => c.DockStatusChanged?.Invoke(m));
            On<ShipCombatStatus>(map, (c, m) => c.ShipCombatStatusChanged?.Invoke(m));
            On<SpaceState>(map, (c, m) => c.SpaceStateReceived?.Invoke(m));
            On<SpaceShipDesign>(map, (c, m) => c.SpaceShipDesignReceived?.Invoke(m));
            On<StructureBlockChanged>(map, (c, m) => c.StructureBlockChangedReceived?.Invoke(m));
            On<StructureMiningProgress>(map, (c, m) => c.StructureMiningProgressReceived?.Invoke(m));
            On<LandedShipState>(map, (c, m) => c.LandedShipReceived?.Invoke(m));
            On<SpaceEntityDestroyed>(map, (c, m) => c.SpaceEntityDestroyed?.Invoke(m));
            On<SpaceClosed>(map, (c, m) => c.SpaceClosed?.Invoke(m));
            On<SpaceWarpFx>(map, (c, m) => c.SpaceWarpReceived?.Invoke(m));
            On<StationBoarded>(map, (c, m) => c.StationBoardedReceived?.Invoke(m));
            On<PlanetEnemyList>(map, (c, m) => c.PlanetEnemiesReceived?.Invoke(m));
            On<PlanetEnemyDefeated>(map, (c, m) => c.PlanetEnemyDefeated?.Invoke(m));
            On<SentryShot>(map, (c, m) => c.SentryShotReceived?.Invoke(m));
            On<CreatureList>(map, (c, m) => c.CreaturesReceived?.Invoke(m));
            On<CreatureMotionFrame>(map, (c, m) => c.CreatureMotionReceived?.Invoke(m));
            On<WorldFx>(map, (c, m) => c.WorldFxReceived?.Invoke(m));
            On<ActionFx>(map, (c, m) => c.ActionFxReceived?.Invoke(m)); // #2158
            On<CreatureDefeated>(map, (c, m) => c.CreatureDefeatedReceived?.Invoke(m)); // #2154
            On<ContainerList>(map, (c, m) => c.ContainersReceived?.Invoke(m));
            On<ContainerContents>(map, (c, m) => c.ContainerContentsReceived?.Invoke(m)); // #2436
            On<DropPacketList>(map, (c, m) => c.DropPacketsReceived?.Invoke(m));
            On<ShipPlacement>(map, (c, m) => c.ShipPlacementReceived?.Invoke(m));
            On<ShipStations>(map, (c, m) => c.ShipStationsReceived?.Invoke(m));
            On<StationsInReach>(map, (c, m) => c.StationsInReachReceived?.Invoke(m));
            On<StationLocation>(map, (c, m) => c.StationLocationReceived?.Invoke(m));
            On<PlanetPoiList>(map, (c, m) => c.PlanetPoisReceived?.Invoke(m));
            On<BeaconList>(map, (c, m) => c.BeaconsReceived?.Invoke(m));
            On<BeamList>(map, (c, m) => c.BeamsReceived?.Invoke(m));
            On<CrystalNetList>(map, (c, m) => c.CrystalNetsReceived?.Invoke(m));
            On<CrystalDeviceList>(map, (c, m) => c.CrystalDevicesReceived?.Invoke(m));
            On<CrystalDeviceDelta>(map, (c, m) => c.CrystalDeviceDeltaReceived?.Invoke(m));
            On<LiftList>(map, (c, m) => c.LiftsReceived?.Invoke(m));
            On<SoundFx>(map, (c, m) => c.SoundFxReceived?.Invoke(m));
            On<BioBook>(map, (c, m) => c.BioBookReceived?.Invoke(m));
            On<BioLabResult>(map, (c, m) => c.BioLabResultReceived?.Invoke(m));
            On<BeamTeleported>(map, (c, m) => c.BeamTeleportedReceived?.Invoke(m));
            On<BeamFx>(map, (c, m) => c.BeamFxReceived?.Invoke(m));
            On<BaseList>(map, (c, m) => c.BasesReceived?.Invoke(m));
            On<LandingPadList>(map, (c, m) => c.LandingPadsReceived?.Invoke(m));
            On<SystemWeather>(map, (c, m) => c.SystemWeatherReceived?.Invoke(m));
            On<ShipTransitFx>(map, (c, m) => c.ShipTransitReceived?.Invoke(m));
            On<ChatMessage>(map, (c, m) => c.ChatReceived?.Invoke(m));
            On<VoiceFrame>(map, (c, m) => c.VoiceReceived?.Invoke(m));
            On<StarMapData>(map, (c, m) => c.StarMapReceived?.Invoke(m));
            On<MissionList>(map, (c, m) => c.MissionsReceived?.Invoke(m));
            On<MissionResult>(map, (c, m) => c.MissionResultReceived?.Invoke(m));
            On<RespawnNotice>(map, (c, m) => c.RespawnNoticeReceived?.Invoke(m));
            On<RespawnOptions>(map, (c, m) => c.RespawnOptionsReceived?.Invoke(m));
            On<PauseState>(map, (c, m) => c.PauseStateReceived?.Invoke(m));
            On<ServerRules>(map, (c, m) => c.ServerRulesReceived?.Invoke(m));
            On<PlayerPresence>(map, (c, m) => c.PlayerPresenceReceived?.Invoke(m));
            On<PlayerLeft>(map, (c, m) => c.PlayerLeftReceived?.Invoke(m));
            On<PlayerFace>(map, (c, m) => c.PlayerFaceReceived?.Invoke(m));
            On<PlayerBodyPaint>(map, (c, m) => c.PlayerBodyPaintReceived?.Invoke(m));
            On<PlayerToolLook>(map, (c, m) => c.PlayerToolLookReceived?.Invoke(m));
            On<PaintDesignData>(map, (c, m) => c.PaintDesignReceived?.Invoke(m));
            On<PaintDesignList>(map, (c, m) => c.PaintDesignListReceived?.Invoke(m));
            On<CustomShapeData>(map, (c, m) => c.CustomShapeReceived?.Invoke(m));
            On<CustomShapeList>(map, (c, m) => c.CustomShapeListReceived?.Invoke(m));
            On<WorldTextureData>(map, (c, m) => c.WorldTextureReceived?.Invoke(m));
            On<WorldTextureList>(map, (c, m) => c.WorldTextureListReceived?.Invoke(m));
            On<OwnedShips>(map, (c, m) => c.OwnedShipsReceived?.Invoke(m));
            On<WorldEnvironment>(map, (c, m) => c.WorldEnvironmentReceived?.Invoke(m));
            On<WorldReset>(map, (c, m) => c.HandleWorldReset(m));
            On<NpcList>(map, (c, m) => c.NpcsReceived?.Invoke(m));
            On<DoorList>(map, (c, m) => c.DoorsReceived?.Invoke(m));
            On<DataCubeList>(map, (c, m) => c.DataCubesReceived?.Invoke(m));
            On<FactoryList>(map, (c, m) => c.FactoriesReceived?.Invoke(m));
            On<GameUnlocks>(map, (c, m) => c.GameUnlocksReceived?.Invoke(m));
            On<MiningProgress>(map, (c, m) => c.MiningProgressReceived?.Invoke(m));
            On<FloraRegrowStarted>(map, (c, m) => c.FloraRegrowStartedReceived?.Invoke(m));
            On<ScanResult>(map, (c, m) => c.ScanResultReceived?.Invoke(m));
            On<WreckRepairStatus>(map, (c, m) => c.WreckRepairStatusChanged?.Invoke(m));
            On<ShipRepairStatus>(map, (c, m) => c.ShipRepairStatusChanged?.Invoke(m));
            On<TradeUpdate>(map, (c, m) => c.TradeUpdated?.Invoke(m));
            On<TradeClosed>(map, (c, m) => c.TradeClosedReceived?.Invoke(m));
            On<NpcGreeting>(map, (c, m) => c.NpcGreetingReceived?.Invoke(m));
            On<ShipAiLine>(map, (c, m) => c.ShipAiLineReceived?.Invoke(m));
            On<VegaJournal>(map, (c, m) => c.VegaJournalReceived?.Invoke(m));
            On<OreScanResult>(map, (c, m) => c.OreScanReceived?.Invoke(m));
            On<PlanetScanResult>(map, (c, m) => c.PlanetScanReceived?.Invoke(m));
            On<WeatherForecast>(map, (c, m) => c.WeatherForecastReceived?.Invoke(m));
            On<DiscoveryLog>(map, (c, m) => c.DiscoveryLogReceived?.Invoke(m));
            On<EnemyLog>(map, (c, m) => c.EnemyLogReceived?.Invoke(m));
            On<PvpTargets>(map, (c, m) => c.PvpTargetsReceived?.Invoke(m));
            On<DuelNotice>(map, (c, m) => c.DuelNoticeReceived?.Invoke(m));
            On<AchievementList>(map, (c, m) => c.AchievementsReceived?.Invoke(m));
            On<AchievementUnlocked>(map, (c, m) => c.AchievementUnlockedReceived?.Invoke(m));
            On<AchievementRewardDeferred>(map, (c, m) => c.AchievementRewardDeferredReceived?.Invoke(m));
            On<AllianceList>(map, (c, m) => c.AllianceListReceived?.Invoke(m));
            On<AllianceRequestNotice>(map, (c, m) => c.AllianceRequestReceived?.Invoke(m));
            On<TameProgress>(map, (c, m) => c.TameProgressReceived?.Invoke(m));
            On<TameResult>(map, (c, m) => c.TameResultReceived?.Invoke(m));
            On<CompanionList>(map, (c, m) => c.CompanionsReceived?.Invoke(m));
            On<SpeederList>(map, (c, m) => c.SpeedersReceived?.Invoke(m));
            On<SpeederFx>(map, (c, m) => c.SpeederFxReceived?.Invoke(m));
            On<RailList>(map, (c, m) => c.RailsReceived?.Invoke(m)); // #2113
            On<TrainList>(map, (c, m) => c.TrainsReceived?.Invoke(m)); // #2113
            On<CrewList>(map, (c, m) => c.CrewListReceived?.Invoke(m));
            On<CrewInviteNotice>(map, (c, m) => c.CrewInviteReceived?.Invoke(m));
            On<MarkerList>(map, (c, m) => c.MarkerListReceived?.Invoke(m));
            On<NoteList>(map, (c, m) => c.NoteListReceived?.Invoke(m));
            On<StoryStateMessage>(map, (c, m) => c.StoryStateReceived?.Invoke(m));
            On<NetFragmentList>(map, (c, m) => c.NetFragmentsReceived?.Invoke(m));
            On<NetFragmentRevealed>(map, (c, m) => c.NetFragmentRevealedReceived?.Invoke(m));
            On<PlayerMemoryRevealed>(map, (c, m) => c.PlayerMemoryReceived?.Invoke(m));
            On<LoreTextRevealed>(map, (c, m) => c.LoreTextRevealedReceived?.Invoke(m));
            On<ExploredMapData>(map, (c, m) => c.ExploredMapReceived?.Invoke(m));
            On<BuildCodeResult>(map, (c, m) => c.BuildCodeReceived?.Invoke(m));
            On<BuildPasteResult>(map, (c, m) => c.BuildPasteResultReceived?.Invoke(m));
            On<NpcStandingList>(map, (c, m) => c.NpcStandingsReceived?.Invoke(m));
            On<KnownNpcList>(map, (c, m) => c.KnownNpcsReceived?.Invoke(m));
            On<BaseResidentList>(map, (c, m) => c.BaseResidentsReceived?.Invoke(m));
            On<GuardianSystemRevealed>(map, (c, m) => c.GuardianSystemRevealedReceived?.Invoke(m));
            On<CoreHackProgress>(map, (c, m) => c.CoreHackProgressReceived?.Invoke(m));
            On<CoreDialogueMessage>(map, (c, m) => c.CoreDialogueReceived?.Invoke(m));
            On<StoryResolved>(map, (c, m) => c.StoryResolvedReceived?.Invoke(m));
            On<CutscenePlay>(map, (c, m) => c.CutscenePlayReceived?.Invoke(m));
            On<ObjectiveList>(map, (c, m) => c.ObjectiveListReceived?.Invoke(m));
            On<StoryPackStates>(map, (c, m) => c.StoryPackStatesReceived?.Invoke(m));
            On<RelayNetworkState>(map, (c, m) => c.RelayNetworkReceived?.Invoke(m));
            On<NpcDialogState>(map, (c, m) => c.NpcDialogReceived?.Invoke(m));
            On<BanditDemand>(map, (c, m) => c.BanditDemandReceived?.Invoke(m));
            On<BanditEncounterResult>(map, (c, m) => c.BanditResultReceived?.Invoke(m));
            return map;
        }

        private void HandleJoinAccepted(JoinAccepted m)
        {
            string ours = EffectiveContentFingerprint;
            if (ours.Length == 0 || !string.Equals(m.ContentFingerprint, ours, StringComparison.Ordinal))
            {
                RefuseMismatchedServer(); // #2222: missing or different — never draw a world with the wrong palette
                return;
            }

            _joined = true;
            AdoptWorld(m.WorldId);
            ResyncHotbarSlot(); // #2525: the server restored the slot saved at the quit; the hand on screen wins
            JoinAccepted?.Invoke(m);
            ReplayHeldBeforeJoin();
        }

        private void HandleWorldReset(WorldReset m)
        {
            AdoptWorld(m.WorldId);
            ResyncHotbarSlot(); // #2525: a world change may load another saved slot — keep the one on screen
            WorldResetReceived?.Invoke(m);
            ReplayFutureWorld();
        }

        // ---------------- Test seams ----------------

        /// <summary>Every server message type with a handler, so a test can pin that no message lost its event when the
        /// switch became a registry. Public because Client.Core has no InternalsVisibleTo (like
        /// <see cref="MaxDispatchPerPoll"/>).</summary>
        public static IEnumerable<Type> HandledMessageTypesForTest => MessageHandlers.Keys;
    }
}
