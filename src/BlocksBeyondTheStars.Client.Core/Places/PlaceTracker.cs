// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lively places, on the client: which inhabited place the player stands in (<see cref="NetPlace"/>, sent by the
    /// server with its footprint) and when to greet them with its name. Unity-free, so the headless tests drive it.
    /// <list type="bullet">
    /// <item><b>Inside</b> = within the place's footprint grown by <see cref="Margin"/> blocks, the short way round the
    /// world's east–west seam, and not far above or below its ground (a cave under a village is not the village).</item>
    /// <item><b>The greeting</b> comes once when the player walks in — and again only after they have been away for
    /// <see cref="GreetAgainSeconds"/>, or when something starts there while they stand in it (a market day, a festival).
    /// Walking along the edge does not make it flicker: leaving needs <see cref="LeaveMargin"/> more blocks than entering.</item>
    /// </list>
    /// </summary>
    public sealed class PlaceTracker
    {
        /// <summary>Blocks outside the footprint that still count as "in the place" on the way in.</summary>
        public const int Margin = 3;

        /// <summary>Blocks outside the footprint at which the player has left it (more than <see cref="Margin"/>).</summary>
        public const int LeaveMargin = 10;

        /// <summary>How far above or below the place's ground row the player may be (a tower's top, a cellar).</summary>
        public const int HeightReach = 40;

        /// <summary>Seconds away from a place before coming back is greeted again.</summary>
        public const double GreetAgainSeconds = 600.0;

        private readonly Dictionary<string, double> _lastSeenAt = new Dictionary<string, double>(StringComparer.Ordinal);
        private string _currentKey = string.Empty;
        private string _currentState = string.Empty;

        /// <summary>The place the player stands in, or null.</summary>
        public NetPlace? Current { get; private set; }

        /// <summary>Whether a position lies in a place's footprint grown by <paramref name="margin"/> blocks.</summary>
        public static bool Contains(NetPlace place, double x, double y, double z, int circumference, int margin)
        {
            // Measured from the footprint's middle, the short way round both seams (X at the circumference, Z at the
            // latitude period — the player's own position runs on unwrapped while they lap the world).
            double halfX = (place.MaxX - place.MinX + 1) / 2.0, halfZ = (place.MaxZ - place.MinZ + 1) / 2.0;
            double dx = WorldConstants.WrapDeltaX(x - (place.MinX + halfX), circumference);
            double dz = WorldConstants.WrapDeltaZ(z - (place.MinZ + halfZ), circumference);
            return Math.Abs(dx) <= halfX + margin && Math.Abs(dz) <= halfZ + margin
                   && Math.Abs(y - place.Y) <= HeightReach;
        }

        /// <summary>
        /// Looks where the player is. Returns the place to greet them with right now — the one they just walked into (unless
        /// they were there a moment ago), or the one they stand in when something started there — else null.
        /// </summary>
        public NetPlace? Update(IReadOnlyList<NetPlace> places, double x, double y, double z, int circumference, double now)
        {
            NetPlace? inside = null;
            foreach (var p in places)
            {
                // The place the player is already in holds on to them a little longer than a new one takes them in.
                bool mine = p.Key == _currentKey;
                if (Contains(p, x, y, z, circumference, mine ? LeaveMargin : Margin) && (inside is null || mine))
                {
                    inside = p;
                    if (mine)
                    {
                        break;
                    }
                }
            }

            Current = inside;
            if (inside is null)
            {
                _currentKey = string.Empty;
                _currentState = string.Empty;
                return null;
            }

            bool entered = inside.Key != _currentKey;
            bool somethingStarted = !entered && inside.State.Length > 0 && inside.State != _currentState;
            bool wasAway = !_lastSeenAt.TryGetValue(inside.Key, out double seen) || now - seen >= GreetAgainSeconds;
            _currentKey = inside.Key;
            _currentState = inside.State;
            _lastSeenAt[inside.Key] = now;
            return (entered && wasAway) || somethingStarted ? inside : null;
        }

        /// <summary>Forgets everything (a new world: its places have other keys anyway, and the first one is greeted).</summary>
        public void Reset()
        {
            _lastSeenAt.Clear();
            _currentKey = string.Empty;
            _currentState = string.Empty;
            Current = null;
        }
    }

    /// <summary>
    /// The lively places: the sound layer of the place the player stands in — a quiet bed of a village's or a town's day
    /// (far voices, steps, a door), louder and busier on a market day, a station's deck aboard one. Unity-free: it only says
    /// which loop and how loud; the client's audio plays it under the weather bed, muffled under a roof like every bed.
    /// A place is quiet at night, and a hamlet of three houses has no murmur at all.
    /// </summary>
    public static class PlaceAmbience
    {
        public const string None = "";
        public const string Village = "amb_place_village";
        public const string Town = "amb_place_town";
        public const string Market = "amb_place_market";
        public const string StationDeck = "amb_station_deck";

        /// <summary>The local day fractions between which a settlement's people are up and about (the layer fades in the
        /// evening and is silent at night).</summary>
        public const float DayFrom = 0.24f;
        public const float EveningFrom = 0.62f;
        public const float NightFrom = 0.80f;

        /// <summary>How loud a station's deck hums, by its size tier: a small outpost has a few machines, a colossal
        /// station a whole plant behind its walls (an unknown tier hums like a large one).</summary>
        public static float StationHumLevel(string? tier) => tier switch
        {
            "small" => 0.6f,
            "medium" => 0.7f,
            "large" => 0.8f,
            "huge" => 0.9f,
            "colossal" => 1f,
            _ => 0.8f,
        };

        /// <summary>The pitch the loop plays at (1 = as recorded). A settlement's bed is never bent; a station's hum is
        /// computed from what the station is — deeper the bigger it is, deeper still where ore is smelted
        /// (<see cref="WorldGeneration.StationPurposes.Mining"/>), a little brighter over a research station's
        /// instruments — so two stations do not sound like the same room.</summary>
        public static float PitchFor(NetPlace? place)
        {
            if (place is null || place.Kind != PlaceKinds.Station)
            {
                return 1f;
            }

            float size = place.Tier switch
            {
                "small" => 1.06f,
                "medium" => 1.03f,
                "huge" => 0.97f,
                "colossal" => 0.94f,
                _ => 1f,
            };
            float purpose = place.Purpose switch
            {
                WorldGeneration.StationPurposes.Mining => 0.94f,
                WorldGeneration.StationPurposes.Research => 1.05f,
                _ => 1f,
            };
            return size * purpose;
        }

        /// <summary>The loop of a place at a time of day ("" = none).</summary>
        public static string LoopFor(NetPlace? place, float timeOfDay)
        {
            if (place is null)
            {
                return None;
            }

            if (place.Kind == PlaceKinds.Station)
            {
                return StationDeck; // a station hums round the clock
            }

            if (LevelFor(place, timeOfDay) <= 0f)
            {
                return None;
            }

            if (place.State == PlaceStates.Market)
            {
                return Market;
            }

            return place.Tier is "town" or "city" or "metropolis" ? Town : Village;
        }

        /// <summary>How loud the place's layer plays, 0..1: by the size of the place and the time of day.</summary>
        public static float LevelFor(NetPlace? place, float timeOfDay)
        {
            if (place is null)
            {
                return 0f;
            }

            if (place.Kind == PlaceKinds.Station)
            {
                return StationHumLevel(place.Tier);
            }

            float size = place.Tier switch
            {
                "hamlet" => 0f, // three houses do not murmur
                "village" => 0.7f,
                "town" => 0.9f,
                _ => 1f,
            };
            if (place.State == PlaceStates.Market || place.State == PlaceStates.Festival)
            {
                size = Math.Max(size, 0.9f);
            }

            float day = timeOfDay < DayFrom || timeOfDay >= NightFrom ? 0f
                : timeOfDay >= EveningFrom ? 1f - (0.6f * ((timeOfDay - EveningFrom) / (NightFrom - EveningFrom)))
                : timeOfDay < DayFrom + 0.04f ? (timeOfDay - DayFrom) / 0.04f
                : 1f;
            return size * day;
        }
    }
}
