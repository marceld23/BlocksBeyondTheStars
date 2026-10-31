// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>The Feed action (#2018): throws ONE piece of the food in <see cref="Slot"/> a few blocks ahead for a begging herd.
/// The server checks that the slot holds food an animal would eat and that a begging animal is near, spills the piece as a
/// drop packet the thrower cannot pick up again for a few seconds, and sends the herd to it.</summary>
public sealed class ThrowFoodIntent
{
    public int Slot { get; set; }
}

/// <summary>
/// A live procedural creature near the player, with enough of its species descriptor for the
/// client's parametric blocky renderer to draw it (appearance) and label it (habitat/behaviour).
/// </summary>
public sealed class NetCreature
{
    public string Id { get; set; } = string.Empty;
    public string SpeciesId { get; set; } = string.Empty;
    public string NameKey { get; set; } = "creature.generic.name";
    public string Name { get; set; } = string.Empty; // coined species name (shown on scan / look)

    public bool Hostile { get; set; }
    public bool Asleep { get; set; }
    public bool Frozen { get; set; } // held in stasis (item 36) — client tints it icy blue + still

    /// <summary>#2278: dazed by a shock-glove push — it neither moves nor bites for a moment; the client draws circling
    /// stars over it. Additive: an older client ignores it and an older server leaves it false, so no protocol bump.</summary>
    public bool Staggered { get; set; }

    /// <summary>Owner player id if this is a tamed companion (empty = wild fauna). The client draws owned
    /// creatures with a friendly tint + ground ring + floating <see cref="CustomName"/> nameplate.</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>The owner's chosen name for a tamed companion (empty for wild fauna).</summary>
    public string CustomName { get; set; } = string.Empty;

    /// <summary>#3035: the companion wears a name tag — the client draws its name as a hologram over a projector ring.
    /// Additive (contractless): an older client ignores it, an older server leaves it false.</summary>
    public bool NameTag { get; set; }

    /// <summary>Companion payoff (#1210): true for a few seconds after a companion growled at a hostile in sight —
    /// the client draws the amber "!" nameplate pose. Always false for wild fauna.</summary>
    public bool Alerting { get; set; }

