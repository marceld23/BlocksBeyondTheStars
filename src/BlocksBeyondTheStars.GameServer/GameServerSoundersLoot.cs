// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Sounders' finds (#3352): what the relic caches under the old monuments hold besides their archaeology.
/// <list type="bullet">
/// <item><b>Story loot, per player.</b> While a player's Sounders pack is active and they have decoded disc 1
/// (<c>snd:disc:song_disc_beginning</c>), the next relic cache THEY loot also holds the next story disc they were never handed
/// — builders, then hearing, then hunger (<see cref="SongDiscs.LootOrder"/>). The disc is added the moment that player loots
/// (<see cref="AddStoryLoot"/>, called by the loot flow), so a cache another player emptied first is no dead end: the next
/// cache brings it. Handed-out discs are remembered (<see cref="SongDiscs.GotMilestone"/>) — never a duplicate.</item>
/// <item><b>The flight recorder.</b> The alien debris field's recorder (the "Alien hum" theme) hands out the hunger disc once
/// (<see cref="TryGiveFlightRecorderDisc"/>).</item>
/// <item><b>Finds for everyone.</b> Any relic cache has a 0.5 % chance of a Song Spindle and about 5 % of a collectible Song
/// Disc — rolled from the cache's own key when it spawns, never from the shared loot stream, so every existing cache's
/// contents stay as they were (<see cref="AddSounderRelicFinds"/>).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The id part every relic cache shares (<c>loot_&lt;structure&gt;_relic_cache_x_y_z</c>).</summary>
    private const string RelicCacheIdMarker = "_relic_cache_";

    /// <summary>The theme of the alien debris field whose flight recorder hums the hunger disc.</summary>
    internal const string AlienDebrisTheme = "alien";

    /// <summary>Chance a relic cache holds a Song Spindle (non-story find).</summary>
    internal const double SpindleRelicChance = 0.005;

    /// <summary>Chance a relic cache holds a collectible Song Disc (non-story find).</summary>
    internal const double SongDiscRelicChance = 0.05;

    /// <summary>Whether a container is a relic cache (monuments, the alien shrine, the sound chambers).</summary>
    internal static bool IsRelicCache(StoredContainer c)
        => c.Id.StartsWith("loot_", System.StringComparison.Ordinal) && c.Id.Contains(RelicCacheIdMarker, System.StringComparison.Ordinal);

    /// <summary>Whether the Sounders story hands out its discs to this player now: the pack runs and disc 1 is decoded.</summary>
    internal bool SoundersStoryLootDue(PlayerSession session)
        => session.State.Milestones.Contains(SoundersIds.DiscMilestone(SoundersIds.DiscBeginning)) && SoundersActive(session);

    /// <summary>The first disc of <paramref name="order"/> the player has not decoded and does not have — never handed, or
    /// handed but no longer carried or worn (lost, dropped, left in a crate): a story disc is never lost for good, so the
    /// next cache hands it out again (a spare in a crate is harmless). A disc still waiting in a relic cache locked to the
    /// player (a sound chamber they opened but have not emptied yet) is at hand too — the next chamber brings the NEXT disc.
    /// Null when every disc is decoded or at hand.</summary>
    internal string? NextMissingDisc(PlayerState p, IEnumerable<string> order)
        => order.FirstOrDefault(d => !p.Milestones.Contains(SoundersIds.DiscMilestone(d))
                                     && (!p.Milestones.Contains(SongDiscs.GotMilestone(d))
                                         || (!CarriesSounderItem(p, d) && !WaitsInOwnRelicCache(p, d))));

    /// <summary>Whether <paramref name="disc"/> lies in a relic cache of this world that is locked to <paramref name="p"/>.</summary>
    private bool WaitsInOwnRelicCache(PlayerState p, string disc)
        => _containers.Any(c => IsRelicCache(c) && c.Owner.Length > 0 && string.Equals(c.Owner, p.PlayerId, System.StringComparison.Ordinal)
                                && c.Items.Any(s => string.Equals(s.Item, disc, System.StringComparison.Ordinal)));

    /// <summary>Whether <paramref name="p"/> carries <paramref name="item"/> in the backpack or wears it (a disc in the socket).</summary>
    private static bool CarriesSounderItem(PlayerState p, string item)
        => p.Inventory.CountOf(item) > 0 || p.Equipment.Slots.Any(s => s is not null && string.Equals(s.Item, item, System.StringComparison.Ordinal));

    /// <summary>Called by the loot flow before the transfer: a relic cache gets the player's next story disc.</summary>
    private void AddStoryLoot(PlayerSession session, StoredContainer container)
    {
        if (!IsRelicCache(container) || !SoundersStoryLootDue(session)
            || NextMissingDisc(session.State, SongDiscs.LootOrder) is not { } disc)
        {
            return;
        }

        container.Items.Add(new ItemStack(disc, 1));
        session.State.Milestones.Add(SongDiscs.GotMilestone(disc));
        _repo.SaveContainer(container);
    }

    /// <summary>The non-story finds of a relic cache, rolled from its own key (seed-pure, outside the shared loot stream).</summary>
    private void AddSounderRelicFinds(string lootKey, List<ItemStack> items)
    {
        long h = _meta.Seed ^ WorldGenerator.StableHash("sounders:relic:" + lootKey);
        var rng = new System.Random(unchecked((int)(h ^ (h >> 32))));
        if (rng.NextDouble() < SpindleRelicChance && _content.GetItem(SoundersIds.SongSpindle) is not null)
        {
            items.Add(new ItemStack(SoundersIds.SongSpindle, 1));
        }

        if (rng.NextDouble() < SongDiscRelicChance && _content.GetItem(SoundersIds.DiscSong) is not null)
        {
            items.Add(new ItemStack(SoundersIds.DiscSong, 1));
        }
    }

    /// <summary>The alien debris field's flight recorder hums the hunger disc — once, for a player the story hands discs to
    /// (called from <see cref="ScanDebrisField"/>).</summary>
    private void TryGiveFlightRecorderDisc(PlayerSession session, string themeKey)
    {
        var p = session.State;
        if (themeKey != AlienDebrisTheme || !SoundersStoryLootDue(session)
            || p.Milestones.Contains(SongDiscs.GotMilestone(SoundersIds.DiscHunger))
            || p.Milestones.Contains(SoundersIds.DiscMilestone(SoundersIds.DiscHunger)))
        {
            return;
        }

        GiveSoundersItem(session, SoundersIds.DiscHunger, 1);
        Send(session, new ServerMessage { Text = "@srv.sounders.recorder_disc" });
    }

    /// <summary>Test seam: the flight recorder of a field with theme <paramref name="themeKey"/> was read.</summary>
    internal void FlightRecorderReadForTest(PlayerSession session, string themeKey) => TryGiveFlightRecorderDisc(session, themeKey);

    /// <summary>Test seam: spawns a monument's relic cache at <paramref name="cell"/> (the real spawn path) and returns its id.</summary>
    internal string AddRelicCacheForTest(Vector3i cell)
    {
        SpawnStructureLoot("monument", "relic_cache", new Vector3f(cell.X, cell.Y, cell.Z), new System.Random(cell.X ^ cell.Z));
        return "loot_" + $"monument:relic_cache:{cell.X}:{cell.Y}:{cell.Z}".Replace(':', '_');
    }

    /// <summary>Test seam: the non-story finds a relic cache with this loot key gets.</summary>
    internal List<ItemStack> SounderRelicFindsForTest(string lootKey)
    {
        var items = new List<ItemStack>();
        AddSounderRelicFinds(lootKey, items);
        return items;
    }
}
