// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3731: the trade window's Samples section. Under the backpack rows of the left column the kinds in the sample
    /// case are listed — kind icon, name, count and, for an analysed plant or animal, its effect icon — each with the
    /// same −/n/+ control as a backpack row. A sample is offered through the same offer list (its line carries the
    /// sample key); the server takes it out of the sample case and puts it into the partner's.
    /// </summary>
    public sealed partial class PlayerInteractions
    {
        private const float SampleHeadH = 30f;

        private GameObject _sampleHead;
        private Text _sampleHeadText;
        private readonly List<SampleRow> _sampleRows = new List<SampleRow>();
        private int _renderedBioRevision = -1;

        /// <summary>One sample line: kind icon + name + count (+ effect icon) + the −/n/+ offer control.</summary>
        private sealed class SampleRow
        {
            public GameObject Go;
            public Image Back, Kind, Effect;
            public Text Name, Have, Offered;
            public string Item;
        }

        /// <summary>Whether an item key is a sample of the sample case (a species sample or a mineral sample).</summary>
        private static bool IsSampleKey(string item)
        {
            if (string.IsNullOrEmpty(item))
            {
                return false;
            }

            string baseKey = ItemKey.Base(item);
            return baseKey == BioItems.Sample || baseKey == BioItems.MineralSample;
        }

        /// <summary>What an offer line is called: an item by its display name, a sample as "Sample · species" /
        /// "Mineral Sample · material" — so a deposit's sample never reads like the ore itself.</summary>
        private string OfferName(BlocksBeyondTheStars.Shared.Localization.Localizer loc, string item)
        {
            if (!IsSampleKey(item))
            {
                return ItemName(loc, item);
            }

            string kind = loc.Get("item." + ItemKey.Base(item) + ".name");
            string name = BioLabUi.SampleName(Game, item);
            return string.IsNullOrEmpty(name) || name == kind ? kind : kind + " · " + name;
        }

        /// <summary>Fills the Samples section from the sample case, in the order the inventory's Samples page lists it
        /// (without its filter). The heading and the rows are moved behind the backpack rows on every rebuild, so a
        /// backpack row pooled later still sits above them. Returns the number of kinds listed.</summary>
        private int FillSampleRows(BlocksBeyondTheStars.Shared.Localization.Localizer loc, Dictionary<string, int> offered)
        {
            var entries = SampleCaseView.Ordered(SampleCaseView.Entries(Game.Bio), SampleKindFilter.All, SampleEffectFilter.All,
                e => BioLabUi.SampleName(Game, e.Item));
            EnsureSampleHead();
            _sampleHeadText.text = loc.Get("ui.trade.samples");
            _sampleHead.SetActive(entries.Count > 0);
            _sampleHead.transform.SetAsLastSibling();

            int i = 0;
            foreach (var entry in entries)
            {
                var row = SampleRowAt(i++);
                row.Item = entry.Item;
                int give = offered.TryGetValue(entry.Item, out var o) ? o : 0;
                SetBioIcon(row.Kind, BioSymbols.KindIcon(entry.Kind), UiKit.Cyan);
                bool hasEffect = entry.Effect != BioEffect.None;
                row.Effect.enabled = hasEffect;
                if (hasEffect)
                {
                    SetBioIcon(row.Effect, BioSymbols.EffectIcon(entry.Effect), BioIcons.EffectColor(entry.Effect));
                }

                row.Name.text = BioLabUi.SampleName(Game, entry.Item);
                row.Have.text = $"×{entry.Count}";
                row.Offered.text = give > 0 ? give.ToString() : "–";
                row.Offered.color = give > 0 ? UiKit.Ok : UiKit.CyanDim;
                row.Back.color = give > 0 ? new Color(0.10f, 0.32f, 0.52f, 0.95f) : new Color(0.06f, 0.14f, 0.26f, 0.85f);
                row.Go.transform.SetAsLastSibling();
                row.Go.SetActive(true);
            }

            for (; i < _sampleRows.Count; i++)
            {
                _sampleRows[i].Go.SetActive(false);
            }

            return entries.Count;
        }

        /// <summary>Points a pooled icon at one of the bio lab's symbols (<see cref="BioIcons.Find"/>), tinted; the plain
        /// disc when the art is missing, so a row never loses its mark.</summary>
        private static void SetBioIcon(Image image, string icon, Color tint)
        {
            var sprite = BioIcons.Find(icon);
            image.sprite = sprite != null ? sprite : UiKit.DiscSprite;
            image.color = tint;
        }

        /// <summary>The section heading — a row of the inventory list like any other, built once.</summary>
        private void EnsureSampleHead()
        {
            if (_sampleHead != null)
            {
                return;
            }

            float w = ColLeftW - 8f; // list padding
            _sampleHead = new GameObject("SamplesHead", typeof(RectTransform));
            _sampleHead.transform.SetParent(_invContent, false);
            _sampleHead.GetComponent<RectTransform>().sizeDelta = new Vector2(w, SampleHeadH);
            var le = _sampleHead.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = SampleHeadH;
            _sampleHeadText = UiKit.AddText(_sampleHead.transform, 4f, 4f, w - 8f, SampleHeadH - 4f, string.Empty, 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
        }

        private SampleRow SampleRowAt(int index)
        {
            while (index >= _sampleRows.Count)
            {
                float w = ColLeftW - 8f; // list padding
                var go = new GameObject("SampleRow", typeof(RectTransform));
                go.transform.SetParent(_invContent, false);
                go.GetComponent<RectTransform>().sizeDelta = new Vector2(w, InvRowH);
                var le = go.AddComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = InvRowH;

                var row = new SampleRow { Go = go };
                row.Back = UiKit.AddPanel(go.transform, 0f, 0f, w, InvRowH, new Color(0.06f, 0.14f, 0.26f, 0.85f));
                row.Kind = UiKit.AddImage(go.transform, 12f, 12f, 32f, 32f, UiKit.DiscSprite, UiKit.Cyan);
                // The same columns as a backpack row: name over the count, then the three 44 px touch targets.
                float ctrlX = w - 140f;
                row.Name = UiKit.AddText(go.transform, 56f, 5f, ctrlX - 56f - 8f, 26f, string.Empty, 19, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                row.Have = UiKit.AddText(go.transform, 56f, 30f, 52f, 22f, string.Empty, 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                row.Effect = UiKit.AddImage(go.transform, 112f, 31f, 20f, 20f, UiKit.DiscSprite, Color.white);
                row.Effect.enabled = false;
                UiKit.AddButton(go.transform, ctrlX, 6f, 44f, 44f, "−", () => Adjust(row.Item, -1));
                row.Offered = UiKit.AddText(go.transform, ctrlX + 44f, 6f, 48f, 44f, "–", 20, UiKit.CyanDim, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.AddButton(go.transform, ctrlX + 92f, 6f, 44f, 44f, "+", () => Adjust(row.Item, +1));
                _sampleRows.Add(row);
            }

            return _sampleRows[index];
        }
    }
}
