// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Globalization;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Which transition cinematic a <see cref="TransitionShotPlan"/> stages.</summary>
    public enum TransitionShotStage
    {
        /// <summary>The own ship sinking onto its pad (#3140): outside first, the end in the cockpit if need be.</summary>
        Touchdown,

        /// <summary>The own ship lifting off (#3141): the start in the cockpit if need be, outside for the climb.</summary>
        Liftoff,
    }

    /// <summary>What a <see cref="TransitionShotPlan"/> is planned from — all in scene coordinates.</summary>
    public sealed class TransitionShotInput
    {
        public TransitionShotInput(CinematicStageScan.ClearSampler path, CinematicStageScan.ClearSampler spot)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Spot = spot ?? throw new ArgumentNullException(nameof(spot));
        }

        /// <summary>Lines of sight and camera moves: the terrain (and other parked ships); the own hull reads as air. Unknown
        /// cells must read as blocked.</summary>
        public CinematicStageScan.ClearSampler Path { get; }

        /// <summary>Where a camera may sit: the terrain plus every parked hull. Unknown cells must read as blocked.</summary>
        public CinematicStageScan.ClearSampler Spot { get; }

        /// <summary>The centre of the hull's box at rest (standing on its pad).</summary>
        public float CenterX { get; set; }

        /// <inheritdoc cref="CenterX"/>
        public float CenterY { get; set; }

        /// <inheritdoc cref="CenterX"/>
        public float CenterZ { get; set; }

        /// <summary>The half sizes of the hull's box.</summary>
        public float ExtentX { get; set; }

        /// <inheritdoc cref="ExtentX"/>
        public float ExtentY { get; set; }

        /// <inheritdoc cref="ExtentX"/>
        public float ExtentZ { get; set; }

        /// <summary>The player's eyes (the camera's home at either end of the shot).</summary>
        public float EyeX { get; set; }

        /// <inheritdoc cref="EyeX"/>
        public float EyeY { get; set; }

        /// <inheritdoc cref="EyeX"/>
        public float EyeZ { get; set; }

        /// <summary>How far beyond the hull's radius the far camera ring stands (the near ring at 70 %).</summary>
        public float Distance { get; set; } = 10f;

        /// <summary>The low spots' height over the hull's bottom (the classic shot).</summary>
        public float GroundHeight { get; set; } = 2.2f;

        /// <summary>The touchdown's drop height (<see cref="TransitionFlightPath.TouchdownDrop"/>); unused for a lift-off.</summary>
        public float Drop { get; set; } = 40f;
    }

    /// <summary>
    /// The voxel shot plan of the own ship's landing and lift-off cinematics (#3258). The director used to pick its camera
    /// spot with Physics (colliders not cooked yet on a fresh world, beyond the collider range, an unchecked fallback
    /// "straight above the rear", the line of sight checked once to the hull at rest) — on a pad in a hall or a courtyard
    /// the camera ended up inside the buildings, and the chunk visibility walk (#1823) rightly hid everything around it.
    /// Now, before the shot, every candidate spot (8 directions × 2 distances × 3 heights: ground, roofline, crane) is
    /// sampled along the stage at <see cref="SampleCount"/> moments against the client's voxel world
    /// (<see cref="CinematicStageScan"/>; unknown cells are blocked): the spot clear, the lines of sight to the hull's centre
    /// and top clear, the camera's move between two samples clear. The best spot is the one that sees the most of the shot
    /// (the classic low rear quarter wins a tie); whatever no spot can see plays in the cockpit instead.
    /// <list type="bullet">
    /// <item><b>Touchdown</b>: outside from the start while the descent is visible; at its first blocked moment the shot
    /// cuts into the cockpit (on the dust beat when it is near) — but only after <see cref="MinOutsideSeconds"/>, else it
    /// plays in the cockpit from the start.</item>
    /// <item><b>Lift-off</b>: outside from the start when the spot sees all of it and the glide out of the player's eyes
    /// is clear (the classic shot); else the cockpit first, and a cut outside on the first beat (lift, climb, boom) from
    /// which the spot sees the rest of the climb.</item>
    /// </list>
    /// Pure and deterministic; covered by <c>TransitionShotPlanTests</c>.
    /// </summary>
    public sealed partial class TransitionShotPlan
    {
        /// <summary>Moments sampled per stage.</summary>
        public const int SampleCount = 16;

        /// <summary>A touchdown shows less than this outside → it plays in the cockpit from the start.</summary>
        public const float MinOutsideSeconds = 1f;

        /// <summary>A lift-off that would come outside for less than this before the cloud cover stays in the cockpit.</summary>
        public const float MinTailSeconds = 0.5f;

        /// <summary>The touchdown's cut moves back to the dust beat when that is at most this much earlier.</summary>
        public const float BeatSnapSeconds = 0.6f;

        private TransitionShotPlan(TransitionShotStage stage, float duration)
        {
            Stage = stage;
            Duration = duration;
            Direction = DistanceStep = HeightStep = -1;
        }

        /// <summary>The cinematic this plan stages.</summary>
        public TransitionShotStage Stage { get; }

        /// <summary>The planned stretch, in seconds of stage time (touchdown: descent + settle; lift-off: up to the clouds).</summary>
        public float Duration { get; }

        /// <summary>True when a spot sees enough of the shot to go outside at all.</summary>
        public bool HasSpot { get; private set; }

        /// <summary>The chosen camera spot (meaningful while <see cref="HasSpot"/>).</summary>
        public float SpotX { get; private set; }

        /// <inheritdoc cref="SpotX"/>
        public float SpotY { get; private set; }

        /// <inheritdoc cref="SpotX"/>
        public float SpotZ { get; private set; }

        /// <summary>The chosen spot's direction index (0 rear-right … 7 front; -1 none).</summary>
        public int Direction { get; private set; }

        /// <summary>The chosen spot's distance step (0 far, 1 near; -1 none).</summary>
        public int DistanceStep { get; private set; }

        /// <summary>The chosen spot's height step (0 ground, 1 roofline, 2 crane; -1 none).</summary>
        public int HeightStep { get; private set; }

        /// <summary>The stretch the chosen spot sees, before the beat rules (<see cref="VisibleTo"/> &lt; <see cref="VisibleFrom"/>
        /// when it sees nothing).</summary>
        public float VisibleFrom { get; private set; }

        /// <inheritdoc cref="VisibleFrom"/>
        public float VisibleTo { get; private set; } = -1f;

        /// <summary>The stretch the director plays outside (a touchdown from 0, a lift-off up to <see cref="Duration"/>);
        /// empty when the whole shot plays in the cockpit.</summary>
        public float OutsideFrom { get; private set; }

        /// <inheritdoc cref="OutsideFrom"/>
        public float OutsideTo { get; private set; }

        /// <summary>True when there is an outside stretch at all.</summary>
        public bool HasOutside => OutsideTo > OutsideFrom;

        /// <summary>The straight camera glide between the spot and the player's eyes is clear: a touchdown's from the end of
        /// its push-in to the eyes, a lift-off's from the eyes to the spot.</summary>
        public bool GlideClear { get; private set; }

        /// <summary>The camera glides between the eyes and the spot (the classic shot): a touchdown outside to the end, a
        /// lift-off outside from the start, with <see cref="GlideClear"/>.</summary>
        public bool Glide => HasOutside && GlideClear
                             && (Stage == TransitionShotStage.Touchdown ? OutsideTo >= Duration : OutsideFrom <= 0f);

        /// <summary>How many candidate spots were sampled (the search stops at a spot nothing can beat).</summary>
        public int CandidatesChecked { get; private set; }

        /// <summary>One line for the log: the stretches, the way home (or out), the spot.</summary>
        public string Describe(bool cabinGlide)
        {
            var c = CultureInfo.InvariantCulture;
            string segments;
            if (!HasOutside)
            {
                segments = string.Format(c, "cockpit 0–{0:0.0} s", Duration);
            }
            else if (Stage == TransitionShotStage.Touchdown)
            {
                segments = OutsideTo >= Duration
                    ? string.Format(c, "outside 0–{0:0.0} s", Duration)
                    : string.Format(c, "outside 0–{0:0.0} s, cockpit {0:0.0}–{1:0.0} s", OutsideTo, Duration);
            }
            else
            {
                segments = OutsideFrom <= 0f
                    ? string.Format(c, "outside 0–{0:0.0} s", Duration)
                    : string.Format(c, "cockpit 0–{0:0.0} s, outside {0:0.0}–{1:0.0} s", OutsideFrom, Duration);
            }

            string spot = HasSpot
                ? string.Format(c, "spot {0} {1} {2} (sees {3:0.0}–{4:0.0} s)", DirectionNames[Direction],
                    DistanceStep == 0 ? "far" : "near", HeightNames[HeightStep], VisibleFrom, VisibleTo)
                : "no clear spot";
            return string.Format(c, "{0}: {1}, {2}; {3}, {4} of {5} spots sampled",
                Stage == TransitionShotStage.Touchdown ? "touchdown" : "lift-off", segments, HomeLine(cabinGlide), spot,
                CandidatesChecked, CandidateCount);
        }

        private string HomeLine(bool cabinGlide)
        {
            bool touchdown = Stage == TransitionShotStage.Touchdown;
            if (Glide)
            {
                return touchdown ? "glide to the eyes" : "glide out of the eyes";
            }

            bool endsInside = touchdown ? !HasOutside || OutsideTo < Duration : !HasOutside || OutsideFrom > 0f;
            string way = endsInside && cabinGlide ? "cabin glide" : "blink";
            return touchdown ? way + " to the eyes" : way + " to the helm";
        }

        /// <summary>The direction names of the log line, in candidate order.</summary>
        internal static readonly string[] DirectionNames =
        {
            "rear-right", "rear-left", "front-right", "front-left", "right", "left", "rear", "front",
        };

        private static readonly string[] HeightNames = { "ground", "roofline", "crane" };
    }
}
