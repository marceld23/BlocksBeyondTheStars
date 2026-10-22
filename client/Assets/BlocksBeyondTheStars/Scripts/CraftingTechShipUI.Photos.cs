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
    /// The Photos tab of <see cref="CraftingTechShipUI"/>: the local camera gallery with thumbnails and an editable
    /// note per photo.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        // --- Photos tab (local camera gallery: thumbnails + editable per-photo note) ---

        private PhotoStore _photoStore;
        private long _photoSeed = long.MinValue;
        private readonly System.Collections.Generic.Dictionary<string, string> _photoNoteDraft = new();

        /// <summary>The photo store for the current world (cached; rebuilt when the world seed changes), always
        /// re-scanned so shots taken since the menu opened appear.</summary>
        private PhotoStore PhotoStoreNow()
        {
            long seed = Game != null ? Game.WorldSeed : 0L;
            if (_photoStore == null || _photoSeed != seed)
            {
                _photoStore?.UnloadTextures();
                _photoStore = PhotoStore.Open(seed);
                _photoSeed = seed;
            }
            else
            {
                _photoStore.Reload();
            }

            return _photoStore;
        }

        private static string PhotoWhen(PhotoStore.Entry e)
        {
            try { return new System.DateTime(e.TakenUtcTicks, System.DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm"); }
            catch { return string.Empty; }
        }

        /// <summary>Places a photo texture into the parent, letter-boxed to fit the given box (keeps aspect).</summary>
        private static void AddPhoto(Transform parent, float x, float y, float boxW, float boxH, Texture2D tex)
        {
            float w = boxW, h = boxH;
            if (tex != null && tex.width > 0 && tex.height > 0)
            {
                float ar = (float)tex.width / tex.height;
                if (boxW / boxH > ar) { w = boxH * ar; }
                else { h = boxW / ar; }
            }

            var go = new GameObject("Photo", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x + (boxW - w) * 0.5f, y + (boxH - h) * 0.5f, w, h);
            var ri = go.AddComponent<RawImage>();
            ri.texture = tex;
            ri.color = tex != null ? Color.white : new Color(0.1f, 0.16f, 0.24f, 1f);
            ri.raycastTarget = false;
        }

        private float BuildPhotosList()
        {
            var store = PhotoStoreNow();
            var shots = store.Entries;

            float y = 0f;
            UiKit.AddText(_listContent, 8, y, 760, 28, string.Format(L("ui.photos.count"), shots.Count), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 34f;

            if (shots.Count == 0)
            {
                var empty = UiKit.AddText(_listContent, 8, y, 760, 80, HudUi.ExpandKeyTokens(Game?.Localizer, L("ui.photos.empty")), 18, UiKit.CyanDim, TextAnchor.UpperLeft); // {key:Place} (#2502)
                empty.horizontalOverflow = HorizontalWrapMode.Wrap;
                return y + 90f;
            }

            // Selecting nothing yet? Land on the newest so the preview pane isn't empty on open.
            if (string.IsNullOrEmpty(_selected) || shots.All(s => s.File != _selected))
            {
                _selected = shots[0].File;
            }

            foreach (var e in shots)
            {
                string file = e.File;
                var card = UiKit.AddButton(_listContent, 0, y, 780, 104, string.Empty, () => { _selected = file; RebuildList(); RebuildDetail(); });
                if (_selected == file)
                {
                    card.GetComponent<Image>().color = UiKit.Cyan;
                }

                AddPhoto(card.transform, 10, 10, 148, 84, store.GetTexture(file));
                UiKit.AddText(card.transform, 172, 12, 596, 26, PhotoWhen(e), 18, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                string note = string.IsNullOrEmpty(e.Note) ? L("ui.photos.note_placeholder") : e.Note;
                var nt = UiKit.AddText(card.transform, 172, 42, 596, 52, note, 15,
                    string.IsNullOrEmpty(e.Note) ? UiKit.CyanDim : UiKit.TextCol, TextAnchor.UpperLeft);
                nt.horizontalOverflow = HorizontalWrapMode.Wrap;

                y += 112f;
            }

            return y;
        }

        private float BuildPhotosDetail()
        {
            var store = PhotoStoreNow();
            var e = string.IsNullOrEmpty(_selected) ? null : store.Entries.FirstOrDefault(x => x.File == _selected);
            if (e == null)
            {
                var hint = UiKit.AddText(_detail, 8, 16, 620, 120, L("ui.photos.select_hint"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                return 140f;
            }

            float y = 8f;
            UiKit.AddText(_detail, 8, y, 620, 28, L("ui.photos.taken") + ": " + PhotoWhen(e), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 36f;

            AddPhoto(_detail, 8, y, 624, 360, store.GetTexture(e.File));
            y += 372f;

            UiKit.AddText(_detail, 8, y, 620, 24, L("ui.photos.note"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 28f;

            string draft = _photoNoteDraft.TryGetValue(e.File, out var d) ? d : e.Note;
            string file = e.File;
            var field = UiKit.AddInput(_detail, 8, y, 624, 96, draft, v => _photoNoteDraft[file] = v, L("ui.photos.note_placeholder"), 280);
            field.lineType = InputField.LineType.MultiLineNewline;
            var fieldText = field.textComponent;
            if (fieldText != null) { fieldText.alignment = TextAnchor.UpperLeft; }
            y += 104f;

            UiKit.AddButton(_detail, 8, y, 200, 46, L("ui.photos.save_note"), () =>
            {
                string nm = _photoNoteDraft.TryGetValue(file, out var dd) ? dd : string.Empty;
                store.SetNote(file, nm);
                RebuildList(); // refresh the list note preview
            });

            var del = UiKit.AddButton(_detail, 432, y, 200, 46, L("ui.photos.delete"), () =>
            {
                store.Delete(file);
                _photoNoteDraft.Remove(file);
                _selected = string.Empty;
                RebuildList();
                RebuildDetail();
            });
            del.GetComponent<Image>().color = new Color(0.5f, 0.22f, 0.22f);

            return y + 60f;
        }
    }
}
