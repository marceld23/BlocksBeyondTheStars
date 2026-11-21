// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Who lives on the other side (#3905) — animals and figures that exist only for the players who are over there
/// (<see cref="CombatEntity.OtherSideOnly"/>, <c>ServerNpc.OtherSideOnly</c>; the lists of our side never name them):
/// <list type="bullet">
/// <item><b>The Qualis</b> ("the Lights", as the Echoes call them): while a player is over there on a planet, a few drift
/// low and close — <see cref="QualiMinNear"/>–<see cref="QualiMaxNear"/> around each player (a group shares them), never
/// more than <see cref="QualiWorldCap"/> in a world. They are the Quali species with a low hover band
/// (<see cref="QualiHoverAltitude"/>), moved by the creatures' own drifter code, untouchable like every Quali, outside the
/// fauna cap, and gone when nobody over there is near. The Qualis never speak.</item>
/// <item><b>The Sounders who crossed over</b>: at a gate near a player over there stand one to three Echo figures (look
/// <c>sounder_echo</c>, names from <see cref="OtherSideFolkNames"/> by the world seed and the gate), gone when nobody over
/// there is near. They answer the use key with small talk — the dialog <c>snd_over_folk</c>, bound to them by the
/// character id <see cref="OtherSideFolkCharacter"/>.</item>
/// </list>
/// The four Echoes of the story (Elora, Kamo, Feya, Tini) are not placed here: the story places them through
/// <see cref="SpawnOtherSideEcho"/> and takes them away with <c>RemoveSounderNpc</c>.
/// <para>A gate is found where the server already knows one may stand — the player's entry gate, a monument, a melody
/// site (the Singing Shrine, the Choir Tower, a ring), the nearest rune shrine — and around the player themselves (the
/// scan the Echoes of chapter 5 use), so stones a player placed get their folk when somebody walks up to them. One
/// player and at most <see cref="FolkGateScansPerPass"/> scans every two seconds.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The species id of the low-drifting Qualis of the other side (the Quali record, another hover band).</summary>
    internal const string OtherSideQualiSpeciesId = "au_quali_over";

    /// <summary>The middle of the band they drift in, blocks over the ground (their own wave makes it about 10–18).</summary>
    internal const float QualiHoverAltitude = 14f;

    /// <summary>A player over there has at least this many Qualis near …</summary>
    internal const int QualiMinNear = 2;

    /// <summary>… and at most this many (which of the three is the player's own number).</summary>
    internal const int QualiMaxNear = 4;

    /// <summary>No world holds more Qualis of the other side than this.</summary>
    internal const int QualiWorldCap = 8;

    /// <summary>A Quali comes in between these distances from its player (blocks) …</summary>
    internal const float QualiSpawnMin = 14f;

    /// <summary>… see <see cref="QualiSpawnMin"/>.</summary>
    internal const float QualiSpawnMax = 34f;

    /// <summary>A Quali within this of a player is theirs; one farther than this from every player over there is gone
    /// (another one comes in closer).</summary>
    internal const float QualiLeaveRange = 56f;

    /// <summary>The character id of the Echo figures at the gates — the dialog <c>snd_over_folk</c> is theirs.</summary>
    internal const string OtherSideFolkCharacter = "snd_over_folk";

    /// <summary>How far from a player over there a known site is looked at for a gate (blocks).</summary>
    internal const float FolkSiteRange = 96f;

    /// <summary>The figures of a gate leave when no player over there is within this (blocks).</summary>
    internal const float FolkLeaveRange = 110f;

    /// <summary>Two rune groups whose centres lie closer than this are one gate.</summary>
    internal const float FolkGateSpacing = 14f;

    /// <summary>Rune-stone scans per folk pass at most (each reads a 17³ box of loaded cells).</summary>
    internal const int FolkGateScansPerPass = 3;

    /// <summary>Seconds a place that turned out to be no gate is left alone, and between two looks at a known gate.</summary>
    private const double FolkRecheckSeconds = 20.0;

    /// <summary>The names of the Sounders at the gates — invented, language-neutral like every NPC name, and none of the
    /// story's (Elora, Kamo, Feya, Tini, Roll).</summary>
    internal static readonly string[] OtherSideFolkNames =
    {
        "Anuri", "Belu", "Dovi", "Esun", "Ilmo", "Kelu", "Lenua", "Maro",
        "Nuvi", "Oria", "Pelu", "Sumi", "Teva", "Ulan", "Vano", "Yeru",
    };

    /// <summary>The other side's runtime state of one world: the pace of its watch, the gates that have their folk.</summary>
    private sealed class OtherSideWorld
    {
        public double Clock;
        public int Pass;
        public int Rotor;
        public readonly List<OtherSideGate> Gates = new();
        public readonly Dictionary<Vector3i, double> NoGateUntil = new();
        public Random Rng = new(1);
    }

    private sealed class OtherSideGate
    {
        public Vector3f Centre;
        public double CheckAt;
        public readonly List<ServerNpc> Folk = new();
    }

    private readonly Dictionary<string, OtherSideWorld> _otherSideWorlds = new(StringComparer.Ordinal);

    private OtherSideWorld OtherSideWorldOf(string locationId)
    {
        if (!_otherSideWorlds.TryGetValue(locationId, out var world))
        {
            long seed = _meta.Seed ^ WorldGenerator.StableHash("otherside:" + locationId);
            _otherSideWorlds[locationId] = world = new OtherSideWorld { Rng = new Random(unchecked((int)(seed ^ (seed >> 32)))) };
        }

        return world;
    }

    // ---------------- The story's Echoes ----------------

    /// <summary>
    /// Puts a story Echo on the other side of the active world: an Echo figure only the players over there see and can
    /// talk to, carrying <paramref name="characterId"/> (so the pack's dialog for that character answers) and named after
    /// it (<c>elora</c> → "Elora"). Returns its NPC id; <c>RemoveSounderNpc</c> takes it away again. NPCs are runtime state:
    /// place it again whenever the world was loaded anew.
    /// </summary>
    internal int SpawnOtherSideEcho(string characterId, Vector3f at)
    {
        string name = characterId.Length > 0 ? char.ToUpperInvariant(characterId[0]) + characterId.Substring(1) : string.Empty;
        return SpawnOverThere(at, name, characterId).Id;
    }

    private ServerNpc SpawnOverThere(Vector3f at, string name, string characterId)
    {
        SpawnSounderNpc(SoundersIds.Echo, at, name, characterId, leash: 1.2f);
        var npc = _npcs[^1];
        npc.OtherSideOnly = true; // before this tick's flush: no list of our side ever names it
        return npc;
    }

    /// <summary>An NPC with this id that lives over there, or null.</summary>
    private ServerNpc? OtherSideNpc(int npcId)
    {
        foreach (var npc in _npcs)
        {
            if (npc.Id == npcId)
            {
                return npc.OtherSideOnly ? npc : null;
            }
        }

        return null;
    }

    // ---------------- The pass ----------------

    /// <summary>Once a second per world (from <see cref="TickOtherSide"/>): the Qualis and the figures at the gates follow
    /// the players who are over there; without one, the world's other side is empty.</summary>
    private void TickOtherSideFolk(OtherSideWorld world)
    {
        var over = PlayersOverThere().ToList();
        if (over.Count == 0)
        {
            ClearOtherSideFolk(world);
            return;
        }

        world.Pass++;
        if (TickOtherSideQualis(world, over))
        {
            BroadcastCreatures();
        }

        bool npcsChanged = KeepOtherSideGates(world, over);
        if (world.Pass % 2 == 0)
        {
            npcsChanged |= FindOtherSideGate(world, over[world.Rotor++ % over.Count]);
        }

        if (npcsChanged)
        {
            BroadcastNpcs();
        }
    }

    /// <summary>Nobody is over there (any more): the Qualis and the figures of this world go.</summary>
    private void ClearOtherSideFolk(OtherSideWorld world)
    {
        if (_creatures.RemoveAll(c => c.OtherSideOnly) > 0)
        {
            BroadcastCreatures();
        }

        world.NoGateUntil.Clear();
        if (world.Gates.Count == 0)
        {
            return;
        }

        foreach (var gate in world.Gates)
        {
            RemoveFolk(gate);
        }

        world.Gates.Clear();
        BroadcastNpcs();
    }

    // ---------------- The Qualis ----------------

    /// <summary>The Quali of the other side as a species of the active world — the authored Quali with the low hover band,
    /// awake at any hour. Known to the creature tick, never in the world's roster (the spawner must not bring it to our
    /// side). Null on a world without ground life (a station) or without the record.</summary>
    private CreatureSpecies? OtherSideQualiSpecies()
    {
        if (_speciesById.TryGetValue(OtherSideQualiSpeciesId, out var known))
        {
            return known;
        }

        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        if (planet is null || planet.Void || !_content.AuthoredCreatures.TryGetValue(SoundersIds.Quali, out var record))
        {
            return null;
        }

        var sp = CreatureGenerator.MakeAuthored(record, planet, WorldGenerator.RosterSeedFor(_meta.Seed, _world.LocationId));
        sp.Id = OtherSideQualiSpeciesId;
        sp.HoverAltitude = QualiHoverAltitude;
        sp.Activity = CreatureActivity.Cathemeral;
        _speciesById[sp.Id] = sp;
        _locoProfiles[sp.Id] = LocomotionController.ForSpecies(sp);
        return sp;
    }

    /// <summary>How many Qualis <paramref name="s"/> has about them (2–4, the player's own number).</summary>
    internal static int QualisWantedNear(PlayerSession s)
        => QualiMinNear + (int)((ulong)WorldGenerator.StableHash("quali:" + s.State.PlayerId) % (QualiMaxNear - QualiMinNear + 1));

    /// <summary>Lets the far Qualis go and brings one in for a player who has too few near (one a pass, so they drift in
    /// one after the other). True when the creature list changed.</summary>
    private bool TickOtherSideQualis(OtherSideWorld world, List<PlayerSession> over)
    {
        double leaveSq = (double)QualiLeaveRange * QualiLeaveRange;
        bool changed = _creatures.RemoveAll(c => c.OtherSideOnly && !AnyWithin(over, c.Position, leaveSq)) > 0;
        if (OtherSideQualiSpecies() is not { } sp)
        {
            return changed;
        }

        int total = 0;
        foreach (var c in _creatures)
        {
            if (c.OtherSideOnly)
            {
                total++;
            }
        }

        foreach (var s in over)
        {
            if (total >= QualiWorldCap)
            {
                break;
            }

            int near = 0;
            foreach (var c in _creatures)
            {
                if (c.OtherSideOnly && WrapDistSq(c.Position, s.State.Position) <= leaveSq)
                {
                    near++;
                }
            }

            if (near < QualisWantedNear(s))
            {
                SpawnOtherSideQuali(world, sp, s.State.Position);
                return true;
            }
        }

        return changed;
    }

    private void SpawnOtherSideQuali(OtherSideWorld world, CreatureSpecies sp, Vector3f around)
    {
        double angle = world.Rng.NextDouble() * Math.PI * 2.0;
        double dist = QualiSpawnMin + (world.Rng.NextDouble() * (QualiSpawnMax - QualiSpawnMin));
        int circ = _world.Circumference;
        var at = new Vector3f(
            (float)WorldConstants.WrapX(around.X + (Math.Cos(angle) * dist), circ),
            around.Y + QualiHoverAltitude,
            (float)WorldConstants.WrapZ(around.Z + (Math.Sin(angle) * dist), circ));
        SpawnCreature(sp, AdjustHabitatHeight(sp, at));
        _creatures[^1].OtherSideOnly = true;
    }

    private bool AnyWithin(List<PlayerSession> players, Vector3f at, double rangeSq)
    {
        foreach (var s in players)
        {
            if (WrapDistSq(s.State.Position, at) <= rangeSq)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- The Sounders at the gates ----------------

    /// <summary>The gates that have their folk: one nobody over there is near any more, one whose stones are gone, and one
    /// whose figures the world forgot (it was loaded anew) lose them. True when an NPC left.</summary>
    private bool KeepOtherSideGates(OtherSideWorld world, List<PlayerSession> over)
    {
        bool changed = false;
        double leaveSq = (double)FolkLeaveRange * FolkLeaveRange;
        for (int i = world.Gates.Count - 1; i >= 0; i--)
        {
            var gate = world.Gates[i];
            bool keep = AnyWithin(over, gate.Centre, leaveSq) && (gate.Folk.Count == 0 || _npcs.Contains(gate.Folk[0]));
            if (keep && _uptime >= gate.CheckAt)
            {
                gate.CheckAt = _uptime + FolkRecheckSeconds;
                keep = IsGate(gate.Centre, out _);
            }

            if (!keep)
            {
                changed |= RemoveFolk(gate);
                world.Gates.RemoveAt(i);
            }
        }

        return changed;
    }

    private bool RemoveFolk(OtherSideGate gate)
    {
        bool any = false;
        foreach (var npc in gate.Folk)
        {
            any |= _npcs.Remove(npc);
        }

        gate.Folk.Clear();
        return any;
    }

    /// <summary>Looks for one gate near <paramref name="s"/> that has no folk yet and gives it its figures. True when
    /// somebody came.</summary>
    private bool FindOtherSideGate(OtherSideWorld world, PlayerSession s)
    {
        int scans = 0;
        foreach (var place in GatePlacesNear(s))
        {
            var cell = new Vector3i((int)Math.Floor(place.X / 8f), (int)Math.Floor(place.Y / 8f), (int)Math.Floor(place.Z / 8f));
            if (GateKnownNear(world, place) || (world.NoGateUntil.TryGetValue(cell, out double until) && _uptime < until))
            {
                continue;
            }

            if (scans++ >= FolkGateScansPerPass)
            {
                break;
            }

            if (!IsGate(place, out var centre) || GateKnownNear(world, centre))
            {
                world.NoGateUntil[cell] = _uptime + FolkRecheckSeconds;
                continue;
            }

            var gate = new OtherSideGate { Centre = centre, CheckAt = _uptime + FolkRecheckSeconds };
            world.Gates.Add(gate);
            PlaceFolkAt(gate);
            return gate.Folk.Count > 0;
        }

        return false;
    }

    /// <summary>Where a gate may stand near a player over there: where they are, where they crossed, and the sites the
    /// server knows within <see cref="FolkSiteRange"/>.</summary>
    private IEnumerable<Vector3f> GatePlacesNear(PlayerSession s)
    {
        var at = s.State.Position;
        double rangeSq = (double)FolkSiteRange * FolkSiteRange;
        yield return at;
        if (EntryGateOf(s) is { } entry && WrapDistSq(entry, at) <= rangeSq)
        {
            yield return entry;
        }

        foreach (var m in _monuments)
        {
            if (WrapDistSq(m.Center, at) <= rangeSq)
            {
                yield return m.Center;
                yield return new Vector3f(m.Center.X, m.Min.Y + 1f, m.Center.Z); // the stones often stand at its foot
            }
        }

        if (_melodySites.TryGetValue(_world.LocationId, out var sites))
        {
            foreach (var site in sites.Values)
            {
                if (WrapDistSq(site.CentreF, at) <= rangeSq)
                {
                    yield return site.CentreF;
                }
            }
        }

        if (NearestRuneShrine(at, (int)FolkSiteRange) is { } shrine)
        {
            yield return new Vector3f(shrine.X + 0.5f, shrine.Y + 1f, shrine.Z + 0.5f);
        }
    }

    private bool GateKnownNear(OtherSideWorld world, Vector3f at)
    {
        foreach (var gate in world.Gates)
        {
            if (WrapDistSq(gate.Centre, at) <= FolkGateSpacing * FolkGateSpacing)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One to three Echo figures around the gate's centre, each on a free spot of ground; names and number come
    /// from the world seed and the gate, so the same gate has the same folk every time.</summary>
    private void PlaceFolkAt(OtherSideGate gate)
    {
        var c = gate.Centre;
        ulong hash = (ulong)WorldGenerator.StableHash(
            FormattableString.Invariant($"overfolk:{_meta.Seed}:{_world.LocationId}:{Math.Floor(c.X / 4f)}:{Math.Floor(c.Z / 4f)}"));
        int count = 1 + (int)(hash % 3);
        int first = (int)((hash >> 8) % (ulong)OtherSideFolkNames.Length);
        double turn = (hash >> 16) % 628 / 100.0;
        for (int i = 0; i < count; i++)
        {
            if (FolkSpot(c, turn + (i * Math.PI * 2.0 / count)) is not { } spot)
            {
                continue;
            }

            string name = OtherSideFolkNames[(first + (i * 5)) % OtherSideFolkNames.Length];
            var npc = SpawnOverThere(spot, name, OtherSideFolkCharacter);
            DressAsSounder(npc, echo: true, SounderSeed(name, string.Empty)); // its own height and hum, not the group's
            gate.Folk.Add(npc);
        }
    }

    /// <summary>A place to stand near the gate's centre in the direction <paramref name="angle"/>: solid ground with three
    /// free cells over it (an Echo is tall). Tries a few distances and a little to either side.</summary>
    private Vector3f? FolkSpot(Vector3f centre, double angle)
    {
        foreach (float radius in FolkRadii)
        {
            foreach (double side in FolkSides)
            {
                int x = (int)Math.Floor(centre.X + (Math.Cos(angle + side) * radius));
                int z = (int)Math.Floor(centre.Z + (Math.Sin(angle + side) * radius));
                for (int y = (int)Math.Floor(centre.Y) + 3; y >= (int)Math.Floor(centre.Y) - 5; y--)
                {
                    if (IsCollidingCellIfLoaded(x, y - 1, z, foliagePasses: false) && FreeForFolk(x, y, z)
                        && !IsRuneStoneCell(new Vector3i(x, y - 1, z))) // beside the stones, never on top of one
                    {
                        return new Vector3f(x + 0.5f, y, z + 0.5f);
                    }
                }
            }
        }

        return null;
    }

    private static readonly float[] FolkRadii = { 3.2f, 4.6f, 2.2f };
    private static readonly double[] FolkSides = { 0.0, 0.5, -0.5 };

    private bool FreeForFolk(int x, int y, int z)
        => !IsCollidingCellIfLoaded(x, y, z, foliagePasses: false) && !IsCollidingCellIfLoaded(x, y + 1, z, foliagePasses: false)
           && !IsCollidingCellIfLoaded(x, y + 2, z, foliagePasses: false);

    // ---------------- Test seams ----------------

    /// <summary>Test seam: <see cref="SpawnOtherSideEcho"/> on the active world.</summary>
    public int SpawnOtherSideEchoForTest(string characterId, Vector3f at) => SpawnOtherSideEcho(characterId, at);

    /// <summary>Test seam: the ids of the Qualis of the other side on the active world.</summary>
    public IReadOnlyList<string> OtherSideQualiIdsForTest() => _creatures.Where(c => c.OtherSideOnly).Select(c => c.Id).ToList();

    /// <summary>Test seam: the names of the Echo figures standing at the gates of the active world.</summary>
    public IReadOnlyList<string> OtherSideFolkNamesForTest()
        => _otherSideWorlds.TryGetValue(_world.LocationId, out var world)
            ? world.Gates.SelectMany(g => g.Folk).Select(n => n.Name).ToList()
            : new List<string>();

    /// <summary>Test seam: the ids of the NPCs of the active world that live over there.</summary>
    public IReadOnlyList<int> OtherSideNpcIdsForTest() => _npcs.Where(n => n.OtherSideOnly).Select(n => n.Id).ToList();
}
