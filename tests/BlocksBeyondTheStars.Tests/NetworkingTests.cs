using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public class NetworkingTests
{
    [Fact]
    public void Codec_RoundTrips_IntentMessage()
    {
        var msg = new PlaceBlockIntent { X = 3, Y = -7, Z = 12, ItemKey = "iron_wall" };
        var decoded = NetCodec.Decode(NetCodec.Encode(msg));

        var typed = Assert.IsType<PlaceBlockIntent>(decoded);
        Assert.Equal(3, typed.X);
        Assert.Equal(-7, typed.Y);
        Assert.Equal(12, typed.Z);
        Assert.Equal("iron_wall", typed.ItemKey);
    }

    [Fact]
    public void Codec_RoundTrips_ChunkData()
    {
        var blocks = new ushort[] { 0, 1, 2, 3, 65535 };
        var msg = new ChunkDataMessage { Cx = 1, Cy = 2, Cz = 3, Blocks = blocks };
        var typed = Assert.IsType<ChunkDataMessage>(NetCodec.Decode(NetCodec.Encode(msg)));

        Assert.Equal(blocks, typed.Blocks);
        Assert.Equal(2, typed.Cy);
    }

    [Fact]
    public void Codec_ReturnsNull_ForUnknownTag()
    {
        Assert.Null(NetCodec.Decode(new byte[] { 200, 1, 2, 3 }));
        Assert.Null(NetCodec.Decode(Array.Empty<byte>()));
    }

    [Fact]
    public void Loopback_DeliversBothDirections_AndSignalsConnection()
    {
        var link = new LoopbackLink();
        using var server = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);

        int connectedId = -1;
        object? serverGot = null;
        object? clientGot = null;

        server.ClientConnected += id => connectedId = id;
        server.PayloadReceived += (_, payload) => serverGot = NetCodec.Decode(payload);
        client.PayloadReceived += payload => clientGot = NetCodec.Decode(payload);

        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Pilot" }), DeliveryMode.ReliableOrdered);

        // Server processes the connect + message.
        server.Poll();
        Assert.Equal(1, connectedId);
        var join = Assert.IsType<JoinRequest>(serverGot);
        Assert.Equal("Pilot", join.PlayerName);

        // Server replies; client processes it.
        server.Send(connectedId, NetCodec.Encode(new JoinAccepted { PlayerId = "p1", WorldSeed = 99 }),
            DeliveryMode.ReliableOrdered);
        client.Poll();
        var accepted = Assert.IsType<JoinAccepted>(clientGot);
        Assert.Equal("p1", accepted.PlayerId);
        Assert.Equal(99, accepted.WorldSeed);
    }
}
