// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The landing-pad lists the client knows, one per body (#2740). The server answers every pad request (the flight
    /// pad chooser asks about any body, a padless one included) and re-sends the arrival body's list on every world
    /// entry; the client used to keep a single slot that every <see cref="LandingPadList"/> overwrote, so the world map —
    /// which read that slot without checking the body — lost its pad markers after the chooser had looked at another
    /// body, or drew the previous body's pads after a recovery to another one. Each view now reads the list of the body
    /// it shows. An empty <see cref="LandingPadList.BodyId"/> is the chooser's "the current body" request, echoed back,
    /// and is kept under that empty key.
    /// </summary>
    public sealed class LandingPadLists
    {
        private readonly Dictionary<string, LandingPadList> _byBody = new Dictionary<string, LandingPadList>(StringComparer.Ordinal);

        /// <summary>Bumped on every stored list — a view redraws its markers when it changes.</summary>
        public int Version { get; private set; }

        /// <summary>How many bodies have a list.</summary>
        public int Count => _byBody.Count;

        /// <summary>Stores a list for its body, replacing that body's previous one and no other.</summary>
        public void Apply(LandingPadList? message)
        {
            if (message is null)
            {
                return;
            }

            _byBody[message.BodyId ?? string.Empty] = message;
            Version++;
        }

        /// <summary>The pads of <paramref name="bodyId"/>, or null when no list for that body has arrived (a body without
        /// pads has an empty array).</summary>
        public NetLandingPad[]? PadsFor(string? bodyId)
            => bodyId != null && _byBody.TryGetValue(bodyId, out var list) ? list.Pads ?? Array.Empty<NetLandingPad>() : null;

        /// <summary>The day fraction a landing on <paramref name="bodyId"/> arrives at, from its last list; null when unknown.</summary>
        public float? TimeOfDayFor(string? bodyId)
            => bodyId != null && _byBody.TryGetValue(bodyId, out var list) ? list.TimeOfDay : (float?)null;

        /// <summary>Forgets every list (leaving the world).</summary>
        public void Clear()
        {
            _byBody.Clear();
            Version++;
        }
    }
}
