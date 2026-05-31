using MessagePack;
using MessagePack.Resolvers;
using Spacecraft.Networking.Messages;

namespace Spacecraft.Networking;

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

    /// <summary>Decodes a payload into a message object, or null if the tag is unknown/empty.</summary>
    public static object? Decode(byte[] payload)
    {
        if (payload.Length == 0 || !TagToType.TryGetValue(payload[0], out var type))
        {
            return null;
        }

        var body = new ReadOnlyMemory<byte>(payload, 1, payload.Length - 1);
        return MessagePackSerializer.Deserialize(type, body, Options);
    }
}
