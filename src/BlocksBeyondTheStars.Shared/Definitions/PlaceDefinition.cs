// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The fixed places of a body (#3981): <c>data/places/&lt;planetType&gt;.json</c>, one file per planet type, read by every body
/// of that type (a map planet: Sol's Earth). Each entry stands at a longitude and latitude and is one <see cref="PlaceListKinds"/>
/// kind: a landmark at a given seat, a complex of landmarks side by side, a city, a landing pad. The list order is part of the
/// data — a landmark's record index (<see cref="PlaceRules.ListedLandmarks"/>) and a pad's index follow it, so a place is only
/// ever appended.
/// </summary>
public sealed class PlaceListDefinition
{
    /// <summary>The planet type the list belongs to (the file name; set by the loader).</summary>
    public string PlanetType { get; set; } = string.Empty;

    /// <summary>The places, in their pinned order.</summary>
    public List<PlaceDefinition> Places { get; set; } = new();

    /// <summary>The places of one kind, in list order.</summary>
    public IEnumerable<PlaceDefinition> OfKind(string kind) => Places.Where(p => string.Equals(p.Kind, kind, StringComparison.Ordinal));
}

/// <summary>One fixed place of a body (#3981). Every kind has a key, a longitude and a latitude; the rest belongs to its kind.</summary>
public sealed class PlaceDefinition
{
    /// <summary>The place's key, unique within its list (key-safe: lower-case letters, digits, underscores).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>What stands there: one of <see cref="PlaceListKinds.All"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Degrees east of the prime meridian (west negative), −180 ≤ longitude &lt; 360.</summary>
    public double Longitude { get; set; }

    /// <summary>Degrees north of the equator (south negative), −90 &lt; latitude &lt; 90.</summary>
    public double Latitude { get; set; }

    /// <summary>A <c>landmark</c>: the landmark template that stands there (a <c>pinOnly</c> one — no pool offers it).</summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>The quarter turn (0..3) the structure stands at.</summary>
    public int Turn { get; set; }

    /// <summary>A <c>city</c>: its theme key (the city record's <c>city:&lt;theme&gt;</c>, the composer's lane); empty = the
    /// place's key.</summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>A <c>city</c>: its district grid (districts per side, <see cref="PlaceRules.MinCityGrid"/>..<see cref="PlaceRules.MaxCityGrid"/>);
    /// 0 = the composer's default.</summary>
    public int Grid { get; set; }

    /// <summary>A <c>city</c>: its plaza district <c>[column, row]</c> (X index, Z index) — the place's centre and landing pad
    /// lie in its middle; empty = the centre district.</summary>
    public List<int> Plaza { get; set; } = new();

    /// <summary>A <c>city</c>: how many free squares it keeps for rail halls (the rail network builds them).</summary>
    public int Stations { get; set; } = 1;

    /// <summary>A <c>city</c>: its colours — per material token (<c>wall accent roof floor path</c>) a block key with an
    /// optional dye, <c>concrete#F0E2C0</c>; a token it leaves out keeps the city kit's default.</summary>
    public Dictionary<string, string> Palette { get; set; } = new(StringComparer.Ordinal);

    /// <summary>A <c>city</c>: a pinned city kit; empty = the theme's own.</summary>
    public string Kit { get; set; } = string.Empty;

    /// <summary>A <c>city</c>: the locale key of its name; empty = none.</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>A <c>city</c>: the landmark templates that stand in it, each on a square of its own (each <c>pinOnly</c>).</summary>
    public List<string> Landmarks { get; set; } = new();

    /// <summary>A <c>complex</c>: its parts, in their pinned order (each part's landmark record follows it), placed around the
    /// place's column as the anchor.</summary>
    public List<PlaceMember> Members { get; set; } = new();

    /// <summary>A <c>rail</c> line (#3985): its stops in line order — two or more city keys of the same list — each with the
    /// waypoints the line runs through on its way from the stop before.</summary>
    public List<PlaceRailStop> Stops { get; set; } = new();

    /// <summary>The city's theme: <see cref="Theme"/>, else the key.</summary>
    public string ThemeOrKey => Theme.Length > 0 ? Theme : Key;

