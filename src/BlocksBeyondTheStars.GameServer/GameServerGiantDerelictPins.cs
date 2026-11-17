// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What a giant wreck in space is pinned to (#3614). A wreck stores only the players' changes over its BASE build, and the base
/// is rebuilt on every server run — from the template, the ruin pass (<see cref="GiantShipGenerator.RuinRevision"/>), the
/// ship-mode decay (<see cref="DerelictGenerator.ShipDecayRevision"/>) and the content's block properties. So the save pins all
/// of it in <see cref="Shared.State.WorldMetadata.GiantWreckDesigns"/> — <c>key|template fingerprint|r&lt;ruin&gt;|d&lt;decay&gt;|b&lt;base
/// hash&gt;</c> (<see cref="GiantDerelictLayout.BaseHash"/>: the decayed wreck by block KEYS, dyes, shapes, doors, arrival and
/// bay) — and its flight spot in <see cref="Shared.State.WorldMetadata.GiantWreckSpots"/>, written the first time.
/// <list type="bullet">
/// <item>Anything differs and the save holds something of the wreck (its delta blob, its interior overlay, door records): it
/// <b>stays away</b> for this run — off the star map, no contact — with a warning; nothing stored is touched, and it comes back
/// as it was once the base comes out as stored again. Checked at registration for the ship and the revisions, and at the
/// build for the base hash.</item>
/// <item>Anything differs and nothing is stored (it was only seen): it is simply built anew — the ship's new version or a fresh
/// pick — and pinned (and placed) again.</item>
/// <item>A pin written before the revisions and the hash were (only ever by a development build) counts as matching in the parts
/// it lacks, and gets them at its next build.</item>
/// </list>
/// The planet wrecks pin their ruin revision with their placement record the same way (GameServerGiantWrecks.cs).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A parsed pin. A part the pin does not carry is null.</summary>
    private sealed class GiantPin
    {
        public string Key = string.Empty;
        public string Fingerprint = string.Empty;
        public int? Ruin;
        public int? Decay;
        public string? Base;

        public static GiantPin Parse(string text)
        {
            var parts = text.Split('|');
            var pin = new GiantPin { Key = parts[0], Fingerprint = parts.Length > 1 ? parts[1] : string.Empty };
            foreach (string p in parts.Skip(2))
            {
                if (p.Length > 1 && p[0] == 'r' && int.TryParse(p.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r))
                {
                    pin.Ruin = r;
                }
                else if (p.Length > 1 && p[0] == 'd' && int.TryParse(p.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int d))
                {
                    pin.Decay = d;
                }
                else if (p.Length > 1 && p[0] == 'b')
                {
                    pin.Base = p.Substring(1);
                }
            }

            return pin;
        }

        public static string Format(string key, string fingerprint, string baseHash)
            => key + "|" + fingerprint + "|r" + GiantShipGenerator.RuinRevision.ToString(CultureInfo.InvariantCulture)
               + "|d" + DerelictGenerator.ShipDecayRevision.ToString(CultureInfo.InvariantCulture) + "|b" + baseHash;

        /// <summary>Whether the template and the revisions are still what the pin says (the base hash is checked at the build).</summary>
        public bool SameRecipe(StructureTemplate? t)
            => t is not null && GiantShipGenerator.Fingerprint(t) == Fingerprint
               && (Ruin ?? GiantShipGenerator.RuinRevision) == GiantShipGenerator.RuinRevision
               && (Decay ?? DerelictGenerator.ShipDecayRevision) == DerelictGenerator.ShipDecayRevision;
    }

    /// <summary>The ship a giant wreck is: the pinned one while it and the revisions match; with nothing of the wreck stored,
    /// the pinned ship's new version or a fresh pick; null when the save holds the wreck's changes and its recipe changed
    /// (kept, with a warning), or when there is no ship at all. A fresh pick avoids the designs in <paramref name="taken"/>
    /// while another one is left (#3764: the wrecks of one world are different ships).</summary>
    private StructureTemplate? ResolveGiantDesign(string id, ICollection<string>? taken = null)
    {
        if (_meta.GiantWreckDesigns.TryGetValue(id, out var raw))
        {
            var pin = GiantPin.Parse(raw);
            var pinned = _content.GetGiantShip(pin.Key);
            if (pin.SameRecipe(pinned))
            {
                return pinned;
            }

            if (GiantWreckHasStoredState(id))
            {
                string what = pinned is null ? "its ship '" + pin.Key + "' is gone"
                    : GiantShipGenerator.Fingerprint(pinned) != pin.Fingerprint ? "its ship '" + pin.Key + "' changed"
                    : "the ruin or the decay changed";
                _log.Warn($"Giant wreck '{id}': {what} — it stays away until it comes out as stored again (its changes are kept).");
                return null;
            }

            if (pinned is not null)
            {
                return pinned; // nothing stored was made on the old build: the ship as it is now, pinned anew at the build
            }
        }

        return GiantWreckPlacer.PickDesign(GiantWreckPlacer.Untaken(GiantWreckCandidates(), taken), _meta.Seed, id);
    }

    /// <summary>The designs the save already pins for the world's giant wrecks (#3764) — a wreck built once keeps its ship,
    /// and no wreck picked afresh takes one of these while another design is free.</summary>
    private HashSet<string> PinnedGiantDesignKeys()
    {
        var keys = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (string raw in _meta.GiantWreckDesigns.Values)
        {
            keys.Add(GiantPin.Parse(raw).Key);
        }

        return keys;
    }

    /// <summary>Whether the save holds anything made on a giant wreck's build — the derived station's record, its delta blob, its
    /// interior overlay or door records (<see cref="DerivedHasStoredState"/>).</summary>
    private bool GiantWreckHasStoredState(string id) => DerivedHasStoredState(id);

    /// <summary>Whether the base just built is the one the stored changes were made on — or may replace it. The derived
    /// station's own record names that base (<see cref="DerivedStoredBaseHash"/>): nothing stored = free to rebuild; a stored
    /// hash must match. A record stored without one (before the hash was kept) falls back to the pin's hash.</summary>
    private bool GiantBaseStillMatches(GiantDerelictSite site, string baseHash)
    {
        string? stored = DerivedStoredBaseHash(site.Id);
        if (stored is null && !GiantWreckHasStoredState(site.Id))
        {
            return true; // nothing was made on any base yet
        }

        if (!string.IsNullOrEmpty(stored))
        {
            return stored == baseHash;
        }

        var pin = _meta.GiantWreckDesigns.TryGetValue(site.Id, out var raw) ? GiantPin.Parse(raw) : null;
        return pin?.Base is null || pin.Base == baseHash;
    }

    /// <summary>A wreck whose base no longer comes out as the save stored it stays away this run: off the star map, no contact,
    /// no hull; its pin, its spot and every change stored for it are kept.</summary>
    private void RetireGiantDerelict(GiantDerelictSite site)
    {
        site.Retired = true;
        site.Job = null;
        _galaxy.Systems.FirstOrDefault(s => s.Id == site.SystemId)?.Bodies.Remove(site.Body);
        _log.Warn($"Giant wreck '{site.Id}' ({site.Template.Key}): its base no longer comes out as stored (a block or the generator " +
                  "changed) — it stays away until it does again; its changes are kept.");
    }

    /// <summary>Pins the ship, the revisions and the base hash, and marks the wreck's shape
    /// (<see cref="GiantShipRules.ShapeMarkerPrefix"/>). Returns true when the base changed (a first pin, or a new one) — the
    /// flight spot is then placed anew. <paramref name="dirty"/>: the metadata needs a write.</summary>
    private bool PinGiantDesign(GiantDerelictSite site, string baseHash, out bool dirty)
    {
        string pin = GiantPin.Format(site.Template.Key, GiantShipGenerator.Fingerprint(site.Template), baseHash);
        dirty = false;
        bool rebased = false;
        if (!_meta.GiantWreckDesigns.TryGetValue(site.Id, out var had) || had != pin)
        {
            var old = had is null ? null : GiantPin.Parse(had);
            rebased = old is null || old.Key != site.Template.Key || !old.SameRecipe(site.Template) || (old.Base is { } b && b != baseHash);
            if (had is not null && rebased)
            {
                _log.Info($"Giant wreck '{site.Id}': nothing was stored on its old base — it is '{site.Template.Key}' as it comes out now.");
            }

            _meta.GiantWreckDesigns[site.Id] = pin;
            dirty = true;
        }

        string shape = GiantShipRules.ShapeMarkerPrefix + site.Id;
        if (!_meta.GeneratedLoot.Contains(shape))
        {
            _meta.GeneratedLoot.Add(shape);
            dirty = true;
        }

        return rebased;
    }

    /// <summary>The wreck's flight spot: the pinned one, unless its base changed (then — and the first time — placed by its
    /// size and pinned).</summary>
    private Vector3f GiantWreckSpot(GiantDerelictSite site, bool rebased, ref bool dirty)
    {
        if (!rebased && _meta.GiantWreckSpots.TryGetValue(site.Id, out var raw) && TryParseSpot(raw, out var pinned))
        {
            return pinned;
        }

        var spot = GiantWreckSpacePosition(site);
        _meta.GiantWreckSpots[site.Id] = FormatSpot(spot);
        dirty = true;
        return spot;
    }

    private static string FormatSpot(Vector3f p)
        => string.Join(";", p.X.ToString("R", CultureInfo.InvariantCulture), p.Y.ToString("R", CultureInfo.InvariantCulture),
            p.Z.ToString("R", CultureInfo.InvariantCulture));

    private static bool TryParseSpot(string raw, out Vector3f spot)
    {
        spot = default;
        var parts = raw.Split(';');
        if (parts.Length != 3
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            return false;
        }

        spot = new Vector3f(x, y, z);
        return true;
    }
}
