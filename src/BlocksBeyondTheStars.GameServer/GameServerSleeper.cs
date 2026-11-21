// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// THE GUARD POST AND THE SLEEPER (#3897; canon C20, C21 in docs/developer/LORE_STRUCTURE.md, the how-it-works in
/// docs/developer/SPS_TOWERS.md §9). Beside the foot of some guide towers stands a guard post with a sealed capsule tank
/// (<see cref="SpsTowerRecord.HasGuardPost"/>, <see cref="SpsTowerRecord.Tank"/>).
/// <list type="bullet">
/// <item><b>The tank</b> (<see cref="UseCapsuleTank"/>, the use handler of the block <c>capsule_tank</c>): without a hacking
/// chip a line says it is sealed. With one the use takes ONE, the tank's two lamps turn blue, both tank cells become
/// <see cref="SleeperTankOpenBlock"/>, the cue <see cref="SleeperTankSound"/> plays, <see cref="SpsTowerRecord.TankOpened"/> is
/// set, everybody within <see cref="SleeperWitnessRange"/> blocks gets <see cref="GameEventKeys.CapsuleOpened"/> and the
/// pack's cutscene <see cref="SleeperTankCutscene"/> (a line of VEGA's without it) — and the guard steps out. Once per tank:
/// an opened tank is another block, so no second chip can be spent on it.</item>
/// <item><b>The guard</b>: an NPC of the look <see cref="SleeperLook"/> (the client draws the escort wing's armour with blue
/// lights), the story character <see cref="SleeperCharacterId"/> — so the VEGA pack's dialog <see cref="SleeperDialogKey"/>
/// is his, per player, with its gift once per player. Every guard post holds a DIFFERENT guard: the first tank a save opens
/// wakes the one named by <see cref="SleeperNameKey"/>, each later one the next name of <see cref="SleeperNames"/>, kept in
/// <see cref="SpsTowerRecord.GuardName"/>. They share the character, so whichever guard a player talks to first gives the
/// full talk and the gift; the others have their everyday talk for them. An NPC cannot be attacked and a settler never
/// trades.</item>
/// <item><b>Where he lives</b>: by his tank until <see cref="SleeperLeaveSeconds"/> after his first conversation ended; then
/// — and at every later load of the world — in the nearest inhabited settlement of that world
/// (<see cref="SpsTowerRecord.GuardSettlement"/>, its name). He is not walked there: he is taken away and put down again,
/// like the Echoes. A world with no inhabited settlement keeps him at his post. NPCs are runtime state, so he is placed
/// again from the record whenever his world is loaded (<see cref="EnsureSleepersPlaced"/>).</item>
/// <item><b>Only with the VEGA story</b>: the sleeper is that story's figure. In a world that runs another story or none the
/// tank stays sealed and takes no chip (<see cref="SleeperCanWake"/>).</item>
/// </list>
/// VEGA's words about him are in GameServerSleeperStory.cs.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The story character the guard is — the VEGA pack's dialogs name it in their <c>character</c>.</summary>
    internal const string SleeperCharacterId = "sps_guard";

    /// <summary>The guard's own dialog (once per player; its last reply hands over the gift).</summary>
    internal const string SleeperDialogKey = "sps_guard";

    /// <summary>The look id the client draws the guard with.</summary>
    internal const string SleeperLook = "sps_guard";

    /// <summary>The locale key of the name of the FIRST guard a save wakes (one key, so a rename is one line). Every later
    /// guard post holds another guard, named from <see cref="SleeperNames"/>.</summary>
    internal const string SleeperNameKey = "chr.sps_guard.name";

    /// <summary>The names of the guards after the first one, in the order their tanks are opened in a save — invented,
    /// language-neutral like every NPC name, and none of the stories' or of the Sounders at the gates. A save that opens
    /// more tanks than this starts the list again.</summary>
    internal static readonly string[] SleeperNames = { "Mara", "Tobin", "Selka", "Joren", "Nima", "Halden", "Ruvi" };

    /// <summary>The nameplate key of the guard's role.</summary>
    internal const string SleeperRoleKey = "npc.role.sps_guard";

    /// <summary>The block an opened tank cell becomes.</summary>
    internal const string SleeperTankOpenBlock = "capsule_tank_open";

    /// <summary>The sound cue of the tank opening.</summary>
    internal const string SleeperTankSound = "tank_opens";

    /// <summary>The VEGA pack's cutscene of the tank opening, played for everyone near — only when the pack defines it.</summary>
    internal const string SleeperTankCutscene = "tank.opens";

    /// <summary>VEGA's line at the opening while the pack has no such scene (or a player saw it at another tank).</summary>
    internal const string SleeperTankLine = "story.vega.tank.opens";

    /// <summary>How far the opening of a tank reaches (blocks): the event, the scene, the lines.</summary>
    internal const float SleeperWitnessRange = 24f;

    /// <summary>How long after his first conversation ended the guard leaves his post for the settlement (seconds).</summary>
    internal const double SleeperLeaveSeconds = 60.0;

    /// <summary>The guard of one tower while his world is loaded (RAM only — the record is the truth).</summary>
    private sealed class SleeperState
    {
        public SleeperState(ServerNpc npc, string locationId, bool atPost)
        {
            (Npc, LocationId, AtPost) = (npc, locationId, atPost);
        }

        public ServerNpc Npc { get; set; }

        public string LocationId { get; }

        /// <summary>He still stands by his tank and may leave for the settlement.</summary>
        public bool AtPost { get; set; }

        /// <summary>Somebody talked with him at the post.</summary>
        public bool Talked { get; set; }

        /// <summary>Uptime at which he leaves the post (0 = not decided).</summary>
        public double LeaveAt { get; set; }
    }

    /// <summary>The sleepers placed in loaded worlds, by their tower's site id.</summary>
    private readonly Dictionary<string, SleeperState> _sleepers = new(StringComparer.Ordinal);

    /// <summary>Whether a tank can be opened in this world: the VEGA story runs and holds the guard's dialog.</summary>
    internal bool SleeperCanWake => SpsTowerStoryRuns && _story!.Dialogs.Exists(d => d.Key == SleeperDialogKey);

    // ---------------------------------------------------------------- the tank

    /// <summary>The use handler of the block <c>capsule_tank</c> (either of the tank's two cells). A tank that belongs to no
    /// guard post of a tower does nothing.</summary>
    private void UseCapsuleTank(PlayerSession session, Vector3i cell)
    {
        if (FindSpsTowerByTank(cell) is not { HasGuardPost: true, TankOpened: false } tower)
        {
            return;
        }

        if (!SleeperCanWake)
        {
            Send(session, new ServerMessage { Text = "@srv.capsule.silent" });
            return;
        }

        if (!session.State.Inventory.Has(SpsTowerChipItem, 1))
        {
            Send(session, new ServerMessage { Text = "@srv.capsule.sealed" });
            return;
        }

        OpenCapsuleTank(session, tower);
    }

    /// <summary>Opens a tower's capsule tank with ONE hacking chip of the player (see the class summary).</summary>
    private void OpenCapsuleTank(PlayerSession session, SpsTowerRecord tower)
    {
        var p = session.State;
        var tank = tower.Tank!.ToVector();
        ushort open = _content.GetBlock(SleeperTankOpenBlock)?.NumericId.Value ?? 0;
        p.Inventory.Remove(SpsTowerChipItem, 1);
        tower.TankOpened = true;
        tower.GuardName = NextSleeperName();
        _repo.RunInTransaction(() =>
        {
            TurnSpsLampsBlue(tower.TankLamps, 0);
            if (open != 0)
            {
                PutSleeperCell(tank, open);
                PutSleeperCell(new Vector3i(tank.X, tank.Y + 1, tank.Z), open);
            }

            _repo.SaveMetadata(_meta);
            _repo.SavePlayer(p);
        });
        SendInventory(session);

        var at = new Vector3f(tank.X + 0.5f, tank.Y + 1f, tank.Z + 0.5f);
        BroadcastToWorld(new SoundFx { SoundId = SleeperTankSound, X = at.X, Y = at.Y, Z = at.Z });
        _log.Info($"'{p.Name}' opened the capsule tank of the guide tower '{tower.SiteId}'.");

        string packId = StoryRegistry.DefaultStoryId;
        bool sceneDefined = _content.Stories.TryGetValue(packId, out var pack) && pack.FindCutscene(SleeperTankCutscene) is not null;
        foreach (var w in SpsTowerWitnesses(session, at, SleeperWitnessRange))
        {
            RaiseGameEvent(w, GameEventKeys.CapsuleOpened, tower.SiteId);
            Send(w, new ServerMessage { Text = "@srv.capsule.opened" });
            if (!sceneDefined || !PlayCutscene(w, packId, SleeperTankCutscene, anchor: at))
            {
                SendVegaLine(w, SleeperTankLine, 2); // no scene (yet), or this player saw it at another tank
            }
        }

        var spot = AnchorSpotNear(new Vector3f(tank.X + 0.5f, tank.Y, tank.Z + 0.5f), 3, 1.2f, p.Position)
                   ?? new Vector3f(tank.X + 0.5f, tank.Y, tank.Z + 0.5f); // boxed in: he stands in the open tank
        _sleepers[tower.SiteId] = new SleeperState(SpawnSleeper(tower, spot, leash: 0f), _world.LocationId, atPost: true);
    }

    /// <summary>One cell of an opened tank: the block, told to the world.</summary>
    private void PutSleeperCell(Vector3i cell, ushort block)
    {
        var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        _world.SetBlock(c, new BlockId(block));
        BroadcastToWorld(new BlockChanged { X = c.X, Y = c.Y, Z = c.Z, Block = block, Moved = true });
    }

    // ---------------------------------------------------------------- the guard

    /// <summary>The name of the first guard of a save: the text of <see cref="SleeperNameKey"/> (a proper name — the same
    /// in every language).</summary>
    private string FirstSleeperName()
    {
        string name = Localize("en", SleeperNameKey);
        return string.IsNullOrWhiteSpace(name) || name == SleeperNameKey || name.StartsWith("[", StringComparison.Ordinal) ? "Iven" : name;
    }

    /// <summary>The name for the guard of the tank that is opened now: the first name no guard of this save carries yet —
    /// the first guard's (<see cref="SleeperNameKey"/>), then <see cref="SleeperNames"/> in order; once all are taken the
    /// list starts again. Decided by the order the tanks are opened and stored in the tower's record, so it never changes.</summary>
    private string NextSleeperName()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tower in _meta.SpsTowers)
        {
            if (tower.GuardName.Length > 0)
            {
                taken.Add(tower.GuardName);
            }
        }

        string first = FirstSleeperName();
        if (!taken.Contains(first))
        {
            return first;
        }

        foreach (string name in SleeperNames)
        {
            if (!taken.Contains(name))
            {
                return name;
            }
        }

        return SleeperNames[taken.Count % SleeperNames.Length];
    }

    /// <summary>The name of a tower's guard (<see cref="SpsTowerRecord.GuardName"/>). An opened tank whose record holds
    /// none yet (a save from before the names) gets the next free one now, saved.</summary>
    private string SleeperNameOf(SpsTowerRecord tower)
    {
        if (tower.GuardName.Length == 0)
        {
            tower.GuardName = NextSleeperName();
            SaveSpsTowers();
        }

        return tower.GuardName;
    }

    /// <summary>Puts the guard of <paramref name="tower"/> on the active world at <paramref name="pos"/> and returns him: a
    /// settler by role (he never trades), the story character <see cref="SleeperCharacterId"/>, the look
    /// <see cref="SleeperLook"/>, the same face on every load.</summary>
    private ServerNpc SpawnSleeper(SpsTowerRecord tower, Vector3f pos, float leash)
    {
        int seed = unchecked((int)WorldGenerator.StableHash("sps_guard:" + tower.SiteId));
        var npc = MakeNpc("settler", "settlers", robotic: false, pos, new Random(seed));
        npc.NameKey = SleeperRoleKey;
        npc.Name = SleeperNameOf(tower);
        npc.CharacterId = SleeperCharacterId;
        npc.Look = SleeperLook;
        npc.Size = 1.0f;
        npc.OutfitRgb = 0x39424E; // the dark plates of the escort wing's field armour (the look draws the blue lights)
        npc.LegsRgb = 0x2F3640;
        npc.FaceSeed = seed != 0 ? seed : 1;
        npc.Leash = Math.Max(0f, leash);
        _npcs.Add(npc);
        BroadcastNpcs();
        return npc;
    }

    /// <summary>
    /// 1 Hz on a world with a tower: every opened tank of the active world has its guard. One who is missing — the world was
    /// loaded again, or the server started — is put down where he lives now: in his settlement (the recorded one while people
    /// still live there, else the nearest inhabited one, which is recorded), or by his tank when nobody lives on the world.
    /// </summary>
    private void EnsureSleepersPlaced()
    {
        foreach (var tower in _meta.SpsTowers)
        {
            if (!tower.TankOpened || tower.LocationId != _world.LocationId
                || (_sleepers.TryGetValue(tower.SiteId, out var known) && _npcs.Contains(known.Npc)))
            {
                continue;
            }

            if (SleeperHome(tower) is { } home)
            {
                RecordSleeperHome(tower, home);
                _sleepers[tower.SiteId] = new SleeperState(SpawnSleeper(tower, SleeperSpotIn(home), leash: 2.5f), _world.LocationId, atPost: false);
            }
            else
            {
                _sleepers[tower.SiteId] = new SleeperState(SpawnSleeper(tower, SleeperPostSpot(tower), leash: 0f), _world.LocationId, atPost: true);
            }
        }
    }

    /// <summary>Every tick while a guard waits at his post on the active world: a conversation with him is noted, and
    /// <see cref="SleeperLeaveSeconds"/> after the first one ended he leaves for the settlement.</summary>
    private void TickSleeperTalks()
    {
        if (_sleepers.Count == 0)
        {
            return;
        }

        string here = _world.LocationId;
        foreach (var (siteId, state) in _sleepers)
        {
            if (!state.AtPost || state.LocationId != here || !_npcs.Contains(state.Npc))
            {
                continue;
            }

            if (SleeperInConversation(state.Npc))
            {
                state.Talked = true;
                state.LeaveAt = 0;
            }
            else if (state.Talked && state.LeaveAt <= 0)
            {
                state.LeaveAt = _uptime + SleeperLeaveSeconds;
            }
            else if (state.LeaveAt > 0 && _uptime >= state.LeaveAt)
            {
                MoveSleeperHome(siteId, state);
                return; // the dictionary's entry changed — the next tick looks at the others
            }
        }
    }

    /// <summary>Whether a player's dialog panel is open with this very NPC.</summary>
    private bool SleeperInConversation(ServerNpc npc)
    {
        foreach (var s in _sessions.Values)
        {
            if (s.Npc.Conversation is { } c && ReferenceEquals(c.Npc, npc))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The guard leaves his post: taken away there and put down in the nearest inhabited settlement (recorded), with
    /// a line for whoever stands near the post. Without an inhabited settlement he stays, and is not asked again.</summary>
    private void MoveSleeperHome(string siteId, SleeperState state)
    {
        var tower = _meta.SpsTowers.Find(t => t.SiteId == siteId);
        if (tower is null || SleeperHome(tower) is not { } home)
        {
            state.LeaveAt = double.MaxValue; // nobody lives on this world: he keeps his post (asked again after the next talk)
            return;
        }

        var post = state.Npc.Pos;
        string name = state.Npc.Name;
        _npcs.Remove(state.Npc);
        RecordSleeperHome(tower, home);
        _sleepers[siteId] = new SleeperState(SpawnSleeper(tower, SleeperSpotIn(home), leash: 2.5f), _world.LocationId, atPost: false);
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.Spectating && !InSpace(s.State.PlayerId) && WrapDistSq(s.State.Position, post) <= (double)SleeperWitnessRange * SleeperWitnessRange)
            {
                Send(s, new ServerMessage
                {
                    Text = Localize(s.Locale, "srv.capsule.guard_left")
                        .Replace("{name}", name, StringComparison.Ordinal).Replace("{place}", home.Name, StringComparison.Ordinal),
                });
            }
        }
    }

    /// <summary>Writes the guard's settlement into the tower's record when it changed.</summary>
    private void RecordSleeperHome(SpsTowerRecord tower, SettlementInstance home)
    {
        if (!string.Equals(tower.GuardSettlement, home.Name, StringComparison.Ordinal))
        {
            tower.GuardSettlement = home.Name;
            SaveSpsTowers();
        }
    }

    /// <summary>The settlement of the active world the guard of <paramref name="tower"/> lives in: the recorded one while it
    /// is inhabited, else the inhabited settlement nearest to the tower; null when nobody lives on the world.</summary>
    private SettlementInstance? SleeperHome(SpsTowerRecord tower)
    {
        SettlementInstance? best = null;
        double bestSq = double.MaxValue;
        var from = SpsTowerCentre(tower);
        foreach (var s in _settlements)
        {
            if (s.Ruined)
            {
                continue;
            }

            if (tower.GuardSettlement.Length > 0 && string.Equals(s.Name, tower.GuardSettlement, StringComparison.Ordinal))
            {
                return s;
            }

            double d = WrapDistSq(from, SettlementSquareCentre(s));
            if (d < bestSq)
            {
                (best, bestSq) = (s, d);
            }
        }

        return best;
    }

    /// <summary>Where the guard stands in a settlement: a free spot a few steps off its square (the square's middle when the
    /// ground there cannot be read yet).</summary>
    private Vector3f SleeperSpotIn(SettlementInstance home)
    {
        var square = SettlementSquareCentre(home);
        return AnchorSpotNear(new Vector3f(square.X + 3f, square.Y, square.Z + 2f), 3, 0f, square) ?? square;
    }

    /// <summary>Where the guard stands at his post: a free spot beside the tank (in the open tank when it is boxed in).</summary>
    private Vector3f SleeperPostSpot(SpsTowerRecord tower)
    {
        if (tower.Tank is not { } tank)
        {
            return SpsTowerCentre(tower);
        }

        var feet = new Vector3f(tank.X + 0.5f, tank.Y, tank.Z + 0.5f);
        return AnchorSpotNear(feet, 3, 1.2f, feet) ?? feet;
    }

    /// <summary>Where the guard of an opened tank is for a marker: where he stands on the loaded world, else his
    /// settlement's square, else null (the caller points at the tank).</summary>
    private Vector3f? SleeperPosition(SpsTowerRecord tower)
    {
        if (_sleepers.TryGetValue(tower.SiteId, out var state) && _worlds.Find(tower.LocationId)?.Npcs.Contains(state.Npc) == true)
        {
            return state.Npc.Pos;
        }

        if (tower.GuardSettlement.Length > 0 && _worlds.Find(tower.LocationId) is { } world)
        {
            foreach (var s in world.Settlements)
            {
                if (!s.Ruined && string.Equals(s.Name, tower.GuardSettlement, StringComparison.Ordinal))
                {
                    return SettlementSquareCentre(s);
                }
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- test seams

    /// <summary>Test seam: uses a tower's capsule tank (its upper cell when <paramref name="upper"/>) as the use key aimed at
    /// it would (reach is not checked). False when the tower has no tank or does not stand on the active world.</summary>
    internal bool UseCapsuleTankForTest(PlayerSession s, SpsTowerRecord tower, bool upper = false)
    {
        if (tower.Tank is not { } tank || tower.LocationId != _world.LocationId)
        {
            return false;
        }

        Serve(s);
        UseCapsuleTank(s, new Vector3i(tank.X, tank.Y + (upper ? 1 : 0), tank.Z));
        return true;
    }

    /// <summary>Test seam: the guard of a tower on the active world — his NPC id, where he stands and whether he is still at
    /// his post — or null while he is not placed.</summary>
    internal (int NpcId, Vector3f Pos, bool AtPost)? SleeperForTest(SpsTowerRecord tower)
        => _sleepers.TryGetValue(tower.SiteId, out var state) && _npcs.Contains(state.Npc)
            ? (state.Npc.Id, state.Npc.Pos, state.AtPost)
            : null;

    /// <summary>Test seam: every NPC of the guard's look on the active world.</summary>
    internal IReadOnlyList<int> SleeperNpcIdsForTest() => _npcs.Where(n => n.Look == SleeperLook).Select(n => n.Id).ToList();

    /// <summary>Test seam: the moment a guard at his post will leave it (0 = not decided).</summary>
    internal double SleeperLeaveAtForTest(SpsTowerRecord tower) => _sleepers.TryGetValue(tower.SiteId, out var state) ? state.LeaveAt : 0;

    /// <summary>Test seam: forgets the guards placed in loaded worlds and drops them from the active world — what a reload of
    /// the world does to its NPCs.</summary>
    internal void ForgetSleepersForTest()
    {
        _npcs.RemoveAll(n => n.Look == SleeperLook);
        _sleepers.Clear();
    }
}
