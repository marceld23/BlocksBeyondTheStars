// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Text;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.State;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The text side of <see cref="BioLabUi"/>: the static helpers that name and describe samples, preparations and
    /// changed items for the lab itself and for the other screens (inventory, Codex, HUD), so all of them read alike.
    /// The symbols beside these texts come from <see cref="BioIcons"/> and <see cref="BioCard"/> (#3717).
    /// </summary>
    public sealed partial class BioLabUi
    {

        private static string Loc(GameBootstrap game, string key) => game?.Localizer?.Get(key) ?? key;

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        private static string Dim(string text) => "<color=" + DimHex + ">" + text + "</color>";

        /// <summary>The colour that stands for an effect wherever it shows: its icon in the lab, on the HUD and in the
        /// lists, and the tint of a preparation's icon. It is the colour of the effect's family (#3719) — five colours
        /// a player can tell apart instead of nineteen; the icon says which effect of the family it is.</summary>
        public static Color EffectColor(BioEffect effect) => BioIcons.EffectColor(effect);

        /// <summary>"Speed III" in the player's language.</summary>
        public static string EffectLabel(GameBootstrap game, BioEffect effect, int level)
            => game?.Localizer != null ? BioItems.EffectLabel(game.Localizer, effect, level) : effect + " " + BioItems.Roman(level);

        /// <summary>The name of a side effect.</summary>
        public static string SideLabel(GameBootstrap game, BioSideEffect side)
            => Loc(game, "bio.side." + side.ToString().ToLowerInvariant());

        /// <summary>What a sample of the case is called: a species by its coined name, a deposit by its material,
        /// a plant without a name by its body block.</summary>
        public static string SampleName(GameBootstrap game, string itemKey)
        {
            if (game == null) return string.Empty;
            game.Bio.Species.TryGetValue(ItemKey.Seed(itemKey), out var species);
            return SpeciesLabel(game, species, IsMineral(itemKey));
        }

        /// <summary>The name of a species of the research book (see <see cref="SampleName"/>); <paramref name="species"/>
        /// may be null for a seed the book does not hold.</summary>
        public static string SpeciesLabel(GameBootstrap game, NetBioSpecies species, bool mineral)
        {
            if (mineral || (species != null && species.Kind == (int)BioKind.Mineral))
            {
                return species != null && !string.IsNullOrEmpty(species.MaterialItem)
                    ? Loc(game, "item." + species.MaterialItem + ".name")
                    : Loc(game, "item." + BioItems.MineralSample + ".name");
            }

            if (species != null && !string.IsNullOrEmpty(species.Name)) return species.Name;
            if (species != null && !string.IsNullOrEmpty(species.BodyBlock)) return Loc(game, "block." + species.BodyBlock + ".name");
            return Loc(game, "item." + BioItems.Sample + ".name");
        }

        /// <summary>"Plant · Found on: Kepler" — what a sample is and where it is from.</summary>
        public static string SampleOrigin(GameBootstrap game, string itemKey)
        {
            if (game == null) return string.Empty;
            game.Bio.Species.TryGetValue(ItemKey.Seed(itemKey), out var species);
            string kind = IsMineral(itemKey) ? "mineral" : species != null && species.Kind == (int)BioKind.Animal ? "animal" : "plant";
            string text = Loc(game, "bio.kind." + kind);
            if (species != null && !string.IsNullOrEmpty(species.OriginBodyName))
            {
                text += "  ·  " + Loc(game, "ui.bio.origin") + ": " + species.OriginBodyName;
            }

            return text;
        }

        /// <summary>"Movement", "Senses" … — the name of an effect family (#2300).</summary>
        public static string FamilyLabel(GameBootstrap game, BioEffectFamily family)
            => Loc(game, "bio.family." + family.ToString().ToLowerInvariant());

        /// <summary>The heading of a kind in the sample lists: "Plants", "Animals", "Deposits".</summary>
        public static string KindLabel(GameBootstrap game, BioKind kind) => kind switch
        {
            BioKind.Animal => Loc(game, "ui.bio.filter.animals"),
            BioKind.Mineral => Loc(game, "ui.bio.filter.deposits"),
            _ => Loc(game, "ui.bio.filter.plants"),
        };

        /// <summary>A kind chip's word (#2300): "All" or the kind's heading.</summary>
        public static string KindFilterLabel(GameBootstrap game, SampleKindFilter kind) => kind switch
        {
            SampleKindFilter.Plants => KindLabel(game, BioKind.Plant),
            SampleKindFilter.Animals => KindLabel(game, BioKind.Animal),
            SampleKindFilter.Deposits => KindLabel(game, BioKind.Mineral),
            _ => Loc(game, "ui.bio.filter.all"),
        };

        /// <summary>The effect button's text (#2300): "Effect: All ▸", "Effect: Senses ▸", "Effect: Not analysed ▸".</summary>
        public static string EffectFilterLabel(GameBootstrap game, SampleEffectFilter effect)
        {
            string value = effect switch
            {
                SampleEffectFilter.All => Loc(game, "ui.bio.filter.all"),
                SampleEffectFilter.NotAnalysed => Loc(game, "ui.bio.not_analysed"),
                _ => FamilyLabel(game, (BioEffectFamily)(int)effect),
            };
            return Loc(game, "ui.bio.filter.effect").Replace("{0}", value) + "  ▸";
        }

        /// <summary>
        /// What a sample of the case holds, in one short line for a list (#2299): an analysed plant or animal its effect
        /// ("Speed II"), an analysed deposit its strongest traits ("Hard 3 · Conductive 2"), anything else "Not
        /// analysed" — the effect stays a secret until the analysis, as on the lab's card.
        /// </summary>
        public static string SampleDetail(GameBootstrap game, SampleCaseEntry entry)
        {
            if (entry.Effect != BioEffect.None)
            {
                return EffectLabel(game, entry.Effect, entry.Level);
            }

            if (!entry.Analysed)
            {
                return Loc(game, "ui.bio.not_analysed");
            }

            if (entry.Kind == BioKind.Mineral && MaterialOf(game, entry.Seed) is { } material)
            {
                string traits = StrongestTraits(game, material, 2);
                if (traits.Length > 0)
                {
                    return traits;
                }
            }

            return Loc(game, "ui.bio.analysed");
        }

        /// <summary>The <paramref name="count"/> highest traits of a deposit (its trace left out, as on the card).</summary>
        private static string StrongestTraits(GameBootstrap game, MaterialProfile material, int count)
        {
            var traits = new List<MatTrait>();
            for (int i = 1; i < material.Levels.Length; i++)
            {
                if (material.Levels[i] > 0 && (MatTrait)i != material.Trace) traits.Add((MatTrait)i);
            }

            traits.Sort((a, b) => material.Levels[(int)b].CompareTo(material.Levels[(int)a]));
            var sb = new StringBuilder();
            for (int i = 0; i < traits.Count && i < count; i++)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(TraitName(game, traits[i])).Append(' ').Append(material.Levels[(int)traits[i]]);
            }

            return sb.ToString();
        }

        /// <summary>The icon a sample shows (#2299): what it was taken from — a deposit its material, a plant its body
        /// block — or null for the sample item's own icon.</summary>
        public static string IconKeyOf(NetBioSpecies species)
            => species == null ? null
                : !string.IsNullOrEmpty(species.MaterialItem) ? species.MaterialItem
                : !string.IsNullOrEmpty(species.BodyBlock) ? species.BodyBlock
                : null;

        /// <summary>The material profile of a deposit of the research book, with the fixed traits from the item data.</summary>
        public static MaterialProfile MaterialOf(GameBootstrap game, uint seed)
            => game == null ? null : game.Bio.MaterialOf(seed, key => game.Content?.GetItem(key)?.LabTraits);

        /// <summary>The profile of a plain material from the backpack (an ingot, an alloy, synthesised ore): its
        /// fixed traits at purity 3. Null for an item without lab traits.</summary>
        public static MaterialProfile SyntheticMaterial(GameBootstrap game, string itemKey)
            => !string.IsNullOrEmpty(itemKey) && game?.Content?.GetItem(itemKey) is { LabTraits: { Count: > 0 } } def
                ? MaterialProfiles.Synthetic(MaterialProfiles.BaseLevels(def.LabTraits))
                : null;

        /// <summary>What a mix makes, computed as the server computes it. Null when the research book lacks a species
        /// the mix needs. <paramref name="washed"/> says whether a detoxifier washed the toxic samples of the mix
        /// (#2216) — the washed and the unwashed mix of the same inputs are two experiments with two results.</summary>
        public static Compound ComputeMix(GameBootstrap game, uint activeSeed, BioForm form, uint stabiliserSeed,
            string stabiliserItem, uint modifierSeed, bool washed = false)
        {
            var active = game?.Bio.ProfileOf(activeSeed);
            if (active == null) return null;

            MaterialProfile stabiliser = null;
            if (stabiliserSeed != 0)
            {
                stabiliser = MaterialOf(game, stabiliserSeed);
                if (stabiliser == null) return null;
            }
            else if (!string.IsNullOrEmpty(stabiliserItem))
            {
                stabiliser = SyntheticMaterial(game, stabiliserItem);
                if (stabiliser == null) return null;
            }

            BioProfile modifier = null;
            if (modifierSeed != 0)
            {
                modifier = game.Bio.ProfileOf(modifierSeed);
                if (modifier == null) return null;
            }

            return Synthesis.Compute(new SynthesisInput
            {
                Active = active,
                ActiveCleaned = washed,
                Form = form,
                Stabiliser = stabiliser,
                Modifier = modifier,
                ModifierCleaned = washed,
            });
        }

        /// <summary>The catch of a substance as rich text — amber, one arrow per level, its name — or "no side effect".</summary>
        public static string SideText(GameBootstrap game, BioSideEffect side, int level)
            => side == BioSideEffect.None || level <= 0
                ? Loc(game, "ui.bio.no_side")
                : "<color=" + Hex(UiKit.Warn) + ">" + new string('▼', Mathf.Clamp(level, 1, 3)) + " " + SideLabel(game, side) + "</color>";

        /// <summary>The analysed profile of a deposit: purity, the traits with their levels, the trace trait —
        /// joined by <paramref name="separator"/> (a line break for a card, " · " for a list line).</summary>
        public static string MaterialText(GameBootstrap game, MaterialProfile material, string separator)
        {
            var sb = new StringBuilder();
            sb.Append(Loc(game, "ui.bio.purity")).Append(": ").Append(material.Purity).Append("/5");
            var traits = new StringBuilder();
            for (int i = 1; i < material.Levels.Length; i++)
            {
                if (material.Levels[i] <= 0 || (MatTrait)i == material.Trace) continue;
                if (traits.Length > 0) traits.Append(", ");
                traits.Append(TraitName(game, (MatTrait)i)).Append(' ').Append(material.Levels[i]);
            }

            if (traits.Length > 0)
            {
                sb.Append(separator).Append(Loc(game, "ui.bio.traits")).Append(": ").Append(traits);
            }

            if (material.Trace != MatTrait.None)
            {
                sb.Append(separator).Append(Loc(game, "ui.bio.trace")).Append(": ").Append(TraitName(game, material.Trace));
            }

            return sb.ToString();
        }

        private static string TraitName(GameBootstrap game, MatTrait trait)
            => Loc(game, "bio.trait." + trait.ToString().ToLowerInvariant());

        /// <summary>What the lab changed on a tool or a piece of gear, as rich text: every gain as green arrows (one
        /// per level), the stat and "+n", the drawback as amber arrows and "−n". <paramref name="headers"/> puts the
        /// words "better" / "worse" above them (the lab's card); a list line passes false and " · ".</summary>
        public static string ModsText(GameBootstrap game, ItemMods mods, bool headers, string separator)
        {
            var sb = new StringBuilder();
            bool gains = mods.First != ModStat.None || mods.Second != ModStat.None;
            if (headers && gains)
            {
                sb.Append(Dim(Loc(game, "ui.bio.gain")));
            }

            AppendMod(sb, game, mods.First, mods.FirstLevel, true, separator);
            AppendMod(sb, game, mods.Second, mods.SecondLevel, true, separator);
            if (mods.Drawback != ModStat.None)
            {
                if (headers)
                {
                    if (sb.Length > 0) sb.Append(separator);
                    sb.Append(Dim(Loc(game, "ui.bio.loss")));
                }

                AppendMod(sb, game, mods.Drawback, mods.DrawbackLevel, false, separator);
            }

            return sb.ToString();
        }

        private static void AppendMod(StringBuilder sb, GameBootstrap game, ModStat stat, int level, bool gain, string separator)
        {
            if (stat == ModStat.None || level <= 0) return;
            if (sb.Length > 0) sb.Append(separator);
            sb.Append("<color=").Append(Hex(gain ? UiKit.Ok : UiKit.Warn)).Append('>').Append(gain ? '▲' : '▼', level).Append("</color> ")
                .Append(Loc(game, "bio.stat." + stat.ToString().ToLowerInvariant()))
                .Append(gain ? " +" : " −").Append(level);
        }
    }
}
