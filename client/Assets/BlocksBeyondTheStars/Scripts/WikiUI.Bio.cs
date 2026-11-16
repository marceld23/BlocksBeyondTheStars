// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using System.Text;
using BlocksBeyondTheStars.Shared.Bio;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The bio lab's research book in the Codex (#2203): Substances, Mixes and Materials. Per-player progress like
    /// Discoveries. Nothing here is stored as text: a profile, a mix and a change are recomputed from the book with the
    /// rules the server used, so a balance change shows here too. Substances and Mixes are drawn, not printed (#3717):
    /// each substance stands behind its effect's icon, and the reaction chart stands above the tried mixes.
    /// </summary>
    public sealed partial class WikiUI
    {
        /// <summary>Every plant and animal species this player has analysed: the substance, what it does, its catch,
        /// how rare it is and where it is from — under the headings of the effect families (#2300), the ones the
        /// sample case filters by, each line behind the icon of its effect in the family's colour (#3719).</summary>
        private float RenderSubstances(Transform content, float textW, float y)
        {
            y = AddTextBlock(content, textW, y, "<b><size=24>" + L("ui.wiki.substances") + "</size></b>\n\n");

            var byFamily = new Dictionary<BioEffectFamily, List<(string Name, BioEffect Effect, string Line)>>();
            if (Game != null)
            {
                foreach (var species in Game.Bio.Species.Values)
                {
                    if (!species.Analysed || species.Kind == (int)BioKind.Mineral || !(Game.Bio.ProfileOf(species.Seed) is { } profile))
                    {
                        continue;
                    }

                    string name = BioLabUi.SpeciesLabel(Game, species, false);
                    var line = new StringBuilder();
                    line.Append("<b>").Append(name).Append("</b>  —  ").Append(profile.Substance)
                        .Append(" · <b>").Append(BioLabUi.EffectLabel(Game, profile.Effect, profile.Level)).Append("</b>")
                        .Append(" · ").Append(BioLabUi.SideText(Game, profile.Side, profile.SideLevel))
                        .Append(" · ").Append(L("bio.rarity." + profile.Rarity));
                    AppendOrigin(line, species.OriginBodyName);
                    var family = BioRules.Family(profile.Effect);
                    if (!byFamily.TryGetValue(family, out var lines))
                    {
                        byFamily[family] = lines = new List<(string Name, BioEffect Effect, string Line)>();
                    }

                    lines.Add((name, profile.Effect, line.ToString()));
                }
            }

            if (byFamily.Count == 0)
            {
                return AddTextBlock(content, textW, y, L("ui.wiki.substances.empty"));
            }

            foreach (var family in BioRules.Families)
            {
                if (!byFamily.TryGetValue(family, out var lines))
                {
                    continue;
                }

                string hex = ColorUtility.ToHtmlStringRGB(BioIcons.Rgb(BioSymbols.FamilyRgb(family)));
                y = AddTextBlock(content, textW, y,
                    "<b><color=#" + hex + ">" + BioLabUi.FamilyLabel(Game, family) + "</color> (" + lines.Count + ")</b>\n");
                lines.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                foreach (var (_, effect, line) in lines)
                {
                    y = AddSubstanceRow(content, textW, y, effect, line);
                }

                y += 16f;
            }

            return y;
        }

        /// <summary>One substance: its effect's icon, the line beside it; returns the next y.</summary>
        private static float AddSubstanceRow(Transform content, float textW, float y, BioEffect effect, string line)
        {
            const float indent = 34f;
            BioIcons.AddEffect(content, Pad + 4f, y + 2f, 22f, effect);
            var t = UiKit.AddText(content, Pad + indent, y, textW - indent, 100f, line, 18, UiKit.TextCol, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            float textH = t.preferredHeight;
            ((RectTransform)t.transform).sizeDelta = new Vector2(textW - indent, textH + 6f);
            return y + Mathf.Max(24f, textH) + 4f;
        }

        /// <summary>The Mixes chapter: the reaction chart of the substance groups — a cell fills in once two samples of
        /// those groups were mixed (#3721) — and under it the mixes this player has tried.</summary>
        private float RenderCompounds(Transform content, float textW, float y)
        {
            y = AddTextBlock(content, textW, y, "<b><size=24>" + L("ui.wiki.compounds") + "</size></b>\n\n");
            if (Game != null)
            {
                y = BioChartView.Build(Game, content, Pad, y, textW) + 18f;
            }

            return AddTextBlock(content, textW, y, BuildCompounds());
        }

        /// <summary>The mixes this player has tried: "active substance + form (+ stabiliser, + modifier) → result", with
        /// the stability — or the note that the mix fell apart. A mix a detoxifier washed is its own entry (#2216): it
        /// is recomputed as washed and says so. A mix whose species the book no longer holds is left out: nothing
        /// could be said about it — and so is a signature this version cannot read (there is no text for it).</summary>
        private string BuildCompounds()
        {
            var sb = new StringBuilder();
            var lines = new List<(string Name, string Line)>();
            if (Game != null)
            {
                foreach (string signature in Game.Bio.Reactions)
                {
                    if (!Synthesis.TryParseSignature(signature, out uint active, out var form, out string stabiliserItem, out uint stabiliserSeed, out uint modifier, out bool washed)
                        || !(BioLabUi.ComputeMix(Game, active, form, stabiliserSeed, stabiliserItem, modifier, washed) is { } compound))
                    {
                        continue;
                    }

                    string name = SeedName(active, false);
                    lines.Add((name, MixLine(name, form, stabiliserItem, stabiliserSeed, modifier, washed, compound)));
                }
            }

            if (lines.Count == 0)
            {
                sb.Append(L("ui.wiki.compounds.empty"));
                return sb.ToString();
            }

            AppendSorted(sb, lines);
            return sb.ToString();
        }

        private string MixLine(string name, BioForm form, string stabiliserItem, uint stabiliserSeed, uint modifier, bool washed, Compound compound)
        {
            var line = new StringBuilder();
            line.Append("  • <b>").Append(name).Append("</b> + ").Append(L("item." + BioItems.PreparationKey(form) + ".name"));
            if (stabiliserSeed != 0)
            {
                line.Append(" + ").Append(SeedName(stabiliserSeed, true));
            }
            else if (stabiliserItem.Length > 0)
            {
                line.Append(" + ").Append(L("item." + stabiliserItem + ".name"));
            }

            if (modifier != 0)
            {
                line.Append(" + ").Append(SeedName(modifier, false));
            }

            line.Append("  →  ");
            if (compound.Failed)
            {
                line.Append("<color=#9fb4c8>").Append(L("ui.bio.will_fail")).Append("</color>");
            }
            else
            {
                line.Append("<b>").Append(BioLabUi.EffectLabel(Game, compound.Effect, compound.Level)).Append("</b>");
            }

            line.Append("  (").Append(L("ui.bio.stability")).Append(' ').Append(compound.Stability).Append(" %)");
            if (washed)
            {
                line.Append("  ·  <color=#9fb4c8>").Append(L("ui.bio.washed")).Append("</color>");
            }

            return line.ToString();
        }

        /// <summary>One line per deposit this player has analysed: material, purity, traits, where from.</summary>
        private List<(string Name, string Line)> DepositLines()
        {
            var deposits = new List<(string Name, string Line)>();
            if (Game == null)
            {
                return deposits;
            }

            foreach (var species in Game.Bio.Species.Values)
            {
                if (!species.Analysed || species.Kind != (int)BioKind.Mineral || !(BioLabUi.MaterialOf(Game, species.Seed) is { } material))
                {
                    continue;
                }

                string name = BioLabUi.SpeciesLabel(Game, species, true);
                var line = new StringBuilder();
                line.Append("  • <b>").Append(name).Append("</b>  —  ").Append(BioLabUi.MaterialText(Game, material, " · "));
                AppendOrigin(line, species.OriginBodyName);
                deposits.Add((name, line.ToString()));
            }

            return deposits;
        }

        /// <summary>One line per tool or piece of gear this player has changed in a bio lab. A change is recorded as
        /// "base key|payload" — the item key the changed tool carries, in two parts.</summary>
        private List<(string Name, string Line)> ChangeLines()
        {
            var changes = new List<(string Name, string Line)>();
            if (Game == null)
            {
                return changes;
            }

            foreach (string record in Game.Bio.Changes)
            {
                int bar = record.IndexOf('|');
                if (bar <= 0)
                {
                    continue;
                }

                string baseKey = record.Substring(0, bar);
                var mods = ItemMods.Of(baseKey + Shared.State.ItemKey.Separator + ItemMods.Tag + record.Substring(bar + 1));
                if (mods.IsEmpty)
                {
                    continue;
                }

                string name = L("item." + baseKey + ".name");
                changes.Add((name, "  • <b>" + name + "</b>  —  " + BioLabUi.ModsText(Game, mods, false, " · ")));
            }

            return changes;
        }

        /// <summary>The deposits this player has analysed (material, purity, traits, where from) and the tools and
        /// pieces of gear they have changed in a bio lab, with what each change gave and what it cost.</summary>
        private string BuildMaterials()
        {
            var sb = new StringBuilder();
            sb.Append("<b><size=24>").Append(L("ui.wiki.materials")).Append("</size></b>\n\n");

            var deposits = DepositLines();
            var changes = ChangeLines();
            if (deposits.Count == 0 && changes.Count == 0)
            {
                sb.Append(L("ui.wiki.materials.empty"));
                return sb.ToString();
            }

            if (deposits.Count > 0)
            {
                sb.Append("<b>").Append(L("bio.kind.mineral")).Append(" (").Append(deposits.Count).Append(")</b>\n");
                AppendSorted(sb, deposits);
                sb.Append('\n');
            }

            if (changes.Count > 0)
            {
                sb.Append("<b>").Append(L("ui.bio.changed")).Append(" (").Append(changes.Count).Append(")</b>\n");
                AppendSorted(sb, changes);
            }

            return sb.ToString();
        }
    }
}
