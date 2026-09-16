// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Posts at home (#1865): a trading post (<c>station_vendor</c>) or a mission board placed inside a planet base is
/// staffed by one of the base's residents — barter and board jobs without leaving home (Marcel 2026-09-13). Until
/// now both blocks worked only on a station: on a planet they registered nothing, gave no hint and did nothing.
/// <list type="bullet">
/// <item>A post only counts inside the base (see <see cref="BaseCellInside"/>) and only while a resident holds its
/// job — no settler, no trader. The markers are rebuilt with the residents on every base-life scan.</item>
/// <item>The base board coins its missions like a settlement board (gather, one build job, one survey), keyed by
/// the base's core cell so ids survive a restart (base ids are load order). Accept and turn-in are gated on
/// standing at the board; the board's jobs are listed while the player is within the base's reach.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How close a player stands to a base trading post or board to use it (the settlement reach).</summary>
    private const float BasePostReach = 4f;

    private List<(int BaseId, string Type, Vector3f Pos)> _baseMarkers => _worlds.Active.BaseMarkers;

    /// <summary>The base board's stable key — the core cell, not the load-order base id.</summary>
    private static string BaseBoardKey(ServerBase b) => $"base:{b.Planet}:{b.Cell.X}:{b.Cell.Y}:{b.Cell.Z}";

    /// <summary>The base board's mission id prefix (<c>home_&lt;hash&gt;_</c>).</summary>
    private static string BaseBoardPrefix(ServerBase b) => $"home_{(uint)WorldGenerator.StableHash(BaseBoardKey(b)) % 100000u}_";

    /// <summary>A mission coined by a base board (#1865).</summary>
    private static bool IsBaseMission(string missionId) => missionId.StartsWith("home_", System.StringComparison.Ordinal);

    /// <summary>Rebuilds a base's post markers from its index and its residents' jobs; stocks the board the first time
    /// a quartermaster stands at it. Markers are server-side only, so this never changes what the client sees.</summary>
    private bool RefreshBaseMarkers(ServerBase b, BaseIndex idx, List<ServerNpc> residents)
    {
        _baseMarkers.RemoveAll(m => m.BaseId == b.Id);
        if (residents.Any(n => n.Job == "vendor"))
        {
            foreach (var post in idx.VendorPosts)
            {
                _baseMarkers.Add((b.Id, "vendor", new Vector3f(post.X + 0.5f, post.Y + 0.5f, post.Z + 0.5f)));
            }
        }

        foreach (var profession in NpcProfessions.All)
        {
            if (residents.Any(n => n.Job == profession.Job) && idx.ProfessionPosts.TryGetValue(profession.Job, out var professionPosts))
            {
                foreach (var post in professionPosts)
                {
                    _baseMarkers.Add((b.Id, profession.Marker, new Vector3f(post.X + 0.5f, post.Y + 0.5f, post.Z + 0.5f)));
                }
            }
        }

        if (residents.FirstOrDefault(n => n.Job == "quartermaster") is { } qm)
        {
            foreach (var board in idx.Boards)
            {
                _baseMarkers.Add((b.Id, "mission_board", new Vector3f(board.X + 0.5f, board.Y + 0.5f, board.Z + 0.5f)));
            }

            if (!idx.BoardStocked)
            {
                StockBoard(BaseBoardPrefix(b), BaseBoardKey(b), idx.MissionIds, qm.Name, withBuild: true);
                idx.BoardStocked = true;
            }
        }

        return false;
    }

    /// <summary>The base post of this type within <paramref name="reach"/> of the player, with its base id.</summary>
    private bool NearBaseMarker(PlayerState player, string type, float reach, out int baseId)
    {
        foreach (var (id, markerType, pos) in _baseMarkers)
        {
            if (markerType == type && WrapDistSq(player.Position, pos) <= reach * reach)
            {
                baseId = id;
                return true;
            }
        }

        baseId = 0;
        return false;
    }

    /// <summary>True if the player stands at a staffed trading post of a planet base (#1865) — barter works there.</summary>
    public bool NearBaseVendor(PlayerState player)
    {
        foreach (var (_, markerType, pos) in _baseMarkers)
        {
            // The classic trading post or a trading profession's post (2026-09).
            if (NpcProfessions.IsTradeMarker(markerType) && WrapDistSq(player.Position, pos) <= BasePostReach * BasePostReach)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if the player stands at a staffed mission board of a planet base (#1865).</summary>
    public bool NearBaseMissionBoard(PlayerState player) => NearBaseMarker(player, "mission_board", BasePostReach, out _);

    /// <summary>The base (on the active world) whose reach box holds the player, or null.</summary>
    private ServerBase? BaseAround(PlayerState player)
    {
        var cell = WorldConstants.CanonicalBlock(player.Position.ToBlock(), _world.Circumference);
        foreach (var b in _bases)
        {
            if (b.Planet == _world.LocationId && WithinBaseWallReach(b, cell))
            {
                return b;
            }
        }

        return null;
    }

    /// <summary>Keeps the base board of the base the player stands in stocked (#1865) — the settlement window's rules:
    /// three deliveries, one build job, one survey.</summary>
    private void EnsureBaseWindow(PlayerState player, HashSet<string> currentBoardIds)
    {
        if (BaseAround(player) is not { } b || !_baseIndex.TryGetValue(b.Id, out var idx)
            || !_baseMarkers.Any(m => m.BaseId == b.Id && m.Type == "mission_board"))
        {
            return;
        }

        string prefix = BaseBoardPrefix(b);
        string key = BaseBoardKey(b);
        string giver = ResidentNpcs(b).FirstOrDefault(n => n.Job == "quartermaster")?.Name ?? CoinGiverName(key);
        EnsureBoardWindow(player, prefix, key, idx.MissionIds, giver, currentBoardIds);
        EnsureBoardWindow(player, prefix + "b", key, idx.MissionIds, giver, currentBoardIds, BoardSlotKind.Build, window: 1);
        EnsureBoardWindow(player, prefix + "s", key, idx.MissionIds, giver, currentBoardIds, BoardSlotKind.Scan, window: 1);
    }

    /// <summary>The resident of the player's base holding a job, for the memory of a trade or an accepted mission.</summary>
    private ServerNpc? BaseResidentWithJob(PlayerState player, string job)
        => BaseAround(player) is { } b ? ResidentNpcs(b).FirstOrDefault(n => n.Job == job) : null;

    /// <summary>A trading post or mission board was placed or mined (#1865). On a planet the base around it rescans at
    /// once so the post is staffed (or its keeper goes back to being a settler) without waiting for the round-robin,
    /// and the builder hears what happens to it — the post used to stand there silently doing nothing.</summary>
    private void OnBasePostChanged(PlayerSession? session, Vector3i pos, bool placed)
    {
        if (_world.Planet?.Void == true || IsPlayerStationWorld(_world.LocationId))
        {
            return; // a station registers its posts when it is boarded
        }

        var cell = WorldConstants.CanonicalBlock(pos, _world.Circumference);
        var b = _bases.FirstOrDefault(x => x.Planet == _world.LocationId && WithinBaseWallReach(x, cell));
        if (b is null)
        {
            if (placed && session != null)
            {
                Send(session, new ServerMessage { Text = "@srv.base.post_no_base" });
            }

            return;
        }

        if (_baseIndex.TryGetValue(b.Id, out var idx))
        {
            idx.Dirty = true;
        }

        UpdateBaseResidents(b);
        if (!placed || session is null)
        {
            return;
        }

        var centre = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        string text = !BaseCellInside(b, cell, new Vector3i(cell.X, cell.Y + 1, cell.Z)) ? "@srv.base.post_outside"
            : _baseMarkers.Any(m => m.BaseId == b.Id && WrapDistSq(m.Pos, centre) < 0.01) ? "@srv.base.post_staffed"
            : ResidentNpcs(b).Count == 0 ? "@srv.base.post_no_resident"
            : "@srv.base.post_no_free_resident";
        Send(session, new ServerMessage { Text = text });
    }

    /// <summary>Test seam (#1865): the mission ids a base's board has coined so far.</summary>
    public IReadOnlyCollection<string> BaseBoardMissionIdsForTest(int baseId)
        => _baseIndex.TryGetValue(baseId, out var idx) ? idx.MissionIds.ToList() : new List<string>();

    /// <summary>Test seam (#1865): whether barter is available to the player right now (at a vendor or aboard).</summary>
    public bool MarketAvailableForTest(PlayerState player) => MarketAvailable(player);
}
