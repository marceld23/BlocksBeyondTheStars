// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Reflection;
using System.Text;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class NetCodecTests
{
    private const string MessageNamespace =
        "BlocksBeyondTheStars.Networking.Messages";

    [Fact]
    public void TopLevelMessages_HaveExactlyOneNetCodecTag()
    {
        var topLevelMessages = GetTopLevelMessageTypes();

        var missing = topLevelMessages
            .Where(type => !NetCodec.RegisteredMessageTags.ContainsKey(type))
            .OrderBy(type => type.FullName)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "Top-level messages without a NetCodec tag: " +
            string.Join(", ", missing.Select(type => type.FullName)));
    }

    [Fact]
    public void EveryNetCodecTag_MapsToATopLevelMessage()
    {
        var topLevelMessages = GetTopLevelMessageTypes();

        var nonTopLevelRegistrations = NetCodec.RegisteredMessages
            .Where(entry => !topLevelMessages.Contains(entry.Value))
            .OrderBy(entry => entry.Key)
            .ToArray();

        Assert.True(
            nonTopLevelRegistrations.Length == 0,
            "NetCodec tags that do not map to top-level messages: " +
            string.Join(
                ", ",
                nonTopLevelRegistrations.Select(
                    entry => $"{entry.Key} -> {entry.Value.FullName}")));
    }
    private static readonly Dictionary<byte, Type> ProtocolGoldenList = new()
    {
        [1] = typeof(JoinRequest),
        [2] = typeof(MoveIntent),
        [3] = typeof(MineBlockIntent),
        [4] = typeof(PlaceBlockIntent),
        [5] = typeof(CraftIntent),
        [6] = typeof(UnlockBlueprintIntent),
        [7] = typeof(SelectHotbarIntent),
        [8] = typeof(RequestStarMap),
        [9] = typeof(AdminCommandIntent),
        [10] = typeof(RequestMissions),
        [11] = typeof(AcceptMissionIntent),
        [12] = typeof(TurnInMissionIntent),
        [13] = typeof(CreateMissionIntent),
        [14] = typeof(DockRequestIntent),
        [15] = typeof(DockResponseIntent),
        [16] = typeof(UndockIntent),
        [17] = typeof(BuildShipModuleIntent),
        [18] = typeof(EnterSpaceIntent),
        [19] = typeof(LeaveSpaceIntent),
        [20] = typeof(FireWeaponIntent),
        [21] = typeof(AttackEntityIntent),
        [22] = typeof(UseStationIntent),
        [23] = typeof(SetAppearanceIntent),
        [24] = typeof(CraftShipIntent),
        [25] = typeof(SwitchShipIntent),
        [26] = typeof(ConsumeItemIntent),
        [27] = typeof(LootContainerIntent),
        [28] = typeof(ShipMoveIntent),
        [29] = typeof(DisassembleIntent),
        [30] = typeof(TradeRequestIntent),
        [31] = typeof(TradeRespondIntent),
        [32] = typeof(TradeOfferIntent),
        [33] = typeof(TradeConfirmIntent),
        [34] = typeof(TradeCancelIntent),
        [35] = typeof(ScanIntent),
        [36] = typeof(ScanEntityIntent),
        [37] = typeof(LoadRationIntent),
        [38] = typeof(TeleportToShipIntent),
        [39] = typeof(ToggleStealthIntent),
        [40] = typeof(BoardStationIntent),
        [41] = typeof(LeaveStationIntent),
        [42] = typeof(RepairWreckIntent),
        [43] = typeof(ClaimWreckIntent),
        [44] = typeof(TravelIntent),
        [45] = typeof(SetJetpackIntent),
        [46] = typeof(DoorInteractIntent),
        [47] = typeof(FallDamageIntent),
        [48] = typeof(ShootBlockIntent),

        [50] = typeof(JoinAccepted),
        [51] = typeof(JoinRejected),
        [52] = typeof(ChunkDataMessage),
        [53] = typeof(BlockChanged),
        [54] = typeof(InventoryUpdate),
        [55] = typeof(PlayerStateUpdate),
        [56] = typeof(CraftResult),
        [57] = typeof(ActionRejected),
        [58] = typeof(ServerMessage),
        [59] = typeof(ServerRules),
        [60] = typeof(RespawnNotice),
        [61] = typeof(StarMapData),
        [62] = typeof(MissionList),
        [63] = typeof(MissionResult),
        [64] = typeof(DockRequestNotice),
        [65] = typeof(DockStatus),
        [66] = typeof(ShipCombatStatus),
        [67] = typeof(SpaceState),
        [68] = typeof(SpaceEntityDestroyed),
        [69] = typeof(SpaceClosed),
        [70] = typeof(PlanetEnemyList),
        [71] = typeof(PlanetEnemyDefeated),
        [72] = typeof(ShipPlacement),
        [73] = typeof(ShipStations),
        [74] = typeof(PlayerPresence),
        [75] = typeof(PlayerLeft),
        [76] = typeof(OwnedShips),
        [77] = typeof(WorldEnvironment),
        [78] = typeof(CreatureList),
        [79] = typeof(ContainerList),
        [80] = typeof(TradeUpdate),
        [81] = typeof(TradeClosed),
        [82] = typeof(ScanResult),
        [83] = typeof(StationBoarded),
        [84] = typeof(NpcList),
        [85] = typeof(WreckRepairStatus),
        [86] = typeof(WorldReset),
        [87] = typeof(MiningProgress),
        [88] = typeof(PlanetPoiList),
        [89] = typeof(ChatIntent),
        [90] = typeof(ChatMessage),
        [91] = typeof(SaveGameIntent),
        [92] = typeof(TractorPullIntent),
        [93] = typeof(DoorList),
        [94] = typeof(SetEvaIntent),
        [95] = typeof(EnterShipIntent),
        [96] = typeof(ExitShipIntent),
        [97] = typeof(TradeKnowledgeIntent),
        [98] = typeof(DepositContainerIntent),
        [99] = typeof(UseGadgetIntent),
        [100] = typeof(BeaconList),
        [101] = typeof(SetBeaconLabelIntent),
        [102] = typeof(RequestLandingPadsIntent),
        [103] = typeof(LandingPadList),
        [104] = typeof(ShipTransitFx),
        [105] = typeof(SpaceShipDesign),
        [106] = typeof(StructureEditIntent),
        [107] = typeof(StructureBlockChanged),
        [108] = typeof(DeployStationCoreIntent),
        [109] = typeof(NpcGreetIntent),
        [110] = typeof(NpcGreeting),
        [111] = typeof(MoveItemIntent),
        [112] = typeof(OreScanResult),
        [113] = typeof(ShipAiLine),
        [114] = typeof(SkipOnboardingIntent),
        [115] = typeof(SetWorldRulesIntent),
        [116] = typeof(LandedShipState),
        [117] = typeof(HyperjumpSystemIntent),
        [118] = typeof(DataCubeList),
        [119] = typeof(UnlockGameIntent),
        [120] = typeof(GameUnlocks),
        [121] = typeof(MinigameResultIntent),
        [122] = typeof(BaseList),
        [123] = typeof(SetBaseNameIntent),
        [124] = typeof(SetStationNameIntent),
        [125] = typeof(TintCraftIntent),
        [126] = typeof(RequestAllianceListIntent),
        [127] = typeof(RequestAllianceIntent),
        [128] = typeof(AllianceResponseIntent),
        [129] = typeof(DissolveAllianceIntent),
        [130] = typeof(AllianceList),
        [131] = typeof(AllianceRequestNotice),
        [132] = typeof(SetFaceIntent),
        [133] = typeof(PlayerFace),
        [134] = typeof(BeamList),
        [135] = typeof(SetBeamNameIntent),
        [136] = typeof(BeamTeleportIntent),
        [137] = typeof(BeamTeleported),
        [138] = typeof(BeamFx),
        [139] = typeof(StoryStateMessage),
        [147] = typeof(StorySelectIntent),
        [140] = typeof(NetFragmentFoundIntent),
        [141] = typeof(NetFragmentRevealed),
        [142] = typeof(PlayerMemoryRevealed),
        [148] = typeof(NetFragmentList),
        [143] = typeof(GuardianSystemRevealed),
        [144] = typeof(CoreDialogueMessage),
        [145] = typeof(CoreDialogueChoiceIntent),
        [146] = typeof(CoreHackIntent),
        [149] = typeof(CoreHackProgress),
        [150] = typeof(SpaceWarpFx),
        [151] = typeof(RepairShipIntent),
        [152] = typeof(ShipRepairStatus),
        [153] = typeof(TameRespondIntent),
        [154] = typeof(TameProgress),
        [155] = typeof(TameResult),
        [156] = typeof(RequestCompanionsIntent),
        [157] = typeof(CompanionList),
        [158] = typeof(SetCompanionNameIntent),
        [159] = typeof(ReleaseCompanionIntent),
        [160] = typeof(ShapeCraftIntent),
        [161] = typeof(SpeederList),
        [162] = typeof(EnterSpeederIntent),
        [163] = typeof(ExitSpeederIntent),
        [164] = typeof(StowSpeederIntent),
        [165] = typeof(RefuelSpeederIntent),
        [166] = typeof(SpeederImpactIntent),
        [167] = typeof(SpeederFx),
        [168] = typeof(BumpReport),
        [169] = typeof(VoiceFrame),
        [170] = typeof(MoveCargoItemIntent),
        [171] = typeof(FloraRegrowStarted),
        [172] = typeof(FactoryList),
        [173] = typeof(ClaimStructureIntent),
        [174] = typeof(MaintenanceNotice),
        [175] = typeof(SetSpawnPointIntent),
        [176] = typeof(RespawnOptions),
        [177] = typeof(RespawnChoiceIntent),
        [178] = typeof(DiscoveryLog),
        [179] = typeof(BanditDemand),
        [180] = typeof(BanditResponseIntent),
        [181] = typeof(BanditEncounterResult),
        [182] = typeof(DiscardItemIntent),
        [183] = typeof(PauseIntent),
        [184] = typeof(PauseState),
        [185] = typeof(AchievementList),
        [186] = typeof(AchievementUnlocked),
        [187] = typeof(AchievementRewardDeferred),
        [188] = typeof(StructureMiningProgress),
        [189] = typeof(VegaJournal),
        [190] = typeof(SetSeatedIntent),
        [191] = typeof(PaintBlockIntent),
        [192] = typeof(PaintDesignData),
        [193] = typeof(PaintDesignList),
        [194] = typeof(CustomShapeCraftIntent),
        [195] = typeof(CustomShapeData),
        [196] = typeof(CustomShapeList),
        [197] = typeof(DropPacketList),
        [198] = typeof(SetBodyPaintIntent),
        [199] = typeof(PlayerBodyPaint),
        [200] = typeof(WeatherForecastRequest),
        [201] = typeof(WeatherForecast),
        [202] = typeof(PaintCraftIntent),
        [203] = typeof(TradeRequestNotice),
        [204] = typeof(SetContainerFilterIntent),
    };

    [Fact]
    public void RegisteredMessageTags_MatchProtocolGoldenList()
    {

        Assert.Equal(ProtocolGoldenList.Count, NetCodec.RegisteredMessages.Count);

        foreach (var (tag, expectedType) in ProtocolGoldenList)
        {
            Assert.True(
                NetCodec.RegisteredMessages.TryGetValue(tag, out var actualType),
                $"Expected NetCodec tag {tag} to be registered as {expectedType.Name}.");

            Assert.Equal(expectedType, actualType);
        }
    }

    [Fact]
    public void ProtocolGoldenList_ContainsExactlyAllTopLevelMessages()
    {

        var topLevelMessages = GetTopLevelMessageTypes();

        Assert.Equal(topLevelMessages.Count, ProtocolGoldenList.Count);
        Assert.Equal(topLevelMessages.Count, NetCodec.RegisteredMessages.Count);
    }

    [Fact]
    public void EveryRegisteredMessage_RoundTripsThroughMessagePack()
    {
        foreach (var type in NetCodec.RegisteredMessageTags.Keys)
        {
            var message = Activator.CreateInstance(type);

            Assert.NotNull(message);

            var decoded = NetCodec.Decode(NetCodec.Encode(message));

            Assert.NotNull(decoded);
            Assert.Equal(type, decoded.GetType());
        }
    }

    [Fact]
    public void JoinRequest_PreservesFieldsThroughMessagePackRoundTrip()
    {
        var original = new JoinRequest
        {
            ProtocolVersion = 123,
            PlayerName = "TestPlayer",
            Password = "secret",
            Token = "test-token",
            HostedToken = "host-token",
            Locale = "de",
            ViewDistanceChunks = 12,
        };

        var decoded = Assert.IsType<JoinRequest>(
            NetCodec.Decode(NetCodec.Encode(original)));

        Assert.Equal(original.ProtocolVersion, decoded.ProtocolVersion);
        Assert.Equal(original.PlayerName, decoded.PlayerName);
        Assert.Equal(original.Password, decoded.Password);
        Assert.Equal(original.Token, decoded.Token);
        Assert.Equal(original.HostedToken, decoded.HostedToken);
        Assert.Equal(original.Locale, decoded.Locale);
        Assert.Equal(original.ViewDistanceChunks, decoded.ViewDistanceChunks);
    }

    [Fact]
    public void EveryRegisteredMessage_RoundTripsThroughJson()
    {
        foreach (var type in NetCodec.RegisteredMessageTags.Keys)
        {
            var message = Activator.CreateInstance(type);

            Assert.NotNull(message);

            var payload = NetCodec.EncodeJson(message);
            var decoded = NetCodec.Decode(payload);

            Assert.NotNull(decoded);
            Assert.Equal(type, decoded.GetType());
        }
    }

    [Fact]
    public void JoinRequest_PreservesFieldsThroughJsonRoundTrip()
    {
        var original = new JoinRequest
        {
            ProtocolVersion = 123,
            PlayerName = "TestPlayer",
            Password = "secret",
            Token = "test-token",
            HostedToken = "host-token",
            Locale = "de",
            ViewDistanceChunks = 12,
        };

        var decoded = Assert.IsType<JoinRequest>(
            NetCodec.Decode(NetCodec.EncodeJson(original)));

        Assert.Equal(original.ProtocolVersion, decoded.ProtocolVersion);
        Assert.Equal(original.PlayerName, decoded.PlayerName);
        Assert.Equal(original.Password, decoded.Password);
        Assert.Equal(original.Token, decoded.Token);
        Assert.Equal(original.HostedToken, decoded.HostedToken);
        Assert.Equal(original.Locale, decoded.Locale);
        Assert.Equal(original.ViewDistanceChunks, decoded.ViewDistanceChunks);
    }

    [Fact]
    public void MutatedMessagePackPayload_NeverThrows()
    {
        foreach (var type in NetCodec.RegisteredMessageTags.Keys)
        {
            var message = Activator.CreateInstance(type);

            Assert.NotNull(message);

            var original = NetCodec.Encode(message);

            // Truncate the payload at every possible boundary after the tag.
            for (int length = 0; length < original.Length; length++)
            {
                var truncated = original[..length];

                var exception = Record.Exception(() => NetCodec.Decode(truncated));

                Assert.Null(exception);
            }

            // Flip each byte individually.
            for (int index = 0; index < original.Length; index++)
            {
                var mutated = (byte[])original.Clone();
                mutated[index] ^= 0xFF;

                var exception = Record.Exception(() => NetCodec.Decode(mutated));

                Assert.Null(exception);
            }
        }
    }

    [Fact]
    public void TruncatedJsonEnvelope_NeverThrows()
    {
        var original = NetCodec.EncodeJson(
            new JoinRequest
            {
                ProtocolVersion = 123,
                PlayerName = "TestPlayer",
                Locale = "en",
            });

        // JSON envelope uses the dedicated tag 255.
        Assert.Equal(255, original[0]);

        // Try every truncated prefix, including the tag-only payload.
        for (int length = 0; length < original.Length; length++)
        {
            var truncated = original[..length];

            var exception = Record.Exception(() => NetCodec.Decode(truncated));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void MalformedJsonEnvelopes_AreRejectedWithoutThrowing()
    {
        var malformedPayloads = new[]
        {
            new byte[] { 255 },
            JsonPayload("{"),
            JsonPayload("{\"body\":{}}"),
            JsonPayload("{\"tag\":1}"),
            JsonPayload("{\"tag\":\"not-a-number\",\"body\":{}}"),
            JsonPayload("{\"tag\":256,\"body\":{}}"),
            JsonPayload("{\"tag\":254,\"body\":{}}"),
            JsonPayload("{\"tag\":1,\"body\":{"),
        };

        foreach (var payload in malformedPayloads)
        {
            var exception = Record.Exception(() => NetCodec.Decode(payload));

            Assert.Null(exception);
            Assert.Null(NetCodec.Decode(payload));
        }
    }

    private static byte[] JsonPayload(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var payload = new byte[body.Length + 1];
        payload[0] = 255;
        Buffer.BlockCopy(body, 0, payload, 1, body.Length);
        return payload;
    }

    [Fact]
    public void DeeplyNestedJsonBody_IsRejectedWithoutThrowing()
    {
        const int depth = 100;

        var body = new StringBuilder();

        for (int i = 0; i < depth; i++)
        {
            body.Append("{\"nested\":");
        }

        body.Append("{}");

        for (int i = 0; i < depth; i++)
        {
            body.Append('}');
        }

        var json =
            "{\"tag\":1,\"body\":" +
            body +
            "}";

        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var payload = new byte[jsonBytes.Length + 1];

        payload[0] = 255;
        Buffer.BlockCopy(jsonBytes, 0, payload, 1, jsonBytes.Length);

        var exception = Record.Exception(() => NetCodec.Decode(payload));

        Assert.Null(exception);
        Assert.Null(NetCodec.Decode(payload));
    }

    [Fact]
    public void MutatedJsonPayload_NeverThrows()
    {
        var original = NetCodec.EncodeJson(
            new JoinRequest
            {
                ProtocolVersion = 123,
                PlayerName = "TestPlayer",
                Locale = "en",
            });

        Assert.Equal(255, original[0]);

        // Truncate at every possible boundary.
        for (int length = 0; length < original.Length; length++)
        {
            var truncated = original[..length];

            var exception = Record.Exception(() => NetCodec.Decode(truncated));

            Assert.Null(exception);
        }

        // Flip every byte individually.
        for (int index = 0; index < original.Length; index++)
        {
            var mutated = (byte[])original.Clone();
            mutated[index] ^= 0xFF;

            var exception = Record.Exception(() => NetCodec.Decode(mutated));

            Assert.Null(exception);
        }
    }
    private static HashSet<Type> GetTopLevelMessageTypes()
    {
        var messageTypes = GetMessageTypes();
        var referencedTypes = new HashSet<Type>();

        foreach (var messageType in messageTypes)
        {
            foreach (var property in messageType.GetProperties(
                         BindingFlags.Instance | BindingFlags.Public))
            {
                CollectMessageTypes(property.PropertyType, referencedTypes);
            }
        }

        return messageTypes
            .Where(type => !referencedTypes.Contains(type))
            .ToHashSet();
    }

    private static Type[] GetMessageTypes()
    {
        return typeof(NetCodec).Assembly
            .GetTypes()
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                type.Namespace != null &&
                (type.Namespace == MessageNamespace ||
                 type.Namespace.StartsWith(
                     MessageNamespace + ".",
                     StringComparison.Ordinal)))
            .ToArray();
    }

    private static void CollectMessageTypes(
        Type type,
        HashSet<Type> referencedTypes)
    {
        if (type.IsArray)
        {
            CollectMessageTypes(type.GetElementType()!, referencedTypes);
            return;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                CollectMessageTypes(argument, referencedTypes);
            }
        }

        if (type.Namespace == MessageNamespace ||
            (type.Namespace?.StartsWith(
                MessageNamespace + ".",
                StringComparison.Ordinal) ?? false))
        {
            if (type.IsClass)
            {
                referencedTypes.Add(type);
            }
        }
    }
}
