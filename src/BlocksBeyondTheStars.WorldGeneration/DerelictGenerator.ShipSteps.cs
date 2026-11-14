// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

public static partial class DerelictGenerator
{
    /// <summary>The steps of the ship-mode decay (see <see cref="GenerateShip"/>), in the order <see cref="ShipPass.Run"/> calls
    /// them — every draw from the one seeded stream, in a fixed order.</summary>
    private sealed partial class ShipPass
    {
        /// <summary>Which walkable rooms are torn open. A room with nothing to tear (no outer face) stays closed after all; a
        /// closed room protects more walls, so this settles until nothing changes.</summary>
        private void DecideBreaches()
        {
            int n = _rooms.Count;
            _breached = new bool[n];
            int choosable = 0, first = -1;
            for (int r = 0; r < n; r++)
            {
                double roll = _rng.NextDouble();
                if (_walkable[r] && r != _arrivalRoom)
                {
                    _breached[r] = roll < GiantBreachChance;
                    choosable++;
                    first = first < 0 ? r : first;
                }
            }

            if (choosable >= 2 && Array.IndexOf(_breached, true) < 0)
            {
                _breached[first] = true; // a wreck that big is open somewhere
            }

            _protect = new bool[_g.Size];
            bool changed;
            do
            {
                changed = false;
                FillProtectedMask();
                _candidates = BreachCandidates();
                for (int r = 0; r < n; r++)
                {
                    if (_breached[r] && _candidates[r].Count == 0)
                    {
                        _breached[r] = false;
                        changed = true;
                    }
                }
            }
            while (changed);
        }

        /// <summary>The cells no breach may remove: the air of every closed room with its walls (a face neighbour), the doorways
        /// with their frames, the chests, the sounds and the arrival column with theirs.</summary>
        private void FillProtectedMask()
        {
            Array.Clear(_protect, 0, _protect.Length);
            for (int i = 0; i < _protect.Length; i++)
            {
                int r = _g.RoomOf(_label, i);
                if (r >= 0 && !_breached[r])
                {
                    _g.MarkAround(_protect, i);
                }
            }

            foreach (var c in _doorwayCells)
            {
                _g.MarkAround(_protect, c);
            }

            foreach (var m in _chests)
            {
                MarkColumn(m.LocalPos);
            }

            foreach (var m in _sounds)
            {
                _g.MarkAround(_protect, m.LocalPos);
            }

            MarkColumn(_arrival);
        }

