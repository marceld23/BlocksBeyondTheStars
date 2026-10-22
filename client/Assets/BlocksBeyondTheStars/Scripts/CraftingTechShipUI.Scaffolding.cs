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
    /// uGUI scaffolding and text helpers of <see cref="CraftingTechShipUI"/> every tab uses: scroll views, cards,
    /// search boxes, section headings and the item/locale/description name helpers.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        /// <summary>#2566: the A–Z order of the lists (crafting, blueprints, Sandbox catalog) in the language the titles are
        /// shown in — not the operating system's culture, which put a German "Äpfel" wherever the PC's language wanted it.</summary>
        private System.StringComparer NameOrder => LocaleCollation.Comparer(Game.Localizer?.Locale ?? Game.Locale);

        /// <summary>A non-selectable section heading inside the Alliances list; returns the y below it.</summary>
        private float AllianceSection(string text, float y)
        {
            UiKit.AddText(_listContent, 4, y, 760, 30, text, 18, UiKit.Cyan, TextAnchor.LowerLeft, FontStyle.Bold);
            return y + 36f;
        }

        /// <param name="trimRight">#2729: room kept free at the card's right end for a button beside it (a hold stack's
        /// "Take").</param>
        private void AddCard(float y, string title, string icon, string status, Color statusCol, string key, System.Action onClick, float indent = 0f, string contentKey = null,
            float trimRight = 0f)
        {
            float cw = 780f - indent - trimRight;
            var card = UiKit.AddButton(_listContent, indent, y, cw, 78, string.Empty, onClick);
            if (_selected == key)
            {
                card.GetComponent<Image>().color = UiKit.Cyan;
            }

            float tx = 16f;
            // Prefer the real content-styled icon (item/material/module art); fall back to the category icon.
            var sprite = string.IsNullOrEmpty(contentKey) ? null : IconResolver.Resolve(contentKey, Game);
            bool placed = sprite != null
                ? UiKit.AddIconSprite(card.transform, 14, 14, 50, sprite, IconResolver.Tint(contentKey, Game)) != null
                : UiKit.AddIcon(card.transform, 14, 14, 50, icon) != null;
            if (placed)
            {
                tx = 78f;
            }

            UiKit.AddText(card.transform, tx, 8, cw - tx - 16, 40, title, 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(card.transform, tx, 44, cw - tx - 16, 28, status, 18, statusCol, TextAnchor.MiddleLeft);

            // Freshly crafted / freshly unlocked → the card pulses for a moment (celebration juice).
            if (!string.IsNullOrEmpty(contentKey) && contentKey == _celebrateKey && Time.unscaledTime < _celebrateUntil)
            {
                card.gameObject.AddComponent<CardPulse>().Until = _celebrateUntil;
            }
        }

        private string IconFor(string item)
        {
            var def = Game.Content.GetItem(item);
            if (def == null)
            {
                return null;
            }

            if (def.Tool?.Kind == ToolKind.Weapon) return "cat_weapons";
            if (def.Category == ItemCategory.Tool) return "cat_tools";
            if (def.Category == ItemCategory.Consumable) return "cat_medicine";
            if (def.ArmorResistance > 0f || def.OxygenBonus > 0f || def.ThermalInsulation > 0f) return "cat_suit";
            if (def.CraftTab == "machines") return "cat_machines";
            if (def.Category == ItemCategory.Block || !string.IsNullOrEmpty(def.PlacesBlock)) return "cat_blocks";
            return "cat_components";
        }

        // --- uGUI scaffolding ---

        private static RectTransform MakeScroll(Transform parent, float x, float y, float w, float h)
        {
            var viewGo = new GameObject("Scroll", typeof(RectTransform));
            viewGo.transform.SetParent(parent, false);
            UiKit.Place(viewGo, x, y, w, h);
            var sr = viewGo.AddComponent<ScrollRect>();
            sr.horizontal = false;
            var mask = viewGo.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewGo.transform, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.sizeDelta = new Vector2(w, h);
            content.anchoredPosition = Vector2.zero;
            sr.content = content;
            sr.viewport = viewGo.GetComponent<RectTransform>();
            sr.scrollSensitivity = 30f;
            UiKit.AddInlineScrollbar(sr); // sidebar/list/detail all get a position indicator (#664)
            return content;
        }

        private const float ContentBottomPad = 28f; // breathing room so the last row clears the mask edge

        private static void SetContentHeight(RectTransform content, float h)
        {
            // Floor the content at the VIEWPORT height (so it fills the masked area) but let it SHRINK back
            // for short pages — flooring at the content's own current height (the old code) never shrank, so
            // a tall page left the scroll range stuck large. Add bottom padding so the last row isn't clipped.
            float viewportH = content.parent is RectTransform vp ? vp.rect.height : 0f;
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(h + ContentBottomPad, viewportH));
        }

        /// <summary>Scrolls a list/detail view back to the top — called when its page changes so a position
        /// carried over from a previous (taller) page can't hide the new page's first rows.</summary>
        private static void ScrollToTop(RectTransform content)
        {
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
            if (content.parent != null && content.parent.GetComponent<ScrollRect>() is { } sr)
            {
                sr.velocity = Vector2.zero; // kill any fling momentum so it stays at the top
            }
        }

        private void AddSearchBox(Transform parent, float x, float y, float w, float h)
        {
            // Built through UiKit.AddInput like every other field, NOT assembled by hand (#1404): the themed
            // builder attaches the PadTextEntryBridge that keeps a field deactivated while a pad is in hand.
            // This box used to be the one field without it — and it sits right under the tab row, so the
            // stick landed on it first: the activated field swallowed the navigation axes, and because
            // TextFieldFocused() then read "typing", GameMenu ignored B and Start too. No way out on a pad.
            var input = UiKit.AddInput(parent, x, y, w, h, _search,
                s => { _search = s; RebuildList(); }, L("ui.craft.search"), fontSize: 22);
            input.gameObject.name = "Search";
            input.GetComponent<Image>().color = new Color(0.07f, 0.17f, 0.34f, 0.95f); // the old tint ÷ the resting 0.70
            ((Text)input.placeholder).color = UiKit.CyanDim;
        }

        private static void SetInteractable(Button b, bool on)
        {
            b.interactable = on;
            var img = b.GetComponent<Image>();
            if (!on)
            {
                img.color = new Color(0.3f, 0.34f, 0.4f, 0.8f);
            }
        }

        private static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                Destroy(t.GetChild(i).gameObject);
            }
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        /// <summary>Localized label for a raw data/enum identifier (blueprint category, body kind/status,
        /// objective type, story-fragment category): resolves <paramref name="prefix"/> + the lower-cased id
        /// and falls back to the raw id for values without a key, so new data/enum members degrade to
        /// today's behaviour instead of showing a bracketed key.</summary>
        private string IdLabel(string prefix, string id)
            => string.IsNullOrEmpty(id) || Game?.Localizer?.Has(prefix + id.ToLowerInvariant()) != true
                ? id
                : L(prefix + id.ToLowerInvariant());

        /// <summary>The kind word of a station body: "Space Station", or "Derelict station" for a broken old one (#2632).</summary>
        private string StationKindLabel(string bodyId)
            => L(BlocksBeyondTheStars.Client.Core.SpaceTargeting.IsDerelictId(bodyId) ? "ui.map.kind_derelict" : "ui.map.kind_station");

        // The shared display-name helper (#927) owns the base-key strip + modifier suffixes; the resolver
        // names player-designed forms after their creator's label instead of the generic "own form".
        private string ItemName(string item)
            => BlocksBeyondTheStars.Shared.Localization.ItemNames.Display(Game.Localizer, item,
                _customFormName ??= idx => Game.CustomShapes?.NameOf(idx),
                _speciesName ??= seed => Game.Bio.SpeciesName(seed)); // #2201: a sample or a seedling names its species

        private System.Func<int, string> _customFormName;
        private System.Func<uint, string> _speciesName;
        /// <summary>A description block in the detail pane that grows with its text (#2545): a fixed 80 px box with a
        /// fixed 84 px step let a long text — the boat's, a cargo module's — run into "Needs:" and the ingredient rows
        /// below. The tech tab grew its own block since #2249 (the bio lab blueprints); every detail pane now shares
        /// this. Returns the next y.</summary>
        private float AddDetailDescription(string desc, float y)
        {
            var t = UiKit.AddText(_detail, 8, y, 620, 80, desc, 20, UiKit.CyanDim, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            float height = Mathf.Max(80f, t.preferredHeight);
            t.rectTransform.sizeDelta = new Vector2(620f, height);
            return y + height + 4f;
        }

        /// <summary>A detail-pane line that wraps when its text is longer than the pane is wide (#2585): the aboard station
        /// reason names the ship's module, where it is built and that the block in the ship is decoration — two lines, which
        /// a fixed one-line box let run off the pane's right edge. The box grows with its text like
        /// <see cref="AddDetailDescription"/>, so the rows below move down. Returns the y just below the text.</summary>
        private float AddWrappedDetailLine(string text, float y, float lineHeight, int size, Color color)
        {
            var t = UiKit.AddText(_detail, 8, y, 620, lineHeight, text, size, color, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            float height = Mathf.Max(lineHeight, t.preferredHeight);
            t.rectTransform.sizeDelta = new Vector2(620f, height);
            return y + height;
        }

        private string Desc(string key)
        {
            // Localizer.Get returns "[key]" (never the bare key) on a miss, so comparing against the key
            // can't detect one — ask Has() instead, like WikiUI does, and show nothing for absent texts.
            // #2503: item, blueprint and module descriptions name controls by {key:Action} tokens ("press
            // {key:Interact}", "Use {key:Place} to …") — resolved like a HUD prompt, so they follow the device and
            // the player's rebinding.
            return Game?.Localizer?.Has(key) == true ? HudUi.ExpandKeyTokens(Game.Localizer, L(key)) : string.Empty;
        }
    }
}
