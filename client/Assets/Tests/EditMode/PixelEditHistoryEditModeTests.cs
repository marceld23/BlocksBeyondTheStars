// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The undo/redo stack the pixel editors share (<see cref="PixelEditHistory"/>). Before it, the editor
    /// kept ONE snapshot that the undo button swapped in and out: the last stroke could be taken back and
    /// nothing before it, which is not what a child means by "undo". These pin the rules that are easy to
    /// get subtly wrong and impossible to see in a screenshot.
    /// </summary>
    public sealed class PixelEditHistoryEditModeTests
    {
        private static byte[] Grid(params byte[] cells) => cells;

        [Test]
        public void WalksBackThroughSeveralSteps()
        {
            var history = new PixelEditHistory();
            history.PushGrid(0, Grid(0, 0), Grid(1, 0));
            history.PushGrid(0, Grid(1, 0), Grid(1, 2));
            history.PushGrid(0, Grid(1, 2), Grid(3, 2));

            Assert.AreEqual(3, history.Count);
            Assert.AreEqual(new byte[] { 1, 2 }, history.Undo().GridBefore);
            Assert.AreEqual(new byte[] { 1, 0 }, history.Undo().GridBefore);
            Assert.AreEqual(new byte[] { 0, 0 }, history.Undo().GridBefore);
            Assert.IsFalse(history.CanUndo);
            Assert.IsNull(history.Undo()); // the bottom of the stack is not an error, it is just the bottom
        }

        [Test]
        public void RedoPutsStepsBackInOrder()
        {
            var history = new PixelEditHistory();
            history.PushGrid(0, Grid(0), Grid(1));
            history.PushGrid(0, Grid(1), Grid(2));
            history.Undo();
            history.Undo();

            Assert.AreEqual(new byte[] { 1 }, history.Redo().GridAfter);
            Assert.AreEqual(new byte[] { 2 }, history.Redo().GridAfter);
            Assert.IsFalse(history.CanRedo);
            Assert.IsNull(history.Redo());
        }

        [Test]
        public void PaintingAfterAnUndoForgetsWhatWasRedoable()
        {
            var history = new PixelEditHistory();
            history.PushGrid(0, Grid(0), Grid(1));
            history.PushGrid(0, Grid(1), Grid(2));
            history.Undo();

            history.PushGrid(0, Grid(1), Grid(9)); // a fresh stroke on top of the undone state

            Assert.IsFalse(history.CanRedo, "the abandoned branch must not come back on Redo");
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual(new byte[] { 9 }, history.Undo().GridAfter);
        }

        [Test]
        public void AStrokeThatChangedNothingIsNotAStep()
        {
            var history = new PixelEditHistory();
            history.PushGrid(0, Grid(4, 4), Grid(4, 4));

            Assert.AreEqual(0, history.Count, "repainting pixels in the colour they already had must not eat an undo");
            Assert.IsFalse(history.CanUndo);
        }

        [Test]
        public void TheOldestStepFallsOffAFullStack()
        {
            var history = new PixelEditHistory(maxSteps: 3);
            for (byte i = 1; i <= 5; i++)
            {
                history.PushGrid(0, Grid((byte)(i - 1)), Grid(i));
            }

            Assert.AreEqual(3, history.Count);
            Assert.AreEqual(new byte[] { 5 }, history.Undo().GridAfter);
            Assert.AreEqual(new byte[] { 4 }, history.Undo().GridAfter);
            Assert.AreEqual(new byte[] { 3 }, history.Undo().GridAfter);
            Assert.IsFalse(history.CanUndo);
        }

        [Test]
        public void OneColourWheelDragIsOneStep()
        {
            var history = new PixelEditHistory();
            history.PushColor(0, Color.black, Color.red, fromDrag: true);
            history.PushColor(0, Color.red, Color.green, fromDrag: true);
            history.PushColor(0, Color.green, Color.blue, fromDrag: true);

            Assert.AreEqual(1, history.Count, "a drag over the wheel is one gesture, so it is one undo");
            var step = history.Undo();
            Assert.IsTrue(step.IsColor);
            Assert.AreEqual(Color.black, step.ColorBefore, "undo goes back to where the drag STARTED");
            Assert.AreEqual(Color.blue, step.ColorAfter);
        }

        [Test]
        public void ANewDragAfterTheHandLiftsIsItsOwnStep()
        {
            var history = new PixelEditHistory();
            history.PushColor(0, Color.black, Color.red, fromDrag: true);
            history.EndColorRun(); // the mouse button came up
            history.PushColor(0, Color.red, Color.green, fromDrag: true);

            Assert.AreEqual(2, history.Count);
        }

        [Test]
        public void ClickingASwatchIsAlwaysItsOwnStep()
        {
            var history = new PixelEditHistory();
            history.PushColor(0, Color.black, Color.red, fromDrag: false);
            history.PushColor(0, Color.red, Color.green, fromDrag: false);

            Assert.AreEqual(2, history.Count);
        }

        [Test]
        public void AStrokeEndsTheColourDragEvenWithoutTheMouseComingUp()
        {
            var history = new PixelEditHistory();
            history.PushColor(0, Color.black, Color.red, fromDrag: true);
            history.PushGrid(0, Grid(0), Grid(1));
            history.PushColor(0, Color.red, Color.green, fromDrag: true);

            Assert.AreEqual(3, history.Count, "the stroke between them belongs to neither colour step");
        }

        [Test]
        public void StepsRememberWhichPartTheyHappenedOn()
        {
            var history = new PixelEditHistory();
            history.PushGrid(1, Grid(0), Grid(1));   // torso
            history.PushGrid(3, Grid(0), Grid(1));   // legs

            Assert.AreEqual(3, history.Undo().Subject, "the editor carries the tab back with the step");
            Assert.AreEqual(1, history.Undo().Subject);
        }

        [Test]
        public void ColourStepsOnDifferentPartsNeverMerge()
        {
            var history = new PixelEditHistory();
            history.PushColor(1, Color.black, Color.red, fromDrag: true);
            history.PushColor(2, Color.white, Color.blue, fromDrag: true);

            Assert.AreEqual(2, history.Count);
        }

        [Test]
        public void WearingAnOutfitClearsEverything()
        {
            var history = new PixelEditHistory();
            history.PushGrid(0, Grid(0), Grid(1));
            history.Undo();
            history.Clear();

            Assert.AreEqual(0, history.Count);
            Assert.IsFalse(history.CanUndo);
            Assert.IsFalse(history.CanRedo, "undoing into the look you took off would mix two outfits together");
        }
    }
}
