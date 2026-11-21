// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// A giant wreck in space after the derelict decay (#3614): the decayed grid with its doors, arrival and guardian bay
/// (<see cref="Layout"/>, the shape every derelict layout has), plus what the template's markers become aboard — the chests
/// (one-time salvage containers, <see cref="Chests"/>) and the sound emitters (<see cref="Sounds"/>), in layout cells — and
/// <see cref="BaseHash"/>, what the server pins. Read-only once built (the server shares one between servers of a process).
/// </summary>
public sealed class GiantDerelictLayout
{
    internal GiantDerelictLayout(string key, DerelictLayout layout, IReadOnlyList<SettlementMarker> chests,
        IReadOnlyList<SettlementMarker> sounds, string baseHash, Vector3i? console = null, IReadOnlyList<Vector3i>? lamps = null)
    {
        Key = key;
        Layout = layout;
        Chests = chests;
        Sounds = sounds;
        BaseHash = baseHash;
        Console = console;
        Lamps = lamps ?? Array.Empty<Vector3i>();
    }

    /// <summary>The giant ship's template key.</summary>
    public string Key { get; }

    /// <summary>The decayed wreck: cells, doors, arrival (the entrance), the guardian bay's spot, the room verdict.</summary>
    public DerelictLayout Layout { get; }

    /// <summary>The template's chest markers that survived (<see cref="SettlementMarker.Data"/> = the marker's settings).</summary>
    public IReadOnlyList<SettlementMarker> Chests { get; }

    /// <summary>The template's sound markers that survived (<see cref="SettlementMarker.Data"/> = the marker's settings).</summary>
    public IReadOnlyList<SettlementMarker> Sounds { get; }

    /// <summary>#3902: the cell of a warship's bridge console (the block <c>sps_console</c> stands there), in layout cells —
    /// null for every ship without a <see cref="GiantShipRules.ConsoleMarker"/>.</summary>
    public Vector3i? Console { get; }

    /// <summary>#3902: the cells of the red lamps beside the bridge console (the template's
    /// <see cref="GiantShipRules.LampMarker"/> markers), in layout cells.</summary>
    public IReadOnlyList<Vector3i> Lamps { get; }

    /// <summary>A hash of the decayed wreck by block KEYS (a save version may shift the block ids, never the keys) with every
    /// dye, glow and shape, the doors, the arrival and the guardian bay — 16 hex digits. A change of any block property that
    /// makes a different wreck (a light's off twin, a category, solidity, airtightness) changes it, so the server can tell
    /// that a stored wreck's base no longer comes out as it was.</summary>
    public string BaseHash { get; }
}

public static partial class DerelictGenerator
{
    /// <summary>The <see cref="DerelictLayout.Source"/> prefix of a giant wreck: <c>giant:&lt;key&gt;</c>.</summary>
    public const string GiantSourcePrefix = "giant:";

    /// <summary>The size tier a giant wreck's layout (and its boardable record) carries.</summary>
    public const string GiantSizeTier = "colossal";

    /// <summary>The revision of the ship-mode decay (#3614). Raise it with every change that builds a different wreck from the
    /// same template and seed: the server pins it, and a stored wreck is never rebuilt from a different base.</summary>
    public const int ShipDecayRevision = 1;

    /// <summary>The chance a walkable room of a giant wreck (but the arrival's) is torn open to space.</summary>
    private const double GiantBreachChance = 0.5;

    /// <summary>The most rooms one wreck labels (the room labels are 16 bit); smaller pockets past it stay unlabelled.</summary>
    private const int MaxShipRooms = ushort.MaxValue - 1;

