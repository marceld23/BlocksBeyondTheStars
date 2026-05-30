using System.Collections.Generic;
using System.IO;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Localization;
using Spacecraft.Shared.World;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Entry point MonoBehaviour. Loads data-driven content, connects to a server, and turns
    /// incoming chunk/state messages into the rendered world. Attach to a single GameObject
    /// in the scene and assign a material for chunk meshes.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("Connection")]
        public string Host = "127.0.0.1";
        public int Port = 31415;
        public string PlayerName = "Pilot";
        public string Password = "";

        [Header("Localization")]
        public bool German = false;

        [Header("Rendering")]
        public Material ChunkMaterial;

        public GameContent Content { get; private set; }
        public Localizer Localizer { get; private set; }
        public NetworkClient Network { get; private set; }
        public ClientWorld World { get; private set; }

        // Latest authoritative player vitals for the HUD.
        public float Health { get; private set; } = 100f;
        public float Oxygen { get; private set; } = 100f;
        public float SuitEnergy { get; private set; } = 100f;

        private readonly Dictionary<ChunkCoord, GameObject> _chunkObjects = new Dictionary<ChunkCoord, GameObject>();
        private readonly HashSet<ChunkCoord> _dirty = new HashSet<ChunkCoord>();

        private void Start()
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "data");
            Content = ContentLoader.LoadFromDirectory(dataDir);
            Localizer = Content.CreateLocalizer(German ? GameLocale.German : GameLocale.English);
            World = new ClientWorld();

            Network = new NetworkClient();
            Network.JoinAccepted += m => Debug.Log($"Joined as {m.PlayerId} on planet {m.PlanetType} (seed {m.WorldSeed}).");
            Network.JoinRejected += m => Debug.LogWarning($"Join rejected: {m.Reason}");
            Network.ChunkReceived += OnChunk;
            Network.BlockChanged += OnBlockChanged;
            Network.PlayerStateUpdated += OnPlayerState;
            Network.ActionRejected += m => Debug.Log($"Action '{m.Action}' rejected: {m.Reason}");
            Network.ServerMessageReceived += m => Debug.Log(m.Text);

            Network.Connect(Host, Port);
            Network.Join(PlayerName, string.IsNullOrEmpty(Password) ? null : Password);
        }

        private void Update()
        {
            Network?.Poll();

            // Rebuild any chunk meshes that changed this frame.
            if (_dirty.Count > 0)
            {
                foreach (var coord in _dirty)
                {
                    RebuildChunk(coord);
                }

                _dirty.Clear();
            }
        }

        private void OnChunk(Spacecraft.Networking.Messages.ChunkDataMessage m)
        {
            var coord = new ChunkCoord(m.Cx, m.Cy, m.Cz);
            World.StoreChunk(coord, m.Blocks);
            _dirty.Add(coord);
        }

        private void OnBlockChanged(Spacecraft.Networking.Messages.BlockChanged m)
        {
            if (World.ApplyBlockChange(m.X, m.Y, m.Z, m.Block, out var coord))
            {
                _dirty.Add(coord);
            }
        }

        private void OnPlayerState(Spacecraft.Networking.Messages.PlayerStateUpdate m)
        {
            Health = m.Health;
            Oxygen = m.Oxygen;
            SuitEnergy = m.SuitEnergy;
        }

        private void RebuildChunk(ChunkCoord coord)
        {
            if (!World.TryGetChunk(coord, out var chunk))
            {
                return;
            }

            var mesh = ChunkMesher.Build(chunk, Content, World.GetBlock);

            if (!_chunkObjects.TryGetValue(coord, out var go))
            {
                go = new GameObject($"Chunk {coord.X},{coord.Y},{coord.Z}");
                go.transform.SetParent(transform);
                var origin = WorldConstants.ChunkOrigin(coord);
                go.transform.position = new Vector3(origin.X, origin.Y, origin.Z);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ChunkMaterial;
                go.AddComponent<MeshCollider>();
                _chunkObjects[coord] = go;
            }

            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshCollider>().sharedMesh = mesh;
        }

        private void OnDestroy() => Network?.Dispose();
    }
}
