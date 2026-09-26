# monsters Specification

## Purpose
Reproduce the gameplay consequences of DOOM's monster behaviour: dormant until alerted by sight or
sound, a crude 8-direction chase through the real geometry, DOOM's attack rhythm, pain, infighting and
death. The point is to see whether a map full of these rules still feels like DOOM.

## Requirements

### Requirement: Monster types
Monsters SHALL use these stats (radius 20 and height 56 unless noted):

| Type | Doomednum | Health | Speed | Pain chance | Attack |
|---|---|---|---|---|---|
| Zombieman | 3004 | 20 | 8 | 200 | 1 hitscan bullet; drops clip |
| Shotgun guy | 9 | 30 | 8 | 170 | 3 hitscan pellets; drops shotgun |
| Imp | 3001 | 60 | 8 | 200 | claw (1..8)×3 or fireball (1..8)×3, speed 10 |
| Demon / Spectre | 3002 / 58 | 150 | 10 | 180 | bite (1..10)×4; radius 30 |
| Baron | 3003 | 1000 | 8 | 50 | claw (1..8)×10 or fireball (1..8)×8, speed 15; radius 24, height 64 |

E1M1 only contains the first three types. The others are table rows only, not exercised.

### Requirement: Dormancy and waking
A dormant monster SHALL check every 10 tics. It SHALL wake if the sector it stands in has heard a
noise from a living target, except that an ambush-flagged ("deaf") monster must also be able to see
that target. It SHALL also wake if it can see the player within 90° of its facing, or anywhere within 64 units.
Waking SHALL be recorded so the renderer can show it.

#### Scenario: Gunfire wakes some, not all
- **WHEN** the player makes a noise at the E1M1 start
- **THEN** within a second at least one monster is awake, and monsters behind closed doors stay dormant

### Requirement: Noise propagation
A noise alert SHALL flood outward from the emitter's sector through every two-sided line whose opening
is above zero. It SHALL pass through at most one sound-blocking line (flag 64). Every sector reached
SHALL remember the emitter as its sound target, as DOOM's P_RecursiveSound does.

### Requirement: Line of sight
Sight SHALL follow P_CheckSight's idea. From an eye at 3/4 of the looker's height, a visible slope
window onto the target's full height is narrowed at every two-sided line crossed, using that line's
opening. One-sided lines, closed openings, or an emptied window SHALL block sight.

#### Scenario: Closed door
- **WHEN** a closed door lies between two points
- **THEN** sight between them is blocked

### Requirement: Chase decisions
An awake monster SHALL make a chase decision every ChaseTics (zombieman 4, shotgun guy 3, imp 3,
demon 2, baron 3). It SHALL move smoothly along its current direction between decisions, covering Speed
units per decision. Each decision SHALL, in order:
1. count down reaction time (8 at spawn, 0 on skill 5) and the target-switch threshold;
2. if the target is gone, look for the player all around, and go dormant if not found;
3. right after an attack, pick a new direction;
4. melee if the target is within 64 − 20 + target radius and visible;
5. otherwise, only when the current movement run has ended, fire if the missile-range check passes.
   The check always passes just after being hurt and never passes during reaction time. Otherwise it fails
   with a chance that grows with distance (capped at 200/256; shooters without melee fire more);
6. otherwise continue, or choose a new direction when the run ends or the monster cannot move.

### Requirement: Eight-direction pathing
Choosing a direction SHALL follow P_NewChaseDir. Try the diagonal toward the target, then the two
cardinal directions (swapped at random or when the vertical distance dominates), then the old direction,
then all directions in a random rotation, and never the reverse unless nothing else works. A direction is
valid if a full-speed step is possible, and the new movement run lasts `random & 15` decisions.
Monsters SHALL use the same collision rules as the player. They also SHALL NOT step off a ledge
higher than 24, and SHALL be blocked by lines flagged block-monsters.

#### Scenario: Monsters stay inside the map
- **WHEN** monsters chase an idle player for 30 seconds
- **THEN** every monster is still inside a real sector

### Requirement: Monsters open doors
A monster blocked by a line with special 1, 32, 33 or 34 that is not flagged secret SHALL use it to
open the door, then wait and retry the same direction. Monsters SHALL NOT close doors. Monsters SHALL
only trigger the walk-over specials 10 and 88 (lifts).

### Requirement: Attacks
Starting an attack SHALL lock the monster for AttackTotal tics and fire once after AttackWindup tics
(zombieman 10/26, shotgun guy 10/30, imp 16/22, demon and baron 16/24), turning to face the target.
Hitscan pellets SHALL spread ±22.4° and deal (1..5)×3. Monsters with melee SHALL claw or bite
instead of shooting when in melee range at the moment of the attack. Missiles SHALL start at the
monster's height + 32, aim vertically at the target and advance half a step on spawn. They SHALL explode on
walls, on openings they don't fit through, on floors and ceilings, and on solid things.

#### Scenario: Imp fireball
- **WHEN** an imp is woken with the player 400 units away in view
- **THEN** a fireball is seen and the player's health drops

### Requirement: Pain, infighting and death
When hurt, a monster SHALL enter pain for PainTics with its pain chance. Pain cancels any
attack in progress and makes the next missile check succeed. Being hurt SHALL clear reaction time. If the
monster is not locked onto a target (threshold 0), it SHALL retarget whoever hurt it, player or monster,
lock on for 100 decisions, and wake if dormant. Projectiles SHALL NOT damage monsters of the same type as
their shooter. At 0 health a monster SHALL die, stop being solid or shootable, count as a kill, and spawn its drop.

### Requirement: Barrels
Barrels (radius 10, 20 health) SHALL be solid and shootable. 15 tics after dying they SHALL explode for
128 − distance damage (distance minus the victim's radius) to every shootable thing in sight. The
explosion SHALL be credited to whoever destroyed the barrel, which allows chain reactions.

#### Scenario: Shooting a barrel
- **WHEN** the player shoots a barrel from 90 units away
- **THEN** it explodes and the player is hurt