    /// <summary>
    /// The derelict decay in SHIP mode (#3614) over a giant wreck as it drifts in space (<see cref="GiantShipGenerator.BuildSpace"/>
    /// — already ruined, revision 3, profile <c>hull</c>). Deterministic from <paramref name="seed"/>. What it does, in order:
    /// <list type="number">
    /// <item>the force fields are dead (gone) — no power aboard;</item>
    /// <item>the rooms: the inside air the outside cannot reach, split at the template's doorways (<c>door_slide</c> markers with
    /// <c>axis=x|z</c>) — the open way in (the <c>entrance</c>) and the ruin's holes lead to the outside; a room behind a
    /// doorway is walkable;</item>
    /// <item>which rooms are torn open: each walkable room but the arrival's with <see cref="GiantBreachChance"/> (at least one of
    /// two or more), never through a wall a closed room, a doorway, a chest, a sound or the arrival relies on;</item>
    /// <item>doors: a doorway that still parts two places, at least one a closed room, gets an intact sliding door (one column
    /// walled up in the jamb's block, a door record in the other); between open places it stays open;</item>
    /// <item>the breaches (a rough hole each), the lights (a dead light beside a closed room burns again as an emergency light:
    /// a red lamp, a warm strip), soot round the new holes (a human hull only), the arrival standable, a little scrap on the
    /// decks (a human hull only) and ONE guardian bay — in the largest closed walkable room but the arrival's, facing the way
    /// in.</item>
    /// </list>
    /// No beta-station damage, no trading posts and no crate spots: the template's chest markers are the chests. The station
    /// paths (<see cref="Generate"/>, <see cref="GenerateLegacy"/>) never run any of this. Memory: the rooms are a 16-bit label
    /// per cell plus their size and centre — no cell list is kept per room.
    /// <para>#3902: a warship's bridge — a <see cref="GiantShipRules.ConsoleMarker"/> and its
    /// <see cref="GiantShipRules.LampMarker"/> markers — is never torn, and after everything else the console block and its red
    /// lamps are set there (no shipped giant wreck carries one). <paramref name="fresh"/> (the escort cruiser's wreck, shot a
    /// moment ago): no soot of the centuries and no scrap, and the lights did not all die — the dead lamps of the bridge and
    /// <see cref="FreshLampShare"/> of the others still burn red. Instead it carries the fight's own scars, laid last from a
    /// random stream of their own (#3915, DerelictGenerator.ShipBattle.cs): charred plating right round its breaches
    /// (<see cref="ScorchTint"/>), most of its screens dead (<see cref="DeadScreenTint"/>) and a few loose hull plates on the
    /// decks — the chests, doors, arrival, guardian bay, bridge and lamps stay exactly where they are without them. False
    /// builds exactly the wreck this pass always built.</para>
    /// </summary>
    public static GiantDerelictLayout GenerateShip(GiantSpaceLayout giant, long seed, GameContent content, bool alien, bool fresh = false)
        => GenerateShip(giant, seed, content, alien, fresh, battleScars: fresh);

    /// <summary>Test seam (#3915): <see cref="GenerateShip(GiantSpaceLayout, long, GameContent, bool, bool)"/> with the battle
    /// scars of a fresh wreck switched on or off (<paramref name="battleScars"/> false: the fresh wreck as it came out before
    /// #3915 — the same cells for everything but the scars).</summary>
    internal static GiantDerelictLayout GenerateShip(GiantSpaceLayout giant, long seed, GameContent content, bool alien, bool fresh,
        bool battleScars)
    {
        var pass = new ShipPass(new Grid(giant), new Ids(content), content, giant, alien,
            new Random(StableHashing.Seed32("derelict-giant:" + giant.Key, seed)), fresh, seed, battleScars);
        pass.Run();
        return pass.Result(content);
    }

    /// <summary>A room of the ship-mode decay: its size and the sums of its cells' coordinates (its centre).</summary>
    private readonly struct ShipRoom
    {
        public ShipRoom(int size, long sx, long sy, long sz)
        {
            Size = size;
            SumX = sx;
            SumY = sy;
            SumZ = sz;
        }

        public int Size { get; }

        public long SumX { get; }

        public long SumY { get; }

        public long SumZ { get; }
    }

    /// <summary>One run of the ship-mode decay: the working state and its steps (see <see cref="GenerateShip"/>).</summary>
    private sealed partial class ShipPass
    {
        private readonly Grid _g;
        private readonly Ids _ids;
        private readonly GiantSpaceLayout _source;
        private readonly bool _alien;
        private readonly Random _rng;
        private readonly HashSet<ushort> _noCarve = new();
        private readonly Dictionary<ushort, ushort> _emergency = new();
        private readonly List<Doorway> _doorways = new();
        private readonly List<SettlementMarker> _chests = new();
        private readonly List<SettlementMarker> _sounds = new();
        private readonly HashSet<Vector3i> _reserved = new();
        private readonly List<Vector3i> _breachCentres = new();
        private readonly List<DerelictDoor> _doors = new();
        private readonly List<ShipRoom> _rooms = new();
        private List<Vector3i> _doorwayCells = new();
        private bool[] _wall = Array.Empty<bool>();
        private bool[] _exterior = Array.Empty<bool>();
        private bool[] _protect = Array.Empty<bool>();
        private ushort[] _label = Array.Empty<ushort>();
        private bool[] _walkable = Array.Empty<bool>();
        private bool[] _breached = Array.Empty<bool>();
        private List<Vector3i>[] _candidates = Array.Empty<List<Vector3i>>();
        private List<Vector3i>?[] _standable = Array.Empty<List<Vector3i>?>();
        private Vector3i _arrival;
        private int _arrivalRoom = -1;
        private Vector3i? _spawner;