    public float Hull { get; set; }
    public float HullMax { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    // Species traits (string enums) + parametric appearance.
    public string Habitat { get; set; } = "Land";
    public string Activity { get; set; } = "Diurnal";
    public string Temperament { get; set; } = "Passive";
    public float Size { get; set; } = 1f;
    public int Legs { get; set; } = 4;
    public bool HasWings { get; set; }
    public bool HasTail { get; set; }
    public int BodySegments { get; set; } = 1;
    public int ColorRgb { get; set; } = 0xFFFFFF;
    public bool Glows { get; set; }
    public int Eyes { get; set; } = 2;
    public int Horns { get; set; }
    public bool HasCrest { get; set; }
    public int BellyRgb { get; set; } = 0xFFFFFF;

    /// <summary>The iris colour (packed 0xRRGGBB, #2069); 0 = the classic pale eye with a dark pupil. Additive: an older
    /// client ignores it, an older server leaves it 0.</summary>
    public int EyeRgb { get; set; }

    // item-21 morphology rest: tentacles, snail-like eyestalks, a translucent buoyancy gas-sac.
    public int Tentacles { get; set; }
    public bool EyeStalks { get; set; }
    public bool HasGasSac { get; set; }

    /// <summary>Pectoral + tail fins — a legless swimmer's only limbs. Additive field: an older client
    /// ignores it and draws the finless body it always did, and an older server leaves it false.</summary>
    public bool HasFins { get; set; }

    // Body plans (#637/#638): which architecture the client renders ("Standard" | "Medusa" | "Titan"),
    // plus the titan-only neck/trunk traits. Additive fields — older clients ignore them and draw the
    // standard body, so the wire tag is unchanged.
    public string BodyPlan { get; set; } = "Standard";
    public int NeckLength { get; set; }
    public bool HasTrunk { get; set; }

    /// <summary>The head's silhouette (#2009): "Box" (the head every species had) or one of the arachnid plan's pyramids
    /// ("Pyramid" | "Spire" | "Frustum" | "Ziggurat"). Additive: an older client draws the box; an older server sends
    /// nothing, which the client reads as the box.</summary>
    public string HeadShape { get; set; } = "Box";

    /// <summary>#2009: an arachnid ambusher sitting motionless in wait — the client holds the crouch and skips the idle
    /// flourishes. Additive; false for everything else and from an older server.</summary>
    public bool Lurking { get; set; }
    /// <summary>#2018: a herd animal begging for the food a player holds (or squabbling over a thrown piece) — the client
    /// raises the head, bounces and calls at a fast cadence. Additive; false for everything else and from an older server.</summary>
    public bool Begging { get; set; }

    /// <summary>#2081: the arms of a biped (2; 0 on every other plan) and its head size relative to the classic head. Additive:
    /// an older client ignores them; an older server leaves them at 0, which the client reads as no arms and a classic head.</summary>
    public int Arms { get; set; }
    public float HeadRatio { get; set; } = 1f;

    /// <summary>Heads / wing pairs / fin pairs (#1780-#1782, generation 6). Additive: an older client ignores them and
    /// draws the classic single head / pair / pair; an older server leaves them at 0, which the client reads as 1.</summary>
    public int Heads { get; set; } = 1;
    public int WingPairs { get; set; } = 1;
    public int FinPairs { get; set; } = 1;

    /// <summary>The hide tile the client paints the body with ("fur", "shaggy", "petal", …) for an authored species
    /// (#1763); empty = the classic id-hashed pick. Additive: an older client ignores it.</summary>
    public string Hide { get; set; } = string.Empty;

    /// <summary>The planet type key of the world this creature's look belongs to (#2868) — the client picks the hide
    /// theme from that type's data. The server fills it for every creature: a companion, a clone or a cross names its
    /// home world, every other animal the world it is on. Additive: an older client ignores it; an older server sends
    /// nothing (empty), which the client reads as "no theme" (the plain hide pools).</summary>
    public string LookPlanet { get; set; } = string.Empty;

    /// <summary>The world look seed of that world (#2868, <c>FaunaLooks.WorldLookSeed</c>) — the client derives the
    /// world's random hide motif from it. Never 0 from a current server. Additive like <see cref="LookPlanet"/>: an older
    /// client ignores it, an older server leaves it 0 (no motif sent), so the wire tag is unchanged.</summary>
    public int LookSeed { get; set; }

    /// <summary>Seed for this species' generated voice (#907) — the client derives phrase, cadence and
    /// timbre from it via <c>CreatureVoices.Derive</c>. Additive field: an older client ignores it and
    /// a newer client falls back to hashing the trait tuple when a legacy server sends 0, so the wire
    /// tag is unchanged either way.</summary>
    public int VoiceSeed { get; set; }

    /// <summary>The species' motion class (#1333): "walker" | "crawler" | "flier" | "hoverer" | "swimmer" —
    /// for an amphibian the class in effect right now. Additive field; an older client ignores it and a
    /// newer client treats a legacy server's empty value as a grounded walker.</summary>
    public string Motion { get; set; } = "walker";

    /// <summary>In the air right now: a walker mid-jump/fall, or a flier that is not perched. Drives the
    /// client's wing flap / leg tuck and its local ballistic Y integration. Additive.</summary>
    public bool Airborne { get; set; }

    /// <summary>A flier sitting on the ground (#1332) — wings folded, legs down. Additive.</summary>
    public bool Perched { get; set; }

    /// <summary>Vertical velocity (blocks/s, +up) while airborne — a ground class mid-jump/fall, or a perched
    /// flier whose perch went (#1368) — so the client can integrate the arc between the ~2 Hz position updates
    /// instead of smearing it. 0 otherwise. Additive.</summary>
    public float VertVel { get; set; }

    /// <summary>The server's ground-bird predicate (<c>CreatureMotion.Glides</c>: wings ∧ land ∧ can jump — no
    /// giants, no air species): this walker falls under reduced gravity while airborne, so the client's local
    /// arc integration uses the same glide factor the server does (#1368). Additive; a legacy server sends
    /// false, which is also what it simulates.</summary>
    public bool Glides { get; set; }

    // --- Giants (#1998, generation 9). All additive: an older client ignores them (and draws a giant as the plain
    // body its traits describe); for every other creature they stay at their defaults. ---

    /// <summary>The giant's height in blocks (0 = not a giant).</summary>
    public float GiantHeight { get; set; }

    /// <summary>Colossus: its back ("plates", "spikes", "crystals", "forest", empty) and leg length vs torso.</summary>
    public string BackFeature { get; set; } = string.Empty;
    public float LegRatio { get; set; } = 1f;

    /// <summary>Sandworm: mandible petals, body length and diameter.</summary>
    public int Mandibles { get; set; }
    public float WormLength { get; set; }
    public float WormGirth { get; set; }

    /// <summary>The server's heading (radians, dirX = cos, dirZ = sin) — a giant turns in arcs, so the client does
    /// not guess it from the ~2 Hz positions. Since #2665 filled for every creature (its locomotion heading), which the
    /// client turns a standing body toward.</summary>
    public float Facing { get; set; }

    /// <summary>#2665: the creature's real velocity over the last server tick (blocks/s), whatever moved it — the client's
    /// interpolator uses it as the curve's tangent. 0 after a teleport-sized move. Additive: an older server leaves all
    /// three at 0 (and sends no <see cref="CreatureList.ServerTime"/>, which tells the client to take tangents from the
    /// positions instead).</summary>
    public float VelX { get; set; }

    /// <inheritdoc cref="VelX"/>
    public float VelY { get; set; }

    /// <inheritdoc cref="VelX"/>
    public float VelZ { get; set; }

    /// <summary>What the giant is doing: "walk", "stand", "stomp" (colossus); "hidden", "approach", "breach", "rear"
    /// (sandworm). Empty for every other creature.</summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>Seconds already run of the current phase when this snapshot was sent, and the phase's length —
    /// the client runs the same shared curve (<c>SandwormPath</c>) or stomp timing from them.</summary>
    public float PhaseT { get; set; }
    public float PhaseDur { get; set; }

    /// <summary>The phase's anchor point: the stomp's landing spot; the breach's ground point under the top of the arc;
    /// the rear's strike target (Y = the surface there).</summary>
    public float EvX { get; set; }
    public float EvY { get; set; }
    public float EvZ { get; set; }

    /// <summary>The move's direction on the ground (unit X/Z), its peak height and — for a rear — how far before its
    /// target the tower rises. For a stomp <see cref="EvLeg"/> is the leg (0..3: front-left, front-right, rear-left,
    /// rear-right).</summary>
    public float EvDirX { get; set; }
    public float EvDirZ { get; set; }
    public float EvPeak { get; set; }
    public float EvStrike { get; set; }
    public int EvLeg { get; set; }

    /// <summary>#2565: the SPECIES size — <see cref="Size"/> carries this individual's cosmetic variance on top. The
    /// client measures the bite reach by it (<c>CreatureReach.BiteRange</c>, the server's own rule). New field on an
    /// existing contractless MessagePack message: an older client ignores it, an older server leaves it 0 (the client
    /// then falls back to <see cref="Size"/>).</summary>
    public float SpeciesSize { get; set; }
}

/// <summary>A world effect at a spot (#1998): a thumper's thump, a colossus stomp, a sandworm breaching or striking. The
/// client throws dust, shakes the camera with distance and plays the sound; inside <see cref="Radius"/> (a stomp, a
/// strike) it also knocks its own player away from the spot — movement is the client's, the damage the server's.</summary>
public sealed class WorldFx
{
    /// <summary>"thump" | "stomp" | "breach" | "strike" | "dive" | "rumble" | "swallow" (#2076: a worm ate an animal at the spot)
    /// | "arrow" (#2645: a hunter's arrow from X/Y/Z to <see cref="ToX"/>/<see cref="ToY"/>/<see cref="ToZ"/>).</summary>
    public string Kind { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>0..1: how big the dust and the shake are.</summary>
    public float Strength { get; set; }

    /// <summary>Knock-back radius (blocks); 0 = no push.</summary>
    public float Radius { get; set; }

    /// <summary>The far end of a travelling effect (#2645, kind <c>"arrow"</c>: a hunter's arrow flies from X/Y/Z to here);
    /// unused (0) by the spot effects. New fields on an existing contractless MessagePack message: an older client ignores
    /// them, and it ignores the unknown kind as well.</summary>
    public float ToX { get; set; }

    /// <inheritdoc cref="ToX"/>
    public float ToY { get; set; }

    /// <inheritdoc cref="ToX"/>
    public float ToZ { get; set; }

    /// <summary>The NPC id that caused the effect (#2645: the hunter whose arrow it is, so the client plays its release
    /// gesture); 0 = none.</summary>
    public int SourceId { get; set; }
}

/// <summary>Snapshot of live creatures (fauna) near the player on the planet surface.</summary>
public sealed class CreatureList
{
    public NetCreature[] Creatures { get; set; } = System.Array.Empty<NetCreature>();

