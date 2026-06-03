using System.Collections.Generic;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Renders planet enemies (M25) as simple coloured blocks at the positions the server
    /// reports (<c>GameBootstrap.PlanetEnemies</c>), syncing the set each frame. The server is
    /// authoritative over spawns/positions/deaths; the player attacks with F (PlayerController).
    /// </summary>
    public sealed class WorldEntities : MonoBehaviour
    {
        public GameBootstrap Game;

        private readonly Dictionary<string, GameObject> _cubes = new Dictionary<string, GameObject>();
        private Material _enemyMat;

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            var seen = new HashSet<string>();
            foreach (var e in Game.PlanetEnemies)
            {
                seen.Add(e.Id);
                if (!_cubes.TryGetValue(e.Id, out var go))
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.transform.SetParent(transform, true); // under the game root → not leaked into menus/editors
                    var col = go.GetComponent<Collider>();
                    if (col != null)
                    {
                        Destroy(col);
                    }

                    go.transform.localScale = new Vector3(0.9f, 1.6f, 0.9f);
                    if (_enemyMat == null)
                    {
                        var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Spacecraft/VertexColorOpaque");
                        _enemyMat = new Material(shader) { color = new Color(0.8f, 0.15f, 0.15f) };
                    }

                    go.GetComponent<Renderer>().sharedMaterial = _enemyMat;
                    _cubes[e.Id] = go;
                }

                go.transform.position = new Vector3(e.X, e.Y + 0.8f, e.Z);
            }

            // Remove cubes whose entity is gone (killed / out of range).
            if (_cubes.Count > seen.Count)
            {
                var stale = new List<string>();
                foreach (var id in _cubes.Keys)
                {
                    if (!seen.Contains(id))
                    {
                        stale.Add(id);
                    }
                }

                foreach (var id in stale)
                {
                    Destroy(_cubes[id]);
                    _cubes.Remove(id);
                }
            }
        }
    }
}
