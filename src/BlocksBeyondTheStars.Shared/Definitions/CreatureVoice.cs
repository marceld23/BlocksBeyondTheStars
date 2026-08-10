// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>The offline sampler op a species' voice is rendered with (#903). Each is a cheap
/// <c>float[] → float[]</c> pass the client bakes into a new AudioClip — Unity Web silently ignores
/// audio filter components, so every effect has to be baked rather than attached (#878).</summary>
public enum VoiceOp
{
    None,        // the dry sample
    Drive,       // soft-clip saturation — hostile, throaty
    Tremolo,     // amplitude modulation — alien warble
    Comb,        // feed-forward comb — metallic / insectoid resonance
    Dull,        // one-pole low-pass — muffled, breathy
    Thin,        // one-pole high-pass — small, papery
    Crush,       // bit + rate reduction — chittery, artificial
    ReverseTail, // a reversed, quieter echo of the sample after it — alien, keeps the attack
    Shape,       // envelope reshape — a clipped bark, or a slow drawn-out swell
}

/// <summary>How the pitch moves across a species' call phrase (#902).</summary>
public enum VoiceContour
{
    Flat,
    Rising,
    Falling,
    Bowl, // down then up
    Arch, // up then down
}

/// <summary>
/// A generated species' voice — the audible counterpart to its generated body (#902/#903/#904).
/// Derived deterministically from <see cref="CreatureSpecies.VoiceSeed"/> plus the traits the
/// species already has, so the sound reads as coming from <em>that</em> animal: eyeless cave
/// dwellers click like echolocators, titans bellow slowly, medusae barely make a sound.
/// </summary>
public readonly struct CreatureVoice
{
    public CreatureVoice(string call, string layerCall, int layerOffsetMs, int layerDetune,
        VoiceOp op, int opAmount, int tail, int pulses, int pulseGapMs, VoiceContour contour,
        float cadenceMin, float cadenceMax, int pitchStep)
    {
        PitchStep = pitchStep;
        Call = call;
        LayerCall = layerCall;
        LayerOffsetMs = layerOffsetMs;
        LayerDetune = layerDetune;
        Op = op;
        OpAmount = opAmount;
        Tail = tail;
        Pulses = pulses;
        PulseGapMs = pulseGapMs;
        Contour = contour;
        CadenceMin = cadenceMin;
        CadenceMax = cadenceMax;
    }

    /// <summary>The base sample id (a <c>creature_call_*</c> clip).</summary>
    public string Call { get; }

    /// <summary>A second sample mixed in behind the base one, or empty for none (#904).</summary>
    public string LayerCall { get; }

    /// <summary>How far behind the base sample the layer sits, in milliseconds.</summary>
    public int LayerOffsetMs { get; }

    /// <summary>Quantised detune step (0–2) applied to the layer.</summary>
    public int LayerDetune { get; }

    public VoiceOp Op { get; }

    /// <summary>Quantised op intensity (0–2). Quantised on purpose: a continuous amount would give
    /// every species its own bake and the sampler cache could never hit (#901).</summary>
    public int OpAmount { get; }

    /// <summary>Quantised reverb-tail amount (0–2), independent of the cave-echo flag.</summary>
    public int Tail { get; }

    /// <summary>How many one-shots the species' call phrase is made of (1–7).</summary>
    public int Pulses { get; }

    /// <summary>The gap between phrase pulses, in milliseconds.</summary>
    public int PulseGapMs { get; }

    public VoiceContour Contour { get; }

    /// <summary>Seconds between phrases (the species' calling rate) — drawn per utterance.</summary>
    public float CadenceMin { get; }

    public float CadenceMax { get; }

    /// <summary>The species' fixed pitch offset step (0–36), combined with its size by the client. Kept
    /// here so the whole voice comes from one place rather than a second hash at the call site.</summary>
    public int PitchStep { get; }

    /// <summary>True when this voice needs a baked variant at all (dry + no tail + no layer = play the
    /// sample as-is, which costs nothing).</summary>
    public bool NeedsBake => Op != VoiceOp.None || Tail > 0 || LayerCall.Length > 0;

    /// <summary>The sampler cache key for this voice's baked variant. Every component is DISCRETE — that
    /// is the point (#901): one continuous value in here and no two species could ever share a bake, so a
    /// planet-hopping session would grow the cache without limit. The key space is still large; what keeps
    /// memory flat is that a single planet's roster needs only a few dozen of them, under the client's LRU
    /// cap, and that the whole cache is dropped on world teardown.</summary>
    public string BakeKey =>
        $"{Call}|{LayerCall}|{LayerOffsetMs}|{LayerDetune}|{(int)Op}|{OpAmount}|{Tail}";
}

