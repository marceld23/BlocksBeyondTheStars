// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lively places on the client: which inhabited place the player stands in right now, and the greetings that are
    /// due (the HUD shows them as the place banner). The server only names the places (<see cref="Places"/>); the
    /// <see cref="PlaceTracker"/> decides — twice a second — where the player is. Pure presentation: nothing here is sent
    /// back, and the server never learns whether a banner was shown.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>Seconds between two looks at where the player stands.</summary>
        private const float PlaceLookSeconds = 0.5f;

        /// <summary>The most greetings that wait for the HUD (two neighbouring places crossed in a sprint).</summary>
        private const int PlaceGreetingCap = 2;

        private static readonly NetPlace[] NoPlaces = System.Array.Empty<NetPlace>();
        private readonly PlaceTracker _placeTracker = new PlaceTracker();
        private float _placeLookAt;

        /// <summary>The places the player just walked into (or where a market just opened around them), oldest first —
        /// the HUD's celebration slot takes them when it is free.</summary>
        public readonly Queue<NetPlace> PlaceGreetings = new Queue<NetPlace>();

        /// <summary>The inhabited place the player stands in, or null (in the wild, in space, in flight).</summary>
        public NetPlace CurrentPlace => _placeTracker.Current;

        /// <summary>The server's list of the current world's places (an empty one clears the last world's).</summary>
        private void OnPlaces(PlaceList m)
        {
            Places = m.Places ?? NoPlaces;
            PlacesVersion++;
        }

        /// <summary>Update's step for the places: looks where the player is and queues a greeting when one is due.</summary>
        private void TickPlaces()
        {
            if (Time.unscaledTime < _placeLookAt)
            {
                return;
            }

            _placeLookAt = Time.unscaledTime + PlaceLookSeconds;
            // In space and in the space view there is no place; an empty list makes the tracker let go of the last one.
            bool onTheGround = World != null && Environment != null && !InSpace && !SpaceViewActive && !InEva && !OnFootInSpace;
            var p = PlayerPosition;
            var greet = _placeTracker.Update(onTheGround ? Places : NoPlaces, p.x, p.y, p.z, Circumference, Time.unscaledTimeAsDouble);
            if (greet != null && PlaceGreetings.Count < PlaceGreetingCap)
            {
                PlaceGreetings.Enqueue(greet);
            }
        }
    }
}
