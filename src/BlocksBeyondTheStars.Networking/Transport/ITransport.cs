namespace BlocksBeyondTheStars.Networking.Transport;

/// <summary>
/// Server-side transport. Carries raw payloads (already encoded by <see cref="NetCodec"/>)
/// to/from connected clients. Events are raised during <see cref="Poll"/>, so the game
/// server stays single-threaded and tick-driven.
/// </summary>
public interface IServerTransport : IDisposable
{
    event Action<int>? ClientConnected;
    event Action<int>? ClientDisconnected;
    event Action<int, byte[]>? PayloadReceived;

    void Start(int port);
    void Send(int connectionId, byte[] payload, DeliveryMode mode);
    void Broadcast(byte[] payload, DeliveryMode mode);

    /// <summary>Processes pending network events; call once per server tick.</summary>
    void Poll();

    void Stop();
}

/// <summary>Client-side transport mirror of <see cref="IServerTransport"/>.</summary>
public interface IClientTransport : IDisposable
{
    event Action? Connected;
    event Action? Disconnected;
    event Action<byte[]>? PayloadReceived;

    void Connect(string host, int port);
    void Send(byte[] payload, DeliveryMode mode);

    /// <summary>Processes pending network events; call once per client frame.</summary>
    void Poll();

    void Disconnect();
}
