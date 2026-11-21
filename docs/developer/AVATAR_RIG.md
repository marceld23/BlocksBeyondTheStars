# The humanoid figure — rig, walk and joints

Epic #3888 (issues #3882–#3887). Every humanoid figure in the game is one class, `PlayerAvatar`: the own avatar in
third person, the other players, villagers and station crew, bandits, and the big figures of the character editors. It is
built from boxes in code and animated procedurally — no animation clips, no skinned mesh, so the box look, the body paint
projection (#874) and the armour fit (#3752) stay exact. This page is the map of its rig and its walk; the emotes, body
states and the face are in [GAME_FEEL.md](GAME_FEEL.md) §5, the held item in [HELD_ITEMS.md](HELD_ITEMS.md), the animals'
rig in [CREATURE_RIG.md](CREATURE_RIG.md).

## 1. The hierarchy

```
root (feet on the ground, the caller's position, yaw and scale)
└─ Hips            origin pivot — turns about the hip joints (0, 0.92, 0), dips and sways
   ├─ Pelvis, waist battery …
   ├─ LegLeft / LegRight (hip pivots) → Upper, Knee → Lower, KneeJoint, Ankle → Foot
   └─ UpperBody    origin pivot — turns about the waist (0, 1.06, 0): lean, counter-rotation, bow
      ├─ Abdomen, Chest, Shoulders, Neck, Head (face, hair, helmet), suit pack and panel, chest plates …
      └─ ArmLeft / ArmRight (shoulder pivots) → Upper, Elbow → Lower, ElbowJoint, Hand
```

**Origin pivots.** `Hips`, `UpperBody` and the two `Ankle`s sit at their parent's origin and turn about a point by moving
as well as turning (`PoseAbout`: position = point − turn · point + shift). At rest they are the identity, so every part
keeps the root-frame (or knee-frame) position it was built at: the 60-odd parts and gear plates that are built "on the
root" need no new numbers, and the source-text guards (`AvatarArmourFitTests`) still read `AddCube("…", transform, …)`.

**Routing.** `AddCube` and `NewPivot` pass every parent through `RouteParent`: a part asked onto the root goes onto
`UpperBody` from the waist (`WaistY` 1.06) up and onto `Hips` below it; a part asked onto a knee whose centre is below
`FootPartBelowY` (−0.38, knee frame) goes onto that leg's ankle — a boot's sole and toe cap follow the foot, its cuff
stays on the shin. The spring boots' coil sits just above that line on the sole's top face, so `BuildSpringBoot` puts its
root onto the ankle by hand. A new part therefore lands on the right pivot by its height alone; a part that must not move with the
body (none today) would have to be parented by hand.

## 2. The walk (`Feel/HumanGait.cs`, `PlayerAvatar.Gait.cs`)

The curves are Unity-free in Client.Core (`HumanGait`, tested headless in `HumanGaitTests`); `PlayerAvatar` drives the rig
with them. The rig measures its own speed from the root's motion (players, remotes and NPCs alike) and keeps the stride
true to the ground it covers (#2667): the cycle rate follows the speed, the leg swing is the arc that reach asks for.

The gait phase `p` runs 0 → 1 per stride of one leg (`HumanGait.Phase` of the rig's walk phase): 0 its heel strike (the
leg furthest forward), 0.5 its toe-off (furthest back).

| Part | What it does | Before |
|---|---|---|
| Knee | a give of 6–14° after the heel strike, a fold of 40–58° (+32° running) in the swing, peaking at p ≈ 0.68; nothing while standing | bent hardest with the leg at its rearmost point, straight on the forward swing; a stroll peaked at ~18° |
| Elbow | 10° + the figure's rest bend, up to +26° on the arm's forward swing, +40–60° running | bent on the back swing |
| Arms | swing with the opposite leg, 3 % of a stride later, a touch further forward than back, hanging 2–6° away from the body | parallel, a pure mirror of the leg |
| Foot | held level through the stance, toes up 12° at the heel strike, the heel off up to 24° at push-off, hanging relaxed (22°) in the water, under the glider, in the air and in bed; the ankle stays within −35°…55° of the shin | fixed to the shin |
| Hips | dip at double support by what the spread legs ask (L (1 − cos swing), at most 0.07), plus a small bounce of the figure's own; sway 2–3 cm over the standing foot; roll ±3° (the swinging leg's hip drops); turn ±3–6° with the stride | rigid |
| Upper body | counters the hips' turn so the shoulders turn against them (±3–7°), counters 85 % of their roll, leans 0.5–10° into the pace; the bow emote bends here (34°). It twists and rolls about the waist's centre but leans and bows about the back of the waist (`WaistBack`, z −0.12), so a bow folds the belly into the pelvis instead of opening a notch at the back (`PoseUpper`). With a chest piece on — its waist plate spans the hips/upper-body seam — the twist is cut to 30 % and the bow to 72 % (`ArmouredWaist`), so plate and body boxes do not cut through each other | rigid; the bow was a deep nod |
| Head | keeps its gaze: takes back 60 % of the lean and 80 % of the shoulders' turn | — |
| Legs | roll against the hips' roll and sway and turn back against their yaw, so the feet stay under the body and step straight ahead | — |

**Standing** (`StandBody`): the weight rests on one leg for a while and moves over to the other (`HumanGait.IdleShift`, a
clamped slow sine): the hips over the standing foot, the free hip a little lower, the free knee eased 9° and its foot a
little forward; a breath in the upper body. Arms and head keep their old idle sway and look-around.

**Poses with their own body** — asleep, seated, climbing, crouched, gliding, swimming, airborne, the sit emote, defeated —
ease the body motion out over a fraction of a second (`_walkBody`, `_idleBody`), so they look exactly as before.

## 3. Each villager's own gait (`GaitStyle`)

`HumanGait.StyleFor(seed)` draws a cadence (× 0.9–1.1 — the stride follows), arm swing (× 0.75–1.25), a slight arm
asymmetry, a rest bend of the elbows, a bounce (× 0.7–1.35), a lean, how far the arms hang out, and the idle shift's
speed and phase. The seed is the look seed `Build(…, variantSeed)` already gets — the face seed — so a villager walks the
same way on every client and every visit. Seed 0 (players, previews, androids) walks `GaitStyle.Neutral`.

## 4. Joints (#3885)

A joint block at each knee and elbow (`AvatarArmourFit.KneeJoint` 0.23 × 0.13 × 0.21, `ElbowJoint` 0.17 × 0.12 × 0.17),
centred on the pivot and turning with the lower limb: it fills the wedge a bend opens and stands half a centimetre proud
of the limb at its sides (and of the shin in front). Its material is the limb's colour × 0.78 (`JointTone`); a machine's
is dark servo metal (`ServoTone`). Painted limbs paint the joint too (it is registered with the limb's body paint, inside
the limb's paint box, so a painting maps as before). `AvatarArmourFitTests` holds it clear of every plate, pad and limb
face (no flicker) and inside the plates and pads that cover it.

## 5. Machines (#3886)

`SetMachine(true)` — `NpcView` calls it for every `IsRobot` NPC (androids and the G.D.S. guardians) — sets the figure's
liveliness to `HumanGait.MachineLiveliness` (0.5): the hips' sway, roll, turn and dip-bounce, the counter-rotation, the
lean and the idle shift at half, the knee fold at 87.5 %, the arm swing at 85 %, the foot roll at 80 %, and servo-coloured
joints. Their look seed is 0, so they all walk the even stock gait — stiffer than a villager, no longer rigid.

## 6. The villagers' pace (#3887, server)

All settlement NPCs share one stroll (`GameServer.NpcProfile`, cruise 0.7 blocks/s, burst 0.9). `NpcPaceFactor(id, name,
child, machine)` gives each its own factor — 0.85–1.2 from the same id and name its face is drawn from, a child × 1.15,
machines the even 1.0 — and `PaceProfile` scales the stroll by it before every step. The client gets no new field: the
figure's walk follows the speed it measures. See [NPC_ROUTINES.md](NPC_ROUTINES.md) §8.

## 7. Cost

Per figure: four joint cubes and four origin pivots more, two materials; per frame a dozen trigonometric calls and four
transform writes. The curves are evaluated for every figure every frame as before (there is no humanoid LOD).

## Tests

- `tests/BlocksBeyondTheStars.Client.Tests/HumanGaitTests.cs` — the curves: knee timing and depth, elbow, foot roll, body
  dip, sway and turn, the phase against the rig's swing, the styles, the idle shift, machines' liveliness.
- `tests/BlocksBeyondTheStars.Client.Tests/AvatarArmourFitTests.cs` — the joint blocks against limbs, plates and pads.
- `tests/BlocksBeyondTheStars.Tests/SettlementNpcTests.cs` `EveryVillager_WalksAtItsOwnPace_MachinesAtTheSharedOne`.
- Unity EditMode: `AvatarBodyMotionEditModeTests` (the parts on their pivots, the walking body and level stance foot, a
  still seated body, a stiffer machine) and `AvatarKneeBendEditModeTests` (no knee ever folds forward).
