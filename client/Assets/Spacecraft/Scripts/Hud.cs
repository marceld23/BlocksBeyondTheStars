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
            GUI.Label(new Rect(20, 14, 260, 18), $"📍 {loc.Get("ui.hud.location")}");
            GUI.Label(new Rect(20, 32, 260, 18), string.IsNullOrEmpty(Game.LocationName) ? "—" : Game.LocationName);

            // Vitals.
            GUI.Box(new Rect(10, 62, 220, 86), GUIContent.none);
            GUI.Label(new Rect(20, 68, 200, 20), $"{loc.Get("ui.hud.health")}: {Mathf.RoundToInt(Game.Health)}");
            GUI.Label(new Rect(20, 90, 200, 20), $"{loc.Get("ui.hud.oxygen")}: {Mathf.RoundToInt(Game.Oxygen)}");
            GUI.Label(new Rect(20, 112, 200, 20), $"{loc.Get("ui.hud.energy")}: {Mathf.RoundToInt(Game.SuitEnergy)}");

            DrawHotbar(loc);

            // Server feedback toast (craft result, rejection, message).
            if (!string.IsNullOrEmpty(Game.LastMessage))
            {
                GUI.Label(new Rect(10, 154, Screen.width - 20, 22), Game.LastMessage);
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
                    GUI.Label(new Rect(rect.x + 2, rect.y + 20, SlotSize - 8, 34), ShortName(loc, item));
                }
            }
        }

        private static string ShortName(Spacecraft.Shared.Localization.Localizer loc, string itemKey)
        {
            string name = loc.Get($"item.{itemKey}.name");
            return name.Length > 12 ? name.Substring(0, 12) : name;
        }
    }
}