/// <summary>
/// Derives a <see cref="CreatureVoice"/> from a species' seed and traits (#902–#905). Lives in Shared
/// so the server can roll it at generation time and the client can render it from the same rules.
///
/// Two properties matter and are load-bearing:
/// <list type="bullet">
/// <item>Deterministic and platform-stable — the hash is an explicit FNV/mix, never
/// <c>string.GetHashCode</c>, which .NET randomises per process (#720).</item>
/// <item>Every value that reaches a sampler cache key is quantised and drawn from a finite set, so
/// species can share bakes and a planet's working set fits the client's LRU cache (#901).</item>
/// </list>
/// </summary>
public static class CreatureVoices
{
    // Signature idle calls, grouped by the character they read as. A species picks from its habitat's
    // pool, narrowed to the class its body suggests — a titan does not squeak.
    private static readonly string[] Deep =
    {
        "creature_call_bellow", "creature_call_rumble", "creature_call_moan", "creature_call_growl",
        "creature_call_drone", "creature_call_thrum", "creature_call_wail", "creature_call_snarl",
        "creature_call_hoot",
    };

    private static readonly string[] High =
    {
        "creature_call_chirp", "creature_call_squeak", "creature_call_whistle", "creature_call_trill",
        "creature_call_screech", "creature_call_keen", "creature_call_yelp", "creature_call_click",
        "creature_call_chitter", "creature_call_cluck",
    };

    private static readonly string[] Wet =
    {
        "creature_call_croak", "creature_call_gurgle", "creature_call_burble", "creature_call_warble",
        "creature_call_purr",
    };

    private static readonly string[] Sibilant = { "creature_call_hiss", "creature_call_sizzle" };

    // Habitat-flavoured pools: cave dwellers sound deep + echoey, amphibians wet + croaky, water
    // creatures burble, lava critters hiss/rumble, fliers shriek/trill. Land uses the full pool.
    private static readonly string[] LandCalls =
    {
        "creature_call_chirp", "creature_call_croak", "creature_call_growl", "creature_call_screech",
        "creature_call_warble", "creature_call_hoot", "creature_call_trill", "creature_call_click",
        "creature_call_rumble", "creature_call_bellow", "creature_call_hiss", "creature_call_chitter",
        "creature_call_purr", "creature_call_moan", "creature_call_squeak", "creature_call_drone",
        "creature_call_gurgle", "creature_call_yelp", "creature_call_snarl", "creature_call_whistle",
        "creature_call_cluck", "creature_call_wail",
        // #906 — land top-up so the biggest pool keeps pace with the multipliers.
        "creature_call_bark", "creature_call_grunt", "creature_call_caw",
    };

    private static readonly string[] CaveCalls =
    {
        "creature_call_moan", "creature_call_drone", "creature_call_wail", "creature_call_hoot",
        "creature_call_whistle", "creature_call_click", "creature_call_thrum",
        // #906
        "creature_call_knock", "creature_call_echo_click", "creature_call_groan", "creature_call_flutter",
    };

    private static readonly string[] AmphibianCalls =
    {
        "creature_call_croak", "creature_call_gurgle", "creature_call_warble", "creature_call_trill",
        "creature_call_cluck", "creature_call_burble",
        // #906
        "creature_call_ribbit", "creature_call_slurp", "creature_call_peep", "creature_call_bubble_pop",
    };

    private static readonly string[] WaterCalls =
    {
        "creature_call_gurgle", "creature_call_warble", "creature_call_click", "creature_call_whistle",
        "creature_call_burble",
        // #906 — this was the most starved pool of all (five voices for every ocean in the game).
        "creature_call_sonar", "creature_call_bubble_pop", "creature_call_moo_deep", "creature_call_squelch",
        "creature_call_chime_wet",
    };

    private static readonly string[] LavaCalls =
    {
        "creature_call_hiss", "creature_call_rumble", "creature_call_growl", "creature_call_snarl",
        "creature_call_sizzle",
        // #906
        "creature_call_crackle", "creature_call_roar_low", "creature_call_ember_hiss", "creature_call_grind",
    };

    private static readonly string[] AirCalls =
    {
        "creature_call_screech", "creature_call_whistle", "creature_call_trill", "creature_call_chirp",
        "creature_call_warble", "creature_call_keen",
        // #906
        "creature_call_caw", "creature_call_shriek", "creature_call_flutter", "creature_call_pipe",
    };

