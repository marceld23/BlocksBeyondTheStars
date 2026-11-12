// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The pinned generator goldens of #3511 — taken from main @ 7604df5c (Windows 11, .NET 10), before the structure
/// pipeline refactor (#3510). Never edit by hand; never re-pin for terrain generation ≤ 37.</summary>
public sealed partial class StructureGeneratorGoldenTests
{
    private static readonly Dictionary<string, string> MonumentPins = new()
    {
        ["monument:arcade:20261112:grass:cache"] = "9e8e089836165727",
        ["monument:arcade:25214903917:stone:plain"] = "24f08e2c71da1ee7",
        ["monument:gate:20261112:grass:cache"] = "eefc5e6dc2665883",
        ["monument:gate:25214903917:stone:plain"] = "0f2b36f5f1de3664",
        ["monument:circle:20261112:grass:cache"] = "936ebc1e480eec15",
        ["monument:circle:25214903917:stone:plain"] = "7b1bdb50c567087b",
        ["monument:obelisk:20261112:grass:cache"] = "6bd028b7ae78d745",
        ["monument:obelisk:25214903917:stone:plain"] = "d0e63153eea4044e",
        ["monument:altar:20261112:grass:cache"] = "1ae44891d30b77fd",
        ["monument:altar:25214903917:stone:plain"] = "a3901fdbd3645cfe",
        ["monument:bridge:20261112:grass:cache"] = "9be71d8430bee68d",
        ["monument:bridge:25214903917:stone:plain"] = "12b5b5b5c5f63716",
        ["monument:watchtower:20261112:grass:cache"] = "1be50a728f9ab622",
        ["monument:watchtower:25214903917:stone:plain"] = "7a5358651de6be8b",
        ["monument:tomb:20261112:grass:cache"] = "74718ba62dba5c59",
        ["monument:tomb:25214903917:stone:plain"] = "29435940269dac46",
        ["monument:ziggurat:20261112:grass:cache"] = "0b30d493fdc62485",
        ["monument:ziggurat:25214903917:stone:plain"] = "2f8f680e99e89b89",
        ["monument:colossus:20261112:grass:cache"] = "2ea9e35712959535",
        ["monument:colossus:25214903917:stone:plain"] = "83bb867bd41dc1b6",
        ["monument:aqueduct:20261112:grass:cache"] = "780e42da49fec868",
        ["monument:aqueduct:25214903917:stone:plain"] = "962aa6a880bd7022",
        ["monument:fossil_sauropod:20261112:grass:cache"] = "4b877402e1c52443",
        ["monument:fossil_sauropod:25214903917:stone:plain"] = "b7d2a14e675a72c1",
        ["monument:fossil_skull:20261112:grass:cache"] = "590756e1bae20a8b",
        ["monument:fossil_skull:25214903917:stone:plain"] = "8feeab56a7a74ed0",
        ["monument:fossil_serpent:20261112:grass:cache"] = "9e39f8503164772c",
        ["monument:fossil_serpent:25214903917:stone:plain"] = "bb2bb56c746deee8",
    };

    private static readonly Dictionary<string, string> RailPins = new()
    {
        ["railstation:h0"] = "81eca1c4066dd31d",
        ["railruin:h0:20261112:grass"] = "e4a85e89f62ebee8",
        ["railruin:h0:25214903917:sand"] = "9d1a8c7d617a757c",
        ["railstation:h1"] = "dfe794dc22e22437",
        ["railruin:h1:20261112:grass"] = "dbfa47c692a00e21",
        ["railruin:h1:25214903917:sand"] = "9cb1a64816f5e025",
        ["railstation:h2"] = "9c2fb7c9c7c36893",
        ["railruin:h2:20261112:grass"] = "ad684aa95b392c84",
        ["railruin:h2:25214903917:sand"] = "cd2c54ac37d73dbc",
        ["railstation:h3"] = "17e6918d87c04a47",
        ["railruin:h3:20261112:grass"] = "a3b804a5b0574227",
        ["railruin:h3:25214903917:sand"] = "2baed8ae4c466373",
    };

