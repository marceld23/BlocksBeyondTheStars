// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The one gate every hit on a player passes (#2712, "you shouldn't be able to take damage at all in Sandbox"). Sandbox —
/// the Creative game mode, the world's own or a player's per-player override (<c>/mode Player creative</c>, #1121) — is
/// damage-free: oxygen runs out nowhere (not on a spacewalk either), and suffocation, lava, fire, the gas sea, toxic water,
/// corrosive air, heat, cold, weather, falls, speeder crashes, bad food and every hostile leave the health alone. Machines
/// and bandits do not spawn in a Sandbox world, but aggressive and provoked animals do: every hostile's choice of target asks
/// <see cref="HostilesIgnore"/>, so nothing chases, bites or strikes a Sandbox player — and no hit marker or combat grace
/// follows. In space the hostiles neither hunt a Sandbox pilot nor dent their ship, and a raider never hails one
/// (<see cref="SpaceQuarry"/>); a Sandbox speeder or boat keeps its hull (<see cref="VehicleTakesDamage"/>). God mode rides
/// the same gate. Explorer and the Creative world mode (survival + free flight) keep every damage rule. A stuck player is
/// not a damage case: the void rescue and "back to my ship" still bring them home.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Whether anything may hurt this player: not in god mode and not in Sandbox (#2712).</summary>
    private bool PlayerTakesDamage(PlayerState p) => !p.GodMode && Rules.PlayerTakesDamageFor(p.ModeOverride);

    /// <summary>Hostiles — machines, bandits, aggressive or provoked animals, giants — treat this player as not there: the
    /// player's own <see cref="PlayerState.IgnoredByHostiles"/> (god mode, cloak, a stealth preparation, the per-player
    /// Creative override) or a Sandbox world (#2712), which the player state alone cannot know. Food begging keeps reading
    /// the player's own flag: a begging herd is no hostile, and a Sandbox world keeps its tame-by-feeding herds.</summary>
    private bool HostilesIgnore(PlayerState p) => p.IgnoredByHostiles || !PlayerTakesDamage(p);

    /// <summary>Takes <paramref name="amount"/> health from the player — through armour and a preparation's shield first
    /// when <paramref name="mitigated"/> — unless nothing may hurt them (<see cref="PlayerTakesDamage"/>; their armour
    /// and shield are left untouched then too). Returns the health actually lost.</summary>
    private float DamagePlayer(PlayerState p, float amount, bool mitigated = false)
    {
        if (amount <= 0f || !PlayerTakesDamage(p))
        {
            return 0f;
        }

        if (mitigated)
        {
            amount = Mitigate(p, amount);
        }

        float before = p.Health;
        p.Health = System.Math.Max(0f, p.Health - amount);
        return before - p.Health;
    }

    /// <summary>Whether a speeder or boat may be dented (#2712): not while its owner or its driver plays Sandbox (or god
    /// mode) — a crash, a bite or a leviathan never wrecks a Sandbox vehicle, so its item is never lost. A vehicle only
    /// exists while its owner is present; an owner or driver not found is no reason to spare it.</summary>
    private bool VehicleTakesDamage(ServerSpeeder s)
        => (FindSessionByPlayerId(s.OwnerId) is not { } owner || PlayerTakesDamage(owner.State))
           && (FindSessionByPlayerId(s.DriverId) is not { } driver || PlayerTakesDamage(driver.State));

    /// <summary>Whether space hostiles may hunt and hit this pilot (#2712): not a Sandbox or god-mode pilot. A pilot whose
    /// session is gone stays a quarry, exactly as before the rule.</summary>
    private bool SpaceQuarry(string pilotId)
        => FindSessionByPlayerId(pilotId) is not { } s || PlayerTakesDamage(s.State);

    /// <summary>Test seam (#2712): whether the player can be hurt right now.</summary>
    public bool PlayerTakesDamageForTest(PlayerState p) => PlayerTakesDamage(p);
}
