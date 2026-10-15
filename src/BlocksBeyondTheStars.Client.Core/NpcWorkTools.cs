// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What a working NPC's tool does on the client: how often it swings (#1869, the swing cadence table, moved here from
    /// <c>NpcView</c>) and — since the wilderness folk (#2647) — the work sounds. The woodcutter's axe chops with a quiet,
    /// short-range sound of its own; the hunter's bow never swings by itself (it draws and releases when the server's arrow
    /// arrives); the bow shot and a falling tree are positional cues the server sends, kept short-range and quiet so a busy
    /// lodge never turns into a jumble (the maintainer rule of #2592). Unity-free.
    /// </summary>
    public static class NpcWorkTools
    {
        /// <summary>The woodcutter's axe (<c>NetNpc.Held</c>).</summary>
        public const string Axe = "npc_axe";

        /// <summary>The hunter's bow (<c>NetNpc.Held</c>).</summary>
        public const string Bow = "npc_bow";

        /// <summary>The axe's chop — client-local, on the woodcutter's swing.</summary>
        public const string ChopClip = "npc_axe_chop";

        /// <summary>The bow's twang — a server <c>SoundFx</c> at the shot.</summary>
        public const string BowShotClip = "npc_bow_shot";

        /// <summary>A felled tree coming down — a server <c>SoundFx</c>.</summary>
        public const string TreeFallClip = "tree_fall";

        /// <summary>The activity a woodcutter shows while felling (<c>NetNpc.ActivityKey</c>) — only then does its swing
        /// make the chop sound; a swing on the way or at the hut stays silent.</summary>
        public const string ChoppingActivity = "npc.activity.chopping";

        /// <summary>The blow lands this long after a swing starts (the avatar's 0.42 s swing has the tool level in front at
        /// about two thirds) — when the chop is heard.</summary>
        public const float ChopImpactSeconds = 0.28f;

        /// <summary>A chop is heard only from a woodcutter this close to the listener (metres).</summary>
        public const float ChopRadius = 16f;

        /// <summary>The chop's volume (before master × SFX): quiet, it is background work.</summary>
        public const float ChopVolume = 0.35f;

        /// <summary>At least this long between two chop sounds, whoever swings — two woodcutters never stack.</summary>
        public const float ChopGapSeconds = 0.5f;

        /// <summary>The bow shot and a falling tree are not played beyond this distance from the listener (their 3D
        /// rolloff is silent there anyway, so they never take a pooled voice for nothing).</summary>
        public const float WorkSoundRadius = 20f;

        /// <summary>
        /// How often an NPC plays a work gesture (seconds, min/max): the tool sets the pace — the craftsman hammers, the
        /// gardener hoes, the guard keeps the blade still, the woodcutter chops at a steady beat — else the theme/role: miners
        /// chip away often, settlers/builders place now and then, vendors/researchers gesture occasionally.
        /// </summary>
        public static (float Lo, float Hi) Cadence(string? held, string? theme, string? role)
        {
            switch (held)
            {
                case "npc_hammer": return (1.2f, 2.4f);
                case "npc_hoe": return (2.0f, 3.5f);
                case "blade": return (30f, 60f);
                case "npc_pickaxe": return (1.0f, 2.0f);     // 2026-09: the blockfarmer quarries
                case "npc_camera": return (3.0f, 5.5f);      // the streamer films
                case "npc_microphone": return (4.0f, 7.0f);  // the reporter takes notes
                case Axe: return (2.2f, 3.4f);               // #2647: the woodcutter's steady chop
            }

            string t = (theme ?? string.Empty).ToLowerInvariant();
            string r = (role ?? string.Empty).ToLowerInvariant();
            if (t.Contains("miner", System.StringComparison.Ordinal))
            {
                return (1.2f, 2.4f);
            }

            if (r == "settler" || t.Contains("settler", System.StringComparison.Ordinal) || t.Contains("build", System.StringComparison.Ordinal))
            {
                return (2.6f, 4.6f);
            }

            return (4.5f, 8.5f); // vendors / quartermasters / traders / researchers
        }

        /// <summary>Whether an NPC holding <paramref name="held"/> plays the periodic work swing — everyone but the hunter,
        /// whose bow only moves for a shot.</summary>
        public static bool SwingsPeriodically(string? held) => held != Bow;

        /// <summary>The volume a server <c>SoundFx</c> <paramref name="soundId"/> plays at: the bow shot and a falling tree
        /// are quiet; every other id keeps <paramref name="fallback"/>.</summary>
        public static float FxVolume(string? soundId, float fallback) => soundId switch
        {
            BowShotClip => 0.45f,
            TreeFallClip => 0.55f,
            _ => fallback,
        };

        /// <summary>Whether a server <c>SoundFx</c> <paramref name="soundId"/> at <paramref name="distance"/> metres from
        /// the listener is played at all: the NPC work sounds only within <see cref="WorkSoundRadius"/>; every other id
        /// always (its own rolloff decides).</summary>
        public static bool FxAudible(string? soundId, float distance)
            => !(soundId is BowShotClip or TreeFallClip) || distance <= WorkSoundRadius;
    }

    /// <summary>
    /// #2647: the budget of the woodcutters' chop sounds — within <see cref="NpcWorkTools.ChopRadius"/> of the listener, at
    /// most one every <see cref="NpcWorkTools.ChopGapSeconds"/> (so two woodcutters never stack), only while the NPC is
    /// actually felling, and never while VEGA or a cutscene speaks (the NPC voices hold back then too). Pure: the caller
    /// passes the time and the distance.
    /// </summary>
    public sealed class NpcChopGate
    {
        private float _last = float.NegativeInfinity;

        /// <summary>
        /// Asks to play a chop at <paramref name="now"/> from a woodcutter <paramref name="distance"/> metres from the
        /// listener, whose activity is <paramref name="activityKey"/>. Returns the volume to play it at
        /// (<see cref="NpcWorkTools.ChopVolume"/>), or 0 when it stays silent; a granted chop starts the gap.
        /// </summary>
        public float Request(float now, float distance, string? activityKey, bool narratorBusy)
        {
            if (narratorBusy || activityKey != NpcWorkTools.ChoppingActivity || distance > NpcWorkTools.ChopRadius
                || now - _last < NpcWorkTools.ChopGapSeconds)
            {
                return 0f;
            }

            _last = now;
            return NpcWorkTools.ChopVolume;
        }

        /// <summary>Forgets the last chop (a world change).</summary>
        public void Reset() => _last = float.NegativeInfinity;
    }
}
