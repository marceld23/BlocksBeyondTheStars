// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The item &amp; recipe editor's check (#3694): the mock's "Check" card lists what is wrong (red), what is fine (green)
    /// and what to look at (amber) after every change, and the first error also stands in the status bar with what to do
    /// ("the key contains a space — please use only a–z, 0–9 and _"). Save waits until there is no error. Before, the
    /// editor rewrote a bad key silently and found unknown item keys only when Save was pressed — in green.
    /// </summary>
    public sealed partial class ContentEditor
    {
        /// <summary>Lines the check card has room for.</summary>
        internal const int MaxCheckLines = 8;

        /// <summary>The check card's text: its size, its left inset (after the mark) and its width.</summary>
        internal const int CheckSize = 18;

        internal const float CheckTextX = 26f, CheckTextW = CheckW - 2f * Pad - CheckTextX;

        private readonly List<(Image Mark, Image Cross, Text Text)> _checkUi = new List<(Image, Image, Text)>();
        private string _shownCheck;     // the status text a check put there — a fixed problem takes it away again
        private string _iconChecked;    // the key the icon lookup ran for last (typing must not load per letter twice)
        private bool _iconFound;

        /// <summary>The check lines on show — for tests.</summary>
        public IReadOnlyList<(EditorStatusSeverity Severity, string Text)> ChecksForTests => Checks();

        private void BuildCheckCard()
        {
            var card = _shell.AddCard(CheckX, CardY, CheckW, CardH);
            UiKit.AddKicker(card, Pad, 20f, CheckW - 2f * Pad, L("ui.content.check"));
            for (int i = 0; i < MaxCheckLines; i++)
            {
                var mark = UiKit.AddImage(card, Pad, 0f, 14f, 14f, UiKit.DiscSprite, Color.white);
                var cross = UiKit.AddIcon(card, Pad - 5f, 0f, 24f, "btn_close");
                var text = UiKit.AddText(card, Pad + CheckTextX, 0f, CheckTextW, 24f, string.Empty, CheckSize, UiKit.TextCol,
                    TextAnchor.UpperLeft);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                _checkUi.Add((mark, cross, text));
            }

            if (EditorShell.DesktopFeatures)
            {
                UiKit.AddLinkButton(card, Pad, CardH - 64f, CheckW - 2f * Pad, 30f, L("ui.photos.open_folder"), OpenExportFolder);
            }
        }

        /// <summary>Checks the definition, fills the card and puts the first error in the status bar.</summary>
        private void RunChecks()
        {
            var lines = Checks();
            ShowCheckLines(lines);
            string error = null;
            foreach (var line in lines)
            {
                if (line.Severity == EditorStatusSeverity.Error)
                {
                    error = line.Text;
                    break;
                }
            }

            ShowCheckStatus(error);
        }

        private string FirstError()
        {
            foreach (var line in Checks())
            {
                if (line.Severity == EditorStatusSeverity.Error)
                {
                    return line.Text;
                }
            }

            return null;
        }

        /// <summary>Errors first, then "all set" when there is none, then the warnings.</summary>
        private List<(EditorStatusSeverity Severity, string Text)> Checks()
        {
            var lines = new List<(EditorStatusSeverity, string)>();
            if (_b == null)
            {
                return lines;
            }

            string keyProblem = KeyProblemText(L, _b.key);
            if (keyProblem != null)
            {
                lines.Add((EditorStatusSeverity.Error, keyProblem));
            }

            if (string.IsNullOrWhiteSpace(_b.name))
            {
                lines.Add((EditorStatusSeverity.Error, L("ui.ed.check.need_name")));
            }

            // Item/block keys are typed by hand. An unknown one only surfaces AFTER the merge, as a content
            // validation failure that stops the game from starting — so check them against the live registry
            // here, while the author can still fix the typo (#517).
            var unknown = UnknownKeys();
            if (unknown.Count > 0)
            {
                lines.Add((EditorStatusSeverity.Error, $"{L("ui.content.unknown_keys")} {string.Join(", ", unknown)}"));
            }

            if (lines.Count == 0)
            {
                lines.Add((EditorStatusSeverity.Success, L("ui.ed.check.all_set")));
            }

            AddWarnings(lines, keyProblem == null ? ContentKeyRules.Clean(_b.key) : null);
            return lines;
        }

        /// <summary>The amber lines: the key exists already (the merge updates that item), no ingredient, no icon.</summary>
        private void AddWarnings(List<(EditorStatusSeverity, string)> lines, string validKey)
        {
            var content = Shell != null ? Shell.Content : null;
            if (validKey != null && content != null && content.GetItem(validKey) != null)
            {
                lines.Add((EditorStatusSeverity.Warning, L("ui.content.check.key_taken").Replace("{key}", validKey)));
            }

            if (CleanAmounts(_b.inputs).Count == 0)
            {
                lines.Add((EditorStatusSeverity.Warning, L("ui.content.check.no_inputs")));
            }

            // merge_recipe.py: a block-placing item borrows its block's tile; anything else shows a placeholder until
            // icons/item_<key>.png exists.
            if (validKey != null && string.IsNullOrWhiteSpace(_b.placesBlock) && !HasIcon(validKey))
            {
                lines.Add((EditorStatusSeverity.Warning, L("ui.content.check.no_icon")));
            }
        }

        private bool HasIcon(string key)
        {
            if (key != _iconChecked)
            {
                _iconChecked = key;
                _iconFound = Resources.Load<Texture2D>("icons/item_" + key) != null;
            }

            return _iconFound;
        }

        /// <summary>Every typed item/block key this design references that the loaded content does not know.
        /// Empty when no content is loaded (nothing to check against) or everything resolves.</summary>
        private List<string> UnknownKeys()
        {
            var unknown = new List<string>();
            var content = Shell != null ? Shell.Content : null;
            if (content == null)
            {
                return unknown;
            }

            void CheckItem(string raw)
            {
                string k = (raw ?? string.Empty).Trim();
                if (k.Length > 0 && content.GetItem(k) == null && !unknown.Contains(k))
                {
                    unknown.Add(k);
                }
            }

            foreach (var a in _b.inputs)
            {
                CheckItem(a?.item);
            }

            if (_b.hasBlueprint)
            {
                foreach (var a in _b.unlockCost)
                {
                    CheckItem(a?.item);
                }
            }

            string block = (_b.placesBlock ?? string.Empty).Trim();
            if (block.Length > 0 && content.GetBlock(block) == null && !unknown.Contains(block))
            {
                unknown.Add(block);
            }

            return unknown;
        }

        /// <summary>The words for a key problem — null for a good key. Shared with the material editor.</summary>
        internal static string KeyProblemText(Func<string, string> localize, string rawKey)
        {
            string key = ContentKeyRules.Clean(rawKey);
            switch (ContentKeyRules.Check(key))
            {
                case ContentKeyProblem.Empty:
                    return localize("ui.ed.check.need_key");
                case ContentKeyProblem.Space:
                    return localize("ui.ed.check.key_space").Replace("{key}", key);
                case ContentKeyProblem.BadCharacter:
                    return localize("ui.ed.check.key_chars").Replace("{key}", key);
                default:
                    return null;
            }
        }

        // ---- showing them ------------------------------------------------------------------------------------------------

        private void ShowCheckLines(List<(EditorStatusSeverity Severity, string Text)> lines)
        {
            float y = 60f;
            for (int i = 0; i < _checkUi.Count; i++)
            {
                var (mark, cross, text) = _checkUi[i];
                bool on = i < lines.Count;
                bool error = on && lines[i].Severity == EditorStatusSeverity.Error;
                text.enabled = on;
                mark.enabled = on && (!error || cross == null);
                if (cross != null)
                {
                    cross.enabled = error;
                    cross.color = UiKit.DangerCol;
                }

                if (!on)
                {
                    continue;
                }

                var color = EditorShell.SeverityColor(lines[i].Severity);
                text.text = lines[i].Text;
                text.color = color;
                mark.color = color;
                float h = Mathf.Max(24f, Mathf.Ceil(UiKit.MeasureHeight(text.text, CheckTextW, CheckSize, FontStyle.Normal, false)));
                UiKit.Place(text.gameObject, Pad + CheckTextX, y, CheckTextW, h);
                UiKit.Place(mark.gameObject, Pad, y + 6f, 14f, 14f);
                if (cross != null)
                {
                    UiKit.Place(cross.gameObject, Pad - 5f, y + 1f, 24f, 24f);
                }

                y += h + 14f;
            }
        }

        /// <summary>The first error stands in the status bar; once it is fixed, the line it left there goes again (a message
        /// of something else — "Exported" — stays).</summary>
        private void ShowCheckStatus(string error)
        {
            bool ours = _shownCheck != null && _shell.StatusText.text == _shownCheck;
            if (error != null)
            {
                _shell.SetStatus(error, EditorStatusSeverity.Error);
                _shownCheck = _shell.StatusText.text;
            }
            else if (ours)
            {
                _shell.ClearStatus();
                _shownCheck = null;
            }
        }

        /// <summary>Opens this item's export folder (or the exports folder before the first export) — the status bar names
        /// no paths any more. Desktop only.</summary>
        private void OpenExportFolder()
        {
            string key = ContentKeyRules.Clean(_b.key);
            string dir = ContentKeyRules.Check(key) == ContentKeyProblem.None && Directory.Exists(ExportDir(key))
                ? ExportDir(key)
                : Path.Combine(ExportRoot ?? AppPaths.Root, "content_exports");
            if (!DevEditorForm.OpenFolder(dir))
            {
                _shell.SetStatus(L("ui.ed.open_folder_failed"), EditorStatusSeverity.Error);
            }
        }
    }
}
