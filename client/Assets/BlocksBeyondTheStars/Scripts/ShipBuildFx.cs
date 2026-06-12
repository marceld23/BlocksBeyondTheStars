using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// "Materialize" dressing when the active ship is (re)stamped while you watch: switching ships
    /// at a pad re-streams the hull, and this adds a rising holo-sweep ring + a shimmer cue so the
    /// new hull reads as building up instead of popping in. Keyed off the ShipStations message
    /// (always re-sent after a restamp); the join-time snapshot only baselines, and an unchanged
    /// resend does nothing.
    /// </summary>
    public sealed class ShipBuildFx : MonoBehaviour
    {
        public GameBootstrap Game;

        private bool _subscribed;
        private string _signature;
        private Material _sweepMat;

        private void Update()
        {
            if (Game?.Network == null)
            {
                return;
            }

            if (!_subscribed)
            {
                Game.Network.ShipStationsReceived += OnStations;
                _subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.ShipStationsReceived -= OnStations;
            }
        }

        private void OnStations(ShipStations m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in m.Stations ?? System.Array.Empty<NetShipStation>())
            {
                sb.Append(s.Type).Append(s.X).Append(s.Y).Append(s.Z).Append('|');
            }

            string sig = sb.ToString();
            bool first = _signature == null;
            bool changed = _signature != sig;
            _signature = sig;
            if (first || !changed || m.Stations == null || m.Stations.Length == 0
                || Game.SpaceViewActive)
            {
                return;
            }

            // Ship footprint from the stations' bounding box (stations sit just inside the walls).
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            float floorY = float.MaxValue;
            foreach (var s in m.Stations)
            {
                minX = Mathf.Min(minX, s.X);
                maxX = Mathf.Max(maxX, s.X);
                minZ = Mathf.Min(minZ, s.Z);
                maxZ = Mathf.Max(maxZ, s.Z);
                floorY = Mathf.Min(floorY, s.Y);
            }

            var center = Game.ScenePos((minX + maxX) * 0.5f, floorY, (minZ + maxZ) * 0.5f);
            Sweep(center, (maxX - minX) * 0.5f + 2f, (maxZ - minZ) * 0.5f + 2f);
            ClientAudio.Instance?.Cue("teleport", 0.8f); // materialize shimmer
        }

        /// <summary>A perimeter ring of glowing cyan bits that rises through the hull volume and fades.</summary>
        private void Sweep(Vector3 center, float halfX, float halfZ)
        {
            _sweepMat ??= new Material(Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque"))
            {
                color = ShaderColor.Srgb(new Color(0.45f, 0.95f, 1f)),
            };

            const int count = 24;
            for (int i = 0; i < count; i++)
            {
                // Evenly spaced around the footprint rectangle's perimeter.
                float t = i / (float)count * 4f;
                Vector3 offset = (int)t switch
                {
                    0 => new Vector3(Mathf.Lerp(-halfX, halfX, t), 0f, -halfZ),
                    1 => new Vector3(halfX, 0f, Mathf.Lerp(-halfZ, halfZ, t - 1f)),
                    2 => new Vector3(Mathf.Lerp(halfX, -halfX, t - 2f), 0f, halfZ),
                    _ => new Vector3(-halfX, 0f, Mathf.Lerp(halfZ, -halfZ, t - 3f)),
                };

                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = p.GetComponent<Collider>();
                if (col != null)
                {
                    Destroy(col);
                }

                p.transform.position = center + offset;
                p.transform.localScale = new Vector3(0.25f, 0.06f, 0.25f);
                p.GetComponent<Renderer>().sharedMaterial = _sweepMat;
                p.AddComponent<Riser>();
            }
        }

        /// <summary>One sweep bit: rises ~5 blocks over 1.5 s, shrinking away at the top.</summary>
        private sealed class Riser : MonoBehaviour
        {
            private const float Life = 1.5f;
            private float _t;

            private void Update()
            {
                _t += Time.deltaTime;
                float k = Mathf.Clamp01(_t / Life);
                transform.position += Vector3.up * (5f / Life) * Time.deltaTime;
                transform.localScale = new Vector3(0.25f, 0.06f, 0.25f) * Mathf.Max(0f, 1f - k * k);
                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
