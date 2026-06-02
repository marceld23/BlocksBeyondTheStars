using System.Collections.Generic;
using Spacecraft.Networking.Messages;
using UnityEngine;

namespace Spacecraft.Client
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
            public Vector3 Target;
            public float Yaw;
            public string Label;
        }

        private readonly Dictionary<int, Npc> _npcs = new Dictionary<int, Npc>();
        private bool _subscribed;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.NpcsReceived += OnNpcs;
                _subscribed = true;
            }

            foreach (var n in _npcs.Values)
            {
                n.Go.transform.position = Vector3.Lerp(n.Go.transform.position, n.Target, Time.deltaTime * 8f);
                n.Go.transform.rotation = Quaternion.Euler(0f, n.Yaw, 0f);
            }
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
                    go.transform.position = new Vector3(nd.X, nd.Y, nd.Z);

                    var avatar = go.AddComponent<PlayerAvatar>();
                    Color skin = nd.IsRobot ? new Color(0.75f, 0.78f, 0.82f) : Rgb(nd.SkinRgb);
                    Color outfit = Rgb(nd.OutfitRgb);
                    avatar.Build(skin, outfit, outfit * 0.85f, outfit * 0.7f);
                    avatar.SetVisible(true);

                    if (nd.Size > 0f && !Mathf.Approximately(nd.Size, 1f))
                    {
                        go.transform.localScale = Vector3.one * nd.Size;
                    }

                    n = new Npc { Go = go };
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

        private string Label(NetNpc nd)
        {
            var loc = Game?.Localizer;
            return loc != null ? loc.Get(nd.NameKey) : nd.Role;
        }

        private void OnGUI()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = UiKit.Cyan } };
            foreach (var n in _npcs.Values)
            {
                var head = n.Go.transform.position + Vector3.up * 2.1f;
                var sp = cam.WorldToScreenPoint(head);
                if (sp.z <= 0f)
                {
                    continue; // behind the camera
                }

                GUI.Label(new Rect(sp.x - 75f, Screen.height - sp.y - 10f, 150f, 20f), n.Label, style);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.NpcsReceived -= OnNpcs;
            }
        }

        private static Color Rgb(uint rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
