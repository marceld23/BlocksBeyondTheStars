// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

public static partial class DerelictGenerator
{
    /// <summary>#3915: the char of a FRESH wreck's battle scars — the plating round its hull breaches and its loose plates,
    /// burnt darker than the soot of the centuries (<see cref="GiantShipGenerator.SootTint"/>).</summary>
    public const int ScorchTint = 0x2A2420;

    /// <summary>#3915: a screen that went dark — the tint of a FRESH wreck's dead <c>lab_panel</c> screens.</summary>
    public const int DeadScreenTint = 0x1C2024;

    /// <summary>#3915: how far round a breach's first cell the plating is scorched (blocks, a sphere).</summary>
    public const int ScorchReach = 3;

    /// <summary>#3915: the share of the plating within <see cref="ScorchReach"/> of a breach that is scorched.</summary>
    public const double ScorchShare = 0.55;

    /// <summary>#3915: the share of a FRESH wreck's <c>lab_panel</c> screens that went dark.</summary>
    public const double DeadScreenShare = 0.75;

    /// <summary>#3915: the screen block of a warship's consoles (the cruiser has no "off" twin of it — and a new block
    /// would shift every block id, so a dead screen is a tint).</summary>
    private const string ScreenBlock = "lab_panel";

    /// <summary>#3915: no loose plate lies this close (blocks, sideways) to the guardian bay — its release ring stays free.</summary>
    private const int PlateBayClearance = 2;

    /// <summary>#3915: the form of a loose hull plate on a deck — the thin quarter-height plate (a square: no yaw to tell).</summary>
    public static int LoosePlateShape => ShapeCode.Pack(BlockShape.Panel, 0);

    /// <summary>
    /// The ship-mode decay's BATTLE SCARS (#3915) — a FRESH wreck only (the escort cruiser's, shot a moment ago), a human hull
    /// only, after everything else (<see cref="ShipPass.Run"/> calls it last) and from a random stream of its own, so the
    /// chests, the doors, the arrival, the guardian bay, the bridge and the lamps come out exactly where they did without
    /// them, and a wreck that is not fresh never takes a branch of this:
    /// <list type="number">
    /// <item>dead screens: <see cref="DeadScreenShare"/> of the <c>lab_panel</c> cells take <see cref="DeadScreenTint"/>
    /// (no glow);</item>
    /// <item>scorch round every hull breach: within <see cref="ScorchReach"/> of its first cell, <see cref="ScorchShare"/>
    /// of the plating takes <see cref="ScorchTint"/> (its glow kept) — never glass, a lamp, a machine (the bridge console,
    /// the guardian bay), a door, a ladder, a plant or a reserved cell;</item>
    /// <item>loose plates: in every walkable room one or two hull plates (a torn room) or none or one (a closed room) lie on
    /// the deck — the hull block in the <see cref="BlockShape.Panel"/> form, scorched — never on a reserved cell (a doorway
    /// or right in front of one, the arrival, a chest, a sound, the bridge), never on top of one, never within
    /// <see cref="PlateBayClearance"/> of the guardian bay and never within 3 of the arrival; each is reserved.</item>
    /// </list>
    /// </summary>
    private sealed partial class ShipPass
    {
        private readonly HashSet<ushort> _unscarred = new();
        private readonly bool _battleScars;
        private ushort _screen;

        /// <summary>The screen block and the blocks no scorch ever darkens: every lamp (on or off), every glass, every machine.</summary>
        private void ReadBattleContent(GameContent content)
        {
            _screen = content.GetBlock(ScreenBlock)?.NumericId.Value ?? 0;
            foreach (var def in content.Blocks.Values)
            {
                ushort id = def.NumericId.Value;
                string key = def.Key;
                if (id != 0 && (def.Category is "light" or "machine" || key.StartsWith("light_", StringComparison.Ordinal)
                                || key.StartsWith("strip_light", StringComparison.Ordinal) || key.StartsWith("glass", StringComparison.Ordinal)))
                {
                    _unscarred.Add(id);
                }
            }
        }

