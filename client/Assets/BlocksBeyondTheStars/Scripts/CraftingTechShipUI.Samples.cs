// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BlocksBeyondTheStars.Client.Portal;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Samples page of <see cref="CraftingTechShipUI"/>'s Inventory tab (#2201, #2300): the header filter chips,
    /// the sample case as cards and the readable bio-lab items (preparations, changed gear, derelict logs).
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        /// <summary>#2300: the kind chips (with their counts) and the effect button over the Samples page — the same
        /// session filter the bio lab's case uses, so both show the same choice.</summary>
        private void AddSampleFilters(Transform p, float x, float y)
        {
            var all = SampleCaseView.Entries(Game.Bio);
            var labels = new string[4];
            for (int i = 0; i < 4; i++)
            {
                var kind = (SampleKindFilter)i;
                labels[i] = BioLabUi.KindFilterLabel(Game, kind) + " " + SampleCaseView.Count(all, kind, SampleCaseView.Effect);
            }

            // #2324: chips sized by their words — "Lagerstätten 12" and "Месторождения 12" used to break in the middle of the
            // word in a fixed 112 px chip; the effect button takes what is left of the row (the list frame's 796 px).
            var chips = UiKit.AddChipRow(p, x, y, 796f, 44, labels, i =>
            {
                SampleCaseView.Kind = (SampleKindFilter)i;
                BuildHeader();
                RebuildList();
                ScrollToTop(_listContent); // a new filter is a new list (RebuildList keeps the scroll of the old one)
            }, gap: 8f, tailMin: 170f, tailPreferred: 300f, out float effectX, out float effectW);
            chips[(int)SampleCaseView.Kind].GetComponent<Image>().color = UiKit.Cyan;

            var effect = UiKit.AddButton(p, effectX, y, effectW, 44, BioLabUi.EffectFilterLabel(Game, SampleCaseView.Effect), () =>
            {
                SampleCaseView.Effect = SampleCaseView.NextEffect(SampleCaseView.Effect);
                BuildHeader();
                RebuildList();
                ScrollToTop(_listContent);
            });
            if (SampleCaseView.Effect != SampleEffectFilter.All)
            {
                effect.GetComponent<Image>().color = UiKit.Cyan;
            }
        }

        /// <summary>#2201: the sample case as cards — what was taken from plants, animals and deposits, one stack per
        /// species. Samples never lie in the backpack; the bio lab works with them, and a card's detail shows what an
        /// analysed one holds.</summary>
        private float BuildSampleList()
        {
            // #2299/#2300: the case's order and the session filter — the same list the bio lab shows.
            var all = SampleCaseView.Entries(Game.Bio);
            var shown = SampleCaseView.Ordered(all, SampleCaseView.Kind, SampleCaseView.Effect, e => BioLabUi.SampleName(Game, e.Item));
            float y = 0f;
            for (int i = 0; i < shown.Count; i++)
            {
                var entry = shown[i];
                if (i == 0 || shown[i - 1].Kind != entry.Kind)
                {
                    int n = shown.Count(e => e.Kind == entry.Kind);
                    UiKit.AddText(_listContent, 8, y, 752, 30, BioLabUi.KindLabel(Game, entry.Kind) + "  (" + n + ")", 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                    y += 36f;
                }

                string key = entry.Item;
                string origin = entry.Species?.OriginBodyName ?? string.Empty;
                // The effect in its colour (the lab's square, as rich text), what a deposit is, or "Not analysed".
                string detail = BioLabUi.SampleDetail(Game, entry);
                if (entry.Effect != BlocksBeyondTheStars.Shared.Bio.BioEffect.None)
                {
                    detail = "<color=#" + ColorUtility.ToHtmlStringRGB(BioLabUi.EffectColor(entry.Effect)) + ">" + detail + "</color>";
                }

                string status = "×" + entry.Count + "  ·  " + detail + (string.IsNullOrEmpty(origin) ? string.Empty : "  ·  " + origin);

                // The lab's icon (#2299): the deposit's material or the plant's body block; the sample item's own else.
                string icon = BioLabUi.IconKeyOf(entry.Species) is { } from && IconResolver.Resolve(from, Game) != null ? from : key;
                AddCard(y, BioLabUi.SampleName(Game, key), "cat_medicine", status, UiKit.CyanDim, "smp:" + key,
                    () => { _selected = "smp:" + key; RebuildDetail(); }, contentKey: icon);
                y += 88f;
            }

            if (shown.Count == 0)
            {
                string text = all.Count == 0 ? L("ui.bio.empty_case") : L("ui.bio.filter_empty");
                var empty = UiKit.AddText(_listContent, 8, 8, 752, 60, text, 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                empty.horizontalOverflow = HorizontalWrapMode.Wrap;
                return 76f;
            }

            return y;
        }

        // ---------------- #2201–#2206: the bio lab's items, readable without the lab ----------------

        /// <summary>A sample's card: what it is, where it is from and — once analysed in a bio lab — what it holds.</summary>
        private float DetailSample()
        {
            string key = _selected.Substring(4);
            uint seed = ItemKey.Seed(key);
            Game.Bio.Species.TryGetValue(seed, out var species);
            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 40, BioLabUi.SampleName(Game, key), 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 48f;
            UiKit.AddText(_detail, 8, y, 620, 28, BioLabUi.SampleOrigin(Game, key), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
            y += 38f;

            bool analysed = Game.Bio.Analysed(seed);
            bool mineral = ItemKey.Base(key) == BlocksBeyondTheStars.Shared.Bio.BioItems.MineralSample;
            var profile = analysed && !mineral ? Game.Bio.ProfileOf(seed) : null;
            var material = analysed && mineral ? BioLabUi.MaterialOf(Game, seed) : null;
            if (profile != null)
            {
                y = AddEffectHeadline(y, profile.Effect, profile.Level);
                y = AddDetailPara(y, BioLabUi.ProfileText(Game, profile, species), UiKit.TextCol);
            }
            else if (material != null)
            {
                y = AddDetailPara(y, BioLabUi.MaterialText(Game, material, "\n"), UiKit.TextCol);
            }
            else
            {
                y = AddDetailPara(y, L("ui.bio.unknown_substance"), UiKit.CyanDim);
            }

            return AddSampleDiscard(y + 8f, key);
        }

        /// <summary>
        /// #2301: "Throw away" for a sample kind — the way out of a full case. The backpack's two-step (#599/#1484): the
        /// first click arms the button and names the sample, the second sends; every sample of the kind leaves the
        /// case, the research book keeps what was learned.
        /// </summary>
        private float AddSampleDiscard(float y, string key)
        {
            if (Game.Bio.SampleCount(ItemKey.Seed(key), ItemKey.Base(key) == BlocksBeyondTheStars.Shared.Bio.BioItems.MineralSample) <= 0)
            {
                return y;
            }

            bool armed = _discardArmed == _selected;
            string label = armed
                ? L("ui.inventory.discard_confirm").Replace("{item}", BioLabUi.SampleName(Game, key))
                : L("ui.inventory.discard");
            var button = UiKit.AddButton(_detail, 8, y, 320, 46, label, () =>
            {
                if (_discardArmed != _selected)
                {
                    _discardArmed = _selected; // the first click only asks
                    RebuildDetail();
                    return;
                }

                _discardArmed = string.Empty;
                Game.Network?.SendDiscardSample(key);
                _selected = string.Empty; // the entry is about to vanish from the list
                RebuildList();
                RebuildDetail();
            });
            var img = button.GetComponent<Image>();
            if (img != null)
            {
                img.color = armed ? new Color(0.62f, 0.20f, 0.20f) : new Color(0.40f, 0.26f, 0.26f);
            }

            y += 54f;
            if (armed)
            {
                var warn = UiKit.AddText(_detail, 8, y, 620, 48, L("ui.inventory.discard_warn"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                warn.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 52f;
            }

            return y;
        }

        /// <summary>What a backpack item of the bio lab carries in its key: a preparation's effect, catch and duration,
        /// or what the lab changed on a tool or a piece of gear. Nothing for any other item.</summary>
        private float AddBioItemInfo(string item, float y)
        {
            if (BlocksBeyondTheStars.Shared.Bio.BioItems.CompoundOf(item) is { } compound)
            {
                y = AddEffectHeadline(y, compound.Effect, compound.Level);
                return AddDetailPara(y, BioLabUi.CompoundText(Game, compound, true), UiKit.TextCol) + 8f;
            }

            var mods = BlocksBeyondTheStars.Shared.Bio.ItemMods.Of(item);
            if (!mods.IsEmpty)
            {
                UiKit.AddText(_detail, 8, y, 620, 26, L("ui.bio.changed"), 18, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                y += 30f;
                return AddDetailPara(y, BioLabUi.ModsText(Game, mods, true, "\n"), UiKit.TextCol) + 8f;
            }

            return y;
        }

        /// <summary>An effect's colour as a small square (there are no effect icons) and its name with the level.</summary>
        private float AddEffectHeadline(float y, BlocksBeyondTheStars.Shared.Bio.BioEffect effect, int level)
        {
            UiKit.AddImage(_detail, 8, y + 6, 18, 18, UiKit.SolidSprite, BioLabUi.EffectColor(effect));
            UiKit.AddText(_detail, 36, y, 592, 30, BioLabUi.EffectLabel(Game, effect, level), 22, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            return y + 36f;
        }

        /// <summary>A wrapped block of rich text in the detail pane; returns the y under it.</summary>
        private float AddDetailPara(float y, string text, Color color)
        {
            var t = UiKit.AddText(_detail, 8, y, 620, 40, text, 18, color, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            float h = t.preferredHeight;
            t.rectTransform.sizeDelta = new Vector2(620, h + 6f);
            return y + h + 8f;
        }

        /// <summary>#2633: an old log from a derelict's supply crate reads right here — the text is written from the seed
        /// its key carries (WorldGeneration.DerelictLogs, the same code the server uses), in the player's language — and
        /// "Read" opens it in the story reader. Any other item: nothing. Returns the next y.</summary>
        private float AddDerelictLogText(string item, float y)
        {
            if (!BlocksBeyondTheStars.WorldGeneration.DerelictLogs.IsLogItem(item))
            {
                return y;
            }

            var log = BlocksBeyondTheStars.WorldGeneration.DerelictLogs.Compose(
                BlocksBeyondTheStars.WorldGeneration.DerelictLogs.SeedOfItem(item), Game.Content?.DerelictLogs);
            if (log == null)
            {
                return AddDetailPara(y, L("ui.derelict_log.faded"), UiKit.CyanDim);
            }

            string text = log.Render(L);
            y = AddDetailPara(y, text, UiKit.TextCol);
            string title = ItemName(item);
            // From the open menu, like the Story tab's Read buttons (Open would queue behind this very menu).
            UiKit.AddButton(_detail, 8, y, 320, 46, L("ui.story.read"), () => StoryReaderUi.Instance?.OpenOverMenu(title, log.Writer, text));
            return y + 54f;
        }
    }
}
