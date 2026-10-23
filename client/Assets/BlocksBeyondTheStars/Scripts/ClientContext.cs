// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The in-world panels of one world rig, by type — the per-<see cref="GameBootstrap"/> replacement for the panels'
    /// former <c>static Instance</c> singletons (#2913). A <see cref="UiPanel"/> registers itself when its
    /// <see cref="UiPanel.Game"/> is set (WorldRig assigns it right after <c>AddComponent</c>) and unregisters in its
    /// <c>OnDestroy</c>, so a reader asks <c>Game.Context.Panel&lt;CrateUi&gt;()</c> and gets the panel of ITS rig, or null.
    /// Plain C#: no Unity lifecycle, testable as is.
    /// </summary>
    public sealed class ClientContext
    {
        private readonly Dictionary<System.Type, UiPanel> _panels = new();

        /// <summary>Records <paramref name="panel"/> as the panel of its type (a later one of the same type replaces it).</summary>
        public void Register(UiPanel panel)
        {
            if (panel != null)
            {
                _panels[panel.GetType()] = panel;
            }
        }

        /// <summary>Forgets <paramref name="panel"/> — only while it is still the registered one of its type, as the old
        /// <c>if (Instance == this) Instance = null;</c> did.</summary>
        public void Unregister(UiPanel panel)
        {
            if (panel != null && _panels.TryGetValue(panel.GetType(), out var current) && ReferenceEquals(current, panel))
            {
                _panels.Remove(panel.GetType());
            }
        }

        /// <summary>The registered panel of type <typeparamref name="T"/>, or null when this rig has none.</summary>
        public T Panel<T>() where T : UiPanel => _panels.TryGetValue(typeof(T), out var p) ? (T)p : null;
    }
}
