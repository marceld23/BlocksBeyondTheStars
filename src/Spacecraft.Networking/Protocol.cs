namespace Spacecraft.Networking;

/// <summary>Shared protocol constants for client/server compatibility.</summary>
public static class Protocol
{
    /// <summary>Bumped whenever the wire format or message set changes incompatibly.</summary>
    public const int Version = 1;

    public const int DefaultGameplayPort = 31415;
    public const int DefaultAdminPort = 31416;
}

/// <summary>Network delivery guarantees, mapped onto transport channels.</summary>
public enum DeliveryMode
{
    /// <summary>Guaranteed, in-order delivery — for actions and world deltas.</summary>
    ReliableOrdered,

    /// <summary>Best-effort, may drop/reorder — for frequent position updates.</summary>
    Unreliable,
}
