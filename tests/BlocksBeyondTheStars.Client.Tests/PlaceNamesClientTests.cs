// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2952 through the real wire: the player names the world they stand on, the server lays that name over their own star
/// map (<see cref="NetBody.CustomName"/>) and the client shows it — the HUD line reads the private name with the generated
/// one small beside it. <see cref="NetBody.Name"/> stays the generated name the client keys its looks on.
/// </summary>
public sealed class PlaceNamesClientTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // the encoded wire, as on a socket: the new intent and field must survive MessagePack
    public void NamingTheWorldYouStandOn_ComesBackOnYourStarMap(bool passObjects)
    {
        using var h = new ClientServerHarness(Content, passObjects: passObjects);
        StarMapData? map = null;
        h.Client.StarMapReceived += m => map = m;
        h.Join();
        var join = h.JoinAccepted!;
        h.Client.SendRequestStarMap();
        Assert.True(h.PumpUntil(() => map != null, 30), "the star map must arrive");
        string here = map!.ActiveLocationId;
        string generated = map.Systems.SelectMany(s => s.Bodies).Single(b => b.Id == here).Name;

        map = null;
        h.Client.SendSetPlaceName(here, "Mon");
        Assert.True(h.PumpUntil(() => map != null, 30), "naming a place must refresh the star map");

        var body = map!.Systems.SelectMany(s => s.Bodies).Single(b => b.Id == here);
        Assert.Equal("Mon", body.CustomName);
        Assert.Equal(generated, body.Name);
        Assert.Equal("Mon", body.Shown());

        string location = string.IsNullOrEmpty(join.SystemName) ? join.PlanetName : $"{join.SystemName} · {join.PlanetName}";
        string shown = PlaceNameDisplay.ShownLocation(map, location, out string original);
        Assert.Equal(location.Replace(generated, "Mon"), shown);
        Assert.Equal(generated, original);
    }

    [Fact]
    public void ShownLocation_KeepsTheGeneratedLine_WhileTheMapStillDescribesTheOldWorld()
    {
        var map = Map(systemCustom: string.Empty, bodyCustom: "Mon");
        Assert.Equal("Gheyee · Somewhere", PlaceNameDisplay.ShownLocation(map, "Gheyee · Somewhere", out string original));
        Assert.Equal(string.Empty, original);
    }

    [Fact]
    public void ShownLocation_NamesTheRenamedParts_AndOnlyThoseAsTheOriginal()
    {
        Assert.Equal("Gheyee · Mon", PlaceNameDisplay.ShownLocation(Map(string.Empty, "Mon"), "Gheyee · Kraegre", out string onlyBody));
        Assert.Equal("Kraegre", onlyBody);

        Assert.Equal("Sonne · Kraegre", PlaceNameDisplay.ShownLocation(Map("Sonne", string.Empty), "Gheyee · Kraegre", out string onlySystem));
        Assert.Equal("Gheyee", onlySystem);

        Assert.Equal("Sonne · Mon", PlaceNameDisplay.ShownLocation(Map("Sonne", "Mon"), "Gheyee · Kraegre", out string both));
        Assert.Equal("Gheyee · Kraegre", both);

        Assert.Equal("Gheyee · Kraegre", PlaceNameDisplay.ShownLocation(Map(string.Empty, string.Empty), "Gheyee · Kraegre", out string none));
        Assert.Equal(string.Empty, none);
    }

    [Fact]
    public void ShownBodyName_FindsTheArrivalByItsGeneratedNames()
    {
        var map = Map(string.Empty, "Mon");
        Assert.Equal("Mon", PlaceNameDisplay.ShownBodyName(map, "Gheyee", "Kraegre"));
        Assert.Equal("Kraegre", PlaceNameDisplay.ShownBodyName(map, "Elsewhere", "Kraegre"));
        Assert.Equal("Seana", PlaceNameDisplay.ShownBodyName(null, "Gheyee", "Seana"));
    }

    private static StarMapData Map(string systemCustom, string bodyCustom) => new()
    {
        ActiveLocationId = "sys2-p2",
        Systems = new[]
        {
            new NetStarSystem
            {
                Id = "sys2",
                Name = "Gheyee",
                CustomName = systemCustom,
                Bodies = new[] { new NetBody { Id = "sys2-p2", Name = "Kraegre", Kind = "Planet", CustomName = bodyCustom } },
            },
        },
    };
}
