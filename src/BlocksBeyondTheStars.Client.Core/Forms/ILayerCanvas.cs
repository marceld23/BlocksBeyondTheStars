// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the layer-canvas editors of the main menu share (the form editor over <see cref="FormCanvasModel"/>, "My
    /// tools" over <see cref="ToolLookCanvasModel"/>): a voxel volume painted one horizontal layer at a time, with
    /// strokes as undo steps. The client's editor base drives either model through this surface; what a cell holds and
    /// how it is painted stays with each model.
    /// </summary>
    public interface ILayerCanvas
    {
        /// <summary>The layer (Y slice) the canvas shows.</summary>
        int Layer { get; }

        /// <summary>Shows another layer, clamped to the volume.</summary>
        void SetLayer(int layer);

        /// <summary>Starts a stroke: everything painted until <see cref="EndStroke"/> is one undo step.</summary>
        void BeginStroke();

        /// <summary>Ends the stroke begun by <see cref="BeginStroke"/>.</summary>
        void EndStroke();

        /// <summary>Takes the last step back; false when there is nothing to undo.</summary>
        bool Undo();

        /// <summary>Brings the last undone step back; false when there is nothing to redo.</summary>
        bool Redo();
    }
}
