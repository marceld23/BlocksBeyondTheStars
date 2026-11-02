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
    /// (<see cref="HandleStations"/> — the pick itself is made in <c>PlayerController.InteractTarget.cs</c>, #3088), the bio
    /// lab's reach, a vendor's trade-or-talk choice (<see cref="OfferVendorChoice"/>) and the owned radio beacon / base core
    /// in the crosshair that E renames (<see cref="TryAimOwnedBeacon"/>, <see cref="TryAimOwnedBase"/>).
    /// </summary>
    public sealed partial class PlayerController
    {
        private void HandleStations()
        {
            PublishAimState();

            // #2147: the hand door E works — the one under the crosshair, else the nearest; its label follows suit.
            int handDoor = TargetHandDoor(out bool doorAimed);
            if (Game.Views.Doors != null)
            {
                Game.Views.Doors.SetInteractTarget(handDoor);
            }

            // #3088: ONE pick for the key and the HUD's prompt line — what the crosshair rests on beats what merely stands near.
            ResolveInteraction(handDoor, doorAimed);

            if (!InputMap.Down(InputAction.Interact) || (Game.Views.LaunchPrompt?.IsOpen ?? false) || (Game.Views.VendorChoice?.IsOpen ?? false))
            {
                return; // the launch / trade-or-talk question owns E while it is up (#1455)
            }

            RunInteraction(Game.Interaction);
        }

        /// <summary>The aim readouts the HUD and the menus read every on-foot frame: the ship station looked at or stood beside
        /// (<see cref="GameBootstrap.NearbyStation"/> — the crafting gates and the ship repair key ask it too), the block under
        /// the crosshair, the Crystal Net device and its network, the scanner's target, the own base core and a base pad.</summary>
        private void PublishAimState()
        {
            // Prefer the station you're looking at; fall back to the nearest one you're standing by. (Pure
            // proximity made a cramped ship always read as the central station, "whatever you looked at".)
            // #2725: a guest in a docked friend's ship gets none — the host's cockpit, console, cargo and medbay are room
            // decor for us (no prompt, no E: the cockpit would fly THEIR ship, the console repair it). U takes us home.
            _lookedStation = Game.Visiting ? string.Empty : Game.LookedStationType(Camera, Reach) ?? string.Empty;
            _nearStation = _lookedStation.Length == 0 && !Game.Visiting
                ? Game.NearestStationType(transform.position, 3f) ?? string.Empty
                : string.Empty;
            Game.NearbyStation = _lookedStation.Length > 0 ? _lookedStation : _nearStation;
            if (string.IsNullOrEmpty(Game.NearbyStation) && Game.NearVendor)
            {
                Game.NearbyStation = "market"; // a settlement/station vendor in trading reach (E's pick: trade or talk)
            }

            // #2049: the Crystal Net device in the crosshair, for the HUD prompt (one ray, the same reach as E).
            // #2268: the march tests the parked ships too, so a switch in the own cabin is aimed at like one on the ground.
            _targetAimed = AimTarget(out _targetHit, out _, out _targetShip);
            Game.AimedCrystalDevice = _targetAimed ? Game.CrystalDeviceAt(_targetHit.x, _targetHit.y, _targetHit.z) : null;
            Game.AimedCell = _targetAimed ? _targetHit : (Vector3Int?)null; // #2267: the network under the crosshair is outlined

            // The world block under the crosshair — read once, for the station-block prompt and the interact pick.
            _aim.Reset();
            _aim.BlockAimed = AimBlock(out _aim.Cell, out _);
            _aim.BlockKey = _aim.BlockAimed ? Game.Content?.BlockById(Game.World.GetBlock(_aim.Cell.x, _aim.Cell.y, _aim.Cell.z))?.Key : null;
            Game.AimedStationBlock = AimedStationBlockKey();

            // The scanner's prompt only shows when it would actually read something (#1458).
            Game.ScanTargetInView = HoldingScanner() && TryFindScanTarget(out _, out _, out _);

            // Your own base core in the crosshair → the HUD shows the rename key + the core's air readout (#1267).
            Game.AimedOwnBase = AimedOwnedBase();
            Game.AimedBasePadCell = AimedBasePadBlock(); // #3070: a base landing pad — the HUD names it, E opens its panel
        }

        /// <summary>#1073: a placed crafting-station block in the crosshair (workbench, forge, …) tells you what it powers in
        /// the menu — the server unlocks recipes by standing next to it, but nothing in the world said so; a bed, a crew bunk
        /// and a heal tank (#1456) say that E sets the home spawn (without a prompt the bed was indistinguishable from a
        /// decorative slab); a bio lab in reach names its own panel (#2203). Null otherwise. #3088: read even with a ship
        /// station three metres off — the block in the crosshair is what E works on.</summary>
        private string AimedStationBlockKey()
        {
            string aimedKey = _aim.BlockKey;
            if (aimedKey is "workbench" or "forge" or "detoxifier" or "matter_forge" or "decontaminator" or "algae_tank" or "campfire"
                or "bed" or "crew_bunk" or "heal_tank")
            {
                return aimedKey;
            }

            return aimedKey == BlocksBeyondTheStars.Shared.Bio.BioItems.Lab && BioLabInReach(_aim.Cell) ? aimedKey : null;
        }

        /// <summary>A radio beacon you own that you're aiming at → rename it (item 37).</summary>
        private void RenameAimedBeacon()
        {
            int beaconId = _aim.BeaconId;
            Game?.Context.Panel<BeaconLabelUi>()?.Open(
                Game.Localizer?.Get("ui.beacon.rename_prompt") ?? "Rename beacon",
                _aim.BeaconLabel,
                label => Game.Network?.SendSetBeaconLabel(beaconId, label));
        }

        /// <summary>A planet base you own (Grundstein) that you're aiming at → rename it, see who lives there (#2717) and what
        /// it gathered (#2999). #2996: several bases may stand on one body — the core cell says which one.</summary>
        private void OpenAimedBase()
        {
            if (TryAimOwnedBase(out string baseBodyId, out string baseName, out var baseCell))
            {
                Game?.Context.Panel<BeaconLabelUi>()?.OpenBase(
                    Game.Localizer?.Get("ui.base.rename_prompt") ?? "Rename base",
                    baseName,
                    baseBodyId,
                    baseCell,
                    name => Game.Network?.SendSetBaseName(baseBodyId, name, baseCell.x, baseCell.y, baseCell.z));
            }
        }

        /// <summary>Inside your own boarded station, aiming at the station core → rename the station (server checks owner).</summary>
        private void RenameBoardedStation()
        {
            string stationId = Game.CurrentStationId;
            Game?.Context.Panel<BeaconLabelUi>()?.OpenStation( // #2999: with the station's yield strip
                Game.Localizer?.Get("ui.map.rename") ?? "Rename",
                Game.StationName,
                stationId,
                name => Game.Network?.SendSetStationName(stationId, name));
        }

        /// <summary>#2049: a Crystal Net device you're aiming at — a switch toggles, a button presses, everything with a menu
        /// opens it. Aim-based, so it works the same with mouse look, the right stick and the touch look pad.</summary>
        private void UseAimedCrystalDevice()
        {
            if (Game.AimedCrystalDevice is not { } crystalDev)
            {
                return;
            }

            var crystalKind = CrystalDeviceUi.KindOf(crystalDev);
            if (crystalKind == CrystalDeviceKind.Switch)
            {
                Game.SendCrystalDevice(crystalDev.X, crystalDev.Y, crystalDev.Z, 0);
            }
            else if (crystalKind is CrystalDeviceKind.Button or CrystalDeviceKind.LiftStop)
            {
                Game.SendCrystalDevice(crystalDev.X, crystalDev.Y, crystalDev.Z, 1); // a button presses; #2266: a lift stop calls
            }
            else if (CrystalNetRules.IsConfigurable(crystalKind))
            {
                Game?.Context.Panel<CrystalDeviceUi>()?.Open(crystalDev);
            }
        }

        /// <summary>A heal tank — or its low-tech precursor, the bed (#804) — you're aiming at → make it your home spawn point
        /// (base/station, issue #461). A bed or a crew bunk also lays you down to rest (#2823).</summary>
        private void UseAimedBed()
        {
            var tankHit = _aim.Cell;
            string tankKey = _aim.BlockKey;
            Game.Network?.SendSetSpawnPoint(tankHit.x, tankHit.y, tankHit.z);
            ClientAudio.Instance?.Cue("heal");
            if (tankKey != "heal_tank" && BedLooksTaken(tankHit))
            {
                // #2985 (Lyxette: "Ich kuschle hier mit einem Siedler"): a villager sleeps in it — say so instead of
                // laying the player down beside them.
                Game.ShowMessage(Game.Localizer?.Get("ui.rest.bed_taken") ?? "Someone is already sleeping here.");
            }
            else if (tankKey != "heal_tank")
            {
                TryLieDown(tankHit, onShip: false);
            }
        }

        /// <summary>A chair- or bench-shaped cell in any material seats the player (#806, #1846).</summary>
        private void SitOnAimedSeat()
        {
            // #2122: somebody already sits there — say so instead of dropping the camera into them. The server
            // checks again (it may know a sitter this client has not heard of yet).
            if (SeatLooksTaken(_aim.Cell))
            {
                Game.ShowMessage(Game.Localizer?.Get("srv.seat.taken") ?? "Someone is already sitting there.");
                return;
            }

            SitDown(_aim.Cell);
        }

        /// <summary>A data cube within reach downloads its minigame into the Arcade collection (item: arcade).</summary>
        private void DownloadDataCube()
        {
            if (_aim.CubeOwned)
            {
                Game.ShowMessage(Game.Localizer?.Get("ui.datacube.already") ?? "This fragment is already in your collection.");
            }
            else if (!string.IsNullOrEmpty(_aim.CubeGame))
            {
                Game.Network?.SendUnlockGame(_aim.Cube, _aim.CubeGame);
                ClientAudio.Instance?.At("data_cube_download", transform.position, 1f, 1f);
            }
        }

        /// <summary>A claimable factory terminal within reach → claim it with an access code (it becomes your base).</summary>
        private void ClaimFactory()
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
                Game.Network?.SendClaimStructure(_aim.Factory);
            }
            else
            {
                Game.ShowMessage(Game.Localizer?.Get("ui.factory.need_code") ?? "You need an access code to claim this.");
            }
        }

        /// <summary>E at a ship station (looked at or stood beside): the stations that open a client panel; the rest are
        /// resolved server-side.</summary>
        private void UseShipStation(string station)
        {
            switch (station)
            {
                case "cockpit":
                    UseCockpit();
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
                    Menu?.OpenMarket(0); // #2626: the ship's own trade console (no vendor)
                    Game.Network?.SendNpcGreet("vendor"); // item 15: vendor greeting
                    break;
                case "cargo": Menu?.OpenCargo(); break;
                case "console": Menu?.OpenShip(); Game.Network?.SendUseStation("console"); break; // ship status/repairs (#463)
                case "quarters":
                    // The ship's bunk sets the respawn to the quarters as before — and lays you down on it (#2823). A parked
                    // ship's hull is no world cell, so the aimed-bed pick never sees this one.
                    Game.Network?.SendUseStation("quarters");
                    if (QuartersBedCell(out var quarters, out bool quartersOnShip))
                    {
                        TryLieDown(quarters, quartersOnShip);
                    }

                    break;
                default:
                    if (station == "medbay") ClientAudio.Instance?.Cue("heal");
                    Game.Network?.SendUseStation(station);
                    break; // medbay and the rest, resolved server-side
            }
        }

        /// <summary>E at the cockpit. Aboard the floating interior the cockpit is the helm — the prompt says "Take the helm
        /// (fly)", so E takes it (#2705): the server returns to the flight view (or says why the ship can't fly). On a landed
        /// ship E asks "launch into space?" first (#1455): the launch button lived only at the top of the Map tab, and a
        /// first-time player at the cockpit never found it. Declining opens the map as before.</summary>
        private void UseCockpit()
        {
            if (Game.LoadingPlanetType == "ship_interior")
            {
                Game.Network?.SendUseStation("cockpit");
                return;
            }

            if (Game.Views.LaunchPrompt == null || !Game.Views.LaunchPrompt.TryOffer(() => Menu?.OpenMap()))
            {
                Menu?.OpenMap();
            }
        }

        /// <summary>E at a trader (under the crosshair, or the one in trading reach): "trade or talk?" — or, when no prompt can
        /// be shown, the trade screen bound to them (#2626).</summary>
        private void TradeWithVendor()
        {
            if (!OfferVendorChoice())
            {
                var tradeWith = VendorReach.ById(Game.Npcs, Game.InteractNpcId) ?? Game.NearestTradingVendor();
                Menu?.OpenMarket(tradeWith != null ? tradeWith.Id : 0);
                Game.Network?.SendNpcGreet("vendor"); // item 15: vendor greeting
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
        /// market as before). #3088: the trader under the crosshair when E picked one, else the one in trading reach.</summary>
        private bool OfferVendorChoice()
        {
            var prompt = Game?.Views.VendorChoice;
            if (prompt == null)
            {
                return false;
            }

            // #2626: the same picker as the "Trade" prompt (Game.NearVendor), and Trade binds the screen to THIS vendor.
            var vendor = VendorReach.ById(Game.Npcs, Game.InteractNpcId) ?? Game.NearestTradingVendor();
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

        /// <summary>True if the cell <paramref name="hit"/> in the crosshair holds a radio beacon the player owns — returns its
        /// id + current label so E can open the rename overlay (item 37). Only the owner gets the rename prompt; everyone
        /// sees markers.</summary>
        private bool TryAimOwnedBeacon(Vector3Int hit, out int beaconId, out string label)
        {
            beaconId = 0;
            label = string.Empty;
            if (Game?.Beacons == null || Game.Beacons.Length == 0 || string.IsNullOrEmpty(Game.LocalPlayerId))
            {
                return false;
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
        private bool TryAimOwnedBase(out string bodyId, out string name, out Vector3Int cell)
        {
            var b = AimedOwnedBase();
            bodyId = b?.BodyId ?? string.Empty;
            name = b?.Name ?? string.Empty;
            cell = b == null ? default : new Vector3Int(Mathf.FloorToInt(b.X), Mathf.FloorToInt(b.Y), Mathf.FloorToInt(b.Z));
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
