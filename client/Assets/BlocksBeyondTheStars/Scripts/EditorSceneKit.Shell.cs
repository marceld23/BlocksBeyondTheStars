// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The build editors' helpers for life on the <see cref="EditorShell"/> (#3691, epic #3696): destroying a rebuilt UI
    /// part in play mode and in the edit-mode tests alike, and the per-player switches an editor remembers (whether its
    /// "Advanced" section is open).
    /// </summary>
    internal static partial class EditorSceneKit
    {
        /// <summary>Destroys <paramref name="target"/> — at the end of the frame in play mode, at once in edit mode (the
        /// EditMode tests build the editors for real; <see cref="Object.Destroy(Object)"/> is an error there).</summary>
        public static void Discard(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>The player-preference key of an editor switch: <c>bbs.editor.&lt;editor&gt;.&lt;name&gt;</c>.</summary>
        public static string FlagKey(string editorKey, string name) => "bbs.editor." + editorKey + "." + name;

        /// <summary>
        /// A switch the editor remembers for this player across sessions — e.g. whether its "Advanced" section is open.
        /// Kept in <see cref="PlayerPrefs"/> (the registry on Windows, browser storage on the web): a convenience that may come
        /// back empty (a new PC, cleared data), so <paramref name="fallback"/> is what an editor shows the first time.
        /// </summary>
        public static bool RememberedFlag(string key, bool fallback)
        {
            try
            {
                return PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) != 0 : fallback;
            }
            catch (System.Exception)
            {
                return fallback; // storage blocked: the default look, nothing lost
            }
        }

        /// <summary>Remembers a switch read by <see cref="RememberedFlag"/>.</summary>
        public static void RememberFlag(string key, bool value)
        {
            try
            {
                PlayerPrefs.SetInt(key, value ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch (System.Exception)
            {
                // A convenience only: the switch works for this session either way.
            }
        }
    }
}
