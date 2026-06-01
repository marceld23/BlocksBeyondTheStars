using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Minimal IMGUI HUD showing the authoritative vitals (health, oxygen, energy) with
    /// localized labels. A real UI (uGUI/UI Toolkit) replaces this later; this proves the
    /// localized, server-driven values render.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int HotbarSlots = 9;
        private const int SlotSize = 64;

        private GUIStyle _slotNameStyle;

        /// <summary>Small centred label style for the item name shown in each hotbar slot (built lazily during OnGUI).</summary>
        private GUIStyle SlotNameStyle => _slotNameStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 9,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = false,
        };

        private void OnGUI()
        {
            if (Game?.Localizer == null)
            {
                return;
            }

            var loc = Game.Localizer;

            // Centre crosshair (hidden while a UI panel is open).
            if (!Game.MenuOpen)
            {
                float cx = Screen.width / 2f, cy = Screen.height / 2f;
                GUI.DrawTexture(new Rect(cx - 1, cy - 9, 2, 18), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - 9, cy - 1, 18, 2), Texture2D.whiteTexture);
            }

            // Location (top-left): which system / planet the player is on.
            GUI.Box(new Rect(10, 10, 280, 44), GUIContent.none);
            string place = string.IsNullOrEmpty(Game.LocationName) ? "—" : Game.LocationName;
            if (Game.Aboard)
            {
                place += $"  ({loc.Get("ui.hud.aboard")})";
            }

            GUI.Label(new Rect(20, 14, 260, 18), $"📍 {loc.Get("ui.hud.location")}");
            GUI.Label(new Rect(20, 32, 260, 18), place);

            // Vitals (+ ship hull/shield).
            bool ship = Game.ShipCombat != null;
            GUI.Box(new Rect(10, 62, 220, ship ? 200 : 108), GUIContent.none);
            GUI.DrawTexture(new Rect(20, 68, 16, 16), IconFactory.Health);
            GUI.DrawTexture(new Rect(20, 90, 16, 16), IconFactory.Oxygen);
            GUI.DrawTexture(new Rect(20, 112, 16, 16), IconFactory.Energy);
            GUI.Label(new Rect(42, 68, 200, 20), $"{loc.Get("ui.hud.health")}: {Mathf.RoundToInt(Game.Health)}");
            string oxygen = $"{loc.Get("ui.hud.oxygen")}: {Mathf.RoundToInt(Game.Oxygen)}";
            if (Game.Environment != null && Game.Environment.Breathable)
            {
                oxygen += $" ({loc.Get("ui.hud.breathable")})";
            }
            GUI.Label(new Rect(42, 90, 220, 20), oxygen);
            GUI.Label(new Rect(42, 112, 200, 20), $"{loc.Get("ui.hud.energy")}: {Mathf.RoundToInt(Game.SuitEnergy)}");
            GUI.Label(new Rect(42, 134, 200, 20), $"{loc.Get("ui.hud.hunger")}: {Mathf.RoundToInt(Game.Hunger)}");
            if (ship)
            {
                var c = Game.ShipCombat;
                GUI.Label(new Rect(20, 156, 200, 20), $"{loc.Get("ui.hud.hull")}: {Mathf.RoundToInt(c.Hull)}/{Mathf.RoundToInt(c.HullMax)}");
                GUI.Label(new Rect(20, 178, 200, 20), $"{loc.Get("ui.hud.shield")}: {Mathf.RoundToInt(c.Shield)}/{Mathf.RoundToInt(c.ShieldMax)}");
            }

            DrawHotbar(loc);
            DrawShipCompass(loc);
            DrawScanReadout(loc);
            DrawWreckPanel(loc);
            DrawLootPrompt(loc);

            // Server feedback toast (craft result, rejection, message).
            if (!string.IsNullOrEmpty(Game.LastMessage))
            {
                GUI.Label(new Rect(10, 204, Screen.width - 20, 22), Game.LastMessage);
            }

            // In-space indicator.
            if (Game.InSpace)
            {
                var sp = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(Screen.width / 2f - 100, 8, 200, 22), loc.Get("ui.hud.in_space"), sp);
            }

            // Station interaction prompt (when standing next to one and no panel is open).
            if (!Game.MenuOpen && !string.IsNullOrEmpty(Game.NearbyStation))
            {
                var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                string label = $"{loc.Get("ui.hud.use")}: {loc.Get($"ui.station.{Game.NearbyStation}")}";
                GUI.Label(new Rect(Screen.width / 2f - 150, Screen.height / 2f + 24, 300, 22), label, style);
            }
            else if (!Game.MenuOpen && HoldingScanner())
            {
                // Holding the scanner: the primary action (LMB) scans what you look at.
                var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(Screen.width / 2f - 150, Screen.height / 2f + 24, 300, 22), loc.Get("ui.scan.use_hint"), style);
            }

            // Hint.
            GUI.Label(new Rect(10, Screen.height - 22, 600, 20), loc.Get("ui.hud.hint"));
        }

        private void DrawHotbar(Spacecraft.Shared.Localization.Localizer loc)
        {
            float totalWidth = HotbarSlots * SlotSize;
            float x0 = (Screen.width - totalWidth) / 2f;
            float y = Screen.height - SlotSize - 28;

            for (int i = 0; i < HotbarSlots; i++)
            {
                var rect = new Rect(x0 + i * SlotSize, y, SlotSize - 4, SlotSize - 4);
                bool selected = i == Game.SelectedHotbarSlot;
                GUI.Box(rect, GUIContent.none);
                if (selected)
                {
                    // Draw a simple selection frame by overlaying a second box.
                    GUI.Box(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), GUIContent.none);
                }

                string item = Game.ItemInSlot(i);
                GUI.Label(new Rect(rect.x + 4, rect.y + 2, SlotSize - 10, 18), (i + 1).ToString());
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

                    // Item name under the icon, so different items (drill / placer / scanner …) are distinguishable.
                    GUI.Label(new Rect(rect.x + 2, rect.y + SlotSize - 22, SlotSize - 6, 14), ShortName(loc, item), SlotNameStyle);
                }
            }
        }

        private static string ShortName(Spacecraft.Shared.Localization.Localizer loc, string itemKey)
        {
            string name = loc.Get($"item.{itemKey}.name");
            return name.Length > 9 ? name.Substring(0, 8) + "…" : name;
        }

        /// <summary>True if the selected hotbar item is a handheld scanner (its primary action scans).</summary>
        private bool HoldingScanner()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && Game.Content?.GetItem(held)?.Tool?.Kind == Spacecraft.Shared.Definitions.ToolKind.Scanner;
        }

        /// <summary>Bottom-left scanner readout (subject + info + threat + knowledge), auto-hidden a few seconds after a scan.</summary>
        private void DrawScanReadout(Spacecraft.Shared.Localization.Localizer loc)
        {
            var scan = Game.LastScan;
            if (scan == null || Time.time - Game.LastScanAt > 8f)
            {
                return;
            }

            const float w = 280f, h = 92f;
            float x = 10f, y = Screen.height - h - 48f;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none);
            GUI.Label(new Rect(x + 8, y + 4, w - 16, 18), $"🔍 {loc.Get("ui.scan.title")}: {scan.Subject}");
            GUI.Label(new Rect(x + 8, y + 24, w - 16, 18), scan.Info);
            GUI.Label(new Rect(x + 8, y + 44, w - 16, 18), $"{loc.Get("ui.scan.threat")}: {scan.Threat}");
            GUI.Label(new Rect(x + 8, y + 64, w - 16, 18), $"{loc.Get("ui.scan.knowledge")}: {scan.KnowledgeTotal}");
        }

        /// <summary>Right-side wreck repair progress, with a Claim button once the hull is fully restored.</summary>
        private void DrawWreckPanel(Spacecraft.Shared.Localization.Localizer loc)
        {
            var wreck = Game.Wreck;
            if (wreck == null)
            {
                return;
            }

            const float w = 240f, h = 96f;
            float x = Screen.width - w - 10f, y = 140f;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none);
            GUI.Label(new Rect(x + 8, y + 4, w - 16, 18), $"🛠 {loc.Get("ui.wreck.title")}");
            GUI.Label(new Rect(x + 8, y + 24, w - 16, 18), wreck.WreckName);
            int done = wreck.Total - wreck.Remaining;
            GUI.Label(new Rect(x + 8, y + 44, w - 16, 18), $"{loc.Get("ui.wreck.progress")}: {done}/{wreck.Total}");
            if (wreck.Claimable)
            {
                if (GUI.Button(new Rect(x + 8, y + 66, w - 16, 24), loc.Get("ui.action.claim")))
                {
                    Game.Network?.SendClaimWreck();
                }
            }
            else
            {
                GUI.Label(new Rect(x + 8, y + 68, w - 16, 18), loc.Get("ui.wreck.hint"));
            }
        }

        /// <summary>Centre-screen prompt to loot the nearest container (salvage capsule / corpse) with G.</summary>
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

            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(Screen.width / 2f - 150, Screen.height / 2f + 48, 300, 22), $"{loc.Get("ui.hud.loot")} ({nearest.ItemCount})", style);
        }

        /// <summary>
        /// Top-right minimap/compass that always points toward the player's ship, relative to
        /// where the player is facing, with the distance in blocks.
        /// </summary>
        private void DrawShipCompass(Spacecraft.Shared.Localization.Localizer loc)
        {
            const float size = 120f;
            float ox = Screen.width - size - 10f, oy = 10f;
            GUI.Box(new Rect(ox, oy, size, size), GUIContent.none);
            GUI.Label(new Rect(ox + 6, oy + 2, size - 12, 18), $"🚀 {loc.Get("ui.hud.ship")}");

            var center = new Vector2(ox + size / 2f, oy + size / 2f + 6f);
            float radius = size / 2f - 16f;

            // Player marker at the centre + a forward tick (up = where the player looks).
            GUI.DrawTexture(new Rect(center.x - 2, center.y - 2, 4, 4), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(center.x - 1, center.y - radius, 2, 8), Texture2D.whiteTexture);

            if (!Game.ShipPosition.HasValue)
            {
                return;
            }

            var ship = Game.ShipPosition.Value;
            float dx = ship.x - Game.PlayerPosition.x;
            float dz = ship.z - Game.PlayerPosition.z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);

            // Bearing to the ship relative to the player's heading (0 = straight ahead).
            float worldAngle = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            float rel = (worldAngle - Game.PlayerYaw) * Mathf.Deg2Rad;

            float r = Mathf.Clamp(distance * 1.2f, 10f, radius);
            var blip = new Vector2(center.x + Mathf.Sin(rel) * r, center.y - Mathf.Cos(rel) * r);

            // Line from the player to the ship blip (rotated 1px texture).
            var prev = GUI.matrix;
            var prevColor = GUI.color;
            GUI.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            float ang = Mathf.Atan2(blip.y - center.y, blip.x - center.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(ang, center);
            GUI.DrawTexture(new Rect(center.x, center.y - 1, r, 2), Texture2D.whiteTexture);
            GUI.matrix = prev;

            GUI.DrawTexture(new Rect(blip.x - 4, blip.y - 4, 8, 8), Texture2D.whiteTexture);
            GUI.color = prevColor;

            GUI.Label(new Rect(ox, oy + size - 20, size, 18), $"{Mathf.RoundToInt(distance)} m");
        }
    }
}