    private static readonly Dictionary<string, string> WreckPins = new()
    {
        ["wreck:corvette:1"] = "77892c513eb393f3",
        ["wreck:corvette:20261112"] = "cbe3edb29cc63872",
        ["wreck:courier:1"] = "69702af520c8b392",
        ["wreck:courier:20261112"] = "455e398e99a8c8d5",
        ["wreck:deathblock:1"] = "7f1a83d7bcc96d65",
        ["wreck:deathblock:20261112"] = "e780bc6fad96d037",
        ["wreck:hammerhead:1"] = "21614805a0e1b273",
        ["wreck:hammerhead:20261112"] = "269797006a54df43",
        ["wreck:hauler:1"] = "ab565d31fc18a560",
        ["wreck:hauler:20261112"] = "54b4fb3ad0187199",
        ["wreck:scout:1"] = "a4d7fb4c8721b60a",
        ["wreck:scout:20261112"] = "4b8b3b5c8bf37eaf",
        ["wreck:starter:1"] = "d72bf19362ff9839",
        ["wreck:starter:20261112"] = "e20db16af0bedcd4",
        ["wreck:thunderbolt:1"] = "dfbc8b1ff33aac16",
        ["wreck:thunderbolt:20261112"] = "e60298d83ae8d6d5",
        ["derelict:beta:1"] = "5190fc7bbd9e028f",
        ["derelict:beta:20261112"] = "ac6b93e688c5d2e2",
        ["derelict:beta:25214903917"] = "7092360990053deb",
        ["derelict:legacy:7919:procedural"] = "07a0f2b9227c047d",
        ["derelict:legacy:20261112:any"] = "97d2529d87edd8e8",
        ["derelict:legacy:39595:any"] = "328e6e3d9c23f68e",
        ["derelict:legacy:25214903917:any"] = "b24d287f6f0ecd5e",
    };

    private static readonly Dictionary<string, string> SettlementPins = new()
    {
        ["settlement:hamlet:intact:1:grass"] = "85b2dd7a8d901fb3",
        ["settlement:hamlet:intact:20261112:sand"] = "776bb2fd6d6b3792",
        ["settlement:hamlet:intact:25214903917:ice"] = "6e9cb21b6e8b67ca",
        ["settlement:hamlet:ruined:1:grass"] = "6757676351aa93a0",
        ["settlement:hamlet:ruined:20261112:sand"] = "8140afeed6d035ee",
        ["settlement:hamlet:ruined:25214903917:ice"] = "297724a0434367e9",
        ["settlement:village:intact:1:grass"] = "b47494fd5a011078",
        ["settlement:village:intact:20261112:sand"] = "3845d432d3832cbf",
        ["settlement:village:intact:25214903917:ice"] = "4dbb0bb61ff8d558",
        ["settlement:village:ruined:1:grass"] = "5ec0e3ed996f3d87",
        ["settlement:village:ruined:20261112:sand"] = "f19695dcdd4d6d84",
        ["settlement:village:ruined:25214903917:ice"] = "d59ba42c3fcfffcb",
        ["settlement:town:intact:1:grass"] = "4c63a0753e378f49",
        ["settlement:town:intact:20261112:sand"] = "b8c3f384d7d1894a",
        ["settlement:town:intact:25214903917:ice"] = "6dabc9892e756466",
        ["settlement:town:ruined:1:grass"] = "dff843157f29fb09",
        ["settlement:town:ruined:20261112:sand"] = "13fa864f5bd01a2a",
        ["settlement:town:ruined:25214903917:ice"] = "b11686a17e3cb229",
        ["settlement:city:intact:1:grass"] = "b332c202dd6583de",
        ["settlement:city:intact:20261112:sand"] = "249cf168d22b5ef1",
        ["settlement:city:intact:25214903917:ice"] = "b5f0bc448fb991f0",
        ["settlement:city:ruined:1:grass"] = "2947121c888169de",
        ["settlement:city:ruined:20261112:sand"] = "aa6766e8872bd20f",
        ["settlement:city:ruined:25214903917:ice"] = "314fa1b170bfd70b",
        ["settlement:town:legacy-crops"] = "e530da3a82387a7f",
    };

