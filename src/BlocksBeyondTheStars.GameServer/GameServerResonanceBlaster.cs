// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Resonance Blaster (#3353, the Sounders story pack — <i>Musik-Dröhner</i>, never "Sound Blaster"): a held gadget from
/// the hidden blueprint <c>sounder_resonance</c>. Its use (<see cref="UseGadgetIntent"/>, the gadget table in
/// GameServerGadgets.cs) does one of two things:
/// <list type="bullet">
/// <item><b>Resonate</b> — aimed at a rune stone of a monument within reach, it opens that monument's sound chamber, once per
/// monument (<see cref="TryResonate"/>, GameServerSoundChambers.cs).</item>
/// <item><b>Sonic push</b> — anywhere else a cone of sound (<see cref="ResonanceConeCos"/>, the item's range) pushes every
/// HOSTILE creature, planet machine and bandit in it back and dazes it for the tool's <c>staggerSeconds</c> (3 s) — the
/// shock gloves' push and daze (<see cref="ApplyKnockback"/>), with no damage at all: it fends off, it never hurts. Friendly
/// animals, pets, Qualis and giants are left alone, and so is every player. A Soundrak in the cone is also calmed for
/// <see cref="SoundrakCalmSeconds"/> (<see cref="CalmCreature"/>, the creatures' calm).</item>
/// </list>
/// Each pulse sounds <c>blaster_pulse_1</c>/<c>_2</c> in turn. While held, the client shows the melody bar (the sound part).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds between two pulses.</summary>
    internal const double ResonanceBlasterCooldown = 1.2;

    /// <summary>The cone's reach (blocks) when the item data names none.</summary>
    private const float ResonanceDefaultRange = 8f;

    /// <summary>The cone's half-angle as a cosine (about 55°): wide enough that a child hits what is in front of them.</summary>
    internal const float ResonanceConeCos = 0.57f;

    /// <summary>Within this many blocks anything counts as in the cone, whatever the aim (point-blank).</summary>
    private const float ResonancePointBlank = 1.5f;

    /// <summary>How long a Soundrak stays calm after a pulse.</summary>
    internal const double SoundrakCalmSeconds = 30.0;

    private int _blasterPulseCount;

    /// <summary>The blaster's use. False when nothing came of it (a chamber refused): it costs neither energy nor cooldown.</summary>
    private bool UseResonanceBlaster(PlayerSession session, Vector3f target)
    {
        var cell = AimedCell(session.State, target);
        if (cell is { } c && WithinReach(session.State, c))
        {
            var resonance = TryResonate(session, c);
            if (resonance != ResonanceOutcome.NotARuneStone)
            {
                return resonance == ResonanceOutcome.Opened;
            }

            if (OrnasaChamberAtSeal(c) is { } chamber && chamber.Index != SounderSiteLayout.BloomTrailChamber)
            {
                // #3358: Ornasa's two sealed chambers on Soaniri open to the blaster (the third to the bell-bloom trail).
                bool opened = OpenOrnasaChamber(session, chamber.Index, byBlaster: true);
                if (opened)
                {
                    BroadcastToWorld(new SoundFx { SoundId = "chamber_open", X = c.X + 0.5f, Y = c.Y + 0.5f, Z = c.Z + 0.5f });
                }

                return opened;
            }
        }

        SonicPush(session, target);
        return true;
    }

    /// <summary>The block a gadget aim point lies on: the point nudged a little further along the eye's ray, so a point on a
    /// block face names the block behind it (like the rope gun). Null for a point that is not finite.</summary>
    private Vector3i? AimedCell(Shared.State.PlayerState p, Vector3f target)
    {
        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y) || !float.IsFinite(target.Z))
        {
            return null;
        }

        var eye = new Vector3f(p.Position.X, p.Position.Y + SightEyeHeight, p.Position.Z);
        var near = Unwrapped(eye, target);
        float dx = near.X - eye.X, dy = near.Y - eye.Y, dz = near.Z - eye.Z;
        float len = (float)System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        float k = len > 1e-3f ? 0.05f / len : 0f;
        return new Vector3i(
            (int)System.Math.Floor(near.X + (dx * k)),
            (int)System.Math.Floor(near.Y + (dy * k)),
            (int)System.Math.Floor(near.Z + (dz * k)));
    }

    /// <summary>The sonic push: every hostile in the cone from the eyes toward <paramref name="target"/> is pushed back and
    /// dazed, every Soundrak in it calmed. Returns how many were pushed or calmed.</summary>
    internal int SonicPush(PlayerSession session, Vector3f target)
    {
        var p = session.State;
        var tool = _content.GetItem(SoundersIds.ResonanceBlaster)?.Tool ?? new ToolProperties { Knockback = 4f, StaggerSeconds = 3f };
        float range = tool.Range > 0f ? tool.Range : ResonanceDefaultRange;
        var eye = new Vector3f(p.Position.X, p.Position.Y + SightEyeHeight, p.Position.Z);
        var aim = AimDirection(p, eye, target);
        int affected = 0;
        bool creaturesChanged = false, machinesChanged = false;
        foreach (var c in new List<CombatEntity>(_creatures))
        {
            if (!InResonanceCone(eye, aim, c.Position, range))
            {
                continue;
            }

            bool soundrak = _speciesById.TryGetValue(c.SpeciesId, out var sp) && SoundersFauna.IsSoundrak(sp);
            bool calmed = soundrak && CalmCreature(c, SoundrakCalmSeconds);
            bool pushed = sp is not null && !c.IsCompanion && (sp.Hostile || c.ProvokeTimer > 0 || soundrak)
                          && ApplyKnockback(session, c, tool, isCreature: true, aim);
            if (calmed || pushed)
            {
                affected++;
                creaturesChanged = true;
            }
        }

        foreach (var e in _planetEnemies.Concat(_bandits).ToList())
        {
            if (e.Hostile && InResonanceCone(eye, aim, e.Position, range) && ApplyKnockback(session, e, tool, isCreature: false, aim))
            {
                affected++;
                machinesChanged = true;
            }
        }

        if (creaturesChanged)
        {
            BroadcastCreatures();
        }

        if (machinesChanged)
        {
            BroadcastPlanetEnemies(); // the bandits travel in the same list
        }

        string sound = (_blasterPulseCount++ % 2) == 0 ? "blaster_pulse_1" : "blaster_pulse_2";
        BroadcastToWorld(new SoundFx { SoundId = sound, X = eye.X, Y = eye.Y, Z = eye.Z });
        return affected;
    }

    /// <summary>The unit direction from the eyes to the aim point (the short way round a seam); the facing when the point
    /// sits on the eyes.</summary>
    private Vector3f AimDirection(Shared.State.PlayerState p, Vector3f eye, Vector3f target)
    {
        var near = float.IsFinite(target.X) && float.IsFinite(target.Y) && float.IsFinite(target.Z) ? Unwrapped(eye, target) : eye;
        float dx = near.X - eye.X, dy = near.Y - eye.Y, dz = near.Z - eye.Z;
        float len = (float)System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (len < 1e-3f || !float.IsFinite(len))
        {
            double yaw = p.Yaw * System.Math.PI / 180.0; // the facing, level
            return new Vector3f((float)System.Math.Sin(yaw), 0f, (float)System.Math.Cos(yaw));
        }

        return new Vector3f(dx / len, dy / len, dz / len);
    }

    /// <summary>Whether a body at <paramref name="feet"/> (its middle a block above) lies in the cone.</summary>
    private bool InResonanceCone(Vector3f eye, Vector3f aim, Vector3f feet, float range)
    {
        var mid = Unwrapped(eye, new Vector3f(feet.X, feet.Y + 1f, feet.Z));
        float dx = mid.X - eye.X, dy = mid.Y - eye.Y, dz = mid.Z - eye.Z;
        float distSq = (dx * dx) + (dy * dy) + (dz * dz);
        if (distSq > range * range)
        {
            return false;
        }

        float dist = (float)System.Math.Sqrt(distSq);
        return dist <= ResonancePointBlank || ((dx * aim.X) + (dy * aim.Y) + (dz * aim.Z)) / dist >= ResonanceConeCos;
    }
}