    /// <summary>The block column of the place on a body of <paramref name="circumference"/> (<see cref="PlaceRules.BlockOf"/>).</summary>
    public (int X, int Z) BlockOn(int circumference) => PlaceRules.BlockOf(Longitude, Latitude, circumference);
}

/// <summary>
/// One part of a <c>complex</c> place (#3986): a landmark template whose footprint's low (south-west) corner lies
/// <see cref="East"/> blocks east and <see cref="North"/> blocks north of the complex's anchor column. Every part of one complex
/// stands on the same ground row and unturned, so parts built in one frame (the SPS headquarters) meet edge to edge.
/// </summary>
public sealed class PlaceMember
{
    /// <summary>The landmark template's key (a <c>pinOnly</c> one).</summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>Blocks east (+X) of the anchor to the footprint's low corner.</summary>
    public int East { get; set; }

    /// <summary>Blocks north (+Z) of the anchor to the footprint's low corner.</summary>
    public int North { get; set; }
}

/// <summary>
/// One stop of a <c>rail</c> place (#3985): the city it serves (a <c>city</c> place's key in the same list) and the waypoints
/// the line runs through on its way to it from the stop before — each <c>[longitude, latitude]</c>, to steer the line over
/// the sea and round the mountains. The first stop has none.
/// </summary>
public sealed class PlaceRailStop
{
    /// <summary>The city's place key.</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>The waypoints from the stop before, each <c>[longitude, latitude]</c> in degrees.</summary>
    public List<List<double>> Via { get; set; } = new();
}

/// <summary>
/// One landmark a place list stands, in record order (<see cref="PlaceRules.ListedLandmarks"/>): a <c>landmark</c> place, or
/// one part of a <c>complex</c>.
/// </summary>
public readonly struct ListedLandmark
{
    public ListedLandmark(PlaceDefinition place, PlaceMember? member)
    {
        Place = place;
        Member = member;
    }

    /// <summary>The place: the landmark place itself, or the complex the part belongs to.</summary>
    public PlaceDefinition Place { get; }

    /// <summary>The complex's part, null for a landmark place.</summary>
    public PlaceMember? Member { get; }

    /// <summary>The landmark template's key.</summary>
    public string Template => Member?.Template ?? Place.Template;

    /// <summary>The footprint's centre column on a body of <paramref name="circumference"/>, for a footprint of
    /// <paramref name="width"/> × <paramref name="length"/>: a landmark place's block column; a part's low corner (the anchor
    /// plus its offset) plus half its footprint — the centre <c>GivenSeat.OriginFor</c> turns back into that corner.</summary>
    public (int X, int Z) CentreOn(int circumference, int width, int length)
    {
        var (x, z) = Place.BlockOn(circumference);
        return Member is not { } m ? (x, z)
            : (WorldConstants.WrapX(x + m.East + (width / 2), circumference), z + m.North + (length / 2));
    }
}

/// <summary>The kinds a place may be (#3981). The format is open: a later kind (a rail stop) is one more constant here and one
/// more branch in <see cref="PlaceRules.Problems"/>.</summary>
public static class PlaceListKinds
{
    /// <summary>A landmark template at a given seat (<c>GivenSeat</c>), stamped first in the structures stage.</summary>
    public const string Landmark = "landmark";

    /// <summary>A group of landmark templates placed side by side around one anchor (#3986, <see cref="PlaceMember"/>): all on
    /// one ground row — the median ground under all their footprints together — and unturned. Each part is a landmark record
    /// of its own, stamped like a <see cref="Landmark"/> place.</summary>
    public const string Complex = "complex";

    /// <summary>A city (#3983: a metropolis, a <c>CityPlace</c> laid out on the land round the place's landing pad).</summary>
    public const string City = "city";

    /// <summary>A landing pad: a body with pad places takes exactly these pads, in list order (the first is the home touchdown).</summary>
    public const string Pad = "pad";

    /// <summary>A rail line (#3985) through cities of the list (<see cref="PlaceDefinition.Stops"/>): a station on a rail-hall
    /// square of each, a public train shuttling end to end. Its longitude and latitude mean nothing.</summary>
    public const string Rail = "rail";

    public static readonly string[] All = { Landmark, Complex, City, Pad, Rail };
}

