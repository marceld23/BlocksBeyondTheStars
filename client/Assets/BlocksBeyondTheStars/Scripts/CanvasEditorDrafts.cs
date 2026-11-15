// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The draft plumbing the paint and shape editors share on the <see cref="EditorShell"/> (#3689): the texture editor,
    /// the appearance screen / avatar designer, the block paint tool, the form and tool-look editors and the shape designer.
    /// Unattended editor screenshots (<c>-captureEditors</c>, <see cref="ScreenshotDirector.CapturingEditors"/>) must
    /// neither show the "Continue your draft?" question nor touch the player's drafts: they get a throwaway drafts folder
    /// and no offer. The draft text itself is <see cref="CanvasDraftFormat"/> (a versioned header + the editor's payload).
    /// </summary>
    internal static class CanvasEditorDrafts
    {
        /// <summary>The <see cref="EditorShellOptions.DraftRoot"/> to pass: null (the player's drafts folder) normally, a
        /// throwaway folder under the temporary cache while the editors are being photographed.</summary>
        public static string DraftRoot
            => ScreenshotDirector.CapturingEditors ? Path.Combine(Application.temporaryCachePath, "editor-capture-drafts") : null;

        /// <summary>Offers the draft left from last time (<see cref="EditorShell.OfferDraft"/>) — never during a capture run.
        /// Returns true when the question is on screen.</summary>
        public static bool Offer(EditorShell shell)
            => shell != null && !ScreenshotDirector.CapturingEditors && shell.OfferDraft();
    }
}
