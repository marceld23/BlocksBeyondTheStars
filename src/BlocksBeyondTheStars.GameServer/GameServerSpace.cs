using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Fixed, pre-planned landing pads (item 38). Each body has a deterministic set of landing pads — a
/// seeded-random count within its size-class range (asteroids fewest, moons more, planets most) scattered
/// across BOTH longitude and latitude, nudged onto dry land. Pads are <b>communal</b>: occupancy is
/// <b>live</b> — a pad counts as taken
/// only while a player is standing on the body (not flown off to space), so it frees the moment they leave.
/// Landing lets the player pick a free pad; a body whose pads are all occupied is <b>full</b> and refuses
/// landing. No one may build on a pad (the pad is reserved); the ship is PLACED on the player's pad as a
/// structure object (ship-as-object) and the pad terrain is levelled by worldgen.
/// </summary>
public sealed partial class GameServer
{
    private const int LandingPadRadius = 8;       // one generous size — clears the largest ship (hauler 7×9)
    private const int PadClearanceHeight = 16;    // reserve only the landing volume above the pad, not the whole sky

    /// <summary>A fixed landing pad on a body. Deterministic from the body seed; no owner (communal).</summary>
    internal sealed class LandingPad
    {
        public int Index;
        public int CenterX;
        public int CenterZ;
        public int CenterY;                       // ground height at the pad (the reserved volume sits just above it)
        public int Radius = LandingPadRadius;
    }

    private List<LandingPad> _landingPads => _worlds.Active.LandingPads;

    // --- deterministic pad set ---

    /// <summary>The seeded RNG for a body's pads — stable per body, so the pad count + positions are the same
    /// every load and can be queried cheaply (e.g. for the star-map "full" signal) without loading the world.</summary>
    private System.Random PadRng(string locationId)
    {
        long seed = _meta.Seed ^ WorldGenerator.StableHash("landingpads:" + locationId);
        return new System.Random(unchecked((int)(seed ^ (seed >> 32))));
    }

    /// <summary>How many pads a body has: a seeded-random base count varying within its size-class range,
    /// DOUBLED so each body offers twice as many landing spots — asteroids 2–4, moons 4–8, planets 8–16
    /// (fewest → most by world size). The ×2 is applied AFTER the single deterministic draw, so the same
    /// seed still yields the same base count (determinism preserved) — only the multiplier scales it.
    /// This is the single source of truth for the pad count: BOTH BuildLandingPads (the in-world placement)
    /// and HandleRequestLandingPads (the approach landing map / pad chooser) derive their count from here,
    /// so the map always shows exactly the spots that exist in the world.</summary>
    private int PadCountFor(string locationId, string planetKey, CelestialKind kind)
    {
        var cls = WorldConstants.SizeClassFor(kind, planetKey);
        (int lo, int hi) = cls switch
        {
            WorldConstants.WorldSizeClass.Asteroid => (1, 2),
            WorldConstants.WorldSizeClass.Moon => (2, 4),
            _ => (4, 8),
        };
        int baseCount = lo + PadRng(locationId).Next(hi - lo + 1);
        return baseCount * 2; // double the landing spots per world (kept consistent across both consumers)
    }

    /// <summary>(Re)builds the active world's deterministic pad set and hands it to worldgen for terrain
    /// levelling. Idempotent — called on every world load (the pads aren't persisted; they're recomputed from
    /// the body seed).</summary>
    private void BuildLandingPads()
    {
        var pads = _landingPads;
        pads.Clear();

        var body = _galaxy?.FindBody(_world.LocationId);
        var kind = body?.Kind ?? CelestialKind.Planet;
        pads.AddRange(ComputeLandingPads(_world.Planet, kind, _world.LocationId, _world.Circumference));

        // Hand the planned pads to worldgen so their terrain is levelled at generation time (ship-as-object:
        // the landed ship is a placed structure that needs flat, clear ground). Must run before any pad-area
        // chunk generates — ComputeLandingPads only needs noise queries, so it is safe this early.
        var flats = _world.LandingPadFlats;
        flats.Clear();
        foreach (var pad in pads)
        {
            flats.Add(new BlocksBeyondTheStars.WorldGeneration.LandingPadFlatten(pad.CenterX, pad.CenterZ, pad.CenterY, pad.Radius));
        }
    }

