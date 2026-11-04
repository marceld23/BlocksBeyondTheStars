// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3177: a trader's (or another pilot's) ship lands and lifts off like the own ship does since #3140/#3141, instead of a
    /// separate ghost sliding down into a hull that already stood on the pad. On a landing FX the parked hull is hidden the
    /// moment <see cref="LandedShipView"/> has built it (a few frames — the server sends the hull just before the FX), a
    /// render-only stand-in sinks from high above on its engine fire, slowing as the dust in the ground's colour races out,
    /// settles with a thump and vents steam, and the real hull shows again. On a launch FX <see cref="LandedShipView"/> hands
    /// the departing hull over (<see cref="AdoptDeparting"/>) and it lifts off with the own ship's beats: ignition, the heavy
    /// rise and hover, the nose coming up, full burn, the vapour cone and its boom. Neither takes the camera. Without a hull
    /// (none known, or none built in time) the old ghost plays.
    /// </summary>
    public sealed partial class ShipTransitView
    {
        private const float HullWaitSeconds = 0.75f;   // how long a landing waits for its hull to be built (a launch for the hand-over)
        private const float RemoteDescendSeconds = 4.2f;
        private const float RemoteSettleSeconds = 1.6f;
        private const float RemoteIgniteAt = 0.35f;
        private const float RemoteLiftAt = 1.6f;
        private const float RemoteClimbAt = 2.8f;
        private const float RemoteBoomAt = 3.3f;
        private const float RemoteGoneAt = 7.5f;       // the climbing hull is a speck high in the sky by then
        private static readonly Color EngineGlowColor = new(1f, 0.62f, 0.3f);

        private sealed class PendingTransit
        {
            public ShipTransitFx Fx;
            public float Since;
        }

        private sealed class HullFlight
        {
            public bool Landing;
            public GameObject Hull;         // landing: the parked hull, hidden while its stand-in flies
            public GameObject Stand;        // landing: the render-only stand-in; lift-off: the adopted hull itself
            public GameObject Doors;        // landing: the stand-in's door copies (their meshes are their own)
            public List<(Vector3 Local, float Size)> Exhausts;
            public Bounds Box;              // the hull's box at rest (world)
            public Vector3 Rest;            // the hull root at rest
            public float Drop;
            public float T;
            public Color Dust;
            public int Light;
            public bool Impact, Ignited, Lifted, Climbing, Boomed;
            public Vector3 Nose = Vector3.forward;
        }

        private readonly List<PendingTransit> _pending = new();
        private readonly List<HullFlight> _flights = new();

        /// <summary>Queues a transit FX to fly the real hull (#3177): a landing always waits a moment for its hull; a launch only
        /// when the hull is still known (the server removes it right after the FX, and <see cref="LandedShipView"/> hands it
        /// over). False when there is nothing to wait for — the ghost plays at once.</summary>
        private bool QueueHullFlight(ShipTransitFx fx)
        {
            if (fx == null || string.IsNullOrEmpty(fx.PlayerId) || Game?.Views.LandedShips == null || (!fx.Landing && ModelOf(fx.PlayerId) == null))
            {
                return false;
            }

            _pending.Add(new PendingTransit { Fx = fx, Since = Time.time });
            return true;
        }

        private void LateUpdate()
        {
            if (Game == null)
            {
                return;
            }

            TickPending();
            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                var f = _flights[i];
                f.T += Time.deltaTime;
                bool done = f.Landing ? UpdateRemoteTouchdown(f) : UpdateRemoteLiftoff(f);
                if (done || Game.SpaceViewActive)
                {
                    EndFlight(f);
                    _flights.RemoveAt(i);
                }
            }
        }

        /// <summary>Starts a waiting landing once its hull is built — in LateUpdate, so a hull <see cref="LandedShipView"/> built
        /// this frame is hidden before it is ever drawn — or falls back to the ghost when it never comes.</summary>
        private void TickPending()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (Game.SpaceViewActive)
                {
                    _pending.RemoveAt(i);
                    continue;
                }

                if (p.Fx.Landing && TryStartRemoteTouchdown(p.Fx))
                {
                    _pending.RemoveAt(i);
                }
                else if (Time.time - p.Since > HullWaitSeconds)
                {
                    _pending.RemoveAt(i);
                    StartGhost(p.Fx);
                }
            }
        }

        /// <summary>The parked-ship model of an owner (a player or <c>npc:…</c> trader) on this world, or null.</summary>
        private LandedShipModel ModelOf(string ownerId)
        {
            foreach (var m in Game.LandedShips.Values)
            {
                if (m.OwnerId == ownerId)
                {
                    return m;
                }
            }

            return null;
        }

        /// <summary>
        /// <see cref="LandedShipView"/> hands over a parked hull the server removed (#3177): taken when a launch FX of its owner
        /// is waiting — its colliders go, its meshes stay, and it lifts off right there. False for any other removal (a logout,
        /// a world switch): the view drops it as before.
        /// </summary>
        public bool AdoptDeparting(GameObject root, LandedShipModel model)
        {
            int index = root == null || model == null ? -1 : _pending.FindIndex(p => !p.Fx.Landing && p.Fx.PlayerId == model.OwnerId);
            if (index < 0)
            {
                return false;
            }

            var fx = _pending[index].Fx;
            _pending.RemoveAt(index);
            ShipCinematicFx.StripColliders(root);
            var f = new HullFlight
            {
                Landing = false,
                Stand = root,
                Box = ShipCinematicFx.RenderBounds(root),
                Rest = root.transform.position,
                Exhausts = ShipMeshBuilder.ExhaustPoints(Game.Content, model.Cells, Vector3.zero),
                Dust = GroundUnder(fx),
            };

            // #3159: the doors are entities of their own — the hull lifts off with copies of them (or its hatch shut by the plan).
            if (Game.Views.Doors?.CloneDoorsWithin(DoorBox(f), root.transform) == null)
            {
                ShipHatchDoor.Build(root.transform, ShipHatchDoor.PlanFor(model), Vector3.zero);
            }

            f.Light = FxLights.Attach(root.transform, f.Box.center - f.Rest + Vector3.down * (f.Box.extents.y + 1.5f), EngineGlowColor, 0f, 18f, flicker: true);
            _flights.Add(f);
            ClientAudio.Instance?.At("engine_thrust", f.Box.center, 1f, 0.6f);
            return true;
        }

        /// <summary>Hides the built parked hull and sends its render-only stand-in down from high above. False while the hull is
        /// not built yet.</summary>
        private bool TryStartRemoteTouchdown(ShipTransitFx fx)
        {
            var model = ModelOf(fx.PlayerId);
            var root = model != null ? Game.Views.LandedShips?.RootOf(model.StructureId) : null;
            if (root == null)
            {
                return false;
            }

            var box = ShipCinematicFx.RenderBounds(root);
            if (box.size.sqrMagnitude < 0.01f)
            {
                return false;
            }

            var f = new HullFlight
            {
                Landing = true,
                Hull = root,
                Box = box,
                Rest = root.transform.position,
                Drop = 56f + box.size.y * 2.5f,
                Exhausts = ShipMeshBuilder.ExhaustPoints(Game.Content, model.Cells, Vector3.zero),
                Dust = GroundUnder(fx),
            };
            f.Stand = ShipCinematicFx.CloneRenderers(root, "RemoteTouchdownShip");
            f.Stand.transform.SetParent(transform, true);
            f.Doors = Game.Views.Doors?.CloneDoorsWithin(DoorBox(f), f.Stand.transform)
                      ?? ShipHatchDoor.Build(f.Stand.transform, ShipHatchDoor.PlanFor(model), Vector3.zero);
            ShipCinematicFx.SetRenderersEnabled(root, false);
            Game.Views.Doors?.SetDoorsHiddenWithin(DoorBox(f), true);
            f.Stand.transform.position = f.Rest + Vector3.up * f.Drop;
            f.Light = FxLights.Attach(f.Stand.transform, f.Box.center - f.Rest + Vector3.down * (f.Box.extents.y + 1.5f), EngineGlowColor, 0f, 18f, flicker: true);
            _flights.Add(f);
            ClientAudio.Instance?.At("engine_thrust", f.Stand.transform.position, 1f, 0.7f);
            return true;
        }

        /// <summary>One frame of a remote touchdown: the stand-in sinks, slowing, on its engine fire with the dust swirling up;
        /// at the ground the real hull takes over, the engines spool down and steam vents. True when it is over.</summary>
        private bool UpdateRemoteTouchdown(HullFlight f)
        {
            if (f.Hull == null)
            {
                return true; // the hull left (a launch, a world switch) while its stand-in was still coming down
            }

            // The view keeps the hull at the copy nearest the player (round worlds): the rest point moves along.
            var shift = f.Hull.transform.position - f.Rest;
            f.Rest += shift;
            f.Box.center += shift;
            if (!f.Impact)
            {
                ShipCinematicFx.SetRenderersEnabled(f.Hull, false); // a re-mesh meanwhile builds visible chunks again
                Game.Views.Doors?.SetDoorsHiddenWithin(DoorBox(f), true); // a door list that came in meanwhile
            }

            float t = Mathf.Clamp01(f.T / RemoteDescendSeconds);
            float e = 1f - (1f - t) * (1f - t) * (1f - t);
            float height = f.Drop * (1f - e);
            if (f.Stand != null)
            {
                float sway = (1f - e) * 0.6f;
                f.Stand.transform.SetPositionAndRotation(
                    f.Rest + Vector3.up * height + new Vector3(Mathf.Sin(f.T * 1.7f), 0f, Mathf.Cos(f.T * 1.3f)) * sway,
                    Quaternion.Euler(5f * (1f - e), 0f, 2.5f * Mathf.Sin(f.T * 2.1f) * (1f - e)));
            }

            float power = f.Impact ? Mathf.Clamp01(1f - (f.T - RemoteDescendSeconds) * 1.6f) : Mathf.Lerp(0.55f, 1f, t);
            var ship = f.Stand != null ? f.Stand.transform : f.Hull.transform;
            ShipCinematicFx.EngineFire(ship, f.Exhausts, Underside(f), Vector3.down, power);
            FxLights.Set(f.Light, EngineGlowColor, 3.2f * power);
            var ground = new Vector3(f.Box.center.x, f.Box.min.y, f.Box.center.z);
            ShipCinematicFx.DustSwirl(ground, f.Box.extents.magnitude + 2f, Mathf.Clamp01(1f - height / 14f) * power, f.Dust);
            if (f.Impact)
            {
                ShipCinematicFx.Steam(f.Box, Mathf.Clamp01(1.4f - (f.T - RemoteDescendSeconds)));
            }
            else if (t >= 1f)
            {
                RemoteImpact(f, ground);
            }

            return f.T >= RemoteDescendSeconds + RemoteSettleSeconds;
        }

        /// <summary>The stand-in is down at rest: the real hull (and its doors) show again, the stand-in goes, the dust bursts out.</summary>
        private void RemoteImpact(HullFlight f, Vector3 ground)
        {
            f.Impact = true;
            ShipCinematicFx.SetRenderersEnabled(f.Hull, true);
            Game.Views.Doors?.SetDoorsHiddenWithin(DoorBox(f), false);
            DropStandIn(f);
            FxLights.Remove(f.Light);
            f.Light = FxLights.Attach(f.Hull.transform, f.Box.center - f.Rest + Vector3.down * (f.Box.extents.y + 1.5f), EngineGlowColor, 0f, 18f, flicker: true);
            ShipCinematicFx.DustBurst(ground, f.Box.extents.magnitude + 1f, f.Dust);
            ClientAudio.Instance?.At("ship_touchdown", ground, 1f, 0.9f);
            ShakeNearby(ground, 0.25f);
        }

        /// <summary>One frame of a remote lift-off — the own ship's beats (#3141) without the camera. True when it is gone.</summary>
        private bool UpdateRemoteLiftoff(HullFlight f)
        {
            if (f.Stand == null)
            {
                return true;
            }

            Game.Views.Doors?.SetDoorsHiddenWithin(DoorBox(f), true); // the server takes the real doors away a moment later
            var ground = new Vector3(f.Box.center.x, f.Box.min.y, f.Box.center.z);
            RemoteLiftoffBeats(f, ground);

            float t = f.T;
            float rise = t < RemoteLiftAt ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - RemoteLiftAt) / (RemoteClimbAt - RemoteLiftAt))) * 5f;
            float climbT = Mathf.Max(0f, t - RemoteClimbAt);
            float pitch = Mathf.SmoothStep(0f, 16f, Mathf.Clamp01((t - RemoteLiftAt - 0.4f) / 1.2f));
            f.Nose = Quaternion.Euler(-pitch, 0f, 0f) * Vector3.forward;
            var climb = (f.Nose * 0.6f + Vector3.up).normalized * (climbT * climbT * 14f);
            float wobble = t > RemoteLiftAt && t < RemoteClimbAt + 0.5f ? 0.8f : 0.2f;
            var shake = new Vector3(Mathf.Sin(t * 13f), 0f, Mathf.Cos(t * 11f)) * 0.05f * wobble;
            f.Stand.transform.SetPositionAndRotation(f.Rest + Vector3.up * rise + climb + shake, Quaternion.Euler(-pitch, 0f, Mathf.Sin(t * 2.3f) * 1.5f * wobble));

            float power = t < RemoteIgniteAt ? 0f : t < RemoteLiftAt ? Mathf.Lerp(0.2f, 0.75f, (t - RemoteIgniteAt) / (RemoteLiftAt - RemoteIgniteAt)) : 1f;
            var push = t < RemoteClimbAt ? Vector3.down : (-f.Nose * 0.5f + Vector3.down).normalized;
            ShipCinematicFx.EngineFire(f.Stand.transform, f.Exhausts, Underside(f), push, power);
            FxLights.Set(f.Light, EngineGlowColor, 3.4f * power);
            ShipCinematicFx.DustSwirl(ground, f.Box.extents.magnitude + 3f, power * Mathf.Clamp01(1f - (rise + climb.y) / 18f), f.Dust);
            return t >= RemoteGoneAt;
        }

        /// <summary>The one-off moments of a remote lift-off: ignition, the lift, the climb, the boom.</summary>
        private void RemoteLiftoffBeats(HullFlight f, Vector3 ground)
        {
            var centre = f.Stand.transform.position + (f.Box.center - f.Rest);
            if (!f.Ignited && f.T >= RemoteIgniteAt)
            {
                f.Ignited = true;
                ClientAudio.Instance?.At("engine_ignite", centre, 1f, 0.9f);
            }

            if (!f.Lifted && f.T >= RemoteLiftAt)
            {
                f.Lifted = true;
                ShipCinematicFx.DustBurst(ground, f.Box.extents.magnitude + 1f, f.Dust);
                ShakeNearby(ground, 0.18f);
            }

            if (!f.Climbing && f.T >= RemoteClimbAt)
            {
                f.Climbing = true;
                ClientAudio.Instance?.At("ship_launch", centre, 1f, 0.9f);
            }

            if (!f.Boomed && f.T >= RemoteBoomAt)
            {
                f.Boomed = true;
                ShipCinematicFx.VapourCone(centre, (f.Nose + Vector3.up * 2f).normalized, f.Box.extents.magnitude);
                ClientAudio.Instance?.At("sonic_boom", centre, 1f, 0.9f);
            }
        }

        /// <summary>Tears a flight down: the light, the stand-in (or the adopted hull with its meshes), the hidden hull shown again.</summary>
        private void EndFlight(HullFlight f)
        {
            if (f.Light != 0)
            {
                FxLights.Remove(f.Light);
                f.Light = 0;
            }

            if (f.Landing)
            {
                DropStandIn(f);
                ShipCinematicFx.SetRenderersEnabled(f.Hull, true);
            }
            else
            {
                ShipCinematicFx.DestroyWithMeshes(f.Stand); // the adopted hull owns its meshes (its door copies too)
                f.Stand = null;
            }

            if (f.Box.size.sqrMagnitude > 0.01f)
            {
                Game?.Views.Doors?.SetDoorsHiddenWithin(DoorBox(f), false);
            }
        }

        /// <summary>Drops a landing's stand-in: it shares the parked hull's meshes (those stay) — only its door copies own theirs.</summary>
        private static void DropStandIn(HullFlight f)
        {
            ShipCinematicFx.DestroyWithMeshes(f.Doors);
            f.Doors = null;
            if (f.Stand != null)
            {
                Destroy(f.Stand);
                f.Stand = null;
            }
        }

        /// <summary>Every flight and waiting FX goes with the view (a world switch, the rig torn down).</summary>
        private void EndAllFlights()
        {
            foreach (var f in _flights)
            {
                EndFlight(f);
            }

            _flights.Clear();
            _pending.Clear();
        }

        /// <summary>#3159: the box a parked ship's doors stand in — its hull box, a little wider (a door sits in the outer wall).</summary>
        private static Bounds DoorBox(HullFlight f)
        {
            var box = f.Box;
            box.Expand(1.5f);
            return box;
        }

        /// <summary>The fire's spot under a hull without engines of its own: the middle of its underside (root-local).</summary>
        private static Vector3 Underside(HullFlight f) => f.Box.center - f.Rest + Vector3.down * f.Box.extents.y;

        /// <summary>The dust colour of the ground under a transit FX's spot.</summary>
        private Color GroundUnder(ShipTransitFx fx)
            => ShipCinematicFx.GroundColor(Game, new BlocksBeyondTheStars.Shared.Geometry.Vector3i(
                Mathf.FloorToInt(fx.X), Mathf.FloorToInt(fx.Y) - 1, Mathf.FloorToInt(fx.Z)));

        /// <summary>A little camera shake when the ship sets down (or lifts) close to the player — none from afar.</summary>
        private static void ShakeNearby(Vector3 at, float trauma)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            float near = Mathf.Clamp01(1f - Vector3.Distance(cam.transform.position, at) / 40f);
            if (near > 0f)
            {
                FxCamera.AddTrauma(trauma * near);
            }
        }
    }
}
