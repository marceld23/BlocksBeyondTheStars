// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Localization;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3088 — the HUD's on-foot prompt line. It names what the interact key does right now: the pick PlayerController made
    /// this frame (<see cref="GameBootstrap.Interaction"/>, <see cref="InteractResolver"/>) and acts on when E is pressed —
    /// the HUD no longer runs a chain of its own that could promise "Talk to Vhaerk (E)" while E swung the door beside him.
    /// Only when E does nothing that has a line here does it fall back to what the crosshair rests on (a workbench names the
    /// menu tab it powers, a linked Crystal Net device, a mission board too far off) and the other hints.
    /// </summary>
    public sealed partial class HudUi
    {
        /// <summary>The on-foot prompt (the flight view draws its own): the settle freeze and the bed come first, then boarding
        /// an own speeder (E boards it before anything else), then what E does, then what the crosshair rests on.</summary>
        private string OnFootPrompt(Localizer loc)
        {
            if (Game.SettlingHint)
            {
                // The spawn/respawn settle freeze is holding the body (#1462) — say so instead of leaving a fully drawn ship
                // that ignores WASD.
                return loc.Get("ui.hud.settling");
            }

            if (RestingInBed())
            {
                // #2823: lying in a bed — how to get up, in the glyphs of the device in hand.
                return ExpandKeyTokens(loc, loc.Get("ui.rest.get_up"));
            }

            bool onFoot = string.IsNullOrEmpty(Game.InSpeeder);
            if (onFoot && OwnParkedVehicleInBoardReach())
            {
                // Beside an own parked speeder/boat (#1670): "Board (E) · Pack up (X)", the same verbs PlayerController polls
                // on foot — and E boards it before any other interaction (#3088: so this line comes first too).
                return $"{loc.Get("hud.speeder.board")} {InParentheses(PromptGlyph(loc, InputAction.Interact))}"
                    + $"  ·  {loc.Get("hud.speeder.stow")} {InParentheses(PromptGlyph(loc, InputAction.StowVehicle))}";
            }

            var kind = onFoot && Game.InteractionFrame >= Time.frameCount - 1 ? Game.Interaction : InteractKind.None;
            string use = InteractPrompt(loc, kind);
            if (use.Length > 0)
            {
                return use;
            }

            // A door, a data cube, a net fragment or a factory terminal names itself in the world. A person beside it is
            // talked to by looking at them — say so instead of nothing (Justus could not talk to the resident by the door).
            if (onFoot && InteractResolver.HasWorldLabel(kind) && NearestTalkableNpc() is { } beside)
            {
                return string.Format(loc.Get("ui.hud.talk_look"), beside);
            }

            return AimedInfoPrompt(loc, onFoot);
        }

        /// <summary>The line for what E does — empty for a crate (the stash line names it), a door, a data cube, a factory
        /// terminal and a net fragment (their labels in the world do) and for nothing.</summary>
        private string InteractPrompt(Localizer loc, InteractKind kind)
        {
            switch (kind)
            {
                case InteractKind.AimedTalk:
                case InteractKind.Talk:
                    return TalkPrompt(loc);
                case InteractKind.AimedTrade:
                case InteractKind.Trade:
                    return $"{ExpandKeyTokens(loc, loc.Get("ui.hud.use"))}: {loc.Get("ui.station.market")}"; // trade or talk
                case InteractKind.OwnBeacon:
                    return $"{ExpandKeyTokens(loc, loc.Get("ui.hud.use"))}: {loc.Get("ui.beacon.rename_prompt")}";
                case InteractKind.OwnBase:
                    return Game.AimedOwnBase is { } ownBase ? BaseAirPrompt(loc, ownBase) : string.Empty;
                case InteractKind.BasePad:
                    return Game.AimedBasePadCell is { } padCell ? BasePadPrompt(loc, padCell) : string.Empty; // #3070
                case InteractKind.StationCore:
                    return $"{ExpandKeyTokens(loc, loc.Get("ui.hud.use"))}: {loc.Get("ui.map.rename")}";
                case InteractKind.CrystalDevice:
                    return Game.AimedCrystalDevice is { } device ? CrystalDevicePrompt(loc, device) : string.Empty;
                case InteractKind.BioLab:
                case InteractKind.SpawnBed:
                    return StationBlockPrompt(loc);
                case InteractKind.Seat:
                    return ExpandKeyTokens(loc, loc.Get("ui.hud.sit"));
                case InteractKind.MissionBoard:
                    return ExpandKeyTokens(loc, loc.Get("ui.board.prompt")); // #3087: "Mission board — E: jobs"
                case InteractKind.LookedStation:
                case InteractKind.NearStation:
                    return ShipStationPrompt(loc);
                case InteractKind.BeamPad:
                    // Standing on / beside an own or allied teleporter pad — the pad never announced itself before (#1560).
                    return ExpandKeyTokens(loc, loc.Get("ui.beam.use_hint")); // "Press {key:Interact}: Transporter" (#2463)
                case InteractKind.LiftRide:
                    return string.Format(loc.Get("ui.lift.prompt.choose"), InteractGlyph(loc)); // #3171: riding — choose the floor
                case InteractKind.UseBlock:
                    return $"{ExpandKeyTokens(loc, loc.Get("ui.hud.use"))}: {loc.Get(UseBlockLabelKey(loc, Game.AimedUseBlockKey))}"; // #3345
                case InteractKind.AimedCompanion:
                    return CompanionTalkPrompt(loc); // #3394: "Talk to Robo (E)"
                case InteractKind.GiveCompanion:
                    return CompanionGivePrompt(loc); // #3398/#3399: "Give Laser pistol to Robo (E)"
                default:
                    return string.Empty;
            }
        }

        /// <summary>What the crosshair rests on when E does nothing with a line of its own, then the scanner's and the parked
        /// vehicle's hints.</summary>
        private string AimedInfoPrompt(Localizer loc, bool onFoot)
        {
            if (Game.AimedBoardOutOfReach)
            {
                return loc.Get("ui.board.closer"); // #3087: the server takes jobs at the board only
            }

            if (!string.IsNullOrEmpty(Game.AimedStationBlock))
            {
                return StationBlockPrompt(loc); // #1073: "Workbench — crafting: menu (Tab) → Crafting"
            }

            if (Game.AimedCrystalDevice is { } device)
            {
                return CrystalDevicePrompt(loc, device); // a device that only links, or someone else's
            }

            if (HoldingScanner() && Game.ScanTargetInView)
            {
                // Only when the scanner would actually read something — an unconditional hint over a target the scan can't
                // acquire read as "titans can't be scanned" (#1458).
                return ExpandKeyTokens(loc, loc.Get("ui.scan.use_hint")); // "{key:Scan}: scan" (#2502)
            }

            if (onFoot && OwnParkedVehicleOutOfReach(out string vehicleKind, out float vehicleDist))
            {
                // An own vehicle a few metres off but beyond the pack-up reach (#1661): the X action used to stay silent
                // exactly when the speeder had sunk or wedged just out of reach.
                string stowGlyph = PromptGlyph(loc, InputAction.StowVehicle); // #2502: the Actions list / ACT, not "X"
                return LineText.CollapseGlyphParentheses(
                    string.Format(loc.Get("hud.speeder.out_of_reach"),
                        loc.Get(vehicleKind == "boat" ? "item.boat.name" : "item.speeder.name"),
                        Mathf.RoundToInt(vehicleDist), stowGlyph),
                    stowGlyph);
            }

            return string.Empty;
        }

        /// <summary>"Talk to … (E)" for the person E picked (#1869) — the one under the crosshair or the nearest in reach.</summary>
        private string TalkPrompt(Localizer loc)
        {
            var npc = VendorReach.ById(Game.Npcs, Game.InteractNpcId);
            if (npc == null)
            {
                return string.Empty;
            }

            string name = string.IsNullOrEmpty(npc.Name) ? loc.Get(npc.NameKey) : npc.Name;
            string talkGlyph = PromptGlyph(loc, InputAction.Interact); // #2502: USE on a tablet, not "E"
            return LineText.CollapseGlyphParentheses(string.Format(loc.Get("ui.hud.talk"), name, talkGlyph), talkGlyph);
        }

        /// <summary>#3394: "Talk to Robo (E)" for the own robot companion under the crosshair — the same words as for a person.</summary>
        private string CompanionTalkPrompt(Localizer loc)
        {
            string name = Game.InteractCompanionName;
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            string talkGlyph = PromptGlyph(loc, InputAction.Interact);
            return LineText.CollapseGlyphParentheses(string.Format(loc.Get("ui.hud.talk"), name, talkGlyph), talkGlyph);
        }

        /// <summary>#3398/#3399: "Give Laser pistol to Robo (E)" — a weapon or a jetpack in hand, the own robot under the crosshair.</summary>
        private string CompanionGivePrompt(Localizer loc)
        {
            string name = Game.InteractCompanionName, item = Game.InteractGiftItem;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(item))
            {
                return string.Empty;
            }

            var def = Game.Content?.GetItem(item);
            string itemName = def != null ? loc.Get(def.NameKey) : item;
            string glyph = PromptGlyph(loc, InputAction.Interact);
            return LineText.CollapseGlyphParentheses(string.Format(loc.Get("ui.interact.give_companion"), itemName, name, glyph), glyph);
        }

        /// <summary>The ship station E uses ("Press E: Workshop") and what else is done there: stowing at the cargo hold,
        /// calling a vehicle back and the repair trigger at the cockpit or the console.</summary>
        private string ShipStationPrompt(Localizer loc)
        {
            // Inside the ship while it floats in space, the cockpit reads as the helm (take it to fly again).
            string stationKey = (Game.NearbyStation == "cockpit" && Game.LoadingPlanetType == "ship_interior")
                ? "ui.station.helm"
                : $"ui.station.{Game.NearbyStation}";
            string prompt = $"{ExpandKeyTokens(loc, loc.Get("ui.hud.use"))}: {loc.Get(stationKey)}"; // "Press {key:Interact}" (#2463)
            if (Game.NearbyStation == "cargo")
            {
                // #2502: the glyph names the pad's Actions list / the tablet's ACT button, not the key letter.
                prompt += $"  ·  {loc.Get("ui.hud.stow_cargo")} {InParentheses(PromptGlyph(loc, InputAction.DepositToCrate))}";
            }

            bool helm = Game.NearbyStation == "cockpit" || Game.NearbyStation == "console";
            if (helm && Game.LoadingPlanetType != "ship_interior" && OwnParkedVehicleCount() > 0)
            {
                // The ship can call a vehicle left out on this world back beside it (#1661).
                prompt += $"  ·  {loc.Get("ui.key.recall_vehicle")} {InParentheses(PromptGlyph(loc, InputAction.RecallVehicle))}";
            }

            if (helm && Game.ShipRepair != null && InputMap.ActiveDevice != InputDeviceKind.Touch)
            {
                // Something to repair and this is the terminal for it: name the trigger right here (the static cockpit label
                // no longer carries a hard-coded "(R)"; a pad reaches it through the Actions list).
                string g = InputMap.ActiveDevice == InputDeviceKind.Gamepad
                    ? InputMap.Glyph(InputAction.ContextActions)
                    : InputMap.Glyph(InputAction.RepairWreck);
                prompt += $"  ·  {loc.Get("ui.shiprepair.repair")} ({g})";
            }

            return prompt;
        }

        /// <summary>#1073: the aimed station block's line — the tab a workbench powers, what E does at a bed; #2203: the bio
        /// lab names its own panel with the Interact glyph.</summary>
        private string StationBlockPrompt(Localizer loc)
        {
            string block = Game.AimedStationBlock;
            if (string.IsNullOrEmpty(block))
            {
                return string.Empty;
            }

            return block == BlocksBeyondTheStars.Shared.Bio.BioItems.Lab
                ? string.Format(loc.Get("ui.bio.prompt"), InteractGlyph(loc))
                : ExpandKeyTokens(loc, loc.Get(StationBlockPromptKey(block)));
        }

        /// <summary>#2049: what Interact does to the Crystal Net device in the crosshair — the glyph follows the device in
        /// hand; #2256: someone else's device says who may use it instead of offering a key that would be refused. #3172: a
        /// named device is called by its name — "E: North drawbridge" for a menu, the verb and the name for the rest.</summary>
        private string CrystalDevicePrompt(Localizer loc, NetCrystalDevice device)
        {
            bool liftWaits = LiftRiding.WaitingAtStop(Game.Lifts, device.X, device.Y, device.Z, Game.Circumference) != null;
            string key = Game.CanOperateCrystal(device) ? CrystalPromptKey(device, liftWaits) : "ui.crystal.prompt.locked";
            string glyph = InteractGlyph(loc);
            if (string.IsNullOrEmpty(device.Label))
            {
                return string.Format(loc.Get(key), glyph);
            }

            return key == "ui.crystal.prompt.menu"
                ? string.Format(loc.Get("ui.crystal.prompt.named"), glyph, device.Label)
                : string.Format(loc.Get(key), glyph) + "  ·  " + device.Label;
        }
    }
}
