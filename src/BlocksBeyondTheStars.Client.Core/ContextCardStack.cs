// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The right column's context cards (#3668, HudZones E: the scan result, the wreck and the ship-repair card), stacked top
    /// to bottom under the objectives. On a touch device the column may not run under the action buttons at the bottom right
    /// (<see cref="TouchReservation.StackLimit"/>): a card that would cross that line goes into a second column left of the
    /// first, which ends at <see cref="TouchHudLayout.SecondColumnRight"/> — clear of the crosshair — and starts level with
    /// the first column's cards, rising only as far as it must for its last card to end above the buttons under it (never
    /// above the map part or the touch row over it). A card that fits in neither column — three cards at once on a 16:9
    /// tablet — is held back (<see cref="Shown"/>) until one of the others goes; the first card of the second column always
    /// shows. Without touch every card goes into the first column, as before. Filled once per HUD refresh:
    /// <see cref="Begin"/>, then one <see cref="Add"/> per showing card; an add may move the second column's earlier cards,
    /// so the caller re-reads every placement after each one.
    /// </summary>
    public sealed class ContextCardStack
    {
        private readonly List<HudRect> _cards = new List<HudRect>(4);
        private readonly List<bool> _shown = new List<bool>(4);
        private readonly List<int> _second = new List<int>(4);
        private TouchReservation _touch = TouchReservation.None;
        private float _top, _firstRight, _secondRight, _mapTop;
        private bool _firstTaken;

        /// <summary>Where the next card of the first column goes (its last card's bottom plus the gap).</summary>
        public float FirstColumnBottom { get; private set; }

        public int Count => _cards.Count;

        /// <summary>The placement of the <paramref name="index"/>-th card added since <see cref="Begin"/>.</summary>
        public HudRect this[int index] => _cards[index];

        /// <summary>Whether the <paramref name="index"/>-th card has a place (see the class summary).</summary>
        public bool Shown(int index) => _shown[index];

        /// <summary>Starts a pass: the first card goes to <paramref name="top"/> (HudZones.ContextTop), right-aligned to
        /// <paramref name="firstRight"/>; the second column ends at <paramref name="secondRight"/> and never rises above
        /// <paramref name="mapTop"/> (where the map part starts) or the touch row over it. <paramref name="firstColumnTaken"/>
        /// (touch only): another card holds the first column — in flight the planet card, which is too tall to share it above
        /// the action buttons — so every card goes into the second.</summary>
        public void Begin(float top, float firstRight, float secondRight, float mapTop, TouchReservation touch, bool firstColumnTaken = false)
        {
            _cards.Clear();
            _shown.Clear();
            _second.Clear();
            _touch = touch ?? TouchReservation.None;
            _top = top;
            _firstRight = firstRight;
            _secondRight = secondRight;
            _mapTop = mapTop;
            _firstTaken = firstColumnTaken && _touch.Any;
            FirstColumnBottom = top;
        }

        /// <summary>Places a <paramref name="w"/> × <paramref name="h"/> card; returns its index.</summary>
        public int Add(float w, float h)
        {
            float x = _firstRight - w;
            int index = _cards.Count;
            _shown.Add(true);
            if (!_firstTaken && FirstColumnBottom + h <= _touch.StackLimit(x, _firstRight, _mapTop))
            {
                _cards.Add(new HudRect(x, FirstColumnBottom, w, h));
                FirstColumnBottom += h + TouchHudLayout.Gap;
                return index;
            }

            _cards.Add(new HudRect(_secondRight - w, _top, w, h));
            _second.Add(index);
            if (!LaySecondColumn() && _second.Count > 1)
            {
                _second.RemoveAt(_second.Count - 1);
                _shown[index] = false;
                LaySecondColumn();
            }

            return index;
        }

        /// <summary>Lays the second column out; false when its last card still ends below the buttons under it.</summary>
        private bool LaySecondColumn()
        {
            float left = _secondRight, total = -TouchHudLayout.Gap;
            foreach (int i in _second)
            {
                left = Math.Min(left, _secondRight - _cards[i].W);
                total += _cards[i].H + TouchHudLayout.Gap;
            }

            float limit = _touch.StackLimit(left, _secondRight, _mapTop);
            float minTop = Math.Max(_mapTop, _touch.TopRowBottom(left, _secondRight) + TouchHudLayout.Gap);
            float y = Math.Max(minTop, Math.Min(_top, limit - total));
            foreach (int i in _second)
            {
                var card = _cards[i];
                _cards[i] = new HudRect(_secondRight - card.W, y, card.W, card.H);
                y += card.H + TouchHudLayout.Gap;
            }

            return y - TouchHudLayout.Gap <= limit;
        }
    }
}