    private static readonly Dictionary<string, string> SettlementKitPins = new()
    {
        ["modules:hamlet"] = "0d20085b1585a859",
        ["modules:village"] = "a5d03c50c0355bc0",
        ["modules:town"] = "4a77868e156cfe86",
        ["modules:city"] = "8083fd18d9870c03",
        ["modules:village:legacy-roll"] = "a7c3516604eb7e85",
        ["kit:hamlet_default_1:1:1,2,8,6,0,0,1"] = "5037d1c2219fcbc0",
        ["kit:hamlet_default_1:20261112:2,2,8,6,0,0,1"] = "961d8b31269dbf93",
        ["kit:hamlet_modular_1:1:1,2,10,8,1,1,1"] = "94f10de9f807e2b7",
        ["kit:hamlet_modular_1:20261112:2,2,10,8,1,1,1"] = "31b6fcab2cfc0732",
        ["kit:hamlet_modular_2:1:1,2,10,8,1,1,1"] = "548ca2c52d8171cb",
        ["kit:hamlet_modular_2:20261112:2,2,10,8,1,1,1"] = "48b5145bf0c624bc",
        ["kit:village_default_1:1:2,2,8,6,0,0,1"] = "e70b1367f0cb139e",
        ["kit:village_default_1:20261112:3,2,8,6,0,0,1"] = "9a7eeb69a4f77881",
        ["kit:village_modular_1:1:2,2,10,8,1,1,1"] = "1bd786c6764e0228",
        ["kit:village_modular_1:20261112:3,2,10,8,1,1,1"] = "ab0adec496cfc359",
        ["kit:village_modular_2:1:2,2,10,8,1,1,1"] = "a65f8cb8e2b31c9a",
        ["kit:village_modular_2:20261112:3,2,10,8,1,1,1"] = "8c67577436448767",
        ["kit:town_default_1:1:3,3,8,6,0,0,1"] = "f7b75609ecc53759",
        ["kit:town_default_1:20261112:4,3,8,6,0,0,1"] = "a0ece1062a5bea2e",
        ["kit:town_modular_1:1:3,3,10,8,2,1,1"] = "dad69751e5d2cb27",
        ["kit:town_modular_1:20261112:4,3,10,8,2,1,1"] = "6dcc3faf1d6ee700",
        ["kit:town_modular_2:1:3,3,10,8,2,1,1"] = "1ef3d711c608f702",
        ["kit:town_modular_2:20261112:4,3,10,8,2,1,1"] = "c17bfad4fd2baaf8",
        ["kit:city_default_1:1:4,4,8,6,0,0,1"] = "38f429f57d922580",
        ["kit:city_default_1:20261112:5,4,8,6,0,0,1"] = "eb6a2a766f902f17",
        ["kit:city_modular_1:1:4,4,10,8,3,1,1"] = "68c04a33042e6a01",
        ["kit:city_modular_1:20261112:5,4,10,8,3,1,1"] = "99f1e3399a09528f",
        ["kit:city_modular_2:1:4,4,10,8,3,1,1"] = "a606f17f20a21a1e",
        ["kit:city_modular_2:20261112:5,4,10,8,3,1,1"] = "6d3a4c55ef08b1c0",
        ["template:river_hamlet"] = "f3d090e86a38ef65",
        ["template:river_hamlet_home"] = "41e0a4573bcf879e",
        ["template:stilt_hamlet"] = "03135f02b6897659",
        ["template:stilt_hamlet_home"] = "356627583d558e91",
        ["template:stone_roundhouse"] = "9dc067213ad8d3c4",
        ["template:stone_roundhouse_home"] = "5c5b7f7f406287fe",
        ["template:walled_market"] = "bef40b124b9920b0",
        ["template:walled_market_home"] = "d3a77d92dc42b57f",
    };

