// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;

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
        CruiseSpeed = 0.7f,
        BurstSpeed = 0.9f,
        Accel = 2.5f,
        TurnRate = 3.0f,
        HoldMin = 1.2f,
        HoldMax = 3.0f,
        PauseChance = 0.6f,
        PauseMin = 2.0f,
        PauseMax = 5.0f,
        WeaveAmp = 0.15f,
        WeaveFreq = 1.0f,
        VertAmp = 0f,
        VertFreq = 0f,
    };

    /// <summary>A settlement inhabitant. Lives only on the server; the client sees a <c>NetNpc</c>.</summary>
    internal sealed class ServerNpc
    {
        public int Id;
        public string Role = string.Empty;
        public string Theme = string.Empty;
        public string Settlement = string.Empty; // name of the settlement this NPC belongs to (memory/greeting key)
        public string NameKey = string.Empty;
        public string Name = string.Empty; // coined personal name (item 12)
        public Vector3f Home;
        public Vector3f Pos;
        public float Facing;
        public float Size;
        public uint SkinRgb;
        public uint OutfitRgb;
        public uint LegsRgb;
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

        // Every inhabited settlement on this world gets its own residents (ruins are abandoned).
        foreach (var settlement in _settlements)
        {
            if (settlement.Ruined)
            {
                continue;
            }

            // Each settlement has a deterministic trade profession (miners/traders/researchers/settlers) — it
            // drives the residents' outfits + work gestures AND which goods the vendor posts, so different
            // settlements offer different trades (the old per-NPC theme was the human/alien look).
            string settlementTheme = SettlementTradeFor(settlement.Name);
            int vendorIndex = 0;

            foreach (var (type, pos) in settlement.Markers)
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
                string npcTheme = role == "vendor" ? VendorThemeFor(settlement.Name, vendorIndex++, settlementTheme) : settlementTheme;
                bool robotic = npcTheme == "researchers" && rng.Next(100) < 60; // most research staff are service androids — but not all (#711)

                // NPCs have no physics, so place their feet on top of the floor block. Markers sit centred
                // in the air cell above the floor (+0.5 from the cell-centre conversion), so Floor() drops
                // the feet onto the floor surface — same fix as station crews. The Max keeps an authored
                // TEMPLATE marker's own storey (#480, was ST-8): an upper-floor vendor is not teleported to
                // the ground floor, but no NPC hovers half a block over it either (#711).
                var standing = new Vector3f(pos.X, (float)System.Math.Floor(System.Math.Max(settlement.Min.Y + 1f, pos.Y)), pos.Z);
                var npc = MakeNpc(role, npcTheme, robotic, standing, rng);
                npc.Settlement = settlement.Name;
                if (role == "quartermaster")
                {
                    npc.Name = CoinGiverName(settlement.Name); // the mission-giver's name matches its missions (item 13)
                }

                _npcs.Add(npc);
            }
        }

        if (_npcs.Count > 0)
        {
            _log.Info($"Spawned {_npcs.Count} NPCs across {_settlements.Count(s => !s.Ruined)} inhabited settlement(s).");
        }
    }

    private ServerNpc MakeNpc(string role, string theme, bool robotic, Vector3f home, System.Random rng)
    {
        // Human skin tones, deliberately SPREAD across the range rather than sampled from one tan band.
        // The old five (F2C9A0, D9A066, 8D5524, C68642, FFDBAC) had four neighbours in the same light-to-mid
        // tan, so a settlement full of NPCs read as one skin tone — a player reported exactly that ("sie
        // sollten wie Menschen 2 verschiedene Hautfarben haben") while the variety technically existed.
        // Ordered light → dark with visible gaps between steps so adjacent rolls are told apart at
        // gameplay distance, not just in a colour picker.
        uint[] skinTones =
        {
            0xFFE0C4, 0xF2C9A0, 0xE8B98A, 0xD9A066, 0xC68642,
            0xA9713A, 0x8D5524, 0x6B3F1E, 0x4A2A15,
        };
        // Android chassis tones — a small spread so robots aren't one stamped grey either (#711).
        uint[] chassisTones = { 0xBFC7CF, 0xD5DBE1, 0xA8B2BC, 0xC9CCB8 };
        // Six outfit tones per theme (was three), lifted out of the mud: the client multiplies these by the
        // greyscale suit texture, so anything authored dark lands near black on screen (#711).
        uint[] outfitByTheme = theme switch
        {
            "miners" => new uint[] { 0xD97B29, 0xA8ADB5, 0x8A6A45, 0xE0B23C, 0x6E7B8A, 0xB5651D },
            "traders" => new uint[] { 0x3D7EBF, 0x8A63BF, 0xD9AE33, 0x2FA48E, 0xC24B5A, 0x2E5E8C },
            "researchers" => new uint[] { 0xECECEC, 0x5FB6E0, 0xBFD7EA, 0x9AD9C0, 0xC9C2E8, 0xE8D9A0 },
            _ => new uint[] { 0x5C9950, 0xA37B4F, 0x7C9950, 0xB3A05C, 0x6B8FA3, 0x9C6B3C }, // settlers (default)
        };

        // Trousers are picked independently of the top, so two NPCs sharing a jacket colour still differ.
        uint[] legsTones = { 0x4A4E57, 0x5C5346, 0x3E4A5C, 0x6B5C4A, 0x777C85, 0x4E3D30 };

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
            Size = 0.92f + (float)rng.NextDouble() * 0.16f, // people vary a little (±8 %), not like fauna (#711)
            SkinRgb = robotic ? chassisTones[rng.Next(chassisTones.Length)] : skinTones[rng.Next(skinTones.Length)],
            OutfitRgb = outfitByTheme[rng.Next(outfitByTheme.Length)],
            LegsRgb = legsTones[rng.Next(legsTones.Length)],
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

            // Follow the REAL floor instead of freezing Y at spawn forever (#711): when the block column is
            // loaded and has a standable cell near the NPC, use it — so a doorstep lifts them and a mined-out
            // floor drops them instead of leaving them hanging in mid-air. Capped at ±2 blocks per step (a
            // strolling settler doesn't climb cliffs). When the column has no answer (chunk unloaded
            // server-side, someone walled the cell in, or only a far-off cell) fall back to the home marker's
            // floor Y — never the noise surface, which inside a stamped settlement can be metres off.
            int gx = (int)System.Math.Floor(res.Position.X), gz = (int)System.Math.Floor(res.Position.Z);
            int refY = (int)System.Math.Floor(npc.Pos.Y);
            float nextY = TryGroundFeetYAt(gx, gz, refY, out int feet) && System.Math.Abs(feet - refY) <= 2
                ? feet : npc.Home.Y;
            var next = new Vector3f(res.Position.X, nextY, res.Position.Z);

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

    /// <summary>True when nothing solid stands between <paramref name="from"/> and <paramref name="to"/> (both
    /// lifted to roughly eye height). Unlike <see cref="PathBlockedByWorld"/> this samples the segment in 3D —
    /// the Y axis included — so a player who drops into a cave or puts a wall/ridge between themselves and a
    /// hunter genuinely breaks the sightline. Used to gate aggression/damage on line-of-sight: an enemy can't
    /// bite (or keep chasing) a target it can't see. Sampled in the target's unwrapped frame so it stays correct
    /// across the longitude seam; <see cref="IsSolidCell"/> canonicalises each cell, so raw coords are fine.</summary>
    private bool HasLineOfSight(Vector3f from, Vector3f to)
    {
        const float eye = 1.5f; // sight originates near the head, not the feet, on both ends
        var dst = Unwrapped(from, to);
        float ax = from.X, ay = from.Y + eye, az = from.Z;
        float dx = dst.X - ax, dy = (dst.Y + eye) - ay, dz = dst.Z - az;
        float dist = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        int steps = System.Math.Max(1, (int)System.Math.Ceiling(dist / 0.25f));
        for (int s = 1; s < steps; s++) // skip both endpoints — the bodies themselves aren't occluders
        {
            float f = s / (float)steps;
            int x = (int)System.Math.Floor(ax + dx * f);
            int y = (int)System.Math.Floor(ay + dy * f);
            int z = (int)System.Math.Floor(az + dz * f);
            if (IsSightBlockingCell(x, y, z)) // fluids occlude like before water lost its Solid flag
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Test/util: expose the sightline check so the line-of-sight gating can be verified directly,
    /// without fighting the enemy/creature ground-snapping that would move a hand-placed combatant.</summary>
    public bool HasLineOfSightForTest(Vector3f from, Vector3f to) => HasLineOfSight(from, to);

    /// <summary>True if an NPC's body (feet + head) would sit inside a colliding block at this position — a wall,
    /// so it can't stroll there. A doorway opening stays air, so NPCs pass through doorways but not walls.
    /// Fluids block too (<c>fluidsPass: false</c>): settlement NPCs have no swim logic, so a pond must stay a
    /// wall to them even though a player swims straight in.</summary>
    private bool BlockedByWorld(Vector3f pos)
    {
        int x = (int)System.Math.Floor(pos.X);
        int y = (int)System.Math.Floor(pos.Y);
        int z = (int)System.Math.Floor(pos.Z);
        return IsCollidingCell(x, y, z)       // feet
            || IsCollidingCell(x, y + 1, z);  // head
    }

    /// <summary>A cell that blocks an NPC's SIGHT: solid (the plain flag — hiding in tall grass works, a
    /// meadow occludes), or a fluid. Water/lava lost their <c>Solid</c> flag (a submerged player must not
    /// count as entombed — see GameServerSpawnSafety), but a body of water must keep breaking the sightline
    /// exactly as before: no aggro through a lake.</summary>
    private bool IsSightBlockingCell(int x, int y, int z)
        => IsSolidCell(x, y, z) || IsFluid(_world.GetBlock(new Vector3i(x, y, z)).Value);

    /// <summary>Whether a cell is a movement-blocking solid block. Keyed on the block's <c>Solid</c> flag, not
    /// just "non-air", so the two are kept distinct: <b>glass</b> is solid-but-transparent (blocks NPCs, you see
    /// through it), while a non-solid transparent block like <b>water</b> is passable. Air is never solid.</summary>
    private bool IsSolidCell(int x, int y, int z) => IsSolidBlock(_world.GetBlock(new Vector3i(x, y, z)));

    private bool IsSolidBlock(BlockId id)
    {
        if (id.IsAir)
        {
            return false;
        }

        var def = _content.BlockById(id);
        return def == null || def.Solid; // unknown id → treat as solid (safe default)
    }

    /// <summary>Whether a cell actually <b>collides</b> with a walking body. <see cref="IsSolidCell"/> keys on
    /// the <c>Solid</c> flag alone, which defaults to <c>true</c> — so every cross-billboard prop (small flora,
    /// the torch/lantern, the walk-through ladder) counts as solid there even though the mesher gives it no
    /// collider and the player strolls straight through it. Movement must use this predicate instead, or a
    /// meadow would be an impassable wall for anything that isn't a player. Sight (<see cref="HasLineOfSight"/>)
    /// keeps the plain solid test.</summary>
    private bool IsCollidingCell(int x, int y, int z)
        => IsCollidingBlock(_world.GetBlock(new Vector3i(x, y, z)), fluidsPass: false, foliagePasses: false);

    /// <summary>The no-load sibling of <see cref="IsCollidingCell"/> used by the creature gates: an unloaded chunk
    /// reads as air (permissive, matching <c>StandableAt</c>), so a per-tick movement check never generates chunks
    /// as a side effect. Fluids never block an animal — swimmers live in them — and flying species additionally
    /// pass through tree canopies (their hover altitude sits right inside the crown on forest worlds).</summary>
    private bool IsCollidingCellIfLoaded(int x, int y, int z, bool foliagePasses)
        => IsCollidingBlock(_world.GetBlockIfLoaded(new Vector3i(x, y, z)), fluidsPass: true, foliagePasses);

    private bool IsCollidingBlock(BlockId id, bool fluidsPass, bool foliagePasses)
    {
        // Fluids are decided EXPLICITLY, not via the Solid flag: water is Solid=false in the content DB
        // (a submerged player must not read as entombed — see GameServerSpawnSafety), yet a pond must stay
        // a wall to a walking NPC (fluidsPass: false) while swimmers wade right through (fluidsPass: true).
        if (IsFluid(id.Value))
        {
            return !fluidsPass;
        }

        if (!IsSolidBlock(id))
        {
            return false;
        }

        var def = _content.BlockById(id);
        return def != null && !IsWalkThroughProp(def.Key, foliagePasses);
    }

    /// <summary>Blocks that are solid on paper but have <b>no collider</b> in the mesher, so bodies pass through
    /// them (mirrors the client's <c>PlayerController.IsCollidingKey</c>). <paramref name="foliagePasses"/> adds
    /// tree canopies for FLYING creatures — a winged animal weaves through a crown rather than bouncing off it.</summary>
    private static bool IsWalkThroughProp(string key, bool foliagePasses)
        => key.StartsWith("flora_", System.StringComparison.Ordinal)
            || key is "torch" or "lantern" or "ladder"
            || (foliagePasses && key == "tree_leaves");

    // NOTE: BroadcastNpcs runs on the 0.2 s position-sync cadence — per-receiver standings (#1118) must
    // NOT ride on it; they go out via SendNpcs (world entry) and explicitly when a relationship changes.
    private void BroadcastNpcs() => BroadcastToWorld(new NpcList { Npcs = _npcs.Select(ToNetNpc).ToArray() });

    private void SendNpcs(PlayerSession session)
    {
        Send(session, new NpcList { Npcs = _npcs.Select(ToNetNpc).ToArray() });
        SendNpcStandings(session); // #1118: the receiver's relationship stages for these NPCs
    }

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
        LegsRgb = n.LegsRgb,
        IsRobot = n.IsRobot,
    };
}
