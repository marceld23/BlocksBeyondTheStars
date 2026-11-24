// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3970: the admin's <c>/sol</c> (<c>/system &lt;id&gt;</c>, verb <c>reveal_system</c>) — a fixed star system of
/// <c>data/systems/</c> joins the galaxy now, whatever its rule says (a play test does not wait for six growths). It is
/// pinned like any other reveal, so it stays after a restart. A cheat: the world's cheat rule is the gate.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The system <c>/sol</c> names when no id is given.</summary>
    internal const string DefaultRevealSystemId = "sol";

    /// <summary>Appends the named data system now (default <see cref="DefaultRevealSystemId"/>) and tells the admin.</summary>
    private void AdminRevealSystem(PlayerSession session, string? systemId)
    {
        string id = string.IsNullOrWhiteSpace(systemId) ? DefaultRevealSystemId : systemId!.Trim().ToLowerInvariant();
        if (DataFixedSystems().FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal)) is not { } def)
        {
            Reject(session, "admin", "@srv.admin.system_unknown:" + id);
            return;
        }

        bool already = _galaxy.Systems.Any(s => s.Id == def.Id);
        if (!already)
        {
            RevealFixedSystem(def);
        }

        Send(session, new ServerMessage
        {
            Text = (already ? "@srv.admin.system_already:" : "@srv.admin.system_revealed:") + FixedSystemName(def, session.Locale),
        });
        CheatLog(session.State, $"revealed the fixed system {def.Id}");
    }
}
