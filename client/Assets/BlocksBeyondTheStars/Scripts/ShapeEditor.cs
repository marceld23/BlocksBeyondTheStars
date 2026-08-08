// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The in-game form designer (#845) — the 3-D sibling of <see cref="FaceEditor"/>. Where the face/paint
    /// editor paints one grid of pixels, this one sculpts a block out of micro cubes, ONE LAYER AT A TIME:
    /// the canvas is a single horizontal slice you paint exactly like a pixel grid, and the layer selector
    /// walks up and down the block. A live 3-D preview to the right shows what the finished form looks like.
    ///
    /// It deliberately does NOT reuse FaceEditor: the two share a look, not a model (pixels vs. layers), and
    /// folding a third dimension into that class would have made the simple paint host harder to read. What
    /// they do share is the shell — the modal overlay, the library column, the host hooks — so the two feel
    /// like the same tool to a player.
    /// </summary>
    public sealed class ShapeEditor : MonoBehaviour
    {
        // Host-supplied hooks, mirroring FaceEditor's.
        public string InitialVoxels;                     // form to preload (empty = start from scratch)
        public string InitialName;                       // its name, when editing/copying an existing form
        public Func<string, string> Localizer;
        public Action<string, string> OnApply;           // (voxels, name) — the host crafts or keeps it
        public string ApplyLabelKey = "ui.shape.custom.craft"; // hosts differ: craft from the menu, keep from the tool
        public Action<string, string> OnSaveDesign;      // (voxels, name) — save to the local library
        public Func<List<(string Name, string Voxels)>> LibraryProvider;
        public Action OnClosed;
        public GameBootstrap Game;                       // for the 3-D preview material/atlas (may be null)

        private int _grid = CustomShape.GridLarge;
        private char[] _cells;                           // _grid³ micro cells, '0' = empty
        private int _layer;                              // current Y slice
        private string _name = string.Empty;

        private Texture2D _tex;
        private RectTransform _canvasRt;
        private Canvas _ui;
        private Text _layerLabel, _budgetLabel;
        private RectTransform _libList;
        private GameObject _previewRoot;
        private Transform _previewSpin;

        private void Start()
        {
            LoadFrom(InitialVoxels, InitialName);
            BuildUi();
        }

        private void OnDestroy()
        {
            if (_ui != null) Destroy(_ui.gameObject);
            if (_tex != null) Destroy(_tex);
            if (_previewRoot != null) Destroy(_previewRoot);
            OnClosed?.Invoke();
        }

        // ── model ────────────────────────────────────────────────────────────────────────────────

        private int Index(int x, int y, int z) => CustomShape.IndexOf(x, y, z, _grid);

        private bool Filled(int x, int y, int z) => _cells[Index(x, y, z)] != '0';

        private string Encode() => new string(_cells);

        private void LoadFrom(string voxels, string name)
        {
            int grid = CustomShape.GridOf(voxels);
            _grid = grid == 0 ? CustomShape.GridLarge : grid;
            _cells = new char[_grid * _grid * _grid];
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = grid == 0 ? '0' : voxels[i];
            }

            _layer = 0;
            _name = name ?? string.Empty;
            RebuildTexture();
            RenderLayer();
            RefreshPreview();
        }

        /// <summary>Switches the grid resolution, re-sampling what is already drawn so a form sketched at 4³
        /// can be refined at 8³ (and the other way round, coarsely) instead of starting over.</summary>
        private void SetGrid(int grid)
        {
            if (grid == _grid)
            {
                return;
            }

            var old = _cells;
            int oldGrid = _grid;
            var next = new char[grid * grid * grid];
            for (int y = 0; y < grid; y++)
            {
                for (int z = 0; z < grid; z++)
                {
                    for (int x = 0; x < grid; x++)
                    {
                        int sx = x * oldGrid / grid, sy = y * oldGrid / grid, sz = z * oldGrid / grid;
                        next[CustomShape.IndexOf(x, y, z, grid)] = old[CustomShape.IndexOf(sx, sy, sz, oldGrid)];
                    }
                }
            }

            _grid = grid;
            _cells = next;
            _layer = Mathf.Clamp(_layer, 0, _grid - 1);
            RebuildTexture();
            RenderLayer();
            RefreshPreview();
            UpdateLabels();
        }

        // ── painting ─────────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_canvasRt == null) return;

            bool left = Input.GetMouseButton(0), right = Input.GetMouseButton(1);
            if (!left && !right) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_canvasRt, Input.mousePosition, null)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, Input.mousePosition, null, out var lp)) return;

            // Place() anchors the rect top-left with pivot (0,1): local x∈[0,w], y∈[-h,0]. The canvas shows the
            // layer from ABOVE, so the top row is +Z (away from the player) — the same as looking down at it.
            float w = _canvasRt.rect.width, h = _canvasRt.rect.height;
            int gx = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(lp.x / w) * _grid), 0, _grid - 1);
            int fromTop = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(-lp.y / h) * _grid), 0, _grid - 1);
            int gz = _grid - 1 - fromTop;

            Paint(gx, gz, right ? '0' : '1');
        }

        private void Paint(int x, int z, char value)
        {
            int cell = Index(x, _layer, z);
            if (_cells[cell] == value)
            {
                return;
            }

            _cells[cell] = value;
            RenderLayer();
            RefreshPreview();
            UpdateLabels();
        }

        // ── canvas rendering ─────────────────────────────────────────────────────────────────────

        private void RebuildTexture()
        {
            if (_tex != null)
            {
                Destroy(_tex);
            }

            _tex = new Texture2D(_grid, _grid, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
        }

        /// <summary>Draws the current layer, with the layer BELOW showing through dimly — that ghost is what
        /// makes stacking layers into a shape possible without constantly flipping back and forth.</summary>
        private void RenderLayer()
        {
            for (int z = 0; z < _grid; z++)
            {
                for (int x = 0; x < _grid; x++)
                {
                    Color c;
                    if (Filled(x, _layer, z))
                    {
                        c = UiKit.Cyan;
                    }
                    else if (_layer > 0 && Filled(x, _layer - 1, z))
                    {
                        c = new Color(UiKit.Cyan.r * 0.32f, UiKit.Cyan.g * 0.32f, UiKit.Cyan.b * 0.32f, 1f);
                    }
                    else
                    {
                        c = FacePalette.EditorBackground;
                    }

                    // Texture row 0 is the BOTTOM; grid z 0 is the near edge, drawn at the bottom of the canvas.
                    _tex.SetPixel(x, z, c);
                }
            }

            _tex.Apply();
        }

        // ── 3-D preview ──────────────────────────────────────────────────────────────────────────

        /// <summary>Rebuilds the little rotating preview from the SAME geometry the world mesher would emit,
        /// so what the player sees here is what they will place.</summary>
        private void RefreshPreview()
        {
            if (_previewSpin == null)
            {
                return;
            }

            for (int i = _previewSpin.childCount - 1; i >= 0; i--)
            {
                Destroy(_previewSpin.GetChild(i).gameObject);
            }

            string voxels = Encode();
            if (!CustomShape.IsValidVoxels(voxels))
            {
                return;
            }

            var mesh = EditorVoxelPreview.CustomShapeMesh(voxels);
            if (mesh == null)
            {
                return;
            }

            var go = new GameObject("FormPreview", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_previewSpin, false);
            go.transform.localPosition = new Vector3(-0.5f, -0.5f, -0.5f); // centre the unit cell on the pivot
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = EditorVoxelPreview.PreviewMaterial();
            go.layer = _previewSpin.gameObject.layer;
        }

        private void UpdateLabels()
        {
            if (_layerLabel != null)
            {
                _layerLabel.text = string.Format(L("ui.shape.custom.layer"), _layer + 1, _grid);
            }

            if (_budgetLabel != null)
            {
                int boxes = CustomShape.Merge(Encode()).Count;
                _budgetLabel.text = string.Format(L("ui.shape.custom.budget"), boxes, CustomShape.MaxBoxes);
                _budgetLabel.color = boxes > CustomShape.MaxBoxes ? new Color(1f, 0.45f, 0.35f) : UiKit.CyanDim;
            }
        }

        // ── UI ───────────────────────────────────────────────────────────────────────────────────

        private void BuildUi()
        {
            _ui = UiKit.CreateCanvas("Shape Editor UI");
            _ui.sortingOrder = 60; // above the in-game menu, like the paint editor
            var root = _ui.transform;

            const float panelW = 1180f;
            var (_, panel) = UiKit.AddModalOverlay(root, 370f, 40f, panelW, 990f);
            UiKit.AddText(panel, 24f, 18f, panelW - 48f, 30f, L("ui.shape.custom.title"), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Layer canvas.
            var canvasGo = new GameObject("LayerCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(panel, false);
            _canvasRt = UiKit.Place(canvasGo, 24f, 100f, 480f, 480f);
            canvasGo.AddComponent<RawImage>().texture = _tex;

            _layerLabel = UiKit.AddText(panel, 24f, 60f, 300f, 30f, string.Empty, 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.AddButton(panel, 330f, 56f, 80f, 38f, "▲", () => StepLayer(1));
            UiKit.AddButton(panel, 420f, 56f, 80f, 38f, "▼", () => StepLayer(-1));

            // Authoring helpers — the difference between "possible" and "pleasant".
            float hx = 24f, hy = 596f;
            UiKit.AddButton(panel, hx, hy, 232f, 44f, L("ui.shape.custom.copy_below"), CopyLayerBelow);
            UiKit.AddButton(panel, hx + 248f, hy, 232f, 44f, L("ui.shape.custom.mirror_x"), () => Mirror(mirrorX: true));
            UiKit.AddButton(panel, hx, hy + 52f, 232f, 44f, L("ui.shape.custom.mirror_z"), () => Mirror(mirrorX: false));
            UiKit.AddButton(panel, hx + 248f, hy + 52f, 232f, 44f, L("ui.shape.custom.clear_layer"), ClearLayer);
            UiKit.AddButton(panel, hx, hy + 104f, 232f, 44f, L("ui.shape.custom.clear_all"), ClearAll);
            UiKit.AddButton(panel, hx + 248f, hy + 104f, 232f, 44f, L("ui.shape.custom.grid_toggle"),
                () => SetGrid(_grid == CustomShape.GridLarge ? CustomShape.GridSmall : CustomShape.GridLarge));

            _budgetLabel = UiKit.AddText(panel, 24f, 760f, 480f, 26f, string.Empty, 15, UiKit.CyanDim, TextAnchor.MiddleLeft);

            // Name + actions.
            UiKit.AddText(panel, 24f, 800f, 480f, 24f, L("ui.shape.custom.name"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddInput(panel, 24f, 828f, 480f, 46f, _name, v => _name = v, L("ui.shape.custom.name_hint"), 24);

            UiKit.AddButton(panel, 24f, 890f, 232f, 56f, L(ApplyLabelKey), Apply);
            UiKit.AddButton(panel, 272f, 890f, 232f, 56f, L("ui.menu.back"), Close);

            // 3-D preview.
            UiKit.AddText(panel, 530f, 76f, 380f, 24f, L("ui.shape.custom.preview"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            BuildPreview(panel);

            // Library column — the same idea as the paint editor's "my designs".
            UiKit.AddText(panel, 930f, 76f, 226f, 24f, L("ui.shape.custom.library"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            if (OnSaveDesign != null)
            {
                UiKit.AddButton(panel, 930f, 104f, 226f, 48f, L("ui.shape.custom.save"), () =>
                {
                    OnSaveDesign(Encode(), _name);
                    RebuildLibraryList();
                });
            }

            // Sharing (#846): a form travels as a short code the player can paste anywhere — the library
            // files are local, this is what crosses machines.
            UiKit.AddButton(panel, 930f, 158f, 110f, 40f, L("ui.shape.custom.export"), ExportCode);
            UiKit.AddButton(panel, 1046f, 158f, 110f, 40f, L("ui.shape.custom.import"), ImportCode);

            var listGo = new GameObject("FormLibraryList", typeof(RectTransform));
            listGo.transform.SetParent(panel, false);
            _libList = UiKit.Place(listGo, 930f, 210f, 226f, 660f);
            RebuildLibraryList();

            UiKit.AddText(panel, 24f, 952f, panelW - 48f, 24f, L("ui.shape.custom.hint"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UpdateLabels();
        }

        /// <summary>A tiny render-texture stage: an orthographic camera on its own layer looking at the form,
        /// so the preview cannot pick up the world behind the menu.</summary>
        private void BuildPreview(Transform panel)
        {
            var rt = new RenderTexture(360, 360, 16) { name = "FormPreviewRT" };
            _previewRoot = new GameObject("FormPreviewStage");
            _previewRoot.transform.position = new Vector3(0f, -5000f, 0f); // parked far from the world
            _previewSpin = new GameObject("Spin").transform;
            _previewSpin.SetParent(_previewRoot.transform, false);

            var camGo = new GameObject("FormPreviewCam", typeof(Camera));
            camGo.transform.SetParent(_previewRoot.transform, false);
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 0.95f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.07f, 0.14f, 1f);
            cam.targetTexture = rt;
            camGo.transform.localPosition = new Vector3(2.2f, 1.7f, -2.2f);
            camGo.transform.LookAt(_previewRoot.transform.position);

            var viewGo = new GameObject("FormPreviewView", typeof(RectTransform));
            viewGo.transform.SetParent(panel, false);
            UiKit.Place(viewGo, 530f, 104f, 360f, 360f);
            viewGo.AddComponent<RawImage>().texture = rt;

            RefreshPreview();
        }

        private void Update_SpinPreview()
        {
            if (_previewSpin != null)
            {
                _previewSpin.Rotate(Vector3.up, 28f * Time.unscaledDeltaTime, Space.Self);
            }
        }

        private void LateUpdate() => Update_SpinPreview();

        private void StepLayer(int delta)
        {
            _layer = Mathf.Clamp(_layer + delta, 0, _grid - 1);
            RenderLayer();
            UpdateLabels();
        }

        private void CopyLayerBelow()
        {
            if (_layer == 0)
            {
                return;
            }

            for (int z = 0; z < _grid; z++)
            {
                for (int x = 0; x < _grid; x++)
                {
                    _cells[Index(x, _layer, z)] = _cells[Index(x, _layer - 1, z)];
                }
            }

            RenderLayer();
            RefreshPreview();
            UpdateLabels();
        }

        /// <summary>Mirrors the WHOLE form (not just this layer) — symmetry is what most hand-built forms want,
        /// and doing it per layer would leave the other layers behind.</summary>
        private void Mirror(bool mirrorX)
        {
            var next = (char[])_cells.Clone();
            for (int y = 0; y < _grid; y++)
            {
                for (int z = 0; z < _grid; z++)
                {
                    for (int x = 0; x < _grid; x++)
                    {
                        int sx = mirrorX ? _grid - 1 - x : x;
                        int sz = mirrorX ? z : _grid - 1 - z;
                        if (_cells[Index(sx, y, sz)] != '0')
                        {
                            next[Index(x, y, z)] = _cells[Index(sx, y, sz)];
                        }
                    }
                }
            }

            _cells = next;
            RenderLayer();
            RefreshPreview();
            UpdateLabels();
        }

        private void ClearLayer()
        {
            for (int z = 0; z < _grid; z++)
            {
                for (int x = 0; x < _grid; x++)
                {
                    _cells[Index(x, _layer, z)] = '0';
                }
            }

            RenderLayer();
            RefreshPreview();
            UpdateLabels();
        }

        private void ClearAll()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = '0';
            }

            RenderLayer();
            RefreshPreview();
            UpdateLabels();
        }

        private void RebuildLibraryList()
        {
            if (_libList == null || LibraryProvider == null)
            {
                return;
            }

            for (int i = _libList.childCount - 1; i >= 0; i--)
            {
                Destroy(_libList.GetChild(i).gameObject);
            }

            var entries = LibraryProvider() ?? new List<(string, string)>();
            const int maxShown = 14;
            float y = 0f;
            for (int i = 0; i < entries.Count && i < maxShown; i++)
            {
                var entry = entries[i];
                UiKit.AddButton(_libList, 0f, y, 226f, 42f, entry.Name, () => LoadFrom(entry.Voxels, entry.Name));
                y += 50f;
            }
        }

        /// <summary>Puts the current form on the clipboard as a share code (#846).</summary>
        private void ExportCode()
        {
            string voxels = Encode();
            if (!CustomShape.IsValidVoxels(voxels))
            {
                return;
            }

            GUIUtility.systemCopyBuffer = ShareCode.EncodeForm(voxels, _name);
            Game?.ShowMessage(L("ui.shape.custom.exported"));
        }

        /// <summary>Loads a share code off the clipboard onto the canvas — validated exactly like the server
        /// validates a form, so a mistyped or hostile string can never reach the registry.</summary>
        private void ImportCode()
        {
            if (ShareCode.TryDecodeForm(GUIUtility.systemCopyBuffer, out string voxels, out string name))
            {
                LoadFrom(voxels, name);
                CustomShapeLibrary.Save(voxels, name);
                Game?.ShowMessage(L("ui.shape.custom.imported"));
                RebuildLibraryList();
                UpdateLabels();
            }
            else
            {
                Game?.ShowMessage(L("ui.shape.custom.import_failed"));
            }
        }

        private void Apply()
        {
            string voxels = Encode();
            if (!CustomShape.IsValidVoxels(voxels) || !CustomShape.FitsBudget(voxels))
            {
                UpdateLabels(); // the budget line turns red / the form is empty — nothing to craft
                return;
            }

            OnApply?.Invoke(voxels, _name);
            Close();
        }

        private void Close() => Destroy(gameObject);

        private string L(string key) => Localizer?.Invoke(key) ?? key;
    }

    /// <summary>
    /// The client-local form library (#845): one small JSON per saved form under
    /// <c>persistentDataPath/custom_shapes/</c> — the <c>paint_designs</c> pattern, with the player-chosen
    /// NAME that library never had. World-independent by construction: only the voxels travel, the registered
    /// shape index belongs to whichever save it was crafted in.
    /// </summary>
    internal static class CustomShapeLibrary
    {
        [Serializable]
        private sealed class Entry
        {
            public string name;
            public string voxels;
        }

        private static string Dir => Path.Combine(Application.persistentDataPath, "custom_shapes");

        /// <summary>Saves a form under the player's name for it (a nameless save falls back to "Form N").
        /// Saving the same NAME again overwrites that entry — editing a form and saving it should not leave
        /// a trail of near-duplicates.</summary>
        public static void Save(string voxels, string name)
        {
            if (!CustomShape.IsValidVoxels(voxels))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Dir);
                string clean = Sanitize(name);
                string file = Path.Combine(Dir, FileNameFor(clean));
                if (string.IsNullOrEmpty(clean))
                {
                    int n = 1;
                    while (File.Exists(Path.Combine(Dir, $"form-{n:00}.json")))
                    {
                        n++;
                    }

                    clean = $"Form {n:00}";
                    file = Path.Combine(Dir, $"form-{n:00}.json");
                }

                File.WriteAllText(file, JsonUtility.ToJson(new Entry { name = clean, voxels = voxels }));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Forms] Could not save the form: {ex.Message}");
            }
        }

        /// <summary>All saved forms, name-sorted. Corrupt or malformed files are skipped, never thrown.</summary>
        public static List<(string Name, string Voxels)> List()
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
                        if (entry != null && CustomShape.IsValidVoxels(entry.voxels))
                        {
                            result.Add((string.IsNullOrEmpty(entry.name) ? Path.GetFileNameWithoutExtension(file) : entry.name, entry.voxels));
                        }
                    }
                    catch
                    {
                        // skip corrupt entry
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Forms] Could not list the form library: {ex.Message}");
            }

            return result;
        }

        /// <summary>Removes a saved form by name (no-op when it is not there).</summary>
        public static void Delete(string name)
        {
            try
            {
                string file = Path.Combine(Dir, FileNameFor(Sanitize(name)));
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Forms] Could not delete the form: {ex.Message}");
            }
        }

        private static string Sanitize(string name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            return trimmed.Length > 24 ? trimmed.Substring(0, 24).Trim() : trimmed;
        }

        /// <summary>A file name derived from the form name — same name, same file, so re-saving overwrites.</summary>
        private static string FileNameFor(string name)
        {
            var safe = new string((name ?? string.Empty).Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
            return (string.IsNullOrEmpty(safe) ? "form" : safe) + ".json";
        }
    }
}
