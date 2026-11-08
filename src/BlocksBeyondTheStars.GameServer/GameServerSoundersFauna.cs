// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class CombatEntity
{
    /// <summary>#3359: server uptime until which this animal is calm (the calm disc, the Resonance Blaster): it is provoked by
    /// nothing, hunts and bites nobody. 0 = not calmed. Server-only runtime state, never persisted.</summary>
    public double CalmUntil { get; set; }

    /// <summary>#3359: server uptime of a Soundrak's next territorial scream (unused by every other animal).</summary>
    public double ScreamReadyAt { get; set; }

    /// <summary>#3424: server uptime of a Soundrak's last scream — its warning before an attack (negative infinity: it never
    /// screamed). Runtime state only.</summary>
    public double ScreamedAt { get; set; } = double.NegativeInfinity;
}

/// <summary>
/// The creatures of Soaniri on the server (#3359, epic #3368 — the Sounders story pack). The four species are authored records
/// (<c>data/creatures.json</c>) that join a world's roster only where its planet type lists them; their own rules live here,
/// apart from the creature state machine:
/// <list type="bullet">
/// <item><b>Caps</b> — the spawner keeps one Soundrak and a few Qualis about at a time (<see cref="SoundersFauna.MaxWildAtOnce"/>).</item>
/// <item><b>The Qualis</b> are untouchable: no hit, no push (<see cref="UntouchableCreature"/>); passive, high in the sky.</item>
/// <item><b>The Soundrak</b> is hostile from the start (#3424): a player within <see cref="SoundrakRules.TerritoryRange"/> hears
/// its short, loud scream; one it notices within <see cref="SoundrakRules.NoticeRange"/> hears the scream as a warning and,
/// still there <see cref="SoundrakRules.WarningSeconds"/> later, is attacked (provoked into the classic charge and bite; only
/// where planet enemies are on — never in Peaceful or Creative). No story progress is needed. A player who soothes Soundraks
/// (the calm disc in the obsidian chest's socket — the items pack's hook <see cref="SoothesSoundraks"/>) is ignored; the whole
/// obsidian set's quiet steps let a player come closer before it notices.</item>
/// <item><b>The calm</b> — <see cref="CalmCreature"/> / <see cref="CalmCreaturesNear"/> (the Resonance Blaster, a story beat):
/// for a while the animal is provoked by nothing, hunts and bites nobody.</item>
/// </list>
/// The Soundelings' light-carrying lives in <c>GameServerSoundelings.cs</c>.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>#3354, the items pack's hook: set <paramref name="soothing"/> when <paramref name="session"/>'s player soothes
    /// Soundraks — the calm disc in the obsidian chest's socket. A Soundrak neither screams at nor charges such a player.
    /// Without an implementation nobody soothes them.</summary>
    partial void SoothesSoundraks(PlayerSession session, ref bool soothing);

    /// <summary>Whether the spawner must skip <paramref name="sp"/>: one Soundrak, a few Qualis about at a time.</summary>
    internal bool SoundersCapReached(CreatureSpecies sp)
    {
        int max = SoundersFauna.MaxWildAtOnce(sp);
        return max != int.MaxValue && WildCountOf(sp.Id) >= max;
    }

    /// <summary>A creature no player may hit or push (#3359): a Quali.</summary>
    private bool UntouchableCreature(CombatEntity creature)
        => _speciesById.TryGetValue(creature.SpeciesId, out var sp) && SoundersFauna.Untouchable(sp);

    /// <summary>Whether <paramref name="creature"/> is calm right now.</summary>
    internal bool IsCalmed(CombatEntity creature) => creature.CalmUntil > _uptime;

    /// <summary>
    /// Calms one wild animal for <paramref name="seconds"/> (#3359 — the Resonance Blaster's pulse, the calm disc, a story beat):
    /// its grudge is gone at once (no provoke, no chase) and for as long as the calm lasts nothing provokes it and it hunts and
    /// bites nobody. A longer calm already running is kept. Companions (they are calm) and giants (their own rules) are left
    /// alone. Returns whether the animal was calmed.
    /// </summary>
    internal bool CalmCreature(CombatEntity creature, double seconds)
    {
        if (creature.IsCompanion || creature.IsGiant || seconds <= 0 || double.IsNaN(seconds))
        {
            return false;
        }

        creature.CalmUntil = System.Math.Max(creature.CalmUntil, _uptime + seconds);
        creature.ProvokeTimer = 0;
        creature.ChaseTimer = 0;
        BroadcastCreatures(); // the angry red goes at once
        return true;
    }

    /// <summary>Calms every wild animal within <paramref name="radius"/> blocks of <paramref name="at"/> for
    /// <paramref name="seconds"/> (see <see cref="CalmCreature"/>); with <paramref name="soundraksOnly"/> only the Soundraks.
    /// Returns how many were calmed.</summary>
    internal int CalmCreaturesNear(Vector3f at, float radius, double seconds, bool soundraksOnly = false)
    {
        int calmed = 0;
        double rangeSq = (double)radius * radius;
        foreach (var c in _creatures)
        {
            if (WrapDistSq(c.Position, at) > rangeSq
                || (soundraksOnly && !(_speciesById.TryGetValue(c.SpeciesId, out var sp) && SoundersFauna.IsSoundrak(sp))))
            {
                continue;
            }

            if (CalmCreature(c, seconds))
            {
                calmed++;
            }
        }

        return calmed;
    }

    /// <summary>Per tick, while a calm lasts: no grudge builds up (whatever hit it meanwhile).</summary>
    private void TickCalm(CombatEntity creature)
    {
        if (IsCalmed(creature))
        {
            creature.ProvokeTimer = 0;
            creature.ChaseTimer = 0;
        }
    }

    /// <summary>Whether <paramref name="session"/>'s player soothes Soundraks right now (the items pack's hook).</summary>
    private bool Soothes(PlayerSession session)
    {
        bool soothing = false;
        SoothesSoundraks(session, ref soothing);
        return soothing;
    }

    /// <summary>
    /// The Soundrak's patch (#3359) and its hostility from the start (#3424), once per tick. The nearest player in
    /// <see cref="SoundrakRules.TerritoryRange"/> it may bother (not cloaked, not soothing, no death choice pending) hears its
    /// scream — a short, loud <c>soundrak_scream_1..3</c> from its crown, every 9–15 s while they stay, and VEGA warns once.
    /// The nearest player it <b>notices</b> (within <see cref="SoundrakRules.NoticeRange"/>, not overlooked — the calm disc, the
    /// obsidian set's quiet steps) gets <see cref="SoundrakRules.AlertFor"/>: the scream as a warning first, then — still that
    /// close <see cref="SoundrakRules.WarningSeconds"/> later — the attack, i.e. the classic provoked charge and bite. Any fresh
    /// scream is the warning, so a player who heard it from the edge of the patch and walks in is not warned twice. The attack
    /// needs planet enemies on (never in Peaceful or Creative); a calm, frozen or dazed Soundrak does none of it. No story
    /// progress is involved: a fresh world's Soundrak is as hostile as a late one.
    /// </summary>
    private void GuardSoundrakTerritory(CombatEntity creature, List<PlayerSession> targets)
    {
        if (IsCalmed(creature) || creature.FrozenTimer > 0 || IsStaggered(creature))
        {
            return;
        }

        var intruder = SoundrakIntruder(creature, targets, out var noticed);
        if (intruder is null)
        {
            return;
        }

        if (noticed is not null && PlanetEnemiesActive && creature.ProvokeTimer <= 0)
        {
            switch (SoundrakRules.AlertFor(_uptime - creature.ScreamedAt))
            {
                case SoundrakAlert.Warn:
                    ScreamSoundrak(creature, noticed); // the warning: the attack comes only after it
                    return;
                case SoundrakAlert.Wait:
                    break; // the warning is fresh: time to back away
                case SoundrakAlert.Attack:
                    creature.ProvokeTimer = CreatureProvokeSeconds; // the charge: it hunts and bites like an aggressor
                    creature.AwakeOverrideTimer = System.Math.Max(creature.AwakeOverrideTimer, CreatureWakeSeconds);
                    BroadcastCreatures(); // the angry red shows at once
                    break;
            }
        }

        if (_uptime >= creature.ScreamReadyAt)
        {
            ScreamSoundrak(creature, intruder);
        }
    }

    /// <summary>The nearest player in a Soundrak's patch it may bother, or null; in <paramref name="noticed"/> the nearest one it
    /// notices (within <see cref="SoundrakRules.NoticeRange"/> and not overlooked, <see cref="PreyOverlooked"/>), or null.</summary>
    private PlayerSession? SoundrakIntruder(CombatEntity creature, List<PlayerSession> targets, out PlayerSession? noticed)
    {
        PlayerSession? intruder = null;
        noticed = null;
        double bestSq = SoundrakRules.TerritoryRange * SoundrakRules.TerritoryRange;
        double noticeSq = SoundrakRules.NoticeRange * SoundrakRules.NoticeRange;
        foreach (var s in targets)
        {
            if (HostilesIgnore(s.State) || s.Respawn.ChoicePending || Soothes(s))
            {
                continue;
            }

            double d = WrapDistSq(creature.Position, s.State.Position);
            if (d <= bestSq)
            {
                bestSq = d;
                intruder = s;
            }

            if (d <= noticeSq && !PreyOverlooked(creature, s, d))
            {
                noticeSq = d;
                noticed = s;
            }
        }

        return intruder;
    }

    /// <summary>The scream itself — one of the three takes, from the Soundrak's bud mouth at the top of its body — heard by
    /// <paramref name="hearer"/>, to whom VEGA explains it once. Every scream is also the warning before an attack (#3424).</summary>
    private void ScreamSoundrak(CombatEntity creature, PlayerSession hearer)
    {
        int roll = StableStringHash(creature.Id) ^ (int)(_uptime * 7.0);
        float height = _speciesById.TryGetValue(creature.SpeciesId, out var sp)
            ? SoundrakRules.HeightFor(sp.Size * creature.SizeScale)
            : 10f;
        var at = creature.Position;
        BroadcastToWorld(new SoundFx { SoundId = SoundrakRules.ScreamSound(roll), X = at.X, Y = at.Y + (height * 0.9f), Z = at.Z });
        creature.ScreamReadyAt = _uptime + SoundrakRules.ScreamGap(((uint)roll % 1000u) / 1000.0);
        creature.ScreamedAt = _uptime;
        ShipAiHintOnce(hearer, "soundrak"); // VEGA: it is dangerous — when it screams, back away (or calm it with a song)
    }

    /// <summary>The scan's line about what a player can do with a creature of Soaniri, or null.</summary>
    private static string? SoundersScanTrait(CreatureSpecies sp) => sp.BodyPlan switch
    {
        CreatureBodyPlan.Soundi => "ui.scan.body.soundi",
        CreatureBodyPlan.Soundrak => "ui.scan.body.soundrak",
        CreatureBodyPlan.Quali => "ui.scan.body.quali",
        CreatureBodyPlan.Soundeling => "ui.scan.behaviour.carries_light",
        _ => null,
    };

    /// <summary>
    /// #3359: puts a live animal of the authored species <paramref name="key"/> (<c>soundi</c>, <c>quali</c> …) at
    /// <paramref name="at"/> on the active world — for a story beat (the Qualis that appear at the Choir Tower, the pack's own
    /// Soundi). The roster's species when the world has it; otherwise the record joins this world's roster first (scannable,
    /// tamable and respawned like any species). Returns the new creature's id, or null for an unknown key or a world without
    /// ground life.
    /// </summary>
    internal string? SpawnAuthoredCreature(string key, Vector3f at)
    {
        var sp = EnsureAuthoredSpecies(key);
        if (sp is null)
        {
            return null;
        }

        SpawnCreature(sp, at);
        BroadcastCreatures();
        return _creatures[^1].Id;
    }

    /// <summary>The active world's species of the authored record <paramref name="key"/> — added to its roster when missing — or
    /// null when the key is unknown or the world is void.</summary>
    internal CreatureSpecies? EnsureAuthoredSpecies(string key)
    {
        if (_speciesById.TryGetValue(SoundersFauna.SpeciesIdOf(key), out var known))
        {
            return known;
        }

        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        if (planet is null || planet.Void || !_content.AuthoredCreatures.TryGetValue(key, out var record))
        {
            return null;
        }

        var sp = CreatureGenerator.MakeAuthored(record, planet, WorldGenerator.RosterSeedFor(_meta.Seed, _world.LocationId));
        _speciesRoster = _speciesRoster.Append(sp).ToArray();
        _speciesById[sp.Id] = sp;
        _locoProfiles[sp.Id] = LocomotionController.ForSpecies(sp);
        return sp;
    }

    // --- Test seams (#3359) ----------------------------------------------------------------------------------------------

    /// <summary>Test seam: <see cref="EnsureAuthoredSpecies"/>.</summary>
    public CreatureSpecies? EnsureAuthoredSpeciesForTest(string key) => EnsureAuthoredSpecies(key);

    /// <summary>Test seam: <see cref="SpawnAuthoredCreature"/>.</summary>
    public string? SpawnAuthoredCreatureForTest(string key, Vector3f at) => SpawnAuthoredCreature(key, at);

    /// <summary>Test seam: <see cref="CalmCreaturesNear"/>.</summary>
    public int CalmCreaturesNearForTest(Vector3f at, float radius, double seconds) => CalmCreaturesNear(at, radius, seconds);

    /// <summary>Test seam: whether the creature <paramref name="id"/> is calm.</summary>
    public bool IsCalmedForTest(string id) => _creatures.Find(c => c.Id == id) is { } c && IsCalmed(c);

    /// <summary>Test seam: whether the spawner would skip the species for its Soaniri cap.</summary>
    public bool SoundersCapReachedForTest(string speciesId) => SoundersCapReached(_speciesById[speciesId]);
}
