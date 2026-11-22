// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The main menu's pilot-name chip asks for a name while it has none: its frame carries the light that runs around
    /// the "Join in" button (<see cref="UiRunningLight"/>, the same amber) for as long as the name field is empty, and
    /// loses it with the first letter. After a fresh install no name is set, and every play action is gated on one
    /// (#221) — the warning line under the chip only speaks once a play button was pressed, so a new player had to
    /// find the field alone. The light is on the box around the field, not on the input itself, so the caret and the
    /// typed text stay calm. Under <see cref="UiKit.ReducedMotion"/> the running light stands still (a lit frame).
    /// </summary>
    public sealed class UiNameChipMark : MonoBehaviour
    {
        private RectTransform _frame;
        private Func<string> _name;
        private float _radius;
        private UiRunningLight _light;

        /// <summary>Puts the mark on <paramref name="frame"/> (the box around the name field, corner radius
        /// <paramref name="radius"/>); <paramref name="name"/> reads the field's current text.</summary>
        public static UiNameChipMark Attach(RectTransform frame, Func<string> name, float radius)
        {
            if (frame == null || name == null)
            {
                return null;
            }

            var mark = frame.gameObject.AddComponent<UiNameChipMark>();
            mark._frame = frame;
            mark._name = name;
            mark._radius = radius;
            mark.Refresh();
            return mark;
        }

        private void Update() => Refresh();

        private void Refresh()
        {
            bool empty = string.IsNullOrWhiteSpace(_name());
            if (empty == (_light != null))
            {
                return;
            }

            if (!empty)
            {
                Destroy(_light.gameObject);
                _light = null;
                return;
            }

            // The colours of the "Join in" button's invitation (UiJoinInMark): amber follows the colour-vision palette.
            Color amber = UiKit.Warn;
            var rest = new Color(amber.r * 0.62f, amber.g * 0.62f, amber.b * 0.62f, 1f);
            _light = UiRunningLight.Attach(_frame, Color.Lerp(rest, amber, 0.25f), amber, Color.Lerp(amber, Color.white, 0.75f),
                running: true);
            _light.Radius = _radius;
        }
    }
}
