using System.Collections.Generic;
using Spacecraft.Networking.Messages;
using UnityEngine;

namespace Spacecraft.Client
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

        private sealed class Remote
        {
            public GameObject Go;
            public PlayerAvatar Avatar;
            public string Name;
            public Vector3 Target;       // latest reported position in canonical world space
            public Vector3 SettledWorld;  // smoothed position in world space (converted to scene for display)
            public float Yaw;
            public bool Jetpacking;        // show a thrust flame under the avatar while firing
            public int Gear = -1;          // cached so gear is only rebuilt on change
            public string Held = "\0";     // cached held item key
        }

        private readonly Dictionary<string, Remote> _remotes = new Dictionary<string, Remote>();
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
                _subscribed = true;
            }

            // Smoothly move avatars toward their latest reported position/heading. Smoothing is done in
            // world space, then mapped to the scene at the copy nearest the player (longitude wraps).
            foreach (var r in _remotes.Values)
            {
                r.SettledWorld = Vector3.Lerp(r.SettledWorld, r.Target, Time.deltaTime * 10f);
                r.Go.transform.position = Game != null ? Game.ScenePos(r.SettledWorld.x, r.SettledWorld.y, r.SettledWorld.z) : r.SettledWorld;
                r.Go.transform.rotation = Quaternion.Euler(0f, r.Yaw, 0f);

                if (r.Jetpacking && Weapons != null)
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
                avatar.SetVisible(true);
                r = new Remote { Go = go, Avatar = avatar, Name = m.Name, SettledWorld = new Vector3(m.X, m.Y, m.Z) };
                _remotes[m.PlayerId] = r;
            }

            r.Name = m.Name;
            r.Target = new Vector3(m.X, m.Y, m.Z);
            r.Yaw = m.Yaw;
            r.Jetpacking = m.Jetpacking;

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

        private void OnLeft(PlayerLeft m)
        {
            if (_remotes.TryGetValue(m.PlayerId, out var r))
            {
                Destroy(r.Go);
                _remotes.Remove(m.PlayerId);
            }
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
                labels.World(cam, r.Go.transform.position + Vector3.up * 2.1f, r.Name, UiKit.TextCol);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.PlayerPresenceReceived -= OnPresence;
                Game.Network.PlayerLeftReceived -= OnLeft;
            }
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
