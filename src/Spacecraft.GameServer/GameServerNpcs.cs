using System.Collections.Generic;
using System.Linq;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Geometry;

namespace Spacecraft.GameServer;

/// <summary>
/// Settlement NPCs — humanoid inhabitants stationed at an inhabited settlement's interaction markers
/// (a vendor at the market stall, a quartermaster at the mission board, settlers around town). They
/// are server-authoritative, non-hostile, and never damage the player: they idle and gently wander
/// within a short leash of their home marker, turning to face a nearby player. The client renders
/// them with its avatar renderer from the <see cref="NetNpc"/> projection. Ruined settlements are
/// abandoned and get no NPCs (only scavengeable loot caches).
/// </summary>
public sealed partial class GameServer
{
    private const double NpcBroadcastInterval = 0.5;  // position-sync cadence (client interpolates)
    private const float NpcWanderLeash = 2.5f;        // how far an NPC drifts from its home marker
    private const double NpcWanderSpeed = 0.4;        // idle-stroll angular speed
    private const float NpcFaceRange = 6f;            // turn to face a player within this range
    private const double NpcMoveDtCap = 0.25;         // cap per-step movement so big ticks can't jump

    /// <summary>A settlement inhabitant. Lives only on the server; the client sees a <c>NetNpc</c>.</summary>
    internal sealed class ServerNpc
    {
        public int Id;
        public string Role = string.Empty;
        public string Theme = string.Empty;
        public string NameKey = string.Empty;
        public Vector3f Home;
        public Vector3f Pos;
        public float Facing;
        public float Size;
        public uint SkinRgb;
        public uint OutfitRgb;
        public bool IsRobot;
        public double WanderPhase;
    }

    private List<ServerNpc> _npcs => _worlds.Active.Npcs;
    private double _npcBroadcastTimer;
    private int _nextNpcId = 1;

    /// <summary>Read-only view of live settlement NPCs (id/role/current/home) for tests + inspection.</summary>
    public IReadOnlyList<(int Id, string Role, Vector3f Pos, Vector3f Home)> NpcSnapshots
        => _npcs.Select(n => (n.Id, n.Role, n.Pos, n.Home)).ToList();

    /// <summary>Number of NPCs currently populating the world's settlement.</summary>
    public int NpcCount => _npcs.Count;

    /// <summary>
    /// Populates an inhabited settlement with NPCs from its markers: a vendor at the market, a
    /// quartermaster at the mission board, and a settler at each npc spawn marker. Deterministic from
    /// the settlement's seeded RNG so the same world always has the same residents. No-op for ruins.
    /// </summary>
    private void SpawnSettlementNpcs(System.Random rng)
    {
        _npcs.Clear();
        _npcBroadcastTimer = 0;
        _nextNpcId = 1;

        if (!_settlementStamped || _settlementRuined)
        {
            return; // abandoned ruins have no inhabitants
        }

        string theme = string.IsNullOrEmpty(_settlementInhabitant) ? "settlers" : _settlementInhabitant;
        bool robotic = theme == "researchers"; // research outposts are staffed by service androids

        foreach (var (type, pos) in _settlementMarkers)
        {
            string? role = type switch
            {
                "vendor" => "vendor",
                "mission_board" => "quartermaster",
                "npc" => "settler",
                _ => null,
            };

            if (role is null)
            {
                continue; // loot markers etc. don't get an NPC
            }

            _npcs.Add(MakeNpc(role, theme, robotic, pos, rng));
        }

        _log.Info($"Spawned {_npcs.Count} NPCs at settlement '{_settlementName}'.");
    }

