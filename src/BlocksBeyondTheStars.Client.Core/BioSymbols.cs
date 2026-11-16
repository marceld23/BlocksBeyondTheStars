// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Bio;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The bio lab's symbol language (#3717): which icon and which colour stand for what a sample, a deposit, a
    /// preparation or a changed item carries. Presentation only — nothing here decides anything — and Unity-free, so
    /// the headless tests pin that every property has a symbol and every symbol a file.
    /// <para>
    /// The grammar: an <b>icon</b> names a theme (speed, heat, grip …) and is shared wherever the theme returns — the
    /// effect "heat ward", the material trait "heat-proof" and the catch "runs out faster in the heat" all wear the
    /// flame. The <b>colour</b> names the class: an effect takes the colour of its family (five colours, the families
    /// the sample case filters by), a side effect and a weakness are amber, a gain is green. So colour is never the only
    /// carrier of a meaning, and nineteen effects no longer need nineteen colours.
    /// </para>
    /// </summary>
    public static class BioSymbols
    {
        /// <summary>The plate a material trait's icon sits on, and the mark of a toxic sample.</summary>
        public const string HexPlate = "bio_hex";
        public const string ToxicIcon = "bio_toxic";

        /// <summary>Segments of the strength bar and the steps one segment fills in: 5 × 3 = the fifteen levels, cut
        /// exactly where <see cref="BioRules.RarityOfLevel"/> cuts them (1–3, 4–6, 7–9, 10–12, 13–15).</summary>
        public const int BarSegments = BioRules.MaxRarity + 1;
        public const int StepsPerSegment = BioRules.MaxLevel / BarSegments;

        /// <summary>The most reasons a card names for an effect (<see cref="Reasons"/>).</summary>
        public const int MaxReasons = 3;

        /// <summary>The icon of an effect; null for <see cref="BioEffect.None"/>. Five effects wear the icon of the
        /// vital they act on, so the HUD's effect row repeats the mark of the bar above it.</summary>
        public static string? EffectIcon(BioEffect effect) => effect switch
        {
            BioEffect.Speed => "bio_speed",
            BioEffect.Jump => "bio_jump",
            BioEffect.FeatherFall => "bio_feather",
            BioEffect.Grip => "bio_grip",
            BioEffect.Shield => "vital_shield",
            BioEffect.Regeneration => "vital_health",
            BioEffect.Strength => "bio_strength",
            BioEffect.Mining => "bio_mining",
            BioEffect.Reflex => "bio_reflex",
            BioEffect.Breath => "vital_oxygen",
            BioEffect.HeatWard => "bio_heat",
            BioEffect.ColdWard => "bio_cold",
            BioEffect.ToxinWard => "bio_toxinward",
            BioEffect.Satiety => "vital_hunger",
            BioEffect.NightSight => "bio_nightsight",
            BioEffect.Perception => "bio_perception",
            BioEffect.Stealth => "bio_stealth",
            BioEffect.Energy => "vital_energy",
            BioEffect.Gathering => "bio_gathering",
            _ => null,
        };

        /// <summary>The effect a side effect is the opposite of — every catch undoes exactly one effect (hungry ↔ satiety,
        /// breathless ↔ breath, sluggish ↔ speed, leaden ↔ jump, tired ↔ regeneration, slow reflexes ↔ reflex), so it
        /// wears that effect's icon, in amber.</summary>
        public static BioEffect Undoes(BioSideEffect side) => side switch
        {
            BioSideEffect.Hungry => BioEffect.Satiety,
            BioSideEffect.Breathless => BioEffect.Breath,
            BioSideEffect.Sluggish => BioEffect.Speed,
            BioSideEffect.Leaden => BioEffect.Jump,
            BioSideEffect.Tired => BioEffect.Regeneration,
            BioSideEffect.SlowReflex => BioEffect.Reflex,
            _ => BioEffect.None,
        };

        /// <summary>The icon of a side effect: the icon of the effect it undoes.</summary>
        public static string? SideIcon(BioSideEffect side) => EffectIcon(Undoes(side));

        /// <summary>The icon of a material trait; null for <see cref="MatTrait.None"/>.</summary>
        public static string? TraitIcon(MatTrait trait) => trait switch
        {
            MatTrait.Conductive => "vital_energy",
            MatTrait.Hard => "bio_hard",
            MatTrait.HeatProof => "bio_heat",
            MatTrait.ColdProof => "bio_cold",
            MatTrait.Heavy => "bio_heavy",
            MatTrait.Light => "bio_feather",
            MatTrait.Magnetic => "bio_magnetic",
            MatTrait.Unstable => "bio_unstable",
            _ => null,
        };

        /// <summary>The icon of a value the lab changes on a tool or a piece of gear; null for <see cref="ModStat.None"/>.</summary>
        public static string? StatIcon(ModStat stat) => stat switch
        {
            ModStat.Power => "bio_strength",
            ModStat.Energy => "vital_energy",
            ModStat.Cooldown => "bio_reflex",
            ModStat.Range => "bio_range",
            ModStat.Insulation => "vital_exposure",
            ModStat.Armor => "vital_shield",
            ModStat.Corrosion => "bio_toxinward",
            ModStat.Fall => "bio_feather",
            ModStat.Grip => "bio_grip",
            ModStat.Oxygen => "vital_oxygen",
            ModStat.Weight => "bio_heavy",
            _ => null,
        };

        /// <summary>The mark of a substance group (0..<see cref="BioRules.Groups"/>-1): eight plain shapes.</summary>
        public static string GroupIcon(int group)
            => "bio_group_" + (((group % BioRules.Groups) + BioRules.Groups) % BioRules.Groups);

        /// <summary>The mark of what two substance groups do to each other.</summary>
        public static string ReactionIcon(BioReaction reaction) => reaction switch
        {
            BioReaction.Amplify => "bio_react_amplify",
            BioReaction.Inhibit => "bio_react_inhibit",
            BioReaction.Couple => "bio_react_couple",
            BioReaction.Transmute => "bio_react_transmute",
            _ => "bio_react_neutral",
        };

        /// <summary>The icon of what a sample was taken from (existing art: the planet overview's and the map's).</summary>
        public static string KindIcon(BioKind kind) => kind switch
        {
            BioKind.Animal => "ov_animals",
            BioKind.Mineral => "map_mark_ore",
            _ => "ov_plants",
        };

        /// <summary>The icon of a substance's weakness to the weather; null for a stable one.</summary>
        public static string? ThermalIcon(BioThermal thermal) => thermal switch
        {
            BioThermal.HeatSensitive => "bio_heat",
            BioThermal.ColdSensitive => "bio_cold",
            _ => null,
        };

        /// <summary>The stabiliser trait that steadies a heat- or cold-sensitive substance (<c>Synthesis.Compute</c>).</summary>
        public static MatTrait ThermalCounter(BioThermal thermal) => thermal switch
        {
            BioThermal.HeatSensitive => MatTrait.HeatProof,
            BioThermal.ColdSensitive => MatTrait.ColdProof,
            _ => MatTrait.None,
        };

        // --- Colours (0xRRGGBB) ---

        /// <summary>The colour of an effect family. Five hues that stay apart on the HUD's dark panel and keep clear of
        /// the amber of a warning.</summary>
        public static int FamilyRgb(BioEffectFamily family) => family switch
        {
            BioEffectFamily.Movement => 0x4DB8FF,   // blue
            BioEffectFamily.Protection => 0xFF5A5A, // red
            BioEffectFamily.Work => 0xF2EE5C,       // lemon yellow
            BioEffectFamily.Survival => 0x5ED67A,   // green
            BioEffectFamily.Senses => 0xB48CFF,     // violet
            _ => 0xFFFFFF,
        };

        /// <summary>The colour of an effect: its family's.</summary>
        public static int EffectRgb(BioEffect effect) => FamilyRgb(BioRules.Family(effect));

        /// <summary>The colour of a rarity tier 0 (common) .. 4 (legendary).</summary>
        public static int RarityRgb(int rarity) => Math.Clamp(rarity, 0, BioRules.MaxRarity) switch
        {
            0 => 0xB8C4D0, // grey
            1 => 0x6FD98A, // green
            2 => 0x5AA8FF, // blue
            3 => 0xC08CFF, // violet
            _ => 0xFFC83D, // gold
        };

        // --- The strength bar ---

        /// <summary>How far a level fills one segment of the strength bar: 0..<see cref="StepsPerSegment"/>.</summary>
        public static int SegmentFill(int level, int segment)
            => Math.Clamp(Math.Clamp(level, 0, BioRules.MaxLevel) - segment * StepsPerSegment, 0, StepsPerSegment);

        // --- Why a species carries its effect ---

        /// <summary>
        /// The context tags of a species that pull exactly this effect (<see cref="BioRules.Affinities"/>), the strongest
        /// pull first, at most <see cref="MaxReasons"/> — what a card names under "fits how it lives". Empty when the
        /// effect was the seed's surprise: no tag of the species asks for it.
        /// </summary>
        public static List<BioTag> Reasons(BioTag tags, BioEffect effect)
        {
            var pulls = new List<(BioTag Tag, int Bonus, int Order)>();
            for (int i = 0; i < BioRules.Affinities.Length; i++)
            {
                var a = BioRules.Affinities[i];
                if (a.Effect == effect && (tags & a.Tag) != 0)
                {
                    pulls.Add((a.Tag, a.Bonus, i));
                }
            }

            pulls.Sort((x, y) => x.Bonus != y.Bonus ? y.Bonus.CompareTo(x.Bonus) : x.Order.CompareTo(y.Order));
            var reasons = new List<BioTag>();
            for (int i = 0; i < pulls.Count && i < MaxReasons; i++)
            {
                reasons.Add(pulls[i].Tag);
            }

            return reasons;
        }

        /// <summary>The locale key of a context tag's phrase ("lives by lava").</summary>
        public static string TagKey(BioTag tag) => "bio.tag." + tag.ToString().ToLowerInvariant();

        /// <summary>Every context tag a card can name: the ones the affinity table knows.</summary>
        public static List<BioTag> NamedTags()
        {
            var tags = new List<BioTag>();
            foreach (var a in BioRules.Affinities)
            {
                if (!tags.Contains(a.Tag))
                {
                    tags.Add(a.Tag);
                }
            }

            return tags;
        }

        /// <summary>Every icon this class can name — the list the icon test checks against the client's files.</summary>
        public static List<string> AllIcons()
        {
            var icons = new List<string> { HexPlate, ToxicIcon };
            void Add(string? name)
            {
                if (name != null && !icons.Contains(name))
                {
                    icons.Add(name);
                }
            }

            foreach (var effect in BioRules.Effects)
            {
                Add(EffectIcon(effect));
            }

            foreach (MatTrait trait in Enum.GetValues(typeof(MatTrait)))
            {
                Add(TraitIcon(trait));
            }

            foreach (ModStat stat in Enum.GetValues(typeof(ModStat)))
            {
                Add(StatIcon(stat));
            }

            foreach (BioReaction reaction in Enum.GetValues(typeof(BioReaction)))
            {
                Add(ReactionIcon(reaction));
            }

            foreach (BioKind kind in Enum.GetValues(typeof(BioKind)))
            {
                Add(KindIcon(kind));
            }

            for (int group = 0; group < BioRules.Groups; group++)
            {
                Add(GroupIcon(group));
            }

            return icons;
        }
    }

    /// <summary>
    /// The reaction chart (#3721): which pairs of substance groups this player has seen react. The table itself is fixed
    /// (<see cref="BioRules.Reaction"/>) and the same in every save; what a player knows of it is derived here from the
    /// mixes the research book remembers — a pair counts once two samples of those groups were mixed, whatever came of
    /// it (a failed mix showed the reaction too). No server state, no message: the book already carries the mixes.
    /// </summary>
    public sealed class ReactionChart
    {
        private readonly bool[,] _known = new bool[BioRules.Groups, BioRules.Groups];

        /// <summary>The unordered pairs of groups, a group with itself included: 8 × 9 / 2.</summary>
        public const int Pairs = BioRules.Groups * (BioRules.Groups + 1) / 2;

        /// <summary>How many of the <see cref="Pairs"/> the player has seen.</summary>
        public int Known { get; private set; }

        /// <summary>Whether the player has mixed two samples of these groups.</summary>
        public bool Knows(int groupA, int groupB) => _known[Wrap(groupA), Wrap(groupB)];

        /// <summary>
        /// The chart of a research book. <paramref name="signatures"/> are its tried mixes
        /// (<see cref="Synthesis.Signature"/>), <paramref name="profileOf"/> the profile of a species as far as the book
        /// can read it (null for one it does not hold or has not analysed — such a mix shows nothing: the player was not
        /// told the groups either).
        /// </summary>
        public static ReactionChart From(IEnumerable<string> signatures, Func<uint, BioProfile?> profileOf)
        {
            var chart = new ReactionChart();
            foreach (string signature in signatures)
            {
                if (!Synthesis.TryParseSignature(signature, out uint active, out _, out _, out _, out uint modifier, out _)
                    || active == 0 || modifier == 0)
                {
                    continue;
                }

                var a = profileOf(active);
                var b = profileOf(modifier);
                if (a is null || b is null)
                {
                    continue;
                }

                chart.Mark(a.Group, b.Group);
            }

            return chart;
        }

        private void Mark(int groupA, int groupB)
        {
            int a = Wrap(groupA), b = Wrap(groupB);
            if (_known[a, b])
            {
                return;
            }

            _known[a, b] = true;
            _known[b, a] = true;
            Known++;
        }

        private static int Wrap(int group) => ((group % BioRules.Groups) + BioRules.Groups) % BioRules.Groups;
    }
}
