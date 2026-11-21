// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Sounders;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What the Resonance Blaster does this frame on the other side.</summary>
    public enum SoundShapeAction
    {
        /// <summary>Nothing.</summary>
        None = 0,

        /// <summary>Send the gadget intent with <c>Alt = false</c>: shape a sound block at the aim.</summary>
        Shape,

        /// <summary>Send the gadget intent with <c>Alt = true</c>: dissolve the sound block aimed at.</summary>
        Dissolve,
    }

    /// <summary>
    /// The Resonance Blaster's two keys on the other side (#3908), Unity-free: the use key shapes — at most one pulse every
    /// <see cref="Interval"/> while it is held, so a wall grows at a building pace — and the mine key dissolves the sound
    /// block under the crosshair, at the same pace while held. The mine key aimed at anything else does nothing (the
    /// server would do nothing either). The server has the last word on every pulse (reach, the cap, whose block it is);
    /// after a refusal it holds the blaster for a second, and so does this trigger (<see cref="Hold"/>).
    /// </summary>
    public sealed class SoundShaper
    {
        /// <summary>Seconds between two pulses: the server's cadence plus a little, so a pulse does not arrive a frame
        /// early and get dropped.</summary>
        public const float Interval = (float)OtherSideRules.ShapeCooldownSeconds + 0.02f;

        /// <summary>Seconds the trigger rests after the server refused a pulse (the server's own hold).</summary>
        public const float RefusalHold = 1f;

        private float _wait;

        /// <summary>Seconds until the next pulse may go out.</summary>
        public float Wait => _wait;

        /// <summary>
        /// One frame. <paramref name="active"/>: the player is over there with the blaster in hand and the hands are free
        /// (no menu, no chat). <paramref name="useHeld"/>: the use key (the one that fires the blaster) is down.
        /// <paramref name="mineHeld"/>: the mine key is down. <paramref name="aimsAtSoundBlock"/>: a sound block is under
        /// the crosshair, in reach. The use key wins when both are down.
        /// </summary>
        public SoundShapeAction Tick(float dt, bool active, bool useHeld, bool mineHeld, bool aimsAtSoundBlock)
        {
            if (_wait > 0f && dt > 0f)
            {
                _wait = Math.Max(0f, _wait - dt);
            }

            if (!active || _wait > 0f)
            {
                return SoundShapeAction.None;
            }

            if (useHeld)
            {
                _wait = Interval;
                return SoundShapeAction.Shape;
            }

            if (mineHeld && aimsAtSoundBlock)
            {
                _wait = Interval;
                return SoundShapeAction.Dissolve;
            }

            return SoundShapeAction.None;
        }

        /// <summary>The server refused a pulse: no further one for <see cref="RefusalHold"/> (it would be dropped).</summary>
        public void Hold() => _wait = Math.Max(_wait, RefusalHold);

        /// <summary>A new session: ready at once.</summary>
        public void Reset() => _wait = 0f;
    }

    /// <summary>Where a pulse of the blaster goes: the point the client sends and the cells it means.</summary>
    public readonly struct SoundAimResult
    {
        public SoundAimResult(bool surface, int hitX, int hitY, int hitZ, int cellX, int cellY, int cellZ, float pointX, float pointY, float pointZ)
        {
            Surface = surface;
            HitX = hitX;
            HitY = hitY;
            HitZ = hitZ;
            CellX = cellX;
            CellY = cellY;
            CellZ = cellZ;
            PointX = pointX;
            PointY = pointY;
            PointZ = pointZ;
        }

        /// <summary>The aim rests on a face in reach (a real block's or a sound block's); false: a point in the open air.</summary>
        public bool Surface { get; }

        /// <summary>The cell behind the face — the one a dissolve means. Only meaningful with <see cref="Surface"/>.</summary>
        public int HitX { get; }

        /// <summary>See <see cref="HitX"/>.</summary>
        public int HitY { get; }

        /// <summary>See <see cref="HitX"/>.</summary>
        public int HitZ { get; }

        /// <summary>The cell a shaping pulse fills: in front of the face, or the open-air cell at the blaster's reach.</summary>
        public int CellX { get; }

        /// <summary>See <see cref="CellX"/>.</summary>
        public int CellY { get; }

        /// <summary>See <see cref="CellX"/>.</summary>
        public int CellZ { get; }

        /// <summary>The aim point to send (the middle of the face, or the point in the air).</summary>
        public float PointX { get; }

        /// <summary>See <see cref="PointX"/>.</summary>
        public float PointY { get; }

        /// <summary>See <see cref="PointX"/>.</summary>
        public float PointZ { get; }
    }

    /// <summary>
    /// The blaster's aim on the other side (#3908): which point to send and which cell the server will take it for, so
    /// the outline shows the very cell that gets the block. It mirrors the server's reading of a pulse
    /// (<c>GameServerSoundLayer.Blaster.cs</c>): the server looks along the ray from the player's EYES to the point, at the
    /// cell 0.05 behind the point (a surface?) and the cell 0.05 in front of it (where the block goes); a point with no
    /// surface behind it is a point in the open air, pulled back to the blaster's reach.
    /// <para>The client aims with the camera (the crosshair) and sends the MIDDLE of the face the crosshair rests on — a
    /// point that names the same two cells from the eyes as from a third-person camera — or, with nothing in reach, the
    /// point at reach along the view.</para>
    /// </summary>
    public static class SoundAim
    {
        /// <summary>The server's eye height over the feet (<c>SightEyeHeight</c>).</summary>
        public const float EyeHeight = 1.5f;

        /// <summary>The blaster's reach when the item data names none (the server's default).</summary>
        public const float DefaultReach = 8f;

        private const float Nudge = 0.05f;

        /// <summary>
        /// Resolves the aim. <paramref name="solid"/> says whether a cell holds a block (real or sound; fluids are no
        /// surface for the client's crosshair). The camera at (<paramref name="camX"/>, …) looks along the unit vector
        /// (<paramref name="dirX"/>, …); the player's feet are at (<paramref name="feetX"/>, …).
        /// </summary>
        public static SoundAimResult Resolve(Func<int, int, int, bool> solid, float camX, float camY, float camZ,
            float dirX, float dirY, float dirZ, float feetX, float feetY, float feetZ, float reach)
        {
            if (reach <= 0f)
            {
                reach = DefaultReach;
            }

            float eyeX = feetX, eyeY = feetY + EyeHeight, eyeZ = feetZ;
            float back = Length(camX - eyeX, camY - eyeY, camZ - eyeZ); // a third-person camera stands behind the eyes
            float span = reach + back;
            float px = camX + (dirX * span), py = camY + (dirY * span), pz = camZ + (dirZ * span);
            if (VoxelRay.Cast(solid, camX, camY, camZ, px, py, pz, out var hit))
            {
                FaceNormal(hit.Face, out int nx, out int ny, out int nz);
                px = hit.X + 0.5f + (nx * 0.5f);
                py = hit.Y + 0.5f + (ny * 0.5f);
                pz = hit.Z + 0.5f + (nz * 0.5f);
            }
            else
            {
                px = camX + (dirX * reach);
                py = camY + (dirY * reach);
                pz = camZ + (dirZ * reach);
            }

            return FromEyes(solid, eyeX, eyeY, eyeZ, px, py, pz, reach);
        }

        /// <summary>The server's reading of the point (<paramref name="px"/>, …) from the eyes.</summary>
        private static SoundAimResult FromEyes(Func<int, int, int, bool> solid, float eyeX, float eyeY, float eyeZ,
            float px, float py, float pz, float reach)
        {
            float dx = px - eyeX, dy = py - eyeY, dz = pz - eyeZ;
            float len = Length(dx, dy, dz);
            float k = len > 1e-3f ? Nudge / len : 0f;
            int ix = Floor(px + (dx * k)), iy = Floor(py + (dy * k)), iz = Floor(pz + (dz * k));
            if (len <= reach + 1f && solid(ix, iy, iz))
            {
                return new SoundAimResult(true, ix, iy, iz, Floor(px - (dx * k)), Floor(py - (dy * k)), Floor(pz - (dz * k)), px, py, pz);
            }

            float pull = len > reach ? reach / len : 1f;
            return new SoundAimResult(false, ix, iy, iz, Floor(eyeX + (dx * pull)), Floor(eyeY + (dy * pull)), Floor(eyeZ + (dz * pull)), px, py, pz);
        }

        private static void FaceNormal(int face, out int x, out int y, out int z)
        {
            x = face == 0 ? 1 : face == 1 ? -1 : 0;
            y = face == 2 ? 1 : face == 3 ? -1 : 0;
            z = face == 4 ? 1 : face == 5 ? -1 : 0;
        }

        private static float Length(float x, float y, float z) => (float)Math.Sqrt((x * x) + (y * y) + (z * z));

        private static int Floor(float v) => (int)Math.Floor(v);
    }

    /// <summary>
    /// The melody bar as the palette (#3908): the tone the player played last on the bar is the tone — and the glow — of
    /// their next sound block. The server keeps that tone per session (0 before any tone was played); this is the
    /// client's mirror of the same rule, so the bar can mark it.
    /// </summary>
    public sealed class SoundPalette
    {
        /// <summary>The tone (0–7) the next sound block gets.</summary>
        public int Tone { get; private set; }

        /// <summary>Moves when the tone changes.</summary>
        public int Version { get; private set; }

        /// <summary>The player played a tone on the melody bar (a tone outside 0–7, the bar's "leave", changes nothing).</summary>
        public void Played(int tone)
        {
            if (tone < 0 || tone >= OtherSideRules.ToneGlow.Length || tone == Tone)
            {
                return;
            }

            Tone = tone;
            Version++;
        }

        /// <summary>A new session: the server starts at tone 0 again.</summary>
        public void Reset()
        {
            if (Tone != 0)
            {
                Tone = 0;
                Version++;
            }
        }

        /// <summary>The glow colour of a tone as three 0..1 channels (<see cref="OtherSideRules.ToneGlow"/>).</summary>
        public static (float R, float G, float B) ColorOf(int tone)
        {
            int rgb = OtherSideRules.GlowOfTone(tone);
            return (((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
        }
    }

    /// <summary>
    /// Walking on sound blocks sounds their tones (#3908): when the cell under the feet becomes another cell and that one
    /// is a sound block, its tone plays — a staircase becomes a melody. A short rest keeps a run from machine-gunning.
    /// Local only: nobody else hears it.
    /// </summary>
    public sealed class SoundStepTones
    {
        /// <summary>Seconds between two step tones at least.</summary>
        public const float Rest = 0.16f;

        private bool _known;
        private int _x, _y, _z;
        private float _rest;

        /// <summary>
        /// One on-foot frame. (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) is the cell under the
        /// feet, <paramref name="grounded"/> whether the player stands on it, <paramref name="glow"/> the cell's glow
        /// colour when it is a sound block (0 otherwise). True with the tone to play when the feet just came onto a
        /// sound block.
        /// </summary>
        public bool Step(float dt, bool grounded, int x, int y, int z, bool soundBlock, int glow, out int tone)
        {
            tone = 0;
            if (_rest > 0f && dt > 0f)
            {
                _rest = Math.Max(0f, _rest - dt);
            }

            if (!grounded)
            {
                _known = false; // a jump lands as a new step, on the same block too
                return false;
            }

            bool moved = !_known || x != _x || y != _y || z != _z;
            _known = true;
            _x = x;
            _y = y;
            _z = z;
            if (!moved || !soundBlock || _rest > 0f)
            {
                return false;
            }

            _rest = Rest;
            tone = OtherSideRules.ToneOfGlow(glow);
            return true;
        }

        /// <summary>Forgets where the feet were (a teleport, a new world).</summary>
        public void Reset()
        {
            _known = false;
            _rest = 0f;
        }
    }
}
