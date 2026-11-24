// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Creature movement (<see cref="MoveCreatures"/>): one step per creature and tick — chase, flee and wander, the
/// step's barriers (ship, fence, door, terrain, body sweep), running jumps, turning, steering around an obstacle and the
/// step preview.
/// </summary>
public sealed partial class GameServer
{
    private readonly List<CombatEntity> _creatureEvictions = new(); // boxed-in sleepers removed after the move loop (#1320)

    /// <summary>Advances every creature: hunters approach, skittish flee, the rest wander; sleepers rest.
    /// Returns true when a creature was REMOVED (a boxed-in sleeper, #1320) so the caller re-broadcasts.</summary>
    private bool MoveCreatures(List<PlayerSession> targets, double dt)
    {
        if (_creatures.Count == 0)
        {
            return false;
        }

        double moveDt = System.Math.Min(dt, CreatureMoveDtCap);
        _creatureClock += moveDt;
        RefreshLureTargets(targets); // #2018: who holds food this tick — one dictionary lookup per player, read by every beggar
        ResolvePendingFeedTames();   // #2082: last tick's favourite meals that won an animal over (outside the loop: it edits the list)

        foreach (var creature in _creatures)
        {
            if (creature.IsCompanion)
            {
                MoveCompanion(creature, moveDt); // tamed companions follow their owner instead of wandering/hunting
                continue;
            }

            if (creature.IsGiant)
            {
                continue; // #1998: the colossus and the sandworms move by their own rules (TickGiants)
            }

            if (!_speciesById.TryGetValue(creature.SpeciesId, out var sp))
            {
                continue;
            }

            // Safety net: a wild creature that somehow ended up inside a parked ship (ship placed/grown over it,
            // an old save, a numeric edge) is pushed back out of the hull this tick instead of being stuck inside.
            // Body-aware for large species (#1320): a sleeping titan herd lay with its centres a block outside
            // the hull and its bodies filling the cabin — the point test never saw it.
            if (TryPushOutsideShip(creature.Position, out var ejected, CreatureShipMargin(sp)))
            {
                creature.Position = ejected;
                continue;
            }

            // #3656: the same for a train's wagon — an animal found inside one is set down beside it (GameServerRailWildlife).
            if (TryPushOutOfWagon(creature, sp, out var besideWagon))
            {
                creature.Position = besideWagon;
                continue;
            }

            if (creature.FrozenTimer > 0)
            {
                creature.FrozenTimer = System.Math.Max(0, creature.FrozenTimer - dt);
                continue; // held in stasis (item 36) — no movement this tick
            }

            if (creature.ProvokeTimer > 0)
            {
                creature.ProvokeTimer = System.Math.Max(0, creature.ProvokeTimer - dt);
            }

            if (creature.PanicTimer > 0)
            {
                creature.PanicTimer = System.Math.Max(0, creature.PanicTimer - dt); // startle (#653) wears off
            }

            if (creature.ThreatTimer > 0)
            {
                creature.ThreatTimer = System.Math.Max(0, creature.ThreatTimer - dt); // #2645: a hunter it noticed fades from mind
            }

            if (creature.AwakeOverrideTimer > 0)
            {
                creature.AwakeOverrideTimer = System.Math.Max(0, creature.AwakeOverrideTimer - dt);
            }

            TickCalm(creature); // #3359: a calmed animal (the calm disc, the Resonance Blaster) holds no grudge while the calm lasts

            // A provoked territorial creature hunts like an aggressor until it calms down.
            var temperament = CreatureBehaviour.EffectiveTemperament(sp, creature.ProvokeTimer > 0);
            Vector3f? nearest = NearestPlayerPosition(targets, creature.Position);
            // #2712: what an aggressor hunts — the nearest player hostiles do NOT ignore. A cloaked, god-mode or Sandbox
            // player still wakes a sleeper and sends a skittish one running, but is nobody's prey.
            Vector3f? prey = NearestPlayerPosition(targets, creature.Position, preyOnly: true, hunter: creature); // #3354: quiet steps, the calm disc
            if (sp.BodyPlan == CreatureBodyPlan.Arachnid && !creature.IsCompanion)
            {
                AnnounceArachnid(creature, targets); // #2009: VEGA points out the first one a player gets near
            }
            else if (HoundRules.IsScaleHound(sp) && !creature.IsCompanion)
            {
                AnnounceHound(creature, targets); // #3326: VEGA warns about the Schuppenhund
            }
            else if (sp.BodyPlan == CreatureBodyPlan.Soundrak && !creature.IsCompanion)
            {
                GuardSoundrakTerritory(creature, targets); // #3359/#3424: its scream, and after the warning the attack
            }

            // Give-up leash: an aggressor that has been chasing within aggro range too long backs off for a
            // while — it wanders away and won't chase/attack — so creatures never hound the player forever.
            // Big species notice you from further away (#638): the range grows with size past 2.
            bool hunting = SreekmakraHunting(creature); // 2026-09: the shapeshifter hunts in any shape
            bool aggressor = hunting || temperament is CreatureTemperament.Aggressive or CreatureTemperament.PackHunter;
            aggressor &= !IsCalmed(creature); // #3359: calm means calm — it hunts nobody while it lasts
            float aggro = HoundAggro(sp, CreatureAggroRange + System.Math.Max(0f, sp.Size - 2f)); // #3326: a hound sees further
            // #1997: a mining grudge reaches as far as the anger does — it was set out to 16 blocks but only hunted
            // within 8, so a flowerling angered from 12 blocks flipped to "hostile" and wandered off.
            bool miningGrudge = sp.AngeredByMining && creature.ProvokeTimer > 0;
            if (miningGrudge)
            {
                aggro = System.Math.Max(aggro, MiningAngerRange);
            }
            ApplyFightLock(creature, targets, ref temperament, ref aggressor, ref prey, ref aggro); // #3365: a duel, a defending pack
            if (creature.GiveUpTimer > 0)
            {
                creature.GiveUpTimer = System.Math.Max(0, creature.GiveUpTimer - dt);
            }
            else if (aggressor && prey is { } np && WrapDistSq(creature.Position, np) <= aggro * aggro)
            {
                // While it can see the prey the chase clock ticks normally; once the player breaks line-of-sight
                // (behind cover, into a cave) the creature tires of the hunt faster, so hiding actually shakes it
                // off rather than only stopping the bite.
                bool sees = HasLineOfSight(creature.Position, np);
                creature.ChaseTimer += dt * (sees ? 1.0 : CreatureBlindChaseGiveUpRate);
                if (creature.ChaseTimer >= (miningGrudge ? MiningGrudgeChaseSeconds : CreatureChaseGiveUpSeconds))
                {
                    creature.GiveUpTimer = CreatureGiveUpCooldownSeconds;
                    creature.ChaseTimer = 0;
                }
            }
            else
            {
                creature.ChaseTimer = System.Math.Max(0, creature.ChaseTimer - dt); // decay when not chasing
            }

            var profile = ProfileFor(creature.SpeciesId);
            if (miningGrudge)
            {
                profile.CruiseSpeed *= MiningGrudgeSpeedFactor; // #1997: an angry flowerling means it (still slower than a walk)
                profile.BurstSpeed *= MiningGrudgeSpeedFactor;
            }

            if (hunting)
            {
                profile.CruiseSpeed *= SreekmakraSpeedFactor; // the shape's own speed ×1.5 (2026-09)
                profile.BurstSpeed *= SreekmakraSpeedFactor;
                profile.Accel *= SreekmakraSpeedFactor;
            }

            ApplyHoundCharge(creature, sp, aggressor, prey, ref profile); // #3326: the Schuppenhund charges the last metres

            // A creature in its off-phase is asleep — but a player coming within wake distance stirs it, unless they sneak
            // (#3751; being hit does too, via ProvokeCreature). Once roused it stays alert for a while, then settles back.
            if (!SpeciesActive(sp, creature.Position) && creature.AwakeOverrideTimer <= 0 && nearest is { } wakePos
                && SleeperWokenByAPlayer(targets, creature.Position, wakePos)
                && !Shared.Sounders.SoundelingRules.IsSoundeling(sp)) // #3366: a Soundeling stays curled up all night
            {
                creature.AwakeOverrideTimer = CreatureWakeSeconds;
            }

            // Sleepers rest in place during their off-phase — only their vertical state runs (#1331/#1332: a
            // sleeping walker still falls if its floor is dug away, a sleeping flier lands and sleeps on the
            // ground when there is any, a hoverer holds its altitude), no roaming or hunting. A roused sleeper
            // falls through to normal temperament-driven behaviour (skittish ones flee, hunters seek, others
            // just wander).
            var motion = EffectiveMotion(creature, sp);
            bool asleep = !SpeciesActive(sp, creature.Position) && creature.AwakeOverrideTimer <= 0
                && !StrandedOutOfWater(creature, sp); // #3552: a fish ashore does not lie down there

            // #1320: a sleeper skips every collision gate on the movement path, so a player building a wall
            // or floor THROUGH a sleeping herd left the bodies embedded in the masonry all night. Re-validate
            // the body BEFORE the vertical resolve (which would otherwise hop it onto the new wall): rouse +
            // step aside to the nearest clear spot, or despawn when boxed in. A few block reads per sleeper
            // per tick — far cheaper than the movement path an awake animal runs.
            // #1357: an AWAKE animal walled in by the player never checked its own cell either — the swept
            // step check samples only the cells ahead, so every step out of the block read as blocked and a
            // cathemeral grazer stood inside the masonry for good. Same check, rate-limited per creature.
            // #1854: a floating land grazer that got into the masonry is first put back on the nearest real
            // floor THROUGH the rock — the sideways relocation below evicts an animal it finds no spot for,
            // and a gas sac that drifted into a cave's ceiling has its floor right under it.
            LiftEmbeddedHoverer(creature, sp, motion);

            if (asleep || _uptime >= creature.NextBodyCheckAt)
            {
                creature.NextBodyCheckAt = _uptime + AwakeBodyCheckInterval;
                if (DisplaceEmbeddedCreature(creature, sp))
                {
                    continue;
                }
            }

            // #2278: dazed by a shock-glove push — it stays where the push left it; only the vertical state runs, so the
            // little hop lands and a push over a low ledge falls. When the daze runs out the clients hear of it at once.
            if (StaggerJustEnded(creature))
            {
                BroadcastCreatures();
            }

            if (IsStaggered(creature))
            {
                creature.Position = ResolveVertical(creature, sp, motion, creature.Position, 0f, profile, moveDt,
                    asleep: false, MoveMode.Roam, moving: false);
                LiftEmbeddedHoverer(creature, sp, motion);
                continue;
            }

            // #3366: a Soundeling thinks over a light it was handed, or hides curled up in the dark by day — it holds still.
            if (Shared.Sounders.SoundelingRules.IsSoundeling(sp) && SoundelingHolds(creature, sp, motion, profile, moveDt, asleep))
            {
                continue;
            }

            // Sleepers rest in place during their off-phase — only their vertical state runs (#1331/#1332). #2753: a sleeper
            // lying inside an earlier sleeper's body shuffles aside slowly, without waking.
            if (asleep)
            {
                var rest = ShuffleSleeper(creature, sp, motion, moveDt, out int? restGround);
                creature.Position = ResolveVertical(creature, sp, motion, rest, 0f, profile, moveDt,
                    asleep: true, MoveMode.Roam, moving: false, restGround);
                LiftEmbeddedHoverer(creature, sp, motion); // #1854: a sleeping gas sac does not sink either
                continue;
            }

            // #2009: an arachnid ambusher lies in wait — motionless like a rock until a player comes within LurkRange, then
            // it is provoked (a territorial one hunts like an aggressor for the provoke window, a hunter simply starts its
            // chase) and rushes. It never roams on its own; after a chase gives up it wanders through the cooldown like any
            // aggressor and settles back into the wait when that runs out. Companions never lurk.
            creature.Lurking = false;
            if (!creature.IsCompanion && creature.ProvokeTimer <= 0 && creature.GiveUpTimer <= 0 && creature.PanicTimer <= 0
                && ArachnidRules.Lurks(sp))
            {
                if (prey is { } lurkPrey && WrapDistSq(creature.Position, lurkPrey) <= ArachnidRules.LurkRange * ArachnidRules.LurkRange)
                {
                    creature.ProvokeTimer = CreatureProvokeSeconds;
                    temperament = CreatureBehaviour.EffectiveTemperament(sp, provoked: true);
                    aggressor = true;
                }
                else
                {
                    creature.Lurking = true;
                    creature.Position = ResolveVertical(creature, sp, motion, creature.Position, 0f, profile, moveDt,
                        asleep: false, MoveMode.Roam, moving: false);
                    LiftEmbeddedHoverer(creature, sp, motion);
                    continue;
                }
            }

            // Decide intent: hunters Seek a nearby player, skittish flee one, everyone else (and a give-up
            // aggressor) roams with stop-and-go. Pack-hunters angle their approach so kin converge from spread
            // directions (encircle) rather than all stacking on one beeline. A startled non-retaliator (#653)
            // flees too — panic reaches further than the skittish reflex and moves even placid grazers.
            var intent = MoveMode.Roam;
            Vector3f? target = null;
            // #2018: a begging herd animal (food in a nearby hand, a thrown piece, the trot away) decides its own intent and the
            // temperament intents below are skipped while it does. Only peaceful species beg (a skittish beggar, #3973, flees
            // again once the food is gone); frozen, sleeping and panicked animals never reach this line with a live phase.
            bool begging = !hunting && (TryBegIntent(creature, sp, nearest, ref profile, ref intent, ref target)
                || TryCallerIntent(creature, sp, nearest, ref profile, ref intent, ref target) // #2057: a caller block calls the peaceful ones
                || TrySoundelingIntent(creature, sp, ref intent, ref target)); // #3366: a Soundeling carries a light, or walks to one in the dark
            Vector3f? stepTarget = begging || (aggressor && creature.GiveUpTimer > 0) ? null : aggressor ? prey : nearest;
            if (stepTarget is { } tp)
            {
                float dx = tp.X - creature.Position.X, dz = tp.Z - creature.Position.Z;
                float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
                if (aggressor && dist <= aggro)
                {
                    // Hold at a size-scaled ring around the prey instead of steering into its exact
                    // position (#749) — without this a hunter overshoots the player's coordinates and
                    // oscillates back and forth through the body. The proximity damage aura reaches
                    // well past the ring, so combat is unaffected; only the overlap goes away.
                    if (dist <= System.Math.Max(CreatureStopRange, sp.Size * 0.9f))
                    {
                        // #2753: a pack holding its ring still keeps its personal space — through the usual step barriers.
                        var held = SeparateFromCreatures(creature, sp, motion, creature.Position,
                            SpacingStep(creature.Position, creature.Position, profile, moveDt), asleep: false);
                        if (held.X != creature.Position.X || held.Z != creature.Position.Z)
                        {
                            ApplyCreatureStep(creature, sp, motion, held, 0f, profile, moveDt, MoveMode.Seek, moving: false,
                                terrainGates: true);
                            continue;
                        }

                        creature.Position = ResolveVertical(creature, sp, motion, creature.Position, 0f, profile, moveDt,
                            asleep: false, MoveMode.Seek, moving: false);
                        LiftEmbeddedHoverer(creature, sp, motion); // #1854
                        continue;
                    }

                    intent = MoveMode.Seek;
                    target = temperament == CreatureTemperament.PackHunter ? FlankPoint(creature, tp, dx, dz) : tp;
                }
                else if (temperament == CreatureTemperament.Skittish && dist <= CreatureFleeRange)
                {
                    intent = MoveMode.Flee;
                    target = tp;
                }
                else if (creature.PanicTimer > 0 && dist <= CreaturePanicFleeRange
                         && !CreatureBehaviour.RetaliatesWhenAttacked(temperament))
                {
                    intent = MoveMode.Flee;
                    target = tp;
                }
            }

            // #2645: a hunter it noticed (a non-player threat). Only while one is set — an animal no hunter came near
            // decides exactly as before.
            if (creature.ThreatTimer > 0 && !begging && intent != MoveMode.Seek)
            {
                ApplyThreatIntent(creature, temperament, ref intent, ref target);
            }

            // A skittish animal BOLTING (entering flee) startles its nearby kin (#653) — the whole herd
            // takes off together. Only the genuine reflex spreads panic; already-panicked members don't
            // re-trigger it, so a startled herd settles back down instead of chain-panicking forever.
            if (intent == MoveMode.Flee && temperament == CreatureTemperament.Skittish
                && creature.PanicTimer <= 0 && creature.Loco.Mode != MoveMode.Flee)
            {
                StartleKin(creature);
            }

            // #3658: a train's horn asked it off the track — it steps aside for a few seconds (GameServerRailWildlife).
            if (!begging)
            {
                ApplyTrackAsideIntent(creature, ref intent, ref target);
            }

            // #2668: a hunter or fleer that just found its way round an obstacle keeps to that detour for a moment — aiming
            // the controller along it (it still turns there at its own rate) instead of back into the wall every tick.
            if ((intent == MoveMode.Seek || intent == MoveMode.Flee) && target is not null && creature.SteerUntil > _creatureClock)
            {
                float sx = (float)System.Math.Cos(creature.SteerHeading) * 4f, sz = (float)System.Math.Sin(creature.SteerHeading) * 4f;
                var at = creature.Position;
                target = intent == MoveMode.Seek
                    ? new Vector3f(at.X + sx, at.Y, at.Z + sz)
                    : new Vector3f(at.X - sx, at.Y, at.Z - sz); // a fleer runs AWAY from its target
            }

            uint seed = (uint)StableStringHash(creature.Id);
            var res = LocomotionController.Step(creature.Loco, profile, creature.Position, intent, target, moveDt, seed);
            creature.Loco = res.State;

            // Group steering (#639/#651): a roaming member of a social species drifts gently toward its
            // nearby kin (cohesion), keeps its personal space (separation) and — for schoolers — falls in
            // with the group's heading (alignment). Fleeing/hunting always wins, loners are unaffected.
            var stepped = res.Position;
            if (intent == MoveMode.Roam && sp.SocialGroupSize > 1 && res.Moving)
            {
                stepped = GroupSteer(creature, sp, stepped, moveDt, profile);
            }
            else if (begging && res.Moving)
            {
                stepped = BegSeparation(creature, sp, stepped, moveDt, profile); // #2018: twelve animals on one ring keep their spacing
            }

            // #2753: personal space from every animal around, of any species, sized by the drawn bodies — standing animals
            // too, so an existing pile-up drifts apart; the nudged step still goes through every barrier below.
            stepped = SpaceAwakeStep(creature, sp, motion, stepped, profile, moveDt, roaming: intent == MoveMode.Roam && res.Moving);

            // Apply the horizontal step through every barrier (ship, fence, terrain gate, body sweep) and then
            // the class's vertical mechanics (#1331): walkers jump ledges and fall, crawlers haul over, fliers
            // land and take off, hoverers drift, swimmers porpoise.
            ApplyCreatureStep(creature, sp, motion, stepped, res.VertWave, profile, moveDt, intent, res.Moving,
                terrainGates: true);
        }

        if (_creatureEvictions.Count == 0)
        {
            return false;
        }

        foreach (var gone in _creatureEvictions)
        {
            _creatures.Remove(gone);
        }

        _creatureEvictions.Clear();
        return true;
    }

