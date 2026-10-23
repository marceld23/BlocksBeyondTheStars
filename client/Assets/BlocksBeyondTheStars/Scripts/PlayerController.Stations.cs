// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Stations and owned structures: the station in the crosshair or next to the player and what E does there
    /// (<see cref="HandleStations"/>), the bio lab's reach, a vendor's trade-or-talk choice (<see cref="OfferVendorChoice"/>)
    /// and the owned radio beacon / base core in the crosshair that E renames (<see cref="TryAimOwnedBeacon"/>,
    /// <see cref="TryAimOwnedBase"/>).
    /// </summary>
    public sealed partial class PlayerController
    {
        private bool _marketFromVendor; // NearbyStation "market" came from a vendor NPC in reach, not a market block

        private void HandleStations()
        {
            // Prefer the station you're looking at; fall back to the nearest one you're standing by. (Pure
            // proximity made a cramped ship always read as the central station, "whatever you looked at".)
            // #2725: a guest in a docked friend's ship gets none — the host's cockpit, console, cargo and medbay are room
            // decor for us (no prompt, no E: the cockpit would fly THEIR ship, the console repair it). U takes us home.
            Game.NearbyStation = Game.Visiting ? null : Game.LookedStationType(Camera, Reach);
            if (string.IsNullOrEmpty(Game.NearbyStation) && !Game.Visiting)
            {
                Game.NearbyStation = Game.NearestStationType(transform.position, 3f);
            }

            _marketFromVendor = false;
            if (string.IsNullOrEmpty(Game.NearbyStation) && Game.NearVendor)
            {
                Game.NearbyStation = "market"; // a settlement/station vendor → "trade" prompt + E asks trade or talk
                _marketFromVendor = true;
            }

            // #1073: a placed crafting-station block you're LOOKING at (workbench, forge, …) tells you what it
            // powers in the menu — the server unlocks recipes by standing next to it, but nothing in the world
            // said so. Ship marker cells are covered by NearbyStation above; the factory terminal has its own label.
            Game.AimedStationBlock = null;
            // #2049: the Crystal Net device in the crosshair, for the HUD prompt (one ray, the same reach as E).
            // #2268: the march tests the parked ships too, so a switch in the own cabin is aimed at like one on the ground.
            bool aimingAtBlock = AimTarget(out var crystalAim, out _, out _);
            Game.AimedCrystalDevice = aimingAtBlock ? Game.CrystalDeviceAt(crystalAim.x, crystalAim.y, crystalAim.z) : null;
            Game.AimedCell = aimingAtBlock ? crystalAim : (Vector3Int?)null; // #2267: the network under the crosshair is outlined
            if (string.IsNullOrEmpty(Game.NearbyStation) && AimBlock(out var aimHit, out _))
            {
                string aimedKey = Game.Content?.BlockById(Game.World.GetBlock(aimHit.x, aimHit.y, aimHit.z))?.Key;
                // bed + heal_tank (#1456): both take E for the home spawn (HandleSetSpawnPoint below), but without
                // a prompt the bed was indistinguishable from a decorative slab ("man kann es nicht benutzen").
                if (aimedKey is "workbench" or "forge" or "detoxifier" or "matter_forge" or "decontaminator" or "algae_tank" or "campfire"
                    or "bed" or "crew_bunk" or "heal_tank")
                {
                    Game.AimedStationBlock = aimedKey;
                }
                else if (aimedKey == BlocksBeyondTheStars.Shared.Bio.BioItems.Lab && BioLabInReach(aimHit))
                {
                    Game.AimedStationBlock = aimedKey; // #2203: the HUD reads "E: Bio lab" (its own prompt key)
                }
            }

            // The scanner's prompt only shows when it would actually read something (#1458).
            Game.ScanTargetInView = HoldingScanner() && TryFindScanTarget(out _, out _, out _);

            // Your own base core in the crosshair → the HUD shows the rename key + the core's air readout (#1267).
            Game.AimedOwnBase = AimedOwnedBase();

            // #2147: the hand door E works — the one under the crosshair, else the nearest; its label follows suit.
            int handDoor = TargetHandDoor();
            if (Game.Views.Doors != null)
            {
                Game.Views.Doors.SetInteractTarget(handDoor);
            }

            if (!InputMap.Down(InputAction.Interact) || (Game.Views.LaunchPrompt?.IsOpen ?? false) || (Game.Views.VendorChoice?.IsOpen ?? false))
            {
                return; // the launch / trade-or-talk question owns E while it is up (#1455)
            }

            // A radio beacon you own that you're aiming at → rename it (item 37).
            if (TryAimOwnedBeacon(out int beaconId, out string current))
            {
                Game?.Context.Panel<BeaconLabelUi>()?.Open(
                    Game.Localizer?.Get("ui.beacon.rename_prompt") ?? "Rename beacon",
                    current,
                    label => Game.Network?.SendSetBeaconLabel(beaconId, label));
                return;
            }

            // A planet base you own (Grundstein) that you're aiming at → rename it, and see who lives there (#2717).
            if (TryAimOwnedBase(out string baseBodyId, out string baseName))
            {
                Game?.Context.Panel<BeaconLabelUi>()?.OpenBase(
                    Game.Localizer?.Get("ui.base.rename_prompt") ?? "Rename base",
                    baseName,
                    baseBodyId,
                    name => Game.Network?.SendSetBaseName(baseBodyId, name));
                return;
            }

            // Inside your own boarded station, aiming at the station core → rename the station (server checks owner).
            if (!string.IsNullOrEmpty(Game.CurrentStationId)
                && AimBlock(out var coreHit, out _)
                && Game.Content?.BlockById(Game.World.GetBlock(coreHit.x, coreHit.y, coreHit.z))?.Key == "station_core")
            {
                string stationId = Game.CurrentStationId;
                Game?.Context.Panel<BeaconLabelUi>()?.Open(
                    Game.Localizer?.Get("ui.map.rename") ?? "Rename",
                    Game.StationName,
                    name => Game.Network?.SendSetStationName(stationId, name));
                return;
            }

            // A storage crate you're aiming at → the crate screen (#2436): its contents above, your backpack below, click
            // to move a stack; the filter picker (#1032) is a button on it. Matched by the container list, not the block
            // alone, so a crate block that hasn't registered yet no-ops. #2613: the crosshair cell is a scene cell and the
            // crate's a canonical one — matched the short way round the seam, or a crate across X = 0 never opened.
            if (AimBlock(out var crateHit, out _)
                && Game.Content?.BlockById(Game.World.GetBlock(crateHit.x, crateHit.y, crateHit.z))?.Key is "crate" or "wood_crate"
                && InteractReach.CrateAtCell(Game.Containers, crateHit.x, crateHit.y, crateHit.z, Game.Circumference) is { } aimedCrate)
            {
                Game?.Context.Panel<CrateUi>()?.Open(aimedCrate.Id, aimedCrate.Filter);
                return;
            }

            // #2049: a Crystal Net device you're aiming at — a switch toggles, a button presses, everything with a menu
            // opens it. Aim-based, so it works the same with mouse look, the right stick and the touch look pad.
            if (AimTarget(out var crystalHit, out _, out _) && Game.CrystalDeviceAt(crystalHit.x, crystalHit.y, crystalHit.z) is { } crystalDev)
            {
                var crystalKind = CrystalDeviceUi.KindOf(crystalDev);
                if (crystalKind == BlocksBeyondTheStars.Shared.Definitions.CrystalDeviceKind.Switch)
                {
                    Game.SendCrystalDevice(crystalDev.X, crystalDev.Y, crystalDev.Z, 0);
                    return;
                }

                // A button presses; #2266: a lift stop calls the platform to its floor.
                if (crystalKind is BlocksBeyondTheStars.Shared.Definitions.CrystalDeviceKind.Button
                    or BlocksBeyondTheStars.Shared.Definitions.CrystalDeviceKind.LiftStop)
                {
                    Game.SendCrystalDevice(crystalDev.X, crystalDev.Y, crystalDev.Z, 1);
                    return;
                }

                if (BlocksBeyondTheStars.Shared.Definitions.CrystalNetRules.IsConfigurable(crystalKind))
                {
                    Game?.Context.Panel<CrystalDeviceUi>()?.Open(crystalDev);
                    return;
                }
            }

            // #2203: a bio lab you're aiming at, from where the server lets you use it → its panel (analyse, mix, change).
            if (AimBlock(out var labHit, out _)
                && Game.Content?.BlockById(Game.World.GetBlock(labHit.x, labHit.y, labHit.z))?.Key == BlocksBeyondTheStars.Shared.Bio.BioItems.Lab
                && BioLabInReach(labHit))
            {
                Game?.Context.Panel<BioLabUi>()?.Open();
                return;
            }

            // A heal tank — or its low-tech precursor, the bed (#804) — you're aiming at → make it your
            // home spawn point (base/station, issue #461). A bed or a crew bunk also lays you down to rest (#2823).
            if (AimBlock(out var tankHit, out _)
                && Game.Content?.BlockById(Game.World.GetBlock(tankHit.x, tankHit.y, tankHit.z))?.Key is { } tankKey
                && tankKey is "heal_tank" or "bed" or "crew_bunk")
            {
                Game.Network?.SendSetSpawnPoint(tankHit.x, tankHit.y, tankHit.z);
                ClientAudio.Instance?.Cue("heal");
                if (tankKey != "heal_tank")
                {
                    TryLieDown(tankHit, onShip: false);
                }

                return;
            }

            // A chair- or bench-shaped cell in any material seats the player (#806, #1846).
            if (_seatCell is null && AimBlock(out var chairHit, out _)
                && FurnitureShapes.IsSeat(ShapeCode.ShapeOf(Game.World.GetShape(chairHit.x, chairHit.y, chairHit.z))))
            {
                // #2122: somebody already sits there — say so instead of dropping the camera into them. The server
                // checks again (it may know a sitter this client has not heard of yet).
                if (SeatLooksTaken(chairHit))
                {
                    Game.ShowMessage(Game.Localizer?.Get("srv.seat.taken") ?? "Someone is already sitting there.");
                    return;
                }

                SitDown(chairHit);
                return;
            }

            // A beam block (teleporter pad) you're standing on / next to opens the transporter — pick a destination
            // among your own + allied pads on this world, then beam to it.
            int beam = Game.Views.Beams != null ? Game.Views.Beams.NearestUsableBeam(transform.position, 2.2f) : 0;
            if (beam != 0)
            {
                Game?.Context.Panel<BeamPadUi>()?.Open(beam);
                return;
            }

            // A settlement hinge door you're standing at opens/closes with E — checked BEFORE stations so a door
            // next to a market stall still opens (sci-fi slide doors open themselves; this is for village doors) (B47).
            // Which door: the aimed one before the nearest (#2147), chosen above.
            if (handDoor != 0)
            {
                Game.Network?.SendDoorInteract(handDoor);
                return;
            }

            // A data cube within reach downloads its minigame into the Arcade collection (item: arcade).
            if (Game.Views.DataCubes != null)
            {
                int cube = Game.Views.DataCubes.NearestDataCube(transform.position, 3.2f, out string gameKey, out bool owned);
                if (cube != 0)
                {
                    if (owned)
                    {
                        Game.ShowMessage(Game.Localizer?.Get("ui.datacube.already") ?? "This fragment is already in your collection.");
                    }
                    else if (!string.IsNullOrEmpty(gameKey))
                    {
                        Game.Network?.SendUnlockGame(cube, gameKey);
                        ClientAudio.Instance?.At("data_cube_download", transform.position, 1f, 1f);
                    }

                    return;
                }
            }

            // A claimable factory terminal within reach → claim it with an access code (it becomes your base).
            if (Game.Views.Factories != null)
            {
                int factory = Game.Views.Factories.NearestClaimable(transform.position, 4f);
                if (factory != 0)
                {
                    bool hasCode = false;
                    if (Game.Personal != null)
                    {
                        foreach (var s in Game.Personal)
                        {
                            if (s.Item == "access_code" && s.Count > 0) { hasCode = true; break; }
                        }
                    }

                    if (hasCode)
                    {
                        Game.Network?.SendClaimStructure(factory);
                    }
                    else
                    {
                        Game.ShowMessage(Game.Localizer?.Get("ui.factory.need_code") ?? "You need an access code to claim this.");
                    }

                    return;
                }
            }

            // A net fragment within reach → recover it (text-only story find; reveals its archive + advances the story).
            if (Game.Views.NetFragments != null)
            {
                int frag = Game.Views.NetFragments.NearestNetFragment(transform.position, 3.2f, out _);
                if (frag != 0)
                {
                    Game.Network?.SendNetFragmentFound(frag);
                    ClientAudio.Instance?.At("data_cube_download", transform.position, 1f, 1f);
                    return;
                }
            }

            if (string.IsNullOrEmpty(Game.NearbyStation))
            {
                // No station block in reach — an NPC nearby? E talks to them (#1127). Station blocks keep
                // priority so E at a market still opens the market (vendors are usually right beside one).
                // #2613: the HUD's talk prompt asks the same picker, which measures the short way round the seam.
                var talkTo = Game.NearestTalkableNpc();
                if (talkTo != null)
                {
                    Game.Network?.SendTalkToNpc(talkTo.Id);
                }

                return;
            }

            // Stations that open a client UI panel; the rest are resolved server-side.
            switch (Game.NearbyStation)
            {
                case "cockpit":
                    // Aboard the floating interior the cockpit is the helm — the prompt says "Take the helm (fly)", so E takes
                    // it (#2705): the server returns to the flight view (or says why the ship can't fly). E used to open the
                    // map here like on a landed ship, and only its top button flew on.
                    if (Game.LoadingPlanetType == "ship_interior")
                    {
                        Game.Network?.SendUseStation("cockpit");
                        break;
                    }

                    // On a landed ship E asks "launch into space?" first (#1455): the launch button lived only
                    // at the top of the Map tab, and a first-time player at the cockpit never found it. Declining
                    // opens the map as before.
                    if (Game.Views.LaunchPrompt == null || !Game.Views.LaunchPrompt.TryOffer(() => Menu?.OpenMap()))
                    {
                        Menu?.OpenMap();
                    }

                    break;
                case "workshop":
                    // #2248: with the bio lab module fitted, the lab lives in the workshop room — E asks which one.
                    // "Workshop (E)" keeps crafting one key press away ({key:Interact}, expanded by the prompt — #2502).
                    if (!BioLabUi.ShipLabAboard(Game) || Game.Views.VendorChoice == null
                        || !Game.Views.VendorChoice.TryOfferPair(
                            Game.Localizer?.Get("ui.station.workshop") ?? "Workshop",
                            Game.Localizer?.Get("ui.bio.workshop_choice_craft") ?? "Workshop ({key:Interact})", () => Menu?.OpenCrafting(),
                            Game.Localizer?.Get("ui.bio.workshop_choice_lab") ?? "Bio lab", () => Game?.Context.Panel<BioLabUi>()?.Open()))
                    {
                        Menu?.OpenCrafting();
                    }

                    break;
                case "market":
                    if (!_marketFromVendor || !OfferVendorChoice())
                    {
                        // #2626: the screen is bound to the vendor in reach (0 = the ship's own trade console).
                        var tradeWith = _marketFromVendor ? Game.NearestTradingVendor() : null;
                        Menu?.OpenMarket(tradeWith != null ? tradeWith.Id : 0);
                        Game.Network?.SendNpcGreet("vendor"); // item 15: vendor greeting
                    }

                    break;
                case "cargo": Menu?.OpenCargo(); break;
                case "console": Menu?.OpenShip(); Game.Network?.SendUseStation("console"); break; // ship status/repairs (#463)
                case "quarters":
                    // The ship's bunk sets the respawn to the quarters as before — and lays you down on it (#2823). A parked
                    // ship's hull is no world cell, so the aimed-bed branch above never sees this one.
                    Game.Network?.SendUseStation("quarters");
                    if (QuartersBedCell(out var quarters, out bool quartersOnShip))
                    {
                        TryLieDown(quarters, quartersOnShip);
                    }

                    break;
                default:
                    if (Game.NearbyStation == "medbay") ClientAudio.Instance?.Cue("heal");
                    Game.Network?.SendUseStation(Game.NearbyStation);
                    break; // medbay and the rest, resolved server-side
            }
        }

        /// <summary>#2203: whether a bio lab cell is close enough for the server to take a lab intent — it looks for a lab
        /// three cells around the feet and two up or down. The prompt and E use the same reach, so the panel never opens
        /// on a lab that would then refuse everything. #2216: never from inside a ship — the server refuses the lab
        /// while the player is aboard or stands in a parked hull (<see cref="BioLabUi.RefusedAboard"/>), so a lab seen
        /// through the hull has no prompt and E does not open it.</summary>
        private bool BioLabInReach(Vector3Int cell)
        {
            if (BioLabUi.RefusedAboard(Game))
            {
                return false;
            }

            var feet = transform.position;
            return Mathf.Abs(cell.x - Mathf.FloorToInt(feet.x)) <= 3
                && Mathf.Abs(cell.y - Mathf.FloorToInt(feet.y)) <= 2
                && Mathf.Abs(cell.z - Mathf.FloorToInt(feet.z)) <= 3;
        }

        /// <summary>E at a vendor NPC: ask "trade or talk?" — a vendor is a market station, so E used to open the market
        /// every time and its dialogues were unreachable. False when no prompt could be shown (the caller then opens the
        /// market as before).</summary>
        private bool OfferVendorChoice()
        {
            var prompt = Game?.Views.VendorChoice;
            if (prompt == null)
            {
                return false;
            }

            // #2626: the same picker as the "Trade" prompt (Game.NearVendor), and Trade binds the screen to THIS vendor.
            var vendor = Game.NearestTradingVendor();
            if (vendor == null)
            {
                return false;
            }

            int id = vendor.Id;
            string role = Game.Localizer != null && !string.IsNullOrEmpty(vendor.NameKey) ? Game.Localizer.Get(vendor.NameKey) : string.Empty;
            string label = VendorReach.Label(vendor.Name, role);
            return prompt.TryOffer(label,
                () =>
                {
                    Menu?.OpenMarket(id);
                    Game.Network?.SendNpcGreet("vendor");
                },
                () => Game.Network?.SendTalkToNpc(id));
        }

        /// <summary>True if the player is looking at a radio beacon block they own — returns its id + current label
        /// so E can open the rename overlay (item 37). Only the owner gets the rename prompt; everyone sees markers.</summary>
        private bool TryAimOwnedBeacon(out int beaconId, out string label)
        {
            beaconId = 0;
            label = string.Empty;
            if (Game?.Beacons == null || Game.Beacons.Length == 0 || string.IsNullOrEmpty(Game.LocalPlayerId))
            {
                return false;
            }

            if (!AimBlock(out var hit, out _))
            {
                return false; // not looking at a block within reach
            }

            foreach (var b in Game.Beacons)
            {
                // #2613: a canonical beacon cell against the scene cell in the crosshair — the short way round the seam.
                if (b.OwnerId == Game.LocalPlayerId
                    && WorldConstants.SameCell(Mathf.FloorToInt(b.X), Mathf.FloorToInt(b.Y), Mathf.FloorToInt(b.Z),
                        hit.x, hit.y, hit.z, Game.Circumference))
                {
                    beaconId = b.Id;
                    label = b.Label ?? string.Empty;
                    return true;
                }
            }

            return false;
        }

        /// <summary>True if the player is looking at a base core (Grundstein) they own — returns its body id + the
        /// current base name so E can open the rename overlay. Only the owner gets the prompt; everyone sees the marker.</summary>
        private bool TryAimOwnedBase(out string bodyId, out string name)
        {
            var b = AimedOwnedBase();
            bodyId = b?.BodyId ?? string.Empty;
            name = b?.Name ?? string.Empty;
            return b != null;
        }

        /// <summary>The base you own whose core block is in the crosshair, or null.</summary>
        private BlocksBeyondTheStars.Networking.Messages.NetBase AimedOwnedBase()
        {
            if (Game?.Bases == null || Game.Bases.Length == 0 || string.IsNullOrEmpty(Game.LocalPlayerId))
            {
                return null;
            }

            if (!AimBlock(out var hit, out _))
            {
                return null;
            }

            foreach (var b in Game.Bases)
            {
                // #2613: NetBase carries the core cell's centre, canonical; the crosshair cell is a scene cell.
                if (b.OwnerId == Game.LocalPlayerId
                    && WorldConstants.SameCell(Mathf.FloorToInt(b.X), Mathf.FloorToInt(b.Y), Mathf.FloorToInt(b.Z),
                        hit.x, hit.y, hit.z, Game.Circumference))
                {
                    return b;
                }
            }

            return null;
        }
    }
}
