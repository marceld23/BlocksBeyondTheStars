using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// In-game gameplay UI (M22), toggled with Tab: inventory + cargo, crafting, blueprint
    /// unlock (Tech) and ship-module build. IMGUI like the rest of the scaffold; every action
    /// sends an existing authoritative intent and the server validates it. While open, the
    /// cursor is freed and the player controller pauses (via <c>GameBootstrap.MenuOpen</c>).
    /// </summary>
    public sealed class GameMenu : MonoBehaviour
    {
        public GameBootstrap Game;

        private enum Tab { Inventory, Crafting, Tech, Ship }

        private Tab _tab = Tab.Inventory;
        private bool _open;
        private Vector2 _scroll;

        private void Update()
        {
            if (Game != null && Input.GetKeyDown(KeyCode.Tab))
            {
                Toggle();
            }
        }

        private void Toggle()
        {
            _open = !_open;
            Game.MenuOpen = _open;
            Cursor.lockState = _open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = _open;
        }

        private void OnGUI()
        {
            if (!_open || Game?.Localizer == null || Game.Content == null)
            {
                return;
            }

            var loc = Game.Localizer;
            const float w = 580f, h = 440f;
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(area, "Spacecraft");

            GUILayout.BeginArea(new Rect(area.x + 12, area.y + 28, w - 24, h - 40));

            GUILayout.BeginHorizontal();
            TabButton(loc.Get("ui.inventory.title"), Tab.Inventory);
            TabButton(loc.Get("ui.crafting.title"), Tab.Crafting);
            TabButton(loc.Get("ui.tab.tech"), Tab.Tech);
            TabButton(loc.Get("ui.tab.ship"), Tab.Ship);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(loc.Get("ui.action.close"), GUILayout.Width(90)))
            {
                Toggle();
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            _scroll = GUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Inventory: DrawInventory(loc); break;
                case Tab.Crafting: DrawCrafting(loc); break;
                case Tab.Tech: DrawTech(loc); break;
                case Tab.Ship: DrawShip(loc); break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void TabButton(string label, Tab tab)
        {
            string text = _tab == tab ? "▸ " + label : label;
            if (GUILayout.Button(text, GUILayout.Width(110)))
            {
                _tab = tab;
            }
        }

        private void DrawInventory(Spacecraft.Shared.Localization.Localizer loc)
        {
            GUILayout.Label(loc.Get("ui.inventory.title"));
            foreach (var s in Game.Personal)
            {
                GUILayout.Label($"  [{s.Slot}] {ItemName(loc, s.Item)}  ×{s.Count}");
            }

            GUILayout.Space(10);
            GUILayout.Label(loc.Get("ui.cargo.title"));
            if (Game.Cargo.Length == 0)
            {
                GUILayout.Label("  —");
            }

            foreach (var s in Game.Cargo)
            {
                GUILayout.Label($"  {ItemName(loc, s.Item)}  ×{s.Count}");
            }
        }

        private void DrawCrafting(Spacecraft.Shared.Localization.Localizer loc)
        {
            foreach (var r in Game.Content.Recipes.Values)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(RecipeLabel(loc, r), GUILayout.Width(400));
                if (GUILayout.Button(loc.Get("ui.action.craft"), GUILayout.Width(100)))
                {
                    Game.Network.SendCraft(r.Key, 1);
                }

                GUILayout.EndHorizontal();
            }
        }

        private void DrawTech(Spacecraft.Shared.Localization.Localizer loc)
        {
            foreach (var bp in Game.Content.Blueprints.Values)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{loc.Get($"blueprint.{bp.Key}.name")}  ({CostLabel(loc, bp.UnlockCost)})", GUILayout.Width(400));
                if (GUILayout.Button(loc.Get("ui.action.unlock"), GUILayout.Width(100)))
                {
                    Game.Network.SendUnlock(bp.Key);
                }

                GUILayout.EndHorizontal();
            }
        }

        private void DrawShip(Spacecraft.Shared.Localization.Localizer loc)
        {
            foreach (var m in Game.Content.ShipModules.Values)
            {
                if (m.Mandatory)
                {
                    continue; // mandatory modules are part of the starter ship
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{loc.Get($"module.{m.Key}.name")}  ({CostLabel(loc, m.BuildCost)})", GUILayout.Width(400));
                if (GUILayout.Button(loc.Get("ui.action.build"), GUILayout.Width(100)))
                {
                    Game.Network.SendBuildModule(m.Key);
                }

                GUILayout.EndHorizontal();
            }
        }

        private static string ItemName(Spacecraft.Shared.Localization.Localizer loc, string itemKey)
            => loc.Get($"item.{itemKey}.name");

        private string RecipeLabel(Spacecraft.Shared.Localization.Localizer loc, Spacecraft.Shared.Definitions.RecipeDefinition r)
        {
            string outputs = string.Empty;
            foreach (var o in r.Outputs)
            {
                outputs += (outputs.Length > 0 ? ", " : string.Empty) + $"{ItemName(loc, o.Item)}×{o.Count}";
            }

            return $"{outputs}  ⟵  {CostLabel(loc, r.Inputs)}";
        }

        private string CostLabel(Spacecraft.Shared.Localization.Localizer loc, System.Collections.Generic.IEnumerable<Spacecraft.Shared.Definitions.ItemAmount> cost)
        {
            string s = string.Empty;
            foreach (var c in cost)
            {
                s += (s.Length > 0 ? ", " : string.Empty) + $"{ItemName(loc, c.Item)}×{c.Count}";
            }

            return s.Length == 0 ? "—" : s;
        }
    }
}