        public ShipPass(Grid grid, Ids ids, GameContent content, GiantSpaceLayout source, bool alien, Random rng, bool fresh = false,
            long seed = 0, bool battleScars = false)
        {
            _g = grid;
            _ids = ids;
            _source = source;
            _alien = alien;
            _rng = rng;
            _fresh = fresh;
            _seed = seed;
            _battleScars = battleScars;
            ReadContent(content);
            if (fresh && battleScars)
            {
                ReadBattleContent(content);
            }
        }

        public void Run()
        {
            ReadMarkers();
            _g.RemoveAll(_ids.Field); // dead power: no force field holds anything any more
            _exterior = _g.FloodOutside(_wall);
            _label = _g.LabelShipRooms(_exterior, _wall, _rooms);
            _wall = Array.Empty<bool>(); // the doorways are in the labels now
            _arrivalRoom = RoomAt(_arrival);
            FindWalkableRooms();
            DecideBreaches();
            HangDoors();
            CarveBreaches();
            _exterior = Array.Empty<bool>();
            _protect = Array.Empty<bool>();
            Lights();
            if (!_fresh)
            {
                Soot(); // a ship shot a moment ago carries no soot and no scrap (#3902); both draw from the stream only here
            }

            ClearArrival();
            CollectStandable();
            if (!_fresh)
            {
                Scrap();
            }

            PlaceGuardianBay();
            PlaceFreshBay();
            PlaceBridge();
            BattleScars(); // #3915: a fresh wreck's scars — last, from a stream of their own, so nothing above moves
        }

        public GiantDerelictLayout Result(GameContent content)
        {
            int walkable = 0, closed = 0, open = 0;
            for (int r = 0; r < _rooms.Count; r++)
            {
                if (_walkable[r])
                {
                    walkable++;
                    closed += _breached[r] ? 0 : 1;
                    open += _breached[r] ? 1 : 0;
                }
            }

            string hash = _g.ContentHash(content, _doors, _arrival, _spawner);
            var layout = _g.ToLayout(GiantSizeTier, GiantSourcePrefix + _source.Key);
            layout.Doors = _doors;
            layout.Arrival = _arrival;
            layout.ArrivalSealed = _arrivalRoom >= 0 && !_breached[_arrivalRoom];
            layout.SpawnerSpot = _spawner;
            layout.RoomCount = walkable;
            layout.SealedRoomCount = closed;
            layout.BreachedRoomCount = open;
            return new GiantDerelictLayout(_source.Key, layout, _chests, _sounds, hash, _console, _lampCells);
        }

        /// <summary>The room of a cell (its label), else of the cell above it (something stands there), else −1.</summary>
        private int RoomAt(Vector3i c) => _g.ShipRoomAt(_label, c);

        /// <summary>The blocks a breach never tears (plants, ladders, doors) and the emergency twin of every dead lamp (a
        /// <c>light_*_off</c> becomes a red lamp, a <c>strip_light_*_off</c> a warm strip; a dead torch, lantern or forge stays
        /// dead).</summary>
        private void ReadContent(GameContent content)
        {
            ushort Id(string key) => content.GetBlock(key)?.NumericId.Value ?? 0;
            ushort redLamp = Id("light_red"), warmStrip = Id("strip_light_warm");
            _redLamp = redLamp;
            _consoleBlock = Id(GiantShipRules.ConsoleMarker);
            foreach (var def in content.Blocks.Values)
            {
                ushort id = def.NumericId.Value;
                if (id == 0)
                {
                    continue;
                }

                if (def.Category == "flora" || def.Key.Contains("ladder") || def.Key.StartsWith("door_", StringComparison.Ordinal))
                {
                    _noCarve.Add(id);
                }

                bool strip = def.Key.StartsWith("strip_light", StringComparison.Ordinal);
                bool lamp = strip || def.Key.StartsWith("light_", StringComparison.Ordinal);
                if (lamp && def.Key.EndsWith("_off", StringComparison.Ordinal)
                    && content.GetBlock(def.Key.Substring(0, def.Key.Length - 4)) is not null && (strip ? warmStrip : redLamp) != 0)
                {
                    _emergency[id] = strip ? warmStrip : redLamp;
                }
            }
        }

