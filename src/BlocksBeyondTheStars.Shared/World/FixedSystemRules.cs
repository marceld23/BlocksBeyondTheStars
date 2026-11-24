// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// The rules of a <see cref="FixedSystemDefinition"/> (#3969): the data folder, the defaults a file may leave out
/// (<see cref="Normalize"/>) and everything that makes a file unusable (<see cref="Problems"/>).
/// </summary>
public static class FixedSystemRules
{
    /// <summary>The data folder of the fixed systems (<c>data/systems/&lt;key&gt;.json</c>).</summary>
    public const string DataFolder = "systems";

    /// <summary>The Guardian finale's system id — reserved, never a fixed system's.</summary>
    public const string GuardianSystemId = "guardian_finale";

    /// <summary>The smallest fixed circumference (a small rock).</summary>
    public const int MinCircumference = 256;

    /// <summary>The largest fixed circumference — the biggest a rolled lone giant gets (the planet band's top × 4/3).</summary>
    public const int MaxCircumference = 16000;

    /// <summary>The lightest fixed gravity — the bottom of the tested range (the asteroids' band).</summary>
    public static float MinGravity => BodyGravity.BandFor(WorldConstants.WorldSizeClass.Asteroid).Lo;

    /// <summary>The heaviest fixed gravity — the top of the tested range (the planets' band).</summary>
    public static float MaxGravity => BodyGravity.BandFor(WorldConstants.WorldSizeClass.Planet).Hi;

    /// <summary>Fills what a file may leave out: the lists, the id (the file key), the placement, the parsed star colour.</summary>
    public static void Normalize(FixedSystemDefinition def)
    {
        def.Bodies ??= new List<FixedBodyDefinition>();
        def.Appear ??= new FixedSystemAppearance();
        def.Appear.VisibleIf ??= new List<string>();
        def.NameKey ??= string.Empty;
        def.Name ??= string.Empty;
        if (string.IsNullOrWhiteSpace(def.Id))
        {
            def.Id = def.Key;
        }

        if (string.IsNullOrWhiteSpace(def.Placement))
        {
            def.Placement = FixedSystemPlacements.Edge;
        }

        if (def.Star is { } star)
        {
            star.ColorRgb = TryParseColor(star.Color, out int rgb) ? rgb : 0;
        }
    }

    /// <summary>Everything that makes <paramref name="def"/> unusable, one sentence each — empty when it can join a galaxy.
    /// <paramref name="planetExists"/> tells whether a planet type is loaded.</summary>
    public static List<string> Problems(FixedSystemDefinition def, Func<string, bool> planetExists)
    {
        var problems = new List<string>();
        if (!StoryPackValidator.IsKeySafeId(def.Id) || StarSystemIds.HasRolledPrefix(def.Id)
            || string.Equals(def.Id, GuardianSystemId, StringComparison.Ordinal))
        {
            problems.Add($"the id '{def.Id}' is empty, not key-safe, rolled ('sys…') or the Guardian's");
        }

        if (string.IsNullOrEmpty(def.NameKey) && string.IsNullOrEmpty(def.Name))
        {
            problems.Add("a system needs a nameKey or a name");
        }

        if (!FixedSystemPlacements.All.Contains(def.Placement))
        {
            problems.Add($"the placement '{def.Placement}' is none of {string.Join(", ", FixedSystemPlacements.All)}");
        }

        bool byGrowth = def.Appear.AfterGrowth > 0, byStory = def.Appear.VisibleIf.Count > 0;
        if (byGrowth == byStory || def.Appear.AfterGrowth < 0)
        {
            problems.Add("appear needs exactly one of afterGrowth (1 or more) and visibleIf");
        }

        if (def.Star is { } star && !string.IsNullOrEmpty(star.Color) && star.ColorRgb <= 0)
        {
            problems.Add($"the star colour '{star.Color}' is no 0xRRGGBB or #RRGGBB colour");
        }

        if (def.Star is { VisibleSize: < 0f })
        {
            problems.Add("the star's visibleSize is negative");
        }

        if (def.Bodies.Count == 0)
        {
            problems.Add("a system needs at least one body");
        }

        var seen = new Dictionary<string, CelestialKind>(StringComparer.Ordinal);
        foreach (var body in def.Bodies)
        {
            problems.AddRange(BodyProblems(body, seen, planetExists));
        }

        return problems;
    }

