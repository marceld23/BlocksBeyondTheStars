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

        private void OnGUI()
        {
            if (Game?.Localizer == null)
            {
                return;
            }

            var loc = Game.Localizer;
            GUI.Box(new Rect(10, 10, 220, 86), GUIContent.none);
            GUI.Label(new Rect(20, 16, 200, 20), $"{loc.Get("ui.hud.health")}: {Mathf.RoundToInt(Game.Health)}");
            GUI.Label(new Rect(20, 38, 200, 20), $"{loc.Get("ui.hud.oxygen")}: {Mathf.RoundToInt(Game.Oxygen)}");
            GUI.Label(new Rect(20, 60, 200, 20), $"{loc.Get("ui.hud.energy")}: {Mathf.RoundToInt(Game.SuitEnergy)}");
        }
    }
}
