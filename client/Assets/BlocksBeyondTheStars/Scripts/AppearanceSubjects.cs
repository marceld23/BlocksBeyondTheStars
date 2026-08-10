// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;
using BodyPaint = BlocksBeyondTheStars.Shared.State.BodyPaint;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Builds the five tabs of the appearance screen (#899) — face, torso, arms, legs, helmet — as
    /// <see cref="FaceEditor.Subject"/>s, so the in-game menu and the main-menu Avatar Designer show the
    /// same screen over their own storage (settings vs. the designer's scratch values).
    /// <para>
    /// Each tab carries its canvas layout (from <see cref="BodyPaintKit"/> for the body parts), its payload
    /// codec, where a commit goes — and the <b>base colour</b> that shows through unpainted pixels, which is
    /// what merged "pick a colour" and "paint it" into one screen instead of two menu levels apart. The
    /// helmet takes the torso colour: its shell is part of the suit (see <c>PlayerAvatar.ApplyBodyPaint</c>).
    /// </para>
    /// </summary>
    public static class AppearanceSubjects
    {
        /// <summary>Which base colour a tab tints: 0 skin, 1 torso, 2 arms, 3 legs.</summary>
        private static int ColorSlot(int part) => part switch
        {
            BodyPaint.Arms => 2,
            BodyPaint.Legs => 3,
            _ => 1, // torso AND helmet — the helmet shell is suit-coloured
        };

        private static string TabKey(int part) => part switch
        {
            BodyPaint.Torso => "ui.paint.tab.torso",
            BodyPaint.Arms => "ui.paint.tab.arms",
            BodyPaint.Legs => "ui.paint.tab.legs",
            _ => "ui.paint.tab.helmet",
        };

        /// <summary>The five tabs, reading and writing through the host's accessors.</summary>
        public static List<FaceEditor.Subject> Build(
            Func<string> getFace, Action<string> setFace,
            Func<int, string> getPaint, Action<int, string> setPaint,
            Func<int, Color> getColor, Action<int, Color> setColor)
        {
            var subjects = new List<FaceEditor.Subject>
            {
                new FaceEditor.Subject
                {
                    LabelKey = "ui.paint.tab.face",
                    TitleKey = "ui.face.title",
                    HintKey = "ui.face.hint",
                    Pixels = getFace() ?? string.Empty,
                    OnApply = setFace,
                    GetBaseColor = () => getColor(0),
                    SetBaseColor = c => setColor(0, c),
                    PreviewPart = -1,
                },
            };

            for (int part = 0; part < BodyPaint.PartCount; part++)
            {
                int which = part;
                int slot = ColorSlot(part);
                subjects.Add(new FaceEditor.Subject
                {
                    LabelKey = TabKey(part),
                    TitleKey = BodyPaintKit.PartKey(part),
                    HintKey = part == BodyPaint.Helmet ? "ui.paint.body.helmet_hint" : "ui.face.hint",
                    GridW = BodyPaintKit.CanvasW(part),
                    GridH = BodyPaintKit.CanvasH(part),
                    Pixels = getPaint(part) ?? string.Empty,
                    Decode = pixels => BodyPaintKit.ToCanvas(which, pixels),
                    Encode = grid => BodyPaintKit.FromCanvas(which, grid),
                    ColumnLabelKeys = BodyPaintKit.ColumnKeys(part),
                    RowLabelKeys = BodyPaintKit.RowKeys(part),
                    OnApply = pixels => setPaint(which, pixels),
                    GetBaseColor = () => getColor(slot),
                    SetBaseColor = c => setColor(slot, c),
                    PreviewPart = which,
                });
            }

            return subjects;
        }

        /// <summary>The host's stored appearance for the editor's live preview figure.</summary>
        public static FaceEditor.AppearanceSnapshot Snapshot(
            Func<int, Color> getColor, Func<string> getFace, Func<int, string> getPaint)
        {
            var paints = new string[BodyPaint.PartCount];
            for (int part = 0; part < paints.Length; part++)
            {
                paints[part] = getPaint(part) ?? string.Empty;
            }

            return new FaceEditor.AppearanceSnapshot
            {
                Skin = getColor(0),
                Torso = getColor(1),
                Arms = getColor(2),
                Legs = getColor(3),
                Face = getFace() ?? string.Empty,
                Paints = paints,
            };
        }
    }
}
