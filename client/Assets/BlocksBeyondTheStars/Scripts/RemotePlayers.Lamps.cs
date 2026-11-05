// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3234: other players' suit lamps light this world. The presence says whose lamp is lit (the <c>GearLook.LampLit</c>
    /// bit of its gear mask);
    /// every frame the nearest lit ones within <see cref="HeadlampRules.RemoteReach"/> (at most
    /// <see cref="HeadlampRules.MaxRemoteLamps"/>) go to the shaders as remote lamps — from the lamp on each avatar's helmet,
    /// along the way its head faces. A hidden (stealthed, in space) or timed-out avatar's lamp lights nothing.
    /// </summary>
    public sealed partial class RemotePlayers
    {
        private readonly List<Vector3f> _lampSpots = new List<Vector3f>();
        private readonly List<PlayerAvatar> _lampAvatars = new List<PlayerAvatar>();
        private readonly List<int> _lampPick = new List<int>();
        private readonly List<Vector3> _lampPositions = new List<Vector3>();
        private readonly List<Vector3> _lampAxes = new List<Vector3>();
        private bool _lampsPublished;

        private void PublishRemoteLamps(Camera cam)
        {
            _lampSpots.Clear();
            _lampAvatars.Clear();
            if (cam != null && (Game == null || !Game.SpaceViewActive))
            {
                foreach (var r in _remotes.Values)
                {
                    if (r.LampOn && !r.Hidden && !r.TimedOut && r.Avatar != null && r.Avatar.TryHelmetLamp(out var spot))
                    {
                        _lampSpots.Add(new Vector3f(spot.x, spot.y, spot.z));
                        _lampAvatars.Add(r.Avatar);
                    }
                }
            }

            _lampPositions.Clear();
            _lampAxes.Clear();
            if (_lampSpots.Count > 0)
            {
                var v = cam.transform.position;
                HeadlampRules.SelectRemote(_lampSpots, new Vector3f(v.x, v.y, v.z), HeadlampRules.RemoteReach, HeadlampRules.MaxRemoteLamps, _lampPick);
                foreach (int i in _lampPick)
                {
                    var p = _lampSpots[i];
                    _lampPositions.Add(new Vector3(p.X, p.Y, p.Z));
                    _lampAxes.Add(_lampAvatars[i].HelmetForward);
                }
            }

            // Publish while lamps shine, and once more when the last one goes out.
            if (_lampPositions.Count > 0 || _lampsPublished)
            {
                Headlamp.PublishRemotes(_lampPositions, _lampAxes);
                _lampsPublished = _lampPositions.Count > 0;
            }
        }
    }
}
