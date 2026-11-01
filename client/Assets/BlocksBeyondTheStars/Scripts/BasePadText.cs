// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3070: how the views word and colour a player-built base landing pad — its name (or "Pad n" in its base), its state
    /// for this player (free, your ship, someone's ship, locked) and who may land there. One place, so the land chooser,
    /// the planet map, the pad panel and the HUD prompt say the same thing. The pads themselves come from the server's
    /// pad list (<see cref="BasePadView"/> sorts and groups them).
    /// </summary>
    internal static class BasePadText
    {
        /// <summary>The base colours of the planet map (#2997): teal = own, green = an ally's, amber = someone else's.</summary>
        public static readonly Color OwnCol = new Color(0.36f, 0.82f, 0.86f);
        public static readonly Color AllyCol = new Color(0.45f, 0.9f, 0.5f);
        public static readonly Color OtherCol = new Color(1f, 0.72f, 0.3f);

        /// <summary>A pad this player may not land on.</summary>
        public static readonly Color LockedCol = new Color(0.58f, 0.6f, 0.64f);

        /// <summary>The colour of a base's relation to this player.</summary>
        public static Color RelationColor(byte relation)
            => relation == NetBase.RelationOwn ? OwnCol : relation == NetBase.RelationAlly ? AllyCol : OtherCol;

        /// <summary>A pad marker's colour, like the communal pads': cyan = your ship's pad, red = taken, green = free —
        /// and grey when the access shuts this player out.</summary>
        public static Color MarkerColor(NetLandingPad pad)
            => pad.Mine ? new Color(0.45f, 0.85f, 1f)
                : pad.Locked ? LockedCol
                : pad.Occupied ? new Color(1f, 0.45f, 0.4f)
                : new Color(0.5f, 0.9f, 0.6f);

        /// <summary>The pad's name, or "Pad n" for an unnamed one — <paramref name="number"/> is its 1-based place among its
        /// base's pads (<see cref="BasePadView.GroupByBase"/> order).</summary>
        public static string Name(NetLandingPad pad, int number, System.Func<string, string> loc)
            => !string.IsNullOrEmpty(pad.Name) ? pad.Name : loc("ui.basepad.unnamed").Replace("{n}", number.ToString());

        /// <summary>The pad's name, its number looked up among its base's pads in <paramref name="pads"/> (only when unnamed —
        /// the HUD asks every frame).</summary>
        public static string Name(NetLandingPad pad, NetLandingPad[] pads, System.Func<string, string> loc)
            => !string.IsNullOrEmpty(pad.Name) ? pad.Name : Name(pad, NumberInBase(pad, pads), loc);

        /// <summary>The 1-based place of <paramref name="pad"/> among its base's pads in the list (1 when not found).</summary>
        public static int NumberInBase(NetLandingPad pad, NetLandingPad[] pads)
        {
            foreach (var g in BasePadView.GroupByBase(pads))
            {
                int i = g.Pads.IndexOf(pad);
                if (i >= 0)
                {
                    return i + 1;
                }
            }

            return 1;
        }

        /// <summary>The base's name, or the default base word.</summary>
        public static string BaseName(string name, System.Func<string, string> loc)
            => string.IsNullOrEmpty(name) ? loc("ui.base.default") : name;

        /// <summary>The pad's state for this player: your ship is parked here, locked for you, someone's ship, or free.</summary>
        public static string State(NetLandingPad pad, System.Func<string, string> loc)
        {
            if (pad.Mine)
            {
                return loc("ui.basepad.state_mine");
            }

            if (pad.Locked)
            {
                return loc("ui.basepad.state_locked");
            }

            if (pad.Occupied)
            {
                return string.IsNullOrEmpty(pad.Occupant)
                    ? loc("ui.basepad.state_taken_anon")
                    : loc("ui.basepad.state_taken").Replace("{name}", pad.Occupant);
            }

            return loc("ui.basepad.state_free");
        }

        /// <summary>Who may land, as a reader sees it ("the owner and allies").</summary>
        public static string Access(int access, System.Func<string, string> loc) => BasePadRules.AccessOf(access) switch
        {
            BasePadAccess.Owner => loc("ui.basepad.access_owner"),
            BasePadAccess.Everyone => loc("ui.basepad.access_everyone"),
            _ => loc("ui.basepad.access_allies"),
        };

        /// <summary>The owner's toggle label for an access ("Only me", "Me + allies", "Everyone").</summary>
        public static string AccessChoice(BasePadAccess access, System.Func<string, string> loc) => access switch
        {
            BasePadAccess.Owner => loc("ui.basepad.choice_owner"),
            BasePadAccess.Everyone => loc("ui.basepad.choice_everyone"),
            _ => loc("ui.basepad.choice_allies"),
        };

        /// <summary>The base pad at a scene cell of the current world (the crosshair's), or null. The list keeps canonical
        /// cells and the scene runs on past both seams, so the cell is wrapped first (#2613).</summary>
        public static NetLandingPad FindAt(GameBootstrap game, Vector3Int cell)
        {
            if (game == null)
            {
                return null;
            }

            int circ = game.Circumference;
            return BasePadView.Find(game.CurrentBodyPads, WorldConstants.WrapX(cell.x, circ), cell.y, WorldConstants.WrapZ(cell.z, circ));
        }
    }
}
