// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Named teleport targets for <c>/tp</c> — "take me to the village" instead of "take me to 812 / 71 / -1904".
///
/// <para>This is deliberately the <b>same-body</b> half of the problem. Cross-body jumping stays
/// <c>/goto</c>, which is fleet-admin only because it reaches into worlds other people own (issue #487).
/// Naming a destination on the body you are already standing on grants no reach the world admin did not
/// already have with <c>/tp X Y Z</c>, so this lives under the ordinary <c>CheatsAllowed</c> gate next to
/// the coordinate teleport it extends.</para>
///
/// <para>Targets are addressed by <b>kind + number</b> (<c>village</c>, <c>village2</c>, <c>pad3</c>), never
/// by name: settlement names are procedural, easy to mistype and sometimes duplicated, whereas the numbering
/// is stable for a world because it follows the order the structures were stamped in. <c>/tp</c> with no
/// argument prints the numbered list.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far up a column the target search climbs looking for two clear blocks before giving up.
    /// Sized for the tallest stamped structure (settlement halls, monument arcades) plus headroom.</summary>
    private const int TeleportClearProbeHeight = 48;

    /// <summary>Chat is a scrollback, not a table: cap the listing so a world full of loot chests cannot
    /// scroll the admin's own screen into uselessness (same reasoning as <c>/builds</c>).</summary>
    private const int TeleportListMaxLines = 24;

    /// <summary>One resolvable destination on the current body. <paramref name="Number"/> is 1-based within
    /// its kind, so the wire form is <c>{Kind}{Number}</c>.</summary>
    private readonly record struct TeleportTarget(string Kind, int Number, string Label, Vector3f Position);

    /// <summary>Target words in the order they are offered to the admin, worldgen first, player-built last.</summary>
    private static readonly string[] TeleportKinds =
    {
        "ship", "pad", "village", "ruin", "vault", "wreck", "factory", "camp", "monument", "treasure",
        "base", "beacon", "beam", "station",
    };

    /// <summary>Accepted spellings of a target word. Plurals and the map's own vocabulary are folded in so
    /// "what it is called on the map" and "what you type" never have to be looked up.</summary>
    private static string NormalizeTeleportKind(string word) => word switch
    {
        "ships" => "ship",
        "pads" or "landing" or "landingpad" => "pad",
        "settlement" or "settlements" or "villages" or "town" or "towns" => "village",
        "ruins" or "settlement_ruin" => "ruin",
        "vaults" or "vault_ruin" => "vault",
        "wrecks" => "wreck",
        "factories" => "factory",
        "camps" or "bandit" or "bandits" or "banditcamp" => "camp",
        "monuments" => "monument",
        "chest" or "chests" or "treasures" => "treasure",
        "bases" => "base",
        "beacons" => "beacon",
        "beams" => "beam",
        "stations" => "station",
        _ => word,
    };

    /// <summary>Everything on the player's current body that <c>/tp</c> can resolve, in listing order.</summary>
    private List<TeleportTarget> TeleportTargets(PlayerSession session)
    {
        var list = new List<TeleportTarget>();
        bool de = De(session);

        // The player's own parked ship. Per-player, not per-world: everyone has their own ship at their own
        // pad, so this resolves against the session rather than the world's structure lists.
        var landed = _worlds.Active.LandedFor(session.State.PlayerId);
        if (landed.Placed)
        {
            list.Add(new TeleportTarget("ship", 1, de ? "dein Schiff" : "your ship", landed.HealTank));
        }

        for (int i = 0; i < _landingPads.Count; i++)
        {
            var pad = _landingPads[i];
            list.Add(new TeleportTarget("pad", i + 1, $"pad{i + 1}",
                new Vector3f(pad.CenterX + 0.5f, pad.CenterY + 2f, pad.CenterZ + 0.5f)));
        }

        // Settlements split into two numbering series, because "village" and "ruin" are different places to
        // an admin even though the generator treats them as one list with a flag.
        int villages = 0, ruins = 0;
        foreach (var s in _settlements)
        {
            string kind = s.Ruined ? "ruin" : "village";
            int number = s.Ruined ? ++ruins : ++villages;
            list.Add(new TeleportTarget(kind, number, Named(kind, number, s.Name),
                InteriorSpot(s.Markers, s.Min, s.Max)));
        }

        for (int i = 0; i < _vaultEntrances.Count; i++)
        {
            var e = _vaultEntrances[i];
            list.Add(new TeleportTarget("vault", i + 1, $"vault{i + 1}", SurfaceSpot(e.X, e.Z)));
        }

        if (_wreckStamped && !_wreckClaimed)
        {
            // No reveal check here, unlike the map POI: an admin looking for the wreck is doing support, not
            // playing the discovery content.
            var (wx, wz) = WreckPoiCenter();
            list.Add(new TeleportTarget("wreck", 1, Named("wreck", 1, _wreckName),
                SurfaceSpot((int)MathF.Floor(wx), (int)MathF.Floor(wz))));
        }

        for (int i = 0; i < _factories.Count; i++)
        {
            // The production terminal is the reason to go there, and it is a standing spot by construction.
            list.Add(new TeleportTarget("factory", i + 1, Named("factory", i + 1, _factories[i].Name),
                _factories[i].TerminalPos));
        }

        for (int i = 0; i < _banditCamps.Count; i++)
        {
            var c = _banditCamps[i];
            list.Add(new TeleportTarget("camp", i + 1, $"camp{i + 1}", InteriorSpot(c.Markers, c.Min, c.Max)));
        }

        for (int i = 0; i < _monuments.Count; i++)
        {
            var m = _monuments[i];
            list.Add(new TeleportTarget("monument", i + 1, Named("monument", i + 1, m.Archetype),
                SurfaceSpot((int)MathF.Floor(m.Center.X), (int)MathF.Floor(m.Center.Z))));
        }

        int chests = 0;
        foreach (var cont in _containers)
        {
            if (cont.Id.StartsWith(ChestContainerIdPrefix, StringComparison.Ordinal))
            {
                chests++;
                list.Add(new TeleportTarget("treasure", chests, $"treasure{chests}",
                    new Vector3f(cont.Position.X + 0.5f, cont.Position.Y + 1f, cont.Position.Z + 0.5f)));
            }
        }

        // Player-built structures, restricted to this body — the cross-body form of this lookup is /goto.
        var byKind = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var b in AllBuilds().Where(b => b.Body == session.CurrentLocationId))
        {
            int number = byKind[b.Kind] = byKind.GetValueOrDefault(b.Kind) + 1;
            list.Add(new TeleportTarget(b.Kind, number, Named(b.Kind, number, b.Name),
                new Vector3f(b.Cell.X + 0.5f, b.Cell.Y + 2f, b.Cell.Z + 0.5f)));
        }

        return list;
    }

    /// <summary>"village2" on its own, or "village2 'Kelmar'" when the thing has a name worth showing.</summary>
    private static string Named(string kind, int number, string name)
        => string.IsNullOrWhiteSpace(name) ? $"{kind}{number}" : $"{kind}{number} '{name}'";

    /// <summary>A standing spot inside a stamped structure: its first interaction marker (a vendor/NPC spawn
    /// is standable by construction) or, for a structure without markers, the clear column over its centre.</summary>
    private Vector3f InteriorSpot(List<(string Type, Vector3f Pos)> markers, Vector3i min, Vector3i max)
        => markers.Count > 0
            ? markers[0].Pos
            : SurfaceSpot((min.X + max.X) / 2, (min.Z + max.Z) / 2);

    /// <summary>A standing spot on the column at x/z. Climbs out of whatever is stamped over the terrain until
    /// it finds two clear blocks, so jumping to a settlement centre lands on the roof rather than inside a
    /// wall — <see cref="WorldGeneration.IWorldGenerator.SurfaceHeight"/> knows the terrain, not the buildings
    /// standing on it.</summary>
    private Vector3f SurfaceSpot(int x, int z)
    {
        int y = _generator.SurfaceHeight(_world.Planet, x, z) + 1;
        for (int i = 0; i < TeleportClearProbeHeight; i++, y++)
        {
            if (_world.GetBlock(new Vector3i(x, y, z)).IsAir && _world.GetBlock(new Vector3i(x, y + 1, z)).IsAir)
            {
                break;
            }
        }

        return new Vector3f(x + 0.5f, y, z + 0.5f);
    }

    /// <summary><c>/tp &lt;kind&gt;[n]</c> — the named half of the admin teleport. Empty argument lists what is
    /// resolvable here; anything else is a kind, optionally suffixed or followed by a 1-based number.</summary>
    private void AdminTeleportNamed(PlayerSession session, string? argument)
    {
        bool de = De(session);
        var p = session.State;

        // Every list below reads the world + ship cursors, so point them at this player. The network dispatch
        // has already done it; the test seam and any future non-socket caller have not.
        Serve(session);

        // A named target is a place on a planet surface; while flying a space instance there is nothing to
        // resolve against (and the snap channel would fight the flight scene).
        if (InSpace(p.PlayerId))
        {
            Reject(session, "admin", de
                ? "Im Raumflug gibt es keine Ziele auf der Oberfläche — erst landen."
                : "No surface targets while flying in space — land first.");
            return;
        }

        var targets = TeleportTargets(session);
        string arg = (argument ?? string.Empty).Trim();
        if (arg.Length == 0 || arg.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            ListTeleportTargets(session, targets);
            return;
        }

        var (kind, number) = ParseTeleportTarget(arg);
        if (!TeleportKinds.Contains(kind))
        {
            Reject(session, "admin", de
                ? $"Unbekanntes Ziel '{kind}'. Mögliche Ziele mit /tp auflisten."
                : $"Unknown target '{kind}'. List what is here with /tp.");
            return;
        }

        int available = targets.Count(t => t.Kind == kind);
        if (available == 0)
        {
            Reject(session, "admin", de
                ? $"Auf diesem Himmelskörper gibt es kein '{kind}'."
                : $"There is no '{kind}' on this body.");
            return;
        }

        if (number < 1 || number > available)
        {
            Reject(session, "admin", de
                ? $"Nur {available}× '{kind}' hier — {kind}1 bis {kind}{available}."
                : $"Only {available} '{kind}' here — {kind}1 to {kind}{available}.");
            return;
        }

        var hit = targets.First(t => t.Kind == kind && t.Number == number);
        SnapPlayerTo(session, hit.Position, de ? $"Teleportiert zu {hit.Label}." : $"Teleported to {hit.Label}.");
        UpdateAboard(session); // landing inside the ship must flip the aboard state now, not on the next move
        CheatLog(p, $"teleported to {hit.Label}");
    }

    /// <summary>Splits "village2", "village 2" and "village" into a kind and a 1-based number (default 1).</summary>
    private static (string Kind, int Number) ParseTeleportTarget(string argument)
    {
        var parts = argument.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string word = parts[0].ToLowerInvariant();
        int number = 0;

        // Trailing-digit form ("village2"). Split it off before the alias lookup so plurals still normalize.
        int cut = word.Length;
        while (cut > 0 && char.IsDigit(word[cut - 1]))
        {
            cut--;
        }

        if (cut > 0 && cut < word.Length)
        {
            int.TryParse(word.Substring(cut), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
            word = word.Substring(0, cut);
        }

        // Separate-token form ("village 2") — only consulted when the word carried no suffix.
        if (number == 0 && parts.Length >= 2)
        {
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
        }

        return (NormalizeTeleportKind(word), number <= 0 ? 1 : number);
    }

    /// <summary>Prints every resolvable target on this body with the exact word to type and its distance, so
    /// the numbering is discoverable instead of guessed at.</summary>
    private void ListTeleportTargets(PlayerSession session, List<TeleportTarget> targets)
    {
        bool de = De(session);
        if (targets.Count == 0)
        {
            Send(session, new ServerMessage
            {
                Text = de
                    ? "Hier gibt es keine benannten Ziele — /tp X Y Z benutzen."
                    : "No named targets on this body — use /tp X Y Z.",
            });
            return;
        }

        Send(session, new ServerMessage
        {
            Text = de ? $"— Teleportziele ({targets.Count}) —" : $"— Teleport targets ({targets.Count}) —",
        });

        var here = session.State.Position;
        foreach (var t in targets.Take(TeleportListMaxLines))
        {
            float dx = t.Position.X - here.X, dz = t.Position.Z - here.Z;
            Send(session, new ServerMessage
            {
                Text = $"/tp {t.Kind}{t.Number} · {t.Label} · {MathF.Sqrt((dx * dx) + (dz * dz)):0} m",
            });
        }

        if (targets.Count > TeleportListMaxLines)
        {
            Send(session, new ServerMessage
            {
                Text = de
                    ? $"… {targets.Count - TeleportListMaxLines} weitere."
                    : $"… {targets.Count - TeleportListMaxLines} more.",
            });
        }
    }

    /// <summary>Moves the served player to a position on the body they are already on. Every server-side
    /// teleport has to ride the <see cref="RespawnNotice"/> snap channel: a plain <c>PlayerStateUpdate</c>
    /// position is discarded by the client and then reverted by its own move stream (#414 M7/N17).</summary>
    private void SnapPlayerTo(PlayerSession session, Vector3f target, string reason)
    {
        session.State.Position = target;
        Send(session, new RespawnNotice { X = target.X, Y = target.Y, Z = target.Z, Reason = reason });
        SendPlayerState(session);
    }

    /// <summary>Test seam: the resolvable targets on the player's current body, as <c>/tp</c> would list them.</summary>
    public IReadOnlyList<(string Kind, int Number, string Label, Vector3f Position)> TeleportTargetsForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not { } s)
        {
            return new List<(string, int, string, Vector3f)>();
        }

        Serve(s);
        return TeleportTargets(s).Select(t => (t.Kind, t.Number, t.Label, t.Position)).ToList();
    }
}
