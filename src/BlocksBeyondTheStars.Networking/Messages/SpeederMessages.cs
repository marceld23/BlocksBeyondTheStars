namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Hover speeders (craftable single-seat surface vehicles). A speeder is a server-authoritative entity the
/// owner deploys from the <c>speeder</c> hotbar item (via the existing <see cref="UseGadgetIntent"/> path),
/// boards, and drives across a planet surface. It hovers over the terrain, runs on its own energy cell and can
/// be damaged + destroyed. The server owns position, hull, fuel and the driver bond; the client renders what
/// these messages describe and predicts its own driving locally. While a speeder is driven its live position
/// follows the driver's presence stream, so no high-rate movement message is needed — only the discrete state
/// snapshot below plus collision reports.
/// </summary>
public sealed class NetSpeeder
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Owning player (only the owner can board, pack up or refuel it).</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Player currently piloting it, or empty when parked. When set, the client anchors the speeder to
    /// that player's presence pose so it moves smoothly without a dedicated movement channel.</summary>
    public string DriverId { get; set; } = string.Empty;

    /// <summary>Parked world position (authoritative when not driven).</summary>
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Heading in degrees.</summary>
    public float Yaw { get; set; }

    public float Hull { get; set; }
    public float HullMax { get; set; }
    public float Fuel { get; set; }
    public float FuelMax { get; set; }

    /// <summary>Hull paint as 0xRRGGBB (0 = default).</summary>
    public int HullColor { get; set; }
}

/// <summary>Full set of speeders the client should show on its world (server → client). Sent on join and on any
/// discrete change (deploy / board / exit / pack up / refuel / damage / destroy), and to the driver periodically
/// while driving so their HUD hull + energy gauges stay current.</summary>
public sealed class SpeederList
{
    public NetSpeeder[] Speeders { get; set; } = System.Array.Empty<NetSpeeder>();
}

/// <summary>The player asks to board a parked speeder they own (client → server). The server validates ownership,
/// proximity and that it isn't already driven, then bonds the player as its driver.</summary>
public sealed class EnterSpeederIntent
{
    public string SpeederId { get; set; } = string.Empty;
}

/// <summary>The driving player asks to dismount (client → server). The server parks the speeder where it is and
/// places the player back on foot beside it.</summary>
public sealed class ExitSpeederIntent
{
}

/// <summary>The owner asks to pack a deployed speeder back into the <c>speeder</c> item (client → server). The
/// server removes the entity, returns one item and forgets the record. Refused if someone is driving it.</summary>
public sealed class StowSpeederIntent
{
    public string SpeederId { get; set; } = string.Empty;
}

/// <summary>The owner asks to refuel a speeder from an energy cell in their inventory (client → server).</summary>
public sealed class RefuelSpeederIntent
{
    public string SpeederId { get; set; } = string.Empty;
}

/// <summary>The driving client reports a hard collision (it owns the hover physics, like on-foot fall damage):
/// the speed at which the speeder slammed into terrain/structure. The server computes + applies the hull damage
/// (and a small jolt to the driver), so the magnitude stays authoritative.</summary>
public sealed class SpeederImpactIntent
{
    public string SpeederId { get; set; } = string.Empty;
    public float Speed { get; set; }
}

/// <summary>A one-shot speeder effect broadcast to everyone on the world (server → client): a deploy shimmer or a
/// destruction explosion at a position, so other players see it too.</summary>
public sealed class SpeederFx
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>"deploy" (materialise shimmer) or "explode" (destruction burst).</summary>
    public string Kind { get; set; } = string.Empty;
}
