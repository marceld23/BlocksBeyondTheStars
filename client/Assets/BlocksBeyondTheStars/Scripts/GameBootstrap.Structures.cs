// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's world structures (#2912): ship stations, points of interest, beacons and beams, the crystal net
    /// and lifts, speeders and trains, bases, landing pads, containers, data cubes and factories.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>Interactive ship stations, and the one the player is currently next to (or empty).</summary>
        public NetShipStation[] Stations { get; private set; } = System.Array.Empty<NetShipStation>();

        /// <summary>Planet points of interest (settlement, …) for the world map.</summary>
        public NetPoi[] PlanetPois { get; private set; } = System.Array.Empty<NetPoi>();

        /// <summary>Placed radio beacons (labelled waypoints) for the world map + compass (item 37).</summary>
        public NetBeacon[] Beacons { get; private set; } = System.Array.Empty<NetBeacon>();

        /// <summary>Placed beam blocks (teleporter pads) on the current world, for the map + the transporter panel.</summary>
        public NetBeam[] Beams { get; private set; } = System.Array.Empty<NetBeam>();

        /// <summary>#2046 / #2268: the Crystal Net of the current world and of the own ships parked on it, composed in world
        /// cells (Unity-free bookkeeping in <see cref="ClientCrystalNet"/>).</summary>
        public readonly ClientCrystalNet Crystal = new ClientCrystalNet();

        /// <summary>#2046: the Crystal Net's networks on the current world (cells + ON/OFF) — <see cref="CrystalNetView"/> draws the glow.</summary>
        public NetCrystalNet[] CrystalNets => Crystal.Nets;

        /// <summary>#2046: the Crystal Net's devices on the current world (kind, mode, config, output) — for the E-ladder + the device menu.</summary>
        public NetCrystalDevice[] CrystalDevices => Crystal.Devices;

        /// <summary>#2049: the Crystal Net device at a world cell (a parked ship's included), or null.</summary>
        public NetCrystalDevice CrystalDeviceAt(int x, int y, int z) => Crystal.DeviceAt(x, y, z);

        /// <summary>#2268: sends a device intent in the device's own frame — a parked ship's device goes as its ship-local
        /// cell, tagged with the ship.</summary>
        public void SendCrystalDevice(int x, int y, int z, int action, int mode = 0, string config = "", string label = "")
        {
            if (Crystal.TryFrameCell(x, y, z, out string frame, out var local))
            {
                Network?.SendSetCrystalDevice(local.X, local.Y, local.Z, action, mode, config, label, frame);
            }
            else
            {
                Network?.SendSetCrystalDevice(x, y, z, action, mode, config, label);
            }
        }

        /// <summary>#2266: the lifts of the current world — <see cref="LiftView"/> draws and moves their platforms.</summary>
        public NetLift[] Lifts { get; private set; } = System.Array.Empty<NetLift>();

        /// <summary>When <see cref="Lifts"/> last arrived (unscaled time) — a moving platform eases on from there.</summary>
        public float LiftsReceivedAt { get; private set; }

        /// <summary>#2049: the Crystal Net device the player is looking at this frame (set by PlayerController, read by the HUD prompt).</summary>
        public NetCrystalDevice AimedCrystalDevice { get; set; }

        /// <summary>#2267: the block cell in the crosshair this frame (any block, the parked ships included), or null —
        /// <see cref="CrystalNetView"/> outlines the whole network it belongs to.</summary>
        public Vector3Int? AimedCell { get; set; }

        /// <summary>Live hover speeders on the current world (parked + driven). <see cref="SpeederView"/> renders
        /// them; <see cref="PlayerController"/> reads them to board/drive. Server-authoritative.</summary>
        public NetSpeeder[] Speeders { get; private set; } = System.Array.Empty<NetSpeeder>();

        /// <summary>Id of the speeder the local player is currently driving ("" = on foot) — from the authoritative
        /// player state. Drives the vehicle HUD + the drive controls.</summary>
        public string InSpeeder { get; private set; } = string.Empty;

        /// <summary>#2113: the monorail's rail lines and trains on the current world (<see cref="RailView"/> draws the lines,
        /// <see cref="TrainView"/> the wagons), and the train frame the local player rides in ("" = on foot) with the seat.</summary>
        public NetRailLine[] Rails { get; private set; } = System.Array.Empty<NetRailLine>();
        public NetTrain[] Trains { get; private set; } = System.Array.Empty<NetTrain>();
        public double TrainsReceivedAt { get; private set; }
        public string InTrain { get; private set; } = string.Empty;
        public int TrainSeat { get; private set; } = -1;

        /// <summary>The train the local player rides (null on foot).</summary>
        public NetTrain RiddenTrain
        {
            get
            {
                if (string.IsNullOrEmpty(InTrain) || !RailRules.TryParseFrame(InTrain, out string trainId, out _))
                {
                    return null;
                }

                foreach (var t in Trains)
                {
                    if (t != null && t.Id == trainId)
                    {
                        return t;
                    }
                }

                return null;
            }
        }

        /// <summary>Current local speeder ground speed (m/s, signed), published by <see cref="PlayerController"/>
        /// while driving so the vehicle HUD can show it.</summary>
        public float SpeederSpeed;

        /// <summary>True while the local player's boat (#1215) has no water under its hull (beached) — the HUD
        /// shows the aground hint. Published by <see cref="PlayerController"/>; always false on foot / in a speeder.</summary>
        public bool VehicleAground;

        /// <summary>The local player's driven speeder (null if not driving), for the HUD gauges + drive physics.</summary>
        public NetSpeeder DrivenSpeeder
        {
            get
            {
                if (string.IsNullOrEmpty(InSpeeder))
                {
                    return null;
                }

                foreach (var s in Speeders)
                {
                    if (s != null && s.Id == InSpeeder)
                    {
                        return s;
                    }
                }

                return null;
            }
        }

        /// <summary>Pending one-shot speeder effects (deploy shimmer / destruction burst). <see cref="SpeederView"/>
        /// drains this each frame and plays each at its position.</summary>
        public readonly System.Collections.Generic.List<SpeederFx> PendingSpeederFx = new();

        /// <summary>True if the local player may use this beam block as a source/destination: they own it or are
        /// allied with the owner (admins are handled server-side). Drives the transporter's destination list.</summary>
        public bool CanUseBeam(NetBeam b)
        {
            if (b == null)
            {
                return false;
            }

            if (b.OwnerId == LocalPlayerId)
            {
                return true;
            }

            var allies = Alliances?.Allies;
            if (allies != null)
            {
                foreach (var a in allies)
                {
                    if (a.PartnerId == b.OwnerId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>#2256: whether the local player may operate a Crystal Net device — the owner, an ally, and everyone on an
        /// ownerless device or a world circuit (#2260). The server decides; this only picks the HUD prompt.</summary>
        public bool CanOperateCrystal(NetCrystalDevice d)
        {
            if (d == null || string.IsNullOrEmpty(d.OwnerId) || d.OwnerId == CrystalNetRules.WorldOwnerId || d.OwnerId == LocalPlayerId)
            {
                return true;
            }

            var allies = Alliances?.Allies;
            if (allies != null)
            {
                foreach (var a in allies)
                {
                    if (a.PartnerId == d.OwnerId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Player-founded planet bases (Grundstein) on the current world, for the planet map markers.</summary>
        public NetBase[] Bases { get; private set; } = System.Array.Empty<NetBase>();

        /// <summary>Fixed landing pads + occupancy of the most recent <see cref="LandingPadList"/> (item 38) — the pad
        /// chooser's slot: it gates on <see cref="LandingPadsBody"/> matching the body it asked about. The world map reads
        /// <see cref="LandingPadsFor"/> instead (#2740), since any list — a padless body the chooser looked at — lands here.</summary>
        public NetLandingPad[] LandingPads { get; private set; } = System.Array.Empty<NetLandingPad>();
        public string LandingPadsBody { get; private set; } = string.Empty;

        /// <summary>The day fraction (0..1) the player arrives at when landing on <see cref="LandingPadsBody"/> —
        /// drives the day/night terminator on the pad chooser map (local time at a pad = this + padX/circ).</summary>
        public float LandingPadsTimeOfDay { get; private set; } = 0.35f;

        /// <summary>#2740: every body's last pad list, keyed by body id — a list for one body never overwrites another's.</summary>
        public LandingPadLists LandingPadBook { get; } = new LandingPadLists();

        /// <summary>#2740: the pads of <paramref name="bodyId"/> (the world map passes the active body), or null when no
        /// list for it has arrived yet.</summary>
        public NetLandingPad[] LandingPadsFor(string bodyId) => LandingPadBook.PadsFor(bodyId);
        public string NearbyStation;

        /// <summary>The placed crafting-station block the player is aiming at (workbench, forge, …) or null —
        /// drives the HUD "Workbench — crafting: Tab → Crafting" prompt (#1073). Set by PlayerController.</summary>
        public string AimedStationBlock;

        /// <summary>Your own base core in the crosshair (#1267) — the HUD prompt shows its air readout; null otherwise.</summary>
        public NetBase AimedOwnBase;

        /// <summary>True while the held scanner has something it can read under the crosshair (creature body,
        /// critter or block) — gates the "LMB: scan" prompt (#1458). Set by PlayerController each frame.</summary>
        public bool ScanTargetInView;

        /// <summary>True while the spawn/respawn settle freeze has held the body for more than a blink — the
        /// HUD shows "Stabilising…" so a frozen body reads as a state, not a bug (#1462). Set by PlayerController.</summary>
        public bool SettlingHint;

        /// <summary>Set when the appearance editor changed the arm painting/colours: the first-person hand is
        /// keyed on the hotbar item only, so without this it kept the old paint until a slot switch (#1464).
        /// PlayerController clears it on the next refresh.</summary>
        public bool HeldItemDirty;

        /// <summary>Lootable containers on the planet (salvage capsules / corpses).</summary>
        public NetContainer[] Containers { get; private set; } = System.Array.Empty<NetContainer>();

        /// <summary>Data cubes on the current world (synced from the server) — rendered by <c>DataCubeView</c>.</summary>
        public NetDataCube[] DataCubes { get; private set; } = System.Array.Empty<NetDataCube>();

        /// <summary>Factories on the current world (synced from the server) — rendered by <c>FactoryView</c>
        /// (animated machines) and used for the roster-filtered production terminal.</summary>
        public NetFactory[] Factories { get; private set; } = System.Array.Empty<NetFactory>();
    }
}
