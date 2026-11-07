// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Sounders;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>
    /// One run of the note-key minigame (#3352) on the canvas: the word's slots at the top, two buttons to hear the word or
    /// the current letter again, and the alphabet table — seven rows (the first tone), four columns (the second tone), the
    /// headers named by the locale's note names (<c>ui.note.names</c>). A sung tone lights its row header (first tone of a
    /// letter) or its column header (second tone), so the table teaches itself. Pick a cell with the pointer or the arrows +
    /// confirm; a click on a header plays that tone. Keys: Primary = the word again, Secondary = the letter again.
    /// </summary>
    public sealed class NoteKeyRun
    {
        public const int W = 600, H = 440;
        internal const float NoteSeconds = 0.34f;
        internal const int GridX = 210, GridY = 124, RowHeadW = 36, ColHeadH = 24, CellW = 80, CellH = 36, Gap = 4;
        internal const int BtnX = 16, BtnW = 176, BtnH = 40, BtnWordY = 150, BtnLetterY = 202;
        internal const int Rows = 7;

        private static readonly Rgba Bg = Rgba.Rgb(10, 12, 26);
        private static readonly Rgba Ink = Rgba.Rgb(230, 222, 255);
        private static readonly Rgba Dim = Rgba.Rgb(96, 88, 132);
        private static readonly Rgba Violet = Rgba.Rgb(180, 140, 255);
        private static readonly Rgba CellBg = Rgba.Rgb(24, 20, 46);
        private static readonly Rgba Lit = Rgba.Rgb(64, 48, 120);
        private static readonly Rgba Ok = Rgba.Rgb(124, 255, 176);
        private static readonly Rgba Soft = Rgba.Rgb(255, 196, 120);

        private readonly MinigameApi _api;
        private readonly int _instrument;
        private readonly string[] _toneNames;
        private Canvas2D _canvas = null!;
        private int _curRow, _curCol;
        private int _hlRow = -1, _hlCol = -1;
        private int _flashRow = -1, _flashCol = -1;
        private bool _flashOk;
        private int _singId;
        private string _message = string.Empty;
        private bool _over;

        internal NoteKeyRun(MinigameApi api, NoteKeyPuzzle puzzle, int instrument)
        {
            _api = api;
            Puzzle = puzzle;
            _instrument = instrument;
            _toneNames = ToneNames(api);
        }

        internal NoteKeyPuzzle Puzzle { get; }

        /// <summary>The note names the headers show: the locale's (<c>ui.note.names</c>, eight names), else C D E F G A B C'.</summary>
        private static string[] ToneNames(MinigameApi api)
        {
            string names = api.Localize("ui.note.names") ?? string.Empty;
            string[] split = names.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            return split.Length == SounderAlphabet.ToneCount ? split : SounderAlphabet.ToneNames;
        }

        internal void Start()
        {
            _canvas = _api.Canvas(W, H);
            _api.Bind(MinigameAction.Left, () => MoveCursor(0, -1));
            _api.Bind(MinigameAction.Right, () => MoveCursor(0, 1));
            _api.Bind(MinigameAction.Up, () => MoveCursor(-1, 0));
            _api.Bind(MinigameAction.Down, () => MoveCursor(1, 0));
            _api.Bind(MinigameAction.Confirm, () => PickCell(_curRow, _curCol));
            _api.Bind(MinigameAction.Primary, SingWord);
            _api.Bind(MinigameAction.Secondary, SingLetter);
            _api.Pointer(OnPointer);
            if (Puzzle.RoundCount == 0)
            {
                _api.Complete(0, 1); // nothing to decode — never a dead end
                return;
            }

            UpdateHud();
            Say("minigame.note_key.listen");
            _api.After(SingWordThenLetter, 0.4f);
        }

        // ---------------- singing ----------------

        private void SingWord() => Sing(Puzzle.CurrentWordNotes);

        private void SingLetter() => Sing(Puzzle.CurrentLetterNotes);

        /// <summary>The whole word, then (a beat later) the letter to find.</summary>
        private void SingWordThenLetter()
        {
            if (_over)
            {
                return;
            }

            float length = Sing(Puzzle.CurrentWordNotes);
            int id = _singId;
            _api.After(() =>
            {
                if (id == _singId)
                {
                    SingLetter();
                }
            }, length + 0.5f);
        }

        /// <summary>Sings <paramref name="notes"/>: each tone sounds and lights its header — the first tone of a letter its row,
        /// the second its column. A newer song cuts the lights of an older one. Returns its length in seconds.</summary>
        private float Sing(int[] notes)
        {
            int id = ++_singId;
            int inLetter = 0;
            for (int i = 0; i < notes.Length; i++)
            {
                int tone = notes[i];
                if (tone < 0)
                {
                    inLetter = 0;
                    continue;
                }

                bool first = inLetter++ % 2 == 0;
                _api.After(() =>
                {
                    _api.PlayNote(_instrument, tone);
                    if (id == _singId)
                    {
                        _hlRow = first ? tone : _hlRow;
                        _hlCol = first ? -1 : tone;
                        Draw();
                    }
                }, i * NoteSeconds);
            }

            float length = notes.Length * NoteSeconds;
            _api.After(() =>
            {
                if (id == _singId)
                {
                    _hlRow = _hlCol = -1;
                    Draw();
                }
            }, length + 0.25f);
            return length;
        }

        /// <summary>One tone from a header: it sounds and lights its row or column for a moment.</summary>
        private void LightTone(int tone, bool isRow)
        {
            int id = ++_singId;
            _api.PlayNote(_instrument, tone);
            _hlRow = isRow ? tone : -1;
            _hlCol = isRow ? -1 : tone;
            Draw();
            _api.After(() =>
            {
                if (id == _singId)
                {
                    _hlRow = _hlCol = -1;
                    Draw();
                }
            }, NoteSeconds + 0.25f);
        }

        // ---------------- picking ----------------

        private void MoveCursor(int dRow, int dCol)
        {
            _curRow = System.Math.Clamp(_curRow + dRow, 0, Rows - 1);
            _curCol = System.Math.Clamp(_curCol + dCol, 0, SounderAlphabet.Columns - 1);
            while (SounderAlphabet.LetterOf(_curRow, _curCol) is null && _curCol > 0)
            {
                _curCol--; // the last row holds only Y and Z
            }

            Draw();
        }

        /// <summary>A pick of the cell at (<paramref name="row"/>, <paramref name="col"/>).</summary>
        internal void PickCell(int row, int col)
        {
            if (_over || SounderAlphabet.LetterOf(row, col) is not { } letter)
            {
                return;
            }

            _curRow = row;
            _curCol = col;
            int[] target = Puzzle.CurrentLetterNotes;
            var result = Puzzle.Pick(letter);
            _flashRow = row;
            _flashCol = col;
            _flashOk = result is NoteKeyPick.Correct or NoteKeyPick.WordDone or NoteKeyPick.AllDone;
            OnPicked(result, letter, row, col, target);
            UpdateHud();
            Draw();
        }

        private void OnPicked(NoteKeyPick result, char letter, int row, int col, int[] target)
        {
            switch (result)
            {
                case NoteKeyPick.Correct:
                    Say("minigame.note_key.right");
                    _api.After(SingLetter, 0.5f);
                    break;
                case NoteKeyPick.Wrong:
                    Say("minigame.note_key.not_yet");
                    Sing(new[] { row, col, SounderAlphabet.Rest, SounderAlphabet.Rest, target[0], target[1] });
                    break;
                case NoteKeyPick.Revealed:
                    Say("minigame.note_key.revealed");
                    _api.After(SingLetter, 0.8f);
                    break;
                case NoteKeyPick.WordDone:
                    Say("minigame.note_key.word_done");
                    _api.After(SingWordThenLetter, 1.2f);
                    break;
                case NoteKeyPick.AllDone:
                    Say("minigame.note_key.all_done");
                    _over = true;
                    _api.After(() => _api.Complete(Puzzle.Score, Puzzle.Rating), 1.4f);
                    break;
                case NoteKeyPick.Failed:
                    Say("minigame.note_key.failed");
                    _over = true;
                    _api.After(() => _api.Fail(Puzzle.Score), 1.2f);
                    break;
            }
        }

        private void OnPointer(PointerInput p)
        {
            if (p.Phase != PointerPhase.Down)
            {
                return;
            }

            if (InRect(p, BtnX, BtnWordY, BtnW, BtnH))
            {
                SingWord();
            }
            else if (InRect(p, BtnX, BtnLetterY, BtnW, BtnH))
            {
                SingLetter();
            }
            else if (NoteKeyLayout.HitHeader(p.X, p.Y, out int tone, out bool isRow))
            {
                LightTone(tone, isRow); // a header sings its tone, so the player can compare
            }
            else if (NoteKeyLayout.HitCell(p.X, p.Y, out int row, out int col))
            {
                PickCell(row, col);
            }
        }

        private static bool InRect(PointerInput p, int x, int y, int w, int h) => p.X >= x && p.X < x + w && p.Y >= y && p.Y < y + h;

        private void Say(string key)
        {
            _message = _api.Localize(key).ToUpperInvariant();
            if (Puzzle.Done || Puzzle.Failed || key != "minigame.note_key.revealed")
            {
                return;
            }

            // The shown letter is the one just written into the word — the last of the spelled ones, or of the last answer.
            string spelled = Puzzle.Spelled.Length > 0 ? Puzzle.Spelled : Puzzle.Answers.Count > 0 ? Puzzle.Answers[Puzzle.Answers.Count - 1] : string.Empty;
            if (spelled.Length > 0)
            {
                _message += " " + spelled[spelled.Length - 1];
            }
        }

        private void UpdateHud()
        {
            _api.Hud(_api.Localize("minigame.note_key.round"), System.Math.Min(Puzzle.Round + 1, Puzzle.RoundCount) + "/" + Puzzle.RoundCount);
            _api.Hud(_api.Localize("minigame.note_key.mistakes"), Puzzle.Mistakes + "/" + NoteKeyPuzzle.MaxMistakes);
            _api.Hud(Puzzle.Score);
        }

        // ---------------- drawing ----------------

        internal void Draw()
        {
            if (_canvas is null)
            {
                return;
            }

            _canvas.Clear(Bg);
            DrawWord();
            _canvas.DrawTextCentered(W / 2, 92, _message, Soft, 2);
            DrawButton(BtnWordY, "minigame.note_key.word_again");
            DrawButton(BtnLetterY, "minigame.note_key.letter_again");
            _canvas.DrawText(BtnX, 262, _api.Localize("minigame.note_key.known").ToUpperInvariant() + " " + Puzzle.KnownCount + "/26", Dim, 1);
            DrawTable();
        }

        private void DrawWord()
        {
            string round = (_api.Localize("minigame.note_key.round") + " " + System.Math.Min(Puzzle.Round + 1, Puzzle.RoundCount) + "/" + Puzzle.RoundCount).ToUpperInvariant();
            _canvas.DrawText(16, 12, round, Violet, 2);
            string word = Puzzle.Done ? (Puzzle.Answers.Count > 0 ? Puzzle.Answers[Puzzle.Answers.Count - 1] : string.Empty) : Puzzle.Word;
            const int box = 34, gap = 8;
            int x0 = (W - ((word.Length * (box + gap)) - gap)) / 2;
            for (int i = 0; i < word.Length; i++)
            {
                int x = x0 + (i * (box + gap));
                bool found = Puzzle.Done || i < Puzzle.LetterIndex;
                bool current = !Puzzle.Done && i == Puzzle.LetterIndex;
                _canvas.FillRect(x, 40, box, 40, current ? Lit : CellBg);
                _canvas.DrawRect(x, 40, box, 40, current ? Violet : Dim);
                _canvas.DrawTextCentered(x + (box / 2), 50, found ? word[i].ToString() : current ? "?" : "-", found ? Ok : Ink, 3);
            }
        }

        private void DrawButton(int y, string key)
        {
            _canvas.FillRect(BtnX, y, BtnW, BtnH, CellBg);
            _canvas.DrawRect(BtnX, y, BtnW, BtnH, Violet);
            _canvas.DrawTextCentered(BtnX + (BtnW / 2), y + 13, _api.Localize(key).ToUpperInvariant(), Ink, 2);
        }

        private void DrawTable()
        {
            for (int col = 0; col < SounderAlphabet.Columns; col++)
            {
                var (x, y, w, h) = NoteKeyLayout.ColumnHeader(col);
                _canvas.FillRect(x, y, w, h, col == _hlCol ? Violet : CellBg);
                _canvas.DrawTextCentered(x + (w / 2), y + 5, _toneNames[col], col == _hlCol ? Bg : Ink, 2);
            }

            for (int row = 0; row < Rows; row++)
            {
                var (x, y, w, h) = NoteKeyLayout.RowHeader(row);
                _canvas.FillRect(x, y, w, h, row == _hlRow ? Violet : CellBg);
                _canvas.DrawTextCentered(x + (w / 2), y + 11, _toneNames[row], row == _hlRow ? Bg : Ink, 2);
                for (int col = 0; col < SounderAlphabet.Columns; col++)
                {
                    DrawCell(row, col);
                }
            }
        }

        private void DrawCell(int row, int col)
        {
            if (SounderAlphabet.LetterOf(row, col) is not { } letter)
            {
                return; // the last row holds only Y and Z
            }

            var (x, y, w, h) = NoteKeyLayout.Cell(row, col);
            bool lit = row == _hlRow && (_hlCol < 0 || col == _hlCol);
            _canvas.FillRect(x, y, w, h, lit ? Lit : CellBg);
            bool flash = row == _flashRow && col == _flashCol;
            var edge = flash ? (_flashOk ? Ok : Soft) : row == _curRow && col == _curCol ? Violet : Dim;
            _canvas.DrawRect(x, y, w, h, edge);
            bool known = Puzzle.Knows(letter);
            _canvas.DrawTextCentered(x + (w / 2), y + 8, known ? letter.ToString() : "?", known ? Ink : Dim, 3);
        }
    }

    /// <summary>The table's geometry on the note-key canvas — shared by the drawing and the hit tests (and the tests).</summary>
    public static class NoteKeyLayout
    {
        /// <summary>The rectangle of the cell at (<paramref name="row"/>, <paramref name="col"/>).</summary>
        public static (int X, int Y, int W, int H) Cell(int row, int col)
            => (NoteKeyRun.GridX + NoteKeyRun.RowHeadW + NoteKeyRun.Gap + (col * (NoteKeyRun.CellW + NoteKeyRun.Gap)),
                NoteKeyRun.GridY + NoteKeyRun.ColHeadH + NoteKeyRun.Gap + (row * (NoteKeyRun.CellH + NoteKeyRun.Gap)),
                NoteKeyRun.CellW, NoteKeyRun.CellH);

        /// <summary>The header of column <paramref name="col"/> (its tone is the second of a letter).</summary>
        public static (int X, int Y, int W, int H) ColumnHeader(int col)
        {
            var (x, _, w, _) = Cell(0, col);
            return (x, NoteKeyRun.GridY, w, NoteKeyRun.ColHeadH);
        }

        /// <summary>The header of row <paramref name="row"/> (its tone is the first of a letter).</summary>
        public static (int X, int Y, int W, int H) RowHeader(int row)
        {
            var (_, y, _, h) = Cell(row, 0);
            return (NoteKeyRun.GridX, y, NoteKeyRun.RowHeadW, h);
        }

        public static bool HitCell(float px, float py, out int row, out int col)
        {
            for (row = 0; row < NoteKeyRun.Rows; row++)
            {
                for (col = 0; col < SounderAlphabet.Columns; col++)
                {
                    var (x, y, w, h) = Cell(row, col);
                    if (px >= x && px < x + w && py >= y && py < y + h && SounderAlphabet.LetterOf(row, col) is not null)
                    {
                        return true;
                    }
                }
            }

            row = col = -1;
            return false;
        }

        public static bool HitHeader(float px, float py, out int tone, out bool isRow)
        {
            for (int row = 0; row < NoteKeyRun.Rows; row++)
            {
                var (x, y, w, h) = RowHeader(row);
                if (px >= x && px < x + w && py >= y && py < y + h)
                {
                    tone = row;
                    isRow = true;
                    return true;
                }
            }

            for (int col = 0; col < SounderAlphabet.Columns; col++)
            {
                var (x, y, w, h) = ColumnHeader(col);
                if (px >= x && px < x + w && py >= y && py < y + h)
                {
                    tone = col;
                    isRow = false;
                    return true;
                }
            }

            tone = -1;
            isRow = false;
            return false;
        }
    }
}