/// <summary>The place-list rules (#3981): the block column of a longitude and latitude, and the checks of a list.</summary>
public static class PlaceRules
{
    /// <summary>The folder under the data directory that holds the lists.</summary>
    public const string DataFolder = "places";

    /// <summary>The smallest and the largest district grid of a city place.</summary>
    public const int MinCityGrid = 3;
    public const int MaxCityGrid = 12;

    /// <summary>The most rail-hall squares a city place keeps.</summary>
    public const int MaxCityStations = 3;

    /// <summary>The token names a city palette may set (the material tokens without their <c>@</c>).</summary>
    public static readonly string[] PaletteTokens = { "wall", "accent", "roof", "floor", "path" };

    /// <summary>A palette entry: a block key, optionally <c>#RRGGBB</c> after it for a dye (0 = none). False when malformed.</summary>
    public static bool TryParsePaletteEntry(string? text, out string block, out int tint)
    {
        block = string.Empty;
        tint = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int hash = text!.IndexOf('#');
        block = (hash < 0 ? text : text.Substring(0, hash)).Trim();
        if (hash >= 0 && !(text.Length - hash - 1 == 6 && int.TryParse(text.Substring(hash + 1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out tint)))
        {
            return false;
        }

        return block.Length > 0;
    }

    /// <summary>
    /// The block column of a longitude and latitude on a body of <paramref name="circumference"/>: X = longitude / 360 · C,
    /// wrapped to [0, C) — longitude 0 is world X 0, east is +X; Z = latitude / 180 · P with P =
    /// <see cref="WorldConstants.LatitudePeriodFor"/>(C) — the equator is Z 0, north is +Z. This is the planet map's own
    /// projection (its row 0 is the south edge, Z −P/2), so a place sits on the map cell of its coordinates. Rounded to the
    /// nearest block, halves away from zero.
    /// </summary>
    public static (int X, int Z) BlockOf(double longitude, double latitude, int circumference)
    {
        int x = (int)Math.Round(longitude / 360.0 * circumference, MidpointRounding.AwayFromZero);
        int z = (int)Math.Round(latitude / 180.0 * WorldConstants.LatitudePeriodFor(circumference), MidpointRounding.AwayFromZero);
        return (WorldConstants.WrapX(x, circumference), z);
    }

    /// <summary>What is wrong with a list, one sentence each (empty = fine). <paramref name="landmark"/> finds a landmark
    /// template by key (null = unknown); <paramref name="blockKnown"/>, when given, says whether a block key exists (a city
    /// palette's blocks).</summary>
    public static List<string> Problems(PlaceListDefinition list, Func<string, StructureTemplate?> landmark, Func<string, bool>? blockKnown = null)
    {
        var problems = new List<string>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var place in list.Places)
        {
            string at = $"place '{place.Key}'";
            if (!IsKeySafe(place.Key) || !keys.Add(place.Key))
            {
                problems.Add($"{at} needs a key of lower-case letters, digits and underscores, unique in its list");
            }

            if (Array.IndexOf(PlaceListKinds.All, place.Kind) < 0)
            {
                problems.Add($"{at} has the unknown kind '{place.Kind}' ({string.Join(" | ", PlaceListKinds.All)})");
            }

            if (!(place.Latitude > -90.0 && place.Latitude < 90.0) || !(place.Longitude >= -180.0 && place.Longitude < 360.0))
            {
                problems.Add($"{at} lies outside −90 < latitude < 90, −180 ≤ longitude < 360");
            }

            if (place.Turn is < 0 or > 3)
            {
                problems.Add($"{at} has a turn outside 0..3");
            }

            problems.AddRange(KindProblems(place, landmark, blockKnown).Select(p => $"{at} {p}"));
        }

        problems.AddRange(RailProblems(list));
        return problems;
    }