        /// <summary>A standing spot's floor, feet and head cells with their face neighbours.</summary>
        private void MarkColumn(Vector3i feet)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                _g.MarkAround(_protect, new Vector3i(feet.X, feet.Y + dy, feet.Z));
            }
        }

        /// <summary>Per torn room, the cells a breach may start at — a block that may tear, next to the room's air and touching
        /// the outside — in cell order (the seeded pick is stable). One pass over the grid.</summary>
        private List<Vector3i>[] BreachCandidates()
        {
            var sets = new SortedSet<int>?[_rooms.Count];
            for (int i = 0; i < _label.Length; i++)
            {
                int r = _g.RoomOf(_label, i);
                if (r < 0 || !_breached[r])
                {
                    continue;
                }

                for (int dir = 0; dir < 6; dir++)
                {
                    int n = _g.Step(i, dir);
                    if (n >= 0 && _g.Tearable(n, _protect, _noCarve) && _g.TouchesOutside(_exterior, n))
                    {
                        (sets[r] ??= new SortedSet<int>()).Add(n);
                    }
                }
            }

            var lists = new List<Vector3i>[_rooms.Count];
            for (int r = 0; r < lists.Length; r++)
            {
                lists[r] = new List<Vector3i>();
                foreach (int i in sets[r] ?? (IEnumerable<int>)Array.Empty<int>())
                {
                    lists[r].Add(_g.CellOf(i)); // index order is (x, y, z) order
                }
            }

            return lists;
        }

        /// <summary>A doorway that still parts two places, at least one of them a closed room, keeps an intact sliding door: the
        /// marker's column walled up in the block of the wall beside it (a station door is one block wide) and a door record in
        /// the other column. A doorway between open places — or one the ruin broke round — stays open.</summary>
        private void HangDoors()
        {
            foreach (var d in _doorways)
            {
                int a = RoomAt(d.Side(-1)), b = RoomAt(d.Side(+1));
                bool closedSide = (a >= 0 && !_breached[a]) || (b >= 0 && !_breached[b]);
                if (a == b || !closedSide)
                {
                    continue;
                }

                var jamb = d.XWall ? new Vector3i(d.Marker.X, d.Marker.Y, d.Marker.Z - 1) : new Vector3i(d.Marker.X - 1, d.Marker.Y, d.Marker.Z);
                ushort wall = _g.Get(jamb);
                var mod = _g.InBounds(jamb) ? _g.ModAt(_g.Idx(jamb.X, jamb.Y, jamb.Z)) : (0, 0);
                bool plain = wall != 0 && wall != _ids.Glass && _g.InBounds(jamb) && _g.ShapeAt(_g.Idx(jamb.X, jamb.Y, jamb.Z)) == 0;
                for (int dy = 0; dy < 3; dy++)
                {
                    var c = new Vector3i(d.Marker.X, d.Marker.Y + dy, d.Marker.Z);
                    _g.Set(c.X, c.Y, c.Z, plain ? wall : _ids.Hull);
                    _g.SetModifier(c.X, c.Y, c.Z, plain ? mod : (0, 0));
                    _g.MarkAround(_protect, c);
                    _g.MarkAround(_protect, new Vector3i(d.Other.X, d.Other.Y + dy, d.Other.Z));
                }

                _doors.Add(new DerelictDoor(d.Other, axisX: !d.XWall));
            }
        }

        /// <summary>One rough hole per torn room, in a wall, floor or roof between its air and the space outside.</summary>
        private void CarveBreaches()
        {
            for (int r = 0; r < _rooms.Count; r++)
            {
                if (!_breached[r] || _candidates[r].Count == 0)
                {
                    continue;
                }

                var centre = _candidates[r][_rng.Next(_candidates[r].Count)];
                _breachCentres.Add(centre);
                double radius = 1.5 + _rng.NextDouble() * 1.5;
                _g.ShipCarve(centre, radius, _protect, _noCarve, _rng);
            }
        }

        /// <summary>The lights: every light of the wreck is dead (the ruin switched them off); one beside the air of a closed
        /// walkable room burns again as an emergency light — a red lamp, a warm strip — with its mount kept.</summary>
        private void Lights()
        {
            if (_emergency.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _label.Length; i++)
            {
                if (!_emergency.TryGetValue(_g.BlockAt(i), out ushort on))
                {
                    continue;
                }

                for (int dir = 0; dir < 6; dir++)
                {
                    int n = _g.Step(i, dir);
                    int r = n >= 0 ? _g.RoomOf(_label, n) : -1;
                    if (r >= 0 && _walkable[r] && !_breached[r])
                    {
                        _g.SetKeepShape(i, on);
                        break;
                    }
                }
            }
        }

        /// <summary>Soot round the new holes (a human hull only — the plating stays, darkened, as the ruin's scorch does it).</summary>
        private void Soot()
        {
            if (_alien)
            {
                return;
            }

            foreach (var centre in _breachCentres)
            {
                for (int dx = -4; dx <= 4; dx++)
                    for (int dy = -4; dy <= 4; dy++)
                        for (int dz = -4; dz <= 4; dz++)
                        {
                            var c = new Vector3i(centre.X + dx, centre.Y + dy, centre.Z + dz);
                            double roll = _rng.NextDouble();
                            ushort b = _g.Get(c);
                            if (b == 0 || b == _ids.Glass || _noCarve.Contains(b) || _emergency.ContainsKey(b)
                                || (dx * dx) + (dy * dy) + (dz * dz) > 16 || roll >= 0.5)
                            {
                                continue;
                            }

                            var (_, glow) = _g.ModAt(_g.Idx(c.X, c.Y, c.Z));
                            _g.SetModifier(c.X, c.Y, c.Z, (GiantShipGenerator.SootTint, glow));
                        }
            }
        }

        /// <summary>The arrival stands: free feet and head cells over a floor (the generator already moved an entrance that lost
        /// its floor, so this only mends what the decay could not have touched anyway).</summary>
        private void ClearArrival()
        {
            var a = _arrival;
            if (!_g.InBounds(a) || _g.Standable(a.X, a.Y, a.Z))
            {
                return;
            }

            _g.Set(a.X, a.Y, a.Z, 0);
            _g.Set(a.X, a.Y + 1, a.Z, 0);
            if (!_g.Solid(a.X, a.Y - 1, a.Z))
            {
                _g.Set(a.X, a.Y - 1, a.Z, _ids.Hull);
            }
        }

        /// <summary>A little scrap on the decks of the walkable rooms (a human hull only): one to three pieces in a torn room,
        /// none or one in a closed one — never on a reserved cell.</summary>
        private void Scrap()
        {
            for (int r = 0; r < _rooms.Count; r++)
            {
                if (!_walkable[r])
                {
                    continue;
                }

                int pieces = _breached[r] ? 1 + _rng.Next(3) : _rng.Next(2);
                if (_alien)
                {
                    continue;
                }

                var spots = Spots(r, minArrivalDistance: 3);
                for (int i = 0; i < pieces && spots.Count > 0; i++)
                {
                    var c = spots[_rng.Next(spots.Count)];
                    spots.Remove(c);
                    ushort scrap = _rng.NextDouble() < 0.5 ? _ids.ScrapMetal : _ids.ScrapPile;
                    if (scrap != 0)
                    {
                        _g.Set(c.X, c.Y, c.Z, scrap);
                        _reserved.Add(c);
                    }
                }
            }
        }

        /// <summary>The deck cells of room <paramref name="r"/> that still stand free, are not reserved and lie at least
        /// <paramref name="minArrivalDistance"/> from the arrival (Chebyshev), in cell order.</summary>
        private List<Vector3i> Spots(int r, int minArrivalDistance)
        {
            var list = new List<Vector3i>();
            foreach (var c in _standable[r] ?? new List<Vector3i>())
            {
                int d = Math.Max(Math.Abs(c.X - _arrival.X), Math.Max(Math.Abs(c.Y - _arrival.Y), Math.Abs(c.Z - _arrival.Z)));
                if (d >= minArrivalDistance && !_reserved.Contains(c) && _g.Standable(c.X, c.Y, c.Z))
                {
                    list.Add(c);
                }
            }

            return list;
        }

        /// <summary>ONE guardian bay (#2635): in the largest closed walkable room but the arrival's (its guardians stay inside),
        /// else the largest walkable one, on that room's floor as near its centre as the distance to the way in allows, its
        /// front toward the arrival. A wreck with no walkable room has none.</summary>
        private void PlaceGuardianBay()
        {
            int room = BayRoom(closedOnly: true);
            room = room >= 0 ? room : BayRoom(closedOnly: false);
            if (room < 0 || _ids.GuardianBay == 0)
            {
                return;
            }

            foreach (int keepAway in new[] { 6, 3, 0 })
            {
                var spots = Spots(room, keepAway);
                OnTheDeck(spots, _reserved);
                _spawner = NearestToCentre(spots, _rooms[room]);
                if (_spawner is not null)
                {
                    break;
                }
            }

            if (_spawner is { } bay)
            {
                _g.Set(bay.X, bay.Y, bay.Z, _ids.GuardianBay);
                _g.SetShape(bay.X, bay.Y, bay.Z, CubeFacing.Pack(FaceToward(bay, _arrival)));
                _reserved.Add(bay);
            }
        }

        private int BayRoom(bool closedOnly)
        {
            int best = -1;
            for (int r = 0; r < _rooms.Count; r++)
            {
                if (r == _arrivalRoom || !_walkable[r] || (closedOnly && _breached[r]))
                {
                    continue;
                }

                if (best < 0 || _rooms[r].Size > _rooms[best].Size)
                {
                    best = r;
                }
            }

            return best;
        }

        /// <summary>The spot nearest a room's centre (height weighted four times, as the station decay does), ties to the
        /// first in cell order.</summary>
        private static Vector3i? NearestToCentre(List<Vector3i> spots, ShipRoom room)
        {
            double cx = (double)room.SumX / room.Size, cy = (double)room.SumY / room.Size, cz = (double)room.SumZ / room.Size;
            Vector3i? best = null;
            double bestD = double.MaxValue;
            foreach (var c in spots)
            {
                double d = ((c.X - cx) * (c.X - cx)) + ((c.Y - cy) * (c.Y - cy) * 4) + ((c.Z - cz) * (c.Z - cz));
                if (d < bestD)
                {
                    bestD = d;
                    best = c;
                }
            }

            return best;
        }
    }
}
