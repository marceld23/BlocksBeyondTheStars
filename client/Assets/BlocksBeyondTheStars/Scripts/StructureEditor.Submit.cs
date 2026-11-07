// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Station/Town editor's export bundle and its Submit button (#3262): the same <c>structure.json</c> +
    /// <c>layout.json</c> Save writes — plus the module's <c>kit.json</c> when it belongs to a kit — sent to the
    /// developers through <see cref="CreationSubmitDialog"/> with a picture from the editor's own camera. The save gate
    /// (<see cref="ValidateForExport"/>: ports, seal, door lanes) runs first, and a module above
    /// <see cref="CreationSubmission.MaxCells"/> cells is refused with a sentence instead of being sent.
    /// The reviewer adopts it with <c>tools/pull_submissions.py --kind structure</c> and <c>tools/merge_structure.py</c>.
    /// </summary>
    public sealed partial class StructureEditor
    {
        [Serializable] private sealed class CellJson { public int x, y, z; public string kind, id; public int tint, glow, shape; public string port = string.Empty; public int mode; public string config = string.Empty, label = string.Empty; }
        [Serializable] private sealed class LayoutJson { public int width, height, length; public List<CellJson> cells = new(); }
        [Serializable] private sealed class MetaJson
        {
            public string key, name, kind, tier, pack, layout;
            public int weight = 1;
            public List<string> planetTypes = new(); // carried through so a re-merge keeps the restriction (#1399)
            public string role = string.Empty;       // #1826: "" = whole structure, else a building-module role
            public string kit = string.Empty;        // #1877: the kit this module belongs to
            public string function = string.Empty;   // #1877: the module's function within its kit
            public string style = string.Empty;      // #1890: "" = human, "alien" = the alien variant
            public bool underground;                 // #3333: a whole settlement template only the underground stamper seats
        }

        // Data-shaped StructureTemplate (matches the server's StructureTemplate JSON) written straight to
        // the user-content folder so a structure built in-game appears in the next new world WITHOUT a merge.
        [Serializable] private sealed class TemplateJson
        {
            public string key, name, tier, kind, pack;
            public int weight = 1;
            public List<string> planetTypes = new();
            public string role = string.Empty;
            public string kit = string.Empty;
            public string function = string.Empty;
            public string style = string.Empty;
            public bool underground;
            public int width, height, length;
            public List<CellJson> cells = new();
        }

        /// <summary>#3333: the whole-template "Underground" toggle (settlement editor only — a station hangs in space, and a
        /// module takes the flag from its kit). An underground template is never rolled for the surface.</summary>
        private bool _underground;

        /// <summary>The key of the shipped template last loaded as a starting point (LOAD → built-in), else empty.</summary>
        private string _basedOn = string.Empty;

        private CreationSubmitDialog _submitDialog;
        private bool _capturingPreview;

        /// <summary>Save and Submit side by side on the footer row Save had alone — each well over 140 px, so the label
        /// fits in every language.</summary>
        private void AddSaveAndSubmit(Transform meta, float y)
        {
            const float gap = 12f, width = (348f - gap) / 2f;
            UiKit.AddFitButton(meta, 16f, y, width, 42f, L("ui.struct.save"), Export);
            UiKit.AddFitButton(meta, 16f + width + gap, y, width, 42f, L("ui.submit.button"), Submit);
        }

        /// <summary>The structure as its export files: every authored cell with its device settings (#2260), the
        /// bounding size, and the template facts (kind, tier, pack, role, kit, function, style, underground).</summary>
        private (MetaJson Meta, LayoutJson Layout) BuildExport(string key)
        {
            int maxX = 0, maxY = 0, maxZ = 0;
            var layout = new LayoutJson();
            foreach (var kv in _design)
            {
                var d = kv.Value;
                layout.cells.Add(new CellJson
                {
                    x = kv.Key.X, y = kv.Key.Y, z = kv.Key.Z,
                    kind = string.IsNullOrEmpty(d.Kind) ? "block" : d.Kind, id = d.Id,
                    tint = d.Tint, glow = d.Glow, shape = d.Shape, port = d.Port ?? string.Empty,
                    mode = DeviceMode(d), config = DeviceConfig(d), label = DeviceLabel(d), // #2260
                });
                maxX = Mathf.Max(maxX, kv.Key.X);
                maxY = Mathf.Max(maxY, kv.Key.Y);
                maxZ = Mathf.Max(maxZ, kv.Key.Z);
            }

            layout.width = maxX + 1;
            layout.height = maxY + 1;
            layout.length = maxZ + 1;
            return (BuildMeta(key), layout);
        }

        private MetaJson BuildMeta(string key)
        {
            // #1826: a module's role rides along; a city-district module always carries the metropolis tier.
            // #1877: a kit module carries its kit and function too (the settlement role mirrors a known function so
            // the legacy per-plot composer still recognises it).
            SyncRole();
            string role = EditorMode == Mode.Settlement ? _role ?? string.Empty : string.Empty;
            return new MetaJson
            {
                key = key,
                name = _name,
                kind = ModeName,
                tier = StructureRoles.IsCityRole(role) ? StructureRoles.MetropolisTier : _tiers[_tier],
                pack = string.IsNullOrWhiteSpace(_pack) ? "default" : Slug(_pack),
                weight = Mathf.Max(1, _weight),
                layout = $"{key}.json",
                planetTypes = EditorMode == Mode.Settlement ? PlanetTypeList() : new List<string>(),
                role = role,
                kit = _moduleMode ? Slug(_kit) : string.Empty,
                function = _moduleMode ? _function ?? string.Empty : string.Empty,
                style = _moduleMode && EditorMode == Mode.Settlement ? _style ?? string.Empty : string.Empty,
                underground = !_moduleMode && EditorMode == Mode.Settlement && _underground,
            };
        }

        /// <summary>Submit: the save gate, the cell cap, the bundle Save would write (+ kit), a picture, then the dialog.</summary>
        private void Submit()
        {
            if (_capturingPreview || CreationSubmitDialog.IsAnyOpen)
            {
                return;
            }

            string key = Slug(_key);
            if (string.IsNullOrEmpty(key))
            {
                SetStatus(L("ui.ed.need_key"));
                return;
            }

            if (_design.Count == 0)
            {
                SetStatus(L("ui.submit.empty"));
                return;
            }

            int maxCells = CreationSubmission.MaxCells(CreationSubmission.KindStructure);
            if (_design.Count > maxCells)
            {
                SetStatus(string.Format(L("ui.submit.too_many_cells"), _design.Count, maxCells));
                return;
            }

            if (!ValidateForExport())
            {
                return; // the same gate as Save: a broken port or a leaking module is not sent either
            }

            var payload = BuildPayload(key);
            _capturingPreview = true;
            StartCoroutine(EditorPreviewCapture.FromCamera(_cam, _design.Keys, MaxW, MaxL, png =>
            {
                _capturingPreview = false;
                OpenSubmitDialog(key, payload, png);
            }));
        }

        private CreationPayload BuildPayload(string key)
        {
            var (meta, layout) = BuildExport(key);
            var payload = new CreationPayload
            {
                CellCount = _design.Count,
                BlockFingerprint = Shell?.Content?.BlockFingerprint ?? string.Empty,
                BasedOn = BasedOn(key),
            };
            payload.Details["structureKind"] = meta.kind;
            payload.Add("structure.json", JsonUtility.ToJson(meta)).Add("layout.json", JsonUtility.ToJson(layout));

            // A town or station module belongs to its kit: the reviewer needs the kit to place it (#1877).
            if (!string.IsNullOrEmpty(meta.kit) && FindKitFile(meta.kit) is { } kit)
            {
                payload.Details["kit"] = meta.kit;
                payload.Add("kit.json", JsonUtility.ToJson(kit));
            }

            return payload;
        }

        /// <summary>The kit file of <paramref name="kitKey"/>: the user's own, else the shipped one.</summary>
        private KitJson FindKitFile(string kitKey)
        {
            foreach (var kit in KitEditorPanel.KnownKits(Shell, ModeName))
            {
                if (string.Equals(kit.key, kitKey, StringComparison.OrdinalIgnoreCase))
                {
                    return kit;
                }
            }

            return null;
        }

        /// <summary>The shipped template this design started from: the one last loaded, or the one whose key it carries.</summary>
        private string BasedOn(string key)
        {
            if (!string.IsNullOrEmpty(_basedOn))
            {
                return _basedOn;
            }

            if (Shell?.Content == null)
            {
                return string.Empty;
            }

            var pool = EditorMode == Mode.Station ? Shell.Content.StationTemplates : Shell.Content.SettlementTemplates;
            foreach (var t in pool)
            {
                if (t.Key == key)
                {
                    return key;
                }
            }

            return string.Empty;
        }

        private void OpenSubmitDialog(string key, CreationPayload payload, byte[] png)
        {
            if (_submitDialog == null)
            {
                _submitDialog = gameObject.AddComponent<CreationSubmitDialog>();
                _submitDialog.Localize = L;
                _submitDialog.Settings = Shell != null ? Shell.Settings : null;
                _submitDialog.OnOutcome = (message, _) => SetStatus(message);
            }

            _submitDialog.Open(new CreationSubmitRequest
            {
                Kind = CreationSubmission.KindStructure,
                Key = key,
                DisplayName = _name,
                Payload = payload,
                PreviewPng = png,
            });
        }
    }
}
