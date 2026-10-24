// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client asks for the star map (cockpit). Server replies with <see cref="StarMapData"/>.</summary>
public sealed class RequestStarMap
{
}

/// <summary>#2952: the player names a star system, planet or moon for themselves — the map detail's "Your name" field
/// (client → server). <see cref="PlaceId"/> is a system or body id the player has visited; an empty <see cref="Name"/>
/// gives the place its generated name back. Only this player ever sees the name.</summary>
public sealed class SetPlaceNameIntent
{
    public string PlaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Client asks to travel to (and land on) another celestial body, picked from the star map.</summary>
public sealed class TravelIntent
{
    public string DestinationBodyId { get; set; } = string.Empty;

    /// <summary>Which fixed landing pad to touch down on (item 38); -1 = auto-pick the first free pad.</summary>
    public int PadIndex { get; set; } = -1;
}

/// <summary>#1614: the client has played the current stage of an automatic transit (the take-off, or the warp into
/// the destination system) — the server moves the transit on: warp, land, or hand over in free flight.</summary>
public sealed class TransitLaunchDoneIntent
{
}

/// <summary>#2242: fly through this wormhole (the pilot's [E] at the rift). The server checks everything — in flight,
/// aboard, in range of that rift, a twin outside the story's systems, no arrival lock — before it moves anyone.</summary>
public sealed class WormholeTransitIntent
{
    public string WormholeId { get; set; } = string.Empty;
}

public sealed class NetBody
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>#2952: the receiving player's own name for this body (empty = none) — private, sent to that player only.
    /// A display name: <see cref="Name"/> stays the generated name every key and seed is derived from.</summary>
    public string CustomName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? PlanetType { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>For a SpaceStation body: the owning player's name (empty for procedural/NPC stations and for
    /// non-station bodies). Lets the travel screen mark a station "yours" and otherwise show "Station of {owner}".</summary>
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>System-space coordinates (star at origin) for the system-scale flight layer.</summary>
    public float SystemX { get; set; }
    public float SystemY { get; set; }
    public float SystemZ { get; set; }

    /// <summary>Visual orbital period in (in-game) days; the client advances the body around its parent by
    /// 2π·SystemTimeDays/OrbitPeriodDays for its sky position + sun-lit phase. Signed (negative = retrograde),
    /// seeded per system. 0 = static. Does not move <see cref="SystemX"/>/<see cref="SystemZ"/> (the t=0 ref).</summary>
    public float OrbitPeriodDays { get; set; }

    /// <summary>The body this one orbits (a moon's parent planet); empty = orbits the star at the origin.</summary>
    public string ParentId { get; set; } = string.Empty;

    /// <summary>Fixed landing pads on this body (item 38): how many there are and how many are currently free
    /// (live occupancy). PadsFree == 0 means the body is full — landing there is refused.</summary>
    public int PadsTotal { get; set; }
    public int PadsFree { get; set; }

    /// <summary>Size bias in [-1, 1] (#549) — the archetype's size identity for this body. Already applied in
    /// <see cref="Circumference"/>; the client reads it only to size a body for an older server that sent no
    /// circumference (the classic <c>WorldConstants.CircumferenceFor(id, class, bias)</c>).
    /// 0 for every pre-variance body, so old saves and old clients keep the classic hashed size.</summary>
    public float SizeBias { get; set; }

    /// <summary>Planetary-ring style seed (#596): 0 = no rings, non-zero = the client renders a
    /// Saturn-like ring system around this planet, deriving tilt/bands/hue from the seed. Purely
    /// cosmetic; an additive contractless field, so old clients/servers simply ignore it.</summary>
    public int RingSeed { get; set; }

    /// <summary>#2354: a space wreck salvaged down to nothing — the chart and the travel screen say "salvaged" instead
    /// of "fly there to salvage", and the flight holds no hull at its position any more. Additive.</summary>
    public bool Salvaged { get; set; }

    /// <summary>#2691: this body's walkable circumference in blocks, exactly as the server sizes its world (its size
    /// class, archetype bias and — from terrain generation 26 — the world seed). Every client view that sizes a body
    /// (orbit sphere, sky disc, map bake, landing-pad map) reads THIS value instead of recomputing it, so the views can
    /// never disagree with the world you land on. 0 = an older server that does not send it: the client falls back to
    /// the classic <c>WorldConstants.CircumferenceFor(id, class, <see cref="SizeBias"/>)</c>, which is what such a
    /// server uses. Additive contractless field (no protocol bump): an older client ignores it.</summary>
    public int Circumference { get; set; }
}

public sealed class NetStarSystem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>#2952: the receiving player's own name for this system (empty = none), like <see cref="NetBody.CustomName"/>.</summary>
    public string CustomName { get; set; } = string.Empty;
    public float MapX { get; set; }
    public float MapY { get; set; }
    public NetBody[] Bodies { get; set; } = System.Array.Empty<NetBody>();

