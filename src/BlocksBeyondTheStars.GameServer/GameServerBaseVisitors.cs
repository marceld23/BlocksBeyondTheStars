// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// "Scouts at the gate" (#1224) — the opt-in third stage of base life (#1120 stage 3, long deferred).
///
/// Nothing ever approached a base: camp guards leash to their camp and lone robbers only ever stalk a
/// PLAYER, so a home never felt watched. With the world rule <c>BaseVisitors</c> on, a founded base whose
/// owner is home occasionally gets two bandit scouts: they walk up to the EDGE of the base zone, stand there
/// for a minute looking, and wander off. They never step inside, never demand anything, never touch a
/// block and never take a thing — the kid-friendly line of #1197 holds. Hit one and it fights like any
/// robber; drive them off and the homestead bounty + the <c>base:defended</c> counter credit it.
///
/// Gated four ways, each on purpose: the rule (default OFF everywhere but the <c>dangerous</c> preset),
/// <see cref="BanditsActive"/> (scouts ARE bandits — no robbers, no scouts), Survival (inside BanditsActive),
/// and the owner being home on that body — the same "no private war on an empty world" rule the sentry
/// (#1214) uses. No launch-rule lift: the default is off, so an old save deserialising the missing field to
/// <c>false</c> is already right, and an existing dangerous world must not quietly grow teeth on update.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Scouts per visit.</summary>
    private const int ScoutsPerVisit = 2;

    /// <summary>How far out from the core a visit starts — beyond the zone, inside notice range.</summary>
    private const float ScoutSpawnDistance = 40f;

    /// <summary>How long scouts linger at the edge before leaving.</summary>
    private const double ScoutVisitSeconds = 60.0;

    /// <summary>Quiet spell per base between two visits, scaled by the bandit slider like the lone-robber
    /// cooldown — a visit is an event, not a siege.</summary>
    private double ScoutVisitCooldown => Rules.Bandits switch
    {
        AlienActivity.Rare => 1500.0,
        AlienActivity.Normal => 900.0,
        AlienActivity.Frequent => 600.0,
        AlienActivity.Extreme => 420.0,
        _ => 900.0,
    };

    /// <summary>Uptime of the next possible visit per base id (RAM only: a visit is not a mark on the save).</summary>
    private readonly Dictionary<int, double> _nextScoutVisitAt = new();

    /// <summary>Whether a scout visit may happen on this world at all right now.</summary>
    private bool BaseVisitorsActive => Rules.BaseVisitors && BanditsActive;

    /// <summary>Per-tick: rolls one visit at most, for a base whose owner is home. Called from
    /// <see cref="TickBandits"/> next to the lone-robber spawner so the two share the gates and the tick.</summary>
    private void TrySpawnBaseScouts(List<PlayerSession> targets)
    {
        if (!BaseVisitorsActive || targets.Count == 0)
        {
            return;
        }

        foreach (var b in _bases)
        {
            if (b.Planet != _world.LocationId)
            {
                continue;
            }

            if (!_nextScoutVisitAt.TryGetValue(b.Id, out double next))
            {
                // First sight of this base: arm the window instead of knocking the moment someone founds it.
                _nextScoutVisitAt[b.Id] = _uptime + ScoutVisitCooldown * (0.5 + _banditRng.NextDouble() * 0.5);
                continue;
            }

            if (_uptime < next)
            {
                continue;
            }

            var owner = ScoutableOwner(b, targets);
            if (owner is null)
            {
                continue; // nobody home — the window simply stays armed until there is
            }

            _nextScoutVisitAt[b.Id] = _uptime + ScoutVisitCooldown * (0.8 + _banditRng.NextDouble() * 0.4);
            if (_banditRng.NextDouble() > 0.6)
            {
                continue; // like the lone robber: not every window produces a visit
            }

            if (ScoutsAlreadyAt(b.Id))
            {
                continue;
            }

            SpawnScoutsAt(b, owner);
            BroadcastPlanetEnemies();
            return; // one visit per tick
        }
    }

    /// <summary>The base owner if they are home: joined, on this body, on foot, and not in a mode hostiles
    /// ignore (creative / god / stealth — a scout has nothing to scout).</summary>
    private PlayerSession? ScoutableOwner(ServerBase b, List<PlayerSession> targets)
    {
        foreach (var s in targets)
        {
            if (s.State.PlayerId == b.OwnerId && !s.State.IgnoredByHostiles)
            {
                return s;
            }
        }

        return null;
    }

    private bool ScoutsAlreadyAt(int baseId)
    {
        foreach (var bandit in _bandits)
        {
            if (bandit.ScoutBaseId == baseId)
            {
                return true;
            }
        }

        return false;
    }

    private void SpawnScoutsAt(ServerBase b, PlayerSession owner)
    {
        double ang = _banditRng.NextDouble() * System.Math.PI * 2.0;
        for (int i = 0; i < ScoutsPerVisit; i++)
        {
            // Two scouts a few blocks apart on the same bearing, so they arrive as a pair and not a line.
            double a = ang + (i == 0 ? -0.08 : 0.08);
            int ex = (int)System.Math.Round(b.Cell.X + System.Math.Cos(a) * ScoutSpawnDistance);
            int ez = (int)System.Math.Round(b.Cell.Z + System.Math.Sin(a) * ScoutSpawnDistance);
            int ey = GroundFeetYAt(ex, ez, _generator.SurfaceHeight(_world.Planet, ex, ez) + 1);

            bool gunner = i == 1; // one of each, like a patrol would
            _bandits.Add(new CombatEntity
            {
                Id = NextEntityId(),
                Kind = gunner ? CombatEntityKind.BanditGunner : CombatEntityKind.Bandit,
                Name = NameGenerator.Person(_banditRng),
                Hostile = false, // looking, not fighting — hostility is earned exactly as for a robber
                Hull = BanditHull,
                HullMax = BanditHull,
                Position = new Vector3f(ex, ey, ez),
                DamagePerSecond = gunner ? BanditGunDps : BanditMeleeDps,
                BanditPhase = BanditPhase.Scouting,
                BanditTargetId = owner.State.PlayerId,
                ScoutBaseId = b.Id,
                Loot = { new ItemAmount("iron_plate", 2) },
            });
        }

        string baseName = string.IsNullOrEmpty(b.Name) ? owner.State.Name ?? "?" : b.Name;
        Send(owner, new ServerMessage { Text = "@srv.base.scouts:" + baseName });
        if (!owner.ScoutsVegaSaid)
        {
            owner.ScoutsVegaSaid = true; // VEGA explains it once per session; the toast repeats per visit
            SendVegaLine(owner, "vega.sys.base_scouts", 1);
        }

        _log.Info($"Base scouts: two bandits are looking at '{baseName}' (owner '{owner.State.Name}').");
    }

    /// <summary>The scouting script inside <see cref="MoveBandit"/>: walk to the zone edge, stand, leave.
    /// Returns the locomotion intent; a non-null <paramref name="target"/> is where to walk.</summary>
    private MoveMode ScoutIntent(CombatEntity bandit, double dt, ref bool changed, out Vector3f? target)
    {
        target = null;
        ServerBase? home = null;
        foreach (var b in _bases)
        {
            if (b.Id == bandit.ScoutBaseId)
            {
                home = b;
                break;
            }
        }

        // The base was dissolved, the owner left, or the rule was switched off mid-visit: nothing to see.
        var owner = FindSessionByPlayerId(bandit.BanditTargetId);
        if (home is null || !BaseVisitorsActive || owner is null || !owner.Joined
            || owner.CurrentLocationId != _world.LocationId || InSpace(owner.State.PlayerId))
        {
            BeginBanditLeave(bandit);
            changed = true;
            return MoveMode.Roam;
        }

        bandit.GiveUpTimer += dt; // doubles as the visit clock — the field is free in this phase
        if (bandit.GiveUpTimer > ScoutVisitSeconds)
        {
            BeginBanditLeave(bandit);
            changed = true;
            return MoveMode.Roam;
        }

        // The spot to stand on: the zone boundary, one block outside, on the line from the core to the scout.
        var edge = ScoutEdgePoint(home.Cell, bandit.Position);

        // The promise of this feature: a scout is NEVER inside the zone. Clamp like the fence does — if the
        // walk (or a slope) carried it over the line, it is set back onto the edge this very tick.
        if (WithinBaseZone(home.Cell, bandit.Position.ToBlock()))
        {
            bandit.Position = edge;
            changed = true;
        }

        // Always SEEK the edge point, never Roam: Roam is the controller's "wander" mode (it picks its own
        // headings and pauses), and a wandering scout would drift over the line and be clamped back every
        // tick — a broadcast per tick for nothing. Seeking a point you are already standing on is standing.
        target = Unwrapped(bandit.Position, edge);
        return MoveMode.Seek;
    }

    /// <summary>The fence rule for scouts (#1224): a step that would land inside the zone is refused. Checked
    /// by <see cref="MoveBandit"/> AFTER the locomotion step — the pre-step clamp in <see cref="ScoutIntent"/>
    /// covers a scout that somehow already stands inside, this covers the one that is about to.</summary>
    private bool ScoutStepBlocked(CombatEntity bandit, Vector3f candidate)
    {
        if (bandit.BanditPhase != BanditPhase.Scouting || bandit.ScoutBaseId <= 0)
        {
            return false;
        }

        foreach (var b in _bases)
        {
            if (b.Id == bandit.ScoutBaseId)
            {
                return WithinBaseZone(b.Cell, candidate.ToBlock());
            }
        }

        return false;
    }

    /// <summary>A point two blocks outside the zone cube, on the bearing from the core to <paramref name="from"/>.
    /// Chebyshev, like the zone test itself, so the result is always just past the face the scout is nearest;
    /// two blocks (not one) so the locomotion's arrival overshoot has room before the fence rule bites.</summary>
    private static Vector3f ScoutEdgePoint(Vector3i core, Vector3f from)
    {
        float dx = from.X - core.X;
        float dz = from.Z - core.Z;
        float r = BaseProtectionRadius + 2.5f;
        float m = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz));
        if (m < 0.01f)
        {
            dx = 1f;
            m = 1f;
        }

        float k = r / m;
        return new Vector3f(core.X + 0.5f + dx * k, from.Y, core.Z + 0.5f + dz * k);
    }

    /// <summary>A scout of <paramref name="baseId"/> was beaten: the homestead bounty and the
    /// <c>base:defended</c> counter credit the player who did it.</summary>
    private void OnScoutDefeated(PlayerSession session, CombatEntity scout)
    {
        if (scout.ScoutBaseId <= 0)
        {
            return;
        }

        OnMissionDefeat(session, DefeatTargetScout);
        Advance(session, AchievementCounters.BaseDefended);
    }

    /// <summary>Test seam: spawns a scout visit at a base right now, skipping the cooldown roll (#1224).</summary>
    public bool SpawnScoutsForTest(int baseId)
    {
        foreach (var b in _bases)
        {
            if (b.Id != baseId || b.Planet != _world.LocationId)
            {
                continue;
            }

            var targets = new List<PlayerSession>(JoinedInActiveWorld());
            var owner = ScoutableOwner(b, targets);
            if (owner is null || !BaseVisitorsActive)
            {
                return false;
            }

            SpawnScoutsAt(b, owner);
            return true;
        }

        return false;
    }

    /// <summary>Test seam: one scout-visit roll with the cooldown already elapsed for every base (#1224).</summary>
    public void TrySpawnBaseScoutsForTest()
    {
        foreach (var b in _bases)
        {
            _nextScoutVisitAt[b.Id] = 0; // the window is open
        }

        var targets = new List<PlayerSession>();
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.State.AboardShip && !InSpace(s.State.PlayerId) && !s.Spectating)
            {
                targets.Add(s);
            }
        }

        // The 60 % roll is the one random element; a test wants the deterministic answer, so roll until
        // it lands or the gates say no (bounded — the gates are checked first every time).
        for (int i = 0; i < 64 && !_bandits.Exists(x => x.ScoutBaseId > 0); i++)
        {
            foreach (var b in _bases)
            {
                _nextScoutVisitAt[b.Id] = 0;
            }

            TrySpawnBaseScouts(targets);
            if (!BaseVisitorsActive)
            {
                return;
            }
        }
    }

    /// <summary>Test seam: steps every bandit's movement by <paramref name="dt"/> (#1224).</summary>
    public void TickBanditsForTest(double dt) => TickBandits(dt);
}
