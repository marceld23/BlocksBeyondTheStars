using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// In-game HUD (M27 themed): authoritative vitals as bars, a framed hotbar, location + ship
    /// compass, server-feedback toasts and the scan/wreck/loot panels — all in the sci-fi cyan-on
    /// -deep-blue style of the uGUI menu (drawn in IMGUI for low-risk real-time rendering, sharing
    /// the <see cref="UiKit"/> palette). Server-driven values only; presentation.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int HotbarSlots = 9;
        private const int SlotSize = 64;

        private static readonly Color PanelFill = new Color(0.05f, 0.12f, 0.24f, 0.82f);
        private static readonly Color BarBg = new Color(0.03f, 0.07f, 0.13f, 0.9f);
        private static readonly Color Health = new Color(0.92f, 0.32f, 0.34f);
        private static readonly Color Oxygen = new Color(0.36f, 0.78f, 1f);
        private static readonly Color Energy = new Color(1f, 0.82f, 0.25f);
        private static readonly Color Hunger = new Color(1f, 0.6f, 0.25f);
        private static readonly Color Hull = new Color(0.6f, 0.66f, 0.74f);
        private static readonly Color Shield = new Color(0.4f, 0.7f, 1f);

        private GUIStyle _label, _cyan, _bar, _slot, _center;

        private void EnsureStyles()
        {
            if (_label != null)
            {
                return;
            }

            _label = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = UiKit.TextCol } };
            _cyan = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = UiKit.Cyan } };
            _bar = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 2, 0, 0), normal = { textColor = UiKit.TextCol } };
            _slot = new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.MiddleCenter, normal = { textColor = UiKit.TextCol } };
            _center = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UiKit.Cyan } };
        }

        private void OnGUI()
        {
            if (Game?.Localizer == null)
            {
                return;
            }

            EnsureStyles();
            var loc = Game.Localizer;
            UiScale.Begin(); // lay out in a virtual 1080p space so the HUD scales with resolution

            // Centre crosshair (hidden while a UI panel is open).
            if (!Game.MenuOpen)
            {
                float cx = UiScale.Width / 2f, cy = UiScale.Height / 2f;
                var prev = GUI.color;
                GUI.color = UiKit.Cyan;
                GUI.DrawTexture(new Rect(cx - 1, cy - 9, 2, 18), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - 9, cy - 1, 18, 2), Texture2D.whiteTexture);
                GUI.color = prev;
            }

            // Location (top-left).
            Frame(new Rect(10, 10, 280, 46));
            string place = string.IsNullOrEmpty(Game.LocationName) ? "—" : Game.LocationName;
            if (Game.Aboard)
            {
                place += $"  ({loc.Get("ui.hud.aboard")})";
            }

            GUI.Label(new Rect(20, 13, 260, 18), loc.Get("ui.hud.location").ToUpperInvariant(), _cyan);
            GUI.Label(new Rect(20, 32, 260, 18), place, _label);

            // Vitals (+ ship hull/shield) as bars.
            bool ship = Game.ShipCombat != null;
            Frame(new Rect(10, 64, 226, ship ? 196f : 116f));
            float vy = 72f;
            Vital(20, vy, IconFactory.Health, loc.Get("ui.hud.health"), Game.Health, Game.Health / 100f, Health); vy += 24f;
            string oxy = loc.Get("ui.hud.oxygen") + (Game.Environment != null && Game.Environment.Breathable ? " *" : string.Empty);
            Vital(20, vy, IconFactory.Oxygen, oxy, Game.Oxygen, Game.Oxygen / 100f, Oxygen); vy += 24f;
            Vital(20, vy, IconFactory.Energy, loc.Get("ui.hud.energy"), Game.SuitEnergy, Game.SuitEnergy / 100f, Energy); vy += 24f;
            Vital(20, vy, null, loc.Get("ui.hud.hunger"), Game.Hunger, Game.Hunger / 100f, Hunger); vy += 26f;
            if (ship)
            {
                var c = Game.ShipCombat;
                Vital(20, vy, null, loc.Get("ui.hud.hull"), c.Hull, c.HullMax > 0 ? c.Hull / c.HullMax : 0f, Hull); vy += 24f;
                Vital(20, vy, null, loc.Get("ui.hud.shield"), c.Shield, c.ShieldMax > 0 ? c.Shield / c.ShieldMax : 0f, Shield);
            }

            DrawHotbar(loc);
            DrawShipCompass(loc);
            DrawScanReadout(loc);
            DrawWreckPanel(loc);
            DrawLootPrompt(loc);

            // Server feedback toast.
            if (!string.IsNullOrEmpty(Game.LastMessage))
            {
                GUI.Label(new Rect(14, 268, UiScale.Width - 28, 22), Game.LastMessage, _cyan);
            }

            // In-space indicator.
            if (Game.InSpace)
            {
                GUI.Label(new Rect(UiScale.Width / 2f - 100, 8, 200, 22), loc.Get("ui.hud.in_space"), _center);
            }

            // Centre interaction prompt: station, else scanner-in-hand.
            if (!Game.MenuOpen && !string.IsNullOrEmpty(Game.NearbyStation))
            {
                GUI.Label(new Rect(UiScale.Width / 2f - 150, UiScale.Height / 2f + 24, 300, 22),
                    $"{loc.Get("ui.hud.use")}: {loc.Get($"ui.station.{Game.NearbyStation}")}", _center);
            }
            else if (!Game.MenuOpen && HoldingScanner())
            {
                GUI.Label(new Rect(UiScale.Width / 2f - 150, UiScale.Height / 2f + 24, 300, 22), loc.Get("ui.scan.use_hint"), _center);
            }

            // Hint.
            GUI.Label(new Rect(12, UiScale.Height - 24, 1000, 20), loc.Get("ui.hud.hint"), _label);

            UiScale.End();
        }

        /// <summary>One vital row: optional icon + a value bar with an overlaid "label value" caption.</summary>
        private void Vital(float x, float y, Texture icon, string label, float value, float frac, Color color)
        {
            if (icon != null)
            {
                GUI.DrawTexture(new Rect(x, y, 16, 16), icon);
            }

            var bar = new Rect(x + 22, y, 178, 16);
            Bar(bar, frac, color);
            GUI.Label(bar, $"{label}  {Mathf.RoundToInt(value)}", _bar);
        }

        private void DrawHotbar(Spacecraft.Shared.Localization.Localizer loc)
        {
            float totalWidth = HotbarSlots * SlotSize;
            float x0 = (UiScale.Width - totalWidth) / 2f;
            float y = UiScale.Height - SlotSize - 28;

            for (int i = 0; i < HotbarSlots; i++)
            {
                var rect = new Rect(x0 + i * SlotSize, y, SlotSize - 4, SlotSize - 4);
                bool selected = i == Game.SelectedHotbarSlot;
                Frame(rect);
                if (selected)
                {
                    Border(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), UiKit.Cyan, 2);
                }

                string item = Game.ItemInSlot(i);
                GUI.Label(new Rect(rect.x + 4, rect.y + 2, SlotSize - 10, 18), (i + 1).ToString(), _slot);
                if (!string.IsNullOrEmpty(item))
                {
                    var iconRect = new Rect(rect.x + 16, rect.y + 14, SlotSize - 36, SlotSize - 36);
                    var blockDef = Game.Content?.GetBlock(item);
                    if (blockDef != null && Game.Atlas != null)
                    {
                        GUI.DrawTextureWithTexCoords(iconRect, Game.Atlas.Texture, Game.Atlas.TileUv(blockDef.NumericId.Value));
                    }
                    else
                    {
                        var kind = Game.Content?.GetItem(item)?.Tool?.Kind ?? Spacecraft.Shared.Definitions.ToolKind.None;
                        GUI.DrawTexture(iconRect, IconFactory.ForItem(item, kind));
                    }

                    GUI.Label(new Rect(rect.x + 2, rect.y + SlotSize - 22, SlotSize - 6, 14), ShortName(loc, item), _slot);
                }
            }
        }

        private static string ShortName(Spacecraft.Shared.Localization.Localizer loc, string itemKey)
        {
            string name = loc.Get($"item.{itemKey}.name");
            return name.Length > 9 ? name.Substring(0, 8) + "…" : name;
        }

        private bool HoldingScanner()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && Game.Content?.GetItem(held)?.Tool?.Kind == Spacecraft.Shared.Definitions.ToolKind.Scanner;
        }

        private void DrawScanReadout(Spacecraft.Shared.Localization.Localizer loc)
        {
            var scan = Game.LastScan;
            if (scan == null || Time.time - Game.LastScanAt > 8f)
            {
                return;
            }

            const float w = 290f, h = 96f;
            float x = 10f, y = UiScale.Height - h - 48f;
            Frame(new Rect(x, y, w, h));
            GUI.Label(new Rect(x + 10, y + 6, w - 18, 18), $"{loc.Get("ui.scan.title").ToUpperInvariant()}: {scan.Subject}", _cyan);
            GUI.Label(new Rect(x + 10, y + 28, w - 18, 18), scan.Info, _label);
            GUI.Label(new Rect(x + 10, y + 48, w - 18, 18), $"{loc.Get("ui.scan.threat")}: {scan.Threat}", _label);
            GUI.Label(new Rect(x + 10, y + 68, w - 18, 18), $"{loc.Get("ui.scan.knowledge")}: {scan.KnowledgeTotal}", _label);
        }

        private void DrawWreckPanel(Spacecraft.Shared.Localization.Localizer loc)
        {
            var wreck = Game.Wreck;
            if (wreck == null)
            {
                return;
            }

            const float w = 250f, h = 100f;
            float x = UiScale.Width - w - 10f, y = 140f;
            Frame(new Rect(x, y, w, h));
            GUI.Label(new Rect(x + 10, y + 6, w - 18, 18), loc.Get("ui.wreck.title").ToUpperInvariant(), _cyan);
            GUI.Label(new Rect(x + 10, y + 26, w - 18, 18), wreck.WreckName, _label);
            int done = wreck.Total - wreck.Remaining;
            Bar(new Rect(x + 10, y + 48, w - 20, 14), wreck.Total > 0 ? done / (float)wreck.Total : 0f, UiKit.Cyan);
            GUI.Label(new Rect(x + 10, y + 47, w - 20, 16), $"{loc.Get("ui.wreck.progress")}  {done}/{wreck.Total}", _bar);
            if (wreck.Claimable)
            {
                if (GUI.Button(new Rect(x + 10, y + 70, w - 20, 24), loc.Get("ui.action.claim")))
                {
                    Game.Network?.SendClaimWreck();
                }
            }
            else
            {
                GUI.Label(new Rect(x + 10, y + 72, w - 18, 18), loc.Get("ui.wreck.hint"), _label);
            }
        }

        private void DrawLootPrompt(Spacecraft.Shared.Localization.Localizer loc)
        {
            if (Game.MenuOpen)
            {
                return;
            }

            Spacecraft.Networking.Messages.NetContainer nearest = null;
            float bestSq = 6f * 6f;
            foreach (var c in Game.Containers)
            {
                float dx = c.X + 0.5f - Game.PlayerPosition.x;
                float dy = c.Y + 0.5f - Game.PlayerPosition.y;
                float dz = c.Z + 0.5f - Game.PlayerPosition.z;
                float d = dx * dx + dy * dy + dz * dz;
                if (d < bestSq)
                {
                    bestSq = d;
                    nearest = c;
                }
            }

            if (nearest == null)
            {
                return;
            }

            GUI.Label(new Rect(UiScale.Width / 2f - 150, UiScale.Height / 2f + 48, 300, 22), $"{loc.Get("ui.hud.loot")} ({nearest.ItemCount})", _center);
        }

        /// <summary>Top-right minimap/compass that always points toward the player's ship, with the distance.</summary>
        private void DrawShipCompass(Spacecraft.Shared.Localization.Localizer loc)
        {
            const float size = 120f;
            float ox = UiScale.Width - size - 10f, oy = 10f;
            Frame(new Rect(ox, oy, size, size));
            GUI.Label(new Rect(ox + 8, oy + 3, size - 14, 18), loc.Get("ui.hud.ship").ToUpperInvariant(), _cyan);

            var center = new Vector2(ox + size / 2f, oy + size / 2f + 6f);
            float radius = size / 2f - 16f;

            var pc = GUI.color;
            GUI.color = UiKit.TextCol;
            GUI.DrawTexture(new Rect(center.x - 2, center.y - 2, 4, 4), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(center.x - 1, center.y - radius, 2, 8), Texture2D.whiteTexture);
            GUI.color = pc;

            if (!Game.ShipPosition.HasValue)
            {
                return;
            }

            var ship = Game.ShipPosition.Value;
            float dx = ship.x - Game.PlayerPosition.x;
            float dz = ship.z - Game.PlayerPosition.z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);

            float worldAngle = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            float rel = (worldAngle - Game.PlayerYaw) * Mathf.Deg2Rad;

            float r = Mathf.Clamp(distance * 1.2f, 10f, radius);
            var blip = new Vector2(center.x + Mathf.Sin(rel) * r, center.y - Mathf.Cos(rel) * r);

            var prev = GUI.matrix;
            var prevColor = GUI.color;
            GUI.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            float ang = Mathf.Atan2(blip.y - center.y, blip.x - center.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(ang, center);
            GUI.DrawTexture(new Rect(center.x, center.y - 1, r, 2), Texture2D.whiteTexture);
            GUI.matrix = prev;

            GUI.DrawTexture(new Rect(blip.x - 4, blip.y - 4, 8, 8), Texture2D.whiteTexture);
            GUI.color = prevColor;

            GUI.Label(new Rect(ox, oy + size - 20, size, 18), $"{Mathf.RoundToInt(distance)} m", _center);
        }

        // --- themed primitives ---

        private static void Frame(Rect r)
        {
            var prev = GUI.color;
            GUI.color = PanelFill;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
            Border(r, UiKit.Cyan, 1);
        }

        private static void Border(Rect r, Color color, float t)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private static void Bar(Rect r, float frac, Color color)
        {
            var prev = GUI.color;
            GUI.color = BarBg;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(frac), r.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