    /// <summary>The single source of truth for a body's landing pads — usable for ANY body, loaded or not, so
    /// the in-world placement and the pre-landing pad-chooser map agree exactly. Pads are spread across BOTH
    /// longitude (X) and latitude (Z) — pad 0 is the prime-meridian/equator home touchdown, the rest are
    /// scattered with a golden-ratio latitude sequence + an even longitude spread — each nudged onto dry,
    /// reasonably flat ground. Deterministic from the body seed. Configures the shared generator for the target
    /// body (circumference + airless-moon cratering) and restores it afterwards.</summary>
    private List<LandingPad> ComputeLandingPads(PlanetType planet, CelestialKind kind, string locationId, int circ)
    {
        int savedCirc = _generator.Circumference;
        bool savedCratered = _generator.Cratered;
        _generator.SetCircumference(circ);
        bool airlessMoon = kind == CelestialKind.Moon
            && string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        _generator.SetCratered(airlessMoon);

        try
        {
            int count = PadCountFor(locationId, planet.Key, kind);
            int latP = WorldConstants.LatitudePeriodFor(circ);
            // Keep pads inside a navigable mid-latitude band (so they spread well on the map without touching
            // the latitude wrap), and ensure the footprint fits.
            int latBand = System.Math.Min((int)(latP * 0.38), latP / 2 - LandingPadRadius - 8);
            if (latBand < 0)
            {
                latBand = 0;
            }

            // A stable per-body latitude offset so different bodies don't share the same scatter pattern.
            double latOffset = (WorldGenerator.StableHash("padlat:" + locationId) & 0x3FF) / 1024.0;

            var pads = new List<LandingPad>(count);
            for (int i = 0; i < count; i++)
            {
                int baseX, baseZ;
                if (i == 0)
                {
                    baseX = 0;
                    baseZ = 0; // home touchdown: prime meridian, equator
                }
                else
                {
                    baseX = WorldConstants.WrapX((int)((i / (double)count) * circ), circ);
                    double gz = (i * 0.61803398875 + latOffset) % 1.0; // golden-ratio stratified latitude
                    baseZ = (int)System.Math.Round((gz - 0.5) * 2.0 * latBand);
                }

                // March the longitude (at this pad's latitude) to the nearest dry + reasonably flat column, so
                // a ship never lands in water (B36) or perches on a terrain spike (dramatic-terrain worlds).
                int cx = NudgePadToDryAndFlat(planet, baseX, baseZ);
                pads.Add(new LandingPad { Index = i, CenterX = cx, CenterZ = baseZ, CenterY = PadGroundY(planet, cx, baseZ) });
            }

            return pads;
        }
        finally
        {
            _generator.SetCircumference(savedCirc);
            _generator.SetCratered(savedCratered);
        }
    }

    /// <summary>The pad/ship ground height on the ACTIVE world: the MEDIAN surface height over the landing
    /// footprint (centre + four corners), not the centre column alone — one rocky spike no longer hoists the
    /// whole ship. Used by every ship-placement consumer.</summary>
    private int PadGroundY(int cx, int cz) => PadGroundY(_world.Planet, cx, cz);

    /// <summary>As <see cref="PadGroundY(int,int)"/> but for an explicit planet (the generator must already be
    /// configured for that body's circumference) — so pads can be computed for any body, loaded or not.</summary>
    private int PadGroundY(PlanetType planet, int cx, int cz)
    {
        const int r = 4;
        var h = new[]
        {
            _generator.SurfaceHeight(planet, cx, cz),
            _generator.SurfaceHeight(planet, cx - r, cz - r),
            _generator.SurfaceHeight(planet, cx + r, cz - r),
            _generator.SurfaceHeight(planet, cx - r, cz + r),
            _generator.SurfaceHeight(planet, cx + r, cz + r),
        };
        System.Array.Sort(h);
        return h[2];
    }

    /// <summary>Height spread over the landing footprint — small = flat enough to set a ship down on.</summary>
    private int PadFootprintSpread(PlanetType planet, int cx, int cz)
    {
        const int r = 4;
        int min = int.MaxValue, max = int.MinValue;
        foreach (var (dx, dz) in new[] { (0, 0), (-r, -r), (r, -r), (-r, r), (r, r), (-r, 0), (r, 0), (0, -r), (0, r) })
        {
            int y = _generator.SurfaceHeight(planet, cx + dx, cz + dz);
            min = System.Math.Min(min, y);
            max = System.Math.Max(max, y);
        }

        return max - min;
    }

