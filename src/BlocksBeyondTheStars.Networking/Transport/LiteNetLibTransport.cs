using System.Collections.Generic;
using LiteNetLib;

namespace BlocksBeyondTheStars.Networking.Transport;

/// <summary>Maps our <see cref="DeliveryMode"/> onto LiteNetLib delivery methods.</summary>
internal static class DeliveryMapping
{
    public static DeliveryMethod ToLiteNetLib(this DeliveryMode mode) => mode switch
    {
        DeliveryMode.ReliableOrdered => DeliveryMethod.ReliableOrdered,
        DeliveryMode.Unreliable => DeliveryMethod.Unreliable,
        _ => DeliveryMethod.ReliableOrdered,
    };
}

/// <summary>
/// UDP server transport built on LiteNetLib. Lightweight and dependency-free of any game
/// engine, so it runs on a plain .NET host (including Raspberry Pi 5). Connection ids are
/// LiteNetLib peer ids.
/// </summary>
public sealed class LiteNetLibServerTransport : IServerTransport
{
    private const string ConnectionKey = "blocks-beyond-the-stars";

    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _manager;
    private readonly Dictionary<int, NetPeer> _peers = new();
    private readonly int _maxConnections;

    public event Action<int>? ClientConnected;
    public event Action<int>? ClientDisconnected;
    public event Action<int, byte[]>? PayloadReceived;

    public LiteNetLibServerTransport(int maxConnections = 16)
    {
        _maxConnections = maxConnections;
        _manager = new NetManager(_listener) { AutoRecycle = true };

        _listener.ConnectionRequestEvent += request =>
        {
            if (_manager.ConnectedPeersCount < _maxConnections)
            {
                request.AcceptIfKey(ConnectionKey);
            }
            else
            {
                request.Reject();
            }
        };

        _listener.PeerConnectedEvent += peer =>
        {
            _peers[peer.Id] = peer;
            ClientConnected?.Invoke(peer.Id);
        };

        _listener.PeerDisconnectedEvent += (peer, _) =>
        {
            _peers.Remove(peer.Id);
            ClientDisconnected?.Invoke(peer.Id);
        };

        _listener.NetworkReceiveEvent += (peer, reader, _, _) =>
        {
            var bytes = reader.GetRemainingBytes();
            PayloadReceived?.Invoke(peer.Id, bytes);
        };
    }

    public void Start(int port) => _manager.Start(port);

    public void Send(int connectionId, byte[] payload, DeliveryMode mode)
    {
        if (_peers.TryGetValue(connectionId, out var peer))
        {
            peer.Send(payload, mode.ToLiteNetLib());
        }
    }

    public void Broadcast(byte[] payload, DeliveryMode mode)
        => _manager.SendToAll(payload, mode.ToLiteNetLib());

    public void Poll() => _manager.PollEvents();

    public void Stop() => _manager.Stop();

    public void Dispose() => _manager.Stop();
}

/// <summary>UDP client transport built on LiteNetLib.</summary>
public sealed class LiteNetLibClientTransport : IClientTransport
{
    private const string ConnectionKey = "blocks-beyond-the-stars";

    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _manager;
    private NetPeer? _serverPeer;

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<byte[]>? PayloadReceived;

    public LiteNetLibClientTransport()
    {
        _manager = new NetManager(_listener) { AutoRecycle = true };

        _listener.PeerConnectedEvent += peer =>
        {
            _serverPeer = peer;
            Connected?.Invoke();
        };

        _listener.PeerDisconnectedEvent += (_, _) =>
        {
            _serverPeer = null;
            Disconnected?.Invoke();
        };

        _listener.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            var bytes = reader.GetRemainingBytes();
            PayloadReceived?.Invoke(bytes);
        };
    }

    public void Connect(string host, int port)
    {
        _manager.Start();
        _manager.Connect(host, port, ConnectionKey);
    }

    public void Send(byte[] payload, DeliveryMode mode) => _serverPeer?.Send(payload, mode.ToLiteNetLib());

    public void Poll() => _manager.PollEvents();

    public void Disconnect() => _manager.Stop();

    public void Dispose() => _manager.Stop();
}
