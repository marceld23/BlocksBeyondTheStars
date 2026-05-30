namespace Spacecraft.Networking.Transport;

/// <summary>
/// Runs several server transports as one (e.g. LiteNetLib for native clients + WebSocket
/// for browser clients) so the game server sees a single connection space. Each child is
/// given a disjoint connection-id range so ids never collide, and sends are routed back to
/// the owning child (technical requirements / `anf_webclient.md` §16.3).
/// </summary>
public sealed class CompositeServerTransport : IServerTransport
{
    private const int IdStride = 1_000_000;

    private readonly IServerTransport[] _children;

    public event Action<int>? ClientConnected;
    public event Action<int>? ClientDisconnected;
    public event Action<int, byte[]>? PayloadReceived;

    public CompositeServerTransport(params IServerTransport[] children)
    {
        _children = children;
        for (int i = 0; i < _children.Length; i++)
        {
            int baseId = (i + 1) * IdStride;
            var child = _children[i];
            child.ClientConnected += id => ClientConnected?.Invoke(baseId + id);
            child.ClientDisconnected += id => ClientDisconnected?.Invoke(baseId + id);
            child.PayloadReceived += (id, payload) => PayloadReceived?.Invoke(baseId + id, payload);
        }
    }

    /// <summary>The same port is passed to every child; supply pre-configured children with their own ports if needed.</summary>
    public void Start(int port)
    {
        foreach (var child in _children)
        {
            child.Start(port);
        }
    }

    /// <summary>Starts each child on its own port (parallel to <see cref="_children"/>).</summary>
    public void StartEach(int[] ports)
    {
        for (int i = 0; i < _children.Length; i++)
        {
            _children[i].Start(ports[i]);
        }
    }

    public void Send(int connectionId, byte[] payload, DeliveryMode mode)
    {
        int index = connectionId / IdStride - 1;
        if (index >= 0 && index < _children.Length)
        {
            _children[index].Send(connectionId % IdStride, payload, mode);
        }
    }

    public void Broadcast(byte[] payload, DeliveryMode mode)
    {
        foreach (var child in _children)
        {
            child.Broadcast(payload, mode);
        }
    }

    public void Poll()
    {
        foreach (var child in _children)
        {
            child.Poll();
        }
    }

    public void Stop()
    {
        foreach (var child in _children)
        {
            child.Stop();
        }
    }

    public void Dispose()
    {
        foreach (var child in _children)
        {
            child.Dispose();
        }
    }
}
