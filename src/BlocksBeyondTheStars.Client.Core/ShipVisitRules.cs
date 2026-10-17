// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>What the docking visit offers in the flight view or on a spacewalk this frame (#2724).</summary>
public enum ShipVisitPrompt
{
    /// <summary>Not docked: nothing to say.</summary>
    None,

    /// <summary>Docked, and nothing more to offer: the partner's ship is not in this flight instance, out of reach, or
    /// the server cannot do visits (an older server) — the prompt line just reads "Docked with Lena".</summary>
    Docked,

    /// <summary>Docked, and the partner sits at the helm: "Lena is flying". A visit needs her to step inside first; E
    /// still asks (the server tells her that a friend wants to come over).</summary>
    PartnerFlying,

    /// <summary>Docked, and the partner's ship floats parked — she walks inside it, or is out on a spacewalk — within
    /// reach: "E: go over to Lena's ship".</summary>
    GoOver,
}

/// <summary>The docking-visit picture of one frame (#2724): which prompt applies, who the partner is (id for the intent,
/// name for the text), and how far away the partner's parked ship floats (0 when it is not known).</summary>
public readonly struct ShipVisitView
{
    private readonly string? _partnerId;
    private readonly string? _partnerName;

    public ShipVisitView(ShipVisitPrompt prompt, string partnerId, string partnerName, float distance)
    {
        Prompt = prompt;
        _partnerId = partnerId;
        _partnerName = partnerName;
        Distance = distance;
    }

    public ShipVisitPrompt Prompt { get; }

    /// <summary>The partner's player id — what <c>VisitShipIntent.HostId</c> carries. Empty when not docked.</summary>
    public string PartnerId => _partnerId ?? string.Empty;

    /// <summary>The partner's display name (their pilot name in the instance, else the id). Empty when not docked.</summary>
    public string PartnerName => _partnerName ?? string.Empty;

    /// <summary>Distance from the own ship (or suit) to the partner's parked ship — or to the ship she flies
    /// (<see cref="ShipVisitPrompt.PartnerFlying"/>); 0 when neither is known.</summary>
    public float Distance { get; }

    /// <summary>True for every docked state (the "Docked with Lena" line shows).</summary>
    public bool Docked => Prompt != ShipVisitPrompt.None;
}

/// <summary>
/// The pure rules behind docking and visiting from the flight view and on a spacewalk (#2724) — client presentation only;
/// the server checks range, docking and the host's state again and has the last word.
/// <list type="bullet">
/// <item><b>K</b> docks with the nearest other pilot within <see cref="DockRange"/>, measured to their pose the way the
/// server measures it (the ship at the helm or parked while its pilot walks inside, the suit on a spacewalk). NPC traders
/// ride the same pose list with an <c>npc:</c> id and are never a docking partner.</item>
/// <item>Once docked, the partner's ship can be visited while it floats parked: its pilot walks inside it
/// (<see cref="NetSpacePlayer.Parked"/>) or is out on a spacewalk (#2709's ship pose). A partner at the helm is "flying".</item>
/// <item>A server that cannot do visits (<c>DockStatus.CanVisit</c> false) gets no visit prompt — docking alone still works.</item>
/// </list>
/// </summary>
public static class ShipVisitRules
{
    /// <summary>How close another pilot must be for K to dock — the server's <c>DockRange</c>.</summary>
    public const float DockRange = 48f;

    /// <summary>How close the partner's parked ship must float for E to go over — the same range as docking.</summary>
    public const float VisitRange = 48f;

    /// <summary>True for a pose that can be a docking partner: a real player (not an NPC trader, not an empty id) in the
    /// flight instance — at the helm, on a spacewalk, or walking inside their parked ship (the server measures that pilot
    /// at the parked ship's pose, #2723: "step inside, I'm coming over" is the natural moment to dock).</summary>
    public static bool IsDockCandidate(NetSpacePlayer? pilot)
        => pilot != null && !string.IsNullOrEmpty(pilot.PlayerId) && !SpaceTargeting.IsTraderId(pilot.PlayerId);

