// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How much chunk meshing the desktop client runs at once (#4045). The mesher builds on worker threads there, so the
    /// limit is the machine's cores — not a count per frame. Before, the client started four builds a frame whatever the
    /// machine, so the throughput was four times the frame rate: the slower the frame (the High preset), the slower the
    /// world filled, while the worker cores idled. Measured on the reference laptop (16 logical processors, High, view
    /// distance 8, about 30 fps): while walking, a chunk waited 0.9 s in the median and up to 9.5 s for its first mesh
    /// under that rule, 0.2 s and at most 1.2 s under this one.
    /// <para><b>In flight.</b> A build is in flight from its dispatch until the main thread takes its result off the
    /// finished queue, so the cap bounds the worker threads busy at once AND the finished geometry waiting for its
    /// upload. It follows the logical processors, less the ones the main thread, the render thread and the bundled
    /// server keep busy while a view streams in, and never falls below the four the client always started in a frame —
    /// so a machine with up to eight logical processors meshes about as before. A build takes about a millisecond for a
    /// chunk of air or rock and 30 ms and more for a chunk full of surface detail, so eight at once are several hundred
    /// builds a second: past that the main thread's uploads are the limit.</para>
    /// <para><b>Per frame.</b> The main thread's own share — copying a chunk and its neighbourhood for a dispatch
    /// (about 0.06 ms), and uploading a finished build (about 0.5 ms, 3 ms for a large one) — gets a time budget instead
    /// of a count, so a burst of large chunks cannot make one long frame. The upload budget grows with the frame time: a
    /// client that runs at 30 fps has fewer frames to upload in, and when the graphics card is the limit the main thread
    /// waits for it anyway. One dispatch and one upload always run, so the pipeline moves on every frame.</para>
    /// <para><b>While loading.</b> Behind the loading veil nobody sees a frame, so the budgets are wide and more builds
    /// are in flight: the veil lifts when the view around the player is meshed, and that is what the player waits for.</para>
    /// The browser builds inline on its only thread and keeps its own budgets (#1819); this rule is not asked there.
    /// </summary>
    public static class ChunkBuildBudget
    {
        /// <summary>The fewest builds in flight on any machine: the four a frame every desktop client started before.</summary>
        public const int MinInFlight = 4;

        /// <summary>The most builds in flight in play, and behind the loading veil, whatever the machine.</summary>
        public const int MaxInFlight = 8, MaxInFlightLoading = 12;

        /// <summary>Logical processors left to the main thread, the render thread and the bundled server.</summary>
        public const int ReservedProcessors = 4;

        /// <summary>Milliseconds per frame the main thread spends on starting builds (after the first, which always
        /// runs): in play, and behind the loading veil.</summary>
        public const double DispatchMs = 1.5, LoadingDispatchMs = 5.0;

        /// <summary>The upload budget's bounds in milliseconds per frame, and the share of the last frame's time it takes
        /// between them: 3 ms at 60 fps and faster, 5 ms at 30 fps, never more than 8 ms.</summary>
        public const double MinUploadMs = 3.0, MaxUploadMs = 8.0, UploadFrameShare = 0.15;

        /// <summary>The upload budget behind the loading veil: wide, but a frame of a transition that plays while the
        /// world loads still fits.</summary>
        public const double LoadingUploadMs = 10.0;

        /// <summary>The builds in flight a machine with <paramref name="logicalProcessors"/> logical processors runs in play.</summary>
        public static int InFlight(int logicalProcessors)
            => Math.Max(MinInFlight, Math.Min(MaxInFlight, logicalProcessors - ReservedProcessors));

        /// <summary>The builds in flight behind the loading veil: every processor but the reserved ones, up to
        /// <see cref="MaxInFlightLoading"/>.</summary>
        public static int InFlightWhileLoading(int logicalProcessors)
            => Math.Max(MinInFlight, Math.Min(MaxInFlightLoading, logicalProcessors - ReservedProcessors));

        /// <summary>Milliseconds the main thread may spend on starting builds in a frame.</summary>
        public static double DispatchBudgetMs(bool loading) => loading ? LoadingDispatchMs : DispatchMs;

        /// <summary>Milliseconds the main thread may spend on uploads in a frame that follows one of
        /// <paramref name="lastFrameMs"/> milliseconds.</summary>
        public static double UploadMs(double lastFrameMs, bool loading)
            => loading ? LoadingUploadMs : Math.Max(MinUploadMs, Math.Min(MaxUploadMs, lastFrameMs * UploadFrameShare));

        /// <summary>Whether another build may start this frame: the cap has room, and it is the frame's first or the
        /// dispatch budget is not spent.</summary>
        public static bool MayDispatch(int inFlight, int cap, int startedThisFrame, double elapsedMs, double budgetMs)
            => inFlight < cap && (startedThisFrame == 0 || elapsedMs < budgetMs);

        /// <summary>Whether another finished build may be uploaded this frame: it is the frame's first, or the frame's
        /// upload budget (<see cref="UploadMs"/>) is not spent.</summary>
        public static bool MayUpload(int uploadedThisFrame, double elapsedMs, double budgetMs)
            => uploadedThisFrame == 0 || elapsedMs < budgetMs;

        /// <summary>The rule before #4045 — four builds started and eight uploaded per frame, whatever the machine and
        /// whatever they cost. Kept for PerfProbe's <c>-perfFeature meshbudget=frame</c>, so the two rules can be compared
        /// in one build.</summary>
        public const int LegacyDispatchesPerFrame = 4, LegacyUploadsPerFrame = 8;
    }
}
