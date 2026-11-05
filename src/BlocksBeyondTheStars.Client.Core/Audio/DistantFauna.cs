// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What the listener's spot is like right now, read by <see cref="DistantFauna.Tick"/> (#3245).</summary>
    public readonly struct DistantFaunaConditions
    {
        public DistantFaunaConditions(float daylight01, bool raining, bool storm, bool roofed, bool vacuum, bool inSpace,
            bool cutscene, bool nearWater, bool underground, Func<string, bool>? speciesNearby = null)
        {
            Daylight01 = daylight01;
            Raining = raining;
            Storm = storm;
            Roofed = roofed;
            Vacuum = vacuum;
            InSpace = inSpace;
            Cutscene = cutscene;
            NearWater = nearWater;
            Underground = underground;
            SpeciesNearby = speciesNearby;
        }

        /// <summary>How light it is: 0 = night, 1 = noon (the twilight lies in between).</summary>
        public float Daylight01 { get; }

        public bool Raining { get; }

        public bool Storm { get; }

        /// <summary>Under a roof — indoors, in a cabin, on a station deck.</summary>
        public bool Roofed { get; }

        /// <summary>No air to carry a sound.</summary>
        public bool Vacuum { get; }

        public bool InSpace { get; }

        public bool Cutscene { get; }

        /// <summary>Open water within earshot: only then may a water or shore species call.</summary>
        public bool NearWater { get; }

        /// <summary>Below ground (a cave, a deep shaft): only a cave species calls there, and nothing else does.</summary>
        public bool Underground { get; }

        /// <summary>True for a species id of which a creature is already within hearing — its own voice plays up close, so
        /// it does not call from afar as well. Null = none is near.</summary>
        public Func<string, bool>? SpeciesNearby { get; }

        /// <summary>Nothing calls from afar: rain or a storm drowns it, a roof or a vacuum keeps it out, space has no world
        /// around the listener, and a cutscene owns the sound.</summary>
        public bool Silenced => Raining || Storm || Roofed || Vacuum || InSpace || Cutscene;
    }

    /// <summary>One distant call (#3245): which species, and where around the listener it plays.</summary>
    public readonly struct DistantCall
    {
        public DistantCall(NetFaunaSpecies species, float angleRadians, float distance, float volume01)
        {
            Species = species;
            AngleRadians = angleRadians;
            Distance = distance;
            Volume01 = volume01;
        }

        /// <summary>The calling species — its voice comes from <c>CreatureVoices.Derive(VoiceSeed, VoiceTraits)</c>.</summary>
        public NetFaunaSpecies Species { get; }

        /// <summary>The bearing around the listener in the horizontal plane, uniform in [0, 2π).</summary>
        public float AngleRadians { get; }

        /// <summary>How far out the caller is, in blocks: <see cref="DistantFauna.MinDistance"/> to
        /// <see cref="DistantFauna.MaxDistance"/>, the far end only for big species.</summary>
        public float Distance { get; }

        /// <summary>The call's volume, <see cref="DistantFauna.FarVolume"/> at the far end up to
        /// <see cref="DistantFauna.NearVolume"/> at the near end.</summary>
        public float Volume01 { get; }
    }

    /// <summary>
    /// The distant fauna (#3245): now and then one of the world's own species calls from far away, out of sight, so a
    /// living world sounds alive even when no animal is near. The server sends the world's species once per world entry
    /// (<see cref="FaunaRoster"/>); the Unity side hands it to <see cref="SetRoster"/> and calls <see cref="Tick"/> every
    /// frame, playing each returned call at its bearing and distance. The rules:
    /// <list type="bullet">
    /// <item>one call every <see cref="MinIntervalSeconds"/>–<see cref="MaxIntervalSeconds"/> seconds (uniform), the first
    /// one that long after the roster arrived;</item>
    /// <item>none while <see cref="DistantFaunaConditions.Silenced"/> (rain, storm, a roof, a vacuum, space, a cutscene) — a
    /// call that came due meanwhile waits, and comes <see cref="MinGraceSeconds"/>–<see cref="MaxGraceSeconds"/> seconds
    /// after the silence lifts, so stepping out of a door is not answered by an animal on cue;</item>
    /// <item>the species fits the hour: a species that is awake now always may call, a sleeping one only now and then
    /// (<see cref="OffHourChance"/>) — nocturnal ones mostly at night, diurnal ones by day, crepuscular ones at dawn and
    /// dusk, cathemeral ones at any hour;</item>
    /// <item>the species fits the spot: a water or shore species only near water, a cave species only underground — and
    /// underground nothing but a cave species;</item>
    /// <item>a species already within hearing (<see cref="DistantFaunaConditions.SpeciesNearby"/>) does not call from afar
    /// as well;</item>
    /// <item>never the same species twice in a row while another one may call;</item>
    /// <item>a near-silent body (a medusa, anything with tentacles) only rarely (<see cref="QuietBodyChance"/>);</item>
    /// <item>the caller stands <see cref="MinDistance"/>–<see cref="MaxDistance"/> blocks out (a small animal is not heard
    /// from the far end), at a uniform bearing, and the farther it is the quieter it sounds.</item>
    /// </list>
    /// A slot in which nothing may call stays silent and the next one is scheduled. Seeded, so the tests are deterministic.
    /// </summary>
    public sealed class DistantFauna
    {
        public const float MinIntervalSeconds = 20f;
        public const float MaxIntervalSeconds = 60f;
        public const float MinGraceSeconds = 2f;
        public const float MaxGraceSeconds = 8f;
        public const float MinDistance = 40f;
        public const float MaxDistance = 120f;

        /// <summary>The farthest a small animal (<see cref="SmallSize"/> or less) is heard from.</summary>
        public const float SmallMaxDistance = 75f;

        public const float NearVolume = 0.6f;
        public const float FarVolume = 0.35f;

        /// <summary>The chance a species that sleeps at this hour may still call in a slot.</summary>
        public const double OffHourChance = 0.15;

        /// <summary>The chance a near-silent body (a medusa, tentacles) may call in a slot.</summary>
        public const double QuietBodyChance = 0.12;

        /// <summary>Species size at and below which the call stays within <see cref="SmallMaxDistance"/>, and at and above
        /// which it may come from <see cref="MaxDistance"/>.</summary>
        public const float SmallSize = 0.6f;

        public const float BigSize = 2.2f;

        /// <summary>Daylight below which it is night (a nocturnal species is awake).</summary>
        public const float NightBelow = 0.25f;

        /// <summary>Daylight above which it is day (a diurnal species is awake).</summary>
        public const float DayAbove = 0.35f;

        /// <summary>The twilight band (a crepuscular species is awake): dawn and dusk, between deep night and full day.</summary>
        public const float TwilightFrom = 0.08f;

        public const float TwilightTo = 0.6f;

        private readonly Random _rng;
        private readonly List<NetFaunaSpecies> _roster = new List<NetFaunaSpecies>();
        private readonly List<NetFaunaSpecies> _pool = new List<NetFaunaSpecies>(); // reused per slot
        private float _dueAt = float.NaN; // NaN = schedule on the next tick (a new roster)
        private bool _heldBack;
        private string? _lastSpeciesId;

        public DistantFauna(int seed)
        {
            _rng = new Random(seed);
        }

        /// <summary>How many species may call (the last roster's).</summary>
        public int SpeciesCount => _roster.Count;

        /// <summary>Replaces the roster (a new world) and starts the timers over: the first call comes a full interval later,
        /// and the last caller is forgotten.</summary>
        public void SetRoster(IReadOnlyList<NetFaunaSpecies>? roster)
        {
            _roster.Clear();
            if (roster is not null)
            {
                foreach (var sp in roster)
                {
                    if (sp is not null)
                    {
                        _roster.Add(sp);
                    }
                }
            }

            _dueAt = float.NaN;
            _heldBack = false;
            _lastSpeciesId = null;
        }

        /// <summary>Advances the scheduler to <paramref name="now"/> (seconds, any monotonic clock) and returns the call to
        /// play now, or null.</summary>
        public DistantCall? Tick(float now, in DistantFaunaConditions c)
        {
            if (_roster.Count == 0)
            {
                return null;
            }

            if (float.IsNaN(_dueAt))
            {
                _dueAt = now + Range(MinIntervalSeconds, MaxIntervalSeconds);
                return null;
            }

            if (c.Silenced)
            {
                _heldBack |= now >= _dueAt;
                return null;
            }

            if (_heldBack)
            {
                _heldBack = false;
                _dueAt = Math.Max(_dueAt, now + Range(MinGraceSeconds, MaxGraceSeconds));
            }

            if (now < _dueAt)
            {
                return null;
            }

            _dueAt = now + Range(MinIntervalSeconds, MaxIntervalSeconds);
            var species = Pick(c);
            if (species is null)
            {
                return null;
            }

            _lastSpeciesId = species.SpeciesId;
            return Place(species);
        }

        /// <summary>The species that may call in this slot, one of them picked uniformly — or null when none may.</summary>
        private NetFaunaSpecies? Pick(in DistantFaunaConditions c)
        {
            _pool.Clear();
            int others = 0;
            foreach (var sp in _roster)
            {
                if (!FitsSpot(sp, c.NearWater, c.Underground) || (c.SpeciesNearby?.Invoke(sp.SpeciesId) ?? false))
                {
                    continue;
                }

                if (!AwakeAt(sp.Activity, c.Daylight01) && _rng.NextDouble() >= OffHourChance)
                {
                    continue;
                }

                if (IsQuietBody(sp) && _rng.NextDouble() >= QuietBodyChance)
                {
                    continue;
                }

                _pool.Add(sp);
                if (!string.Equals(sp.SpeciesId, _lastSpeciesId, StringComparison.Ordinal))
                {
                    others++;
                }
            }

            if (others > 0 && others < _pool.Count)
            {
                _pool.RemoveAll(sp => string.Equals(sp.SpeciesId, _lastSpeciesId, StringComparison.Ordinal));
            }

            return _pool.Count == 0 ? null : _pool[_rng.Next(_pool.Count)];
        }

        private DistantCall Place(NetFaunaSpecies species)
        {
            float angle = (float)(_rng.NextDouble() * 2.0 * Math.PI);
            float reach = Clamp01((species.Size - SmallSize) / (BigSize - SmallSize));
            float farthest = SmallMaxDistance + (MaxDistance - SmallMaxDistance) * reach;
            float distance = Range(MinDistance, farthest);
            float volume = NearVolume + (FarVolume - NearVolume) * ((distance - MinDistance) / (MaxDistance - MinDistance));
            return new DistantCall(species, angle, distance, volume);
        }

        /// <summary>Whether a species lives where a call could come from: water and shore species only near water, cave
        /// species only underground, and underground nothing else.</summary>
        public static bool FitsSpot(NetFaunaSpecies species, bool nearWater, bool underground)
        {
            string habitat = species.Habitat ?? string.Empty;
            if (Is(habitat, "Cave"))
            {
                return underground;
            }

            if (underground)
            {
                return false;
            }

            return nearWater || !(Is(habitat, "Water") || Is(habitat, "Amphibian"));
        }

        /// <summary>Whether a species of <paramref name="activity"/> is awake at <paramref name="daylight01"/>: nocturnal below
        /// <see cref="NightBelow"/>, diurnal above <see cref="DayAbove"/>, crepuscular within the twilight band, cathemeral
        /// (and anything unknown) always.</summary>
        public static bool AwakeAt(string? activity, float daylight01)
        {
            if (Is(activity, "Nocturnal"))
            {
                return daylight01 < NightBelow;
            }

            if (Is(activity, "Diurnal"))
            {
                return daylight01 > DayAbove;
            }

            if (Is(activity, "Crepuscular"))
            {
                return daylight01 > TwilightFrom && daylight01 < TwilightTo;
            }

            return true;
        }

        /// <summary>A medusa or anything with tentacles — near-silent bodies that only rarely call.</summary>
        public static bool IsQuietBody(NetFaunaSpecies species)
            => species.Tentacles > 0 || Is(species.BodyPlan, "Medusa");

        private static bool Is(string? value, string name) => string.Equals(value, name, StringComparison.OrdinalIgnoreCase);

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        private float Range(float min, float max) => min + (float)_rng.NextDouble() * (max - min);
    }
}