    /// <summary>
    /// One creature's movement step (#1331): the horizontal move from the locomotion controller is checked
    /// against the ship hull, energy fences, the terrain gate (wild fauna only) and the swept body check; a
    /// blocked step steers around (#651) or re-rolls; a ground mover facing a one-block rise first jumps or
    /// climbs in place and only steps over once its feet are up (so the body never clips the ledge); then the
    /// class's vertical mechanics resolve the Y at the accepted spot. Shared by wild fauna and companions.
    /// </summary>
    private void ApplyCreatureStep(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f stepped,
        float vertWave, in LocomotionProfile prof, double dt, MoveMode intent, bool moving, bool terrainGates)
    {
        var cur = c.Position;
        float targetX = cur.X, targetZ = cur.Z;

        // A flier coming down onto a perch or sitting on one does not walk (#1332).
        bool hold = motion == MotionClass.Flier && c.Vert.Flight is FlightPhase.Landing or FlightPhase.Perched;
        int? groundHint = null; // the accepted target column's feet cell, when PreviewStep already found it (#1367)
        if (!hold)
        {
            var cand = PreviewStep(sp, motion, cur, stepped, out bool needsRise, out int riseFeet, out int? nextFeet);

            // #1348: a ground mover never STARTS a jump or a climb for a rise past its step-up limit. Wild
            // fauna is already walled by the terrain gate; a companion keeps its freedom from that gate
            // (water, drops — it must follow its owner down anything) but not from this one — without it a
            // walker pet under a cliff hopped in place for as long as the owner stood above, and a crawler
            // pet levitated straight up the wall (BeginClimb to the cliff top). Blocked, it waits at the
            // base like any animal; the leash teleport remains the fallback.
            bool riseTooHigh = needsRise && riseFeet - (int)System.Math.Floor(cur.Y) > CreatureMotion.StepUpLimit(motion);
            // #1367: nor does it start one without head clearance — the swept step samples only the cells
            // AHEAD at the raised height, so under a low ceiling (a 2-high tunnel with a 1-block step) the
            // animal launched in its own column and sat with its head in the roof for half a second. The
            // body column at the landing height, right where it stands, must be clear first; otherwise the
            // rise is a wall like any other.
            bool riseBlocked = needsRise && !riseTooHigh && CreatureBodyBlocked(sp, new Vector3f(cur.X, riseFeet, cur.Z));
            if (riseTooHigh || riseBlocked || StepBlocked(c, sp, motion, cur, cand, needsRise, terrainGates, nextFeet))
            {
                // Creatures don't walk into the player's ship — hold position at the hull. Energy fences pen
                // them in the same way, and terrain gates (#648) reuse the exact same mechanic. Instead of only
                // re-rolling a random heading, a blocked creature first probes alternative headings around the
                // obstacle (#651) — so it slides along cliff bases, walls and fence lines like an animal
                // skirting an obstacle. Only when every probe is blocked too does it fall back to the re-roll,
                // which preserves the "nothing can ever get stuck" property.
                c.Vert.ClimbTargetY = 0f; // whatever it was hauling toward is off the table
                if (TrySteerAround(c, sp, motion, stepped, terrainGates, out var detour, out float detourHeading)
                    && !DetourLeadsNowhere(c, sp, motion, detour, terrainGates)) // #3880: not round and round its own column
                {
                    // #2668: the body takes the free detour step, but the heading TURNS toward it (twice the turn rate)
                    // instead of snapping 35-110° in one tick. A roamer makes the detour its new roam heading; a hunter or
                    // fleer keeps to it for a moment (MoveCreatures) instead of turning back into the wall every tick.
                    float rate = prof.TurnRate > 0f ? prof.TurnRate : 6f;
                    c.Loco.Heading = TurnAngleToward(c.Loco.Heading, detourHeading, 2f * rate * (float)dt);
                    if (intent == MoveMode.Roam)
                    {
                        c.Loco.TurnTo = detourHeading;
                        c.Loco.Turning = true;
                        c.Loco.TurnAge = 0f;
                    }
                    else if (intent is MoveMode.Seek or MoveMode.Flee)
                    {
                        c.SteerHeading = detourHeading;
                        c.SteerUntil = _creatureClock + CreatureDetourHoldSeconds;
                    }
                    targetX = detour.X;
                    targetZ = detour.Z;
                }
                else
                {
                    // #3880: no detour ahead — turn toward a free neighbouring column, or stand calmly when there is none
                    // (GameServerCreatureBoxedIn). It slows down either way (#2668), so the way out starts with an ease-in.
                    OnNoDetourAhead(c, sp, motion, stepped, intent, prof, dt, terrainGates);
                }
            }
            else if (needsRise)
            {
                // A one-block ledge ahead: get the feet up FIRST — a walker jumps, a crawler or giant hauls
                // itself up in place — and step over on a later tick once the body clears the lip.
                if (!c.Vert.Airborne && c.Vert.ClimbTargetY <= 0f)
                {
                    if (CreatureMotion.CanJump(sp))
                    {
                        VerticalMotion.Launch(ref c.Vert, LedgeJumpImpulse(sp));
                    }
                    else
                    {
                        VerticalMotion.BeginClimb(ref c.Vert, riseFeet);
                    }
                }

                // #2668: a jumper keeps moving forward INSIDE its own column while its feet come up — a running jump,
                // not a hop on the spot. The body never enters the ledge's column before the feet are above the lip
                // (that still happens on a later tick, exactly as before); crawlers and giants keep hauling in place.
                if (CreatureMotion.CanJump(sp))
                {
                    var inside = InsideOwnColumn(cur, stepped);
                    if (!StepBlocked(c, sp, motion, cur, inside, needsRise: false, terrainGates))
                    {
                        targetX = inside.X;
                        targetZ = inside.Z;
                    }
                }
            }
            else
            {
                c.Vert.ClimbTargetY = 0f; // no rise ahead any more (heading changed) — don't finish a pointless haul
                targetX = cand.X;
                targetZ = cand.Z;
                groundHint = nextFeet; // the step is taken as previewed — the probe need not run a third time
                if (moving && !c.Vert.Airborne && CreatureMotion.CanJump(sp))
                {
                    TryRunningJump(c, sp, motion, cur, cand, (float)dt, terrainGates); // #2668
                }
            }
        }

        c.Position = ResolveVertical(c, sp, motion, new Vector3f(targetX, cur.Y, targetZ), vertWave, prof, dt,
            asleep: false, intent, moving, groundHint);
        LiftEmbeddedHoverer(c, sp, motion);
    }