    /// <summary>Marches a pad longitude (at a fixed latitude) to the nearest column that is both DRY and
    /// reasonably FLAT (footprint spread ≤ 5). Falls back to the flattest dry candidate seen, then to the dry
    /// nudge. Uses the generator's currently-configured circumference for the wrap.</summary>
    private int NudgePadToDryAndFlat(PlanetType planet, int baseX, int baseZ)
    {
        int circ = _generator.Circumference;
        int bestX = NudgePadToDry(planet, baseX, baseZ);
        int bestSpread = LandingFootprintWet(planet, bestX, baseZ) ? int.MaxValue : PadFootprintSpread(planet, bestX, baseZ);
        if (bestSpread <= 5)
        {
            return bestX;
        }

        for (int step = 1; step <= 40; step++)
        {
            foreach (int x in new[] { WorldConstants.WrapX(baseX + step * 3, circ), WorldConstants.WrapX(baseX - step * 3, circ) })
            {
                if (LandingFootprintWet(planet, x, baseZ))
                {
                    continue;
                }

                int spread = PadFootprintSpread(planet, x, baseZ);
                if (spread <= 5)
                {
                    return x; // flat + dry — done
                }

                if (spread < bestSpread)
                {
                    bestSpread = spread;
                    bestX = x;
                }
            }
        }

        return bestX; // the flattest dry spot found (worst case: the old dry nudge)
    }

    /// <summary>Nudges a pad longitude (at a fixed latitude) to the nearest dry column (deterministic
    /// out-stepping), so the ship never lands in a sea or upland pond (B36). Returns the original X on an
    /// all-ocean band (seabed pad fallback — the cabin floor is a dry platform anyway).</summary>
    private int NudgePadToDry(PlanetType planet, int baseX, int baseZ)
    {
        int circ = _generator.Circumference;
        if (!LandingFootprintWet(planet, baseX, baseZ))
        {
            return baseX;
        }

        for (int step = 1; step <= 40; step++)
        {
            int xp = WorldConstants.WrapX(baseX + step * 3, circ);
            if (!LandingFootprintWet(planet, xp, baseZ)) { return xp; }
            int xm = WorldConstants.WrapX(baseX - step * 3, circ);
            if (!LandingFootprintWet(planet, xm, baseZ)) { return xm; }
        }

        return baseX;
    }

    /// <summary>True if the pad (its centre or any radius edge) sits over surface water/lava on the ACTIVE
    /// world.</summary>
    private bool LandingFootprintWet(int cx, int cz) => LandingFootprintWet(_world.Planet, cx, cz);

    /// <summary>As above but for an explicit planet (the generator must already be configured for that body's
    /// circumference) — so a ship never touches down in a sea or pond, on any body (B36/B54).</summary>
    private bool LandingFootprintWet(PlanetType planet, int cx, int cz)
    {
        int r = LandingPadRadius;
        bool Wet(int x, int z) => _generator.IsSurfaceWater(planet, x, z) || _generator.IsSurfaceLava(planet, x, z);
        return Wet(cx, cz) || Wet(cx - r, cz) || Wet(cx + r, cz) || Wet(cx, cz - r) || Wet(cx, cz + r);
    }

    // --- live occupancy (derived from sessions, never persisted) ---