    /// <summary>The checks of a list's rail lines (#3985): each has a name key and two or more stops, every stop a city of
    /// the list (none twice in one line) with well-formed waypoints, the first stop none; and no city serves more lines than it
    /// keeps rail-hall squares (<see cref="PlaceDefinition.Stations"/>) — each line's hall stands on a square of its own.</summary>
    public static IEnumerable<string> RailProblems(PlaceListDefinition list)
    {
        var cities = list.OfKind(PlaceListKinds.City).GroupBy(c => c.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var lines = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var place in list.Places)
        {
            string at = $"place '{place.Key}'";
            if (place.Kind != PlaceListKinds.Rail)
            {
                if (place.Stops.Count > 0)
                {
                    yield return $"{at} names stops, which only a rail line has";
                }

                continue;
            }

            if (place.NameKey.Length == 0)
            {
                yield return $"{at} is a rail line without a name key";
            }

            if (place.Stops.Count < 2)
            {
                yield return $"{at} is a rail line with fewer than two stops";
            }

            foreach (string problem in RailStopProblems(place, cities, lines))
            {
                yield return $"{at} {problem}";
            }
        }

        foreach (var kv in lines.Where(kv => kv.Value > cities[kv.Key].Stations))
        {
            yield return $"place '{kv.Key}' keeps {cities[kv.Key].Stations} rail-hall square(s) for {kv.Value} rail lines";
        }
    }

