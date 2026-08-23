// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Crews (#1216): a named group of up to 8 players layered on the pairwise alliance primitive — membership
/// implies being allied with every other member, so a family or class of six confirms one invite each instead
/// of fifteen pairs. Crew-derived alliance edges live in their own set on the server: dissolving a manual
/// pairwise alliance never cuts crew access, and leaving the crew never cuts a manual alliance.
/// Roles are minimal by design: the owner invites / kicks / renames / disbands; anyone may leave; the owner
/// leaving hands the crew to its oldest member. Invites go only to ONLINE players and there are no join codes
/// (kid safety: nobody can be pulled into a group by a string pasted from outside the game).
/// A player is in at most ONE crew. The crew name is screened like any player-typed name.
/// </summary>
public sealed class NetCrewMember
{
    /// <summary>The member's player id (== display name in this game).</summary>
    public string PlayerId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Online { get; set; }

    /// <summary>True for the crew owner (the only role besides plain member).</summary>
    public bool IsOwner { get; set; }
}

/// <summary>A pending crew invite shown to its target (accept / decline in the Crew view).</summary>
public sealed class NetCrewInvite
{
    public string CrewId { get; set; } = string.Empty;
    public string CrewName { get; set; } = string.Empty;

    /// <summary>Display name of the owner who sent the invite.</summary>
    public string FromName { get; set; } = string.Empty;
}

/// <summary>One player's full crew state (server → client): the crew they are in (if any) with its member
/// roster, plus any invites awaiting their answer. Sent on join, on opening the Crew view, and whenever the
/// player's crew or invites change. An empty <see cref="CrewId"/> means "not in a crew".</summary>
public sealed class CrewList
{
    public string CrewId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public NetCrewMember[] Members { get; set; } = System.Array.Empty<NetCrewMember>();

    /// <summary>Invites addressed to THIS player, awaiting accept / decline.</summary>
    public NetCrewInvite[] Invites { get; set; } = System.Array.Empty<NetCrewInvite>();
}

/// <summary>A toast that a crew owner just invited the recipient (server → client), so they can react without
/// the menu open. The full state still arrives via <see cref="CrewList"/>.</summary>
public sealed class CrewInviteNotice
{
    public string CrewId { get; set; } = string.Empty;
    public string CrewName { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
}

/// <summary>
/// Every crew verb in one envelope (client → server) so the whole feature costs a single NetCodec tag.
/// <see cref="Kind"/> picks the verb; unused fields stay empty. Verbs: <c>create</c> (Name), <c>invite</c>
/// (TargetPlayerId; owner only, target online + crewless), <c>accept</c> / <c>decline</c> (CrewId of the
/// invite), <c>leave</c>, <c>kick</c> (TargetPlayerId; owner only), <c>rename</c> (Name; owner only),
/// <c>disband</c> (owner only), <c>list</c> (re-request the roster — the one verb allowed while paused).
/// </summary>
public sealed class CrewActionIntent
{
    public string Kind { get; set; } = string.Empty;

    /// <summary>Crew name for <c>create</c>/<c>rename</c>; the invite's crew id for <c>accept</c>/<c>decline</c>.</summary>
    public string Name { get; set; } = string.Empty;

    public string TargetPlayerId { get; set; } = string.Empty;
}
