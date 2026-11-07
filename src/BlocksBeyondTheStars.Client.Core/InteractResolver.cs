// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What the interact key (E) does on foot right now (#3088) — one value the key handler acts on and the HUD's
    /// prompt line names, so the prompt can never promise something else than the key does.</summary>
    public enum InteractKind
    {
        /// <summary>Nothing — E does nothing here.</summary>
        None = 0,

        // ---- the aimed tier: what the crosshair rests on ----

        /// <summary>A person under the crosshair (nothing solid in front of them) — talk to them.</summary>
        AimedTalk,

        /// <summary>A trader under the crosshair within trading reach — "trade or talk?".</summary>
        AimedTrade,

        /// <summary>The own radio beacon — rename it.</summary>
        OwnBeacon,

        /// <summary>The own base core — its panel (name, residents, yield, focus).</summary>
        OwnBase,

        /// <summary>A base landing pad — its panel.</summary>
        BasePad,

        /// <summary>The core of the own boarded station — rename it.</summary>
        StationCore,

        /// <summary>A storage crate — its screen.</summary>
        Crate,

        /// <summary>A Crystal Net device E works (a switch, a button, a lift stop, a device with a menu).</summary>
        CrystalDevice,

        /// <summary>A bio lab in reach — its panel.</summary>
        BioLab,

        /// <summary>A bed, a crew bunk or a heal tank — the home spawn (and a bed lays the player down).</summary>
        SpawnBed,

        /// <summary>A chair- or bench-shaped cell — sit down.</summary>
        Seat,

        /// <summary>A mission board in reach (a world cell or a cell of the own ship, #3087) — the Missions tab.</summary>
        MissionBoard,

        /// <summary>A ship station under the crosshair (cockpit, workshop, cargo, …).</summary>
        LookedStation,

        /// <summary>A hand door under the crosshair — open or close it.</summary>
        AimedDoor,

        // ---- the proximity tier: what stands beside the player ----

        /// <summary>A teleporter pad the player stands on or beside — the transporter.</summary>
        BeamPad,

        /// <summary>The nearest hand door — open or close it.</summary>
        NearDoor,

        /// <summary>A data cube in reach — download its minigame.</summary>
        DataCube,

        /// <summary>A claimable factory terminal — claim it.</summary>
        Factory,

        /// <summary>A net fragment in reach — recover it.</summary>
        NetFragment,

        /// <summary>A ship station within a few metres, whatever the crosshair rests on.</summary>
        NearStation,

        /// <summary>A trader within trading reach — "trade or talk?".</summary>
        Trade,

        /// <summary>The nearest person within talk reach — talk to them.</summary>
        Talk,

        /// <summary>#3171: the player stands on a lift platform — choose the floor (first of the proximity tier).</summary>
        LiftRide,

        /// <summary>#3345: a usable block under the crosshair (a rune stone, a Song Spindle) — the server's use of it. The last
        /// of the AIMED tier (appended so no stored value moves): anything else E works on the same cell comes first.</summary>
        UseBlock,

        /// <summary>#3394: the player's own robot companion under the crosshair (nothing solid in front) — talk to it. Of the
        /// AIMED tier, right after a person (appended so no stored value moves).</summary>
        AimedCompanion,

        /// <summary>#3398/#3399: the own robot under the crosshair while a weapon or a jetpack is held — give it to the robot
        /// (instead of talking). Of the AIMED tier, in the robot's place.</summary>
        GiveCompanion,
    }

    /// <summary>
    /// The facts the interact pick is made from (#3088), gathered once per frame by the client's player rig: what the
    /// crosshair rests on (in reach) and what stands close by. Plain flags so the pick stays Unity-free and testable.
    /// </summary>
    public sealed class InteractFacts
    {
        /// <summary>A person under the crosshair, in talk reach and with nothing solid in front.</summary>
        public bool AimedNpc { get; set; }

        /// <summary>The person under the crosshair is a trader within trading reach.</summary>
        public bool AimedNpcTrades { get; set; }

        public bool OwnBeacon { get; set; }
        public bool OwnBase { get; set; }
        public bool BasePad { get; set; }
        public bool StationCore { get; set; }
        public bool Crate { get; set; }
        public bool CrystalDevice { get; set; }
        public bool BioLab { get; set; }
        public bool SpawnBed { get; set; }
        public bool Seat { get; set; }
        public bool MissionBoard { get; set; }

        /// <summary>A ship station under the crosshair (empty: none).</summary>
        public string LookedStation { get; set; } = string.Empty;

        public bool AimedDoor { get; set; }
        public bool BeamPad { get; set; }
        public bool NearDoor { get; set; }
        public bool DataCube { get; set; }
        public bool Factory { get; set; }
        public bool NetFragment { get; set; }

        /// <summary>A ship station within the proximity reach (empty: none).</summary>
        public string NearStation { get; set; } = string.Empty;

        /// <summary>A trader within trading reach.</summary>
        public bool Vendor { get; set; }

        /// <summary>A person within talk reach.</summary>
        public bool TalkNear { get; set; }

        /// <summary>#3171: the player stands on a lift platform (<see cref="LiftRiding.StandingOn"/>).</summary>
        public bool OnLift { get; set; }

        /// <summary>#3345: the block under the crosshair is <c>usable</c> (its definition's flag) and in reach.</summary>
        public bool UsableBlock { get; set; }

        /// <summary>#3394: the player's own robot companion under the crosshair, in talk reach and with nothing solid in front.</summary>
        public bool AimedCompanion { get; set; }

        /// <summary>#3398/#3399: the held item is a piece of gear the robot takes (a weapon or a jetpack).</summary>
        public bool HoldsRobotGear { get; set; }

        /// <summary>Forgets every fact (the next frame gathers them anew).</summary>
        public void Clear()
        {
            AimedNpc = AimedNpcTrades = OwnBeacon = OwnBase = BasePad = StationCore = Crate = CrystalDevice = false;
            BioLab = SpawnBed = Seat = MissionBoard = AimedDoor = BeamPad = NearDoor = DataCube = false;
            Factory = NetFragment = Vendor = TalkNear = OnLift = UsableBlock = AimedCompanion = HoldsRobotGear = false;
            LookedStation = NearStation = string.Empty;
        }
    }

    /// <summary>
    /// THE interact pick (#3088): what E does, from <see cref="InteractFacts"/>. The rule is "aimed beats nearby": a thing
    /// under the crosshair wins over everything that merely stands close — a person you look at over the door beside you,
    /// a mission board in the cabin over the workshop station three metres away (Justus 2026-11-01: the prompt said
    /// "E: Workshop" at his ship's board, and "Talk to Vhaerk (E)" while E swung a door). Within each tier the order is the
    /// one E always had. The player rig acts on the result and the HUD names it, so the two cannot disagree.
    /// </summary>
    public static class InteractResolver
    {
        /// <summary>What E does with these facts.</summary>
        public static InteractKind Resolve(InteractFacts f)
        {
            if (f == null)
            {
                return InteractKind.None;
            }

            InteractKind aimed = ResolveAimed(f);
            return aimed != InteractKind.None ? aimed : ResolveNearby(f);
        }

        /// <summary>Whether the pick is a thing under the crosshair (the aimed tier).</summary>
        public static bool IsAimed(InteractKind kind)
            => (kind >= InteractKind.AimedTalk && kind <= InteractKind.AimedDoor) || kind == InteractKind.UseBlock
                || kind == InteractKind.AimedCompanion || kind == InteractKind.GiveCompanion;

        /// <summary>Whether the target names itself with a label in the world (a door, a data cube, a net fragment, a
        /// factory terminal) — the HUD's prompt line leaves it to that label.</summary>
        public static bool HasWorldLabel(InteractKind kind)
            => kind is InteractKind.AimedDoor or InteractKind.NearDoor or InteractKind.DataCube or InteractKind.Factory
                or InteractKind.NetFragment;

        /// <summary>Whether E talks to (or trades with) a person.</summary>
        public static bool IsPerson(InteractKind kind)
            => kind is InteractKind.AimedTalk or InteractKind.AimedTrade or InteractKind.Trade or InteractKind.Talk;

        /// <summary>The aimed tier, in the order E always tried these — after the person under the crosshair, who has nothing
        /// solid in front of them and so stands before every block behind them, and the own robot companion (#3394), likewise.</summary>
        private static InteractKind ResolveAimed(InteractFacts f)
            => f.AimedNpc ? (f.AimedNpcTrades ? InteractKind.AimedTrade : InteractKind.AimedTalk)
                : f.AimedCompanion ? (f.HoldsRobotGear ? InteractKind.GiveCompanion : InteractKind.AimedCompanion) // #3398/#3399
                : f.OwnBeacon ? InteractKind.OwnBeacon
                : f.OwnBase ? InteractKind.OwnBase
                : f.BasePad ? InteractKind.BasePad
                : f.StationCore ? InteractKind.StationCore
                : f.Crate ? InteractKind.Crate
                : f.CrystalDevice ? InteractKind.CrystalDevice
                : f.BioLab ? InteractKind.BioLab
                : f.SpawnBed ? InteractKind.SpawnBed
                : f.Seat ? InteractKind.Seat
                : f.MissionBoard ? InteractKind.MissionBoard
                : !string.IsNullOrEmpty(f.LookedStation) ? InteractKind.LookedStation
                : f.AimedDoor ? InteractKind.AimedDoor
                : f.UsableBlock ? InteractKind.UseBlock // #3345: after every older use of the aimed cell
                : InteractKind.None;

        /// <summary>The proximity tier, in E's old order: a door before the stations (a door beside a market stall still
        /// opens, B47), a trader before a talk (traders stand right beside their stall). #3171: the lift platform the player
        /// stands on comes first — riding, the floor is what E is for (Lyxette: "standing on it I can't start anything").</summary>
        private static InteractKind ResolveNearby(InteractFacts f)
            => f.OnLift ? InteractKind.LiftRide
                : f.BeamPad ? InteractKind.BeamPad
                : f.NearDoor ? InteractKind.NearDoor
                : f.DataCube ? InteractKind.DataCube
                : f.Factory ? InteractKind.Factory
                : f.NetFragment ? InteractKind.NetFragment
                : !string.IsNullOrEmpty(f.NearStation) ? InteractKind.NearStation
                : f.Vendor ? InteractKind.Trade
                : f.TalkNear ? InteractKind.Talk
                : InteractKind.None;
    }
}
