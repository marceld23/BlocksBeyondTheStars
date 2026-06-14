using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Story;

/// <summary>
/// The story-agnostic pacing core. Pure functions over a <see cref="StoryDefinition"/> (the active pack) and
/// a mutable <see cref="StoryState"/> (per-save counters). The key rule is <b>threshold storytelling</b>:
/// beats reveal by a weighted progress score, not by which specific fragment was found — which is what lets a
/// linear arc work inside a procedurally generated, randomly-ordered world.
///
/// Nothing here touches the server, networking or persistence; the caller (GameServerStory) records events,
/// asks the engine which beats newly crossed, then speaks + persists them.
/// </summary>
public static class StoryEngine
{
    /// <summary>
    /// The weighted progress score: <c>fragments*Wf + min(kills, killCap)*Wk + milestones*Wm</c>. Machine
    /// kills are capped (diminishing returns) so combat can advance the story but never be farmed past the
    /// fragments that drive it.
    /// </summary>
    public static int Progress(StoryDefinition def, StoryState state, float progressScale = 1f)
    {
        if (def is null) throw new ArgumentNullException(nameof(def));
        if (state is null) throw new ArgumentNullException(nameof(state));

        int cappedKills = Math.Min(Math.Max(0, state.MachineKills), Math.Max(0, def.KillContributionCap));
        int raw = Math.Max(0, state.FragmentsFound) * def.FragmentWeight
                + cappedKills * def.KillWeight
                + Math.Max(0, state.Milestones) * def.MilestoneWeight;

        // World option (P8 StoryDensity): scale the score so a denser setting reveals the arc sooner. Clamped
        // to a sane range so a misconfigured scale can't invert progress.
        float scale = progressScale <= 0f ? 1f : Math.Min(4f, progressScale);
        return (int)Math.Round(raw * scale);
    }

    /// <summary>
    /// Reveals any beats whose threshold the current progress has now crossed, strictly in arc order, and
    /// advances <see cref="StoryState.BeatsRevealed"/>. Returns the newly revealed beats (in order) for the
    /// caller to speak/persist; returns an empty list when nothing crossed. Monotonic: a later call with the
    /// same or lower progress reveals nothing. A beat only reveals once its predecessor has, so an
    /// out-of-order (lower) threshold can never skip ahead.
    /// </summary>
    public static IReadOnlyList<StoryBeat> AdvanceBeats(StoryDefinition def, StoryState state, float progressScale = 1f)
    {
        if (def is null) throw new ArgumentNullException(nameof(def));
        if (state is null) throw new ArgumentNullException(nameof(state));

        int progress = Progress(def, state, progressScale);
        var beats = def.Beats;
        List<StoryBeat>? revealed = null;

        while (state.BeatsRevealed < beats.Count && beats[state.BeatsRevealed].Threshold <= progress)
        {
            (revealed ??= new List<StoryBeat>()).Add(beats[state.BeatsRevealed]);
            state.BeatsRevealed++;
        }

        return (IReadOnlyList<StoryBeat>?)revealed ?? Array.Empty<StoryBeat>();
    }

    /// <summary>True once every beat in the arc has been revealed (the finale is in reach).</summary>
    public static bool AllBeatsRevealed(StoryDefinition def, StoryState state)
        => state.BeatsRevealed >= def.Beats.Count;
}
