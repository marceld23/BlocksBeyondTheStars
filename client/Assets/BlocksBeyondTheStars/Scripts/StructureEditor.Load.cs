// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Station/Village editor's LOAD dialog (#1395; moved out of StructureEditor.cs, #3692): the shipped templates of the
    /// editor's kind as copies, the user's own template files and export bundles — and, from their partial files, the landmarks,
    /// giant ships, civilization buildings and generator entries. Picking one over unsaved work asks first
    /// (<see cref="EditorShell.ConfirmDiscard"/>).
    /// </summary>
    public sealed partial class StructureEditor
    {
        /// <summary>The LOAD dialog (#1395): the shipped templates of this mode (from the loaded content), the templates already
        /// in the user-content folder, then the user's export bundles. Picking one over unsaved work asks first (#3692 — the
        /// shell's question replaced the picker's own "replace your build?").</summary>
        private void OpenLoadPicker()
        {
            _loadPicker?.Close();
            var builtIn = BuiltInSection();
            var user = UserTemplateSection();
            var mine = ExportSection();
            AddLandmarkItems(builtIn, user); // #3492
            AddGiantShipItems(builtIn, user); // #3616
            AddEntryItems(builtIn, user); // #3523
            var sections = new[] { builtIn, user, mine };
            foreach (var section in sections)
            {
                foreach (var item in section.Items)
                {
                    var load = item.Load;
                    item.Load = () => _shell.ConfirmDiscard(load);
                }
            }

            _loadPicker = EditorLoadPicker.Show(Shell, _canvas.transform, sections, 0, () => _loadPicker = null);
        }

        private EditorLoadPicker.Section BuiltInSection()
        {
            var builtIn = new EditorLoadPicker.Section { Title = L("ui.ed.sec_builtin_templates") };
            if (Shell?.Content == null)
            {
                return builtIn;
            }

            var sorted = new List<StructureTemplate>(EditorMode == Mode.Station ? Shell.Content.StationTemplates : Shell.Content.SettlementTemplates);
            sorted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            foreach (var t in sorted)
            {
                if (t.IsSource)
                {
                    continue; // a generator entry has no cells (#3523: AddEntryItems lists it)
                }

                var tpl = t;
                builtIn.Items.Add(new EditorLoadPicker.Item
                {
                    Label = string.IsNullOrEmpty(t.Name) ? t.Key : t.Name,
                    Detail = Detail(t.Tier, t.Width, t.Length, t.Height, t.Cells.Count),
                    Load = () => LoadBuiltIn(tpl),
                });
            }

            return builtIn;
        }

        private EditorLoadPicker.Section UserTemplateSection()
        {
            var user = new EditorLoadPicker.Section { Title = L("ui.ed.sec_user_templates") };
            if (!Directory.Exists(UserTemplatesRoot))
            {
                return user;
            }

            var files = Directory.GetFiles(UserTemplatesRoot, "*.json");
            Array.Sort(files, string.CompareOrdinal);
            foreach (var file in files)
            {
                TemplateJson tpl;
                try
                {
                    tpl = JsonUtility.FromJson<TemplateJson>(File.ReadAllText(file));
                }
                catch (Exception)
                {
                    continue; // one unreadable file must not hide the rest (the server skips it the same way)
                }

                if (tpl == null || tpl.cells == null || tpl.cells.Count == 0)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(tpl.key))
                {
                    tpl.key = Path.GetFileNameWithoutExtension(file);
                }

                var t = tpl;
                user.Items.Add(new EditorLoadPicker.Item
                {
                    Label = string.IsNullOrEmpty(t.name) ? t.key : t.name,
                    Detail = Detail(t.tier, t.width, t.length, t.height, t.cells.Count),
                    Load = () => ApplyTemplate(t.key, t.name, t.tier, t.pack, t.weight, t.planetTypes, t.cells, copy: false, role: t.role, kit: t.kit,
                        function: t.function, style: t.style, underground: t.underground, protection: t.protection),
                });
            }

            return user;
        }

        private EditorLoadPicker.Section ExportSection()
        {
            var mine = new EditorLoadPicker.Section { Title = L("ui.ed.sec_exports") };
            if (!Directory.Exists(ExportsRoot))
            {
                return mine;
            }

            var dirs = Directory.GetDirectories(ExportsRoot);
            Array.Sort(dirs, string.CompareOrdinal);
            foreach (var d in dirs)
            {
                if (File.Exists(Path.Combine(d, "layout.json")))
                {
                    string k = Path.GetFileName(d);
                    mine.Items.Add(new EditorLoadPicker.Item { Label = k, Load = () => LoadDesign(k) });
                }
            }

            return mine;
        }
    }
}
