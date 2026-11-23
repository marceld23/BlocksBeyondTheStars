// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Which way a station's decor form faces (<see cref="StationDecorForms"/>: its front, keys and screen, is
    /// +Z in the form's own frame). The form is turned by whole QUARTERS about the vertical through the cell's centre:
    /// quarter q turns +Z the way Unity's <c>Quaternion.Euler(0, 90·q, 0)</c> does — 0 → +Z, 1 → +X, 2 → −Z, 3 → −X.
    /// <list type="bullet">
    /// <item>The cockpit faces the pilot, who stands on the interior side and looks forward over the console: against the
    /// ship's nose (<see cref="TransitionCockpit.Nose"/>, from the engines to the helm).</item>
    /// <item>A terminal faces a side where someone can stand — the neighbouring cell and the one above it passable, a floor
    /// under them — and among several such sides the one pointing most toward the ship's interior. Without a side with a
    /// floor, a side that is merely passable; without any, the interior itself.</item>
    /// </list>
    /// Pure and deterministic; the client decides once per decor build and only reads blocks through the callback.
    /// </summary>
    public static class StationDecorFacing
    {
        /// <summary>The direction quarter <paramref name="quarter"/> turns the form's front (+Z) to, as a cell step.</summary>
        public static (int X, int Z) Side(int quarter) => Turn(0, 1, quarter);

        /// <summary>Turns the step (<paramref name="dx"/>, <paramref name="dz"/>) by <paramref name="quarters"/> quarter
        /// turns the way Unity's yaw does (seen from above: +Z → +X → −Z → −X).</summary>
        public static (int X, int Z) Turn(int dx, int dz, int quarters)
        {
            int q = ((quarters % 4) + 4) % 4;
            for (int i = 0; i < q; i++)
            {
                int x = dx;
                dx = dz;
                dz = -x;
            }

            return (dx, dz);
        }

        /// <summary>The quarter whose side is nearest to the direction (<paramref name="dx"/>, <paramref name="dz"/>); the
        /// Z axis wins an exact diagonal, and no direction at all is quarter 0.</summary>
        public static int QuarterOf(float dx, float dz)
        {
            if (Math.Abs(dx) < 1e-5f && Math.Abs(dz) < 1e-5f)
            {
                return 0;
            }

            if (Math.Abs(dx) > Math.Abs(dz))
            {
                return dx > 0f ? 1 : 3;
            }

            return dz >= 0f ? 0 : 2;
        }

        /// <summary>The cockpit's quarter: its front against the ship's nose (<paramref name="noseX"/>,
        /// <paramref name="noseZ"/>), toward the pilot. No nose is read as the stock ships' +Z.</summary>
        public static int CockpitFront(float noseX, float noseZ)
        {
            if (Math.Abs(noseX) < 1e-5f && Math.Abs(noseZ) < 1e-5f)
            {
                noseZ = 1f;
            }

            return QuarterOf(-noseX, -noseZ);
        }

        /// <summary>Among the sides marked in <paramref name="open"/> (indexed by quarter), the one pointing most toward
        /// the interior (<paramref name="interiorX"/>, <paramref name="interiorZ"/>); an exact tie goes to the lower
        /// quarter. No open side: the interior's own quarter.</summary>
        public static int Choose(bool[] open, float interiorX, float interiorZ)
        {
            if (open == null || open.Length < 4)
            {
                throw new ArgumentException("four sides expected", nameof(open));
            }

            float len = (float)Math.Sqrt((interiorX * interiorX) + (interiorZ * interiorZ));
            float ix = len > 1e-5f ? interiorX / len : 0f, iz = len > 1e-5f ? interiorZ / len : 0f;
            int best = -1;
            float bestDot = float.MinValue;
            for (int q = 0; q < 4; q++)
            {
                if (!open[q])
                {
                    continue;
                }

                var side = Side(q);
                float dot = (side.X * ix) + (side.Z * iz);
                if (dot > bestDot + 1e-4f)
                {
                    best = q;
                    bestDot = dot;
                }
            }

            return best >= 0 ? best : QuarterOf(interiorX, interiorZ);
        }

        /// <summary>
        /// The terminal's quarter for its marker block at cell (<paramref name="x"/>, <paramref name="y"/>,
        /// <paramref name="z"/>): a side is standable when the neighbouring cell at the marker's height and the one above it
        /// (the decor's height) are <paramref name="passable"/> and the cell under the neighbour is not (a floor). Without a
        /// standable side, a side passable at both heights counts; then <see cref="Choose"/> picks toward the interior.
        /// </summary>
        public static int TerminalFront(Func<int, int, int, bool> passable, int x, int y, int z, float interiorX, float interiorZ)
        {
            if (passable == null)
            {
                throw new ArgumentNullException(nameof(passable));
            }

            var standable = new bool[4];
            var free = new bool[4];
            bool anyStandable = false;
            for (int q = 0; q < 4; q++)
            {
                var side = Side(q);
                int nx = x + side.X, nz = z + side.Z;
                free[q] = passable(nx, y, nz) && passable(nx, y + 1, nz);
                standable[q] = free[q] && !passable(nx, y - 1, nz);
                anyStandable |= standable[q];
            }

            return Choose(anyStandable ? standable : free, interiorX, interiorZ);
        }
    }
}
