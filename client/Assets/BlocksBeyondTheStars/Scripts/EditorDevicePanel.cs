// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The structure editor's device tool (#2260): the settings a pre-built Crystal Net device in a settlement or station
    /// template comes out with — its <b>mode</b> (the same picker the in-game device menu shows), its <b>direction</b> (a
    /// gate, a piston, a bridge motor: the four quarter turns, up and down — written as <c>yaw=</c>), its <b>name</b> (a
    /// receiver's, a display's line) and, for everything else, the raw <b>settings line</b> (<c>key=value;…</c>: a timer's
    /// period, a display's symbols). The server sanitises all of it again when it stamps the circuit (owner <c>@world</c>:
    /// anyone may use it, only an admin re-configures it). A modal like <see cref="EditorFormPicker"/>.
    /// </summary>
    internal sealed class EditorDevicePanel
    {
        private const float W = 980f, H = 640f;

        private readonly AppShell _shell;
        private readonly GameObject _overlay;
        private readonly Transform _panel;
        private readonly Action _onClosed;
        private readonly CrystalDeviceKind _kind;
        private readonly string _blockName;
        private readonly Action<int, string, string> _onApply;
        private int _mode;
        private string _config;
        private string _label;
        private GameObject _body;

        private EditorDevicePanel(AppShell shell, Transform canvas, CrystalDeviceKind kind, string blockName, int mode, string config, string label,
            Action<int, string, string> onApply, Action onClosed)
        {
            _shell = shell;
            _kind = kind;
            _blockName = blockName;
            _mode = mode;
            _config = config ?? string.Empty;
            _label = label ?? string.Empty;
            _onApply = onApply;
            _onClosed = onClosed;
            var (overlay, panel) = UiKit.AddModalOverlay(canvas, (1920f - W) * 0.5f, (1080f - H) * 0.5f, W, H);
            _overlay = overlay;
            _panel = panel;
        }

        private string L(string key) => _shell != null ? _shell.L(key) : key;

        /// <summary>Opens the panel for one device cell; <paramref name="onApply"/> gets (mode, settings line, name).</summary>
        public static EditorDevicePanel Show(AppShell shell, Transform canvas, CrystalDeviceKind kind, string blockName, int mode, string config,
            string label, Action<int, string, string> onApply, Action onClosed = null)
        {
            var panel = new EditorDevicePanel(shell, canvas, kind, blockName, mode, config, label, onApply, onClosed);
            panel.Build();
            return panel;
        }

        public void Close()
        {
            if (_overlay != null)
            {
                UnityEngine.Object.Destroy(_overlay);
            }

            _onClosed?.Invoke();
        }

        private void Build()
        {
            if (_body != null)
            {
                UnityEngine.Object.Destroy(_body);
            }

            _body = new GameObject("Body", typeof(RectTransform));
            var rt = _body.GetComponent<RectTransform>();
            rt.SetParent(_panel, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var body = _body.transform;

            UiKit.AddText(body, 32f, 20f, W - 64f, 36f, string.Format(L("ui.ed.device.title"), _blockName), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            float y = 70f;

            // A switch has no mode picker in the game, but a pre-built one is stamped ON or OFF (its lever is its mode).
            bool lever = _kind == CrystalDeviceKind.Switch;
            int modes = lever ? 2 : CrystalNetRules.ModeCount(_kind);
            string group = ModeGroup(_kind);
            if (modes > 0 && (group != null || lever))
            {
                const float cellW = 220f, cellH = 48f, gap = 10f;
                for (int i = 0; i < modes; i++)
                {
                    int mode = i;
                    var b = UiKit.AddButton(body, 32f + (i % 4) * (cellW + gap), y + (i / 4) * (cellH + gap), cellW, cellH,
                        lever ? L(i == 1 ? "ui.crystal.output_on" : "ui.crystal.output_off") : L("ui.crystal.mode." + group + "." + i),
                        () => { _mode = mode; Build(); });
                    if (_mode == mode && b.GetComponent<Image>() is { } img)
                    {
                        img.color = UiKit.Cyan;
                    }
                }

                y += ((modes + 3) / 4) * (cellH + gap) + 8f;
            }

            if (CrystalNetRules.IsDirectional(_kind))
            {
                // The direction: written as yaw= (0..3 the quarter turns, 4 up, 5 down) — the brush turn's front otherwise.
                string yaw = CrystalMenuEdits.ValueOf(_config, "yaw");
                UiKit.AddText(body, 32f, y + 6f, 360f, 36f, L("ui.ed.device.direction"), 18, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.AddButton(body, 400f, y, 300f, 48f, yaw == null ? L("ui.ed.device.dir_front") : L("ui.ed.device.dir." + yaw), () =>
                {
                    int next = yaw == null ? 0 : (int.TryParse(yaw, out int v) ? v + 1 : 0);
                    _config = next > CrystalNetRules.YawDown
                        ? Without(_config, "yaw")
                        : CrystalMenuEdits.With(_config, "yaw", next.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Build();
                });
                y += 60f;
            }

            UiKit.AddText(body, 32f, y + 6f, 360f, 36f, L("ui.crystal.name"), 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(body, 400f, y, 520f, 48f, _label, v => _label = v ?? string.Empty, string.Empty, 24);
            y += 60f;

            UiKit.AddText(body, 32f, y + 6f, 360f, 36f, L("ui.ed.device.settings"), 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(body, 400f, y, 520f, 48f, _config, v => _config = v ?? string.Empty, "period=2;count=4", 96);
            y += 56f;
            UiKit.AddText(body, 400f, y, 520f, 26f, L("ui.ed.device.settings_hint"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);

            UiKit.AddButton(body, 32f, H - 72f, 300f, 48f, L("ui.ed.device.apply"), () =>
            {
                _onApply?.Invoke(_mode, _config.Replace("|", string.Empty).Trim(), _label.Replace("|", string.Empty).Trim());
                Close();
            });
            UiKit.AddButton(body, W - 32f - 220f, H - 72f, 220f, 48f, L("ui.crystal.close"), Close);
        }

        private static string Without(string config, string key)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var part in (config ?? string.Empty).Split(';'))
            {
                int eq = part.IndexOf('=');
                if (part.Length > 0 && !(eq > 0 && part.Substring(0, eq) == key))
                {
                    parts.Add(part);
                }
            }

            return string.Join(";", parts);
        }

        /// <summary>The mode labels' group — the in-game device menu's (<c>ui.crystal.mode.&lt;group&gt;.&lt;i&gt;</c>).</summary>
        private static string ModeGroup(CrystalDeviceKind kind) => kind switch
        {
            CrystalDeviceKind.Switch => null,
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
            CrystalDeviceKind.Piston => "piston",
            CrystalDeviceKind.SignalDisplay => "display",
            CrystalDeviceKind.DiceBlock => "dice",
            CrystalDeviceKind.EnvironmentSensor => "environment",
            _ => null,
        };
    }
}
