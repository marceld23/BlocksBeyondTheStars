using System;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
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
        public event Action<SpaceShipDesign> SpaceShipDesignReceived; // item 20 S1: own ship as a voxel structure
        public event Action<StructureBlockChanged> StructureBlockChangedReceived; // item 20 S2: a structure cell changed
        public event Action<LandedShipState> LandedShipReceived; // ship-as-object: a parked ship placed/replaced/removed
        public event Action<SpaceEntityDestroyed> SpaceEntityDestroyed;
        public event Action<SpaceClosed> SpaceClosed;
        public event Action<SpaceWarpFx> SpaceWarpReceived; // peaceful NPC trader warp-in/out flash for bystanders
        public event Action<StationBoarded> StationBoardedReceived;
        public event Action<PlanetEnemyList> PlanetEnemiesReceived;
        public event Action<PlanetEnemyDefeated> PlanetEnemyDefeated;
        public event Action<CreatureList> CreaturesReceived;
        public event Action<ContainerList> ContainersReceived;
        public event Action<ShipPlacement> ShipPlacementReceived;
        public event Action<ShipStations> ShipStationsReceived;
        public event Action<PlanetPoiList> PlanetPoisReceived;
        public event Action<BeaconList> BeaconsReceived;
        public event Action<BeamList> BeamsReceived; // placed beam blocks (teleporter pads) on the current world
        public event Action<BeamTeleported> BeamTeleportedReceived; // my arrival after a beam (snap + arrival fx)
        public event Action<BeamFx> BeamFxReceived; // beam column VFX at both pads, shown to everyone on the world
        public event Action<BaseList> BasesReceived; // player-founded planet bases (Grundstein) on the current world
        public event Action<LandingPadList> LandingPadsReceived;
        public event Action<ShipTransitFx> ShipTransitReceived;
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
        public event Action<PlayerFace> PlayerFaceReceived; // another player's custom pixel face
        public event Action<OwnedShips> OwnedShipsReceived;
        public event Action<WorldEnvironment> WorldEnvironmentReceived;
        public event Action<WorldReset> WorldResetReceived;
        public event Action<NpcList> NpcsReceived;
        public event Action<DoorList> DoorsReceived;
        public event Action<MiningProgress> MiningProgressReceived;

        // Data-cube minigames: cubes to render on the current world + the player's downloaded-games collection.
        public event Action<DataCubeList> DataCubesReceived;
        public event Action<GameUnlocks> GameUnlocksReceived;

        // Story system ("The VEGA Protocol"): the active story's shared progress, the world's net fragments,
        // a picked-up fragment's archive text, and a personal player memory unlocked by a machine kill.
        public event Action<StoryStateMessage> StoryStateReceived;
        public event Action<NetFragmentList> NetFragmentsReceived;
        public event Action<NetFragmentRevealed> NetFragmentRevealedReceived;
        public event Action<PlayerMemoryRevealed> PlayerMemoryReceived;
        public event Action<GuardianSystemRevealed> GuardianSystemRevealedReceived; // finale system placed on the map
        public event Action<CoreHackProgress> CoreHackProgressReceived;             // core-hack channel progress
        public event Action<CoreDialogueMessage> CoreDialogueReceived;              // argument-duel node / win

        // Scanning (knowledge), crashed-ship wreck repair, and player-to-player trade.
        public event Action<ScanResult> ScanResultReceived;
        public event Action<WreckRepairStatus> WreckRepairStatusChanged;
        public event Action<ShipRepairStatus> ShipRepairStatusChanged;
        public event Action<TradeUpdate> TradeUpdated;
        public event Action<TradeClosed> TradeClosedReceived;

        // Contextual NPC greetings (item 15): a speech-bubble line when interacting with a vendor/quartermaster.
        public event Action<NpcGreeting> NpcGreetingReceived;

        // Terrain-scanner pulse result (Feature 40): ore positions for the through-wall glow markers.
        public event Action<OreScanResult> OreScanReceived;

        // Ship AI companion "VEGA": onboarding/advisor/story lines + the active objective chip.
        public event Action<ShipAiLine> ShipAiLineReceived;

        // Player alliances: the full roster (allies + pending requests) and a "someone proposed" toast notice.
        public event Action<AllianceList> AllianceListReceived;
        public event Action<AllianceRequestNotice> AllianceRequestReceived;

        // Creature taming + companions: the live ritual state, the finished result, and the player's roster.
        public event Action<TameProgress> TameProgressReceived;
        public event Action<TameResult> TameResultReceived;
        public event Action<CompanionList> CompanionsReceived;

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

        public void Join(string playerName, string password = null, string locale = "en", string token = null)
            => Send(new JoinRequest { PlayerName = playerName, Password = password, Locale = locale, Token = token });

        /// <summary>Asks the server for the greeting line of the nearby NPC of this role ("vendor"/"quartermaster")
        /// when opening its interaction (item 15). The server gates on proximity and replies with an NpcGreeting.</summary>
        public void SendNpcGreet(string role) => Send(new NpcGreetIntent { Role = role });

        /// <summary>Skips the VEGA onboarding (grants the whole stage chain server-side) — or restarts it
        /// from the intro when <paramref name="restart"/> is set (the way back after a skip).</summary>
        public void SendSkipOnboarding(bool restart = false) => Send(new SkipOnboardingIntent { Restart = restart });

        /// <summary>World admin: live-edits the gameplay world options (empty fields = unchanged).</summary>
        public void SendSetWorldRules(string creatures = "", string planetEnemies = "", string spaceNpcs = "", string ufos = "", string instantTravel = "")
            => Send(new SetWorldRulesIntent
            {
                CreatureAbundance = creatures,
                PlanetEnemies = planetEnemies,
                SpaceNpcEnemies = spaceNpcs,
                AlienUfos = ufos,
                InstantTravel = instantTravel,
            });

        /// <summary>Hyperjump into a (possibly unvisited) star system, arriving in flight mode there.</summary>
        public void SendHyperjumpSystem(string systemId) => Send(new HyperjumpSystemIntent { SystemId = systemId });

        public void SendMove(Vector3 pos, float yaw, float pitch)
            => Send(new MoveIntent { X = pos.x, Y = pos.y, Z = pos.z, Yaw = yaw, Pitch = pitch }, DeliveryMode.Unreliable);

        public void SendMine(int x, int y, int z) => Send(new MineBlockIntent { X = x, Y = y, Z = z });

        public void SendPlace(int x, int y, int z, string itemKey, string label = null)
            => Send(new PlaceBlockIntent { X = x, Y = y, Z = z, ItemKey = itemKey, Label = label ?? string.Empty });

        public void SendSetBeaconLabel(int beaconId, string label)
            => Send(new SetBeaconLabelIntent { BeaconId = beaconId, Label = label ?? string.Empty });

        /// <summary>Name/rename a beam block I own — by E on the pad (opens the transporter, which has a rename button).</summary>
        public void SendSetBeamName(int beamId, string name)
            => Send(new SetBeamNameIntent { BeamId = beamId, Name = name ?? string.Empty });

        /// <summary>Beam from the pad I'm standing at to a chosen destination pad on this world.</summary>
        public void SendBeamTeleport(int sourceId, int targetId)
            => Send(new BeamTeleportIntent { SourceId = sourceId, TargetId = targetId });

        /// <summary>Name/rename my base on a body (Grundstein) — by E at the stone, or the Map "Rename base" button.</summary>
        public void SendSetBaseName(string bodyId, string name)
            => Send(new SetBaseNameIntent { BodyId = bodyId ?? string.Empty, Name = name ?? string.Empty });

        /// <summary>Rename a commissioned station I built — via the Map "Rename" button or E at the station core.</summary>
        public void SendSetStationName(string stationId, string name)
            => Send(new SetStationNameIntent { StationId = stationId ?? string.Empty, Name = name ?? string.Empty });

        public void SendCraft(string recipeKey, int count = 1)
            => Send(new CraftIntent { RecipeKey = recipeKey, Count = count });

        /// <summary>Always-available "Dye"/"Glow" action: recolour a held building material (surface tint
        /// and/or a coloured light source). Output is the same item carrying the colour in its key.</summary>
        public void SendTintCraft(string sourceItemKey, int tint, int glow, int count = 1)
            => Send(new TintCraftIntent { SourceItemKey = sourceItemKey ?? string.Empty, Tint = tint, Glow = glow, Count = count });
        public void SendFallDamage(float impactSpeed) => Send(new FallDamageIntent { ImpactSpeed = impactSpeed });

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

        /// <summary>Rearranges the personal inventory: swap two slots, or stow out of the quick-bar with toSlot=-1
        /// (B58 — customising the quick-bar).</summary>
        public void SendMoveItem(int fromSlot, int toSlot) => Send(new MoveItemIntent { FromSlot = fromSlot, ToSlot = toSlot });

        // --- Ship docking (M18) ---
        public void SendDockRequest(string targetPlayer) => Send(new DockRequestIntent { TargetPlayer = targetPlayer });

        public void SendDockResponse(string requester, bool accept)
            => Send(new DockResponseIntent { Requester = requester, Accept = accept });

        public void SendUndock() => Send(new UndockIntent());

        // --- Free space flight & combat (M19) ---
        public void SendBuildModule(string moduleKey) => Send(new BuildShipModuleIntent { ModuleKey = moduleKey });

        public void SendEnterSpace() => Send(new EnterSpaceIntent());

        public void SendEnterShip() => Send(new EnterShipIntent());

        public void SendExitShip() => Send(new ExitShipIntent());

        public void SendLeaveSpace() => Send(new LeaveSpaceIntent());

        /// <summary>Leave space and land on a body (empty = the current body), on a chosen landing pad (item 38;
        /// padIndex -1 = auto-pick the first free pad).</summary>
        public void SendLeaveSpace(string destinationBodyId, int padIndex = -1)
            => Send(new LeaveSpaceIntent { DestinationBodyId = destinationBodyId, PadIndex = padIndex });

        /// <summary>Asks the server for a body's fixed landing pads + their live occupancy (the pad chooser).</summary>
        public void SendRequestLandingPads(string bodyId) => Send(new RequestLandingPadsIntent { BodyId = bodyId });

        public void SendFireWeapon(string weaponKey, string targetEntityId)
            => Send(new FireWeaponIntent { WeaponKey = weaponKey, TargetEntityId = targetEntityId });

        public void SendAttackEntity(string entityId) => Send(new AttackEntityIntent { EntityId = entityId });

        /// <summary>Asks the server to save the world + players to disk now (explicit save).</summary>
        public void SendSaveGame() => Send(new SaveGameIntent());

        /// <summary>Fires the ship's tractor beam as a manual sweep to pull in nearby salvage.</summary>
        public void SendTractorPull() => Send(new TractorPullIntent());

        public void SendConsume(string itemKey) => Send(new ConsumeItemIntent { ItemKey = itemKey });

        public void SendUseGadget(string gadgetKey, Vector3 target)
            => Send(new UseGadgetIntent { GadgetKey = gadgetKey, X = target.x, Y = target.y, Z = target.z });

        public void SendLoadRation(string itemKey, int count) => Send(new LoadRationIntent { ItemKey = itemKey, Count = count });

        public void SendTeleportToShip() => Send(new TeleportToShipIntent());

        public void SendToggleStealth() => Send(new ToggleStealthIntent());

        public void SendSetJetpack(bool active) => Send(new SetJetpackIntent { Active = active });

        public void SendSetEva(bool active) => Send(new SetEvaIntent { Active = active });

        public void SendDisassemble(string itemKey) => Send(new DisassembleIntent { ItemKey = itemKey });

        public void SendScan(string subjectType, string subjectKey) => Send(new ScanIntent { SubjectType = subjectType, SubjectKey = subjectKey });

        public void SendScanEntity(string entityId) => Send(new ScanEntityIntent { EntityId = entityId });

        public void SendLootContainer(string containerId) => Send(new LootContainerIntent { ContainerId = containerId });

        public void SendDepositContainer(string containerId) => Send(new DepositContainerIntent { ContainerId = containerId });

        // --- Crashed-ship wreck repair / claim ---
        public void SendRepairWreck(int x, int y, int z, string itemKey)
            => Send(new RepairWreckIntent { X = x, Y = y, Z = z, ItemKey = itemKey });

        public void SendClaimWreck() => Send(new ClaimWreckIntent());

        // --- Own-ship repair (hull stat + missing design hull cells) ---
        public void SendRepairShip(string mode) => Send(new RepairShipIntent { Mode = mode });

        public void SendRepairShipCell(int x, int y, int z)
            => Send(new RepairShipIntent { Mode = "cell", X = x, Y = y, Z = z });

        // --- Player-to-player trade ---
        public void SendTradeRequest(string targetPlayer) => Send(new TradeRequestIntent { TargetPlayer = targetPlayer });

        public void SendTradeRespond(bool accept) => Send(new TradeRespondIntent { Accept = accept });

        public void SendTradeOffer(NetTradeItem[] items) => Send(new TradeOfferIntent { Items = items });

        public void SendTradeKnowledge(int amount) => Send(new TradeKnowledgeIntent { Amount = amount });

        public void SendTradeConfirm() => Send(new TradeConfirmIntent());

        public void SendTradeCancel() => Send(new TradeCancelIntent());

        // Reports the ship's position while flying in space (for server-side collision).
        public void SendShipMove(Vector3 pos, float yaw = 0f) => Send(new ShipMoveIntent { X = pos.x, Y = pos.y, Z = pos.z, Yaw = yaw });

        /// <summary>EVA build/mine on a voxel structure (item 20 S2). Design-local cell coords.</summary>
        public void SendStructureEdit(string structureId, int x, int y, int z, bool mine, string itemKey = "")
            => Send(new StructureEditIntent { StructureId = structureId, X = x, Y = y, Z = z, Mine = mine, ItemKey = itemKey });

        /// <summary>Deploy a station core in front of the suit to start a player-built station (item 20 S4).</summary>
        public void SendDeployStationCore() => Send(new DeployStationCoreIntent());

        public void SendBoardStation(string stationId) => Send(new BoardStationIntent { StationId = stationId });

        public void SendLeaveStation() => Send(new LeaveStationIntent());

        public void SendUseStation(string station) => Send(new UseStationIntent { Station = station });
        public void SendDoorInteract(int doorId) => Send(new DoorInteractIntent { DoorId = doorId });

        /// <summary>Downloads the data cube the player is standing at into their arcade collection. The client
        /// resolves which game the cube holds (from its seed); the server validates proximity to the cube.</summary>
        public void SendUnlockGame(int cubeId, string gameKey) => Send(new UnlockGameIntent { CubeId = cubeId, GameKey = gameKey ?? string.Empty });

        // Story: pick up a net fragment I'm standing at; and (admin) choose the active story pack.
        public void SendNetFragmentFound(int fragmentId) => Send(new NetFragmentFoundIntent { FragmentId = fragmentId });
        public void SendStorySelect(string storyId) => Send(new StorySelectIntent { StoryId = storyId ?? string.Empty });
        public void SendCoreHackTick() => Send(new CoreHackIntent());                                  // channel the core hack
        public void SendCoreDialogueChoice(int choiceIndex) => Send(new CoreDialogueChoiceIntent { ChoiceIndex = choiceIndex }); // duel rebuttal

        /// <summary>Reports a finished minigame run so the server can grant a knowledge reward.</summary>
        public void SendMinigameResult(string gameKey, int score, int rating, bool completed)
            => Send(new MinigameResultIntent { GameKey = gameKey ?? string.Empty, Score = score, Rating = rating, Completed = completed });

        // --- Navigation & missions (M23) ---
        public void SendRequestStarMap() => Send(new RequestStarMap());

        public void SendTravel(string bodyId) => Send(new TravelIntent { DestinationBodyId = bodyId });

        public void SendRequestMissions() => Send(new RequestMissions());

        public void SendAcceptMission(string missionId) => Send(new AcceptMissionIntent { MissionId = missionId });

        public void SendTurnInMission(string missionId) => Send(new TurnInMissionIntent { MissionId = missionId });

        /// <summary>Posts a player-created mission (item 31): objectives others can complete + a staked reward.</summary>
        public void SendCreateMission(string title, string description, NetMissionObjective[] objectives, NetReward[] rewards)
            => Send(new CreateMissionIntent { Title = title, Description = description, Objectives = objectives, Rewards = rewards });

        public void SendAppearance(int skin, int torso, int arms, int legs, int hull = 0)
            => Send(new SetAppearanceIntent { Skin = skin, Torso = torso, Arms = arms, Legs = legs, Hull = hull });

        /// <summary>Sends the player's custom pixel face (empty clears it). Sent on join and on each edit —
        /// out of band from the presence stream.</summary>
        public void SendFace(string pixels) => Send(new SetFaceIntent { Pixels = pixels ?? string.Empty });

        public void SendCraftShip(string shipType) => Send(new CraftShipIntent { ShipType = shipType });

        public void SendSwitchShip(string shipId) => Send(new SwitchShipIntent { ShipId = shipId });

        // --- Player alliances ---
        /// <summary>Asks the server for the current alliance roster (sent when the Alliances tab opens).</summary>
        public void SendRequestAllianceList() => Send(new RequestAllianceListIntent());

        /// <summary>Proposes an alliance to another player (they must accept before it forms).</summary>
        public void SendRequestAlliance(string targetPlayerId) => Send(new RequestAllianceIntent { TargetPlayerId = targetPlayerId ?? string.Empty });

        /// <summary>Accepts or declines a pending alliance request from another player.</summary>
        public void SendAllianceResponse(string requesterId, bool accept)
            => Send(new AllianceResponseIntent { RequesterId = requesterId ?? string.Empty, Accept = accept });

        /// <summary>Ends an existing alliance with a partner (one-sided — either side may dissolve it).</summary>
        public void SendDissolveAlliance(string partnerId) => Send(new DissolveAllianceIntent { PartnerId = partnerId ?? string.Empty });

        // --- Creature taming + companions ---
        /// <summary>A response in the taming ritual ("feed" / "calm" / "approach" / "space" / "cancel"). The
        /// ritual is started by right-clicking the creature_translator gadget (SendUseGadget).</summary>
        public void SendTameRespond(string creatureId, string response)
            => Send(new TameRespondIntent { CreatureId = creatureId ?? string.Empty, Response = response ?? string.Empty });

        /// <summary>Asks the server for the player's companion roster (sent when the Companions tab opens).</summary>
        public void SendRequestCompanions() => Send(new RequestCompanionsIntent());

        /// <summary>Rename a companion I own.</summary>
        public void SendSetCompanionName(string companionId, string name)
            => Send(new SetCompanionNameIntent { CompanionId = companionId ?? string.Empty, Name = name ?? string.Empty });

        /// <summary>Release a companion (untame it).</summary>
        public void SendReleaseCompanion(string companionId) => Send(new ReleaseCompanionIntent { CompanionId = companionId ?? string.Empty });

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
                case SpaceShipDesign m: SpaceShipDesignReceived?.Invoke(m); break;
                case StructureBlockChanged m: StructureBlockChangedReceived?.Invoke(m); break;
                case LandedShipState m: LandedShipReceived?.Invoke(m); break;
                case SpaceEntityDestroyed m: SpaceEntityDestroyed?.Invoke(m); break;
                case SpaceClosed m: SpaceClosed?.Invoke(m); break;
                case SpaceWarpFx m: SpaceWarpReceived?.Invoke(m); break;
                case StationBoarded m: StationBoardedReceived?.Invoke(m); break;
                case PlanetEnemyList m: PlanetEnemiesReceived?.Invoke(m); break;
                case PlanetEnemyDefeated m: PlanetEnemyDefeated?.Invoke(m); break;
                case CreatureList m: CreaturesReceived?.Invoke(m); break;
                case ContainerList m: ContainersReceived?.Invoke(m); break;
                case ShipPlacement m: ShipPlacementReceived?.Invoke(m); break;
                case ShipStations m: ShipStationsReceived?.Invoke(m); break;
                case PlanetPoiList m: PlanetPoisReceived?.Invoke(m); break;
                case BeaconList m: BeaconsReceived?.Invoke(m); break;
                case BeamList m: BeamsReceived?.Invoke(m); break;
                case BeamTeleported m: BeamTeleportedReceived?.Invoke(m); break;
                case BeamFx m: BeamFxReceived?.Invoke(m); break;
                case BaseList m: BasesReceived?.Invoke(m); break;
                case LandingPadList m: LandingPadsReceived?.Invoke(m); break;
                case ShipTransitFx m: ShipTransitReceived?.Invoke(m); break;
                case ChatMessage m: ChatReceived?.Invoke(m); break;
                case StarMapData m: StarMapReceived?.Invoke(m); break;
                case MissionList m: MissionsReceived?.Invoke(m); break;
                case MissionResult m: MissionResultReceived?.Invoke(m); break;
                case RespawnNotice m: RespawnNoticeReceived?.Invoke(m); break;
                case ServerRules m: ServerRulesReceived?.Invoke(m); break;
                case PlayerPresence m: PlayerPresenceReceived?.Invoke(m); break;
                case PlayerLeft m: PlayerLeftReceived?.Invoke(m); break;
                case PlayerFace m: PlayerFaceReceived?.Invoke(m); break;
                case OwnedShips m: OwnedShipsReceived?.Invoke(m); break;
                case WorldEnvironment m: WorldEnvironmentReceived?.Invoke(m); break;
                case WorldReset m: WorldResetReceived?.Invoke(m); break;
                case NpcList m: NpcsReceived?.Invoke(m); break;
                case DoorList m: DoorsReceived?.Invoke(m); break;
                case DataCubeList m: DataCubesReceived?.Invoke(m); break;
                case GameUnlocks m: GameUnlocksReceived?.Invoke(m); break;
                case MiningProgress m: MiningProgressReceived?.Invoke(m); break;
                case ScanResult m: ScanResultReceived?.Invoke(m); break;
                case WreckRepairStatus m: WreckRepairStatusChanged?.Invoke(m); break;
                case ShipRepairStatus m: ShipRepairStatusChanged?.Invoke(m); break;
                case TradeUpdate m: TradeUpdated?.Invoke(m); break;
                case TradeClosed m: TradeClosedReceived?.Invoke(m); break;
                case NpcGreeting m: NpcGreetingReceived?.Invoke(m); break;
                case ShipAiLine m: ShipAiLineReceived?.Invoke(m); break;
                case OreScanResult m: OreScanReceived?.Invoke(m); break;
                case AllianceList m: AllianceListReceived?.Invoke(m); break;
                case AllianceRequestNotice m: AllianceRequestReceived?.Invoke(m); break;
                case TameProgress m: TameProgressReceived?.Invoke(m); break;
                case TameResult m: TameResultReceived?.Invoke(m); break;
                case CompanionList m: CompanionsReceived?.Invoke(m); break;
                case StoryStateMessage m: StoryStateReceived?.Invoke(m); break;
                case NetFragmentList m: NetFragmentsReceived?.Invoke(m); break;
                case NetFragmentRevealed m: NetFragmentRevealedReceived?.Invoke(m); break;
                case PlayerMemoryRevealed m: PlayerMemoryReceived?.Invoke(m); break;
                case GuardianSystemRevealed m: GuardianSystemRevealedReceived?.Invoke(m); break;
                case CoreHackProgress m: CoreHackProgressReceived?.Invoke(m); break;
                case CoreDialogueMessage m: CoreDialogueReceived?.Invoke(m); break;
            }
        }

        public void Dispose()
        {
            _transport.Disconnect();
            _transport.Dispose();
        }
    }
}
