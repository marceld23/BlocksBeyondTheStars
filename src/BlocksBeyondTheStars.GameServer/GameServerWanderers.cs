// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// <b>Wanderers</b> (#2646, wilderness folk #2639): named travellers met out in the open land — a herb gatherer at a bush,
/// a prospector tapping at a rock, a woodcutter at a tree, a hunter on a herd's trail. They work a little near the player,
/// trade and talk, and walk on.
/// <list type="bullet">
/// <item><b>Which worlds</b> (<see cref="WanderersBelongHere"/>): the lodges' — terrain generation 25 or newer
/// (<see cref="WorldDescription.WildernessFolkGeneration"/>), <see cref="Shared.Configuration.ServerConfig.PlaceWanderers"/>
/// on, never a void, airless, ruined-only, gas, city or restricted extreme world, and not with the world's "Settlements"
/// option off. An existing save never meets one.</item>
/// <item><b>The cast</b> (<see cref="WandererRoster"/>): <see cref="WandererRosterMin"/>–<see cref="WandererRosterMax"/>
/// people per world, derived from the world seed and location on every load (RAM only, nothing saved): a name, a face, an
/// outfit and a profession of <c>data/lodges.json</c>, weighted by the planet like the lodges' (no hunter without huntable
/// fauna, no woodcutter without trees, no prospector without tier-1 ore) and spread before one repeats. Each is a person:
/// <see cref="ServerNpc.PersonKey"/> <c>wanderer:&lt;world&gt;:&lt;slot&gt;</c> (<see cref="IsWandererPersonKey"/>), so the
/// villagers' standing rules, the "People you know" list and the welcome-back work for them like for a villager.</item>
/// <item><b>An encounter</b> (<see cref="TryWandererEncounter"/>), checked every <see cref="WandererCheckSeconds"/> per
/// player on foot on this world: local daytime (<see cref="WandererDayStart"/>–<see cref="WandererDayEnd"/>), not underground,
/// at least <see cref="WandererPlaceClearance"/> m from every settlement (ruins too), base, lodge and bandit camp, nothing
/// hostile within 24 m, the player's quiet spell over (<see cref="WandererCooldownSeconds"/> × 0.8–1.2 of play since their
/// last encounter ended — and since they arrived), then a <see cref="WandererChance"/> roll. At most
/// <see cref="WanderersPerWorld"/> are out on a world, one per player, and nobody of the cast twice.</item>
/// <item><b>Spawn</b> on a 40–55 m ring round the player, out of the player's sight where any spot is
/// (<see cref="WandererSpawnCandidates"/>: real standable ground, not walled in, not in water or lava, not protected). The
/// task centre is a standable spot 15–25 m from the player towards the spawn, and the one who comes is a free member of the
/// cast with something to do there (a wild edible plant, an outcrop, a natural tree, an animal a hunter may take); up to
/// <see cref="WandererSidesTried"/> sides are looked at, else nobody comes this time.</item>
/// <item><b>The visit</b> (1 Hz, <see cref="StepWanderers"/>): <i>arriving</i> — walks to the task centre (A*);
/// <i>working</i> — the shared job state machine (<see cref="TickNpcWork"/>, GameServerNpcWork.cs) with
/// <see cref="NpcWorkPlan.RespectAllProtection"/>: never a lodge's plot, a settlement or a ruin; the prospector's sites are
/// the outcrops round the centre (<c>FindNpcOutcrops</c>, the cells fill up again). Up to 1–3 harvests, 1–2 cells, one tree
/// or one animal (and the hunter's three a day), or 3–5 minutes, whichever comes first; the yield goes into a small RAM
/// pack — no store crate, no present. <i>Leaving</i> — on along the bearing away from the player, gone where nobody sees
/// it (the base guests' rule: nobody within 32 at the end of its walk, or nobody within 24 after two minutes). Night falling,
/// the player more than <see cref="WandererHostLeaveRange"/> m away or gone from the world send it on its way at once.</item>
/// <item><b>A customer</b> within <see cref="WandererCustomerRange"/> stops it where it walks and it faces them (the
/// keeper-holds-the-counter rule, <c>KeeperCustomer</c>); a talk holds it like any NPC. Role <c>vendor</c> with the
/// profession's market theme, so the trade-or-talk prompt and the themed deals work unchanged.</item>
/// <item><b>Talk</b> (<c>data/dialogs.json</c> <c>wanderer_&lt;job&gt;</c>, home <see cref="DialogDefinition.HomeWander"/> —
/// never a lodge resident's, and a lodge resident's never theirs; authored + <c>aiPrompt</c>): what they are up to, where
/// they are going and their trade; a tip (<see cref="WandererTipConsequence"/>) that names the nearest hut nobody showed
/// the player yet and puts it on the map (<see cref="TryEmitLodgeHint"/>), else the lodge residents' tip (a wreck, a cache
/// for a friend, a camp, else the way to the village).</item>
/// <item><b>Playtest helpers</b> (admin, cheats on): <c>/wanderer</c> (<see cref="AdminSummonWanderer"/>) — an encounter
/// right now, without the quiet spell, the roll and the daytime (nor does the night send it off), someone of the free cast
/// even with nothing to do round the spot; and <c>/tp lodge [n]</c> (GameServerAdminTeleport.cs).</item>
/// </list>
/// Everything here is RAM only and per world (the location id keys every table); a world nobody ticked for
/// <see cref="WandererFrozenSeconds"/> sends its wanderers away when it ticks again.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The memory-key prefix of a wanderer (<c>wanderer:&lt;world&gt;:&lt;slot&gt;</c>).</summary>
    internal const string WandererKeyPrefix = "wanderer:";

    /// <summary>The dialogue consequence of a wanderer's tip: the nearest unrevealed hut, else the lodge residents' tip.</summary>
    internal const string WandererTipConsequence = "wandertip";

    /// <summary>The nameplate activity while a wanderer walks to its task or on its way.</summary>
    internal const string WandererActivityKey = "npc.activity.wandering";

    /// <summary>The prospector's nameplate role (a wanderer of the miner profession).</summary>
    internal const string ProspectorNameKey = "npc.role.prospector";

    /// <summary>The place an AI line says a wanderer is at.</summary>
    private const string WandererAiPlace = "on the road, out in the open land between the places people live";

    /// <summary>The situation of a wanderer's AI dialogue opening.</summary>
    private const string WandererAiSituation = "met by chance out in the open land, travelling and working along the way";

    /// <summary>The smallest cast of a world.</summary>
    internal const int WandererRosterMin = 4;

    /// <summary>The largest cast of a world.</summary>
    internal const int WandererRosterMax = 6;

    /// <summary>The most wanderers out on one world at once.</summary>
    internal const int WanderersPerWorld = 2;

    /// <summary>How often a player's encounter is checked.</summary>
    private const double WandererCheckSeconds = 60.0;

    /// <summary>A player's quiet spell between two encounters, in seconds of play (× 0.8–1.2).</summary>
    internal const double WandererCooldownSeconds = 720.0;

    /// <summary>A due check brings a wanderer this often.</summary>
    private const double WandererChance = 0.5;

    /// <summary>Encounters only in daylight at the player: local day fraction from …</summary>
    private const double WandererDayStart = 0.27;

    /// <summary>… to (the evening; a wanderer out there heads off at sunset, <c>IsNightAt</c>).</summary>
    private const double WandererDayEnd = 0.68;

    /// <summary>The player stands at least this far (m) from every settlement, base, lodge and camp for an encounter.</summary>
    internal const float WandererPlaceClearance = 60f;

    /// <summary>Spawn distances from the player.</summary>
    private static readonly float[] WandererSpawnRadii = { 40f, 47f, 55f };

    /// <summary>Bearings tried round the player, spread from a random one.</summary>
    private const int WandererSpawnBearings = 12;

    /// <summary>The most spawn sides whose task centre is looked round for work per encounter check.</summary>
    private const int WandererSidesTried = 4;

    /// <summary>The task centre's distances from the player towards the spawn, in the order tried.</summary>
    private static readonly float[] WandererTaskDistances = { 20f, 17f, 23f, 15f, 25f };

    /// <summary>How far round the task centre a wanderer looks for its work (a hunter's hunting ground).</summary>
    private const int WandererWorkRadius = 16;

    /// <summary>The prospector's outcrops: this far round the task centre …</summary>
    private const int WandererOutcropRadius = 12;

    /// <summary>… at most this many.</summary>
    private const int WandererOutcropCount = 3;

    /// <summary>The time a wanderer works at most: 3 …</summary>
    private const double WandererWorkMinSeconds = 180.0;

    /// <summary>… to 5 minutes from its arrival on the ring.</summary>
    private const double WandererWorkMaxSeconds = 300.0;

    /// <summary>A walk to the task centre that takes longer starts the work where it stands.</summary>
    private const double WandererArriveTimeout = 75.0;

    /// <summary>The wanderer counts as at its task centre within this distance.</summary>
    private const float WandererArriveReach = 3f;

    /// <summary>The player walked further than this from the wanderer: it goes on its way.</summary>
    internal const float WandererHostLeaveRange = 80f;

    /// <summary>A player this close stops a walking wanderer (the trade reach and a little more, like a keeper's).</summary>
    private const float WandererCustomerRange = 4.5f;

    /// <summary>Each step with a customer (or a talk) keeps the wanderer at least this much longer.</summary>
    private const double WandererCustomerHoldSeconds = 15.0;

    /// <summary>A player whose feet are this far under the natural surface is underground — no wanderer comes there.</summary>
    private const float WandererUndergroundDepth = 6f;

    /// <summary>The most a spawn spot may lie above or below the player's feet.</summary>
    private const int WandererSpawnRise = 10;

    /// <summary>The most a task centre may lie above or below the player's feet, and the spawn's.</summary>
    private const int WandererTaskRise = 8;

    /// <summary>The most different goods a wanderer's pack keeps (a new one pushes the oldest out).</summary>
    private const int WandererPackStacks = 6;

    /// <summary>The most of one good a wanderer's pack keeps.</summary>
    private const int WandererPackItemCap = 20;

    /// <summary>A world not ticked for this long (nobody there) sends its wanderers away when it ticks again.</summary>
    private const double WandererFrozenSeconds = 20.0;

    /// <summary>A wandering hunter's kills per in-game day at most (the lodge hunter's cap).</summary>
    internal const int WandererDailyKillCap = 3;

    /// <summary>One person of a world's cast (#2646) — derived, never saved.</summary>
    internal sealed class WandererMember
    {
        public int Slot;
        public string PersonKey = string.Empty;
        public string Name = string.Empty;
        public required LodgeProfessionDefinition Profession;

        /// <summary>The look generator's seed (<c>MakeNpc</c>): the same name, outfit and size at every encounter.</summary>
        public int Seed;

        /// <summary>A fixed client face seed: the same face at every encounter.</summary>
        public int FaceSeed;

        /// <summary>The hunter's kills per in-game day (<see cref="NpcWorkState.KillDay"/>/<see cref="NpcWorkState.KillsToday"/>
        /// carried over from one encounter to the next).</summary>
        public long KillDay = -1;

        public int KillsToday;

        /// <summary>What the work yielded (RAM only, capped): no store crate, no present.</summary>
        public readonly List<(string Item, int Count)> Pack = new();
    }

    /// <summary>Where a wanderer is in its visit.</summary>
    internal enum WandererPhase : byte
    {
        Arriving,
        Working,
        Leaving,
    }

    /// <summary>One wanderer out on a world (RAM only).</summary>
    internal sealed class WandererOut
    {
        public int NpcId;
        public required WandererMember Member;

        /// <summary>The player the encounter is with.</summary>
        public string HostId = string.Empty;

        public WandererPhase Phase;
        public double PhaseSince;
        public Vector3f Spawn;

        /// <summary>The task centre: where it works.</summary>
        public Vector3f Center;

        /// <summary>The bearing (radians) from the player to the spawn — the way it came; on leaving, away from the player.</summary>
        public double Bearing;

        /// <summary>Uptime after which it works no longer (3–5 minutes after its arrival on the ring).</summary>
        public double WorkUntil;

        /// <summary>Yields after which it moves on (1–3 harvests, 1–2 cells, one tree, one animal).</summary>
        public int YieldTarget;

        public NpcWorkPlan? Plan;

        /// <summary>A customer stopped it.</summary>
        public bool Held;

        /// <summary>Called by an admin (<c>/wanderer</c>): the evening does not send it away (a playtest at any hour).</summary>
        public bool Summoned;
    }

    /// <summary>Each world's cast (by location id), built on first need.</summary>
    private readonly Dictionary<string, List<WandererMember>> _wandererRosters = new(System.StringComparer.Ordinal);

    /// <summary>The wanderers out on each world (by location id).</summary>
    private readonly Dictionary<string, List<WandererOut>> _wanderersOut = new(System.StringComparer.Ordinal);

    /// <summary>Per-world 1 Hz throttle of <see cref="TickWanderers"/>.</summary>
    private readonly Dictionary<string, double> _wandererTickAt = new(System.StringComparer.Ordinal);

    /// <summary>Per world: the uptime of its last pass (a long gap = nobody was there).</summary>
    private readonly Dictionary<string, double> _wandererLastPassAt = new(System.StringComparer.Ordinal);

    /// <summary>Per player: the uptime of their next encounter check.</summary>
    private readonly Dictionary<string, double> _wandererNextCheckAt = new(System.StringComparer.Ordinal);

    /// <summary>Per player: the seconds of play left in their quiet spell.</summary>
    private readonly Dictionary<string, double> _wandererCooldownLeft = new(System.StringComparer.Ordinal);

    /// <summary>Per player: the uptime their quiet spell was last counted down at.</summary>
    private readonly Dictionary<string, double> _wandererCountedAt = new(System.StringComparer.Ordinal);

    /// <summary>The uptime of the next sweep of the per-player tables above: a player without a session leaves them (a
    /// rejoin arms a fresh quiet spell anyway), so a long-running server does not keep every id it ever saw.</summary>
    private double _wandererPruneAt;

    /// <summary>The players on the surface of the active world (reused per pass).</summary>
    private readonly List<PlayerSession> _wandererTargets = new();

    private readonly System.Random _wandererRng = new(0x2646);

    // ---------------- Who and where ----------------

    /// <summary>Whether a memory key is a wanderer's (#2646): <c>wanderer:&lt;world&gt;:&lt;slot&gt;</c>.</summary>
    internal static bool IsWandererPersonKey(string key)
    {
        if (string.IsNullOrEmpty(key) || !key.StartsWith(WandererKeyPrefix, System.StringComparison.Ordinal))
        {
            return false;
        }

        int colon = key.LastIndexOf(':');
        return colon >= WandererKeyPrefix.Length && colon < key.Length - 1 && key.Substring(colon + 1).All(char.IsDigit);
    }

    /// <summary>Whether a memory key is one of the wilderness folk's — a lodge resident (#2641) or a wanderer (#2646): a person
    /// like a villager for the standing rules (the first meeting's lift, the person's talk cooldown, a passing hello).</summary>
    internal static bool IsWildernessPersonKey(string key) => IsLodgePersonKey(key) || IsWandererPersonKey(key);

    /// <summary>Whether an NPC is a wanderer.</summary>
    private static bool IsWandererNpc(ServerNpc npc) => npc.LodgeKey.Length == 0 && IsWandererPersonKey(npc.PersonKey);

    /// <summary>A wanderer's memory key.</summary>
    private static string WandererPersonKey(string world, int slot)
        => WandererKeyPrefix + world + ":" + slot.ToString(CultureInfo.InvariantCulture);

    /// <summary>The nameplate role of a wanderer: the profession's, the miner's a prospector.</summary>
    private static string WandererNameKey(LodgeProfessionDefinition profession)
        => profession.Work == LodgeRules.WorkMine ? ProspectorNameKey : profession.NameKey;

    /// <summary>Whether wanderers come by on the active world at all (see the class summary).</summary>
    private bool WanderersBelongHere()
    {
        if (!_config.PlaceWanderers || _content.Lodges.Count == 0
            || _generator.TerrainGeneration < WorldDescription.WildernessFolkGeneration)
        {
            return false; // switched off, no professions, or an older world
        }

        var planet = _world.Planet;
        if (planet is null || planet.Void || planet.IsAirless || planet.RuinedSettlementsOnly || planet.IsGasWorld
            || planet.CityWorld.Length > 0
            || (planet.RestrictStructures && _generator.TerrainGeneration >= WorldDescription.ExtremePlanetsGeneration))
        {
            return false; // nobody travels a station, an airless rock, a toxic ruin world, a gas giant, the city or Titas/Valuma
        }

        return _meta.Description.Settlements.StructureFactor() > 0; // "Settlements: Off" — nobody out in the open either
    }

    /// <summary>The active world's cast (built on first need; derived from the world seed and location, never saved).</summary>
    private List<WandererMember> WandererRoster()
    {
        string world = _world.LocationId;
        if (!_wandererRosters.TryGetValue(world, out var roster))
        {
            roster = BuildWandererRoster();
            _wandererRosters[world] = roster;
        }

        return roster;
    }

    /// <summary>4–6 people, each with a profession the world calls for (the lodges' weights, spread before one repeats), a
    /// name and a look of their own seed. Empty when no profession fits the world.</summary>
    private List<WandererMember> BuildWandererRoster()
    {
        var roster = new List<WandererMember>();
        var planet = _world.Planet;
        if (planet is null || _content.Lodges.Count == 0)
        {
            return roster;
        }

        string world = _world.LocationId;
        var traits = LodgeTraits(planet, Tier1OresOf(planet));
        long rSeed = _meta.Seed ^ WorldGenerator.StableHash("wanderers:" + world);
        var rng = new System.Random(unchecked((int)(rSeed ^ (rSeed >> 32))));
        int count = rng.Next(WandererRosterMin, WandererRosterMax + 1);
        var used = new List<string>();
        for (int slot = 0; slot < count; slot++)
        {
            if (PickLodgeProfession(traits, used, rng) is not { } profession)
            {
                break; // nothing on this world calls for anyone to travel it
            }

            used.Add(profession.Key);
            string tag = world + ":" + slot.ToString(CultureInfo.InvariantCulture);
            int seed = unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash("wanderer:" + tag)));
            roster.Add(new WandererMember
            {
                Slot = slot,
                PersonKey = WandererPersonKey(world, slot),
                Profession = profession,
                Seed = seed,
                FaceSeed = unchecked((int)WorldGenerator.StableHash("wanderer-face:" + tag)) | 1,
                Name = NameGenerator.Person(new System.Random(seed)), // MakeNpc's first draw from the same seed
            });
        }

        return roster;
    }

    /// <summary>The wanderers out on the active world.</summary>
    private List<WandererOut> WanderersOut()
    {
        string world = _world.LocationId;
        if (!_wanderersOut.TryGetValue(world, out var list))
        {
            list = new List<WandererOut>();
            _wanderersOut[world] = list;
        }

        return list;
    }

    // ---------------- Tick ----------------

    /// <summary>Per-world 1 Hz pass: every wanderer's visit takes a step, then each player's encounter check (at most one
    /// new wanderer per pass).</summary>
    private void TickWanderers(double dt)
    {
        _ = dt;
        if (_worlds.Active is null)
        {
            return;
        }

        string world = _world.LocationId;
        if (_wandererTickAt.TryGetValue(world, out double due) && _uptime < due)
        {
            return;
        }

        _wandererTickAt[world] = _uptime + 1.0;
        bool frozen = _wandererLastPassAt.TryGetValue(world, out double last) && _uptime - last > WandererFrozenSeconds;
        _wandererLastPassAt[world] = _uptime;
        StepWanderers(frozen);
        ScheduleWanderers();
        PruneWandererPlayerTables();
    }

    /// <summary>Once a minute: drops the per-player entries of players who have no session any more.</summary>
    private void PruneWandererPlayerTables()
    {
        if (_uptime < _wandererPruneAt)
        {
            return;
        }

        _wandererPruneAt = _uptime + 60.0;
        var live = new HashSet<string>(_sessions.Values.Select(s => s.State.PlayerId), System.StringComparer.Ordinal);
        foreach (var table in new[] { _wandererNextCheckAt, _wandererCooldownLeft, _wandererCountedAt })
        {
            foreach (string pid in table.Keys.Where(k => !live.Contains(k)).ToList())
            {
                table.Remove(pid);
            }
        }
    }

    /// <summary>Test seam: how many player ids the per-player wanderer tables hold (all three together).</summary>
    public int WandererPlayerTableSizeForTest
        => _wandererNextCheckAt.Count + _wandererCooldownLeft.Count + _wandererCountedAt.Count;

    /// <summary>Test seam: runs the sweep of the per-player tables now.</summary>
    public void PruneWandererPlayerTablesForTest()
    {
        _wandererPruneAt = 0;
        PruneWandererPlayerTables();
    }

    /// <summary>Counts every player's quiet spell down and runs the due encounter checks.</summary>
    private void ScheduleWanderers()
    {
        bool belong = WanderersBelongHere();
        var outs = WanderersOut();
        foreach (var s in JoinedInActiveWorld().ToList())
        {
            string pid = s.State.PlayerId;
            CountWandererCooldown(pid, hosting: outs.Any(w => w.HostId == pid));
            if (!belong || s.Spectating)
            {
                continue;
            }

            if (_wandererNextCheckAt.TryGetValue(pid, out double next) && _uptime < next)
            {
                continue;
            }

            _wandererNextCheckAt[pid] = _uptime + WandererCheckSeconds;
            if (TryWandererEncounter(s, roll: true, admin: false, out _) is not null)
            {
                return; // one new wanderer per pass
            }
        }
    }

    /// <summary>A player's quiet spell runs only while they play (at most two seconds per pass — a gap is no play) and
    /// stands still while their own wanderer is out. The first sight arms a whole spell: a player who just arrived plays a
    /// while before anyone comes by.</summary>
    private void CountWandererCooldown(string playerId, bool hosting)
    {
        if (!_wandererCooldownLeft.TryGetValue(playerId, out double left))
        {
            _wandererCooldownLeft[playerId] = WandererCooldownSeconds * (0.8 + _wandererRng.NextDouble() * 0.4);
            _wandererCountedAt[playerId] = _uptime;
            return;
        }

        double since = _wandererCountedAt.TryGetValue(playerId, out double at) ? _uptime - at : 0.0;
        _wandererCountedAt[playerId] = _uptime;
        if (!hosting && left > 0)
        {
            _wandererCooldownLeft[playerId] = System.Math.Max(0.0, left - System.Math.Clamp(since, 0.0, 2.0));
        }
    }

    // ---------------- Encounter ----------------

    /// <summary>
    /// An encounter for <paramref name="s"/> now (see the class summary), or null with the <paramref name="reason"/> (the
    /// admin's answer key: <c>none_here</c>, <c>on_foot</c>, <c>cooldown</c>, <c>night</c>, <c>too_close</c>, <c>hostile</c>,
    /// <c>busy</c>, <c>roll</c>, <c>no_spot</c>, <c>no_work</c>). <paramref name="admin"/> (<c>/wanderer</c>) skips the quiet
    /// spell, the daytime and the roll, and when nobody has anything to do round the spot one of the free cast comes anyway
    /// (<paramref name="anyWork"/> alone does only the latter). <paramref name="pick"/> narrows the cast and
    /// <paramref name="bearing"/> sets the preferred spawn bearing (tests).
    /// </summary>
    private WandererOut? TryWandererEncounter(PlayerSession s, bool roll, bool admin, out string reason,
        System.Func<WandererMember, bool>? pick = null, double? bearing = null, bool anyWork = false)
    {
        reason = "none_here";
        if (!WanderersBelongHere())
        {
            return null;
        }

        reason = "on_foot";
        if (!GuestOwnerOnFoot(s))
        {
            return null;
        }

        string pid = s.State.PlayerId;
        var at = s.State.Position;
        reason = "cooldown";
        if (!admin && _wandererCooldownLeft.TryGetValue(pid, out double left) && left > 0)
        {
            return null;
        }

        reason = "night";
        double day = LocalDayFraction(at);
        if (!admin && (day < WandererDayStart || day > WandererDayEnd))
        {
            return null;
        }

        reason = "too_close";
        if (WandererPlaceNear(at, WandererPlaceClearance))
        {
            return null;
        }

        reason = "hostile";
        if (GuestHostileNear(at))
        {
            return null;
        }

        reason = "busy";
        var outs = WanderersOut();
        if (outs.Count >= WanderersPerWorld || outs.Any(w => w.HostId == pid))
        {
            return null;
        }

        var free = WandererRoster()
            .Where(m => outs.All(w => w.Member.Slot != m.Slot) && (pick is null || pick(m)))
            .ToList();
        if (free.Count == 0)
        {
            return null;
        }

        reason = "roll";
        if (roll && !admin && _wandererRng.NextDouble() >= WandererChance)
        {
            return null;
        }

        reason = "no_spot";
        if (WandererUnderground(at))
        {
            return null;
        }

        // The spawn sides in order (out of sight first), each with its task centre: the first side where somebody of the
        // free cast finds work wins — or, for the admin, the first side at all. A few sides at most (each looks round).
        var sides = new HashSet<int>();
        foreach (var (spawn, spawnBearing, side) in WandererSpawnCandidates(s, bearing ?? _wandererRng.NextDouble() * System.Math.PI * 2.0))
        {
            if (sides.Contains(side) || WandererTaskCenter(at, spawnBearing, spawn.Y) is not { } center)
            {
                continue;
            }

            sides.Add(side);
            var professionWorks = new Dictionary<string, bool>(System.StringComparer.Ordinal);
            var working = free.Where(m => WandererHasWorkAt(m, center, professionWorks)).ToList();
            if (working.Count == 0 && !admin && !anyWork)
            {
                if (sides.Count >= WandererSidesTried)
                {
                    break;
                }

                continue; // nothing for anyone to do on this side
            }

            var pool = working.Count > 0 ? working : free;
            reason = string.Empty;
            var w = SpawnWanderer(pool[_wandererRng.Next(pool.Count)], s, spawn, center, spawnBearing);
            w.Summoned = admin;
            return w;
        }

        reason = sides.Count > 0 ? "no_work" : "no_spot"; // nothing for anyone to do round here: nobody comes by
        return null;
    }

    /// <summary>Whether a point lies within <paramref name="range"/> of a settlement's box (a ruin's too), a bandit camp's,
    /// a lodge's footprint or a base's zone on this world.</summary>
    private bool WandererPlaceNear(Vector3f at, float range)
    {
        foreach (var st in _settlements)
        {
            if (WandererBoxWithin(at, st.Min.X, st.Min.Z, st.Max.X, st.Max.Z, range))
            {
                return true;
            }
        }

        foreach (var c in _banditCamps)
        {
            if (WandererBoxWithin(at, c.Min.X, c.Min.Z, c.Max.X, c.Max.Z, range))
            {
                return true;
            }
        }

        foreach (var l in _lodges)
        {
            if (WandererBoxWithin(at, l.Origin.X, l.Origin.Z, l.Origin.X + l.Layout.Width - 1, l.Origin.Z + l.Layout.Length - 1, range))
            {
                return true;
            }
        }

        foreach (var b in _bases)
        {
            if (b.Planet == _world.LocationId && WandererBoxWithin(at, b.Cell.X - BaseProtectionRadius, b.Cell.Z - BaseProtectionRadius,
                    b.Cell.X + BaseProtectionRadius, b.Cell.Z + BaseProtectionRadius, range))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a point lies within <paramref name="range"/> (horizontally, the short way round the seams) of an
    /// inclusive cell box.</summary>
    private bool WandererBoxWithin(Vector3f at, int minX, int minZ, int maxX, int maxZ, float range)
    {
        int circ = _world.Circumference;
        double cx = (minX + maxX + 1) * 0.5, cz = (minZ + maxZ + 1) * 0.5;
        double hw = (maxX - minX + 1) * 0.5, hl = (maxZ - minZ + 1) * 0.5;
        double dx = System.Math.Max(0.0, System.Math.Abs(WorldConstants.WrapDeltaX(at.X - cx, circ)) - hw);
        double dz = System.Math.Max(0.0, System.Math.Abs(WorldConstants.WrapDeltaZ(at.Z - cz, circ)) - hl);
        return dx * dx + dz * dz <= (double)range * range;
    }

    /// <summary>Whether the player's feet are well under the natural surface (a cave, a mine): no wanderer comes there.</summary>
    private bool WandererUnderground(Vector3f at)
    {
        int x = (int)System.Math.Floor(at.X), z = (int)System.Math.Floor(at.Z);
        return at.Y < _generator.SurfaceHeight(_world.Planet, x, z) - WandererUndergroundDepth;
    }

    /// <summary>Where nobody may appear or work: a settlement's box, a lodge, a ruin, a base's zone, a station, a factory,
    /// the Guardian core, a ship.</summary>
    private bool WandererSpotProtected(Vector3i cell) => GatherCellProtected(cell) || NpcCellStrictlyProtected(cell);

    /// <summary>
    /// The spawn spots on the rings <see cref="WandererSpawnRadii"/> round the player (<see cref="WandererSpawnBearings"/>
    /// bearings spread from <paramref name="prefer"/>, <c>Side</c> = the bearing's index): real standable ground within
    /// <see cref="WandererSpawnRise"/> of the player's feet, not walled in, not in a sealed room, water or lava
    /// (<see cref="BanditColumnRejected"/>), not protected — those the player cannot see first (it is simply there when
    /// they look), then the rest (open land, where every spot is in sight), each group in bearing order.
    /// </summary>
    private List<(Vector3f Spot, double Bearing, int Side)> WandererSpawnCandidates(PlayerSession s, double prefer)
    {
        var o = s.State.Position;
        int refY = (int)System.Math.Floor(o.Y);
        int circ = _world.Circumference;
        var hidden = new List<(Vector3f Spot, double Bearing, int Side)>();
        var seen = new List<(Vector3f Spot, double Bearing, int Side)>();
        for (int k = 0; k < WandererSpawnBearings; k++)
        {
            int step = (k + 1) / 2 * (k % 2 == 1 ? 1 : -1); // 0, +1, −1, +2, −2 … — nearest the preferred bearing first
            double a = prefer + step * (System.Math.PI * 2.0 / WandererSpawnBearings);
            foreach (float radius in WandererSpawnRadii)
            {
                int x = WorldConstants.WrapX((int)System.Math.Floor(o.X + System.Math.Cos(a) * radius), circ);
                int z = (int)System.Math.Floor(o.Z + System.Math.Sin(a) * radius);
                if (!TryGroundFeetYAt(x, z, refY, CreatureBodyMinHeight, CreatureWideGroundScan, out int feet)
                    || System.Math.Abs(feet - refY) > WandererSpawnRise
                    || StandableSpot(x, feet, z) is not { } spot || BanditColumnRejected(x, feet, z)
                    || WandererSpotProtected(new Vector3i(x, feet, z)))
                {
                    continue;
                }

                (HasLineOfSight(o, spot) ? seen : hidden).Add((spot, a, k));
            }
        }

        hidden.AddRange(seen);
        return hidden;
    }

    /// <summary>The task centre: a standable spot 15–25 m from the player on <paramref name="bearing"/> (the spawn's side),
    /// within <see cref="WandererTaskRise"/> of the player's feet and of the spawn's (<paramref name="spawnY"/> — the walk's
    /// route search reaches eight blocks up or down) and not protected; null when none fits.</summary>
    private Vector3f? WandererTaskCenter(Vector3f player, double bearing, float spawnY)
    {
        int refY = (int)System.Math.Floor(player.Y);
        int fromY = (int)System.Math.Floor(spawnY);
        int circ = _world.Circumference;
        foreach (float d in WandererTaskDistances)
        {
            int x = WorldConstants.WrapX((int)System.Math.Floor(player.X + System.Math.Cos(bearing) * d), circ);
            int z = (int)System.Math.Floor(player.Z + System.Math.Sin(bearing) * d);
            if (TryNpcGroundFeetYAt(x, z, refY, out int feet) && System.Math.Abs(feet - refY) <= WandererTaskRise
                && System.Math.Abs(feet - fromY) <= WandererTaskRise
                && StandableSpot(x, feet, z) is { } spot && !WandererSpotProtected(new Vector3i(x, feet, z)))
            {
                return spot;
            }
        }

        return null;
    }

    /// <summary>Whether a member of the cast finds work round a task centre: a wild edible plant, an outcrop, a natural tree,
    /// an animal a hunter may take (and the hunter has not had its three today). <paramref name="memo"/> keeps each
    /// profession's search for this centre (two of the cast with one profession look once).</summary>
    private bool WandererHasWorkAt(WandererMember m, Vector3f center, Dictionary<string, bool> memo)
    {
        if (m.Profession.Work == LodgeRules.WorkHunt && m.KillDay == MarketDay && m.KillsToday >= WandererDailyKillCap)
        {
            return false; // enough for today
        }

        if (!memo.TryGetValue(m.Profession.Work, out bool works))
        {
            works = m.Profession.Work switch
            {
                LodgeRules.WorkMine => FindNpcOutcrops(center, WandererOutcropRadius, 1).Count > 0,
                LodgeRules.WorkChop => FindNpcTrees(center, WandererWorkRadius, 1, respectAllProtection: true).Count > 0,
                LodgeRules.WorkHunt => HuntableNear(center, WandererWorkRadius),
                _ => FindNpcGatherSites(center, WandererWorkRadius, 1, respectAllProtection: true).Count > 0,
            };
            memo[m.Profession.Work] = works;
        }

        return works;
    }

    /// <summary>Whether an animal a hunter may take stands within <paramref name="range"/> of a point, outside every
    /// protected place.</summary>
    private bool HuntableNear(Vector3f center, float range)
    {
        double rangeSq = (double)range * range;
        foreach (var c in _creatures)
        {
            if (WrapDistSq(c.Position, center) <= rangeSq && NpcMayHunt(c) && !NpcHuntGroundProtected(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Puts a member of the cast on the ring and sends it walking to its task (see the class summary).</summary>
    private WandererOut SpawnWanderer(WandererMember m, PlayerSession host, Vector3f spawn, Vector3f center, double bearing)
    {
        var profession = m.Profession;
        var npc = MakeNpc("vendor", profession.Theme, robotic: false, spawn, new System.Random(m.Seed));
        npc.Name = m.Name;
        npc.Job = profession.Key;
        npc.NameKey = WandererNameKey(profession);
        npc.Held = profession.Held;
        npc.Theme = profession.Theme;
        npc.PersonKey = m.PersonKey;
        npc.FaceSeed = m.FaceSeed;
        npc.RoutineEnabled = false; // the visit owns its goals (no bed, no bench, no day)
        npc.Leash = 0.6f;
        npc.ActivityKey = WandererActivityKey;
        _npcs.Add(npc);

        var w = new WandererOut
        {
            NpcId = npc.Id,
            Member = m,
            HostId = host.State.PlayerId,
            Phase = WandererPhase.Arriving,
            PhaseSince = _uptime,
            Spawn = spawn,
            Center = center,
            Bearing = bearing,
            WorkUntil = _uptime + WandererWorkMinSeconds + _wandererRng.NextDouble() * (WandererWorkMaxSeconds - WandererWorkMinSeconds),
            YieldTarget = profession.Work switch
            {
                LodgeRules.WorkGather => _wandererRng.Next(1, 4),
                LodgeRules.WorkMine => _wandererRng.Next(1, 3),
                _ => 1,
            },
        };
        WanderersOut().Add(w);
        SetNpcGoal(npc, center, NpcArrival.None, 1.5f);
        BroadcastNpcs();
        if (AiLinesEnabled)
        {
            WarmDialogOpening(host, npc, NpcKeyForNpc(host, npc)); // the walk-in covers the re-wording's latency
        }

        _log.Info($"Wanderers: {profession.Key} '{npc.Name}' comes by '{host.State.Name}' on '{_world.LocationId}'.");
        return w;
    }

    // ---------------- The visit ----------------

    /// <summary>One step of every wanderer's visit on the active world (see the class summary).</summary>
    private void StepWanderers(bool frozen)
    {
        var outs = WanderersOut();
        if (outs.Count == 0)
        {
            return;
        }

        _wandererTargets.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if ((!_shipPlaced || !s.State.AboardShip) && !InSpace(s.State.PlayerId))
            {
                _wandererTargets.Add(s);
            }
        }

        for (int i = outs.Count - 1; i >= 0; i--)
        {
            var w = outs[i];
            var npc = _npcs.FirstOrDefault(n => n.Id == w.NpcId && n.PersonKey == w.Member.PersonKey);
            if (npc is null || frozen)
            {
                EndWanderer(w, npc); // gone already, or nobody was here for a while — the visit is over
                continue;
            }

            if (_uptime < npc.TalkHoldUntil)
            {
                w.WorkUntil = System.Math.Max(w.WorkUntil, _uptime + WandererCustomerHoldSeconds);
                continue; // a player is talking with it: everything waits
            }

            var host = FindSessionByPlayerId(w.HostId);
            bool hostHere = GuestOwnerOnFoot(host);
            if (w.Phase != WandererPhase.Leaving)
            {
                if (!hostHere && !AnyPlayerWithin(npc.Pos, BaseGuestDespawnUnseenRange))
                {
                    EndWanderer(w, npc); // the player left the world or boarded: nobody sees it go
                    continue;
                }

                if (!hostHere || WrapDistSq(host!.State.Position, npc.Pos) > WandererHostLeaveRange * (double)WandererHostLeaveRange
                    || (!w.Summoned && IsNightAt(npc.Pos))) // night falls: it goes on its way
                {
                    BeginWandererLeaving(w, npc, host);
                    continue;
                }
            }

            bool customer = WandererCustomerNear(npc);
            if (customer)
            {
                HoldWanderer(w, npc);
                if (w.Phase != WandererPhase.Working)
                {
                    continue; // it stands and faces them (MoveNpcs' keeper rule)
                }
            }
            else if (w.Held)
            {
                ResumeWanderer(w, npc);
            }

            switch (w.Phase)
            {
                case WandererPhase.Arriving:
                    StepWandererArriving(w, npc);
                    break;
                case WandererPhase.Working:
                    StepWandererWorking(w, npc, host);
                    break;
                default:
                    StepWandererLeaving(w, npc);
                    break;
            }
        }
    }

    private void StepWandererArriving(WandererOut w, ServerNpc npc)
    {
        bool there = npc.Goal is null || WrapDistSq(npc.Pos, w.Center) <= WandererArriveReach * (double)WandererArriveReach;
        if (there || npc.PathFailures >= 2 || _uptime - w.PhaseSince > WandererArriveTimeout)
        {
            StartWandererWork(w, npc); // arrived — or no way there: it works from where it stands
        }
    }

    /// <summary>The work starts: the plan of its profession round the task centre (the prospector's outcrops fixed now).</summary>
    private void StartWandererWork(WandererOut w, ServerNpc npc)
    {
        w.Phase = WandererPhase.Working;
        w.PhaseSince = _uptime;
        var m = w.Member;
        var profession = m.Profession;
        var kind = profession.Work switch
        {
            LodgeRules.WorkMine => NpcWorkKind.Mine,
            LodgeRules.WorkChop => NpcWorkKind.Chop,
            LodgeRules.WorkHunt => NpcWorkKind.Hunt,
            _ => NpcWorkKind.Gather,
        };
        var outcrops = kind == NpcWorkKind.Mine ? FindNpcOutcrops(w.Center, WandererOutcropRadius, WandererOutcropCount) : null;
        w.Plan = new NpcWorkPlan
        {
            Kind = kind,
            Center = w.Center,
            Radius = WandererWorkRadius,
            FixedSites = outcrops is { Count: > 0 } ? outcrops : null,
            Bank = (item, count) => BankIntoWandererPack(m, item, count),
            SiteAllowed = c => !WandererSpotProtected(c),
            ActivityKey = profession.ActivityKey,
            Held = profession.Held,
            DailyKillCap = WandererDailyKillCap, // per wanderer and in-game day, carried from one encounter to the next
            RespectAllProtection = true, // never a lodge's plot, a settlement or a ruin
        };
        npc.WorkState = new NpcWorkState { Plan = w.Plan, KillDay = m.KillDay, KillsToday = m.KillsToday };
        npc.ActivityKey = string.Empty;
        BroadcastNpcs();
    }

    private void StepWandererWorking(WandererOut w, ServerNpc npc, PlayerSession? host)
    {
        if (w.Plan is not { } plan || npc.WorkState is not { } ws)
        {
            StartWandererWork(w, npc);
            return;
        }

        TickNpcWork(npc, plan, _wandererTargets);
        w.Member.KillDay = ws.KillDay;
        w.Member.KillsToday = ws.KillsToday;

        bool busy = ws.Stage is NpcWorkStage.Working or NpcWorkStage.Felling or NpcWorkStage.Aiming;
        if (!busy && (ws.Yields >= w.YieldTarget || _uptime >= w.WorkUntil))
        {
            BeginWandererLeaving(w, npc, host); // done for today — or enough time spent here
        }
    }

    /// <summary>Time to go: on along the bearing away from the player (or the way it came), walking out of sight.</summary>
    private void BeginWandererLeaving(WandererOut w, ServerNpc npc, PlayerSession? host)
    {
        w.Phase = WandererPhase.Leaving;
        w.PhaseSince = _uptime;
        w.Held = false;
        if (npc.WorkState is { } ws)
        {
            ResetNpcWork(npc, ws); // drops the machine's own walk; a tree already coming down still comes down
        }

        npc.ActivityKey = WandererActivityKey;
        npc.AttendUntil = 0;
        if (host is not null && GuestOwnerOnFoot(host) && WrapDistSq(host.State.Position, npc.Pos) > 1.0)
        {
            w.Bearing = BearingFrom(host.State.Position, npc.Pos);
        }

        WandererWalkOn(w, npc);
        BroadcastNpcs();
    }

    /// <summary>Sends a leaving wanderer as far out along its bearing as open ground allows, or lets it stand.</summary>
    private void WandererWalkOn(WandererOut w, ServerNpc npc)
    {
        foreach (float d in new[] { 36f, 30f, 24f, 18f, 12f })
        {
            if (GuestSpotAlong(npc.Pos, w.Bearing, d) is { } spot)
            {
                SetNpcGoal(npc, spot, NpcArrival.None, 0.6f);
                return;
            }
        }

        StopGuestWalking(npc);
    }

    /// <summary>The base guests' leave rule: gone at the end of its walk when nobody is within 32, or after two minutes
    /// with nobody within 24; still watched at the end of its walk, it walks on a little.</summary>
    private void StepWandererLeaving(WandererOut w, ServerNpc npc)
    {
        if (_uptime - w.PhaseSince > BaseGuestHardDespawnSeconds && !AnyPlayerWithin(npc.Pos, BaseGuestHardDespawnRange))
        {
            EndWanderer(w, npc);
            return;
        }

        if (npc.Goal is not null)
        {
            return; // on its way
        }

        if (!AnyPlayerWithin(npc.Pos, BaseGuestDespawnUnseenRange))
        {
            EndWanderer(w, npc); // nobody sees it go
            return;
        }

        if (GuestSpotAlong(npc.Pos, w.Bearing, BaseGuestLeaveStep) is { } further)
        {
            SetNpcGoal(npc, further, NpcArrival.None, 0.6f);
        }
    }

    /// <summary>Whether a player on foot stands within <see cref="WandererCustomerRange"/> of the wanderer.</summary>
    private bool WandererCustomerNear(ServerNpc npc)
    {
        double rangeSq = WandererCustomerRange * (double)WandererCustomerRange;
        foreach (var s in _wandererTargets)
        {
            if (!s.Spectating && WrapDistSq(s.State.Position, npc.Pos) <= rangeSq)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A customer came over: a walk stops where it is (its spot becomes the keeper's counter — MoveNpcs turns it to
    /// face them), and the visit lasts a little longer. Work in hand (a harvest, a tree coming down, an aimed shot) is
    /// finished; no new walk starts while they stand there (<c>KeeperCustomer</c>).</summary>
    private void HoldWanderer(WandererOut w, ServerNpc npc)
    {
        w.WorkUntil = System.Math.Max(w.WorkUntil, _uptime + WandererCustomerHoldSeconds);
        if (w.Phase == WandererPhase.Leaving)
        {
            w.PhaseSince = _uptime; // no "two minutes on its way" while somebody trades
        }

        bool walking = npc.Goal is not null;
        if (npc.WorkState is { Stage: NpcWorkStage.Walking } ws)
        {
            ResetNpcWork(npc, ws);
            walking = true;
        }

        if (walking || !w.Held)
        {
            StopGuestWalking(npc); // its home is here now — the keeper's spot the customer is measured from
            BroadcastNpcs();
        }

        w.Held = true;
    }

    /// <summary>The customer went: on with the visit — the walk it was on is taken up again (the work machine picks its next
    /// site by itself).</summary>
    private void ResumeWanderer(WandererOut w, ServerNpc npc)
    {
        w.Held = false;
        switch (w.Phase)
        {
            case WandererPhase.Arriving:
                SetNpcGoal(npc, w.Center, NpcArrival.None, 1.5f);
                break;
            case WandererPhase.Leaving:
                WandererWalkOn(w, npc);
                break;
        }
    }

    /// <summary>Takes a wanderer out of the world; its player's quiet spell starts now.</summary>
    private void EndWanderer(WandererOut w, ServerNpc? npc)
    {
        if (npc is not null)
        {
            _npcs.Remove(npc);
        }

        WanderersOut().Remove(w);
        _wandererCooldownLeft[w.HostId] = WandererCooldownSeconds * (0.8 + _wandererRng.NextDouble() * 0.4);
        _wandererCountedAt[w.HostId] = _uptime;
        BroadcastNpcs();
        _log.Info($"Wanderers: '{w.Member.Name}' moved on from '{_world.LocationId}'.");
    }

    /// <summary>A yield into a wanderer's pack: stacks merge up to <see cref="WandererPackItemCap"/>, a new good in a full pack
    /// (<see cref="WandererPackStacks"/> kinds) pushes the oldest out — sold on the way.</summary>
    private static void BankIntoWandererPack(WandererMember m, string item, int count)
    {
        if (count <= 0 || string.IsNullOrEmpty(item))
        {
            return;
        }

        int at = m.Pack.FindIndex(s => s.Item == item);
        if (at >= 0)
        {
            m.Pack[at] = (item, System.Math.Min(WandererPackItemCap, m.Pack[at].Count + count));
            return;
        }

        while (m.Pack.Count >= WandererPackStacks)
        {
            m.Pack.RemoveAt(0);
        }

        m.Pack.Add((item, System.Math.Min(WandererPackItemCap, count)));
    }

    // ---------------- Talk ----------------

    /// <summary>"Seen anything interesting on your way?" (#2646): the nearest hut the map does not show yet ("Tilda the
    /// Gatherer lives in a hut …", revealed — <see cref="TryEmitLodgeHint"/>); else the lodge residents' tip (a wreck, a cache
    /// for someone the wanderer knows, a camp, else the way to the nearest village); with nothing at all, the authored
    /// "nothing new".</summary>
    private void ApplyWandererTip(PlayerSession session, ServerNpc npc)
    {
        string tip = TryEmitLodgeHint(session);
        if (tip.Length == 0)
        {
            bool known = RelationshipTier(RelationshipValue(session.State, NpcKeyForNpc(session, npc))) is "known" or "trusted";
            tip = LodgeTipLine(session, shareCaches: known);
        }

        if (tip.Length > 0)
        {
            _dialogResponseOverride = tip;
        }
    }

    // ---------------- Playtest helper ----------------

    /// <summary><c>/wanderer</c> (#2646, admin, cheats on): one of this world's wanderers comes by the calling admin right
    /// now — without the quiet spell, the roll and the daytime; everything else (the world, on foot, clear of places and
    /// hostiles, room on the world, a spawn spot) still applies, and the answer says which rule said no.</summary>
    private void AdminSummonWanderer(PlayerSession session)
    {
        Serve(session);
        var w = TryWandererEncounter(session, roll: false, admin: true, out string reason);
        if (w is null)
        {
            Reject(session, "admin", "@srv.admin.wanderer_" + reason);
            return;
        }

        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.admin.wanderer_coming")
                .Replace("{name}", w.Member.Name)
                .Replace("{job}", Localize(session.Locale, WandererNameKey(w.Member.Profession))),
        });
        CheatLog(session.State, $"called the wanderer '{w.Member.Name}' ({w.Member.Profession.Key})");
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/inspection (#2646): the active world's cast — slot, name, person key and profession.</summary>
    public IReadOnlyList<(int Slot, string Name, string PersonKey, string Job)> WandererRosterForTest
        => WandererRoster().Select(m => (m.Slot, m.Name, m.PersonKey, m.Profession.Key)).ToList();

    /// <summary>Test/inspection (#2646): the wanderers out on the active world.</summary>
    public IReadOnlyList<(int NpcId, int Slot, string Name, string PersonKey, string Job, string Phase, Vector3f Spawn, Vector3f Center, string HostId, int YieldTarget)> WanderersForTest
        => WanderersOut().Select(w => (w.NpcId, w.Member.Slot, w.Member.Name, w.Member.PersonKey, w.Member.Profession.Key,
            w.Phase.ToString(), w.Spawn, w.Center, w.HostId, w.YieldTarget)).ToList();

    /// <summary>Test seam (#2646): an encounter for the player now — every rule but the once-a-minute check and the roll
    /// (<paramref name="job"/> and <paramref name="slot"/> narrow the cast, <paramref name="bearing"/> is the preferred spawn
    /// side, <paramref name="anyWork"/> lets a member come with nothing to do round the spot). Returns the wanderer's NPC
    /// id, or null.</summary>
    public int? WandererEncounterForTest(string playerId, string? job = null, double? bearing = null, int? slot = null,
        bool anyWork = false)
        => WandererEncounterReasonForTest(playerId, out _, job, bearing, slot, anyWork);

    /// <summary>Test seam (#2646): <see cref="WandererEncounterForTest"/> with the refusal's reason ("" on success).</summary>
    public int? WandererEncounterReasonForTest(string playerId, out string reason, string? job = null, double? bearing = null,
        int? slot = null, bool anyWork = false)
    {
        reason = "no_player";
        if (FindSessionByPlayerId(playerId) is not { } s)
        {
            return null;
        }

        Serve(s);
        return TryWandererEncounter(s, roll: false, admin: false, out reason,
            m => (job is null || m.Profession.Key == job) && (slot is null || m.Slot == slot), bearing, anyWork)?.NpcId;
    }

    /// <summary>Test seam (#2646): whether wanderers come by on the active world at all.</summary>
    public bool WanderersBelongHereForTest => WanderersBelongHere();

    /// <summary>Test seam (#2646): one step of every wanderer's visit now (skips the 1 Hz throttle).</summary>
    public void StepWanderersForTest() => StepWanderers(frozen: false);

    /// <summary>Test seam (#2646): the seconds of play left in a player's quiet spell (0 = none; −1 = never seen).</summary>
    public double WandererCooldownForTest(string playerId)
        => _wandererCooldownLeft.TryGetValue(playerId, out double left) ? left : -1.0;

    /// <summary>Test seam (#2646): sets a player's quiet spell.</summary>
    public void SetWandererCooldownForTest(string playerId, double seconds)
    {
        _wandererCooldownLeft[playerId] = seconds;
        _wandererCountedAt[playerId] = _uptime;
    }

    /// <summary>Test seam (#2646): a cast member's pack.</summary>
    public IReadOnlyList<(string Item, int Count)> WandererPackForTest(int slot)
        => WandererRoster().FirstOrDefault(m => m.Slot == slot)?.Pack.ToList() ?? new List<(string, int)>();

    /// <summary>Test seam (#2646): every wanderer's time is up (it heads off once its work in hand is done).</summary>
    public void EndWandererWorkForTest()
    {
        foreach (var w in WanderersOut())
        {
            w.WorkUntil = _uptime;
        }
    }

    /// <summary>Test seam (#2646): forgets the active world's cast (the next need builds it again — after a test changed the
    /// species roster).</summary>
    public void ResetWandererRosterForTest() => _wandererRosters.Remove(_world.LocationId);
}
