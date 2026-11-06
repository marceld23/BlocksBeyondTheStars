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
    public sealed partial class TextureEditor : MonoBehaviour
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
            _content = WorldHost?.Content ?? (Shell != null ? Shell.Content : null); // in a world: ITS content (packs)
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

            var mode = entry.IsIcon ? TextureAlphaMode.Free : TextureTiles.AlphaModeOf(entry.Key);
            var current = entry.IsIcon ? null : GameTextures.Resolve(entry.Key);
            if (entry.IsIcon)
            {
                // The player's own icon if there is one, else the shipped one — read back through the GPU, because the
                // build's icons are not CPU-readable and come in every size. The read lands inline on a synchronous-
                // read API and a frame or two later on WebGPU (#2390), so the model starts blank and fills in; the
                // shipped icon is cached for "reset to official".
                var official = Resources.Load<Texture2D>("icons/" + entry.IconName);
                var source = TexturePackFolder.IconOverride(entry.IconName) ?? official;
                string iconName = entry.IconName;
                _model.Load(null, 0, mode);
                ReadIcon(official, raw => _iconCache[iconName] = raw);
                ReadIcon(source, raw =>
                {
                    if (!ReferenceEquals(_entry, entry) || _model == null)
                    {
                        return;
                    }

                    _model.Load(new[] { raw }, 0, mode);
                    RefreshTileColors();
                    RefreshAll();
                });
            }
            else if (current != null)
            {
                _model.Load(current.Frames, current.Fps, mode);
            }
            else if (entry.Block != null && _atlas != null)
            {
                // A block the build ships no tile for: the atlas painted it in code — start from those pixels.
                _model.Load(new[] { _atlas.ReadTile(entry.Block.NumericId.Value) }, 0, mode);
            }
            else if ((PropTextures.BaseColor(entry.Key) ?? HeldTextures.BaseColor(entry.Key)) is { } propColor)
            {
                _model.Load(new[] { PropTextures.FlatTile(propColor) }, 0, mode); // a door or machine part, a held material (#2836): its colour as a tile
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

        /// <summary>Shipped icons already read back this session (icon name → 64×64 frame), so "reset to official"
        /// has the pixels at hand even where the read is asynchronous.</summary>
        private readonly Dictionary<string, byte[]> _iconCache = new Dictionary<string, byte[]>();

        /// <summary>An icon as one 64×64 frame in the tile layout (RGBA32, rows bottom-up), handed to
        /// <paramref name="done"/>. Blitting into a small render target and reading that back works for
        /// non-readable textures and scales in one go; the read goes through <see cref="GpuReadback"/>, so the
        /// callback runs inline on a synchronous-read API and a frame or two later on WebGPU (#2390).</summary>
        private static void ReadIcon(Texture source, System.Action<byte[]> done)
        {
            var raw = new byte[TextureTiles.BytesPerFrame];
            if (source == null)
            {
                done(raw);
                return;
            }

            var rt = RenderTexture.GetTemporary(Tile, Tile, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var filter = source.filterMode;
            source.filterMode = source.width > Tile ? FilterMode.Bilinear : FilterMode.Point;
            Graphics.Blit(source, rt);
            source.filterMode = filter;
            GpuReadback.Read(rt, TextureFormat.RGBA32, copy =>
            {
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null)
                {
                    var data = copy.GetRawTextureData<byte>();
                    if (data.Length == raw.Length)
                    {
                        data.CopyTo(raw);
                    }

                    Destroy(copy);
                }

                done(raw);
            });
        }

        /// <summary>The texture as shipped — every frame, or the code-painted tile of a block that has no file.</summary>
        private TextureFrames OfficialFrames()
        {
            if (_entry == null)
            {
                return null;
            }

            if (_entry.IsIcon)
            {
                string iconName = _entry.IconName;
                if (!_iconCache.ContainsKey(iconName))
                {
                    ReadIcon(Resources.Load<Texture2D>("icons/" + iconName), raw => _iconCache[iconName] = raw); // inline, or a frame later on WebGPU
                }

                return _iconCache.TryGetValue(iconName, out var cached)
                    ? new TextureFrames(new[] { cached }, 0, TextureLayer.Official)
                    : null;
            }

            var bundled = GameTextures.Official(_entry.Key);
            if (bundled != null)
            {
                return bundled;
            }

            if ((PropTextures.BaseColor(_entry.Key) ?? HeldTextures.BaseColor(_entry.Key)) is { } propColor)
            {
                return new TextureFrames(new[] { PropTextures.FlatTile(propColor) }, 0, TextureLayer.Official);
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
        private CreationSubmitDialog _submit;

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
            if ((!_stroking && !_dragShape && !RectTransformUtility.RectangleContainsScreenPoint(_canvasRt, Input.mousePosition, null)) // #3179: a drag runs on along the edge
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
                string layer = _entry.IsIcon ? (TexturePackFolder.IconOverride(_entry.IconName) != null ? "ui.tex.layer_local" : "ui.tex.layer_official")
                    : GameTextures.HasWorld(_entry.Key) ? "ui.tex.layer_world"
                    : GameTextures.HasLocal(_entry.Key) ? "ui.tex.layer_local"
                    : GameTextures.Official(_entry.Key) != null ? "ui.tex.layer_official"
                    : "ui.tex.layer_code";
                string alpha = _model.AlphaMode == TextureAlphaMode.Opaque ? "ui.tex.alpha_opaque" : "ui.tex.alpha_cutout";
                string owner = GameTextures.WorldOwner(_entry.Key);
                _layerLabel.text = L(layer) + (owner.Length > 0 ? " (" + owner + ")" : string.Empty) + "  ·  " + L(alpha);
            }

            bool canPublish = WorldHost != null && WorldHost.CanPublish && !_entry.IsIcon; // an icon is never a world texture
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

            if (_entry.IsIcon)
            {
                var icon = new Texture2D(Tile, Tile, TextureFormat.RGBA32, false);
                Upload(icon, _model.Frames[0]);
                bool ok = TexturePackFolder.SaveIcon(_entry.IconName, icon);
                Destroy(icon);
                if (ok)
                {
                    _model.MarkSaved();
                }

                SetStatus(L(ok ? "ui.tex.saved_local" : "ui.tex.save_failed"), ok ? UiKit.Ok : UiKit.Warn);
                RefreshBadges();
                return;
            }

            if (TexturePackFolder.Save(_entry.Key, _model.CopyFrames(), _model.Fps))
            {
                _model.MarkSaved();
                // The world wins over the local pack (decision 2026-09-20) — say so, or "it did not work" is what
                // the player concludes when nothing changes around them.
                bool hidden = GameTextures.HasWorld(_entry.Key) && GameTextures.ShowWorldTextures;
                SetStatus(L(hidden ? "ui.tex.saved_local_world_wins" : "ui.tex.saved_local"), hidden ? UiKit.Warn : UiKit.Ok);
            }
            else
            {
                SetStatus(L("ui.tex.save_failed"), UiKit.Warn);
            }

            RefreshBadges();
        }

        private void RemoveMine()
        {
            if (_entry != null && _entry.IsIcon)
            {
                if (TexturePackFolder.IconOverride(_entry.IconName) == null)
                {
                    SetStatus(L("ui.tex.none_local"), UiKit.Warn);
                    return;
                }

                TexturePackFolder.RemoveIcon(_entry.IconName);
                SetStatus(L("ui.tex.removed_local"), UiKit.Ok);
                Open(_entry); // back to the shipped icon on the canvas
                return;
            }

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
                _submit = gameObject.AddComponent<CreationSubmitDialog>();
                _submit.Localize = L;
                _submit.Settings = Shell != null ? Shell.Settings : null;
                _submit.OnOutcome = SetStatus;
            }

            _submit.Open(_entry.Key, _entry.Label, _model.CopyFrames(), _model.Fps, _entry.IsIcon);
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
                    kind = _entry.IsIcon ? "icon" : "tile",
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
                Debug.LogWarning($"[TextureEditor] Export of '{_entry.Key}' failed: {e}");
                SetStatus(L("ui.ed.export_failed_hint"), UiKit.Warn); // a friendly line; the exception goes to the log (#3263)
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
                Debug.LogWarning($"[TextureEditor] Could not open the texture folder: {e}");
                SetStatus(L("ui.ed.open_folder_failed"), UiKit.Warn);
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
                var label = UiKit.AddText(frame.transform, 3f, 1f, Mathf.Max(60f, w), 18f, part + side, UiKit.EditorMinFontSize,
                    new Color(1f, 0.92f, 0.55f), TextAnchor.UpperLeft, FontStyle.Bold); // 13 px, not 11 (#3263)
                label.raycastTarget = false;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
        }

        private static string LocalOr(string key, string fallback)
        {
            string text = L(key);
            return string.IsNullOrEmpty(text) || text == key ? fallback : text;
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
