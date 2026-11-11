// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3088 — what E does on foot, decided ONCE per frame: the facts around the player (what the crosshair rests on, what
    /// stands close by) go to <see cref="InteractResolver"/>, the pick goes to <see cref="GameBootstrap.Interaction"/>, and
    /// both the key (<see cref="RunInteraction"/>) and the HUD's prompt line act on that one value. Before, the HUD asked its
    /// own chain in its own order and promised "Talk to Vhaerk (E)" while E swung the door beside him (Justus 2026-11-01).
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>The cells and ids the facts were read from — what the E press acts on. Reset and the aimed world cell
        /// read by <see cref="PublishAimState"/>, the rest filled by <see cref="ResolveInteraction"/>, every on-foot frame.</summary>
        private sealed class InteractAim
        {
            public bool BlockAimed;   // AimBlock found a world cell in reach
            public Vector3Int Cell;   // that cell (a scene cell)
            public string BlockKey;   // its block key
            public int BeaconId;
            public string BeaconLabel = string.Empty;
            public NetContainer Crate;
            public int Door;
            public int Beam;
            public int Cube;
            public string CubeGame = string.Empty;
            public bool CubeOwned;
            public int Factory;
            public int Fragment;
            public int Npc;           // the person under the crosshair
            public NetLift Lift;      // #3171: the lift platform the player stands on
            public NetCreature Companion; // #3394: the own robot companion under the crosshair
            public string GiftItem = string.Empty; // #3398/#3399: the held weapon or jetpack the robot would take

            public void Reset()
            {
                BlockAimed = false;
                BlockKey = null;
                BeaconId = Door = Beam = Cube = Factory = Fragment = Npc = 0;
                BeaconLabel = CubeGame = GiftItem = string.Empty;
                Crate = null;
                Lift = null;
                Companion = null;
                CubeOwned = false;
            }
        }

        private readonly InteractFacts _facts = new InteractFacts();
        private readonly InteractAim _aim = new InteractAim();
        private string _lookedStation = string.Empty; // the ship station under the crosshair (this frame)
        private string _nearStation = string.Empty;   // the ship station within 3 m when none is looked at
        private bool _targetAimed;                     // AimTarget (world or parked ship cell) found a cell in reach
        private Vector3Int _targetHit;
        private LandedShipModel _targetShip;

        /// <summary>Gathers this frame's facts and publishes the pick (<see cref="GameBootstrap.Interaction"/>, the person it
        /// names). <paramref name="handDoor"/> is the hand door E works, <paramref name="doorAimed"/> whether it is the one under
        /// the crosshair.</summary>
        private void ResolveInteraction(int handDoor, bool doorAimed)
        {
            _facts.Clear(); // _aim was reset and the aimed world cell read by PublishAimState this frame
            GatherAimedBlockFacts();
            GatherLiftFacts(); // #3171: after the aimed blocks — the platform hides the motor under it
            GatherAimedNpcFact();
            GatherAimedCompanionFact(); // #3394: the own robot — E talks to it
            _facts.LookedStation = _lookedStation;
            _aim.Door = handDoor;
            _facts.AimedDoor = handDoor != 0 && doorAimed;
            _facts.NearDoor = handDoor != 0 && !doorAimed;
            GatherNearbyFacts();

            Game.Interaction = InteractResolver.Resolve(_facts);
            Game.InteractionFrame = Time.frameCount;
            Game.InteractNpcId = Game.Interaction switch
            {
                InteractKind.AimedTalk or InteractKind.AimedTrade => _aim.Npc,
                InteractKind.Trade => Game.NearestTradingVendor()?.Id ?? 0,
                InteractKind.Talk => Game.NearestTalkableNpc()?.Id ?? 0,
                _ => 0,
            };
            bool robot = Game.Interaction is InteractKind.AimedCompanion or InteractKind.GiveCompanion;
            Game.InteractCompanionName = robot ? CompanionAim.DisplayName(_aim.Companion) : string.Empty;
            Game.InteractGiftItem = Game.Interaction == InteractKind.GiveCompanion ? _aim.GiftItem : string.Empty; // #3398/#3399
        }

        /// <summary>The blocks under the crosshair: the own beacon, the boarded station's core, a crate, a bio lab in reach, a
        /// bed, a seat (world cells), the own base core and a base pad (published above), a Crystal Net device E works and a
        /// mission board in reach (world or own ship cell).</summary>
        private void GatherAimedBlockFacts()
        {
            if (_aim.BlockAimed)
            {
                var aimHit = _aim.Cell;
                string key = _aim.BlockKey;
                _facts.OwnBeacon = TryAimOwnedBeacon(aimHit, out _aim.BeaconId, out _aim.BeaconLabel);
                _facts.StationCore = !string.IsNullOrEmpty(Game.CurrentStationId) && key == "station_core";
                _aim.Crate = key is "crate" or "wood_crate" or BlocksBeyondTheStars.Shared.State.CarriedContents.TransportCrate // #3098
                    ? AimedCrate(aimHit)
                    : null;
                _facts.Crate = _aim.Crate != null;
                _facts.BioLab = key == BlocksBeyondTheStars.Shared.Bio.BioItems.Lab && BioLabInReach(aimHit);
                _facts.SpawnBed = key is "heal_tank" or "bed" or "crew_bunk";
                _facts.Seat = _seatCell is null
                    && FurnitureShapes.IsSeat(ShapeCode.ShapeOf(Game.World.GetShape(aimHit.x, aimHit.y, aimHit.z)));
                _facts.UsableBlock = key != null && Game.Content?.GetBlock(key)?.Usable == true; // #3345: a rune stone sings
            }

            // #3474: aboard, a wagon seat in reach shows the same "sit down" prompt (a person under the crosshair still wins).
            _facts.Seat |= _train.Frame != null && Game.TrainSeat < 0 && _train.SeatInReach();
            Game.AimedUseBlockKey = _facts.UsableBlock ? _aim.BlockKey : null; // the HUD names the use ("E: play the rune")

            _facts.OwnBase = Game.AimedOwnBase != null;
            _facts.BasePad = Game.AimedBasePadCell != null;
            _facts.CrystalDevice = Game.AimedCrystalDevice is { } dev && CrystalWorksOnInteract(dev);
            _facts.MissionBoard = AimedMissionBoardInReach();
        }

        /// <summary>#3171: the lift platform the player stands on — E there opens the floor picker (the platform is no block, so
        /// the crosshair never rests on it). Standing on it, the cells under the platform are hidden by it: the voxel-only aim
        /// ray went straight through onto the motor at the bottom floor, and E opened the motor's menu instead.</summary>
        private void GatherLiftFacts()
        {
            var feet = transform.position;
            _aim.Lift = Game.SpaceViewActive ? null : LiftRiding.StandingOn(Game.Lifts, feet.x, feet.y, feet.z, Game.Circumference);
            _facts.OnLift = _aim.Lift != null;
            if (_aim.Lift == null)
            {
                return;
            }

            if (_aim.BlockAimed && LiftRiding.HidesCell(_aim.Lift, _aim.Cell.x, _aim.Cell.y, _aim.Cell.z, Game.Circumference))
            {
                _facts.OwnBeacon = _facts.StationCore = _facts.Crate = _facts.BioLab = _facts.SpawnBed = _facts.Seat = _facts.UsableBlock = false;
            }

            if (_targetAimed && LiftRiding.HidesCell(_aim.Lift, _targetHit.x, _targetHit.y, _targetHit.z, Game.Circumference))
            {
                _facts.CrystalDevice = _facts.MissionBoard = false;
            }
        }

        /// <summary>#3171: opens the floor picker of a lift.</summary>
        private void OpenLiftFloors(NetLift lift)
        {
            if (lift != null)
            {
                Game?.Context.Panel<LiftFloorUi>()?.Open(lift.Id);
            }
        }

        /// <summary>The storage crate registered at the aimed cell (#2436) — matched by the container list, not the block alone,
        /// so a crate block that has not registered yet is no target. #2613: the crosshair cell is a scene cell and the crate's
        /// a canonical one — matched the short way round the seam.</summary>
        private NetContainer AimedCrate(Vector3Int crateHit)
            => InteractReach.CrateAtCell(Game.Containers, crateHit.x, crateHit.y, crateHit.z, Game.Circumference);

        /// <summary>#2049: whether E does something to the device — a switch toggles, a button presses, a lift stop calls
        /// the platform, a configurable device opens its menu. A device that only links does not take E.</summary>
        private static bool CrystalWorksOnInteract(NetCrystalDevice dev)
        {
            var kind = CrystalDeviceUi.KindOf(dev);
            return kind is CrystalDeviceKind.Switch or CrystalDeviceKind.Button or CrystalDeviceKind.LiftStop
                || CrystalNetRules.IsConfigurable(kind);
        }

        /// <summary>#3087: a mission board under the crosshair within the server's board reach — a world cell (settlement,
        /// station, base) or a cell of the player's OWN parked ship (a friend's ship's board is the host's). One just out of
        /// reach sets <see cref="GameBootstrap.AimedBoardOutOfReach"/> instead (the HUD says to walk closer).</summary>
        private bool AimedMissionBoardInReach()
        {
            Game.AimedBoardOutOfReach = false;
            if (!_targetAimed)
            {
                return false;
            }

            var hit = _targetHit;
            string key = _targetShip == null
                ? Game.Content?.BlockById(Game.World.GetBlock(hit.x, hit.y, hit.z))?.Key
                : _targetShip.OwnerId == Game.LocalPlayerId && !Game.Visiting
                    ? Game.Content?.BlockById(Game.LandedShipBlockAt(hit.x, hit.y, hit.z, out _, out _))?.Key
                    : null;
            if (key != InteractionReach.MissionBoardBlock)
            {
                return false;
            }

            var centre = new Vector3(hit.x + 0.5f, hit.y + 0.5f, hit.z + 0.5f);
            bool inReach = (centre - transform.position).sqrMagnitude <= InteractionReach.MissionBoard * InteractionReach.MissionBoard;
            Game.AimedBoardOutOfReach = !inReach;
            return inReach;
        }

        /// <summary>#3088: the person under the crosshair — within talk reach, the aim ray entering their body before any
        /// solid block (a wall in front hides them). A trader in trading reach offers "trade or talk?".</summary>
        private void GatherAimedNpcFact()
        {
            if (Camera == null || !string.IsNullOrEmpty(Game.InSpeeder))
            {
                return;
            }

            Vector3 o = Camera.transform.position, d = Camera.transform.forward, p = transform.position;
            var npc = NpcAim.Aimed(Game.Npcs, o.x, o.y, o.z, d.x, d.y, d.z, Reach, p.x, p.y, p.z, Game.Circumference, out float npcDistance);
            if (npc == null)
            {
                return;
            }

            if (_targetAimed && RayBox.Entry(o.x, o.y, o.z, d.x, d.y, d.z, _targetHit.x, _targetHit.y, _targetHit.z,
                    _targetHit.x + 1f, _targetHit.y + 1f, _targetHit.z + 1f) < npcDistance)
            {
                return; // a wall, a hull or the ground in front of them
            }

            _aim.Npc = npc.Id;
            _facts.AimedNpc = true;
            _facts.AimedNpcTrades = npc.Role == VendorReach.VendorRole
                && InteractReach.SeamDistanceSquared(p.x, p.y, p.z, npc.X, npc.Y, npc.Z, Game.Circumference)
                   <= MarketStock.TradeReach * (double)MarketStock.TradeReach;
        }

        /// <summary>#3394: the player's own robot companion under the crosshair — within talk reach, the aim ray entering its body
        /// before any solid block (like a person, <see cref="GatherAimedNpcFact"/>). Another player's robot is no target.</summary>
        private void GatherAimedCompanionFact()
        {
            if (Camera == null || !string.IsNullOrEmpty(Game.InSpeeder))
            {
                return;
            }

            Vector3 o = Camera.transform.position, d = Camera.transform.forward, p = transform.position;
            var robot = CompanionAim.AimedOwnRobot(Game.Creatures, Game.LocalPlayerId, o.x, o.y, o.z, d.x, d.y, d.z, Reach,
                p.x, p.y, p.z, Game.Circumference, out float robotDistance);
            if (robot == null)
            {
                return;
            }

            if (_targetAimed && RayBox.Entry(o.x, o.y, o.z, d.x, d.y, d.z, _targetHit.x, _targetHit.y, _targetHit.z,
                    _targetHit.x + 1f, _targetHit.y + 1f, _targetHit.z + 1f) < robotDistance)
            {
                return; // a wall, a hull or the ground in front of it
            }

            _aim.Companion = robot;
            _facts.AimedCompanion = true;

            // #3398/#3399: a weapon or a jetpack in hand — E gives it to the robot instead of talking.
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            _facts.HoldsRobotGear = RobotGearView.GiftPiece(Game.Content, held).Length > 0;
            _aim.GiftItem = _facts.HoldsRobotGear ? held : string.Empty;
        }

        /// <summary>#3394: E at the own robot — it answers in VEGA's panel (the server picks the line).</summary>
        private void TalkToAimedCompanion()
        {
            if (_aim.Companion != null)
            {
                Game.Network?.SendTalkToCompanion(CompanionAim.RobotCompanionId(Game.Companions));
            }
        }

        /// <summary>#3398/#3399: E at the own robot with a weapon or a jetpack in hand — one of it goes to the robot (the server
        /// checks the slot, the robot and the reach).</summary>
        private void GiveToAimedCompanion()
        {
            if (_aim.Companion != null)
            {
                Game.Network?.SendGiveCompanionGear(CompanionAim.RobotCompanionId(Game.Companions), Game.SelectedHotbarSlot);
            }
        }

        /// <summary>What stands close by: a usable beam pad, a data cube, a claimable factory terminal, a net fragment, a ship
        /// station within 3 m, a trader, a person to talk to — each with the reach its E always had.</summary>
        private void GatherNearbyFacts()
        {
            var pos = transform.position;
            _aim.Beam = Game.Views.Beams != null ? Game.Views.Beams.NearestUsableBeam(pos, 2.2f) : 0;
            _facts.BeamPad = _aim.Beam != 0;
            if (Game.Views.DataCubes != null)
            {
                _aim.Cube = Game.Views.DataCubes.NearestDataCube(pos, DataCubeView.Reach, out _aim.CubeGame, out _aim.CubeOwned);
                _facts.DataCube = _aim.Cube != 0;
            }

            _aim.Factory = Game.Views.Factories != null ? Game.Views.Factories.NearestClaimable(pos, 4f) : 0;
            _facts.Factory = _aim.Factory != 0;
            _aim.Fragment = Game.Views.NetFragments != null ? Game.Views.NetFragments.NearestNetFragment(pos, NetFragmentView.Reach, out _) : 0;
            _facts.NetFragment = _aim.Fragment != 0;
            _facts.NearStation = _nearStation;
            _facts.Vendor = Game.NearVendor;
            _facts.TalkNear = Game.NearestTalkableNpc() != null;
        }

        /// <summary>E: does what <see cref="GameBootstrap.Interaction"/> names — the very thing the HUD's prompt says.</summary>
        private void RunInteraction(InteractKind kind)
        {
            switch (kind)
            {
                case InteractKind.AimedTalk:
                case InteractKind.Talk:
                    Game.Network?.SendTalkToNpc(Game.InteractNpcId); // #1127
                    break;
                case InteractKind.AimedTrade:
                case InteractKind.Trade:
                    TradeWithVendor();
                    break;
                case InteractKind.OwnBeacon:
                    RenameAimedBeacon();
                    break;
                case InteractKind.OwnBase:
                    OpenAimedBase();
                    break;
                case InteractKind.BasePad:
                    TryOpenBasePad(); // #3070
                    break;
                case InteractKind.StationCore:
                    RenameBoardedStation();
                    break;
                case InteractKind.Crate:
                    Game?.Context.Panel<CrateUi>()?.Open(_aim.Crate.Id, _aim.Crate.Filter); // #2436
                    break;
                case InteractKind.CrystalDevice:
                    UseAimedCrystalDevice();
                    break;
                case InteractKind.BioLab:
                    Game?.Context.Panel<BioLabUi>()?.Open(); // #2203
                    break;
                case InteractKind.SpawnBed:
                    UseAimedBed();
                    break;
                case InteractKind.Seat:
                    SitOnAimedSeat();
                    break;
                case InteractKind.MissionBoard:
                    Menu?.OpenMissionBoard(); // #3087
                    break;
                case InteractKind.LookedStation:
                case InteractKind.NearStation:
                    UseShipStation(Game.NearbyStation);
                    break;
                default:
                    RunNearbyInteraction(kind);
                    break;
            }
        }

        /// <summary>E on a lift platform, at a door, a beam pad, a data cube, a factory terminal or a net fragment — and on a
        /// usable block (#3345: the server decides what it does; a rune stone sings its word) and at the own robot (#3394).</summary>
        private void RunNearbyInteraction(InteractKind kind)
        {
            switch (kind)
            {
                case InteractKind.AimedCompanion:
                    TalkToAimedCompanion();
                    break;
                case InteractKind.GiveCompanion:
                    GiveToAimedCompanion(); // #3398/#3399
                    break;
                case InteractKind.UseBlock:
                    Game.Network?.SendUseBlock(_aim.Cell.x, _aim.Cell.y, _aim.Cell.z);
                    break;
                case InteractKind.LiftRide:
                    OpenLiftFloors(_aim.Lift); // #3171
                    break;
                case InteractKind.AimedDoor:
                case InteractKind.NearDoor:
                    Game.Network?.SendDoorInteract(_aim.Door); // B47, #2147
                    break;
                case InteractKind.BeamPad:
                    Game?.Context.Panel<BeamPadUi>()?.Open(_aim.Beam);
                    break;
                case InteractKind.DataCube:
                    DownloadDataCube();
                    break;
                case InteractKind.Factory:
                    ClaimFactory();
                    break;
                case InteractKind.NetFragment:
                    Game.Network?.SendNetFragmentFound(_aim.Fragment);
                    ClientAudio.Instance?.At("data_cube_download", transform.position, 1f, 1f);
                    break;
            }
        }
    }
}
