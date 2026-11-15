// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3695: the one undo history the editors share (<see cref="UndoHistory{T}"/>). It must cover what the three canvas models
/// did on their own — capped steps, a stroke as one step, a no-op is no step, a new step forgets the redo tail — plus the
/// colour-drag merging of the face editor and an "unsaved" answer that survives undo.
/// </summary>
public sealed class UndoHistoryTests
{
    /// <summary>A tiny editor over a string state, driven like the editors drive their models.</summary>
    private sealed class Doc
    {
        public string State = "a";
        public readonly UndoHistory<string> History;

        public Doc(int capacity = UndoHistory<string>.DefaultCapacity) => History = new UndoHistory<string>(capacity);

        public void Change(string next)
        {
            string before = State;
            State = next;
            History.Record(before, State);
        }

        public bool Undo()
        {
            if (!History.TryUndo(State, out var restore))
            {
                return false;
            }

            State = restore;
            return true;
        }

        public bool Redo()
        {
            if (!History.TryRedo(State, out var restore))
            {
                return false;
            }

            State = restore;
            return true;
        }
    }

    [Fact]
    public void UndoRedo_WalkTheStatesBackAndForth()
    {
        var doc = new Doc();
        doc.Change("b");
        doc.Change("c");
        Assert.True(doc.History.CanUndo);
        Assert.False(doc.History.CanRedo);

        Assert.True(doc.Undo());
        Assert.Equal("b", doc.State);
        Assert.True(doc.Undo());
        Assert.Equal("a", doc.State);
        Assert.False(doc.Undo());
        Assert.Equal("a", doc.State);

        Assert.True(doc.Redo());
        Assert.Equal("b", doc.State);
        Assert.True(doc.Redo());
        Assert.Equal("c", doc.State);
        Assert.False(doc.Redo());
    }

    [Fact]
    public void ANewStep_ForgetsTheRedoTail()
    {
        var doc = new Doc();
        doc.Change("b");
        doc.Change("c");
        doc.Undo();
        doc.Change("x");
        Assert.False(doc.History.CanRedo);
        doc.Undo();
        Assert.Equal("b", doc.State);
    }

    [Fact]
    public void AChangeThatChangedNothing_IsNoStep()
    {
        var doc = new Doc();
        doc.Change("a");
        Assert.False(doc.History.CanUndo);
        Assert.False(doc.History.IsDirty);
    }

    [Fact]
    public void AFullHistory_DropsTheOldestStep()
    {
        var doc = new Doc(capacity: 3);
        foreach (string s in new[] { "b", "c", "d", "e" })
        {
            doc.Change(s);
        }

        Assert.Equal(3, doc.History.UndoCount);
        while (doc.Undo())
        {
        }

        Assert.Equal("b", doc.State); // "a" fell off the bottom
    }

    [Fact]
    public void AGesture_IsOneStep_AndAGestureThatChangedNothingIsNone()
    {
        var doc = new Doc();
        doc.History.Begin(doc.State);
        doc.State = "a1";
        doc.History.Record("a"); // a change inside the gesture belongs to it
        doc.History.Begin("a1");  // a second Begin keeps the first "before"
        doc.State = "a2";
        Assert.True(doc.History.InGesture);
        Assert.True(doc.History.Commit(doc.State));
        Assert.Equal(1, doc.History.UndoCount);
        doc.Undo();
        Assert.Equal("a", doc.State);

        doc.History.Begin(doc.State);
        Assert.False(doc.History.Commit(doc.State));
        Assert.Equal(0, doc.History.UndoCount);
    }

    [Fact]
    public void UndoDuringAGesture_TakesTheGestureBack()
    {
        var doc = new Doc();
        doc.History.Begin(doc.State);
        doc.State = "painted";
        Assert.True(doc.Undo());
        Assert.Equal("a", doc.State);
        Assert.False(doc.History.InGesture);
        Assert.True(doc.Redo());
        Assert.Equal("painted", doc.State);
    }

