using BlocksBeyondTheStars.Networking.Messages;
using MessagePack;
using MessagePack.Resolvers;

namespace BlocksBeyondTheStars.Networking;

/// <summary>
/// Serializes/deserializes protocol messages to/from byte payloads. Each payload is a
/// one-byte message-type tag followed by a MessagePack (contractless) body, so message
/// classes need no serialization attributes and the format stays compact.
/// </summary>
public static class NetCodec
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    // Stable tag <-> type registry. Append new messages with new ids; never reuse ids.
    private static readonly Dictionary<byte, Type> TagToType = new();
    private static readonly Dictionary<Type, byte> TypeToTag = new();

    static NetCodec()
    {
        // Client -> Server
        Register(1, typeof(JoinRequest));
        Register(2, typeof(MoveIntent));
        Register(3, typeof(MineBlockIntent));
        Register(4, typeof(PlaceBlockIntent));
        Register(5, typeof(CraftIntent));
        Register(6, typeof(UnlockBlueprintIntent));
        Register(7, typeof(SelectHotbarIntent));
        Register(8, typeof(RequestStarMap));
        Register(9, typeof(AdminCommandIntent));
        Register(10, typeof(RequestMissions));
        Register(11, typeof(AcceptMissionIntent));
        Register(12, typeof(TurnInMissionIntent));
        Register(13, typeof(CreateMissionIntent));
        Register(14, typeof(DockRequestIntent));
        Register(15, typeof(DockResponseIntent));
        Register(16, typeof(UndockIntent));
        Register(17, typeof(BuildShipModuleIntent));
        Register(18, typeof(EnterSpaceIntent));
        Register(19, typeof(LeaveSpaceIntent));
        Register(20, typeof(FireWeaponIntent));
        Register(21, typeof(AttackEntityIntent));
        Register(22, typeof(UseStationIntent));
        Register(23, typeof(SetAppearanceIntent));
        Register(24, typeof(CraftShipIntent));
        Register(25, typeof(SwitchShipIntent));
        Register(26, typeof(ConsumeItemIntent));
        Register(27, typeof(LootContainerIntent));
        Register(28, typeof(ShipMoveIntent));
        Register(29, typeof(DisassembleIntent));
        Register(30, typeof(TradeRequestIntent));
        Register(31, typeof(TradeRespondIntent));
        Register(32, typeof(TradeOfferIntent));
        Register(33, typeof(TradeConfirmIntent));
        Register(34, typeof(TradeCancelIntent));
        Register(35, typeof(ScanIntent));
        Register(36, typeof(ScanEntityIntent));
        Register(37, typeof(LoadRationIntent));
        Register(38, typeof(TeleportToShipIntent));
        Register(39, typeof(ToggleStealthIntent));
        Register(40, typeof(BoardStationIntent));
        Register(41, typeof(LeaveStationIntent));
        Register(42, typeof(RepairWreckIntent));
        Register(43, typeof(ClaimWreckIntent));
        Register(44, typeof(TravelIntent));
        Register(45, typeof(SetJetpackIntent));
        Register(46, typeof(DoorInteractIntent));
        Register(47, typeof(FallDamageIntent));

        // Server -> Client
        Register(50, typeof(JoinAccepted));
        Register(51, typeof(JoinRejected));
        Register(52, typeof(ChunkDataMessage));
        Register(53, typeof(BlockChanged));
        Register(54, typeof(InventoryUpdate));
        Register(55, typeof(PlayerStateUpdate));
        Register(56, typeof(CraftResult));
        Register(57, typeof(ActionRejected));
        Register(58, typeof(ServerMessage));
        Register(59, typeof(ServerRules));
        Register(60, typeof(RespawnNotice));
        Register(61, typeof(StarMapData));
        Register(62, typeof(MissionList));
        Register(63, typeof(MissionResult));
        Register(64, typeof(DockRequestNotice));
        Register(65, typeof(DockStatus));
        Register(66, typeof(ShipCombatStatus));
        Register(67, typeof(SpaceState));
        Register(68, typeof(SpaceEntityDestroyed));
        Register(69, typeof(SpaceClosed));
        Register(70, typeof(PlanetEnemyList));
        Register(71, typeof(PlanetEnemyDefeated));
        Register(72, typeof(ShipPlacement));
        Register(73, typeof(ShipStations));
        Register(74, typeof(PlayerPresence));
        Register(75, typeof(PlayerLeft));
        Register(76, typeof(OwnedShips));
        Register(77, typeof(WorldEnvironment));
        Register(78, typeof(CreatureList));
        Register(79, typeof(ContainerList));
        Register(80, typeof(TradeUpdate));
        Register(81, typeof(TradeClosed));
        Register(82, typeof(ScanResult));
        Register(83, typeof(StationBoarded));
        Register(84, typeof(NpcList));
        Register(85, typeof(WreckRepairStatus));
        Register(86, typeof(WorldReset));
        Register(87, typeof(MiningProgress));
        Register(88, typeof(PlanetPoiList));
        Register(89, typeof(ChatIntent));
        Register(90, typeof(ChatMessage));
        Register(91, typeof(SaveGameIntent));
        Register(92, typeof(TractorPullIntent));
        Register(93, typeof(DoorList));

        // Client -> Server (space EVA / ship-interior intents — append-only, never reuse ids).
        Register(94, typeof(SetEvaIntent));
        Register(95, typeof(EnterShipIntent));
        Register(96, typeof(ExitShipIntent));

        // Client -> Server (item 11: knowledge trading).
        Register(97, typeof(TradeKnowledgeIntent));

        // Client -> Server (Task 5 Stage 3b: storage crate deposit).
        Register(98, typeof(DepositContainerIntent));

        // Client -> Server (item 36: right-click gadgets — field medkit / stasis projector / terrain blaster).
        Register(99, typeof(UseGadgetIntent));

        // Server -> Client (item 37: placed radio beacons — labelled map/compass waypoints).
        Register(100, typeof(BeaconList));

        // Client -> Server (item 37: rename a beacon you own).
        Register(101, typeof(SetBeaconLabelIntent));

        // item 38: fixed landing pads — client asks for a body's pads + occupancy, server replies with the list.
        Register(102, typeof(RequestLandingPadsIntent));
        Register(103, typeof(LandingPadList));

        // item 38: another player's ship landing/launching at a pad (other players on the body see the animation).
        Register(104, typeof(ShipTransitFx));

        // item 20 S1: the player's own ship as a voxel structure for the flight view (replaces the cube model).
        Register(105, typeof(SpaceShipDesign));

        // item 20 S2: free-space EVA build/mine on a voxel structure (client intent + server broadcast).
        Register(106, typeof(StructureEditIntent));
        Register(107, typeof(StructureBlockChanged));

        // item 20 S4: deploy a station core to start a player-built station.
        Register(108, typeof(DeployStationCoreIntent));

        // item 15: contextual NPC greetings (client asks on interaction; server replies with the line).
        Register(109, typeof(NpcGreetIntent));      // Client -> Server
        Register(110, typeof(NpcGreeting));         // Server -> Client

        // B58: customisable quick-bar — client swaps two personal-inventory slots.
        Register(111, typeof(MoveItemIntent));      // Client -> Server

        // Feature 40: terrain-scanner pulse result (ore positions for the through-wall glow markers).
        Register(112, typeof(OreScanResult));       // Server -> Client

        // Ship AI companion "VEGA" (onboarding/advisor/story lines + objective chip).
        Register(113, typeof(ShipAiLine));          // Server -> Client
        Register(114, typeof(SkipOnboardingIntent)); // Client -> Server

        // World options: live admin edit of the gameplay rules (creatures + enemy activities).
        Register(115, typeof(SetWorldRulesIntent)); // Client -> Server

        // Ship-as-object: a parked ship on a world as a placed voxel structure (place/replace/remove).
        Register(116, typeof(LandedShipState));     // Server -> Client

        // Travel screen: hyperjump into a (possibly unvisited) star system, arriving in flight mode there.
        Register(117, typeof(HyperjumpSystemIntent)); // Client -> Server

        // Data-cube minigames: cubes scattered on bodies grant bundled minigames into a per-player collection.
        Register(118, typeof(DataCubeList));    // Server -> Client (cubes to render on the current world)
        Register(119, typeof(UnlockGameIntent)); // Client -> Server (download the cube I'm standing at)
        Register(120, typeof(GameUnlocks));      // Server -> Client (my full downloaded-games collection)
        Register(121, typeof(MinigameResultIntent)); // Client -> Server (a minigame run finished → knowledge reward)

        // Planet bases (Grundstein): a player founds a named base by placing a base_core block on a body.
        Register(122, typeof(BaseList));            // Server -> Client (bases on the current world for the planet map)
        Register(123, typeof(SetBaseNameIntent));   // Client -> Server (name/rename my base on a body)

        // Rename a commissioned space station you built (travel screen / station core).
        Register(124, typeof(SetStationNameIntent)); // Client -> Server

        // Always-available "Dye"/"Glow" crafting: recolour a held building material (surface tint and/or
        // a coloured light source). Output is the same item with the colour encoded in its key.
        Register(125, typeof(TintCraftIntent)); // Client -> Server

        // Player alliances: two players co-own each other's stations + bases and can't harm one another.
        Register(126, typeof(RequestAllianceListIntent)); // Client -> Server (open the Alliances tab)
        Register(127, typeof(RequestAllianceIntent));     // Client -> Server (propose an alliance)
        Register(128, typeof(AllianceResponseIntent));    // Client -> Server (accept/decline a request)
        Register(129, typeof(DissolveAllianceIntent));    // Client -> Server (end an alliance)
        Register(130, typeof(AllianceList));              // Server -> Client (my full roster + pending)
        Register(131, typeof(AllianceRequestNotice));     // Server -> Client (someone proposed an alliance)

        // Custom pixel face: a per-player avatar face drawn in the in-game editor, relayed out of band from
        // the 10 Hz presence stream (heavier payload, changes rarely).
        Register(132, typeof(SetFaceIntent)); // Client -> Server (set/clear my face)
        Register(133, typeof(PlayerFace));    // Server -> Client (another player's face)

        // Beam blocks (teleporter pads): craftable, named pads that beam the player to their own/allied pads
        // on the same world. Like beacons, the block is a real voxel; these messages carry the metadata + jump.
        Register(134, typeof(BeamList));          // Server -> Client (beam blocks on the current world)
        Register(135, typeof(SetBeamNameIntent)); // Client -> Server (name/rename a beam block I own)
        Register(136, typeof(BeamTeleportIntent)); // Client -> Server (beam from the pad I'm at to a chosen pad)
        Register(137, typeof(BeamTeleported));    // Server -> Client (my arrival position — snap + arrival fx)
        Register(138, typeof(BeamFx));            // Server -> Client (beam column VFX at both pads, for everyone)

        // Story system (pluggable story packs): the active story's shared per-save progress + narrator beats.
        // Beats arrive on the existing ShipAiLine channel; this carries the aggregate meter/state.
        Register(139, typeof(StoryStateMessage)); // Server -> Client (active story progress + flags)
        Register(147, typeof(StorySelectIntent)); // Client -> Server (admin: choose the active story / "none")

        // Net fragments: text-only story finds scattered in the world (surface, datacube-style, + structures).
        Register(140, typeof(NetFragmentFoundIntent)); // Client -> Server (pick up the fragment I'm standing at)
        Register(141, typeof(NetFragmentRevealed));    // Server -> Client (the picked-up fragment's archive text)
        Register(142, typeof(PlayerMemoryRevealed));   // Server -> Client (a personal memory unlocked by a machine kill)
        Register(148, typeof(NetFragmentList));        // Server -> Client (net fragments on the current world)

        // Finale (P6): Guardian-system reveal → core hack (channel) → argument duel (defeat by contradiction).
        Register(143, typeof(GuardianSystemRevealed));   // Server -> Client (finale system placed on the map)
        Register(144, typeof(CoreDialogueMessage));      // Server -> Client (current duel node: prompt + choices)
        Register(145, typeof(CoreDialogueChoiceIntent)); // Client -> Server (the player's rebuttal pick)
        Register(146, typeof(CoreHackIntent));           // Client -> Server (channel the core hack one tick)
        Register(149, typeof(CoreHackProgress));         // Server -> Client (core-hack channel progress)

        // Peaceful NPC trader traffic: a localized warp-in/out flash so other players see traders arrive/leave.
        Register(150, typeof(SpaceWarpFx));              // Server -> Client

        // Own-ship repair (hull stat + missing design voxel cells): cockpit "Repair ship" + guided field/EVA fill.
        Register(151, typeof(RepairShipIntent));         // Client -> Server
        Register(152, typeof(ShipRepairStatus));         // Server -> Client

        // Creature taming + companions (design: docs/developer/CREATURE_TAMING.md). The translator gadget starts the
        // ritual via the existing UseGadgetIntent; these carry the responses, progress + companion roster.
        Register(153, typeof(TameRespondIntent));        // Client -> Server (a response in the taming ritual)
        Register(154, typeof(TameProgress));             // Server -> Client (decoded mood + need + trust)
        Register(155, typeof(TameResult));               // Server -> Client (attempt finished)
        Register(156, typeof(RequestCompanionsIntent));  // Client -> Server (open the Companions tab)
        Register(157, typeof(CompanionList));            // Server -> Client (my companion roster)
        Register(158, typeof(SetCompanionNameIntent));   // Client -> Server (rename a companion)
        Register(159, typeof(ReleaseCompanionIntent));   // Client -> Server (release a companion)

        // Always-available "Shape" crafting: re-form a held building material into another geometric shape
        // (sphere/dome/pyramid/ramp/…). Output is the same item with the shape encoded in its key.
        Register(160, typeof(ShapeCraftIntent));         // Client -> Server

        // Hover speeders (craftable single-seat surface vehicles): deployed from the speeder item, driven over
        // the surface, refuellable + destructible. Deploy reuses UseGadgetIntent; these carry state + actions.
        Register(161, typeof(SpeederList));              // Server -> Client (speeders on the current world)
        Register(162, typeof(EnterSpeederIntent));       // Client -> Server (board a parked speeder)
        Register(163, typeof(ExitSpeederIntent));        // Client -> Server (dismount)
        Register(164, typeof(StowSpeederIntent));        // Client -> Server (pack a speeder back into the item)
        Register(165, typeof(RefuelSpeederIntent));      // Client -> Server (refuel from an energy cell)
        Register(166, typeof(SpeederImpactIntent));      // Client -> Server (hard collision → server-side damage)
        Register(167, typeof(SpeederFx));                // Server -> Client (deploy shimmer / destruction burst)

        // /bump bug report carrying a screenshot (client -> server). The text-only /bump still arrives as a
        // ChatIntent the server intercepts; this variant additionally ships a JPG screenshot.
        Register(168, typeof(BumpReport));               // Client -> Server (bug report + optional screenshot)
    }

    private static void Register(byte tag, Type type)
    {
        TagToType[tag] = type;
        TypeToTag[type] = tag;
    }

    public static byte[] Encode(object message)
    {
        var type = message.GetType();
        if (!TypeToTag.TryGetValue(type, out var tag))
        {
            throw new InvalidOperationException($"Message type '{type.Name}' is not registered with NetCodec.");
        }

        var body = MessagePackSerializer.Serialize(type, message, Options);
        var payload = new byte[body.Length + 1];
        payload[0] = tag;
        Buffer.BlockCopy(body, 0, payload, 1, body.Length);
        return payload;
    }

    /// <summary>Decodes a payload into a message object, or null if the tag is unknown/empty or the body is
    /// malformed. A corrupt/truncated/maliciously-shaped body must never throw out to the caller — a single
    /// bad packet would otherwise crash the single-threaded server tick (DoS); we swallow it and return null
    /// so the caller can drop the message.</summary>
    public static object? Decode(byte[] payload)
    {
        if (payload.Length == 0 || !TagToType.TryGetValue(payload[0], out var type))
        {
            return null;
        }

        try
        {
            var body = new ReadOnlyMemory<byte>(payload, 1, payload.Length - 1);
            return MessagePackSerializer.Deserialize(type, body, Options);
        }
        catch (MessagePackSerializationException)
        {
            return null; // corrupt/truncated body for this tag — drop it
        }
    }
}
