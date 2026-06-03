using UnityEngine;
using UnityEngine.UI;
using Spacecraft.Shared.World;

namespace Spacecraft.Client
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

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.M))
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
            Game.MenuOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Build();
        }

        private void Close()
        {
            _open = false;
            Game.MenuOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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
            _info = UiKit.AddText(root, ix, iy, 840, 600, string.Empty, 20, UiKit.TextCol, TextAnchor.UpperLeft);
            _info.horizontalOverflow = HorizontalWrapMode.Wrap;
            _info.verticalOverflow = VerticalWrapMode.Overflow;

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
                for (int lx = 0; lx < n; lx++)
                for (int lz = 0; lz < n; lz++)
                {
                    int wx = o.X + lx, wz = o.Z + lz;
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

                var col = MapColor(Game.Content.BlockById(new Spacecraft.Shared.Primitives.BlockId(block[i]))?.Key);
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

            // Ship + stations + waypoint as dots; player as a heading arrow.
            if (Game.ShipPosition.HasValue)
            {
                Marker(Game.ShipPosition.Value.x, Game.ShipPosition.Value.z, 18f, new Color(0.5f, 0.9f, 1f), "▣");
            }

            if (Game.Waypoint.HasValue)
            {
                Marker(Game.Waypoint.Value.x, Game.Waypoint.Value.z, 22f, new Color(1f, 0.85f, 0.3f), "✛");
            }

            // World POIs (settlements, …) from the server.
            var poiLines = new System.Text.StringBuilder();
            if (Game.PlanetPois != null)
            {
                foreach (var p in Game.PlanetPois)
                {
                    var (glyph, col) = PoiLook(p.Type);
                    Marker(p.X, p.Z, 22f, col, glyph);
                    float d = Vector3.Distance(new Vector3(Game.PlayerPosition.x, 0, Game.PlayerPosition.z), new Vector3(p.X, 0, p.Z));
                    poiLines.Append($"\n{glyph} {p.Name}  —  {Mathf.RoundToInt(d)} m");
                }
            }

            // Ship station tiles (workshop / lab / medbay / …) as small dots.
            if (Game.Stations != null)
            {
                foreach (var s in Game.Stations)
                {
                    Marker(s.X, s.Z, 11f, new Color(0.55f, 0.8f, 1f, 0.85f), "•");
                }
            }

            // Player arrow (rotated to heading; ▲ points north / +Z).
            var pa = Marker(Game.PlayerPosition.x, Game.PlayerPosition.z, 26f, UiKit.Cyan, "▲");
            if (pa != null)
            {
                pa.transform.localRotation = Quaternion.Euler(0, 0, -Game.PlayerYaw);
            }

            string wp = Game.Waypoint.HasValue
                ? $"\n{L("ui.map.waypoint")}: {Mathf.RoundToInt(Vector3.Distance(new Vector3(Game.PlayerPosition.x, 0, Game.PlayerPosition.z), new Vector3(Game.Waypoint.Value.x, 0, Game.Waypoint.Value.z)))} m"
                : string.Empty;
            if (_info != null)
            {
                string pois = poiLines.Length > 0 ? $"\n\n{L("ui.map.pois")}:{poiLines}" : string.Empty;
                _info.text =
                    $"{L("ui.map.you")}: X {Mathf.RoundToInt(Game.PlayerPosition.x)}  Z {Mathf.RoundToInt(Game.PlayerPosition.z)}\n" +
                    $"{L("ui.map.scale")}: ±{_radius} m\n" +
                    $"▲ {L("ui.map.you")}    ▣ {L("ui.hud.ship")}    ✛ {L("ui.map.waypoint")}\n" +
                    $"{L("ui.map.click_hint")}{wp}{pois}";
            }
        }

        private static (string glyph, Color col) PoiLook(string type) => type switch
        {
            "settlement" => ("⌂", new Color(0.5f, 0.95f, 0.6f)),
            "settlement_ruin" => ("⌂", new Color(0.65f, 0.6f, 0.55f)),
            "wreck" => ("✖", new Color(1f, 0.55f, 0.3f)),
            "landing" => ("⊕", new Color(0.5f, 0.85f, 1f)),
            _ => ("◆", new Color(0.8f, 0.8f, 0.9f)),
        };

        private Text Marker(float wx, float wz, float size, Color color, string glyph)
        {
            float u = (wx - _ox) / _side, v = (wz - _oz) / _side;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
            {
                return null;
            }

            var go = new GameObject("Marker", typeof(RectTransform));
            go.transform.SetParent(_mapRt, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(u, v);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.text = glyph;
            t.fontSize = Mathf.RoundToInt(size);
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
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
