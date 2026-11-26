// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Text;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// PerfProbe's chunk-work numbers (#4044): how long the streamed view takes to settle after the world is entered, how
    /// long the far view takes to be complete, and per phase where the chunk work goes — the mesher's build on its
    /// thread, the main thread's share (dispatch copy, upload, the whole chunk pass of a frame), the receive path, and
    /// how far the mesh backlog grew. Read from <see cref="ChunkWorkStats"/>, which the pipeline feeds on every platform.
    /// </summary>
    public sealed partial class PerfProbe
    {
        private const float FillQuietSeconds = 1.5f; // no chunk arrived and nothing left to build for this long
        private const float FillGuardSeconds = 600f;
        private const int FillMinBuilds = 50;        // the view counts as streaming once this many builds started

        private float _fillSeconds = -1f;
        private float _worldReadySeconds = -1f;
        private float _streamEndSeconds = -1f;
        private float _farViewSeconds = -1f;
        private int _fillBacklogMax;
        private int _fillInFlightMax;
        private int _fillBuilds;
        private float _fillCoresBusy;
        private float _fillUploadMs;
        private int _phaseBacklogMax;
        private int _phaseInFlightMax;
        private long _phaseDetailChunks;
        private int _phaseWatchFrames;

        /// <summary>Runs from the moment the world is entered until the probe ends. It notes when the loading veil lifted
        /// (the world was ready), when the streamed view first settled (the world is ready, no chunk waits for a build,
        /// none is in flight, none arrived for <see cref="FillQuietSeconds"/>) with what the fill was made of, and when the
        /// far view first had every patch; and it tracks each phase's largest backlog and the detail pass's chunks.</summary>
        private IEnumerator WatchFill(GameBootstrap boot)
        {
            float start = Time.realtimeSinceStartup;
            int builds0 = GameBootstrap.MeshBuildsDispatched;
            while (boot != null && Time.realtimeSinceStartup - start < FillGuardSeconds)
            {
                int backlog = boot.PendingMeshCount, flight = boot.BuildsInFlight;
                _phaseBacklogMax = Mathf.Max(_phaseBacklogMax, backlog);
                _phaseInFlightMax = Mathf.Max(_phaseInFlightMax, flight);
                _phaseDetailChunks += boot.DetailChunksInReach;
                _phaseWatchFrames++;
                float elapsed = Time.realtimeSinceStartup - start;
                if (_fillSeconds < 0f)
                {
                    // Not before the first chunks were meshed: until the join lands the world reads as ready and empty.
                    bool streamed = GameBootstrap.MeshBuildsDispatched - builds0 >= FillMinBuilds;
                    WatchFillStep(boot, elapsed, backlog, flight, streamed);
                }

                var far = boot.FarView;
                if (_farViewSeconds < 0f && far != null && far.ActiveRange > 0 && far.PatchCount > 0 && far.PendingPatchCount() == 0)
                {
                    _farViewSeconds = elapsed;
                    Debug.Log($"[PerfProbe] Far view complete after {_farViewSeconds:0.0}s ({far.PatchCount} patches, range {far.ActiveRange}, {far.WorkerCount} worker(s)).");
                }

                yield return null;
            }
        }

        /// <summary>One frame of the fill watch, until the view has settled.</summary>
        private void WatchFillStep(GameBootstrap boot, float elapsed, int backlog, int flight, bool streamed)
        {
            _fillBacklogMax = Mathf.Max(_fillBacklogMax, backlog);
            _fillInFlightMax = Mathf.Max(_fillInFlightMax, flight);
            if (boot.TimeSinceLastChunk < 0.05f)
            {
                _streamEndSeconds = elapsed; // the last chunk so far
            }

            if (streamed && _worldReadySeconds < 0f && boot.WorldReady)
            {
                _worldReadySeconds = elapsed;
                Debug.Log($"[PerfProbe] World ready (the loading veil lifts) after {_worldReadySeconds:0.0}s.");
            }

            if (streamed && boot.WorldReady && backlog == 0 && flight == 0 && boot.TimeSinceLastChunk >= FillQuietSeconds)
            {
                _fillSeconds = Mathf.Max(0f, elapsed - FillQuietSeconds);
                var build = ChunkWorkStats.Build.Snapshot(reset: false);
                _fillBuilds = (int)build.Count;
                _fillCoresBusy = (float)(build.TotalMs / Mathf.Max(1f, _fillSeconds * 1000f));
                _fillUploadMs = (float)ChunkWorkStats.Upload.Snapshot(reset: false).TotalMs;
                Debug.Log($"[PerfProbe] View settled after {_fillSeconds:0.0}s (backlog max {_fillBacklogMax}, in flight max {_fillInFlightMax}).");
            }
        }

        /// <summary>Starts a phase's chunk-work window.</summary>
        private void BeginChunkWork()
        {
            ChunkWorkStats.Reset();
            _phaseBacklogMax = 0;
            _phaseInFlightMax = 0;
            _phaseDetailChunks = 0;
            _phaseWatchFrames = 0;
        }

        /// <summary>Writes the phase's chunk-work window into its result.</summary>
        private void EndChunkWork(PhaseResult r)
        {
            var build = ChunkWorkStats.Build.Snapshot(reset: false);
            var dispatch = ChunkWorkStats.Dispatch.Snapshot(reset: false);
            var upload = ChunkWorkStats.Upload.Snapshot(reset: false);
            var pass = ChunkWorkStats.FramePass.Snapshot(reset: false);
            var receive = ChunkWorkStats.Receive.Snapshot(reset: false);
            var store = ChunkWorkStats.Store.Snapshot(reset: false);
            r.buildsFinished = (int)build.Count;
            (r.buildTotalMs, r.buildP50Ms, r.buildP90Ms, r.buildMaxMs) = ((float)build.TotalMs, build.P50Ms, build.P90Ms, build.MaxMs);
            (r.dispatchTotalMs, r.dispatchP90Ms) = ((float)dispatch.TotalMs, dispatch.P90Ms);
            r.uploads = (int)upload.Count;
            (r.uploadTotalMs, r.uploadP90Ms, r.uploadMaxMs) = ((float)upload.TotalMs, upload.P90Ms, upload.MaxMs);
            (r.chunkPassTotalMs, r.chunkPassP50Ms, r.chunkPassP90Ms, r.chunkPassMaxMs) = ((float)pass.TotalMs, pass.P50Ms, pass.P90Ms, pass.MaxMs);
            r.chunksReceived = (int)receive.Count;
            (r.receiveTotalMs, r.receiveP90Ms, r.receiveMaxMs) = ((float)receive.TotalMs, receive.P90Ms, receive.MaxMs);
            r.storeTotalMs = (float)store.TotalMs;
            var detail = ChunkWorkStats.DetailPass.Snapshot(reset: false);
            (r.detailPassTotalMs, r.detailPassP50Ms, r.detailPassP90Ms, r.detailPassMaxMs) = ((float)detail.TotalMs, detail.P50Ms, detail.P90Ms, detail.MaxMs);
            r.detailChunksAvg = _phaseDetailChunks / (float)Mathf.Max(1, _phaseWatchFrames);
            var latency = ChunkWorkStats.MeshLatency.Snapshot(reset: false);
            (r.meshLatencyP50Ms, r.meshLatencyP90Ms, r.meshLatencyMaxMs) = (latency.P50Ms, latency.P90Ms, latency.MaxMs);
            r.backlogMax = _phaseBacklogMax;
            r.inFlightMax = _phaseInFlightMax;
        }

        /// <summary>The screen a run rendered to: without the pixel count a GPU time says little.</summary>
        private static string ScreenLine()
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            string scale = urp != null ? $", render scale {urp.renderScale:0.00}, MSAA {urp.msaaSampleCount}x" : string.Empty;
            return $"{Screen.width}x{Screen.height} @ {Screen.currentResolution.refreshRateRatio.value:0} Hz, {Screen.fullScreenMode}{scale}, "
                   + $"{SystemInfo.graphicsDeviceType}, rendering {SystemInfo.renderingThreadingMode}";
        }

        /// <summary>Writes the run's fill times, its screen and its machine into the result.</summary>
        private void StampFill(ProbeResult result)
        {
            result.fillSeconds = _fillSeconds;
            result.worldReadySeconds = _worldReadySeconds;
            result.streamEndSeconds = _streamEndSeconds;
            result.fillBuilds = _fillBuilds;
            result.fillCoresBusy = _fillCoresBusy;
            result.fillUploadMs = _fillUploadMs;
            result.farViewSeconds = _farViewSeconds;
            result.fillBacklogMax = _fillBacklogMax;
            result.fillInFlightMax = _fillInFlightMax;
            result.logicalCores = SystemInfo.processorCount;
            result.buildsInFlightCap = GameBootstrap.BuildsInFlightCap;
            result.screen = ScreenLine();
        }

        /// <summary>The run's fill times, for the text report's head.</summary>
        private void AppendFill(StringBuilder txt)
        {
            string ready = _worldReadySeconds >= 0f ? $"{_worldReadySeconds:0.0} s" : "not within the run";
            string settled = _fillSeconds >= 0f ? $"{_fillSeconds:0.0} s" : "not within the run";
            string far = _farViewSeconds >= 0f ? $"{_farViewSeconds:0.0} s" : "not within the run (or off)";
            txt.AppendLine($"Screen {ScreenLine()}");
            txt.AppendLine($"World ready after {ready}; view settled after {settled} (the last chunk arrived at {_streamEndSeconds:0.0} s; "
                           + $"{_fillBuilds} builds kept {_fillCoresBusy:0.00} cores busy, their uploads took {_fillUploadMs:0} ms of the main thread; "
                           + $"backlog max {_fillBacklogMax}, in flight max {_fillInFlightMax}); far view complete after {far}; "
                           + $"{SystemInfo.processorCount} logical cores, builds in flight at most {GameBootstrap.BuildsInFlightCap}");
        }

        /// <summary>One phase's chunk-work line for the text report. "per frame" divides a sum by the phase's frames.</summary>
        private static void AppendChunkWork(StringBuilder txt, PhaseResult ph)
        {
            float frames = Mathf.Max(1, ph.frames), seconds = Mathf.Max(0.001f, ph.seconds);
            txt.AppendLine($"    chunk work: {ph.buildsFinished} builds ({ph.buildsFinished / seconds:0}/s), on the worker p50/p90/max "
                           + $"{ph.buildP50Ms:0.00}/{ph.buildP90Ms:0.00}/{ph.buildMaxMs:0.0} ms ({ph.buildTotalMs / (seconds * 1000f):0.00} cores busy); "
                           + $"main thread: chunk pass p50/p90/max {ph.chunkPassP50Ms:0.00}/{ph.chunkPassP90Ms:0.00}/{ph.chunkPassMaxMs:0.0} ms "
                           + $"({ph.chunkPassTotalMs / frames:0.00} ms/frame), of it dispatch {ph.dispatchTotalMs / frames:0.00} ms/frame (p90 {ph.dispatchP90Ms:0.000} each), "
                           + $"{ph.uploads} uploads {ph.uploadTotalMs / frames:0.00} ms/frame (p90 {ph.uploadP90Ms:0.00}, max {ph.uploadMaxMs:0.0} each); "
                           + $"detail pass p50/p90/max {ph.detailPassP50Ms:0.00}/{ph.detailPassP90Ms:0.00}/{ph.detailPassMaxMs:0.0} ms "
                           + $"({ph.detailChunksAvg:0} chunks in reach); "
                           + $"received {ph.chunksReceived} chunks {ph.receiveTotalMs / frames:0.00} ms/frame (p90 {ph.receiveP90Ms:0.000}, max {ph.receiveMaxMs:0.00} each; "
                           + $"store {ph.storeTotalMs / frames:0.00} ms/frame); from arrival to first mesh p50/p90/max "
                           + $"{ph.meshLatencyP50Ms:0}/{ph.meshLatencyP90Ms:0}/{ph.meshLatencyMaxMs:0} ms; backlog max {ph.backlogMax}, in flight max {ph.inFlightMax}");
        }
    }
}
