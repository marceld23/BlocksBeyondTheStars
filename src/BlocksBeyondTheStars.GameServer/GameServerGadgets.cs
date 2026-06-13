using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Right-click gadgets (item 36): the <b>field medkit</b> (heal yourself + nearby allies), the <b>stasis
/// projector</b> (briefly freeze creatures so they can be scanned safely) and the <b>terrain blaster</b>
/// (clear a sphere of terrain — no loot). All are reusable tools gated behind a blueprint, costing suit
/// energy with a short cooldown. The effect is keyed by the item id so one intent drives all three.
/// </summary>
public sealed partial class GameServer
{
    // Uses the existing monotonic _uptime clock (GameServerBump.SampleHistories increments it once per tick).
    private readonly Dictionary<string, double> _gadgetReadyAt = new(); // "playerId|gadget" -> uptime usable again

    // --- balance: field medkit ---
    private const float MedkitHealAmount = 45f; // HP restored to the user + each nearby ally
    private const float MedkitRadius = 6f;       // ally heal radius (blocks)
    private const double MedkitCooldown = 4.0;   // seconds between uses

    // --- balance: stasis projector ---
    private const float StasisRadius = 7f;       // creatures within this of the aim point are frozen
    private const double StasisDuration = 6.0;   // seconds a creature stays in stasis (scan window)
    private const double StasisCooldown = 6.0;

    // --- balance: terrain blaster ---
    private const int BlasterRadius = 3;         // sphere radius (blocks) — a sizeable crater (~120 blocks)
    private const double BlasterCooldown = 3.0;

    // --- balance: terrain scanner (Feature 40) ---
    private const int ScannerRadius = 20;        // pulse radius (blocks) around the player
    private const int ScannerMaxHits = 80;       // nearest hits sent (bounds the message on ore-rich worlds)
    private const float ScannerSeconds = 8f;     // how long the client shows the glow markers
    private const double ScannerCooldown = 10.0;

    private void HandleUseGadget(PlayerSession session, UseGadgetIntent intent)
    {
        var p = session.State;
        var item = _content.GetItem(intent.GadgetKey);
        if (item?.Tool is null || item.Tool.Kind != ToolKind.Gadget)
        {
            Reject(session, "gadget", "Not a usable gadget.");
            return;
        }

        if (!p.Inventory.Has(intent.GadgetKey, 1))
        {
            Reject(session, "gadget", "You don't have that gadget.");
            return;
        }

        string cdKey = p.PlayerId + "|" + intent.GadgetKey;
        if (_gadgetReadyAt.TryGetValue(cdKey, out var readyAt) && _uptime < readyAt)
        {
            return; // still cooling down (the client also rate-limits) — ignore quietly
        }

        if (p.SuitEnergy < item.Tool.EnergyPerUse)
        {
            Reject(session, "gadget", "Not enough suit energy.");
            return;
        }

        var target = new Vector3f(intent.X, intent.Y, intent.Z);
        double cooldown;
        switch (intent.GadgetKey)
        {
            case "field_medkit":
                UseFieldMedkit(session);
                cooldown = MedkitCooldown;
                break;
            case "stasis_projector":
                UseStasisProjector(target);
                cooldown = StasisCooldown;
                break;
            case "terrain_blaster":
                UseTerrainBlaster(session, target);
                cooldown = BlasterCooldown;
                break;
            case "terrain_scanner":
                UseTerrainScanner(session);
                cooldown = ScannerCooldown;
                break;
            default:
                Reject(session, "gadget", "Unknown gadget.");
                return;
        }

        p.SuitEnergy = System.Math.Max(0f, p.SuitEnergy - item.Tool.EnergyPerUse);
        _gadgetReadyAt[cdKey] = _uptime + cooldown;
        SendPlayerState(session);
    }

    /// <summary>Heals the user and every other on-foot player within <see cref="MedkitRadius"/> in the same
    /// world (a shared first-aid pulse) — item 36.</summary>
    private void UseFieldMedkit(PlayerSession user)
    {
        var origin = user.State.Position;
        foreach (var s in JoinedInActiveWorld())
        {
            if (InSpace(s.State.PlayerId))
            {
                continue; // piloting in space, not on foot
            }

            if (WrapDistSq(origin, s.State.Position) > MedkitRadius * MedkitRadius)
            {
                continue;
            }

            var t = s.State;
            if (t.Health <= 0f)
            {
                continue; // already down — a medkit can't revive
            }

            float before = t.Health;
            t.Health = System.Math.Min(100f, t.Health + MedkitHealAmount);
            if (t.Health != before)
            {
                SendPlayerState(s);
            }
        }
    }

    /// <summary>Freezes every creature within <see cref="StasisRadius"/> of the aim point for
    /// <see cref="StasisDuration"/> seconds (item 36) — they stop moving + biting so you can scan them safely.</summary>
    private void UseStasisProjector(Vector3f target)
    {
        bool any = false;
        foreach (var c in _creatures)
        {
            if (WrapDistSq(target, c.Position) <= StasisRadius * StasisRadius)
            {
                c.FrozenTimer = System.Math.Max(c.FrozenTimer, StasisDuration); // never shorten an existing freeze
                any = true;
            }
        }

        if (any)
        {
            BroadcastCreatures(); // push the Frozen flag now so the client tints them immediately
        }
    }

    /// <summary>Test hook: seconds a creature is still frozen (0 = not frozen).</summary>
    public double CreatureFrozenForTest(string creatureId)
        => _creatures.FirstOrDefault(c => c.Id == creatureId)?.FrozenTimer ?? 0;