    [Fact]
    public void CancelledGesture_LeavesNoStep()
    {
        var doc = new Doc();
        doc.History.Begin(doc.State);
        Assert.Equal("a", doc.History.GestureStart);
        doc.History.Cancel();
        Assert.False(doc.History.InGesture);
        Assert.False(doc.History.CanUndo);
    }

    [Fact]
    public void Merge_AColourDragIsOneStep_UntilTheRunEnds()
    {
        var h = new UndoHistory<string>();
        h.Merge("red", "red1", "colour");
        h.Merge("red1", "red2", "colour");
        h.Merge("red2", "red3", "colour");
        Assert.Equal(1, h.UndoCount);
        Assert.True(h.TryUndo("red3", out var back));
        Assert.Equal("red", back);

        h.TryRedo("red", out _);
        h.EndMerge();
        h.Merge("red3", "blue", "colour");
        Assert.Equal(2, h.UndoCount);
        h.Merge("blue", "green", "other");
        Assert.Equal(3, h.UndoCount);
    }

    [Fact]
    public void Merge_AfterAnUndo_StartsANewStep()
    {
        var h = new UndoHistory<string>();
        h.Merge("a", "b", "colour");
        h.TryUndo("b", out _);
        h.Merge("a", "c", "colour");
        Assert.Equal(1, h.UndoCount);
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void IsDirty_FollowsTheSavedState_AcrossUndoAndRedo()
    {
        var doc = new Doc();
        Assert.False(doc.History.IsDirty);
        doc.Change("b");
        Assert.True(doc.History.IsDirty);
        doc.History.MarkSaved();
        Assert.False(doc.History.IsDirty);
        doc.Change("c");
        Assert.True(doc.History.IsDirty);
        doc.Undo();
        Assert.False(doc.History.IsDirty); // back at the saved "b"
        doc.Undo();
        Assert.True(doc.History.IsDirty);
        doc.Redo();
        Assert.False(doc.History.IsDirty);
    }

    [Fact]
    public void IsDirty_AMergedChangeAfterSavingIsUnsaved()
    {
        var h = new UndoHistory<string>();
        h.Merge("a", "b", "colour");
        h.MarkSaved();
        h.Merge("b", "c", "colour");
        Assert.True(h.IsDirty);
    }

    [Fact]
    public void ResetAndClear_DropTheSteps_ResetAlsoTheUnsavedState()
    {
        var doc = new Doc();
        doc.Change("b");
        doc.History.Clear();
        Assert.False(doc.History.CanUndo);
        Assert.True(doc.History.IsDirty); // the outfit swap keeps "unsaved"

        doc.Change("c");
        doc.History.Reset();
        Assert.False(doc.History.CanUndo);
        Assert.False(doc.History.IsDirty); // another piece of work was opened
    }

    [Fact]
    public void Changed_IsRaisedForEveryChangeOfTheHistory()
    {
        var doc = new Doc();
        int raised = 0;
        doc.History.Changed += () => raised++;
        doc.Change("b");
        doc.Undo();
        doc.Redo();
        doc.History.MarkSaved();
        doc.History.Reset();
        Assert.Equal(5, raised);
    }

    [Fact]
    public void ByteSnapshots_CompareByContent()
    {
        var h = new UndoHistory<byte[]>(sameState: UndoSnapshots.Bytes);
        Assert.False(h.Record(new byte[] { 1, 2 }, new byte[] { 1, 2 }));
        Assert.True(h.Record(new byte[] { 1, 2 }, new byte[] { 1, 3 }));
        Assert.True(UndoSnapshots.Bytes.Equals(new byte[] { 4 }, new byte[] { 4 }));
        Assert.Equal(UndoSnapshots.Bytes.GetHashCode(new byte[] { 4, 5 }), UndoSnapshots.Bytes.GetHashCode(new byte[] { 4, 5 }));
    }
}
