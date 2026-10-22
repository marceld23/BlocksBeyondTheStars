// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>Both layer-canvas models (#2906) serve the shared editor base through <see cref="ILayerCanvas"/>: a layer
/// change, a stroke and its undo/redo taken through the interface act on the model itself.</summary>
public sealed class LayerCanvasModelsTests
{
    [Fact]
    public void FormCanvasModel_IsALayerCanvas_AndItsLayerAndHistoryWorkThroughIt()
    {
        var model = new FormCanvasModel();
        ILayerCanvas canvas = Assert.IsAssignableFrom<ILayerCanvas>(model);

        canvas.SetLayer(2);
        Assert.Equal(2, canvas.Layer);
        Assert.Equal(2, model.Layer);

        canvas.BeginStroke();
        Assert.True(model.Paint(1, 1, true));
        canvas.EndStroke();
        Assert.True(model.Get(1, 2, 1));

        Assert.True(canvas.Undo());
        Assert.False(model.Get(1, 2, 1));
        Assert.False(canvas.Undo());

        Assert.True(canvas.Redo());
        Assert.True(model.Get(1, 2, 1));
        Assert.False(canvas.Redo());
    }

    [Fact]
    public void ToolLookCanvasModel_IsALayerCanvas_AndItsLayerAndHistoryWorkThroughIt()
    {
        var model = new ToolLookCanvasModel();
        ILayerCanvas canvas = Assert.IsAssignableFrom<ILayerCanvas>(model);

        canvas.SetLayer(1);
        Assert.Equal(1, canvas.Layer);
        Assert.Equal(1, model.Layer);

        canvas.BeginStroke();
        Assert.True(model.Paint(4, 5, 12));
        canvas.EndStroke();
        Assert.Equal(12, model.Get(4, 1, 5));

        Assert.True(canvas.Undo());
        Assert.Equal(0, model.Get(4, 1, 5));
        Assert.False(canvas.Undo());

        Assert.True(canvas.Redo());
        Assert.Equal(12, model.Get(4, 1, 5));
        Assert.False(canvas.Redo());
    }
}