    /// <summary>Destroys a sphere of mineable, unprotected terrain around the aim point (item 36) — a clearing
    /// blast with <b>no loot</b> (so it can't out-mine a drill). Respects ship / settlement / station / other
    /// players' landing-zone protection and leaves indestructible blocks alone.</summary>
    private void UseTerrainBlaster(PlayerSession session, Vector3f target)
    {
        var center = WorldConstants.CanonicalBlock(new Vector3i(
            (int)System.Math.Floor(target.X), (int)System.Math.Floor(target.Y), (int)System.Math.Floor(target.Z)), _world.Circumference);

        for (int dx = -BlasterRadius; dx <= BlasterRadius; dx++)
        for (int dy = -BlasterRadius; dy <= BlasterRadius; dy++)
        for (int dz = -BlasterRadius; dz <= BlasterRadius; dz++)
        {
            if (dx * dx + dy * dy + dz * dz > BlasterRadius * BlasterRadius)
            {
                continue; // carve a sphere, not a cube
            }

            var p = WorldConstants.CanonicalBlock(new Vector3i(center.X + dx, center.Y + dy, center.Z + dz), _world.Circumference);
            var b = _world.GetBlock(p);
            if (b.IsAir || IsShipBlock(p) || IsSettlementBlock(p) || IsStationBlock(p)
                || IsBaseProtected(p, session.State.PlayerId, session.State.IsAdmin))
            {
                continue;
            }

            var d = _world.Definition(b);
            if (d is null || !d.Mineable)
            {
                continue; // leave bedrock / indestructible blocks intact
            }

            _world.SetBlock(p, BlockId.Air);
            _miningProgress.Remove(p);
            BroadcastToWorld(new BlockChanged { X = p.X, Y = p.Y, Z = p.Z, Block = BlockId.AirValue });
            if (d.Key == "radio_beacon")
            {
                RemoveBeaconAt(p); // don't orphan a blasted beacon's label/marker (item 37)
            }
            if (d.Key == "base_core")
            {
                RemoveBaseAt(p); // don't orphan a blasted base's claim/marker (Grundstein)
            }
            if (IsFluid(b.Value) || HasFluidNeighbor(p))
            {
                OnFluidRemoved(p); // a hole opened in/under water or lava refills
            }
        }
    }

    /// <summary>Terrain scanner (Feature 40): scans a sphere around the player for valuable blocks (ores,
    /// crystal, data caches) and sends their positions to that player as <see cref="OreScanResult"/> — the
    /// client renders them as through-wall glow markers. Non-destructive; nearest hits win when the world is
    /// richer than the message cap.</summary>
    private void UseTerrainScanner(PlayerSession session)
        => Send(session, BuildOreScan(session.State, VegaScannerRadiusBonus(session)));

    /// <summary>Test seam: the scan result the terrain scanner would send for a player right now
    /// (including any AI-core radius bonus — VEGA crunches the returns).</summary>
    public OreScanResult OreScanForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s ? BuildOreScan(s.State, VegaScannerRadiusBonus(s)) : new OreScanResult();

    private OreScanResult BuildOreScan(BlocksBeyondTheStars.Shared.State.PlayerState state, int radiusBonus = 0)
    {
        int radius = ScannerRadius + radiusBonus;
        var p = state.Position;
        var centre = WorldConstants.CanonicalBlock(new Vector3i(
            (int)System.Math.Floor(p.X), (int)System.Math.Floor(p.Y), (int)System.Math.Floor(p.Z)), _world.Circumference);

        var hits = new List<(Vector3i Pos, ushort Block, int DistSq)>();
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        for (int dz = -radius; dz <= radius; dz++)
        {
            int distSq = dx * dx + dy * dy + dz * dz;
            if (distSq > radius * radius)
            {
                continue; // a pulse sphere, not a cube
            }

            var cell = WorldConstants.CanonicalBlock(new Vector3i(centre.X + dx, centre.Y + dy, centre.Z + dz), _world.Circumference);
            var b = _world.GetBlock(cell);
            if (b.IsAir)
            {
                continue;
            }

            if (IsValuableBlock(_world.Definition(b)?.Key))
            {
                hits.Add((cell, b.Value, distSq));
            }
        }

        // Nearest finds first; cap so an ore-rich world doesn't flood the message.
        hits.Sort((a, b2) => a.DistSq.CompareTo(b2.DistSq));
        int n = System.Math.Min(hits.Count, ScannerMaxHits);
        var result = new OreScanResult
        {
            X = new int[n], Y = new int[n], Z = new int[n], Block = new ushort[n],
            Seconds = ScannerSeconds,
        };
        for (int i = 0; i < n; i++)
        {
            result.X[i] = hits[i].Pos.X;
            result.Y[i] = hits[i].Pos.Y;
            result.Z[i] = hits[i].Pos.Z;
            result.Block[i] = hits[i].Block;
        }

        return result;
    }

    /// <summary>What the scanner counts as "valuable": every ore vein block, crystal, and data caches.</summary>
    private static bool IsValuableBlock(string? key)
        => key != null
           && (key.EndsWith("_ore", System.StringComparison.Ordinal) || key is "crystal" or "data_cache");

    /// <summary>Test hook: how many seconds until the gadget is usable again for this player (0 = ready).</summary>
    public double GadgetCooldownForTest(string playerId, string gadgetKey)
        => _gadgetReadyAt.TryGetValue(playerId + "|" + gadgetKey, out var at) ? System.Math.Max(0, at - _uptime) : 0;

    /// <summary>Test hook: run a gadget use as if the intent arrived.</summary>
    public void UseGadgetForTest(string playerId, string gadgetKey, Vector3f target)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleUseGadget(s, new UseGadgetIntent { GadgetKey = gadgetKey, X = target.X, Y = target.Y, Z = target.Z });
        }
    }
}
