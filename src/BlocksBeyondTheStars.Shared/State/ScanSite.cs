// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.State;

/// <summary>Where a Codex discovery was made (#1843): the body the player stood on — or, in flight, the body
/// the space instance is anchored to — and its star system, captured at scan time. The NAMES travel with
/// the ids: a body's display name is resolvable from the galaxy later, but recording it here keeps the Codex
/// line free of a galaxy lookup on the client and stable if a body is ever renamed. Persisted per ledger key
/// in <see cref="PlayerState.ScannedWhere"/>.</summary>
public sealed class ScanSite
{
    /// <summary>Galaxy body id (e.g. <c>sys3-p2</c>, <c>sys3-st</c>).</summary>
    public string BodyId { get; set; } = string.Empty;

    /// <summary>The body's display name at scan time.</summary>
    public string BodyName { get; set; } = string.Empty;

    /// <summary>Star system id the body belongs to (a player station's HOST system, like the star map).</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>The star system's display name at scan time (empty when the galaxy had no system for the body).</summary>
    public string SystemName { get; set; } = string.Empty;
}
