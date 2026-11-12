// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Client.Admin;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The admin panel's Unity-free parts (#3509): the follow tracker (the chase point behind the player, waiting for the first
/// sighting after the jump, ending when the player is gone), the panel's texts (role / state / kind / category keys, a player
/// row, a report headline, the clock, the chat window's direct-line prefix, the roster layout signature), and a locale guard:
/// every key those helpers and the Unity panel name exists in English and German.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class AdminPanelTextTests
{
    private static readonly Lazy<Dictionary<string, string>> English = new(() => Table("en"));
    private static readonly Lazy<Dictionary<string, string>> German = new(() => Table("de"));

    private static Dictionary<string, string> Table(string code)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json")))!;

    private static string En(string key) => English.Value.TryGetValue(key, out var text) ? text : key;

    // ---- Follow -------------------------------------------------------------------------------------------------------

    /// <summary>Observes every 0.1 s (a frame-like rate) for <paramref name="seconds"/>, or until following is lost.</summary>
    private static AdminFollowStep Run(AdminFollowTracker follow, ref double now, double seconds, bool seen)
    {
        var step = AdminFollowStep.Idle;
        for (double end = now + seconds - 1e-9; now < end;)
        {
            now += 0.1;
            step = follow.Observe(now, seen);
            if (step == AdminFollowStep.Lost)
            {
                break;
            }
        }

        return step;
    }

    [Fact]
    public void Following_WaitsForTheFirstSighting_ThenEndsTwoSecondsAfterThePlayerIsGone()
    {
        var follow = new AdminFollowTracker();
        Assert.Equal(AdminFollowStep.Idle, follow.Observe(0d, seen: true));

        double now = 100d;
        follow.Start("p1", "Anna", now);
        Assert.True(follow.Active);
        Assert.Equal(AdminFollowStep.Waiting, Run(follow, ref now, 5d, seen: false)); // the jump is still on its way
        Assert.Equal(AdminFollowStep.Following, Run(follow, ref now, 0.1d, seen: true));
        Assert.Equal(AdminFollowStep.Following, Run(follow, ref now, 1.5d, seen: false)); // a dropped packet is no departure
        Assert.Equal(AdminFollowStep.Lost, Run(follow, ref now, 1d, seen: false));
        Assert.Equal("Anna", follow.TargetName); // still readable for the note
        follow.Stop();
        Assert.False(follow.Active);
        Assert.Equal(AdminFollowStep.Idle, follow.Observe(now + 1d, seen: false));
    }

    [Fact]
    public void Following_GivesUp_WhenThePlayerNeverShowsUp()
    {
        var follow = new AdminFollowTracker();
        double now = 0d;
        follow.Start("p1", string.Empty, now);

        Assert.Equal("p1", follow.TargetName); // no name: the id stands in
        Assert.Equal(AdminFollowStep.Waiting, Run(follow, ref now, AdminFollowTracker.FirstSightSeconds - 1d, seen: false));
        Assert.Equal(AdminFollowStep.Lost, Run(follow, ref now, 2d, seen: false));
    }

    [Fact]
    public void APauseInObserving_CountsNeitherAsWaitingNorAsGone()
    {
        // The admin reopened the panel (no observer frames meanwhile), or the world loaded after the jump.
        var follow = new AdminFollowTracker();
        double now = 0d;
        follow.Start("p1", "Anna", now);
        now += 60d;
        Assert.Equal(AdminFollowStep.Waiting, follow.Observe(now, seen: false)); // a long load: still waiting
        Assert.Equal(AdminFollowStep.Following, Run(follow, ref now, 0.2d, seen: true));
        now += 30d;
        Assert.Equal(AdminFollowStep.Following, follow.Observe(now, seen: false)); // the panel was open for 30 s
        Assert.Equal(AdminFollowStep.Lost, Run(follow, ref now, 2.5d, seen: false));
    }

    [Fact]
    public void TheChasePoint_IsSixBehindAndThreeAbove_AlongThePlayersFacing()
    {
        var (x, y, z) = AdminFollowTracker.ChasePoint(10f, 64f, 20f, 0f); // facing +Z
        Assert.Equal(10f, x, 3);
        Assert.Equal(67f, y, 3);
        Assert.Equal(14f, z, 3);

        (x, _, z) = AdminFollowTracker.ChasePoint(10f, 64f, 20f, 90f); // facing +X
        Assert.Equal(4f, x, 3);
        Assert.Equal(20f, z, 3);

        var (yaw, pitch) = AdminFollowTracker.LookAt(0f, 10f, 0f, 0f, 0f, 10f); // ahead along +Z and below
        Assert.Equal(0f, yaw, 3);
        Assert.Equal(45f, pitch, 3); // positive = looking down, as Unity's camera pitch
        (yaw, _) = AdminFollowTracker.LookAt(0f, 0f, 0f, 5f, 0f, 0f);
        Assert.Equal(90f, yaw, 3);
    }

    // ---- Texts --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Keys_ForRolesStatesKindsAndCategories_FallBackForUnknownValues()
    {
        Assert.Equal("ui.admin.role_fleet", AdminPanelText.RoleKey("Player", fleetAdmin: true));
        Assert.Equal("ui.admin.role_owner", AdminPanelText.RoleKey("WorldAdmin", fleetAdmin: false));
        Assert.Equal("ui.admin.role_player", AdminPanelText.RoleKey("Something", fleetAdmin: false));
        Assert.Equal("ui.admin.state_space", AdminPanelText.StateKey(AdminPlayerStates.InSpace));
        Assert.Equal("ui.admin.state_foot", AdminPanelText.StateKey("swimming"));
        Assert.Equal("ui.admin.kind_help", AdminPanelText.KindKey(AdminReportKinds.Help));
        Assert.Equal("ui.admin.kind_player", AdminPanelText.KindKey(null));
        Assert.Equal("ui.admin.cat_pvp", AdminPanelText.CategoryKey(AdminEventCategories.Pvp));
    }

    [Fact]
    public void APlayerRow_NamesOnlyTheFlagsThatApply()
    {
        var p = new NetAdminPlayer
        {
            Name = "Anna",
            Role = "Player",
            State = AdminPlayerStates.OnFoot,
            HasPosition = true,
            X = 10.7f,
            Y = 64.2f,
            Z = -3.5f,
            Health = 79.2f,
            IdleSeconds = 59,
        };

        Assert.Equal("Anna  ·  Player  ·  on foot", AdminPanelText.PlayerHeadline(p, En));
        Assert.Equal("10 · 64 · -4  ·  Health 80", AdminPanelText.PlayerDetail(p, En));

        p.InDuel = true;
        p.DuelWith = "Ben";
        p.Silenced = true;
        p.IdleSeconds = 185;
        Assert.Equal("10 · 64 · -4  ·  Health 80  ·  duel with Ben  ·  chat paused  ·  idle 3 min", AdminPanelText.PlayerDetail(p, En));

        // #3502 review 11a: the visible variant gets no position — the row shows none.
        p.HasPosition = false;
        Assert.Equal("Health 80  ·  duel with Ben  ·  chat paused  ·  idle 3 min", AdminPanelText.PlayerDetail(p, En));
    }

    [Fact]
    public void TheRosterLayout_ChangesWithWhoIsWhere_NotWithWhereTheyStand()
    {
        var model = new AdminRosterModel();
        model.Apply(new AdminRoster { Players = new[] { Player("a", "p1"), Player("b", "p1") } });
        string first = AdminPanelText.RosterLayout(model.ByBody());

        model.Apply(new AdminRoster { Players = new[] { Player("a", "p1", x: 50f), Player("b", "p1") } });
        Assert.Equal(first, AdminPanelText.RosterLayout(model.ByBody()));

        model.Apply(new AdminRoster { Players = new[] { Player("a", "p2"), Player("b", "p1") } });
        Assert.NotEqual(first, AdminPanelText.RosterLayout(model.ByBody()));
    }

    private static NetAdminPlayer Player(string id, string body, float x = 0f)
        => new() { PlayerId = id, Name = id, LocationId = body, LocationName = body, X = x };

    [Fact]
    public void AReportHeadline_NamesTheTarget_OnlyWhenThereIsOne()
    {
        var player = new NetAdminReport { Kind = AdminReportKinds.Player, ReporterName = "Anna", TargetName = "Ben" };
        var help = new NetAdminReport { Kind = AdminReportKinds.Help, ReporterName = "Anna" };

        Assert.Equal("Player report  ·  Anna → Ben", AdminPanelText.ReportHeadline(player, En));
        Assert.Equal("Call for an admin  ·  Anna", AdminPanelText.ReportHeadline(help, En));
    }

    [Fact]
    public void TheClock_IsHoursMinutesSeconds_InTheGivenZone()
    {
        Assert.Equal("00:00:05", AdminPanelText.Clock(5, TimeSpan.Zero));
        Assert.Equal("01:00:05", AdminPanelText.Clock(5, TimeSpan.FromHours(1)));
        Assert.Matches(@"^\d\d:\d\d:\d\d$", AdminPanelText.LocalClock(1_800_000_000));
        Assert.Equal("1970-01-01 02:00", AdminPanelText.DateAndTime(0, TimeSpan.FromHours(2)));
        Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d$", AdminPanelText.LocalDateTime(1_800_000_000));
    }

    [Fact]
    public void ADirectLine_ReadsAdminToYou_OrYouToThePlayer_AndOthersConversationsStayOut()
    {
        var fromAdmin = new DirectChatMessage { FromId = "adm", FromName = "Marcel", ToId = "me", ToName = "Anna", FromAdmin = true };
        var answer = new DirectChatMessage { FromId = "me", FromName = "Anna", ToId = "adm", ToName = "Marcel" };
        var adminWrites = new DirectChatMessage { FromId = "me", FromName = "Marcel", ToId = "p", ToName = "Ben", FromAdmin = true };
        var mirrored = new DirectChatMessage { FromId = "adm2", FromName = "Eva", ToId = "p", ToName = "Ben", FromAdmin = true };

        Assert.Equal("Admin Marcel → you", AdminPanelText.DirectLinePrefix(fromAdmin, "me", En));
        Assert.Equal("You → Admin Marcel", AdminPanelText.DirectLinePrefix(answer, "me", En));
        Assert.Equal("You → Ben", AdminPanelText.DirectLinePrefix(adminWrites, "me", En));
        Assert.Null(AdminPanelText.DirectLinePrefix(mirrored, "me", En));
        Assert.Null(AdminPanelText.DirectLinePrefix(fromAdmin, string.Empty, En));
    }

    // ---- Locale guard -------------------------------------------------------------------------------------------------

    private static readonly Regex KeyLiteral = new("\"(?<key>(?:ui\\.(?:admin|pause|hud|chat|key)|admin\\.ev)\\.[a-z0-9_]*[a-z0-9])\"",
        RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    /// <summary>The keys the helpers produce and every literal key of the Unity panel and its admin parts.</summary>
    private static IEnumerable<string> PanelKeys()
    {
        foreach (string state in AdminPanelText.States)
        {
            yield return AdminPanelText.StateKey(state);
        }

        foreach (string kind in AdminPanelText.Kinds)
        {
            yield return AdminPanelText.KindKey(kind);
        }

        foreach (string category in AdminEventLogModel.Categories)
        {
            yield return AdminPanelText.CategoryKey(category);
        }

        foreach (string role in new[] { "Player", "Moderator", "Admin", "WorldAdmin" })
        {
            yield return AdminPanelText.RoleKey(role, fleetAdmin: false);
        }

        yield return AdminPanelText.RoleKey(null, fleetAdmin: true);
        string scripts = ClientTestPaths.ScriptPartsText("AdminPanelUi") + "\n" + ClientTestPaths.ScriptPartsText("ChatUi") + "\n"
                         + ClientTestPaths.ScriptPartsText("HudUi") + "\n" + ClientTestPaths.ScriptPartsText("AppShell");
        foreach (Match m in KeyLiteral.Matches(scripts))
        {
            yield return m.Groups["key"].Value;
        }
    }

    [Fact]
    public void EveryKeyThePanelNames_ExistsInEnglishAndGerman()
    {
        var keys = PanelKeys().Distinct(StringComparer.Ordinal).ToList();
        Assert.Contains("ui.admin.title", keys); // the guard reads the panel it means to read

        var missing = keys.Where(k => !English.Value.ContainsKey(k) || !German.Value.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "admin panel keys missing from en.json or de.json: " + string.Join(", ", missing));
    }
}
