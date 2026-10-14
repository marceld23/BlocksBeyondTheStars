// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The push-to-talk key's rules (#2568), engine-free so the headless suite pins them. The key used to default to V —
    /// the camera view's key, so every word spoken flipped the view — and the settings picker cycled through keys that
    /// were mostly bound to something else (B stealth, T trade, F fire, the middle mouse button, Ctrl crouch). Now the
    /// default is Caps Lock (read by no other control, on foot or at the helm), a saved V moves to it once, and the
    /// picker offers only the candidates no input action is bound to at the moment. Key names are
    /// <c>UnityEngine.KeyCode</c> names, the form the setting is stored in.
    /// </summary>
    public static class PushToTalkKeys
    {
        /// <summary>The default key.</summary>
        public const string Default = "CapsLock";

        /// <summary>The default before #2568 — the camera view's key.</summary>
        public const string LegacyDefault = "V";

        /// <summary>The keys the settings picker cycles through, in this order — minus the ones an action is bound to
        /// (<see cref="Free"/>). No candidate is read by a hard-wired control (movement, jump, crouch on Ctrl/C, sprint,
        /// the hotbar numbers, Escape/Tab/Enter). V, B, T, G and H are bound by default and stay in the list because
        /// they become free once the player rebinds their action elsewhere. Mouse3/Mouse4 are the side buttons.</summary>
        public static readonly IReadOnlyList<string> Candidates = new[]
        {
            "CapsLock", "Z", "Y", "LeftAlt", "Mouse3", "Mouse4", "V", "B", "T", "G", "H",
        };

        /// <summary>The candidates no action is bound to, in picker order. <paramref name="boundKeys"/> are the live key
        /// names of every input action (the player's rebinds included); names compare case-insensitively.</summary>
        public static List<string> Free(IEnumerable<string>? boundKeys)
        {
            var bound = new HashSet<string>(boundKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var free = new List<string>(Candidates.Count);
            foreach (var key in Candidates)
            {
                if (!bound.Contains(key))
                {
                    free.Add(key);
                }
            }

            return free;
        }

        /// <summary>The picker's next key after <paramref name="current"/>: the next free candidate in cycle order. A
        /// current key outside the free list (an older pick that an action uses now) steps to the first free one. With
        /// every candidate bound the default is the answer.</summary>
        public static string Next(string? current, IEnumerable<string>? boundKeys)
        {
            var free = Free(boundKeys);
            if (free.Count == 0)
            {
                return Default;
            }

            int index = free.FindIndex(k => string.Equals(k, current, StringComparison.OrdinalIgnoreCase));
            return free[(index + 1) % free.Count]; // not found (−1) → the first free key
        }

        /// <summary>The one-time settings migration (#2568): a saved key that still is the old default V moves to
        /// <see cref="Default"/>, an empty one too; any other choice stays. <paramref name="migrated"/> is the settings'
        /// done-flag — once it is set, a V the player picks later on purpose (after moving the camera view elsewhere)
        /// is kept.</summary>
        public static string Migrate(string? saved, bool migrated)
        {
            if (string.IsNullOrEmpty(saved))
            {
                return Default;
            }

            return !migrated && string.Equals(saved, LegacyDefault, StringComparison.OrdinalIgnoreCase) ? Default : saved!;
        }

        /// <summary>Locale key of a key's on-screen name where the KeyCode name reads badly ("CapsLock", "Mouse3"), or
        /// null — then the KeyCode name itself is the label ("V", "Z").</summary>
        public static string? LabelKey(string? keyName) => keyName switch
        {
            "CapsLock" => "ui.key.caps_lock",
            "LeftAlt" => "ui.key.left_alt",
            "Mouse2" => "ui.key.mouse_middle",
            "Mouse3" => "ui.key.mouse_4",
            "Mouse4" => "ui.key.mouse_5",
            _ => null,
        };
    }
}
