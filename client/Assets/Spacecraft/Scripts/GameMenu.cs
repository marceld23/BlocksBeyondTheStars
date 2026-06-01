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
        public ClientSettings Settings;   // for in-game character customization
        public PlayerAvatar Avatar;       // local avatar, recoloured live

        private enum Tab { Inventory, Crafting, Tech, Ship, Map, Missions, Character, Space }

        private Tab _tab = Tab.Inventory;
        private bool _open;
        private Vector2 _scroll;

        private void Update()
        {
            if (Game != null && Input.GetKeyDown(KeyCode.Tab))
            {
                SetOpen(!_open);
            }
        }

        // Public entry points used by station interactions (cockpit → map, etc.).
        public void OpenInventory() => OpenAt(Tab.Inventory);
        public void OpenCrafting() => OpenAt(Tab.Crafting);
        public void OpenMap() => OpenAt(Tab.Map);
        public void OpenMissions() => OpenAt(Tab.Missions);

        private void OpenAt(Tab tab)
        {
            SwitchTo(tab);
            SetOpen(true);
        }

        private void SetOpen(bool open)
        {
            _open = open;
            Game.MenuOpen = _open;
            Cursor.lockState = _open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = _open;
            if (_open)
            {
                SwitchTo(_tab); // refresh data for the current tab
            }
        }

        /// <summary>Switches tab and (re)requests server data for data-driven tabs.</summary>
        private void SwitchTo(Tab tab)
        {
            _tab = tab;
            if (tab == Tab.Map)
            {
                Game.Network?.SendRequestStarMap();
            }
            else if (tab == Tab.Missions)
            {
                Game.Network?.SendRequestMissions();
            }
        }

        private void OnGUI()
        {
            if (!_open || Game?.Localizer == null || Game.Content == null)
            {
                return;
            }

            var loc = Game.Localizer;
            const float w = 760f, h = 560f;

            // Resolution-independent scaling: lay the menu out in a virtual 1080p space (centred), so
            // it is comfortably large and keeps its physical size on high-DPI / 4K screens.
            UiScale.Begin();
            var area = new Rect((UiScale.Width - w) / 2f, (UiScale.Height - h) / 2f, w, h);

            // Themed panel (deep-blue fill + cyan border) + a cyan tint for the IMGUI controls.
            var prevBg = GUI.backgroundColor;
            var prevContent = GUI.contentColor;
            var prevColor = GUI.color;
            GUI.color = new Color(0.05f, 0.12f, 0.24f, 0.94f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = UiKit.Cyan;
            GUI.DrawTexture(new Rect(area.x, area.y, area.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(area.x, area.yMax - 2f, area.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(area.x, area.y, 2f, area.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(area.xMax - 2f, area.y, 2f, area.height), Texture2D.whiteTexture);
            GUI.color = prevColor;

            GUI.contentColor = UiKit.Cyan;
            GUI.Label(new Rect(area.x + 14f, area.y + 5f, w - 28f, 20f), "SPACECRAFT");
            GUI.contentColor = UiKit.TextCol;
            GUI.backgroundColor = new Color(0.35f, 0.7f, 1f, 1f);

            GUILayout.BeginArea(new Rect(area.x + 12, area.y + 28, w - 24, h - 40));

            GUILayout.BeginHorizontal();
            TabButton(loc.Get("ui.inventory.title"), Tab.Inventory);
            TabButton(loc.Get("ui.crafting.title"), Tab.Crafting);
            TabButton(loc.Get("ui.tab.tech"), Tab.Tech);
            TabButton(loc.Get("ui.tab.ship"), Tab.Ship);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(loc.Get("ui.action.close"), GUILayout.Width(80)))
            {
                SetOpen(false);
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            TabButton(loc.Get("ui.tab.map"), Tab.Map);
            TabButton(loc.Get("ui.tab.missions"), Tab.Missions);
            TabButton(loc.Get("ui.settings.character"), Tab.Character);
            TabButton(loc.Get("ui.tab.space"), Tab.Space);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            _scroll = GUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Inventory: DrawInventory(loc); break;
                case Tab.Crafting: DrawCrafting(loc); break;
                case Tab.Tech: DrawTech(loc); break;
                case Tab.Ship: DrawShip(loc); break;
                case Tab.Map: DrawMap(loc); break;
                case Tab.Missions: DrawMissions(loc); break;
                case Tab.Character: DrawCharacter(loc); break;
                case Tab.Space: DrawSpace(loc); break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            GUI.backgroundColor = prevBg;
            GUI.contentColor = prevContent;
            UiScale.End();
        }

        private void TabButton(string label, Tab tab)
        {
            string text = _tab == tab ? "▸ " + label : label;
            if (GUILayout.Button(text, GUILayout.Width(92)))
            {
                SwitchTo(tab);
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

            // --- Disassemble crafted gear back into ~half of its components (needs a workshop) ---
            GUILayout.Space(10);
            GUILayout.Label(loc.Get("ui.crafting.disassemble"));
            bool any = false;
            foreach (var s in Game.Personal)
            {
                if (!IsCraftable(s.Item))
                {
                    continue; // raw materials have no recipe to reverse
                }

                any = true;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{ItemName(loc, s.Item)} ×{s.Count}", GUILayout.Width(400));
                if (GUILayout.Button(loc.Get("ui.action.disassemble"), GUILayout.Width(100)))
                {
                    Game.Network.SendDisassemble(s.Item);
                }

                GUILayout.EndHorizontal();
            }

            if (!any)
            {
                GUILayout.Label("  —");
            }
        }

        /// <summary>True if some recipe produces this item (so the workshop can disassemble it back into parts).</summary>
        private bool IsCraftable(string itemKey)
        {
            foreach (var r in Game.Content.Recipes.Values)
            {
                foreach (var o in r.Outputs)
                {
                    if (o.Item == itemKey)
                    {
                        return true;
                    }
                }
            }

            return false;
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

            // --- Fleet: craft ship types + switch the active ship ---
            GUILayout.Space(10);
            GUILayout.Label(loc.Get("ui.ships.fleet"));
            foreach (var ship in Game.OwnedShips)
            {
                GUILayout.BeginHorizontal();
                string tag = ship.Active ? "  ▸ " : "    ";
                GUILayout.Label($"{tag}{loc.Get($"ship.{ship.Type}.name")}", GUILayout.Width(400));
                if (!ship.Active && GUILayout.Button(loc.Get("ui.ships.switch"), GUILayout.Width(100)))
                {
                    Game.Network.SendSwitchShip(ship.Id);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.Label(loc.Get("ui.ships.craft_new"));
            foreach (var s in Game.Content.Ships.Values)
            {
                if (string.IsNullOrEmpty(s.RequiredBlueprint))
                {
                    continue; // the starter type is already owned
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{loc.Get($"ship.{s.Key}.name")}  ({CostLabel(loc, s.CraftCost)})", GUILayout.Width(400));
                if (GUILayout.Button(loc.Get("ui.action.craft"), GUILayout.Width(100)))
                {
                    Game.Network.SendCraftShip(s.Key);
                }

                GUILayout.EndHorizontal();
            }
        }

        private void DrawMap(Spacecraft.Shared.Localization.Localizer loc)
        {
            var map = Game.StarMap;
            if (map == null || map.Systems.Length == 0)
            {
                GUILayout.Label(loc.Get("ui.map.loading"));
                return;
            }

            foreach (var sys in map.Systems)
            {
                GUILayout.Label($"★ {sys.Name}");
                foreach (var b in sys.Bodies)
                {
                    bool here = b.Id == map.ActiveLocationId;
                    string marker = here ? "  ▸ " : "    ";
                    GUILayout.Label($"{marker}{b.Name}  [{b.Kind}]  {(here ? loc.Get("ui.map.here") : b.Status)}");
                }

                GUILayout.Space(4);
            }
        }

        private void DrawMissions(Spacecraft.Shared.Localization.Localizer loc)
        {
            var list = Game.Missions;
            if (list == null)
            {
                GUILayout.Label(loc.Get("ui.map.loading"));
                return;
            }

            GUILayout.Label(loc.Get("ui.missions.available"));
            foreach (var m in list.Available)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{m.Title}", GUILayout.Width(400));
                if (GUILayout.Button(loc.Get("ui.action.accept"), GUILayout.Width(100)))
                {
                    Game.Network.SendAcceptMission(m.Id);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label(loc.Get("ui.missions.active"));
            foreach (var m in list.Active)
            {
                GUILayout.BeginHorizontal();
                string progress = m.Objectives.Length > 0 ? $" ({m.Objectives[0].Progress}/{m.Objectives[0].Required})" : string.Empty;
                GUILayout.Label($"{m.Title}{progress}", GUILayout.Width(400));
                if (GUILayout.Button(loc.Get("ui.action.turn_in"), GUILayout.Width(100)))
                {
                    Game.Network.SendTurnInMission(m.Id);
                }

                GUILayout.EndHorizontal();
            }
        }

        private void DrawSpace(Spacecraft.Shared.Localization.Localizer loc)
        {
            var c = Game.ShipCombat;
            if (c != null)
            {
                GUILayout.Label($"{loc.Get("ui.hud.hull")}: {Mathf.RoundToInt(c.Hull)}/{Mathf.RoundToInt(c.HullMax)}    " +
                                $"{loc.Get("ui.hud.shield")}: {Mathf.RoundToInt(c.Shield)}/{Mathf.RoundToInt(c.ShieldMax)}");
                GUILayout.Space(6);
            }

            if (!Game.InSpace)
            {
                if (GUILayout.Button(loc.Get("ui.space.enter"), GUILayout.Width(200)))
                {
                    Game.Network?.SendEnterSpace();
                }

                return;
            }

            if (GUILayout.Button(loc.Get("ui.space.leave"), GUILayout.Width(200)))
            {
                Game.Network?.SendLeaveSpace();
            }

            GUILayout.Space(6);
            var space = Game.Space;
            if (space == null || space.Entities.Length == 0)
            {
                GUILayout.Label("—");
                return;
            }

            foreach (var e in space.Entities)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{e.Kind}  ({Mathf.RoundToInt(e.Hull)}/{Mathf.RoundToInt(e.HullMax)})", GUILayout.Width(300));

                // Asteroids need the mining beam; hostiles need a combat cannon.
                string weapon = e.Kind == "Asteroid" ? "asteroid_breaker" : "ship_cannon_1";
                if (GUILayout.Button(loc.Get("ui.action.fire"), GUILayout.Width(110)))
                {
                    ClientAudio.Instance?.Cue("ship_weapon");
                    Game.Network?.SendFireWeapon(weapon, e.Id);
                }

                GUILayout.EndHorizontal();
            }
        }

        private static readonly Color[] Palette =
        {
            new Color(0.85f, 0.68f, 0.55f), new Color(0.55f, 0.40f, 0.28f), new Color(0.90f, 0.85f, 0.80f),
            new Color(0.80f, 0.20f, 0.20f), new Color(0.20f, 0.45f, 0.80f), new Color(0.20f, 0.65f, 0.35f),
            new Color(0.90f, 0.75f, 0.20f), new Color(0.55f, 0.30f, 0.70f), new Color(0.25f, 0.25f, 0.32f),
            new Color(0.92f, 0.92f, 0.95f),
        };

        private void DrawCharacter(Spacecraft.Shared.Localization.Localizer loc)
        {
            if (Settings == null)
            {
                GUILayout.Label("—");
                return;
            }

            ColorRow(loc, loc.Get("ui.settings.skin"), ref Settings.SkinColor);
            ColorRow(loc, loc.Get("ui.settings.torso"), ref Settings.TorsoColor);
            ColorRow(loc, loc.Get("ui.settings.arms"), ref Settings.ArmColor);
            ColorRow(loc, loc.Get("ui.settings.legs"), ref Settings.LegColor);
        }

        private void ColorRow(Spacecraft.Shared.Localization.Localizer loc, string label, ref Color color)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(120));
            if (GUILayout.Button(loc.Get("ui.settings.next_color"), GUILayout.Width(140)))
            {
                color = NextColor(color);
                ApplyAppearance();
            }

            var rect = GUILayoutUtility.GetRect(48, 20, GUILayout.Width(48));
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
            GUILayout.EndHorizontal();
        }

        /// <summary>Applies the edited colours to the local avatar, persists them, and tells the server.</summary>
        private void ApplyAppearance()
        {
            Avatar?.ApplyColors(Settings);
            Settings.Save();
            Game.Network?.SendAppearance(Rgb(Settings.SkinColor), Rgb(Settings.TorsoColor), Rgb(Settings.ArmColor), Rgb(Settings.LegColor));
        }

        private static int Rgb(Color c)
            => (Mathf.RoundToInt(c.r * 255f) << 16) | (Mathf.RoundToInt(c.g * 255f) << 8) | Mathf.RoundToInt(c.b * 255f);

        private static Color NextColor(Color current)
        {
            int idx = -1;
            for (int i = 0; i < Palette.Length; i++)
            {
                if (Mathf.Approximately(Palette[i].r, current.r) &&
                    Mathf.Approximately(Palette[i].g, current.g) &&
                    Mathf.Approximately(Palette[i].b, current.b))
                {
                    idx = i;
                    break;
                }
            }

            return Palette[(idx + 1) % Palette.Length];
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
