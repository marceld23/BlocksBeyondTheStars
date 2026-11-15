// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>What is wrong with a key typed into a developer editor (#3694).</summary>
    public enum ContentKeyProblem
    {
        /// <summary>A usable key.</summary>
        None,

        /// <summary>Nothing typed (blanks only).</summary>
        Empty,

        /// <summary>A space inside the key ("my item") — the most common slip, so it gets its own message.</summary>
        Space,

        /// <summary>Any other character than a–z, 0–9 and _ (capitals, a hyphen, an umlaut).</summary>
        BadCharacter,
    }

    /// <summary>
    /// The rule for the keys the developer editors export (#3694: item &amp; recipe, material). A key names the item or the
    /// block in <c>data/*.json</c>, and the merge tools build locale keys, recipe and blueprint keys and file names from it —
    /// every shipped key uses only a–z, 0–9 and _. Before, the editors silently rewrote a bad key on export ("My Item!"
    /// became "my_item"), so the bundle carried another key than the one on screen; now the editor names the problem and
    /// Save waits until it is fixed. Blanks around the key are ignored (<see cref="Clean"/>). Unity-free, so the rule is
    /// tested headless.
    /// </summary>
    public static class ContentKeyRules
    {
        /// <summary>The key as it is checked and exported: without blanks around it.</summary>
        public static string Clean(string? key) => (key ?? string.Empty).Trim();

        /// <summary>The problem with <paramref name="key"/> (after <see cref="Clean"/>), or <see cref="ContentKeyProblem.None"/>.</summary>
        public static ContentKeyProblem Check(string? key)
        {
            string k = Clean(key);
            if (k.Length == 0)
            {
                return ContentKeyProblem.Empty;
            }

            bool bad = false;
            foreach (char c in k)
            {
                if (char.IsWhiteSpace(c))
                {
                    return ContentKeyProblem.Space;
                }

                bad |= !IsKeyChar(c);
            }

            return bad ? ContentKeyProblem.BadCharacter : ContentKeyProblem.None;
        }

        /// <summary>True for a–z, 0–9 and _ — the characters every shipped key is made of.</summary>
        public static bool IsKeyChar(char c) => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
    }
}
