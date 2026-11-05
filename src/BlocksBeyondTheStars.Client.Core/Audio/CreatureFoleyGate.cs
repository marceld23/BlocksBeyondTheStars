// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One creature as the foley gate sees it this frame.</summary>
    public readonly struct FoleyCandidate
    {
        public FoleyCandidate(string id, string species, float size, float distance, bool moving, bool flier)
        {
            Id = id;
            Species = species;
            Size = size;
            Distance = distance;
            Moving = moving;
            Flier = flier;
        }

        public string Id { get; }

        public string Species { get; }

        public float Size { get; }

        public float Distance { get; }

        public bool Moving { get; }

        public bool Flier { get; }
    }

    /// <summary>
    /// The anti-clutter budget for creature movement sounds (#3242) — footfalls, wing beats, splashes, chewing.
    ///
    /// <para>Why it exists: foley added naively is the fastest way to ruin a soundscape. A herd of twelve four-legged
    /// grazers plants about two feet per animal per second — ~100 footfall events a second, which reads as rain or a
    /// rattle, not as animals. So:</para>
    /// <list type="bullet">
    /// <item><b>Per species, only the two nearest moving animals</b> make footfalls; the rest of the herd is silent, and a
    /// herd sounds like "some animals walking", never like a drum roll.</item>
    /// <item><b>Per animal</b>, at most one footfall per gait cycle (a big body steps slower), never for tiny animals,
    /// only while moving, only within a size-scaled range.</item>
    /// <item><b>Globally</b>, at most <see cref="MaxPerSecond"/> foley events a second and never two closer than
    /// <see cref="MinGap"/> — a token bucket over everything.</item>
    /// <item>Wing beats: only the two nearest fliers, each at most every <see cref="WingInterval"/>.</item>
    /// </list>
    /// The mixer adds a hard cap of concurrent foley voices on top (AudioMixRules, #3249). Unity-free and unit-tested.
    /// </summary>
    public sealed class CreatureFoleyGate
    {
        public const int MaxPerSecond = 6;
        public const float MinGap = 0.07f;
        public const int NearestPerSpecies = 2;
        public const int NearestFliers = 2;
        public const float MinSize = 0.6f;
        public const float WingInterval = 0.5f;

        private readonly HashSet<string> _walkers = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _fliers = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _next = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Queue<float> _recent = new Queue<float>();
        private readonly Dictionary<string, List<FoleyCandidate>> _bySpecies = new Dictionary<string, List<FoleyCandidate>>(StringComparer.Ordinal);
        private readonly List<FoleyCandidate> _flierScratch = new List<FoleyCandidate>();
        private float _last = float.NegativeInfinity;

        /// <summary>How far a footfall of a body this size carries (blocks).</summary>
        public static float Range(float size) => 12f * Clamp((float)Math.Sqrt(Math.Max(0f, size)), 0.8f, 2f);

        /// <summary>The shortest time between two footfalls of one animal — one per gait cycle; a big body steps slower.</summary>
        public static float FootfallInterval(float size) => Clamp(0.32f * (float)Math.Sqrt(Math.Max(0f, size)), 0.26f, 0.9f);

        /// <summary>Footfall level (before distance): clearly under the player's own steps, heavier for big bodies.</summary>
        public static float FootfallVolume(float size) => Clamp(0.18f + 0.1f * size, 0.22f, 0.5f);

        /// <summary>Footfall pitch: small feet patter high, big ones thud low.</summary>
        public static float FootfallPitch(float size) => Clamp(1.3f - 0.2f * size, 0.55f, 1.25f);

        /// <summary>Picks this frame's emitters: per species the nearest moving walkers, overall the nearest fliers.</summary>
        public void Rank(IEnumerable<FoleyCandidate> candidates)
        {
            _walkers.Clear();
            _fliers.Clear();
            _flierScratch.Clear();
            foreach (var list in _bySpecies.Values)
            {
                list.Clear();
            }

            foreach (var c in candidates)
            {
                if (!c.Moving || c.Size < MinSize)
                {
                    continue;
                }

                if (c.Flier)
                {
                    _flierScratch.Add(c);
                    continue;
                }

                if (c.Distance > Range(c.Size))
                {
                    continue;
                }

                if (!_bySpecies.TryGetValue(c.Species ?? string.Empty, out var list))
                {
                    list = new List<FoleyCandidate>();
                    _bySpecies[c.Species ?? string.Empty] = list;
                }

                list.Add(c);
            }

            foreach (var list in _bySpecies.Values)
            {
                list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                for (int i = 0; i < list.Count && i < NearestPerSpecies; i++)
                {
                    _walkers.Add(list[i].Id);
                }
            }

            _flierScratch.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            for (int i = 0; i < _flierScratch.Count && i < NearestFliers; i++)
            {
                _fliers.Add(_flierScratch[i].Id);
            }
        }

        /// <summary>May this animal's foot that just came down make a sound? Consumes a token when it may.</summary>
        public bool TryFootfall(string id, float size, float now)
            => _walkers.Contains(id) && size >= MinSize && TryAnimal(id, FootfallInterval(size), now);

        /// <summary>May this flier beat its wings audibly now?</summary>
        public bool TryWing(string id, float now) => _fliers.Contains(id) && TryAnimal(id, WingInterval, now);

        /// <summary>A one-off foley event outside the ranking (a splash, a chew): only the global budget applies, plus the
        /// animal's own spacing.</summary>
        public bool TryOneOff(string id, float minSpacing, float now) => TryAnimal(id, minSpacing, now);

        /// <summary>Forgets an animal that left the view.</summary>
        public void Forget(string id) => _next.Remove(id);

        private bool TryAnimal(string id, float interval, float now)
        {
            if (_next.TryGetValue(id, out float next) && now < next)
            {
                return false;
            }

            if (!TryGlobal(now))
            {
                return false;
            }

            _next[id] = now + interval;
            return true;
        }

        private bool TryGlobal(float now)
        {
            if (now - _last < MinGap)
            {
                return false;
            }

            while (_recent.Count > 0 && now - _recent.Peek() >= 1f)
            {
                _recent.Dequeue();
            }

            if (_recent.Count >= MaxPerSecond)
            {
                return false;
            }

            _recent.Enqueue(now);
            _last = now;
            return true;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