    /// <summary>The problems of one body; <paramref name="seen"/> holds the bodies before it (a moon's parent comes first).</summary>
    private static IEnumerable<string> BodyProblems(FixedBodyDefinition? body, Dictionary<string, CelestialKind> seen,
        Func<string, bool> planetExists)
    {
        if (body is null || !StoryPackValidator.IsKeySafeId(body.Id) || seen.ContainsKey(body.Id))
        {
            yield return $"a body id '{body?.Id}' is empty, not key-safe or used twice";
            yield break;
        }

        string b = $"body '{body.Id}'";
        if (KindOf(body.Kind) is not { } kind)
        {
            yield return $"{b}: the kind '{body.Kind}' is none of planet, moon, asteroidField";
            yield break;
        }

        seen[body.Id] = kind;
        if (string.IsNullOrEmpty(body.PlanetType) || !planetExists(body.PlanetType))
        {
            yield return $"{b}: the planet type '{body.PlanetType}' is not loaded";
        }

        bool hasParent = !string.IsNullOrEmpty(body.Parent);
        if ((kind == CelestialKind.Moon) != hasParent
            || (hasParent && (!seen.TryGetValue(body.Parent, out var parentKind) || parentKind == CelestialKind.Moon || body.Parent == body.Id)))
        {
            yield return $"{b}: a moon (and only a moon) names its parent — a planet or asteroid listed before it";
        }

        if (body.Circumference != 0 && (body.Circumference < MinCircumference || body.Circumference > MaxCircumference
                                        || body.Circumference % WorldConstants.ChunkSize != 0))
        {
            yield return $"{b}: the circumference {body.Circumference} is not a whole number of chunks in {MinCircumference}–{MaxCircumference}";
        }

        if (body.Gravity != 0f && !(body.Gravity >= MinGravity && body.Gravity <= MaxGravity))
        {
            yield return $"{b}: the gravity {body.Gravity} is outside the tested {MinGravity}–{MaxGravity}";
        }

        if (!(body.OrbitRadius >= 0f) || float.IsInfinity(body.OrbitRadius) || float.IsNaN(body.OrbitAngle)
            || float.IsInfinity(body.OrbitAngle) || float.IsNaN(body.OrbitPeriodDays) || float.IsInfinity(body.OrbitPeriodDays)
            || !(body.VisibleDiameter >= 0f) || float.IsInfinity(body.VisibleDiameter))
        {
            yield return $"{b}: an orbit value or the visible diameter is negative or not a number";
        }
    }

    /// <summary>The body kind a file names (<c>planet</c>, <c>moon</c>, <c>asteroidField</c>, any letter case), or null.</summary>
    public static CelestialKind? KindOf(string? kind)
        => string.Equals(kind, "planet", StringComparison.OrdinalIgnoreCase) ? CelestialKind.Planet
            : string.Equals(kind, "moon", StringComparison.OrdinalIgnoreCase) ? CelestialKind.Moon
            : string.Equals(kind, "asteroidField", StringComparison.OrdinalIgnoreCase) ? CelestialKind.AsteroidField
            : null;

    /// <summary>Reads <c>"0xRRGGBB"</c> or <c>"#RRGGBB"</c> into a packed RGB above 0 (pure black is no star colour).</summary>
    public static bool TryParseColor(string? text, out int rgb)
    {
        rgb = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string hex = text!.Trim();
        hex = hex.StartsWith("#", StringComparison.Ordinal) ? hex.Substring(1)
            : hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex.Substring(2)
            : string.Empty;
        return hex.Length == 6 && int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb) && rgb > 0;
    }
}
