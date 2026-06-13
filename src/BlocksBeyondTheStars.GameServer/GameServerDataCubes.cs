using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// "Data cubes" — glowing download terminals scattered on a body's surface that grant the player a small
/// bundled minigame for their personal arcade collection (played from the in-game menu). Like vaults, they
/// are deterministic from the world seed and stamped once when a world is built: a random count per body
/// (some bodies get none), each at a deterministic surface spot away from the landing zone. Cubes are NOT
/// blocks (the client mesher would collide every non-air block) — they are server entities rendered + lightly
/// collided client-side, exactly like doors.
///
/// The server stays ignorant of the minigame catalogue: a cube only carries a position + a stable seed. The
/// client hashes the seed against its bundled catalogue to decide which game the cube holds (consistent
/// across clients of the same build) and, on download, sends back the resolved key. Because minigames have no
/// gameplay effect, trusting the client for that mapping is safe; the server still validates that the cube
/// exists and the player is standing at it before adding the key to their persisted collection.
/// </summary>
public sealed partial class GameServer
{
    private const float DataCubeReach = 3.2f; // how close a player must stand to download a cube (press E)

    /// <summary>A data cube living in the world. Only on the server; the client sees a <see cref="NetDataCube"/>.</summary>
    internal sealed class ServerDataCube
    {
        public int Id;
        public Vector3f Pos;  // cube centre, just above the surface
        public long Seed;     // stable seed the client maps to a bundled game
    }

    private List<ServerDataCube> _dataCubes => _worlds.Active.DataCubes;
    private int _nextDataCubeId { get => _worlds.Active.NextDataCubeId; set => _worlds.Active.NextDataCubeId = value; }

    /// <summary>Whether the singleplayer "guaranteed start cube" has been dropped yet this server session (so it
    /// lands once, on the first world with a landing pad — the start/home world in a fresh solo game).</summary>
    private bool _startCubePlaced;

    /// <summary>Data-cube positions for tests + inspection.</summary>
    public IReadOnlyList<(int Id, Vector3f Pos)> DataCubeSnapshots
        => _dataCubes.Select(c => (c.Id, c.Pos)).ToList();

    /// <summary>Number of data cubes on the active world.</summary>
    public int DataCubeCount => _dataCubes.Count;

    /// <summary>Scatters this world's data cubes (idempotent per world). A random count per body — many bodies
    /// get none, the rest carry one to a few — so a cube find stays a small surprise. Deterministic from the
    /// world seed, so the same body always yields the same cubes (which game each holds is the client's call
    /// from the cube seed). Void worlds (orbital stations) and the random "none" roll get skipped.</summary>
    private void StampDataCubes()
    {
        if (_dataCubes.Count > 0)
        {
            return; // already stamped for this resident world
        }

        var planet = _world.Planet;
        if (planet.Void)
        {
            return; // stations have no surface to scatter cubes on
        }

        long cSeed = _meta.Seed ^ WorldGenerator.StableHash("datacube:" + _world.LocationId);
        var rng = new System.Random(unchecked((int)(cSeed ^ (cSeed >> 32))));

        // Most bodies carry none; the rest one to three. Tuned so cubes feel like an occasional reward, and
        // moons/asteroids/barren worlds are as likely to be empty as not.
        double r = rng.NextDouble();
        int count = r < 0.45 ? 0 : r < 0.80 ? 1 : r < 0.95 ? 2 : 3;

        for (int i = 0; i < count; i++)
        {
            // A deterministic spot away from the spawn/landing area, each cube in its own direction (mirrors vaults).
            int ax = WorldConstants.WrapX((60 + rng.Next(360)) * (rng.Next(2) == 0 ? 1 : -1), _world.Circumference);
            int az = (60 + rng.Next(360)) * (rng.Next(2) == 0 ? 1 : -1);
            int surfaceY = _generator.SurfaceHeight(planet, ax, az);

            _dataCubes.Add(new ServerDataCube
            {
                Id = _nextDataCubeId++,
                Pos = new Vector3f(ax + 0.5f, surfaceY + 1f, az + 0.5f),
                Seed = WorldGenerator.StableHash($"datacube:{_world.LocationId}:{i}") ^ _meta.Seed,
            });
        }

        // Singleplayer/admin convenience: drop one guaranteed cube right beside the start world's landing pad,
        // once per session (so the first world a solo player lands on always has a minigame within reach).
        if (_config.GuaranteeStartDataCube && !_startCubePlaced && _landingPads.Count > 0)
        {
            _startCubePlaced = true;
            int gx = WorldConstants.WrapX(_landingPads[0].CenterX + 6, _world.Circumference);
            int gz = _landingPads[0].CenterZ + 6;
            int gy = _generator.SurfaceHeight(planet, gx, gz);
            _dataCubes.Add(new ServerDataCube
            {
                Id = _nextDataCubeId++,
                Pos = new Vector3f(gx + 0.5f, gy + 1f, gz + 0.5f),
                Seed = WorldGenerator.StableHash($"datacube-start:{_world.LocationId}") ^ _meta.Seed,
            });
        }

        if (_dataCubes.Count > 0)
        {
            _log.Info($"Scattered {_dataCubes.Count} data cube(s) on '{_world.LocationId}'.");
        }
    }

    private void SendDataCubes(PlayerSession session)
        => Send(session, new DataCubeList { Cubes = _dataCubes.Select(ToNetDataCube).ToArray() });

    private static NetDataCube ToNetDataCube(ServerDataCube c) => new()
    {
        Id = c.Id,
        X = c.Pos.X,
        Y = c.Pos.Y,
        Z = c.Pos.Z,
        Seed = c.Seed,
    };

    /// <summary>The player's downloaded-games collection (server → client). Sent on join and after each download.</summary>
    private void SendGameUnlocks(PlayerSession session)
        => Send(session, new GameUnlocks { Unlocked = session.State.UnlockedGames.ToArray() });

    /// <summary>A player downloads a data cube they're standing at (press E). Validates the cube exists and the
    /// player is within reach, then adds the client-resolved game key to their persisted collection. The cube
    /// is never consumed, so other players (and a fresh download attempt) still work.</summary>
    private void HandleUnlockGame(PlayerSession session, UnlockGameIntent intent)
    {
        var cube = _dataCubes.FirstOrDefault(c => c.Id == intent.CubeId);
        if (cube is null)
        {
            return; // unknown cube (stale client, wrong world)
        }

        if (WrapDistSq(session.State.Position, cube.Pos) > DataCubeReach * DataCubeReach)
        {
            return; // too far to reach the terminal
        }

        string key = (intent.GameKey ?? string.Empty).Trim();
        if (key.Length == 0 || key.Length > 64)
        {
            return; // missing/implausible key
        }

        if (session.State.UnlockedGames.Add(key))
        {
            SendGameUnlocks(session);
        }
    }
}