    /// <summary>#1854: a floating LAND grazer whose body ended up inside a block after the vertical resolve is
    /// put back on the nearest real floor this very tick instead of easing out of the masonry over seconds —
    /// its class has no gravity resolve to clamp it (walkers get <see cref="VerticalMotion.Ground"/>), and the
    /// buoyant ease is slow on purpose. The floor is looked for through the rock in both directions (the
    /// cave under it counts, the surface above it counts, whichever is nearer); with none in reach the
    /// body is lifted to the first clear cell above. Air hoverers keep their canopy freedom and are left alone.</summary>
    private void LiftEmbeddedHoverer(CombatEntity c, CreatureSpecies sp, MotionClass motion)
    {
        if (motion != MotionClass.Hoverer || sp.Habitat == CreatureHabitat.Air
            || !CreatureBodyBlocked(sp, c.Position, foliagePasses: false))
        {
            return;
        }

        int x = (int)System.Math.Floor(c.Position.X), z = (int)System.Math.Floor(c.Position.Z);
        int y = (int)System.Math.Floor(c.Position.Y);
        if (TryNearestStandableThroughRock(x, z, y, CreatureHeadroom(sp), CreatureWideGroundScan, out int feet, canopyCarries: false) // #2945
            && !CreatureBodyBlocked(sp, new Vector3f(c.Position.X, feet, c.Position.Z), foliagePasses: false))
        {
            c.Position = new Vector3f(c.Position.X, feet, c.Position.Z);
            return;
        }

        for (int dy = 1; dy <= CreatureWideGroundScan; dy++)
        {
            var lifted = new Vector3f(c.Position.X, y + dy, c.Position.Z);
            if (!CreatureBodyBlocked(sp, lifted, foliagePasses: false))
            {
                c.Position = lifted;
                return;
            }
        }
    }

