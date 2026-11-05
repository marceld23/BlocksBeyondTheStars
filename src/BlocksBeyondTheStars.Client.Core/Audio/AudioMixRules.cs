// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>How far a positional one-shot carries (#3249).</summary>
    public enum AudioRange
    {
        Small,  // footfalls, wing beats, splashes, breath: an arm's length to a few strides
        Medium, // the default: voices, tools, devices (the old 4–20 m curve)
        Large,  // giants, blasts, warp flashes
        Huge,   // a ship launching or landing, an explosion in space
    }

    /// <summary>The voice budget a one-shot counts against (#3249).</summary>
    public enum AudioCategory
    {
        Other,
        Creature,     // creature calls and combat cues
        Foley,        // creature footfalls, wing beats, slither, splashes
        RemotePlayer, // other players' footsteps and landings
    }

    /// <summary>
    /// The mixing rules every 3D one-shot goes through (#3249), picked by the cue id so no call site has to know them.
    /// <list type="bullet">
    /// <item>A distance profile: before this every one-shot used the same 4–20 m linear curve, so a sandworm breach and
    /// a cricket both ended at 20 m and a ship landing 56 blocks up was inaudible.</item>
    /// <item>A per-category voice budget: a refused request is dropped (never queued), the nearest wins — a herd or a
    /// crowd never floods the pool and steals the player's own sounds.</item>
    /// <item>Roof occlusion: a source on the other side of a roof than the listener is muffled (the weather beds' rule,
    /// #2805).</item>
    /// </list>
    /// Unity-free so the tables are unit-tested.
    /// </summary>
    public static class AudioMixRules
    {
        /// <summary>The level of a one-shot heard through a roof (or a hull) — the baked muffle carries the rest.</summary>
        public const float OccludedLevel = 0.5f;

        /// <summary>Closer than this nothing is occluded: a sound that near is rarely behind a wall, and a wrong muffle on
        /// the player's own mining target would read as a bug.</summary>
        public const float OcclusionMinDistance = 6f;

        private static readonly string[] SmallPrefixes =
        {
            "step_", "wing_flap", "slither", "swim_", "water_splash", "breath_", "heartbeat", "surface_gasp", "climb_",
            "suit_thruster", "land", "jump", "eat",
        };

        private static readonly string[] LargePrefixes =
        {
            "giant_stomp", "sandworm_", "leviathan_", "thumper_", "terrain_blast", "geyser_", "horn_", "tank_spider_burst",
            "guardian_bay", "asteroid_break", "tree_fall", "raider_warp_in", "hunter_shuttle_warp",
            "ship_laser", "ship_weapon", "ship_mine", // #3248: other ships' shots carry across the flight scale
        };

        private static readonly string[] HugePrefixes =
        {
            "ship_destroyed", "ship_launch", "ship_touchdown", "ship_landing", "engine_thrust", "engine_ignite", "sonic_boom",
            "sky_giant_call", "reentry_roar",
        };

        private static readonly string[] FoleyPrefixes = { "wing_flap", "slither", "creature_step", "creature_splash" };

        /// <summary>The distance profile for a cue id.</summary>
        public static AudioRange RangeFor(string? id)
        {
            string k = id ?? string.Empty;
            if (StartsWithAny(k, HugePrefixes))
            {
                return AudioRange.Huge;
            }

            if (StartsWithAny(k, LargePrefixes))
            {
                return AudioRange.Large;
            }

            return StartsWithAny(k, SmallPrefixes) ? AudioRange.Small : AudioRange.Medium;
        }

        /// <summary>Full volume inside <c>Min</c>, silent beyond <c>Max</c> (linear in between).</summary>
        public static (float Min, float Max) Distances(AudioRange range) => range switch
        {
            AudioRange.Small => (2f, 14f),
            AudioRange.Large => (10f, 80f),
            AudioRange.Huge => (20f, 160f),
            _ => (4f, 20f),
        };

        /// <summary>The voice budget a cue id counts against. Remote players' steps are tagged by the caller (their ids
        /// are the same step clips the player's own feet use).</summary>
        public static AudioCategory CategoryFor(string? id)
        {
            string k = id ?? string.Empty;
            if (StartsWithAny(k, FoleyPrefixes))
            {
                return AudioCategory.Foley;
            }

            return k.StartsWith("creature_", StringComparison.Ordinal) ? AudioCategory.Creature : AudioCategory.Other;
        }

        /// <summary>How many one-shots of a category may play at once.</summary>
        public static int BudgetFor(AudioCategory category) => category switch
        {
            AudioCategory.Creature => 4,
            AudioCategory.Foley => 6,
            AudioCategory.RemotePlayer => 3,
            _ => int.MaxValue,
        };

        /// <summary>Whether a new request may take a slot: under the budget always; at the budget only when it is closer
        /// than the farthest sound of its category now playing (which it then replaces).</summary>
        public static bool Admit(int playingInCategory, int budget, float distance, float farthestPlaying)
            => playingInCategory < budget || distance < farthestPlaying;

        /// <summary>Roof occlusion: muffled when exactly one of listener and source is under a roof and the source is not
        /// right beside the listener.</summary>
        public static bool Occluded(bool listenerRoofed, bool sourceRoofed, float distance)
            => listenerRoofed != sourceRoofed && distance >= OcclusionMinDistance;

        private static bool StartsWithAny(string key, string[] prefixes)
        {
            foreach (string p in prefixes)
            {
                if (key.StartsWith(p, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
