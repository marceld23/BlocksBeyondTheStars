// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3520: the ship as user content. Save also writes it as ONE data-shaped file, <c>usercontent/ship_types/&lt;key&gt;.json</c>
    /// (the server's <c>UserShipFile</c>: definition + layout), which the local server reads at start — so a designed ship can
    /// turn up as a raider or a trader in your worlds without <c>tools/merge_ship.py</c> or a rebuild. The LOAD picker lists
    /// those files in a section of their own. The server checks every file and drops a broken one with a warning.
    /// </summary>
    public sealed partial class ShipEditor
    {
        /// <summary>The single-file shape (<c>UserShipFile</c> on the server), JsonUtility-friendly.</summary>
        [Serializable]
        private sealed class UserShipJson
        {
            public string key, name, description, requiredBlueprint, pack;
            public float baseHull, baseShield, flightSpeed, handling;
            public int weight = 1;
            public List<ExportCostJson> craftCost = new();
            public List<string> startModules = new();
            public List<string> uses = new();
            public ExportLayoutJson layout = new();
        }

        private static string UserShipsRoot => Path.Combine(AppPaths.Root, "usercontent", "ship_types");

        /// <summary>Writes the user-content copy of a saved ship and returns the key it went under. A key the game reserves
        /// (<c>custom</c>, <c>starter</c>) or a shipped ship or layout has would be refused by the server, so the copy takes the
        /// first free <c>&lt;key&gt;_2</c>, <c>_3</c>, … (<see cref="UserShipFileRules.FreeKey"/>). A file already there keeps its
        /// uses (an empty list too — <see cref="UserShipFileRules.UsesToWrite"/>), pack and weight: the editor has no field for
        /// them, and an operator may have added <c>wreck</c> or <c>craft</c> by hand.</summary>
        private string WriteUserShip(string key, ExportShipJson ship, ExportLayoutJson layout)
        {
            var content = Shell?.Content;
            key = UserShipFileRules.FreeKey(key, k => content != null && content.IsReservedUserShipKey(k));
            string path = Path.Combine(UserShipsRoot, key + ".json");
            string oldJson = ReadUserShipText(path);
            var old = ParseUserShip(oldJson);
            var file = new UserShipJson
            {
                key = key,
                name = ship.name,
                description = ship.description,
                requiredBlueprint = ship.requiredBlueprint,
                pack = old?.pack ?? string.Empty,
                baseHull = ship.baseHull,
                baseShield = ship.baseShield,
                flightSpeed = ship.flightSpeed,
                handling = ship.handling,
                weight = old != null && old.weight > 0 ? old.weight : 1,
                craftCost = ship.craftCost,
                startModules = ship.startModules,
                uses = UserShipFileRules.UsesToWrite(old != null ? oldJson : null, old?.uses),
                layout = layout,
            };

            Directory.CreateDirectory(UserShipsRoot);
            File.WriteAllText(path, JsonUtility.ToJson(file, true));
            return key;
        }

        /// <summary>The status line's extra line when the user-content copy went under another key than the export (empty when
        /// it did not).</summary>
        private string UserShipRenamedNote(string key, string userKey)
            => string.Equals(key, userKey, StringComparison.Ordinal) ? string.Empty
                : "\n" + string.Format(L("ui.ed.user_ship_renamed"), userKey, key);

        /// <summary>A user ship file, or null when it is absent or unreadable.</summary>
        private static UserShipJson ReadUserShip(string path) => ParseUserShip(ReadUserShipText(path));

        /// <summary>The raw text of a user ship file, or null when it is absent or unreadable.</summary>
        private static string ReadUserShipText(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception)
            {
                return null; // one unreadable file must not hide the rest (the server skips it the same way)
            }
        }

        /// <summary>A user ship file's text in the editor's shape, or null when there is none or it does not parse.</summary>
        private static UserShipJson ParseUserShip(string json)
        {
            try
            {
                return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<UserShipJson>(json);
            }
            catch (Exception)
            {
                return null; // one unreadable file must not hide the rest (the server skips it the same way)
            }
        }

        /// <summary>The LOAD picker's section of the user ships in <c>usercontent/ship_types/</c> (file-name order).</summary>
        private EditorLoadPicker.Section UserShipsSection()
        {
            var section = new EditorLoadPicker.Section { Title = L("ui.ed.sec_user_ships") };
            if (!Directory.Exists(UserShipsRoot))
            {
                return section;
            }

            var files = Directory.GetFiles(UserShipsRoot, "*.json");
            Array.Sort(files, string.CompareOrdinal);
            foreach (var file in files)
            {
                var ship = ReadUserShip(file);
                if (ship?.layout?.cells == null || ship.layout.cells.Count == 0)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(ship.key))
                {
                    ship.key = Path.GetFileNameWithoutExtension(file);
                }

                var s = ship;
                section.Items.Add(new EditorLoadPicker.Item
                {
                    Label = string.IsNullOrEmpty(s.name) ? s.key : s.name,
                    Detail = $"{s.layout.width}×{s.layout.length}×{s.layout.height} · " + string.Format(L("ui.ed.cells"), s.layout.cells.Count),
                    Load = () => LoadUserShip(s),
                });
            }

            return section;
        }

        /// <summary>Rebuilds the editor from a user ship file: its cells, interior, texts, stats, modules and cost.</summary>
        private void LoadUserShip(UserShipJson s)
        {
            _basedOn = string.Empty; // the player's own design, like an export
            var skippedIds = new List<string>();
            int skipped = ApplyCells(s.layout.cells, skippedIds);
            if (s.layout.width > 0 && s.layout.length > 0 && s.layout.height > 0)
            {
                _intW = s.layout.width;
                _intL = s.layout.length;
                _intH = s.layout.height;
            }

            _key = s.key;
            _shipName = string.IsNullOrEmpty(s.name) ? _shipName : s.name;
            _desc = string.IsNullOrEmpty(s.description) ? _desc : s.description;
            _requiredBlueprint = s.requiredBlueprint ?? string.Empty;
            if (s.baseHull > 0) _hull = s.baseHull;
            _shield = s.baseShield;
            if (s.flightSpeed > 0) _flightSpeed = s.flightSpeed;
            if (s.handling > 0) _handling = s.handling;
            _startModules = s.startModules != null ? new List<string>(s.startModules) : new List<string>();
            if (s.craftCost != null && s.craftCost.Count > 0)
            {
                _craftCost.Clear();
                foreach (var c in s.craftCost)
                {
                    _craftCost.Add(new CostRow { Item = c.item ?? string.Empty, Count = c.count });
                }
            }

            FinishLoad(string.IsNullOrEmpty(s.name) ? s.key : s.name, skipped, skippedIds);
        }
    }
}
