// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3087 — the mission board in the player's OWN ship works (Marcel 2026-11-01: Justus built one into his ship, and it
/// did nothing). It needs no quartermaster: the board is the ship's cargo terminal and relays the endless delivery jobs
/// of the region — the board rolls every settlement board uses, under texts of their own: the radio orders' stories
/// (#3180, place <c>ship</c> in <c>data/board_stories.json</c>; <c>mission.ship.gather.*</c> without one).
/// <list type="bullet">
/// <item>Jobs are taken and handed in while the player stands within <see cref="InteractionReach.MissionBoard"/> of a board
/// cell of the own parked ship — landed on a body and in the walkable interior in space alike (both are the parked ship of
/// the active world); never while flying or visiting a friend's ship (the board there is the host's). They are listed while
/// the player is aboard (or at the board).</item>
/// <item>The board belongs to the player, not to a world or one ship: its jobs are keyed by the player id
/// (<c>shipboard_&lt;hash&gt;_&lt;slot&gt;</c>), travel with the player from body to body and across a ship switch, and the
/// held ones are coined back on every world entry (<see cref="RebuildHeldShipBoardJobs"/>). Deliveries only — no build or
/// survey job, whose templates depend on the world the ship happens to stand on.</item>
/// <item>No person hands them out, so nobody remembers the job and nobody radios about the next one.</item>
/// <item>The stand-alone board jobs and the chain steps offered at "any" board are taken here as well (#3090,
/// <see cref="BoardPlaceKey"/>).</item>
/// </list>
/// A trading post or a profession's post built into a ship stays decoration — the builder is told so
/// (<see cref="NoteShipPost"/>).
/// </summary>
public sealed partial class GameServer
{
    private const string ShipBoardIdPrefix = "shipboard_";

    /// <summary>The ship board jobs coined so far (the board window's id set).</summary>
    private readonly HashSet<string> _shipBoardMissionIds = new(System.StringComparer.Ordinal);

    /// <summary>The ship board's roll key — one board per player, whichever ship carries it.</summary>
    private static string ShipBoardKey(string playerId) => "ship:" + playerId;

    /// <summary>The ship board's place key (<c>shipboard_&lt;hash&gt;</c>) — its jobs' id prefix without the trailing
    /// underscore, and where a chain step taken at it is bound (#3090).</summary>
    private static string ShipBoardPlace(string playerId)
        => ShipBoardIdPrefix + ((uint)WorldGenerator.StableHash(ShipBoardKey(playerId))).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string ShipBoardPrefix(string playerId) => ShipBoardPlace(playerId) + "_";

    /// <summary>A job coined by a ship board (#3087).</summary>
    internal static bool IsShipBoardMission(string missionId) => missionId.StartsWith(ShipBoardIdPrefix, System.StringComparison.Ordinal);

    /// <summary>The place key of a ship board (#3090).</summary>
    private static bool IsShipBoardPlace(string placeKey) => placeKey.StartsWith(ShipBoardIdPrefix, System.StringComparison.Ordinal);

    /// <summary>The player's own ship parked on the active world and the mission board's block id — false while the
    /// player flies, visits a friend's ship, has no ship here or the content has no board.</summary>
    private bool OwnParkedShipWithBoards(PlayerState p, out LandedShip rec, out BlockId board)
    {
        rec = null!;
        board = BlockId.Air;
        if (InSpace(p.PlayerId) || IsVisitingShip(p.PlayerId) || _content.GetBlock(InteractionReach.MissionBoardBlock) is not { } def)
        {
            return false;
        }

        rec = _worlds.Active.LandedFor(p.PlayerId);
        board = def.NumericId;
        return rec.Placed;
    }

