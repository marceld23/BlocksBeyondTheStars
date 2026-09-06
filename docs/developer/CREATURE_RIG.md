# Creature rig — how a blocky animal is built and animated

How the client turns a `NetCreature` descriptor into a moving body. The server is authoritative over
which creatures exist and where they are; everything in this document is render-only, runs on the
client, and never touches the wire, a save, or gameplay.

Related: [WORLD_GENERATION.md](WORLD_GENERATION.md) (which species a world rolls and how they move
through it), [CREATURE_TAMING.md](CREATURE_TAMING.md) (companions and their species snapshot),
[ART_BIBLE.md](ART_BIBLE.md) (the visual language the rig has to speak).

## The pieces

| File | Role |
| --- | --- |
| `CreatureBuilder.cs` | Builds the body from the descriptor — cubes in code, no art asset |
| `CreatureRig.cs` | `LegRig` / `WingRig` / `RigDescription` / `CreatureLod` — what the builder hands the animator |
| `CreatureAnimator.cs` | Poses the rig every frame |
| `CreatureFeet.cs` | Decides when each foot steps and where it plants |
| `Shared/Definitions/CreatureGait.cs` | The pure gait mathematics (unit-tested off Unity) |
| `Shared/Definitions/CreatureIk.cs` | Two-bone leg IK (unit-tested off Unity) |

The same descriptor must always yield the same body: every client draws a species identically, so
per-individual variation is derived from `StableIdHash(SpeciesId)`, never from `Random` at build
time. (`Random` inside the animator is fine — that is per-client cosmetic timing, not body shape.)

Three body plans branch in `Build`: **Standard**, **Titan** (#638) and **Medusa** (#637).

## Rig conventions

Parts hang off a `BodyRig` child of the entity root, so the animator can undulate the whole creature
without disturbing the root's movement-driven facing (which `CreatureView` owns, together with slope
pitch and flier banking).

**Every leg states its own identity.** The animator used to receive a flat `Transform[]` and infer
side and row from the array index — and the two body plans filled that array in opposite orders (the
standard path numbered the *rear* pair first, the titan path the *front* pair), so any gait keyed off
the row ran mirrored on titans. `LegRig` now carries `Side` (0 = left), `Row` (**0 = front-most** on
every plan), `Rows`, the segment lengths and a `KneeSign`.

**Rest poses are captured at build time** (`HipRest`, `HipRestPos`, `ShoulderRest`, …) and every
effect poses *additively* on top of them, so splay, gait, airborne tuck and idle sway compose instead
of overwriting one another.

**Hips sit at the body's real half-width.** They used to be pinned at `0.5` units while a wide body
(`bodyWide` up to 1.40) reaches `0.77`, so broad species grew their legs out of the middle of the
belly. `TaperAt` gives the half-width at a point along the spine, because the segments taper.

**Leg rows spread over the whole torso.** They used to be distributed over a single *segment's*
length regardless of body length, so a 4-segment body carried all its legs in a cluster at the
centre.

## The gait

The single most visible thing about a walking animal is whether its feet stay put while they carry
weight. The old animator advanced a sine wave at `3 + speed·2.2` — a rate unrelated to the distance
the body actually covered — so every creature in the game skated. `CreatureGait` fixes the
relationship:

```
stride length = 2 · legLength · sin(amplitude)      // the chord a planted foot sweeps
cycle rate    = speed ÷ stride length               // ⇒ the planted foot is stationary
```

Within the stance the **angle is the arcsine of a linear sweep**, not a linear sweep itself: it is
the foot's ground *position* that must move linearly (the hip travels over a still foot). A linear
angle leaves a ~15 % velocity ripple across the stance at 30°, which is exactly the residual skate.

`IdleAmpDeg`/`WalkAmpDeg` keep the amplitude in a deliberately narrow band (12°–30°): the cycle
*rate* carries the speed, the stride length only stretches ~2×, so the frequency range stays
believable. A giant's slow stride then falls out of the geometry — its stride length is metres — and
the hand-tuned `CadenceScale` from #638 is left as a small extra drag rather than doing the whole job.

### Footfall patterns

| Gait | Used by | Pattern |
| --- | --- | --- |
| `Walk` | everything at low speed | lateral sequence LH → LF → RH → RF, three feet always down |
| `Trot` | quadrupeds at mid speed, giants at speed | diagonal couplets |
| `Bound` | non-giant walkers at speed | front pair together, hind pair together |
| `Tripod` | 6+ legs at speed | the insect alternating tripod |
| `Metachronal` | 6+ legs at a crawl | a wave running from the rear legs forward |
| `Paddle` | swimmers and legless bodies | left/right half a cycle apart |

Giants (titan plan or `Size ≥ CreatureMotion.GiantSize`) and crawlers never bound. `IsAllowed` states
these rules and a test asserts `Select` never violates them for any body the generator can produce.

A gait change re-phases every leg, so the animator holds the switch until the speed is clear of the
transition band (`TransitionHysteresis`) and then cross-fades the two poses over `GaitFadeDuration` —
otherwise the legs pop.

Standing still the cycle rate is zero and the pose would freeze mid-stride, so the gait is faded out
against a barely-there idle sway.

### Body weight shift

`BodyBob` and `BodyRoll` ride on the same phase: walking and trotting bodies dip twice per stride
(once per support pair), a bounding body once, and only the gaits with a left/right support asymmetry
roll. This is what sells the body's mass; it is scaled by how much of the gait is showing, so a
standing animal does not bob on the spot.

## Joints

Every limb is jointed, mirroring the pattern `PlayerAvatar` has always used (shoulder → elbow → hand,
hip → knee → foot). A single rigid stick can only pendulum; it cannot shorten to clear the ground on
the swing or straighten to carry weight on the plant.

