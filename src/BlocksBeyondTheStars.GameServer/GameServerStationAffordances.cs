using System.Globalization;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Station affordances for the Tab menu (#1070, #1072, #1074).
///
/// The server is the single source of truth for "which station am I at": it publishes
/// <see cref="StationsInReach"/> — the crafting stations usable right now plus the research/ship-build
/// gates — on join and whenever the set changes, and answers <see cref="LocateStationIntent"/> with the
/// nearest block (or ship station) that would satisfy a station, so the menu can point the player there.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How often each player's station set is re-evaluated (seconds). Cheap: a handful of ±3-block
    /// box scans per player; the message itself only goes out when something changed.</summary>
    private const double StationScanInterval = 0.5;

    /// <summary>Sphere radius (blocks) of the "where is the nearest …?" scan. Wide enough to find the
    /// workbench in the next room of a base, small enough to stay a sub-millisecond scan.</summary>
    private const int StationLocateRadius = 24;

    /// <summary>Minimum seconds between two locate requests from one player (the menu asks once per hint).</summary>
    private const double StationLocateCooldown = 0.5;

    /// <summary>The crafting stations the menu can be gated on. Hand needs no station, Market/Factory keep
    /// their own client-side checks (vendor proximity / factory roster) and are not part of this set.</summary>
    private static readonly CraftingStation[] GatedStations =
    {
        CraftingStation.Workshop,
        CraftingStation.Refinery,
        CraftingStation.Detoxifier,
        CraftingStation.Transmuter,
        CraftingStation.AlgaeTank,
        CraftingStation.Campfire,
        CraftingStation.Factory,
        CraftingStation.Decontaminator, // #2067
    };

    /// <summary>The placed world block that stands in for a crafting station off the ship (mirrors
    /// <see cref="StationAvailable(PlayerState, ShipState, CraftingStation)"/>).</summary>
    internal static string? StationBlockFor(CraftingStation station) => station switch
    {
        CraftingStation.Workshop => "workbench",
        CraftingStation.Refinery => "forge",
        CraftingStation.Detoxifier => "detoxifier",
        CraftingStation.Transmuter => "matter_forge",
        CraftingStation.AlgaeTank => "algae_tank",
        CraftingStation.Campfire => "campfire",
        CraftingStation.Factory => "factory_terminal",
        CraftingStation.Decontaminator => "decontaminator", // #2067
        _ => null,
    };

    /// <summary>The ship module that provides a crafting station aboard.</summary>
    internal static string? StationModuleFor(CraftingStation station) => station switch
    {
        CraftingStation.Workshop => "workshop",
        CraftingStation.Refinery => "refinery",
        CraftingStation.Detoxifier => "detoxifier",
        CraftingStation.Transmuter => "transmuter",
        CraftingStation.Decontaminator => "decontaminator", // #2067
        _ => null,
    };

    /// <summary>#2585: the ship module that does the job of a crafting-station BLOCK aboard (forge → refinery, workbench →
    /// workshop, …), or null for a block no module stands in for (a campfire, an algae tank, any other block).</summary>
    internal static string? StationModuleForBlock(string blockKey)
    {
        foreach (var station in GatedStations)
        {
            if (StationBlockFor(station) == blockKey)
            {
                return StationModuleFor(station);
            }
        }

        return null;
    }

    /// <summary>
    /// #2585: why a recipe's station is out of reach, as the token the client localizes. Off the ship it is the station
    /// itself (<c>@need_station:refinery</c> — a placed forge in reach would do). Aboard, only a fitted ship module counts
    /// (<see cref="StationAvailable(PlayerState, ShipState, CraftingStation)"/>; a station block built into a ship is
    /// decoration, #2248), so the token names the module that does the job (<c>@srv.craft.need_module:Refinery</c>) — or,
    /// for a station no module provides (a campfire, an algae tank), says it works on a world only
    /// (<c>@srv.craft.world_only:Campfire</c>). Both names are localized here, for the session's language. A player who
    /// built a forge right beside the cabin's workbench to make titanium plates was told "you need a forge nearby" and
    /// never learned that aboard it is the refinery module. The factory keeps <c>@need_station:factory</c> aboard too:
    /// "next to this block" would point at a terminal block, and a player-placed terminal never produces (#1265) — only a
    /// spawned factory does.
    /// </summary>
    private string StationMissingReason(PlayerSession session, CraftingStation station)
    {
        if (session.State.AboardShip && station != CraftingStation.Factory)
        {
            if (StationModuleFor(station) is { } moduleKey)
            {
                return "@srv.craft.need_module:" + LocalizedName(session.Locale, _content.GetShipModule(moduleKey)?.NameKey, moduleKey);
            }

            if (StationBlockFor(station) is { } blockKey)
            {
                return "@srv.craft.world_only:" + LocalizedName(session.Locale, _content.GetBlock(blockKey)?.NameKey, blockKey);
            }
        }

        return "@need_station:" + station.ToString().ToLowerInvariant();
    }

    /// <summary>#2585: the materials a refused craft lacks, named with what the player has and what the batch needs —
    /// <c>@srv.craft.missing_named:Titanium Ore (0/2), Carbon (1/3)</c>. Counted from the very <see cref="MaterialPool"/>
    /// the check used, so the ship's hold counts exactly when it counts for the craft (aboard) and a changed tool counts
    /// like a plain one. The item names are localized for the session; the client puts the list into its sentence as it
    /// is. "Missing materials." never said which one.</summary>
    private string MissingMaterialsReason(PlayerSession session, MaterialPool pool, IEnumerable<ItemAmount> inputs)
    {
        var missing = new List<string>();
        foreach (var need in inputs)
        {
            int have = pool.Count(need); // #2958: a group ingredient counts every member and is named as the group
            if (have < need.Count)
            {
                string? nameKey = string.IsNullOrEmpty(need.Group) ? _content.GetItem(need.Item)?.NameKey : Shared.Content.GameContent.ItemGroupNameKey(need.Group);
                missing.Add(LocalizedName(session.Locale, nameKey, need.Item)
                    + " (" + have.ToString(CultureInfo.InvariantCulture) + "/" + need.Count.ToString(CultureInfo.InvariantCulture) + ")");
            }
        }

        return missing.Count == 0 ? "@srv.craft.missing_materials" : "@srv.craft.missing_named:" + string.Join(", ", missing);
    }

    /// <summary>The ship of a session without touching the ship cursor.</summary>
    private ShipState ShipOf(PlayerSession session)
        => session.Ships.TryGetValue(session.ActiveShipId, out var ship) ? ship : _noShip;

    /// <summary>Research happens at the cockpit (#1074): aboard the parked ship and within station reach of
    /// its cockpit cell — or at the helm while flying (the pilot IS in the cockpit).</summary>
    private bool ResearchAvailable(PlayerSession session)
    {
        var p = session.State;
        if (InSpace(p.PlayerId))
        {
            return true;
        }

        var landed = _worlds.Active.LandedFor(p.PlayerId);
        if (!landed.Placed)
        {
            // No parked ship on this world (worlds started without a starter ship, or the ship is elsewhere):
            // there is no cockpit to walk to, so the gate would be a dead end — research stays open.
            return true;
        }

        if (!p.AboardShip)
        {
            return false;
        }

        foreach (var s in landed.Stations)
        {
            if (s.Type == "cockpit" && WrapDistSq(p.Position, s.Pos) <= ShipStationReach * ShipStationReach)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Ship modules are built aboard, at the workshop module (mirrors <c>HandleBuildModule</c>).</summary>
    private bool ShipBuildAvailable(PlayerSession session)
        => session.State.AboardShip && ShipOf(session).HasModule("workshop");

    private StationsInReach BuildStationsInReach(PlayerSession session)
    {
        // Free-crafting worlds (Creative) skip every station/material check in HandleCraft/HandleUnlock, so
        // the menu must not dim anything there either.
        bool free = !Rules.CraftingCostsMaterialsFor(session.State.ModeOverride);
        var ship = ShipOf(session);
        var available = new List<string>();
        foreach (var station in GatedStations)
        {
            if (free || StationAvailable(session.State, ship, station))
            {
                available.Add(station.ToString().ToLowerInvariant());
            }
        }

        return new StationsInReach
        {
            Available = available.ToArray(),
            ResearchOk = free || ResearchAvailable(session),
            ShipBuildOk = ShipBuildAvailable(session),
        };
    }

    /// <summary>Publishes the station set to one player unconditionally (join / world change).</summary>
    private void SendStationsInReach(PlayerSession session)
    {
        var msg = BuildStationsInReach(session);
        session.Sync.LastStationsInReach = StationsKey(msg);
        session.Sync.StationScanIn = StationScanInterval;
        Send(session, msg);
    }

    private static string StationsKey(StationsInReach m)
        => string.Join(",", m.Available) + "|" + (m.ResearchOk ? 1 : 0) + (m.ShipBuildOk ? 1 : 0);

    /// <summary>Per-tick: re-evaluate each player's station set on a short cadence and push it only when it
    /// changed, so the Tab menu's gates track the player walking up to (or away from) a bench.</summary>
    private void TickStationsInReach(double dt)
    {
        foreach (var session in JoinedInActiveWorld())
        {
            session.Sync.StationScanIn -= dt;
            if (session.Sync.StationScanIn > 0)
            {
                continue;
            }

            session.Sync.StationScanIn = StationScanInterval;
            var msg = BuildStationsInReach(session);
            string key = StationsKey(msg);
            if (key == session.Sync.LastStationsInReach)
            {
                continue;
            }

            session.Sync.LastStationsInReach = key;
            Send(session, msg);
        }
    }

    /// <summary>Test/diagnostic: the station set the server would publish to a player right now.</summary>
    public StationsInReach StationsInReachForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s ? BuildStationsInReach(s) : new StationsInReach();

    private void HandleLocateStation(PlayerSession session, LocateStationIntent intent)
    {
        double now = _uptime;
        if (now - session.LastStationLocateAt < StationLocateCooldown)
        {
            return; // the menu re-asks on every rebuild; one answer per half second is plenty
        }

        session.LastStationLocateAt = now;
        Send(session, LocateStation(session, intent.Station ?? string.Empty));
    }

    /// <summary>Test/diagnostic: the locate answer for a player and station key.</summary>
    public StationLocation LocateStationForTest(string playerId, string station)
    {
        if (FindSessionByPlayerId(playerId) is not { } s)
        {
            return new StationLocation { Station = station };
        }

        Serve(s);
        return LocateStation(s, station);
    }

    private StationLocation LocateStation(PlayerSession session, string station)
    {
        var p = session.State;
        var result = new StationLocation { Station = station };
        var landed = _worlds.Active.LandedFor(p.PlayerId);

        // Research / ship building live aboard the parked ship: point at its cockpit (research) or its
        // workshop station cell (module building), falling back to the cockpit when a hull has no marker.
        if (station is "research" or "shipbuild")
        {
            if (!landed.Placed)
            {
                return result;
            }

            string want = station == "research" ? "cockpit" : "workshop";
            var cell = ShipStationCell(landed, want) ?? ShipStationCell(landed, "cockpit");
            if (cell is null)
            {
                return result;
            }

            result.Found = true;
            result.Kind = "ship";
            result.BlockKey = want;
            (result.X, result.Y, result.Z) = FloorCell(cell.Value);
            return result;
        }

        if (!Enum.TryParse<CraftingStation>(station, ignoreCase: true, out var cs))
        {
            return result;
        }

        // A world block first (a base bench nearby is the natural answer even when the ship is parked
        // further away) …
        string? blockKey = StationBlockFor(cs);
        if (blockKey is not null && NearestBlock(p.Position, blockKey, StationLocateRadius) is { } hit)
        {
            result.Found = true;
            result.Kind = "block";
            result.BlockKey = blockKey;
            result.X = hit.X;
            result.Y = hit.Y;
            result.Z = hit.Z;
            return result;
        }

        // … then the parked ship, if it carries the module: its matching station marker (workshop) or the
        // cockpit as the "go aboard" anchor.
        string? moduleKey = StationModuleFor(cs);
        if (moduleKey is not null && landed.Placed && ShipOf(session).HasModule(moduleKey))
        {
            var cell = ShipStationCell(landed, moduleKey) ?? ShipStationCell(landed, "cockpit");
            if (cell is not null)
            {
                result.Found = true;
                result.Kind = "ship";
                result.BlockKey = moduleKey;
                (result.X, result.Y, result.Z) = FloorCell(cell.Value);
            }
        }

        return result;
    }

    private static Vector3f? ShipStationCell(LandedShip landed, string type)
    {
        foreach (var s in landed.Stations)
        {
            if (s.Type == type)
            {
                return s.Pos;
            }
        }

        return null;
    }

    private static (int X, int Y, int Z) FloorCell(Vector3f v)
        => ((int)Math.Floor(v.X), (int)Math.Floor(v.Y), (int)Math.Floor(v.Z));

    /// <summary>Nearest cell holding <paramref name="blockKey"/> within a sphere around the player, or null.
    /// Same canonical-cell walk as the ore scanner (torus-wrapped X/Z), a fraction of its radius.</summary>
    private Vector3i? NearestBlock(Vector3f around, string blockKey, int radius)
    {
        if (_content.GetBlock(blockKey) is not { } def || def.NumericId.Value == 0)
        {
            return null;
        }

        ushort id = def.NumericId.Value;
        var centre = WorldConstants.CanonicalBlock(new Vector3i(
            (int)Math.Floor(around.X), (int)Math.Floor(around.Y), (int)Math.Floor(around.Z)), _world.Circumference);

        Vector3i? best = null;
        int bestSq = int.MaxValue;
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    int distSq = dx * dx + dy * dy + dz * dz;
                    if (distSq > radius * radius || distSq >= bestSq)
                    {
                        continue;
                    }

                    var cell = WorldConstants.CanonicalBlock(new Vector3i(centre.X + dx, centre.Y + dy, centre.Z + dz), _world.Circumference);
                    if (_world.GetBlock(cell).Value == id)
                    {
                        best = cell;
                        bestSq = distSq;
                    }
                }

        return best;
    }
}
