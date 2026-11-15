// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Editor screenshots (#3687): launched with <c>-captureEditors</c> (plus the usual <c>-lang de|en</c> and <c>-shotOut</c>),
    /// the director skips the world entirely, goes from the main menu to the editors menu and opens every creative editor in
    /// turn — through the same <see cref="AppShell"/> open/close methods the menu cards and Back buttons use — with a shipped
    /// template or an in-memory sample on its table, then writes one PNG per editor and per main view into
    /// <c>&lt;out&gt;/editors/NN-&lt;editor&gt;[-&lt;view&gt;].png</c> and quits. The point is before/after pictures of an editor
    /// overhaul without clicking.
    /// <para>
    /// Nothing is saved: the samples are loaded the way a load-dialog row loads them, no Save/Apply/Export is ever pressed, and
    /// the two in-game editors (shape designer, paint tool) get no-op apply/save callbacks. <see cref="CapturingEditors"/> tells
    /// any editor that would write on its own (a draft autosave) to stand down. The capture language is set only on the way out
    /// of the main menu, so the menu-phase quality calibration can never save it into the player's settings.
    /// </para>
    /// <para>
    /// The hooks it needs inside the editors are tiny <c>Capture*</c> methods (load a shipped template, open a picker or the
    /// HUD mode): <c>ShipEditor</c>, <c>StructureEditor</c>, <c>AvatarEditor</c>, <c>FaceEditor.HudMode</c>, <c>FormEditor</c>.
    /// Desktop only — the browser build has no editors in its menu, so the switch is inert there.
    /// </para>
    /// </summary>
    public sealed partial class ScreenshotDirector
    {
        private const float EditorSettle = 2.5f;          // after an editor opens: atlas, voxel meshes, fonts, the first preview frames
        private const float ViewSettle = 1.2f;            // after a picker or mode inside an open editor
        private const float EditorMenuTimeout = 60f;      // give the splash chain this long to reach the main menu
        private const float EditorCaptureTimeout = 300f;  // the whole run; a stuck editor must not keep the process alive

        // The samples: preferred shipped keys first, else the first whole template / laid-out ship by key.
        private const string SampleShipKey = "corvette";
        private static readonly string[] SampleStationKeys = { "twin_dock_bazaar", "hub_outpost" };
        private static readonly string[] SampleVillageKeys = { "river_hamlet", "walled_market" };

        private string _editorDir;

        /// <summary>True while <c>-captureEditors</c> drives the editors (#3687). The capture shows in-memory samples and must
        /// never touch the player's saved content, so an editor that writes on its own (a draft autosave) skips that while it
        /// is set. Always false in normal play and in the browser build.</summary>
        public static bool CapturingEditors { get; private set; }

        /// <summary>The <c>-captureEditors</c> switch; never on in the browser build (no editors there, no command line).</summary>
        private static bool EditorCaptureRequested()
        {
#if UNITY_WEBGL
            return false;
#else
            return Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-captureEditors", StringComparison.OrdinalIgnoreCase));
#endif
        }

        /// <summary>The whole editor run, in the order of the editors menu's cards, then the two in-game editors. Quits.</summary>
        private IEnumerator CaptureEditors(AppShell shell)
        {
            CapturingEditors = true;
            StartCoroutine(EditorCaptureWatchdog());
            yield return OpenEditorsMenu(shell);
            if (!shell.ContentReady || shell.Phase != ShellPhase.Editors)
            {
                Debug.LogError($"[Capture] The editors menu did not open (phase {shell.Phase}, content ready {shell.ContentReady}).");
                Quit(1);
                yield break;
            }

            _editorDir = Path.Combine(ResolveOutDir(), "editors");
            Directory.CreateDirectory(_editorDir);
            Debug.Log($"[Capture] Editors: lang={_lang} out={_editorDir}");

            yield return EditorShot("00-editors", EditorSettle);
            yield return ShipEditorShots(shell);
            yield return AvatarEditorShots(shell);
            yield return StructureEditorShots(shell, StructureEditor.Mode.Station, "03-station");
            yield return StructureEditorShots(shell, StructureEditor.Mode.Settlement, "04-village");
            yield return SimpleEditorShot(shell.OpenTextureEditor, shell.CloseTextureEditor, "05-texture");
            yield return FormEditorShot(shell);
            yield return SimpleEditorShot(shell.OpenToolLookEditor, shell.CloseToolLookEditor, "07-tools");
            yield return SimpleEditorShot(shell.OpenContentEditor, shell.CloseContentEditor, "08-content");
            yield return SimpleEditorShot(shell.OpenMaterialEditor, shell.CloseMaterialEditor, "09-material");
            yield return ShapeDesignerShot(shell);
            yield return PaintToolShot(shell);

            CapturingEditors = false;
            Debug.Log("[Capture] Done (editors).");
            Quit(0);
        }

        /// <summary>Waits for the main menu (or the first-launch intro, which is left without stamping it as seen), then sets
        /// the capture language and opens the editors menu in the same step — what the menu's Editors button does.</summary>
        private IEnumerator OpenEditorsMenu(AppShell shell)
        {
            bool AtMenu() => shell.ContentReady && (shell.Phase == ShellPhase.MainMenu || shell.Phase == ShellPhase.Intro);
            float t = 0f;
            while (t < EditorMenuTimeout && !AtMenu())
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!AtMenu() || shell.Settings == null)
            {
                yield break; // still in a splash: forcing the phase now would be undone by the splash's own hand-off
            }

            shell.Settings.Language = _lang;
            shell.LoadLocalizer();
            shell.GoTo(ShellPhase.Editors);
        }

        private IEnumerator EditorCaptureWatchdog()
        {
            yield return new WaitForSecondsRealtime(EditorCaptureTimeout);
            if (CapturingEditors)
            {
                Debug.LogError($"[Capture] The editor run did not finish within {EditorCaptureTimeout} s — quitting.");
                Quit(1);
            }
        }

        /// <summary>Lets the screen settle, then writes <c>editors/&lt;name&gt;.png</c>.</summary>
        private IEnumerator EditorShot(string name, float settle)
        {
            yield return new WaitForSecondsRealtime(settle);
            yield return Capture(Path.Combine(_editorDir, name + ".png"));
        }

        private static IEnumerator WaitFrames(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }
        }

        /// <summary>An editor that opens on content of its own (the texture editor on the first texture, My tools on the first
        /// tool, the developer editors on their default item / base material): open, shoot, Back.</summary>
        private IEnumerator SimpleEditorShot(Action open, Action close, string name)
        {
            open();
            yield return EditorShot(name, EditorSettle);
            close();
            yield return WaitFrames(2);
        }

        /// <summary>Ship editor: a shipped ship loaded as from the load dialog, then the load dialog and the form picker.</summary>
        private IEnumerator ShipEditorShots(AppShell shell)
        {
            shell.OpenShipEditor();
            yield return WaitFrames(2); // Start builds the room, the palette and the panels
            var editor = FindAnyObjectByType<ShipEditor>();
            var ship = SampleShip(shell.Content);
            if (editor != null && ship != null)
            {
                editor.CaptureLoadBuiltIn(ship);
            }

            yield return EditorShot("01-ship", EditorSettle);
            if (editor != null)
            {
                var load = editor.CaptureOpenLoadPicker();
                yield return EditorShot("01-ship-load", ViewSettle);
                load?.Close();
                var form = editor.CaptureOpenFormPicker();
                yield return EditorShot("01-ship-form", ViewSettle);
                form?.Close();
            }

            shell.CloseShipEditor();
            yield return WaitFrames(2);
        }

        /// <summary>Avatar designer: the player's current look, then the appearance screen over it and its HUD frame mode. The
        /// appearance screen is left through its own Back (it destroys itself) before the designer's Back.</summary>
        private IEnumerator AvatarEditorShots(AppShell shell)
        {
            shell.OpenAvatarEditor();
            yield return EditorShot("02-avatar", EditorSettle);
            var avatar = FindAnyObjectByType<AvatarEditor>();
            var face = avatar != null ? avatar.CaptureOpenAppearance() : null;
            if (face != null)
            {
                yield return EditorShot("02-avatar-appearance", EditorSettle);
            }

            if (face != null)
            {
                face.CaptureEnterHudMode();
                yield return EditorShot("02-avatar-hudframe", ViewSettle);
            }

            if (face != null)
            {
                Destroy(face.gameObject); // what the appearance screen's own Back does (FaceEditor.Close)
                yield return WaitFrames(2);
            }

            shell.CloseAvatarEditor();
            yield return WaitFrames(2);
        }

        /// <summary>Station or village editor: a shipped whole template (loaded as a copy, as from the load dialog), then the
        /// load dialog and the kit window.</summary>
        private IEnumerator StructureEditorShots(AppShell shell, StructureEditor.Mode mode, string name)
        {
            shell.OpenStructureEditor(mode);
            yield return WaitFrames(2);
            var editor = FindAnyObjectByType<StructureEditor>();
            var content = shell.Content;
            var sample = content == null ? null
                : mode == StructureEditor.Mode.Station ? SampleTemplate(content.StationTemplates, SampleStationKeys)
                : SampleTemplate(content.SettlementTemplates, SampleVillageKeys);
            if (editor != null && sample != null)
            {
                editor.CaptureLoadBuiltIn(sample);
            }

            yield return EditorShot(name, EditorSettle);
            if (editor != null)
            {
                var load = editor.CaptureOpenLoadPicker();
                yield return EditorShot(name + "-load", ViewSettle);
                load?.Close();
                var kits = editor.CaptureOpenKitPanel();
                yield return EditorShot(name + "-kits", ViewSettle);
                kits?.Close();
            }

            shell.CloseStructureEditor();
            yield return WaitFrames(2);
        }

        /// <summary>Form editor: an in-memory stair on the canvas (the editor ships no forms of its own).</summary>
        private IEnumerator FormEditorShot(AppShell shell)
        {
            shell.OpenFormEditor();
            yield return WaitFrames(2);
            var editor = FindAnyObjectByType<FormEditor>();
            if (editor != null)
            {
                editor.CaptureLoadSample(shell.L("ui.shape.stairs"), StairsVoxels());
            }

            yield return EditorShot("06-shapes", EditorSettle);
            shell.CloseFormEditor();
            yield return WaitFrames(2);
        }

        /// <summary>The shape designer is in-game only (the shape tool and the crafting tab host it), but it needs no world:
        /// opened here over the editors menu with the shape tool's own settings and the in-memory stair. Apply and save are
        /// no-ops; the library column lists the player's forms read-only. Left through its own Back (it destroys itself).</summary>
        private IEnumerator ShapeDesignerShot(AppShell shell)
        {
            var go = new GameObject("ShapeEditor");
            var editor = go.AddComponent<ShapeEditor>();
            editor.InitialVoxels = StairsVoxels();
            editor.InitialName = shell.L("ui.shape.stairs");
            editor.Localizer = shell.L;
            editor.ApplyLabelKey = string.Empty; // not the crafting host: no "Craft" button
            editor.OnApply = (_, _) => { };
            editor.OnSaveDesign = (_, _) => { };
            editor.LibraryProvider = CustomShapeLibrary.List;
            yield return EditorShot("10-shape-designer", EditorSettle);
            Destroy(go);
            yield return WaitFrames(2);
        }

        /// <summary>The block-paint editor is in-game only (PaintToolUi hosts it), but it needs no world: opened here over the
        /// editors menu with the paint tool's settings (32 × 32, paper canvas, paint texts) and an in-memory design. Apply,
        /// save, share and import are no-ops; the library column lists the player's designs read-only.</summary>
        private IEnumerator PaintToolShot(AppShell shell)
        {
            var go = new GameObject("PaintEditor");
            var editor = go.AddComponent<FaceEditor>();
            editor.GridSize = PaintDesignAtlas.Size;
            editor.InitialFace = PaintSample();
            editor.InitialName = string.Empty;
            editor.BaseColor = PaintDesignAtlas.Canvas;
            editor.Localizer = key => shell.L(key switch
            {
                "ui.face.title" => "ui.paint.title",
                _ => key,
            });
            editor.OnApply = _ => { };
            editor.OnSaveDesign = (_, _) => { };
            editor.LibraryProvider = PaintLibrary.List;
            editor.OnShare = (_, _) => { };
            editor.OnImport = () => null;
            yield return EditorShot("11-paint", EditorSettle);
            Destroy(go);
            yield return WaitFrames(2);
        }

        // ---------------------------------------------------------------- samples

        /// <summary>The preferred shipped ship, else the first one (by key) that has a layout.</summary>
        private static ShipDefinition SampleShip(GameContent content)
        {
            if (content == null)
            {
                return null;
            }

            if (content.Ships.TryGetValue(SampleShipKey, out var preferred) && content.GetShipLayout(preferred.Layout) != null)
            {
                return preferred;
            }

            var keys = new List<string>(content.Ships.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                if (content.GetShipLayout(content.Ships[key].Layout) != null)
                {
                    return content.Ships[key];
                }
            }

            return null;
        }

        /// <summary>The first preferred key that is a whole template with cells (no kit module, no generator entry), else the
        /// first such template by key.</summary>
        private static StructureTemplate SampleTemplate(IReadOnlyList<StructureTemplate> pool, string[] preferred)
        {
            var whole = new List<StructureTemplate>();
            foreach (var t in pool ?? Array.Empty<StructureTemplate>())
            {
                if (!t.IsSource && !t.IsModule && t.Cells.Count > 0)
                {
                    whole.Add(t);
                }
            }

            foreach (string key in preferred)
            {
                var hit = whole.Find(t => t.Key == key);
                if (hit != null)
                {
                    return hit;
                }
            }

            whole.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return whole.Count > 0 ? whole[0] : null;
        }

        /// <summary>A three-step stair on the fine grid (8³), 2, 5 and 8 micro cells high: the form editors' sample.</summary>
        private static string StairsVoxels()
        {
            const int grid = CustomShape.GridLarge;
            var cells = new char[grid * grid * grid];
            for (int y = 0; y < grid; y++)
            {
                for (int z = 0; z < grid; z++)
                {
                    int top = ((z * 3 / grid) + 1) * grid / 3;
                    for (int x = 0; x < grid; x++)
                    {
                        cells[CustomShape.IndexOf(x, y, z, grid)] = y < top ? '1' : '0';
                    }
                }
            }

            return new string(cells);
        }

        /// <summary>A small ringed planet among stars on the 32 × 32 paint grid: the paint editor's sample design.</summary>
        private static string PaintSample()
        {
            const int size = FacePalette.Size, mid = size / 2;
            var grid = new int[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    grid[(y * size) + x] = PlanetPixel(x - mid, y - mid);
                }
            }

            return FacePalette.Encode(grid);
        }

        /// <summary>One pixel of <see cref="PaintSample"/> (palette indices: 12 blue, 28 dark blue, 11 green, 10 yellow,
        /// 4 white, 0 transparent).</summary>
        private static int PlanetPixel(int dx, int dy)
        {
            int d2 = (dx * dx) + (dy * dy);
            if (d2 <= 81)
            {
                if (((dx - 2) * (dx - 2)) + ((dy + 3) * (dy + 3)) <= 9)
                {
                    return 11;
                }

                return dx + dy > 5 ? 28 : 12;
            }

            if (d2 <= 196 && dy == dx / 3)
            {
                return 10;
            }

            return ((dx * 7) + (dy * 13)) % 53 == 0 ? 4 : 0;
        }
    }
}
