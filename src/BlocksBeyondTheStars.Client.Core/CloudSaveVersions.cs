// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The one rule behind "does the cloud copy replace the local world?" for the glitch.fun cloud save
    /// (the Unity <c>GlitchCloudSaves</c> applies it; kept Unity-free so it can be tested). A fetch that
    /// only PEEKS at the cloud blob (the deep-linked name lookup, #1322) must not record the version it
    /// saw: recording is what tells the next fetch "this browser already has that version", and a peek
    /// that recorded it made the boot right after it fall back to the older local blob (#1355).
    /// </summary>
    public static class CloudSaveVersions
    {
        /// <summary>True when the fetched cloud version should be used instead of the local blob: it is
        /// newer than the last version this browser synced, or there is no local blob at all (fresh
        /// browser, cleared site data — then even an already-seen version beats an empty world).</summary>
        public static bool CloudWins(int cloudVersion, int lastSyncedVersion, bool localExists)
            => cloudVersion > lastSyncedVersion || !localExists;
    }
}
