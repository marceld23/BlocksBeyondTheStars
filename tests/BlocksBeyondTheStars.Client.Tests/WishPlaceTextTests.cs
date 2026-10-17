// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2714 — a held wish's village on the client: the marker the server puts on the player's marker list is found for its
/// mission (only while it is in the list, i.e. on the village's body), the map labels it "Wish · Testville", and the
/// Missions tab reads "Village: Testville · 120 m" — without the distance on another body, nothing for other missions.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class WishPlaceTextTests
{
    private static string L(string key) => key switch
    {
        WishPlaceText.MapKey => "Wunsch · {0}",
        WishPlaceText.PlaceKey => "Dorf: {0}",
        WishPlaceText.PlaceDistanceKey => "Dorf: {0} · {1} m",
        _ => "<" + key + ">",
    };

    private static NetMission Wish() => new() { Id = "wish_123_ab_0_0", PlaceName = "Testville", PlaceMarkerId = "wish:settle_123" };

    private static NetMarker VillageMarker() => new() { Id = "wish:settle_123", Label = "Testville", Wish = true, X = 40f, Z = 2f };

    [Fact]
    public void OnlyAMarkerFlaggedWish_IsAWishVillage()
    {
        Assert.True(WishPlaceText.IsWish(VillageMarker()));
        Assert.False(WishPlaceText.IsWish(new NetMarker { Id = "wish:settle_123", Label = "Testville" })); // an older server
        Assert.False(WishPlaceText.IsWish(null));
    }

    [Fact]
    public void TheMissionFindsItsVillageMarker_OnlyWhileItIsInTheList()
    {
        var marker = VillageMarker();
        var others = new[] { new NetMarker { Id = "mk1", Label = "Home" }, new NetMarker { Id = "ping", Ping = true }, marker };
        Assert.Same(marker, WishPlaceText.MarkerFor(Wish(), others));

        Assert.Null(WishPlaceText.MarkerFor(Wish(), new[] { new NetMarker { Id = "mk1" } })); // on another body: not sent
        Assert.Null(WishPlaceText.MarkerFor(Wish(), null));
        Assert.Null(WishPlaceText.MarkerFor(new NetMission { Id = "settle_1_job" }, others)); // not a wish
        Assert.Null(WishPlaceText.MarkerFor(Wish(), new[] { new NetMarker { Id = "wish:settle_123" } })); // a player's own marker of that id
    }

    [Fact]
    public void TheMapLabel_AndTheMissionLine_NameTheVillage()
    {
        Assert.Equal("Wunsch · Testville", WishPlaceText.MapLabel(VillageMarker(), L));
        Assert.Equal("Dorf: Testville · 120 m", WishPlaceText.MissionLine(Wish(), 120, L));
        Assert.Equal("Dorf: Testville", WishPlaceText.MissionLine(Wish(), -1, L)); // the village lies on another body
        Assert.Equal(string.Empty, WishPlaceText.MissionLine(new NetMission(), 10, L)); // no place: no line
        Assert.Equal(string.Empty, WishPlaceText.MissionLine(null, 10, L));
    }

    [Fact]
    public void Formatting_NeverThrows_OnBracesOrMissingSlots()
    {
        var odd = new NetMission { PlaceName = "{1} Hill", PlaceMarkerId = "wish:x" };
        Assert.Equal("Dorf: {1} Hill · 7 m", WishPlaceText.MissionLine(odd, 7, L)); // a brace in a name is no slot
        Assert.Equal("Wish Testville", WishPlaceText.MapLabel(VillageMarker(), _ => "Wish")); // a translation without its slot
        Assert.Equal("Village Testville", WishPlaceText.MissionLine(Wish(), -1, _ => "Village"));
    }

    [Fact]
    public void TheNewFields_SurviveTheWire()
    {
        var list = Assert.IsType<MarkerList>(NetCodec.Decode(NetCodec.Encode(new MarkerList { Markers = new[] { VillageMarker() } })));
        Assert.True(Assert.Single(list.Markers).Wish);

        var missions = Assert.IsType<MissionList>(NetCodec.Decode(NetCodec.Encode(new MissionList { Active = new[] { Wish() } })));
        var wish = Assert.Single(missions.Active);
        Assert.Equal("Testville", wish.PlaceName);
        Assert.Equal("wish:settle_123", wish.PlaceMarkerId);
    }
}
