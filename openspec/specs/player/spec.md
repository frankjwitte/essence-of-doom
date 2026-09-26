# player Specification

## Purpose
Let one player move through the real level with DOOM's movement and collision rules, fight with
DOOM's weapons, collect pickups, take damage, die, and finish the level.

## Requirements

### Requirement: Screen-relative movement with DOOM momentum
The player SHALL move in the world direction given by WASD or the arrow keys (up = north), always
running unless Shift is held. Each tic the input SHALL add thrust (1.5625 units/tic² running,
0.78 walking), then velocity SHALL be multiplied by DOOM's friction 0.90625. Speeds below 0.05 SHALL
snap to zero. Top speed is therefore about 16.7 units/tic running.

#### Scenario: Walking north
- **WHEN** W is held
- **THEN** the player accelerates north and stops shortly after W is released

### Requirement: Collision with walls, heights and things
The player (radius 16, height 56) SHALL NOT overlap one-sided lines, lines flagged Blocking, or solid
things. The player SHALL NOT enter a position where the ceiling-to-floor gap is below 56, where the
ceiling is less than 56 above their feet, or where the floor rises more than 24 above their feet.
Moves SHALL be split into steps no longer than half the radius. When blocked, the player SHALL first
move as close as possible to the obstacle, then slide along it (along the blocking line, or
tangentially around a blocking thing), then fall back to moving along each axis separately.
The player's height SHALL be the highest floor their footprint touches.

#### Scenario: Wandering never escapes
- **WHEN** the player walks in random directions for a minute with no monsters present
- **THEN** they never leave the map and never overlap a one-sided wall

#### Scenario: Closed door blocks
- **WHEN** the player walks into a closed door
- **THEN** they stop on the near side

### Requirement: Use lines
Pressing use (E, Space or right mouse) SHALL trace 64 units from the player in the aim direction.
The first line crossed that has a special SHALL be activated if the player is on its front side.
A wall or closed opening crossed first SHALL stop the trace.

#### Scenario: Opening a door
- **WHEN** the player stands 40 units in front of an E1M1 door, aims at it and presses use
- **THEN** the door opens enough to walk through within 40 tics

### Requirement: Mouse aim and weapons
The player SHALL face the mouse cursor. Holding fire (left mouse or Ctrl) SHALL attack with the
current weapon, using DOOM's delay before the shot and its repeat period:

| Weapon | Key | Delay / period (tics) | Attack |
|---|---|---|---|
| Fist | 1 | 4 / 17 | (1..10)×2 damage, 64-unit reach |
| Pistol | 2 | 4 / 14 | 1 bullet, 5×(1..3) damage |
| Shotgun | 3 | 3 / 37 | 7 pellets, 5×(1..3) each, ±5.6° spread |
| Chaingun | 4 | 0 / 4 | 1 bullet per shot |

The first pistol or chaingun shot of a burst SHALL be exact; later shots in the same burst SHALL spread ±5.6°.
Bullets and shells SHALL be consumed when the shot fires. With no ammo, the player SHALL switch to the
best owned weapon that has ammo. The player SHALL start with fist, pistol and 50 bullets.

#### Scenario: Killing a zombieman
- **WHEN** the player holds fire aimed at a visible zombieman 140 units away
- **THEN** it dies, the kill counter reaches 1, and a clip is dropped where it fell

### Requirement: Every attack makes noise
Starting any attack, including a punch, SHALL raise a noise alert from the player's position
(see the monsters spec for how it spreads).

### Requirement: Pickups
Touching a pickup (within the combined radius, height overlap from −8 to +56) SHALL apply it if it is
useful, show its message, and remove it. Rules:
- Stimpack +10 and medikit +25 health, only below 100 and capped at 100. Health bonus +1 and
  Supercharge +100, capped at 200, always picked up.
- Armor bonus +1 (cap 200, becomes green armor if there was none). Green armor sets 100 (type 1),
  blue armor sets 200 (type 2), only if that is an improvement.
- Clip 10, box of bullets 50, 4 shells, box of shells 20; max 200 bullets / 50 shells. Items dropped by
  monsters give half.
- Shotgun: owned and selected, +8 shells. Chaingun: owned and selected, +20 bullets.
- Keycards and skull keys add the matching key colour.
Health bonus, armor bonus and Supercharge SHALL count towards the level's items total.

#### Scenario: Clip
- **WHEN** the player walks onto a clip with 50 bullets
- **THEN** they have 60 bullets and the clip is gone

### Requirement: Damage, armor and death
Damage to the player SHALL be halved on skill 1. Armor SHALL absorb 1/3 (green) or 1/2 (blue) of
damage, up to the armor remaining. Damage SHALL push the player away from its source and flash the
screen edge red. At 0 health the player SHALL die: controls stop, "YOU DIED" is shown, and R restarts
the level from the WAD. The world SHALL keep running while the player is dead.

#### Scenario: Dying to a zombieman
- **WHEN** a player with 15 health idles in view of an awake zombieman
- **THEN** within two minutes the game state becomes Dead with health 0

### Requirement: Damaging floors and secrets
While standing on the floor of a sector with special 7, 5, or 16/4, the player SHALL take 5, 10 or 20
damage every 32 tics. Entering a sector with special 9 SHALL count a secret once.

#### Scenario: Nukage
- **WHEN** the player stands in E1M1's nukage pool for 3 seconds
- **THEN** their health is below 100