    /// <summary>#2665: the server clock (uptime, seconds) of the tick these positions belong to — the client stamps its
    /// interpolation snapshots with it, so arrival jitter does not bend the motion. Additive: an older server sends 0 and
    /// the client stamps on arrival; an older client ignores it.</summary>
    public double ServerTime { get; set; }
}

/// <summary>
/// #2665: the compact, frequent creature position stream — every second server tick (7.5 Hz at 15 Hz), per player, for the
/// animals within <c>CreatureMotionRange</c> of that player. The heavy <see cref="CreatureList"/> (appearance, traits,
/// names) keeps its 2 Hz beat; this carries only what the client's interpolator needs, packed as fixed-point integers
/// (<see cref="Stride"/> per creature, in <see cref="Ids"/> order) so both the MessagePack and the browser JSON form stay
/// small. New message class: an older client drops the unknown tag and keeps interpolating the 2 Hz list.
/// </summary>
public sealed class CreatureMotionFrame
{
    /// <summary>Integers per creature in <see cref="Data"/>: X, Y, Z, VelX, VelY, VelZ, Facing, Flags.</summary>
    public const int Stride = 8;

    /// <summary>Positions travel in hundredths of a block.</summary>
    public const float PositionScale = 100f;

    /// <summary>Velocities travel in hundredths of a block per second.</summary>
    public const float VelocityScale = 100f;

