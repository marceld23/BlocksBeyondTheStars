// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
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
            var shader = Shader.Find("BlocksBeyondTheStars/VertexColorOpaque") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader);

            // Server link + world streaming/meshing.
            var linkGo = new GameObject("Server Link");
            linkGo.transform.SetParent(root.transform);
            var boot = linkGo.AddComponent<GameBootstrap>();
            boot.Host = shell.Host;
            boot.Port = int.TryParse(shell.Port, out var p) && p > 0 ? p : 31415;
            boot.PlayerName = string.IsNullOrWhiteSpace(shell.PlayerName) ? "Pilot" : shell.PlayerName;
            boot.Password = shell.Password ?? "";
            boot.Token = shell.Settings.PlayerToken ?? "";
            boot.HostInfo = shell.HostInfo ?? "";
            boot.German = shell.Settings.Language == "de";
            boot.ViewDistanceChunks = shell.Settings.ViewDistanceChunks; // forward the slider so remote hosts stream this radius
            boot.ChunkMaterial = material;
            boot.SkinRgb = Rgb(shell.Settings.SkinColor);
            boot.TorsoRgb = Rgb(shell.Settings.TorsoColor);
            boot.ArmRgb = Rgb(shell.Settings.ArmColor);
            boot.LegRgb = Rgb(shell.Settings.LegColor);
            boot.HullRgb = Rgb(shell.Settings.HullColor); // ship hull tint (item 32)
            boot.FacePixels = shell.Settings.FacePixels ?? ""; // custom pixel face, sent on join
            boot.Settings = shell.Settings; // live read for the auto-stow comfort option

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
            // Auto-step + slope walking so the crafted building shapes are usable on foot: a slab (top at 0.5)
            // and each stair tread (0.5 high) are climbed without jumping, while a full 1.0-high block still
            // needs a jump. The 50° slope limit lets the 45° ramp wedge be walked up smoothly. (Issues #127.)
            cc.stepOffset = 0.6f;
            cc.slopeLimit = 50f;

            var camGo = new GameObject("Player Camera");
            camGo.transform.SetParent(player.transform);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>(); // hear procedural SFX (M26)
            camGo.tag = "MainCamera";

            // URP look wiring: a code-created URP camera has post-processing OFF by default, so the global
            // UrpScenePost Volume (bloom/tonemap/vignette/teal-orange grade/lens flare) and SMAA would never run
            // in-game. Turn it on here and push the per-camera look (SMAA + SSAO-renderer choice) from settings;
            // ActiveCameraData lets the pause-menu graphics toggles re-apply live. No-op under Built-in RP.
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
            {
                ClientSettings.ActiveCameraData = cam.GetUniversalAdditionalCameraData();
                shell.Settings.ApplyCameraLook();
            }

            // Holographic visor HUD: render the diegetic HUD through a UI camera + a BlocksBeyondTheStars/Visor pass.
            // Created before the HUD components so UiKit.HudCamera is set when they build their canvases.
            var visor = root.AddComponent<VisorHud>();
            visor.MainCamera = cam;
            visor.Settings = shell.Settings; // so the "visor effect" toggle reads live
            visor.ApplyPreset(shell.Settings.Preset, shell.Settings.ReducedEffects);

            // Blocky avatar (shown in third-person), coloured from the player's settings.
            var avatarGo = new GameObject("Avatar");
            avatarGo.transform.SetParent(player.transform, false);
            var avatar = avatarGo.AddComponent<PlayerAvatar>();
            avatar.Build(shell.Settings);
            avatar.SetFace(shell.Settings.FacePixels); // custom pixel face on our own figure (third person)

            var pc = player.AddComponent<PlayerController>();
            pc.Game = boot;
            pc.Camera = cam;
            pc.Avatar = avatar;
            pc.MouseSensitivity = shell.Settings.MouseSensitivity;
            pc.InvertY = shell.Settings.InvertY;
            pc.CameraMotion = shell.Settings.CameraMotion;
            pc.ThirdPerson = shell.Settings.ThirdPerson;

            // Localized vitals HUD + hotbar.
            var hud = root.AddComponent<HudUi>(); // modern uGUI HUD
            hud.Game = boot;
            hud.Settings = shell.Settings; // live read for the optional playtime readout

            // Toggleable full-screen planet map (key M), distinct from the star map.
            var worldMap = root.AddComponent<WorldMap>();
            worldMap.Game = boot;

            // Player chat overlay (Enter to type; needs a comm radio).
            var chat = root.AddComponent<ChatUi>();
            chat.Game = boot;

            // Live push-to-talk voice chat (opt-in: server flag + the Concentus plugin via the BBS_VOICE define).
            // Inert otherwise; shares the radio gate + tiered reach with text chat.
            var voice = root.AddComponent<VoiceChat>();
            voice.Game = boot;
            voice.Settings = shell.Settings;

            // Player feedback (F1 or the small HUD button): bug reports + feature wishes from any player →
            // website API + the existing /bump server snapshot.
            var feedback = root.AddComponent<FeedbackUi>();
            feedback.Game = boot;
            feedback.Settings = shell.Settings;

            // Beacon name/rename overlay (item 37): opens when placing or renaming a radio beacon.
            var beaconLabel = root.AddComponent<BeaconLabelUi>();
            beaconLabel.Game = boot;

            // Floating beacon labels above their blocks in the world (item 37).
            var beaconView = root.AddComponent<BeaconView>();
            beaconView.Game = boot;

            // Beam blocks (teleporter pads): glow column + idle hum + floating names + jump VFX.
            var beamView = root.AddComponent<BeamView>();
            beamView.Game = boot;

            // Transporter panel: opens on E at a beam pad (destinations: own + allied pads on this world).
            var beamPad = root.AddComponent<BeamPadUi>();
            beamPad.Game = boot;

            // Ship AI companion "VEGA": onboarding lines, objective chip, advisor hints, story beats.
            var vega = root.AddComponent<VegaPanel>();
            vega.Game = boot;
            vega.Settings = shell.Settings;

            // Other players' ships landing/launching at pads, in multiplayer (item 38).
            var shipTransit = root.AddComponent<ShipTransitView>();
            shipTransit.Game = boot;

            // Ships parked on this world as placed voxel objects (ship-as-object): the own + other players'
            // landed ships, painted in their hull colours, with colliders to walk on/in.
            var landedShips = root.AddComponent<LandedShipView>();
            landedShips.Game = boot;

            // Hover speeders (craftable surface vehicles): voxel objects parked/driven on this world, with
            // hover dust + engine glow and deploy/destruction bursts.
            var speeders = root.AddComponent<SpeederView>();
            speeders.Game = boot;

            // Hyperspace warp animation (plays on a system-to-system jump).
            var warp = root.AddComponent<HyperspaceWarp>();
            warp.Game = boot;

            // Loading curtain over the world build-up on join / landing / station boarding.
            var loading = root.AddComponent<WorldLoadingOverlay>();
            loading.Game = boot;
            // Raise the curtain opaque NOW, synchronously, before this frame renders — so the freshly-built
            // rig never flashes its raw scene (the star system, then the bare surface) during entry. It holds
            // until the join confirms and the world is ready, then fades to reveal the world.
            loading.PrimeForInitialLoad();

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

            // Render glowing minigame "data cubes" scattered on the surface; press E to download (item: arcade).
            var dataCubes = root.AddComponent<DataCubeView>();
            dataCubes.Game = boot;

            // Render factories' animated machines (pistons/rotors/conveyors) + the production-terminal prompt.
            var factories = root.AddComponent<FactoryView>();
            factories.Game = boot;

            // Render story "net fragments" scattered on the surface; press E to recover (text-only story finds).
            var netFragments = root.AddComponent<NetFragmentView>();
            netFragments.Game = boot;

            // Finale (P6): the core-hack channel bar + the argument-duel panel at the Guardian core.
            var finale = root.AddComponent<FinaleView>();
            finale.Game = boot;

            // Death feedback: red flash + sound on planet death, explosion glare on ship destruction.
            var deathFx = root.AddComponent<DeathFx>();
            deathFx.Game = boot;

            // "Du bist gestorben" / "Das Schiff wurde zerstört" prompt: after the death/explosion animation,
            // hold the respawn behind a Weiter button (gates AwaitingRespawnConfirm).
            var respawnPrompt = root.AddComponent<RespawnPrompt>();
            respawnPrompt.Game = boot;

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

            // Background music is driven by the persistent AppShell-level ClientMusic director (it spans the
            // shell screens too); it reads this world via AppShell.CurrentBoot, so nothing is wired here.

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
            sky.ViewChunks = shell.Settings.ViewDistanceChunks; // scale distance fog to the render distance
            sky.FogEnabled = shell.Settings.VolumetricFog;      // "Volumetric fog / light shafts" toggle → distance haze + god-rays

            // Procedural nebula backdrop behind the stars in deep space (colour + depth instead of flat black).
            var nebula = root.AddComponent<NebulaField>();
            nebula.Game = boot;
            nebula.Camera = cam;

            // Additive atmosphere glow on planets: horizon brightening + warm dawn/dusk sun scattering.
            var atmosphere = root.AddComponent<AtmosphereDome>();
            atmosphere.Game = boot;
            atmosphere.Camera = cam;

            // Slow ambient air motes (dust/pollen/embers) on the surface — the air feels alive.
            var ambientDust = root.AddComponent<AmbientParticles>();
            ambientDust.Game = boot;
            ambientDust.Camera = cam;
            ambientDust.ReducedEffects = shell.Settings.ReducedEffects;

            // Tiny cosmetic micro-fauna ("Kleinstlebewesen") — fluttering / crawling / swimming critters + cave
            // glow-worms that make planets feel alive. Client-only, biome + day/night gated, never attack.
            var microFauna = root.AddComponent<MicroFaunaView>();
            microFauna.Game = boot;
            microFauna.Camera = cam;
            microFauna.ReducedEffects = shell.Settings.ReducedEffects;

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

            // Geyser / vent eruptions (item 21): rising plume + hiss at geyser_vent blocks.
            var geysers = root.AddComponent<GeyserView>();
            geysers.Game = boot;

            // Waterfall mist: spray rising off the impact wherever water falls more than three blocks.
            var waterfalls = root.AddComponent<WaterfallMistView>();
            waterfalls.Game = boot;

            // Lavafall embers + heat-haze: the molten counterpart, wherever lava falls more than three blocks.
            var lavafalls = root.AddComponent<LavaFallView>();
            lavafalls.Game = boot;

            // Ship-station decor (cockpit console + holo map, medbay tank, terminals, workshop sparks).
            var stationDecor = root.AddComponent<StationDecorView>();
            stationDecor.Game = boot;
            stationDecor.Camera = cam;

            // Interior ship-damage dressing (sparks below 50% hull, emergency light + alarm below 25%).
            var shipDamage = root.AddComponent<ShipDamageView>();
            shipDamage.Game = boot;
            shipDamage.Camera = cam;

            // Ship-restamp materialize sweep (holo ring + shimmer when the hull is swapped at a pad).
            var shipBuild = root.AddComponent<ShipBuildFx>();
            shipBuild.Game = boot;

            // URP post-processing volume (bloom/tonemap/vignette/grade + menu blur); no-op under Built-in RP.
            var urpPost = root.AddComponent<UrpScenePost>();
            urpPost.Game = boot;
            urpPost.ReducedEffects = shell.Settings.ReducedEffects; // skip alarm/damage pulses + bursts
            urpPost.Preset = shell.Settings.Preset;                 // gates lens flare (Medium+) / motion blur (High+)
            urpPost.LensFlareEnabled = shell.Settings.LensFlare;
            urpPost.MotionBlurEnabled = shell.Settings.MotionBlur;
            urpPost.Brightness = shell.Settings.Brightness; // global scene brightness (post-exposure)

            // Terrain-scanner overlay (Feature 40): through-wall ore glow markers after a scan pulse.
            var oreScan = root.AddComponent<OreScanView>();
            oreScan.Game = boot;

            // Night auroras on cold worlds ("Welten reicher" W-R4).
            var aurora = root.AddComponent<AuroraView>();
            aurora.Game = boot;

            // Heat-haze shimmer on hot worlds: a depth-faded screen warp over the far field (URP only).
            var heat = root.AddComponent<HeatShimmer>();
            heat.Game = boot;
            heat.Camera = cam;

            // Orbital bodies in the planet sky (moons/neighbour planets/landable asteroids — no stations),
            // each on its own deterministic per-planet sky cycle.
            var skyBodies = root.AddComponent<SkyBodiesView>();
            skyBodies.Game = boot;

            // Block selection outline + mining/placing particle feedback (M27 polish).
            var miningFx = root.AddComponent<MiningFx>();
            miningFx.Game = boot;
            miningFx.Camera = cam;
            miningFx.Reach = pc.Reach;

            // Flora spawn/regrow cue: a growing sprout marks a harvested cell while its plant regrows.
            var floraGrowthFx = root.AddComponent<FloraGrowthFx>();
            floraGrowthFx.Game = boot;

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
