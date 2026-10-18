// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// One entry in the clip manifest — a single short clip to record. Plain serializable fields so Unity's
    /// <see cref="JsonUtility"/> can load it; the field initializers are the defaults applied when a key is
    /// absent from the JSON. See <see cref="ClipManifest"/> for the file shape and <see cref="ClipDirector"/>
    /// for how each field drives capture.
    /// </summary>
    [Serializable]
    public sealed class ClipSpec
    {
        public string name = "clip";          // output sub-folder + mp4 name; also selected by -clipName
        public string scene = "space";        // space | surface | cockpit
        public string planet = "";            // optional: pin the world to this planet type (surface scenes)
        public float lengthSeconds = 8f;
        public int fps = 30;
        public bool hud;                       // true = full frame incl. HUD; false = clean HUD-free view
        public bool openMenu;                  // cockpit: open the Tab menu (needs hud=true to be visible)
        public int[] menuTabs;                 // cockpit: tab indices to step through while open (0=Inv,1=Craft,2=Tech,3=Ship,4=Map,5=Missions,6=Character,7=Alliances,8=Story,9=Companions,10=Photos)

        // Recording-camera move (HUD-free clips only): how the capture camera itself moves.
        //   static    — follow the live view camera (no move)
        //   yaw_sweep — DIEGETIC: sweep the SHIP heading (space); camera stays on the view
        //   orbit     — orbit the capture camera around the live view, sweeping yawStart..yawEnd degrees
        //   dolly     — push the capture camera along the view direction by dollyDistance metres
        //   pan       — rotate the capture camera in place, sweeping yaw (yawStart..End) + pitch (pitchStart..End)
        //   rig, subject — still-shot cameras (#2765), see below
        public string camera = "static";
        public float yawStart;                 // yaw_sweep ship heading / orbit angle / pan yaw — first frame
        public float yawEnd;                   // ... last frame
        public float pitchStart;               // pan camera pitch — first frame
        public float pitchEnd;                 // ... last frame
        public float orbitRadius = 12f;        // orbit: distance from the focus point
        public float orbitHeight = 3f;         // orbit: camera height above the focus
        public float dollyDistance = 6f;       // dolly: metres moved along the view dir over the clip (+ = push in)

        // Diegetic motion (the world actually moves; all client-side, deterministic under captureFramerate).
        public float shipThrottle;             // space: 0..1 forward cruise (0 = drift)
        public float shipPitch;                // space: ship climb(+)/dive(-) heading, degrees
        public float walkForward;              // surface: -1..1 walk (forward = +1)
        public float walkStrafe;               // surface: -1..1 strafe
        public float lookYawStart;             // surface: body/look yaw sweep while walking — first frame
        public float lookYawEnd;               // ... last frame
        public float lookPitch;                // surface: look pitch

        // Environment + placement pins (#2405) — the look checks need dawn, rain, a cave, the sea floor … on purpose.
        //   timeOfDay — local time of day to pin, 0..1 (0 = midnight, 0.27 ≈ dawn, 0.5 = noon, 0.75 ≈ dusk);
        //               negative = leave the world's own clock alone (the default, as before)
        //   weather   — weather to pin: a state (clear, clouds, rain, storm, fog, ground_fog, blizzard, gale, …) or a
        //               precipitation form (snow, sleet, hail, sandstorm, dust, ash, …); "" = the world's own weather
        //   pose      — where the on-foot player stands (surface scenes): spawn (default, open ground near the ship),
        //               cave (a covered room underground), underwater (three blocks of water overhead), forest (dense
        //               flora), shore (dry ground beside water), ridge (the highest open spot around), vista (#2765: a
        //               raised open spot whose view scores best — depth, relief, variety, no wall or ship in front, the
        //               sun beside or behind), here (#2765: stay where a `poi` jump put the player and only turn to the
        //               best view — a raised rig camera then looks over the site). When no such spot is streamed in near
        //               the spawn the clip logs it and falls back to the spawn pose.
        // The time and the weather are also sent to the server as /settime and /setweather (#2765), so the animals that
        // are awake and the server's weather match the pinned sky.
        public float timeOfDay = -1f;
        public string weather = "";
        public string pose = "";

        // Still shots (#2765): a still is a one-frame clip (lengthSeconds × fps = 1); capture-stills.ps1 copies its
        // frames/frame_00001.png out. HUD-free clips (hud = false) also hide the first-person hand and the block
        // selection frame. Two more recording-camera moves for stills:
        //   rig     — the camera sits camHeight metres above the player's eye, camBack behind it and camSide to its right,
        //             looking along the player's heading, camPitch degrees below the horizon (negative = up)
        //   subject — the camera frames the creature that `subject` picks: on a third line, facing into the frame,
        //             subjectFill of the frame height, open land behind it
        public float camHeight;
        public float camBack;
        public float camSide;
        public float camPitch;
        public float fov;                      // the recording camera's vertical field of view in degrees; 0 = the view's
        public bool astronaut;                 // third person: the player's avatar stands in the world (seen by a rig)
        public string subject = "";            // creature | giant — what the "subject" camera frames
        public float subjectFill = 0.38f;
        public int creatures = -1;             // world rule at creation: 0 Off, 1 Rare, 2 Normal, 3 Frequent, 4 Extreme; -1 = capture default
        public string poi = "";                // a "/tp <kind>" target (village, city, lodge, monument, factory, …) reached before the pose
        public string[] adminCommands;         // admin intents sent once the pose is reached: "<command> [argument]", e.g. "summon_giant sandworm"
        public float settleSeconds;            // extra wait before rolling (creature spawns, a summoned giant walking up)

        // Story-pack cutscene (#2468) — "<pack>/<id>" (e.g. "vega_protocol/prologue", "vega_protocol/__recap"): the clip
        // plays that scene through the cutscene director the moment recording starts, so a scene can be checked by eye on
        // a fixed seed. Use camera "static" (the capture follows the director's camera) and hud true (the captions and the
        // letterbox are UI); lengthSeconds should cover the scene. "" = no cutscene (the default).
        public string cutscene = "";
    }

    /// <summary>
    /// A list of clips to record, loaded from a JSON file passed via <c>-clipManifest &lt;path&gt;</c>. Shape:
    /// <code>{ "clips": [ { "name": "space_pan", "scene": "space", "camera": "yaw_sweep", ... }, ... ] }</code>
    /// One player run records ONE clip (selected with <c>-clipName</c>) and quits — capture-clips.ps1 reads the
    /// same file and loops the player over every clip, which keeps each run a single, proven world start
    /// (multi-world-per-process is intentionally avoided).
    /// </summary>
    [Serializable]
    public sealed class ClipManifest
    {
        public ClipSpec[] clips = Array.Empty<ClipSpec>();

        public static ClipManifest Load(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return null;
                }

                var m = JsonUtility.FromJson<ClipManifest>(File.ReadAllText(path));
                if (m == null || m.clips == null || m.clips.Length == 0)
                {
                    Debug.LogWarning($"[Clip] manifest '{path}' has no clips.");
                    return null;
                }

                return m;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Clip] failed to load manifest '{path}': {e.Message}");
                return null;
            }
        }

        public ClipSpec Find(string clipName)
        {
            if (clips == null)
            {
                return null;
            }

            foreach (var c in clips)
            {
                if (c != null && string.Equals(c.name, clipName, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }

            return null;
        }
    }
}
