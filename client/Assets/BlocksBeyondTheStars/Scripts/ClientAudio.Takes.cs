// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Takes of the most repeated sounds (<see cref="ClipTakes"/>): mining, placing, melee, the weapons, landing and
    /// jumping play a different recording each time, never the same twice in a row. <see cref="Cue"/> and
    /// <see cref="At"/> resolve a rotating family's plain name through <see cref="Take"/>; every other id passes
    /// through untouched. A new take is just a file (<c>mine_stone_4.mp3</c>) — found by its name on first use.
    /// </summary>
    public sealed partial class ClientAudio
    {
        private Dictionary<string, string[]> _takes;
        private readonly Dictionary<string, int> _lastTake = new Dictionary<string, int>();

        /// <summary>The clip name to play for <paramref name="id"/>: the next take of a rotating family, else the id itself.</summary>
        private string Take(string id)
        {
            _takes ??= FindTakes();
            if (id == null || !_takes.TryGetValue(id, out var takes))
            {
                return id;
            }

            int previous = _lastTake.TryGetValue(id, out int last) ? last : -1;
            int pick = ClipTakes.Next(takes.Length, previous, _rng.NextDouble());
            _lastTake[id] = pick;
            return takes[pick];
        }

        /// <summary>The rotating families that really have more than one bundled take.</summary>
        private Dictionary<string, string[]> FindTakes()
        {
            var found = new Dictionary<string, string[]>();
            foreach (string family in ClipTakes.Families)
            {
                var takes = ClipTakes.Find(family, _clips.ContainsKey);
                if (takes.Length > 1)
                {
                    found[family] = takes;
                }
            }

            return found;
        }

        /// <summary>The bundled recording <paramref name="id"/>, or null when only a code-made stand-in (or nothing) exists.</summary>
        private UnityEngine.AudioClip Recorded(string id)
            => _clips.TryGetValue(id, out var clip) && _bundled.Contains(clip) ? clip : null;

        /// <summary>The sound of a block being set down, by its material (<see cref="BlockSoundRules"/>).</summary>
        private string PlaceCue(string blockKey) => BlockSoundRules.PlaceClip(blockKey, _clips.ContainsKey);
    }
}
