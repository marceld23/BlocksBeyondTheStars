// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The developer part of the Station/Village editor behind the <b>Advanced</b> switch (#3692, maintainer's decision
    /// 2026-11-15): a child who wants to build a village no longer has to get past generator entries, ports, sources, stamping and
    /// zones. Off (the default) the right card shows only the building parts; on, the developer parts follow as collapsible
    /// sections — generator entry (registration: key, use as, pack, weight, planet types, protection), ports (a kit module), sources
    /// &amp; kits (generate, previews), landmark, giant ship and civilization (the village editor), stamping and the brush colours.
    /// The switch is remembered across sessions (<see cref="AdvancedPrefKey"/>, PlayerPrefs — <c>ClientSettings</c> belongs to the
    /// shell). A developer design (anything but a whole structure) always shows its parts: the switch stays on and greyed.
    /// </summary>
    public sealed partial class StructureEditor
    {
        /// <summary>Where the Advanced switch is remembered.</summary>
        internal const string AdvancedPrefKey = "bbs.structure_editor.advanced";

        /// <summary>Reads the remembered switch; tests swap it (PlayerPrefs is the real one).</summary>
        internal static Func<bool> LoadAdvancedPref = () => PlayerPrefs.GetInt(AdvancedPrefKey, 0) == 1;

        /// <summary>Remembers the switch; tests swap it.</summary>
        internal static Action<bool> StoreAdvancedPref = on =>
        {
            PlayerPrefs.SetInt(AdvancedPrefKey, on ? 1 : 0);
            PlayerPrefs.Save();
        };

        private bool _advanced;
        private readonly HashSet<string> _openSections = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Whether the developer sections show: the remembered switch, or a design whose "use as" is a developer kind.</summary>
        internal bool ShowAdvanced => _advanced || IsDeveloperDesign;

        /// <summary>A kit module, a landmark, a giant ship, a generator entry or a civilization building — anything but a whole
        /// structure, whose form rows only the developer sections have.</summary>
        internal bool IsDeveloperDesign => _moduleMode || _landmarkMode || _giantShipMode || _entryMode || _civ.IsSet;

        /// <summary>The Advanced switch: remembered, then the card follows.</summary>
        internal void SetAdvanced(bool on)
        {
            _advanced = on;
            StoreAdvancedPref?.Invoke(on);
            RebuildUi();
        }

        /// <summary>Unattended screenshots (-captureEditors): the developer view for one shot ("Dorf · Erweitert" in the approved
        /// mock), the generator entry open — nothing is remembered.</summary>
        internal void CaptureShowAdvanced(bool on)
        {
            _advanced = on;
            if (on)
            {
                _openSections.Add("entry");
            }

            RebuildUi();
        }

        /// <summary>The section the current "use as" fills — opened when the mode changes or a developer design is loaded.</summary>
        private string ActiveSection
            => _entryMode ? "entry" : _moduleMode ? "ports" : CivilizationMode ? "civ" : _landmarkMode ? "landmark" : _giantShipMode ? "giant_ship" : null;

        /// <summary>Opens the active mode's section (after a load or a change of "use as").</summary>
        private void OpenActiveSection()
        {
            if (ActiveSection is { } id)
            {
                _openSections.Add(id);
            }
        }

        /// <summary>The amber "Advanced — for developers" line and the sections. Returns the body's height.</summary>
        private float BuildAdvancedRows(Transform p, float y)
        {
            y += 8f;
            var head = UiKit.AddText(p, 16f, y, 348f, 26f, L("ui.struct.advanced_title"), 16, UiKit.Warn, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(head, 348f, UiKit.EditorMinFontSize, 16);
            y += 32f;
            y = Section(p, y, "entry", "ui.struct.sec.entry", UseLabel(), BuildEntrySection);
            y = Section(p, y, "ports", "ui.struct.sec.ports", PortCellCount().ToString(), BuildPortsSection);
            if (!_entryMode)
            {
                y = Section(p, y, "sources", "ui.struct.sec.sources", L(CurrentSource().LabelKey), BuildSourcesSection);
            }

            if (EditorMode == Mode.Settlement)
            {
                y = Section(p, y, "landmark", "ui.ed.landmark.title", OnOff(_landmarkMode && !CivilizationMode), BuildLandmarkSection);
                y = Section(p, y, "giant_ship", "ui.ed.giantship.title", OnOff(_giantShipMode), BuildGiantShipSection);
                y = Section(p, y, "civ", "ui.struct.sec.civ", CivilizationName(), BuildCivilizationSection);
            }

            y = BuildStampingRows(p, y + 4f); // #3523: what happens when it is stamped (its own fold)
            return _entryMode ? y : Section(p, y, "brush", "ui.struct.brush", HexOf(_brushTint), BuildBrushColourRows);
        }

        /// <summary>A collapsible section: a header button "▸ Title · summary", the rows below it while open.</summary>
        private float Section(Transform p, float y, string id, string titleKey, string summary, Func<Transform, float, float> build)
        {
            bool open = _openSections.Contains(id);
            string label = (open ? "▾ " : "▸ ") + L(titleKey) + (string.IsNullOrEmpty(summary) ? string.Empty : "  ·  " + summary);
            var header = UiKit.AddFitButton(p, 16f, y, 348f, KidRowH, label, () =>
            {
                if (!_openSections.Remove(id))
                {
                    _openSections.Add(id);
                }

                RebuildUi();
            });
            header.name = "Section_" + id;
            y += KidRowH + 6f;
            return open ? build(p, y) + 10f : y;
        }

        private string OnOff(bool on) => L(on ? "ui.struct.sec.on" : "ui.struct.sec.off");

        private string CivilizationName()
        {
            if (!_civ.IsSet)
            {
                return L("ui.struct.sec.none");
            }

            var civ = Shell?.Content?.GetCivilization(_civ.Civilization);
            return civ != null ? L(civ.NameKey) : _civ.Civilization;
        }

        /// <summary>A line that says how a section's rows come up (a section whose mode the design is not in).</summary>
        private float SectionHint(Transform p, float y, string key)
        {
            UiKit.AddMeasuredText(p, 16f, y, 348f, L(key), 16, UiKit.CyanDim, out float h);
            return y + h + 6f;
        }

        // ---------------------------------------------------------------- the sections

        /// <summary>The registration: key, use as, pack, weight, planet types, protection — and a generator entry's own rows.</summary>
        private float BuildEntrySection(Transform p, float y)
        {
            y = BuildIdentityRows(p, y);
            if (_entryMode)
            {
                y = BuildEntryRows(p, y); // #3523: a generator entry
            }

            y = BuildPackRows(p, y);
            return StructureStampRules.TakesProtection(_target) ? BuildProtectionRows(p, y) : y; // generation 39
        }

        /// <summary>A kit module's rows (kit, function, door or style, seal, assemble), else how to get there.</summary>
        private float BuildPortsSection(Transform p, float y)
            => _moduleMode ? BuildModuleRows(p, y) : SectionHint(p, y, "ui.struct.sec.ports_hint");

        /// <summary>Generate from a source and the previews (quarter turn, materials, the ruin or the wreck).</summary>
        private float BuildSourcesSection(Transform p, float y)
        {
            y = BuildPreviewRows(p, y); // #3523: quarter turn, materials, ruin
            return BuildGenerateRows(p, y);
        }

        private float BuildLandmarkSection(Transform p, float y)
            => _landmarkMode && !CivilizationMode ? BuildLandmarkRows(p, y) : SectionHint(p, y, "ui.struct.sec.landmark_hint"); // #3492

        private float BuildGiantShipSection(Transform p, float y)
            => _giantShipMode ? BuildGiantShipRows(p, y) : SectionHint(p, y, "ui.struct.sec.giant_ship_hint"); // #3616

        private float BuildCivilizationSection(Transform p, float y)
            => CivilizationMode ? BuildLandmarkRows(p, y) : SectionHint(p, y, "ui.struct.sec.civ_hint"); // #3646: the landmark form

        /// <summary>How many cells carry a docking port (the ports section's summary).</summary>
        private int PortCellCount()
        {
            int n = 0;
            foreach (var d in _design.Values)
            {
                n += string.IsNullOrEmpty(d.Port) ? 0 : 1;
            }

            return n;
        }
    }
}
