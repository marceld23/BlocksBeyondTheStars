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
            public bool Hidden;            // stealth field active, or the player is up in space — no avatar
            public int Gear = -1;          // cached so gear is only rebuilt on change
            public string Held = "\0";     // cached held item key
        }

        private readonly Dictionary<string, Remote> _remotes = new Dictionary<string, Remote>();

        /// <summary>Custom pixel faces by player id. Kept separately so a face that arrives before the player's
        /// first presence (or after their avatar is rebuilt) is still applied.</summary>
        private readonly Dictionary<string, string> _faces = new Dictionary<string, string>();
        private bool _subscribed;

        /// <summary>Names of other players within <paramref name="range"/> of <paramref name="from"/> (for dock/trade targeting).</summary>
        public List<string> PlayersWithin(Vector3 from, float range)
        {
            var result = new List<string>();
            float sq = range * range;
            foreach (var r in _remotes.Values)
            {
                if ((r.Go.transform.position - from).sqrMagnitude <= sq)
                {
                    result.Add(r.Name);
                }
            }

            return result;
        }

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.PlayerPresenceReceived += OnPresence;
                Game.Network.PlayerLeftReceived += OnLeft;
                Game.Network.PlayerFaceReceived += OnFace;
                _subscribed = true;
            }

            // Render each remote avatar from its snapshot interpolation buffer (B Tier1b): the pose is sampled at
            // a fixed delay behind the newest packet and interpolated along the SHORTEST wrap path on the torus
            // (the canonical coordinate jumps a whole world at a seam, which a plain lerp would sweep across).
            int circ = Game != null ? Game.Circumference : BlocksBeyondTheStars.Shared.World.WorldConstants.Circumference;
            double now = Time.timeAsDouble;
            foreach (var r in _remotes.Values)
            {
                if (r.Interp.Sample(now, circ, out var pos, out var yaw))
                {
                    r.Go.transform.position = Game != null ? Game.ScenePos(pos.X, pos.Y, pos.Z) : new Vector3(pos.X, pos.Y, pos.Z);
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
                avatar.Build(Rgb(m.Skin), Rgb(m.Torso), Rgb(m.Arms), Rgb(m.Legs));
                if (_faces.TryGetValue(m.PlayerId, out var face))
                {
                    avatar.SetFace(face); // a face we already received before this first presence
                }

                avatar.SetVisible(true);
                r = new Remote { Go = go, Avatar = avatar, Name = m.Name, Interp = new RemoteEntityInterpolator(InterpolationDelay) };
                _remotes[m.PlayerId] = r;
            }

            r.Name = m.Name;
            r.Interp.Push(Time.timeAsDouble, new Vector3f(m.X, m.Y, m.Z), m.Yaw);
            r.Jetpacking = m.Jetpacking;

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

        private void OnLeft(PlayerLeft m)
        {
            if (_remotes.TryGetValue(m.PlayerId, out var r))
            {
                Destroy(r.Go);
                _remotes.Remove(m.PlayerId);
            }

            _faces.Remove(m.PlayerId);
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
                if (!r.Hidden)
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
            }
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
