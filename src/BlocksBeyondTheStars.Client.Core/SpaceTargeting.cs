// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>How the flight target lock reads a space object (#2277). Every disposition has its own colour AND its own
/// shape (frame and edge arrow), so colour is never the only cue: enemy = red diamond with "!", a raider demanding
/// cargo = orange hollow diamond, neutral = white square brackets, friendly = cyan ring.</summary>
public enum TargetDisposition
{
    Hostile,
    Caution,
    Neutral,
    Friendly,
}

/// <summary>One lockable object of the flight instance: a space entity (<see cref="NetCombatEntity"/>), another pilot or
/// an NPC trader (<see cref="NetSpacePlayer"/>), or a body of the system. Positions are in the instance frame; the
/// <see cref="Distance"/> is filled by <see cref="SpaceTargeting.Order"/>.</summary>
public readonly struct TargetCandidate
{
    public TargetCandidate(string id, string kind, bool hostile, bool isPilot, bool isTrader, float x, float y, float z, float distance = 0f)
    {
        Id = id ?? string.Empty;
        Kind = kind ?? string.Empty;
        Hostile = hostile;
        IsPilot = isPilot;
        IsTrader = isTrader;
        X = x;
        Y = y;
        Z = z;
        Distance = distance;
    }

    public string Id { get; }
    public string Kind { get; }
    public bool Hostile { get; }
    public bool IsPilot { get; }
    public bool IsTrader { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public float Distance { get; }

    public TargetDisposition Disposition => SpaceTargeting.Classify(Kind, Hostile, IsPilot, IsTrader);

    public TargetCandidate WithDistance(float distance) => new TargetCandidate(Id, Kind, Hostile, IsPilot, IsTrader, X, Y, Z, distance);

    /// <summary>A space entity of the instance snapshot.</summary>
    public static TargetCandidate FromEntity(NetCombatEntity e)
        => new TargetCandidate(e.Id, e.Kind, e.Hostile, false, false, e.X, e.Y, e.Z);

    /// <summary>Another pilot in the instance — or an NPC trader, whose pose rides the same list with an <c>npc:</c> id.</summary>
    public static TargetCandidate FromPilot(NetSpacePlayer p)
    {
        bool trader = SpaceTargeting.IsTraderId(p.PlayerId);
        return new TargetCandidate(p.PlayerId, trader ? SpaceTargeting.TraderKind : SpaceTargeting.PilotKind, false, !trader, trader, p.X, p.Y, p.Z);
    }
}

/// <summary>
/// The pure rules of the flight target lock (#2277, #2283) — client presentation only: which objects can be locked, in
/// what order the cycle key walks them, when a lock is released, and where the edge arrow sits for a target off screen.
/// The lock never reaches the server; it only decides which target id the client writes into the intents it already
/// sends (fire, tractor, scan), and the server validates those as before. It is also the one home of the space-object
/// kind lists the flight view used to copy by hand (fire targets, scannables, hostile ships, navigation points).
/// </summary>
public static class SpaceTargeting
{
    public const string Drone = "Drone";
    public const string Ufo = "Ufo";
    public const string Cruiser = "Cruiser";
    public const string BanditShip = "BanditShip";
    public const string SpaceStation = "SpaceStation";
    public const string Wreck = "Wreck";
    public const string Asteroid = "Asteroid";
    public const string ResourceDrop = "ResourceDrop";
    public const string EscapePod = "EscapePod";
    public const string Anomaly = "Anomaly";
    public const string Wormhole = "Wormhole";
    public const string DebrisField = "DebrisField";       // #2353: the field's marker (its flight recorder)
    public const string Debris = "Debris";                 // #2353/#2356: a wreckage fragment — mined like a rock
    public const string SalvageCapsule = "SalvageCapsule"; // #2353: a sealed capsule — pulled in like a salvage drop

    /// <summary>Synthetic kind of another pilot (not a server entity kind).</summary>
    public const string PilotKind = "Pilot";

    /// <summary>Synthetic kind of an NPC trader (a pose with an <see cref="TraderIdPrefix"/> id).</summary>
    public const string TraderKind = "Trader";

    /// <summary>Synthetic kind of a planet or moon of the system (the scanner's name for it, too).</summary>
    public const string BodyKind = "Body";

    /// <summary>The id prefix of the NPC traders' synthetic poses in <c>SpaceState.Players</c>.</summary>
    public const string TraderIdPrefix = "npc:";

    /// <summary>A hostile closer than this is attacking: the server's damage aura (<c>ShipEngageRange</c>) and the
    /// client's drawn enemy shots use the same range — one shared constant since #2284, so the two can never drift.</summary>
    public const float AttackRange = BlocksBeyondTheStars.Shared.Definitions.SpaceCombatRules.EngageRange;

    /// <summary>A lock is released only beyond its lock range plus this slack, so a target on the rim never flickers.</summary>
    public const float ReleaseSlack = 1.1f;

    /// <summary>With the AutoAim world rule on, the weapon prefers the locked target inside this cone around the nose —
    /// wider than the free ±30° acquisition cone, still well inside the server's ±60° arc (<c>ValidateSpaceAim</c>),
    /// so a lock-assisted shot is never rejected for its angle.</summary>
    public const float LockAssistConeDegrees = 40f;

    /// <summary><see cref="LockAssistConeDegrees"/> as the minimum dot product of the nose and the target direction.</summary>
    public static readonly float LockAssistMinDot = (float)System.Math.Cos(LockAssistConeDegrees * System.Math.PI / 180.0);

    /// <summary>The edge arrow's ellipse: semi-axes as a fraction of the screen width and height — inside the HUD
    /// (radar, vitals, instruments, quick-bar sit at the hard edges), close to the crosshair, in the TV safe area.</summary>
    public const float EllipseX = 0.42f;
    public const float EllipseY = 0.38f;

    // ---- Kind lists ------------------------------------------------------------------------------------

    /// <summary>The ship kinds that can turn hostile and shoot: drones, saucers, cruisers and raiders.</summary>
    public static bool IsHostileShipKind(string? kind) => kind == Drone || kind == Ufo || kind == Cruiser || kind == BanditShip;

    /// <summary>The fixed navigation points of a system — lockable from anywhere in it, like the radar pins them.</summary>
    public static bool IsNavigationKind(string? kind)
        => kind == SpaceStation || kind == Wreck || kind == EscapePod || kind == Anomaly || kind == Wormhole || kind == DebrisField;

    /// <summary>The encounters that never move (life pod, anomaly, wormhole, a debris field's marker).</summary>
    public static bool IsStaticEncounterKind(string? kind)
        => kind == EscapePod || kind == Anomaly || kind == Wormhole || kind == DebrisField;

    /// <summary>What the ship's weapons shoot: hostile ships, asteroids, the wreck and debris fragments (salvage is
    /// mined with the beam).</summary>
    public static bool IsFireTargetKind(string? kind) => IsMiningKind(kind) || IsHostileShipKind(kind);

    /// <summary>The space objects the ship scanner reads (mirrors the server's list).</summary>
    public static bool IsScannableKind(string? kind)
        => kind == Asteroid || kind == Anomaly || kind == Wreck || kind == EscapePod || kind == SpaceStation
           || kind == Wormhole || kind == DebrisField || IsHostileShipKind(kind);

    /// <summary>What the tractor beam pulls in (a locked one in reach is the one pulled): salvage drops and the
    /// debris fields' sealed capsules (#2353).</summary>
    public static bool IsCollectableKind(string? kind) => kind == ResourceDrop || kind == SalvageCapsule;

    /// <summary>The entity kinds the cycle keys always walk. Asteroids, salvage drops and planets are left out (a belt
    /// would bury the enemies under twenty rocks) — "target ahead" reaches them, and the mining context (#2328) lets the
    /// nearest rocks in (see <see cref="Tier"/>).</summary>
    public static bool IsCycleKind(string? kind) => IsHostileShipKind(kind) || IsNavigationKind(kind);

    /// <summary>What the mining beam carves: asteroids, the derelict wreck and debris fragments (#2353) — the targets
    /// <see cref="WeaponSuits"/> gives a mining tool. The mining lock (#2327: a shot locks one, after it breaks the lock
    /// moves to the next in reach) and the mining context of the cycle (#2328) read this list.</summary>
    public static bool IsMiningKind(string? kind) => kind == Asteroid || kind == Wreck || kind == Debris;

    /// <summary>Whether a ship weapon of <paramref name="weaponClass"/> (<c>weapon_class</c>: 0 mining tool, 1 combat,
    /// 2 both) can mine. A pure combat cannon breaks rocks only where the server rules allow it, so it never opens the
    /// mining context.</summary>
    public static bool CanMine(int weaponClass) => weaponClass != 1;

    /// <summary>#2328: at most this many rocks join the cycle in a mining context — the nearest ones in weapon range,
    /// never the whole field (nine at a dense launch) and never the belt.</summary>
    public const int MaxRocksInCycle = 3;

    /// <summary>The kinds that can be locked at any distance: the navigation points and the bodies of the system.</summary>
    public static bool IsSystemWide(string? kind) => IsNavigationKind(kind) || kind == BodyKind;

    /// <summary>Whether a ship weapon of <paramref name="weaponClass"/> (the module stat <c>weapon_class</c>: 0 mining
    /// tool, 1 combat weapon, 2 both) is built for a target kind. The lock assist only pushes a weapon onto a target it
    /// is meant for, so a locked drone never draws a stream of server refusals from the asteroid breaker.</summary>
    public static bool WeaponSuits(int weaponClass, string? kind)
    {
        if (IsHostileShipKind(kind))
        {
            return weaponClass != 0;
        }

        return IsMiningKind(kind) && weaponClass != 1;
    }

    /// <summary>True for an NPC trader's pose id.</summary>
    public static bool IsTraderId(string? playerId)
        => playerId != null && playerId.StartsWith(TraderIdPrefix, System.StringComparison.Ordinal);

    // ---- Disposition -----------------------------------------------------------------------------------

    /// <summary>The one disposition table: a hostile is an enemy; a raider still talking (it demands cargo before it
    /// fights) is caution; stations, life pods and other pilots are friendly; NPC traders, wrecks, rocks, anomalies,
    /// wormholes, drops and planets are neutral. If ship-vs-ship PvP ever arrives, this is the place to read the rule.</summary>
    public static TargetDisposition Classify(string? kind, bool hostile, bool isPilot = false, bool isTrader = false)
    {
        if (isTrader || kind == TraderKind)
        {
            return TargetDisposition.Neutral;
        }

        if (isPilot || kind == PilotKind)
        {
            return TargetDisposition.Friendly;
        }

        if (hostile)
        {
            return TargetDisposition.Hostile;
        }

        if (kind == BanditShip)
        {
            return TargetDisposition.Caution;
        }

        return kind == SpaceStation || kind == EscapePod ? TargetDisposition.Friendly : TargetDisposition.Neutral;
    }

    /// <summary>True while a hostile at <paramref name="distance"/> is attacking (inside the damage aura).</summary>
    public static bool IsAttacking(string? kind, bool hostile, float distance)
        => hostile && IsHostileShipKind(kind) && distance <= AttackRange;

    /// <summary>True when a candidate (with its <see cref="TargetCandidate.Distance"/> filled) is attacking.</summary>
    public static bool IsAttacking(in TargetCandidate c) => IsAttacking(c.Kind, c.Hostile, c.Distance);

    // ---- Range -----------------------------------------------------------------------------------------

    /// <summary>Whether a target at <paramref name="distance"/> can be locked: navigation points and bodies from anywhere
    /// in the system, everything else within the radar's range — or anything while the Quantum scanner's system ping
    /// lasts. <paramref name="slack"/> widens the range (the release hysteresis).</summary>
    public static bool InLockRange(string? kind, float distance, float lockRange, bool pingActive, float slack = 1f)
        => pingActive || IsSystemWide(kind) || distance <= lockRange * slack;

    /// <summary>Whether a held lock stays: in lock range with the <see cref="ReleaseSlack"/> — locked at 100 %, kept to
    /// 110 %, so a target on the rim does not flicker in and out.</summary>
    public static bool StillValid(in TargetCandidate c, float shipX, float shipY, float shipZ, float lockRange, bool pingActive)
        => InLockRange(c.Kind, Distance(c, shipX, shipY, shipZ), lockRange, pingActive, ReleaseSlack);

    public static float Distance(in TargetCandidate c, float shipX, float shipY, float shipZ)
    {
        float dx = c.X - shipX, dy = c.Y - shipY, dz = c.Z - shipZ;
        return (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    // ---- Cycle order -----------------------------------------------------------------------------------

    private const int AttackingTier = 0;
    private const int RockTier = 2;

    /// <summary>The cycle tier of a candidate at its distance, or −1 when the cycle skips it: 0 hostiles attacking right
    /// now, 1 every other hostile and a raider demanding cargo, 2 the rocks of a mining context (#2328: with
    /// <paramref name="miningRange"/> &gt; 0 — the selected laser can mine — an asteroid within that range; what you are
    /// about to shoot is more immediate than a system-wide station), 3 the navigation points, 4 other pilots and traders.
    /// <see cref="Order"/> adds the two rules a single candidate cannot know: only the <see cref="MaxRocksInCycle"/>
    /// nearest rocks, and none while a hostile is attacking.</summary>
    public static int Tier(in TargetCandidate c, float lockRange, bool pingActive, float miningRange = 0f)
    {
        if (c.IsPilot || c.IsTrader)
        {
            return InLockRange(c.Kind, c.Distance, lockRange, pingActive) ? 4 : -1;
        }

        if (c.Kind == Asteroid || c.Kind == Debris)
        {
            // Rocks and wreckage fragments (#2353) join the cycle only in a mining context, nearest first.
            return miningRange > 0f && c.Distance <= miningRange && InLockRange(c.Kind, c.Distance, lockRange, pingActive) ? RockTier : -1;
        }

        if (!IsCycleKind(c.Kind) || !InLockRange(c.Kind, c.Distance, lockRange, pingActive))
        {
            return -1;
        }

        var disposition = c.Disposition;
        if (disposition == TargetDisposition.Hostile || disposition == TargetDisposition.Caution)
        {
            return IsAttacking(c) ? AttackingTier : 1;
        }

        return IsNavigationKind(c.Kind) ? 3 : -1;
    }

    /// <summary>The cycle list, fresh for one key press: lockable candidates by tier (<see cref="Tier"/>), nearest
    /// first within a tier, the id breaking ties so the order is stable. Each entry carries its distance. Fills and
    /// returns <paramref name="into"/> (a new list when null). <paramref name="miningRange"/> &gt; 0 opens the mining
    /// context (#2328): the <see cref="MaxRocksInCycle"/> nearest asteroids within it join — unless a hostile is
    /// attacking, when a fight keeps the cycle an enemy list.</summary>
    public static List<TargetCandidate> Order(IReadOnlyList<TargetCandidate> all, float shipX, float shipY, float shipZ,
        float lockRange, bool pingActive, List<TargetCandidate>? into = null, float miningRange = 0f)
    {
        var result = into ?? new List<TargetCandidate>(all.Count);
        result.Clear();
        var keyed = new List<(int Tier, TargetCandidate C)>(all.Count);
        bool underAttack = false;
        for (int i = 0; i < all.Count; i++)
        {
            var c = all[i].WithDistance(Distance(all[i], shipX, shipY, shipZ));
            int tier = Tier(c, lockRange, pingActive, miningRange);
            if (tier >= 0)
            {
                keyed.Add((tier, c));
                underAttack |= tier == AttackingTier;
            }
        }

        keyed.Sort((a, b) =>
        {
            int t = a.Tier.CompareTo(b.Tier);
            if (t != 0)
            {
                return t;
            }

            int d = a.C.Distance.CompareTo(b.C.Distance);
            return d != 0 ? d : string.CompareOrdinal(a.C.Id, b.C.Id);
        });
        int rocks = 0;
        foreach (var k in keyed)
        {
            if (k.Tier == RockTier && (underAttack || rocks++ >= MaxRocksInCycle))
            {
                continue;
            }

            result.Add(k.C);
        }

        return result;
    }

    /// <summary>#2327: the mining flow after a kill — the nearest asteroid or wreck within <paramref name="range"/> (the
    /// selected mining laser's reach) other than <paramref name="excludeId"/>, with its distance; null when there is
    /// none. Measured from the ship, the id breaking ties.</summary>
    public static TargetCandidate? NearestMineable(IReadOnlyList<TargetCandidate> all, float shipX, float shipY, float shipZ,
        float range, string? excludeId = null)
    {
        TargetCandidate? best = null;
        for (int i = 0; i < all.Count; i++)
        {
            var c = all[i];
            if (c.IsPilot || c.IsTrader || !IsMiningKind(c.Kind) || c.Id == excludeId)
            {
                continue;
            }

            c = c.WithDistance(Distance(c, shipX, shipY, shipZ));
            if (c.Distance <= range && (best == null || Nearer(c, best.Value)))
            {
                best = c;
            }
        }

        return best;
    }

    /// <summary>"Next target": the entry after <paramref name="currentId"/>, wrapping at the end; the first entry when
    /// nothing (or something no longer in the list) is locked; null for an empty list.</summary>
    public static TargetCandidate? Next(string? currentId, IReadOnlyList<TargetCandidate> ordered)
    {
        if (ordered.Count == 0)
        {
            return null;
        }

        int at = IndexOf(currentId, ordered);
        return at < 0 ? ordered[0] : ordered[(at + 1) % ordered.Count];
    }

    /// <summary>"Nearest enemy": the nearest hostile (a raider demanding cargo counts); pressed again while one is
    /// locked, the next nearest after it, wrapping. Null when there is none.</summary>
    public static TargetCandidate? NearestHostile(string? currentId, IReadOnlyList<TargetCandidate> ordered)
    {
        var enemies = new List<TargetCandidate>();
        for (int i = 0; i < ordered.Count; i++)
        {
            var d = ordered[i].Disposition;
            if (d == TargetDisposition.Hostile || d == TargetDisposition.Caution)
            {
                enemies.Add(ordered[i]);
            }
        }

        if (enemies.Count == 0)
        {
            return null;
        }

        enemies.Sort((a, b) =>
        {
            int d = a.Distance.CompareTo(b.Distance);
            return d != 0 ? d : string.CompareOrdinal(a.Id, b.Id);
        });
        int at = IndexOf(currentId, enemies);
        return at < 0 ? enemies[0] : enemies[(at + 1) % enemies.Count];
    }

    /// <summary>The nearest hostile attacking right now (the auto-advance after a kill), or null.</summary>
    public static TargetCandidate? FirstAttacking(IReadOnlyList<TargetCandidate> ordered, string? excludeId = null)
    {
        for (int i = 0; i < ordered.Count; i++)
        {
            if (IsAttacking(ordered[i]) && ordered[i].Id != excludeId)
            {
                return ordered[i];
            }
        }

        return null;
    }

    /// <summary>The threat ticks (#2283): up to <paramref name="max"/> hostiles attacking right now other than the
    /// locked one, nearest first, each with its distance. Fills and returns <paramref name="into"/>. Runs every frame,
    /// so it allocates nothing: an insertion sort over the handful of attackers (the Guardian finale has twelve).</summary>
    public static List<TargetCandidate> NearestAttackers(IReadOnlyList<TargetCandidate> all, float shipX, float shipY, float shipZ,
        string? excludeId, int max, List<TargetCandidate> into)
    {
        into.Clear();
        for (int i = 0; i < all.Count; i++)
        {
            var c = all[i];
            if (c.IsPilot || c.IsTrader || c.Id == excludeId)
            {
                continue;
            }

            c = c.WithDistance(Distance(c, shipX, shipY, shipZ));
            if (!IsAttacking(c))
            {
                continue;
            }

            int at = into.Count;
            while (at > 0 && Nearer(c, into[at - 1]))
            {
                at--;
            }

            if (at < max)
            {
                into.Insert(at, c);
                if (into.Count > max)
                {
                    into.RemoveAt(into.Count - 1);
                }
            }
        }

        return into;
    }

    private static bool Nearer(in TargetCandidate a, in TargetCandidate b)
        => a.Distance < b.Distance || (a.Distance == b.Distance && string.CompareOrdinal(a.Id, b.Id) < 0);

    private static int IndexOf(string? id, IReadOnlyList<TargetCandidate> list)
    {
        if (id == null)
        {
            return -1;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    // ---- Target ahead ----------------------------------------------------------------------------------

    /// <summary>"Target ahead" (the scanner's cone rule): a target whose direction lies within <paramref name="coneDeg"/>
    /// of the nose, widened by its apparent size, qualifies; <paramref name="score"/> is the angle left after the apparent
    /// size (smaller = better aligned). <paramref name="fx"/>/<paramref name="fy"/>/<paramref name="fz"/> is the nose
    /// direction (any length).</summary>
    public static bool AheadScore(float dx, float dy, float dz, float fx, float fy, float fz, float radius, float coneDeg, out float score)
    {
        score = float.MaxValue;
        float dist = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        float flen = (float)System.Math.Sqrt(fx * fx + fy * fy + fz * fz);
        if (dist < 0.01f || flen < 1e-6f)
        {
            return false;
        }

        float cos = (dx * fx + dy * fy + dz * fz) / (dist * flen);
        float angle = (float)(System.Math.Acos(System.Math.Max(-1f, System.Math.Min(1f, cos))) * 180.0 / System.Math.PI);
        float apparent = (float)(System.Math.Atan2(System.Math.Max(0f, radius), dist) * 180.0 / System.Math.PI);
        if (angle > coneDeg + apparent)
        {
            return false;
        }

        score = angle - apparent;
        return true;
    }

    // ---- Edge arrow ------------------------------------------------------------------------------------

    /// <summary>Where the edge arrow goes for a projected target (#2277): <paramref name="sx"/>/<paramref name="sy"/>
    /// /<paramref name="sz"/> is the camera's screen point (pixels, y up, z = depth). A point in front of the camera and
    /// at least <paramref name="margin"/> pixels inside the screen is <c>OnScreen</c> (the frame draws, no arrow).
    /// Otherwise the arrow sits on an ellipse inside the screen (<see cref="EllipseX"/>/<see cref="EllipseY"/> of the
    /// size) in the direction of the target; <c>AngleDeg</c> is that direction, counter-clockwise from screen-right.
    /// Behind the camera the projection flips through the centre, so the direction is mirrored back — a target behind
    /// and to the left points left, never forward; one dead behind the centre points down.</summary>
    public static (bool OnScreen, float X, float Y, float AngleDeg) EdgePlacement(float sx, float sy, float sz, float width, float height,
        float ellipseX = EllipseX, float ellipseY = EllipseY, float margin = 0f)
    {
        float cx = width * 0.5f, cy = height * 0.5f;
        bool behind = sz < 0f;
        if (!behind && sx >= margin && sx <= width - margin && sy >= margin && sy <= height - margin)
        {
            return (true, sx, sy, 0f);
        }

        float dx = sx - cx, dy = sy - cy;
        if (behind)
        {
            dx = -dx;
            dy = -dy;
        }

        if (dx * dx + dy * dy < 1e-6f)
        {
            dx = 0f;
            dy = -1f; // dead behind (or a degenerate projection): "turn round" reads as down
        }

        float a = System.Math.Max(1f, width * ellipseX), b = System.Math.Max(1f, height * ellipseY);
        float k = 1f / (float)System.Math.Sqrt(dx * dx / (a * a) + dy * dy / (b * b));
        float angle = (float)(System.Math.Atan2(dy, dx) * 180.0 / System.Math.PI);
        return (false, cx + dx * k, cy + dy * k, angle);
    }

    /// <summary>The z rotation (degrees) for a sprite that points UP at rest (<c>UiKit.TriangleSprite</c>) to point along
    /// <paramref name="angleDeg"/> (counter-clockwise from screen-right).</summary>
    public static float UpSpriteRotation(float angleDeg) => angleDeg - 90f;
}

/// <summary>
/// Notices hostiles that START attacking (#2277 auto-lock): fed the instance's candidates every frame, it remembers which
/// hostiles were inside the attack range at the previous look and reports the nearest newcomer. A hostile that was
/// already attacking when the player cleared the lock is not a newcomer, so clearing is never undone by the next frame.
/// </summary>
public sealed class AttackWatch
{
    private readonly HashSet<string> _attacking = new HashSet<string>(System.StringComparer.Ordinal);
    private readonly HashSet<string> _now = new HashSet<string>(System.StringComparer.Ordinal);

    /// <summary>How many hostiles were attacking at the last look.</summary>
    public int Count => _attacking.Count;

    /// <summary>True when <paramref name="id"/> was attacking at the last look.</summary>
    public bool IsAttacking(string id) => _attacking.Contains(id);

    /// <summary>One look at the instance; returns the id of the nearest hostile that started attacking since the last
    /// look, or null.</summary>
    public string? Update(IReadOnlyList<TargetCandidate> all, float shipX, float shipY, float shipZ)
    {
        _now.Clear();
        string? newcomer = null;
        float newcomerDist = float.MaxValue;
        for (int i = 0; i < all.Count; i++)
        {
            var c = all[i];
            if (c.IsPilot || c.IsTrader)
            {
                continue;
            }

            float d = SpaceTargeting.Distance(c, shipX, shipY, shipZ);
            if (!SpaceTargeting.IsAttacking(c.Kind, c.Hostile, d))
            {
                continue;
            }

            _now.Add(c.Id);
            if (!_attacking.Contains(c.Id) && d < newcomerDist)
            {
                newcomer = c.Id;
                newcomerDist = d;
            }
        }

        _attacking.Clear();
        foreach (var id in _now)
        {
            _attacking.Add(id);
        }

        return newcomer;
    }

    /// <summary>Forgets everything (a new flight).</summary>
    public void Reset()
    {
        _attacking.Clear();
        _now.Clear();
    }
}
