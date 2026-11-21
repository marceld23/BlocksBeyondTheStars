// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Who gets what when a space boss falls (#3903) — the rule of the guardian giants, in space. An ordinary hostile pays
/// the pilot of the last shot (<see cref="CreditSpaceKill"/>, <see cref="PayEntityLoot"/>); a boss keeps the list of
/// every pilot who hurt it (<see cref="SpaceBossRuntime.Fighters"/>), and each of them who is still in the game — also
/// one whose ship went down in the fight and who waits at home — gets the kill credit, the story event
/// <see cref="GameEventKeys.CruiserDefeated"/> (arg: the system id) and their own full share of the loot, banked
/// straight into backpack and hold (what finds no room floats where the boss fell). The story's machine counter moves
/// once.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A pilot's shot hurt a boss: they are one of its fighters from now on (a no-op for every other entity).</summary>
    private static void NoteSpaceBossFighter(CombatEntity target, PlayerSession? session)
    {
        if (session is not null && target.Boss is { } boss)
        {
            boss.Fighters.Add(session.State.PlayerId);
        }
    }

    /// <summary>The fighters of a fallen boss who are still in the game, in a stable order.</summary>
    private List<PlayerSession> SpaceBossFightersPresent(CombatEntity boss)
    {
        var list = new List<PlayerSession>();
        if (boss.Boss is { } rt)
        {
            foreach (string id in rt.Fighters.OrderBy(id => id, System.StringComparer.Ordinal))
            {
                if (FindSessionByPlayerId(id) is { Joined: true } s)
                {
                    list.Add(s);
                }
            }
        }

        return list;
    }

    /// <summary>A boss fell: the story counts one machine, every fighter gets the defeat (the survey orders, a memory's
    /// chance, the events), the bar gets its last status, and the wreck hook is told where it fell.</summary>
    private void CreditSpaceBossKill(SpaceInstance instance, PlayerSession? lastShot, CombatEntity boss)
    {
        var rt = boss.Boss!;
        NoteSpaceBossFighter(boss, lastShot);
        if (instance.BossId == boss.Id)
        {
            instance.BossId = string.Empty;
        }

        instance.BossRolled = true; // this flight has had its boss
        RecordStoryMachineKill();
        var fighters = SpaceBossFightersPresent(boss);
        string systemId = SystemIdOfInstance(instance);
        foreach (var s in fighters)
        {
            OnMachineDefeated(s);
            TryDropPlayerMemory(s);
            RaiseGameEvent(s, GameEventKeys.DefeatAny, GameEventKeys.DefeatSpaceMachine);
            RaiseGameEvent(s, GameEventKeys.CruiserDefeated, systemId);
            SendVegaLine(s, "vega.cruiser.down", 2);
        }

        SendSpaceBossStatus(instance, boss, active: false, defeated: true);
        var fighterIds = new List<string>(rt.Fighters);
        fighterIds.Sort(System.StringComparer.Ordinal);
        OnEscortCruiserDefeated(instance, boss, fighterIds, rt.CarriesEcho);
        _log.Info($"The escort cruiser in '{instance.Id}' was defeated by {fighterIds.Count} pilot(s).");
    }

    /// <summary>A fallen boss's loot: every fighter still in the game gets the whole list (their own share), whatever
    /// their ship carries — a tractor beam does not turn a share into a drop somebody else could collect.</summary>
    private void PaySpaceBossLoot(SpaceInstance instance, CombatEntity boss)
    {
        var served = _current;
        foreach (var s in SpaceBossFightersPresent(boss))
        {
            SetCurrent(s); // the pool below banks into THIS fighter's backpack and hold
            BankEntityLoot(instance, boss, s);
        }

        if (served is not null)
        {
            SetCurrent(served);
        }
    }

    /// <summary>Test seam (#3903): the pilots who have hurt the live boss of the player's flight so far.</summary>
    public IReadOnlyCollection<string> SpaceBossFightersForTest(string playerId)
        => SpaceBossForTest(playerId)?.Boss?.Fighters ?? (IReadOnlyCollection<string>)System.Array.Empty<string>();
}