    /// <summary>True if another player currently holds this pad on this body — i.e. is standing on the body
    /// (not flown off to space) with this pad assigned. The exception is the player being served.</summary>
    private bool PadOccupiedByOther(string locationId, int padIndex, string exceptPlayerId)
    {
        foreach (var s in _sessions.Values)
        {
            if (!s.Joined || s.State.PlayerId == exceptPlayerId)
            {
                continue;
            }

            if (s.AssignedPadIndex == padIndex && s.CurrentLocationId == locationId && !InSpace(s.State.PlayerId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The lowest free pad index on a body, or -1 if every pad is currently taken (the body is full).</summary>
    private int FirstFreePadIndex(string locationId, int total, string exceptPlayerId)
    {
        for (int i = 0; i < total; i++)
        {
            if (!PadOccupiedByOther(locationId, i, exceptPlayerId))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>How many of a body's pads are currently free (live occupancy).</summary>
    private int FreePadCount(string locationId, int total)
    {
        int free = 0;
        for (int i = 0; i < total; i++)
        {
            if (!PadOccupiedByOther(locationId, i, string.Empty))
            {
                free++;
            }
        }

        return free;
    }

    /// <summary>The name of the player currently holding a pad on a body, or null if it's free.</summary>
    private string? PadOccupantName(string locationId, int padIndex)
    {
        foreach (var s in _sessions.Values)
        {
            if (s.Joined && s.AssignedPadIndex == padIndex && s.CurrentLocationId == locationId && !InSpace(s.State.PlayerId))
            {
                return s.State.Name;
            }
        }

        return null;
    }

    /// <summary>Picks the pad a landing player will touch down on: their requested pad if it's free, else (for an
    /// auto request, index &lt; 0) the first free pad. Returns -1 and a reason if the pad is taken or the body is
    /// full. Validates against the destination body's deterministic pad count (works before the world is loaded).</summary>
    private int TryClaimPad(PlayerSession session, string locationId, int total, int requestedIndex, out string reason)
    {
        reason = string.Empty;
        if (total <= 0)
        {
            return 0; // a body with no pads (shouldn't happen) — touch down at the origin pad
        }

        if (requestedIndex >= 0)
        {
            if (requestedIndex >= total)
            {
                reason = "That landing pad doesn't exist.";
                return -1;
            }

            if (PadOccupiedByOther(locationId, requestedIndex, session.State.PlayerId))
            {
                reason = "That landing pad is already taken.";
                return -1;
            }

            return requestedIndex;
        }

        int free = FirstFreePadIndex(locationId, total, session.State.PlayerId);
        if (free < 0)
        {
            reason = "All landing zones are taken.";
        }

        return free;
    }

    /// <summary>The active world's pad for a player (their assigned one; auto-assigns the first free pad as a
    /// safety net if somehow unassigned, e.g. an initial spawn). Used to stamp the ship + place the player.</summary>
    private LandingPad PlayerPad(PlayerSession session)
    {
        if (_landingPads.Count == 0)
        {
            BuildLandingPads();
        }

        int idx = session.AssignedPadIndex;
        if (idx < 0 || idx >= _landingPads.Count)
        {
            idx = FirstFreePadIndex(_world.LocationId, _landingPads.Count, session.State.PlayerId);
            if (idx < 0)
            {
                idx = 0; // overflow: the body is full but an initial spawn must still place the player
            }

            session.AssignedPadIndex = idx;
        }

        return _landingPads[idx];
    }

    /// <summary>True if a cell falls within a pad's footprint columns (longitude-wrap aware).</summary>
    private bool OnPadColumn(LandingPad pad, int x, int z)
        => System.Math.Abs(WorldConstants.WrapDeltaX(x - pad.CenterX, _world.Circumference)) <= pad.Radius
            && System.Math.Abs(z - pad.CenterZ) <= pad.Radius;

    /// <summary>True if a cell lies in a landing pad's reserved <b>landing volume</b> — its footprint, from just
    /// below the ground up to the ship-clearance height. No one may build there (the pad is kept clear for ships);
    /// building high above the pad is fine. Mining is unaffected (only placing is blocked).</summary>
    private bool IsOnLandingPad(Vector3i pos)
    {
        foreach (var pad in _landingPads)
        {
            if (OnPadColumn(pad, pos.X, pos.Z) && pos.Y >= pad.CenterY - 2 && pos.Y <= pad.CenterY + PadClearanceHeight)
            {
                return true;
            }
        }

        return false;
    }

    // --- landing flow + networking ---

    /// <summary>Claims the player's chosen (or first free) pad on a body before committing a landing. Sends a
    /// reject + returns false if the pad is taken or the body is full (so the caller leaves the player in flight),
    /// else records the pad on the session and returns true. Validates against the body's deterministic pad count,
    /// so it works whether or not the destination world is loaded yet.</summary>
    private bool ClaimPadOrReject(PlayerSession session, string bodyId, int padIndex)
    {
        var body = _galaxy?.FindBody(bodyId);
        int total = body != null ? PadCountFor(bodyId, body.PlanetType ?? string.Empty, body.Kind) : 1;
        int chosen = TryClaimPad(session, bodyId, total, padIndex, out string reason);
        if (chosen < 0)
        {
            Reject(session, "land", reason);
            return false;
        }

        session.AssignedPadIndex = chosen;
        return true;
    }

    /// <summary>Sets a player (and re-stamps their ship) down on the pad they claimed, on the body they're already
    /// on — used when landing back on the current body (the cross-body case goes through travel).</summary>
    private void RelocateToAssignedPad(PlayerSession session)
    {
        SetActiveWorld(session.CurrentLocationId);
        SetCurrent(session);
        MarkArrivedOnBody(session, session.CurrentLocationId); // touched down here → a quick-travel target
        if (_ship is not null)
        {
            _ship.CurrentLocationId = session.CurrentLocationId; // keep the ship's body in sync so a later launch rises off THIS body (mirrors HandleTravel; B48) — fixes launching off an asteroid landing you adrift in the wrong orbit
        }

        if (_config.PlaceStarterShip)
        {
            PlaceLandedShip();
        }

        var pad = PlayerPad(session);
        int surfaceY = PadGroundY(pad.CenterX, pad.CenterZ); // matches the ship placement's median footprint height
        var spawn = _shipPlaced ? _healTank : new Vector3f(pad.CenterX + 0.5f, surfaceY + 2f, pad.CenterZ + 0.5f);
        session.State.Position = spawn;
        session.State.RespawnPoint = _shipPlaced ? _healTank : spawn;
        session.State.AboardShip = true;
        SendPlayerState(session);
        SendLandedShips(session); // the landing world's parked ship objects (incl. the player's own)
        SendLandingPads(session);
        BroadcastShipTransit(session, session.CurrentLocationId, pad.CenterX + 0.5f, surfaceY, pad.CenterZ + 0.5f, landing: true); // others see the descent
    }

    /// <summary>Sends the active body's pads + live occupancy to a player (on world entry) — drives the pad
    /// markers on the world map.</summary>
    private void SendLandingPads(PlayerSession session)
    {
        var pads = new NetLandingPad[_landingPads.Count];
        for (int i = 0; i < _landingPads.Count; i++)
        {
            var p = _landingPads[i];
            string occ = PadOccupantName(_world.LocationId, p.Index) ?? string.Empty;
            pads[i] = new NetLandingPad { Index = p.Index, X = p.CenterX, Z = p.CenterZ, Occupied = occ.Length > 0, Occupant = occ };
        }

        Send(session, new LandingPadList { BodyId = _world.LocationId, Pads = pads });
    }

    /// <summary>Tells the players already on a body that another player's ship is arriving/departing at a pad, so
    /// they see a landing/launch animation (item 38). Sent only to the others on that body (not the mover, not
    /// anyone in space).</summary>
    private void BroadcastShipTransit(PlayerSession mover, string bodyId, float x, float y, float z, bool landing)
    {
        ShipTransitFx? msg = null;
        SpaceStructure? design = null;
        foreach (var s in _sessions.Values)
        {
            if (!s.Joined || s == mover || s.CurrentLocationId != bodyId || InSpace(s.State.PlayerId))
            {
                continue;
            }

            msg ??= new ShipTransitFx
            {
                PlayerId = mover.State.PlayerId,
                Name = mover.State.Name,
                X = x,
                Y = y,
                Z = z,
                Landing = landing,
                Hull = mover.HullColor,
            };

            // The mover's REAL voxel ship design rides ahead of the FX, so the watcher's animation
            // shows the actual ship that is landing/launching, not a generic silhouette.
            design ??= BuildShipStructure(mover.State.PlayerId);
            SendShipDesign(s, design, "ship_remote");
            Send(s, msg);
        }
    }

    /// <summary>Replies to a client's request for a body's pads + occupancy (the pad chooser shown before landing).
    /// The body may be remote (not loaded), so positions are the deterministic base longitudes — exact dry-land
    /// positions arrive once the player is actually on the body; the chooser only needs index + occupancy.</summary>
    private void HandleRequestLandingPads(PlayerSession session, RequestLandingPadsIntent intent)
    {
        var body = _galaxy?.FindBody(intent.BodyId);
        if (body is null)
        {
            return;
        }

        // Compute the body's REAL pads (same source of truth as the in-world placement), so the chooser map
        // shows each pad exactly where the ship will touch down — including its true latitude (Z), not a line.
        var computed = ComputeLandingPadsForBody(body);
        var pads = new NetLandingPad[computed.Count];
        for (int i = 0; i < computed.Count; i++)
        {
            var p = computed[i];
            string occ = PadOccupantName(body.Id, p.Index) ?? string.Empty;
            pads[i] = new NetLandingPad { Index = p.Index, X = p.CenterX, Z = p.CenterZ, Occupied = occ.Length > 0, Occupant = occ };
        }

        Send(session, new LandingPadList { BodyId = body.Id, Pads = pads });
    }

    /// <summary>The real pad set for a body (the chooser path). Resolves the body's planet type + circumference,
    /// then delegates to the shared <see cref="ComputeLandingPads"/>. Empty for a body with no surface (a
    /// station/wreck you dock with rather than land on).</summary>
    private List<LandingPad> ComputeLandingPadsForBody(CelestialBody body)
    {
        var planet = _content.GetPlanet(body.PlanetType ?? string.Empty);
        if (planet is null)
        {
            return new List<LandingPad>();
        }

        int circ = WorldConstants.CircumferenceFor(body.Id, WorldConstants.SizeClassFor(body.Kind, body.PlanetType ?? string.Empty));
        return ComputeLandingPads(planet, body.Kind, body.Id, circ);
    }

    // --- test hooks ---

    /// <summary>Number of pads on the active world.</summary>
    public int LandingPadCount => _landingPads.Count;

    /// <summary>Test hook: the pad centres (index, x, z) the approach landing map / pad chooser would advertise
    /// for the active body — i.e. what <see cref="HandleRequestLandingPads"/> derives. It MUST equal
    /// <see cref="LandingPadCenters"/>: the chooser map shows each pad exactly where the ship lands.</summary>
    public IReadOnlyList<(int Index, int X, int Z)> ApproachMapPadsForTest()
    {
        var body = _galaxy?.FindBody(_world.LocationId);
        if (body is null)
        {
            return System.Array.Empty<(int, int, int)>();
        }

        return ComputeLandingPadsForBody(body).ConvertAll(p => (p.Index, p.CenterX, p.CenterZ));
    }

    /// <summary>Test hook: the number of pads the approach landing map advertises for the active body.</summary>
    public int ApproachMapPadCountForTest() => ApproachMapPadsForTest().Count;

    /// <summary>Pad centres (index, x, z) on the active world, for tests/inspection.</summary>
    public IReadOnlyList<(int Index, int X, int Z)> LandingPadCenters
        => _landingPads.ConvertAll(p => (p.Index, p.CenterX, p.CenterZ));

    /// <summary>Test hook: true if the active world's pad at this index sits on dry land (B36).</summary>
    public bool LandingPadIsDry(int index)
        => index >= 0 && index < _landingPads.Count && !LandingFootprintWet(_landingPads[index].CenterX, _landingPads[index].CenterZ);

    /// <summary>Test hook: true if a cell column lies on a reserved pad (checked at the pad's ground level).</summary>
    public bool IsOnLandingPadForTest(int x, int z)
    {
        foreach (var pad in _landingPads)
        {
            if (OnPadColumn(pad, x, z))
            {
                return IsOnLandingPad(new Vector3i(x, pad.CenterY + 1, z));
            }
        }

        return false;
    }

    /// <summary>Test hook: how many of the active world's pads are currently free.</summary>
    public int FreePadCountForTest() => FreePadCount(_world.LocationId, _landingPads.Count);

    /// <summary>Test hook: runs the landing-pad claim for a player (mirrors a landing). Returns the chosen pad
    /// index (or -1 with a reason if the pad is taken / the body is full).</summary>
    public (int Chosen, string Reason) TryClaimPadForTest(PlayerSession session, int padIndex)
    {
        int chosen = TryClaimPad(session, _world.LocationId, _landingPads.Count, padIndex, out string reason);
        return (chosen, reason);
    }
}
