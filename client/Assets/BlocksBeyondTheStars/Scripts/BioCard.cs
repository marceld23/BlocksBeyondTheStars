// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Bio;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A card of the bio lab built from rows (#3720): what a sample, a deposit, a preparation or a changed item carries,
    /// one property per row with its symbol in a column on the left and its words beside it. It replaces the one
    /// paragraph of text the lab and the inventory used to print — a paragraph cannot carry an icon — and it shows
    /// the three rules the lab acted on without saying so: what a sample is made of and the form that suits it, the
    /// stabiliser that takes a catch away, and why a species carries its effect.
    /// <para>
    /// A card is a cursor: every call draws at <see cref="Y"/> and moves it down, so the caller puts the next thing
    /// under whatever the card needed. Everything shown is derived on the client with the <c>Shared</c> rules; the
    /// caller decides what the player may see (an unanalysed sample gets no card).
    /// </para>
    /// </summary>
    public sealed class BioCard
    {
        private const float IconSize = 22f;
        private const float Indent = 40f; // the words' left edge: under the headline's label
        private const float RowGap = 5f;
        private const string DimHex = "#9fb4c8"; // the Codex's dim text colour

        private readonly GameBootstrap _game;
        private readonly Transform _parent;
        private readonly float _x, _w;
        private readonly int _size;

        public BioCard(GameBootstrap game, Transform parent, float x, float y, float w, int size = 18)
        {
            _game = game;
            _parent = parent;
            _x = x;
            _w = w;
            _size = size;
            Y = y;
        }

        /// <summary>Where the next row goes.</summary>
        public float Y { get; private set; }

        public void Gap(float height) => Y += height;

        private string L(string key) => _game?.Localizer?.Get(key) ?? key;

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        private static string Dim(string text) => "<color=" + DimHex + ">" + text + "</color>";

        private static string Lower(System.Enum value) => value.ToString().ToLowerInvariant();

        // ---------------- rows ----------------

        /// <summary>The headline of an effect: its icon in its family's colour, its name with the tier, and the
        /// strength bar at the right edge.</summary>
        public void Headline(string text, BioEffect effect, int level)
        {
            const float barW = 150f;
            BioIcons.AddEffect(_parent, _x, Y + 2f, 30f, effect);
            float room = _w - Indent - barW - 18f;
            float width = Mathf.Min(room, UiKit.MeasureWidth(text, _size + 4, FontStyle.Bold) + 4f);
            var label = UiKit.AddText(_parent, _x + Indent, Y, width, 34f, text, _size + 4, UiKit.TextCol,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabel(label, 13, _size + 4);
            BioIcons.AddLevelBar(_parent, _x + Indent + width + 18f, Y + 11f, barW, 12f, level); // right behind the name
            Y += 42f;
        }

        /// <summary>A dim heading over a group of rows ("Better", "Traits").</summary>
        public void Heading(string text)
        {
            UiKit.AddText(_parent, _x, Y, _w, 24f, text, _size - 1, UiKit.CyanDim, TextAnchor.MiddleLeft);
            Y += 28f;
        }

        /// <summary>A row of words without a symbol, aligned with the rows that have one.</summary>
        public void Line(string rich) => Y += Para(rich, UiKit.TextCol) + RowGap;

        /// <summary>A row with an icon of the symbol set in the symbol column.</summary>
        public void IconRow(string icon, Color tint, string rich)
        {
            BioIcons.Add(_parent, _x + 4f, Y + 1f, IconSize, icon, tint);
            Y += Mathf.Max(IconSize, Para(rich, UiKit.TextCol)) + RowGap;
        }

        private void SpriteRow(Sprite sprite, string rich)
        {
            UiKit.AddIconSprite(_parent, _x + 2f, Y - 1f, IconSize + 4f, sprite, Color.white);
            Y += Mathf.Max(IconSize, Para(rich, UiKit.TextCol)) + RowGap;
        }

        /// <summary>A material trait: its chip, its name and one pip per level (a trace trait has none — it is
        /// always level 1 and says so in words).</summary>
        private void TraitRow(MatTrait trait, int level, bool trace)
        {
            BioIcons.AddTrait(_parent, _x + 1f, Y - 2f, IconSize + 6f, trait, trace);
            string name = L("bio.trait." + Lower(trait));
            string text = trace ? L("ui.bio.trace") + ": " + name : name;
            UiKit.AddText(_parent, _x + Indent, Y, _w - Indent, IconSize + 2f, text, _size, trace ? UiKit.CyanDim : UiKit.TextCol, TextAnchor.MiddleLeft);
            if (!trace)
            {
                float after = _x + Indent + UiKit.MeasureWidth(text, _size, FontStyle.Normal) + 14f;
                BioIcons.AddPips(_parent, after, Y + 6f, level, 4, 11f, UiKit.Cyan);
            }

            Y += IconSize + 8f;
        }

        /// <summary>"Stabiliser against it: Heat-proof" with the trait's chip — the rule the mixer follows
        /// (<see cref="BioRules.Counter"/>, the heat- and cold-proof steadiers), shown where it matters.</summary>
        private void StabiliserHint(MatTrait trait)
        {
            if (trait == MatTrait.None) return;
            BioIcons.AddTrait(_parent, _x + 3f, Y - 1f, IconSize + 2f, trait);
            string text = L("ui.bio.stabiliser_against").Replace("{0}", L("bio.trait." + Lower(trait)));
            Y += Mathf.Max(IconSize, Para(Dim(text), UiKit.TextCol)) + RowGap;
        }

        private float Para(string rich, Color color)
        {
            var t = UiKit.AddText(_parent, _x + Indent, Y, _w - Indent, 40f, rich, _size, color, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            float h = t.preferredHeight;
            t.rectTransform.sizeDelta = new Vector2(_w - Indent, h + 6f);
            return h;
        }

        // ---------------- properties ----------------

        /// <summary>The catch of a substance: the icon of the effect it undoes in amber, its name behind the down
        /// marks, what it does (<paramref name="withDesc"/>) and the stabiliser that takes it away
        /// (<paramref name="withFix"/>) — or "no side effect".</summary>
        public void Side(BioSideEffect side, int level, bool withDesc, bool withFix)
        {
            if (side == BioSideEffect.None || level <= 0)
            {
                Line(L("ui.bio.no_side"));
                return;
            }

            IconRow(BioSymbols.SideIcon(side), UiKit.Warn, L("ui.bio.side") + ": " + BioLabUi.SideText(_game, side, level));
            if (withDesc) Line(Dim(L("bio.side." + Lower(side) + ".desc")));
            if (withFix) StabiliserHint(BioRules.Counter(side));
        }

        /// <summary>How a substance takes the weather: the flame or the snowflake in amber for a sensitive one, and the
        /// stabiliser that steadies it (<paramref name="withFix"/>).</summary>
        public void Thermal(BioThermal thermal, bool withFix)
        {
            string text = Dim(L("bio.thermal." + Lower(thermal)));
            string icon = BioSymbols.ThermalIcon(thermal);
            if (icon == null)
            {
                Line(text);
                return;
            }

            IconRow(icon, UiKit.Warn, text);
            if (withFix) StabiliserHint(BioSymbols.ThermalCounter(thermal));
        }

        private void Toxicity(int toxicity)
        {
            if (toxicity <= 0)
            {
                Line(L("ui.bio.not_toxic"));
                return;
            }

            IconRow(BioSymbols.ToxicIcon, UiKit.Warn,
                "<color=" + Hex(UiKit.Warn) + ">" + L("ui.bio.toxicity") + " " + toxicity + "/3</color>");
            Line(Dim(L("ui.bio.toxic_hint")));
        }

        /// <summary>What a sample is made of and the form a mix of it is steadiest in (#3720: the lab gave the bonus
        /// and never said so).</summary>
        private void MadeOf(BioCarrier carrier)
        {
            if (carrier == BioCarrier.Mineral) return;
            var form = BioRules.PreferredForm(carrier);
            string formKey = BioItems.PreparationKey(form);
            string text = L("ui.bio.made_of") + ": " + L("bio.carrier." + Lower(carrier))
                          + "  —  " + L("ui.bio.best_form").Replace("{0}", L("item." + formKey + ".name"));
            var sprite = IconResolver.Resolve(formKey, _game);
            if (sprite != null) SpriteRow(sprite, text);
            else Line(text);
        }

        /// <summary>"Fits how it lives: lives by lava · hot place" — the tags of this species that pull exactly this
        /// effect. Nothing for a cross (its effect is a parent's) or when the effect was the seed's surprise.</summary>
        private void Reasons(BioEffect effect, NetBioSpecies species)
        {
            if (species == null || !species.HasContext || species.ParentA != 0) return;
            var reasons = BioSymbols.Reasons((BioTag)species.Tags, effect);
            if (reasons.Count == 0) return;

            var sb = new StringBuilder();
            foreach (var tag in reasons)
            {
                if (sb.Length > 0) sb.Append("  ·  ");
                sb.Append(L(BioSymbols.TagKey(tag)));
            }

            Line(Dim(L("ui.bio.fits_life").Replace("{0}", sb.ToString())));
        }

        // ---------------- whole cards ----------------

        /// <summary>The analysed profile of a plant or an animal.</summary>
        public void Profile(BioProfile profile, NetBioSpecies species)
        {
            Headline(BioLabUi.EffectLabel(_game, profile.Effect, profile.Level), profile.Effect, profile.Level);
            Line(Dim(L("bio.effect." + Lower(profile.Effect) + ".desc")));
            Reasons(profile.Effect, species);

            UiKit.AddImage(_parent, _x + 9f, Y + 6f, 12f, 12f, UiKit.DiscSprite, BioIcons.RarityColor(profile.Rarity));
            Line(L("ui.bio.rarity") + ": <color=" + Hex(BioIcons.RarityColor(profile.Rarity)) + ">" + L("bio.rarity." + profile.Rarity) + "</color>");
            Line(L("ui.bio.substance") + ": <b>" + profile.Substance + "</b>");
            Side(profile.Side, profile.SideLevel, true, true);
            Toxicity(profile.Toxicity);
            IconRow(BioSymbols.GroupIcon(profile.Group), UiKit.Cyan, L("ui.bio.group") + ": " + L("bio.group." + profile.Group));
            MadeOf(profile.Carrier);
            Line(L("ui.bio.duration") + ": " + L("ui.bio.seconds").Replace("{0}", profile.DurationSeconds.ToString()));
            Thermal(profile.Thermal, true);
            if (species != null && species.ParentA != 0 && species.ParentB != 0 && _game != null)
            {
                _game.Bio.Species.TryGetValue(species.ParentA, out var a);
                _game.Bio.Species.TryGetValue(species.ParentB, out var b);
                Line(L("ui.bio.cross_of") + ": " + BioLabUi.SpeciesLabel(_game, a, false) + " × " + BioLabUi.SpeciesLabel(_game, b, false));
            }
        }

        /// <summary>What a preparation does besides its main effect (the caller draws the <see cref="Headline"/>): a
        /// coupled second effect, the catch, how long it lasts and how it takes the weather.
        /// <paramref name="withDesc"/> adds the one-line explanations — for a reader without the lab at hand;
        /// <paramref name="withFix"/> names the stabilisers, where the mix can still be changed.</summary>
        public void Preparation(Compound compound, bool withDesc, bool withFix)
        {
            if (withDesc) Line(Dim(L("bio.effect." + Lower(compound.Effect) + ".desc")));
            if (compound.Secondary != BioEffect.None && compound.SecondaryLevel > 0)
            {
                IconRow(BioSymbols.EffectIcon(compound.Secondary), BioIcons.EffectColor(compound.Secondary),
                    "+ <b>" + BioLabUi.EffectLabel(_game, compound.Secondary, compound.SecondaryLevel) + "</b>");
                if (withDesc) Line(Dim(L("bio.effect." + Lower(compound.Secondary) + ".desc")));
            }

            Side(compound.Side, compound.SideLevel, withDesc, withFix);
            Line(L("ui.bio.duration") + ": " + L("ui.bio.seconds").Replace("{0}", compound.DurationSeconds.ToString()));
            Thermal(compound.Thermal, withFix);
        }

        /// <summary>The analysed profile of a deposit: its purity as pips, every trait as a chip with its level, the
        /// trace trait last.</summary>
        public void Material(MaterialProfile material)
        {
            string purity = L("ui.bio.purity") + ": " + material.Purity + "/5";
            UiKit.AddText(_parent, _x + Indent, Y, _w - Indent, IconSize + 2f, purity, _size, UiKit.TextCol, TextAnchor.MiddleLeft);
            float after = _x + Indent + UiKit.MeasureWidth(purity, _size, FontStyle.Normal) + 14f;
            BioIcons.AddPips(_parent, after, Y + 6f, material.Purity, 5, 11f, UiKit.Cyan);
            Y += IconSize + 10f;

            foreach (var trait in BioIcons.TraitsByLevel(material))
            {
                TraitRow(trait, material.Levels[(int)trait], trait == material.Trace);
            }
        }

        /// <summary>What the lab changed on a tool or a piece of gear: every gain with the value's icon in green, the
        /// drawback in amber.</summary>
        public void Mods(ItemMods mods)
        {
            if (mods.First != ModStat.None || mods.Second != ModStat.None)
            {
                Heading(L("ui.bio.gain"));
                Mod(mods.First, mods.FirstLevel, true);
                Mod(mods.Second, mods.SecondLevel, true);
            }

            if (mods.Drawback != ModStat.None)
            {
                Heading(L("ui.bio.loss"));
                Mod(mods.Drawback, mods.DrawbackLevel, false);
            }
        }

        private void Mod(ModStat stat, int level, bool gain)
        {
            if (stat == ModStat.None || level <= 0) return;
            var color = gain ? UiKit.Ok : UiKit.Warn;
            IconRow(BioSymbols.StatIcon(stat), color,
                "<color=" + Hex(color) + ">" + new string(gain ? '▲' : '▼', level) + "</color> "
                + L("bio.stat." + Lower(stat)) + (gain ? " +" : " −") + level);
        }
    }
}
