// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// Items that carry other items (#3098, #3099): the <b>transport crate</b> (a block — mined, it keeps what was inside) and
/// the <b>storage bag</b> (an item that is opened in the inventory, up to <see cref="BagCapacity"/> items). An item stack
/// is only a key and a count, so what such an item holds lives in a server-side record (the "carried contents" store),
/// and the item points at it through its key: the base key, the <c>'#'</c> separator, then <see cref="Tag"/> and the
/// record id (<see cref="RecordIdLength"/> lowercase hex digits) — <c>"storage_bag#h0f3a9c21b7d4e605"</c>. The paint
/// design id (<c>p</c>) is the precedent: the item carries the reference, the save carries the data.
/// <para>
/// One key, one record, one item: a key with a record never stacks (<c>GameContent.MaxStackOf</c> answers 1), so wherever
/// the key goes — a crate, the hold, a drop packet, a salvage capsule, another player in a trade — the record follows it
/// and has exactly one owner. An EMPTY bag or crate has no record and is the plain base key, which stacks as usual.
/// </para>
/// </summary>
public static class CarriedContents
{
    /// <summary>The key tag of a carried-contents record id. Not a hex digit, and none of the other tags
    /// (<c>t g s p x u</c>, see <see cref="ItemKey"/>).</summary>
    public const char Tag = 'h';

    /// <summary>Hex digits of a record id (64 random bits).</summary>
    public const int RecordIdLength = 16;

    /// <summary>The transport crate: its item key and the block it places.</summary>
    public const string TransportCrate = "transport_crate";

    /// <summary>The storage bag's item key.</summary>
    public const string Bag = "storage_bag";

    /// <summary>How many items a storage bag holds in total (Justus: "at most 1000 items").</summary>
    public const int BagCapacity = 1000;

    /// <summary>The record id an item key points at, or an empty string for a key without one (allocation-free for a
    /// plain key). A malformed id (wrong length) counts as none.</summary>
    public static string RecordId(string? itemKey)
    {
        string id = ItemKey.GetTag(itemKey, Tag);
        return id.Length == RecordIdLength ? id : string.Empty;
    }

    /// <summary>True when the key points at a carried-contents record — a filled bag or a packed transport crate.</summary>
    public static bool HasRecord(string? itemKey) => RecordId(itemKey).Length > 0;

    /// <summary>The key of <paramref name="baseKey"/> pointing at the record <paramref name="recordId"/>.</summary>
    public static string WithRecord(string baseKey, string recordId) => ItemKey.SetTag(ItemKey.Base(baseKey), Tag, recordId);

    /// <summary>The key without its record reference (the plain base key of an empty bag or crate).</summary>
    public static string WithoutRecord(string itemKey) => ItemKey.SetTag(itemKey, Tag, string.Empty);

    /// <summary>True for a storage bag, empty or filled.</summary>
    public static bool IsBag(string? itemKey) => !string.IsNullOrEmpty(itemKey) && ItemKey.Base(itemKey!) == Bag;

    /// <summary>True for a transport crate item, empty or packed.</summary>
    public static bool IsTransportCrate(string? itemKey) => !string.IsNullOrEmpty(itemKey) && ItemKey.Base(itemKey!) == TransportCrate;

    /// <summary>True for an item that carries other items (a bag or a transport crate), whether it holds anything now.</summary>
    public static bool IsCarrier(string? itemKey) => IsBag(itemKey) || IsTransportCrate(itemKey);

    /// <summary>Whether <paramref name="itemKey"/> may go into a storage bag: no bag (empty or not — a bag in a bag would
    /// get around the limit) and nothing that carries a record (a packed transport crate holds more than the bag may).</summary>
    public static bool FitsInBag(string? itemKey) => !string.IsNullOrEmpty(itemKey) && !IsBag(itemKey) && !HasRecord(itemKey);
}
