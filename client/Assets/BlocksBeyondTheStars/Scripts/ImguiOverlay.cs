// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The one place an <c>OnGUI</c> lives (#1553). Every enabled behaviour with an <c>OnGUI</c> makes Unity run
    /// the IMGUI event loop for it every frame — a Layout and a Repaint pass, each allocating a
    /// <c>GUILayoutGroup</c> and the event plumbing — whether or not it draws anything. The weather washes and
    /// the finale panels are on screen a fraction of the time, so their owners keep this component DISABLED
    /// until they have something to show (<see cref="Sync"/>), and it skips the layout pass outright
    /// (<c>useGUILayout = false</c>; the owners draw with absolute <c>GUI</c> calls).
    /// </summary>
    public sealed class ImguiOverlay : MonoBehaviour
    {
        /// <summary>The owner's draw routine, called from <c>OnGUI</c> — for every IMGUI event, or only for
        /// Repaint when <see cref="RepaintOnly"/> is set (pure texture overlays never need Layout / mouse events).</summary>
        public System.Action Draw;

        /// <summary>Skip everything but the Repaint event (the weather overlay: nothing to click).</summary>
        public bool RepaintOnly;

        /// <summary>Attaches an overlay to <paramref name="host"/>, disabled until the owner syncs it on.</summary>
        public static ImguiOverlay Attach(GameObject host, System.Action draw, bool repaintOnly)
        {
            var overlay = host.AddComponent<ImguiOverlay>();
            overlay.Draw = draw;
            overlay.RepaintOnly = repaintOnly;
            overlay.useGUILayout = false;
            overlay.enabled = false;
            return overlay;
        }

        /// <summary>Enables the overlay only while <paramref name="wanted"/>; a disabled behaviour gets no
        /// <c>OnGUI</c> calls at all, so an idle overlay costs nothing.</summary>
        public void Sync(bool wanted)
        {
            if (enabled != wanted)
            {
                enabled = wanted;
            }
        }

        private void Awake()
        {
            useGUILayout = false;
        }

        private void OnGUI()
        {
            if (RepaintOnly && Event.current.type != EventType.Repaint)
            {
                return;
            }

            Draw?.Invoke();
        }
    }
}
