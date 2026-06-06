using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Builds the in-game scene rig entirely in code (M21), so the only thing the launcher
    /// scene needs is an <see cref="AppShell"/>: a server link (<see cref="GameBootstrap"/>),
    /// a chunk material from the bundled vertex-colour shader, a first-person player
    /// (CharacterController + camera + <see cref="PlayerController"/>) and the <see cref="HudUi"/>.
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
            boot.SkinRgb = Rgb(shell.Settings.SkinColor);
            boot.TorsoRgb = Rgb(shell.Settings.TorsoColor);
            boot.ArmRgb = Rgb(shell.Settings.ArmColor);
            boot.LegRgb = Rgb(shell.Settings.LegColor);

            // Only our camera should render in-game; disable any pre-existing scene cameras.
            foreach (var existing in Camera.allCameras)
            {
                existing.enabled = false;
            }

            // And only our listener should hear; mute any pre-existing scene/splash AudioListener.
            foreach (var al in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                al.enabled = false;
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
            camGo.AddComponent<AudioListener>(); // hear procedural SFX (M26)
            camGo.tag = "MainCamera";

            // Post-processing stack: bloom + ACES tonemap + vignette (+ SSAO on High), preset-gated.
            var postFx = camGo.AddComponent<PostFx>();
            postFx.ApplyPreset(shell.Settings.Preset, shell.Settings.ReducedEffects);

            // Holographic visor HUD: render the diegetic HUD through a UI camera + a Spacecraft/Visor pass.
            // Created before the HUD components so UiKit.HudCamera is set when they build their canvases.
            var visor = root.AddComponent<VisorHud>();
            visor.MainCamera = cam;
            visor.ApplyPreset(shell.Settings.Preset, shell.Settings.ReducedEffects);

            // Blocky avatar (shown in third-person), coloured from the player's settings.
            var avatarGo = new GameObject("Avatar");
            avatarGo.transform.SetParent(player.transform, false);
            var avatar = avatarGo.AddComponent<PlayerAvatar>();
            avatar.Build(shell.Settings);

            var pc = player.AddComponent<PlayerController>();
            pc.Game = boot;
            pc.Camera = cam;
            pc.Avatar = avatar;
            pc.MouseSensitivity = shell.Settings.MouseSensitivity;
            pc.InvertY = shell.Settings.InvertY;
            pc.ThirdPerson = shell.Settings.ThirdPerson;

            // Localized vitals HUD + hotbar.
            var hud = root.AddComponent<HudUi>(); // modern uGUI HUD
            hud.Game = boot;

            // Toggleable full-screen planet map (key M), distinct from the star map.
            var worldMap = root.AddComponent<WorldMap>();
            worldMap.Game = boot;

            // Player chat overlay (Enter to type; needs a comm radio).
            var chat = root.AddComponent<ChatUi>();
            chat.Game = boot;

            // Hyperspace warp animation (plays on a system-to-system jump).
            var warp = root.AddComponent<HyperspaceWarp>();
            warp.Game = boot;

            // Loading curtain over the world build-up on join / landing / station boarding.
            var loading = root.AddComponent<WorldLoadingOverlay>();
            loading.Game = boot;

            // In-game gameplay menu (inventory / crafting / tech / ship / map / missions), Tab.
            var menu = root.AddComponent<GameMenu>();
            menu.Game = boot;
            menu.Settings = shell.Settings;
            menu.Avatar = avatar;
            pc.Menu = menu;

            // Render other players (multiplayer presence).
            var remotes = root.AddComponent<RemotePlayers>();
            remotes.Game = boot;

            // Render settlement + space-station NPCs.
            var npcs = root.AddComponent<NpcView>();
            npcs.Game = boot;

            // Render + collide doors (sci-fi sliders auto-open; village hinge doors toggle on E).
            var doors = root.AddComponent<DoorView>();
            doors.Game = boot;

            // Death feedback: red flash + sound on planet death, explosion glare on ship destruction.
            var deathFx = root.AddComponent<DeathFx>();
            deathFx.Game = boot;

            // Player-to-player docking + trade UI (M24).
            var interactions = root.AddComponent<PlayerInteractions>();
            interactions.Game = boot;
            interactions.Remotes = remotes;

            // Render planet enemies (M25).
            var entities = root.AddComponent<WorldEntities>();
            entities.Game = boot;

            // Procedural sound effects (M26).
            var audio = root.AddComponent<ClientAudio>();
            audio.Game = boot;
            audio.Settings = shell.Settings;

            // Procedural background music (M26).
            var music = root.AddComponent<ClientMusic>();
            music.Settings = shell.Settings;

            // Real space view + launch/landing sequences (M25b).
            var space = root.AddComponent<SpaceView>();
            space.Game = boot;
            space.Camera = cam;

            // Space radar HUD (M27 polish).
            var radar = root.AddComponent<SpaceRadar>();
            radar.Game = boot;
            radar.Camera = cam;
            radar.SpaceView = space; // so the radar can show bearings to the system's planets/moons

            // Day/night + weather + sun colour (World systems).
            var sky = root.AddComponent<Sky>();
            sky.Game = boot;
            sky.Camera = cam;

            // Twinkling stars behind the world (space, airless skies, station windows, planet nights).
            var starfield = root.AddComponent<Starfield>();
            starfield.Game = boot;
            starfield.Camera = cam;

            // The planet + sun seen outside an orbital station's windows (only while boarded).
            var backdrop = root.AddComponent<StationBackdrop>();
            backdrop.Game = boot;
            backdrop.Camera = cam;

            // Surface cloud layer (per-planet colour/cover; storms darken it).
            var clouds = root.AddComponent<Clouds>();
            clouds.Game = boot;
            clouds.Camera = cam;

            // Weather overlay (screen wash + lightning, M27 polish).
            var weather = root.AddComponent<WeatherFx>();
            weather.Game = boot;

            // In-world 3D rain + storm fog (P7 weather rest).
            var weather3d = root.AddComponent<WeatherFx3D>();
            weather3d.Game = boot;
            weather3d.Cam = cam;

            // Procedural creatures / fauna (World systems §12).
            var creatures = root.AddComponent<CreatureView>();
            creatures.Game = boot;

            // Block selection outline + mining/placing particle feedback (M27 polish).
            var miningFx = root.AddComponent<MiningFx>();
            miningFx.Game = boot;
            miningFx.Camera = cam;
            miningFx.Reach = pc.Reach;

            // Tool/weapon VFX (beam + muzzle flash + impact sparks, drill sparks).
            var weaponFx = root.AddComponent<WeaponFx>();
            pc.Weapons = weaponFx;
            remotes.Weapons = weaponFx; // remote jetpack thrust flames

            // Jetpack thrust flames for the local third-person avatar would render via the player's own VFX.

            return root;
        }

        private static int Rgb(Color c)
            => (Mathf.RoundToInt(c.r * 255f) << 16) | (Mathf.RoundToInt(c.g * 255f) << 8) | Mathf.RoundToInt(c.b * 255f);
    }
}