    /// <summary>Every call id the voice system can pick — used by the client to pre-fill/verify its
    /// clip table and by tests to assert the pools stay in sync with the shipped assets.</summary>
    public static IReadOnlyList<string> AllCalls
    {
        get
        {
            var set = new List<string>();
            foreach (var pool in new[] { LandCalls, CaveCalls, AmphibianCalls, WaterCalls, LavaCalls, AirCalls })
            {
                foreach (var name in pool)
                {
                    if (!set.Contains(name))
                    {
                        set.Add(name);
                    }
                }
            }

            return set;
        }
    }

    /// <summary>Derives the species' voice. <paramref name="seed"/> must be unique per species per
    /// world — <see cref="CreatureSpecies.Id"/> alone is NOT ("sp0" repeats on every planet), which is
    /// why the generator carries a real sub-seed instead.
    /// <paramref name="available"/> lets the client exclude sample ids it has no clip for, so a partial
    /// asset set degrades to a different voice instead of a silent animal; the server passes null.</summary>
    public static CreatureVoice Derive(int seed, VoiceTraits t, System.Func<string, bool>? available = null)
    {
        var pool = Filter(PoolFor(t.Habitat), available);
        var candidates = Filter(NarrowByBody(pool, t), available);
        string call = Rendezvous(seed, "call", candidates);

        // Layer partner (#904): a second sample behind the first is the only true timbre multiplier —
        // it turns N voices into N². Small species stay unlayered so little animals read crisp.
        string layer = string.Empty;
        int layerOffset = 0, layerDetune = 0;
        if (t.Size >= 0.9f && Roll01(seed, "layer") < 0.38f)
        {
            var others = Without(candidates.Count > 2 ? candidates : pool, call);

            if (others.Count > 0)
            {
                layer = Rendezvous(seed, "layer.pick", others);
                layerOffset = new[] { 45, 70, 100 }[Roll(seed, "layer.off", 3)];
                layerDetune = Roll(seed, "layer.detune", 3);
            }
        }

        var op = PickOp(seed, t);
        int amount = Roll(seed, "op.amount", 3);
        int tail = PickTail(seed, t);
        int pulses = PickPulses(seed, t);
        int gap = PickGap(seed, t);
        var contour = PickContour(seed, t);
        var (cadMin, cadMax) = PickCadence(seed, t, pulses);

        return new CreatureVoice(call, layer, layerOffset, layerDetune, op, amount, tail,
            pulses, gap, contour, cadMin, cadMax, Roll(seed, "pitch", 37));
    }

    /// <summary>The locale key describing this voice on a scan (#907) — the voice becomes a readable
    /// trait like colour instead of a noise the player can only recognise subconsciously.</summary>
    public static string DescriptorKey(CreatureVoice v)
    {
        if (v.Pulses >= 4 && v.PulseGapMs <= 130)
        {
            return "ui.scan.voice.clicks";
        }

        if (v.CadenceMin >= 20f)
        {
            return "ui.scan.voice.drone";
        }

        if (Contains(Deep, v.Call))
        {
            return v.CadenceMin >= 11f ? "ui.scan.voice.bellow" : "ui.scan.voice.rumble";
        }

        if (Contains(Sibilant, v.Call) || v.Op == VoiceOp.Drive)
        {
            return "ui.scan.voice.rasp";
        }

        if (Contains(Wet, v.Call))
        {
            return "ui.scan.voice.croak";
        }

        if (v.Op == VoiceOp.Tremolo)
        {
            return "ui.scan.voice.warble";
        }

        if (v.Pulses >= 3)
        {
            return "ui.scan.voice.chatter";
        }

        return Contains(High, v.Call) ? "ui.scan.voice.shrill" : "ui.scan.voice.call";
    }

    /// <summary>The habitat's full call pool.</summary>
    public static IReadOnlyList<string> PoolFor(string habitat) => (habitat ?? "Land").ToLowerInvariant() switch
    {
        "cave" => CaveCalls,
        "amphibian" => AmphibianCalls,
        "water" => WaterCalls,
        "lava" => LavaCalls,
        "air" => AirCalls,
        _ => LandCalls,
    };

    /// <summary>Drops candidates the caller has no sample for. Never returns an empty list: if the
    /// predicate rejects everything we keep the unfiltered set, because a wrong-sounding animal is a far
    /// better failure than a silent one.</summary>
    private static List<string> Filter(IReadOnlyList<string> pool, System.Func<string, bool>? available)
    {
        var list = new List<string>(pool.Count);
        foreach (var name in pool)
        {
            if (available == null || available(name))
            {
                list.Add(name);
            }
        }

        if (list.Count == 0)
        {
            list.AddRange(pool);
        }

        return list;
    }

