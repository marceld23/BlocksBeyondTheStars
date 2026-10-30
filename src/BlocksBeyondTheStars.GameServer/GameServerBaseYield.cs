// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Base and station yield (#2999, Marcel 2026-10-29): every base core and every commissioned player station gathers
/// resources over time, and the owner has to come and collect them.
/// <list type="bullet">
/// <item><b>Lazy, from the wall clock.</b> A store (<c>WorldMetadata.BaseYields</c>) turns the real time since its last read
/// into items whenever it is read — the core screen, a collect, the base list's aim readout — so nothing ticks while nobody
/// plays and a singleplayer world earns while the game is closed. A full store (<see cref="BaseRules.YieldCapacity"/>)
/// stops; nothing is lost.</item>
/// <item><b>Rate:</b> <see cref="BaseRules.YieldPerHour"/> — a base rate plus a little per resident (a base) or crew member
/// (a station), scaled by the world rule <c>BaseYieldPercent</c> (Off / Half / Normal / Double).</item>
/// <item><b>What:</b> a base gathers the non-rare ores of its own body, weighted by how dense the planet's veins are (the
/// same survey the planet scanner reads); a station gathers orbital salvage (<see cref="BaseRules.StationYield"/>). The
/// picks are deterministic per store.</item>
/// <item><b>Collect</b> at the core: into the backpack as far as it holds; the rest stays. Removing a core hands its rest to
/// the owner when they are online.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Body id → its base yield table (the survey is pure but not free; it never changes for a body).</summary>
    private readonly Dictionary<string, KeyValuePair<string, int>[]> _baseYieldTables = new(StringComparer.Ordinal);

    /// <summary>Wall clock used for the stores (a test can move it).</summary>
    private long YieldNowUnix => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _yieldClockShiftSeconds;

    private long _yieldClockShiftSeconds;

    private static string BaseYieldKey(ServerBase b) => "base:" + b.Planet + "|" + b.Cell.X + "|" + b.Cell.Y + "|" + b.Cell.Z;

    private static string StationYieldKey(string stationId) => "station:" + stationId;

    private BaseYieldStore YieldStore(string key)
    {
        if (!_meta.BaseYields.TryGetValue(key, out var store))
        {
            _meta.BaseYields[key] = store = new BaseYieldStore();
        }

        return store;
    }

    /// <summary>The base's store, brought up to now.</summary>
    private BaseYieldStore AccrueBaseYield(ServerBase b, out int helpers)
    {
        helpers = Math.Min(BaseResidentCount(b), BaseRules.YieldHelperCap);
        var store = YieldStore(BaseYieldKey(b));
        var table = BaseYieldTable(b.Planet);
        string seed = BaseYieldKey(b);
        BaseRules.Accrue(store, YieldNowUnix, BaseRules.YieldPerHour(helpers, Rules.BaseYieldPercent), i => BaseRules.PickWeighted(table, seed, i));
        return store;
    }

    /// <summary>Items ready at the base core (for the owner's aim readout).</summary>
    private int BaseYieldReady(ServerBase b) => AccrueBaseYield(b, out _).CountReady();

    /// <summary>The station's store, brought up to now. Its crew counts only while its interior is the active world (that is
    /// where the station core screen is read).</summary>
    private BaseYieldStore AccrueStationYield(string stationId, out int helpers)
    {
        helpers = _world.LocationId == "station:" + stationId ? Math.Min(_npcs.Count, BaseRules.YieldHelperCap) : 0;
        var store = YieldStore(StationYieldKey(stationId));
        string seed = StationYieldKey(stationId);
        BaseRules.Accrue(store, YieldNowUnix, BaseRules.YieldPerHour(helpers, Rules.BaseYieldPercent),
            i => BaseRules.PickWeighted(BaseRules.StationYield, seed, i));
        return store;
    }

    /// <summary>What a base on <paramref name="body"/> gathers: the body's non-rare ore veins, weighted by density.</summary>
    private KeyValuePair<string, int>[] BaseYieldTable(string body)
    {
        if (_baseYieldTables.TryGetValue(body, out var cached))
        {
            return cached;
        }

        var table = new List<KeyValuePair<string, int>>();
        var celestial = _galaxy?.FindBody(body);
        var planet = celestial is null || string.IsNullOrEmpty(celestial.PlanetType) ? null : _content.GetPlanet(celestial.PlanetType!);
        if (celestial != null && planet != null && !planet.Void)
        {
            bool airlessMoon = celestial.Kind == Shared.World.CelestialKind.Moon
                               && string.Equals(planet.Atmosphere, "none", StringComparison.OrdinalIgnoreCase);
            var survey = _generator.SurveyResources(planet, celestial.Id, FrontierOreBoostFor(FrontierTierForBody(celestial.Id)), airlessMoon);
            foreach (var vein in survey.Veins.Where(v => !v.RareTier))
            {
                string? item = _content.GetBlock(vein.Block)?.Drops.FirstOrDefault()?.Item;
                if (item != null && _content.GetItem(item) != null)
                {
                    table.Add(new KeyValuePair<string, int>(item, (int)Math.Max(1, Math.Round(vein.Density * 100))));
                }
            }
        }

        if (table.Count == 0)
        {
            table.Add(new KeyValuePair<string, int>(BaseRules.BaseYieldFallbackItem, 1));
        }

        var result = table.ToArray();
        _baseYieldTables[body] = result;
        return result;
    }

    private void HandleYield(PlayerSession session, YieldIntent intent)
    {
        bool collect = intent.Action == YieldIntent.ActionCollect;
        var status = intent.Kind == YieldIntent.KindStation
            ? StationYieldAction(session, intent.StationId ?? string.Empty, collect)
            : BaseYieldAction(session, intent.BodyId, new Vector3i(intent.CellX, intent.CellY, intent.CellZ), collect);
        Send(session, status);
    }

    private YieldStatus BaseYieldAction(PlayerSession session, string? bodyId, Vector3i cell, bool collect)
    {
        string body = string.IsNullOrEmpty(bodyId) ? session.CurrentLocationId : bodyId!;
        var reply = new YieldStatus { Kind = YieldIntent.KindBase, BodyId = body, CellX = cell.X, CellY = cell.Y, CellZ = cell.Z };
        var b = FindOwnBase(session.State.PlayerId, body, true, cell);
        if (b is null)
        {
            return reply; // not theirs (or gone): Capacity 0 — the screen shows nothing
        }

        var store = AccrueBaseYield(b, out int helpers);
        if (collect)
        {
            if (b.Planet != session.CurrentLocationId || !WithinReach(session.State, b.Cell))
            {
                Reject(session, "yield", "@srv.yield.too_far");
            }
            else
            {
                reply.Collected = CollectYield(session, store);
                SendBases(session); // the aim readout's count
            }
        }

        FillYieldStatus(reply, store, helpers);
        return reply;
    }

    private YieldStatus StationYieldAction(PlayerSession session, string stationId, bool collect)
    {
        var reply = new YieldStatus { Kind = YieldIntent.KindStation, StationId = stationId };
        if (!_playerStationCells.TryGetValue(stationId, out var station) || station.OwnerId != session.State.PlayerId || !station.Boardable)
        {
            return reply;
        }

        var store = AccrueStationYield(stationId, out int helpers);
        if (collect)
        {
            if (session.CurrentLocationId != "station:" + stationId)
            {
                Reject(session, "yield", "@srv.yield.too_far");
            }
            else
            {
                reply.Collected = CollectYield(session, store);
            }
        }

        FillYieldStatus(reply, store, helpers);
        return reply;
    }

    private void FillYieldStatus(YieldStatus reply, BaseYieldStore store, int helpers)
    {
        reply.Items = store.Items.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new NetYieldItem { Item = kv.Key, Count = kv.Value }).ToArray();
        reply.Ready = store.CountReady();
        reply.Capacity = BaseRules.YieldCapacity;
        reply.Helpers = helpers;
        reply.PerHour = (float)BaseRules.YieldPerHour(helpers, Rules.BaseYieldPercent);
    }

    /// <summary>Moves what lies ready into the backpack as far as it holds; returns how many items moved.</summary>
    private int CollectYield(PlayerSession session, BaseYieldStore store)
    {
        var inv = session.State.Inventory;
        int moved = 0;
        foreach (var item in store.Items.Keys.ToList())
        {
            int count = store.Items[item];
            int left = inv.Add(item, count, _content.MaxStackOf(item));
            moved += count - left;
            if (left > 0)
            {
                store.Items[item] = left;
            }
            else
            {
                store.Items.Remove(item);
            }
        }

        if (moved > 0)
        {
            SendInventory(session);
            _repo.SaveMetadata(_meta); // a collected store must not refill from an older save after a crash
            Send(session, new ServerMessage { Text = "@srv.yield.collected:" + moved });
        }

        string note = store.CountReady() > 0 ? "@srv.yield.backpack_full" : moved == 0 ? "@srv.yield.empty" : string.Empty;
        if (note.Length > 0)
        {
            Send(session, new ServerMessage { Text = note });
        }

        return moved;
    }

    /// <summary>A base core was removed: its owner takes what lay ready (when online) into the backpack, and whatever does not
    /// fit — or everything, with the owner away — falls to the ground at the core as a drop packet. Nothing is lost.</summary>
    private void HandOverBaseYield(ServerBase b)
    {
        string key = BaseYieldKey(b);
        if (!_meta.BaseYields.TryGetValue(key, out var store))
        {
            return;
        }

        AccrueBaseYield(b, out _);
        var owner = FindSessionByPlayerId(b.OwnerId) is { Joined: true } s && s.CurrentLocationId == b.Planet ? s : null;
        var rest = new List<ItemAmount>();
        foreach (var kv in store.Items.Where(kv => kv.Value > 0))
        {
            int left = owner is null ? kv.Value : owner.State.Inventory.Add(kv.Key, kv.Value, _content.MaxStackOf(kv.Key));
            if (left > 0)
            {
                rest.Add(new ItemAmount(kv.Key, left));
            }
        }

        if (owner != null)
        {
            SendInventory(owner);
        }

        SpillToGround(new Vector3i(b.Cell.X, b.Cell.Y + 1, b.Cell.Z), rest);
        _meta.BaseYields.Remove(key);
    }

    /// <summary>Brings every store up to now before the world rule changes, so the old rate pays the time it ran for and the new
    /// one only what comes after.</summary>
    private void AccrueAllYieldStores()
    {
        foreach (var b in _bases)
        {
            AccrueBaseYield(b, out _);
        }

        foreach (var s in _playerStationCells.Values.Where(s => s.Boardable && !string.IsNullOrEmpty(s.OwnerId)))
        {
            AccrueStationYield(s.Id, out _);
        }
    }

    /// <summary>Test seam: a world-rules edit as the client would send it (the admin gate applies).</summary>
    public void SetWorldRulesForTest(PlayerSession session, SetWorldRulesIntent intent) => HandleSetWorldRules(session, intent);

    /// <summary>Test seam: moves the yield clock forward.</summary>
    public void ShiftYieldClockForTest(long seconds) => _yieldClockShiftSeconds += seconds;

    /// <summary>Test seam: a yield intent as the client would send it.</summary>
    public void YieldForTest(PlayerSession session, YieldIntent intent) => HandleYield(session, intent);

    /// <summary>Test seam: the status the server would answer for a base core of the player (no collect).</summary>
    public YieldStatus BaseYieldStatusForTest(PlayerSession session, Vector3i cell)
    {
        Serve(session);
        return BaseYieldAction(session, session.CurrentLocationId, cell, collect: false);
    }
}