- **Leg**: hip → thigh → **knee** → shin → **foot** (a flat wide sole). `KneeSign` is `+1` when the
  shin folds backwards (an elbow) and `-1` forwards (a stifle): fore-limbs fold back, hind-limbs
  forward. A quadruped whose knees all bend the same way reads as a table. Bodies with six or more
  legs fold uniformly back and stand splayed instead. The foot counter-rotates the hip and knee so
  the sole stays flat on the ground rather than pointing at it.
- **Wing**: shoulder → inner panel → **wrist** → outer panel. Folding is the wrist's job — the outer
  panel swings back along the flank and the inner one tips up. The single slab this replaced could
  only be rotated bodily up over the back, which is not what a bird does when it lands. The wrist
  also trails the shoulder by about a fifth of a beat, which is what gives a wingbeat its whip.
  `Glides` (already on the wire, #1334) holds the wings spread with a little dihedral.
- **Tail / tentacles / trunk / neck**: chains of nested pivots. The beat travels outward as a wave
  instead of the whole appendage swinging as one rigid box.
- **Neck (titan)**: the head is parented to the top of the chain and a head gesture is shared out
  over the neck joints and the head, so a graze bends the whole neck. As a static stack a giraffe
  could only nod at the top of a rigid column and never reach the ground.
- **Medusa**: the rim arms are chains too, and the bell's own contraction drives their amplitude, so
  they trail the pulse instead of waving independently of it.

## Fins

`CreatureSpecies.HasFins` is a species trait (`NetCreature.HasFins` on the wire), but it is
**derived, not rolled**: `CreatureMotion.FinsFor` folds the species' own voice seed, so no generator
RNG is consumed and every world created before fins existed keeps its species bit-for-bit. Legless
water and amphibian bodies almost always carry them, a legged water body sometimes does, and a
medusa never does.

Because the derivation reads only persisted fields, a companion snapshot saved before the trait
existed can be lifted on load — that is what `CreatureMotion.HasFins(sp)` is for, and it is what the
server calls at the wire boundary rather than reading the raw flag.

Geometrically: pectorals on the flanks, a vertical caudal on the tail's last link, and a dorsal when
the species is not already wearing a crest there. They beat on the paddle phase, and an amphibian
ashore folds them flat instead of rowing at nothing.

## Foot planting

The gait alone stops the skate, but it does it in the *body's* frame: all feet sit on one flat plane
at one body-relative height. On a hillside half of them hang in the air and half sink into the hill.
`CreatureFeet` gives each foot a real world-space target on a real block, and `CreatureIk` solves the
leg backwards from it.

- A foot steps when its own gait phase says it is its turn **and** the body has actually walked past
  it. Standing still there is no slip, so a standing animal keeps its feet exactly where they are
  instead of marching on the spot.
- Ground is probed **once per new target**, never per frame — a handful of block lookups a second per
  creature. Scene coordinates index the world grid directly (they are the player's own world
  coordinates offset by whole wrap periods — the same assumption `MicroFaunaView` makes). Water is
  not ground: a walker wading a pond keeps its feet on the bed.
- Body height, pitch and roll come from the plane through the planted feet, and `CreatureView` stops
  applying its own velocity-derived slope pitch while that is active (`FootPitchActive`) so the body
  is not tilted twice.
- **Network jitter is the real hazard here.** Positions arrive at ~2 Hz and are lerped and
  dead-reckoned, so a foot planted in world space would drag or pop on every correction. Targets are
  re-planted once they slip past a threshold, a slip of more than three strides snaps rather than
  drags, and a teleport-sized root jump (spawn shove, eviction, a longitude wrap) resets every foot.
- Near LOD only, and only for grounded walkers and crawlers that are neither airborne nor lying down.

## Level of detail

There was none: every creature animated its full rig every frame at any distance, at up to the
world's live cap (~25–45, hard cap 64). `CreatureView` now assigns a tier per creature per frame:

| Tier | Distance | What runs |
| --- | --- | --- |
| `Near` | < 20 m | everything, including foot planting and IK |
| `Mid` | < 45 m | gait, wings, tail, body, face detail; no ground probes |
| `Far` | < 90 m | the same, every third frame with the banked time |
| `Frozen` | beyond | nothing — the body still moves, it just stops posing itself |

**Reduced effects** (the same setting that halves the micro-fauna) scales all three distances to
55 %. Each tier keeps a couple of metres of hysteresis, so an animal grazing on a boundary does not
flip between two levels of detail every frame. Coming back from a coarse tier re-syncs the speed
estimate and re-plants the feet, so a creature does not sprint or drag on its first frame back.

## Testing

`CreatureGait` and `CreatureIk` are pure and live in Shared (netstandard2.1), so they are unit-tested
by the server suite:

- `CreatureGaitTests` — the stance sweep is monotonic, its foot velocity is constant to within 5 %,
  the swept chord equals `StrideLength`, the pose is continuous across both phase boundaries,
  `Select` never returns a gait the body is not allowed.
- `CreatureIkTests` — the load-bearing test runs the solved angles back through forward kinematics
  and asserts the foot lands on the target, swept across the whole reachable volume. That checks the
  solver against the rig's actual frame convention rather than against remembered numbers.
- `CreatureFinsTests` — fins only on water and amphibian bodies, never on a medusa, deterministic per
  seed, and every generated species matches the derivation (which is what makes the snapshot lift safe).

Assertions are on **ranges, orderings and invariants**, never trig-derived float goldens: Windows and
Linux libm disagree in the last bits.

The rig itself needs the Unity editor. `client/Assets` is **never compiled by PR CI**, so any change
here requires a local build (or at minimum a batch-mode script compile) before it is merged.