    /// <summary>Narrows a habitat pool to the calls a body that shape could plausibly produce. Falls
    /// back to the full pool whenever the intersection is too thin to give any choice.</summary>
    private static List<string> NarrowByBody(IReadOnlyList<string> pool, VoiceTraits t)
    {
        string[]? want = null;
        if (t.BodyPlan == "Titan" || t.Size >= 2.6f)
        {
            want = Deep;
        }
        else if (t.Size <= 0.85f)
        {
            want = High;
        }
        else if (t.Legs == 0 && t.BodySegments >= 3)
        {
            want = Sibilant;
        }
        else if (t.Habitat == "Amphibian" || t.Habitat == "Water")
        {
            want = Wet;
        }

        var narrowed = new List<string>();
        if (want != null)
        {
            foreach (var name in pool)
            {
                if (Contains(want, name))
                {
                    narrowed.Add(name);
                }
            }
        }

        if (narrowed.Count >= 2)
        {
            return narrowed;
        }

        var all = new List<string>(pool.Count);
        all.AddRange(pool);
        return all;
    }

    /// <summary>Rendezvous ("highest random weight") selection (#905): score every candidate as
    /// <c>hash(seed|salt|name)</c> and take the max. Unlike <c>pool[hash % pool.Length]</c>, adding a new
    /// call only re-assigns the ~1/N of species whose score for the new name happens to win — everyone
    /// else keeps the voice the player already knows.
    /// <para>Public so the pool-growth guarantee itself can be tested: the property this method exists for
    /// is invisible from <see cref="Derive"/> alone.</para></summary>
    public static string Rendezvous(int seed, string salt, List<string> candidates)
    {
        string best = candidates[0];
        uint bestScore = 0;
        foreach (var name in candidates)
        {
            uint score = Mix(seed, salt + "|" + name);
            if (score > bestScore)
            {
                bestScore = score;
                best = name;
            }
        }

        return best;
    }

    private static List<string> Without(List<string> pool, string exclude)
    {
        var list = new List<string>(pool.Count);
        foreach (var name in pool)
        {
            if (name != exclude)
            {
                list.Add(name);
            }
        }

        return list;
    }


    /// <summary>The timbre op the body suggests — a horned skull rings, a gas sac muffles, a limbless
    /// segmented body buzzes. Only the leftovers roll freely, and "no op at all" stays common so the
    /// world does not sound uniformly processed.</summary>
    private static VoiceOp PickOp(int seed, VoiceTraits t)
    {
        if (t.Temperament == "Aggressive" || t.Temperament == "PackHunter")
        {
            return VoiceOp.Drive;
        }

        if (t.BodyPlan == "Medusa" || t.Tentacles > 0)
        {
            return VoiceOp.Shape;
        }

        if (t.HasGasSac)
        {
            return VoiceOp.Dull;
        }

        if (t.Horns > 0)
        {
            return VoiceOp.Comb;
        }

        if (t.Legs == 0 || t.BodySegments >= 3)
        {
            return VoiceOp.Tremolo;
        }

        if (t.Temperament == "Skittish")
        {
            return VoiceOp.Thin;
        }

        return new[]
        {
            VoiceOp.None, VoiceOp.None, VoiceOp.None,
            VoiceOp.Crush, VoiceOp.ReverseTail, VoiceOp.Shape, VoiceOp.Comb, VoiceOp.Tremolo,
        }[Roll(seed, "op", 8)];
    }

    private static int PickTail(int seed, VoiceTraits t)
    {
        if (t.Habitat == "Cave" || t.BodyPlan == "Medusa")
        {
            return 2;
        }

        return Roll(seed, "tail", 4) == 0 ? 1 : 0; // most fauna stay dry; the odd one gets a little space
    }

    /// <summary>Phrase length. The eyeless-cave click train is the clearest example of the whole idea:
    /// a blind animal that navigates by sound is instantly recognisable, and it costs nothing but
    /// scheduling.</summary>
    private static int PickPulses(int seed, VoiceTraits t)
    {
        if (t.Eyes == 0 && (t.Habitat == "Cave" || t.Habitat == "Water"))
        {
            return 4 + Roll(seed, "pulses.echo", 4); // 4..7 rapid clicks
        }

        if (t.BodyPlan == "Medusa")
        {
            return 1;
        }

        if (t.BodyPlan == "Titan" || t.Size >= 2.6f)
        {
            return 1 + Roll(seed, "pulses.titan", 2);
        }

        if (t.Temperament == "Aggressive" || t.Temperament == "PackHunter")
        {
            return 1 + Roll(seed, "pulses.hostile", 2);
        }

        if (t.Temperament == "Skittish")
        {
            return 2;
        }

        if (t.Size <= 0.85f)
        {
            return 3 + Roll(seed, "pulses.small", 4);
        }

        return 1 + Roll(seed, "pulses", 3);
    }

