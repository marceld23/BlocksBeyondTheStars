// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #2952 (a player's wish: "man ist ja der Entdecker!"): every player can give the star systems, planets and moons they
/// have visited a name of their own. The name is private — it lives in that player's state and only their own star map
/// carries it (<see cref="NetBody.CustomName"/>, <see cref="NetStarSystem.CustomName"/>); another player still reads the
/// generated name and can give the place a name of their own. Because nobody else ever reads it, it needs no content
/// screen — a length cap and the control-character strip, like every other name. The generated name stays the key the
/// sun colour, the flora and the trades are derived from.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Longest private place name, like a base name.</summary>
    internal const int PlaceNameMaxLength = 24;

    /// <summary>How many places one player can name — far more than a galaxy holds, a bound on a save's size.</summary>
    internal const int MaxPlaceNamesPerPlayer = 512;

    /// <summary>The map detail's "Your name" field: names (or, with an empty name, un-names) a visited place for this player.</summary>
    private void HandleSetPlaceName(PlayerSession session, SetPlaceNameIntent intent)
    {
        string id = intent.PlaceId ?? string.Empty;
        string? generated = NameablePlaceName(session, id);
        if (generated is null)
        {
            Reject(session, "place_name", "@srv.place_name.not_visited");
            return;
        }

        string name = SanitizePlaceName(intent.Name);
        var names = session.State.PlaceNames;
        names.TryGetValue(id, out string? current);
        if (name.Length == 0 || name == generated)
        {
            if (!names.Remove(id))
            {
                return; // already the generated name — nothing to save
            }
        }
        else if (name == current)
        {
            return;
        }
        else if (current is null && names.Count >= MaxPlaceNamesPerPlayer)
        {
            Reject(session, "place_name", "@srv.place_name.full");
            return;
        }
        else
        {
            names[id] = name;
        }

        _repo.SavePlayer(session.State);
        SendStarMap(session); // the map, the flight chart and the HUD read the name from the star map
    }

    /// <summary>The generated name of a place the player may name — a planet or moon they have landed on (or stand on
    /// now), a star system they have entered (or are in now) — or null for anything else: an unknown id, a place not
    /// visited yet, a station (its owner renames it for everyone), a wreck, an asteroid or debris field.</summary>
    private string? NameablePlaceName(PlayerSession session, string id)
    {
        if (id.Length == 0)
        {
            return null;
        }

        var here = ResolveLocationBody(session.CurrentLocationId);
        if (_galaxy.FindBody(id) is { } body)
        {
            bool visited = session.State.LandedBodies.Contains(id) || here?.Id == id;
            return visited && (body.Kind is CelestialKind.Planet or CelestialKind.Moon) ? body.Name : null;
        }

        foreach (var sys in _galaxy.Systems)
        {
            if (sys.Id == id)
            {
                return session.State.KnownSystems.Contains(id) || here?.SystemId == id ? sys.Name : null;
            }
        }

        return null;
    }

    private static string SanitizePlaceName(string? raw)
    {
        string trimmed = StripControlChars(raw).Trim();
        return trimmed.Length > PlaceNameMaxLength ? trimmed.Substring(0, PlaceNameMaxLength).TrimEnd() : trimmed;
    }

    /// <summary>Lays the receiving player's private names over one system of their star map (and its bodies).</summary>
    private static NetStarSystem WithPlaceNames(NetStarSystem system, PlayerState receiver)
    {
        var names = receiver.PlaceNames;
        if (names.Count == 0)
        {
            return system;
        }

        system.CustomName = names.TryGetValue(system.Id, out string? own) ? own : string.Empty;
        foreach (var body in system.Bodies)
        {
            body.CustomName = names.TryGetValue(body.Id, out string? mine) ? mine : string.Empty;
        }

        return system;
    }

    /// <summary>Test seam (#2952): the rename intent as a player sends it.</summary>
    internal void SetPlaceNameForTest(PlayerSession session, string placeId, string name)
        => HandleSetPlaceName(session, new SetPlaceNameIntent { PlaceId = placeId, Name = name });
}
