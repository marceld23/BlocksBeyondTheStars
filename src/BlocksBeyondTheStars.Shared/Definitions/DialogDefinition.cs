// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// One NPC dialogue (#1127, the long-reserved "item 15" Dialog backend): a tiny node graph — each node is a
/// prompt with 2–3 choices, each choice carries a response, an optional jump to a follow-up node, and an
/// optional consequence. Engine dialogues load from <c>data/dialogs.json</c> (optional — no file, no
/// dialogues); a story pack may ship its own for its authored characters (#1128). All text is locale keys;
/// the server resolves them per player (dialogue is per-player content — never the shared greeting cache).
/// </summary>
public sealed class DialogDefinition
{
    public string Key { get; set; } = string.Empty;

    /// <summary>NPC role this dialogue attaches to ("vendor" / "quartermaster" / "settler"), or "" for any.
    /// Ignored when <see cref="Character"/> is set — an authored character owns their dialogue outright.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Authored story-pack character id (#1128) this dialogue belongs to, or "" for a role dialogue.</summary>
    public string Character { get; set; } = string.Empty;

    /// <summary>Minimum relationship stage: "" (anyone), "known" or "trusted" — the npcThread convention.</summary>
    public string MinStage { get; set; } = string.Empty;

    /// <summary>True → each player can complete this dialogue once per save (persisted as a milestone).</summary>
    public bool OncePerPlayer { get; set; }

    /// <summary>The nodes, entered at index 0. 2–3 nodes cover every authored dialogue.</summary>
    public List<DialogNode> Nodes { get; set; } = new();
}

/// <summary>One prompt the NPC speaks, with the player's possible replies.</summary>
public sealed class DialogNode
{
    public string PromptKey { get; set; } = string.Empty;

    public List<DialogChoice> Choices { get; set; } = new();
}

/// <summary>One player reply: what they say, how the NPC answers, where the dialogue goes next, and what it
/// does. The server owns the walk and the consequence — an optional LLM may one day rephrase the prose, but
/// it never decides a branch.</summary>
public sealed class DialogChoice
{
    public string TextKey { get; set; } = string.Empty;

    public string ResponseKey { get; set; } = string.Empty;

    /// <summary>Index of the follow-up node, or -1 (default) to end the dialogue after the response.</summary>
    public int Next { get; set; } = -1;

    /// <summary>Optional consequence, colon-separated: "standing:&lt;n&gt;" (relationship bump),
    /// "fragment:&lt;key&gt;" (hand over a story fragment), "gift:&lt;item&gt;:&lt;n&gt;" (a small present),
    /// "radio:&lt;lineKey&gt;" (the NPC calls the player over the radio a little later). "" = none.</summary>
    public string Consequence { get; set; } = string.Empty;

    /// <summary>On a once-per-player dialogue: true keeps the dialogue OPEN when this choice ends it — a
    /// polite "another time" must never burn the player's only chance at the real conversation.</summary>
    public bool KeepOpen { get; set; }
}
