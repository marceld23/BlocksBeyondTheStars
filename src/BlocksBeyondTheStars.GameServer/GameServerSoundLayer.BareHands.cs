// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Runtime.CompilerServices;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Bare hands on sound (#3914). Nobody may shape a sound block within <see cref="OtherSideRules.BodyMargin"/> cells of a
/// stranger, and anybody may dissolve the one that touches their own body — but only with the Resonance Blaster. A player
/// over there WITHOUT one could still be walled in by a ring one cell further out, and the way back is a rune gate,
/// nothing else. So the mine key works on sound with bare hands (anything in hand but the blaster), slowly:
/// <list type="bullet">
/// <item>The gate (<see cref="OtherSideAllowsMore"/>) lets a <see cref="MineBlockIntent"/> through over there only when it
/// hits a sound block, and <c>HandleMine</c> sends every mine intent of a player over there here — never to the real
/// world, whose cell is air under the sound block.</item>
/// <item>Only a sound block in the player's own body box gives way — whoever shaped it, as with the blaster
/// (<see cref="TryDissolveSound"/>); a block farther out is left alone without a word (the blaster reaches farther).</item>
/// <item>The server keeps the time: hits on the same block no more than <see cref="OtherSideRules.BareHandHitGapSeconds"/>
/// apart carry the work on, and after <see cref="OtherSideRules.BareHandDissolveSeconds"/> of it the block dissolves; a
/// pause, another block or a step away starts over. Each hit answers with a <see cref="MiningProgress"/>, so the client's
/// crack overlay shows how far it is — the same as mining.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The block a player over there is working on with bare hands.</summary>
    private sealed class BareHandWork
    {
        public bool Working;
        public Vector3i Cell;
        public double StartedAt;
        public double LastHitAt;
    }

    /// <summary>Session state: the entry goes with the session.</summary>
    private readonly ConditionalWeakTable<PlayerSession, BareHandWork> _bareHands = new();

    /// <summary>Over there the mine key hits a sound block: let it through — <c>HandleMine</c> hands it to
    /// <see cref="BareHandsOnSound"/>.</summary>
    partial void OtherSideAllowsMore(PlayerSession s, object message, ref bool allowed)
    {
        if (message is MineBlockIntent mine && IsSoundBlock(new Vector3i(mine.X, mine.Y, mine.Z)))
        {
            allowed = true;
        }
    }

    /// <summary>One hit of bare hands by a player who is over there (every mine intent of theirs, from <c>HandleMine</c>).
    /// Nothing of the real world is touched.</summary>
    private void BareHandsOnSound(PlayerSession session, MineBlockIntent mine)
    {
        var cell = WorldConstants.CanonicalBlock(new Vector3i(mine.X, mine.Y, mine.Z), _world.Circumference);
        var work = _bareHands.GetOrCreateValue(session);
        if (!IsSoundBlock(cell) || !WithinReach(session.State, cell) || !InSoundBodyBox(session, cell, SoundBodyMargin))
        {
            work.Working = false;
            return; // nothing of sound there, or not touching the player: bare hands do nothing
        }

        bool goesOn = work.Working && work.Cell == cell && _uptime - work.LastHitAt <= OtherSideRules.BareHandHitGapSeconds;
        if (!goesOn)
        {
            work.Working = true;
            work.Cell = cell;
            work.StartedAt = _uptime;
        }

        work.LastHitAt = _uptime;
        double done = (_uptime - work.StartedAt) / OtherSideRules.BareHandDissolveSeconds;
        if (done < 1.0)
        {
            Send(session, new MiningProgress { X = cell.X, Y = cell.Y, Z = cell.Z, Fraction = (float)done });
            return;
        }

        work.Working = false;
        if (!TryDissolveSound(session, cell, out string refusal))
        {
            Reject(session, "mine", "@" + refusal);
        }
    }
}
