namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Player alliances: two players form a mutual alliance so they co-own each other's space stations + planet
/// bases (build/mine/use/board) and cannot harm one another, even on a PVP server. Alliances are pairwise (not
/// transitive) and server-authoritative. The player ship stays owner-only — alliances grant no ship access.
/// Forming needs the other player's confirmation (a request/accept handshake, like ship docking); dissolving is
/// one-sided. The Funk (radio) chat in the Alliances menu tab reuses the existing global <c>ChatMessage</c> feed.
/// </summary>
public sealed class NetAlliance
{
    /// <summary>The allied player's id.</summary>
    public string PartnerId { get; set; } = string.Empty;

    /// <summary>The allied player's display name (may be shared by others — the id is the unique key).</summary>
    public string PartnerName { get; set; } = string.Empty;

    /// <summary>ISO-8601 UTC timestamp the alliance was formed ("allied since"); empty if unknown.</summary>
    public string FormedUtc { get; set; } = string.Empty;

    /// <summary>True while the partner is currently connected.</summary>
    public bool Online { get; set; }
}

/// <summary>A pending alliance request involving the recipient — either incoming (someone asked me) or outgoing
/// (I asked someone), depending on which list of <see cref="AllianceList"/> it appears in.</summary>
public sealed class NetAllianceRequest
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
}

/// <summary>Full alliance roster for one player (server → client): confirmed allies plus the pending requests in
/// both directions. Sent on join, on opening the Alliances tab, and whenever the player's alliances change.</summary>
public sealed class AllianceList
{
    public NetAlliance[] Allies { get; set; } = System.Array.Empty<NetAlliance>();

    /// <summary>Requests others sent to me, awaiting my accept/decline.</summary>
    public NetAllianceRequest[] Incoming { get; set; } = System.Array.Empty<NetAllianceRequest>();

    /// <summary>Requests I sent that are still awaiting the other player's reply.</summary>
    public NetAllianceRequest[] Outgoing { get; set; } = System.Array.Empty<NetAllianceRequest>();
}

/// <summary>A toast notice that another player just proposed an alliance (server → client), so the recipient can
/// react without having the menu open. The full state still arrives via <see cref="AllianceList"/>.</summary>
public sealed class AllianceRequestNotice
{
    public string RequesterId { get; set; } = string.Empty;
    public string RequesterName { get; set; } = string.Empty;
}

/// <summary>The player asks the server for their current alliance roster (client → server) — sent when the
/// Alliances menu tab is opened.</summary>
public sealed class RequestAllianceListIntent
{
}

/// <summary>The player proposes an alliance to another player (client → server). The target must confirm with an
/// <see cref="AllianceResponseIntent"/> before the alliance is established.</summary>
public sealed class RequestAllianceIntent
{
    public string TargetPlayerId { get; set; } = string.Empty;
}

/// <summary>The recipient of a pending request accepts or declines it (client → server). Accepting establishes
/// the mutual alliance; declining just drops the request and notifies the requester.</summary>
public sealed class AllianceResponseIntent
{
    public string RequesterId { get; set; } = string.Empty;
    public bool Accept { get; set; }
}

/// <summary>The player dissolves an existing alliance with a partner (client → server). One-sided: either side
/// may end it; both are notified and lose the shared access immediately.</summary>
public sealed class DissolveAllianceIntent
{
    public string PartnerId { get; set; } = string.Empty;
}