    /// <summary>Frontier tier (#1122): 0 home turf, 1 mid, 2 frontier (richer rare ores, extra
    /// vault/monument — and, with the opt-in "Frontier danger" rule, tougher machines). The star map
    /// tags tier-2 systems so flying far has a visible reason.</summary>
    public int Tier { get; set; }

    /// <summary>The system's star colour as packed RGB (#1604) — the same value the planet sky's
    /// <c>SunColor</c> takes after landing there (the server keys both by the system's name), so the
    /// hyperspace chart's star matches the sun you see. 0 = not sent (an older server): the client falls
    /// back to a warm yellow. Additive field; contractless MessagePack lets older clients ignore it.</summary>
    public int StarColor { get; set; }
}

public sealed class StarMapData
{
    public NetStarSystem[] Systems { get; set; } = System.Array.Empty<NetStarSystem>();
    public string ActiveLocationId { get; set; } = string.Empty;

    /// <summary>Where every other online player currently is, so the star map shows the whole party.</summary>
    public NetPlayerLocation[] Players { get; set; } = System.Array.Empty<NetPlayerLocation>();

    /// <summary>Bodies THIS player has landed on (quick-travel targets when Instant Travel is off).</summary>
    public string[] LandedBodyIds { get; set; } = System.Array.Empty<string>();

    /// <summary>Star systems THIS player has entered — known systems reveal their bodies + mini map; an
    /// unknown system is a single "hyperjump here" entry on the travel screen.</summary>
    public string[] KnownSystemIds { get; set; } = System.Array.Empty<string>();

    /// <summary>Host bodies (planet/moon/asteroid) where THIS player has a commissioned space station orbiting —
    /// the travel screen badges them "you have a station here".</summary>
    public string[] MyStationBodyIds { get; set; } = System.Array.Empty<string>();

    /// <summary>Bodies where THIS player has founded a base, with the base's name — the travel screen badges them
    /// "you have a base here: {name}" and offers a rename.</summary>
    public NetMapBase[] MyBases { get; set; } = System.Array.Empty<NetMapBase>();

    /// <summary>#2242: the wormhole ends THIS player can see — every end in a system they know — with the twin's
    /// system only when they flew through or scanned it. Additive; an older client ignores it.</summary>
    public NetWormhole[] Wormholes { get; set; } = System.Array.Empty<NetWormhole>();
}

/// <summary>#2242: one wormhole end on the star map / system chart.</summary>
public sealed class NetWormhole
{
    public string Id { get; set; } = string.Empty;
    public string SystemId { get; set; } = string.Empty;

    /// <summary>System-space position (the star at the origin), the frame of <see cref="NetBody"/>'s system coordinates.</summary>
    public float SystemX { get; set; }
    public float SystemY { get; set; }
    public float SystemZ { get; set; }

    /// <summary>The twin's system — empty while the player does not know where this one leads ("???").</summary>
    public string LinkedSystemId { get; set; } = string.Empty;
}

/// <summary>A player's own base for the travel screen: which body it's on + its current name.</summary>
public sealed class NetMapBase
{
    public string BodyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Client → server: hyperjump into a (possibly never-visited) star system, arriving in FLIGHT mode
/// in that system's space (not landed). Needs a jump generator. The way to reach a system whose bodies you
/// can't yet see on the travel screen.</summary>
public sealed class HyperjumpSystemIntent
{
    public string SystemId { get; set; } = string.Empty;
}

/// <summary>A player's current body (for the shared star map).</summary>
public sealed class NetPlayerLocation
{
    public string Name { get; set; } = string.Empty;
    public string LocationId { get; set; } = string.Empty;
}
