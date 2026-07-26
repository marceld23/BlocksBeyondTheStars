// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Bandits on foot: rare lone robbers that hold the player up (hand over part of the inventory or
/// fight) and the guards of stamped bandit camps. Bandits are humans, not Guardian machines — they
/// ride the planet-enemy wire (PlanetEnemyList) so targeting/combat work unchanged, but they never
/// advance the machine story and they keep their own list, cap and rules gate.
///
/// The hold-up script (lone robbers + bandit ships share it): a bandit spawns NON-hostile, walks up
/// to its mark, and — only if the mark actually carries something worth taking — sends a
/// <see cref="BanditDemand"/> (~35 % of the 1–2 largest non-tool stacks). Comply and it takes the
/// goods and wanders off for good; refuse, stall past the deadline, or attack it, and it turns
/// hostile. A player with empty pockets is not worth the trouble and is left alone.
/// </summary>
public sealed partial class GameServer
{
    private const float BanditDemandRange = 7f;        // close enough to talk — the demand opens here
    private const double BanditDemandTimeout = 25.0;   // seconds to answer; silence counts as a refusal
    private const float BanditMeleeRange = 4f;         // blade bandit damage aura (matches machine proximity)
    private const float BanditGunRange = 8f;           // gunner damage aura — longer, but cover still blocks it (LOS)
    private const float BanditMeleeDps = 5f;
    private const float BanditGunDps = 3f;
    private const float BanditHull = 35f;
    private const float BanditGiveUpRange = 45f;       // a fleeing mark further than this for a while shakes the bandit
    private const double BanditGiveUpSeconds = 8.0;
    private const double BanditApproachPatience = 60.0; // seconds a bandit keeps walking toward an unreachable mark
    private const float BanditDespawnRange = 60f;      // a Leaving bandit this far from everyone just vanishes
    private const double BanditLeaveSeconds = 20.0;    // ...or after this long walking away
    private const int LoneBanditWorldCap = 2;          // lone robbers per world (camp guards are separate)
    private const float BanditCampLeash = 16f;         // camp guards never stray further than this from camp

    private List<CombatEntity> _bandits => _worlds.Active.Bandits;
    private List<BanditCampInstance> _banditCamps => _worlds.Active.BanditCamps;
    private double _banditSyncTimer { get => _worlds.Active.SinceBanditSync; set => _worlds.Active.SinceBanditSync = value; }
    private readonly System.Random _banditRng = new();
    private int _nextBanditDemandId = 1;
    private readonly List<PlayerSession> _banditTargets = new();   // reused per tick (no per-tick LINQ alloc)
    private readonly List<CombatEntity> _banditRemovals = new();   // reused per tick

    /// <summary>Bandits currently active on the surface (lone robbers + camp guards). Test seam.</summary>
    public IReadOnlyList<CombatEntity> Bandits => _bandits;

    /// <summary>Whether bandits may exist at all given the active rules. Unlike the Guardian machines they
    /// are people, so pacifying the Guardian core does NOT retire them — only the rule slider does.</summary>
    private bool BanditsActive => Rules.Bandits != AlienActivity.Off && Rules.GameMode == GameMode.Survival;

    /// <summary>Deterministic per-body bucket 0..99 — the same body always rolls the same bandit presence,
    /// like the trader-traffic buckets, so some worlds are simply bandit country and some are safe.</summary>
    private int BanditPresence(string locationId)
        => (int)(((ulong)(_meta.Seed ^ WorldGenerator.StableHash("bandits:" + locationId))) % 100);

    /// <summary>Whether lone robbers roam THIS world (seeded presence gate scaled by the rule slider).</summary>
    private bool LoneBanditsOnThisWorld => BanditPresence(_world.LocationId) < Rules.Bandits switch
    {
        AlienActivity.Rare => 30,
        AlienActivity.Normal => 55,
        AlienActivity.Frequent => 80,
        AlienActivity.Extreme => 101,
        _ => 0,
    };

