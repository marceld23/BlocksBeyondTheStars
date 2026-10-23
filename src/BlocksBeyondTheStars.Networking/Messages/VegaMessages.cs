// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Server → client: a line from the player's SHIP AI ("VEGA") shown in the HUD companion panel, plus the
/// current onboarding/advisor objective for the objective chip. Lines are locale KEYS (the client localizes),
/// keeping the AI fully bilingual and offline-safe; <see cref="LineArg"/> fills the optional {0}, {1}… placeholders
/// with proper nouns (world/item names), and <c>{key:Action}</c> tokens in the text are resolved client-side to the
/// player's own control (#1077, #2463). Strictly per-player — every player has their own VEGA.
/// </summary>
public sealed class ShipAiLine
{
    /// <summary>Locale key of the spoken line; empty ⇒ objective-only update (no new speech).</summary>
    public string LineKey { get; set; } = string.Empty;

    /// <summary>Optional substitutions for the line's {0}, {1}… slots — display strings (e.g. a world name), several
    /// packed with U+001F (#1079, <c>VegaText.SplitArgs</c>).</summary>
    public string LineArg { get; set; } = string.Empty;

    /// <summary>LLM-authored display TEXT (VEGA banter) — when non-empty the client shows this verbatim
    /// instead of localizing <see cref="LineKey"/>. Always generated in the player's language.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Locale key of the active objective chip; empty ⇒ clear the chip.</summary>
    public string ObjectiveKey { get; set; } = string.Empty;

    /// <summary>Optional {0} substitution for the objective chip (#1859) — already a display string, e.g. the
    /// body name in "a net fragment lies on {0}". Empty ⇒ the localized key is shown as-is. Additive: an
    /// older peer simply never fills it.</summary>
    public string ObjectiveArg { get; set; } = string.Empty;

    /// <summary>Progress toward the objective (e.g. blocks mined so far). 0/0 ⇒ no counter shown.</summary>
    public int ObjectiveProgress { get; set; }

    public int ObjectiveTarget { get; set; }

    /// <summary>0 = onboarding, 1 = advisor hint, 2 = memory/story, 3 = system (module/ability notices, never
    /// muted), 4 = prologue page (shown in VEGA's speech panel like every line, with a dim or the staged camera orbit
    /// behind it and a skip — #754/#760; the old full-screen overlay is gone), 5 = repeated context tip (#1077 — muted
    /// like 1, and dropped by the client while its speech queue is busy).</summary>
    public byte Kind { get; set; }

    /// <summary>Where the chip's objective comes from (#2462) — one of <see cref="ObjectiveSources"/>. The client
    /// decides the hints mute (only <see cref="ObjectiveSources.Story"/> follows it) from this instead of guessing
    /// from the key prefix. Empty = no objective, or an older server: the client then keeps its prefix guess.
    /// Additive (contractless): an older client ignores it.</summary>
    public string ObjectiveSource { get; set; } = string.Empty;

    /// <summary>True while this player's tutorial still runs, whatever the chip shows (#2462, B03): the finale
    /// objective may sit on the chip in the Guardian system while the lessons are unfinished, and the Settings
    /// button must still offer "Skip", never "Restart". Meaningful only when <see cref="ObjectiveSource"/> is set
    /// (a server that knows the field always sets the source while the tutorial runs). Additive.</summary>
    public bool TutorialActive { get; set; }

    /// <summary>The story pack this LINE belongs to (#2466) — empty for VEGA's own lines (onboarding, hints, beats).
    /// Additive: an older client ignores it.</summary>
    public string PackId { get; set; } = string.Empty;

    /// <summary>Who speaks this line (#2466): the pack's narrator id; empty = VEGA. The client maps an unknown id to
    /// VEGA's panel style. Additive.</summary>
    public string Speaker { get; set; } = string.Empty;

    /// <summary>The story pack whose objective is on the CHIP (#2466) — the client shows that pack's short name instead of
    /// "Objective:" (#2471). Since #2471 also the world story's id for VEGA's lead and the finale ("VEGA: …"); empty = no
    /// objective, or a legacy stage of an older client. Additive.</summary>
    public string ObjectivePackId { get; set; } = string.Empty;

    /// <summary>A space-flight nav target this line points at (#2471, a story step's "where" help in space — the station
    /// to dock at): the client makes it its space waypoint when it has none set, so the radar and the edge arrow lead
    /// there. Empty = none. Additive: an older client ignores it.</summary>
    public string SpaceWaypointId { get; set; } = string.Empty;

