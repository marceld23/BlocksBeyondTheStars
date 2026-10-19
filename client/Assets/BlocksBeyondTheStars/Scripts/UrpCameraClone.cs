// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The URP half of cloning the view camera for an off-screen capture — the clip recorder's clone
    /// (<see cref="ClipFrameWriter"/>, #2405) and the photo camera (<see cref="CameraTool"/>, #2809).
    /// <c>Camera.CopyFrom</c> copies the Camera only: the URP data beside it (post-processing, anti-aliasing, the depth +
    /// opaque copies, the renderer) stays at the clone's defaults, so a capture rendered without the post stack and with
    /// the heat-haze quad re-drawing the frame from an opaque texture that did not exist (a black panel over every hot
    /// world). <see cref="Sync"/> mirrors the live camera's data and forces the two copies on: the clone holds no request
    /// of its own.
    /// </summary>
    public static class UrpCameraClone
    {
        /// <summary>Copies the URP camera data of <paramref name="source"/> onto <paramref name="clone"/> (call after
        /// <c>CopyFrom</c>). <paramref name="rendererIndex"/> is the preset's renderer (<see cref="ClientSettings.RendererIndex"/>:
        /// its SSAO tier); −1 keeps the clone's default. A no-op under the Built-in pipeline.</summary>
        public static void Sync(Camera source, Camera clone, int rendererIndex)
        {
            if (source == null || clone == null)
            {
                return;
            }

            var src = source.GetUniversalAdditionalCameraData();
            var dst = clone.GetUniversalAdditionalCameraData();
            if (src == null || dst == null)
            {
                return;
            }

            dst.renderPostProcessing = src.renderPostProcessing;
            dst.antialiasing = src.antialiasing;
            dst.antialiasingQuality = src.antialiasingQuality;
            dst.renderShadows = src.renderShadows;
            dst.dithering = src.dithering;
            dst.stopNaN = src.stopNaN;
            dst.volumeLayerMask = src.volumeLayerMask;
            dst.volumeTrigger = src.volumeTrigger;
            dst.requiresColorOption = CameraOverrideOption.On;
            dst.requiresDepthOption = CameraOverrideOption.On;
            if (rendererIndex >= 0)
            {
                dst.SetRenderer(rendererIndex);
            }
        }
    }
}
