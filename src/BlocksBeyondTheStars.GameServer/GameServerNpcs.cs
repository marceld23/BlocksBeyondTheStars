using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

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
    private const double NpcBroadcastInterval = 0.2;  // position-sync cadence (client interpolates between)
    private const float NpcWanderLeash = 1.6f;        // how far an NPC drifts from its home marker
    private const float NpcFaceRange = 6f;            // turn to face a player within this range
    private const double NpcMoveDtCap = 0.25;         // cap per-step movement so big ticks can't jump

    /// <summary>One uniform inhabitant gait for ALL settlement NPCs (no per-NPC variation): a slow stroll with
    /// frequent long pauses, so they stand around and potter rather than tracing an endless drift loop.</summary>
    private static readonly LocomotionProfile NpcProfile = new()
    {
        Style = LocomotionStyle.Grazer,
        CruiseSpeed = 0.7f, BurstSpeed = 0.9f, Accel = 2.5f, TurnRate = 3.0f,
        HoldMin = 1.2f, HoldMax = 3.0f,
        PauseChance = 0.6f, PauseMin = 2.0f, PauseMax = 5.0f,
        WeaveAmp = 0.15f, WeaveFreq = 1.0f, VertAmp = 0f, VertFreq = 0f,
    };

    /// <summary>A settlement inhabitant. Lives only on the server; the client sees a <c>NetNpc</c>.</summary>
    internal sealed class ServerNpc
    {
        public int Id;
        public string Role = string.Empty;
        public string Theme = string.Empty;
        public string NameKey = string.Empty;
        public string Name = string.Empty; // coined personal name (item 12)
        public Vector3f Home;
        public Vector3f Pos;
        public float Facing;
        public float Size;
        public uint SkinRgb;
        public uint OutfitRgb;
        public bool IsRobot;
        public double WanderPhase;
        public LocomotionState Loco; // stop-and-go loiter/stroll state
    }

    private List<ServerNpc> _npcs => _worlds.Active.Npcs;
    private double _npcBroadcastTimer { get => _worlds.Active.NpcBroadcastTimer; set => _worlds.Active.NpcBroadcastTimer = value; }
    private readonly List<PlayerSession> _npcTargets = new(); // reused per tick (no per-tick LINQ alloc)
    private int _nextNpcId { get => _worlds.Active.NextNpcId; set => _worlds.Active.NextNpcId = value; }

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

        // Each settlement has a deterministic trade profession (miners/traders/researchers/settlers) — it drives
        // the residents' outfits + work gestures AND which goods the vendor posts, so different settlements offer
        // different trades (the old per-NPC theme was the human/alien look, which the trade UI couldn't use).
        string settlementTheme = SettlementTradeFor(_settlementName);
        int vendorIndex = 0;

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

            // Vendors each get their own profession (B55) so multiple vendors at one settlement sell different
            // goods; settlers/the quartermaster keep the settlement's own theme (its identity).
            string npcTheme = role == "vendor" ? VendorThemeFor(_settlementName, vendorIndex++, settlementTheme) : settlementTheme;
            bool robotic = npcTheme == "researchers"; // research staff are service androids

            // NPCs have no physics, so place their feet exactly on top of the settlement floor block
            // (the marker Y sits inside it) — otherwise they render sunk into the ground.
            var standing = new Vector3f(pos.X, _settlementMin.Y + 1f, pos.Z);
            var npc = MakeNpc(role, npcTheme, robotic, standing, rng);
            if (role == "quartermaster")
            {
                npc.Name = CoinGiverName(_settlementName); // the mission-giver's name matches its missions (item 13)
            }

            _npcs.Add(npc);
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

        // A deterministic personal name from the same seeded rng (robots get a unit designation).
        string name = robotic ? BlocksBeyondTheStars.WorldGeneration.NameGenerator.Robot(rng) : BlocksBeyondTheStars.WorldGeneration.NameGenerator.Person(rng);

        return new ServerNpc
        {
            Id = _nextNpcId++,
            Role = role,
            Theme = theme,
            NameKey = nameKey,
            Name = name,
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

        // Reuse a field list instead of allocating a fresh Where(...).ToList() every tick (15 Hz).
        _npcTargets.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if ((!_shipPlaced || !s.State.AboardShip) && !InSpace(s.State.PlayerId))
            {
                _npcTargets.Add(s);
            }
        }

        var targets = _npcTargets;
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
            // Loiter ↔ stroll around home: stand a while, then potter to a new spot within the leash, then stand
            // again (instead of forever tracing one closed drift loop). Stray past the leash → head straight home.
            float hx = npc.Pos.X - npc.Home.X, hz = npc.Pos.Z - npc.Home.Z;
            bool beyondLeash = hx * hx + hz * hz > NpcWanderLeash * NpcWanderLeash;
            var intent = beyondLeash ? MoveMode.Seek : MoveMode.Roam;
            Vector3f? target = beyondLeash ? npc.Home : (Vector3f?)null;

            var res = LocomotionController.Step(npc.Loco, NpcProfile, npc.Pos, intent, target, moveDt, (uint)npc.Id);
            npc.Loco = res.State;
            var next = new Vector3f(res.Position.X, npc.Home.Y, res.Position.Z); // keep the flat settlement floor Y

            // NPCs don't wander into the player's ship — or through their building's walls/doors. The world
            // check sweeps the whole step (not just the endpoint) so an NPC can't tunnel through a one-block
            // wall or station glass pane when its wander arc clears it on the far side.
            if (!EntityBlockedByShip(next) && !PathBlockedByWorld(npc.Pos, next))
            {
                npc.Pos = next;
            }
            else
            {
                npc.Loco.ModeTimer = 0f; // blocked by a wall → re-pick a heading next tick rather than pushing in
            }

            // Face the nearest player if one is close; else face the way it's walking (and keep the last facing
            // while standing still, so a paused NPC doesn't snap back to a default heading).
            var nearest = NearestPlayerPosition(targets, npc.Pos);
            if (nearest is { } np && WrapDistSq(np, npc.Pos) <= NpcFaceRange * NpcFaceRange)
            {
                npc.Facing = (float)System.Math.Atan2(np.X - npc.Pos.X, np.Z - npc.Pos.Z);
            }
            else if (res.Moving)
            {
                // controller heading is math-convention (dirX=cos, dirZ=sin); NPC facing yaw is atan2(dirX, dirZ).
                npc.Facing = (float)System.Math.Atan2(System.Math.Cos(res.Facing), System.Math.Sin(res.Facing));
            }
        }
    }

    /// <summary>True if moving from <paramref name="from"/> to <paramref name="to"/> would pass through (or end
    /// inside) a solid block. Samples the segment every ~quarter block so an NPC can't tunnel through a one-block
    /// wall or glass pane in a single wander step (the endpoint alone could land in open air on the far side).</summary>
    private bool PathBlockedByWorld(Vector3f from, Vector3f to)
    {
        float dx = to.X - from.X, dz = to.Z - from.Z;
        float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
        int steps = System.Math.Max(1, (int)System.Math.Ceiling(dist / 0.25f));
        for (int s = 1; s <= steps; s++)
        {
            float f = s / (float)steps;
            if (BlockedByWorld(new Vector3f(from.X + dx * f, to.Y, from.Z + dz * f)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if an NPC's body (feet + head) would sit inside a solid block at this position — a wall,
    /// so it can't stroll there. A doorway opening stays air, so NPCs pass through doorways but not walls.</summary>
    private bool BlockedByWorld(Vector3f pos)
    {
        int x = (int)System.Math.Floor(pos.X);
        int y = (int)System.Math.Floor(pos.Y);
        int z = (int)System.Math.Floor(pos.Z);
        return IsSolidCell(x, y, z)       // feet
            || IsSolidCell(x, y + 1, z);  // head
    }

    /// <summary>Whether a cell is a movement-blocking solid block. Keyed on the block's <c>Solid</c> flag, not
    /// just "non-air", so the two are kept distinct: <b>glass</b> is solid-but-transparent (blocks NPCs, you see
    /// through it), while a non-solid transparent block like <b>water</b> is passable. Air is never solid.</summary>
    private bool IsSolidCell(int x, int y, int z)
    {
        var id = _world.GetBlock(new Vector3i(x, y, z));
        if (id.IsAir)
        {
            return false;
        }

        var def = _content.BlockById(id);
        return def == null || def.Solid; // unknown id → treat as solid (safe default)
    }

    private void BroadcastNpcs() => BroadcastToWorld(new NpcList { Npcs = _npcs.Select(ToNetNpc).ToArray() });

    private void SendNpcs(PlayerSession session)
        => Send(session, new NpcList { Npcs = _npcs.Select(ToNetNpc).ToArray() });

    private static NetNpc ToNetNpc(ServerNpc n) => new()
    {
        Id = n.Id,
        Role = n.Role,
        Theme = n.Theme,
        NameKey = n.NameKey,
        Name = n.Name,
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
