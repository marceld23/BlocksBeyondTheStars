// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A trader that lands is seen landing (#3824 — a young player's report: "I cannot see where traders land. Let them fly
/// to the planet until they are as close as a player has to be, and then land with the fire").
/// <para>A trader with a landing destination used to head for a random point 40–100 units around the scene centre and
/// leave the flight with the same warp flash as a ship that jumps away — a landing and a departure looked alike, and
/// neither pointed at a planet. Now it flies to its destination body and stops where a player ship may land
/// (<see cref="SystemBodyLayout.LandApproachDistance"/>); there it holds for <see cref="TraderDescentSeconds"/> while
/// every client in the flight draws its dive into the body (<see cref="SpaceWarpFx.Style"/> =
/// <see cref="TraderDescentStyle"/>), and the pilots read which trader lands on which body.</para>
/// <para>The server knows the launch body's spot exactly (the scene is centred on it). Another body's spot is laid out
/// by the client (moons step outward, overlapping bodies are nudged apart), so <see cref="TraderBodyInScene"/> is an
/// estimate for those — close enough to fly to; the dive itself is drawn by the client at the body it really shows.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds a landing trader holds at its approach point while the clients draw the dive.</summary>
    internal const double TraderDescentSeconds = 5.0;

    /// <summary>The <see cref="SpaceWarpFx.Style"/> of a trader's dive into a body.</summary>
    public const string TraderDescentStyle = "descent";

    /// <summary>Where <paramref name="bodyId"/> sits in <paramref name="instance"/>'s flight scene and how big it is
    /// drawn. False for a body of another system (it is not in the scene).</summary>
    private bool TraderBodyInScene(SpaceInstance instance, string bodyId, out Vector3f centre, out float radius)
    {
        centre = default;
        radius = 0f;
        string homeId = InstanceBodyId(instance);
        var home = _galaxy?.FindBody(homeId);
        var body = _galaxy?.FindBody(bodyId);
        if (home is null || body is null || body.SystemId != home.SystemId)
        {
            return false;
        }

        var homeCentre = new Vector3f(SystemBodyLayout.HomeX, SystemBodyLayout.HomeY, SystemBodyLayout.HomeZ);
        float homeRadius = VisibleDiameterOf(home) * SystemBodyLayout.HomeDiameterFactor * 0.5f;
        if (body.Id == home.Id)
        {
            (centre, radius) = (homeCentre, homeRadius);
            return true;
        }

        radius = VisibleDiameterOf(body) * 0.5f;
        const float scale = SystemBodyLayout.FlightViewScale;
        var parent = body.Kind == CelestialKind.Moon && body.ParentId.Length > 0 ? _galaxy!.FindBody(body.ParentId) : null;
        if (parent is null)
        {
            centre = PlanetInScene(home, body, radius);
            return true;
        }

        // A moon: outside its planet's surface (the client's first orbit slot; further moons step outward from it).
        bool ofHome = parent.Id == home.Id;
        float parentRadius = ofHome ? homeRadius : VisibleDiameterOf(parent) * 0.5f;
        var parentCentre = ofHome ? homeCentre : PlanetInScene(home, parent, parentRadius);
        float rx = (body.SystemX - parent.SystemX) * scale, rz = (body.SystemZ - parent.SystemZ) * scale;
        float len = System.MathF.Sqrt((rx * rx) + (rz * rz));
        float orbit = System.MathF.Max(len, SystemBodyLayout.MinOrbitFor(parentRadius, radius));
        if (IsHomeParent(home, parent))
        {
            // #3919: a sibling of the launch moon — the launch moon holds the planet's first slot, so it rings above it.
            float homeSlot = System.MathF.Sqrt((parentCentre.X * parentCentre.X) + (parentCentre.Z * parentCentre.Z));
            orbit = System.MathF.Max(orbit, SystemBodyLayout.NextMoonOrbit(parentRadius, radius, homeSlot, HomeTrueRadius(home)));
        }

        (rx, rz) = len > 0.01f ? (rx / len, rz / len) : (1f, 0f);
        centre = new Vector3f(parentCentre.X + (rx * orbit), parentCentre.Y, parentCentre.Z + (rz * orbit));
        return true;
    }

    /// <summary>A planet's (or a loose body's) spot in the flight scene of <paramref name="home"/>: its star-map offset,
    /// scaled — except the planet a launch moon orbits (#3919), which keeps its true direction at a clear distance
    /// (<see cref="SystemBodyLayout.HomeParentPosition"/>, the client's rule), not inside the climb's path.</summary>
    private Vector3f PlanetInScene(CelestialBody home, CelestialBody planet, float planetRadius)
    {
        const float scale = SystemBodyLayout.FlightViewScale;
        float x = (planet.SystemX - home.SystemX) * scale, z = (planet.SystemZ - home.SystemZ) * scale;
        if (IsHomeParent(home, planet))
        {
            (x, z) = SystemBodyLayout.HomeParentPosition(x, z, planetRadius, HomeTrueRadius(home));
        }

        return new Vector3f(x, 0f, z);
    }

    /// <summary>Whether <paramref name="planet"/> is the planet the launch body <paramref name="home"/> orbits as its moon.</summary>
    private static bool IsHomeParent(CelestialBody home, CelestialBody planet)
        => home.Kind == CelestialKind.Moon && home.ParentId.Length > 0 && home.ParentId == planet.Id;

    /// <summary>The launch body's true drawn radius — what it is drawn at in another body's flight, not the oversized
    /// launch-body drawing (<see cref="SystemBodyLayout.HomeDiameterFactor"/>).</summary>
    private float HomeTrueRadius(CelestialBody home) => VisibleDiameterOf(home) * 0.5f;

    /// <summary>The point a trader coming from <paramref name="from"/> flies to before it lands on
    /// <paramref name="bodyId"/>: on the body's landing shell, on the side it comes from. False when the body is not in
    /// the scene.</summary>
    private bool TraderApproachPoint(SpaceInstance instance, string bodyId, Vector3f from, out Vector3f point)
    {
        point = default;
        if (!TraderBodyInScene(instance, bodyId, out var centre, out float radius))
        {
            return false;
        }

        float dx = from.X - centre.X, dy = from.Y - centre.Y, dz = from.Z - centre.Z;
        float len = System.MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        (dx, dy, dz) = len > 0.01f ? (dx / len, dy / len, dz / len) : (0f, 1f, 0f);
        float reach = SystemBodyLayout.LandApproachDistance(radius);
        point = new Vector3f(centre.X + (dx * reach), centre.Y + (dy * reach), centre.Z + (dz * reach));
        return true;
    }

    /// <summary>The trader's pad is reserved: it holds where it is, the clients draw the dive, and the pilots in this
    /// flight read who lands where. <see cref="MoveTraders"/> takes it out of the scene when the dive is over.</summary>
    private void BeginTraderDescent(SpaceInstance instance, NpcTrader t)
    {
        t.Phase = NpcTraderPhase.Descending;
        t.Target = t.Pos;
        t.DescentEndsAt = _uptime + TraderDescentSeconds;
        BroadcastToInstance(instance, new SpaceWarpFx
        {
            X = t.Pos.X,
            Y = t.Pos.Y,
            Z = t.Pos.Z,
            Arriving = false,
            Style = TraderDescentStyle,
            Subject = t.PoseId,
            Body = t.DestBodyId,
            Seconds = (float)TraderDescentSeconds,
        });

        string bodyName = BodyDisplayName(t.DestBodyId);
        foreach (var playerId in instance.Players)
        {
            if (FindSessionByPlayerId(playerId) is { } s)
            {
                Send(s, new ServerMessage
                {
                    Text = Localize(s.Locale, "srv.trader.descending").Replace("{name}", t.Name).Replace("{0}", bodyName),
                });
            }
        }
    }

    /// <summary>Test/inspection: the flight-scene targets of the traders in a player's instance — where each is heading
    /// and whether it is on its dive.</summary>
    public IReadOnlyList<(string DestBodyId, Vector3f Position, Vector3f Target, bool Descending)> TraderRoutesForTest(string playerId)
    {
        var list = new List<(string, Vector3f, Vector3f, bool)>();
        if (_playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var inst))
        {
            foreach (var t in inst.Traders)
            {
                list.Add((t.DestBodyId, t.Pos, t.Target, t.Phase == NpcTraderPhase.Descending));
            }
        }

        return list;
    }

    /// <summary>Test/inspection: where the server takes a body to be in a player's flight scene, and its drawn radius.</summary>
    public (Vector3f Centre, float Radius)? TraderBodyInSceneForTest(string playerId, string bodyId)
        => _playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var inst)
           && TraderBodyInScene(inst, bodyId, out var centre, out float radius)
            ? (centre, radius)
            : null;

    /// <summary>Test hook: a trader bound for <paramref name="bodyId"/> warps into the player's flight, as the ambient
    /// traffic would send it. False when the player is not flying or no trader could be coined.</summary>
    public bool SpawnLandingTraderForTest(string playerId, string bodyId)
    {
        if (!_playerInstance.TryGetValue(playerId, out var iid) || !_spaceInstances.TryGetValue(iid, out var inst))
        {
            return false;
        }

        int before = inst.Traders.Count;
        if (!SpawnTrader(inst) || inst.Traders.Count <= before)
        {
            return false;
        }

        var t = inst.Traders[^1];
        t.DestStationId = string.Empty;
        t.DestBodyId = bodyId;
        AimTraderAtBody(inst, t);
        return true;
    }

    /// <summary>Points a trader with a landing destination at that body's approach point; a body that is not in the scene
    /// keeps the inner waypoint it already has (it then passes through).</summary>
    private void AimTraderAtBody(SpaceInstance instance, NpcTrader t)
    {
        if (t.DestBodyId.Length > 0 && TraderApproachPoint(instance, t.DestBodyId, t.Pos, out var approach))
        {
            t.Target = approach;
            FaceTarget(t);
        }
    }
}
