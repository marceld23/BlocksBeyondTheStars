// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Sounders;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The hum of Soaniri's singing plants (#3358, #3367) — Unity-free so the headless tests can drive it. A strict budget:
    /// of the plants the client's scan found around the player, only the <see cref="MaxVoices"/> nearest within
    /// <see cref="Reach"/> sing, each on its own pooled loop voice, preferring different plant kinds (a reed, a bloom and a
    /// vine rather than three reeds). A plant within <see cref="SoundrakSilence"/> blocks of a Soundrak falls silent — the
    /// meadow's warning that one is near. The echo flower and the open bell bloom have no hum of their own (the echo flower
    /// answers with notes; the open bloom sings with the closed ones' loop).
    /// </summary>
    public static class SingingPlantChorus
    {
        /// <summary>The loop of the chime reed (bundled under <c>Resources/audio</c>).</summary>
        public const string ChimeReedLoop = "plant_chime_reed_loop";

        /// <summary>The loop of the bell bloom (closed and open).</summary>
        public const string BellBloomLoop = "plant_bell_bloom_loop";

        /// <summary>The loop of the harp vine.</summary>
        public const string HarpVineLoop = "plant_harp_vine_loop";

        /// <summary>At most this many plants hum at once.</summary>
        public const int MaxVoices = 3;

        /// <summary>A plant is heard within this distance (blocks).</summary>
        public const float Reach = 10f;

        /// <summary>…and at full level within this one.</summary>
        public const float FullWithin = 2.5f;

        /// <summary>A Soundrak this close to a plant silences it (blocks).</summary>
        public const float SoundrakSilence = 24f;

        /// <summary>The level of one plant's hum at full (the loops are soft beds, not cues).</summary>
        public const float Level = 0.35f;

        /// <summary>How far around the player the client scans for plants (blocks; the reach plus a margin).</summary>
        public const int ScanReach = 11;

        /// <summary>One singing plant the scan found: its cell centre and its loop.</summary>
        public readonly struct Plant
        {
            public Plant(float x, float y, float z, string loop)
            {
                X = x;
                Y = y;
                Z = z;
                Loop = loop;
            }

            public float X { get; }

            public float Y { get; }

            public float Z { get; }

            public string Loop { get; }
        }

        /// <summary>One picked voice: the plant (index into the scan) and its level 0–1.</summary>
        public readonly struct Voice
        {
            public Voice(int plant, float level)
            {
                Plant = plant;
                Level = level;
            }

            public int Plant { get; }

            public float Level { get; }
        }

        /// <summary>The loop a plant block hums, or null for a block that does not hum.</summary>
        public static string? LoopFor(string? blockKey) => blockKey switch
        {
            FloraCatalog.ChimeReedKey => ChimeReedLoop,
            FloraCatalog.BellBloomKey or FloraCatalog.BellBloomOpenKey => BellBloomLoop,
            FloraCatalog.HarpVineKey => HarpVineLoop,
            _ => null,
        };

        /// <summary>Whether the world's planet type grows singing plants at all (only then does the client scan for them).</summary>
        public static bool Sings(PlanetType? planet) => planet is { SingingMeadows: true };

        /// <summary>Whether a creature is a Soundrak (#3359: the authored species of that key).</summary>
        public static bool IsSoundrak(NetCreature? creature)
            => creature != null && string.Equals(creature.SpeciesId, SoundersFauna.SpeciesIdOf(SoundersIds.Soundrak), StringComparison.Ordinal);

        /// <summary>The level of a plant <paramref name="distance"/> blocks away: 1 within <see cref="FullWithin"/>, fading
        /// smoothly to 0 at <see cref="Reach"/>.</summary>
        public static float LevelAt(float distance)
        {
            if (distance <= FullWithin)
            {
                return 1f;
            }

            if (distance >= Reach)
            {
                return 0f;
            }

            float t = (distance - FullWithin) / (Reach - FullWithin);
            return 1f - (t * t * (3f - (2f * t)));
        }

        /// <summary>
        /// Picks the voices: plants within <see cref="Reach"/> of the listener that no Soundrak silences, nearest first, the
        /// first of each loop kind before a second of any kind, at most <see cref="MaxVoices"/>. <paramref name="soundraks"/>
        /// are the Soundraks' positions (already the copies nearest the listener). Fills <paramref name="into"/>.
        /// </summary>
        public static void Pick(IReadOnlyList<Plant> plants, float lx, float ly, float lz,
            IReadOnlyList<(float X, float Y, float Z)> soundraks, List<Voice> into)
        {
            into.Clear();
            var near = new List<(int Index, float Distance)>();
            for (int i = 0; i < plants.Count; i++)
            {
                var p = plants[i];
                float d = Distance(p.X - lx, p.Y - ly, p.Z - lz);
                if (d < Reach && !Silenced(p, soundraks))
                {
                    near.Add((i, d));
                }
            }

            near.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            var loops = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (index, d) in near)
            {
                if (into.Count < MaxVoices && loops.Add(plants[index].Loop))
                {
                    into.Add(new Voice(index, LevelAt(d)));
                }
            }

            foreach (var (index, d) in near)
            {
                if (into.Count >= MaxVoices)
                {
                    break;
                }

                if (!into.Exists(v => v.Plant == index))
                {
                    into.Add(new Voice(index, LevelAt(d)));
                }
            }
        }

        /// <summary>Whether a Soundrak stands within <see cref="SoundrakSilence"/> of the plant.</summary>
        public static bool Silenced(Plant plant, IReadOnlyList<(float X, float Y, float Z)> soundraks)
        {
            foreach (var (x, y, z) in soundraks)
            {
                if (Distance(plant.X - x, plant.Y - y, plant.Z - z) < SoundrakSilence)
                {
                    return true;
                }
            }

            return false;
        }

        private static float Distance(float dx, float dy, float dz) => (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
