// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

public static partial class DerelictGenerator
{
    /// <summary>#3902: the share of a FRESH wreck's dead lamps (outside its bridge) that still burn red — the emergency
    /// lighting of a ship that was shot a moment ago. Decided per cell by a hash of the seed, never by the pass's stream.</summary>
    public const double FreshLampShare = 0.35;

    /// <summary>
    /// The ship-mode decay's part for a WARSHIP'S BRIDGE and for a FRESH wreck (#3902, the escort cruiser's wreck) — see
    /// <see cref="GenerateShip"/>. A ship without a <see cref="GiantShipRules.ConsoleMarker"/> and a pass that is not fresh
    /// take none of these branches, so every giant wreck comes out as it always did.
    /// </summary>
    private sealed partial class ShipPass
    {
        private readonly bool _fresh;
        private readonly long _seed;
        private readonly List<Vector3i> _lampCells = new();
        private SettlementMarker? _consoleMarker;
        private Vector3i? _console;
        private ushort _redLamp;
        private ushort _consoleBlock;

        /// <summary>Nothing else (the guardian bay, scrap) ever stands on the console's or a lamp's cell.</summary>
        private void ReserveBridge()
        {
            if (_consoleMarker is { } console)
            {
                _reserved.Add(console.LocalPos);
                _reserved.Add(new Vector3i(console.LocalPos.X, console.LocalPos.Y + 1, console.LocalPos.Z));
            }

            foreach (var lamp in _lampCells)
            {
                _reserved.Add(lamp);
            }
        }

        /// <summary>No breach tears the deck the console stands on, nor a lamp or the post that carries it.</summary>
        private void ProtectBridge()
        {
            if (_consoleMarker is { } console)
            {
                MarkColumn(console.LocalPos);
            }

            foreach (var lamp in _lampCells)
            {
                _g.MarkAround(_protect, lamp);
                _g.MarkAround(_protect, new Vector3i(lamp.X, lamp.Y - 1, lamp.Z));
            }
        }

        /// <summary>Whether a dead light with the emergency twin <paramref name="on"/> still burns on a FRESH wreck: a lamp
        /// (never a strip) of the bridge always, any other lamp with <see cref="FreshLampShare"/>.</summary>
        private bool StillBurns(int i, ushort on)
        {
            if (!_fresh || on == 0 || on != _redLamp)
            {
                return false;
            }

            if (_consoleMarker is { } console)
            {
                var c = _g.CellOf(i);
                var at = console.LocalPos;
                if (Math.Abs(c.X - at.X) <= CruiserWreckRules.BridgeLampReach && Math.Abs(c.Z - at.Z) <= CruiserWreckRules.BridgeLampReach
                    && Math.Abs(c.Y - at.Y) <= CruiserWreckRules.BridgeLampReachY)
                {
                    return true;
                }
            }

            return StableHashing.Unit(StableHashing.Mix64(unchecked((ulong)_seed) ^ (0x9E3779B97F4A7C15UL * (ulong)(i + 1)))) < FreshLampShare;
        }

        /// <summary>A FRESH wreck always carries its guardian bay ("only robots are aboard"): when the fight tore every room
        /// with a doorway open — so the pass found no room to put it in — the bay stands below deck: on the LOWEST deck that
        /// has an open roofed spot (a free 3 × 3 of deck round it, never a reserved cell), there as far from the way in as
        /// it goes, facing the way in.</summary>
        private void PlaceFreshBay()
        {
            if (!_fresh || _spawner is not null || _ids.GuardianBay == 0)
            {
                return;
            }

            Vector3i? best = null;
            long bestDistance = -1;
            for (int i = 0; i < _g.Size; i++)
            {
                var c = _g.CellOf(i);
                if (best is { } b && c.Y > b.Y)
                {
                    continue; // a lower deck already has a spot
                }

                long dx = c.X - _arrival.X, dz = c.Z - _arrival.Z, distance = (dx * dx) + (dz * dz);
                bool lower = best is { } had && c.Y < had.Y;
                if ((lower || distance > bestDistance) && !_reserved.Contains(c) && OpenRoofedDeck(c))
                {
                    bestDistance = distance;
                    best = c;
                }
            }

            if (best is { } bay)
            {
                _g.Set(bay.X, bay.Y, bay.Z, _ids.GuardianBay);
                _g.SetShape(bay.X, bay.Y, bay.Z, CubeFacing.Pack(FaceToward(bay, _arrival)));
                _reserved.Add(bay);
                _spawner = bay;
            }
        }

        /// <summary>A deck cell with standing room on all eight cells round it and a block somewhere above it.</summary>
        private bool OpenRoofedDeck(Vector3i c)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!_g.Standable(c.X + dx, c.Y, c.Z + dz))
                    {
                        return false;
                    }
                }
            }

            for (int y = c.Y + 2; y < _g.H; y++)
            {
                if (_g.Solid(c.X, y, c.Z))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The bridge, after everything else: the console block in its marker's cell — its front to the side the
        /// marker names (<see cref="GiantShipRules.FaceSetting"/>), else toward the way in — and a red lamp in every lamp
        /// marker's cell.</summary>
        private void PlaceBridge()
        {
            if (_consoleMarker is { } marker && _consoleBlock != 0 && _g.InBounds(marker.LocalPos))
            {
                var c = marker.LocalPos;
                int face = MarkerSettings.Parse(marker.Data).Get(GiantShipRules.FaceSetting) switch
                {
                    "+x" => 2,
                    "-x" => 3,
                    "+z" => 4,
                    "-z" => 5,
                    _ => FaceToward(c, _arrival),
                };
                _g.Set(c.X, c.Y, c.Z, _consoleBlock);
                _g.SetModifier(c.X, c.Y, c.Z, (0, 0));
                _g.SetShape(c.X, c.Y, c.Z, CubeFacing.Pack(face));
                _console = c;
            }

            if (_redLamp == 0)
            {
                return;
            }

            foreach (var lamp in _lampCells)
            {
                if (_g.InBounds(lamp))
                {
                    _g.Set(lamp.X, lamp.Y, lamp.Z, _redLamp);
                    _g.SetModifier(lamp.X, lamp.Y, lamp.Z, (0, 0));
                    _g.SetShape(lamp.X, lamp.Y, lamp.Z, 0);
                }
            }
        }
    }
}
