// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json.Serialization;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// A star system that is not rolled (#3969): one file <c>data/systems/&lt;key&gt;.json</c> — its star, its bodies with their
/// fixed values and the rule for when it joins the galaxy. Adding such a system is a data change only. A story pack's
/// system (#3357) is the same thing with one body, built from the pack's fields (<c>GameServer</c>). Checked at load by
/// <see cref="FixedSystemRules.Problems"/>: a broken file is reported and dropped, never a failed load.
/// </summary>
public sealed class FixedSystemDefinition
{
    /// <summary>The file name without <c>.json</c> (set by the loader; the id defaults to it).</summary>
    [JsonIgnore]
    public string Key { get; set; } = string.Empty;

    /// <summary>The system id — unique in the galaxy, key-safe, never <c>sys…</c> (the rolled ids) and never the Guardian's.
    /// Its bodies are <c>&lt;id&gt;-&lt;body id&gt;</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Locale key of the system's name (each player's star map shows it in their language); empty = <see cref="Name"/>.</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>A fixed system name (the same in every language); the fallback of <see cref="NameKey"/>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The star. Null = a rolled star colour, like any other hand-made system.</summary>
    public FixedStarDefinition? Star { get; set; }

    /// <summary>Where the system goes on the star map (<see cref="FixedSystemPlacements"/>, default <c>edge</c>).</summary>
    public string Placement { get; set; } = FixedSystemPlacements.Edge;

    /// <summary>When the system joins the galaxy.</summary>
    public FixedSystemAppearance Appear { get; set; } = new();

    /// <summary>The bodies, a moon after its parent.</summary>
    public List<FixedBodyDefinition> Bodies { get; set; } = new();

    /// <summary>The galaxy id of the body <paramref name="bodyId"/> of system <paramref name="systemId"/>.</summary>
    public static string BodyIdOf(string systemId, string bodyId) => systemId + "-" + bodyId;
}

/// <summary>The star of a <see cref="FixedSystemDefinition"/>.</summary>
public sealed class FixedStarDefinition
{
    /// <summary>Locale key of the star's name; empty = <see cref="Name"/>.</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>A fixed star name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The star colour as <c>"0xRRGGBB"</c> or <c>"#RRGGBB"</c>; empty = the rolled colour of a hand-made system.</summary>
    public string Color { get; set; } = string.Empty;

    /// <summary><see cref="Color"/> parsed at load (<see cref="FixedSystemRules.Normalize"/>); 0 = rolled.</summary>
    [JsonIgnore]
    public int ColorRgb { get; set; }

    /// <summary>How big the star is drawn, relative to an ordinary star (1). 0 = ordinary.</summary>
    public float VisibleSize { get; set; }
}

/// <summary>When a <see cref="FixedSystemDefinition"/> joins the galaxy: after the galaxy grew <see cref="AfterGrowth"/> times
/// (only a world created with a growing galaxy), or the first time a player meets <see cref="VisibleIf"/> (the story-pack
/// rule) — exactly one of the two.</summary>
public sealed class FixedSystemAppearance
{
    /// <summary>The system appears with the galaxy's N-th growth (<c>WorldMetadata.GalaxyGrownSystems</c> ≥ N); 0 = not by
    /// growth.</summary>
    public int AfterGrowth { get; set; }

    /// <summary>Conditions (story grammar; a string or a list = all of them) under which a player may see and target the
    /// system; it joins the galaxy the first time any player meets them. Empty = not by a story condition.</summary>
    [JsonConverter(typeof(StoryConditionListConverter))]
    public List<string> VisibleIf { get; set; } = new();
}

/// <summary>One body of a <see cref="FixedSystemDefinition"/>. Every value left at 0 falls back to the roll a hand-made body
/// gets today (circumference and gravity from its size class, the drawn size from its circumference).</summary>
public sealed class FixedBodyDefinition
{
    /// <summary>The body's id inside its system (key-safe); its galaxy id is <c>&lt;system id&gt;-&lt;id&gt;</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><c>planet</c>, <c>moon</c> or <c>asteroidField</c> (<see cref="CelestialKind"/>).</summary>
    public string Kind { get; set; } = "planet";

    /// <summary>The planet type (<c>data/planets.json</c>).</summary>
    public string PlanetType { get; set; } = string.Empty;

    /// <summary>The <see cref="Id"/> of the body a moon orbits (listed before it); empty = it orbits the star.</summary>
    public string Parent { get; set; } = string.Empty;

    /// <summary>Distance from the star (or the parent), in system units — the rolled planets sit 420 + n·520 out, moons 90 +
    /// m·55 from their planet.</summary>
    public float OrbitRadius { get; set; }

    /// <summary>The body's direction around the star (or the parent) at t = 0, in degrees.</summary>
    public float OrbitAngle { get; set; }

    /// <summary>The visual orbital period in in-game days (negative = retrograde, 0 = static) — planets 6–40, moons 0.4–2.5 like
    /// the rolled ones.</summary>
    public float OrbitPeriodDays { get; set; }

    /// <summary>The walkable circumference in blocks, a whole number of chunks; 0 = rolled.</summary>
    public int Circumference { get; set; }

    /// <summary>The gravity factor (1 = normal), within the tested 0.35–1.6; 0 = rolled.</summary>
    public float Gravity { get; set; }

    /// <summary>The ring style seed; 0 = none unless <see cref="Ring"/>.</summary>
    public int RingSeed { get; set; }

    /// <summary>Carries a ring whose style comes from the body's id (when <see cref="RingSeed"/> is 0).</summary>
    public bool Ring { get; set; }

    /// <summary>Locale key of the body's name; empty = <see cref="Name"/>.</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>A fixed body name (the fallback of <see cref="NameKey"/>; empty = the system's name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The body's drawn diameter in the flight view, decoupled from its walkable size (Jupiter looks far bigger than
    /// Earth); 0 = drawn from its circumference (<see cref="SystemBodyLayout.OrbitDiameter"/>).</summary>
    public float VisibleDiameter { get; set; }
}

/// <summary>The values of <see cref="FixedSystemDefinition.Placement"/> (#3357, #3969).</summary>
public static class FixedSystemPlacements
{
    /// <summary>Out past every rolled star, on a bearing of its own (seed-stable) — "beyond the frontier", like the Guardian's.</summary>
    public const string Edge = "edge";

    /// <summary>Inside the galaxy, at the emptiest spot among the stars (seed-stable).</summary>
    public const string Inside = "inside";

    /// <summary>Every placement.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Edge, Inside };
}
