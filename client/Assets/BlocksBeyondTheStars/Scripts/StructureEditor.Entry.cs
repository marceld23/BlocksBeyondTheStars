// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration.Sources;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Generator entries (#3518, #3523): <i>Use as → Generator entry</i> in both editors. An entry is a template without cells —
    /// a structure source, its parameters, weight, pack and planet types — that joins a pool next to the authored templates; every
    /// world builds it anew with its own seed. The town editor's entries go to the settlement, underground or landmark pool, the
    /// station editor's to the station pool (the pool decides which sources it takes, <see cref="StructureStampRules.PoolTakes(StampTarget, IStructureSource)"/>).
    /// <list type="bullet">
    /// <item><b>The room</b> shows the source's build with the form's seed — read-only, like a preview; the design comes back when
    /// <i>Use as</i> moves on. A changed parameter does not rebuild it (nor move the camera): <i>Refresh preview</i> does.</item>
    /// <item><b>Save</b> checks the entry the way the server does at start (<see cref="SourceTemplates.Problem"/>) and writes the
    /// template to the user-content folder the loader reads for that pool — <c>settlement_templates</c> (an underground entry
    /// flagged), <c>station_templates</c> or <c>landmark_templates</c> — plus <c>entry.json</c> in the export folder. A design
    /// already there under the same key is never overwritten (<see cref="UserTemplateFiles"/>), and the fields the form does not
    /// show (pin-only, the ruin settings, the ground row, …) are written back as the loaded file had them. A landmark entry
    /// offers the landmark conditions (needs air, inhabited worlds only).</item>
    /// <item><b>LOAD</b> lists the shipped entries (as copies) and the user's own, marked ⚙. An entry whose source is unknown or
    /// does not fit its pool says so and is not saved until a source is picked.</item>
    /// </list>
    /// </summary>
    public sealed partial class StructureEditor
    {
        /// <summary>A generator entry's file (matches the server's StructureTemplate JSON: no cells). The fields after
        /// <c>sourceParams</c> have no row in the entry form; they are carried from the loaded file to the saved one.</summary>
        [Serializable] private sealed class EntryJson
        {
            public string key, name, tier, kind, pack = "default";
            public int weight = 1;
            public List<string> planetTypes = new();
            public bool underground;
            public bool landmark;
            public string source = string.Empty, sourceParams = string.Empty;
            public bool pinOnly, legacyPool, @protected;
            public string ruin = string.Empty, ruinProfile = string.Empty, nameKey = string.Empty;
            public double ruinStrength;
            public List<string> requires = new();
            public int groundRow;
        }

        private bool _entryMode;
        private string _entryPool = StructureStampRules.PoolSettlement;

        /// <summary>The loaded entry file, whose fields without a form row are written back on save (null: a new entry).</summary>
        private EntryJson _entryExtras;

        /// <summary>True while a loaded entry's own source is unknown or does not fit its pool: the form shows the pool's default,
        /// and a save is refused until a source is picked.</summary>
        private bool _entrySourceInvalid;

        private string[] EntryPools => EditorMode == Mode.Station ? StructureStampRules.StationPools : StructureStampRules.TownPools;

        // ---------------------------------------------------------------- the form

        /// <summary>The pool (town editor), the source with its parameters, the preview's seed, the surface — and a landmark
        /// entry's conditions.</summary>
        private float BuildEntryRows(Transform p, float y)
        {
            SectionTitle(p, y, L("ui.struct.entry_title"));
            y += 28f;
            if (EntryPools.Length > 1)
            {
                RowLabel(p, y, L("ui.struct.entry_pool"));
                ValueLabel(p, y, L("ui.struct.pool." + _entryPool));
                UiKit.AddFitButton(p, ArrowX, y + 3f, ArrowW, RowH, "→", CyclePool);
                y += ValueRowStep;
            }

            y = BuildSourceRows(p, y, CurrentSource());
            y = BuildSeedRow(p, y);
            UiKit.AddFitButton(p, 16f, y, 348f, 34f, L("ui.struct.entry_preview"), () =>
            {
                ShowEntryPreview();
                RebuildUi();
            });
            y += 40f;
            y = BuildSurfaceRow(p, y);
            if (_entryPool == StructureStampRules.PoolLandmark)
            {
                // The landmark stamper reads these from the entry like from an authored landmark.
                y = LandmarkToggle(p, y, "ui.ed.landmark.atmosphere", _lmAtmosphere, () => _lmAtmosphere = !_lmAtmosphere);
                y = LandmarkToggle(p, y, "ui.ed.landmark.inhabited", _lmInhabited, () => _lmInhabited = !_lmInhabited);
            }

            UiKit.AddMeasuredText(p, 16f, y, 348f, L("ui.struct.entry_hint"), 13, UiKit.TextCol, out float h);
            return y + h + 8f;
        }

        private void CyclePool()
        {
            var pools = EntryPools;
            int i = Array.IndexOf(pools, _entryPool);
            _entryPool = pools[(i + 1) % pools.Length];
            if (_entrySourceInvalid && StructureSources.Get(_sourceKey) is { } own && SourceFits(own))
            {
                _entrySourceInvalid = false; // the loaded entry's own source fits this pool
            }

            ShowEntryPreview(); // a source the new pool does not take falls back to the pool's default (CurrentSource)
            RebuildUi();
        }

        /// <summary>Shows the entry's source, built with the form's seed as it stands in a world, in place of the design. The
        /// camera frames the build when the entry preview first shows; a refresh keeps it where the author put it.</summary>
        private void ShowEntryPreview()
        {
            if (!_entryMode || Shell?.Content == null)
            {
                return;
            }

            var source = CurrentSource();
            var built = BuildSource(source, asInWorld: true);
            if (built == null)
            {
                SetStatus(L("ui.ed.load_failed_hint"));
                return;
            }

            bool frame = !(_previewOn && _previewKind == PreviewKind.Entry);
            ShowPreview(PreviewKind.Entry, BuiltCells(built));
            if (frame)
            {
                EditorSceneKit.Frame(_cam.transform, ref _pitch, _yaw, _design.Keys, MaxW, MaxL);
            }

            SetStatus(string.Format(L("ui.ed.entry.preview"), L(source.LabelKey), _seed, _design.Count)
                + (_entrySourceInvalid ? "\n⚠ " + L("ui.ed.entry.pick_source") : string.Empty));
        }

        // ---------------------------------------------------------------- save

        /// <summary>The saves that are no whole template or module (true when one ran): a generator entry (no cells — its
        /// source builds it), a landmark (one compact file of its own, #3492) — and a refusal while a preview shows, or when the
        /// key is held by a file of the other kind (a design over an entry, an entry over a design).</summary>
        private bool ExportElsewhere(string key)
        {
            if (KeyHeldByOtherKind(key))
            {
                return true;
            }

            if (_entryMode)
            {
                ExportEntry(key);
                return true;
            }

            if (_previewOn)
            {
                SetStatus(L(PreviewLockedKey()));
                return true;
            }

            if (_landmarkMode)
            {
                ExportLandmark(key);
                return true;
            }

            return false;
        }

        /// <summary>
        /// #3518: designs and entries share the user-content folders (<c>&lt;kind&gt;_templates</c>, <c>landmark_templates</c>),
        /// so a save under the key of a file of the other kind would overwrite it. Refuses that save (status line: pick another
        /// key) and returns true; an absent or unreadable file never blocks.
        /// </summary>
        private bool KeyHeldByOtherKind(string key)
        {
            bool landmarkFolder = _entryMode ? CurrentTarget() == StampTarget.LandmarkEntry : _landmarkMode;
            string path = Path.Combine(landmarkFolder ? UserLandmarksRoot : UserTemplatesRoot, key + ".json");
            string existing;
            try
            {
                existing = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception)
            {
                existing = null; // unreadable: overwritten as before
            }

            if (!UserTemplateFiles.HoldsOtherKind(existing, _entryMode))
            {
                return false;
            }

            SetStatus(string.Format(L(_entryMode ? "ui.ed.entry.key_taken_design" : "ui.ed.entry.key_taken_entry"), key));
            return true;
        }

        /// <summary>Writes the entry to its pool's user-content folder (the local server offers it to the next new world) and an
        /// <c>entry.json</c> to the export folder — after the registry's own check, so the server never drops it at start.</summary>
        private void ExportEntry(string key)
        {
            if (Shell?.Content == null)
            {
                return;
            }

            if (_entrySourceInvalid)
            {
                SetStatus(L("ui.ed.entry.pick_source")); // the form shows a default the entry never named
                return;
            }

            var target = CurrentTarget();
            var source = CurrentSource();
            var entry = BuildEntry(key, target, source);
            var (pool, _) = StructureStampRules.RegistryPool(target);
            var check = new StructureTemplate { Key = key, Tier = entry.tier, Source = entry.source, SourceParams = entry.sourceParams, Underground = entry.underground };
            if (SourceTemplates.Problem(check, pool, Shell.Content) is { } problem)
            {
                SetStatus(string.Format(L("ui.ed.landmark.problem"), problem));
                return;
            }

            try
            {
                bool landmark = target == StampTarget.LandmarkEntry;
                string json = JsonUtility.ToJson(entry, true);
                string userDir = landmark ? UserLandmarksRoot : UserTemplatesRoot;
                Directory.CreateDirectory(userDir);
                File.WriteAllText(Path.Combine(userDir, key + ".json"), json);
                string dir = Path.Combine(landmark ? LandmarkExportsRoot : ExportsRoot, key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "entry.json"), json);
                SetStatus(string.Format(L("ui.ed.entry.saved"), key, L(source.LabelKey), L("ui.struct.pool." + PoolOf(target))));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StructureEditor] Entry export failed: {e}");
                SetStatus(L("ui.ed.export_failed_hint"));
            }
        }

        /// <summary>The entry as the form describes it — with the loaded file's fields that have no row carried over, and a
        /// landmark entry's conditions from its toggles.</summary>
        private EntryJson BuildEntry(string key, StampTarget target, IStructureSource source)
        {
            bool landmark = target == StampTarget.LandmarkEntry;
            var extras = _entryExtras ?? new EntryJson();
            var requires = new List<string>(extras.requires ?? new List<string>());
            if (landmark)
            {
                requires = new List<string>();
                if (_lmAtmosphere)
                {
                    requires.Add(LandmarkRules.RequiresAtmosphere);
                }

                if (_lmInhabited)
                {
                    requires.Add(LandmarkRules.RequiresInhabited);
                }
            }

            return new EntryJson
            {
                key = key,
                name = _name,
                tier = _tiers[_tier],
                kind = landmark ? "landmark" : ModeName,
                pack = string.IsNullOrWhiteSpace(_pack) ? "default" : Slug(_pack),
                weight = Mathf.Max(1, _weight),
                planetTypes = EditorMode == Mode.Settlement ? PlanetTypeList() : new List<string>(),
                underground = target == StampTarget.UndergroundEntry,
                landmark = landmark,
                source = source.Key,
                sourceParams = StructureSources.FormatParams(source, Shell.Content, ParamValues(source)),
                pinOnly = extras.pinOnly,
                legacyPool = extras.legacyPool,
                @protected = extras.@protected,
                ruin = extras.ruin ?? string.Empty,
                ruinProfile = extras.ruinProfile ?? string.Empty,
                ruinStrength = extras.ruinStrength,
                nameKey = extras.nameKey ?? string.Empty,
                groundRow = extras.groundRow,
                requires = requires,
            };
        }

        private static string PoolOf(StampTarget t) => t switch
        {
            StampTarget.StationEntry => StructureStampRules.PoolStation,
            StampTarget.UndergroundEntry => StructureStampRules.PoolUnderground,
            StampTarget.LandmarkEntry => StructureStampRules.PoolLandmark,
            _ => StructureStampRules.PoolSettlement,
        };

        // ---------------------------------------------------------------- load

        /// <summary>The LOAD dialog's entries: the shipped ones of this editor's pools (as copies), then the user's own files.</summary>
        private void AddEntryItems(EditorLoadPicker.Section builtIn, EditorLoadPicker.Section user)
        {
            if (Shell?.Content != null)
            {
                foreach (var (t, pool) in ShippedEntries())
                {
                    builtIn.Items.Add(EntryItem(ToEntryJson(t), pool, copy: true));
                }
            }

            foreach (var (file, landmark) in UserEntryFiles())
            {
                EntryJson e;
                try
                {
                    e = JsonUtility.FromJson<EntryJson>(File.ReadAllText(file));
                }
                catch (Exception)
                {
                    continue; // one unreadable file must not hide the rest
                }

                if (e == null || string.IsNullOrWhiteSpace(e.source))
                {
                    continue; // an authored template or landmark: listed by its own section
                }

                if (string.IsNullOrEmpty(e.key))
                {
                    e.key = Path.GetFileNameWithoutExtension(file);
                }

                string pool = landmark ? StructureStampRules.PoolLandmark
                    : EditorMode == Mode.Station ? StructureStampRules.PoolStation
                    : e.underground ? StructureStampRules.PoolUnderground : StructureStampRules.PoolSettlement;
                user.Items.Add(EntryItem(e, pool, copy: false));
            }
        }

        private IEnumerable<(StructureTemplate T, string Pool)> ShippedEntries()
        {
            var content = Shell.Content;
            if (EditorMode == Mode.Station)
            {
                foreach (var t in content.StationTemplates)
                {
                    if (t.IsSource) yield return (t, StructureStampRules.PoolStation);
                }

                yield break;
            }

            foreach (var t in content.SettlementTemplates)
            {
                if (t.IsSource) yield return (t, t.Underground ? StructureStampRules.PoolUnderground : StructureStampRules.PoolSettlement);
            }

            foreach (var t in content.Landmarks)
            {
                if (t.IsSource) yield return (t, StructureStampRules.PoolLandmark);
            }
        }

        /// <summary>The user files that may hold entries: this editor's template folder (and the town editor's landmark folder).</summary>
        private IEnumerable<(string File, bool Landmark)> UserEntryFiles()
        {
            var dirs = new List<(string Dir, bool Landmark)> { (UserTemplatesRoot, false) };
            if (EditorMode == Mode.Settlement)
            {
                dirs.Add((UserLandmarksRoot, true));
            }

            foreach (var (dir, landmark) in dirs)
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                var files = Directory.GetFiles(dir, "*.json");
                Array.Sort(files, string.CompareOrdinal);
                foreach (var file in files)
                {
                    yield return (file, landmark);
                }
            }
        }

        /// <summary>A shipped entry in the file's shape — every field it has, so a copy saves what the original holds.</summary>
        private static EntryJson ToEntryJson(StructureTemplate t) => new EntryJson
        {
            key = t.Key, name = t.Name, tier = t.Tier, kind = t.Kind, pack = t.PackOrDefault, weight = t.Weight,
            planetTypes = new List<string>(t.PlanetTypes), underground = t.Underground, landmark = t.Landmark,
            source = t.Source, sourceParams = t.SourceParams,
            pinOnly = t.PinOnly, legacyPool = t.LegacyPool, @protected = t.Protected,
            ruin = t.Ruin ?? string.Empty, ruinProfile = t.RuinProfile ?? string.Empty, ruinStrength = t.RuinStrength,
            nameKey = t.NameKey ?? string.Empty, groundRow = t.GroundRow, requires = new List<string>(t.Requires),
        };

        private EditorLoadPicker.Item EntryItem(EntryJson e, string pool, bool copy) => new EditorLoadPicker.Item
        {
            Label = "⚙ " + (string.IsNullOrEmpty(e.name) ? e.key : e.name),
            Detail = L(StructureSources.LabelKeyPrefix + e.source) + " · " + L("ui.struct.pool." + pool),
            Load = () => LoadEntry(e, pool, copy),
        };

        /// <summary>Loads an entry: the form (a shipped one as a copy), its source and parameters, the fields without a row (kept
        /// for the save), a landmark entry's conditions, then the source's preview. A source that is unknown here or does not fit
        /// the pool is said so — never silently swapped for the pool's default.</summary>
        private void LoadEntry(EntryJson e, string pool, bool copy)
        {
            ApplyTemplate(e.key, e.name, e.tier, e.pack, e.weight, e.planetTypes, new List<CellJson>(), copy, underground: e.underground);
            _moduleMode = false;
            _landmarkMode = false;
            _entryMode = true;
            _entryPool = pool;
            _entryExtras = e;
            _sourceKey = e.source ?? string.Empty;
            _sourceValues.Clear();
            var source = StructureSources.Get(_sourceKey);
            _entrySourceInvalid = source == null || !SourceFits(source);
            if (source != null && Shell?.Content != null)
            {
                StructureSources.TryParseParams(source, Shell.Content, e.sourceParams, out var values, out _);
                foreach (var kv in values)
                {
                    _sourceValues[kv.Key] = kv.Value;
                }
            }

            if (pool == StructureStampRules.PoolLandmark)
            {
                var requires = e.requires ?? new List<string>();
                _lmAtmosphere = requires.Contains(LandmarkRules.RequiresAtmosphere);
                _lmInhabited = requires.Contains(LandmarkRules.RequiresInhabited);
            }

            ShowEntryPreview();
            if (_entrySourceInvalid)
            {
                SetStatus(string.Format(L("ui.ed.entry.bad_source"), e.source ?? string.Empty) + "\n" + _status);
            }

            RebuildUi();
        }
    }
}
