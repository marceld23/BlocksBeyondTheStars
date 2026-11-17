// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Feel
{
    /// <summary>
    /// "Is this better than what I wear?" — the numbers of a piece of suit gear next to the piece it would replace.
    /// One line per effect either piece has: the candidate's value and how it differs from the worn piece's
    /// (<see cref="Line.Delta"/>: positive = the candidate is better). The effects and their texts are the ones the
    /// Suit tab already shows (<c>ui.equip.effect.*</c>); nothing is weighed or scored — the player decides.
    /// </summary>
    public static class GearCompare
    {
        /// <summary>One effect of the comparison.</summary>
        public readonly struct Line
        {
            public Line(string effectKey, int value, int delta)
            {
                EffectKey = effectKey;
                Value = value;
                Delta = delta;
            }

            /// <summary>The effect's text key (<c>ui.equip.effect.armor</c> …), with a <c>{value}</c> placeholder.</summary>
            public string EffectKey { get; }

            /// <summary>The candidate's value (percent, or a plain amount for oxygen and suit energy).</summary>
            public int Value { get; }

            /// <summary>Candidate minus worn: positive = better, negative = worse, 0 = the same.</summary>
            public int Delta { get; }
        }

        private static readonly (string Key, Func<ItemDefinition, float> Value, float Scale)[] Effects =
        {
            ("ui.equip.effect.armor", d => d.ArmorResistance, 100f),
            ("ui.equip.effect.warmth", d => d.ThermalInsulation, 100f),
            ("ui.equip.effect.oxygen", d => d.OxygenBonus, 1f),
            ("ui.equip.effect.acid", d => d.CorrosionResistance, 100f),
            ("ui.equip.effect.fall", d => d.FallProtection, 100f),
            ("ui.equip.effect.grip", d => d.ClimbGrip, 100f),
            ("ui.equip.effect.jump", d => d.JumpBoost, 100f),
            ("ui.equip.effect.energy", d => d.SuitEnergyBonus, 1f),
        };

        /// <summary>Whether <paramref name="worn"/> is the piece <paramref name="candidate"/> would replace: another
        /// item for the same body slot. The module slots hold several pieces side by side, so a module replaces none.</summary>
        public static bool Replaces(ItemDefinition? candidate, ItemDefinition? worn)
            => candidate != null && worn != null && candidate.Key != worn.Key
               && !string.IsNullOrEmpty(candidate.EquipSlot) && candidate.EquipSlot == worn.EquipSlot
               && !string.Equals(candidate.EquipSlot, "module", StringComparison.OrdinalIgnoreCase);

        /// <summary>The comparison of <paramref name="candidate"/> with <paramref name="worn"/>: a line for every effect
        /// at least one of them has, in the Suit tab's order.</summary>
        public static IReadOnlyList<Line> Lines(ItemDefinition candidate, ItemDefinition worn)
        {
            var lines = new List<Line>(Effects.Length);
            if (candidate == null || worn == null)
            {
                return lines;
            }

            foreach (var (key, value, scale) in Effects)
            {
                int mine = (int)Math.Round(value(candidate) * scale, MidpointRounding.AwayFromZero);
                int theirs = (int)Math.Round(value(worn) * scale, MidpointRounding.AwayFromZero);
                if (mine > 0 || theirs > 0)
                {
                    lines.Add(new Line(key, mine, mine - theirs));
                }
            }

            return lines;
        }
    }
}
