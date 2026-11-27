// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Client.Playtests;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Ship Designer's export bundle and its Submit button (#3262, in the shell's header since #3691): the same
    /// <c>ship.json</c> + <c>layout.json</c> Save writes, sent to the developers through <see cref="CreationSubmitDialog"/>
    /// with a picture from the editor's own camera. The reviewer adopts it with <c>tools/pull_submissions.py --kind ship</c> and <c>tools/merge_ship.py</c>
    /// (stats are re-balanced by hand there).
    /// </summary>
    public sealed partial class ShipEditor : IPlaytestWorkSource
    {
        /// <summary>The key of the shipped ship last loaded as a starting point (LOAD → built-in), else empty.</summary>
        private string _basedOn = string.Empty;

        private CreationSubmitDialog _submitDialog;
        private bool _capturingPreview;

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
                basedOn = BasedOn(key),
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

            string key = SaveKey();
            if (string.IsNullOrEmpty(key))
            {
                Status(L("ui.ed.need_key"), EditorStatusSeverity.Warning);
                ShowAdvanced(true); // the key field lives there
                return;
            }

            if (_design.Count == 0)
            {
                Status(L("ui.submit.empty"), EditorStatusSeverity.Warning);
                return;
            }

            // #4065: a shipped ship, block for block, holds nothing the developers could adopt (stats are re-balanced at
            // merge anyway) — say so kindly instead of sending it, like an empty build.
            string twin = ShippedShipMatch.Find(DesignCells(), Shell?.Content);
            if (twin.Length > 0)
            {
                Status(string.Format(L("ui.submit.unchanged_ship"), ShippedShipName(twin)), EditorStatusSeverity.Warning);
                return;
            }

            var payload = BuildPayload(key);
            _capturingPreview = true;
            StartCoroutine(EditorPreviewCapture.FromCamera(_cam, _design.Keys, MaxW, MaxL, png =>
            {
                _capturingPreview = false;
                OpenSubmitDialog(key, payload, png);
            }));
        }

        /// <summary>The design as the bundle a submission — or a playtest report — carries: the two export files.</summary>
        private CreationPayload BuildPayload(string key)
        {
            var (ship, layout) = BuildExport(key);
            var payload = new CreationPayload
            {
                CellCount = _design.Count,
                BlockFingerprint = Shell?.Content?.BlockFingerprint ?? string.Empty,
                BasedOn = BasedOn(key),
            };
            return payload.Add("ship.json", JsonUtility.ToJson(ship)).Add("layout.json", JsonUtility.ToJson(layout));
        }

        /// <summary>The design on the bench for a playtest report ("send my work along"); nothing while it is empty.</summary>
        public FeedbackAttachment WorkForPlaytest()
        {
            if (_design.Count == 0)
            {
                return null;
            }

            string key = SaveKey();
            key = string.IsNullOrEmpty(key) ? CreationSubmission.KindShip : key;
            return PlaytestWork.OfCreation(CreationSubmission.KindShip, key, BuildPayload(key));
        }

        /// <summary>The shipped ship this design started from: the one last loaded (also from a saved file, #4064), the one whose
        /// key it still carries, or the one it still is block for block (a ship saved before its file kept the base).</summary>
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

            return Shell.Content.Ships.ContainsKey(key) ? key : ShippedShipMatch.Find(DesignCells(), Shell.Content);
        }

        /// <summary>The build in layout coordinates, as <see cref="ShippedShipMatch"/> compares it.</summary>
        private List<CreationCell> DesignCells()
        {
            var cells = new List<CreationCell>(_design.Count);
            foreach (var kv in _design)
            {
                var d = kv.Value;
                cells.Add(new CreationCell(kv.Key.X - Origin.X, kv.Key.Y - Origin.Y, kv.Key.Z - Origin.Z, d.Kind, d.Id, d.Tint, d.Glow, d.Shape));
            }

            return cells;
        }

        /// <summary>A shipped ship's name in the player's language (its key when the content does not know it).</summary>
        private string ShippedShipName(string key)
            => Shell?.Content != null && Shell.Content.Ships.TryGetValue(key, out var def) ? L(def.NameKey) : key;

        /// <summary>A base read back from a saved file (#4064): a key-shaped word, or empty.</summary>
        private static string SavedBase(string fromFile) => Slug(fromFile ?? string.Empty);

        private void OpenSubmitDialog(string key, CreationPayload payload, byte[] png)
        {
            if (_submitDialog == null)
            {
                _submitDialog = gameObject.AddComponent<CreationSubmitDialog>();
                _submitDialog.Localize = L;
                _submitDialog.Settings = Shell != null ? Shell.Settings : null;
                _submitDialog.OnOutcome = (message, color) => Status(message, SeverityOf(color));
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
