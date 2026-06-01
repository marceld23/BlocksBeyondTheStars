using System.Collections.Generic;
using Spacecraft.Networking.Messages;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Renders the server's live creatures (<c>GameBootstrap.Creatures</c>) as parametric blocky
    /// bodies via <see cref="CreatureBuilder"/>, syncing the set each frame. The server is
    /// authoritative over spawns/positions/deaths; positions are interpolated for smoothness and
    /// the player attacks the nearest one with F (PlayerController). Render-only.
    /// </summary>
    public sealed class CreatureView : MonoBehaviour
    {
        public GameBootstrap Game;

        private sealed class Entry
        {
            public GameObject Root;
            public Vector3 Target;
            public string Bank;   // creature_{size}_{disposition} voice bank
            public float Pitch;   // shifted by body size for per-creature variety
            public float NextCall; // Time.time of the next idle vocalisation
            public float NextAttack; // throttles the attack call while hostile + close
            public bool PrevHostile; // to detect the turn-hostile transition (alert)
            public float PrevHull;   // to detect a hull drop (hurt)
        }

        private readonly Dictionary<string, Entry> _creatures = new Dictionary<string, Entry>();

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            var seen = new HashSet<string>();
            foreach (var c in Game.Creatures)
            {
                seen.Add(c.Id);
                var pos = new Vector3(c.X, c.Y, c.Z);
                if (!_creatures.TryGetValue(c.Id, out var entry))
                {
                    var root = new GameObject("Creature_" + c.SpeciesId);
                    root.transform.position = pos;
                    new CreatureBuilder().Build(root, c);
                    entry = new Entry
                    {
                        Root = root,
                        Target = pos,
                        Bank = Bank(c),
                        Pitch = Mathf.Clamp(1.5f - 0.35f * c.Size, 0.7f, 1.6f),
                        NextCall = Time.time + Random.Range(2f, 6f),
                        PrevHostile = c.Hostile,
                        PrevHull = c.Hull,
                    };
                    _creatures[c.Id] = entry;
                }

                entry.Target = pos;
                // Smoothly chase the authoritative position (creatures may wander later).
                entry.Root.transform.position = Vector3.Lerp(entry.Root.transform.position, entry.Target, Time.deltaTime * 8f);

                // Periodic idle vocalisation, spatialised at the creature, pitched by its size.
                if (Time.time >= entry.NextCall)
                {
                    entry.NextCall = Time.time + Random.Range(5f, 12f);
                    ClientAudio.Instance?.At(entry.Bank + "_idle", entry.Root.transform.position, entry.Pitch, 0.8f);
                }

                // React to authoritative state: hurt on a hull drop, alert on turning hostile,
                // and a throttled attack call when a hostile creature is close to the player.
                var audio = ClientAudio.Instance;
                if (audio != null)
                {
                    if (c.Hull < entry.PrevHull - 0.5f)
                    {
                        audio.At(entry.Bank + "_hurt", entry.Root.transform.position, entry.Pitch, 0.9f);
                    }

                    if (c.Hostile && !entry.PrevHostile)
                    {
                        audio.At(entry.Bank + "_alert", entry.Root.transform.position, entry.Pitch, 0.9f);
                    }

                    if (c.Hostile && Time.time >= entry.NextAttack
                        && (entry.Root.transform.position - Game.PlayerPosition).sqrMagnitude < 9f)
                    {
                        entry.NextAttack = Time.time + Random.Range(1.5f, 3.5f);
                        audio.At(entry.Bank + "_attack", entry.Root.transform.position, entry.Pitch);
                    }
                }

                entry.PrevHull = c.Hull;
                entry.PrevHostile = c.Hostile;
            }

            if (_creatures.Count > seen.Count)
            {
                var stale = new List<string>();
                foreach (var id in _creatures.Keys)
                {
                    if (!seen.Contains(id))
                    {
                        stale.Add(id);
                    }
                }

                foreach (var id in stale)
                {
                    var e = _creatures[id];
                    ClientAudio.Instance?.At(e.Bank + "_die", e.Root.transform.position, e.Pitch, 0.9f);
                    Destroy(e.Root);
                    _creatures.Remove(id);
                }
            }
        }

        /// <summary>Voice bank for a creature: size tier (small/medium/large) x disposition (calm/hostile).</summary>
        private static string Bank(NetCreature c)
        {
            string size = c.Size < 0.8f ? "small" : c.Size < 1.6f ? "medium" : "large";
            return $"creature_{size}_{(c.Hostile ? "hostile" : "calm")}";
        }
    }
}
