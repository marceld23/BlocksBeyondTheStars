// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;
using SvSession = BlocksBeyondTheStars.GameServer.PlayerSession;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The fixes of the first code read of the bio lab (#2216, #2218): the wash is part of an experiment's signature, a
/// blank sample / seedling / preparation is never handed out and never taken, an item key is only read when the lab
/// could have made it, and the status effects feel the air the player is really in — also where the temperature hazard
/// never looks (aboard the ship, god mode, the Creative game mode, hazards off).
/// </summary>
public sealed class BioLabFixTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>The lab items that are nothing without what their key carries.</summary>
    private static readonly string[] BlankLabItems =
    {
        BioItems.Sample, BioItems.MineralSample, BioItems.Seedling,
        "prep_injector", "prep_gel", "prep_bar", "prep_capsule", "prep_coating",
    };

    /// <summary>A key a cheat could type: a well-formed payload that names effect 99, which no version knows.</summary>
    private const string UnknownEffectKey = "prep_injector#x6350008000";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_biofix_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }

    // ---------------- Harness ----------------

    /// <summary>Records every decoded outbound message, so a test can read what the lab answered and why something was
    /// refused. Everything passes through the real codec on the way.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public List<(int Conn, object Msg)> Sent { get; } = new();

        public void Start(int port)
        {
        }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m)
            {
                Sent.Add((connectionId, m));
            }
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m)
            {
                Sent.Add((int.MinValue, m));
            }
        }

        public void Poll()
        {
            _ = ClientConnected;
            _ = ClientDisconnected;
            _ = PayloadReceived;
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }

    private SvGameServer NewServer(string world, string planet = "jungle", Action<ServerConfig>? tune = null, IServerTransport? transport = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 77, StartPlanet = planet, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        tune?.Invoke(config);
        var server = new SvGameServer(config, Content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A player on foot at <paramref name="at"/> with an empty backpack and the given items — no starter kit, so a
    /// test counts exactly what it hands out.</summary>
    private static SvSession Player(SvGameServer server, string name, Vector3f at, params string[] items)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false; // no physical ship in these worlds: the flag stays as a test sets it
        p.State.Position = at;
        p.State.SuitEnergy = 100f;
        for (int i = 0; i < p.State.Inventory.SlotCount; i++)
        {
            p.State.Inventory.SetSlot(i, null);
        }

        foreach (string key in items)
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 1, 1));
        }

        return p;
    }

    /// <summary>High up in the air above the origin: empty cells, everything a test places is in reach.</summary>
    private static readonly Vector3f AtTheLab = new(0, 200, 0);

    /// <summary>In the open, well above any terrain and below the atmosphere line — the real outside temperature of the
    /// world, no roof and no underground warmth (the spot the temperature hazard tests use).</summary>
    private static readonly Vector3f Outside = new(500.5f, 150f, 500.5f);

    private static BlockId Block(string key) => Content.GetBlock(key)!.NumericId;

    private static void Ticks(SvGameServer server, int count, double step = 0.1)
    {
        for (int i = 0; i < count; i++)
        {
            server.TickForTest(step);
        }
    }

    private static IEnumerable<T> SentTo<T>(RecordingTransport transport, SvSession who)
        => transport.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<T>();

    private static int Refusals(RecordingTransport transport, SvSession who, string reason)
        => SentTo<ActionRejected>(transport, who).Count(r => r.Reason == reason);

    private static bool Holds(SvSession p, string baseKey)
        => p.State.Inventory.Slots.Concat(p.State.SampleCase.Slots).Any(s => s is { IsEmpty: false } && ItemKey.Base(s.Item) == baseKey);

    private static float Left(SvSession p, BioEffect effect) => p.State.Effects.First(e => e.Effect == effect).SecondsLeft;

    private static string Prep(BioEffect effect, int level, int seconds = 120, BioForm form = BioForm.Injector)
        => BioItems.PreparationItem(new Compound { Form = form, Effect = effect, Level = level, DurationSeconds = seconds });

    /// <summary>The ten hex digits of a preparation key, written by hand — so a test can name what the lab never makes.</summary>
    private static string Payload(int effect, int level, int side = 0, int sideLevel = 0, int thermal = 0, int units = 8,
        int secondary = 0, int secondaryLevel = 0)
        => string.Create(CultureInfo.InvariantCulture,
            $"{effect:x2}{level:x1}{side:x1}{sideLevel | (thermal << 2):x1}{units:x2}{secondary:x2}{secondaryLevel:x1}");

    private static Compound? Read(string payload) => Compound.FromPayload(BioForm.Injector, payload);

    private static MaterialProfile Material(int purity, params (MatTrait Trait, int Level)[] traits)
    {
        var m = new MaterialProfile { Purity = purity };
        foreach (var (trait, level) in traits)
        {
            m.Levels[(int)trait] = level;
        }

        return m;
    }

    // ---------------- 1. The wash is part of the experiment ----------------

    [Fact]
    public void TheSignature_NamesTheWash_AndIsWhatItAlwaysWasWithoutOne()
    {
        // The unwashed signature is the string research books already hold: an older save's entries keep matching.
        Assert.Equal("0000abcd/3/iron_ore:00000001/00000002", Synthesis.Signature(0xABCD, BioForm.Capsule, 1, "iron_ore", 2));
        Assert.Equal("0000abcd/0/:00000000/00000000", Synthesis.Signature(0xABCD, BioForm.Injector, 0, null, 0));
        Assert.Equal(Synthesis.Signature(7, BioForm.Gel, 0, "steel", 9), Synthesis.Signature(7, BioForm.Gel, 0, "steel", 9, washed: false));

        // The washed mix of the same inputs is another experiment.
        Assert.Equal("0000abcd/3/iron_ore:00000001/00000002/w", Synthesis.Signature(0xABCD, BioForm.Capsule, 1, "iron_ore", 2, washed: true));
        Assert.Equal(Synthesis.Signature(7, BioForm.Gel, 0, "steel", 9) + "/w", Synthesis.Signature(7, BioForm.Gel, 0, "steel", 9, washed: true));
    }

    /// <summary>A species whose sample is toxic on any world: it yields a poison gland.</summary>
    private static CreatureSpecies Species(string id, CreatureDropKind drop, int voiceSeed) => new()
    {
        Id = id,
        Name = "Test " + id,
        VoiceSeed = voiceSeed,
        DropKind = drop,
        DropItem = drop == CreatureDropKind.Poison ? "toxic_gland" : "raw_meat",
    };

    private static (bool Failed, string Item) Outcome(Compound compound)
        => (compound.Failed, compound.Failed ? string.Empty : BioItems.PreparationItem(compound));

    [Fact]
    public void AWashedMix_IsItsOwnExperiment_TheBookAndTheAnswerSayWhichOneItWas()
    {
        var transport = new RecordingTransport();
        var server = NewServer("wash", transport: transport);
        var p = Player(server, "Chemist", AtTheLab);
        server.World.SetBlock(new Vector3i(2, 200, 0), Block(BioItems.Lab));
        Assert.Equal(0, p.State.Inventory.Add("carbon", 5, 1024));
        uint seed = server.GiveCreatureSampleForTest(p, Species("venom", CreatureDropKind.Poison, 4242), 8);
        var profile = server.BioProfileForTest(seed)!;
        Assert.True(profile.Toxicity > 0, "the test needs a toxic sample");
        var mix = new BioLabIntent { Action = BioLabIntent.Mix, Sample = seed };

        var unwashed = Outcome(Synthesis.Compute(new SynthesisInput { Active = profile, Form = BioForm.Injector }));
        var washed = Outcome(Synthesis.Compute(new SynthesisInput { Active = profile, ActiveCleaned = true, Form = BioForm.Injector }));
        Assert.NotEqual(unwashed, washed); // the wash changes what comes out — which is why the book must tell the two apart
        string plainSignature = Synthesis.Signature(seed, BioForm.Injector, 0, string.Empty, 0);
        string washedSignature = Synthesis.Signature(seed, BioForm.Injector, 0, string.Empty, 0, washed: true);

        // No detoxifier in reach: the unwashed mix, under the unwashed signature, and no carbon is touched.
        server.BioLabForTest(p, mix);
        var first = SentTo<BioLabResult>(transport, p).Last();
        Assert.False(first.Washed);
        Assert.Equal(unwashed, (first.Failed, first.ItemKey));
        Assert.Equal(5, p.State.Inventory.CountOf("carbon"));
        Assert.Equal(new[] { plainSignature }, server.BioReactionsForTest("Chemist"));

        // A detoxifier stands by: the same inputs are now the washed mix — its own entry, and the answer says so.
        server.World.SetBlock(new Vector3i(-2, 200, 0), Block("detoxifier"));
        server.BioLabForTest(p, mix);
        var second = SentTo<BioLabResult>(transport, p).Last();
        Assert.True(second.Washed);
        Assert.Equal(washed, (second.Failed, second.ItemKey));
        Assert.Equal(4, p.State.Inventory.CountOf("carbon"));
        Assert.Equal(new[] { plainSignature, washedSignature }, server.BioReactionsForTest("Chemist").OrderBy(s => s.Length));
        Assert.Contains(SentTo<BioBook>(transport, p), book => book.Reactions.Contains(washedSignature));

        // The same inputs, washed the same way, give the same result again — an experiment is never a gamble.
        server.BioLabForTest(p, mix);
        var third = SentTo<BioLabResult>(transport, p).Last();
        Assert.True(third.Washed);
        Assert.Equal((second.Failed, second.ItemKey), (third.Failed, third.ItemKey));
        Assert.Equal(2, server.BioReactionsForTest("Chemist").Count);

        // Without carbon the detoxifier washes nothing: the unwashed mix again, known already.
        Assert.True(p.State.Inventory.Remove("carbon", p.State.Inventory.CountOf("carbon")));
        server.BioLabForTest(p, mix);
        var fourth = SentTo<BioLabResult>(transport, p).Last();
        Assert.False(fourth.Washed);
        Assert.Equal(unwashed, (fourth.Failed, fourth.ItemKey));
        Assert.Equal(2, server.BioReactionsForTest("Chemist").Count);
    }

    [Fact]
    public void ACleanSample_IsNeverWashed_HoweverCloseTheDetoxifierStands()
    {
        var transport = new RecordingTransport();
        var server = NewServer("clean", transport: transport);
        var p = Player(server, "Chemist", AtTheLab);
        server.World.SetBlock(new Vector3i(2, 200, 0), Block(BioItems.Lab));
        server.World.SetBlock(new Vector3i(-2, 200, 0), Block("detoxifier"));
        Assert.Equal(0, p.State.Inventory.Add("carbon", 5, 1024));
        uint seed = server.GiveCreatureSampleForTest(p, Species("grazer", CreatureDropKind.Food, 777), 2);
        Assert.Equal(0, server.BioProfileForTest(seed)!.Toxicity);

        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Mix, Sample = seed });

        var result = SentTo<BioLabResult>(transport, p).Last();
        Assert.True(result.Success);
        Assert.False(result.Washed);
        Assert.Equal(5, p.State.Inventory.CountOf("carbon"));
        Assert.Equal(new[] { Synthesis.Signature(seed, BioForm.Injector, 0, string.Empty, 0) }, server.BioReactionsForTest("Chemist"));
    }

    // ---------------- 2. The Sandbox catalog ----------------

    [Fact]
    public void NeedsPayload_IsTrueOnlyForALabItemThatCarriesNothing()
    {
        foreach (string blank in BlankLabItems)
        {
            Assert.True(BioItems.NeedsPayload(blank), blank);
        }

        // With what they carry they are real items.
        Assert.False(BioItems.NeedsPayload(ItemKey.WithSeed(BioItems.Sample, 0x00AB12CD)));
        Assert.False(BioItems.NeedsPayload(ItemKey.WithSeed(BioItems.MineralSample, 1)));
        Assert.False(BioItems.NeedsPayload(ItemKey.WithSeed(BioItems.Seedling, 0xFFFFFFFF)));
        Assert.False(BioItems.NeedsPayload(Prep(BioEffect.Speed, 8)));
        Assert.False(BioItems.NeedsPayload(Prep(BioEffect.Strength, 3, form: BioForm.Coating)));

        // A payload that is none: seed 0, too short, a compound the lab could never make, another tag only.
        Assert.True(BioItems.NeedsPayload("bio_sample#x00000000"));
        Assert.True(BioItems.NeedsPayload("seedling#x12"));
        Assert.True(BioItems.NeedsPayload(UnknownEffectKey));
        Assert.True(BioItems.NeedsPayload("prep_gel#x" + Payload(effect: 1, level: 0)));
        Assert.True(BioItems.NeedsPayload("mineral_sample#t112233"));

        // Nothing else ever needs one — the rule names exactly the eight lab items of the real content.
        Assert.False(BioItems.NeedsPayload(null));
        Assert.False(BioItems.NeedsPayload(string.Empty));
        Assert.False(BioItems.NeedsPayload("stone#t112233"));
        Assert.Equal(
            BlankLabItems.OrderBy(k => k, StringComparer.Ordinal),
            Content.Items.Keys.Where(BioItems.NeedsPayload).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void TheSandboxCatalog_NeverHandsOutABlankLabItem_ButStillEverythingElse()
    {
        var transport = new RecordingTransport();
        var server = NewServer("catalog", tune: c => c.Rules.GameMode = GameMode.Creative, transport: transport);
        var p = Player(server, "Builder", AtTheLab);

        foreach (string blank in BlankLabItems)
        {
            server.CreativeTakeItemForTest("Builder", blank, 3);
            Assert.False(Holds(p, blank), $"the catalog must not hand out a blank {blank}");
        }

        server.CreativeTakeItemForTest("Builder", UnknownEffectKey, 1); // a payload the lab could never make is a blank too
        Assert.False(Holds(p, "prep_injector"));
        Assert.Equal(BlankLabItems.Length + 1, Refusals(transport, p, "@srv.catalog.needs_content"));
        Assert.DoesNotContain(server.Ship.Cargo.Slots, s => s is { IsEmpty: false } && BioItems.NeedsPayload(s.Item)); // nor into the hold

        // What carries something is an item like any other — and so is the lab itself.
        string preparation = Prep(BioEffect.Speed, 8);
        string seedling = ItemKey.WithSeed(BioItems.Seedling, 0x1234ABCD);
        server.CreativeTakeItemForTest("Builder", preparation, 2);
        server.CreativeTakeItemForTest("Builder", seedling, 1);
        server.CreativeTakeItemForTest("Builder", BioItems.Lab, 1);
        server.CreativeTakeItemForTest("Builder", BioItems.Sampler, 1);
        Assert.Equal(2, p.State.Inventory.CountOf(preparation));
        Assert.Equal(1, p.State.Inventory.CountOf(seedling));
        Assert.Equal(1, p.State.Inventory.CountOf(BioItems.Lab));
        Assert.Equal(1, p.State.Inventory.CountOf(BioItems.Sampler));
    }

    // ---------------- 3. Taking a preparation that carries nothing ----------------

    [Fact]
    public void ABlankPreparation_IsRefusedAndKept_NeverEatenAsFood()
    {
        var transport = new RecordingTransport();
        var server = NewServer("blank", transport: transport);
        const string Coating = "prep_coating";
        const string Broken = "prep_capsule#x6350008000"; // a payload, but no compound the lab could have made
        var p = Player(server, "Taster", AtTheLab, "prep_injector", "prep_bar", Broken, Coating, "berries");
        p.State.Hunger = 40f;
        p.State.Health = 80f;

        foreach (string key in new[] { "prep_injector", "prep_bar", Broken, Coating })
        {
            server.ConsumeItem("Taster", key);
            Assert.Equal(1, p.State.Inventory.CountOf(key)); // not used up
        }

        Assert.Empty(p.State.Effects);
        Assert.Equal(40f, p.State.Hunger); // a blank bar does not feed either
        Assert.Equal(80f, p.State.Health);
        Assert.Equal(4, Refusals(transport, p, "@srv.bio.prep_empty"));
        Assert.Equal(0, Refusals(transport, p, "@srv.misc.not_consumable"));

        // Ordinary food is still food …
        server.ConsumeItem("Taster", "berries");
        Assert.Equal(0, p.State.Inventory.CountOf("berries"));
        Assert.True(p.State.Hunger > 40f);

        // … and a real bar still starts its effect and feeds.
        float hunger = p.State.Hunger;
        string bar = Prep(BioEffect.Jump, 5, form: BioForm.Bar);
        Assert.Equal(0, p.State.Inventory.Add(bar, 1, 1));
        server.ConsumeItem("Taster", bar);
        Assert.Equal(0, p.State.Inventory.CountOf(bar));
        Assert.Equal(BioEffect.Jump, Assert.Single(p.State.Effects).Effect);
        Assert.True(p.State.Hunger > hunger);
    }

    // ---------------- 4. A key is only read when the lab could have made it ----------------

    [Fact]
    public void AKeyTheLabCouldNeverMake_IsNoCompound()
    {
        Assert.NotNull(Read(Payload(effect: (int)BioEffect.Speed, level: 8)));
        Assert.Null(BioItems.CompoundOf(UnknownEffectKey));

        // The effect: every number 0..255 — only a defined effect other than None is read.
        for (int effect = 0; effect <= 255; effect++)
        {
            bool known = effect != 0 && Enum.IsDefined(typeof(BioEffect), (byte)effect);
            Assert.Equal(known, Read(Payload(effect, level: 5)) is not null);
        }

        // The level: 1..15.
        Assert.Null(Read(Payload(effect: 1, level: 0)));
        for (int level = 1; level <= BioRules.MaxLevel; level++)
        {
            Assert.Equal(level, Read(Payload(effect: 1, level: level))!.Level);
        }

        // The side effect and the thermal value must be ones this version knows.
        for (int side = 0; side <= 15; side++)
        {
            Assert.Equal(Enum.IsDefined(typeof(BioSideEffect), (byte)side), Read(Payload(1, 5, side: side, sideLevel: 1)) is not null);
        }

        for (int thermal = 0; thermal <= 3; thermal++)
        {
            Assert.Equal(Enum.IsDefined(typeof(BioThermal), (byte)thermal), Read(Payload(1, 5, thermal: thermal)) is not null);
        }

        // A second effect: none with level 0, or a known one with a level in range — nothing in between.
        for (int secondary = 0; secondary <= 255; secondary++)
        {
            bool known = secondary != 0 && Enum.IsDefined(typeof(BioEffect), (byte)secondary);
            Assert.Equal(known, Read(Payload(1, 5, secondary: secondary, secondaryLevel: 4)) is not null);
            Assert.Equal(secondary == 0, Read(Payload(1, 5, secondary: secondary, secondaryLevel: 0)) is not null);
        }
    }

    [Fact]
    public void ADurationBeyondTheLabsLimits_IsCutBackToThem()
    {
        Assert.Equal(120, Read(Payload(1, 5, units: 8))!.DurationSeconds);                     // inside the limits: as written
        Assert.Equal(BioRules.MaxDuration, Read(Payload(1, 5, units: 255))!.DurationSeconds);  // "63 minutes" is ten
        Assert.Equal(BioRules.MaxDuration, Read(Payload(1, 5, units: 40))!.DurationSeconds);
        Assert.Equal(BioRules.MinDuration, Read(Payload(1, 5, units: 0))!.DurationSeconds);
        Assert.Equal(BioRules.MinDuration, Read(Payload(1, 5, units: 1))!.DurationSeconds);

        // Stealth — as the effect or as the second effect — lasts a minute at most.
        int stealth = (int)BioEffect.Stealth;
        Assert.Equal(BioRules.StealthMaxDuration, Read(Payload(stealth, 5, units: 40))!.DurationSeconds);
        Assert.Equal(BioRules.StealthMaxDuration, Read(Payload(1, 5, units: 255, secondary: stealth, secondaryLevel: 3))!.DurationSeconds);
        Assert.Equal(45, Read(Payload(stealth, 5, units: 3))!.DurationSeconds);
        Assert.Equal(BioRules.MinDuration, Read(Payload(stealth, 5, units: 0))!.DurationSeconds);
    }

    [Fact]
    public void WhateverAKeySays_WhatIsReadStaysInsideTheLabsLimits()
    {
        // 300 000 keys nobody made (a fixed sequence, the same on every machine): most are refused, and what is read
        // could have come out of the lab.
        ulong x = 0x9E3779B97F4A7C15UL;
        int read = 0;
        for (int i = 0; i < 300_000; i++)
        {
            x = (x * 6364136223846793005UL) + 1442695040888963407UL;
            string payload = ((x >> 20) & 0xFFFFFFFFFFUL).ToString("x10", CultureInfo.InvariantCulture);
            if (Read(payload) is not { } c)
            {
                continue;
            }

            read++;
            Assert.True(c.Effect != BioEffect.None && Enum.IsDefined(typeof(BioEffect), c.Effect), payload);
            Assert.True(Enum.IsDefined(typeof(BioSideEffect), c.Side), payload);
            Assert.True(Enum.IsDefined(typeof(BioThermal), c.Thermal), payload);
            Assert.InRange(c.Level, 1, BioRules.MaxLevel);
            Assert.InRange(c.SideLevel, 0, 3);
            Assert.InRange(c.DurationSeconds, BioRules.MinDuration, BioRules.MaxDuration);
            if (c.Effect == BioEffect.Stealth || c.Secondary == BioEffect.Stealth)
            {
                Assert.InRange(c.DurationSeconds, BioRules.MinDuration, BioRules.StealthMaxDuration);
            }

            if (c.Secondary == BioEffect.None)
            {
                Assert.Equal(0, c.SecondaryLevel);
            }
            else
            {
                Assert.True(Enum.IsDefined(typeof(BioEffect), c.Secondary), payload);
                Assert.InRange(c.SecondaryLevel, 1, BioRules.MaxLevel);
            }
        }

        Assert.True(read > 0, "the sequence must reach readable keys too, or the test checks nothing");
    }

    private static void AssertRoundTrips(Compound made)
    {
        if (made.Failed)
        {
            return;
        }

        string key = BioItems.PreparationItem(made);
        var back = BioItems.CompoundOf(key);
        Assert.NotNull(back); // the lab made it: its key must read back
        Assert.Equal(made.Form, back!.Form);
        Assert.Equal(made.Effect, back.Effect);
        Assert.Equal(made.Level, back.Level);
        Assert.Equal(made.Side, back.Side);
        Assert.Equal(made.SideLevel, back.SideLevel);
        Assert.Equal(made.Thermal, back.Thermal);
        Assert.Equal(made.DurationSeconds, back.DurationSeconds);
        Assert.Equal(made.Secondary, back.Secondary);
        Assert.Equal(made.SecondaryLevel, back.SecondaryLevel);
        Assert.False(BioItems.NeedsPayload(key));
    }

    [Fact]
    public void EverythingTheMixerMakes_ReadsBackFromItsKey_ExactlyAsItWasMade()
    {
        var forms = (BioForm[])Enum.GetValues(typeof(BioForm));
        var thermals = (BioThermal[])Enum.GetValues(typeof(BioThermal));
        var sides = (BioSideEffect[])Enum.GetValues(typeof(BioSideEffect));
        var stabilisers = new[]
        {
            null,
            Material(5, (MatTrait.Hard, 3), (MatTrait.HeatProof, 2), (MatTrait.ColdProof, 2), (MatTrait.Conductive, 1), (MatTrait.Light, 1), (MatTrait.Magnetic, 1)),
            Material(1, (MatTrait.Unstable, 3)),
        };
        var toxicities = new[] { (Toxicity: 0, Cleaned: false), (Toxicity: 1, Cleaned: false), (Toxicity: 2, Cleaned: false), (Toxicity: 3, Cleaned: true) };
        int made = 0;

        // The rules, corner by corner: every effect, the ends of the level range, every catch, every way of taking the
        // weather, every form, toxic and washed, with and without a stabiliser, and every reaction with a second sample.
        foreach (var effect in BioRules.Effects)
        {
            foreach (int level in new[] { 1, 9, BioRules.MaxLevel })
            {
                foreach (var side in sides)
                {
                    foreach (var thermal in thermals)
                    {
                        foreach (var (toxicity, cleaned) in toxicities)
                        {
                            var active = new BioProfile
                            {
                                Seed = 1,
                                Effect = effect,
                                Level = level,
                                Group = 0,
                                Side = side,
                                SideLevel = side == BioSideEffect.None ? 0 : 1 + ((int)side % 3),
                                Toxicity = toxicity,
                                Carrier = (BioCarrier)((int)effect % 4),
                                DurationSeconds = BioRules.Durations[(int)effect % BioRules.Durations.Length],
                                Thermal = thermal,
                            };

                            for (int group = 0; group < BioRules.Groups; group++)
                            {
                                foreach (var second in new[] { effect, BioEffect.Stealth, BioEffect.Jump })
                                {
                                    // Group 0 against group 0 is "no second sample"; once is enough for it.
                                    if (group == 0 && second != effect)
                                    {
                                        continue;
                                    }

                                    var modifier = group == 0
                                        ? null
                                        : new BioProfile { Seed = 2, Effect = second, Level = 1 + ((group * 5) % BioRules.MaxLevel), Group = group, Toxicity = group % 4, DurationSeconds = 120 };
                                    foreach (var stabiliser in stabilisers)
                                    {
                                        foreach (var form in forms)
                                        {
                                            var compound = Synthesis.Compute(new SynthesisInput
                                            {
                                                Active = active,
                                                ActiveCleaned = cleaned,
                                                Form = form,
                                                Stabiliser = stabiliser,
                                                Modifier = modifier,
                                                ModifierCleaned = cleaned,
                                            });
                                            AssertRoundTrips(compound);
                                            made += compound.Failed ? 0 : 1;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // And what the game really derives: species profiles of many seeds and contexts, mixed with each other.
        var contexts = new[] { BioTag.None, BioTag.Lava | BioTag.Hot, BioTag.Cold | BioTag.Water, BioTag.Skittish | BioTag.Nocturnal, BioTag.Titan | BioTag.Big };
        for (uint seed = 1; seed <= 4000; seed++)
        {
            var context = new BioContext
            {
                Kind = (BioKind)(seed % 2),
                Tags = (ulong)contexts[seed % contexts.Length],
                RarityPoints = (int)(seed % 12),
                Toxicity = (int)(seed % 4),
                Carrier = (BioCarrier)(seed % 4),
            };
            var active = BioProfiles.Derive(seed * 2654435761u, context);
            var modifier = BioProfiles.Derive((seed + 17) * 40503u, context);
            foreach (var form in forms)
            {
                AssertRoundTrips(Synthesis.Compute(new SynthesisInput { Active = active, Form = form }));
                AssertRoundTrips(Synthesis.Compute(new SynthesisInput { Active = active, ActiveCleaned = true, Form = form, Modifier = modifier, ModifierCleaned = true }));
            }
        }

        Assert.True(made > 100_000, $"the sweep must really make compounds (made {made})");
    }

    [Fact]
    public void EveryChangeTheLabMakes_ReadsBackFromItsKey_ExactlyAsItWasMade()
    {
        var coatings = new List<Compound?> { null };
        foreach (var effect in BioRules.Effects)
        {
            foreach (int level in new[] { 1, 6, 11, BioRules.MaxLevel })
            {
                coatings.Add(new Compound { Form = BioForm.Coating, Effect = effect, Level = level });
            }
        }

        var flags = new[] { false, true };
        int changed = 0;
        foreach (bool tool in flags)
        {
            foreach (bool hasCooldown in flags)
            {
                foreach (bool hasRange in flags)
                {
                    foreach (bool usesEnergy in flags)
                    {
                        for (int trait = 1; trait < MaterialProfile.TraitCount; trait++)
                        {
                            for (int traitLevel = 1; traitLevel <= 4; traitLevel++)
                            {
                                for (int purity = 1; purity <= 5; purity++)
                                {
                                    foreach (var extra in new[] { MatTrait.None, MatTrait.Light, MatTrait.Unstable })
                                    {
                                        var material = extra == MatTrait.None || (int)extra == trait
                                            ? Material(purity, ((MatTrait)trait, traitLevel))
                                            : Material(purity, ((MatTrait)trait, traitLevel), (extra, 1));
                                        foreach (var coating in coatings)
                                        {
                                            var mods = ItemModRules.Compute(tool, hasCooldown, hasRange, usesEnergy, material, coating);
                                            string key = mods.ApplyTo("titanium_drill");
                                            Assert.Equal(mods, ItemMods.Of(key));
                                            Assert.Equal(mods.IsEmpty, key == "titanium_drill");
                                            changed += mods.IsEmpty ? 0 : 1;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        Assert.True(changed > 10_000, $"the sweep must really make changes (made {changed})");
    }

    [Fact]
    public void AChangeThatNamesAnUnknownValue_IsNoChangeAtAll()
    {
        var drill = Content.GetItem("titanium_drill")!;
        for (int first = 0; first <= 15; first++)
        {
            for (int second = 0; second <= 15; second++)
            {
                for (int drawback = 0; drawback <= 15; drawback++)
                {
                    string key = string.Create(CultureInfo.InvariantCulture, $"titanium_drill#u{first:x1}3{second:x1}2{drawback:x1}1");
                    var mods = ItemMods.Of(key);
                    bool known = Enum.IsDefined(typeof(ModStat), (byte)first) && Enum.IsDefined(typeof(ModStat), (byte)second)
                        && Enum.IsDefined(typeof(ModStat), (byte)drawback);
                    if (known)
                    {
                        Assert.Equal(new ItemMods((ModStat)first, 3, (ModStat)second, 2, (ModStat)drawback, 1), mods);
                    }
                    else
                    {
                        // One unknown value empties the whole set: the item acts as the plain one, never as half a change.
                        Assert.True(mods.IsEmpty, key);
                        Assert.Same(drill.Tool, ToolMods.Effective(drill, key));
                        Assert.Equal(0f, GearMods.Bonus(new[] { key }, ModStat.Armor));
                    }
                }
            }
        }
    }

    // ---------------- 5. The temperature the effects feel (#2218) ----------------

    [Fact]
    public void OnlyAHeatOrColdSensitiveEffect_TakesTheWeather()
    {
        Assert.False(PlayerEffects.AnyThermal(null));
        Assert.False(PlayerEffects.AnyThermal(new List<ActiveEffect>()));
        Assert.False(PlayerEffects.AnyThermal(new List<ActiveEffect> { new() { Effect = BioEffect.HeatWard, Level = 5, SecondsLeft = 60 } }));
        Assert.True(PlayerEffects.AnyThermal(new List<ActiveEffect>
        {
            new() { Effect = BioEffect.Speed, Level = 5, SecondsLeft = 60 },
            new() { Effect = BioEffect.Jump, Level = 5, SecondsLeft = 60, Thermal = BioThermal.ColdSensitive },
        }));
        Assert.False(PlayerEffects.AnyThermal(new List<ActiveEffect> { new() { Effect = BioEffect.Jump, Level = 5, SecondsLeft = 0, Thermal = BioThermal.HeatSensitive } }));
    }

    [Theory]
    [InlineData("survival")]
    [InlineData("creative")]
    [InlineData("hazards_off")]
    [InlineData("god")]
    public void InTheRealCold_AColdSensitiveEffectRunsOutTwiceAsFast_WhateverTheHazardRulesSay(string mode)
    {
        var server = NewServer("cold_" + mode, "ice", c =>
        {
            c.Rules.GameMode = mode == "creative" ? GameMode.Creative : GameMode.Survival;
            c.Rules.EnvironmentalHazards = mode == "hazards_off" ? HazardLevel.Off : HazardLevel.Normal;
        });
        var p = Player(server, "Walker", Outside);
        p.State.GodMode = mode == "god";
        server.StartEffectForTest(p, BioEffect.Speed, 5, 100, thermal: BioThermal.ColdSensitive);
        server.StartEffectForTest(p, BioEffect.Jump, 5, 100, thermal: BioThermal.HeatSensitive);
        server.StartEffectForTest(p, BioEffect.Grip, 5, 100);

        Ticks(server, 100); // ten seconds

        Assert.True(server.AmbientTemperatureForTest(p) < BioRules.ColdBelow, "an ice world is cold outside");
        Assert.InRange(Left(p, BioEffect.Speed), 79.5f, 80.5f); // twice as fast
        Assert.InRange(Left(p, BioEffect.Jump), 89.5f, 90.5f);  // the heat-sensitive one does not mind the cold
        Assert.InRange(Left(p, BioEffect.Grip), 89.5f, 90.5f);
    }

    [Fact]
    public void InTheRealHeat_AHeatSensitiveEffectRunsOutTwiceAsFast_AlsoInSandbox()
    {
        var server = NewServer("hot", "lava", c => c.Rules.GameMode = GameMode.Creative);
        var p = Player(server, "Walker", Outside);

        // Just above the ground: the air thins and cools with altitude, and high up even a lava world is mild.
        int ground = 149;
        while (ground > 1 && server.World.GetBlock(new Vector3i(500, ground, 500)).IsAir)
        {
            ground--;
        }

        p.State.Position = new Vector3f(500.5f, ground + 3f, 500.5f);
        server.StartEffectForTest(p, BioEffect.Speed, 5, 100, thermal: BioThermal.HeatSensitive);
        server.StartEffectForTest(p, BioEffect.Jump, 5, 100, thermal: BioThermal.ColdSensitive);

        Ticks(server, 100);

        Assert.False(p.State.AboveAtmosphere);
        Assert.True(server.AmbientTemperatureForTest(p) > BioRules.HotAbove, $"a lava world is hot at the ground (read {server.AmbientTemperatureForTest(p)} °C at y {ground + 3})");
        Assert.InRange(Left(p, BioEffect.Speed), 79.5f, 80.5f);
        Assert.InRange(Left(p, BioEffect.Jump), 89.5f, 90.5f);
    }

    [Fact]
    public void AboardTheShip_TheEffectsFeelTheCabin_NotTheColdLeftOutside()
    {
        var server = NewServer("cabin", "ice");
        var p = Player(server, "Pilot", Outside);
        server.StartEffectForTest(p, BioEffect.Speed, 5, 300, thermal: BioThermal.ColdSensitive);
        Ticks(server, 30); // three seconds in the cold: the hazard scan holds its reading
        Assert.True(p.EffectiveTemperatureC < BioRules.ColdBelow);
        Assert.True(Left(p, BioEffect.Speed) < 295f, "outside, the cold eats the effect at double speed");

        // Into the ship. The hazard scan stops there and keeps its last, cold value — the effects must not read that.
        p.State.AboardShip = true;
        Ticks(server, 20); // the effects' own reading is renewed within a second and a half
        float before = Left(p, BioEffect.Speed);
        Ticks(server, 100);

        Assert.InRange(before - Left(p, BioEffect.Speed), 9.5f, 10.5f); // plain speed: the cabin is comfortable
        Assert.Equal(22f, server.AmbientTemperatureForTest(p));
        Assert.True(p.EffectiveTemperatureC < BioRules.ColdBelow, "the hazard's own cache is untouched — and stale, which is why the effects no longer read it");

        // And out again: the cold is back.
        p.State.AboardShip = false;
        Ticks(server, 20);
        before = Left(p, BioEffect.Speed);
        Ticks(server, 100);
        Assert.InRange(before - Left(p, BioEffect.Speed), 19.5f, 20.5f);
    }

    [Fact]
    public void AColdWard_CountsInTheRealCold_AHeatWardDoesNot()
    {
        var server = NewServer("ward", "ice");
        var naked = Player(server, "Naked", Outside);
        var cold = Player(server, "ColdWard", Outside);
        var heat = Player(server, "HeatWard", Outside);
        server.StartEffectForTest(cold, BioEffect.ColdWard, BioRules.MaxLevel, 600);
        server.StartEffectForTest(heat, BioEffect.HeatWard, BioRules.MaxLevel, 600);

        Ticks(server, 200); // twenty seconds

        float nakedDrain = 100f - naked.State.SuitEnergy;
        float coldDrain = 100f - cold.State.SuitEnergy;
        float heatDrain = 100f - heat.State.SuitEnergy;
        Assert.True(nakedDrain > 0.5f, $"an ice world must drain the suit (drained {nakedDrain})");
        float insulation = BioRules.Magnitude(BioEffect.ColdWard, BioRules.MaxLevel);
        Assert.InRange(coldDrain, nakedDrain * (1f - insulation) * 0.97f, nakedDrain * (1f - insulation) * 1.03f);
        Assert.InRange(heatDrain, nakedDrain * 0.97f, nakedDrain * 1.03f);
    }

    [Fact]
    public void APlayerWithoutAThermalEffect_NeverCostsATemperatureReading_AndOneWithCostsAtMostOneASecond()
    {
        // Sandbox: the hazard scan never runs, so every reading here would be one the effects asked for.
        var server = NewServer("nocost", "ice", c => c.Rules.GameMode = GameMode.Creative);
        var p = Player(server, "Walker", Outside);
        Ticks(server, 30);
        Assert.Equal(double.NegativeInfinity, p.AmbientTemperatureReadAt); // no effect at all

        server.StartEffectForTest(p, BioEffect.Speed, 5, 600);    // an effect that does not mind the weather
        server.StartEffectForTest(p, BioEffect.ColdWard, 5, 600); // a ward, but no hazard asks for insulation here
        Ticks(server, 30);
        Assert.Equal(double.NegativeInfinity, p.AmbientTemperatureReadAt);

        server.StartEffectForTest(p, BioEffect.Jump, 5, 600, thermal: BioThermal.ColdSensitive);
        var readings = new HashSet<double>();
        for (int i = 0; i < 60; i++) // six seconds
        {
            server.TickForTest(0.1);
            readings.Add(p.AmbientTemperatureReadAt);
        }

        Assert.DoesNotContain(double.NegativeInfinity, readings);
        Assert.InRange(readings.Count, 3, 6);
    }
}