    private ServerNpc MakeNpc(string role, string theme, bool robotic, Vector3f home, System.Random rng)
    {
        uint[] skinTones = { 0xF2C9A0, 0xD9A066, 0x8D5524, 0xC68642, 0xFFDBAC };
        uint[] outfitByTheme = theme switch
        {
            "miners" => new uint[] { 0xB5651D, 0x808080, 0x5A4632 },
            "traders" => new uint[] { 0x2E5E8C, 0x6A4C93, 0xC9A227 },
            "researchers" => new uint[] { 0xECECEC, 0x4FA1C9, 0xBFD7EA },
            _ => new uint[] { 0x3F6B3F, 0x7A5C3C, 0x556B2F }, // settlers (default)
        };

        string nameKey = role switch
        {
            "vendor" => "npc.role.vendor",
            "quartermaster" => "npc.role.quartermaster",
            _ => $"npc.theme.{theme}",
        };

        return new ServerNpc
        {
            Id = _nextNpcId++,
            Role = role,
            Theme = theme,
            NameKey = nameKey,
            Home = home,
            Pos = home,
            Facing = (float)(rng.NextDouble() * System.Math.PI * 2),
            Size = 1f,
            SkinRgb = robotic ? 0xBFC7CFu : skinTones[rng.Next(skinTones.Length)],
            OutfitRgb = outfitByTheme[rng.Next(outfitByTheme.Length)],
            IsRobot = robotic,
            WanderPhase = rng.NextDouble() * System.Math.PI * 2,
        };
    }

    private void TickNpcs(double dt)
    {
        if (_npcs.Count == 0)
        {
            return;
        }

        var targets = _sessions.Values
            .Where(s => s.Joined && (!_shipStamped || !s.State.AboardShip) && !InSpace(s.State.PlayerId))
            .ToList();

        if (targets.Count == 0)
        {
            return; // nobody on the surface — freeze NPCs (no need to sim/broadcast)
        }

        MoveNpcs(targets, dt);

        _npcBroadcastTimer += dt;
        if (_npcBroadcastTimer >= NpcBroadcastInterval)
        {
            _npcBroadcastTimer = 0;
            BroadcastNpcs();
        }
    }

    private void MoveNpcs(List<PlayerSession> targets, double dt)
    {
        double moveDt = System.Math.Min(dt, NpcMoveDtCap);
        foreach (var npc in _npcs)
        {
            // Gentle stroll around home: a small Lissajous drift bounded by the leash.
            npc.WanderPhase += moveDt * NpcWanderSpeed;
            float ox = (float)System.Math.Cos(npc.WanderPhase) * NpcWanderLeash;
            float oz = (float)System.Math.Sin(npc.WanderPhase * 0.7) * NpcWanderLeash;
            var next = new Vector3f(npc.Home.X + ox, npc.Home.Y, npc.Home.Z + oz);

            // NPCs don't wander into the player's ship.
            npc.Pos = EntityBlockedByShip(next) ? npc.Pos : next;

            // Face the nearest player if one is close, else look along the stroll heading.
            var nearest = NearestPlayerPosition(targets, npc.Pos);
            if (nearest is { } np && np.DistanceSquared(npc.Pos) <= NpcFaceRange * NpcFaceRange)
            {
                npc.Facing = (float)System.Math.Atan2(np.X - npc.Pos.X, np.Z - npc.Pos.Z);
            }
            else
            {
                npc.Facing = (float)npc.WanderPhase;
            }
        }
    }

    private void BroadcastNpcs() => Broadcast(new NpcList { Npcs = _npcs.Select(ToNetNpc).ToArray() });

    private void SendNpcs(PlayerSession session)
        => Send(session, new NpcList { Npcs = _npcs.Select(ToNetNpc).ToArray() });

    private static NetNpc ToNetNpc(ServerNpc n) => new()
    {
        Id = n.Id,
        Role = n.Role,
        Theme = n.Theme,
        NameKey = n.NameKey,
        X = n.Pos.X,
        Y = n.Pos.Y,
        Z = n.Pos.Z,
        Facing = n.Facing,
        Size = n.Size,
        SkinRgb = n.SkinRgb,
        OutfitRgb = n.OutfitRgb,
        IsRobot = n.IsRobot,
    };
}
