// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Runtime.CompilerServices;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Resonance Blaster on the other side (#3908): Sounder technology changes only sound over there. The use key
/// shapes a sound block on the aimed surface (a real block's or a sound block's — or in the open air); the gadget's
/// alternative action (<c>UseGadgetIntent.Alt</c>, the mine key) dissolves the sound block aimed at. Real blocks are
/// never touched, nothing is pushed and no chamber opens. The melody bar is the palette: the tone played last sets the
/// next block's glow
/// (<see cref="OtherSideRules.ToneGlow"/>). Shaping costs no suit energy and has its own quick cadence
/// (<see cref="OtherSideRules.ShapeCooldownSeconds"/>) — the gadget table hands the pulse over before its energy check
/// (<see cref="ShapeSoundWithBlaster"/>). On the real side the blaster is what it always was.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds until the next pulse after a refused one — "one clear line", not five a second while the trigger
    /// is held.</summary>
    internal const double SoundRefusalCooldown = 1.0;

    /// <summary>The tone each session played last on the melody bar — session state: a new session starts at tone 0, and
    /// the entry goes with the session.</summary>
    private readonly ConditionalWeakTable<PlayerSession, StrongBox<int>> _soundTones = new();

    /// <summary>The melody bar played a tone (0–7): it is the palette's pick for the player's next sound block.</summary>
    private void RememberSoundTone(PlayerSession session, int tone)
    {
        if (tone >= 0 && tone < OtherSideRules.ToneGlow.Length)
        {
            _soundTones.GetOrCreateValue(session).Value = tone;
        }
    }

    /// <summary>The tone a player's next sound block gets: the one they played last on the melody bar, 0 before any.</summary>
    internal int SoundToneOf(PlayerSession session) => _soundTones.TryGetValue(session, out var tone) ? tone.Value : 0;

    /// <summary>How far the aim point is nudged along the eye's ray to name the cell behind a face (+) or in front of it (−).</summary>
    private const float SoundAimNudge = 0.05f;

    /// <summary>A pulse of the blaster by a player who is over there — two actions, picked by the intent's <c>Alt</c>:
    /// <list type="bullet">
    /// <item><b>Shape</b> (<paramref name="alt"/> false, the use key): ALWAYS shapes. The aim point lies on a face; the
    /// cell on the NEAR side of it (the point nudged toward the eyes) gets a sound block, if the rules allow — on a real
    /// block's face and on a sound block's face alike, so walls, stairs and bridges grow block by block. With no surface
    /// within reach the point lies in the open: the cell it lies in gets the block (how a bridge leaves the cliff), the
    /// point pulled back to the blaster's reach first.</item>
    /// <item><b>Dissolve</b> (<paramref name="alt"/> true, the mine key on a sound block): the sound block in the cell
    /// BEHIND the face (the point nudged into the surface, as <see cref="AimedCell"/> does) dissolves, if it is the
    /// player's to dissolve. No sound block there: nothing happens, no line, no cadence.</item>
    /// </list>
    /// A refusal is told in one line and holds the blaster for <see cref="SoundRefusalCooldown"/>.</summary>
    private void ShapeSoundWithBlaster(PlayerSession session, Vector3f target, bool alt, string cooldownKey)
    {
        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y) || !float.IsFinite(target.Z))
        {
            return;
        }

        var p = session.State;
        var eye = new Vector3f(p.Position.X, p.Position.Y + SightEyeHeight, p.Position.Z);
        var point = Unwrapped(eye, target);
        var into = SoundRayCell(eye, point, SoundAimNudge);
        string refusal = "out_of_reach";
        bool done;
        if (alt)
        {
            if (!IsSoundBlock(into))
            {
                return; // nothing of sound there: the mine key does nothing over there
            }

            done = WithinReach(p, into) && TryDissolveSound(session, into, out refusal);
        }
        else
        {
            var cell = SoundShapeCell(eye, point, into);
            done = WithinReach(p, cell) && TryShapeSound(session, cell, SoundToneOf(session), out refusal);
        }

        if (!done)
        {
            Reject(session, "gadget", "@" + refusal);
        }

        _gadgetReadyAt[cooldownKey] = _uptime + (done ? OtherSideRules.ShapeCooldownSeconds : SoundRefusalCooldown);
    }

    /// <summary>The cell a shaping pulse fills: in front of the aimed face when the cell behind it is a surface within the
    /// blaster's reach (a real block or a sound block), otherwise the cell the point itself lies in — the point pulled
    /// back along the ray to the blaster's reach, so the world is never read (or generated) farther out than that.</summary>
    private Vector3i SoundShapeCell(Vector3f eye, Vector3f point, Vector3i into)
    {
        float reach = _content.GetItem(SoundersIds.ResonanceBlaster)?.Tool?.Range is { } range && range > 0f ? range : ResonanceDefaultRange;
        float dx = point.X - eye.X, dy = point.Y - eye.Y, dz = point.Z - eye.Z;
        float len = (float)System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        bool surface = len <= reach + 1f
                       && (IsSoundBlock(into) || (WithinBuildHeight(into.Y) && !_world.GetBlock(into).IsAir));
        if (surface)
        {
            return SoundRayCell(eye, point, -SoundAimNudge);
        }

        float k = len > reach ? reach / len : 1f;
        return WorldConstants.CanonicalBlock(CellAt(eye.X + (dx * k), eye.Y + (dy * k), eye.Z + (dz * k)), _world.Circumference);
    }

    /// <summary>The (canonical) cell of the aim point moved <paramref name="along"/> blocks along the eye's ray.</summary>
    private Vector3i SoundRayCell(Vector3f eye, Vector3f point, float along)
    {
        float dx = point.X - eye.X, dy = point.Y - eye.Y, dz = point.Z - eye.Z;
        float len = (float)System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        float k = len > 1e-3f ? along / len : 0f;
        return WorldConstants.CanonicalBlock(CellAt(point.X + (dx * k), point.Y + (dy * k), point.Z + (dz * k)), _world.Circumference);
    }

    private static Vector3i CellAt(float x, float y, float z)
        => new((int)System.Math.Floor(x), (int)System.Math.Floor(y), (int)System.Math.Floor(z));
}