    /// <summary>The buttons this line offers (#2499), sent only to a client that announced
    /// <see cref="ClientFeatures.Choices1"/>: each entry is <see cref="ShipAiChoices.Encode"/>'s
    /// <c>labelKey␟action␟packId[␟chapterId]</c> (U+001F between the parts), the action a <see cref="StoryPackActions"/> word
    /// or <see cref="ShipAiChoices.Continue"/>. The first is the primary button (the continue control picks it), the second
    /// the secondary one (a deliberate click, tap or hold); a choice goes back as an ordinary <see cref="StoryPackIntent"/>.
    /// Empty = a plain line. Additive (contractless): an older client ignores it.</summary>
    public string[] Choices { get; set; } = System.Array.Empty<string>();
}

/// <summary>The wire form of one <see cref="ShipAiLine.Choices"/> entry (#2499): <c>labelKey␟action␟packId[␟chapterId]</c>,
/// the parts separated by U+001F (the separator <see cref="ShipAiLine.LineArg"/> packs its slots with).</summary>
public static class ShipAiChoices
{
    /// <summary>The separator between the parts of an entry.</summary>
    public const char Separator = '\u001F';

    /// <summary>The action of a button that only goes on — the line ends, nothing is sent ("Let's go").</summary>
    public const string Continue = "continue";

    /// <summary>One entry: the button's locale key, its action, the pack it acts on and (for a chapter action) the chapter.
    /// A part never carries the separator itself (it is stripped).</summary>
    public static string Encode(string labelKey, string action, string packId, string chapterId = "")
    {
        string entry = Clean(labelKey) + Separator + Clean(action) + Separator + Clean(packId);
        return string.IsNullOrEmpty(chapterId) ? entry : entry + Separator + Clean(chapterId);
    }

    /// <summary>Reads an entry back. False for anything without a label key and an action (a garbled entry is skipped, never
    /// guessed at); the pack and the chapter may be empty.</summary>
    public static bool TryDecode(string? entry, out string labelKey, out string action, out string packId, out string chapterId)
    {
        labelKey = action = packId = chapterId = string.Empty;
        if (string.IsNullOrEmpty(entry))
        {
            return false;
        }

        string[] parts = entry!.Split(Separator);
        if (parts.Length < 2 || parts.Length > 4 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return false;
        }

        labelKey = parts[0];
        action = parts[1];
        packId = parts.Length > 2 ? parts[2] : string.Empty;
        chapterId = parts.Length > 3 ? parts[3] : string.Empty;
        return true;
    }

    private static string Clean(string? part)
        => string.IsNullOrEmpty(part) ? string.Empty : part!.Replace(Separator.ToString(), string.Empty, System.StringComparison.Ordinal);
}

/// <summary>The values of <see cref="ShipAiLine.ObjectiveSource"/> (#2462). Strings, not an enum, so a later
/// source (another story pack) reaches an older client as an unknown word — which it shows: only
/// <see cref="Story"/> follows the hints mute.</summary>
public static class ObjectiveSources
{
    /// <summary>No objective on the chip — or a server that predates the field.</summary>
    public const string None = "";

    /// <summary>A tutorial lesson. Never muted.</summary>
    public const string Tutorial = "tutorial";

    /// <summary>Optional story coaching (the chapter lead). Follows the VEGA hints mute.</summary>
    public const string Story = "story";

    /// <summary>The story finale (confront the Guardian core). Never muted.</summary>
    public const string Finale = "finale";

    /// <summary>An accepted mission's objective. Never muted.</summary>
    public const string Mission = "mission";
}

/// <summary>Client → server: skip the VEGA onboarding (grants all stage milestones; the advisor hints
/// stay armed) — or, with <see cref="Restart"/>, wipe the stage milestones and run the tutorial again
/// from the intro (the way back after a skip).</summary>
public sealed class SkipOnboardingIntent
{
    public bool Restart { get; set; }
}

/// <summary>
/// Server → client: the player's VEGA milestone ledger (<c>vega:*</c> entries of
/// <see cref="Shared.State.PlayerState.Milestones"/>), sent once on join. The client reconstructs the
/// re-readable "VEGA tips" log from it — a dismissed onboarding lesson or advisor hint was unrecoverable
/// before, because only story lines (ShipAiLine Kind 2) were ever logged client-side.
/// </summary>
public sealed class VegaJournal
{
    /// <summary>Milestone ids exactly as stored server-side (<c>vega:intro</c>, <c>vega:stage:&lt;id&gt;</c>,
    /// <c>vega:hint:&lt;id&gt;</c>, <c>vega:mem:&lt;n&gt;</c>) and, since #2469, the tutorial pack's done steps
    /// (<c>story:&lt;pack&gt;:step:&lt;id&gt;</c>; the client maps them through its copy of the pack). Additive: an older client
    /// ignores the <c>story:</c> keys.</summary>
    public string[] Milestones { get; set; } = System.Array.Empty<string>();
}
