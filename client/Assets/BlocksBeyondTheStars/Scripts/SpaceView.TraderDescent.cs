// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A landing trader is seen landing (#3824 — a young player's report: "I cannot see where traders land"). The server
    /// flies the trader to its destination body, holds it there and names the ship and the body
    /// (<see cref="SpaceWarpFx"/> with the style "descent"); this draws the dive: the ship turns to the planet, falls
    /// ever faster to its surface, glows as it meets the air (a retro burn on an airless body) and shrinks until the
    /// clouds take it. The numbers are <see cref="NpcDescentPath"/>. The body is the one THIS scene shows — the server
    /// only knows where the launch body hangs exactly.
    /// </summary>
    public sealed partial class SpaceView
    {
        private sealed class NpcDescent
        {
            public Vector3 From, To, Body;
            public double Start;
            public float Seconds;
            public bool Airless, Roar;
            public Color Plasma;
        }

        private readonly Dictionary<string, NpcDescent> _npcDescents = new Dictionary<string, NpcDescent>();

        /// <summary>The server says a ship dives into a body. False when this scene cannot show it (no such ship or body
        /// here) — the caller then plays the plain flash.</summary>
        private bool BeginNpcDescent(SpaceWarpFx fx)
        {
            if (string.IsNullOrEmpty(fx.Subject) || fx.Seconds <= 0.1f || !TryLandable(fx.Body, out var body, out float radius))
            {
                return false;
            }

            var from = new Vector3(fx.X, fx.Y, fx.Z);
            if (_remotePlayers.TryGetValue(fx.Subject, out var av) && av.Root != null)
            {
                from = av.Root.transform.localPosition; // where the ship is drawn right now — the dive starts without a jump
            }

            var to = NpcDescentPath.SurfacePoint(ToNumerics(from), ToNumerics(body), radius);
            var d = new NpcDescent
            {
                From = from,
                To = new Vector3(to.X, to.Y, to.Z),
                Body = body,
                Start = Time.timeAsDouble,
                Seconds = fx.Seconds,
            };
            ReadNpcDescentAir(d);
            _npcDescents[fx.Subject] = d;
            return true;
        }

        /// <summary>The scene's body with the star-map id <paramref name="bodyId"/>; the launch body is listed under "".</summary>
        private bool TryLandable(string bodyId, out Vector3 pos, out float radius)
        {
            string want = bodyId ?? string.Empty;
            if (Game?.StarMap != null && want == Game.StarMap.ActiveLocationId)
            {
                want = string.Empty;
            }

            foreach (var b in _landables)
            {
                if (b.Id == want)
                {
                    (pos, radius) = (b.Pos, b.Radius);
                    return true;
                }
            }

            (pos, radius) = (Vector3.zero, 0f);
            return false;
        }

        /// <summary>The glow's colour from the body's own air — the player's own entry reads the same numbers.</summary>
        private void ReadNpcDescentAir(NpcDescent d)
        {
            OrbitBody look = null;
            float bestSq = float.MaxValue;
            foreach (var ob in _orbitBodies)
            {
                float sq = (ob.Pos - d.Body).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    look = ob;
                }
            }

            var atm = look != null ? look.Atmosphere : default;
            d.Airless = look != null && !atm.HasAir;
            var sky = look != null ? TransitionDirector.Rgb(atm.SkyRgb) : new Color(0.55f, 0.75f, 0.95f);
            var air = atm.Breathable ? new Color(1f, 0.5f, 0.42f) : Color.Lerp(sky, new Color(1f, 0.45f, 0.2f), 0.45f);
            d.Plasma = Color.Lerp(new Color(1f, 0.55f, 0.22f), air, 0.55f);
        }

        /// <summary>Once a frame per remote ship, after its pose was set: a ship on its dive is drawn on the dive instead.
        /// Returns true while the dive owns the ship.</summary>
        private bool PoseNpcDescent(string id, RemoteAvatar av, double now)
        {
            if (_npcDescents.Count == 0 || !_npcDescents.TryGetValue(id, out var d) || av.Root == null)
            {
                return false;
            }

            float t = Mathf.Clamp01((float)((now - d.Start) / d.Seconds));
            var root = av.Root.transform;
            if (t >= NpcDescentPath.HideAt)
            {
                if (av.Root.activeSelf)
                {
                    av.Root.SetActive(false); // in the clouds: gone until the server drops the pose
                }

                return true;
            }

            var p = NpcDescentPath.PositionAt(ToNumerics(d.From), ToNumerics(d.To), t);
            var pos = new Vector3(p.X, p.Y, p.Z);
            var dive = d.To - d.From;
            dive = dive.sqrMagnitude > 1e-4f ? dive.normalized : Vector3.down;
            var outward = pos - d.Body;
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.up;
            // Nose down the path, belly to the air; eased in over the first third so the turn is seen.
            var side = Vector3.Cross(outward, Vector3.right);
            var level = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.forward;
            var aim = Quaternion.LookRotation(Vector3.Slerp(level, dive, 0.8f), outward);
            root.localPosition = pos;
            root.localRotation = Quaternion.Slerp(root.localRotation, aim, Mathf.Clamp01(t * 3f));
            root.localScale = Vector3.one * NpcDescentPath.ScaleAt(t);
            NpcEntryFx(d, av, root, dive, t);
            return true;
        }

        private void NpcEntryFx(NpcDescent d, RemoteAvatar av, Transform root, Vector3 diveLocal, float t)
        {
            var centre = root.position;
            var lead = _root.transform.TransformDirection(diveLocal);
            float size = 3.2f * root.lossyScale.x;
            if (!d.Roar && t > 0.1f)
            {
                d.Roar = true;
                ClientAudio.Instance?.At(d.Airless ? "engine_thrust" : "reentry_roar", centre, 1f, 0.7f);
            }

            if (d.Airless)
            {
                float power = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.75f, t));
                int burn = Mathf.RoundToInt(3f * power * FxKit.Density);
                for (int i = 0; i < burn; i++)
                {
                    var hot = Color.Lerp(new Color(1f, 0.6f, 0.25f), new Color(1f, 0.95f, 0.8f), Random.value);
                    FxKit.Emit(FxKit.Kind.Glow, centre + lead * size * 0.6f + Random.insideUnitSphere * size * 0.3f, lead * Random.Range(10f, 18f), size * Random.Range(0.4f, 0.7f), Random.Range(0.12f, 0.22f), hot);
                }

                return;
            }

            float heat = NpcDescentPath.HeatAt(t);
            if (heat < 0.05f)
            {
                return;
            }

            // The glowing head ahead of the hull and the plasma torn off it, streaming back like a comet's tail.
            FxKit.Emit(FxKit.Kind.Glow, centre + lead * size * 0.5f, lead * 2f, size * (0.9f + 0.6f * heat), 0.1f, Color.Lerp(d.Plasma, new Color(1f, 0.93f, 0.75f), heat * 0.5f));
            int n = Mathf.RoundToInt(4f * heat * FxKit.Density) + 1;
            for (int i = 0; i < n; i++)
            {
                var c = Color.Lerp(d.Plasma, new Color(1f, 0.9f, 0.7f), Random.value * 0.6f);
                FxKit.Emit(FxKit.Kind.Sparks, centre + Random.insideUnitSphere * size * 0.6f, -lead * Random.Range(18f, 32f) + Random.insideUnitSphere * 2f, Random.Range(0.16f, 0.3f), Random.Range(0.25f, 0.45f), c);
            }
        }

        /// <summary>True when the ship's dive is over — its avatar goes at once, without the grace a missing pose gets.</summary>
        private bool NpcDescentOver(string id, double now)
            => _npcDescents.TryGetValue(id, out var d) && now - d.Start >= d.Seconds * NpcDescentPath.HideAt;
    }
}