    private static readonly Dictionary<string, string> TabletLandmarkPins = new()
    {
        ["tablet:kuba_school"] = "fb277de8d42e3fbc",
        ["tablet:kuba_initials"] = "7d99e29f1bef05b3",
        ["tablet:jumave_games"] = "a5334093fef45e58",
        ["tablet:plain"] = "2c2c7f613df4a41d",
        ["landmark:sample_tower:intact:t0"] = "0f13be6f388f1859",
        ["landmark:sample_tower:intact:t1"] = "0184d7060c4c6cad",
        ["landmark:sample_tower:intact:t2"] = "df6487c998bbcaeb",
        ["landmark:sample_tower:intact:t3"] = "14b383d8a5832b88",
        ["landmark:sample_tower:ruin:t0"] = "ed6248b7491b79b7",
        ["landmark:sample_tower:ruin:t1"] = "d8482e799677ee58",
        ["landmark:sample_tower:ruin:t2"] = "e71429242ff604c2",
        ["landmark:sample_tower:ruin:t3"] = "99d146e7897311b7",
        ["landmark:sample_tower:own:25214903917:sand"] = "16c22d29885210bc",
        ["landmark:sample_mine:intact:t0"] = "8c4ffca5db6b2037",
        ["landmark:sample_mine:intact:t1"] = "e330447194e0f662",
        ["landmark:sample_mine:intact:t2"] = "34984a8f022e3486",
        ["landmark:sample_mine:intact:t3"] = "383a642602268a0a",
        ["landmark:sample_mine:ruin:t0"] = "a0481eaad2ebc39f",
        ["landmark:sample_mine:ruin:t1"] = "2969b5f1b9d4e4ca",
        ["landmark:sample_mine:ruin:t2"] = "43679ce5b18afc6c",
        ["landmark:sample_mine:ruin:t3"] = "631f895b9d8b15ba",
        ["landmark:sample_mine:own:25214903917:sand"] = "97c4d482450f7b62",
        ["landmark:sample_store:intact:t0"] = "5db7510dc6806a00",
        ["landmark:sample_store:intact:t1"] = "79bbb79f6f27b1df",
        ["landmark:sample_store:intact:t2"] = "7a0499420dfb784e",
        ["landmark:sample_store:intact:t3"] = "47c70467abff8a34",
        ["landmark:sample_store:ruin:t0"] = "77da847ab7cb7ced",
        ["landmark:sample_store:ruin:t1"] = "742b0c5f8cc8e456",
        ["landmark:sample_store:ruin:t2"] = "7eb366667e30679b",
        ["landmark:sample_store:ruin:t3"] = "ce20bdb528910f10",
        ["landmark:sample_store:own:25214903917:sand"] = "79bbb79f6f27b1df",
    };

    private static readonly Dictionary<string, string> AncientRuinPins = new()
    {
        ["gdsruin:20261112:x1:1"] = "a0b7e59a5b15069d",
        ["gdsruin:25214903917:x2:2"] = "e78e2a295ece5176",
        ["enclose:hamlet"] = "e982a22ad0e71441",
    };

