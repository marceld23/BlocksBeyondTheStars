// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Crystal Net device menu (#2049): opens on Interact at a configurable device and shows, per kind, a
    /// <b>mode grid</b> (the picker every kid understands: one button per mode, the current one lit), a few
    /// <b>setting rows</b> that cycle on click (radius, period, count, instrument), and for the pairing kinds a
    /// <b>list</b> (matter receivers, beam pads, recipes, animals). Every click is sent to the server at once —
    /// it validates, persists and echoes the device back through <see cref="GameBootstrap.CrystalDevices"/>.
    /// Modal like <see cref="BeamPadUi"/>: pad-navigable, closes on Esc / pad B / the Close button, touch taps
    /// the buttons directly.
    /// </summary>
    public sealed class CrystalDeviceUi : MonoBehaviour
    {
        public static CrystalDeviceUi Instance { get; private set; }
        public GameBootstrap Game;

        private Canvas _canvas;
        private Transform _panel;
        private GameObject _overlay;
        private bool _open;
        private int _openFrame = -1;
        private NetCrystalDevice _dev;
        private int _mode;
        private string _config = string.Empty;
        private string _label = string.Empty;

        private const float W = 980f, H = 760f;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        public bool IsOpen => _open;

        /// <summary>The device kind behind a wire record (its <c>Kind</c> is the enum name).</summary>
        public static CrystalDeviceKind KindOf(NetCrystalDevice dev)
            => dev != null && System.Enum.TryParse<CrystalDeviceKind>(dev.Kind, out var k) ? k : CrystalDeviceKind.None;

        public void Open(NetCrystalDevice dev)
        {
            if (dev == null) return;
            EnsureCanvas();
            _dev = dev;
            _mode = dev.Mode;
            _config = dev.Config ?? string.Empty;
            _label = dev.Label ?? string.Empty;
            _open = true;
            _openFrame = Time.frameCount;
            _canvas.gameObject.SetActive(true);
            Build();
            Game?.SetMenuOwner(this, true);
        }

        private void Update()
        {
            if (!_open) return;
            if (Time.frameCount != _openFrame && InputMap.Down(InputAction.UiCancel))
            {
                Game?.MarkMenuInputHandled();
                Close();
            }
        }

        private void Close()
        {
            _open = false;
            if (_overlay != null) Destroy(_overlay);
            _overlay = null;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            Game?.SetMenuOwner(this, false);
        }

        private void EnsureCanvas()
        {
            if (_canvas != null) return;
            _canvas = UiKit.CreateCanvas("CrystalDeviceUI");
            _canvas.sortingOrder = 59;
            UiNav.Enable(_canvas.gameObject); // pad: the stick walks the grid, A picks, B closes (#1198)
            _canvas.gameObject.SetActive(false);
        }

        private void Rebuild()
        {
            if (_overlay != null) Destroy(_overlay);
            Build();
        }

        private void Send()
        {
            Game?.Network?.SendSetCrystalDevice(_dev.X, _dev.Y, _dev.Z, 2, _mode, _config, _label);
        }

        private void Build()
        {
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas.transform, (1920f - W) * 0.5f, (1080f - H) * 0.5f, W, H);
            _overlay = overlay;
            _panel = panel;
            var kind = KindOf(_dev);

            string blockKey = Game?.Content?.BlockById(Game.World.GetBlock(_dev.X, _dev.Y, _dev.Z))?.Key;
            string name = blockKey != null ? L("block." + blockKey + ".name") : _dev.Kind;
            var head = UiKit.AddText(panel, 32f, 24f, W - 64f, 40f, string.Format(L("ui.crystal.title"), name), 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(head);
            string state = _dev.Output ? L("ui.crystal.output_on") : L("ui.crystal.output_off");
            UiKit.AddText(panel, 32f, 66f, W - 64f, 26f, state, 16, UiKit.CyanDim, TextAnchor.MiddleLeft);

            float y = 104f;
            int modes = CrystalNetRules.ModeCount(kind);
            if (modes > 0 && kind != CrystalDeviceKind.MelodyBlock)
            {
                y = ModeGrid(panel, y, kind, modes);
            }
            else if (kind == CrystalDeviceKind.MelodyBlock)
            {
                y = ModeGrid(panel, y, kind, modes); // the eight notes
                y = CycleRow(panel, y, L("ui.crystal.instrument"), "inst", new[] { "0", "1", "2", "3" }, v => L("ui.crystal.instrument." + v));
            }

            switch (kind)
            {
                case CrystalDeviceKind.ProximitySensor:
                    y = CycleRow(panel, y, L("ui.crystal.radius"), "r", new[] { "0", "1", "2" }, v => L("ui.crystal.radius." + v));
                    break;
                case CrystalDeviceKind.TimerBlock:
                    y = CycleRow(panel, y, L("ui.crystal.period"), "period", new[] { "0.5", "1", "2", "3", "5", "10" }, v => v + " s");
                    y = CycleRow(panel, y, L("ui.crystal.count"), "count", new[] { "2", "3", "4", "5", "8", "10", "16" }, v => v);
                    break;
                case CrystalDeviceKind.StorageSensor:
                    y = CycleRow(panel, y, L("ui.crystal.count"), "n", new[] { "1", "4", "8", "16", "32", "64" }, v => v);
                    break;
                case CrystalDeviceKind.Announcer:
                    y = LabelRow(panel, y, L("ui.crystal.label"));
                    break;
                case CrystalDeviceKind.MatterSender:
                    y = ListRows(panel, y, L("ui.crystal.pair"), ReceiverOptions(), "pair");
                    break;
                case CrystalDeviceKind.BeamPad:
                    y = ListRows(panel, y, L("ui.crystal.pair"), BeamOptions(), "pair");
                    break;
                case CrystalDeviceKind.Fabricator:
                    y = ListRows(panel, y, L("ui.crystal.recipe"), RecipeOptions(), "recipe");
                    break;
                case CrystalDeviceKind.CloneTank:
                    y = ListRows(panel, y, L("ui.crystal.species"), SpeciesOptions(), "sp");
                    break;
            }

            _ = y;
            bool machine = kind is CrystalDeviceKind.Fabricator or CrystalDeviceKind.MatterSender or CrystalDeviceKind.CloneTank
                or CrystalDeviceKind.AutoDrill or CrystalDeviceKind.Caller or CrystalDeviceKind.Thumper or CrystalDeviceKind.HydroTray;
            if (machine)
            {
                UiKit.AddButton(panel, 32f, H - 72f, 300f, 48f, L("ui.crystal.start"), () =>
                {
                    Game?.Network?.SendSetCrystalDevice(_dev.X, _dev.Y, _dev.Z, 1);
                    ClientAudio.Instance?.Cue("ui_confirm");
                });
            }

            UiKit.AddButton(panel, W - 32f - 220f, H - 72f, 220f, 48f, L("ui.crystal.close"), () =>
            {
                Game?.MarkMenuInputHandled();
                Close();
            });
        }

        /// <summary>One button per mode, four to a row, the current mode lit cyan. A click is sent at once.</summary>
        private float ModeGrid(Transform panel, float y, CrystalDeviceKind kind, int modes)
        {
            const float cellW = 220f, cellH = 56f, gap = 12f;
            string group = ModeGroup(kind);
            for (int i = 0; i < modes; i++)
            {
                int mode = i;
                float x = 32f + (i % 4) * (cellW + gap);
                float yy = y + (i / 4) * (cellH + gap);
                var b = UiKit.AddButton(panel, x, yy, cellW, cellH, L("ui.crystal.mode." + group + "." + i), () =>
                {
                    _mode = mode;
                    Send();
                    ClientAudio.Instance?.Cue("ui_click");
                    Rebuild();
                });
                if (_mode == mode)
                {
                    var img = b.GetComponent<Image>();
                    if (img != null) img.color = UiKit.Cyan;
                }
            }

            int rows = (modes + 3) / 4;
            return y + rows * (cellH + gap) + 12f;
        }

        private static string ModeGroup(CrystalDeviceKind kind) => kind switch
        {
            CrystalDeviceKind.StepPlate => "plate",
            CrystalDeviceKind.ProximitySensor => "proximity",
            CrystalDeviceKind.DaylightSensor => "daylight",
            CrystalDeviceKind.StorageSensor => "storage",
            CrystalDeviceKind.LogicBlock => "logic",
            CrystalDeviceKind.TimerBlock => "timer",
            CrystalDeviceKind.AlarmSiren => "siren",
            CrystalDeviceKind.Chime => "chime",
            CrystalDeviceKind.Horn => "horn",
            CrystalDeviceKind.MelodyBlock => "note",
            CrystalDeviceKind.Announcer => "announce",
            CrystalDeviceKind.AutoDrill => "drill",
            CrystalDeviceKind.CloneTank => "clone",
            _ => "none",
        };

        /// <summary>A labelled row whose value cycles through <paramref name="values"/> on click and is written to
        /// the config under <paramref name="key"/>.</summary>
        private float CycleRow(Transform panel, float y, string label, string key, string[] values, System.Func<string, string> show)
        {
            UiKit.AddText(panel, 32f, y + 10f, 360f, 36f, label, 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            string current = ConfigValue(key) ?? values[0];
            int idx = System.Array.IndexOf(values, current);
            if (idx < 0) idx = 0;
            UiKit.AddButton(panel, 400f, y, 300f, 48f, show(values[idx]), () =>
            {
                _config = ConfigWith(key, values[(idx + 1) % values.Length]);
                Send();
                ClientAudio.Instance?.Cue("ui_click");
                Rebuild();
            });
            return y + 60f;
        }

        private float LabelRow(Transform panel, float y, string label)
        {
            UiKit.AddText(panel, 32f, y + 10f, 360f, 36f, label, 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(panel, 400f, y, 520f, 48f, _label, v => { _label = v ?? string.Empty; }, string.Empty, 24);
            UiKit.AddButton(panel, 32f, y + 60f, 300f, 48f, L("ui.crystal.save"), () =>
            {
                Send();
                ClientAudio.Instance?.Cue("ui_confirm");
            });
            return y + 120f;
        }

        /// <summary>A scrollable list of options for the pairing kinds; the chosen one is lit.</summary>
        private float ListRows(Transform panel, float y, string title, List<(string Value, string Text)> options, string key)
        {
            UiKit.AddText(panel, 32f, y, W - 64f, 30f, title, 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 36f;
            float listH = Mathf.Max(120f, H - 90f - y);
            var list = UiKit.ScrollList(panel, 32f, y, W - 64f, listH, 6f);
            string current = ConfigValue(key) ?? string.Empty;
            if (options.Count == 0)
            {
                Row(list, 56f, go => UiKit.AddText(go.transform, 12f, 8f, 800f, 40f, L("ui.crystal.none"), 17, UiKit.CyanDim, TextAnchor.MiddleLeft));
            }

            foreach (var opt in options)
            {
                var o = opt;
                Row(list, 52f, go =>
                {
                    var b = UiKit.AddButton(go.transform, 8f, 4f, W - 100f, 44f, o.Text, () =>
                    {
                        _config = ConfigWith(key, o.Value);
                        Send();
                        ClientAudio.Instance?.Cue("ui_confirm");
                        Rebuild();
                    });
                    if (o.Value == current)
                    {
                        var img = b.GetComponent<Image>();
                        if (img != null) img.color = UiKit.Cyan;
                    }
                });
            }

            return y + listH;
        }

        private static void Row(RectTransform list, float height, System.Action<GameObject> fill)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(list, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            fill(go);
        }

        private List<(string, string)> ReceiverOptions()
        {
            var result = new List<(string, string)>();
            if (Game?.CrystalDevices == null) return result;
            foreach (var d in Game.CrystalDevices)
            {
                if (d.Kind != nameof(CrystalDeviceKind.MatterReceiver)) continue;
                string label = string.IsNullOrEmpty(d.Label) ? L("ui.crystal.unnamed") : d.Label;
                result.Add((d.Id.ToString(), $"{label}  ·  X {d.X}  Z {d.Z}"));
            }

            return result;
        }

        private List<(string, string)> BeamOptions()
        {
            var result = new List<(string, string)>();
            if (Game?.Beams == null) return result;
            foreach (var b in Game.Beams)
            {
                if (!Game.CanUseBeam(b) || (Mathf.FloorToInt(b.X) == _dev.X && Mathf.FloorToInt(b.Z) == _dev.Z && Mathf.FloorToInt(b.Y) == _dev.Y)) continue;
                string label = string.IsNullOrEmpty(b.Name) ? L("ui.beam.default") : b.Name;
                result.Add((b.Id.ToString(), $"{label}  ·  X {Mathf.FloorToInt(b.X)}  Z {Mathf.FloorToInt(b.Z)}"));
            }

            return result;
        }

        private List<(string, string)> RecipeOptions()
        {
            var result = new List<(string, string)>();
            if (Game?.Content == null) return result;
            foreach (var r in Game.Content.Recipes.Values)
            {
                if (r.Station is not (CraftingStation.Workshop or CraftingStation.Hand) || r.MarketTheme.Length > 0 || r.Outputs.Count == 0) continue;
                if (r.RequiredBlueprint is { Length: > 0 } bp && !Game.UnlockedBlueprints.Contains(bp)) continue;
                var item = Game.Content.GetItem(r.Outputs[0].Item);
                string text = item != null ? L(item.NameKey) : r.Outputs[0].Item;
                result.Add((r.Key, text + " ×" + r.Outputs[0].Count));
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Item2, b.Item2));
            return result;
        }

        /// <summary>The animals around: every species with a creature on this world right now — the server checks
        /// that the owner has scanned or tamed it here, and never clones a hostile one.</summary>
        private List<(string, string)> SpeciesOptions()
        {
            var result = new List<(string, string)>();
            var seen = new HashSet<string>();
            if (Game?.Creatures == null) return result;
            foreach (var c in Game.Creatures)
            {
                if (c.Hostile || string.IsNullOrEmpty(c.SpeciesId) || !seen.Add(c.SpeciesId)) continue;
                string text = string.IsNullOrEmpty(c.Name) ? L(c.NameKey) : c.Name;
                result.Add((c.SpeciesId, text));
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Item2, b.Item2));
            return result;
        }

        private string ConfigValue(string key)
        {
            foreach (var part in _config.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && part.Substring(0, eq) == key) return part.Substring(eq + 1);
            }

            return null;
        }

        private string ConfigWith(string key, string value)
        {
            var parts = new List<string>();
            bool set = false;
            foreach (var part in _config.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && part.Substring(0, eq) == key)
                {
                    parts.Add(key + "=" + value);
                    set = true;
                }
                else if (part.Length > 0)
                {
                    parts.Add(part);
                }
            }

            if (!set) parts.Add(key + "=" + value);
            return string.Join(";", parts);
        }

        private string L(string k) => Game?.Localizer?.Get(k) ?? k;
    }
}
