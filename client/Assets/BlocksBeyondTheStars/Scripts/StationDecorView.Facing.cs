// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The station forms face whoever uses them (<see cref="StationDecorFacing"/>): the stations carry no
    /// orientation, so it is read from the ship around them, once per decor build. The cockpit turns its front against the
    /// ship's nose (from the engine cells to the helm, as the transition camera reads it — this works for a self-built ship
    /// too, whose helm is the "cockpit" station); a lab or ship-console terminal turns toward a side someone can stand on,
    /// the one most toward the middle of the ship. The medbay and the workshop are not turned. While the marker block is
    /// not known yet (the ship or its chunk not streamed), it is asked again twice a second, for at most
    /// <see cref="FormTurnPatience"/> seconds; then the form turns with what is known.
    /// </summary>
    public sealed partial class StationDecorView
    {
        /// <summary>The child of a form's root that holds its body and screen and takes the turn.</summary>
        private const string FormTurnName = "DecorFormTurn";

        private const float FormTurnPatience = 30f;

        private readonly List<(Transform Turn, NetShipStation Station)> _formTurns = new List<(Transform Turn, NetShipStation Station)>();
        private float _nextFormTurn, _formTurnGiveUp;

        /// <summary>Queues the turn of a freshly built cockpit or terminal form.</summary>
        private void QueueFormTurn(GameObject go, NetShipStation s)
        {
            if (go == null || s == null || (s.Type != "cockpit" && s.Type != "lab" && s.Type != "console"))
            {
                return;
            }

            var turn = go.transform.Find(FormTurnName);
            if (turn == null)
            {
                return;
            }

            _formTurns.Add((turn, s));
            _nextFormTurn = 0f;
            _formTurnGiveUp = Time.time + FormTurnPatience;
        }

        /// <summary>Turns the queued forms whose surroundings are known; cheap while nothing is queued.</summary>
        private void TurnPendingForms()
        {
            if (_formTurns.Count == 0 || Time.time < _nextFormTurn)
            {
                return;
            }

            _nextFormTurn = Time.time + 0.5f;
            bool force = Time.time > _formTurnGiveUp;
            for (int i = _formTurns.Count - 1; i >= 0; i--)
            {
                var (turn, station) = _formTurns[i];
                if (turn == null)
                {
                    _formTurns.RemoveAt(i); // the decor was rebuilt
                    continue;
                }

                if (TryFormQuarters(station, force, out int quarters))
                {
                    turn.localRotation = Quaternion.Euler(0f, 90f * quarters, 0f);
                    _formTurns.RemoveAt(i);
                }
            }
        }

        /// <summary>The quarter turns of a station's form, or false while its marker block is not known yet (unless
        /// <paramref name="force"/>).</summary>
        private bool TryFormQuarters(NetShipStation s, bool force, out int quarters)
        {
            quarters = 0;
            int x = Mathf.FloorToInt(s.X), y = Mathf.FloorToInt(s.Y + 0.01f), z = Mathf.FloorToInt(s.Z);
            bool onShip = !Game.LandedShipBlockAt(x, y, z, out var ship, out var local).IsAir;
            bool known = onShip || (Game.World != null && Game.World.TryGetBlock(x, y, z, out var block) && !block.IsAir);
            if (!known && !force)
            {
                return false;
            }

            if (s.Type == "cockpit")
            {
                var nose = ShipNose(ship, local);
                quarters = StationDecorFacing.CockpitFront(nose.X, nose.Z);
                return true;
            }

            var interior = ShipInterior(ship, s);
            quarters = StationDecorFacing.TerminalFront(CellPassable, x, y, z, interior.X, interior.Z);
            return true;
        }

        /// <summary>The ship's nose from its engine cells to the helm cell (ship-local; the ship's cells are not rotated
        /// against the world's). No ship or no engines: the stock ships' +Z.</summary>
        private (float X, float Z) ShipNose(LandedShipModel ship, Vector3i helm)
        {
            if (ship == null || Game.Content == null)
            {
                return (0f, 1f);
            }

            var engines = new List<(int X, int Y, int Z)>();
            foreach (var kv in ship.Cells)
            {
                if (ShipExhausts.IsEngineBlock(Game.Content.BlockById(kv.Value)?.Key))
                {
                    engines.Add((kv.Key.X, kv.Key.Y, kv.Key.Z));
                }
            }

            return TransitionCockpit.Nose(engines, helm.X + 0.5f, helm.Z + 0.5f);
        }

        /// <summary>The way from the station toward the middle of its ship (its cell bounds), or — without a ship model —
        /// toward the middle of the other stations.</summary>
        private (float X, float Z) ShipInterior(LandedShipModel ship, NetShipStation s)
        {
            if (ship != null)
            {
                float mx = ship.Width * 0.5f, mz = ship.Length * 0.5f;
                float sx = WorldConstants.WrapDeltaX(Mathf.FloorToInt(s.X) - ship.Origin.X, Game.Circumference) + 0.5f;
                float sz = Mathf.FloorToInt(s.Z) - ship.Origin.Z + 0.5f;
                return (mx - sx, mz - sz);
            }

            float cx = 0f, cz = 0f;
            int n = 0;
            foreach (var o in Game.Stations ?? System.Array.Empty<NetShipStation>())
            {
                if (o != null && !ReferenceEquals(o, s))
                {
                    cx += o.X;
                    cz += o.Z;
                    n++;
                }
            }

            return n == 0 ? (0f, 0f) : ((cx / n) - s.X, (cz / n) - s.Z);
        }

        /// <summary>True when a person can be in the world cell: neither a solid ship cell nor a solid world block.</summary>
        private bool CellPassable(int x, int y, int z)
        {
            var content = Game.Content;
            var id = Game.LandedShipBlockAt(x, y, z, out _, out _);
            if (id.IsAir && Game.World != null)
            {
                id = Game.World.GetBlock(x, y, z);
            }

            return id.IsAir || (content != null && content.BlockById(id) is { Solid: false });
        }
    }
}