    /// <summary>The heading travels in thousandths of a radian (dirX = cos, dirZ = sin, as <see cref="NetCreature.Facing"/>).</summary>
    public const float FacingScale = 1000f;

    /// <summary>Flag bit: in the air (a walker mid-jump/fall, a flier off its perch, a hoverer) — <see cref="NetCreature.Airborne"/>.</summary>
    public const int FlagAirborne = 1;

    /// <summary>Flag bit: a flier sitting on its perch — <see cref="NetCreature.Perched"/>.</summary>
    public const int FlagPerched = 2;

    /// <summary>The server clock of the tick (see <see cref="CreatureList.ServerTime"/>).</summary>
    public double ServerTime { get; set; }

    /// <summary>The creature ids, one per <see cref="Stride"/> integers of <see cref="Data"/>.</summary>
    public string[] Ids { get; set; } = System.Array.Empty<string>();

    /// <summary>The packed motion, <see cref="Stride"/> integers per id.</summary>
    public int[] Data { get; set; } = System.Array.Empty<int>();

    /// <summary>A float as a fixed-point integer (rounded, clamped to the integer range).</summary>
    public static int ToFixed(float value, float scale)
    {
        double v = System.Math.Round((double)value * scale);
        return v >= int.MaxValue ? int.MaxValue : v <= int.MinValue ? int.MinValue : (int)v;
    }

    /// <summary>The float a fixed-point integer stands for.</summary>
    public static float FromFixed(int value, float scale) => value / scale;
}
