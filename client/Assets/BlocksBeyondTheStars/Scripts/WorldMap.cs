// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A toggleable (key <b>M</b>) full-screen planet map, distinct from the star map (Tab→Map). Renders a
    /// player-centred top-down view of the streamed terrain — only loaded chunks are drawn, so it reveals
    /// as you explore (fog-of-war). Shows the player (with heading), the ship, ship stations and a
    /// click-to-set waypoint (which the HUD compass then points to). uGUI on a DPI-independent canvas.
    /// </summary>
    public sealed class WorldMap : MonoBehaviour
    {
        public GameBootstrap Game;

        private Canvas _canvas;
        private RectTransform _mapRt;   // the terrain RawImage (markers anchor inside it)
        private Text _info;
        private bool _open;
        private int _radius = 180;      // half-side of the shown square, in blocks (zoomable)
        private float _ox, _oz, _side;  // region origin (world) + side, for marker mapping

        // Markers scale with the player's UI-scale setting (#592). The CANVAS must stay absolute-1920
        // (a scaled full-screen layout would run off-screen, #483) — marker size is not layout though,
        // so it alone follows the accessibility setting.
        private static float MarkerScale => UiKit.UserScale;

        /// <summary>The dark backing disc behind every marker icon — the contrast separation that keeps
        /// a marker readable over any terrain colour (#592).</summary>
        private static readonly Color DiscCol = new Color(0.01f, 0.03f, 0.07f, 0.78f);

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            // Esc closes the map like every other panel — before this, the map set MenuOpen (so the app
            // shell left Esc alone) but had no handler of its own, leaving Esc swallowed-but-dead (#413 N6).
            if (_open && Input.GetKeyDown(KeyCode.Escape) && !Game.ChatTyping)
            {
                Game.MarkMenuInputHandled(); // consumed — don't also pop the quit prompt
                Close();
            }

            // Not while the death/ship-destruction prompt is up — confirming it would otherwise reveal a
            // still-open map (#413 N6).
            if (Input.GetKeyDown(KeyCode.M) && !Game.ChatTyping && !Game.AwaitingRespawnConfirm)
            {
                if (_open)
                {
                    Close();
                }
                else if (!Game.MenuOpen && !Game.InSpace)
                {
                    Open();
                }
            }

            // Clicking the map sets a waypoint (and lets the compass guide you there).
            if (_open && Input.GetMouseButtonDown(0) && _mapRt != null)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_mapRt, Input.mousePosition, null)
                    && RectTransformUtility.ScreenPointToLocalPointInRectangle(_mapRt, Input.mousePosition, null, out var lp))
                {
                    var rect = _mapRt.rect;
                    float u = (lp.x - rect.xMin) / rect.width;
                    float v = (lp.y - rect.yMin) / rect.height;
                    Game.Waypoint = new Vector3(_ox + u * _side, 0f, _oz + v * _side);
                    Refresh();
                }
            }
        }

        private void Open()
        {
            _open = true;
            Game.SetMenuOwner(this, true); // the cursor arbiter frees/locks from the owner set (#413)
            Build();
        }

        private void Close()
        {
            _open = false;
            Game.SetMenuOwner(this, false); // arbiter re-locks only once NO other panel is open (#413)
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
            }
        }

        private void Build()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }

            _canvas = UiKit.CreateCanvas("WorldMapUI");
            _canvas.sortingOrder = 60;
            var root = _canvas.transform;

            UiKit.AddImage(root, 0, 0, 1920, 1080, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.92f));
            UiKit.AddLogo(root, 40, 24, 700, 40, L("ui.map.planet").ToUpperInvariant() + "  —  " + (string.IsNullOrEmpty(Game.LocationName) ? "?" : Game.LocationName), 26);

            // Square map area on the left.
            const float A = 900f, ax = 40f, ay = 100f;
            UiKit.AddPanel(root, ax - 6, ay - 6, A + 12, A + 12, UiKit.Panel);
            var tex = BuildTexture();
            var go = new GameObject("Map", typeof(RectTransform));
            go.transform.SetParent(root, false);
            UiKit.Place(go, ax, ay, A, A);
            var raw = go.AddComponent<RawImage>();
            raw.texture = tex;
            _mapRt = go.GetComponent<RectTransform>();

            // Info / legend panel on the right.
            UiKit.AddPanel(root, 980, 100, 900, 900, UiKit.Panel);
            float ix = 1010f, iy = 130f;
            UiKit.AddText(root, ix, iy, 840, 28, L("ui.map.legend"), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            iy += 40f;
            // Truncate (not Overflow): a long POI/beacon list must not run through the legend and buttons
            // below — the last visible line simply cuts off (#592; the layout-overflow class of bug).
            _info = UiKit.AddText(root, ix, iy, 840, 570, string.Empty, 20, UiKit.TextCol, TextAnchor.UpperLeft);
            _info.horizontalOverflow = HorizontalWrapMode.Wrap;
            _info.verticalOverflow = VerticalWrapMode.Truncate;

            // Icon legend — every marker type the map actually draws, each tinted its ON-MAP colour so the
            // legend explains the map instead of showing everything cyan (#592). Two rows; a fixed slot
            // width keeps DE labels from pushing the row past the panel.
            {
                (string icon, string key, Color col)[] entries =
                {
                    ("map_player", "ui.map.you", UiKit.Cyan),
                    ("map_ship", "ui.hud.ship", new Color(0.5f, 0.9f, 1f)),
                    ("map_waypoint", "ui.map.waypoint", new Color(1f, 0.85f, 0.3f)),
                    ("map_beacon", "ui.beacon.default", new Color(1f, 0.72f, 0.2f)),
                    ("map_base", "ui.base.default", new Color(0.36f, 0.82f, 0.86f)),
                    ("map_pad", "ui.map.pad", new Color(0.5f, 0.9f, 0.6f)),
                    ("map_settlement", "ui.map.settlement", new Color(0.5f, 0.95f, 0.6f)),
                    ("map_ruin", "ui.map.ruin", new Color(0.65f, 0.6f, 0.55f)),
                    ("map_wreck", "ui.map.wreck", new Color(1f, 0.55f, 0.3f)),
                    ("map_station", "ui.map.other", new Color(0.8f, 0.8f, 0.9f)),
                };
                const int perRow = 5;
                const float slotW = 168f, rowH = 34f;
                for (int li = 0; li < entries.Length; li++)
                {
                    float lx = ix + (li % perRow) * slotW;
                    float ly = 795f + (li / perRow) * rowH;
                    var sprite = UiKit.Icon(entries[li].icon);
                    if (sprite != null)
                    {
                        UiKit.AddImage(root, lx, ly + 2f, 26, 26, UiKit.DiscSprite, DiscCol);
                        UiKit.AddImage(root, lx + 3f, ly + 5f, 20, 20, sprite, entries[li].col);
                        lx += 32f;
                    }

                    UiKit.AddText(root, lx, ly, slotW - 36f, 30, L(entries[li].key), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                }
            }

            UiKit.AddButton(root, ix, 880, 120, 50, "−", () => { _radius = Mathf.Min(640, _radius + 90); Build(); });
            UiKit.AddButton(root, ix + 140, 880, 120, 50, "+", () => { _radius = Mathf.Max(60, _radius - 90); Build(); });
            UiKit.AddButton(root, ix + 300, 880, 240, 50, L("ui.map.clear_waypoint"), () => { Game.Waypoint = null; Refresh(); });
            UiKit.AddButton(root, ix + 560, 880, 200, 50, L("ui.action.close"), Close);

            Refresh();
        }

        /// <summary>Player-centred surface render of the loaded chunks (fog where nothing is streamed).</summary>
        private Texture2D BuildTexture()
        {
            int r = _radius;
            _ox = Game.PlayerPosition.x - r;
            _oz = Game.PlayerPosition.z - r;
            _side = r * 2f;

            int step = Mathf.Max(1, Mathf.CeilToInt(_side / 360f));
            int size = Mathf.Clamp(Mathf.RoundToInt(_side / step), 16, 512);

            var height = new int[size * size];
            var block = new ushort[size * size];
            for (int i = 0; i < height.Length; i++)
            {
                height[i] = int.MinValue;
            }

            int n = WorldConstants.ChunkSize;
            int minY = int.MaxValue, maxY = int.MinValue;
            foreach (var kv in Game.World.Chunks)
            {
                var o = WorldConstants.ChunkOrigin(kv.Key);
                var c = kv.Value;
                // Round worlds: place the chunk at the scene X AND Z nearest the player so the map has no seam.
                float sxo = Game.SceneX(o.X);
                float szo = Game.SceneZ(o.Z);
                for (int lx = 0; lx < n; lx++)
                for (int lz = 0; lz < n; lz++)
                {
                    float wx = sxo + lx;
                    float wz = szo + lz;
                    int px = Mathf.FloorToInt((wx - _ox) / step), py = Mathf.FloorToInt((wz - _oz) / step);
                    if (px < 0 || px >= size || py < 0 || py >= size)
                    {
                        continue;
                    }

                    for (int ly = n - 1; ly >= 0; ly--)
                    {
                        if (!c.Get(lx, ly, lz).IsAir)
                        {
                            int wy = o.Y + ly, idx = py * size + px;
                            if (wy > height[idx])
                            {
                                height[idx] = wy;
                                block[idx] = c.Get(lx, ly, lz).Value;
                                if (wy < minY) minY = wy;
                                if (wy > maxY) maxY = wy;
                            }

                            break;
                        }
                    }
                }
            }

            float span = Mathf.Max(1, maxY - minY);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px2 = new Color[size * size];
            var fog = new Color(0.06f, 0.09f, 0.16f, 1f);
            for (int i = 0; i < px2.Length; i++)
            {
                if (height[i] == int.MinValue)
                {
                    px2[i] = fog;
                    continue;
                }

                var col = MapColor(Game.Content.BlockById(new BlocksBeyondTheStars.Shared.Primitives.BlockId(block[i]))?.Key);
                float shade = Mathf.Lerp(0.55f, 1.1f, (height[i] - minY) / span);
                px2[i] = new Color(Mathf.Clamp01(col.r * shade), Mathf.Clamp01(col.g * shade), Mathf.Clamp01(col.b * shade), 1f);
            }

            tex.SetPixels(px2);
            tex.Apply();
            return tex;
        }

        private void Refresh()
        {
            if (_mapRt == null)
            {
                return;
            }

            // Clear old markers (children of the map).
            for (int i = _mapRt.childCount - 1; i >= 0; i--)
            {
                Destroy(_mapRt.GetChild(i).gameObject);
            }

            // Ship + stations + waypoint as icons; player as a heading arrow. Sizes were 20–28 px before
            // #592 — with 1–10 % ink line icons that lit a handful of pixels; the bold set at ~1.5× reads.
            if (Game.ShipPosition.HasValue)
            {
                Marker(Game.ShipPosition.Value.x, Game.ShipPosition.Value.z, 32f, new Color(0.5f, 0.9f, 1f), "▣", "map_ship");
            }

            if (Game.Waypoint.HasValue)
            {
                Marker(Game.Waypoint.Value.x, Game.Waypoint.Value.z, 36f, new Color(1f, 0.85f, 0.3f), "✛", "map_waypoint");
            }

            // World POIs (settlements, …) from the server.
            var poiLines = new System.Text.StringBuilder();
            if (Game.PlanetPois != null)
            {
                foreach (var p in Game.PlanetPois)
                {
                    var (glyph, col, icon) = PoiLook(p.Type);
                    Marker(p.X, p.Z, 36f, col, glyph, icon);
                    float d = GroundDistance(p.X, p.Z);
                    poiLines.Append($"\n{glyph} {p.Name}  —  {Mathf.RoundToInt(d)} m");
                }
            }

            // Player-placed radio beacons (item 37): a distinct amber star + the typed label beside it.
            if (Game.Beacons != null)
            {
                var beaconCol = new Color(1f, 0.72f, 0.2f);
                foreach (var b in Game.Beacons)
                {
                    Marker(b.X, b.Z, 32f, beaconCol, "✦", "map_beacon");
                    string name = string.IsNullOrEmpty(b.Label) ? L("ui.beacon.default") : b.Label;
                    MarkerLabel(b.X, b.Z, name, beaconCol);
                    float bd = GroundDistance(b.X, b.Z);
                    poiLines.Append($"\n✦ {name}  —  {Mathf.RoundToInt(bd)} m");
                }
            }

            // Player-placed beam blocks (teleporter pads): a cyan ring + the typed name beside it.
            if (Game.Beams != null)
            {
                var beamCol = new Color(0.28f, 0.85f, 1f);
                foreach (var b in Game.Beams)
                {
                    Marker(b.X, b.Z, 32f, beamCol, "⊕", "map_beacon");
                    string name = string.IsNullOrEmpty(b.Name) ? L("ui.beam.default") : b.Name;
                    MarkerLabel(b.X, b.Z, name, beamCol);
                    float bd = GroundDistance(b.X, b.Z);
                    poiLines.Append($"\n⊕ {name}  —  {Mathf.RoundToInt(bd)} m");
                }
            }

            // Player-founded bases (Grundstein): a teal house glyph at the base core + the base name beside it.
            if (Game.Bases != null)
            {
                var baseCol = new Color(0.36f, 0.82f, 0.86f);
                foreach (var bp in Game.Bases)
                {
                    Marker(bp.X, bp.Z, 36f, baseCol, "⌂", "map_base");
                    string name = string.IsNullOrEmpty(bp.Name) ? L("ui.base.default") : bp.Name;
                    MarkerLabel(bp.X, bp.Z, name, baseCol);
                    float bd = GroundDistance(bp.X, bp.Z);
                    poiLines.Append($"\n⌂ {name}  —  {Mathf.RoundToInt(bd)} m");
                }
            }

            // Fixed landing pads (item 38): ALWAYS shown — at true position when in view, else pinned to the map
            // edge pointing the way (pads are spread round the body, so most sit outside this local window). Green
            // = free, red = another player is on it, cyan = your own pad (#977). Also listed below with distance
            // so every pad is findable.
            if (Game.LandingPads != null)
            {
                foreach (var pad in Game.LandingPads)
                {
                    var col = pad.Mine ? new Color(0.45f, 0.85f, 1f)
                        : pad.Occupied ? new Color(1f, 0.45f, 0.4f)
                        : new Color(0.5f, 0.9f, 0.6f);
                    PadMarker(pad.X, pad.Z, col);
                    int dist = Mathf.RoundToInt(GroundDistance(pad.X, pad.Z));
                    string occ = pad.Occupied || pad.Mine ? $" ({pad.Occupant})" : string.Empty;
                    poiLines.Append($"\n⊕ {L("ui.map.pad")} {pad.Index + 1}{occ}  —  {dist} m");
                }
            }

            // Ship station tiles (workshop / lab / medbay / …) as small dots.
            if (Game.Stations != null)
            {
                foreach (var s in Game.Stations)
                {
                    Marker(s.X, s.Z, 14f, new Color(0.55f, 0.8f, 1f, 0.85f), "•");
                }
            }

            // Player arrow (rotated to heading; the arrowhead icon points north / +Z).
            var pa = Marker(Game.PlayerPosition.x, Game.PlayerPosition.z, 44f, UiKit.Cyan, "▲", "map_player");
            if (pa != null)
            {
                pa.transform.localRotation = Quaternion.Euler(0, 0, -Game.PlayerYaw);
            }

            string wp = Game.Waypoint.HasValue
                ? $"\n{L("ui.map.waypoint")}: {Mathf.RoundToInt(GroundDistance(Game.Waypoint.Value.x, Game.Waypoint.Value.z))} m"
                : string.Empty;
            if (_info != null)
            {
                string pois = poiLines.Length > 0 ? $"\n\n{L("ui.map.pois")}:{poiLines}" : string.Empty;
                _info.text =
                    $"{L("ui.map.you")}: X {Mathf.RoundToInt(Game.PlayerPosition.x)}  Z {Mathf.RoundToInt(Game.PlayerPosition.z)}\n" +
                    $"{L("ui.map.scale")}: ±{_radius} m\n" +
                    $"{L("ui.map.click_hint")}{wp}{pois}";
            }
        }

        /// <summary>Ground (XZ) distance from the player, the short way round both wrap seams (torus).</summary>
        private float GroundDistance(float wx, float wz)
        {
            float dx = Game.SceneX(wx) - Game.PlayerPosition.x;
            float dz = Game.SceneZ(wz) - Game.PlayerPosition.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static (string glyph, Color col, string icon) PoiLook(string type) => type switch
        {
            "settlement" => ("⌂", new Color(0.5f, 0.95f, 0.6f), "map_settlement"),
            "settlement_ruin" => ("⌂", new Color(0.65f, 0.6f, 0.55f), "map_ruin"),
            "vault_ruin" => ("◆", new Color(0.8f, 0.7f, 0.95f), "map_ruin"),
            "wreck" => ("✖", new Color(1f, 0.55f, 0.3f), "map_wreck"),
            "treasure" => ("◈", new Color(1f, 0.8f, 0.25f), null), // NPC-hint reveal; no icon yet → glyph renders
            "bandit_camp" => ("⚑", new Color(1f, 0.45f, 0.35f), null), // bounty-mission reveal (#730); glyph fallback
            "factory" => ("⚒", new Color(1f, 0.7f, 0.45f), "map_station"), // no map_factory art yet — the station icon reads industrial
            "landing" => ("⊕", new Color(0.5f, 0.85f, 1f), "map_pad"),
            _ => ("◆", new Color(0.8f, 0.8f, 0.9f), "map_station"),
        };

        /// <summary>A map marker: a generated HUD ICON when one exists (uGUI icon pass), else the unicode
        /// glyph as fallback — both tinted with the marker colour, drawn on a dark backing disc so the
        /// marker separates from the terrain texture (#592). The icons are pure WHITE ink, so the tint
        /// renders exactly the marker colour (the old cyan-ink set multiplied red tints into grey).</summary>
        private Graphic Marker(float wx, float wz, float size, Color color, string glyph, string icon = null)
        {
            // Round worlds: map the marker's world X/Z to the scene spot nearest the player (no seam on the map).
            float u = (Game.SceneX(wx) - _ox) / _side, v = (Game.SceneZ(wz) - _oz) / _side;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
            {
                return null;
            }

            size *= MarkerScale;

            // Anchor object at the map spot; the disc + icon are children so a caller can rotate the
            // returned graphic (player heading) without the anchor moving.
            var go = new GameObject("Marker", typeof(RectTransform));
            go.transform.SetParent(_mapRt, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(u, v);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);

            float d = size * 1.3f;
            var disc = UiKit.AddImage(go.transform, -(d - size) / 2f, -(d - size) / 2f, d, d, UiKit.DiscSprite, DiscCol);
            disc.raycastTarget = false;

            var sprite = icon != null ? UiKit.Icon(icon) : null;
            if (sprite != null)
            {
                // Centre pivot (not UiKit.Place's top-left) so the player-heading rotation spins in place.
                var igo = new GameObject("Icon", typeof(RectTransform));
                igo.transform.SetParent(go.transform, false);
                var irt = igo.GetComponent<RectTransform>();
                irt.anchorMin = Vector2.zero;
                irt.anchorMax = Vector2.one;
                irt.offsetMin = irt.offsetMax = Vector2.zero;
                var img = igo.AddComponent<Image>();
                img.sprite = sprite;
                img.color = color;
                img.raycastTarget = false;
                return img;
            }

            var tgo = new GameObject("Glyph", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = tgo.AddComponent<Text>();
            t.font = UiKit.Font;
            t.text = glyph;
            t.fontSize = Mathf.RoundToInt(size);
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>Draws a small text label centred just below a marker's world position (beacon names).</summary>
        private void MarkerLabel(float wx, float wz, string text, Color color)
        {
            float u = (Game.SceneX(wx) - _ox) / _side, v = (Game.SceneZ(wz) - _oz) / _side;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
            {
                return;
            }

            var go = new GameObject("MarkerLabel", typeof(RectTransform));
            go.transform.SetParent(_mapRt, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(u, v);
            rt.pivot = new Vector2(0.5f, 1f);            // hang the text below the glyph
            rt.anchoredPosition = new Vector2(0f, -20f * MarkerScale); // clears the bigger #592 markers
            rt.sizeDelta = new Vector2(140f, 18f);
            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.text = text;
            t.fontSize = 14;
            t.color = color;
            t.alignment = TextAnchor.UpperCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }

        /// <summary>Draws a landing-pad glyph (item 38). In view → at its true spot; out of view → pinned to the
        /// map border in the pad's direction (smaller + dimmer), so a far pad is still findable on the local map.</summary>
        private void PadMarker(float wx, float wz, Color color)
        {
            float u = (Game.SceneX(wx) - _ox) / _side, v = (Game.SceneZ(wz) - _oz) / _side;
            bool clamped = u < 0f || u > 1f || v < 0f || v > 1f;
            if (clamped)
            {
                float dx = u - 0.5f, dz = v - 0.5f;
                float m = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                if (m > 0.0001f) { dx *= 0.47f / m; dz *= 0.47f / m; }
                u = 0.5f + dx;
                v = 0.5f + dz;
            }

            var go = new GameObject("PadMarker", typeof(RectTransform));
            go.transform.SetParent(_mapRt, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(u, v);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            float size = (clamped ? 22f : 32f) * MarkerScale;
            rt.sizeDelta = new Vector2(size, size);
            var tint = clamped ? new Color(color.r, color.g, color.b, 0.7f) : color;

            float d = size * 1.3f;
            var discCol = clamped ? new Color(DiscCol.r, DiscCol.g, DiscCol.b, DiscCol.a * 0.7f) : DiscCol;
            UiKit.AddImage(go.transform, -(d - size) / 2f, -(d - size) / 2f, d, d, UiKit.DiscSprite, discCol)
                .raycastTarget = false;

            var sprite = UiKit.Icon("map_pad");
            if (sprite != null)
            {
                var igo = new GameObject("Icon", typeof(RectTransform));
                igo.transform.SetParent(go.transform, false);
                var irt = igo.GetComponent<RectTransform>();
                irt.anchorMin = Vector2.zero;
                irt.anchorMax = Vector2.one;
                irt.offsetMin = irt.offsetMax = Vector2.zero;
                var img = igo.AddComponent<Image>();
                img.sprite = sprite;
                img.color = tint;
                img.raycastTarget = false;
                return;
            }

            var tgo = new GameObject("Glyph", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = tgo.AddComponent<Text>();
            t.font = UiKit.Font;
            t.text = "⊕";
            t.fontSize = Mathf.RoundToInt(size * 0.9f);
            t.color = tint;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }

        public static Color MapColor(string key)
        {
            switch (key)
            {
                case "grass": return new Color(0.30f, 0.55f, 0.25f);
                case "dirt": return new Color(0.45f, 0.33f, 0.20f);
                case "stone": return new Color(0.50f, 0.50f, 0.52f);
                case "sand": return new Color(0.80f, 0.72f, 0.45f);
                case "mud": return new Color(0.35f, 0.28f, 0.20f);
                case "basalt": return new Color(0.24f, 0.24f, 0.27f);
                case "ice": return new Color(0.72f, 0.86f, 0.95f);
                case "water": return new Color(0.20f, 0.40f, 0.70f);
                case "lava": return new Color(0.85f, 0.40f, 0.15f);
                case "crystal": return new Color(0.50f, 0.75f, 0.90f);
                case "iron_wall": return new Color(0.55f, 0.57f, 0.62f);
                case null: return new Color(0.4f, 0.4f, 0.4f);
                default:
                    if (key.StartsWith("flora")) return new Color(0.34f, 0.6f, 0.32f);
                    if (key.EndsWith("_ore")) return new Color(0.55f, 0.5f, 0.42f);
                    return new Color(0.42f, 0.42f, 0.46f);
            }
        }

        private string L(string k) => Game?.Localizer?.Get(k) ?? k;

        private void OnDisable()
        {
            if (_open)
            {
                Close();
            }
        }
    }
}
