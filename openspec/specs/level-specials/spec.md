# level-specials Specification

## Purpose
Make the level itself interactive: doors, the lift, a lowering floor and the exit, following
DOOM's linedef specials and movement speeds. Support only the specials E1M1 uses, plus keyed doors.

## Requirements

### Requirement: Doors
Using a door line (special 1, 26, 27, 28 repeatable; 31, 32, 33, 34 open-and-stay) SHALL raise the
back sector's ceiling to 4 below the lowest neighbouring ceiling at 2 units/tic. Repeatable doors
SHALL wait 150 tics, then close at 2 units/tic. A closing door SHALL reverse if anything would no longer fit
under it. Using a closing door SHALL reopen it. A player using an opening or open repeatable door SHALL
close it.

#### Scenario: Open, pass and close
- **WHEN** the player opens E1M1's first door and walks through
- **THEN** they end up on the far side and the door is fully shut again ten seconds later

### Requirement: Keyed doors
Specials 26/32 need blue, 27/34 yellow, and 28/33 red (card or skull). Without the key the player SHALL
see "You need a <colour> key to open this door." and nothing SHALL happen.
E1M1 has no keys, so this requirement is implemented but not exercised by the target map.

### Requirement: Lifts
Crossing special 10 (once) or 88 (repeatable) SHALL lower every idle sector with the line's tag to its
lowest neighbouring floor at 4 units/tic, wait 105 tics, and rise back. A rising lift SHALL go back down
if something would no longer fit. Things standing on a moving floor SHALL ride it.

#### Scenario: E1M1 lift
- **WHEN** the player walks off the lift across line 195
- **THEN** sector 70's floor starts going down

### Requirement: Lowering floor
Crossing special 36 (once) SHALL lower every idle tagged sector to 8 above its highest neighbouring
floor at 4 units/tic.

### Requirement: Exit
Using a special 11 or 51 switch, or crossing special 52, SHALL end the level. The game SHALL show
kills, items and secrets percentages and the time, and SHALL offer R to play again.

#### Scenario: Exit switch
- **WHEN** the player stands 30 units in front of E1M1's exit switch, aims at it and presses use
- **THEN** the game state becomes Exited

### Requirement: Unsupported specials
Any other special SHALL be ignored, with a single console message per special number. Special 48
(scrolling texture) SHALL be ignored silently because it has no gameplay effect.

### Requirement: The level can be finished
With the doors open, the exit switch SHALL be reachable from the player start under the actual
movement and collision rules.

#### Scenario: Flood fill
- **WHEN** every door is opened and a 12-unit grid is flood-filled from the start with the player's move rules
- **THEN** a cell within 24 units of the exit switch is reached
