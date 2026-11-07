// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Hidden blueprints (#3350): which of the blueprints with a <c>hiddenUntil</c> the server lets this player see.</summary>
    public sealed partial class NetworkClient
    {
        private readonly HashSet<string> _shownHiddenBlueprints = new(StringComparer.Ordinal);

        /// <summary>The set of hidden blueprints this player may see changed (<see cref="BlueprintVisibility"/>).</summary>
        public event Action? BlueprintVisibilityChanged;

        /// <summary>The hidden blueprints this player may see now (from the server's <see cref="BlueprintVisibility"/>).</summary>
        public IReadOnlyCollection<string> ShownHiddenBlueprints => _shownHiddenBlueprints;

        /// <summary>Whether the research tree lists <paramref name="bp"/> for this player — <see cref="HiddenBlueprints.IsShown"/>
        /// with the server's list and the player's researched set.</summary>
        public bool IsBlueprintShown(BlueprintDefinition bp, ICollection<string> researched)
            => HiddenBlueprints.IsShown(bp, researched, _shownHiddenBlueprints);

        private void HandleBlueprintVisibility(BlueprintVisibility m)
        {
            _shownHiddenBlueprints.Clear();
            foreach (var key in m.Shown ?? Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(key))
                {
                    _shownHiddenBlueprints.Add(key);
                }
            }

            BlueprintVisibilityChanged?.Invoke();
        }

        /// <summary>A fresh session shows no hidden blueprint until the server says which.</summary>
        private void ResetStoryPackState() => _shownHiddenBlueprints.Clear();
    }

    /// <summary>The client's rule for hidden blueprints (#3350) — Unity-free, unit-tested.</summary>
    public static class HiddenBlueprints
    {
        /// <summary>Whether the research tree lists <paramref name="bp"/>: always without a <c>hiddenUntil</c>; with one, only
        /// when the player owns it or the server listed it as visible (<paramref name="shown"/>). An older server sends no list —
        /// it has no hidden blueprint either, so nothing it knows is hidden by mistake.</summary>
        public static bool IsShown(BlueprintDefinition bp, ICollection<string>? researched, ICollection<string>? shown)
        {
            if (bp is null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(bp.HiddenUntil))
            {
                return true;
            }

            return (researched?.Contains(bp.Key) ?? false) || (shown?.Contains(bp.Key) ?? false);
        }
    }
}
