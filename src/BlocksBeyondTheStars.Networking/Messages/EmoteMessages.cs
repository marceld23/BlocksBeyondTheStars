// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client → server: the player plays an emote (<see cref="PlayerEmotes"/>). A fixed list, no text — the
/// server checks the id and the pace and tells the players nearby (<see cref="PlayerEmote"/>). An older server drops
/// the unknown tag: the emote then shows on the sender's own figure only.</summary>
public sealed class PlayerEmoteIntent
{
    /// <summary>One of <see cref="PlayerEmotes"/> (a player's own emotes only — never a body state).</summary>
    public byte Emote { get; set; }
}

/// <summary>Server → client: another player's figure plays an emote or shows a body state
/// (<see cref="PlayerEmotes"/>): a wave, a dance — or a flinch when it is hurt, the fall when it is defeated and the
/// rise when it is back. Sent to the players close enough to see it. An older client drops the unknown tag.</summary>
public sealed class PlayerEmote
{
    /// <summary>Whose figure.</summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>One of <see cref="PlayerEmotes"/>.</summary>
    public byte Emote { get; set; }

    /// <summary>#3767: the figure's voice (<c>PlayerVoice.Pack</c>) with a <see cref="PlayerEmotes.Hurt"/> and a
    /// <see cref="PlayerEmotes.Down"/> — the pain grunt and the fall are heard in it; 0 = the middle voice, and what an older
    /// server sends. Additive contractless field.</summary>
    public int Voice { get; set; }

    /// <summary>A <see cref="PlayerEmotes.Hurt"/> that is seen but not heard: the damage ticks (thin air, cold, fire) and a
    /// grunt with every flinch would never stop. False for a real blow — and what an older server sends, whose flinches
    /// are all heard. Additive contractless field.</summary>
    public bool Quiet { get; set; }
}

/// <summary>The emotes a player can play, and the body states the server reports through the same message.</summary>
public static class PlayerEmotes
{
    /// <summary>Nothing.</summary>
    public const byte None = 0;

    /// <summary>A wave with the free hand. A villager who sees it waves back.</summary>
    public const byte Wave = 1;

    /// <summary>A nod: yes.</summary>
    public const byte Nod = 2;

    /// <summary>Both arms up: hooray.</summary>
    public const byte Cheer = 3;

    /// <summary>The arm out straight: look there.</summary>
    public const byte Point = 4;

    /// <summary>The head from side to side: no.</summary>
    public const byte HeadShake = 5;

    /// <summary>A little dance.</summary>
    public const byte Dance = 6;

    /// <summary>Clapping.</summary>
    public const byte Clap = 7;

    /// <summary>Sits down where it stands; the next step or emote gets it up again — and so does <see cref="Stand"/>,
    /// which the wheel sends when its Sit is picked by a figure that already sits.</summary>
    public const byte Sit = 8;

    /// <summary>A bow.</summary>
    public const byte Bow = 9;

    /// <summary>The highest id a player may send.</summary>
    public const byte LastPlayable = Bow;

    /// <summary>Body state (server only): the figure was hurt — a short flinch.</summary>
    public const byte Hurt = 100;

    /// <summary>Body state (server only): the figure was defeated and sinks down.</summary>
    public const byte Down = 101;

    /// <summary>Body state (server only): the figure is back on its feet.</summary>
    public const byte Up = 102;

    /// <summary>Gets up from sitting. Its own id, because <see cref="Sit"/> as a switch could arrive inverted: a player
    /// who came near later, or one press the pace dropped, and the figure sat for the others when it stood. A player
    /// may send it (<see cref="MaySend"/>); an older client does not know the id and shows nothing.</summary>
    public const byte Stand = 103;

    /// <summary>Whether <paramref name="emote"/> is one of the wheel's emotes.</summary>
    public static bool IsPlayable(byte emote) => emote >= Wave && emote <= LastPlayable;

    /// <summary>Whether a player may send <paramref name="emote"/> itself: the wheel's emotes and the rise from sitting.</summary>
    public static bool MaySend(byte emote) => IsPlayable(emote) || emote == Stand;
}