        /// <summary>The battle scars of a fresh wreck (see the summary above), from their own stream.</summary>
        private void BattleScars()
        {
            if (!_fresh || !_battleScars || _alien)
            {
                return;
            }

            var rng = new Random(StableHashing.Seed32("derelict-giant-battle:" + _source.Key, _seed));
            DeadScreens(rng);
            ScorchBreaches(rng);
            LoosePlates(rng);
        }

        /// <summary>Most of the screens went dark: a share of the <c>lab_panel</c> cells, in cell order.</summary>
        private void DeadScreens(Random rng)
        {
            if (_screen == 0)
            {
                return;
            }

            for (int i = 0; i < _g.Size; i++)
            {
                if (_g.BlockAt(i) == _screen && rng.NextDouble() < DeadScreenShare)
                {
                    var c = _g.CellOf(i);
                    _g.SetModifier(c.X, c.Y, c.Z, (DeadScreenTint, 0));
                }
            }
        }

        /// <summary>Scorched plating round every breach — a tighter, darker ring than the centuries' soot.</summary>
        private void ScorchBreaches(Random rng)
        {
            const int reachSq = ScorchReach * ScorchReach;
            foreach (var centre in _breachCentres)
            {
                for (int dx = -ScorchReach; dx <= ScorchReach; dx++)
                    for (int dy = -ScorchReach; dy <= ScorchReach; dy++)
                        for (int dz = -ScorchReach; dz <= ScorchReach; dz++)
                        {
                            var c = new Vector3i(centre.X + dx, centre.Y + dy, centre.Z + dz);
                            if ((dx * dx) + (dy * dy) + (dz * dz) > reachSq || !_g.InBounds(c))
                            {
                                continue;
                            }

                            if (rng.NextDouble() < ScorchShare && Scarrable(c))
                            {
                                var (_, glow) = _g.ModAt(_g.Idx(c.X, c.Y, c.Z));
                                _g.SetModifier(c.X, c.Y, c.Z, (ScorchTint, glow));
                            }
                        }
            }
        }

        /// <summary>Whether a cell's block may be scorched: plating — not air, glass, a lamp, a machine, a door, a ladder or a
        /// plant — on a cell nothing else claims (the reserved cells, the bridge console, its lamps).</summary>
        private bool Scarrable(Vector3i c)
        {
            ushort b = _g.Get(c);
            return b != 0 && b != _ids.Glass && b != _consoleBlock && b != _ids.GuardianBay && !_noCarve.Contains(b)
                   && !_emergency.ContainsKey(b) && !_unscarred.Contains(b) && !_reserved.Contains(c) && c != _console
                   && !_lampCells.Contains(c);
        }

        /// <summary>A few hull plates torn off in the fight lie on the decks of the walkable rooms.</summary>
        private void LoosePlates(Random rng)
        {
            if (_ids.Hull == 0)
            {
                return;
            }

            int shape = LoosePlateShape;
            for (int r = 0; r < _rooms.Count; r++)
            {
                if (!_walkable[r])
                {
                    continue;
                }

                int pieces = _breached[r] ? 1 + rng.Next(2) : rng.Next(2);
                if (pieces == 0)
                {
                    continue;
                }

                var spots = Spots(r, minArrivalDistance: 3);
                spots.RemoveAll(c => !PlateSpot(c));
                for (int n = 0; n < pieces && spots.Count > 0; n++)
                {
                    var c = spots[rng.Next(spots.Count)];
                    spots.Remove(c);
                    _g.Set(c.X, c.Y, c.Z, _ids.Hull);
                    _g.SetShape(c.X, c.Y, c.Z, shape);
                    _g.SetModifier(c.X, c.Y, c.Z, (ScorchTint, 0));
                    _reserved.Add(c);
                }
            }
        }

        /// <summary>A deck cell a loose plate may lie on: not on top of something placed (a reserved cell below — a chest, the
        /// bay, the console, a lamp) and clear of the guardian bay's release ring.</summary>
        private bool PlateSpot(Vector3i c)
        {
            if (_reserved.Contains(new Vector3i(c.X, c.Y - 1, c.Z)))
            {
                return false;
            }

            return _spawner is not { } bay || Math.Abs(c.X - bay.X) > PlateBayClearance || Math.Abs(c.Z - bay.Z) > PlateBayClearance
                   || Math.Abs(c.Y - bay.Y) > 1;
        }
    }
}
