// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The client's copy of the Crystal Net of the current world (#2046) and of the own ships parked on it (#2268). The
    /// server sends one list per <b>frame</b> — the world grid (frame "") and every parked ship (frame = its structure id,
    /// cells ship-local) — as whole lists (<see cref="CrystalNetList"/>, <see cref="CrystalDeviceList"/>) and, between
    /// them, device deltas (<see cref="CrystalDeviceDelta"/>, #2267).
    /// <para>Everything that draws or aims works in world cells, so this class <b>composes</b> one world-cell view of all
    /// frames: a ship's cells are moved by the ship's origin (wrapped on round worlds) — <see cref="Nets"/> and
    /// <see cref="Devices"/> are fresh arrays after every change, so a view can tell a change by reference. A device the
    /// player operates goes back to the server in its own frame (<see cref="TryFrameCell"/>). A ship the client does not
    /// know yet (its lists can arrive before the ship) simply shows up once it does (<see cref="Recompose"/>).</para>
    /// Unity-free, so the bookkeeping is covered by plain .NET tests.
    /// </summary>
    public sealed class ClientCrystalNet
    {
        private readonly Dictionary<string, NetCrystalNet[]> _nets = new Dictionary<string, NetCrystalNet[]>();
        private readonly Dictionary<string, Dictionary<Vector3i, NetCrystalDevice>> _devices = new Dictionary<string, Dictionary<Vector3i, NetCrystalDevice>>();
        private readonly Dictionary<Vector3i, (string Frame, Vector3i Local)> _frameCells = new Dictionary<Vector3i, (string, Vector3i)>();
        private readonly Dictionary<Vector3i, NetCrystalDevice> _byCell = new Dictionary<Vector3i, NetCrystalDevice>();

        /// <summary>The world cell of a parked ship's origin by its structure id, or null while the ship is unknown.</summary>
        public Func<string, Vector3i?> FrameOrigin { get; set; } = _ => null;

        /// <summary>The current world's circumference (0 = no wrap).</summary>
        public Func<int> Circumference { get; set; } = () => 0;

        /// <summary>Every network of every frame, in world cells.</summary>
        public NetCrystalNet[] Nets { get; private set; } = Array.Empty<NetCrystalNet>();

        /// <summary>Every device of every frame, in world cells (a ship's devices are copies moved by its origin).</summary>
        public NetCrystalDevice[] Devices { get; private set; } = Array.Empty<NetCrystalDevice>();

        /// <summary>Whether any parked ship carries a net — only then does a moved or re-sent ship need a recompose.</summary>
        public bool HasFrames { get; private set; }

        public void OnNets(CrystalNetList m)
        {
            string frame = m.Frame ?? string.Empty;
            var nets = m.Nets ?? Array.Empty<NetCrystalNet>();
            if (frame.Length > 0 && nets.Length == 0)
            {
                _nets.Remove(frame);
            }
            else
            {
                _nets[frame] = nets;
            }

            Recompose();
        }

        public void OnDevices(CrystalDeviceList m)
        {
            string frame = m.Frame ?? string.Empty;
            var devices = m.Devices ?? Array.Empty<NetCrystalDevice>();
            if (frame.Length > 0 && devices.Length == 0)
            {
                _devices.Remove(frame);
            }
            else
            {
                var map = new Dictionary<Vector3i, NetCrystalDevice>(devices.Length);
                foreach (var d in devices)
                {
                    map[new Vector3i(d.X, d.Y, d.Z)] = d;
                }

                _devices[frame] = map;
            }

            Recompose();
        }

        /// <summary>#2267: changed devices replace theirs, removed cells (x, y, z triples) leave.</summary>
        public void OnDelta(CrystalDeviceDelta m)
        {
            string frame = m.Frame ?? string.Empty;
            if (!_devices.TryGetValue(frame, out var map))
            {
                map = new Dictionary<Vector3i, NetCrystalDevice>();
                _devices[frame] = map;
            }

            foreach (var d in m.Changed ?? Array.Empty<NetCrystalDevice>())
            {
                map[new Vector3i(d.X, d.Y, d.Z)] = d;
            }

            var removed = m.Removed ?? Array.Empty<int>();
            for (int i = 0; i + 2 < removed.Length; i += 3)
            {
                map.Remove(new Vector3i(removed[i], removed[i + 1], removed[i + 2]));
            }

            Recompose();
        }

        /// <summary>A new world: nothing of the old one stays.</summary>
        public void Clear()
        {
            _nets.Clear();
            _devices.Clear();
            Recompose();
        }

        /// <summary>The device at a world cell, or null.</summary>
        public NetCrystalDevice? DeviceAt(int x, int y, int z)
            => _byCell.TryGetValue(Canonical(x, y, z), out var d) ? d : null;

        /// <summary>Whether a world cell belongs to a parked ship's net — then the server wants the ship's structure id and
        /// the ship-local cell.</summary>
        public bool TryFrameCell(int x, int y, int z, out string frame, out Vector3i local)
        {
            if (_frameCells.TryGetValue(Canonical(x, y, z), out var f))
            {
                frame = f.Frame;
                local = f.Local;
                return true;
            }

            frame = string.Empty;
            local = default;
            return false;
        }

        /// <summary>Builds the world-cell view of every frame again (a list arrived, a ship moved or arrived).</summary>
        public void Recompose()
        {
            var nets = new List<NetCrystalNet>();
            var devices = new List<NetCrystalDevice>();
            _frameCells.Clear();
            _byCell.Clear();
            HasFrames = false;
            int circumference = Circumference();

            foreach (var kv in _nets)
            {
                if (kv.Key.Length == 0)
                {
                    nets.AddRange(kv.Value);
                    continue;
                }

                HasFrames = true;
                if (FrameOrigin(kv.Key) is not { } o)
                {
                    continue;
                }

                foreach (var n in kv.Value)
                {
                    var src = n.Cells ?? Array.Empty<int>();
                    var cells = new int[src.Length];
                    for (int i = 0; i + 2 < src.Length; i += 3)
                    {
                        cells[i] = Wrap(o.X + src[i], circumference);
                        cells[i + 1] = o.Y + src[i + 1];
                        cells[i + 2] = o.Z + src[i + 2];
                    }

                    nets.Add(new NetCrystalNet { Id = n.Id, On = n.On, Cells = cells });
                }
            }

            foreach (var kv in _devices)
            {
                if (kv.Key.Length == 0)
                {
                    foreach (var d in kv.Value.Values)
                    {
                        devices.Add(d);
                        _byCell[new Vector3i(Wrap(d.X, circumference), d.Y, d.Z)] = d;
                    }

                    continue;
                }

                HasFrames = true;
                if (FrameOrigin(kv.Key) is not { } o)
                {
                    continue;
                }

                foreach (var d in kv.Value.Values)
                {
                    var world = new Vector3i(Wrap(o.X + d.X, circumference), o.Y + d.Y, o.Z + d.Z);
                    var copy = new NetCrystalDevice
                    {
                        Id = d.Id, X = world.X, Y = world.Y, Z = world.Z, Kind = d.Kind, Mode = d.Mode, Config = d.Config,
                        Label = d.Label, OwnerId = d.OwnerId, Output = d.Output, Choices = d.Choices,
                    };
                    devices.Add(copy);
                    _byCell[world] = copy;
                    _frameCells[world] = (kv.Key, new Vector3i(d.X, d.Y, d.Z));
                }

                // A ship's conduits carry no device record, but a click on one still names the ship (the server answers "gone").
                if (_nets.TryGetValue(kv.Key, out var shipNets))
                {
                    foreach (var n in shipNets)
                    {
                        var src = n.Cells ?? Array.Empty<int>();
                        for (int i = 0; i + 2 < src.Length; i += 3)
                        {
                            var local = new Vector3i(src[i], src[i + 1], src[i + 2]);
                            var world = new Vector3i(Wrap(o.X + local.X, circumference), o.Y + local.Y, o.Z + local.Z);
                            if (!_frameCells.ContainsKey(world))
                            {
                                _frameCells[world] = (kv.Key, local);
                            }
                        }
                    }
                }
            }

            Nets = nets.ToArray();
            Devices = devices.ToArray();
        }

        private Vector3i Canonical(int x, int y, int z) => new Vector3i(Wrap(x, Circumference()), y, z);

        private static int Wrap(int x, int circumference) => circumference > 0 ? WorldConstants.WrapX(x, circumference) : x;
    }
}
