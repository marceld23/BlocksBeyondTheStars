// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The other side (#3904, #3905, epic #3891): the Qualis' world, where the Sounders who sang the Great Song live. It is
/// <b>the same world, everywhere</b> — the same blocks, in white, grey and black — so a player who crossed is in a
/// <b>state</b> (<see cref="PlayerSession.OtherSide"/>, written here and nowhere else), never in another world or
/// location. How it is built: <c>docs/developer/OTHER_SIDE.md</c>.
/// <list type="bullet">
/// <item><b>The gate</b> is a group of three or more rune stones close together (<see cref="IsGate"/>, the detection the
/// Echoes use). Crossing: the crossing disc in the worn obsidian chest's socket, then the use key on a stone of a gate.
/// Returning: the use key on a stone of a gate while over there. A lone stone only sings.</item>
/// <item><b>Over there</b> a player moves, talks, touches rune stones, talks to the Echoes, uses the Resonance Blaster
/// and the melody bar — everything else is refused with one line (<c>GameServerOtherSide.Gates.cs</c>). Nothing hurts,
/// nothing is needed, hostiles look through them (<see cref="OverThere"/>, asked by the one damage gate).</item>
/// <item><b>Nobody on our side sees them</b>: the server withholds everything it sends about them
/// (<c>GameServerOtherSide.Visibility.cs</c>). Players over there see each other and everything on our side.</item>
/// <item><b>Who lives there</b>: low-drifting Qualis and the Echoes at the gates, sent only to players over there
/// (<c>GameServerOtherSide.Folk.cs</c>).</item>
/// <item><b>Leaving the game over there</b> — or a crash — puts the player at the entry gate on our side at the next
/// join (<see cref="OtherSideMarker"/>, the fallen's pattern).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The milestone a player carries while over there (<c>snd:over</c>): the state as the player record knows
    /// it — the damage gate reads it, a story condition can (<c>milestone:snd:over</c>), and a save written meanwhile is
    /// recognised at the next join.</summary>
    internal const string OtherSideMarker = SoundersIds.MilestonePrefix + "over";

    /// <summary>The milestone of the first crossing (<c>snd:crossed</c>), kept for good.</summary>
    internal const string OtherSideCrossedMilestone = SoundersIds.MilestonePrefix + "crossed";

    /// <summary>Seconds between two flips of one player at the stones — a held use key does not cross and come back.</summary>
    internal const double OtherSideFlipCooldownSeconds = 2.5;

    /// <summary>Refusal: this client cannot show the other side (no <c>otherside1</c>).</summary>
    internal const string OtherSideRefusalClient = "srv.otherside.no_client";

    /// <summary>Refusal: no gate at this stone (fewer than three rune stones close together).</summary>
    internal const string OtherSideRefusalNoGate = "srv.otherside.no_gate";

    /// <summary>Refusal: the crossing disc is not in the worn obsidian chest's socket.</summary>
    internal const string OtherSideRefusalNoDisc = "srv.otherside.no_disc";

    /// <summary>Refusal: not from here — in flight, on a spacewalk, aboard a ship.</summary>
    internal const string OtherSideRefusalNotHere = "srv.otherside.not_here";

    /// <summary>Refusal: not while seated, in a vehicle or on a train.</summary>
    internal const string OtherSideRefusalSeated = "srv.otherside.not_seated";

    /// <summary>Refusal: not now — the player watches (an observer, a fallen player, the respawn choice).</summary>
    internal const string OtherSideRefusalNotNow = "srv.otherside.not_now";

    private readonly Dictionary<string, double> _otherSideFlipReadyAt = new(StringComparer.Ordinal);

    /// <summary>The players whose record was written over there and who are now joining: told once, in the join.</summary>
    private readonly HashSet<string> _cameBackFromOtherSide = new(StringComparer.Ordinal);

    // The two hooks into the sound layer (#3907, #3908) — FillSoundBlockCount(PlayerSession, ref int used, ref int cap) for
    // the state message and SoundLayerSideChanged(PlayerSession), called right after the flag flipped in either direction —
    // have their defining declarations in GameServerSoundLayer.Hooks.cs (C# allows one per partial method, and the sound
    // layer wrote its file first). This core only calls them; without an implementation the counts stay 0.

    /// <summary>The sound layer's hook (its defining declaration is in GameServerSoundLayer.cs): a player's number of
    /// standing sound blocks changed — the state message carries the count, so it goes out again.</summary>
    partial void SoundBlockCountChanged(PlayerSession builder) => ResendOtherSideState(builder);

    // ---------------- The API ----------------

    /// <summary>Whether the player record is over there — the session flag as the record carries it. For the places that
    /// have a <see cref="PlayerState"/> and no session (the damage gate).</summary>
    internal static bool OverThere(PlayerState p) => p.Milestones.Contains(OtherSideMarker);

    /// <summary>Whether a gate stands at <paramref name="at"/> on the active world: three or more rune stones within
    /// <see cref="EchoRuneScanRadius"/> blocks. <paramref name="centre"/> is the middle of them.</summary>
    internal bool IsGate(Vector3f at, out Vector3f centre) => TryFindRuneGroup(at, out centre);

    /// <summary>The players of the active world who are over there.</summary>
    internal IEnumerable<PlayerSession> PlayersOverThere()
    {
        foreach (var s in JoinedInActiveWorld())
        {
            if (s.OtherSide)
            {
                yield return s;
            }
        }
    }

    /// <summary>The centre of the gate <paramref name="s"/> crossed through, or null on our side.</summary>
    internal Vector3f? EntryGateOf(PlayerSession s)
        => s.OtherSide && s.State.OtherSideGateLocation.Length > 0 ? s.State.OtherSideGate : null;

    /// <summary>
    /// Takes <paramref name="s"/> to the other side at the gate of <paramref name="runeStone"/> (a cell of the active
    /// world, which must be the player's). False with a locale key in <paramref name="refusalKey"/> when a rule is
    /// against it: the client cannot show it, the player watches, is not on foot on a surface or in a station, sits or
    /// rides, there is no gate at the stone, or the crossing disc is not in the worn obsidian chest's socket. Already
    /// over there: false with an empty key.
    /// </summary>
    internal bool TryCross(PlayerSession s, Vector3i runeStone, out string refusalKey)
    {
        refusalKey = string.Empty;
        if (s.OtherSide)
        {
            return false;
        }

        if (CrossRefusal(s) is { } refusal)
        {
            refusalKey = refusal;
            return false;
        }

        if (!IsGate(StoneCentre(runeStone), out var gate))
        {
            refusalKey = OtherSideRefusalNoGate;
            return false;
        }

        return CrossAtGate(s, gate, out refusalKey);
    }

    /// <summary>The crossing at a gate that was found: the player's own state, then the disc.</summary>
    private bool CrossAtGate(PlayerSession s, Vector3f gate, out string refusalKey)
    {
        refusalKey = CrossRefusal(s)
                     ?? (HasActiveSocketDisc(s.State, SoundersIds.DiscCrossing) ? string.Empty : OtherSideRefusalNoDisc);
        if (refusalKey.Length > 0)
        {
            return false;
        }

        EnterOtherSide(s, gate, OtherSideReasons.Crossed);
        return true;
    }

    /// <summary>
    /// Brings <paramref name="s"/> back at the gate of <paramref name="runeStone"/> — any gate, the disc is not needed.
    /// The player stays where they stand. False with <see cref="OtherSideRefusalNoGate"/> at a lone stone; not over
    /// there: false with an empty key.
    /// </summary>
    internal bool TryReturn(PlayerSession s, Vector3i runeStone, out string refusalKey)
    {
        refusalKey = string.Empty;
        if (!s.OtherSide)
        {
            return false;
        }

        if (s.CurrentLocationId != _world.LocationId || !IsGate(StoneCentre(runeStone), out _))
        {
            refusalKey = OtherSideRefusalNoGate;
            return false;
        }

        LeaveOtherSide(s, OtherSideReasons.Returned, toEntry: false, line: "@srv.otherside.returned");
        return true;
    }

    /// <summary>Brings <paramref name="s"/> back to our side and puts them at their entry gate (where they stood when they
    /// crossed). <paramref name="reason"/> rides in the state message's <see cref="OtherSideState.Reason"/>
    /// (<see cref="OtherSideReasons.Forced"/> when empty). Nothing happens to a player who is not over there.</summary>
    internal void ForceReturn(PlayerSession s, string reason)
        => LeaveOtherSide(s, string.IsNullOrEmpty(reason) ? OtherSideReasons.Forced : reason, toEntry: true, line: null);

    // ---------------- The rules of a crossing ----------------

    private static Vector3f StoneCentre(Vector3i cell) => new(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);

    /// <summary>Why this player cannot cross right now, whatever stone they use — or null.</summary>
    private string? CrossRefusal(PlayerSession s)
    {
        var p = s.State;
        if (!s.SupportsOtherSide)
        {
            return OtherSideRefusalClient;
        }

        if (s.Watching || s.Respawn.ChoicePending || p.Health <= 0f)
        {
            return OtherSideRefusalNotNow;
        }

        if (s.CurrentLocationId != _world.LocationId || InSpace(p.PlayerId) || p.InEva || InShipInterior(p.PlayerId) || p.AboardShip)
        {
            return OtherSideRefusalNotHere; // planets and stations, on foot — never in flight or inside a ship
        }

        return p.Seated || p.InSpeeder.Length > 0 || p.InTrain.Length > 0 ? OtherSideRefusalSeated : null;
    }

    /// <summary>
    /// The use key on a rune stone, after the stone sang (<see cref="UseRuneStone"/>): over there a stone of a gate brings
    /// the player back; on our side, with the crossing disc in the chest's socket, a stone of a gate takes them over. A
    /// lone stone only sings, in both cases. A player who carries the disc but does not wear it in the socket is told so
    /// at a gate; every other refusal of a crossing is told as well. True when the player changed sides (the flip saved
    /// the player).
    /// </summary>
    private bool OtherSideOnRuneStone(PlayerSession s, Vector3i cell)
    {
        string id = s.State.PlayerId;
        if (_otherSideFlipReadyAt.TryGetValue(id, out double readyAt) && _uptime < readyAt)
        {
            return false;
        }

        if (s.OtherSide)
        {
            if (!TryReturn(s, cell, out _))
            {
                return false;
            }

            _otherSideFlipReadyAt[id] = _uptime + OtherSideFlipCooldownSeconds;
            return true;
        }

        bool socket = HasActiveSocketDisc(s.State, SoundersIds.DiscCrossing);
        if ((!socket && !CarriesSounderItem(s.State, SoundersIds.DiscCrossing))
            || s.CurrentLocationId != _world.LocationId || !IsGate(StoneCentre(cell), out var gate))
        {
            return false; // no crossing disc, or a lone stone: the stone sings, as it always did
        }

        if (CrossAtGate(s, gate, out string refusal))
        {
            _otherSideFlipReadyAt[id] = _uptime + OtherSideFlipCooldownSeconds;
            return true;
        }

        Send(s, new ServerMessage { Text = "@" + refusal }); // at a gate, with the disc: say what is in the way
        return false;
    }

    // ---------------- Crossing and coming back ----------------

    /// <summary>The flag flips to "over there": what the player was doing with our side ends, the entry gate is
    /// remembered, our side forgets the player, and the player is told.</summary>
    private void EnterOtherSide(PlayerSession s, Vector3f gate, string reason)
    {
        var p = s.State;
        EndNpcConversation(s, tellClient: true);
        EndRobotConversation(s, tellClient: true);
        CancelTradesFor(p.PlayerId);
        p.RopeAnchor = null;
        p.Stealthed = false; // the cloak would only drain the suit: nobody on our side sees the player anyway
        s.CarryingBlock = false;

        p.OtherSideGateLocation = s.CurrentLocationId;
        p.OtherSideGate = gate;
        p.OtherSideEntry = p.Position;
        bool first = p.Milestones.Add(OtherSideCrossedMilestone);
        p.Milestones.Add(OtherSideMarker);
        s.OtherSide = true;
        SoundLayerSideChanged(s);

        HideFromOurSide(s);
        SendOtherSideState(s, reason);
        OtherSideGatesOnCross(s); // #3906: the gates' lights
        Send(s, new ServerMessage { Text = first ? "@srv.otherside.crossed_first" : "@srv.otherside.crossed" });
        OtherSideListsChanged(s);
        MarkJournalDirty(s);
        _repo.SavePlayer(p); // a crash right now is recognised at the next join
        RaiseGameEvent(s, GameEventKeys.OtherSideCrossed, s.CurrentLocationId ?? string.Empty); // arg: the body id (#3910: "cross on Soaniri")
        _log.Info($"Player '{p.Name}' crossed to the other side on {s.CurrentLocationId} ({reason}).");
    }

    /// <summary>
    /// The flag flips back. <paramref name="toEntry"/> puts the player where they stood when they crossed (a forced
    /// return); otherwise they stay where they are. <paramref name="line"/> is the system line the player reads, null for
    /// a silent return (something else moved them and says so itself).
    /// </summary>
    private void LeaveOtherSide(PlayerSession s, string reason, bool toEntry, string? line)
    {
        if (!s.OtherSide)
        {
            return;
        }

        var p = s.State;
        var entry = p.OtherSideEntry;
        bool entryHere = p.OtherSideGateLocation.Length > 0 && p.OtherSideGateLocation == s.CurrentLocationId;
        EndNpcConversation(s, tellClient: true); // a talk with an Echo ends at the threshold
        s.OtherSide = false;
        p.Milestones.Remove(OtherSideMarker);
        ForgetEntryGate(p);
        SoundLayerSideChanged(s);

        if (toEntry && entryHere)
        {
            s.AwaitingSpawnAdopt = true; // ignore the pose from over there until the client has snapped back (#865)
            SnapPlayerTo(s, entry, "@srv.otherside.forced");
        }

        ShowOnOurSide(s);
        SendOtherSideState(s, reason);
        OtherSideGatesOnReturn(s); // #3906: an empty list — the lights go out
        if (line is not null)
        {
            Send(s, new ServerMessage { Text = line });
        }

        OtherSideListsChanged(s);
        _repo.SavePlayer(p);
        RaiseGameEvent(s, GameEventKeys.OtherSideReturned, s.CurrentLocationId ?? string.Empty); // arg: the body id
        _log.Info($"Player '{p.Name}' is back from the other side ({reason}).");
    }

    /// <summary>Something else is about to move this player (an admin teleport, a world change, a respawn): they are back
    /// on our side first, silently — the mover says what happened.</summary>
    private void OtherSideBeforeMove(PlayerSession s)
        => LeaveOtherSide(s, OtherSideReasons.Moved, toEntry: false, line: null);

    private static void ForgetEntryGate(PlayerState p)
    {
        p.OtherSideGateLocation = string.Empty;
        p.OtherSideGate = Vector3f.Zero;
        p.OtherSideEntry = Vector3f.Zero;
    }

    /// <summary>Tells the player's client where it stands (<see cref="OtherSideState"/>).</summary>
    private void SendOtherSideState(PlayerSession s, string reason)
    {
        int used = 0, cap = 0;
        FillSoundBlockCount(s, ref used, ref cap);
        var gate = EntryGateOf(s);
        if (reason != OtherSideReasons.Sync)
        {
            s.OtherSideLastChange = reason; // #4024: the /bump snapshot names it
        }

        Send(s, new OtherSideState
        {
            Over = s.OtherSide,
            HasGate = gate.HasValue,
            GateX = gate?.X ?? 0f,
            GateY = gate?.Y ?? 0f,
            GateZ = gate?.Z ?? 0f,
            Reason = reason,
            SoundBlocksUsed = used,
            SoundBlockCap = cap,
        });
    }

    /// <summary>For the sound layer (#3908): the player's sound-block count changed — the state goes out again.</summary>
    internal void ResendOtherSideState(PlayerSession s) => SendOtherSideState(s, OtherSideReasons.Sync);

    // ---------------- No needs, no harm ----------------

    /// <summary>
    /// The needs tick of a player over there (from the per-player survival tick, in place of everything after the god-mode
    /// check): nothing drains and nothing hurts — no air, no hunger, no cold or heat, no lava, no weather. Oxygen, hunger,
    /// health and the exposure meter simply stand still until the player is back. The jetpack and the cloak were ticked
    /// before this point, so flying still costs suit energy; a moved bar reaches the HUD at the vitals' usual pace.
    /// Hits never arrive at all: the one damage gate (<c>PlayerTakesDamage</c>) asks <see cref="OverThere"/>, and with it
    /// every hostile's choice of target (<c>HostilesIgnore</c>), a fall, PvP and a crash.
    /// </summary>
    private void HoldVitalsOverThere(PlayerSession s, double dt)
    {
        var p = s.State;
        p.SuitClimateActive = false;
        s.Hazards.HazardDeathReason = null;
        s.Sync.VitalsSyncTimer += dt;
        if (s.Sync.VitalsSyncTimer < 0.5)
        {
            return;
        }

        s.Sync.VitalsSyncTimer = 0;
        if (Math.Abs(p.SuitEnergy - s.Sync.LastSentEnergy) > 0.4f || Math.Abs(p.Health - s.Sync.LastSentHealth) > 0.4f)
        {
            MarkPlayerStateDirty(s);
        }
    }

    // ---------------- Join, leave, crash ----------------

    /// <summary>Loading a joining player's record: one written while they were over there is put right
    /// (<see cref="UndoOtherSideInSave"/>), and the join tells them.</summary>
    private void OtherSideOnLoad(PlayerState p)
    {
        if (UndoOtherSideInSave(p))
        {
            _cameBackFromOtherSide.Add(p.PlayerId);
        }
    }

    /// <summary>The join: the client learns where it stands — on our side, always (a player who left over there was put
    /// back at the gate when the record was loaded, <see cref="UndoOtherSideInSave"/>).</summary>
    private void OtherSideOnJoin(PlayerSession s)
    {
        if (!s.SupportsOtherSide)
        {
            return; // an older client would drop the message
        }

        bool cameBack = _cameBackFromOtherSide.Remove(s.State.PlayerId);
        SendOtherSideState(s, cameBack ? OtherSideReasons.Rejoined : OtherSideReasons.Sync);
        if (cameBack)
        {
            Send(s, new ServerMessage { Text = "@srv.otherside.rejoined" });
        }
    }

    /// <summary>A player who leaves the game over there is saved at the entry gate, on our side.</summary>
    private void OtherSideOnLeave(PlayerSession s)
    {
        ReleaseOverEchoOnLeave(s); // #3910: the story figure that stood at a gate for this player goes with them
        if (!s.OtherSide)
        {
            return;
        }

        s.OtherSide = false;
        _otherSideFlipReadyAt.Remove(s.State.PlayerId);
        UndoOtherSideInSave(s.State);
    }

    /// <summary>A record written while its player was over there (a leave, a crash, an autosave): the player stands at the
    /// entry gate on our side. True when the record was such a one.</summary>
    private static bool UndoOtherSideInSave(PlayerState p)
    {
        bool was = p.Milestones.Remove(OtherSideMarker);
        if (was && p.OtherSideGateLocation.Length > 0)
        {
            p.CurrentLocationId = p.OtherSideGateLocation;
            p.Position = p.OtherSideEntry;
        }

        ForgetEntryGate(p);
        return was;
    }

    // ---------------- The watch ----------------

    /// <summary>
    /// Once a second per world: a player over there who is no longer where a player can be over there — in flight,
    /// aboard, seated, riding, watching, dead, on another world than the gate's — is back on our side, silently. The
    /// safety net behind the hooks of the known movers (<see cref="OtherSideBeforeMove"/>); then the folk of the other
    /// side (<see cref="TickOtherSideFolk"/>).
    /// </summary>
    private void TickOtherSide(double dt)
    {
        var world = OtherSideWorldOf(_world.LocationId);
        world.Clock += dt;
        if (world.Clock < 1.0)
        {
            return;
        }

        world.Clock = 0;
        List<PlayerSession>? stray = null;
        foreach (var s in PlayersOverThere())
        {
            if (!MayStayOverThere(s))
            {
                (stray ??= new List<PlayerSession>()).Add(s);
            }
        }

        if (stray is not null)
        {
            foreach (var s in stray)
            {
                OtherSideBeforeMove(s);
            }
        }

        TickOtherSideFolk(world);
        TickOtherSideGateLists(world); // #3906: after the folk pass, which finds the gates around the players
        FlushOtherSideStarMaps(); // #3905: the star maps the flips of the last second made stale, once each
    }

    private bool MayStayOverThere(PlayerSession s)
    {
        var p = s.State;
        return !s.Watching && !s.Respawn.ChoicePending && p.Health > 0f
               && !InSpace(p.PlayerId) && !p.InEva && !InShipInterior(p.PlayerId) && !p.AboardShip
               && !p.Seated && p.InSpeeder.Length == 0 && p.InTrain.Length == 0
               && p.OtherSideGateLocation == s.CurrentLocationId;
    }

    // ---------------- The admin verb ----------------

    /// <summary><c>/otherside [on|off]</c> (the QA verb, behind the cheat gate): flips the state for the admin without disc
    /// and gate — the place they stand at is their entry point. The capability word still holds: a client that cannot show
    /// the other side is not taken there.</summary>
    private void AdminOtherSide(PlayerSession s, string? arg)
    {
        string a = (arg ?? string.Empty).Trim().ToLowerInvariant();
        bool on = a.Length == 0 ? !s.OtherSide : a is "on" or "an" or "1" or "true";
        if (on == s.OtherSide)
        {
            Send(s, new ServerMessage { Text = on ? "@srv.otherside.admin_on" : "@srv.otherside.admin_off" });
            return;
        }

        if (!on)
        {
            LeaveOtherSide(s, OtherSideReasons.Admin, toEntry: false, line: "@srv.otherside.admin_off");
            CheatLog(s.State, "left the other side (/otherside)");
            return;
        }

        if (!SetActiveWorld(s.CurrentLocationId))
        {
            return;
        }

        if (CrossRefusal(s) is { } refusal)
        {
            Reject(s, "admin", "@" + refusal);
            return;
        }

        EnterOtherSide(s, s.State.Position, OtherSideReasons.Admin);
        CheatLog(s.State, "crossed to the other side (/otherside)");
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: <see cref="TryCross"/> with the player's world under the cursor.</summary>
    public bool TryCrossForTest(PlayerSession s, Vector3i runeStone, out string refusalKey)
    {
        Serve(s);
        return TryCross(s, runeStone, out refusalKey);
    }

    /// <summary>Test seam: <see cref="TryReturn"/> with the player's world under the cursor.</summary>
    public bool TryReturnForTest(PlayerSession s, Vector3i runeStone, out string refusalKey)
    {
        Serve(s);
        return TryReturn(s, runeStone, out refusalKey);
    }

    /// <summary>Test seam: <see cref="ForceReturn"/>.</summary>
    public void ForceReturnForTest(PlayerSession s, string reason = "")
    {
        Serve(s);
        ForceReturn(s, reason);
    }

    /// <summary>Test seam: <see cref="IsGate"/> on the active world.</summary>
    public bool IsGateForTest(Vector3f at, out Vector3f centre) => IsGate(at, out centre);

    /// <summary>Test seam: <see cref="EntryGateOf"/>.</summary>
    public Vector3f? EntryGateOfForTest(PlayerSession s) => EntryGateOf(s);

    /// <summary>Test seam: the names of the players of the active world who are over there.</summary>
    public IReadOnlyList<string> PlayersOverThereForTest()
    {
        var names = new List<string>();
        foreach (var s in PlayersOverThere())
        {
            names.Add(s.State.Name);
        }

        return names;
    }

    /// <summary>Test seam: the marker a player over there carries.</summary>
    public const string OtherSideMarkerForTest = OtherSideMarker;

    /// <summary>Test seam: the leave step of a player over there.</summary>
    public void OtherSideOnLeaveForTest(PlayerSession s) => OtherSideOnLeave(s);

    /// <summary>Test seam: what loading a player does to a record written while they were over there.</summary>
    public static bool UndoOtherSideInSaveForTest(PlayerState p) => UndoOtherSideInSave(p);

    /// <summary>Test seam: the use key on a rune stone, as the client sends it.</summary>
    public void UseBlockForTest(PlayerSession s, Vector3i cell)
    {
        Serve(s);
        HandleUseBlock(s, new UseBlockIntent { X = cell.X, Y = cell.Y, Z = cell.Z });
    }
}
