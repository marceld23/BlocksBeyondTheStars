// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Renders other players (M24): each <see cref="PlayerPresence"/> creates/updates a blocky
    /// avatar with the sender's colours and a floating nameplate; <see cref="PlayerLeft"/>
    /// removes it. Positions are interpolated toward the latest authoritative value. Presence
    /// is cosmetic — the server stays the source of truth.
    /// </summary>
    public sealed class RemotePlayers : MonoBehaviour
    {
        public GameBootstrap Game;
        public WeaponFx Weapons; // shared VFX layer, for remote jetpack thrust flames

        /// <summary>How far behind the newest presence packet remote avatars are rendered (B Tier1b). Must exceed
        /// the ~0.1 s presence interval so two snapshots usually straddle the render time; 0.15 s absorbs one
        /// late/dropped packet at the cost of seeing others ~150 ms in the past.</summary>
        public float InterpolationDelay = 0.15f;

        private sealed class Remote
        {
            public GameObject Go;
            public PlayerAvatar Avatar;
            public string Name;
            public RemoteEntityInterpolator Interp; // buffered snapshot interpolation of the reported pose (B Tier1b)
            public bool Jetpacking;        // show a thrust flame under the avatar while firing
            public bool Seated;            // sit pose (#806) — avatar lowered onto the chair seat
            public bool Hidden;            // stealth field active, or the player is up in space — no avatar
            public int Gear = -1;          // cached so gear is only rebuilt on change
            public string Held = "\0";     // cached held item key
            public double LastUpdate;      // when the newest presence arrived — drives the stale timeout (#958)
            public bool TimedOut;          // hidden because updates stopped (kept separate from Hidden: that
                                           // flag only toggles visibility on server-sent EDGES)
        }

        // Presence arrives at ~10 Hz for every subject inside the viewer's area of interest. When the stream
        // for a player stops — they left the AoI, boarded a station/ship interior, or launched to space —
        // there is no despawn message (#958), so without a timeout their avatar froze forever at the last
        // delivered position (e.g. standing inside the host's ship).
        private const double StaleHideSeconds = 3.0;
        private const double StaleDestroySeconds = 10.0;

        private readonly Dictionary<string, Remote> _remotes = new Dictionary<string, Remote>();

        /// <summary>Custom pixel faces by player id. Kept separately so a face that arrives before the player's
        /// first presence (or after their avatar is rebuilt) is still applied.</summary>
        private readonly Dictionary<string, string> _faces = new Dictionary<string, string>();

        /// <summary>Body paintings by player id (#874), one slot per BodyPaint part — same cache pattern
        /// (and the same keep-across-world-reset rule) as the faces.</summary>
        private readonly Dictionary<string, string[]> _bodyPaints = new Dictionary<string, string[]>();
        private bool _subscribed;

        /// <summary>Names of other players within <paramref name="range"/> of <paramref name="from"/> (for dock/trade targeting).</summary>
        public List<string> PlayersWithin(Vector3 from, float range)
        {
            var result = new List<string>();
            float sq = range * range;
            foreach (var r in _remotes.Values)
            {
                if (!r.TimedOut && (r.Go.transform.position - from).sqrMagnitude <= sq)
                {
                    result.Add(r.Name);
                }
            }

            return result;
        }

        /// <summary>Visible remote avatars as (name, scene position) — the heat signatures the thermal optic
        /// draws for other players. Hidden remotes (stealth field, or up in space) are skipped, so the scope
        /// cannot be used to defeat a stealth field.</summary>
        public IEnumerable<(string Name, Vector3 Scene)> Contacts()
        {
            foreach (var r in _remotes.Values)
            {
                if (!r.Hidden && !r.TimedOut && r.Go != null)
                {
                    yield return (r.Name, r.Go.transform.position);
                }
            }
        }

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.PlayerPresenceReceived += OnPresence;
                Game.Network.PlayerLeftReceived += OnLeft;
                Game.Network.PlayerFaceReceived += OnFace;
                Game.Network.PlayerBodyPaintReceived += OnBodyPaint;
                Game.Network.WorldResetReceived += OnWorldReset;
                _subscribed = true;
            }

            // Render each remote avatar from its snapshot interpolation buffer (B Tier1b): the pose is sampled at
            // a fixed delay behind the newest packet and interpolated along the SHORTEST wrap path on the torus
            // (the canonical coordinate jumps a whole world at a seam, which a plain lerp would sweep across).
            int circ = Game != null ? Game.Circumference : BlocksBeyondTheStars.Shared.World.WorldConstants.Circumference;
            double now = Time.timeAsDouble;

            // Stale timeout (#958): hide an avatar whose presence stream stopped, and drop it entirely a
            // few seconds later. A fresh presence packet revives it (OnPresence clears TimedOut).
            List<string> stale = null;
            foreach (var kv in _remotes)
            {
                var r = kv.Value;
                double age = now - r.LastUpdate;
                if (age > StaleDestroySeconds)
                {
                    (stale ??= new List<string>()).Add(kv.Key);
                }
                else if (age > StaleHideSeconds && !r.TimedOut)
                {
                    r.TimedOut = true;
                    r.Avatar.SetVisible(false);
                }
            }

            if (stale != null)
            {
                foreach (var id in stale)
                {
                    Destroy(_remotes[id].Go);
                    _remotes.Remove(id); // faces/body paints stay cached — they are per-player, not per-world
                }
            }

            foreach (var r in _remotes.Values)
            {
                if (r.Interp.Sample(now, circ, out var pos, out var yaw))
                {
                    var scene = Game != null ? Game.ScenePos(pos.X, pos.Y, pos.Z) : new Vector3(pos.X, pos.Y, pos.Z);
                    if (r.Seated)
                    {
                        scene.y -= 0.45f; // drop the pelvis onto the chair seat (#806) — the pose bends the legs
                    }

                    r.Go.transform.position = scene;
                    r.Go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                }

                if (r.Jetpacking && !r.Hidden && Weapons != null)
                {
                    Weapons.Sparks(r.Go.transform.position + Vector3.down * 0.1f, new Color(1f, 0.65f, 0.25f), 3);
                }
            }
        }

        private void OnPresence(PlayerPresence m)
        {
            if (Game != null && m.PlayerId == Game.LocalPlayerId)
            {
                return; // never render ourselves
            }

            if (!_remotes.TryGetValue(m.PlayerId, out var r))
            {
                var go = new GameObject($"Player {m.Name}");
                go.transform.SetParent(transform, true); // under the game root → not leaked into menus/editors
                go.transform.position = Game != null ? Game.ScenePos(m.X, m.Y, m.Z) : new Vector3(m.X, m.Y, m.Z);
                var avatar = go.AddComponent<PlayerAvatar>();
                avatar.Build(Rgb(m.Skin), Rgb(m.Torso), Rgb(m.Arms), Rgb(m.Legs), spacesuit: true); // players wear the suit
                if (_faces.TryGetValue(m.PlayerId, out var face))
                {
                    avatar.SetFace(face); // a face we already received before this first presence
                }

                if (_bodyPaints.TryGetValue(m.PlayerId, out var paints))
                {
                    for (int part = 0; part < paints.Length; part++)
                    {
                        if (!string.IsNullOrEmpty(paints[part]))
                        {
                            avatar.SetBodyPaint(part, paints[part]); // body paintings received before the presence
                        }
                    }
                }

                avatar.SetVisible(true);
                r = new Remote { Go = go, Avatar = avatar, Name = m.Name, Interp = new RemoteEntityInterpolator(InterpolationDelay) };
                _remotes[m.PlayerId] = r;
            }

            r.Name = m.Name;
            r.LastUpdate = Time.timeAsDouble;
            if (r.TimedOut)
            {
                r.TimedOut = false;
                r.Avatar.SetVisible(!r.Hidden); // the stream resumed — revive unless server-stealthed
            }

            r.Interp.Push(Time.timeAsDouble, new Vector3f(m.X, m.Y, m.Z), m.Yaw);
            r.Jetpacking = m.Jetpacking;
            if (m.Seated != r.Seated)
            {
                r.Seated = m.Seated;
                r.Avatar.SetSeated(m.Seated);
            }

            // Stealth field active, or the player is up in SPACE (the server stealth-marks orbiters so
            // no frozen ghost avatar keeps standing at the pad they launched from): hide avatar + plate.
            if (m.Stealthed != r.Hidden)
            {
                r.Hidden = m.Stealthed;
                r.Avatar.SetVisible(!m.Stealthed);
            }

            // Equipped gear (helmet/chest/legs/pack/lamp) shown on the remote avatar.
            if (m.Gear != r.Gear)
            {
                r.Gear = m.Gear;
                r.Avatar.SetGear((m.Gear & 1) != 0, (m.Gear & 2) != 0, (m.Gear & 4) != 0, (m.Gear & 8) != 0, (m.Gear & 16) != 0);
            }

            // Held tool/weapon/block shown in the remote avatar's hand.
            if (m.Held != r.Held)
            {
                r.Held = m.Held;
                var (kind, tint, blockKey) = HeldItem.For(Game?.Content, m.Held);
                r.Avatar.SetHeldItem(kind, tint, blockKey);
            }
        }

        private void OnFace(PlayerFace m)
        {
            if (Game != null && m.PlayerId == Game.LocalPlayerId)
            {
                return; // our own face is applied locally
            }

            _faces[m.PlayerId] = m.Pixels ?? string.Empty;
            if (_remotes.TryGetValue(m.PlayerId, out var r) && r.Avatar != null)
            {
                r.Avatar.SetFace(m.Pixels);
            }
        }

        private void OnBodyPaint(PlayerBodyPaint m)
        {
            if (Game != null && m.PlayerId == Game.LocalPlayerId)
            {
                return; // our own paintings are applied locally
            }

            if (m.Part < 0 || m.Part >= BodyPaintKit.PartCount)
            {
                return;
            }

            if (!_bodyPaints.TryGetValue(m.PlayerId, out var paints))
            {
                paints = new string[BodyPaintKit.PartCount];
                _bodyPaints[m.PlayerId] = paints;
            }

            paints[m.Part] = m.Pixels ?? string.Empty;
            if (_remotes.TryGetValue(m.PlayerId, out var r) && r.Avatar != null)
            {
                r.Avatar.SetBodyPaint(m.Part, paints[m.Part]);
            }
        }

        /// <summary>Changing world (planet ↔ station ↔ ship interior) wipes the remote avatars: presence is
        /// per-world, so the previous world's players would linger as frozen ghosts — still offering trade/dock
        /// prompts — at their old-world coordinates (issue #412 M6, mirrors <see cref="NpcView"/>). The new
        /// world's ~10 Hz presence stream repopulates whoever is really here. The face cache is deliberately
        /// KEPT: faces are per-player (not per-world) and the server only sends them on join or change, so a
        /// cleared cache could never be refilled for players we meet again.</summary>
        private void OnWorldReset(WorldReset m)
        {
            foreach (var r in _remotes.Values)
            {
                Destroy(r.Go);
            }

            _remotes.Clear();
        }

        private void OnLeft(PlayerLeft m)
        {
            if (_remotes.TryGetValue(m.PlayerId, out var r))
            {
                Destroy(r.Go);
                _remotes.Remove(m.PlayerId);
            }

            _faces.Remove(m.PlayerId);
            _bodyPaints.Remove(m.PlayerId);
        }

        private void LateUpdate()
        {
            // Modern uGUI nameplates via the shared label layer (replaces IMGUI GUI.Label).
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            var labels = ScreenLabelLayer.Instance;
            foreach (var r in _remotes.Values)
            {
                // #999: TimedOut hides the BODY within seconds (#958) — without checking it here the
                // nameplate kept floating in mid-air over the hidden avatar until the destroy timeout.
                if (!r.Hidden && !r.TimedOut)
                {
                    // Fade names out between 30 m and 45 m — a bit further than NPCs so mates stay recognisable.
                    labels.World(cam, r.Go.transform.position + Vector3.up * 2.1f, r.Name, UiKit.TextCol, false, 30f, 45f);
                }
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.PlayerPresenceReceived -= OnPresence;
                Game.Network.PlayerLeftReceived -= OnLeft;
                Game.Network.PlayerFaceReceived -= OnFace;
                Game.Network.PlayerBodyPaintReceived -= OnBodyPaint;
                Game.Network.WorldResetReceived -= OnWorldReset;
            }
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