    /// <summary>The nearest other pilot within <paramref name="range"/> of (<paramref name="x"/>, <paramref name="y"/>,
    /// <paramref name="z"/>), or null — K's target in flight and on a spacewalk. Measured to the pose (ship or suit).</summary>
    public static NetSpacePlayer? NearestDockPilot(IReadOnlyList<NetSpacePlayer>? players, float x, float y, float z, float range = DockRange)
    {
        if (players == null)
        {
            return null;
        }

        NetSpacePlayer? best = null;
        float bestSq = range * range;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (!IsDockCandidate(p))
            {
                continue;
            }

            float sq = DistanceSquared(p.X, p.Y, p.Z, x, y, z);
            if (sq <= bestSq)
            {
                bestSq = sq;
                best = p;
            }
        }

        return best;
    }

    /// <summary>The pose with id <paramref name="playerId"/> in the instance, or null.</summary>
    public static NetSpacePlayer? Find(IReadOnlyList<NetSpacePlayer>? players, string? playerId)
    {
        if (players == null || string.IsNullOrEmpty(playerId))
        {
            return null;
        }

        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] != null && players[i].PlayerId == playerId)
            {
                return players[i];
            }
        }

        return null;
    }

    /// <summary>True while nobody sits at <paramref name="pilot"/>'s helm: the pilot walks inside the parked ship, or is out
    /// on a spacewalk.</summary>
    public static bool AwayFromHelm(NetSpacePlayer pilot) => pilot.Eva || pilot.Parked;

    /// <summary>Where <paramref name="pilot"/>'s ship floats while nobody is at its helm: the parked pose (the pilot walks
    /// inside), or #2709's ship pose on a spacewalk. False while the pilot flies, and on a spacewalk reported by a server
    /// without the ship pose.</summary>
    public static bool TryParkedShip(NetSpacePlayer pilot, out float x, out float y, out float z)
    {
        if (pilot.Eva && pilot.HasShipPose)
        {
            x = pilot.ShipX;
            y = pilot.ShipY;
            z = pilot.ShipZ;
            return true;
        }

        if (!pilot.Eva && pilot.Parked)
        {
            x = pilot.X;
            y = pilot.Y;
            z = pilot.Z;
            return true;
        }

        x = y = z = 0f;
        return false;
    }

    /// <summary>The name to show for <paramref name="pilot"/>: their pilot name, else the id.</summary>
    public static string DisplayName(NetSpacePlayer? pilot, string? fallbackId)
        => pilot != null && !string.IsNullOrEmpty(pilot.Name) ? pilot.Name : fallbackId ?? string.Empty;

    /// <summary>
    /// The visit prompt for this frame, seen from (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) — the
    /// own ship while flying, the suit on a spacewalk.
    /// </summary>
    /// <param name="canVisit">The server supports visits (<c>DockStatus.CanVisit</c>).</param>
    /// <param name="docked">The authoritative docking state (<c>DockStatus.Docked</c>).</param>
    /// <param name="partnerId">The docking partner's id (<c>DockStatus.Partner</c>).</param>
    /// <param name="players">The other poses of the flight instance (<c>SpaceState.Players</c>).</param>
    public static ShipVisitView Evaluate(bool canVisit, bool docked, string? partnerId, IReadOnlyList<NetSpacePlayer>? players,
        float x, float y, float z, float range = VisitRange)
    {
        if (!docked || string.IsNullOrEmpty(partnerId))
        {
            return default;
        }

        var partner = Find(players, partnerId);
        string name = DisplayName(partner, partnerId);
        if (!canVisit || partner == null)
        {
            return new ShipVisitView(ShipVisitPrompt.Docked, partnerId!, name, 0f);
        }

        if (!AwayFromHelm(partner))
        {
            float shipDist = (float)System.Math.Sqrt(DistanceSquared(partner.X, partner.Y, partner.Z, x, y, z));
            return new ShipVisitView(ShipVisitPrompt.PartnerFlying, partnerId!, name, shipDist);
        }

        if (!TryParkedShip(partner, out float sx, out float sy, out float sz))
        {
            return new ShipVisitView(ShipVisitPrompt.Docked, partnerId!, name, 0f);
        }

        float dist = (float)System.Math.Sqrt(DistanceSquared(sx, sy, sz, x, y, z));
        return new ShipVisitView(dist <= range ? ShipVisitPrompt.GoOver : ShipVisitPrompt.Docked, partnerId!, name, dist);
    }

    private static float DistanceSquared(float ax, float ay, float az, float bx, float by, float bz)
    {
        float dx = ax - bx, dy = ay - by, dz = az - bz;
        return dx * dx + dy * dy + dz * dz;
    }
}