    private static int PickGap(int seed, VoiceTraits t)
    {
        if (t.Eyes == 0 && (t.Habitat == "Cave" || t.Habitat == "Water"))
        {
            return new[] { 60, 80, 105 }[Roll(seed, "gap.echo", 3)];
        }

        if (t.Temperament == "Skittish" || t.Size <= 0.85f)
        {
            return new[] { 95, 130, 165 }[Roll(seed, "gap.quick", 3)];
        }

        return new[] { 150, 220, 300, 390 }[Roll(seed, "gap", 4)];
    }

    private static VoiceContour PickContour(int seed, VoiceTraits t)
    {
        if (t.Eyes == 0 && (t.Habitat == "Cave" || t.Habitat == "Water"))
        {
            return VoiceContour.Rising;
        }

        if (t.Temperament == "Aggressive" || t.Temperament == "PackHunter")
        {
            return VoiceContour.Falling;
        }

        if (t.Temperament == "Passive")
        {
            return Roll(seed, "contour.calm", 2) == 0 ? VoiceContour.Bowl : VoiceContour.Flat;
        }

        return (VoiceContour)Roll(seed, "contour", 5);
    }

    /// <summary>Seconds between phrases. This is the single most under-used species cue in the game
    /// today: before #902 every creature everywhere called every 5–12 s.</summary>
    private static (float Min, float Max) PickCadence(int seed, VoiceTraits t, int pulses)
    {
        if (t.BodyPlan == "Medusa" || t.Tentacles >= 4)
        {
            return (26f, 55f); // a drifting lantern should barely make a sound
        }

        if (t.BodyPlan == "Titan" || t.Size >= 2.6f)
        {
            return (14f, 30f);
        }

        if (t.Size <= 0.85f && pulses >= 3)
        {
            return (2.8f, 6.5f); // chatterer
        }

        if (t.Temperament == "Territorial")
        {
            return (11f, 24f);
        }

        float min = 5f + Roll(seed, "cadence", 4) * 1.5f;
        return (min, min + 7f);
    }

    // --- deterministic, platform-stable hashing (never string.GetHashCode — #720) ---

    private static uint Mix(int seed, string field)
    {
        unchecked
        {
            uint h = 2166136261u ^ (uint)seed;
            foreach (char c in field)
            {
                h ^= c;
                h *= 16777619u;
            }

            h ^= h >> 15;
            h *= 0x2c1b3c6du;
            h ^= h >> 12;
            h *= 0x297a2d39u;
            h ^= h >> 15;
            return h;
        }
    }

    private static int Roll(int seed, string field, int n) => (int)(Mix(seed, field) % (uint)n);

    private static float Roll01(int seed, string field) => (Mix(seed, field) & 0xFFFFFF) / (float)0x1000000;

    private static bool Contains(string[] set, string value)
    {
        foreach (var s in set)
        {
            if (s == value)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>The already-generated traits a voice is derived from. A plain struct rather than a
/// <see cref="CreatureSpecies"/> reference so the client can fill it straight from the wire snapshot
/// without Shared having to know about the networking types.</summary>
public readonly struct VoiceTraits
{
    public VoiceTraits(string habitat, string temperament, string bodyPlan, float size, int legs,
        int eyes, int horns, int bodySegments, int tentacles, bool hasGasSac, bool glows)
    {
        Habitat = habitat ?? "Land";
        Temperament = temperament ?? "Passive";
        BodyPlan = bodyPlan ?? "Standard";
        Size = size;
        Legs = legs;
        Eyes = eyes;
        Horns = horns;
        BodySegments = bodySegments;
        Tentacles = tentacles;
        HasGasSac = hasGasSac;
        Glows = glows;
    }

    public string Habitat { get; }

    public string Temperament { get; }

    public string BodyPlan { get; }

    public float Size { get; }

    public int Legs { get; }

    public int Eyes { get; }

    public int Horns { get; }

    public int BodySegments { get; }

    public int Tentacles { get; }

    public bool HasGasSac { get; }

    public bool Glows { get; }

    public static VoiceTraits From(CreatureSpecies sp) => new VoiceTraits(
        sp.Habitat.ToString(), sp.Temperament.ToString(), sp.BodyPlan.ToString(), sp.Size, sp.Legs,
        sp.Eyes, sp.Horns, sp.BodySegments, sp.Tentacles, sp.HasGasSac, sp.Glows);
}
