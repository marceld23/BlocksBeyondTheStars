// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;
using System.Text.Json;
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
    private const byte JsonEnvelopeTag = 255;
    public const int MaxJsonPayloadBytes = 1024 * 1024;

    /// <summary>Hard cap on a single decoded packet (native MessagePack path). The WebSocket path is already
    /// bounded by <see cref="MaxJsonPayloadBytes"/>; native (LiteNetLib) reliable fragmentation can assemble
    /// far larger buffers, so an equivalent ceiling is enforced here before deserialization. No legitimate
    /// client intent is anywhere near 1 MB.</summary>
    public const int MaxPacketBytes = 1024 * 1024;
    private const int MaxJsonDepth = 64;

    // UntrustedData (#424 S10): every decoded payload is attacker-controlled, so deserialization must be
    // depth-limited (a ~5-byte header can declare an absurdly nested/huge structure inside the 1 MB cap)
    // and allocation-clamped. Security options only affect reading — the encode path is unchanged.
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance)
            .WithSecurity(MessagePackSecurity.UntrustedData);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = MaxJsonDepth,
    };

    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        MaxDepth = MaxJsonDepth,
    };

    /// <summary>
    /// WebGL/IL2CPP cannot rely on MessagePack's contractless runtime formatter path.
    /// Browser transports set this flag so their outbound payloads use the JSON envelope below; native
    /// clients and the server keep the compact MessagePack binary protocol.
    /// </summary>
    public static bool UseJsonEncoding { get; set; }

    // Stable tag <-> type registry. Append new messages with new ids; never reuse ids.
    private static readonly Dictionary<byte, Type> TagToType = new();
    private static readonly Dictionary<Type, byte> TypeToTag = new();

    internal static IReadOnlyDictionary<byte, Type> RegisteredMessages => TagToType;

    internal static IReadOnlyDictionary<Type, byte> RegisteredMessageTags => TypeToTag;

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
        Register(48, typeof(ShootBlockIntent));

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

        // Live voice chat: one Opus frame per ~20 ms. Same class both ways — client uploads the speaker's
        // microphone; the server stamps FromPlayerId and relays the opaque bytes to the sender's tiered radio
        // audience (same world / system / galaxy, like text chat). Sent Unreliable.
        Register(169, typeof(VoiceFrame));               // Client <-> Server

        // Cargo hold: move items between the personal inventory and the ship's cargo hold (per-item or bulk).
        Register(170, typeof(MoveCargoItemIntent));      // Client -> Server

        // Flora regrow cue: a harvested plant has started regrowing — the client shows the spawn source as a
        // sprout that grows in until the plant returns (purely cosmetic; the plant pops back regardless).
        Register(171, typeof(FloraRegrowStarted));       // Server -> Client

        // Factories: rare industrial halls with animated machines + a production terminal. The client renders
        // the moving machines and opens the roster-filtered production UI at the terminal.
        Register(172, typeof(FactoryList));              // Server -> Client (factories to render on this world)
        Register(173, typeof(ClaimStructureIntent));     // Client -> Server (claim a factory with an access code)

        // Maintenance announcements: operator/admin messages ("server restarts in 10 minutes") rendered as a
        // prominent banner/modal instead of the low-key ServerMessage toast. Restart countdowns re-broadcast at
        // shrinking thresholds so late joiners and drifted clients stay in sync.
        Register(174, typeof(MaintenanceNotice));        // Server -> Client

        // Base/station home spawn (issues #461/#462): E on a placed heal tank stores a body-qualified
        // custom spawn point; on death the server offers a choice between the ship and that spawn.
        Register(175, typeof(SetSpawnPointIntent));      // Client -> Server (E on a placed heal tank)
        Register(176, typeof(RespawnOptions));           // Server -> Client (death: pick ship vs home spawn)
        Register(177, typeof(RespawnChoiceIntent));      // Client -> Server (the pick)

        // Codex "Discoveries" (#484): the first-scan ledger, so a scan leaves a permanent record.
        Register(178, typeof(DiscoveryLog));             // Server -> Client (join snapshot + per-scan delta)

        // Bandits: lone robbers on foot and bandit ships in space hail the player and demand part of
        // their goods; comply and they leave, refuse (or ignore the deadline) and they turn hostile.
        Register(179, typeof(BanditDemand));             // Server -> Client (the hold-up, with countdown)
        Register(180, typeof(BanditResponseIntent));     // Client -> Server (hand it over / refuse)
        Register(181, typeof(BanditEncounterResult));    // Server -> Client (paid/refused/expired/fled)

        // Throwing unwanted loot away (#599) — the only path that destroys an item instead of storing it.
        Register(182, typeof(DiscardItemIntent));        // Client -> Server

        // Singleplayer pause: the Esc menu really holds the world now. Server-side because singleplayer runs
        // the bundled server in its own process — a client-only freeze would stop the camera, not the world.
        Register(183, typeof(PauseIntent));              // Client -> Server (menu opened / closed)
        Register(184, typeof(PauseState));               // Server -> Client (holding? and was it allowed?)

        // Achievements with rewards and live progress ("Baue 5 Eisen ap" — a player's own example).
        Register(185, typeof(AchievementList));          // Server -> Client (join + whenever progress moves)
        Register(186, typeof(AchievementUnlocked));      // Server -> Client (celebrate this one)
        Register(187, typeof(AchievementRewardDeferred));// Server -> Client (earned, but make room for the reward)

        // #685: EVA asteroid mining obeys hardness — per-cell progress on a voxel structure.
        Register(188, typeof(StructureMiningProgress));  // Server -> Client

        // VEGA tips log (#737): the player's vega:* milestones on join — the client rebuilds the
        // re-readable "VEGA tips" section of the ship terminal's Story tab from them.
        Register(189, typeof(VegaJournal));              // Server -> Client

        // Sit on chairs (#806): pose flag mirrored into the presence broadcast.
        Register(190, typeof(SetSeatedIntent));          // Client -> Server

        // Player-painted block designs (#817): the bitmap travels once per design; painted blocks
        // reference it via the shape descriptor's design bits through the normal BlockChanged path.
        Register(191, typeof(PaintBlockIntent));         // Client -> Server (paint/clear a block)
        Register(192, typeof(PaintDesignData));          // Server -> Client (one design; empty = wiped)
        Register(193, typeof(PaintDesignList));          // Server -> Client (all designs, on join)

        // Player-designed block forms (#843): craft intent + the registry push, mirroring the paint trio.
        Register(194, typeof(CustomShapeCraftIntent));   // Client -> Server (craft a form from a material)
        Register(195, typeof(CustomShapeData));          // Server -> Client (one form; empty = wiped)
        Register(196, typeof(CustomShapeList));          // Server -> Client (all forms, on join)

        // Ground drop packets (#853): what a full inventory leaves lying on the ground. Pickup is automatic
        // (server-side proximity), so there is no client intent — only the world list.
        Register(197, typeof(DropPacketList));           // Server -> Client

        // Avatar body paint (#874): per-part pixel paintings (torso/arms/legs/helmet), sibling of the face.
        Register(198, typeof(SetBodyPaintIntent));       // Client -> Server (set/clear one part's painting)
        Register(199, typeof(PlayerBodyPaint));          // Server -> Client (another player's painting)

        // Weather forecast gadget (#900): the sky's coming episodes, so weather can be planned around.
        Register(200, typeof(WeatherForecastRequest));   // Client -> Server
        Register(201, typeof(WeatherForecast));          // Server -> Client

        // Hotbar slot actions
        Register(202, typeof(PaintCraftIntent));         // Client -> Server (own texture onto a held material)

        // Player-to-player trade handshake (#981): the invitation needs its own message so the target can
        // answer it — a chat line alone left TradeRespondIntent with no sender in the whole client.
        Register(203, typeof(TradeRequestNotice));       // Server -> Client (someone nearby wants to trade)

        // Per-crate stash filter (#1032): the player decides what belongs in a container. The filter itself
        // travels back on NetContainer.Filter — no extra server->client message needed.
        Register(204, typeof(SetContainerFilterIntent)); // Client -> Server

        // Suit teleporter destination picker (#1056): beam to an allied player on the same body. The recall
        // to the ship stays TeleportToShipIntent (38); a server without this tag simply drops the message.
        Register(205, typeof(TeleportToPlayerIntent));   // Client -> Server

        // Station affordances (#1070/#1072): the server tells the client which crafting stations are in
        // reach (single source of truth for the Tab-menu gates) and answers "where is the nearest one?".
        Register(206, typeof(StationsInReach));          // Server -> Client
        Register(207, typeof(LocateStationIntent));      // Client -> Server
        Register(208, typeof(StationLocation));          // Server -> Client

        // Suit lamp state (#1077): lets VEGA's context tips know the lamp is off in the dark. Informational
        // only — a server without this tag drops the message and simply never gives that tip.
        Register(209, typeof(SetLampIntent));            // Client -> Server

        // Environmental lore texts (#1111): a rune inscription / wreck log / ruin note found at a site.
        Register(210, typeof(LoreTextRevealed));         // Server -> Client

        // Persisted exploration (#1113): the receiver's explored-map cells for the body just arrived on,
        // so the planet map's fog stays lifted across sessions.
        Register(211, typeof(ExploredMapData));          // Server -> Client

        // Whole-build share codes (#1117): copy a region to a BBTS1-B code, paste it back block by block.
        Register(212, typeof(CopyBuildIntent));          // Client -> Server
        Register(213, typeof(BuildCodeResult));          // Server -> Client
        Register(214, typeof(PasteBuildIntent));         // Client -> Server
        Register(215, typeof(BuildPasteResult));         // Server -> Client

        // Living NPCs (#1118/#1119): per-player relationship stages for nameplates, the "People you know"
        // roster, and the NPC radio-call preference.
        Register(216, typeof(NpcStandingList));          // Server -> Client
        Register(217, typeof(RequestKnownNpcsIntent));   // Client -> Server
        Register(218, typeof(KnownNpcList));             // Server -> Client
        Register(219, typeof(SetNpcCallsIntent));        // Client -> Server

        // Story resolution cinematic (#1124): the ending as a shown thing — resolution, credits, epilogue.
        Register(220, typeof(StoryResolved));                // Server -> Client
        Register(221, typeof(RequestStoryResolutionIntent)); // Client -> Server
    }

    private static void Register(byte tag, Type type)
    {
        if (TagToType.TryGetValue(tag, out var existingType))
            throw new InvalidOperationException(
                $"NetCodec tag {tag} is already registered to {existingType.FullName}");

        if (TypeToTag.TryGetValue(type, out var existingTag))
            throw new InvalidOperationException(
                $"NetCodec type {type.FullName} is already registered to tag {existingTag}");

        TagToType[tag] = type;
        TypeToTag[type] = tag;
    }
    public static byte[] Encode(object message)
        => UseJsonEncoding ? EncodeJson(message) : EncodeMessagePack(message);

    /// <summary>Whether a payload carries <typeparamref name="T"/>, WITHOUT deserializing its body — the type
    /// tag alone answers it. Lets a caller triage a queue cheaply (the client's receive budget, #963, caps
    /// expensive chunk messages separately) instead of decoding every payload twice.</summary>
    public static bool IsMessageType<T>(byte[] payload)
    {
        if (payload is null || payload.Length == 0 || !TypeToTag.TryGetValue(typeof(T), out var want))
        {
            return false;
        }

        if (payload[0] != JsonEnvelopeTag)
        {
            return payload[0] == want;
        }

        // Browser JSON envelope: the tag is the first field, written as {"tag":N,"body":...} by EncodeJson.
        // Read those few ASCII digits rather than parsing the (potentially chunk-sized) document.
        const string prefix = "{\"tag\":";
        if (payload.Length < 1 + prefix.Length + 1)
        {
            return false;
        }

        for (int i = 0; i < prefix.Length; i++)
        {
            if (payload[1 + i] != (byte)prefix[i])
            {
                return false;
            }
        }

        int value = 0;
        for (int i = 1 + prefix.Length; i < payload.Length && i < 1 + prefix.Length + 3; i++)
        {
            byte c = payload[i];
            if (c < (byte)'0' || c > (byte)'9')
            {
                return i > 1 + prefix.Length && value == want;
            }

            value = (value * 10) + (c - (byte)'0');
        }

        return value == want;
    }

    private static byte[] EncodeMessagePack(object message)
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

    /// <summary>Encodes a tagged JSON payload for browser WebSocket clients.</summary>
    public static byte[] EncodeJson(object message)
    {
        var type = message.GetType();
        if (!TypeToTag.TryGetValue(type, out var tag))
        {
            throw new InvalidOperationException($"Message type '{type.Name}' is not registered with NetCodec.");
        }

        string body = JsonSerializer.Serialize(message, type, JsonOptions);
        string envelope = "{\"tag\":" + tag + ",\"body\":" + body + "}";
        byte[] json = Encoding.UTF8.GetBytes(envelope);
        var payload = new byte[json.Length + 1];
        payload[0] = JsonEnvelopeTag;
        Buffer.BlockCopy(json, 0, payload, 1, json.Length);
        return payload;
    }

    /// <summary>
    /// Converts an already-encoded NetCodec payload to the browser JSON envelope. Used by the WebSocket server
    /// transport so the authoritative server can keep its normal MessagePack send path internally.
    /// </summary>
    public static bool TryConvertToJsonPayload(byte[] payload, out byte[] jsonPayload)
    {
        var message = Decode(payload);
        if (message == null)
        {
            jsonPayload = Array.Empty<byte>();
            return false;
        }

        jsonPayload = EncodeJson(message);
        return true;
    }

    /// <summary>Decodes a payload into a message object, or null if the tag is unknown/empty or the body is
    /// malformed. A corrupt/truncated/maliciously-shaped body must never throw out to the caller — a single
    /// bad packet would otherwise crash the single-threaded server tick (DoS); we swallow it and return null
    /// so the caller can drop the message.</summary>
    public static object? Decode(byte[] payload)
    {
        if (payload.Length > 0 && payload[0] == JsonEnvelopeTag)
        {
            return DecodeJson(payload);
        }

        if (payload.Length == 0 || payload.Length > MaxPacketBytes || !TagToType.TryGetValue(payload[0], out var type))
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

    private static object? DecodeJson(byte[] payload)
    {
        if (payload.Length <= 1 || payload.Length > MaxJsonPayloadBytes)
        {
            return null;
        }

        try
        {
            string json = Encoding.UTF8.GetString(payload, 1, payload.Length - 1);
            using var doc = JsonDocument.Parse(json, JsonDocumentOptions);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag", out var tagElement)
                || !tagElement.TryGetInt32(out int tag)
                || tag < 0
                || tag > byte.MaxValue)
            {
                return null;
            }

            if (!TagToType.TryGetValue((byte)tag, out var type)
                || !root.TryGetProperty("body", out var bodyElement))
            {
                return null;
            }

            return JsonSerializer.Deserialize(bodyElement.GetRawText(), type, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
