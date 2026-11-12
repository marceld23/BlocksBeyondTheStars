// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The pinned placement goldens of #3511 — taken from main @ 7604df5c (Windows 11, .NET 10), before the structure
/// pipeline refactor (#3510). Never edit by hand; never re-pin for terrain generation ≤ 37.</summary>
public sealed partial class StructurePlacementGoldenTests
{
    /// <summary>The placement-record kinds that exist today. A record of any other kind (a later one) is not hashed.</summary>
    private static readonly HashSet<string> RecordKinds = new(StringComparer.Ordinal)
    {
        "settlement", "factory", "banditcamp", "wreck", "vault", "vault_frontier", "lodge", "outpost", "spslab", "rail_line",
        "rail_station", "rail_ruin", "landmark", "underground", "unique:alien_shrine", "unique:observatory", "sounder:mirun",
        "sounder:ornasa", "sounder:choir_tower",
    };

    /// <summary>The stamped-feature families that exist today (the name up to the first ':' or '@').</summary>
    private static readonly HashSet<string> FeatureFamilies = new(StringComparer.Ordinal)
    {
        "structblocks", "ruins", "railruins", "intercityrail", "banditcamps", "banditcamp", "lodges", "outposts", "spslabs", "monuments",
        "monument", "textmonument", "soundchamber", "wreck", "landmark", "underground", "vaults", "crystalvault",
    };

    /// <summary>Parts the second boot of a save is known NOT to reproduce (an entry switches the replay assertion off for that
    /// part). Empty since the bookkeeping fix of #3513 closed the last gap — the chests that duplicated on reload (their
    /// positions and their loot share the body's random stream, and a chest whose loot existed skipped its draws, so the next
    /// chest landed elsewhere: meadowlands' chest:chest:32:67:-52 and the asteroid's chest:chest:347:49:-76 appeared on the
    /// second boot). A new entry needs an issue that removes it again.</summary>
    private static readonly Dictionary<string, string[]> KnownReplayGaps = new(StringComparer.Ordinal)
    {
    };

