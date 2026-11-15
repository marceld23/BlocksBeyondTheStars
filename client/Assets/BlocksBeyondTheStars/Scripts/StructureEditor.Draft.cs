// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration.Sources;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Station/Village editor's draft (#3689, #3692): while the work is unsaved the shell keeps it as a draft and offers it
    /// on the next opening ("Continue your draft?") instead of asking "Save changes?". The draft is <see cref="DraftPrefix"/>
    /// (a version line), then one JSON object: the editor (station / village), which template the work was opened from
    /// (<c>loadedFrom</c>, <c>basedOn</c> — a submission names the shipped template it started from), every form field, the
    /// fields a loaded file carried without a form row (a generator entry's extras, a giant ship's zones, a civilization's
    /// fields and the carves under its markers) and the design's cells — the design, never a preview that shows instead of it.
    /// One editor's draft never reaches the other (the draft slot is the editor's key, and the draft names its editor).
    /// </summary>
    public sealed partial class StructureEditor
    {
        /// <summary>The draft's first line: the format and its version (a later format can still read this one).</summary>
        internal const string DraftPrefix = "bbs-structure-draft/1\n";

        /// <summary>What the work was opened from — "<c>&lt;kind&gt;:&lt;key&gt;</c>" of a load, the generator or kit it was made
        /// with, or empty for new work.</summary>
        private string _loadedFrom = string.Empty;

        [Serializable]
        private sealed class DraftJson
        {
            public string mode = string.Empty, loadedFrom = string.Empty, basedOn = string.Empty;
            public string key = string.Empty, name = string.Empty, pack = "default", kit = string.Empty, function = string.Empty, style = string.Empty;
            public string role = string.Empty, planetTypes = string.Empty, protection = string.Empty, surface = string.Empty;
            public int tier, weight = 1, seed = 1, groundRow, previewSeed = 1;
            public bool moduleMode, landmarkMode, giantShipMode, entryMode, underground;
            public bool lmRuin = true, lmAtmosphere = true, lmInhabited;
            public string lmProfile = string.Empty, lmNameKey = string.Empty, lmSeat = string.Empty;
            public float lmStrength;
            public int lmMinGeneration;
            public List<ZoneJson> zones = new();
            public string gsStyle = string.Empty, gsNameKey = string.Empty;
            public float gsStrength;
            public int gsMinGeneration;
            public List<ZoneJson> gsZones = new();
            public string entryPool = string.Empty, sourceKey = string.Empty;
            public List<string> sourceKeys = new(), sourceValues = new();
            public bool hasEntryExtras;
            public EntryJson entryExtras = new();
            public string civ = string.Empty, civPool = string.Empty, propPool = string.Empty, civProtection = string.Empty;
            public List<CellJson> carves = new();
            public List<CellJson> cells = new();
        }

        /// <inheritdoc/>
        public string SaveDraft()
        {
            var cells = DesignCells();
            if (cells.Count == 0 && !IsDeveloperDesign && FormSignature() == _cleanSignature)
            {
                return null; // an empty room with an untouched form: nothing worth keeping
            }

            var d = new DraftJson
            {
                mode = ModeName, loadedFrom = _loadedFrom ?? string.Empty, basedOn = _basedOn ?? string.Empty,
                key = _key, name = _name, pack = _pack, kit = _kit, function = _function, style = _style, role = _role,
                planetTypes = _planetTypes, protection = _protection, surface = _surface,
                tier = _tier, weight = _weight, seed = _seed, groundRow = _groundRow, previewSeed = _previewSeed,
                moduleMode = _moduleMode, landmarkMode = _landmarkMode, giantShipMode = _giantShipMode, entryMode = _entryMode, underground = _underground,
                lmRuin = _lmRuin, lmAtmosphere = _lmAtmosphere, lmInhabited = _lmInhabited, lmProfile = _lmProfile, lmNameKey = _lmNameKey,
                lmSeat = _lmSeat, lmStrength = _lmStrength, lmMinGeneration = _lmMinGeneration, zones = ToZoneJsons(_zones),
                gsStyle = _gsStyle, gsNameKey = _gsNameKey, gsStrength = _gsStrength, gsMinGeneration = _gsMinGeneration, gsZones = ToZoneJsons(_gsZones),
                entryPool = _entryPool, sourceKey = _sourceKey, hasEntryExtras = _entryExtras != null, entryExtras = _entryExtras ?? new EntryJson(),
                civ = _civ.Civilization, civPool = _civ.CivPool, propPool = _civ.PropPool, civProtection = _civ.Protection,
                cells = cells,
            };
            foreach (var kv in _sourceValues)
            {
                d.sourceKeys.Add(kv.Key);
                d.sourceValues.Add(kv.Value);
            }

            foreach (var kv in _carves)
            {
                var p = kv.Value;
                d.carves.Add(new CellJson { x = p.X, y = p.Y, z = p.Z, kind = p.Air ? "air" : p.Cavity ? "cavity" : "block", id = p.Id ?? string.Empty,
                    tint = p.Tint, glow = p.Glow, shape = p.Shape, keep = p.Keep });
            }

            return DraftPrefix + JsonUtility.ToJson(d);
        }

        /// <inheritdoc/>
        public bool LoadDraft(string draft)
        {
            if (!TryReadDraft(draft, out var d))
            {
                return false;
            }

            try
            {
                ResetForm();
                ApplyDraftFields(d);
                ApplyCells(d.cells ?? new List<CellJson>(), new List<string>());
                NewDocument();
                _cleanPending = false;
                _draftDirty = true; // a restored draft is unsaved work
                OpenActiveSection();
                ShowEntryPreview(); // a generator entry shows its source's build (nothing otherwise)
                EditorSceneKit.Frame(_cam.transform, ref _pitch, _yaw, _design.Keys, MaxW, MaxL);
                RebuildUi();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StructureEditor] The draft could not be restored: {e.Message}");
                ResetForm();
                ApplyCells(new List<CellJson>(), new List<string>());
                NewDocument();
                RebuildUi();
                return false;
            }
        }

        /// <summary>Reads a draft of THIS editor (its prefix, its JSON, its mode); false for anything else.</summary>
        private bool TryReadDraft(string draft, out DraftJson d)
        {
            d = null;
            if (string.IsNullOrEmpty(draft) || !draft.StartsWith(DraftPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                d = JsonUtility.FromJson<DraftJson>(draft.Substring(DraftPrefix.Length));
            }
            catch (Exception)
            {
                return false;
            }

            return d != null && d.mode == ModeName;
        }

        private void ApplyDraftFields(DraftJson d)
        {
            _loadedFrom = d.loadedFrom ?? string.Empty;
            _basedOn = d.basedOn ?? string.Empty;
            _key = d.key ?? string.Empty;
            _name = d.name ?? string.Empty;
            _pack = string.IsNullOrEmpty(d.pack) ? "default" : d.pack;
            (_kit, _function, _style, _role) = (d.kit ?? string.Empty, d.function ?? string.Empty, d.style ?? string.Empty, d.role ?? string.Empty);
            _planetTypes = d.planetTypes ?? string.Empty;
            _protection = LandmarkRules.ProtectionOf(d.protection);
            _surface = string.IsNullOrEmpty(d.surface) ? _surface : d.surface;
            _tier = Mathf.Clamp(d.tier, 0, _tiers.Length - 1);
            (_weight, _seed, _groundRow, _previewSeed) = (Mathf.Max(1, d.weight), d.seed, d.groundRow, d.previewSeed);
            (_moduleMode, _landmarkMode, _giantShipMode, _entryMode, _underground) = (d.moduleMode, d.landmarkMode, d.giantShipMode, d.entryMode, d.underground);
            (_lmRuin, _lmAtmosphere, _lmInhabited) = (d.lmRuin, d.lmAtmosphere, d.lmInhabited);
            _lmProfile = string.IsNullOrEmpty(d.lmProfile) ? LandmarkRules.ProfileMasonry : d.lmProfile;
            (_lmNameKey, _lmSeat, _lmStrength, _lmMinGeneration) = (d.lmNameKey ?? string.Empty, d.lmSeat ?? string.Empty, d.lmStrength, d.lmMinGeneration);
            LoadZones(FromZoneJsons(d.zones));
            (_gsStyle, _gsNameKey, _gsStrength, _gsMinGeneration) = (d.gsStyle ?? string.Empty, d.gsNameKey ?? string.Empty, d.gsStrength, d.gsMinGeneration);
            _gsZones.Clear();
            _gsZones.AddRange(FromZoneJsons(d.gsZones));
            _entryPool = string.IsNullOrEmpty(d.entryPool) ? StructureStampRules.PoolSettlement : d.entryPool;
            _sourceKey = d.sourceKey ?? string.Empty;
            _sourceValues.Clear();
            for (int i = 0; i < Math.Min(d.sourceKeys?.Count ?? 0, d.sourceValues?.Count ?? 0); i++)
            {
                _sourceValues[d.sourceKeys[i]] = d.sourceValues[i];
            }

            _entryExtras = d.hasEntryExtras ? d.entryExtras : null;
            _entrySourceInvalid = _entryMode && (StructureSources.Get(_sourceKey) is not { } own || !SourceFits(own));
            _civ = new CivilizationFields { Civilization = d.civ ?? string.Empty, CivPool = d.civPool ?? string.Empty, PropPool = d.propPool ?? string.Empty,
                Protection = d.civProtection ?? string.Empty };
            _carves = new Dictionary<(int X, int Y, int Z), TemplateBody.Plain>();
            foreach (var c in d.carves ?? new List<CellJson>())
            {
                _carves[(c.x, c.y, c.z)] = new TemplateBody.Plain(c.x, c.y, c.z, c.kind != "block", c.id, c.tint, c.glow, c.shape, c.keep, c.kind == "air");
            }

            SyncRole();
        }
    }
}