    private static readonly Dictionary<string, string> WildsPins = new()
    {
        ["banditcamp:1:grass"] = "907d19704c69b6aa",
        ["outpost:1:grass"] = "7d2984e1bb78204f",
        ["spslab:1"] = "dfa7f82f0b9954a7",
        ["banditcamp:20261112:sand"] = "abe3257b56b24840",
        ["outpost:20261112:sand"] = "a591eedab947b7c5",
        ["spslab:20261112"] = "f5599569f3d582c5",
        ["banditcamp:25214903917:ice"] = "8f5d5aa4927b7c36",
        ["outpost:25214903917:ice"] = "d8136032d2297464",
        ["spslab:25214903917"] = "c55f0126b9045072",
        ["outpost:wall:stone"] = "1a3d69bc7f437ba8",
        ["outpost:wall:earth"] = "be7041458f68be8a",
        ["outpost:wall:iron"] = "254f0848e5914025",
        ["outpost:wall:steel"] = "ce1c8c7952fea996",
        ["outpost:boat:x"] = "18ad472877dae414",
        ["outpost:boat:z"] = "77c125b4c3af6a56",
        ["lodge:plot:20261112:grass:plain"] = "d1f61208e4128149",
        ["lodge:plot:25214903917:sand:alien"] = "4bb6dbd771c87a4f",
        ["lodge:quarry:20261112:grass:plain"] = "8effa77e3550529c",
        ["lodge:quarry:25214903917:sand:alien"] = "4afd68deb5bdb38d",
        ["lodge:woodpile:20261112:grass:plain"] = "e870b6b99af44164",
        ["lodge:woodpile:25214903917:sand:alien"] = "f7d679227d4cd9e0",
        ["lodge:drying_frame:20261112:grass:plain"] = "0106057c4df1b674",
        ["lodge:drying_frame:25214903917:sand:alien"] = "4045e09122e6ab26",
        ["factory:1:20261112"] = "41172b392ee97787",
        ["factory:3:20261112"] = "9ec2baed9e2cd7a5",
        ["factory:6:20261112"] = "abf62dbfd8fc22a7",
        ["factory:2:25214903917"] = "593b5b7bc1c4f8a7",
    };

    private static readonly Dictionary<string, string> CityPins = new()
    {
        ["city:procedural:1"] = "6421acfb37e99bb3",
        ["city:modules:1"] = "6de0e16e034f07cb",
        ["city:procedural:20261112"] = "baea835f65dea4ed",
        ["city:modules:20261112"] = "98c409947f938654",
        ["city:kit:city_gds_default_1:1"] = "08668ca42493fac3",
        ["city:kit:city_gds_default_1:20261112"] = "462bd347c5303de1",
        ["city:kit:city_gds_modular_1:1"] = "c076691a57c10bc3",
        ["city:kit:city_gds_modular_1:20261112"] = "b7833c4d5cf309b6",
    };

    private static readonly Dictionary<string, string> StationPins = new()
    {
        ["station:small:1"] = "fca006e752e7fe87",
        ["station:small:20261112"] = "d139437c546f62c1",
        ["stationkit:station_small_1:1"] = "d05f19d1691d20db",
        ["stationkit:station_small_1:20261112"] = "021b0ec9393a11ab",
        ["station:medium:1"] = "5cccb04b8233671f",
        ["station:medium:20261112"] = "d3d5069a94651983",
        ["stationkit:station_medium_1:1"] = "57390feb15f5ef47",
        ["stationkit:station_medium_1:20261112"] = "d247dfd596a29f18",
        ["station:large:1"] = "dfcf69e198433af9",
        ["station:large:20261112"] = "394eafcb134ec752",
        ["stationkit:station_large_1:1"] = "6a54952e7bca72bd",
        ["stationkit:station_large_1:20261112"] = "5e604e50a794d4f3",
        ["station:huge:1"] = "155030a15af97d59",
        ["station:huge:20261112"] = "077db726c6117794",
        ["stationkit:station_huge_1:1"] = "6feb0aa524d00422",
        ["stationkit:station_huge_1:20261112"] = "545b32a1f604e0ec",
        ["station:colossal:1"] = "2f1bffc38aaa84a1",
        ["station:colossal:20261112"] = "91f28dcbc02f8058",
        ["stationkit:station_colossal_1:1"] = "fab9346f6308ffab",
        ["stationkit:station_colossal_1:20261112"] = "b27cbcebd697e702",
        ["stationtemplate:hub_outpost"] = "f7fe80a3a81d0c11",
        ["stationtemplate:observation_spire"] = "7a0509663e9ff1e4",
        ["stationtemplate:pocket_waystation"] = "99615f6962334168",
        ["stationtemplate:twin_dock_bazaar"] = "6eb9d2fdaec58fe3",
    };

    /// <summary>Linux-only pins, for a case whose cells pass through <c>Math.Sin/Cos</c> and differ on that libm (none so far).
    /// A case absent here uses the shared pin.</summary>
    private static readonly Dictionary<string, string> LinuxPins = new()
    {
    };
}