        /// <summary>The doorways (a <c>door_slide</c> marker naming its wall's axis), the chests, the sounds and the arrival.</summary>
        private void ReadMarkers()
        {
            foreach (var m in _source.Markers)
            {
                if (m.Type == GiantShipRules.DoorMarker)
                {
                    string axis = MarkerSettings.Parse(m.Data).Get(GiantShipRules.AxisSetting);
                    if (axis != "x" && axis != "z")
                    {
                        continue;
                    }

                    bool xWall = axis == "x"; // the wall is a plane of constant x: the opening runs along z (marker z, z + 1)
                    var dc = m.LocalPos;
                    var d = new Doorway(dc, xWall ? new Vector3i(dc.X, dc.Y, dc.Z + 1) : new Vector3i(dc.X + 1, dc.Y, dc.Z), xWall);
                    _doorways.Add(d);
                    _doorwayCells.AddRange(d.Cells());
                }
                else if (m.Type == LandmarkRules.ChestMarker)
                {
                    _chests.Add(m);
                }
                else if (m.Type == LandmarkRules.SoundMarker)
                {
                    _sounds.Add(m);
                }
                else if (m.Type == GiantShipRules.ConsoleMarker)
                {
                    _consoleMarker ??= m; // one bridge console per ship
                }
                else if (m.Type == GiantShipRules.LampMarker)
                {
                    _lampCells.Add(m.LocalPos);
                }
            }

            _wall = _g.Mask(_doorwayCells);
            _arrival = _source.Entrance;
            ReserveMarkers();
        }

        /// <summary>Nothing (scrap, the guardian bay) ever stands on a doorway, right in front of one, on a chest, a sound or the
        /// arrival column.</summary>
        private void ReserveMarkers()
        {
            _reserved.UnionWith(_doorwayCells);
            foreach (var d in _doorways)
            {
                foreach (int sign in new[] { -1, 1 })
                {
                    var front = d.Side(sign);
                    _reserved.Add(front);
                    _reserved.Add(d.XWall ? new Vector3i(front.X, front.Y, front.Z + 1) : new Vector3i(front.X + 1, front.Y, front.Z));
                }
            }

            foreach (var m in _chests)
            {
                _reserved.Add(m.LocalPos);
                _reserved.Add(new Vector3i(m.LocalPos.X, m.LocalPos.Y + 1, m.LocalPos.Z));
            }

            foreach (var m in _sounds)
            {
                _reserved.Add(m.LocalPos);
            }

            _reserved.Add(_arrival);
            _reserved.Add(new Vector3i(_arrival.X, _arrival.Y + 1, _arrival.Z));
            ReserveBridge();
        }

        /// <summary>A room behind a doorway is one a boarder walks into; so is the arrival's own room.</summary>
        private void FindWalkableRooms()
        {
            _walkable = new bool[_rooms.Count];
            foreach (var d in _doorways)
            {
                foreach (int sign in new[] { -1, 1 })
                {
                    int r = RoomAt(d.Side(sign));
                    if (r >= 0)
                    {
                        _walkable[r] = true;
                    }
                }
            }

            if (_arrivalRoom >= 0)
            {
                _walkable[_arrivalRoom] = true;
            }
        }

        /// <summary>The standable deck cells of every walkable room, in cell order (one pass; checked again where used).</summary>
        private void CollectStandable()
        {
            _standable = new List<Vector3i>?[_rooms.Count];
            for (int i = 0; i < _g.Size; i++)
            {
                int r = _g.RoomOf(_label, i);
                if (r < 0 || !_walkable[r])
                {
                    continue;
                }

                var c = _g.CellOf(i);
                if (_g.Standable(c.X, c.Y, c.Z))
                {
                    (_standable[r] ??= new List<Vector3i>()).Add(c);
                }
            }
        }
    }
}
