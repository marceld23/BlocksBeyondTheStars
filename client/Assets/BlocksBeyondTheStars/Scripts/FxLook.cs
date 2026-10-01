// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The effect look of a tool, weapon or ship module in Unity terms (#2152): the data-driven <c>fx</c> object from
    /// <c>data/items.json</c> / <c>data/ship_modules.json</c>, resolved by the Unity-free <see cref="FxStyleResolver"/>
    /// (Client.Core, which also owns the fallback heuristics for content without <c>fx</c>), turned into
    /// <see cref="Color"/>s. Colours are sRGB-authored like everywhere in the client.
    /// </summary>
    public readonly struct FxLook
    {
        public readonly string Style;
        public readonly Color Color;
        public readonly Color Color2;
        public readonly float Charge;
        public readonly float Size;
        public readonly float Speed;

        public FxLook(string style, Color color, Color color2, float charge, float size, float speed)
        {
            Style = style ?? "generic";
            Color = color;
            Color2 = color2;
            Charge = charge;
            Size = size <= 0f ? 1f : size;
            Speed = speed;
        }

        /// <summary>The look of the item <paramref name="itemKey"/> (null/empty = bare hands).</summary>
        public static FxLook ForItem(GameContent content, string itemKey)
        {
            var tool = string.IsNullOrEmpty(itemKey) ? null : content?.GetItem(itemKey)?.Tool;
            return From(FxStyleResolver.ForTool(itemKey, tool));
        }

        /// <summary>The look of the ship module <paramref name="moduleKey"/>.</summary>
        public static FxLook ForModule(GameContent content, string moduleKey)
        {
            var def = string.IsNullOrEmpty(moduleKey) ? null : content?.GetShipModule(moduleKey);
            return From(FxStyleResolver.ForShipModule(moduleKey, def));
        }

        private static FxLook From(ResolvedFx r)
            => new FxLook(r.Style, new Color(r.Color.R, r.Color.G, r.Color.B), new Color(r.Color2.R, r.Color2.G, r.Color2.B), r.Charge, r.Size, r.Speed);

        public bool Is(string style) => Style == style;

        /// <summary>Melee looks (swings, no projectile).</summary>
        public bool IsMelee => Style is "slash" or "vibro" or "plasma_blade" or "fist";
    }
}
