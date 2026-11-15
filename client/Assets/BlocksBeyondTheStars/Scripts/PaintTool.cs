// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The block-paint tool host (#818): right-clicking with the held <c>paint_tool</c> on a placed block
    /// opens the shared <see cref="FaceEditor"/> at 32×32, pre-loaded with the block's current design, plus
    /// the local design library card (keep + reuse, #820). Since the editor shell (#3693) it is a full editor frame:
    /// Back top left, Undo / Redo, and Save ("on this block") sends a <c>PaintBlockIntent</c>; the server
    /// registers/dedups the bitmap and answers through the ordinary block-change path. Esc / pad B go through
    /// <c>GameMenu</c> to the shell (#3690), which closes an armed tool, never the editor. Player control freezes + the
    /// cursor frees via the same menu-owner arbiter the beacon naming overlay uses.
    /// </summary>
    public sealed class PaintToolUi : MonoBehaviour
    {
        public GameBootstrap Game;

        private FaceEditor _editor;

        private void OnDestroy()
        {
            if (_editor != null)
            {
                Destroy(_editor.gameObject);
            }
        }

        /// <summary>True while the paint editor is open (gameplay hotkeys should stand down).</summary>
        public bool IsOpen => _editor != null;

        /// <summary>Opens the paint editor for a world cell, pre-loaded with its current design (if any).</summary>
        public void OpenFor(Vector3Int cell)
        {
            if (_editor != null || Game == null)
            {
                return;
            }

            string current = string.Empty, currentName = string.Empty;
            int designId = ShapeCode.DesignOf(Game.World?.GetShape(cell.x, cell.y, cell.z) ?? 0);
            if (designId != 0 && Game.PaintAtlas != null && Game.PaintAtlas.TryGetPixels(designId, out var pixels))
            {
                current = pixels;
                // Copying someone else's design keeps their name on it (#846) — the same attribution a
                // copied FORM carries.
                string owner = Game.PaintAtlas.OwnerOf(designId);
                if (!string.IsNullOrEmpty(owner))
                {
                    currentName = string.Format(L("ui.paint.copied_from"), L("ui.paint.title"), owner);
                }
            }

            var go = new GameObject("PaintEditor");
            _editor = go.AddComponent<FaceEditor>();
            _editor.GridSize = PaintDesignAtlas.Size;
            _editor.InitialFace = current;
            _editor.InitialName = currentName;
            // Unpainted pixels become the design's paper canvas on the block, not the editor's dark grey —
            // so the canvas shows what the block will look like (#899).
            _editor.BaseColor = PaintDesignAtlas.Canvas;
            ConfigureFrame(_editor, cell);
            _editor.OnSaveDesign = PaintLibrary.Save;
            _editor.LibraryProvider = PaintLibrary.List;
            if (EditorShell.DesktopFeatures)
            {
                // The share code lives on the clipboard, which the browser build cannot reach — there the buttons go.
                _editor.OnShare = CopyCode;
                _editor.OnImport = PasteCode;
            }

            _editor.OnClosed = () => { _editor = null; Game?.SetMenuOwner(this, false); };
            Game.SetMenuOwner(this, true); // freezes player control + frees the cursor via the arbiter (#413)
        }

        /// <summary>The shell frame: the paint texts, the draft slot, Save = paint the block.</summary>
        private void ConfigureFrame(FaceEditor editor, Vector3Int cell)
        {
            // The shared editor names the face's title; the paint tool reads its own.
            editor.Localizer = key => Game.Localizer?.Get(key == "ui.face.title" ? "ui.paint.title" : key) ?? key;
            editor.EditorKey = "paint";
            editor.ShellTitleKey = "ui.paint.title";
            editor.SaveSublineKey = "ui.editor.save_sub.paint";
            editor.AboutKey = "ui.paint.about";
            editor.OnApply = encoded =>
            {
                // An all-empty canvas clears the paint (the server treats empty as "remove the design").
                string send = FacePalette.IsEmpty(encoded) ? string.Empty : encoded;
                Game.Network?.SendPaintBlock(cell.x, cell.y, cell.z, send);
            };
        }

        /// <summary>Copy code: the design as a share code on the clipboard.</summary>
        private void CopyCode(string pixels, string name)
        {
            GUIUtility.systemCopyBuffer = ShareCode.EncodeDesign(pixels, name);
            _editor?.ShowStatus(L("ui.paint.code_copied"), EditorStatusSeverity.Success);
        }

        /// <summary>Paste code: a share code from the clipboard goes into the library (the editor then puts it on the
        /// canvas). Null when the clipboard holds none.</summary>
        private (string Pixels, string Name)? PasteCode()
        {
            if (!ShareCode.TryDecodeDesign(GUIUtility.systemCopyBuffer, PaintDesignAtlas.PixelChars, out string px, out string nm))
            {
                _editor?.ShowStatus(L("ui.paint.import_failed"), EditorStatusSeverity.Warning);
                return null;
            }

            PaintLibrary.Save(px, nm);
            _editor?.ShowStatus(L("ui.paint.imported"), EditorStatusSeverity.Info);
            return (px, nm);
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;
    }

    /// <summary>
    /// The client-local paint-design library (#820): one small JSON per saved design under
    /// <c>persistentDataPath/paint_designs/</c> — the <c>ship_exports</c>/<c>avatar_exports</c> pattern.
    /// Client-side only and world-independent: applying a saved design in another world just sends its
    /// pixels; the server dedups them into that world's registry.
    /// </summary>
    internal static class PaintLibrary
    {
        [System.Serializable]
        private sealed class Entry
        {
            public string name;
            public string pixels;
        }

        private static string Dir => Path.Combine(AppPaths.Root, "paint_designs");

        /// <summary>Saves the canvas under the player's name for it (#846); an unnamed save still falls back
        /// to the old "Design NN" numbering. Saving the same NAME again overwrites that entry, so refining a
        /// design does not leave a trail of near-duplicates. Empty canvases are ignored.</summary>
        public static void Save(string pixels, string name)
        {
            if (FacePalette.IsEmpty(pixels))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Dir);
                string clean = (name ?? string.Empty).Trim();
                if (clean.Length > 16)
                {
                    clean = clean.Substring(0, 16).Trim();
                }

                string file;
                if (clean.Length == 0)
                {
                    int n = 1;
                    while (File.Exists(Path.Combine(Dir, $"design-{n:00}.json")))
                    {
                        n++;
                    }

                    clean = $"Design {n:00}";
                    file = Path.Combine(Dir, $"design-{n:00}.json");
                }
                else
                {
                    var safe = new string(clean.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
                    file = Path.Combine(Dir, safe + ".json");
                }

                File.WriteAllText(file, JsonUtility.ToJson(new Entry { name = clean, pixels = pixels }));
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Paint] Could not save the design: {ex.Message}");
            }
        }

        /// <summary>All saved designs, name-sorted. Corrupt files are skipped, never thrown.</summary>
        public static List<(string Name, string Pixels)> List()
        {
            var result = new List<(string, string)>();
            try
            {
                if (!Directory.Exists(Dir))
                {
                    return result;
                }

                foreach (var file in Directory.GetFiles(Dir, "*.json").OrderBy(f => f))
                {
                    try
                    {
                        var entry = JsonUtility.FromJson<Entry>(File.ReadAllText(file));
                        if (entry != null && !string.IsNullOrEmpty(entry.pixels))
                        {
                            result.Add((string.IsNullOrEmpty(entry.name) ? Path.GetFileNameWithoutExtension(file) : entry.name, entry.pixels));
                        }
                    }
                    catch
                    {
                        // skip corrupt entry
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Paint] Could not list the design library: {ex.Message}");
            }

            return result;
        }
    }
}
