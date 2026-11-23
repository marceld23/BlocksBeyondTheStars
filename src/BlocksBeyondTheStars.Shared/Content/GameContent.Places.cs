// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// #3981: the fixed places of the bodies (<c>data/places/&lt;planetType&gt;.json</c>, <see cref="PlaceListDefinition"/>) — the
/// landmarks at given seats, the cities and the landing pads of a map planet. The server feeds the given landmarks and the pads
/// from them (<c>GameServerPlaceLists.cs</c>).
/// </summary>
public sealed partial class GameContent
{
    private readonly Dictionary<string, PlaceListDefinition> _placeLists = new(StringComparer.Ordinal);

    /// <summary>Every loaded place list, by planet type.</summary>
    public IReadOnlyDictionary<string, PlaceListDefinition> PlaceLists => _placeLists;

    /// <summary>The place list of a planet type, or null when the type has none.</summary>
    public PlaceListDefinition? PlacesFor(string? planetType)
        => planetType is not null && _placeLists.TryGetValue(planetType, out var list) ? list : null;

    /// <summary>Replaces the place lists (the loader; tests).</summary>
    public void SetPlaceLists(IEnumerable<PlaceListDefinition>? lists)
    {
        _placeLists.Clear();
        foreach (var list in lists ?? Array.Empty<PlaceListDefinition>())
        {
            _placeLists[list.PlanetType] = list;
        }
    }

    /// <summary>The load checks of the place lists: each belongs to a loaded planet type and passes
    /// <see cref="PlaceRules.Problems"/> (known kinds, coordinates in range, templates that exist and are <c>pinOnly</c>).</summary>
    private void ValidatePlaces(List<string> problems)
    {
        foreach (var list in _placeLists.Values)
        {
            if (!_planets.ContainsKey(list.PlanetType))
            {
                problems.Add($"The place list '{list.PlanetType}' names no loaded planet type.");
            }

            problems.AddRange(PlaceRules.Problems(list, LandmarkByKey).Select(p => $"Place list '{list.PlanetType}': {p}."));
        }
    }
}
