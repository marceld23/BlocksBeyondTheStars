// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The world mode by chat command (#1927, "switch between Explorer, Creative and Sandbox with chat commands"). The three
/// modes of the new-world screen used to be creation-only launch flags baked into the save — Explorer = Survival;
/// Creative = Survival + free flight + all blueprints, all ships and the creative kit; Sandbox = the Creative game mode
/// (free crafting, no oxygen or hunger, no planet enemies) + all of that. <c>/gamemode explorer|creative|sandbox</c> sets
/// the same switches on a running world: saved with it (rules override + metadata), sent to everyone at once. Going
/// back to Explorer grants nothing new and takes nothing back. World management like the per-player <c>/mode</c>
/// (#1121), so the admin role is the gate, not the cheats option.
/// </summary>
public sealed partial class GameServer
{
    internal const string WorldModeExplorer = "explorer";
    internal const string WorldModeCreative = "creative";
    internal const string WorldModeSandbox = "sandbox";

    /// <summary>A typed mode word — English as on the new-world screen, the German words, and "survival" for Explorer.</summary>
    internal static string? WorldModeFromWord(string? word) => (word ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "explorer" or "entdecker" or "survival" or "überleben" or "ueberleben" => WorldModeExplorer,
        "creative" or "kreativ" => WorldModeCreative,
        "sandbox" or "sandkasten" => WorldModeSandbox,
        _ => null,
    };

    /// <summary>The world's mode in those words, read back from the switches it sets.</summary>
    private string CurrentWorldMode()
        => Rules.GameMode == GameMode.Creative ? WorldModeSandbox
            : Rules.CreativeFlight || _meta.CreativeUnlockAllBlueprints || _meta.CreativeStartAllShips || _meta.CreativeStarterKit
                ? WorldModeCreative
                : WorldModeExplorer;

    /// <summary>The mode's name as the new-world screen shows it, in the reader's language.</summary>
    private string WorldModeName(string locale, string mode) => Localize(locale, "ui.save.mode_" + mode);

    /// <summary><c>/gamemode [explorer|creative|sandbox]</c>: without a word it names the current mode.</summary>
    private void AdminSetWorldMode(PlayerSession session, string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            Send(session, new ServerMessage
            {
                Text = Localize(session.Locale, "srv.worldmode.current").Replace("{mode}", WorldModeName(session.Locale, CurrentWorldMode())),
            });
            return;
        }

        if (WorldModeFromWord(argument) is not { } mode)
        {
            Reject(session, "admin", "@srv.worldmode.usage");
            return;
        }

        bool downgrade = mode == WorldModeExplorer && CurrentWorldMode() != WorldModeExplorer;
        ApplyWorldMode(mode);
        _meta.RulesOverride = Rules.Clone(); // the world owns its rules — the next launch reads them from the save
        _repo.SaveMetadata(_meta);

        foreach (var s in _sessions.Values)
        {
            if (!s.Joined)
            {
                continue;
            }

            SendRules(s);
            Serve(s); // the ship/world cursors belong to this player for the grants and the cargo dump
            ApplyCreativeGrants(s); // Creative/Sandbox: everyone online gets the unlocks now, not on their next join
            SendInventory(s);
            SendOwnedShips(s);
            SendPlayerState(s); // CanFly follows the mode
            Send(s, new ServerMessage
            {
                Text = Localize(s.Locale, "srv.worldmode.set")
                    .Replace("{player}", session.State.Name)
                    .Replace("{mode}", WorldModeName(s.Locale, mode)),
            });
        }

        Serve(session);
        if (downgrade)
        {
            Send(session, new ServerMessage { Text = Localize(session.Locale, "srv.worldmode.kept") });
        }

        CheatLog(session.State, $"set the world mode to {mode}");
    }

    /// <summary>The switches behind a mode — exactly what the new-world screen passes at creation.</summary>
    private void ApplyWorldMode(string mode)
    {
        bool extras = mode != WorldModeExplorer;
        Rules.GameMode = mode == WorldModeSandbox ? GameMode.Creative : GameMode.Survival;
        Rules.CreativeFlight = extras;
        _meta.CreativeUnlockAllBlueprints = extras;
        _meta.CreativeStartAllShips = extras;
        _meta.CreativeStarterKit = extras; // granted once per world (CreativeKitGranted) however often the mode flips
    }

    /// <summary>Test seam: the world's current mode word.</summary>
    public string WorldModeForTest() => CurrentWorldMode();
}
