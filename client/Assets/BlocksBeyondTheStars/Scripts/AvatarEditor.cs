// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Standalone avatar skin designer (menu tool, sibling of <see cref="ShipEditor"/>). A slowly
    /// rotating live <see cref="PlayerAvatar"/> preview with per-part colour controls and gear toggles;
    /// <b>Apply</b> saves the colours into <see cref="ClientSettings"/> (so the in-game avatar uses them)
    /// and <b>Export</b> writes a skin bundle (skin.json) a developer can fold into the game with
    /// tools/merge_avatar.py. Self-contained on the client (no server); modern uGUI.
    /// <para>
    /// The <b>Outfits</b> panel beside it (#1047) keeps up to <see cref="ClientSettings.MaxOutfits"/> named
    /// looks (colours + face + body paint) in the settings file: <i>Save outfit</i> stores the designer's
    /// current scratch look under the name field (a new outfit, or overwriting the one with that name),
    /// clicking a row loads it into the designer, <i>Rename selected</i> renames the highlighted row to the
    /// name field, ✕ deletes. None of that touches the in-game avatar — only Apply does, exactly as before —
    /// so the server never learns about outfits; it only ever sees the applied look on the next join.
    /// </para>
    /// </summary>
    public sealed class AvatarEditor : MonoBehaviour
    {
        public AppShell Shell;

        private Camera _cam;
        private PlayerAvatar _avatar;
        private Transform _avatarRoot;
        private Canvas _canvas;

        private readonly Color[] _col = new Color[4]; // skin, torso, arms, legs
        private readonly Image[] _swatch = new Image[4];
        private readonly bool[] _gear = new bool[5];  // helmet, chest, legs, pack, lamp
        private readonly Text[] _gearLabel = new Text[5];
        private string _name = "My Skin";
        private Text _status;
        private string _face = string.Empty; // encoded pixel face (same format as the in-game editor)
        private readonly string[] _bodyPaint = new string[BodyPaintKit.PartCount]; // body paintings (#874)
        private FaceEditor _faceEditor;
        private InputField _nameInput;

        // Outfits panel (#1047): rows are rebuilt from Settings.Outfits on every change; the selection is the
        // row last loaded/saved and is what "Rename selected" acts on.
        private RectTransform _outfitPanel;
        private readonly List<GameObject> _outfitRows = new List<GameObject>();
        private readonly List<Text> _outfitLabels = new List<Text>();
        private Text _outfitEmpty;
        private int _selectedOutfit = -1;
        private const float OutfitPanelW = 300f, OutfitPanelH = 480f, OutfitRowH = 32f, OutfitRowStep = 38f, OutfitRowsY = 56f;

        private void Start()
        {
            var s = Shell?.Settings;
            _col[0] = s?.SkinColor ?? AppearancePalette.Colors[0];
            _col[1] = s?.TorsoColor ?? AppearancePalette.Colors[4];
            _col[2] = s?.ArmColor ?? AppearancePalette.Colors[4];
            _col[3] = s?.LegColor ?? AppearancePalette.Colors[8];
            _face = s?.FacePixels ?? string.Empty;
            for (int part = 0; part < _bodyPaint.Length; part++)
            {
                _bodyPaint[part] = s?.GetBodyPaint(part) ?? string.Empty;
            }

            BuildScene();
            BuildUi();
        }

        private void BuildScene()
        {
            var camGo = new GameObject("AvatarCam");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.03f, 0.05f, 0.09f);
            _cam.transform.position = new Vector3(-0.7f, 1.15f, 3.4f);
            _cam.transform.rotation = Quaternion.Euler(4f, 192f, 0f); // look back at the avatar
            camGo.AddComponent<AudioListener>();

            var lightGo = new GameObject("AvatarSun");
            lightGo.transform.SetParent(transform, false);
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(35f, 200f, 0f);
            sun.intensity = 1.1f;

            _avatarRoot = new GameObject("AvatarPreview").transform;
            _avatarRoot.SetParent(transform, false);
            _avatar = _avatarRoot.gameObject.AddComponent<PlayerAvatar>();
            _avatar.Build(_col[0], _col[1], _col[2], _col[3], spacesuit: true); // preview the player's suited look
            _avatar.SetVisible(true);
            _avatar.SetFace(_face); // show the player's current custom face on the preview figure
            for (int part = 0; part < _bodyPaint.Length; part++)
            {
                _avatar.SetBodyPaint(part, _bodyPaint[part]); // and the current body paintings (#874)
            }
        }

        private void Update()
        {
            if (_avatarRoot != null)
            {
                _avatarRoot.Rotate(0f, Time.deltaTime * 24f, 0f, Space.World);
            }
        }

        private void BuildUi()
        {
            _canvas = UiKit.CreateCanvas("Avatar Editor UI");
            _canvas.sortingOrder = 5;
            var root = _canvas.transform;

            // Right-hand control panel (anchored to the top-right).
            const float w = 420f, h = 920f;
            var panel = RightPanel(root, w, h);
            UiKit.AddText(panel, 20f, 14f, w - 40f, 28f, L("ui.avatar.title"), 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            BuildOutfitPanel(root, w); // saved looks, left of the control panel (#1047)

            float y = 56f;
            UiKit.AddText(panel, 20f, y, w - 40f, 22f, L("ui.avatar.colors"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 30f;
            ColorRow(panel, ref y, L("ui.settings.skin"), 0);
            ColorRow(panel, ref y, L("ui.settings.torso"), 1);
            ColorRow(panel, ref y, L("ui.settings.arms"), 2);
            ColorRow(panel, ref y, L("ui.settings.legs"), 3);
            UiKit.AddText(panel, 24f, y, w - 48f, 20f, L("ui.appearance.more_colors"), 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 26f;

            y += 8f;
            UiKit.AddText(panel, 20f, y, w - 40f, 22f, L("ui.avatar.gear_preview"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 30f;
            GearRow(panel, ref y, L("ui.avatar.helmet"), 0);
            GearRow(panel, ref y, L("ui.avatar.chest"), 1);
            GearRow(panel, ref y, L("ui.avatar.legs"), 2);
            GearRow(panel, ref y, L("ui.avatar.pack"), 3);
            GearRow(panel, ref y, L("ui.avatar.lamp"), 4);

            y += 10f;
            UiKit.AddText(panel, 20f, y, w - 40f, 22f, L("ui.avatar.face"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 30f;

            // One button instead of five (#899): face, torso, arms, legs and helmet are tabs of the shared
            // appearance screen, which also carries the base colours for the part being painted.
            UiKit.AddButton(panel, 20f, y, w - 40f, 34f, L("ui.appearance.title"), OpenAppearanceEditor);
            y += 52f;
            UiKit.AddText(panel, 20f, y, w - 40f, 22f, L("ui.avatar.name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            y += 26f;
            _nameInput = UiKit.AddInput(panel, 20f, y, w - 40f, 30f, _name, v => _name = v);
            y += 44f;

            UiKit.AddButton(panel, 20f, y, w - 40f, 38f, L("ui.avatar.apply"), Apply);
            y += 46f;
            UiKit.AddButton(panel, 20f, y, w - 40f, 38f, L("ui.avatar.export"), Export);
            y += 50f;
            _status = UiKit.AddText(panel, 20f, y, w - 40f, 60f, string.Empty, 14, UiKit.Ok, TextAnchor.UpperLeft);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;

            UiKit.AddButton(panel, 20f, h - 56f, 200f, 40f, L("ui.menu.back"), () => Shell?.CloseAvatarEditor());

            // Controls hint (bottom-left under the preview).
            UiKit.AddText(root, 40f, 1020f, 900f, 26f, L("ui.avatar.hint"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);

            UiNav.Enable(_canvas.gameObject); // gamepad can drive the designer + outfits (inert on KB/mouse)
        }

        // --- outfits (#1047) ---

        /// <summary>The Outfits panel: a fixed-height list of named looks (no scrolling — the list is capped at
        /// <see cref="ClientSettings.MaxOutfits"/> rows) plus Save / Rename. Sits left of the control panel,
        /// anchored top-right like it, so both stay together on any aspect ratio.</summary>
        private void BuildOutfitPanel(Transform root, float controlPanelW)
        {
            var go = new GameObject("OutfitPanel", typeof(RectTransform));
            go.transform.SetParent(root, false);
            _outfitPanel = go.GetComponent<RectTransform>();
            _outfitPanel.anchorMin = _outfitPanel.anchorMax = _outfitPanel.pivot = new Vector2(1f, 1f);
            _outfitPanel.sizeDelta = new Vector2(OutfitPanelW, OutfitPanelH);
            _outfitPanel.anchoredPosition = new Vector2(-16f - controlPanelW - 12f, -16f);
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.PanelSprite;
            img.type = Image.Type.Sliced;
            img.color = UiKit.PanelFill;

            UiKit.AddText(_outfitPanel, 16f, 14f, OutfitPanelW - 32f, 28f, L("ui.avatar.outfits"), 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _outfitEmpty = UiKit.AddText(_outfitPanel, 16f, OutfitRowsY, OutfitPanelW - 32f, 80f, L("ui.avatar.outfit_none"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
            _outfitEmpty.horizontalOverflow = HorizontalWrapMode.Wrap;

            float y = OutfitRowsY + ClientSettings.MaxOutfits * OutfitRowStep + 12f;
            UiKit.AddButton(_outfitPanel, 16f, y, OutfitPanelW - 32f, 38f, L("ui.avatar.outfit_save"), SaveOutfit);
            y += 46f;
            UiKit.AddButton(_outfitPanel, 16f, y, OutfitPanelW - 32f, 38f, L("ui.avatar.outfit_rename"), RenameSelectedOutfit);

            RefreshOutfitList();
        }

        /// <summary>Rebuilds the outfit rows from the settings (cheap: at most eight rows, and only on a change).</summary>
        private void RefreshOutfitList()
        {
            foreach (var row in _outfitRows)
            {
                if (row != null)
                {
                    Destroy(row);
                }
            }

            _outfitRows.Clear();
            _outfitLabels.Clear();

            var outfits = Shell?.Settings?.Outfits;
            int count = outfits?.Count ?? 0;
            if (_selectedOutfit >= count)
            {
                _selectedOutfit = -1;
            }

            if (_outfitEmpty != null)
            {
                _outfitEmpty.gameObject.SetActive(count == 0);
            }

            for (int i = 0; i < count && i < ClientSettings.MaxOutfits; i++)
            {
                int idx = i;
                float y = OutfitRowsY + i * OutfitRowStep;
                var name = string.IsNullOrEmpty(outfits[i].Name) ? "?" : outfits[i].Name;
                var load = UiKit.AddButton(_outfitPanel, 16f, y, OutfitPanelW - 32f - 56f, OutfitRowH, name, () => LoadOutfit(idx));
                var label = load.GetComponentInChildren<Text>();
                label.color = idx == _selectedOutfit ? UiKit.Cyan : UiKit.TextCol;
                var del = UiKit.AddButton(_outfitPanel, OutfitPanelW - 16f - 48f, y, 48f, OutfitRowH, "✕", () => DeleteOutfit(idx));
                _outfitRows.Add(load.gameObject);
                _outfitRows.Add(del.gameObject);
                _outfitLabels.Add(label);
            }
        }

        private void SelectOutfit(int index)
        {
            _selectedOutfit = index;
            for (int i = 0; i < _outfitLabels.Count; i++)
            {
                if (_outfitLabels[i] != null)
                {
                    _outfitLabels[i].color = i == index ? UiKit.Cyan : UiKit.TextCol;
                }
            }
        }

        /// <summary>The designer's current scratch look as an outfit (detached copies of the pixel strings).</summary>
        private AvatarOutfit CaptureScratch(string name)
        {
            var o = new AvatarOutfit
            {
                Name = name,
                SkinColor = _col[0],
                TorsoColor = _col[1],
                ArmColor = _col[2],
                LegColor = _col[3],
                FacePixels = _face ?? string.Empty,
            };
            for (int part = 0; part < _bodyPaint.Length; part++)
            {
                o.SetBodyPaint(part, _bodyPaint[part]);
            }

            return o;
        }

        /// <summary>Loads a saved outfit into the designer (scratch values, swatches, preview figure and the name
        /// field) — the in-game avatar is untouched until Apply, which the status line says explicitly.</summary>
        private void LoadOutfit(int index)
        {
            var outfits = Shell?.Settings?.Outfits;
            if (outfits == null || index < 0 || index >= outfits.Count)
            {
                return;
            }

            var o = outfits[index];
            _col[0] = o.SkinColor;
            _col[1] = o.TorsoColor;
            _col[2] = o.ArmColor;
            _col[3] = o.LegColor;
            for (int i = 0; i < _swatch.Length; i++)
            {
                if (_swatch[i] != null)
                {
                    _swatch[i].color = _col[i];
                }
            }

            _face = o.FacePixels ?? string.Empty;
            for (int part = 0; part < _bodyPaint.Length; part++)
            {
                _bodyPaint[part] = o.GetBodyPaint(part);
            }

            if (_avatar != null)
            {
                _avatar.ApplyColors(_col[0], _col[1], _col[2], _col[3]);
                _avatar.SetFace(_face);
                for (int part = 0; part < _bodyPaint.Length; part++)
                {
                    _avatar.SetBodyPaint(part, _bodyPaint[part]);
                }
            }

            _name = o.Name ?? string.Empty;
            if (_nameInput != null)
            {
                _nameInput.text = _name;
            }

            SelectOutfit(index);
            SetStatus(L("ui.avatar.outfit_loaded").Replace("{name}", _name));
        }

        /// <summary>Save outfit: stores the scratch look under the name field — overwriting an outfit that already
        /// carries that name (case-insensitive), otherwise appending a new one up to the cap.</summary>
        private void SaveOutfit()
        {
            if (Shell?.Settings is not { } s)
            {
                return;
            }

            string name = (_name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                SetStatus(L("ui.avatar.need_name"));
                return;
            }

            s.Outfits ??= new List<AvatarOutfit>();
            int existing = s.Outfits.FindIndex(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                s.Outfits[existing] = CaptureScratch(s.Outfits[existing].Name);
                s.Save();
                RefreshOutfitList();
                SelectOutfit(existing);
                SetStatus(L("ui.avatar.outfit_updated").Replace("{name}", s.Outfits[existing].Name));
                return;
            }

            if (s.Outfits.Count >= ClientSettings.MaxOutfits)
            {
                SetStatus(L("ui.avatar.outfit_limit").Replace("{max}", ClientSettings.MaxOutfits.ToString()));
                return;
            }

            s.Outfits.Add(CaptureScratch(name));
            s.Save();
            RefreshOutfitList();
            SelectOutfit(s.Outfits.Count - 1);
            SetStatus(L("ui.avatar.outfit_saved").Replace("{name}", name));
        }

        /// <summary>Rename selected: the highlighted outfit takes the name field's text (its pixels stay).</summary>
        private void RenameSelectedOutfit()
        {
            if (Shell?.Settings is not { } s || s.Outfits == null)
            {
                return;
            }

            if (_selectedOutfit < 0 || _selectedOutfit >= s.Outfits.Count)
            {
                SetStatus(L("ui.avatar.outfit_select_first"));
                return;
            }

            string name = (_name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                SetStatus(L("ui.avatar.need_name"));
                return;
            }

            int clash = s.Outfits.FindIndex(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
            if (clash >= 0 && clash != _selectedOutfit)
            {
                SetStatus(L("ui.avatar.outfit_name_taken").Replace("{name}", s.Outfits[clash].Name));
                return;
            }

            s.Outfits[_selectedOutfit].Name = name;
            s.Save();
            RefreshOutfitList();
            SelectOutfit(_selectedOutfit);
            SetStatus(L("ui.avatar.outfit_renamed").Replace("{name}", name));
        }

        /// <summary>Deletes a saved outfit. The applied in-game look is a separate copy, so deleting even the outfit
        /// you are wearing changes nothing in the game — which is why this needs no confirmation.</summary>
        private void DeleteOutfit(int index)
        {
            if (Shell?.Settings is not { } s || s.Outfits == null || index < 0 || index >= s.Outfits.Count)
            {
                return;
            }

            string name = s.Outfits[index].Name;
            s.Outfits.RemoveAt(index);
            s.Save();
            if (_selectedOutfit == index)
            {
                _selectedOutfit = -1;
            }
            else if (_selectedOutfit > index)
            {
                _selectedOutfit--;
            }

            RefreshOutfitList();
            SetStatus(L("ui.avatar.outfit_deleted").Replace("{name}", name));
        }

        private void ColorRow(Transform panel, ref float y, string label, int which)
        {
            UiKit.AddText(panel, 24f, y, 150f, 30f, label, 16, UiKit.TextCol, TextAnchor.MiddleLeft);

            var sw = new GameObject("Swatch", typeof(RectTransform));
            sw.transform.SetParent(panel, false);
            UiKit.Place(sw, 180f, y + 3f, 24f, 24f);
            var img = sw.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.color = _col[which];
            img.raycastTarget = false;
            _swatch[which] = img;

            int idx = which;
            UiKit.AddButton(panel, 214f, y, 80f, 30f, "→", () => CyclePart(idx, 1));
            UiKit.AddButton(panel, 300f, y, 80f, 30f, "←", () => CyclePart(idx, -1));
            y += 38f;
        }

        private void CyclePart(int which, int dir)
        {
            _col[which] = AppearancePalette.Next(_col[which], dir);
            _swatch[which].color = _col[which];
            _avatar.ApplyColors(_col[0], _col[1], _col[2], _col[3]);
        }

        private void GearRow(Transform panel, ref float y, string label, int which)
        {
            UiKit.AddText(panel, 24f, y, 200f, 30f, label, 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            int idx = which;
            var btn = UiKit.AddButton(panel, 240f, y, 140f, 30f, OffOn(false), () => ToggleGear(idx));
            _gearLabel[which] = btn.GetComponentInChildren<Text>();
            y += 38f;
        }

        private void ToggleGear(int which)
        {
            _gear[which] = !_gear[which];
            _gearLabel[which].text = OffOn(_gear[which]);
            _avatar.SetGear(_gear[0], _gear[1], _gear[2], _gear[3], _gear[4]);
        }

        private string OffOn(bool on) => on ? L("ui.avatar.on") : L("ui.avatar.off");

        /// <summary>Opens the shared appearance screen over the designer — face, torso, arms, legs and helmet
        /// as tabs, each with its base colour. Edits land in this designer's scratch values and on the rotating
        /// figure behind the panel; the panel's Apply is still what persists them into
        /// <see cref="ClientSettings"/>, so backing out of the designer changes nothing.</summary>
        private void OpenAppearanceEditor()
        {
            if (_faceEditor != null)
            {
                return;
            }

            var go = new GameObject("AppearanceEditor");
            go.transform.SetParent(transform, false);
            _faceEditor = go.AddComponent<FaceEditor>();
            _faceEditor.Localizer = L;
            _faceEditor.Subjects = AppearanceSubjects.Build(
                () => _face,
                face =>
                {
                    _face = face ?? string.Empty;
                    _avatar?.SetFace(_face);
                },
                part => _bodyPaint[part],
                (part, pixels) =>
                {
                    _bodyPaint[part] = pixels ?? string.Empty;
                    _avatar?.SetBodyPaint(part, _bodyPaint[part]);
                },
                which => _col[which],
                (which, color) =>
                {
                    _col[which] = color;
                    if (_swatch[which] != null)
                    {
                        _swatch[which].color = color;
                    }

                    _avatar.ApplyColors(_col[0], _col[1], _col[2], _col[3]);
                });
            _faceEditor.PreviewState = () => AppearanceSubjects.Snapshot(
                which => _col[which], () => _face, part => _bodyPaint[part]);
            _faceEditor.OnClosed = () => _faceEditor = null;
        }

        private void Apply()
        {
            if (Shell?.Settings is { } s)
            {
                s.SkinColor = _col[0];
                s.TorsoColor = _col[1];
                s.ArmColor = _col[2];
                s.LegColor = _col[3];
                s.FacePixels = _face;
                for (int part = 0; part < _bodyPaint.Length; part++)
                {
                    s.SetBodyPaint(part, _bodyPaint[part]);
                }

                s.Save();
                SetStatus(L("ui.avatar.applied"));
            }
        }

        [Serializable]
        private sealed class SkinJson
        {
            public string key, name, skin, torso, arms, legs;
        }

        private void Export()
        {
            string key = Slug(_name);
            if (string.IsNullOrEmpty(key))
            {
                SetStatus(L("ui.avatar.need_name"));
                return;
            }

            var skin = new SkinJson
            {
                key = key,
                name = _name,
                skin = Hex(_col[0]),
                torso = Hex(_col[1]),
                arms = Hex(_col[2]),
                legs = Hex(_col[3]),
            };

            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "avatar_exports", key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "skin.json"), JsonUtility.ToJson(skin, true));
                SetStatus($"{L("ui.avatar.exported")}\n{dir}");
            }
            catch (Exception e)
            {
                SetStatus(L("ui.editor.export_failed") + " " + e.Message);
            }
        }

        private void SetStatus(string text)
        {
            if (_status != null)
            {
                _status.text = text;
            }
        }

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        // --- helpers ---

        private static string Hex(Color c)
            => $"#{Mathf.RoundToInt(c.r * 255f):X2}{Mathf.RoundToInt(c.g * 255f):X2}{Mathf.RoundToInt(c.b * 255f):X2}";

        private static string Slug(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder();
            foreach (char c in s.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_') sb.Append('_');
            }

            return sb.ToString();
        }

        private string L(string key) => Shell?.L(key) ?? key;

        private static RectTransform RightPanel(Transform root, float w, float h)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(-16f, -16f);
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.PanelSprite;
            img.type = Image.Type.Sliced;
            img.color = UiKit.PanelFill;
            return rt;
        }
    }
}
