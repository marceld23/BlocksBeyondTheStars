// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client scans a subject with the handheld scanner (creature species id / block key).</summary>
public sealed class ScanIntent
{
    public string SubjectType { get; set; } = string.Empty; // "creature" | "block"
    public string SubjectKey { get; set; } = string.Empty;

    /// <summary>The aimed creature's entity id for a creature scan (#1926, empty from older clients): lets the server read
    /// the one individual under the crosshair — the Sreekmakra's disguise — instead of guessing from the species.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>#3007: the aimed block's world cell for a block scan, so the server can count distinct things ("scan ten
    /// conifers") instead of one tree ten times. <see cref="HasCell"/> false from an older client — then no cell is known.</summary>
    public bool HasCell { get; set; }

    public int CellX { get; set; }

    public int CellY { get; set; }

    public int CellZ { get; set; }
}

/// <summary>Client scans a space entity (asteroid) with the ship scanner to reveal its resources.</summary>
public sealed class ScanEntityIntent
{
    public string EntityId { get; set; } = string.Empty;
}

/// <summary>
/// Result of a scan: a readout + threat, and any knowledge gained (first scan only).
/// <para>
/// The readout is sent as <b>locale keys + structured data</b>, never as prose — the server has no idea
/// what language the player reads. (It used to build English literals like "Yields: iron_ore×1" and
/// "Hostile", which the German build then printed verbatim — #484.) This mirrors the existing
/// <see cref="ShipAiLine.LineKey"/> / tame-state key pattern.
/// </para>
/// </summary>
public sealed class ScanResult
{
    /// <summary>Display name of what was scanned (a coined species name, or a block/item key the client
    /// resolves to a localized name).</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>LEGACY English prose. Still populated so an old client against a new server keeps working;
    /// the current client only falls back to it when <see cref="InfoKey"/>, <see cref="TraitKeys"/> and
    /// <see cref="Drops"/> are all empty. Remove one release after #484 ships.</summary>
    public string Info { get; set; } = string.Empty;

    /// <summary>LEGACY English threat word — see <see cref="Info"/>. Superseded by <see cref="ThreatKey"/>.</summary>
    public string Threat { get; set; } = "—";

    public bool FirstTime { get; set; }
    public int KnowledgeGained { get; set; }
    public int KnowledgeTotal { get; set; }

    /// <summary>The raw species/block key behind <see cref="Subject"/> — the Codex discovery list keys off
    /// this, and it lets the readout show the subject's icon.</summary>
    public string SubjectKey { get; set; } = string.Empty;

    /// <summary>What kind of thing this was: <c>creature</c>, <c>block</c>, <c>tree</c>, <c>flora</c> or
    /// <c>asteroid</c>. Drives whether drops read as "Yields" or "Resources".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Locale key for the threat assessment (<c>ui.scan.threat.hostile</c> etc.), empty when the
    /// subject has none.</summary>
    public string ThreatKey { get; set; } = string.Empty;

    /// <summary>Locale keys describing the subject — for a creature its habitat, activity and temperament.
    /// The client localizes each and joins them.</summary>
    public string[] TraitKeys { get; set; } = System.Array.Empty<string>();

    /// <summary>What the subject yields. <see cref="NetTradeItem.Count"/> of 0 means "type only, no
    /// quantity" (asteroid resource types).</summary>
    public NetTradeItem[] Drops { get; set; } = System.Array.Empty<NetTradeItem>();

    /// <summary>Locale key for a whole-line remark when there is nothing structured to list
    /// (<c>ui.scan.no_yield</c>, <c>ui.scan.barren</c>, <c>ui.scan.foliage</c>, …).</summary>
    public string InfoKey { get; set; } = string.Empty;
}

