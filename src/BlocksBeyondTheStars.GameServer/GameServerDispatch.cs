// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The intent → handler table behind <c>Dispatch</c> (#2894): every client intent the server acts on, registered once
/// per process. <c>Dispatch</c> runs the observer, pause and guest gates first and then looks the message's exact type
/// up here; a type without an entry is dropped, as the former type-pattern <c>switch</c> did.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>
    /// Intent type → handler, built once per process (no reflection, nothing discovered at runtime, so managed
    /// stripping in the Unity/WebGL flavour cannot remove a handler). The lookup is by the message's exact type, which
    /// equals the former type-pattern switch because every registered message class is sealed and none derives from
    /// another. The observer, pause and guest gates stay in <c>Dispatch</c>, before the lookup.
    /// </summary>
    private static readonly Dictionary<Type, Action<GameServer, PlayerSession, object>> IntentHandlers = BuildIntentHandlers();

    /// <summary>Registers one typed handler. <c>Add</c>, not the indexer: a type registered twice throws at type
    /// initialisation instead of silently replacing the first handler.</summary>
    private static void On<T>(Dictionary<Type, Action<GameServer, PlayerSession, object>> map, Action<GameServer, PlayerSession, T> handler)
        where T : class
        => map.Add(typeof(T), (server, session, message) => handler(server, session, (T)message));

    /// <summary>One registration per handled intent, in the order the switch listed them.</summary>
    private static Dictionary<Type, Action<GameServer, PlayerSession, object>> BuildIntentHandlers()
    {
        var map = new Dictionary<Type, Action<GameServer, PlayerSession, object>>(160);
        On<MoveIntent>(map, (gs, s, m) => gs.HandleMove(s, m));
        On<FarTerrainTileRequest>(map, (gs, s, m) => gs.HandleFarTerrainTileRequest(s, m)); // #1821
        On<SelectHotbarIntent>(map, (gs, s, m) => gs.HandleSelectHotbar(s, m));
        On<MoveItemIntent>(map, (gs, s, m) => gs.HandleMoveItem(s, m));
        On<EquipItemIntent>(map, (gs, s, m) => gs.HandleEquipItem(s, m)); // #2110
        On<UnequipItemIntent>(map, (gs, s, m) => gs.HandleUnequipItem(s, m)); // #2110
        On<DiscardItemIntent>(map, (gs, s, m) => gs.HandleDiscardItem(s, m));
        On<MineBlockIntent>(map, (gs, s, m) => gs.HandleMine(s, m));
        On<PlaceBlockIntent>(map, (gs, s, m) => gs.HandlePlace(s, m));
        On<CraftIntent>(map, (gs, s, m) => gs.HandleCraft(s, m));
        On<TintCraftIntent>(map, (gs, s, m) => gs.HandleTintCraft(s, m));
        On<ShapeCraftIntent>(map, (gs, s, m) => gs.HandleShapeCraft(s, m));
        On<UnlockBlueprintIntent>(map, (gs, s, m) => gs.HandleUnlock(s, m));
        On<ChatIntent>(map, (gs, s, m) => gs.HandleChat(s, m));
        On<VoiceFrame>(map, (gs, s, m) => gs.HandleVoice(s, m));
        On<BumpReport>(map, (gs, s, m) => gs.HandleBumpReport(s, m));
        On<RequestStarMap>(map, (gs, s, _) => gs.SendStarMap(s));
        On<SaveGameIntent>(map, (gs, s, _) => gs.HandleSaveGame(s));
        On<TractorPullIntent>(map, (gs, s, m) => gs.HandleTractorPull(s, m));
        On<DoorInteractIntent>(map, (gs, s, m) => gs.HandleDoorInteract(s, m));
        On<SetSpawnPointIntent>(map, (gs, s, m) => gs.HandleSetSpawnPoint(s, m));
        On<PauseIntent>(map, (gs, s, m) => gs.HandlePause(s, m));
        On<RespawnChoiceIntent>(map, (gs, s, m) => gs.HandleRespawnChoice(s, m));
        On<UnlockGameIntent>(map, (gs, s, m) => gs.HandleUnlockGame(s, m));
        On<MinigameResultIntent>(map, (gs, s, m) => gs.HandleMinigameResult(s, m));
        On<FallDamageIntent>(map, (gs, s, m) => gs.HandleFallDamage(s, m));
        On<AdminCommandIntent>(map, (gs, s, m) => gs.HandleAdminCommand(s, m));
        On<RequestMissions>(map, (gs, s, _) => gs.SendMissionList(s));
        On<AcceptMissionIntent>(map, (gs, s, m) => gs.HandleAcceptMission(s, m.MissionId));
        On<TurnInMissionIntent>(map, (gs, s, m) => gs.HandleTurnInMission(s, m.MissionId));
        On<CreateMissionIntent>(map, (gs, s, m) => gs.HandleCreateMission(s, m));
        On<DockRequestIntent>(map, (gs, s, m) => gs.HandleDockRequest(s, m));
        On<DockResponseIntent>(map, (gs, s, m) => gs.HandleDockResponse(s, m));
        On<UndockIntent>(map, (gs, s, _) => gs.HandleUndock(s));
        On<VisitShipIntent>(map, (gs, s, m) => gs.HandleVisitShip(s, m)); // #2723: walk over into the docked friend's ship
        On<BuildShipModuleIntent>(map, (gs, s, m) => gs.HandleBuildModule(s, m));
        On<UninstallShipModuleIntent>(map, (gs, s, m) => gs.HandleUninstallModule(s, m));
        On<EnterSpaceIntent>(map, (gs, s, _) => gs.HandleEnterSpace(s));
        On<HyperjumpSystemIntent>(map, (gs, s, m) => gs.HandleHyperjumpSystem(s, m));
        On<ContributeRelayIntent>(map, (gs, s, m) => gs.HandleContributeRelay(s, m)); // #1125
        On<TalkToNpcIntent>(map, (gs, s, m) => gs.HandleTalkToNpc(s, m)); // #1127
        On<NpcDialogChoiceIntent>(map, (gs, s, m) => gs.HandleNpcDialogChoice(s, m)); // #1127
        On<EnterShipIntent>(map, (gs, s, _) => gs.EnterShipInterior(s.State.PlayerId));
        On<ExitShipIntent>(map, (gs, s, _) => gs.ExitShipToFlight(s.State.PlayerId));
        On<LeaveSpaceIntent>(map, (gs, s, m) => gs.HandleLeaveSpace(s, m));
        On<FireWeaponIntent>(map, (gs, s, m) => gs.HandleFireWeapon(s, m));
        On<AttackEntityIntent>(map, (gs, s, m) => gs.HandleAttackEntity(s, m));
        On<ShootBlockIntent>(map, (gs, s, m) => gs.HandleShootBlock(s, m));
        On<UseStationIntent>(map, (gs, s, m) => gs.HandleUseStation(s, m));
        On<LocateStationIntent>(map, (gs, s, m) => gs.HandleLocateStation(s, m)); // #1072
        On<SetAppearanceIntent>(map, (gs, s, m) => gs.HandleSetAppearance(s, m));
        On<SetFaceIntent>(map, (gs, s, m) => gs.HandleSetFace(s, m));
        On<SetBodyPaintIntent>(map, (gs, s, m) => gs.HandleSetBodyPaint(s, m));
        On<SetToolLookIntent>(map, (gs, s, m) => gs.HandleSetToolLook(s, m));
        On<PaintBlockIntent>(map, (gs, s, m) => gs.HandlePaintBlock(s, m));
        On<PaintCraftIntent>(map, (gs, s, m) => gs.HandlePaintCraft(s, m));
        On<CustomShapeCraftIntent>(map, (gs, s, m) => gs.HandleCustomShapeCraft(s, m));
        On<PublishWorldTextureIntent>(map, (gs, s, m) => gs.HandlePublishWorldTexture(s, m));
        On<RemoveWorldTextureIntent>(map, (gs, s, m) => gs.HandleRemoveWorldTexture(s, m));
        On<CraftShipIntent>(map, (gs, s, m) => gs.HandleCraftShip(s, m));
        On<SwitchShipIntent>(map, (gs, s, m) => gs.HandleSwitchShip(s, m));
        On<ConsumeItemIntent>(map, (gs, s, m) => gs.HandleConsume(s, m));
        On<ThrowFoodIntent>(map, (gs, s, m) => gs.HandleThrowFood(s, m)); // #2018: the Feed action
        On<UseGadgetIntent>(map, (gs, s, m) => gs.HandleUseGadget(s, m));
        On<BioLabIntent>(map, (gs, s, m) => gs.HandleBioLab(s, m)); // #2203
        On<DiscardSampleIntent>(map, (gs, s, m) => gs.HandleDiscardSample(s, m)); // #2301
        On<TameRespondIntent>(map, (gs, s, m) => gs.HandleTameRespond(s, m));
        On<BanditResponseIntent>(map, (gs, s, m) => gs.HandleBanditResponse(s, m));
        On<RequestCompanionsIntent>(map, (gs, s, _) => gs.HandleRequestCompanions(s));
        On<SetCompanionNameIntent>(map, (gs, s, m) => gs.HandleSetCompanionName(s, m));
        On<ReleaseCompanionIntent>(map, (gs, s, m) => gs.HandleReleaseCompanion(s, m));
        On<FeedCompanionIntent>(map, (gs, s, m) => gs.HandleFeedCompanion(s, m));
        On<EnterSpeederIntent>(map, (gs, s, m) => gs.HandleEnterSpeeder(s, m));
        On<ExitSpeederIntent>(map, (gs, s, _) => gs.HandleExitSpeeder(s));
        On<StowSpeederIntent>(map, (gs, s, m) => gs.HandleStowSpeeder(s, m));
        On<RefuelSpeederIntent>(map, (gs, s, m) => gs.HandleRefuelSpeeder(s, m));
        On<SpeederImpactIntent>(map, (gs, s, m) => gs.HandleSpeederImpact(s, m));
        On<RecallVehicleIntent>(map, (gs, s, m) => gs.HandleRecallVehicle(s, m));
        On<EnterTrainIntent>(map, (gs, s, m) => gs.HandleEnterTrain(s, m)); // #2113
        On<ExitTrainIntent>(map, (gs, s, _) => gs.HandleExitTrain(s));
        On<SetTrainIntent>(map, (gs, s, m) => gs.HandleSetTrain(s, m));
        On<StowTrainIntent>(map, (gs, s, m) => gs.HandleStowTrain(s, m));
        On<PlanetScanIntent>(map, (gs, s, m) => gs.HandlePlanetScan(s, m));
        On<FxIntent>(map, (gs, s, m) => gs.HandleFxIntent(s, m)); // #2158: cosmetic, relayed as ActionFx
        On<SetBeaconLabelIntent>(map, (gs, s, m) => gs.HandleSetBeaconLabel(s, m));
        On<SetBeamNameIntent>(map, (gs, s, m) => gs.HandleSetBeamName(s, m));
        On<BeamTeleportIntent>(map, (gs, s, m) => gs.HandleBeamTeleport(s, m));
        On<SetCrystalDeviceIntent>(map, (gs, s, m) => gs.HandleSetCrystalDevice(s, m)); // #2046: toggle / press / configure a device
        On<SetBaseNameIntent>(map, (gs, s, m) => gs.HandleSetBaseName(s, m));
        On<SetStationNameIntent>(map, (gs, s, m) => gs.HandleSetStationName(s, m));
        On<SetPlaceNameIntent>(map, (gs, s, m) => gs.HandleSetPlaceName(s, m)); // #2952
        On<RequestLandingPadsIntent>(map, (gs, s, m) => gs.HandleRequestLandingPads(s, m));
        On<LootContainerIntent>(map, (gs, s, m) => gs.HandleLootContainer(s, m));
        On<DepositContainerIntent>(map, (gs, s, m) => gs.HandleDepositContainer(s, m));
        On<SetContainerFilterIntent>(map, (gs, s, m) => gs.HandleSetContainerFilter(s, m));
        On<OpenContainerIntent>(map, (gs, s, m) => gs.HandleOpenContainer(s, m)); // #2436
        On<MoveContainerItemIntent>(map, (gs, s, m) => gs.HandleMoveContainerItem(s, m)); // #2436
        On<MoveCargoItemIntent>(map, (gs, s, m) => gs.HandleMoveCargoItem(s, m));
        On<ShipMoveIntent>(map, (gs, s, m) => gs.HandleShipMove(s, m));
        On<DisassembleIntent>(map, (gs, s, m) => gs.HandleDisassemble(s, m));
        On<ClaimStructureIntent>(map, (gs, s, m) => gs.HandleClaimStructure(s, m));
        On<TradeRequestIntent>(map, (gs, s, m) => gs.HandleTradeRequest(s, m));
        On<TradeRespondIntent>(map, (gs, s, m) => gs.HandleTradeRespond(s, m));
        On<TradeOfferIntent>(map, (gs, s, m) => gs.HandleTradeOffer(s, m));
        On<TradeKnowledgeIntent>(map, (gs, s, m) => gs.HandleTradeKnowledge(s, m));
        On<TradeConfirmIntent>(map, (gs, s, _) => gs.HandleTradeConfirm(s));
        On<TradeCancelIntent>(map, (gs, s, _) => gs.HandleTradeCancel(s));
        On<ScanIntent>(map, (gs, s, m) => gs.HandleScan(s, m));
        On<ScanEntityIntent>(map, (gs, s, m) => gs.HandleScanEntity(s, m));
        On<LoadRationIntent>(map, (gs, s, m) => gs.HandleLoadRation(s, m));
        On<TeleportToShipIntent>(map, (gs, s, _) => gs.HandleTeleportToShip(s));
        On<TeleportToPlayerIntent>(map, (gs, s, m) => gs.HandleTeleportToPlayer(s, m));
        On<ReturnToShipIntent>(map, (gs, s, _) => gs.HandleReturnToShip(s)); // #2286
        On<ToggleStealthIntent>(map, (gs, s, _) => gs.HandleToggleStealth(s));
        On<SetJetpackIntent>(map, (gs, s, m) => gs.HandleSetJetpack(s, m));
        On<SetGlidingIntent>(map, (gs, s, m) => gs.HandleSetGliding(s, m)); // #2296
        On<ReleaseRopeIntent>(map, (gs, s, _) => gs.HandleReleaseRope(s)); // #2319
        On<SetLampIntent>(map, (gs, s, m) => gs.HandleSetLamp(s, m));
        On<CopyBuildIntent>(map, (gs, s, m) => gs.HandleCopyBuild(s, m)); // #1117: region → share code
        On<PasteBuildIntent>(map, (gs, s, m) => gs.HandlePasteBuild(s, m)); // #1117: share code → blocks
        On<RequestKnownNpcsIntent>(map, (gs, s, _) => gs.HandleRequestKnownNpcs(s)); // #1118: "People you know"
        On<RequestBaseResidentsIntent>(map, (gs, s, m) => gs.HandleRequestBaseResidents(s, m)); // #2717: who lives at the base
        On<SetNpcCallsIntent>(map, (gs, s, m) => gs.HandleSetNpcCalls(s, m)); // #1119: call preference
        On<SetSeatedIntent>(map, (gs, s, m) => gs.HandleSetSeated(s, m));
        On<SetEvaIntent>(map, (gs, s, m) => gs.HandleSetEva(s, m));
        On<StructureEditIntent>(map, (gs, s, m) => gs.HandleStructureEdit(s, m));
        On<DeployStationCoreIntent>(map, (gs, s, _) => gs.HandleDeployStationCore(s));
        On<BoardStationIntent>(map, (gs, s, m) => gs.HandleBoardStation(s, m));
        On<LeaveStationIntent>(map, (gs, s, _) => gs.HandleLeaveStation(s));
        On<SetStationZeroGIntent>(map, (gs, s, m) => gs.HandleSetStationZeroG(s, m)); // #1842: zero-g construction mode
        On<RepairWreckIntent>(map, (gs, s, m) => gs.HandleRepairWreck(s, m));
        On<ClaimWreckIntent>(map, (gs, s, _) => gs.HandleClaimWreck(s));
        On<RepairShipIntent>(map, (gs, s, m) => gs.HandleRepairShip(s, m));
        On<TravelIntent>(map, (gs, s, m) => gs.HandleTravelIntent(s, m));
        On<TransitLaunchDoneIntent>(map, (gs, s, m) => gs.HandleTransitLaunchDone(s, m));
        On<WormholeTransitIntent>(map, (gs, s, m) => gs.HandleWormholeTransit(s, m));
        On<NpcGreetIntent>(map, (gs, s, m) => gs.HandleNpcGreet(s, m));
        On<SkipOnboardingIntent>(map, (gs, s, m) => gs.HandleSkipOnboarding(s, m));
        On<SetWorldRulesIntent>(map, (gs, s, m) => gs.HandleSetWorldRules(s, m));
        On<RequestAllianceListIntent>(map, (gs, s, _) => gs.HandleRequestAllianceList(s));
        On<RequestAllianceIntent>(map, (gs, s, m) => gs.HandleRequestAlliance(s, m));
        On<AllianceResponseIntent>(map, (gs, s, m) => gs.HandleAllianceResponse(s, m));
        On<DissolveAllianceIntent>(map, (gs, s, m) => gs.HandleDissolveAlliance(s, m));
        On<CrewActionIntent>(map, (gs, s, m) => gs.HandleCrewAction(s, m));
        On<MarkerActionIntent>(map, (gs, s, m) => gs.HandleMarkerAction(s, m));
        On<NoteActionIntent>(map, (gs, s, m) => gs.HandleNoteAction(s, m)); // player notes (#1844)
        On<InterviewAnswerIntent>(map, (gs, s, m) => gs.HandleInterviewAnswer(s, m)); // reporter news (2026-09)
        On<CreativeTakeItemIntent>(map, (gs, s, m) => gs.HandleCreativeTakeItem(s, m)); // the Sandbox catalog (#1930)
        On<StorySelectIntent>(map, (gs, s, m) => gs.HandleStorySelect(s, m));
        On<StoryPackIntent>(map, (gs, s, m) => gs.HandleStoryPackIntent(s, m)); // #2466: the player's own pack controls
        On<CutsceneSeenIntent>(map, (gs, s, m) => gs.HandleCutsceneSeen(s, m)); // #2467: the seen ledger
        On<UiTutorialEvent>(map, (gs, s, m) => gs.HandleUiTutorialEvent(s, m)); // #2469: a UI lesson's teaching flag
        On<NetFragmentFoundIntent>(map, (gs, s, m) => gs.HandleNetFragmentFound(s, m));
        On<CoreHackIntent>(map, (gs, s, m) => gs.HandleCoreHack(s, m));
        On<CoreDialogueChoiceIntent>(map, (gs, s, m) => gs.HandleCoreDialogueChoice(s, m));
        On<RequestStoryResolutionIntent>(map, (gs, s, _) => gs.HandleRequestStoryResolution(s)); // #1124: watch the ending again
        return map;
    }

    private void HandleSelectHotbar(PlayerSession session, SelectHotbarIntent hotbar)
    {
        int previousSlot = session.State.SelectedHotbarSlot;
        session.State.SelectedHotbarSlot = System.Math.Clamp(hotbar.Slot, 0, HotbarSlots - 1);
        ClearRopeIfNotHeld(session.State); // #2319: putting the rope gun away drops the rope
        // #2525: a resync (the client re-sending the slot it shows after a join or world change) is no pick.
        if (session.State.SelectedHotbarSlot != previousSlot && !hotbar.Resync)
        {
            RaiseGameEvent(session, GameEventKeys.HotbarSelect, (session.State.SelectedHotbarSlot + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)); // #2469
        }
    }

    private void HandleSaveGame(PlayerSession session)
    {
        SaveAll();
        _log.Info($"Explicit save requested by '{session.State.Name}'.");
    }

    // ---------------- Test seams ----------------

    /// <summary>Every intent type with a handler, so a test can pin that no intent lost its handler when the switch
    /// became a registry (and that each one is a registered, sealed message).</summary>
    public static IEnumerable<Type> HandledIntentTypesForTest => IntentHandlers.Keys;
}