    /// <summary>Whether a board cell of the player's own parked ship lies within <paramref name="reach"/> of the player;
    /// a negative reach asks whether the ship carries a board at all.</summary>
    private bool OwnShipBoardWithin(PlayerState p, float reach)
    {
        if (!OwnParkedShipWithBoards(p, out var rec, out var board))
        {
            return false;
        }

        foreach (var (cell, block) in rec.Structure.Cells)
        {
            if (block != board)
            {
                continue;
            }

            if (reach < 0f)
            {
                return true;
            }

            var centre = new Vector3f(rec.Origin.X + cell.X + 0.5f, rec.Origin.Y + cell.Y + 0.5f, rec.Origin.Z + cell.Z + 0.5f);
            if (WrapDistSq(p.Position, centre) <= reach * reach)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if the player stands at a mission board of their own parked ship (#3087) — its jobs are taken and
    /// handed in there.</summary>
    public bool NearShipMissionBoard(PlayerState player) => OwnShipBoardWithin(player, InteractionReach.MissionBoard);

    /// <summary>Whether the ship board's jobs are listed for the player: at the board, or anywhere aboard a ship that
    /// carries one.</summary>
    private bool ShipBoardListed(PlayerState p)
        => NearShipMissionBoard(p)
           || (OwnParkedShipWithBoards(p, out var rec, out _) && WithinShipExtents(rec, p.Position, 0.4f) && OwnShipBoardWithin(p, -1f));

    /// <summary>Keeps the own ship's board stocked while it is listed (#3087): three deliveries, like every board's
    /// window.</summary>
    private void EnsureShipBoardWindow(PlayerState player, HashSet<string> currentBoardIds)
    {
        if (ShipBoardListed(player))
        {
            EnsureBoardWindow(player, ShipBoardPrefix(player.PlayerId), ShipBoardKey(player.PlayerId), _shipBoardMissionIds,
                string.Empty, currentBoardIds, BoardSlotKind.ShipCargo);
        }
    }

    /// <summary>On world entry: the ship board jobs the player holds get their definitions back, wherever they stand — a
    /// board job is never stored, and the ship's board goes wherever the player goes.</summary>
    private void RebuildHeldShipBoardJobs(PlayerState p)
    {
        if (p.Missions.Any(m => m.Status != MissionStatus.TurnedIn && IsShipBoardMission(m.MissionId)))
        {
            var scratch = new HashSet<string>(System.StringComparer.Ordinal); // nothing is offered from here
            EnsureBoardWindow(p, ShipBoardPrefix(p.PlayerId), ShipBoardKey(p.PlayerId), _shipBoardMissionIds,
                string.Empty, scratch, BoardSlotKind.ShipCargo, window: 0);
        }
    }

    // ---------------- #3090: the place a board job is taken at ----------------

    /// <summary>#3090: where a board job (a chain's board step, a stand-alone board job) is taken: the settlement or the
    /// station the player is in (<see cref="CurrentPlaceKey"/>), else the staffed base board or the own ship's board the
    /// player stands at — so the jobs offered at "any" board (supply_cables, homestead_guard) are offered there too.
    /// Empty in the wild.</summary>
    private string BoardPlaceKey(PlayerState p)
    {
        string place = CurrentPlaceKey(p);
        if (place.Length > 0)
        {
            return place;
        }

        if (NearBaseMarker(p, InteractionReach.MissionBoardBlock, BasePostReach, out int baseId)
            && _bases.FirstOrDefault(b => b.Id == baseId) is { } home)
        {
            return BaseBoardPlace(home);
        }

        return NearShipMissionBoard(p) ? ShipBoardPlace(p.PlayerId) : string.Empty;
    }

    /// <summary>The place a mission is taken at: <see cref="BoardPlaceKey"/> for a job taken at a board (a chain's board
    /// step, a stand-alone board job), <see cref="CurrentPlaceKey"/> for everything else — a dialogue or radio step keeps
    /// the place rules it always had.</summary>
    private string PlaceKeyFor(PlayerState p, MissionDefinition def) => TakenAtBoard(def) ? BoardPlaceKey(p) : CurrentPlaceKey(p);

    /// <summary>A chain step whose surface is the board, or a stand-alone board job.</summary>
    private static bool TakenAtBoard(MissionDefinition def)
        => IsStandAloneBoardJob(def) || (IsChainMission(def) && MissionChains.SurfaceOf(def) == MissionChains.SurfaceBoard);

    /// <summary>#3090: the giver of a chain step taken at a home board — a base board's quartermaster resident remembers it,
    /// a ship board has nobody to. False for any other place (the caller records the place's giver as before).</summary>
    private bool ChainStepTakenAtHomeBoard(PlayerSession session, MissionDefinition def, string place)
    {
        if (IsShipBoardPlace(place))
        {
            return true;
        }

        if (!IsBaseMission(place)) // a base board's place key carries its jobs' "home_" prefix
        {
            return false;
        }

        if (!MissionChains.IsVegaGiver(def) && BaseResidentWithJob(session.State, "quartermaster") is { } qm)
        {
            RecordNpcInteraction(session.State, BaseResidentKeyOf(qm), qm.Name, "quartermaster", NpcInteractionKind.MissionAccepted, qm.Settlement);
            SendNpcStandings(session);
        }

        return true;
    }

    // ---------------- posts built into a ship ----------------

    /// <summary>#3087: tells the builder what a post block built into a ship does there — a mission board works (E at it
    /// opens the jobs), a trading post or a profession's post stays decoration (it is staffed only in a base). Said on every
    /// placement, like the base posts' own lines.</summary>
    private void NoteShipPost(PlayerSession session, BlockDefinition def)
    {
        if (def.Key == InteractionReach.MissionBoardBlock)
        {
            Send(session, new ServerMessage { Text = "@srv.ship.board_ready" });
        }
        else if (def.Key == "station_vendor" || NpcProfessions.ByPostBlock(def.Key) != null)
        {
            Send(session, new ServerMessage { Text = "@srv.ship.post_decor" });
        }
    }

    // ---------------- opening the Missions tab ----------------

    /// <summary>The Missions tab asks for its list (Tab, or E at a board). #3087: a player standing at a mission board of
    /// their base that nobody staffs hears why it offers no jobs — it used to open an empty list without a word.</summary>
    private void HandleRequestMissions(PlayerSession session)
    {
        SendMissionList(session);
        if (!AtAnyMissionBoard(session.State) && BaseAround(session.State) is { } b && BoardBlockNear(session.State) is { } cell)
        {
            Send(session, new ServerMessage { Text = BasePostStateText(b, cell) });
        }
    }

    /// <summary>A mission board block within <see cref="InteractionReach.MissionBoard"/> of the player in the active world,
    /// or null.</summary>
    private Vector3i? BoardBlockNear(PlayerState p)
    {
        if (_content.GetBlock(InteractionReach.MissionBoardBlock) is not { } def)
        {
            return null;
        }

        int r = (int)System.Math.Ceiling(InteractionReach.MissionBoard);
        var feet = p.Position.ToBlock();
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    var cell = new Vector3i(feet.X + dx, feet.Y + dy, feet.Z + dz);
                    if (_world.GetBlock(cell) == def.NumericId
                        && WrapDistSq(p.Position, new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f))
                        <= InteractionReach.MissionBoard * InteractionReach.MissionBoard)
                    {
                        return WorldConstants.CanonicalBlock(cell, _world.Circumference);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Test seam (#3087): the ship board jobs on offer to the player right now (empty when the board is not
    /// listed for them).</summary>
    public IReadOnlyList<string> ShipBoardMissionsForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return System.Array.Empty<string>();
        }

        var ids = new HashSet<string>(System.StringComparer.Ordinal);
        EnsureShipBoardWindow(session.State, ids);
        return ids.Where(id => session.State.Missions.All(m => m.MissionId != id)).OrderBy(id => id, System.StringComparer.Ordinal).ToList();
    }

    /// <summary>Test seam (#3090): the mission ids the player's mission list offers right now (board scoping and the chain
    /// rule applied, as <see cref="SendMissionList"/> sends them).</summary>
    public IReadOnlyList<string> OfferedMissionIdsForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return System.Array.Empty<string>();
        }

        var ids = new HashSet<string>(System.StringComparer.Ordinal);
        EnsureSettlementWindow(session.State, ids);
        EnsureStationWindow(session.State, ids);
        EnsureBaseWindow(session.State, ids);
        EnsureShipBoardWindow(session.State, ids);
        return AvailableMissionsFor(session.State, ids).Select(d => d.Id).ToList();
    }
}
