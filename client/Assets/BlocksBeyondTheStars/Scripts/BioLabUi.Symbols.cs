// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Bio;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The symbol side of <see cref="BioLabUi"/> (#3717): the Reactions page, the line that says what two substance
    /// groups do to each other, and the small marks on the lab's lists.
    /// </summary>
    public sealed partial class BioLabUi
    {
        /// <summary>The Reactions page (#3721): the chart of the substance groups, filled in by the player's own mixes.</summary>
        private void BuildChart(Transform panel)
        {
            BioChartView.Build(Game, panel, PaneX, TopY, PaneW);
        }

        /// <summary>The profile of a species the player has analysed; null for anything else — an unanalysed sample
        /// keeps its group to itself, like the rest of its profile.</summary>
        private BioProfile AnalysedProfile(uint seed)
            => seed != 0 && Game.Bio.Analysed(seed) ? Game.Bio.ProfileOf(seed) : null;

        /// <summary>
        /// The Mix page's group line (#3721): the marks of the active sample's and the modifier's substance groups and
        /// — once the player has mixed two samples of those groups, whichever species they were — what the two do to each
        /// other. The exact result (strength, stability, a failed mix) stays an experiment; only the reaction carries
        /// over, as the fixed table means it to. Draws nothing unless both samples are analysed; returns the height.
        /// </summary>
        private float GroupLine(Transform panel, float y)
        {
            var active = AnalysedProfile(_mixActive);
            var modifier = AnalysedProfile(_mixModifier);
            if (active == null || modifier == null) return 0f;

            bool known = BioChartView.Of(Game).Knows(active.Group, modifier.Group);
            var reaction = BioRules.Reaction(active.Group, modifier.Group);
            float x = PaneX + 4f;
            BioIcons.Add(panel, x, y + 2f, 22f, BioSymbols.GroupIcon(active.Group), UiKit.Cyan);
            UiKit.AddText(panel, x + 24f, y, 20f, 26f, "+", 18, UiKit.CyanDim, TextAnchor.MiddleCenter);
            BioIcons.Add(panel, x + 46f, y + 2f, 22f, BioSymbols.GroupIcon(modifier.Group), UiKit.Cyan);
            UiKit.AddText(panel, x + 70f, y, 26f, 26f, "→", 18, UiKit.CyanDim, TextAnchor.MiddleCenter);
            if (known)
            {
                BioIcons.Add(panel, x + 98f, y + 2f, 22f, BioSymbols.ReactionIcon(reaction), BioChartView.ReactionColor(reaction));
            }
            else
            {
                UiKit.AddText(panel, x + 98f, y, 22f, 26f, "?", 20, UiKit.CyanDim, TextAnchor.MiddleCenter);
            }

            string text = L("bio.group." + active.Group) + " + " + L("bio.group." + modifier.Group) + ": "
                          + (known ? L("bio.reaction." + reaction.ToString().ToLowerInvariant()) : L("ui.bio.chart.untried"));
            var label = UiKit.AddText(panel, x + 130f, y, PaneW - 134f, 26f, text, 17, known ? UiKit.TextCol : UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabel(label, 11, 17);
            return 34f;
        }

        /// <summary>A kind's heading in the sample case: the kind's icon, its name and how many it lists.</summary>
        private static void KindHeading(Transform row, BioKind kind, string title, float width)
        {
            BioIcons.Add(row, 8f, 4f, 20f, BioSymbols.KindIcon(kind), UiKit.Cyan);
            UiKit.AddText(row, 34f, 2f, width - 34f, 24f, title, 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
        }

        /// <summary>What a sample of the case holds, under its name: an analysed plant or animal its effect's icon and
        /// name, an analysed deposit its traits as chips (all of them — the words had room for two), anything else
        /// "Not analysed".</summary>
        private void SampleDetailRow(Transform row, SampleCaseEntry entry, float x, float width)
        {
            if (entry.Effect == BioEffect.None && entry.Analysed && entry.Kind == BioKind.Mineral
                && MaterialOf(Game, entry.Seed) is { } material)
            {
                BioIcons.AddTraitChips(row, x, 32f, 20f, material, width);
                return;
            }

            if (entry.Effect != BioEffect.None)
            {
                BioIcons.AddEffect(row, x, 34f, 18f, entry.Effect);
                x += 24f;
                width -= 24f;
            }

            var detail = UiKit.AddText(row, x, 32f, width, 22f, SampleDetail(Game, entry), 14,
                entry.Analysed ? UiKit.TextCol : UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabel(detail, 10, 14);
        }

        /// <summary>The marks at the right end of a slot option: an analysed plant or animal sample shows its substance
        /// group (the Mix page's pickers show at a glance which groups stand to choose from), a material — an analysed
        /// mineral sample or a plain one from the backpack — its traits as chips.</summary>
        private void ChoiceMarks(Transform button, uint seed, string item, float buttonWidth)
        {
            if (_pick == Pick.Active || _pick == Pick.Modifier)
            {
                var profile = AnalysedProfile(seed);
                if (profile != null)
                {
                    BioIcons.Add(button, buttonWidth - 36f, 11f, 22f, BioSymbols.GroupIcon(profile.Group), UiKit.Cyan);
                }

                return;
            }

            if (_pick != Pick.Stabiliser && _pick != Pick.Material) return;
            var material = seed != 0
                ? (Game.Bio.Analysed(seed) ? MaterialOf(Game, seed) : null)
                : SyntheticMaterial(Game, item);
            if (material == null) return;
            float width = BioIcons.TraitsByLevel(material).Count * 40f;
            BioIcons.AddTraitChips(button, buttonWidth - 12f - width, 12f, 20f, material, width + 1f);
        }
    }
}
