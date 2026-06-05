using System;
using Spacecraft.Networking;
using Spacecraft.Networking.Messages;
using Spacecraft.Networking.Transport;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Thin Unity-side wrapper around the shared <see cref="IClientTransport"/> and
    /// <see cref="NetCodec"/>. It sends intents and raises typed events for authoritative
    /// state from the server. The client never decides outcomes — it only renders what the
    /// server reports.
    /// </summary>
    public sealed class NetworkClient : IDisposable
    {
        private readonly IClientTransport _transport;

        public event Action<JoinAccepted> JoinAccepted;
        public event Action<JoinRejected> JoinRejected;
        public event Action<ChunkDataMessage> ChunkReceived;
        public event Action<BlockChanged> BlockChanged;
        public event Action<InventoryUpdate> InventoryUpdated;
        public event Action<PlayerStateUpdate> PlayerStateUpdated;
        public event Action<CraftResult> CraftCompleted;
        public event Action<ActionRejected> ActionRejected;
        public event Action<ServerMessage> ServerMessageReceived;

        // Ship docking (M18) and free space flight / combat (M19). Rendering is added later;
        // these hooks let the client react to the authoritative state the server reports.
        public event Action<DockRequestNotice> DockRequested;
        public event Action<DockStatus> DockStatusChanged;
        public event Action<ShipCombatStatus> ShipCombatStatusChanged;
        public event Action<SpaceState> SpaceStateReceived;
        public event Action<SpaceEntityDestroyed> SpaceEntityDestroyed;
        public event Action<SpaceClosed> SpaceClosed;
        public event Action<StationBoarded> StationBoardedReceived;
        public event Action<PlanetEnemyList> PlanetEnemiesReceived;
        public event Action<PlanetEnemyDefeated> PlanetEnemyDefeated;
        public event Action<CreatureList> CreaturesReceived;
        public event Action<ContainerList> ContainersReceived;
        public event Action<ShipPlacement> ShipPlacementReceived;
        public event Action<ShipStations> ShipStationsReceived;
        public event Action<PlanetPoiList> PlanetPoisReceived;
        public event Action<ChatMessage> ChatReceived;

        // Navigation, missions & feedback (M23).
        public event Action<StarMapData> StarMapReceived;
        public event Action<MissionList> MissionsReceived;
        public event Action<MissionResult> MissionResultReceived;
        public event Action<RespawnNotice> RespawnNoticeReceived;
        public event Action<ServerRules> ServerRulesReceived;

        // Multiplayer presence (M24).
        public event Action<PlayerPresence> PlayerPresenceReceived;
        public event Action<PlayerLeft> PlayerLeftReceived;
        public event Action<OwnedShips> OwnedShipsReceived;
        public event Action<WorldEnvironment> WorldEnvironmentReceived;
        public event Action<WorldReset> WorldResetReceived;
        public event Action<NpcList> NpcsReceived;
        public event Action<DoorList> DoorsReceived;
        public event Action<MiningProgress> MiningProgressReceived;

        // Scanning (knowledge), crashed-ship wreck repair, and player-to-player trade.
        public event Action<ScanResult> ScanResultReceived;
        public event Action<WreckRepairStatus> WreckRepairStatusChanged;
        public event Action<TradeUpdate> TradeUpdated;
        public event Action<TradeClosed> TradeClosedReceived;

        public bool Connected { get; private set; }

        /// <summary>Uses the UDP transport by default; pass a loopback transport for singleplayer.</summary>
        public NetworkClient(IClientTransport transport = null)
        {
            _transport = transport ?? new LiteNetLibClientTransport();
            _transport.Connected += () => Connected = true;
            _transport.Disconnected += () => Connected = false;
            _transport.PayloadReceived += OnPayload;
        }

        public void Connect(string host, int port) => _transport.Connect(host, port);

        public void Join(string playerName, string password = null)
            => Send(new JoinRequest { PlayerName = playerName, Password = password });

        public void SendMove(Vector3 pos, float yaw, float pitch)
            => Send(new MoveIntent { X = pos.x, Y = pos.y, Z = pos.z, Yaw = yaw, Pitch = pitch }, DeliveryMode.Unreliable);

        public void SendMine(int x, int y, int z) => Send(new MineBlockIntent { X = x, Y = y, Z = z });

        public void SendPlace(int x, int y, int z, string itemKey)
            => Send(new PlaceBlockIntent { X = x, Y = y, Z = z, ItemKey = itemKey });

        public void SendCraft(string recipeKey, int count = 1)
            => Send(new CraftIntent { RecipeKey = recipeKey, Count = count });

        public void SendUnlock(string blueprintKey) => Send(new UnlockBlueprintIntent { BlueprintKey = blueprintKey });
        public void SendChat(string text) => Send(new ChatIntent { Text = text });

        // Admin/cheat console (server validates IsAdmin + the CheatsAllowed rule and replies with a ServerMessage).
        public void SendAdminCommand(string command, string stringArg = null, int intArg = 0,
            string targetPlayer = null, float x = 0f, float y = 0f, float z = 0f)
            => Send(new AdminCommandIntent
            {
                Command = command, StringArg = stringArg, IntArg = intArg,
                TargetPlayer = targetPlayer, X = x, Y = y, Z = z,
            });

        public void SendSelectHotbar(int slot) => Send(new SelectHotbarIntent { Slot = slot });

        // --- Ship docking (M18) ---
        public void SendDockRequest(string targetPlayer) => Send(new DockRequestIntent { TargetPlayer = targetPlayer });

        public void SendDockResponse(string requester, bool accept)
            => Send(new DockResponseIntent { Requester = requester, Accept = accept });

        public void SendUndock() => Send(new UndockIntent());

        // --- Free space flight & combat (M19) ---
        public void SendBuildModule(string moduleKey) => Send(new BuildShipModuleIntent { ModuleKey = moduleKey });

        public void SendEnterSpace() => Send(new EnterSpaceIntent());

        public void SendLeaveSpace() => Send(new LeaveSpaceIntent());

        /// <summary>Leave space and land on a specific body (the one flown to in system-scale flight);
        /// empty = land back on the current body.</summary>
        public void SendLeaveSpace(string destinationBodyId) => Send(new LeaveSpaceIntent { DestinationBodyId = destinationBodyId });

        public void SendFireWeapon(string weaponKey, string targetEntityId)
            => Send(new FireWeaponIntent { WeaponKey = weaponKey, TargetEntityId = targetEntityId });

        public void SendAttackEntity(string entityId) => Send(new AttackEntityIntent { EntityId = entityId });

        /// <summary>Asks the server to save the world + players to disk now (explicit save).</summary>
        public void SendSaveGame() => Send(new SaveGameIntent());

        /// <summary>Fires the ship's tractor beam as a manual sweep to pull in nearby salvage.</summary>
        public void SendTractorPull() => Send(new TractorPullIntent());

        public void SendConsume(string itemKey) => Send(new ConsumeItemIntent { ItemKey = itemKey });

        public void SendLoadRation(string itemKey, int count) => Send(new LoadRationIntent { ItemKey = itemKey, Count = count });

        public void SendTeleportToShip() => Send(new TeleportToShipIntent());

        public void SendToggleStealth() => Send(new ToggleStealthIntent());

        public void SendSetJetpack(bool active) => Send(new SetJetpackIntent { Active = active });

        public void SendDisassemble(string itemKey) => Send(new DisassembleIntent { ItemKey = itemKey });

        public void SendScan(string subjectType, string subjectKey) => Send(new ScanIntent { SubjectType = subjectType, SubjectKey = subjectKey });

        public void SendScanEntity(string entityId) => Send(new ScanEntityIntent { EntityId = entityId });

        public void SendLootContainer(string containerId) => Send(new LootContainerIntent { ContainerId = containerId });

        // --- Crashed-ship wreck repair / claim ---
        public void SendRepairWreck(int x, int y, int z, string itemKey)
            => Send(new RepairWreckIntent { X = x, Y = y, Z = z, ItemKey = itemKey });

        public void SendClaimWreck() => Send(new ClaimWreckIntent());

        // --- Player-to-player trade ---
        public void SendTradeRequest(string targetPlayer) => Send(new TradeRequestIntent { TargetPlayer = targetPlayer });

        public void SendTradeRespond(bool accept) => Send(new TradeRespondIntent { Accept = accept });

        public void SendTradeOffer(NetTradeItem[] items) => Send(new TradeOfferIntent { Items = items });

        public void SendTradeConfirm() => Send(new TradeConfirmIntent());

        public void SendTradeCancel() => Send(new TradeCancelIntent());

        // Reports the ship's position while flying in space (for server-side collision).
        public void SendShipMove(Vector3 pos) => Send(new ShipMoveIntent { X = pos.x, Y = pos.y, Z = pos.z });

        public void SendBoardStation(string stationId) => Send(new BoardStationIntent { StationId = stationId });

        public void SendLeaveStation() => Send(new LeaveStationIntent());

        public void SendUseStation(string station) => Send(new UseStationIntent { Station = station });
        public void SendDoorInteract(int doorId) => Send(new DoorInteractIntent { DoorId = doorId });

        // --- Navigation & missions (M23) ---
        public void SendRequestStarMap() => Send(new RequestStarMap());

        public void SendTravel(string bodyId) => Send(new TravelIntent { DestinationBodyId = bodyId });

        public void SendRequestMissions() => Send(new RequestMissions());

        public void SendAcceptMission(string missionId) => Send(new AcceptMissionIntent { MissionId = missionId });

        public void SendTurnInMission(string missionId) => Send(new TurnInMissionIntent { MissionId = missionId });

        public void SendAppearance(int skin, int torso, int arms, int legs)
            => Send(new SetAppearanceIntent { Skin = skin, Torso = torso, Arms = arms, Legs = legs });

        public void SendCraftShip(string shipType) => Send(new CraftShipIntent { ShipType = shipType });

        public void SendSwitchShip(string shipId) => Send(new SwitchShipIntent { ShipId = shipId });

        /// <summary>Pumps the transport; call once per frame from a MonoBehaviour Update.</summary>
        public void Poll() => _transport.Poll();

        private void Send(object message, DeliveryMode mode = DeliveryMode.ReliableOrdered)
            => _transport.Send(NetCodec.Encode(message), mode);

        private void OnPayload(byte[] payload)
        {
            switch (NetCodec.Decode(payload))
            {
                case JoinAccepted m: JoinAccepted?.Invoke(m); break;
                case JoinRejected m: JoinRejected?.Invoke(m); break;
                case ChunkDataMessage m: ChunkReceived?.Invoke(m); break;
                case BlockChanged m: BlockChanged?.Invoke(m); break;
                case InventoryUpdate m: InventoryUpdated?.Invoke(m); break;
                case PlayerStateUpdate m: PlayerStateUpdated?.Invoke(m); break;
                case CraftResult m: CraftCompleted?.Invoke(m); break;
                case ActionRejected m: ActionRejected?.Invoke(m); break;
                case ServerMessage m: ServerMessageReceived?.Invoke(m); break;
                case DockRequestNotice m: DockRequested?.Invoke(m); break;
                case DockStatus m: DockStatusChanged?.Invoke(m); break;
                case ShipCombatStatus m: ShipCombatStatusChanged?.Invoke(m); break;
                case SpaceState m: SpaceStateReceived?.Invoke(m); break;
                case SpaceEntityDestroyed m: SpaceEntityDestroyed?.Invoke(m); break;
                case SpaceClosed m: SpaceClosed?.Invoke(m); break;
                case StationBoarded m: StationBoardedReceived?.Invoke(m); break;
                case PlanetEnemyList m: PlanetEnemiesReceived?.Invoke(m); break;
                case PlanetEnemyDefeated m: PlanetEnemyDefeated?.Invoke(m); break;
                case CreatureList m: CreaturesReceived?.Invoke(m); break;
                case ContainerList m: ContainersReceived?.Invoke(m); break;
                case ShipPlacement m: ShipPlacementReceived?.Invoke(m); break;
                case ShipStations m: ShipStationsReceived?.Invoke(m); break;
                case PlanetPoiList m: PlanetPoisReceived?.Invoke(m); break;
                case ChatMessage m: ChatReceived?.Invoke(m); break;
                case StarMapData m: StarMapReceived?.Invoke(m); break;
                case MissionList m: MissionsReceived?.Invoke(m); break;
                case MissionResult m: MissionResultReceived?.Invoke(m); break;
                case RespawnNotice m: RespawnNoticeReceived?.Invoke(m); break;
                case ServerRules m: ServerRulesReceived?.Invoke(m); break;
                case PlayerPresence m: PlayerPresenceReceived?.Invoke(m); break;
                case PlayerLeft m: PlayerLeftReceived?.Invoke(m); break;
                case OwnedShips m: OwnedShipsReceived?.Invoke(m); break;
                case WorldEnvironment m: WorldEnvironmentReceived?.Invoke(m); break;
                case WorldReset m: WorldResetReceived?.Invoke(m); break;
                case NpcList m: NpcsReceived?.Invoke(m); break;
                case DoorList m: DoorsReceived?.Invoke(m); break;
                case MiningProgress m: MiningProgressReceived?.Invoke(m); break;
                case ScanResult m: ScanResultReceived?.Invoke(m); break;
                case WreckRepairStatus m: WreckRepairStatusChanged?.Invoke(m); break;
                case TradeUpdate m: TradeUpdated?.Invoke(m); break;
                case TradeClosed m: TradeClosedReceived?.Invoke(m); break;
            }
        }

        public void Dispose()
        {
            _transport.Disconnect();
            _transport.Dispose();
        }
    }
}