    /// <summary>The checks of one rail line's stops; counts each city's lines into <paramref name="lines"/>.</summary>
    private static IEnumerable<string> RailStopProblems(PlaceDefinition place, Dictionary<string, PlaceDefinition> cities, Dictionary<string, int> lines)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < place.Stops.Count; i++)
        {
            var stop = place.Stops[i];
            if (!cities.ContainsKey(stop.City))
            {
                yield return $"stops at '{stop.City}', which is no city of the list";
            }
            else if (!seen.Add(stop.City))
            {
                yield return $"stops at '{stop.City}' twice";
            }
            else
            {
                lines[stop.City] = lines.TryGetValue(stop.City, out int n) ? n + 1 : 1;
            }

            if (i == 0 && stop.Via.Count > 0)
            {
                yield return "has waypoints before its first stop";
            }

            if (stop.Via.Any(p => p is null || p.Count != 2 || !(p[1] > -90.0 && p[1] < 90.0) || !(p[0] >= -180.0 && p[0] < 360.0)))
            {
                yield return $"has a waypoint before '{stop.City}' that is no [longitude, latitude] in range";
            }
        }
    }

    /// <summary>The checks of the fields a kind owns.</summary>
    private static IEnumerable<string> KindProblems(PlaceDefinition place, Func<string, StructureTemplate?> landmark, Func<string, bool>? blockKnown)
    {
        if (place.Kind != PlaceListKinds.Complex && place.Members.Count > 0)
        {
            yield return "names members, which only a complex has";
        }

        switch (place.Kind)
        {
            case PlaceListKinds.Landmark:
                if (TemplateProblem(place.Template, landmark) is { } problem)
                {
                    yield return problem;
                }

                break;
            case PlaceListKinds.Complex:
                foreach (string complexProblem in ComplexProblems(place, landmark))
                {
                    yield return complexProblem;
                }

                break;
            case PlaceListKinds.City:
                foreach (string cityProblem in CityProblems(place, landmark, blockKnown))
                {
                    yield return cityProblem;
                }

                break;
            case PlaceListKinds.Pad:
                if (place.Template.Length > 0)
                {
                    yield return "is a pad and names a template";
                }

                break;
            case PlaceListKinds.Rail:
                if (place.Template.Length > 0 || place.Landmarks.Count > 0)
                {
                    yield return "is a rail line and names a template or landmarks"; // its stops are checked with the list (RailProblems)
                }

                break;
        }
    }

    /// <summary>The checks of a complex (#3986): it names no template of its own, stands unturned, has parts, each part names a
    /// place-only template, and no two parts' footprints overlap (they may meet edge to edge).</summary>
    private static IEnumerable<string> ComplexProblems(PlaceDefinition place, Func<string, StructureTemplate?> landmark)
    {
        if (place.Template.Length > 0)
        {
            yield return "is a complex and names a template (its members name theirs)";
        }

        if (place.Turn != 0)
        {
            yield return "is a complex with a turn (its members stand unturned, in their shared frame)";
        }

        if (place.Members.Count == 0)
        {
            yield return "is a complex without members";
        }

        var boxes = new List<(string Key, int X0, int Z0, int X1, int Z1)>();
        foreach (var member in place.Members)
        {
            if (TemplateProblem(member.Template, landmark) is { } problem)
            {
                yield return problem;
                continue;
            }

            var t = landmark(member.Template)!;
            (string Key, int X0, int Z0, int X1, int Z1) box =
                (member.Template, member.East, member.North, member.East + t.Width, member.North + t.Length);
            foreach (var other in boxes.Where(b => b.X0 < box.X1 && box.X0 < b.X1 && b.Z0 < box.Z1 && box.Z0 < b.Z1))
            {
                yield return $"has the members '{other.Key}' and '{member.Template}' overlapping";
            }

            boxes.Add(box);
        }
    }

    /// <summary>The checks of a city's fields: its grid, plaza and station count in range, its palette's tokens and entries
    /// (blocks the content knows, when <paramref name="blockKnown"/> is given), its landmarks <c>pinOnly</c>.</summary>
    private static IEnumerable<string> CityProblems(PlaceDefinition place, Func<string, StructureTemplate?> landmark, Func<string, bool>? blockKnown)
    {
        if (place.Grid != 0 && place.Grid is < MinCityGrid or > MaxCityGrid)
        {
            yield return $"has a grid outside {MinCityGrid}..{MaxCityGrid}";
        }

        int grid = place.Grid == 0 ? MinCityGrid : place.Grid;
        if (place.Plaza.Count != 0 && (place.Plaza.Count != 2 || place.Plaza.Any(c => c < 0 || c >= grid)))
        {
            yield return "names a plaza that is no [column, row] of its grid";
        }

        if (place.Stations is < 0 or > MaxCityStations)
        {
            yield return $"keeps a station count outside 0..{MaxCityStations}";
        }

        foreach (var kv in place.Palette)
        {
            if (Array.IndexOf(PaletteTokens, kv.Key) < 0)
            {
                yield return $"has the palette token '{kv.Key}' ({string.Join(" | ", PaletteTokens)})";
            }
            else if (!TryParsePaletteEntry(kv.Value, out string block, out _) || (blockKnown is not null && !blockKnown(block)))
            {
                yield return $"has the palette entry '{kv.Value}' for '{kv.Key}', which is no known block (with an optional #RRGGBB)";
            }
        }

        foreach (string key in place.Landmarks)
        {
            if (TemplateProblem(key, landmark) is { } cityProblem)
            {
                yield return cityProblem;
            }
        }
    }

    /// <summary>A place may name only a landmark template that no pool offers (<c>pinOnly</c>): the list is the one place it
    /// stands in.</summary>
    private static string? TemplateProblem(string key, Func<string, StructureTemplate?> landmark)
        => key.Length == 0 ? "names no template"
            : landmark(key) is not { } template ? $"names the unknown landmark template '{key}'"
            : !template.PinOnly ? $"names the landmark template '{key}', which is not pinOnly (a pool would roll it elsewhere too)"
            : null;

    private static bool IsKeySafe(string key) => key.Length > 0 && key.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

    /// <summary>Every landmark template a list names: its landmark places', its complexes' members and its cities' landmarks.</summary>
    public static IEnumerable<string> NamedTemplates(PlaceListDefinition list)
        => ListedLandmarks(list).Select(l => l.Template).Concat(list.OfKind(PlaceListKinds.City).SelectMany(p => p.Landmarks));

    /// <summary>The landmarks a list stands at given seats, in record order: every <c>landmark</c> place, and every member of a
    /// <c>complex</c> in its member order, as they come in the list. The <c>i</c>-th is pinned under landmark record
    /// <c>1000 + i</c> on the server, so a list only ever appends — places, and members to no complex once played.</summary>
    public static IEnumerable<ListedLandmark> ListedLandmarks(PlaceListDefinition list)
    {
        foreach (var place in list.Places)
        {
            if (string.Equals(place.Kind, PlaceListKinds.Landmark, StringComparison.Ordinal))
            {
                yield return new ListedLandmark(place, null);
            }
            else if (string.Equals(place.Kind, PlaceListKinds.Complex, StringComparison.Ordinal))
            {
                foreach (var member in place.Members)
                {
                    yield return new ListedLandmark(place, member);
                }
            }
        }
    }
}
