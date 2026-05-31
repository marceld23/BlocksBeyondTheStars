using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Builds the in-game scene rig entirely in code (M21), so the only thing the launcher
    /// scene needs is an <see cref="AppShell"/>: a server link (<see cref="GameBootstrap"/>),
    /// a chunk material from the bundled vertex-colour shader, a first-person player
    /// (CharacterController + camera + <see cref="PlayerController"/>) and the <see cref="Hud"/>.
    /// Everything is parented under one root so it can be torn down on return to the menu.
    /// </summary>
    public static class WorldRig
    {
        public static GameObject Build(AppShell shell)
        {
            var root = new GameObject("Game");

            // Render the (unlit) per-block vertex colours; fall back if the shader is missing.
            var shader = Shader.Find("Spacecraft/VertexColorOpaque") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader);

            // Server link + world streaming/meshing.
            var linkGo = new GameObject("Server Link");
            linkGo.transform.SetParent(root.transform);
            var boot = linkGo.AddComponent<GameBootstrap>();
            boot.Host = shell.Host;
            boot.Port = int.TryParse(shell.Port, out var p) && p > 0 ? p : 31415;
            boot.PlayerName = string.IsNullOrWhiteSpace(shell.PlayerName) ? "Pilot" : shell.PlayerName;
            boot.German = shell.Settings.Language == "de";
            boot.ChunkMaterial = material;

            // Only our camera should render in-game; disable any pre-existing scene cameras.
            foreach (var existing in Camera.allCameras)
            {
                existing.enabled = false;
            }

            // First-person player rig. Starts high and falls onto the streamed terrain until
            // the server spawn snaps it into place (PlayerController).
            var player = new GameObject("Player");
            player.transform.SetParent(root.transform);
            player.transform.position = new Vector3(0.5f, 100f, 0.5f);

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            var camGo = new GameObject("Player Camera");
            camGo.transform.SetParent(player.transform);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var cam = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";

            var pc = player.AddComponent<PlayerController>();
            pc.Game = boot;
            pc.Camera = cam;
            pc.MouseSensitivity = shell.Settings.MouseSensitivity;
            pc.InvertY = shell.Settings.InvertY;

            // Localized vitals HUD + hotbar.
            var hud = root.AddComponent<Hud>();
            hud.Game = boot;

            // In-game gameplay menu (inventory / crafting / tech / ship), toggled with Tab.
            var menu = root.AddComponent<GameMenu>();
            menu.Game = boot;

            return root;
        }
    }
}