/// <summary>
/// Server → client: the player's first-scan ledger (<see cref="Shared.State.PlayerState.Scanned"/>), which
/// backs the Codex "Discoveries" chapter. Sent once on join and appended to on each first-time scan — the
/// ledger was server-only before #484, so a scan left no permanent record anywhere in the UI.
/// </summary>
public sealed class DiscoveryLog
{
    /// <summary>Ledger entries, each <c>kind:key</c> (e.g. <c>creature:sp_12</c>, <c>tree:t3</c>) exactly as
    /// stored server-side. On the join snapshot this is the full list; on an update it is the delta.</summary>
    public string[] Entries { get; set; } = System.Array.Empty<string>();

    /// <summary>Display name per entry (parallel to <see cref="Entries"/>), captured when the scan happened.
    /// Creature/tree/flora species are generated PER WORLD, so their coined names cannot be looked up from
    /// another planet — they have to travel with the ledger. Empty for entries scanned before the game
    /// recorded names; the client then shows the raw key. Block entries carry the block key, which the
    /// client resolves to a localized name.</summary>
    public string[] Names { get; set; } = System.Array.Empty<string>();

    /// <summary>False = append <see cref="Entries"/> to what the client already has; true = replace.</summary>
    public bool Full { get; set; }

    /// <summary>Where each entry was found (#1843), parallel to <see cref="Entries"/>: the galaxy body id the
    /// player stood on (or orbited) at scan time. Empty string = unknown — an entry from before the game
    /// recorded sites, or a scan the galaxy could not place (a ship interior); the client then shows no
    /// location line. Additive fields: an older peer leaves all four empty.</summary>
    public string[] BodyIds { get; set; } = System.Array.Empty<string>();

    /// <summary>The body's display name per entry (parallel to <see cref="BodyIds"/>); empty = unknown.</summary>
    public string[] BodyNames { get; set; } = System.Array.Empty<string>();

    /// <summary>The star system id per entry (parallel to <see cref="BodyIds"/>); empty = unknown.</summary>
    public string[] SystemIds { get; set; } = System.Array.Empty<string>();

    /// <summary>The star system's display name per entry (parallel to <see cref="BodyIds"/>); empty = unknown.</summary>
    public string[] SystemNames { get; set; } = System.Array.Empty<string>();
}

/// <summary>
/// Server → client: the result of a terrain-scanner pulse (Feature 40) — the positions + block ids of the
/// valuable blocks (ores / crystal / data caches) found in a sphere around the player. The client renders
/// them as through-wall glow markers for a few seconds. Server-authoritative (energy/cooldown validated).
/// </summary>
public sealed class OreScanResult
{
    public int[] X { get; set; } = System.Array.Empty<int>();
    public int[] Y { get; set; } = System.Array.Empty<int>();
    public int[] Z { get; set; } = System.Array.Empty<int>();

    /// <summary>Numeric block id per hit (parallel to X/Y/Z) — lets the client tint markers by ore type.</summary>
    public ushort[] Block { get; set; } = System.Array.Empty<ushort>();

    /// <summary>How long the markers stay visible, in seconds.</summary>
    public float Seconds { get; set; } = 8f;

    /// <summary>True when the world held MORE hits than the message cap, so the arrays are the nearest
    /// slice rather than everything in range. The client's hit-count toast says "80+" instead of an
    /// exact number that would be silently wrong (#482).</summary>
    public bool Capped { get; set; }

    /// <summary>#2372: the pulse also listened for oil — true on a world that holds oil pockets (a living world of terrain
    /// generation 18 or newer, never a gas giant). False everywhere else and from an older server, so the client says
    /// nothing about oil there.</summary>
    public bool OilEcho { get; set; }

    /// <summary>#2372: the oil echo found a pocket within <see cref="OilEchoRange"/> blocks that still holds oil.</summary>
    public bool OilFound { get; set; }

    /// <summary>#2372: the found pocket's top oil cell at its centre column (valid when <see cref="OilFound"/>).</summary>
    public int OilX { get; set; }
    public int OilY { get; set; }
    public int OilZ { get; set; }

    /// <summary>#2372: the echo's horizontal range in blocks (the toast names it when nothing answered).</summary>
    public int OilEchoRange { get; set; }
}
