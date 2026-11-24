// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The fixed places of a body (#3981): its type's place list (<c>data/places/&lt;planetType&gt;.json</c>,
/// <see cref="PlaceListDefinition"/>) feeds two seams of the server.
/// <list type="bullet">
/// <item><b>Landmarks</b> — every <c>landmark</c> place, in list order, is a given landmark (<see cref="GivenLandmarkSource"/>'s
/// default, <see cref="PlaceListLandmarks"/>): its template on a <see cref="GivenSeat"/> at the place's block column and turn,
/// pinned under record <see cref="GivenLandmarkIndexBase"/> + its position among the landmark places.</item>
/// <item><b>Landing pads</b> — a body with <c>pad</c> places takes exactly those pads, in list order (pad 0, the home touchdown, is
/// the first), instead of the searched ones (<see cref="ListedPads"/>). Each becomes what its ground makes it (dry, wet, an
/// islet: <c>DecidePad</c>), is levelled like every pad and pinned in <c>WorldMetadata.BodyLandingPads</c> on its first
/// computation.</item>
/// </list>
/// Both work on a save of any terrain generation: a body with a place list is a map world, and a map world's structure pipeline
/// runs at least at the geography generation (<see cref="PipelineGeneration"/>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The generation the active body's structure pipeline — the footprint registry, the landmark overlays and rules,
    /// the reserved ground — runs at (<see cref="MapWorldRules.PipelineGeneration"/>): <paramref name="generation"/>, raised to
    /// the geography generation on a map world.</summary>
    private int PipelineGeneration(int generation) => MapWorldRules.PipelineGeneration(generation, _world.Planet);

    /// <summary>The given landmarks of the active body from its type's place list (empty without one): each <c>landmark</c>
    /// place's template at its block column and turn on the active world's circumference.</summary>
    private IReadOnlyList<GivenLandmarkPlace> PlaceListLandmarks(string locationId)
    {
        if (!string.Equals(_world.LocationId, locationId, System.StringComparison.Ordinal)
            || _content.PlacesFor(_world.Planet.Key) is not { } list)
        {
            return System.Array.Empty<GivenLandmarkPlace>();
        }

        return list.OfKind(PlaceListKinds.Landmark).Select(p =>
        {
            var (x, z) = p.BlockOn(_world.Circumference);
            return new GivenLandmarkPlace(p.Template, new GivenSeat(x, z, p.Turn));
        }).ToList();
    }

    /// <summary>How many pads a type's place list fixes (0 = none: the pads are searched).</summary>
    private int ListedPadCount(string planetKey) => _content.PlacesFor(planetKey)?.OfKind(PlaceListKinds.Pad).Count() ?? 0;

    /// <summary>The pads of a body whose type lists them, in list order, or null when it lists none. The generator must be
    /// configured for the body (<c>ComputeLandingPadsUncached</c>): each pad is decided on its own ground, never nudged.</summary>
    private List<LandingPad>? ListedPads(PlanetType planet, int circumference)
    {
        if (_content.PlacesFor(planet.Key) is not { } list || !list.OfKind(PlaceListKinds.Pad).Any())
        {
            return null;
        }

        var pads = new List<LandingPad>();
        foreach (var place in list.OfKind(PlaceListKinds.Pad))
        {
            var (x, z) = place.BlockOn(circumference);
            var pad = DecidePad(planet, pads.Count, x, z);
            pad.Molten = !pad.Islet && FootprintLava(planet, x, z, out _);
            pads.Add(pad);
        }

        return pads;
    }
}
