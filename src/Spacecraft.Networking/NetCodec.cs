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
