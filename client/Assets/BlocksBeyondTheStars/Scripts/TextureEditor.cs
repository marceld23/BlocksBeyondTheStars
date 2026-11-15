// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Textures;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The texture editor (#1955): opens EVERY texture of the running game — block tiles, the picture tiles of the
    /// bed and the campfire, plants, creature hides, avatar fabrics — on a true-colour canvas, shows it the way the
    /// game does (cube, built-in form with its face slots, cutout cards, tiled 3×3), and keeps it three ways:
    /// <list type="bullet">
    /// <item><b>Save</b> (the editor shell's primary button, #3693) — into the local texture pack
    /// (<see cref="TexturePackFolder"/>); visible at once, on this install only.</item>
    /// <item><b>Export for the game</b> — a bundle for <c>tools/merge_texture.py</c> (desktop only).</item>
    /// <item><b>For this world</b> — only with a world host and the right to do so (#1959).</item>
    /// </list>
    /// It sits on the shared <see cref="EditorShell"/> (#3688–#3690): Back, the texture's name, Undo / Redo, Submit and
    /// Save in the header, a status bar coloured by severity, an automatic draft (<c>TextureEditor.Document.cs</c>), and
    /// Esc / pad B that never close it — the menu (AppShell) and the world (GameMenu) hand the key to the shell.
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
        private TextureEntry _entry;
        private PixelCanvasModel _model;

        private Canvas _ui;
        private Texture2D _tex;          // the active frame, drawn on the canvas, the tiled view and the 3-D preview
        private readonly Texture2D[] _thumbs = new Texture2D[TextureTiles.MaxFrames];
        private TexturePreviewRig _preview;

        private Tool _tool = Tool.Brush;
        private int _brushSize = 1;
        private TexelColor _primary = new TexelColor(255, 255, 255);
        private TexelColor _secondary = new TexelColor(0, 0, 0);

        private bool _stroking;
        private int _lastX = -1, _lastY = -1;
        private bool _dragShape;          // line / rectangle: first corner set, waiting for release
        private int _shapeX, _shapeY;
        private bool _showSlots = true;
        private bool _showOfficial;       // "before": the canvas shows the shipped texture while the button is held
        private bool _playing;
        private float _playClock;
        private int _playFrame;
        private bool _lastButtonWasSecondary;
        private CreationSubmitDialog _submit;

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
            CanvasEditorDrafts.Offer(_shell); // last: "Continue your draft?" when one is left (#3689)
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
                EditorSceneKit.Discard(_ui.gameObject);
            }

            EditorSceneKit.Discard(_tex);
            foreach (var t in _thumbs)
            {
                EditorSceneKit.Discard(t);
            }

            EditorSceneKit.Discard(_svTex);
            EditorSceneKit.Discard(_hueTex);
            EditorSceneKit.Discard(_checker);
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
                OpenIcon(entry, mode);
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
            _shell?.ClearStatus();
            if (!entry.IsIcon)
            {
                ApplyPendingDraft(entry); // a restored draft lands on the freshly opened texture (#3689)
            }
        }

        /// <summary>The player's own icon if there is one, else the shipped one — read back through the GPU, because the
        /// build's icons are not CPU-readable and come in every size. The read lands inline on a synchronous-read API and a
        /// frame or two later on WebGPU (#2390), so the model starts blank and fills in; the shipped icon is cached for
        /// "back to the original".</summary>
        private void OpenIcon(TextureEntry entry, TextureAlphaMode mode)
        {
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
                ApplyPendingDraft(entry);
            });
        }

        /// <summary>Shipped icons already read back this session (icon name → 64×64 frame), so "back to the original"
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

                    EditorSceneKit.Discard(copy);
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

        /// <summary>What the mouse or the pad does to the canvas this frame.</summary>
        private struct CanvasInput
        {
            public bool PrimaryHeld, SecondaryHeld, PrimaryDown, SecondaryDown, PickDown, Modifier;
            public int X, Y;
        }

        private void Update()
        {
            if (_canvasRt == null || _model == null || _shell == null)
            {
                return;
            }

            if ((_submit != null && _submit.IsOpen) || _shell.IsDialogOpen)
            {
                return; // a dialog is modal: no strokes, no shortcuts underneath
            }

            bool pad = InputMap.ActiveDevice == InputDeviceKind.Gamepad;
            _preview.Tick(Time.unscaledDeltaTime, pad ? InputMap.PadLookX() * 0.6f : 0f);
            TickPlayback();
            HandleKeys();

            _pad.SetCells(Tile, Tile);
            bool padCanvas = _pad.Tick(pad);
            SetHint(!pad ? "ui.tex.hint" : (padCanvas ? "ui.tex.hint_pad" : "ui.tex.hint_pad_panel"));
            if (pad && InputMap.PadDown(PadButton.Rb))
            {
                _shell.Undo(); // the pad's undo goes through the shell, as its button and Ctrl+Z do
            }

            if (!pad && UpdateColorPicker())
            {
                return; // the mouse is dragging a colour control — not a stroke
            }

            if (ReadInput(padCanvas, out var input))
            {
                ApplyInput(input);
            }
        }

        /// <summary>The canvas input of this frame; false when the mouse is off the canvas (a stroke without a held
        /// button is closed then).</summary>
        private bool ReadInput(bool padCanvas, out CanvasInput input)
        {
            input = default;
            if (padCanvas)
            {
                input.PrimaryHeld = InputMap.PadHeld(PadButton.A);
                input.SecondaryHeld = InputMap.PadHeld(PadButton.X);
                input.PrimaryDown = InputMap.PadDown(PadButton.A);
                input.SecondaryDown = InputMap.PadDown(PadButton.X);
                input.PickDown = InputMap.PadDown(PadButton.Y);
                input.Modifier = InputMap.PadHeld(PadButton.Lb);
                input.X = _pad.CellX;
                input.Y = _pad.CellY;
                return true;
            }

            input.PrimaryHeld = Input.GetMouseButton(0);
            input.SecondaryHeld = Input.GetMouseButton(1);
            input.PrimaryDown = Input.GetMouseButtonDown(0);
            input.SecondaryDown = Input.GetMouseButtonDown(1);
            input.PickDown = Input.GetMouseButtonDown(2);
            input.Modifier = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!CanvasCell(out input.X, out input.Y))
            {
                if (!input.PrimaryHeld && !input.SecondaryHeld)
                {
                    EndStroke(-1, -1, false);
                }

                return false;
            }

            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            {
                input.PickDown |= input.PrimaryDown;
            }

            return true;
        }

        /// <summary>Picks a colour, closes a stroke, or paints with the current tool.</summary>
        private void ApplyInput(CanvasInput input)
        {
            if (input.PickDown || (_tool == Tool.Pipette && (input.PrimaryDown || input.SecondaryDown)))
            {
                var picked = _model.Get(input.X, input.Y);
                if (input.SecondaryDown && !input.PickDown)
                {
                    SetSecondary(picked);
                }
                else
                {
                    SetPrimary(picked);
                }

                return;
            }

            if (!input.PrimaryHeld && !input.SecondaryHeld)
            {
                EndStroke(input.X, input.Y, input.Modifier);
                return;
            }

            // The right button (pad: X) always erases; the eraser tool erases with either button.
            bool erase = _tool == Tool.Eraser || (input.SecondaryHeld && !input.PrimaryHeld);
            var color = erase ? EraseColor() : _primary;
            bool down = input.PrimaryDown || input.SecondaryDown;
            switch (_tool)
            {
                case Tool.Fill:
                    if (down)
                    {
                        _model.Fill(input.X, input.Y, color, input.Modifier);
                        AfterChange();
                    }

                    break;

                case Tool.Line:
                case Tool.Rectangle:
                    if (down)
                    {
                        _dragShape = true;
                        _shapeX = input.X;
                        _shapeY = input.Y;
                    }

                    _lastX = input.X;
                    _lastY = input.Y;
                    break;

                default: // brush / eraser
                    StrokeTo(input.X, input.Y, color, down);
                    break;
            }
        }

        /// <summary>The brush and the eraser: a gap-free run from the last cell, ONE undo step per stroke.</summary>
        private void StrokeTo(int x, int y, TexelColor color, bool down)
        {
            if (down || !_stroking)
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

        /// <summary>The tool keys (B / E / G / I / L / R, X swaps the colours, [ ] the brush size) — never while a text
        /// field has the keyboard or Ctrl is down. Ctrl+Z / Ctrl+Y / Ctrl+S belong to the editor shell (#3688).</summary>
        private void HandleKeys()
        {
            if (UiKit.TextFieldFocused() || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
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

        /// <summary>After an edit: redraw, refresh the tile's colours and tell the shell (draft timer, header).</summary>
        private void AfterChange()
        {
            RefreshAll();
            RefreshTileColors();
            _shell?.NotifyChanged();
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

        /// <summary>The line above the canvas: which layer the open texture comes from and whether it may have holes. The
        /// texture's name and "Not saved yet" are the shell header's job now.</summary>
        private void RefreshBadges()
        {
            if (_layerLabel == null || _entry == null)
            {
                return;
            }

            string layer = _entry.IsIcon ? (TexturePackFolder.IconOverride(_entry.IconName) != null ? "ui.tex.layer_local" : "ui.tex.layer_official")
                : GameTextures.HasWorld(_entry.Key) ? "ui.tex.layer_world"
                : GameTextures.HasLocal(_entry.Key) ? "ui.tex.layer_local"
                : GameTextures.Official(_entry.Key) != null ? "ui.tex.layer_official"
                : "ui.tex.layer_code";
            string alpha = _model.AlphaMode == TextureAlphaMode.Opaque ? "ui.tex.alpha_opaque" : "ui.tex.alpha_cutout";
            string owner = GameTextures.WorldOwner(_entry.Key);
            _layerLabel.text = L(layer) + (owner.Length > 0 ? " (" + owner + ")" : string.Empty) + "  ·  " + L(alpha);

            bool canPublish = WorldHost != null && WorldHost.CanPublish && !_entry.IsIcon; // an icon is never a world texture
            if (_publishButton != null)
            {
                _publishButton.gameObject.SetActive(canPublish);
            }

            if (_unpublishButton != null)
            {
                _unpublishButton.gameObject.SetActive(canPublish && GameTextures.HasWorld(_entry.Key));
            }
        }

        // ---------------------------------------------------------------- tools and frames

        private void SetTool(Tool tool)
        {
            _tool = tool;
            _dragShape = false;
            _toolBar?.Select(Array.IndexOf(BarTools, tool));
        }

        private void SetBrushSize(int size)
        {
            _brushSize = Mathf.Clamp(size, 1, 8);
            if (_sizeLabel != null)
            {
                _sizeLabel.text = string.Format(L("ui.tex.brush_size"), _brushSize);
            }
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
            AfterChange();
        }

        private void AddFrame()
        {
            if (_entry != null && _entry.IsIcon)
            {
                SetStatus(L("ui.tex.icon_one_frame"), EditorStatusSeverity.Warning);
                return;
            }

            if (!_model.DuplicateFrame())
            {
                SetStatus(L("ui.tex.frame_limit"), EditorStatusSeverity.Warning);
            }

            AfterChange();
        }

        /// <summary>Plays the animation on the canvas, the tiled view and the 3-D preview; returns whether it plays now.</summary>
        private bool TogglePlay()
        {
            _playing = !_playing && _model.FrameCount > 1;
            _playFrame = _model.ActiveFrame;
            _playClock = 0f;
            UploadActiveFrame();
            return _playing;
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
                EditorSceneKit.Discard(_slotLayer.GetChild(i).gameObject);
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
