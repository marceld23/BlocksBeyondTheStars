// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Textures;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The texture editor (#1955): opens EVERY texture of the running game — block tiles, the picture tiles of the
    /// bed and the campfire, plants, creature hides, avatar fabrics — on a true-colour canvas, shows it the way the
    /// game does (cube, built-in form with its face slots, cutout cards, tiled 3×3), and saves it three ways:
    /// <list type="bullet">
    /// <item><b>Use for me</b> — into the local texture pack (<see cref="TexturePackFolder"/>); visible at once, on
    /// this install only.</item>
    /// <item><b>Export for the game</b> — a bundle for <c>tools/merge_texture.py</c>.</item>
    /// <item><b>Publish to this world</b> — only with a world host and the right to do so (#1959).</item>
    /// </list>
    /// Everything it reads comes from the build and the loaded content, never from a developer's folders, so it works
    /// on any install. The tools themselves are <see cref="PixelCanvasModel"/> (engine-free, pinned by the headless
    /// suite); this class is the uGUI around it. Mouse and gamepad (<see cref="PadCanvasFocus"/>).
    /// </summary>
    public sealed class TextureEditor : MonoBehaviour
    {
        /// <summary>Menu host: set by <see cref="AppShell"/>.</summary>
        public AppShell Shell;

        /// <summary>What a world host (#1959) offers on top of the menu: publishing for everyone. Null in the menu.</summary>
        public ITextureEditorWorldHost WorldHost;

        /// <summary>Called when the player leaves the editor (the host destroys the component's object).</summary>
        public Action OnClose;

        private enum Tool { Brush, Eraser, Fill, Pipette, Line, Rectangle }

        private const int Tile = TextureTiles.Size;
        private const float CanvasPx = 640f;

        private GameContent _content;
        private BlockTextureAtlas _atlas;
        private List<TextureEntry> _entries;
        private readonly List<EditorPaletteKit.Entry> _rows = new List<EditorPaletteKit.Entry>();
        private PaletteListUi _list;
        private RectTransform _listContent;
        private TextureEntry _entry;
        private PixelCanvasModel _model;

        private Canvas _ui;
        private Texture2D _tex;          // the active frame, drawn on the canvas, the tiled view and the 3-D preview
        private RectTransform _canvasRt;
        private RawImage _canvasImg;
        private RectTransform _slotLayer;
        private readonly Texture2D[] _thumbs = new Texture2D[TextureTiles.MaxFrames];
        private readonly RawImage[] _thumbImgs = new RawImage[TextureTiles.MaxFrames];
        private readonly Image[] _thumbFrames = new Image[TextureTiles.MaxFrames];
        private TexturePreviewRig _preview;
        private PadCanvasFocus _pad;

        private Tool _tool = Tool.Brush;
        private int _brushSize = 1;
        private TexelColor _primary = new TexelColor(255, 255, 255);
        private TexelColor _secondary = new TexelColor(0, 0, 0);
        private float _h, _s = 0f, _v = 1f;
        private Texture2D _svTex, _hueTex;
        private RectTransform _svRt, _hueRt, _alphaRt;
        private Image _primarySwatch, _secondarySwatch;
        private InputField _hexField;
        private readonly List<Image> _tileSwatches = new List<Image>();
        private readonly List<TexelColor> _tileColors = new List<TexelColor>();
        private readonly Dictionary<Tool, Image> _toolButtons = new Dictionary<Tool, Image>();

        private bool _stroking;
        private int _lastX = -1, _lastY = -1;
        private bool _dragShape;          // line / rectangle: first corner set, waiting for release
        private int _shapeX, _shapeY;
        private bool _showSlots = true;
        private bool _showOfficial;       // "before": the canvas shows the shipped texture while the button is held
        private bool _playing;
        private float _playClock;
        private int _playFrame;
        private int _dragPicker;          // 0 none, 1 SV square, 2 hue bar, 3 alpha bar
        private string _search = string.Empty;

        private Text _title, _status, _hint, _frameLabel, _sizeLabel, _layerLabel;
        private Button _publishButton, _unpublishButton;

        private static string L(string key) => UiKit.L(key);

        // ---------------------------------------------------------------- life cycle

        private void Start()
        {
            _content = Shell != null ? Shell.Content : WorldHost?.Content;
            _atlas = _content != null ? BlockTextureAtlas.Acquire(_content) : null;
            _entries = TextureCatalog.Build(_content, key => L(key));

            _tex = NewTileTexture();
            for (int i = 0; i < _thumbs.Length; i++)
            {
                _thumbs[i] = NewTileTexture();
            }

            _model = new PixelCanvasModel(TextureAlphaMode.Opaque);
            _preview = new TexturePreviewRig(_tex);
            BuildUi();
            Open(_entries.Count > 0 ? _entries[0] : null);
            GameTextures.Changed += OnTexturesChanged;
        }

        private static Texture2D NewTileTexture()
            => new Texture2D(Tile, Tile, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat, // the tiled view samples it 3×3
                filterMode = FilterMode.Point,
            };

        private void OnDestroy()
        {
            GameTextures.Changed -= OnTexturesChanged;
            _pad?.Release();
            _preview?.Destroy();
            _atlas?.Release();
            if (_ui != null)
            {
                Destroy(_ui.gameObject);
            }

            Destroy(_tex);
            foreach (var t in _thumbs)
            {
                Destroy(t);
            }

            Destroy(_svTex);
            Destroy(_hueTex);
            Destroy(_checker);
        }

        private void OnTexturesChanged(IReadOnlyCollection<string> keys)
        {
            RefreshBadges(); // the layer of the open texture may have changed (saved, removed, world texture arrived)
        }

        // ---------------------------------------------------------------- opening a texture

        private void Open(TextureEntry entry)
        {
            _entry = entry;
            if (entry == null)
            {
                return;
            }

            var mode = TextureTiles.AlphaModeOf(entry.Key);
            var current = GameTextures.Resolve(entry.Key);
            if (current != null)
            {
                _model.Load(current.Frames, current.Fps, mode);
            }
            else if (entry.Block != null && _atlas != null)
            {
                // A block the build ships no tile for: the atlas painted it in code — start from those pixels.
                _model.Load(new[] { _atlas.ReadTile(entry.Block.NumericId.Value) }, 0, mode);
            }
            else
            {
                _model.Load(null, 0, mode);
            }

            _stroking = false;
            _dragShape = false;
            _playing = false;
            _preview.Show(entry, _content);
            RebuildSlotOverlay();
            RefreshTileColors();
            RefreshAll();
            SetStatus(string.Empty, UiKit.Ok);
        }

        /// <summary>The texture as shipped — every frame, or the code-painted tile of a block that has no file.</summary>
        private TextureFrames OfficialFrames()
        {
            if (_entry == null)
            {
                return null;
            }

            var bundled = GameTextures.Official(_entry.Key);
            if (bundled != null)
            {
                return bundled;
            }

            return _entry.Block != null && _atlas != null
                ? new TextureFrames(new[] { _atlas.OfficialTile(_entry.Block) }, 0, TextureLayer.Official)
                : null;
        }

        // ---------------------------------------------------------------- per-frame input

        private void Update()
        {
            if (_canvasRt == null || _model == null)
            {
                return;
            }

            if (_submit != null && _submit.IsOpen)
            {
                return; // the submit dialog is modal: no strokes, no shortcuts underneath
            }

            _preview.Tick(Time.unscaledDeltaTime, InputMap.ActiveDevice == InputDeviceKind.Gamepad ? InputMap.PadLookX() * 0.6f : 0f);
            TickPlayback();
            HandleKeys();

            bool pad = InputMap.ActiveDevice == InputDeviceKind.Gamepad;
            _pad.SetCells(Tile, Tile);
            bool padCanvas = _pad.Tick(pad);
            SetHint(!pad ? "ui.tex.hint" : (padCanvas ? "ui.tex.hint_pad" : "ui.tex.hint_pad_panel"));
            if (pad && InputMap.PadDown(PadButton.Rb))
            {
                DoUndo();
            }

            if (!pad && UpdateColorPicker())
            {
                return; // the mouse is dragging a colour control — not a stroke
            }

            bool primaryHeld, secondaryHeld, primaryDown, secondaryDown, pickDown, modifier;
            int x, y;
            if (padCanvas)
            {
                primaryHeld = InputMap.PadHeld(PadButton.A);
                secondaryHeld = InputMap.PadHeld(PadButton.X);
                primaryDown = InputMap.PadDown(PadButton.A);
                secondaryDown = InputMap.PadDown(PadButton.X);
                pickDown = InputMap.PadDown(PadButton.Y);
                modifier = InputMap.PadHeld(PadButton.Lb);
                x = _pad.CellX;
                y = _pad.CellY;
            }
            else
            {
                primaryHeld = Input.GetMouseButton(0);
                secondaryHeld = Input.GetMouseButton(1);
                primaryDown = Input.GetMouseButtonDown(0);
                secondaryDown = Input.GetMouseButtonDown(1);
                pickDown = Input.GetMouseButtonDown(2);
                modifier = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (!CanvasCell(out x, out y))
                {
                    if (!primaryHeld && !secondaryHeld)
                    {
                        EndStroke(-1, -1, false);
                    }

                    return;
                }

                if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                {
                    pickDown |= primaryDown;
                }
            }

            if (pickDown || (_tool == Tool.Pipette && (primaryDown || secondaryDown)))
            {
                var picked = _model.Get(x, y);
                if (secondaryDown && !pickDown)
                {
                    SetSecondary(picked);
                }
                else
                {
                    SetPrimary(picked);
                }

                return;
            }

            if (!primaryHeld && !secondaryHeld)
            {
                EndStroke(x, y, modifier);
                return;
            }

            // The right button (pad: X) always erases; the eraser tool erases with either button.
            bool erase = _tool == Tool.Eraser || (secondaryHeld && !primaryHeld);
            var color = erase ? EraseColor() : _primary;
            switch (_tool)
            {
                case Tool.Fill:
                    if (primaryDown || secondaryDown)
                    {
                        _model.Fill(x, y, color, modifier);
                        AfterChange();
                    }

                    break;

                case Tool.Line:
                case Tool.Rectangle:
                    if (primaryDown || secondaryDown)
                    {
                        _dragShape = true;
                        _shapeX = x;
                        _shapeY = y;
                    }

                    _lastX = x;
                    _lastY = y;
                    break;

                default: // brush / eraser
                    if (primaryDown || secondaryDown || !_stroking)
                    {
                        _model.BeginEdit();
                        _stroking = true;
                        _lastX = x;
                        _lastY = y;
                    }

                    _model.StrokeTo(_lastX, _lastY, x, y, color, _brushSize); // a gap-free run, erasing included

                    _lastX = x;
                    _lastY = y;
                    UploadActiveFrame();
                    break;
            }
        }

        private void EndStroke(int x, int y, bool modifier)
        {
            if (_stroking)
            {
                _stroking = false;
                _model.CommitEdit();
                AfterChange();
            }

            if (_dragShape)
            {
                _dragShape = false;
                int ex = x >= 0 ? x : _lastX, ey = y >= 0 ? y : _lastY;
                var color = _lastButtonWasSecondary || _tool == Tool.Eraser ? EraseColor() : _primary;
                if (_tool == Tool.Line)
                {
                    _model.Line(_shapeX, _shapeY, ex, ey, color, _brushSize);
                }
                else
                {
                    _model.Rectangle(_shapeX, _shapeY, ex, ey, color, filled: modifier, _brushSize);
                }

                AfterChange();
            }
        }

        private bool _lastButtonWasSecondary;
        private TextureSubmitDialog _submit;

        /// <summary>What erasing writes: see-through where the tile may have holes, the secondary colour where it may
        /// not (a block tile is opaque by rule).</summary>
        private TexelColor EraseColor()
            => _model.AlphaMode == TextureAlphaMode.Opaque ? _secondary : TexelColor.Clear;

        private void LateUpdate()
        {
            // Remember which button drives a line / rectangle drag while it is still down.
            if (Input.GetMouseButton(1) || InputMap.PadHeld(PadButton.X))
            {
                _lastButtonWasSecondary = true;
            }
            else if (Input.GetMouseButton(0) || InputMap.PadHeld(PadButton.A))
            {
                _lastButtonWasSecondary = false;
            }
        }

        private bool CanvasCell(out int x, out int y)
        {
            x = y = 0;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_canvasRt, Input.mousePosition, null)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, Input.mousePosition, null, out var lp))
            {
                return false;
            }

            // Place() anchors the rect top-left with pivot (0,1): local x∈[0,w], y∈[-h,0].
            float u = Mathf.Clamp01(lp.x / _canvasRt.rect.width), fromTop = Mathf.Clamp01(-lp.y / _canvasRt.rect.height);
            x = Mathf.Clamp((int)(u * Tile), 0, Tile - 1);
            y = Mathf.Clamp((int)(fromTop * Tile), 0, Tile - 1);
            return true;
        }

        private void HandleKeys()
        {
            if (UiKit.TextFieldFocused())
            {
                return;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (ctrl && Input.GetKeyDown(KeyCode.Z))
            {
                if (shift)
                {
                    DoRedo();
                }
                else
                {
                    DoUndo();
                }

                return;
            }

            if (ctrl && Input.GetKeyDown(KeyCode.Y))
            {
                DoRedo();
                return;
            }

            if (ctrl)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.B)) SetTool(Tool.Brush);
            else if (Input.GetKeyDown(KeyCode.E)) SetTool(Tool.Eraser);
            else if (Input.GetKeyDown(KeyCode.G)) SetTool(Tool.Fill);
            else if (Input.GetKeyDown(KeyCode.I)) SetTool(Tool.Pipette);
            else if (Input.GetKeyDown(KeyCode.L)) SetTool(Tool.Line);
            else if (Input.GetKeyDown(KeyCode.R)) SetTool(Tool.Rectangle);
            else if (Input.GetKeyDown(KeyCode.X)) SwapColors();
            else if (Input.GetKeyDown(KeyCode.LeftBracket)) SetBrushSize(_brushSize - 1);
            else if (Input.GetKeyDown(KeyCode.RightBracket)) SetBrushSize(_brushSize + 1);
        }

        private void TickPlayback()
        {
            if (!_playing || _model.FrameCount < 2 || _model.Fps <= 0)
            {
                return;
            }

            _playClock += Time.unscaledDeltaTime;
            float step = 1f / _model.Fps;
            if (_playClock < step)
            {
                return;
            }

            _playClock -= step;
            _playFrame = (_playFrame + 1) % _model.FrameCount;
            Upload(_tex, _model.Frames[_playFrame]); // the canvas, the tiled view and the 3-D model all play
        }

        // ---------------------------------------------------------------- model → textures

        private void AfterChange()
        {
            RefreshAll();
            RefreshTileColors();
        }

        private void RefreshAll()
        {
            UploadActiveFrame();
            for (int i = 0; i < _thumbs.Length; i++)
            {
                bool used = i < _model.FrameCount;
                _thumbImgs[i].gameObject.SetActive(used);
                _thumbFrames[i].gameObject.SetActive(used);
                if (used)
                {
                    Upload(_thumbs[i], _model.Frames[i]);
                    _thumbFrames[i].color = i == _model.ActiveFrame ? UiKit.Cyan : new Color(1f, 1f, 1f, 0.18f);
                }
            }

            if (_frameLabel != null)
            {
                _frameLabel.text = _model.FrameCount > 1
                    ? string.Format(L("ui.tex.frames_animated"), _model.ActiveFrame + 1, _model.FrameCount, _model.Fps)
                    : L("ui.tex.frames_still");
            }

            RefreshBadges();
        }

        private void UploadActiveFrame()
        {
            if (_showOfficial)
            {
                return;
            }

            Upload(_tex, _model.Frames[_playing ? Mathf.Min(_playFrame, _model.FrameCount - 1) : _model.ActiveFrame]);
        }

        private static void Upload(Texture2D tex, byte[] raw)
        {
            tex.LoadRawTextureData(raw);
            tex.Apply(false);
        }

        private void RefreshBadges()
        {
            if (_title == null || _entry == null)
            {
                return;
            }

            _title.text = _entry.Label + "  ·  " + _entry.Key + (_model != null && _model.Dirty ? "  *" : string.Empty);
            if (_layerLabel != null)
            {
                string layer = GameTextures.HasWorld(_entry.Key) ? "ui.tex.layer_world"
                    : GameTextures.HasLocal(_entry.Key) ? "ui.tex.layer_local"
                    : GameTextures.Official(_entry.Key) != null ? "ui.tex.layer_official"
                    : "ui.tex.layer_code";
                string alpha = _model.AlphaMode == TextureAlphaMode.Opaque ? "ui.tex.alpha_opaque" : "ui.tex.alpha_cutout";
                _layerLabel.text = L(layer) + "  ·  " + L(alpha);
            }

            bool canPublish = WorldHost != null && WorldHost.CanPublish;
            if (_publishButton != null)
            {
                _publishButton.gameObject.SetActive(canPublish);
            }

            if (_unpublishButton != null)
            {
                _unpublishButton.gameObject.SetActive(canPublish && _entry != null && GameTextures.HasWorld(_entry.Key));
            }
        }

        // ---------------------------------------------------------------- commands

        private void DoUndo()
        {
            if (_model.Undo())
            {
                AfterChange();
            }
        }

        private void DoRedo()
        {
            if (_model.Redo())
            {
                AfterChange();
            }
        }

        private void SetTool(Tool tool)
        {
            _tool = tool;
            _dragShape = false;
            foreach (var kv in _toolButtons)
            {
                kv.Value.color = kv.Key == tool ? new Color(0.45f, 0.82f, 1f, 1f) : Color.white;
            }
        }

        private void SetBrushSize(int size)
        {
            _brushSize = Mathf.Clamp(size, 1, 8);
            if (_sizeLabel != null)
            {
                _sizeLabel.text = string.Format(L("ui.tex.brush_size"), _brushSize);
            }
        }

        private void SwapColors()
        {
            var p = _primary;
            SetPrimary(_secondary);
            SetSecondary(p);
        }

        private void SelectFrame(int index)
        {
            _playing = false;
            _model.SelectFrame(index);
            RefreshAll();
        }

        private void CycleFps()
        {
            var speeds = TextureTiles.AllowedFps;
            int at = 0;
            for (int i = 0; i < speeds.Count; i++)
            {
                if (speeds[i] == _model.Fps)
                {
                    at = i;
                }
            }

            _model.SetFps(speeds[(at + 1) % speeds.Count]);
            RefreshAll();
        }

        private void ResetToOfficial()
        {
            var official = OfficialFrames();
            if (official == null)
            {
                SetStatus(L("ui.tex.no_official"), UiKit.Warn);
                return;
            }

            _model.ReplaceAll(official.Frames, official.Fps);
            AfterChange();
            SetStatus(L("ui.tex.reset_done"), UiKit.Ok);
        }

        private void ShowOfficial(bool show)
        {
            if (_showOfficial == show)
            {
                return;
            }

            _showOfficial = show;
            var official = show ? OfficialFrames() : null;
            if (show && official != null)
            {
                Upload(_tex, official.Frames[0]);
            }
            else
            {
                _showOfficial = false;
                UploadActiveFrame();
            }
        }

        private void SaveForMe()
        {
            if (_entry == null)
            {
                return;
            }

            if (TexturePackFolder.Save(_entry.Key, _model.CopyFrames(), _model.Fps))
            {
                _model.MarkSaved();
                SetStatus(L("ui.tex.saved_local"), UiKit.Ok);
            }
            else
            {
                SetStatus(L("ui.tex.save_failed"), UiKit.Warn);
            }

            RefreshBadges();
        }

        private void RemoveMine()
        {
            if (_entry == null || !GameTextures.HasLocal(_entry.Key))
            {
                SetStatus(L("ui.tex.none_local"), UiKit.Warn);
                return;
            }

            TexturePackFolder.Remove(_entry.Key);
            SetStatus(L("ui.tex.removed_local"), UiKit.Ok);
            RefreshBadges();
        }

        [Serializable]
        private sealed class ExportMeta
        {
            public string key;
            public string kind = "tile";
            public int frames;
            public int fps;
            public string author;
        }

        private void OpenSubmit()
        {
            if (_entry == null)
            {
                return;
            }

            if (_submit == null)
            {
                _submit = gameObject.AddComponent<TextureSubmitDialog>();
                _submit.Localize = L;
                _submit.Settings = Shell != null ? Shell.Settings : null;
                _submit.OnOutcome = SetStatus;
            }

            _submit.Open(_entry.Key, _entry.Label, _model.CopyFrames(), _model.Fps);
        }

        private void ExportForGame()
        {
            if (_entry == null)
            {
                return;
            }

            try
            {
                var frames = _model.CopyFrames();
                string dir = Path.Combine(AppPaths.Root, "texture_exports", _entry.Key);
                Directory.CreateDirectory(dir);
                var meta = new ExportMeta
                {
                    key = _entry.Key,
                    frames = frames.Length,
                    fps = _model.Fps,
                    author = Shell != null && Shell.Settings != null ? Shell.Settings.PlayerName : string.Empty,
                };
                File.WriteAllText(Path.Combine(dir, "texture.json"), JsonUtility.ToJson(meta, true));
                using (var raw = new FileStream(Path.Combine(dir, "texture.bytes"), FileMode.Create, FileAccess.Write))
                {
                    foreach (var f in frames)
                    {
                        raw.Write(f, 0, f.Length); // back to back, in the layout the game loads — merge_texture.py only copies
                    }
                }

                File.WriteAllBytes(Path.Combine(dir, "texture.png"), TexturePackFolder.EncodeStrip(frames));
                SetStatus(L("ui.tex.exported") + "\n" + dir, UiKit.Ok);
            }
            catch (Exception e)
            {
                SetStatus(L("ui.editor.export_failed") + " " + e.Message, UiKit.Warn);
            }
        }

        private void CopyCode()
        {
            if (_entry == null)
            {
                return;
            }

            string code = ShareCode.EncodeTexture(_entry.Key, _model.CopyFrames(), _model.Fps);
            GUIUtility.systemCopyBuffer = code;
            SetStatus(L("ui.tex.code_copied"), UiKit.Ok);
        }

        private void PasteCode()
        {
            if (!ShareCode.TryDecodeTexture(GUIUtility.systemCopyBuffer, out string key, out var frames, out int fps))
            {
                SetStatus(L("ui.tex.code_bad"), UiKit.Warn);
                return;
            }

            // The pixels land on the OPEN texture — a code is a picture, not an instruction where to put it. Only a
            // block animates, and the alpha rule of the open key is applied by the model.
            if (frames.Length > 1 && (_entry == null || _entry.Block == null))
            {
                frames = new[] { frames[0] };
                fps = 0;
            }

            _model.ReplaceAll(frames, fps);
            AfterChange();
            SetStatus(string.Format(L("ui.tex.code_pasted"), key), UiKit.Ok);
        }

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(TexturePackFolder.Root);
                Application.OpenURL("file:///" + TexturePackFolder.Root.Replace('\\', '/'));
            }
            catch (Exception e)
            {
                SetStatus(e.Message, UiKit.Warn);
            }
        }

        private void ReloadFolder()
        {
            int n = TexturePackFolder.Reload();
            SetStatus(string.Format(L("ui.tex.reloaded"), n), UiKit.Ok);
            Open(_entry); // what is on the canvas may just have been replaced from the folder
        }

        private void Publish()
        {
            if (_entry == null || WorldHost == null || !WorldHost.CanPublish)
            {
                return;
            }

            var frames = _model.CopyFrames();
            if (frames.Length > 1 && _entry.Block == null)
            {
                frames = new[] { frames[0] }; // only blocks animate
            }

            WorldHost.Publish(_entry.Key, frames, frames.Length > 1 ? _model.Fps : 0);
            SetStatus(L("ui.tex.publish_sent"), UiKit.Ok);
        }

        private void Unpublish()
        {
            if (_entry != null && WorldHost != null && WorldHost.CanPublish)
            {
                WorldHost.Unpublish(_entry.Key);
            }
        }

        private void Close()
        {
            if (OnClose != null)
            {
                OnClose();
            }
            else if (Shell != null)
            {
                Shell.CloseTextureEditor();
            }
        }

        private void SetStatus(string text, Color color)
        {
            if (_status != null)
            {
                _status.text = text;
                _status.color = color;
            }
        }

        private void SetHint(string key)
        {
            if (_hint != null)
            {
                string text = L(key);
                if (_hint.text != text)
                {
                    _hint.text = text;
                }
            }
        }

        // ---------------------------------------------------------------- colours

        private static Color ToColor(TexelColor c) => new Color32(c.R, c.G, c.B, c.A);

        private static TexelColor FromColor(Color c)
        {
            Color32 b = c;
            return new TexelColor(b.r, b.g, b.b, b.a);
        }

        private void SetPrimary(TexelColor c)
        {
            _primary = c;
            Color.RGBToHSV(ToColor(c), out _h, out _s, out _v);
            RefreshColorUi();
        }

        private void SetSecondary(TexelColor c)
        {
            _secondary = c;
            RefreshColorUi();
        }

        private void RefreshColorUi()
        {
            if (_primarySwatch != null)
            {
                _primarySwatch.color = ToColor(new TexelColor(_primary.R, _primary.G, _primary.B));
            }

            if (_secondarySwatch != null)
            {
                _secondarySwatch.color = ToColor(new TexelColor(_secondary.R, _secondary.G, _secondary.B));
            }

            if (_hexField != null && !_hexField.isFocused)
            {
                _hexField.SetTextWithoutNotify($"{_primary.R:x2}{_primary.G:x2}{_primary.B:x2}{(_primary.A < 255 ? _primary.A.ToString("x2") : string.Empty)}");
            }

            RepaintSvSquare();
        }

        private void OnHexEntered(string text)
        {
            string hex = (text ?? string.Empty).Trim().TrimStart('#');
            if ((hex.Length == 6 || hex.Length == 8)
                && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint v))
            {
                if (hex.Length == 6)
                {
                    v = (v << 8) | 0xFF;
                }

                SetPrimary(new TexelColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v));
            }
        }

        private void RepaintSvSquare()
        {
            if (_svTex == null)
            {
                return;
            }

            int n = _svTex.width;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    px[(y * n) + x] = Color.HSVToRGB(_h, x / (float)(n - 1), y / (float)(n - 1));
                }
            }

            _svTex.SetPixels32(px);
            _svTex.Apply(false);
        }

        /// <summary>Mouse on the colour controls: the saturation/value square, the hue bar, the alpha bar. Returns true
        /// while it owns the mouse, so a drag that leaves the control does not start painting.</summary>
        private bool UpdateColorPicker()
        {
            if (!Input.GetMouseButton(0))
            {
                _dragPicker = 0;
                return false;
            }

            if (Input.GetMouseButtonDown(0))
            {
                _dragPicker = Over(_svRt) ? 1 : Over(_hueRt) ? 2 : Over(_alphaRt) ? 3 : 0;
            }

            if (_dragPicker == 0)
            {
                return false;
            }

            var rt = _dragPicker == 1 ? _svRt : _dragPicker == 2 ? _hueRt : _alphaRt;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, Input.mousePosition, null, out var lp);
            float u = Mathf.Clamp01(lp.x / rt.rect.width), t = Mathf.Clamp01(1f + (lp.y / rt.rect.height)); // t: 0 bottom … 1 top
            byte a = _primary.A;
            if (_dragPicker == 1)
            {
                _s = u;
                _v = t;
            }
            else if (_dragPicker == 2)
            {
                _h = u;
            }
            else
            {
                a = (byte)Mathf.RoundToInt(u * 255f);
            }

            Color32 rgb = Color.HSVToRGB(_h, _s, _v);
            _primary = new TexelColor(rgb.r, rgb.g, rgb.b, a);
            RefreshColorUi();
            return true;
        }

        private static bool Over(RectTransform rt)
            => rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null);

        private void RefreshTileColors()
        {
            _tileColors.Clear();
            _tileColors.AddRange(_model.DominantColors(_tileSwatches.Count));
            for (int i = 0; i < _tileSwatches.Count; i++)
            {
                bool used = i < _tileColors.Count;
                _tileSwatches[i].gameObject.SetActive(used);
                if (used)
                {
                    _tileSwatches[i].color = ToColor(_tileColors[i]);
                }
            }
        }

        // ---------------------------------------------------------------- slot overlay (picture blocks)

        /// <summary>Draws the face slots of a picture block (<c>faces</c> in <c>data/blocks.json</c>) over the canvas:
        /// which region of the picture lands on the pillow, the headboard, the mattress top. Rects are fractions of
        /// the image from its top-left corner — the canvas's own convention.</summary>
        private void RebuildSlotOverlay()
        {
            if (_slotLayer == null)
            {
                return;
            }

            for (int i = _slotLayer.childCount - 1; i >= 0; i--)
            {
                Destroy(_slotLayer.GetChild(i).gameObject);
            }

            var faces = _entry?.Block?.Faces;
            _slotLayer.gameObject.SetActive(_showSlots && faces != null && faces.Count > 0);
            if (faces == null)
            {
                return;
            }

            foreach (var face in faces)
            {
                if (face.Rect == null || face.Rect.Length != 4 || !string.IsNullOrEmpty(face.Tile))
                {
                    continue;
                }

                float x0 = Mathf.Min(face.Rect[0], face.Rect[2]), x1 = Mathf.Max(face.Rect[0], face.Rect[2]);
                float y0 = Mathf.Min(face.Rect[1], face.Rect[3]), y1 = Mathf.Max(face.Rect[1], face.Rect[3]);
                float w = Mathf.Max(2f, (x1 - x0) * CanvasPx), h = Mathf.Max(2f, (y1 - y0) * CanvasPx);
                var frame = UiKit.AddImage(_slotLayer, x0 * CanvasPx, y0 * CanvasPx, w, h, UiKit.ButtonSprite,
                    new Color(1f, 0.85f, 0.25f, 0.9f), Image.Type.Sliced);
                frame.raycastTarget = false;
                frame.fillCenter = false;

                string part = face.Part == "*" ? L("ui.tex.part.any") : LocalOr("ui.tex.part." + face.Part, face.Part);
                string side = face.Side == "*" ? string.Empty : " · " + LocalOr("ui.tex.side." + face.Side, face.Side);
                var label = UiKit.AddText(frame.transform, 3f, 1f, Mathf.Max(60f, w), 16f, part + side, 11,
                    new Color(1f, 0.92f, 0.55f), TextAnchor.UpperLeft, FontStyle.Bold);
                label.raycastTarget = false;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
        }

        private static string LocalOr(string key, string fallback)
        {
            string text = L(key);
            return string.IsNullOrEmpty(text) || text == key ? fallback : text;
        }

        // ---------------------------------------------------------------- UI

        private void BuildUi()
        {
            _ui = UiKit.CreateCanvas("Texture Editor UI");
            _ui.sortingOrder = WorldHost != null ? 40 : 5;
            var root = _ui.transform;
            if (WorldHost != null)
            {
                UiKit.AddModalDim(root); // over a running world: the scene behind must not shine through the panels
            }

            UiKit.AddText(root, 16f, 2f, 1400f, 26f, L("ui.tex.title"), 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            BuildBrowser(root);
            BuildCanvasPanel(root);
            BuildRightPanel(root);

            _hint = UiKit.AddText(root, 16f, 1080f - 30f, 1880f, 24f, L("ui.tex.hint"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiNav.Enable(_ui.gameObject, padHints: false); // this editor draws its own hint line
            _pad = new PadCanvasFocus(_ui.gameObject, _canvasRt);
            SetTool(Tool.Brush);
            SetBrushSize(1);
            SetPrimary(_primary);
        }

        private void BuildBrowser(Transform root)
        {
            var panel = UiKit.AddPanel(root, 16f, 32f, 360f, 1010f, UiKit.PanelFill).transform;
            UiKit.AddText(panel, 14f, 10f, 330f, 24f, L("ui.tex.browser"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddInput(panel, 14f, 40f, 332f, 34f, string.Empty, v =>
            {
                _search = v;
                _list?.Rebuild(_search);
            }, L("ui.tex.search"), 40, 15);

            _listContent = UiKit.ScrollList(panel, 14f, 82f, 332f, 914f);
            _rows.Clear();
            foreach (var e in _entries)
            {
                _rows.Add(new EditorPaletteKit.Entry
                {
                    Id = e.Key,
                    Label = e.Label,
                    Kind = "block",
                    Group = e.Group,
                    Color = new Color(0.4f, 0.5f, 0.6f),
                    Icon = e.Block != null ? EditorPaletteKit.TileSprite(_atlas, e.Block.NumericId.Value) : null,
                });
            }

            _list = new PaletteListUi(Shell != null ? Shell : FindAnyObjectByType<AppShell>(), _listContent, _rows);
            bool first = true;
            _list.OnSelected = index =>
            {
                if (first)
                {
                    first = false; // Rebuild reports the initial selection; Start opens it itself
                    return;
                }

                if (index >= 0 && index < _entries.Count && !ReferenceEquals(_entries[index], _entry))
                {
                    Open(_entries[index]);
                }
            };
            _list.Rebuild(string.Empty);
        }

        private void BuildCanvasPanel(Transform root)
        {
            var panel = UiKit.AddPanel(root, 388f, 32f, 700f, 1010f, UiKit.PanelFill).transform;
            _title = UiKit.AddText(panel, 30f, 8f, 640f, 24f, string.Empty, 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _layerLabel = UiKit.AddText(panel, 30f, 30f, 640f, 18f, string.Empty, 12, UiKit.CyanDim, TextAnchor.MiddleLeft);

            // A checkerboard under the canvas, so see-through texels read as see-through and not as black.
            var checker = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            checker.SetPixels32(new[] { new Color32(70, 74, 84, 255), new Color32(104, 108, 118, 255), new Color32(104, 108, 118, 255), new Color32(70, 74, 84, 255) });
            checker.Apply(false);
            var checkerGo = new GameObject("Checker", typeof(RectTransform));
            checkerGo.transform.SetParent(panel, false);
            UiKit.Place(checkerGo, 30f, 52f, CanvasPx, CanvasPx);
            var checkerImg = checkerGo.AddComponent<RawImage>();
            checkerImg.texture = checker;
            checkerImg.uvRect = new Rect(0f, 0f, 16f, 16f);
            checkerImg.raycastTarget = false;
            _checker = checker;

            var canvasGo = new GameObject("TextureCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(panel, false);
            _canvasRt = UiKit.Place(canvasGo, 30f, 52f, CanvasPx, CanvasPx);
            _canvasImg = canvasGo.AddComponent<RawImage>();
            _canvasImg.texture = _tex;

            var slotGo = new GameObject("FaceSlots", typeof(RectTransform));
            slotGo.transform.SetParent(_canvasRt, false);
            _slotLayer = UiKit.Place(slotGo, 0f, 0f, CanvasPx, CanvasPx);

            // Frame strip.
            float y = 52f + CanvasPx + 10f;
            _frameLabel = UiKit.AddText(panel, 30f, y, 640f, 20f, string.Empty, 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 22f;
            for (int i = 0; i < TextureTiles.MaxFrames; i++)
            {
                int index = i;
                float x = 30f + (i * 60f);
                _thumbFrames[i] = UiKit.AddImage(panel, x - 2f, y - 2f, 56f, 56f, UiKit.SolidSprite, Color.white);
                var go = new GameObject("Frame" + i, typeof(RectTransform));
                go.transform.SetParent(panel, false);
                UiKit.Place(go, x, y, 52f, 52f);
                _thumbImgs[i] = go.AddComponent<RawImage>();
                _thumbImgs[i].texture = _thumbs[i];
                var btn = go.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => SelectFrame(index));
            }

            UiKit.AddButton(panel, 520f, y, 74f, 24f, L("ui.tex.frame_add"), () => { if (!_model.DuplicateFrame()) { SetStatus(L("ui.tex.frame_limit"), UiKit.Warn); } AfterChange(); });
            UiKit.AddButton(panel, 598f, y, 72f, 24f, L("ui.tex.frame_del"), () => { _model.DeleteFrame(); AfterChange(); });
            UiKit.AddButton(panel, 520f, y + 28f, 36f, 24f, "◀", () => { _model.MoveFrame(-1); AfterChange(); });
            UiKit.AddButton(panel, 558f, y + 28f, 36f, 24f, "▶", () => { _model.MoveFrame(1); AfterChange(); });
            UiKit.AddButton(panel, 598f, y + 28f, 72f, 24f, L("ui.tex.fps"), CycleFps);
            y += 62f;

            // Tools.
            AddToolButton(panel, 30f, y, Tool.Brush, "ui.tex.tool.brush");
            AddToolButton(panel, 138f, y, Tool.Eraser, "ui.tex.tool.eraser");
            AddToolButton(panel, 246f, y, Tool.Fill, "ui.tex.tool.fill");
            AddToolButton(panel, 354f, y, Tool.Pipette, "ui.tex.tool.pipette");
            AddToolButton(panel, 462f, y, Tool.Line, "ui.tex.tool.line");
            AddToolButton(panel, 570f, y, Tool.Rectangle, "ui.tex.tool.rect");
            y += 40f;

            UiKit.AddButton(panel, 30f, y, 40f, 32f, "−", () => SetBrushSize(_brushSize - 1));
            _sizeLabel = UiKit.AddText(panel, 74f, y, 130f, 32f, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleCenter);
            UiKit.AddButton(panel, 208f, y, 40f, 32f, "+", () => SetBrushSize(_brushSize + 1));
            var mirrorX = UiKit.AddButton(panel, 262f, y, 100f, 32f, L("ui.tex.mirror_x"), null);
            mirrorX.onClick.AddListener(() => { _model.MirrorX = !_model.MirrorX; Tint(mirrorX, _model.MirrorX); });
            var mirrorY = UiKit.AddButton(panel, 366f, y, 100f, 32f, L("ui.tex.mirror_y"), null);
            mirrorY.onClick.AddListener(() => { _model.MirrorY = !_model.MirrorY; Tint(mirrorY, _model.MirrorY); });
            var slots = UiKit.AddButton(panel, 470f, y, 100f, 32f, L("ui.tex.slots"), null);
            Tint(slots, _showSlots);
            slots.onClick.AddListener(() => { _showSlots = !_showSlots; Tint(slots, _showSlots); RebuildSlotOverlay(); });
            var play = UiKit.AddButton(panel, 574f, y, 96f, 32f, L("ui.tex.play"), null);
            play.onClick.AddListener(() =>
            {
                _playing = !_playing && _model.FrameCount > 1;
                _playFrame = _model.ActiveFrame;
                _playClock = 0f;
                Tint(play, _playing);
                UploadActiveFrame();
            });
            y += 40f;

            UiKit.AddButton(panel, 30f, y, 80f, 32f, L("ui.tex.undo"), DoUndo);
            UiKit.AddButton(panel, 114f, y, 80f, 32f, L("ui.tex.redo"), DoRedo);
            UiKit.AddButton(panel, 198f, y, 80f, 32f, L("ui.tex.flip_h"), () => { _model.Flip(true); AfterChange(); });
            UiKit.AddButton(panel, 282f, y, 80f, 32f, L("ui.tex.flip_v"), () => { _model.Flip(false); AfterChange(); });
            UiKit.AddButton(panel, 366f, y, 36f, 32f, "←", () => { _model.Shift(-8, 0); AfterChange(); });
            UiKit.AddButton(panel, 404f, y, 36f, 32f, "→", () => { _model.Shift(8, 0); AfterChange(); });
            UiKit.AddButton(panel, 442f, y, 36f, 32f, "↑", () => { _model.Shift(0, -8); AfterChange(); });
            UiKit.AddButton(panel, 480f, y, 36f, 32f, "↓", () => { _model.Shift(0, 8); AfterChange(); });
            UiKit.AddButton(panel, 530f, y, 140f, 32f, L("ui.tex.fill_all"), () => { _model.Clear(_primary); AfterChange(); });
            y += 40f;

            UiKit.AddButton(panel, 30f, y, 104f, 32f, L("ui.tex.brighter"), () => { _model.Adjust(0.06f, 0f, 0f); AfterChange(); });
            UiKit.AddButton(panel, 138f, y, 104f, 32f, L("ui.tex.darker"), () => { _model.Adjust(-0.06f, 0f, 0f); AfterChange(); });
            UiKit.AddButton(panel, 246f, y, 104f, 32f, L("ui.tex.contrast_up"), () => { _model.Adjust(0f, 0.12f, 0f); AfterChange(); });
            UiKit.AddButton(panel, 354f, y, 104f, 32f, L("ui.tex.contrast_down"), () => { _model.Adjust(0f, -0.12f, 0f); AfterChange(); });
            UiKit.AddButton(panel, 462f, y, 104f, 32f, L("ui.tex.saturate"), () => { _model.Adjust(0f, 0f, 0.15f); AfterChange(); });
            UiKit.AddButton(panel, 570f, y, 100f, 32f, L("ui.tex.desaturate"), () => { _model.Adjust(0f, 0f, -0.15f); AfterChange(); });
        }

        private Texture2D _checker;

        private void AddToolButton(Transform panel, float x, float y, Tool tool, string key)
        {
            var button = UiKit.AddButton(panel, x, y, 100f, 34f, L(key), () => SetTool(tool));
            _toolButtons[tool] = button.GetComponent<Image>();
        }

        private static void Tint(Button button, bool on)
        {
            var img = button.GetComponent<Image>();
            if (img != null)
            {
                img.color = on ? new Color(0.45f, 0.82f, 1f, 1f) : Color.white;
            }
        }

        private void BuildRightPanel(Transform root)
        {
            var panel = UiKit.AddPanel(root, 1100f, 32f, 804f, 1010f, UiKit.PanelFill).transform;

            // 3-D preview and the tiled 3×3 view (seams!) side by side.
            UiKit.AddText(panel, 16f, 8f, 380f, 22f, L("ui.tex.preview"), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var previewGo = new GameObject("Preview3D", typeof(RectTransform));
            previewGo.transform.SetParent(panel, false);
            UiKit.Place(previewGo, 16f, 34f, 380f, 380f);
            previewGo.AddComponent<RawImage>().texture = _preview.Target;

            UiKit.AddText(panel, 408f, 8f, 380f, 22f, L("ui.tex.tiled"), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var tiledGo = new GameObject("Tiled", typeof(RectTransform));
            tiledGo.transform.SetParent(panel, false);
            UiKit.Place(tiledGo, 408f, 34f, 380f, 380f);
            var tiled = tiledGo.AddComponent<RawImage>();
            tiled.texture = _tex;
            tiled.uvRect = new Rect(0f, 0f, 3f, 3f);
            tiled.raycastTarget = false;

            // Colour: saturation/value square, hue bar, alpha bar.
            float cy = 430f;
            UiKit.AddText(panel, 16f, cy, 300f, 22f, L("ui.tex.color"), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            cy += 26f;
            _svTex = new Texture2D(48, 48, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _svRt = AddRaw(panel, 16f, cy, 220f, 220f, _svTex);
            _hueTex = new Texture2D(96, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var hue = new Color32[96];
            for (int i = 0; i < hue.Length; i++)
            {
                hue[i] = Color.HSVToRGB(i / 95f, 1f, 1f);
            }

            _hueTex.SetPixels32(hue);
            _hueTex.Apply(false);
            _hueRt = AddRaw(panel, 16f, cy + 228f, 220f, 22f, _hueTex);
            var alphaBar = UiKit.AddImage(panel, 16f, cy + 256f, 220f, 18f, UiKit.SolidSprite, new Color(1f, 1f, 1f, 0.55f));
            _alphaRt = (RectTransform)alphaBar.transform;
            UiKit.AddText(panel, 16f, cy + 274f, 220f, 16f, L("ui.tex.alpha_bar"), 11, UiKit.CyanDim, TextAnchor.MiddleLeft);

            // Primary / secondary swatch, swap, hex.
            float sx = 252f;
            _secondarySwatch = UiKit.AddImage(panel, sx + 34f, cy + 28f, 56f, 56f, UiKit.SolidSprite, Color.black);
            _primarySwatch = UiKit.AddImage(panel, sx, cy, 56f, 56f, UiKit.SolidSprite, Color.white);
            UiKit.AddButton(panel, sx + 100f, cy, 110f, 30f, L("ui.tex.swap"), SwapColors);
            _hexField = UiKit.AddInput(panel, sx + 100f, cy + 36f, 110f, 30f, "ffffff", null, "rrggbb", 9, 14);
            _hexField.onEndEdit.AddListener(OnHexEntered);

            // The tile's own colours — a repaint stays in the tile's palette (and a pad can pick from these).
            UiKit.AddText(panel, sx, cy + 96f, 300f, 18f, L("ui.tex.tile_colors"), 12, UiKit.CyanDim, TextAnchor.MiddleLeft);
            for (int i = 0; i < 24; i++)
            {
                int index = i;
                var sw = UiKit.AddImage(panel, sx + ((i % 8) * 34f), cy + 118f + ((i / 8) * 34f), 30f, 30f, UiKit.SolidSprite, Color.white);
                var btn = sw.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.targetGraphic = sw;
                btn.onClick.AddListener(() =>
                {
                    if (index < _tileColors.Count)
                    {
                        SetPrimary(_tileColors[index]);
                    }
                });
                _tileSwatches.Add(sw);
            }

            // Saving.
            float by = 750f;
            UiKit.AddText(panel, 16f, by, 770f, 22f, L("ui.tex.save_title"), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            by += 28f;
            UiKit.AddButton(panel, 16f, by, 250f, 40f, L("ui.tex.save_local"), SaveForMe);
            UiKit.AddButton(panel, 274f, by, 250f, 40f, L("ui.tex.remove_local"), RemoveMine);
            UiKit.AddButton(panel, 532f, by, 256f, 40f, L("ui.tex.export"), ExportForGame);
            by += 46f;
            UiKit.AddButton(panel, 16f, by, 250f, 34f, L("ui.tex.reset"), ResetToOfficial);
            var before = UiKit.AddButton(panel, 274f, by, 250f, 34f, L("ui.tex.before"), null);
            var hold = before.gameObject.AddComponent<HoldButton>();
            hold.OnHold = ShowOfficial;
            UiKit.AddButton(panel, 532f, by, 126f, 34f, L("ui.tex.copy_code"), CopyCode);
            UiKit.AddButton(panel, 662f, by, 126f, 34f, L("ui.tex.paste_code"), PasteCode);
            by += 40f;
            UiKit.AddButton(panel, 16f, by, 250f, 34f, L("ui.tex.open_folder"), OpenFolder);
            UiKit.AddButton(panel, 274f, by, 250f, 34f, L("ui.tex.reload_folder"), ReloadFolder);
            _publishButton = UiKit.AddButton(panel, 532f, by, 126f, 34f, L("ui.tex.publish"), Publish);
            _unpublishButton = UiKit.AddButton(panel, 662f, by, 126f, 34f, L("ui.tex.unpublish"), Unpublish);
            by += 42f;

            _status = UiKit.AddText(panel, 16f, by, 600f, 56f, string.Empty, 13, UiKit.Ok, TextAnchor.UpperLeft);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(panel, 628f, 1010f - 52f, 160f, 40f, L("ui.menu.back"), Close);
            // Offer the texture for the game itself (#1965) — its own dialog with the three consents.
            UiKit.AddButton(panel, 398f, 1010f - 52f, 222f, 40f, L("ui.tex.submit.open"), OpenSubmit, "btn_feedback");
        }

        private static RectTransform AddRaw(Transform parent, float x, float y, float w, float h, Texture tex)
        {
            var go = new GameObject("Raw", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = UiKit.Place(go, x, y, w, h);
            go.AddComponent<RawImage>().texture = tex;
            return rt;
        }
    }

    /// <summary>What a running world adds to the texture editor (#1959): publishing a texture for everyone.</summary>
    public interface ITextureEditorWorldHost
    {
        GameContent Content { get; }

        /// <summary>True when the server offers world textures, the world rule allows them and this player is an admin.</summary>
        bool CanPublish { get; }

        void Publish(string key, byte[][] frames, int fps);

        void Unpublish(string key);
    }

    /// <summary>A button that reports press and release — "hold to see the original".</summary>
    internal sealed class HoldButton : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler,
        UnityEngine.EventSystems.IPointerUpHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public Action<bool> OnHold;

        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData eventData) => OnHold?.Invoke(true);

        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData eventData) => OnHold?.Invoke(false);

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData) => OnHold?.Invoke(false);
    }
}
