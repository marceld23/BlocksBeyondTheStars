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
    /// What happens when it is stamped, and honest tools (#3523). The design's <see cref="StampTarget"/> (its editor, <i>Use as</i>,
    /// the underground toggle, a city role, an entry's pool) decides
    /// <list type="bullet">
    /// <item>the <b>Stamping…</b> panel: the stamp-time steps that apply — seat and foundation, material tokens, furniture,
    /// rotation, the ruin pass, chests, sounds and dens, residents, protection, keep-clear — with this design's values
    /// (<see cref="StampStepsDescriber"/>, Unity-free);</item>
    /// <item>the <b>protection</b> picker (terrain generation 39) of every whole template and generator entry: auto (the rule of
    /// its kind, said in a line under it), protected or open — written as <c>protection</c> by Save, Submit and the user file;</item>
    /// <item>the <b>palette</b>: a tool or marker the target ignores is hidden (<see cref="StructureStampRules.ShowsInPalette"/>);
    /// the design keeps what it has, loading never drops a cell;</item>
    /// <item><b>Save</b>: a warning in the status line for what the target will not read, and a refusal for material tokens a
    /// station module cannot resolve.</item>
    /// </list>
    /// </summary>
    public sealed partial class StructureEditor
    {
        private bool _stampingOpen;

        /// <summary>The target as the form stood at the last UI build (the ghost and the tools read it every frame).</summary>
        private StampTarget _target;

        /// <summary>The palette as shown for <see cref="_target"/>; <see cref="_palette"/> stays whole (loads look cells up there).</summary>
        private EditorPaletteKit.Entry[] _shown = Array.Empty<EditorPaletteKit.Entry>();

        /// <summary>The design's target from the form.</summary>
        private StampTarget CurrentTarget()
            => StructureStampRules.TargetFor(EditorMode == Mode.Station, _moduleMode, _landmarkMode, _entryMode, _underground, IsCityModule(), _entryPool);

        /// <summary>A town module that goes into a district of the walled city: a city role, the metropolis tier or a city kit.</summary>
        private bool IsCityModule()
        {
            if (!_moduleMode || EditorMode != Mode.Settlement)
            {
                return false;
            }

            if (StructureRoles.IsCityRole(_role) || _tiers[_tier] == StructureRoles.MetropolisTier)
            {
                return true;
            }

            return !string.IsNullOrEmpty(_kit) && FindKit(Slug(_kit))?.KindOrDefault == StructureKit.KindCity;
        }

        /// <summary>The form's fields of a <see cref="StampDesign"/> (no cells).</summary>
        private StampDesign StampFormFields(StampDesign d)
        {
            d.Target = CurrentTarget();
            d.Tier = _moduleMode && StructureRoles.IsCityRole(_role) ? StructureRoles.MetropolisTier : _tiers[_tier];
            d.Alien = _moduleMode && _style == StructureTemplate.StyleAlien;
            d.Surface = PreviewSurface();
            d.Function = _function ?? string.Empty;
            d.Ruin = _landmarkMode && _lmRuin;
            d.RuinProfile = _landmarkMode ? _lmProfile : string.Empty;
            d.RuinStrength = _landmarkMode ? _lmStrength : 0.0;
            d.GroundRow = _groundRow;
            d.Protection = _protection;
            d.Seat = _landmarkMode ? _lmSeat : _entryMode ? _entryExtras?.seat ?? string.Empty : string.Empty;
            if (_landmarkMode)
            {
                d.RuinZones.AddRange(_zones);
            }

            d.Source = CurrentSource().Key;
            d.Weight = _weight;
            return d;
        }

        // ---------------------------------------------------------------- protection (terrain generation 39)

        /// <summary>The design's protection: <see cref="LandmarkRules.ProtectionAuto"/> (the rule of its kind),
        /// <see cref="LandmarkRules.ProtectionProtected"/> or <see cref="LandmarkRules.ProtectionOpen"/> — offered for every whole
        /// template and generator entry (<see cref="StructureStampRules.TakesProtection"/>); a kit module follows its structure.</summary>
        private string _protection = LandmarkRules.ProtectionAuto;

        /// <summary>The protection row (auto → protected → open) and the line saying what it means for this target.</summary>
        private float BuildProtectionRows(Transform p, float y)
        {
            RowLabel(p, y, L("ui.ed.protection"));
            ValueLabel(p, y, L("ui.ed.protection." + LandmarkRules.ProtectionOf(_protection)));
            UiKit.AddFitButton(p, ArrowX, y + 3f, ArrowW, RowH, "→", () =>
            {
                var modes = LandmarkRules.ProtectionModes;
                _protection = modes[(Array.IndexOf(modes, LandmarkRules.ProtectionOf(_protection)) + 1) % modes.Length];
                RebuildUi();
            });
            y += ValueRowStep;
            UiKit.AddMeasuredText(p, 16f, y, 348f, L(StructureStampRules.ProtectionKey(StampFormFields(new StampDesign()))), 13, UiKit.CyanDim,
                out float h);
            return y + h + 8f;
        }

        /// <summary>The protection word a save writes: empty for auto (the template behaves as its kind always did), else
        /// <c>protected</c> / <c>open</c> — and empty for a kit module, which has none of its own.</summary>
        private string ProtectionWord()
        {
            string word = LandmarkRules.ProtectionOf(_protection);
            return word == LandmarkRules.ProtectionAuto || !StructureStampRules.TakesProtection(CurrentTarget()) ? string.Empty : word;
        }

        /// <summary>The design (never a preview) as the describer and the save checks read it.</summary>
        private StampDesign CurrentStampDesign()
        {
            var d = StampFormFields(new StampDesign());
            foreach (var c in DesignCells())
            {
                if (c.kind == "marker")
                {
                    d.Markers.Add(new StampMarker(c.id, c.config, c.x, c.y, c.z));
                    continue;
                }

                if (c.kind == LandmarkRules.CavityKind)
                {
                    d.CavityCells++;
                    continue;
                }

                d.KeepCells += c.keep ? 1 : 0;
                d.PortCells += string.IsNullOrEmpty(c.port) ? 0 : 1;
                if (MaterialTokens.IsToken(c.id))
                {
                    d.TokenCells[c.id] = (d.TokenCells.TryGetValue(c.id, out int n) ? n : 0) + 1;
                }
            }

            return d;
        }

        // ---------------------------------------------------------------- the panel

        /// <summary>The collapsible "Stamping…" panel: a toggle, then one paragraph per step.</summary>
        private float BuildStampingRows(Transform p, float y)
        {
            UiKit.AddFitButton(p, 16f, y, 348f, 34f, (_stampingOpen ? "▾ " : "▸ ") + L("ui.struct.stamping"), () =>
            {
                _stampingOpen = !_stampingOpen;
                RebuildUi();
            });
            y += 40f;
            if (!_stampingOpen || Shell?.Content == null)
            {
                return y;
            }

            foreach (var step in StampStepsDescriber.Describe(CurrentStampDesign(), Shell.Content))
            {
                var title = UiKit.AddText(p, 16f, y, 348f, 20f, L(step.Title), 14, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.FitLabelSingleLine(title, 348f, UiKit.EditorMinFontSize, 14);
                y += 22f;
                UiKit.AddMeasuredText(p, 16f, y, 348f, StepText(step), 13, UiKit.TextCol, out float h);
                y += h + 2f;
                if (step is StampListStep list)
                {
                    y = StampListRows(p, y, list);
                }

                y += 6f;
            }

            return y + 4f;
        }

        private float StampListRows(Transform p, float y, StampListStep list)
        {
            foreach (var line in list.Lines)
            {
                UiKit.AddMeasuredText(p, 28f, y, 336f, "• " + StepText(line), 13, UiKit.TextCol, out float h);
                y += h + 2f;
            }

            if (list.More > 0)
            {
                UiKit.AddMeasuredText(p, 28f, y, 336f, string.Format(L("ui.stamp.chest.more"), list.More), 13, UiKit.TextCol, out float h);
                y += h + 2f;
            }

            return y;
        }

        /// <summary>A step's text with its arguments (a key argument translated).</summary>
        private string StepText(StampStep step)
        {
            var args = new object[step.Args.Count];
            for (int i = 0; i < args.Length; i++)
            {
                var a = step.Args[i];
                args[i] = a.IsKey ? L(a.Text) : a.Text;
            }

            try
            {
                return string.Format(L(step.Text), args);
            }
            catch (FormatException)
            {
                return L(step.Text); // a translation with a broken placeholder still reads
            }
        }

        // ---------------------------------------------------------------- the palette

        /// <summary>The material tokens (#1890) — resolved per planet in a settlement, with the hull in a whole station template.</summary>
        private EditorPaletteKit.Entry[] TokenEntries() => new[]
        {
            T(MaterialTokens.Wall, new Color(0.62f, 0.55f, 0.42f)),
            T(MaterialTokens.Accent, new Color(0.55f, 0.85f, 0.95f)),
            T(MaterialTokens.Roof, new Color(0.55f, 0.4f, 0.3f)),
            T(MaterialTokens.Floor, new Color(0.5f, 0.5f, 0.48f)),
            T(MaterialTokens.Path, new Color(0.7f, 0.65f, 0.5f)),
        };

        /// <summary>The palette entries the target reads, in palette order.</summary>
        private EditorPaletteKit.Entry[] ShownPalette()
        {
            var list = new List<EditorPaletteKit.Entry>(_palette.Length);
            foreach (var e in _palette)
            {
                if (StructureStampRules.ShowsInPalette(_target, e.Kind, e.Id))
                {
                    list.Add(e);
                }
            }

            return list.ToArray();
        }

        /// <summary>Recomputes the target and the shown palette; a selection the target hides moves to the first shown entry.
        /// Returns the selection's index in the shown palette.</summary>
        private int RefreshShownPalette()
        {
            _target = CurrentTarget();
            _shown = ShownPalette();
            int shown = _selected >= 0 && _selected < _palette.Length ? IndexIn(_shown, _palette[_selected]) : -1;
            if (shown < 0 && _shown.Length > 0)
            {
                shown = 0;
                _selected = IndexIn(_palette, _shown[0]);
            }

            return Math.Max(0, shown);
        }

        /// <summary>A shown row picked: the selection is kept as an index into the whole palette.</summary>
        private void SelectShown(int i)
        {
            if (i >= 0 && i < _shown.Length)
            {
                _selected = IndexIn(_palette, _shown[i]);
            }
        }

        private static int IndexIn(EditorPaletteKit.Entry[] entries, EditorPaletteKit.Entry e)
            => Array.FindIndex(entries, x => x.Id == e.Id && x.Kind == e.Kind);

        // ---------------------------------------------------------------- save

        /// <summary>The first thing the target will not read, as a status line to append ("" when there is none).</summary>
        private string SaveWarnings()
        {
            var warnings = StructureStampRules.Warnings(CurrentStampDesign());
            if (warnings.Count == 0)
            {
                return string.Empty;
            }

            string text = "\n⚠ " + string.Format(L(warnings[0].Key), warnings[0].Count);
            return warnings.Count > 1 ? text + " " + string.Format(L("ui.ed.warn.more"), warnings.Count - 1) : text;
        }

        /// <summary>Refuses a save the target cannot stamp faithfully (material tokens in a station module); true when refused.</summary>
        private bool RefuseUnreadTokens()
        {
            if (StructureStampRules.Refusal(CurrentStampDesign()) is not { } refusal)
            {
                return false;
            }

            SetStatus(string.Format(L(refusal.Key), refusal.Count));
            return true;
        }
    }
}