    /// <summary>Every barrier a step must pass (#1331): ship hull, energy fence, a gas giant's gas sea (#2583), the
    /// terrain gate (wild fauna; companions keep their freedom to follow the owner anywhere), and the swept body
    /// check — swept at the RAISED height when the step is a ledge the animal will have climbed first, so the
    /// ledge's own block doesn't read as a wall the way it used to.</summary>
    private bool StepBlocked(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f cur, Vector3f cand,
        bool needsRise, bool terrainGates, int? nextFeet = null)
    {
        // #3148: an animal in the air is also held by a fence standing below its path.
        bool airborne = motion == MotionClass.Hoverer || (motion == MotionClass.Flier && c.Vert.Flight != FlightPhase.Perched);
        if (EntityBlockedByShip(cand, CreatureShipMargin(sp)) || BlockedByEnergyFence(cur, cand, airborne))
        {
            return true; // body-aware hull guard for large species (#1320)
        }

        // #2583: a gas giant's gas sea carries nobody — not even a companion, which keeps its freedom from the terrain gate.
        if (StepsIntoGasSea(sp, motion, cur, cand, nextFeet))
        {
            return true;
        }

        // #3453: a wild ground animal does not walk into a town — or deeper into one it is already in (GameServerCreatureTownRules).
        if (TownStepBlocked(c, sp, cur, cand, nextFeet))
        {
            return true;
        }

        // #3656/#3658: nor into a train's wagon — and a tall roamer not onto a running line (GameServerRailWildlife).
        if (RailStepBlocked(c, sp, motion, cur, cand, nextFeet))
        {
            return true;
        }

        if (terrainGates && StepBlockedByTerrain(sp, motion, cur, cand, nextFeet))
        {
            return true;
        }

        // #1862: a shut door is a wall. A doorway is air in the block grid — the door fills it as an entity — so
        // the body sweep never saw one and animals strolled through shut gates into the yard the fill had just
        // declared fenced in (the fill counts shut doors as walls, #1315). NPCs have had this since #1775. Wild
        // fauna is stopped by every shut door; a companion only by a hand-operated one, since a proximity door
        // opens for its owner and never for the pet, and an owner who shut the gate behind them can open it.
        if (_doors.Count > 0 && ClosedDoorOnPath(cur, cand, handOperatedOnly: c.IsCompanion))
        {
            return true;
        }

        // #2943 / #2944: the drawn body past the centre column — a ray's wing disc, a big land animal's head.
        if (WingStepBlocked(c, sp, cur, cand) || HeadStepBlocked(c, sp, motion, cur, cand))
        {
            return true;
        }

        var from = needsRise ? new Vector3f(cur.X, cand.Y, cur.Z) : cur;
        return CreaturePathBlocked(sp, from, cand, motion == MotionClass.Flier && c.Vert.Flight == FlightPhase.Flying);
    }