    private void TickBandits(double dt)
    {
        if (!BanditsActive || _world.Planet.Void)
        {
            return;
        }

        // Eligible marks: joined players on this surface, on foot (spectating fleet admins leave no footprint).
        _banditTargets.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.State.AboardShip && !InSpace(s.State.PlayerId) && !s.Spectating)
            {
                _banditTargets.Add(s);
            }
        }

        var targets = _banditTargets;

        // Deadline enforcement (respawn-choice pattern): an unanswered ground demand counts as a refusal.
        foreach (var s in targets)
        {
            if (s.BanditDemandId != 0 && !s.BanditDemandFromShip && _uptime > s.BanditDemandDeadline)
            {
                var robber = _bandits.FirstOrDefault(b => b.Id == s.BanditDemandBanditId);
                if (robber is not null)
                {
                    ResolveBanditDemand(s, robber, comply: false, outcome: "expired");
                }
                else
                {
                    ClearBanditDemand(s);
                }
            }
        }

        // VEGA pre-briefing (once per world per session): warn about bandit country BEFORE anyone is
        // approached — a camp on the scanner or robber reports both qualify.
        bool hasCamps = false;
        foreach (var camp in _banditCamps)
        {
            if (!camp.Cleared) { hasCamps = true; break; }
        }

        if (hasCamps || LoneBanditsOnThisWorld)
        {
            foreach (var s in targets)
            {
                ShipAiBanditGroundWarning(s, hasCamps);
            }
        }

        if (targets.Count > 0 && LoneBanditsOnThisWorld)
        {
            TrySpawnLoneBandit(targets);
        }

        bool moved = false;
        bool changed = false;
        _banditRemovals.Clear();
        foreach (var bandit in _bandits)
        {
            moved |= MoveBandit(bandit, targets, dt, ref changed);

            // Damage aura: only a hostile bandit hurts, only in range, and only with a clear line of
            // sight — ducking behind cover protects from the gunner too.
            if (!bandit.Hostile)
            {
                continue;
            }

            float range = bandit.Kind == CombatEntityKind.BanditGunner ? BanditGunRange : BanditMeleeRange;
            float dps = bandit.Kind == CombatEntityKind.BanditGunner ? BanditGunDps : BanditMeleeDps;
            foreach (var session in targets)
            {
                var p = session.State;
                if (p.GodMode || p.Stealthed)
                {
                    continue;
                }

                if (WrapDistSq(p.Position, bandit.Position) <= range * range
                    && HasLineOfSight(bandit.Position, p.Position))
                {
                    p.Health = System.Math.Max(0f, p.Health - Mitigate(p, (float)(dps * dt)));
                    SendPlayerState(session);
                    if (p.Health <= 0f)
                    {
                        RespawnPlayer(session, "Beaten by a bandit — recovery to the Medbay heal-tank.");
                    }
                }
            }
        }

        foreach (var gone in _banditRemovals)
        {
            _bandits.Remove(gone);
            BroadcastToWorld(new PlanetEnemyDefeated { Id = gone.Id });
            changed = true;
        }

        _banditSyncTimer += dt;
        if (changed || (moved && _banditSyncTimer >= 0.2))
        {
            _banditSyncTimer = 0;
            BroadcastPlanetEnemies();
        }
    }

    /// <summary>Per-player paced lone-robber spawner: each player rolls at most one ambush attempt per long
    /// cooldown window, so hold-ups stay rare events. The first window of a session is pure grace.</summary>
    private void TrySpawnLoneBandit(List<PlayerSession> targets)
    {
        int loneCount = 0;
        foreach (var b in _bandits)
        {
            if (b.CampKey.Length == 0) { loneCount++; }
        }

        if (loneCount >= LoneBanditWorldCap)
        {
            return;
        }

        double cooldown = Rules.Bandits switch
        {
            AlienActivity.Rare => 900.0,
            AlienActivity.Normal => 600.0,
            AlienActivity.Frequent => 360.0,
            AlienActivity.Extreme => 240.0,
            _ => 900.0,
        };

        foreach (var session in targets)
        {
            var p = session.State;
            if (session.NextBanditAmbushAt <= 0.0)
            {
                // Fresh session: arm the first window instead of ambushing a player who just logged in.
                session.NextBanditAmbushAt = _uptime + cooldown * (0.5 + _banditRng.NextDouble() * 0.5);
                continue;
            }

            if (_uptime < session.NextBanditAmbushAt || p.GodMode || p.Stealthed)
            {
                continue;
            }

            session.NextBanditAmbushAt = _uptime + cooldown * (0.8 + _banditRng.NextDouble() * 0.4);
            if (_banditRng.NextDouble() > 0.6)
            {
                continue; // this window stays quiet — not every roll produces a robber
            }

            bool alreadyStalked = false;
            foreach (var b in _bandits)
            {
                if (b.BanditTargetId == p.PlayerId) { alreadyStalked = true; break; }
            }

            if (alreadyStalked)
            {
                continue;
            }

            SpawnLoneBanditNear(p);
            BroadcastPlanetEnemies();
            return; // at most one new robber per tick
        }
    }

    private void SpawnLoneBanditNear(Shared.State.PlayerState player)
    {
        // Same placement idea as the machine spawner: outside immediate view, on the surface.
        double ang = _banditRng.NextDouble() * System.Math.PI * 2.0;
        float dist = 35f + (float)(_banditRng.NextDouble() * 15.0);
        int ex = (int)System.Math.Round(player.Position.X + System.Math.Cos(ang) * dist);
        int ez = (int)System.Math.Round(player.Position.Z + System.Math.Sin(ang) * dist);
        int ey = _generator.SurfaceHeight(_world.Planet, ex, ez) + 1;

        bool gunner = _banditRng.NextDouble() < 0.4;
        var bandit = new CombatEntity
        {
            Id = NextEntityId(),
            Kind = gunner ? CombatEntityKind.BanditGunner : CombatEntityKind.Bandit,
            Name = NameGenerator.Person(_banditRng),
            Hostile = false, // talks first — hostility is earned
            Hull = BanditHull,
            HullMax = BanditHull,
            Position = new Vector3f(ex, ey, ez),
            DamagePerSecond = gunner ? BanditGunDps : BanditMeleeDps,
            BanditPhase = BanditPhase.Approach,
            BanditTargetId = player.PlayerId,
            Loot = { new ItemAmount("iron_plate", 2) },
        };
        if (_banditRng.NextDouble() < 0.3)
        {
            bandit.Loot.Add(new ItemAmount("gold_ingot", 1)); // some robbers carry earlier spoils
        }

        _bandits.Add(bandit);
    }

    /// <summary>Spawns the guards of a stamped bandit camp from its markers (called by the camp stamper —
    /// never for cleared camps). Guards are hostile from the start, leash to the camp and never respawn.</summary>
    private void SpawnBanditCampGuards(BanditCampInstance camp, System.Random rng)
    {
        foreach (var (type, pos) in camp.Markers)
        {
            if (type != "bandit")
            {
                continue;
            }

            bool gunner = rng.NextDouble() < 0.5;
            _bandits.Add(new CombatEntity
            {
                Id = NextEntityId(),
                Kind = gunner ? CombatEntityKind.BanditGunner : CombatEntityKind.Bandit,
                Name = NameGenerator.Person(rng),
                Hostile = true,
                Hull = BanditHull,
                HullMax = BanditHull,
                Position = pos,
                DamagePerSecond = gunner ? BanditGunDps : BanditMeleeDps,
                BanditPhase = BanditPhase.None,
                CampKey = camp.Key,
                PatrolInitialized = true,
                PatrolCenter = camp.Center,
                Loot = { new ItemAmount("iron_plate", 2), new ItemAmount("carbon", 2) },
            });
        }
    }

    private static readonly LocomotionProfile BanditProfile = new()
    {
        Style = LocomotionStyle.Prowler,
        CruiseSpeed = 1.2f,
        BurstSpeed = 3.3f, // slightly slower than a running player — you can outrun a robber
        Accel = 3.4f,
        TurnRate = 2.2f,
        HoldMin = 2.0f,
        HoldMax = 5.0f,
        PauseChance = 0.35f,
        PauseMin = 0.8f,
        PauseMax = 2.0f,
        WeaveAmp = 0.1f,
        WeaveFreq = 0.8f,
        VertAmp = 0f,
        VertFreq = 0f,
    };

    /// <summary>Drives one bandit through its hold-up script. Sets <paramref name="changed"/> when a phase
    /// flip needs an immediate broadcast; returns whether the bandit physically moved.</summary>
    private bool MoveBandit(CombatEntity bandit, List<PlayerSession> targets, double dt, ref bool changed)
    {
        if (TryPushOutsideShip(bandit.Position, out var ejected))
        {
            bandit.Position = ejected;
            return false;
        }

        MoveMode intent = MoveMode.Roam;
        Vector3f? target = null;

        if (bandit.CampKey.Length > 0)
        {
            // Camp guard: chase a visible player near the camp, otherwise fall back toward the camp.
            (intent, target) = CampGuardIntent(bandit, targets);
        }
        else
        {
            switch (bandit.BanditPhase)
            {
                case BanditPhase.Approach:
                    {
                        var mark = FindSessionByPlayerId(bandit.BanditTargetId);
                        if (!MarkStillRobbable(mark))
                        {
                            BeginBanditLeave(bandit);
                            changed = true;
                            break;
                        }

                        double distSq = WrapDistSq(mark!.State.Position, bandit.Position);
                        bandit.ChaseTimer += dt;
                        if (distSq > 80f * 80f || bandit.ChaseTimer > BanditApproachPatience)
                        {
                            BeginBanditLeave(bandit); // the mark got away — not worth the walk
                            changed = true;
                            break;
                        }

                        if (distSq <= BanditDemandRange * BanditDemandRange
                            && HasLineOfSight(bandit.Position, mark.State.Position))
                        {
                            BeginBanditDemand(mark, bandit);
                            changed = true;
                            break;
                        }

                        intent = MoveMode.Seek;
                        target = Unwrapped(bandit.Position, mark.State.Position);
                        break;
                    }

                case BanditPhase.Demanding:
                    {
                        var mark = FindSessionByPlayerId(bandit.BanditTargetId);
                        if (!MarkStillRobbable(mark))
                        {
                            // The mark vanished mid-hold-up (left the world / boarded the ship): call it off.
                            if (mark is not null && mark.BanditDemandBanditId == bandit.Id)
                            {
                                SendBanditResult(mark, "fled");
                                ClearBanditDemand(mark);
                            }

                            BeginBanditLeave(bandit);
                            changed = true;
                        }

                        return false; // stands its ground while the ultimatum runs
                    }

                case BanditPhase.Fighting:
                    {
                        var prey = FindSessionByPlayerId(bandit.BanditTargetId);
                        if (prey is null || prey.CurrentLocationId != _world.LocationId || InSpace(prey.State.PlayerId)
                            || prey.State.AboardShip || prey.State.Stealthed)
                        {
                            prey = NearestBanditPrey(bandit, targets);
                        }

                        if (prey is null)
                        {
                            BeginBanditLeave(bandit);
                            changed = true;
                            break;
                        }

                        double distSq = WrapDistSq(prey.State.Position, bandit.Position);
                        if (distSq > BanditGiveUpRange * BanditGiveUpRange)
                        {
                            bandit.GiveUpTimer += dt;
                            if (bandit.GiveUpTimer > BanditGiveUpSeconds)
                            {
                                BeginBanditLeave(bandit); // you outran it
                                changed = true;
                                break;
                            }
                        }
                        else
                        {
                            bandit.GiveUpTimer = 0;
                        }

                        if (distSq <= EnemyStopRange * EnemyStopRange)
                        {
                            return false; // in aura range — hold (the aura does the fighting)
                        }

                        intent = MoveMode.Seek;
                        target = Unwrapped(bandit.Position, prey.State.Position);
                        break;
                    }

                case BanditPhase.Leaving:
                    {
                        bandit.GiveUpTimer += dt;
                        double nearestSq = double.MaxValue;
                        Vector3f? nearestPos = null;
                        foreach (var s in targets)
                        {
                            double sq = WrapDistSq(s.State.Position, bandit.Position);
                            if (sq < nearestSq)
                            {
                                nearestSq = sq;
                                nearestPos = s.State.Position;
                            }
                        }

                        if (bandit.GiveUpTimer > BanditLeaveSeconds || nearestPos is null
                            || nearestSq > BanditDespawnRange * BanditDespawnRange)
                        {
                            _banditRemovals.Add(bandit);
                            return false;
                        }

                        // Walk directly away from the nearest player until far enough to vanish.
                        var away = Unwrapped(bandit.Position, nearestPos.Value);
                        target = new Vector3f(
                            bandit.Position.X + (bandit.Position.X - away.X),
                            bandit.Position.Y,
                            bandit.Position.Z + (bandit.Position.Z - away.Z));
                        intent = MoveMode.Seek;
                        break;
                    }
            }
        }

        if (bandit.BanditPhase == BanditPhase.Demanding)
        {
            return false;
        }

        var res = LocomotionController.Step(bandit.Loco, BanditProfile, bandit.Position, intent, target, dt, (uint)StableStringHash(bandit.Id));
        bandit.Loco = res.State;

        float nx = (float)WorldConstants.WrapX(res.Position.X, _world.Circumference);
        float nz = (float)WorldConstants.WrapZ(res.Position.Z, _world.Circumference);
        int prevGround = _generator.SurfaceHeight(_world.Planet, (int)System.Math.Floor(bandit.Position.X), (int)System.Math.Floor(bandit.Position.Z)) + 1;
        int groundY = _generator.SurfaceHeight(_world.Planet, (int)System.Math.Floor(nx), (int)System.Math.Floor(nz)) + 1;
        if (System.Math.Abs(groundY - prevGround) > 3)
        {
            bandit.Loco.ModeTimer = 0f; // cliff in the way — pick a new heading next tick
            return false;
        }

        var candidate = new Vector3f(nx, groundY, nz);
        if (EntityBlockedByShip(candidate) || BlockedByEnergyFence(bandit.Position, candidate))
        {
            bandit.Loco.ModeTimer = 0f;
            return false;
        }

        bandit.Position = candidate;
        return res.Moving;
    }

    private (MoveMode, Vector3f?) CampGuardIntent(CombatEntity guard, List<PlayerSession> targets)
    {
        // Outside the leash? Head home first — guards never chase across the map.
        var home = Unwrapped(guard.Position, guard.PatrolCenter);
        float hdx = home.X - guard.Position.X;
        float hdz = home.Z - guard.Position.Z;
        if (hdx * hdx + hdz * hdz > BanditCampLeash * BanditCampLeash)
        {
            return (MoveMode.Seek, home);
        }

        PlayerSession? nearest = null;
        double bestSq = (double)EnemyHuntRange * EnemyHuntRange;
        foreach (var s in targets)
        {
            if (s.State.GodMode || s.State.Stealthed)
            {
                continue;
            }

            // Only prey that is itself near the camp — the leash caps how far a chase can drift.
            var pAtCamp = Unwrapped(guard.PatrolCenter, s.State.Position);
            float cdx = pAtCamp.X - guard.PatrolCenter.X;
            float cdz = pAtCamp.Z - guard.PatrolCenter.Z;
            if (cdx * cdx + cdz * cdz > (BanditCampLeash + 12f) * (BanditCampLeash + 12f))
            {
                continue;
            }

            double sq = WrapDistSq(s.State.Position, guard.Position);
            if (sq < bestSq && HasLineOfSight(guard.Position, s.State.Position))
            {
                bestSq = sq;
                nearest = s;
            }
        }

        if (nearest is null)
        {
            return (MoveMode.Roam, null);
        }

        if (bestSq <= EnemyStopRange * EnemyStopRange)
        {
            return (MoveMode.Roam, null); // point-blank: the aura fights, no need to shove
        }

        return (MoveMode.Seek, Unwrapped(guard.Position, nearest.State.Position));
    }

    /// <summary>A mark can be robbed while it is present in this world, on foot and visible. Boarding the
    /// ship, cloaking, god mode or leaving the world all end the approach.</summary>
    private bool MarkStillRobbable(PlayerSession? mark)
        => mark is not null
           && mark.Joined
           && mark.CurrentLocationId == _world.LocationId
           && !mark.State.AboardShip
           && !InSpace(mark.State.PlayerId)
           && !mark.State.Stealthed
           && !mark.State.GodMode
           && !mark.Spectating;

    private PlayerSession? NearestBanditPrey(CombatEntity bandit, List<PlayerSession> targets)
    {
        PlayerSession? nearest = null;
        double bestSq = (double)EnemyHuntRange * EnemyHuntRange;
        foreach (var s in targets)
        {
            if (s.State.GodMode || s.State.Stealthed)
            {
                continue;
            }

            double sq = WrapDistSq(s.State.Position, bandit.Position);
            if (sq < bestSq)
            {
                bestSq = sq;
                nearest = s;
            }
        }

        return nearest;
    }

    private void BeginBanditLeave(CombatEntity bandit)
    {
        bandit.BanditPhase = BanditPhase.Leaving;
        bandit.BanditTargetId = string.Empty;
        bandit.GiveUpTimer = 0;
        bandit.Hostile = false;
    }

    // ---------------- The demand protocol ----------------

    /// <summary>The demand share taken from the mark's largest stacks (decided: ~35 %).</summary>
    private const double BanditDemandShare = 0.35;
    private const int BanditDemandKinds = 2;

    /// <summary>Builds a bandit demand from the player's goods: ~35 % (min 1) of the 1–2 largest non-tool
    /// stacks, aggregated per item kind. Tools/weapons are never demanded (death-salvage rule). Empty result
    /// = not worth robbing. <paramref name="includeCargo"/> adds the ship hold (space hold-ups only).</summary>
    private List<ItemAmount> BuildBanditDemand(Shared.State.PlayerState player, bool includeCargo)
    {
        var counts = new Dictionary<string, int>();
        void Scan(Shared.State.Inventory inv)
        {
            for (int i = 0; i < inv.SlotCount; i++)
            {
                if (inv.Slots[i] is { } stack && !stack.IsEmpty)
                {
                    var def = _content.GetItem(stack.Item);
                    if (def is { Category: ItemCategory.Tool })
                    {
                        continue; // tools are never demanded — losing your only pickaxe isn't fun, it's a wall
                    }

                    counts[stack.Item] = counts.TryGetValue(stack.Item, out var c) ? c + stack.Count : stack.Count;
                }
            }
        }

        Scan(player.Inventory);
        if (includeCargo)
        {
            Scan(_ship.Cargo);
        }

        var demand = new List<ItemAmount>();
        foreach (var pick in counts.OrderByDescending(kv => kv.Value).Take(BanditDemandKinds))
        {
            int amount = System.Math.Max(1, (int)System.Math.Ceiling(pick.Value * BanditDemandShare));
            demand.Add(new ItemAmount(pick.Key, amount));
        }

        return demand;
    }

    private void BeginBanditDemand(PlayerSession mark, CombatEntity bandit)
    {
        if (mark.BanditDemandId != 0)
        {
            return; // some other hold-up is already on this player's screen — hold position and retry next tick
        }

        var demand = BuildBanditDemand(mark.State, includeCargo: false);
        if (demand.Count == 0)
        {
            // Empty pockets: not worth the trouble — the robber sizes the mark up and wanders off.
            BeginBanditLeave(bandit);
            return;
        }

        bandit.BanditPhase = BanditPhase.Demanding;
        StartBanditDemand(mark, bandit.Id, demand, fromShip: false, banditName: bandit.Name,
            lineKey: "bandit.line.holdup" + (System.Math.Abs(StableStringHash(bandit.Id)) % 3 + 1));
    }

    /// <summary>Arms the per-player demand state and sends the hold-up to the client. Shared by ground
    /// robbers and bandit ships (the caller owns the bandit-side phase).</summary>
    private void StartBanditDemand(PlayerSession mark, string banditId, List<ItemAmount> demand, bool fromShip,
        string banditName, string lineKey)
    {
        mark.BanditDemandId = _nextBanditDemandId++;
        mark.BanditDemandBanditId = banditId;
        mark.BanditDemandDeadline = _uptime + BanditDemandTimeout;
        mark.BanditDemandFromShip = fromShip;
        mark.BanditDemandItems.Clear();
        mark.BanditDemandItems.AddRange(demand);

        Send(mark, new BanditDemand
        {
            DemandId = mark.BanditDemandId,
            BanditId = banditId,
            Source = fromShip ? "ship" : "foot",
            BanditName = banditName,
            LineKey = lineKey,
            Demanded = demand.Select(d => new NetTradeItem { Item = d.Item, Count = d.Count }).ToArray(),
            SecondsRemaining = (int)BanditDemandTimeout,
        });
    }

    private void HandleBanditResponse(PlayerSession session, BanditResponseIntent intent)
    {
        if (session.BanditDemandId == 0 || intent.DemandId != session.BanditDemandId)
        {
            return; // stale or spoofed — the pending hold-up (if any) is untouched
        }

        if (session.BanditDemandFromShip)
        {
            ResolveBanditShipDemand(session, intent.Comply);
            return;
        }

        var bandit = _bandits.FirstOrDefault(b => b.Id == session.BanditDemandBanditId);
        if (bandit is null)
        {
            ClearBanditDemand(session);
            return;
        }

        ResolveBanditDemand(session, bandit, intent.Comply, intent.Comply ? "paid" : "refused");
    }

    private void ResolveBanditDemand(PlayerSession session, CombatEntity bandit, bool comply, string outcome)
    {
        if (comply)
        {
            TakeBanditPayment(session, bandit, includeCargo: false);
            BeginBanditLeave(bandit);
            session.NextBanditAmbushAt = _uptime + 1200.0; // paying buys a long quiet spell
        }
        else
        {
            BanditTurnsHostile(bandit, session.State.PlayerId);
        }

        SendBanditResult(session, outcome);
        ClearBanditDemand(session);
        BroadcastPlanetEnemies();
    }

    /// <summary>Removes the demanded goods (clamped to what the mark still has — dropping items mid-hold-up
    /// doesn't glitch it) and stows them in the bandit's loot, so killing the robber later wins them back.</summary>
    private void TakeBanditPayment(PlayerSession session, CombatEntity bandit, bool includeCargo)
    {
        var pool = new MaterialPool(_content, session.State, _ship);
        foreach (var want in session.BanditDemandItems)
        {
            int take = System.Math.Min(want.Count, pool.Count(want.Item));
            if (take > 0)
            {
                pool.Remove(new[] { new ItemAmount(want.Item, take) });
                bandit.Loot.Add(new ItemAmount(want.Item, take));
            }
        }

        SendInventory(session);
    }

    private void BanditTurnsHostile(CombatEntity bandit, string targetPlayerId)
    {
        bandit.Hostile = true;
        bandit.BanditPhase = BanditPhase.Fighting;
        bandit.BanditTargetId = targetPlayerId;
        bandit.GiveUpTimer = 0;
    }

    private void SendBanditResult(PlayerSession session, string outcome)
        => Send(session, new BanditEncounterResult { DemandId = session.BanditDemandId, Outcome = outcome });

    private void ClearBanditDemand(PlayerSession session)
    {
        session.BanditDemandId = 0;
        session.BanditDemandBanditId = string.Empty;
        session.BanditDemandDeadline = 0;
        session.BanditDemandFromShip = false;
        session.BanditDemandItems.Clear();
    }

    // ---------------- Combat hooks (called from AttackCombatEntity) ----------------

    /// <summary>Attacking a bandit is an answer too: any pending hold-up by it resolves as refused and it
    /// turns on its attacker. Camp guards are always hostile already.</summary>
    private void OnBanditAttacked(PlayerSession attacker, CombatEntity bandit)
    {
        if (bandit.Hostile && bandit.BanditPhase != BanditPhase.Leaving)
        {
            return;
        }

        var mark = bandit.BanditTargetId.Length > 0 ? FindSessionByPlayerId(bandit.BanditTargetId) : null;
        if (mark is not null && mark.BanditDemandId != 0 && mark.BanditDemandBanditId == bandit.Id)
        {
            SendBanditResult(mark, "refused");
            ClearBanditDemand(mark);
        }

        BanditTurnsHostile(bandit, attacker.State.PlayerId);
    }

    /// <summary>A bandit died: close any pending hold-up by it, and for camp guards check whether the camp
    /// is now cleared — cleared camps are persisted and their guards never come back.</summary>
    private void OnBanditKilled(CombatEntity bandit)
    {
        var mark = bandit.BanditTargetId.Length > 0 ? FindSessionByPlayerId(bandit.BanditTargetId) : null;
        if (mark is not null && mark.BanditDemandId != 0 && mark.BanditDemandBanditId == bandit.Id)
        {
            SendBanditResult(mark, "fled");
            ClearBanditDemand(mark);
        }

        if (bandit.CampKey.Length == 0)
        {
            return;
        }

        foreach (var camp in _banditCamps)
        {
            if (camp.Key != bandit.CampKey || camp.Cleared)
            {
                continue;
            }

            bool anyAlive = false;
            foreach (var b in _bandits)
            {
                if (!ReferenceEquals(b, bandit) && b.CampKey == camp.Key)
                {
                    anyAlive = true;
                    break;
                }
            }

            if (!anyAlive)
            {
                camp.Cleared = true;
                MarkFeatureStamped("banditcamp:" + camp.Key + ":cleared");
            }
        }
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/util: spawn a lone robber approaching a specific player, skipping the pacing rolls.</summary>
    public void SpawnBanditAtForTest(Vector3f at, string targetPlayerId, bool gunner = false)
    {
        _bandits.Add(new CombatEntity
        {
            Id = NextEntityId(),
            Kind = gunner ? CombatEntityKind.BanditGunner : CombatEntityKind.Bandit,
            Name = "Test Robber",
            Hostile = false,
            Hull = BanditHull,
            HullMax = BanditHull,
            Position = at,
            DamagePerSecond = gunner ? BanditGunDps : BanditMeleeDps,
            BanditPhase = BanditPhase.Approach,
            BanditTargetId = targetPlayerId,
            Loot = { new ItemAmount("iron_plate", 2) },
        });
    }

    /// <summary>Test/util: the pending demand id for a player (0 = none).</summary>
    public int PendingBanditDemandIdForTest(string playerId)
        => FindSessionByPlayerId(playerId)?.BanditDemandId ?? 0;

    /// <summary>Test/util: the demanded items of the player's pending hold-up.</summary>
    public IReadOnlyList<ItemAmount> PendingBanditDemandItemsForTest(string playerId)
        => FindSessionByPlayerId(playerId)?.BanditDemandItems ?? (IReadOnlyList<ItemAmount>)System.Array.Empty<ItemAmount>();

    /// <summary>Test/util: answers the pending hold-up as the player would via the UI.</summary>
    public void RespondBanditDemandForTest(string playerId, bool comply)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is not null && session.BanditDemandId != 0)
        {
            HandleBanditResponse(session, new BanditResponseIntent { DemandId = session.BanditDemandId, Comply = comply });
        }
    }
}
