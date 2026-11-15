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
    /// The Avatar Designer of the main menu (menu tool, sibling of <see cref="ShipEditor"/>). Since the editor shell (#3693,
    /// the approved mock's "Avatar &amp; Aussehen" view) it is ONE screen: the shared character editor
    /// (<see cref="FaceEditor"/>) with its tab bar — face, torso, arms, legs, helmet, <b>Figure</b>, HUD frame — instead of a
    /// figure screen with a second "Appearance" window over it. This class is that editor's HOST: it keeps the designer's
    /// scratch look (four colours, the face, the four paintings) read from <see cref="ClientSettings"/>, builds the Figure
    /// tab's extras (the skin's name and Export, <c>AvatarEditor.Figure.cs</c> — the tab itself, colour rows, gear preview
    /// and figure, is the editor's own), and makes the shell's <b>Save</b> write the scratch look into the settings ("on your character") —
    /// nothing reaches the in-game avatar before that. Export writes the whole skin as <c>skin.json</c>; Submit is the
    /// character editor's (#3319), which sends the same file to the developers.
    /// <para>
    /// The <b>Outfits</b> card (#1047) keeps up to <see cref="ClientSettings.MaxOutfits"/> named looks in the settings file:
    /// "Save outfit" stores the scratch look under the typed name, "Wear" loads one into the designer, the red ✕ deletes
    /// it (after asking). None of that touches the in-game avatar — only Save does — so the server never learns about
    /// outfits; it only ever sees the saved look on the next join.
    /// </para>
    /// </summary>
    public sealed partial class AvatarEditor : MonoBehaviour
    {
        public AppShell Shell;

        private readonly Color[] _col = new Color[4]; // skin, torso, arms, legs
        private string _name = string.Empty; // the localized default is set in Start (#3263)
        private string _face = string.Empty; // encoded pixel face (same format as the in-game editor)
        private readonly string[] _bodyPaint = new string[BodyPaintKit.PartCount]; // body paintings (#874)
        private FaceEditor _faceEditor;

        private void Start()
        {
            var s = Shell?.Settings;
            _name = L("ui.ed.default.skin");
            _col[0] = s?.SkinColor ?? AppearancePalette.Colors[0];
            _col[1] = s?.TorsoColor ?? AppearancePalette.Colors[4];
            _col[2] = s?.ArmColor ?? AppearancePalette.Colors[4];
            _col[3] = s?.LegColor ?? AppearancePalette.Colors[8];
            _face = s?.FacePixels ?? string.Empty;
            for (int part = 0; part < _bodyPaint.Length; part++)
            {
                _bodyPaint[part] = s?.GetBodyPaint(part) ?? string.Empty;
            }

            OpenAppearanceEditor();
        }

        /// <summary>Opens the character editor as this designer's whole screen — face, torso, arms, legs and helmet as
        /// tabs, each with its base colour, plus the Figure tab and the HUD frame. Edits land in the scratch values; the
        /// shell's Save is what writes them into <see cref="ClientSettings"/>, so leaving without saving changes nothing
        /// (the shell keeps a draft instead).</summary>
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
                () => _face, SetFace, part => _bodyPaint[part], SetBodyPaint, which => _col[which], SetColor);
            _faceEditor.PreviewState = Look;
            _faceEditor.OutfitNames = OutfitNames;
            _faceEditor.OnWearOutfit = LoadOutfit;
            _faceEditor.OnSaveOutfit = SaveOutfitNamed;
            _faceEditor.OnRenameOutfit = RenameOutfitAt;
            _faceEditor.OnDeleteOutfit = DeleteOutfit;
            ConfigureTemplatesAndHud(_faceEditor); // #3318/#3319/#3321: free skins, the HUD mode, the Submit button
            ConfigureShell(_faceEditor);
            _faceEditor.OnClosed = () => _faceEditor = null;
            _faceEditor.Begin(); // now, not next frame: the capture director and the tests talk to it at once
        }

        /// <summary>The designer's frame on the shell: its title, its draft slot, Save = <see cref="Apply"/>, Back to the
        /// editors' hub, the Figure tab.</summary>
        private void ConfigureShell(FaceEditor editor)
        {
            editor.EditorKey = "avatar";
            editor.ShellTitleKey = "ui.menu.avatar_editor";
            editor.SaveSublineKey = "ui.editor.save_sub.avatar";
            editor.KeepsScratch = true;
            editor.OnSaveAll = Apply;
            editor.OnBack = Leave;
            editor.FigureTabKey = "ui.avatar.figure_tab";
            editor.BuildFigureExtras = BuildFigureExtras; // the skin's name and Export under the tab's own rows
            editor.HostDraft = () => _name;
            editor.RestoreHostDraft = name =>
            {
                _name = name ?? string.Empty;
                if (_nameInput != null)
                {
                    _nameInput.text = _name;
                }
            };
        }

        private void Leave()
        {
            if (Shell != null)
            {
                Shell.CloseAvatarEditor();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>Unattended editor screenshots (-captureEditors, ScreenshotDirector.Editors.cs, #3687): the appearance screen
        /// without a click — the designer IS that screen now, on its Face tab. Keep it working when the designer is restyled.</summary>
        internal FaceEditor CaptureOpenAppearance()
        {
            OpenAppearanceEditor();
            _faceEditor?.CaptureShowFirstTab();
            return _faceEditor;
        }

        // --- the scratch look the character editor paints into (its preview figure shows it) ---

        private void SetFace(string face) => _face = face ?? string.Empty;

        private void SetBodyPaint(int part, string pixels) => _bodyPaint[part] = pixels ?? string.Empty;

        private void SetColor(int which, Color color) => _col[which] = color;

        // --- outfits (#1047) ---

        /// <summary>The saved outfits' names, in order — what the outfits card lists.</summary>
        private List<string> OutfitNames()
        {
            var names = new List<string>();
            var outfits = Shell?.Settings?.Outfits;
            if (outfits == null)
            {
                return names;
            }

            foreach (var outfit in outfits)
            {
                names.Add(outfit.Name ?? string.Empty);
            }

            return names;
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

        /// <summary>Loads a saved outfit into the designer (scratch values, the figure and the name field) — the in-game
        /// avatar is untouched until Save, which the status line says. Returns that line for the status bar.</summary>
        private string LoadOutfit(int index)
        {
            var outfits = Shell?.Settings?.Outfits;
            if (outfits == null || index < 0 || index >= outfits.Count)
            {
                return string.Empty;
            }

            var o = outfits[index];
            PutOnScratch(o);
            _name = o.Name ?? string.Empty;
            if (_nameInput != null)
            {
                _nameInput.text = _name;
            }

            return L("ui.avatar.outfit_loaded").Replace("{name}", _name);
        }

        /// <summary>Save outfit: stores the scratch look under <paramref name="wanted"/> — overwriting an outfit that already
        /// carries that name (case-insensitive), otherwise appending a new one up to the cap.</summary>
        private string SaveOutfitNamed(string wanted)
        {
            if (Shell?.Settings is not { } s)
            {
                return string.Empty;
            }

            string name = (wanted ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return L("ui.avatar.need_name");
            }

            s.Outfits ??= new List<AvatarOutfit>();
            int existing = s.Outfits.FindIndex(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                s.Outfits[existing] = CaptureScratch(s.Outfits[existing].Name);
                s.Save();
                return L("ui.avatar.outfit_updated").Replace("{name}", s.Outfits[existing].Name);
            }

            if (s.Outfits.Count >= ClientSettings.MaxOutfits)
            {
                return L("ui.avatar.outfit_limit").Replace("{max}", ClientSettings.MaxOutfits.ToString());
            }

            s.Outfits.Add(CaptureScratch(name));
            s.Save();
            return L("ui.avatar.outfit_saved").Replace("{name}", name);
        }

        /// <summary>Rename: an outfit takes a new name (its pixels stay).</summary>
        private string RenameOutfitAt(int index, string wanted)
        {
            if (Shell?.Settings is not { } s || s.Outfits == null)
            {
                return string.Empty;
            }

            if (index < 0 || index >= s.Outfits.Count)
            {
                return L("ui.avatar.outfit_select_first");
            }

            string name = (wanted ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return L("ui.avatar.need_name");
            }

            int clash = s.Outfits.FindIndex(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
            if (clash >= 0 && clash != index)
            {
                return L("ui.avatar.outfit_name_taken").Replace("{name}", s.Outfits[clash].Name);
            }

            s.Outfits[index].Name = name;
            s.Save();
            return L("ui.avatar.outfit_renamed").Replace("{name}", name);
        }

        /// <summary>Deletes a saved outfit (the editor asked first, #3690). The saved in-game look is a separate copy, so
        /// deleting even the outfit you are wearing changes nothing in the game.</summary>
        private string DeleteOutfit(int index)
        {
            if (Shell?.Settings is not { } s || s.Outfits == null || index < 0 || index >= s.Outfits.Count)
            {
                return string.Empty;
            }

            string name = s.Outfits[index].Name;
            s.Outfits.RemoveAt(index);
            s.Save();
            return L("ui.avatar.outfit_deleted").Replace("{name}", name);
        }

        // --- save and export ---

        /// <summary>The shell's Save ("on your character"): the scratch look becomes the player's — colours, face and the
        /// four paintings in the settings, which the in-game avatar uses. False without settings to write.</summary>
        private bool Apply()
        {
            if (Shell?.Settings is not { } s)
            {
                return false;
            }

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
            return true;
        }

        private void Export()
        {
            string key = EditorSceneKit.Slug(_name);
            if (string.IsNullOrEmpty(key))
            {
                SetStatus(L("ui.avatar.need_name"), EditorStatusSeverity.Warning);
                return;
            }

            try
            {
                string dir = Path.Combine(AppPaths.Root, "avatar_exports", key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "skin.json"), SkinFile(key, pretty: true));
                SetStatus(L("ui.avatar.exported_folder"), EditorStatusSeverity.Success); // no path: the folder's name only
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarEditor] Export failed: {e}");
                SetStatus(L("ui.ed.export_failed_hint"), EditorStatusSeverity.Error); // a friendly line; the exception goes to the log
            }
        }

        private void SetStatus(string text, EditorStatusSeverity severity) => _faceEditor?.ShowStatus(text, severity);

        // --- helpers ---

        private string L(string key) => Shell?.L(key) ?? key;
    }
}
