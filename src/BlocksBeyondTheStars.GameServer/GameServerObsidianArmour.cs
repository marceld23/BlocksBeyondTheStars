// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Sounders' obsidian armour (#3354): helmet, chest and legs from the hidden blueprint <c>sounder_obsidian</c>, armour
/// like the other mid-tier sets (the item data), plus what makes it the Sounders':
/// <list type="bullet">
/// <item><b>Quiet steps</b> (the set bonus, all three pieces worn): the Sounders were very quiet — a hunting creature notices
/// the wearer only within <see cref="QuietStepsFactor"/> of its usual range (<see cref="PreyOverlooked"/>, asked by the
/// creatures' prey search).</item>
/// <item><b>The disc socket</b> in the chest (<see cref="EquipSlot.Disc"/>): a Song Disc there works only while the obsidian
/// chest is worn (<see cref="HasActiveSocketDisc"/>) — the hearing disc shows the Echoes (the story's Echo code asks), the calm
/// disc makes Soundraks ignore the wearer (<see cref="SoothesSoundraks"/>), the light disc makes the wearer glow softly (the
/// <see cref="GearLook.StyleObsidianGlow"/> look every client draws). Wearing a disc there raises
/// <see cref="Shared.Story.GameEventKeys.DiscInserted"/> (EquipmentSystem).</item>
/// </list>
/// The look is the obsidian style of <see cref="GearLook"/> plus its one <see cref="GearLook.SocketDisc"/> bit — the presence
/// gains no field (#3234).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The share of a creature's notice range within which it notices a wearer of the whole obsidian set.</summary>
    internal const float QuietStepsFactor = 0.5f;

    /// <summary>The Song Disc in the player's disc socket (base key), or "" — whether or not the obsidian chest is worn.</summary>
    internal static string SocketDiscOf(PlayerState p)
        => (int)EquipSlot.Disc < p.Equipment.SlotCount && p.Equipment.Slots[(int)EquipSlot.Disc] is { IsEmpty: false } stack
            ? ItemKey.Base(stack.Item)
            : string.Empty;

    /// <summary>
    /// #3354: whether <paramref name="discKey"/> sits in the player's disc socket AND works — the obsidian chest is worn. The
    /// story's Echo code asks it for the hearing disc (<see cref="SoundersIds.DiscHearing"/>: Echoes visible and audible).
    /// </summary>
    internal static bool HasActiveSocketDisc(PlayerState p, string discKey)
        => !string.IsNullOrEmpty(discKey) && EquipmentSystem.Wears(p, SoundersIds.ObsidianChest) && SocketDiscOf(p) == discKey;

    /// <summary>Whether the player wears the whole obsidian set (helmet, chest and legs) — the quiet steps.</summary>
    internal static bool WearsObsidianSet(PlayerState p)
        => EquipmentSystem.Wears(p, SoundersIds.ObsidianHelmet) && EquipmentSystem.Wears(p, SoundersIds.ObsidianChest)
           && EquipmentSystem.Wears(p, SoundersIds.ObsidianLegs);

    /// <summary>The creatures' hook (GameServerSoundersFauna.cs): the calm disc in a working socket soothes Soundraks.</summary>
    partial void SoothesSoundraks(PlayerSession session, ref bool soothing)
    {
        soothing |= HasActiveSocketDisc(session.State, SoundersIds.DiscCalm);
    }

    /// <summary>
    /// Whether the hunting <paramref name="hunter"/> overlooks <paramref name="session"/>'s player at
    /// <paramref name="distSq"/> (squared blocks) — asked by the prey search for every candidate: a Soundrak overlooks a player
    /// whose calm disc soothes it, and any hunter overlooks a wearer of the whole obsidian set beyond
    /// <see cref="QuietStepsFactor"/> of the range it would otherwise notice prey from (quiet steps). A grudge is no hunt: a
    /// creature angered by mining still finds the miner.
    /// </summary>
    internal bool PreyOverlooked(CombatEntity? hunter, PlayerSession session, double distSq)
    {
        if (hunter is null || !_speciesById.TryGetValue(hunter.SpeciesId, out var sp))
        {
            return false;
        }

        if (SoundersFauna.IsSoundrak(sp) && Soothes(session))
        {
            return true;
        }

        if (!WearsObsidianSet(session.State) || (sp.AngeredByMining && hunter.ProvokeTimer > 0))
        {
            return false;
        }

        float quiet = CreatureNoticeRange(sp) * QuietStepsFactor;
        return distSq > (double)quiet * quiet;
    }

    /// <summary>The range a species notices its prey from — the creature tick's aggro rule (#638: big species see further,
    /// #3326: a hound further still).</summary>
    private float CreatureNoticeRange(CreatureSpecies sp)
        => HoundAggro(sp, CreatureAggroRange + System.Math.Max(0f, sp.Size - 2f));

    /// <summary>Whether a creature leaves this player alone in its bite (a Soundrak and the calm disc).</summary>
    internal bool BiteOverlooked(CombatEntity creature, PlayerSession session)
        => _speciesById.TryGetValue(creature.SpeciesId, out var sp) && SoundersFauna.IsSoundrak(sp) && Soothes(session);
}
