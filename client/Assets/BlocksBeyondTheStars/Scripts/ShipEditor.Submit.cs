// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client.Feedback;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Ship Designer's export bundle and its Submit button (#3262): the same <c>ship.json</c> + <c>layout.json</c>
    /// Save writes, sent to the developers through <see cref="CreationSubmitDialog"/> with a picture from the editor's
    /// own camera. The reviewer adopts it with <c>tools/pull_submissions.py --kind ship</c> and <c>tools/merge_ship.py</c>
    /// (stats are re-balanced by hand there).
    /// </summary>
    public sealed partial class ShipEditor
    {
        /// <summary>The key of the shipped ship last loaded as a starting point (LOAD → built-in), else empty.</summary>
        private string _basedOn = string.Empty;

        private CreationSubmitDialog _submitDialog;
        private bool _capturingPreview;

        /// <summary>Save and Submit side by side on the footer row Save had alone — each well over 140 px, so the label
        /// fits in every language.</summary>
        private void AddSaveAndSubmit(Transform meta, float y)
        {
            const float gap = 12f, width = (356f - gap) / 2f;
            UiKit.AddFitButton(meta, 12f, y, width, 42f, L("ui.struct.save"), Export);
            UiKit.AddFitButton(meta, 12f + width + gap, y, width, 42f, L("ui.submit.button"), Submit);
        }

        /// <summary>The ship as its export files: cells in layout coordinates (room minus the interior origin, #1397);
        /// the interior size is the explicit cabin frame, not the bounding box of everything placed (#1396).</summary>
        private (ExportShipJson Ship, ExportLayoutJson Layout) BuildExport(string key)
        {
            var layout = new ExportLayoutJson { width = _intW, height = _intH, length = _intL };
            foreach (var kv in _design)
            {
                var d = kv.Value;
                layout.cells.Add(new ExportCellJson
                {
                    x = kv.Key.X - Origin.X, y = kv.Key.Y - Origin.Y, z = kv.Key.Z - Origin.Z,
                    kind = string.IsNullOrEmpty(d.Kind) ? "block" : d.Kind, id = d.Id,
                    tint = d.Tint, glow = d.Glow, shape = d.Shape,
                });
            }

            var ship = new ExportShipJson
            {
                key = key,
                name = _shipName,
                description = _desc,
                requiredBlueprint = string.IsNullOrWhiteSpace(_requiredBlueprint) ? null : _requiredBlueprint.Trim(),
                layout = $"{key}.json",
                baseHull = Mathf.Round(_hull),
                baseShield = Mathf.Round(_shield),
                flightSpeed = (float)Math.Round(_flightSpeed, 2),
                handling = (float)Math.Round(_handling, 2),
                cargoSlots = _cargo,
                startModules = new System.Collections.Generic.List<string>(_startModules),
            };

            foreach (var c in _craftCost)
            {
                if (!string.IsNullOrWhiteSpace(c.Item) && c.Count > 0)
                {
                    ship.craftCost.Add(new ExportCostJson { item = c.Item.Trim(), count = c.Count });
                }
            }

            return (ship, layout);
        }

        /// <summary>Submit: the bundle Save would write, a picture of the ship, then the dialog with the consents.</summary>
        private void Submit()
        {
            if (_capturingPreview || CreationSubmitDialog.IsAnyOpen)
            {
                return;
            }

            string key = Slug(_key);
            if (string.IsNullOrEmpty(key))
            {
                _status = L("ui.ed.need_key");
                return;
            }

            if (_design.Count == 0)
            {
                _status = L("ui.submit.empty");
                return;
            }

            var (ship, layout) = BuildExport(key);
            var payload = new CreationPayload
            {
                CellCount = _design.Count,
                BlockFingerprint = Shell?.Content?.BlockFingerprint ?? string.Empty,
                BasedOn = BasedOn(key),
            };
            payload.Add("ship.json", JsonUtility.ToJson(ship)).Add("layout.json", JsonUtility.ToJson(layout));

            _capturingPreview = true;
            StartCoroutine(EditorPreviewCapture.FromCamera(_cam, _design.Keys, MaxW, MaxL, png =>
            {
                _capturingPreview = false;
                OpenSubmitDialog(key, payload, png);
            }));
        }

        /// <summary>The shipped ship this design started from: the one last loaded, or the one whose key it still carries.</summary>
        private string BasedOn(string key)
        {
            if (!string.IsNullOrEmpty(_basedOn))
            {
                return _basedOn;
            }

            return Shell?.Content != null && Shell.Content.Ships.ContainsKey(key) ? key : string.Empty;
        }

        private void OpenSubmitDialog(string key, CreationPayload payload, byte[] png)
        {
            if (_submitDialog == null)
            {
                _submitDialog = gameObject.AddComponent<CreationSubmitDialog>();
                _submitDialog.Localize = L;
                _submitDialog.Settings = Shell != null ? Shell.Settings : null;
                _submitDialog.OnOutcome = (message, _) => _status = message;
            }

            _submitDialog.Open(new CreationSubmitRequest
            {
                Kind = CreationSubmission.KindShip,
                Key = key,
                DisplayName = _shipName,
                Payload = payload,
                PreviewPng = png,
            });
        }
    }
}
