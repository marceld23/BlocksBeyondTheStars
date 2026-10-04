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
    /// The open menu follows that echo (#2214): a newer record of its device — a tank's species list after a new
    /// sample, its settings after a finished job — replaces what the menu was opened with, so a click never sends
    /// yesterday's settings back (see <see cref="CrystalMenuEdits"/>).
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

        // #2214: following the server's record while the menu is open.
        private readonly CrystalMenuEdits _edits = new CrystalMenuEdits(); // the player's changes the server has not answered yet
        private Text _stateText;                                           // the ON/OFF line — a blinking timer changes it in place, no rebuild
        private bool _modeShown, _labelShown;                              // this menu has a mode grid / a name field
        private readonly HashSet<string> _shown = new HashSet<string>();   // the settings this menu has a row for
        private readonly Dictionary<string, RectTransform> _lists = new Dictionary<string, RectTransform>(); // the lists, by config key
        private readonly Dictionary<string, float> _scroll = new Dictionary<string, float>();                // where each was scrolled to

        private const float W = 980f, H = 760f;
        private const float TankH = 960f; // #2208: the clone tank lists a species AND a partner — a taller panel fits both

        private float PanelH => KindOf(_dev) == CrystalDeviceKind.CloneTank ? TankH : H;

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
            _edits.Clear();
            _lists.Clear();
            _scroll.Clear(); // another visit starts at the top of every list
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
                return;
            }

            FollowServerRecord();
        }

        /// <summary>
        /// #2214: takes over the server's newer record of the device this menu shows. Every device list replaces
        /// <see cref="GameBootstrap.CrystalDevices"/>, and lists arrive often (any device of the world that changes
        /// sends one), so the server's mode and settings are taken over at once — a click then sends the server's
        /// latest values for everything the player did not touch (<see cref="CrystalMenuEdits"/>) — while the menu
        /// is rebuilt only when something it <b>shows</b> really changed:
        /// <list type="bullet">
        /// <item>the <b>species list</b> of a clone tank (a new sample, a new scan);</item>
        /// <item>the <b>mode</b>, or a <b>setting this menu has a row for</b> — after a finished job the tank's
        /// species and partner are the server's new ones. What the server keeps in the settings for itself (how deep
        /// a drill laser has cut, which clones a tank holds) is taken over silently;</item>
        /// <item>the <b>name</b> — only while the player has not typed another one into the field.</item>
        /// </list>
        /// The ON/OFF line is rewritten in place. A device that is gone from the list keeps the menu as it is.
        /// </summary>
        private void FollowServerRecord()
        {
            var latest = Game?.CrystalDeviceAt(_dev.X, _dev.Y, _dev.Z);
            if (latest == null)
            {
                return;
            }

            bool newer = !ReferenceEquals(latest, _dev);
            bool rebuild = false;
            if (newer)
            {
                if (latest.Id != _dev.Id || latest.Kind != _dev.Kind)
                {
                    // Another device stands in this cell now: its record as a whole, like a fresh visit.
                    _dev = latest;
                    _mode = latest.Mode;
                    _config = latest.Config ?? string.Empty;
                    _label = latest.Label ?? string.Empty;
                    _edits.Clear();
                    _lists.Clear();
                    _scroll.Clear();
                    Rebuild();
                    return;
                }

                rebuild = !SameChoices(latest.Choices, _dev.Choices);
                string was = _dev.Label ?? string.Empty, now = latest.Label ?? string.Empty;
                if (now != was && _label == was)
                {
                    _label = now;
                    rebuild |= _labelShown;
                }

                if (latest.Output != _dev.Output && _stateText != null)
                {
                    _stateText.text = latest.Output ? L("ui.crystal.output_on") : L("ui.crystal.output_off");
                }

                _dev = latest;
            }

            int modeWas = _mode;
            string configWas = _config;
            if (_edits.Follow(latest.Mode, latest.Config, newer, Time.unscaledTime, ref _mode, ref _config))
            {
                rebuild |= (_modeShown && _mode != modeWas) || !CrystalMenuEdits.SameSettings(_config, configWas, _shown);
            }

            if (rebuild)
            {
                Rebuild();
            }
        }

        private static bool SameChoices(string[] a, string[] b)
        {
            int n = a?.Length ?? 0;
            if (n != (b?.Length ?? 0)) return false;
            for (int i = 0; i < n; i++)
            {
                if (a[i] != b[i]) return false;
            }

            return true;
        }

        private void Close()
        {
            _open = false;
            if (_overlay != null) Destroy(_overlay);
            _overlay = null;
            _stateText = null;
            _edits.Clear();
            _lists.Clear();
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
            // Keep every list where it was scrolled to (the pad's place in the menu is kept by UiNav, by position).
            foreach (var kv in _lists)
            {
                if (kv.Value != null) _scroll[kv.Key] = kv.Value.anchoredPosition.y;
            }

            _lists.Clear();
            if (_overlay != null) Destroy(_overlay);
            Build();
        }

        private void Send()
            => Game?.SendCrystalDevice(_dev.X, _dev.Y, _dev.Z, 2, _mode, _config, _label);

        /// <summary>The player picks a mode: sent at once, and held against the server's record until that answers.</summary>
        private void ChangeMode(int mode)
        {
            _mode = mode;
            _edits.MarkMode(Time.unscaledTime);
            Send();
        }

        /// <summary>The player changes one setting: only this one is the player's — everything else in the line that
        /// is sent is the server's latest.</summary>
        private void Change(string key, string value)
        {
            _config = CrystalMenuEdits.With(_config, key, value);
            _edits.Mark(key, Time.unscaledTime);
            Send();
        }

        private void Build()
        {
            float h = PanelH;
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas.transform, (1920f - W) * 0.5f, (1080f - h) * 0.5f, W, h);
            _overlay = overlay;
            _panel = panel;
            _modeShown = _labelShown = false;
            _shown.Clear(); // the grid and the rows below say what this menu shows
            var kind = KindOf(_dev);

            var standing = Game.World.GetBlock(_dev.X, _dev.Y, _dev.Z);
            if (standing.IsAir)
            {
                standing = Game.LandedShipBlockAt(_dev.X, _dev.Y, _dev.Z, out _, out _); // #2268: a device aboard a parked ship
            }

            string blockKey = Game?.Content?.BlockById(standing)?.Key;
            string name = blockKey != null ? L("block." + blockKey + ".name") : _dev.Kind;
            var head = UiKit.AddText(panel, 32f, 24f, W - 64f, 40f, string.Format(L("ui.crystal.title"), name), 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(head);
            string state = _dev.Output ? L("ui.crystal.output_on") : L("ui.crystal.output_off");
            _stateText = UiKit.AddText(panel, 32f, 66f, W - 64f, 26f, state, 16, UiKit.CyanDim, TextAnchor.MiddleLeft);

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
                case CrystalDeviceKind.BridgeMotor: // #2265: how far the deck reaches
                    y = CycleRow(panel, y, L("ui.crystal.length"), "len", new[] { "2", "3", "4", "5", "6", "8", "10", "12" }, v => v);
                    y = LabelRow(panel, y, L("ui.crystal.name"));
                    break;
                case CrystalDeviceKind.SignalDisplay: // #2263: a symbol pair, its own line, or a counter
                    switch ((DisplayMode)_mode)
                    {
                        case DisplayMode.Symbol:
                            y = CycleRow(panel, y, L("ui.crystal.symbol_on"), "on", SymbolValues, v => SymbolOf(v));
                            y = CycleRow(panel, y, L("ui.crystal.symbol_off"), "off", SymbolValues, v => SymbolOf(v));
                            break;
                        case DisplayMode.Text:
                            y = LabelRow(panel, y, L("ui.crystal.text"));
                            break;
                        default:
                            UiKit.AddText(panel, 32f, y + 10f, 360f, 36f, (CrystalMenuEdits.ValueOf(_dev.Config, "n") ?? "0"), 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                            UiKit.AddButton(panel, 400f, y, 300f, 48f, L("ui.crystal.reset"), () =>
                            {
                                Game?.SendCrystalDevice(_dev.X, _dev.Y, _dev.Z, 4);
                                ClientAudio.Instance?.Cue("ui_click");
                            });
                            y += 60f;
                            break;
                    }

                    break;
                case CrystalDeviceKind.SignalReceiver: // #2263: its name (the remote shows it) and the sender it repeats
                    y = LabelRow(panel, y, L("ui.crystal.name"));
                    y = ListRows(panel, y, L("ui.crystal.sender"), SenderOptions(), "pair", 0f, L("ui.crystal.sender_none"));
                    break;
                case CrystalDeviceKind.SignalSender:
                case CrystalDeviceKind.LiftMotor:
                case CrystalDeviceKind.LiftStop:
                case CrystalDeviceKind.Piston:
                case CrystalDeviceKind.DiceBlock:
                case CrystalDeviceKind.EnvironmentSensor:
                case CrystalDeviceKind.ShipSensor:
                    y = LabelRow(panel, y, L("ui.crystal.name")); // a name the menus and the remote show
                    break;
                case CrystalDeviceKind.BeamPad:
                    y = ListRows(panel, y, L("ui.crystal.pair"), BeamOptions(), "pair");
                    break;
                case CrystalDeviceKind.Fabricator:
                    y = ListRows(panel, y, L("ui.crystal.recipe"), RecipeOptions(), "recipe");
                    break;
                case CrystalDeviceKind.CloneTank:
                {
                    // #2207/#2208: the species to grow and, to cross it, a second one — two lists that share what is
                    // left of the panel. "Nothing" as the partner is a plain clone.
                    float each = (h - 90f - y - 2f * 36f - 12f) * 0.5f;
                    var species = SpeciesOptions();
                    var partners = new List<(string Value, string Text)> { (string.Empty, L("ui.crystal.partner_none")) };
                    partners.AddRange(species);
                    y = ListRows(panel, y, L("ui.crystal.species"), species, "sp", each) + 12f;
                    y = ListRows(panel, y, L("ui.crystal.partner"), partners, "x", each);
                    break;
                }
            }

            _ = y;
            bool machine = kind is CrystalDeviceKind.Fabricator or CrystalDeviceKind.MatterSender or CrystalDeviceKind.CloneTank
                or CrystalDeviceKind.AutoDrill or CrystalDeviceKind.Caller or CrystalDeviceKind.Thumper or CrystalDeviceKind.HydroTray
                or CrystalDeviceKind.DrillLaser or CrystalDeviceKind.LiftMotor or CrystalDeviceKind.LiftStop;
            if (machine)
            {
                UiKit.AddButton(panel, 32f, h - 72f, 300f, 48f, L(kind == CrystalDeviceKind.LiftStop ? "ui.crystal.prompt.call" : "ui.crystal.start").Replace("{0}: ", string.Empty), () =>
                {
                    Game?.SendCrystalDevice(_dev.X, _dev.Y, _dev.Z, 1);
                    ClientAudio.Instance?.Cue("ui_confirm");
                });
            }

            if (CrystalNetRules.IsDirectional(kind))
            {
                // #2267: turn after placing — the next of the six directions (four quarter turns, up, down).
                UiKit.AddButton(panel, machine ? 348f : 32f, h - 72f, 260f, 48f, L("ui.crystal.turn"), () =>
                {
                    Game?.SendCrystalDevice(_dev.X, _dev.Y, _dev.Z, 3);
                    ClientAudio.Instance?.Cue("ui_click");
                });
            }

            UiKit.AddButton(panel, W - 32f - 220f, h - 72f, 220f, 48f, L("ui.crystal.close"), () =>
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
            _modeShown = true;
            for (int i = 0; i < modes; i++)
            {
                int mode = i;
                float x = 32f + (i % 4) * (cellW + gap);
                float yy = y + (i / 4) * (cellH + gap);
                var b = UiKit.AddButton(panel, x, yy, cellW, cellH, L("ui.crystal.mode." + group + "." + i), () =>
                {
                    ChangeMode(mode);
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
            CrystalDeviceKind.DrillLaser => "drill", // #2108: the same two modes (only ore / everything)
            CrystalDeviceKind.CloneTank => "clone",
            CrystalDeviceKind.Piston => "piston",                 // #2265
            CrystalDeviceKind.SignalDisplay => "display",         // #2263
            CrystalDeviceKind.DiceBlock => "dice",
            CrystalDeviceKind.EnvironmentSensor => "environment",
            CrystalDeviceKind.ShipSensor => "ship",               // #2268
            _ => "none",
        };

        /// <summary>A labelled row whose value cycles through <paramref name="values"/> on click and is written to
        /// the config under <paramref name="key"/>.</summary>
        private float CycleRow(Transform panel, float y, string label, string key, string[] values, System.Func<string, string> show)
        {
            UiKit.AddText(panel, 32f, y + 10f, 360f, 36f, label, 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            _shown.Add(key);
            string current = ConfigValue(key) ?? values[0];
            int idx = System.Array.IndexOf(values, current);
            if (idx < 0) idx = 0;
            UiKit.AddButton(panel, 400f, y, 300f, 48f, show(values[idx]), () =>
            {
                Change(key, values[(idx + 1) % values.Length]);
                ClientAudio.Instance?.Cue("ui_click");
                Rebuild();
            });
            return y + 60f;
        }

        private float LabelRow(Transform panel, float y, string label)
        {
            UiKit.AddText(panel, 32f, y + 10f, 360f, 36f, label, 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            _labelShown = true;
            UiKit.AddInput(panel, 400f, y, 520f, 48f, _label, v => { _label = v ?? string.Empty; }, string.Empty, 24);
            UiKit.AddButton(panel, 32f, y + 60f, 300f, 48f, L("ui.crystal.save"), () =>
            {
                Send();
                ClientAudio.Instance?.Cue("ui_confirm");
            });
            return y + 120f;
        }

        /// <summary>A scrollable list of options for the pairing kinds; the chosen one is lit. It takes what is left of
        /// the panel unless <paramref name="height"/> says how tall it is (the clone tank stacks two).</summary>
        private float ListRows(Transform panel, float y, string title, List<(string Value, string Text)> options, string key, float height = 0f, string empty = null)
        {
            UiKit.AddText(panel, 32f, y, W - 64f, 30f, title, 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 36f;
            float listH = height > 0f ? height : Mathf.Max(120f, PanelH - 90f - y);
            var list = UiKit.ScrollList(panel, 32f, y, W - 64f, listH, 6f);
            _shown.Add(key);
            string current = ConfigValue(key) ?? string.Empty;
            if (options.Count == 0)
            {
                Row(list, 56f, go => UiKit.AddText(go.transform, 12f, 8f, 800f, 40f, empty ?? L("ui.crystal.none"), 17, UiKit.CyanDim, TextAnchor.MiddleLeft));
            }

            foreach (var opt in options)
            {
                var o = opt;
                Row(list, 52f, go =>
                {
                    var b = UiKit.AddButton(go.transform, 8f, 4f, W - 100f, 44f, o.Text, () =>
                    {
                        Change(key, o.Value);
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

            // A rebuild (a click, a newer record from the server) leaves the list where the player had scrolled it.
            _lists[key] = list;
            if (_scroll.TryGetValue(key, out float at))
            {
                list.anchoredPosition = new Vector2(list.anchoredPosition.x, at);
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
                result.Add((CellValue(d.X, d.Y, d.Z), $"{label}  ·  X {d.X}  Z {d.Z}")); // #2252: the partner's cell
            }

            return result;
        }

        /// <summary>#2263: the signal senders this receiver may repeat — the player's own and allied ones in the receiver's
        /// own frame (the world, or the same parked ship, #2268), named by their cell in that frame.</summary>
        private List<(string, string)> SenderOptions()
        {
            var result = new List<(string, string)>();
            if (Game?.CrystalDevices == null) return result;
            bool aboard = Game.Crystal.TryFrameCell(_dev.X, _dev.Y, _dev.Z, out string myFrame, out _);
            foreach (var d in Game.CrystalDevices)
            {
                if (d.Kind != nameof(CrystalDeviceKind.SignalSender) || !Game.CanOperateCrystal(d)) continue;
                bool senderAboard = Game.Crystal.TryFrameCell(d.X, d.Y, d.Z, out string frame, out var local);
                if (senderAboard != aboard || (aboard && frame != myFrame)) continue;
                string label = string.IsNullOrEmpty(d.Label) ? L("ui.crystal.unnamed") : d.Label;
                string value = aboard ? CellValue(local.X, local.Y, local.Z) : CellValue(d.X, d.Y, d.Z);
                result.Add((value, $"{label}  ·  X {d.X}  Z {d.Z}"));
            }

            return result;
        }

        private static string CellValue(int x, int y, int z) => x + "," + y + "," + z;

        /// <summary>#2263: the display's symbols — plain glyphs every font in the game draws.</summary>
        public static readonly string[] DisplaySymbols = { "★", "♥", "☺", "!", "?", "→", "←", "↑", "↓", "+", "−", "♪", "●", "■", "▲", "×" };

        private static readonly string[] SymbolValues = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15" };

        /// <summary>The glyph a display's symbol setting stands for.</summary>
        public static string SymbolOf(string value)
            => int.TryParse(value, out int i) && i >= 0 && i < DisplaySymbols.Length ? DisplaySymbols[i] : DisplaySymbols[0];

        private List<(string, string)> BeamOptions()
        {
            var result = new List<(string, string)>();
            if (Game?.Beams == null) return result;
            foreach (var b in Game.Beams)
            {
                if (!Game.CanUseBeam(b) || (Mathf.FloorToInt(b.X) == _dev.X && Mathf.FloorToInt(b.Z) == _dev.Z && Mathf.FloorToInt(b.Y) == _dev.Y)) continue;
                string label = string.IsNullOrEmpty(b.Name) ? L("ui.beam.default") : b.Name;
                result.Add((CellValue(Mathf.FloorToInt(b.X), Mathf.FloorToInt(b.Y), Mathf.FloorToInt(b.Z)), $"{label}  ·  X {Mathf.FloorToInt(b.X)}  Z {Mathf.FloorToInt(b.Z)}"));
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

        /// <summary>#2097: the species the server allows this tank's owner to clone here — scanned or tamed on this world,
        /// never a hostile one — as "id|coined name" in the device's <c>Choices</c>. Empty → nothing to pick yet.
        /// #2207: an id that starts with "g:" is a sample from the owner's sample case (a species of any world); its
        /// label says so.</summary>
        private List<(string, string)> SpeciesOptions()
        {
            var result = new List<(string, string)>();
            if (_dev?.Choices == null) return result;
            foreach (var choice in _dev.Choices)
            {
                int bar = choice.IndexOf('|');
                if (bar <= 0) continue;
                string id = choice.Substring(0, bar);
                string name = choice.Substring(bar + 1);
                if (string.IsNullOrEmpty(name)) name = L("ui.crystal.unnamed");
                if (id.StartsWith("g:", System.StringComparison.Ordinal)) name += " (" + L("ui.crystal.sample") + ")";
                result.Add((id, name));
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Item2, b.Item2));
            return result;
        }

        private string ConfigValue(string key) => CrystalMenuEdits.ValueOf(_config, key);

        private string L(string k) => Game?.Localizer?.Get(k) ?? k;
    }

    /// <summary>
    /// The changes a player made in an open device menu that the server has not answered yet (#2214), and the rule
    /// by which the menu follows the server's record around them:
    /// <list type="bullet">
    /// <item>whatever the player did <b>not</b> touch is the server's — taken over whenever a newer record arrives, so
    /// a click sends the server's latest values for everything else (a tank's species and partner after a finished
    /// job, not the ones the menu was opened with);</item>
    /// <item>a mode or a setting the player <b>changed</b> stays the player's until a newer record names the same
    /// value (the server's answer), so a device list that was already on its way cannot undo the click on screen;</item>
    /// <item>a change that gets no answer within <see cref="AnswerWaitSeconds"/> — the server refused it (not the
    /// owner, out of reach) or cut it — gives way to what the server really holds.</item>
    /// </list>
    /// Unity-free — the caller hands the time in — so the rule can be checked without a scene.
    /// </summary>
    public sealed class CrystalMenuEdits
    {
        /// <summary>How long a change waits for the server's answer before the server's value is taken again.</summary>
        public const float AnswerWaitSeconds = 3f;

        private readonly Dictionary<string, float> _keys = new Dictionary<string, float>(); // setting → when the player changed it
        private readonly List<string> _answered = new List<string>();
        private float _modeSince = float.NegativeInfinity; // when the player picked a mode; −∞ = the server's mode holds

        public void Clear()
        {
            _keys.Clear();
            _modeSince = float.NegativeInfinity;
        }

        public void MarkMode(float now) => _modeSince = now;

        public void Mark(string key, float now) => _keys[key] = now;

        /// <summary>
        /// Brings the menu's <paramref name="mode"/> and <paramref name="config"/> in line with the server's record:
        /// the server's values, with the player's unanswered changes on top. <paramref name="newer"/> says whether
        /// this record arrived since the last call — only a newer record can answer a change. Returns whether mode
        /// or config changed. Cheap while nothing happens (no newer record, no change waited out): it returns at once.
        /// </summary>
        public bool Follow(int serverMode, string serverConfig, bool newer, float now, ref int mode, ref string config)
        {
            serverConfig ??= string.Empty;
            config ??= string.Empty;
            bool gaveWay = false;
            if (!float.IsNegativeInfinity(_modeSince) && ((newer && serverMode == mode) || now - _modeSince >= AnswerWaitSeconds))
            {
                _modeSince = float.NegativeInfinity;
                gaveWay = true;
            }

            if (_keys.Count > 0)
            {
                _answered.Clear();
                foreach (var kv in _keys)
                {
                    if ((newer && Setting(serverConfig, kv.Key) == Setting(config, kv.Key)) || now - kv.Value >= AnswerWaitSeconds)
                    {
                        _answered.Add(kv.Key);
                    }
                }

                foreach (string key in _answered)
                {
                    _keys.Remove(key);
                }

                gaveWay |= _answered.Count > 0;
            }

            if (!newer && !gaveWay)
            {
                return false;
            }

            int nextMode = float.IsNegativeInfinity(_modeSince) ? serverMode : mode;
            string next = serverConfig;
            foreach (var kv in _keys)
            {
                next = With(next, kv.Key, Setting(config, kv.Key));
            }

            bool changed = nextMode != mode || next != config;
            mode = nextMode;
            config = next;
            return changed;
        }

        /// <summary>The value of a setting in a config line ("key=value;key=value"), or null when the line does not name it.</summary>
        public static string ValueOf(string config, string key)
        {
            foreach (var part in (config ?? string.Empty).Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && part.Substring(0, eq) == key) return part.Substring(eq + 1);
            }

            return null;
        }

        /// <summary>As <see cref="ValueOf"/>, with "not named" and "named without a value" read alike (empty).</summary>
        public static string Setting(string config, string key) => ValueOf(config, key) ?? string.Empty;

        /// <summary>Whether two config lines read the same in each of the given settings — the ones a menu has a row
        /// for. What else the lines hold (the server's own keys, the order) shows nowhere and does not count.</summary>
        public static bool SameSettings(string a, string b, HashSet<string> keys)
        {
            foreach (string key in keys)
            {
                if (Setting(a, key) != Setting(b, key)) return false;
            }

            return true;
        }

        /// <summary>The config line with one setting set — in its place when the line names it, at the end otherwise.</summary>
        public static string With(string config, string key, string value)
        {
            var parts = new List<string>();
            bool set = false;
            foreach (var part in (config ?? string.Empty).Split(';'))
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
    }
}
