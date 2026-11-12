// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>A wild creature may belong to a landmark's den (#3495).</summary>
public sealed partial class CombatEntity
{
    /// <summary>The den this animal lives at (<c>GameServer.LandmarkDen.Id</c>), empty for every other creature.</summary>
    public string DenId { get; set; } = string.Empty;
}

/// <summary>
/// A landmark in the living world — see GameServerLandmarks.cs:
/// <list type="bullet">
/// <item><b>Sound emitters</b> (#3494, marker <see cref="LandmarkRules.SoundMarker"/>: <c>sound</c>, <c>radius</c>,
/// <c>volume</c>): a place that plays a loop which gets louder as the listener comes closer. The server only says where
/// they are (<see cref="LandmarkList"/>, on world entry — so a late joiner hears them too); the client plays them.</item>
/// <item><b>Dens</b> (#3495, marker <see cref="LandmarkRules.DenMarker"/>: <c>species</c>, <c>count</c>, <c>radius</c>):
/// animals that live at a place. While a player is near, the den brings its pack up to the count one animal a second,
/// keeps them within its radius and out of the far-creature prune; an animal that was killed or tamed is not replaced
/// before <see cref="DenCooldownSeconds"/>. With nobody near, the animals go the way of every wild creature and the den
/// costs nothing. A den only ever houses a species the world's roster already has (<c>species=native</c> = the planet's
/// first peaceful one) — it never adds a species to a world.</item>
/// <item><b>Discovery</b> (#3496): a landmark goes on everyone's map when a player on foot comes within
/// <see cref="LandmarkRevealRange"/> of it; VEGA points the first one out once per player; scanning any of its blocks
/// reads as the landmark, worth the knowledge of a relic once per body and landmark.</item>
/// <item><b>The far silhouette</b> (#3497) rides in the same list.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How near (blocks, to the footprint) a player on foot must come for a landmark to go on the map.</summary>
    private const int LandmarkRevealRange = 80;

    /// <summary>How near VEGA speaks about it.</summary>
    private const int LandmarkHintRange = 48;

    /// <summary>How far beyond its radius a den notices a player.</summary>
    private const int DenWakeMargin = 48;

    /// <summary>How far beyond its radius a den's animal may stray before it is put back.</summary>
    private const int DenLeashSlack = 6;

    /// <summary>Seconds before an animal that was killed or tamed is replaced.</summary>
    internal const double DenCooldownSeconds = 300;

    /// <summary>The map entry type of a landmark.</summary>
    internal const string LandmarkPoiType = "landmark";

    /// <summary>The den species word for "whatever peaceful animal lives on this planet".</summary>
    private const string DenNativeSpecies = "native";

    /// <summary>One sound emitter of a landmark, in world space.</summary>
    internal sealed class LandmarkEmitter
    {
        public required Vector3f Position { get; init; }
        public required string Sound { get; init; }
        public required float Radius { get; init; }
        public required float Volume { get; init; }
    }

    /// <summary>One den of a landmark.</summary>
    internal sealed class LandmarkDen
    {
        public required string Id { get; init; }
        public required Vector3i Cell { get; init; }
        public required string Species { get; init; }
        public required int Count { get; init; }
        public required int Radius { get; init; }
        public List<string> Members { get; } = new();

        /// <summary>When each lost animal may be replaced (server uptime).</summary>
        public List<double> ReturnAt { get; } = new();
        public bool Awake { get; set; }
    }

    internal sealed partial class LandmarkInstance
    {
        public List<LandmarkEmitter> Emitters { get; } = new();

        public List<LandmarkDen> Dens { get; } = new();

        public string RevealKey { get; set; } = string.Empty;

        public LandmarkSilhouette? Silhouette { get; set; }
    }

    /// <summary>Per-world 1 Hz throttle of <see cref="TickLandmarks"/>.</summary>
    private readonly Dictionary<string, double> _landmarkTickAt = new(System.StringComparer.Ordinal);

    // ---------------------------------------------------------------- markers → emitters and dens

    /// <summary>A sound marker's emitter (#3494) — a landmark's, or (#3519) a settlement's or a station's.</summary>
    private static void AddMarkerEmitter(List<LandmarkEmitter> emitters, Vector3i cell, MarkerSettings settings)
    {
        string sound = settings.Get("sound");
        if (sound.Length == 0)
        {
            return;
        }

        emitters.Add(new LandmarkEmitter
        {
            Position = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f),
            Sound = sound,
            Radius = (float)System.Math.Clamp(settings.GetDouble("radius", 48), 4, 256),
            Volume = (float)System.Math.Clamp(settings.GetDouble("volume", 1.0), 0, 1),
        });
    }

    /// <summary>A den marker's den (#3495) — a landmark's, or (#3519) a settlement's or a station's.</summary>
    private void AddMarkerDen(List<LandmarkDen> dens, Vector3i cell, MarkerSettings settings)
    {
        string species = settings.Get("species", DenNativeSpecies);
        dens.Add(new LandmarkDen
        {
            Id = $"den:{_world.LocationId}:{cell.X}:{cell.Y}:{cell.Z}",
            Cell = cell,
            Species = species,
            Count = System.Math.Clamp(settings.GetInt("count", 3), 1, 12),
            Radius = System.Math.Clamp(settings.GetInt("radius", 12), 4, 48),
        });
    }

    // ---------------------------------------------------------------- the tick

    /// <summary>Per-world 1 Hz pass: the map reveal and the dens (a landmark's, and #3519 a settlement's or a station's).</summary>
    private void TickLandmarks(double dt)
    {
        _ = dt;
        if (_worlds.Active is null || (_landmarks.Count == 0 && !HasStructureDens(_world.LocationId)))
        {
            return;
        }

        string world = _world.LocationId;
        if (_landmarkTickAt.TryGetValue(world, out double due) && _uptime < due)
        {
            return;
        }

        _landmarkTickAt[world] = _uptime + 1.0;
        var players = JoinedInActiveWorld().Where(s => !s.Spectating && OnFootHere(s)).ToList();
        bool spawned = false;
        foreach (var lm in _landmarks)
        {
            if (!_meta.RevealedPois.Contains(lm.RevealKey) && players.Any(s => LandmarkGap(lm, s.State.Position) <= LandmarkRevealRange))
            {
                RevealPoi(lm.RevealKey);
            }

            foreach (var den in lm.Dens)
            {
                spawned |= TickDen(den, players);
            }
        }

        foreach (var den in StructureDensAt(world))
        {
            spawned |= TickDen(den, players); // #3519
        }

        if (spawned)
        {
            BroadcastCreatures();
        }
    }

    /// <summary>Test/util: one landmark pass right now.</summary>
    internal void TickLandmarksForTest()
    {
        _landmarkTickAt.Remove(_world.LocationId);
        TickLandmarks(0);
    }

    /// <summary>The horizontal gap between a position and a landmark's footprint (0 inside it), wrap-aware.</summary>
    private float LandmarkGap(LandmarkInstance lm, Vector3f pos)
    {
        float relX = WorldConstants.WrapDeltaX((int)pos.X - lm.Origin.X, _world.Circumference);
        float dx = Gap(relX, 0, lm.Structure.Width);
        float dz = Gap(pos.Z, lm.Origin.Z, lm.Origin.Z + lm.Structure.Length);
        return (float)System.Math.Sqrt((dx * dx) + (dz * dz));
    }

    // ---------------------------------------------------------------- dens

    /// <summary>Whether the far-creature prune must leave this animal alone: it lives at a den a player is near.</summary>
    private bool DenKeeps(CombatEntity c)
        => c.DenId.Length > 0 && (_landmarks.Any(lm => lm.Dens.Any(d => d.Awake && d.Id == c.DenId))
                                  || StructureDensAt(_world.LocationId).Any(d => d.Awake && d.Id == c.DenId));

    /// <summary>The species a den houses on this world, or null: an authored species the roster already has, or — for
    /// <see cref="DenNativeSpecies"/> — the planet's first peaceful species.</summary>
    private CreatureSpecies? DenSpecies(LandmarkDen den)
    {
        if (den.Species == DenNativeSpecies)
        {
            return _speciesRoster.FirstOrDefault(s => !s.Hostile && s.BodyPlan is not (CreatureBodyPlan.Titan or CreatureBodyPlan.Arachnid));
        }

        return _speciesById.TryGetValue(Shared.Sounders.SoundersFauna.SpeciesIdOf(den.Species), out var sp) ? sp
            : _speciesById.TryGetValue(den.Species, out var byId) ? byId
            : null;
    }

    /// <summary>One den, one second: wake or sleep by the nearest player, count the lost, put a stray back, and bring
    /// one more animal when the pack is short. Returns true when an animal was spawned.</summary>
    private bool TickDen(LandmarkDen den, List<PlayerSession> players)
    {
        var centre = new Vector3f(den.Cell.X + 0.5f, den.Cell.Y, den.Cell.Z + 0.5f);
        double wake = den.Radius + DenWakeMargin;
        bool near = players.Any(s => WrapDistSq(s.State.Position, centre) <= wake * wake);
        if (!near)
        {
            den.Awake = false; // its animals now go the way of every wild creature
            return false;
        }

        // Who is still here? While the den was awake, a missing animal was killed or tamed; after a sleep it just wandered off.
        var alive = new List<string>();
        foreach (string id in den.Members)
        {
            var c = _creatures.FirstOrDefault(e => e.Id == id && !e.IsCompanion);
            if (c is null)
            {
                if (den.Awake)
                {
                    den.ReturnAt.Add(_uptime + DenCooldownSeconds);
                }

                continue;
            }

            alive.Add(id);
            double leash = den.Radius + DenLeashSlack;
            if (WrapDistSq(c.Position, centre) > leash * leash && DenSpot(den) is { } back)
            {
                c.Position = back;
            }
        }

        den.Members.Clear();
        den.Members.AddRange(alive);
        den.Awake = true;
        den.ReturnAt.RemoveAll(t => t <= _uptime);
        if (den.Members.Count >= den.Count - den.ReturnAt.Count || DenSpecies(den) is not { } sp || DenSpot(den) is not { } spot)
        {
            return false;
        }

        SpawnCreature(sp, spot);
        var born = _creatures[^1];
        born.DenId = den.Id;
        den.Members.Add(born.Id);
        return true;
    }

    /// <summary>A free standing spot at the den: its own cell first, then rings around it up to half its radius — air at
    /// foot and head height over something solid.</summary>
    private Vector3f? DenSpot(LandmarkDen den)
    {
        bool Free(int x, int y, int z)
            => _world.GetBlock(new Vector3i(x, y, z)).IsAir && _world.GetBlock(new Vector3i(x, y + 1, z)).IsAir
               && _content.BlockById(_world.GetBlock(new Vector3i(x, y - 1, z))) is { Solid: true, Liquid: false };

        int reach = System.Math.Max(2, den.Radius / 2);
        int turn = den.Members.Count + den.ReturnAt.Count; // a different ring start per animal, so a pack does not stack
        for (int r = 0; r <= reach; r++)
        {
            for (int i = 0; i < System.Math.Max(1, 8 * r); i++)
            {
                double a = (i + turn) * System.Math.PI * 2.0 / System.Math.Max(1, 8 * r);
                int x = den.Cell.X + (int)System.Math.Round(System.Math.Cos(a) * r), z = den.Cell.Z + (int)System.Math.Round(System.Math.Sin(a) * r);
                for (int dy = 1; dy >= -2; dy--)
                {
                    if (Free(x, den.Cell.Y + dy, z))
                    {
                        return new Vector3f(x + 0.5f, den.Cell.Y + dy, z + 0.5f);
                    }
                }
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- the map, VEGA, the scanner

    /// <summary>A landmark's name in a player's language: its locale key when it has one, else the author's name.</summary>
    private string LandmarkName(LandmarkInstance lm, string locale)
    {
        string key = lm.Template.NameKey;
        if (key.Length > 0)
        {
            string text = Localize(locale, key);
            if (text != key && !text.StartsWith("[", System.StringComparison.Ordinal))
            {
                return text;
            }
        }

        return lm.Template.Name.Length > 0 ? lm.Template.Name : lm.Key;
    }

    /// <summary>Appends the landmarks somebody found to the POI list.</summary>
    private void AppendLandmarkPois(PlayerSession session, List<NetPoi> pois)
    {
        foreach (var lm in _landmarks)
        {
            if (_meta.RevealedPois.Contains(lm.RevealKey))
            {
                var (cx, cz, _, _) = lm.Rect;
                pois.Add(new NetPoi { Type = LandmarkPoiType, Name = LandmarkName(lm, session.Locale), X = cx, Z = cz });
            }
        }
    }

    /// <summary>VEGA's line at a landmark (once per player, from the 1 Hz advisor pass, never aboard): a giant ruin, or a
    /// house somebody keeps out in the open.</summary>
    private void TickLandmarkHint(PlayerSession session)
    {
        var p = session.State;
        if (p.AboardShip)
        {
            return;
        }

        foreach (var lm in _landmarks)
        {
            if (LandmarkGap(lm, p.Position) <= LandmarkHintRange)
            {
                ShipAiHintOnce(session, LandmarkIsKept(lm) ? "landmark_house" : "landmark_near");
            }
        }
    }

    /// <summary>The landmark a scan of <paramref name="cell"/> reads: the one whose box holds the aimed cell.</summary>
    private LandmarkInstance? LandmarkForScan(PlayerSession session, Vector3i? cell)
        => _landmarks.Count == 0 || session.CurrentLocationId != _worlds.Active.LocationId || cell is not { } aimed
            ? null
            : LandmarkAt(aimed) is { } lm && lm.Structure.Get(LocalX(lm, aimed), aimed.Y - lm.Origin.Y, aimed.Z - lm.Origin.Z) != 0 ? lm : null;

    private int LocalX(LandmarkInstance lm, Vector3i cell) => WorldConstants.WrapDeltaX(cell.X - lm.Origin.X, _world.Circumference);

    // ---------------------------------------------------------------- the client's list

    private NetLandmark ToNetLandmark(LandmarkInstance lm, PlayerSession session)
    {
        var model = lm.Silhouette ??= LandmarkSilhouetteOf(lm); // generation 39: from its structure overlay
        var corner = WorldConstants.CanonicalBlock(new Vector3i(lm.Origin.X, lm.GroundY, lm.Origin.Z), _world.Circumference);
        return new NetLandmark
        {
            Key = lm.Key,
            Name = LandmarkName(lm, session.Locale),
            X = corner.X,
            Y = corner.Y,
            Z = corner.Z,
            Step = model.Step,
            Width = model.Width,
            Height = model.Height,
            Length = model.Length,
            Cells = model.Cells,
            Blocks = model.Blocks,
            Tints = model.Tints,
            Ruined = lm.Layout.Ruined,
        };
    }

    private LandmarkList LandmarkListFor(PlayerSession session) => new()
    {
        Landmarks = _landmarks.Select(lm => ToNetLandmark(lm, session)).ToArray(),
        Emitters = _landmarks.SelectMany(lm => lm.Emitters).Concat(StructureEmittersAt(_world.LocationId)).Select(e => new NetSoundEmitter
        {
            X = e.Position.X,
            Y = e.Position.Y,
            Z = e.Position.Z,
            Sound = e.Sound,
            Radius = e.Radius,
            Volume = e.Volume,
        }).ToArray(),
    };

    /// <summary>Sends this world's landmarks and sound emitters to a session (on world entry). An empty list is sent too:
    /// it clears what the client held from the world it came from.</summary>
    private void SendLandmarks(PlayerSession session) => Send(session, LandmarkListFor(session));

    /// <summary>Test seam: what a session is sent on world entry.</summary>
    internal LandmarkList LandmarkListForTest(PlayerSession session) => LandmarkListFor(session);

    private void BroadcastLandmarks()
    {
        foreach (var session in JoinedInActiveWorld())
        {
            SendLandmarks(session);
        }
    }
}
