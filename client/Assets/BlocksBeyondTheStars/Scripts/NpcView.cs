using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Renders settlement + space-station NPCs (<see cref="NpcList"/>): a blocky humanoid avatar per
    /// NPC with its skin/outfit colours, heading and a role nameplate. Positions interpolate toward the
    /// latest authoritative value; NPCs no longer in the list are removed. Cosmetic — the server owns
    /// NPC state. Mirrors <see cref="RemotePlayers"/>.
    /// </summary>
    public sealed class NpcView : MonoBehaviour
    {
        public GameBootstrap Game;

        private sealed class Npc
        {
            public GameObject Go;
            public PlayerAvatar Avatar;
            public Vector3 Target;        // canonical world space
            public Vector3 SettledWorld;   // smoothed world position (mapped to scene for display)
            public float Yaw;
            public string Label;
            public float GestureTimer;     // counts down to the next work gesture (mine/place/talk swing)
            public float GestureLo, GestureHi; // cadence range from the NPC's theme/role
            public string Greeting;        // item 15: a contextual speech-bubble line (empty = none showing)
            public float GreetingUntil;    // Time.time after which the bubble fades out
        }

        private const float GreetingSeconds = 7f; // how long a greeting bubble stays up

        private readonly Dictionary<int, Npc> _npcs = new Dictionary<int, Npc>();
        private bool _subscribed;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.NpcsReceived += OnNpcs;
                Game.Network.WorldResetReceived += OnWorldReset;
                Game.Network.NpcGreetingReceived += OnGreeting;
                _subscribed = true;
            }

            foreach (var n in _npcs.Values)
            {
                // Track the latest target without fully catching up before the next snapshot arrives, so the
                // NPC keeps gliding (and its walk cycle keeps running) instead of stop-start jerking.
                n.SettledWorld = Vector3.Lerp(n.SettledWorld, n.Target, Time.deltaTime * 5f);
                n.Go.transform.position = Game != null ? Game.ScenePos(n.SettledWorld.x, n.SettledWorld.y, n.SettledWorld.z) : n.SettledWorld;
                n.Go.transform.rotation = Quaternion.Euler(0f, n.Yaw, 0f);

                // Ambient work gestures: a periodic tool/arm swing so settlement NPCs look busy (miners chip
                // away often, settlers/builders place now and then, vendors gesture occasionally).
                n.GestureTimer -= Time.deltaTime;
                if (n.GestureTimer <= 0f)
                {
                    n.Avatar?.Swing();
                    n.GestureTimer = Random.Range(n.GestureLo, n.GestureHi);
                }
            }
        }

        /// <summary>Changing world (planet ↔ station ↔ space) wipes the NPCs: each world keeps its own list with
        /// IDs that restart at 1, so without this the previous world's NPCs would linger — e.g. a space station's
        /// inhabitants left hanging in a planet's sky (B33). The new world's <see cref="NpcList"/> repopulates.</summary>
        /// <summary>A contextual greeting for an NPC (item 15): show it as a speech bubble over that NPC. When the
        /// server sends an empty line (AI off/unreachable) we render a localized static fallback by role, so a
        /// greeting always appears regardless of whether an LLM backend is running.</summary>
        private void OnGreeting(NpcGreeting m)
        {
            if (!_npcs.TryGetValue(m.NpcId, out var n))
            {
                return; // greeting for an NPC we aren't rendering (left the area) — ignore
            }

            string text = string.IsNullOrWhiteSpace(m.Text) ? FallbackGreeting(m.Role) : m.Text.Trim();
            n.Greeting = text;
            n.GreetingUntil = Time.time + GreetingSeconds;
        }

        /// <summary>The localized static greeting shown when no AI line is available, keyed by NPC role.</summary>
        private string FallbackGreeting(string role)
        {
            var loc = Game?.Localizer;
            string key = role == "quartermaster" ? "npc.greet.quartermaster" : "npc.greet.vendor";
            return loc != null ? loc.Get(key) : string.Empty;
        }

        private void OnWorldReset(WorldReset m)
        {
            foreach (var n in _npcs.Values)
            {
                if (n.Go != null)
                {
                    Destroy(n.Go);
                }
            }

            _npcs.Clear();
        }

        private void OnNpcs(NpcList m)
        {
            var seen = new HashSet<int>();
            foreach (var nd in m.Npcs)
            {
                seen.Add(nd.Id);
                if (!_npcs.TryGetValue(nd.Id, out var n))
                {
                    var go = new GameObject($"NPC {nd.Role}");
                    go.transform.SetParent(transform, true); // under the game root → not leaked into menus/editors
                    go.transform.position = Game != null ? Game.ScenePos(nd.X, nd.Y, nd.Z) : new Vector3(nd.X, nd.Y, nd.Z);

                    var avatar = go.AddComponent<PlayerAvatar>();
                    Color skin = nd.IsRobot ? new Color(0.75f, 0.78f, 0.82f) : Rgb(nd.SkinRgb);
                    Color outfit = Rgb(nd.OutfitRgb);
                    avatar.Build(skin, outfit, outfit * 0.85f, outfit * 0.7f);
                    avatar.SetVisible(true);

                    if (nd.Size > 0f && !Mathf.Approximately(nd.Size, 1f))
                    {
                        go.transform.localScale = Vector3.one * nd.Size;
                    }

                    var (lo, hi) = WorkCadence(nd);
                    n = new Npc
                    {
                        Go = go,
                        Avatar = avatar,
                        SettledWorld = new Vector3(nd.X, nd.Y, nd.Z),
                        GestureLo = lo,
                        GestureHi = hi,
                        GestureTimer = Random.Range(lo, hi),
                    };
                    _npcs[nd.Id] = n;
                }

                n.Target = new Vector3(nd.X, nd.Y, nd.Z);
                n.Yaw = nd.Facing * Mathf.Rad2Deg;
                n.Label = Label(nd);
            }

            if (_npcs.Count > seen.Count)
            {
                var stale = new List<int>();
                foreach (var id in _npcs.Keys)
                {
                    if (!seen.Contains(id))
                    {
                        stale.Add(id);
                    }
                }

                foreach (var id in stale)
                {
                    Destroy(_npcs[id].Go);
                    _npcs.Remove(id);
                }
            }
        }

        /// <summary>How often an NPC plays a work gesture (seconds, min/max), from its theme/role — miners
        /// chip away often, settlers/builders place now and then, vendors/researchers gesture occasionally.</summary>
        private static (float Lo, float Hi) WorkCadence(NetNpc nd)
        {
            string theme = (nd.Theme ?? string.Empty).ToLowerInvariant();
            string role = (nd.Role ?? string.Empty).ToLowerInvariant();
            if (theme.Contains("miner"))
            {
                return (1.2f, 2.4f);
            }

            if (role == "settler" || theme.Contains("settler") || theme.Contains("build"))
            {
                return (2.6f, 4.6f);
            }

            return (4.5f, 8.5f); // vendors / quartermasters / traders / researchers
        }

        private string Label(NetNpc nd)
        {
            var loc = Game?.Localizer;
            string role = loc != null ? loc.Get(nd.NameKey) : nd.Role;
            // "Name · Role" (item 12); fall back to just the role for any NPC without a coined name.
            return string.IsNullOrEmpty(nd.Name) ? role : $"{nd.Name} · {role}";
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
            foreach (var n in _npcs.Values)
            {
                // Names only read up close: fade out between 18 m and 28 m so distant NPCs stay anonymous.
                labels.World(cam, n.Go.transform.position + Vector3.up * 2.1f, n.Label, UiKit.Cyan, false, 18f, 28f);

                // A live greeting bubble sits just above the nameplate (item 15).
                if (!string.IsNullOrEmpty(n.Greeting) && Time.time < n.GreetingUntil)
                {
                    labels.World(cam, n.Go.transform.position + Vector3.up * 2.5f, $"“{n.Greeting}”", UiKit.TextCol, false, 18f, 28f);
                }
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.NpcsReceived -= OnNpcs;
                Game.Network.WorldResetReceived -= OnWorldReset;
                Game.Network.NpcGreetingReceived -= OnGreeting;
            }
        }

        private static Color Rgb(uint rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
