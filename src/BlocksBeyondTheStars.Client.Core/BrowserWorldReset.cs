// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// "New world" for the one-world browser singleplayer (#1181). Deleting the save blob alone is not
    /// enough: until the fresh world has been persisted, two mechanisms would quietly bring the old one
    /// back — the deployment-storage migration (<see cref="PreviousDeploymentStorage"/>, #1177) adopting an
    /// older deployment's copy, and the Glitch Cloud Save fetch restoring the cloud copy at boot. So a reset
    /// also arms a marker file in the save folder; both mechanisms stand down while it exists, and the
    /// browser host clears it the moment the new world is on disk. Unity-free so the rules are unit-tested;
    /// the Unity side supplies the folder and the IDBFS sync.
    /// </summary>
    public static class BrowserWorldReset
    {
        /// <summary>Marker file in the save folder: "a reset is pending — do not restore an old world".</summary>
        public const string MarkerFile = "world.reset";

        /// <summary>True while a reset is pending (marker present): the old world must not come back.</summary>
        public static bool IsPending(string saveDir)
        {
            try
            {
                return File.Exists(Path.Combine(saveDir, MarkerFile));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Deletes the world blob (and a half-written <c>.tmp</c> next to it) and arms the marker.
        /// Everything else in the folder — the cloud-version meta, anything the player did not ask to lose —
        /// stays. Returns true when a blob existed and was deleted; never throws (a failed delete is logged by
        /// the caller through the return value being false while the file still exists).</summary>
        public static bool Reset(string saveDir, string blobFile)
        {
            bool deleted = false;
            try
            {
                Directory.CreateDirectory(saveDir);
                string blob = Path.Combine(saveDir, blobFile);
                if (File.Exists(blob))
                {
                    File.Delete(blob);
                    deleted = true;
                }

                string tmp = blob + ".tmp";
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }

                File.WriteAllText(Path.Combine(saveDir, MarkerFile), DateTime.UtcNow.ToString("o"));
            }
            catch (Exception)
            {
                // best effort — the caller re-checks the folder state
            }

            return deleted;
        }

        /// <summary>Disarms the marker — the fresh world is persisted, the reset has held.</summary>
        public static void ClearPending(string saveDir)
        {
            try
            {
                string marker = Path.Combine(saveDir, MarkerFile);
                if (File.Exists(marker))
                {
                    File.Delete(marker);
                }
            }
            catch (Exception)
            {
                // best effort — a stale marker only delays the next adoption/cloud restore, never loses data
            }
        }
    }
}
