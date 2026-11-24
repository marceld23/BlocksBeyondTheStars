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
/// <item><b>Landmarks</b> — every <c>landmark</c> place and every member of a <c>complex</c>, in list order, is a given landmark
/// (<see cref="GivenLandmarkSource"/>'s default, <see cref="PlaceListLandmarks"/>): its template on a <see cref="GivenSeat"/> at
/// the place's block column and turn (a complex member: at the anchor plus its offset, on the complex's shared ground row),
/// pinned under record <see cref="GivenLandmarkIndexBase"/> + its position among them (<see cref="PlaceRules.ListedLandmarks"/>).</item>
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

    /// <summary>The given landmarks of the active body from its type's place list (empty without one), in record order
    /// (<see cref="PlaceRules.ListedLandmarks"/>): each <c>landmark</c> place's template at its block column and turn on the
    /// active world's circumference, and each member of a <c>complex</c> at the anchor plus its offset, unturned, on the
    /// complex's one ground row (<see cref="ComplexGroundY"/>).</summary>
    private IReadOnlyList<GivenLandmarkPlace> PlaceListLandmarks(string locationId)
    {
        if (!string.Equals(_world.LocationId, locationId, System.StringComparison.Ordinal)
            || _content.PlacesFor(_world.Planet.Key) is not { } list)
        {
            return System.Array.Empty<GivenLandmarkPlace>();
        }

        var grounds = new Dictionary<PlaceDefinition, int>();
        var places = new List<GivenLandmarkPlace>();
        foreach (var listed in PlaceRules.ListedLandmarks(list))
        {
            var template = _content.LandmarkByKey(listed.Template);
            var (x, z) = listed.CentreOn(_world.Circumference, template?.Width ?? 0, template?.Length ?? 0);
            int? groundY = null;
            if (listed.Member is not null)
            {
                if (!grounds.TryGetValue(listed.Place, out int y))
                {
                    grounds[listed.Place] = y = ComplexGroundY(listed.Place);
                }

                groundY = y;
            }

            places.Add(new GivenLandmarkPlace(listed.Template, new GivenSeat(x, z, listed.Place.Turn, groundY)));
        }

        return places;
    }

    /// <summary>The one ground row of a complex (#3986): the median natural ground height of all its members' footprints
    /// together (each sampled like a single footprint, <see cref="FootprintSamples"/>) — the shelf's rule over the whole
    /// complex, so every part is graded to the same row and their corridors meet.</summary>
    private int ComplexGroundY(PlaceDefinition complex)
    {
        var (ax, az) = complex.BlockOn(_world.Circumference);
        var heights = new List<int>();
        foreach (var member in complex.Members)
        {
            if (_content.LandmarkByKey(member.Template) is not { } t)
            {
                continue;
            }

            foreach (var (x, z) in FootprintSamples(ax + member.East, az + member.North, t.Width, t.Length))
            {
                heights.Add(_generator.SurfaceHeight(_world.Planet, x, z));
            }
        }

        if (heights.Count == 0)
        {
            return _generator.SurfaceHeight(_world.Planet, ax, az);
        }

        heights.Sort();
        return heights[heights.Count / 2];
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