    private static readonly Dictionary<string, string> PlacementPins = new(StringComparer.Ordinal)
    {
        ["asteroid_14:edits"] = "d97072801cc19ec7",
        ["asteroid_14:features"] = "304cbdc2dfa01398",
        ["asteroid_14:loot"] = "6e02bf1df7aa75e1",
        ["asteroid_14:records"] = "96421925c69e7f75",
        ["asteroid_14:stations"] = "af63bd4c8601b7df",
        ["asteroid_kuba_w_20261112:edits"] = "fc82813417b2db5c",
        ["asteroid_kuba_w_20261112:features"] = "1b5ec9478fa1609f",
        ["asteroid_kuba_w_20261112:loot"] = "af63bd4c8601b7df",
        ["asteroid_kuba_w_20261112:records"] = "af63bd4c8601b7df",
        ["asteroid_kuba_w_20261112:stations"] = "af63bd4c8601b7df",
        ["desert_20261112:edits"] = "820493ec243a8a90",
        ["desert_20261112:features"] = "92b990d9c0d513c6",
        ["desert_20261112:loot"] = "327eafe8dcce1c6d",
        ["desert_20261112:records"] = "62d5d7dafca19df6",
        ["desert_20261112:stations"] = "af63bd4c8601b7df",
        ["frozen_ocean_20260903:edits"] = "e3684d7f97e10cc8",
        ["frozen_ocean_20260903:features"] = "284eeee485b33262",
        ["frozen_ocean_20260903:loot"] = "74e025798ca8d9f8",
        ["frozen_ocean_20260903:records"] = "d433bf1057e8ec51",
        ["frozen_ocean_20260903:stations"] = "af63bd4c8601b7df",
        ["gas_giant_20261112:edits"] = "5083182637af14b4",
        ["gas_giant_20261112:features"] = "956e409c91108536",
        ["gas_giant_20261112:loot"] = "b0718a94b28ce63b",
        ["gas_giant_20261112:records"] = "4b04f5afd97caac1",
        ["gas_giant_20261112:stations"] = "af63bd4c8601b7df",
        ["gds_desert_20261107:edits"] = "27be6606063bf368",
        ["gds_desert_20261107:features"] = "48f100d131267ec3",
        ["gds_desert_20261107:loot"] = "1d02f64ddd56423b",
        ["gds_desert_20261107:records"] = "0997b1c288ecf39e",
        ["gds_desert_20261107:stations"] = "af63bd4c8601b7df",
        ["landmarks_meadowlands_20261111:edits"] = "461a4a290ac0c21e",
        ["landmarks_meadowlands_20261111:features"] = "a86e12e1a1d2f230",
        ["landmarks_meadowlands_20261111:loot"] = "6038a9a95a5ead0a",
        ["landmarks_meadowlands_20261111:records"] = "f3d7c420faeb8dca",
        ["landmarks_meadowlands_20261111:stations"] = "af63bd4c8601b7df",
        ["meadowlands_20261112:edits"] = "7922ede0665c5584",
        ["meadowlands_20261112:features"] = "79f6d20709d81daa",
        ["meadowlands_20261112:loot"] = "168c0493abb6c559",
        ["meadowlands_20261112:records"] = "b924f19f431d16b3",
        ["meadowlands_20261112:stations"] = "af63bd4c8601b7df",
        ["scrapyard_424242:edits"] = "8022b3a3a731b47f",
        ["scrapyard_424242:features"] = "bcee462b14439799",
        ["scrapyard_424242:loot"] = "3e7ae3260c2a0899",
        ["scrapyard_424242:records"] = "e199af101bfdb10e",
        ["scrapyard_424242:stations"] = "af63bd4c8601b7df",
        ["sounder_world_20261112:edits"] = "c78b6cfb7dd050e1",
        ["sounder_world_20261112:features"] = "9296202af3358bfc",
        ["sounder_world_20261112:loot"] = "3a5a12d85ef11edb",
        ["sounder_world_20261112:records"] = "7d9d1727dd81ab18",
        ["sounder_world_20261112:stations"] = "af63bd4c8601b7df",
        ["titas_20261112:edits"] = "42c7a284b4cb6b0a",
        ["titas_20261112:features"] = "b8aefa3791b7c2fc",
        ["titas_20261112:loot"] = "7f95d600cfacfaf7",
        ["titas_20261112:records"] = "7fc8d1401c08ba35",
        ["titas_20261112:stations"] = "af63bd4c8601b7df",
        ["visit_moon:edits"] = "67bb38e5e7f72af9",
        ["visit_moon:features"] = "9d361bf2c899c679",
        ["visit_moon:loot"] = "6394fd9d4a85e26c",
        ["visit_moon:records"] = "53481387d22a3988",
        ["visit_moon:stations"] = "af63bd4c8601b7df",
        ["visit_observatory:edits"] = "89259546aea1e01f",
        ["visit_observatory:features"] = "8b1e009d7eaa63b2",
        ["visit_observatory:loot"] = "45383a2079418e16",
        ["visit_observatory:records"] = "756064248e053a76",
        ["visit_observatory:stations"] = "af63bd4c8601b7df",
        ["visit_shrine:edits"] = "58b0b7cc907871b6",
        ["visit_shrine:features"] = "9e15cabb68c64fe5",
        ["visit_shrine:loot"] = "75a4cc6022ade3e8",
        ["visit_shrine:records"] = "f7ae53ff374d26bf",
        ["visit_shrine:stations"] = "af63bd4c8601b7df",
    };

    /// <summary>Linux-only pins, for a case whose terrain differs on that libm (none so far).</summary>
    private static readonly Dictionary<string, string> LinuxPins = new(StringComparer.Ordinal)
    {
    };
}
