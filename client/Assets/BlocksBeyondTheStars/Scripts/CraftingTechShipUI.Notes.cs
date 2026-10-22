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
    /// The Story tab's Notes page of <see cref="CraftingTechShipUI"/> (#1844): titled free-text notes with a small
    /// editor toolbar and a §-markup preview.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        // --- Story → Notes (#1844): titled free-text notes with a small editor + §-markup preview ---

        /// <summary>Selection key of the not-yet-saved note the "+ New note" button opens.</summary>
        private const string NewNoteKey = "note:new";
        private const int NoteMaxCount = 20;
        private const int NoteTitleMax = 40;
        private const int NoteBodyMax = 2000;
        private const float NoteBodyHeight = 420f;

        /// <summary>Unsaved edits per note id (or <see cref="NewNoteKey"/>): a rebuild of the pane — a server
        /// list arriving, a category click — recreates the input fields, and these are what they are refilled
        /// from, so no typed text is ever lost. Dropped once the server confirms the save.</summary>
        private readonly System.Collections.Generic.Dictionary<string, string> _noteDraftTitle = new();
        private readonly System.Collections.Generic.Dictionary<string, string> _noteDraftBody = new();

        /// <summary>True while the body pane shows the rendered markup instead of the text field.</summary>
        private bool _notePreview;

        /// <summary>The note body's selection memory while the editor is up (null in preview) — the style toolbar
        /// reads and moves the caret through it (#2577).</summary>
        private NoteSelectionMemory _noteBodyMemory;

        /// <summary>The style toolbar over the note body (#2577): bold, the eight vivid colours, plain. The buttons
        /// write the §-codes so nobody has to know them — see <see cref="NoteMarkup.ApplyStyle"/>.</summary>
        private void BuildNoteToolbar(string key, float x, float y)
        {
            UiKit.AddButton(_detail, x, y, 44, 38, L("ui.notes.style_bold"), () => ApplyNoteStyle(key, 'l'));
            x += 50f;
            foreach (char code in NoteMarkup.ToolbarColors)
            {
                char c = code;
                var swatch = UiKit.AddButton(_detail, x, y, 30, 38, string.Empty, () => ApplyNoteStyle(key, c));
                ColorUtility.TryParseHtmlString("#" + NoteMarkup.Palette[System.Convert.ToInt32(c.ToString(), 16)], out var colour);
                UiKit.AddImage(swatch.transform, 6, 8, 18, 22, UiKit.SolidSprite, colour);
                x += 34f;
            }

            UiKit.AddButton(_detail, x + 2f, y, 96, 38, L("ui.notes.style_plain"), () => ApplyNoteStyle(key, 'r'));
        }

        /// <summary>Applies a toolbar style to the remembered selection (or at the end when the body was never
        /// clicked), keeps the draft, and gives the field its focus and caret back.</summary>
        private void ApplyNoteStyle(string key, char code)
        {
            var memory = _noteBodyMemory;
            if (memory == null || memory.Field == null)
            {
                return;
            }

            string text = memory.Field.text ?? string.Empty;
            int start = memory.HasSelection ? memory.Anchor : text.Length;
            int end = memory.HasSelection ? memory.Focus : text.Length;
            string styled = NoteMarkup.ApplyStyle(text, start, end, code, out int caret);
            if (styled.Length > NoteBodyMax)
            {
                return; // the note is full — the field would cut the codes off anyway
            }

            memory.Field.text = styled; // onValueChanged keeps the draft too
            _noteDraftBody[key] = styled;
            memory.PutCaret(caret);
        }

        /// <summary>
        /// Remembers the note body's selection while it has focus, and puts the caret back after a style was applied
        /// (#2577). uGUI's InputField forgets its selection the moment a toolbar button takes the click (deactivating
        /// the field moves the caret to 0), so the toolbar reads what was recorded in the frames before.
        /// </summary>
        private sealed class NoteSelectionMemory : MonoBehaviour
        {
            public InputField Field;
            public int Anchor = -1, Focus = -1;
            private int _pendingCaret = -1;

            public bool HasSelection => Anchor >= 0 && Focus >= 0;

            public void PutCaret(int caret)
            {
                Anchor = Focus = caret;
                _pendingCaret = caret;
                Field.ActivateInputField();
            }

            private void LateUpdate()
            {
                if (Field == null || !Field.isFocused)
                {
                    return;
                }

                if (_pendingCaret >= 0)
                {
                    // Activation selected the whole text; setting the caret collapses that to the right spot.
                    Field.caretPosition = Mathf.Min(_pendingCaret, Field.text.Length);
                    _pendingCaret = -1;
                }

                Anchor = Field.selectionAnchorPosition;
                Focus = Field.selectionFocusPosition;
            }
        }

        /// <summary>The note a save is pending for (null = none) and the notes version at that moment: once the
        /// server's answer bumps the version, the draft is dropped (the pane then shows the stored, possibly
        /// masked, text) and a NEW note is selected. A refused save bumps nothing, so the draft stays.</summary>
        private string _noteSaveKey;
        private int _noteSaveVersion;

        private NetNote[] NotesNow() => Game?.Notes ?? System.Array.Empty<NetNote>();

        private float BuildNotesList()
        {
            var notes = NotesNow();
            float y = 0f;

            // A pending save the server has answered: forget the draft, land on the new note.
            if (_noteSaveKey != null && Game != null && Game.NotesVersion != _noteSaveVersion)
            {
                _noteDraftTitle.Remove(_noteSaveKey);
                _noteDraftBody.Remove(_noteSaveKey);
                if (_noteSaveKey == NewNoteKey && notes.Length > 0)
                {
                    _selected = notes[0].Id; // the server orders newest first
                }

                _noteSaveKey = null;
            }

            bool full = notes.Length >= NoteMaxCount;
            var add = UiKit.AddButton(_listContent, 0, y, 780, 52, L("ui.notes.new"), () =>
            {
                UiKit.ReleaseTextFieldFocus(_detail);
                _notePreview = false;
                _selected = NewNoteKey;
                RebuildList();
                RebuildDetail();
            });
            add.interactable = !full;
            y += 58f;
            if (full)
            {
                var fullHint = UiKit.AddText(_listContent, 8, y, 760, 28, L("ui.notes.full"), 15, UiKit.Warn, TextAnchor.MiddleLeft);
                fullHint.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 34f;
            }

            if (notes.Length == 0)
            {
                if (_selected != NewNoteKey)
                {
                    var empty = UiKit.AddText(_listContent, 8, y, 760, 80, L("ui.notes.empty"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                    empty.horizontalOverflow = HorizontalWrapMode.Wrap;
                    y += 90f;
                }

                return y;
            }

            // Nothing (or a vanished note) selected? Land on the newest so the editor isn't empty on open.
            if (_selected != NewNoteKey && (string.IsNullOrEmpty(_selected) || notes.All(n => n.Id != _selected)))
            {
                _selected = notes[0].Id;
            }

            foreach (var n in notes)
            {
                string id = n.Id;
                var card = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
                {
                    UiKit.ReleaseTextFieldFocus(_detail);
                    _selected = id;
                    RebuildList();
                    RebuildDetail();
                });
                if (_selected == id)
                {
                    card.GetComponent<Image>().color = UiKit.Cyan;
                }

                bool untitled = string.IsNullOrEmpty(n.Title);
                UiKit.AddText(card.transform, 16, 8, 748, 34, untitled ? L("ui.notes.title_ph") : n.Title, 22,
                    untitled ? UiKit.CyanDim : UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddText(card.transform, 16, 44, 748, 28, NoteMarkup.FirstLine(n.Body), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
                y += 86f;
            }

            return y;
        }

        private float BuildNotesDetail()
        {
            var notes = NotesNow();
            bool isNew = _selected == NewNoteKey;
            var note = isNew ? null : notes.FirstOrDefault(n => n.Id == _selected);
            if (!isNew && note == null)
            {
                var hint = UiKit.AddText(_detail, 8, 16, 620, 120, L("ui.notes.empty"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                return 140f;
            }

            string key = isNew ? NewNoteKey : note.Id;
            string title = _noteDraftTitle.TryGetValue(key, out var dt) ? dt : (note?.Title ?? string.Empty);
            string body = _noteDraftBody.TryGetValue(key, out var db) ? db : (note?.Body ?? string.Empty);

            float y = 8f;
            UiKit.AddInput(_detail, 8, y, 624, 44, title, v => _noteDraftTitle[key] = v, L("ui.notes.title_ph"), NoteTitleMax, 20);
            y += 52f;

            // Preview ⇄ Edit toggle. Editing gets the style toolbar (#2577) — bold, eight colours, plain — and a hint a
            // child can follow; the one-line code cheat sheet stays for the preview.
            _noteBodyMemory = null;
            UiKit.AddButton(_detail, 8, y, 170, 38, L(_notePreview ? "ui.notes.edit" : "ui.notes.preview"), () =>
            {
                UiKit.ReleaseTextFieldFocus(_detail);
                _notePreview = !_notePreview;
                RebuildDetail();
            });
            if (_notePreview)
            {
                var cheat = UiKit.AddText(_detail, 190, y, 442, 38, L("ui.notes.markup_hint"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
                cheat.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 46f;
            }
            else
            {
                BuildNoteToolbar(key, 190f, y);
                y += 44f;
                var tip = UiKit.AddText(_detail, 8, y, 624, 36, L("ui.notes.toolbar_hint"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
                tip.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 40f;
            }

            if (_notePreview)
            {
                // Rendered markup as a column of wrapped rich-text blocks at ABSOLUTE rows (no LayoutGroup —
                // a VerticalLayoutGroup overflows wrapped text here), chunked under the uGUI vertex limit.
                var panel = UiKit.AddPanel(_detail, 8, y, 624, NoteBodyHeight, new Color(0.043f, 0.10f, 0.20f, 0.95f));
                float ty = y + 8f;
                string rendered = NoteMarkup.Render(body);
                foreach (string chunk in UiTextChunks.Split(rendered.Length == 0 ? " " : rendered))
                {
                    var t = UiKit.AddText(_detail, 18, ty, 604, 100, chunk, 17, UiKit.TextCol, TextAnchor.UpperLeft);
                    t.horizontalOverflow = HorizontalWrapMode.Wrap;
                    t.verticalOverflow = VerticalWrapMode.Overflow;
                    float th = t.preferredHeight;
                    ((RectTransform)t.transform).sizeDelta = new Vector2(604f, th + 10f);
                    ty += th;
                }

                float panelH = Mathf.Max(NoteBodyHeight, ty + 8f - y);
                ((RectTransform)panel.transform).sizeDelta = new Vector2(624f, panelH);
                y += panelH + 8f;
            }
            else
            {
                var bodyInput = UiKit.AddInput(_detail, 8, y, 624, NoteBodyHeight, body, v => _noteDraftBody[key] = v, L("ui.notes.body_ph"), NoteBodyMax, 17, multiline: true);
                _noteBodyMemory = bodyInput.gameObject.AddComponent<NoteSelectionMemory>();
                _noteBodyMemory.Field = bodyInput;
                y += NoteBodyHeight + 8f;
            }

            UiKit.AddButton(_detail, 8, y, 200, 46, L("ui.notes.save"), () =>
            {
                string t = _noteDraftTitle.TryGetValue(key, out var a) ? a : (note?.Title ?? string.Empty);
                string b = _noteDraftBody.TryGetValue(key, out var bb) ? bb : (note?.Body ?? string.Empty);
                UiKit.ReleaseTextFieldFocus(_detail);
                _noteSaveKey = key;
                _noteSaveVersion = Game?.NotesVersion ?? 0;
                Game?.Network?.SendNoteSet(isNew ? string.Empty : key, t, b);
                if (_feedback != null) _feedback.text = L("ui.notes.saved"); // the server's list answer rebuilds the pane
            });

            if (!isNew)
            {
                var del = UiKit.AddButton(_detail, 432, y, 200, 46, L("ui.notes.delete"), () =>
                {
                    UiKit.ReleaseTextFieldFocus(_detail);
                    _noteDraftTitle.Remove(key);
                    _noteDraftBody.Remove(key);
                    _noteSaveKey = null;
                    _selected = string.Empty; // the list answer picks the newest remaining note
                    Game?.Network?.SendNoteRemove(key);
                });
                del.GetComponent<Image>().color = new Color(0.5f, 0.22f, 0.22f);
            }

            return y + 60f;
        }
    }
}