    /// <summary>Whether a creature's horizontal step crosses a shut door's cells (#1862): sampled every
    /// <see cref="CreatureSweepStep"/> like the body sweep, at the step's height, so a fast hunter cannot hop the
    /// one-cell doorway between two samples. See <see cref="ClosedDoorBlocks"/> for what a door covers.</summary>
    private bool ClosedDoorOnPath(Vector3f from, Vector3f to, bool handOperatedOnly)
    {
        float dx = to.X - from.X, dz = to.Z - from.Z;
        float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
        int steps = System.Math.Max(1, (int)System.Math.Ceiling(dist / CreatureSweepStep));
        for (int s = 1; s <= steps; s++)
        {
            float f = s / (float)steps;
            if (ClosedDoorBlocks(new Vector3f(from.X + dx * f, to.Y, from.Z + dz * f), handOperatedOnly))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Test seam (#1862): every barrier of <see cref="StepBlocked"/> — hull, fence, terrain gate, shut
    /// doors, body sweep — for a wild creature stepping to <paramref name="next"/> from where it stands; with
    /// <paramref name="terrainGates"/> false, as a companion steps (#2583).</summary>
    public bool CreatureStepBlockedForTest(string creatureId, Vector3f next, bool terrainGates = true)
    {
        var c = _creatures.First(x => x.Id == creatureId);
        var sp = _speciesById[c.SpeciesId];
        return StepBlocked(c, sp, EffectiveMotion(c, sp), c.Position, next, needsRise: false, terrainGates);
    }

    /// <summary>#2668: how long a hunter or fleer keeps to a detour it found round an obstacle (creature-clock seconds).</summary>
    private const double CreatureDetourHoldSeconds = 0.5;

    /// <summary>#2668: a walker takes off this long before it reaches a one-block rise ahead, so its feet are up as it
    /// arrives at the lip (a one-block jump is above the lip from ~0.2 s to ~0.5 s after take-off at normal gravity).</summary>
    private const float RunningJumpLeadSeconds = 0.3f;

    /// <summary>#2668: the running jump's take-off. A walker on the ground that will reach a jumpable one-block rise within
    /// <see cref="RunningJumpLeadSeconds"/> launches now — under exactly the checks the jump at the lip would pass (step-up
    /// limit, head room where it takes off, every barrier of the step onto the ledge) — and keeps walking while it rises.</summary>
    private void TryRunningJump(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f cur, Vector3f next, float dt,
        bool terrainGates)
    {
        float dx = next.X - cur.X, dz = next.Z - cur.Z;
        float len = (float)System.Math.Sqrt((dx * dx) + (dz * dz));
        if (len < 1e-4f || dt <= 0f)
        {
            return;
        }

        float lead = System.Math.Min(0.9f, len / dt * RunningJumpLeadSeconds);
        var ahead = new Vector3f(next.X + (dx / len * lead), next.Y, next.Z + (dz / len * lead));
        if ((int)System.Math.Floor(ahead.X) == (int)System.Math.Floor(next.X) && (int)System.Math.Floor(ahead.Z) == (int)System.Math.Floor(next.Z))
        {
            return; // nothing new within reach of the lead
        }

        var aheadCand = PreviewStep(sp, motion, next, ahead, out bool needsRise, out int riseFeet, out int? aheadFeet);
        if (!needsRise
            || riseFeet - (int)System.Math.Floor(next.Y) > CreatureMotion.StepUpLimit(motion)
            || CreatureBodyBlocked(sp, new Vector3f(next.X, riseFeet, next.Z))
            || StepBlocked(c, sp, motion, next, aheadCand, needsRise: true, terrainGates, aheadFeet))
        {
            return;
        }

        VerticalMotion.Launch(ref c.Vert, LedgeJumpImpulse(sp));
    }

    /// <summary>#2668: <paramref name="stepped"/> held inside the column <paramref name="cur"/> stands in (a hair from its
    /// edges) — how far a jumper may move on while its feet are still below the ledge ahead.</summary>
    private static Vector3f InsideOwnColumn(Vector3f cur, Vector3f stepped)
    {
        const float Margin = 0.02f;
        float x0 = (float)System.Math.Floor(cur.X), z0 = (float)System.Math.Floor(cur.Z);
        return new Vector3f(
            System.Math.Clamp(stepped.X, x0 + Margin, x0 + 1f - Margin),
            cur.Y,
            System.Math.Clamp(stepped.Z, z0 + Margin, z0 + 1f - Margin));
    }

    /// <summary>Turns angle <paramref name="cur"/> toward <paramref name="target"/> by at most <paramref name="maxDelta"/>
    /// radians along the short way.</summary>
    private static float TurnAngleToward(float cur, float target, float maxDelta)
    {
        float diff = (float)System.Math.IEEERemainder(target - cur, 2 * System.Math.PI);
        return System.Math.Abs(diff) <= maxDelta ? cur + diff : cur + (System.Math.Sign(diff) * maxDelta);
    }

    /// <summary>The jump that clears a one-block ledge on this world (Q9: lighter worlds jump higher, exactly
    /// like the player's); a gliding ground bird gets the same height under its reduced airborne gravity.</summary>
    private float LedgeJumpImpulse(CreatureSpecies sp)
    {
        float g = VerticalMotion.Gravity(_gravityFactor) * (CreatureMotion.Glides(sp) ? VerticalMotion.GlideGravityScale : 1f);
        return VerticalMotion.ImpulseFor(g, VerticalMotion.JumpHeightFor(_gravityFactor));
    }

    /// <summary>The motion class in effect for a creature right now (#1334): amphibians swim while their feet
    /// are in water and walk/crawl ashore, with one cell of hysteresis so a shoreline animal doesn't flicker
    /// between the two; everyone else keeps the class its body implies.</summary>
    private MotionClass EffectiveMotion(CombatEntity c, CreatureSpecies sp)
    {
        if (sp.Habitat != CreatureHabitat.Amphibian)
        {
            return CreatureMotion.ClassOf(sp);
        }

        int x = (int)System.Math.Floor(c.Position.X), y = (int)System.Math.Floor(c.Position.Y), z = (int)System.Math.Floor(c.Position.Z);
        bool feetWet = _creatureWaterId != 0 && _world.GetBlockIfLoaded(new Vector3i(x, y, z)).Value == _creatureWaterId;
        bool belowWet = _creatureWaterId != 0 && _world.GetBlockIfLoaded(new Vector3i(x, y - 1, z)).Value == _creatureWaterId;
        c.Vert.InWater = feetWet || (c.Vert.InWater && belowWet);
        return CreatureMotion.EffectiveClass(sp, c.Vert.InWater);
    }

    /// <summary>The embedded-body check (#1320 sleepers, #1357 awake creatures too). False when the body sits
    /// clear of every colliding block. Otherwise the creature is roused (<see cref="CreatureWakeSeconds"/>)
    /// and stepped to the nearest clear standable spot within <see cref="SleeperRelocateRadius"/> — water life to
    /// the nearest water that holds its body (#3551) — or, boxed in on every side, queued for removal (the caller
    /// drops it after the loop; the list can't change mid-iteration).</summary>
    private bool DisplaceEmbeddedCreature(CombatEntity creature, CreatureSpecies sp)
    {
        if (!CreatureBodyBlocked(sp, creature.Position))
        {
            return false;
        }

        // #3551: a standable cell is dry by definition (a fluid fails its headroom), so the rescue set a fish on land — a
        // fish too tall for the water under a walkway laid on its pond landed on the walkway.
        bool moved = sp.Habitat == CreatureHabitat.Water
            ? TryFindWaterSpotNear(sp, creature.Position, out var clear)
            : TryFindClearSpotNear(sp, creature.Position, out clear);
        if (moved)
        {
            creature.Position = clear;
            creature.AwakeOverrideTimer = CreatureWakeSeconds; // it wakes up and walks off, like a creature you bump
        }
        else
        {
            _creatureEvictions.Add(creature);
        }

        return true;
    }

    /// <summary>A block the player just placed may have landed inside an awake creature's body (#1357): every
    /// creature whose body column holds the cell re-validates on its next tick instead of waiting out
    /// <see cref="AwakeBodyCheckInterval"/> — and before the vertical resolve could hop it onto the new block.</summary>
    private void NudgeCreatureBodyChecks(Vector3i cell)
    {
        foreach (var c in _creatures)
        {
            int x = (int)System.Math.Floor(c.Position.X), y = (int)System.Math.Floor(c.Position.Y), z = (int)System.Math.Floor(c.Position.Z);
            if (z == cell.Z && cell.Y >= y && cell.Y < y + CreatureBodyMaxHeight
                && WorldConstants.WrapDeltaX(x - cell.X, _world.Circumference) == 0)
            {
                c.NextBodyCheckAt = 0;
            }
        }
    }

    private static readonly float[] SteerOffsets = { 0.61f, 1.22f, 1.92f }; // ±35°, ±70°, ±110°

    /// <summary>Probes alternative headings around a blocked step (#651): the intended step length swung
    /// ±35°/±70°/±110° off the blocked heading (side order fixed per individual, so a herd doesn't wheel in
    /// unison), first candidate that passes every barrier wins and becomes the new heading — contour/wall
    /// following. A detour never takes a ledge (an animal skirting an obstacle follows the contour, it doesn't
    /// start a climb sideways). Returns false when all probes are blocked (caller falls back to the re-roll).</summary>
    private bool TrySteerAround(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f stepped,
        bool terrainGates, out Vector3f detour, out float heading)
    {
        detour = default;
        heading = 0f;
        float dx = stepped.X - c.Position.X, dz = stepped.Z - c.Position.Z;
        float len = (float)System.Math.Sqrt(dx * dx + dz * dz);
        if (len < 1e-5f)
        {
            return false; // not actually moving — nothing to steer
        }

        float baseHeading = (float)System.Math.Atan2(dz, dx);
        float side = (StableStringHash(c.Id) & 1) == 0 ? 1f : -1f;
        foreach (float off in SteerOffsets)
        {
            for (int s = 0; s < 2; s++)
            {
                float h = baseHeading + off * (s == 0 ? side : -side);
                var swung = new Vector3f(
                    c.Position.X + (float)System.Math.Cos(h) * len,
                    c.Position.Y,
                    c.Position.Z + (float)System.Math.Sin(h) * len);
                var cand = PreviewStep(sp, motion, c.Position, swung, out bool needsRise, out _, out int? nextFeet);
                if (!needsRise && !StepBlocked(c, sp, motion, c.Position, cand, false, terrainGates, nextFeet))
                {
                    detour = cand;
                    heading = h;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The Y a step candidate is evaluated at (#1331), without touching any state: for a ground mover heading
    /// into a higher column that is the ledge's feet cell (it will have jumped or climbed before it steps
    /// over — <paramref name="needsRise"/> tells the caller the feet are still below it); otherwise the
    /// current Y (fliers, hoverers and swimmers ease in place; a drop is taken by falling after the step).
    /// <paramref name="nextFeet"/> hands the target column's feet cell on to the gate and the vertical resolve
    /// (#1367) so one column change probes the ground once, not three times; null when nothing was probed.
    /// </summary>
    private Vector3f PreviewStep(CreatureSpecies sp, MotionClass motion, Vector3f cur, Vector3f stepped,
        out bool needsRise, out int riseFeet, out int? nextFeet)
    {
        needsRise = false;
        riseFeet = 0;
        nextFeet = null;
        if (!CreatureMotion.IsGroundBound(motion))
        {
            return new Vector3f(stepped.X, cur.Y, stepped.Z);
        }

        int cx = (int)System.Math.Floor(cur.X), cz = (int)System.Math.Floor(cur.Z);
        int nx = (int)System.Math.Floor(stepped.X), nz = (int)System.Math.Floor(stepped.Z);
        if (cx == nx && cz == nz)
        {
            return new Vector3f(stepped.X, cur.Y, stepped.Z);
        }

        int refY = (int)System.Math.Floor(cur.Y);
        int feet = GroundFeetFor(sp, nx, nz, refY);
        nextFeet = feet;
        if (feet > cur.Y)
        {
            riseFeet = feet;
            needsRise = VerticalMotion.IsBelow(cur.Y, feet);
            return new Vector3f(stepped.X, feet, stepped.Z);
        }

        return new Vector3f(stepped.X, cur.Y, stepped.Z);
    }
}
