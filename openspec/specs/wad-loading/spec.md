# wad-loading Specification

## Purpose
Read the original DOOM data (the WAD container, one map's geometry and its things) and answer the
spatial questions the rest of the game asks of it: which sector a point is in, and which lines are nearby.

## Requirements

### Requirement: Locate the IWAD
The game SHALL look for `DOOM1.WAD` in this order and use the first file that exists: the `--wad`
argument, the `DOOMWAD` environment variable, the current directory, the executable directory
and up to six of its parent directories, and the user's `Downloads` folder. File name matching SHALL
be case-insensitive on Windows.

#### Scenario: WAD at the repository root
- **WHEN** the game is started from `bin/Release/net8.0` with no arguments and `Doom1.WAD` is at the repo root
- **THEN** that file is loaded

#### Scenario: No WAD anywhere
- **WHEN** no candidate file exists
- **THEN** the game prints how to supply the WAD and exits with code 1 without opening a window

### Requirement: Load a map's lumps
The game SHALL parse VERTEXES, SECTORS, SIDEDEFS, LINEDEFS, THINGS, SEGS, SSECTORS and NODES for the
requested map (default `E1M1`, selectable with `--map`). It SHALL keep each linedef's flags, special, tag and
front/back sectors, and each sector's floor, ceiling, special and tag.

#### Scenario: E1M1 counts
- **WHEN** E1M1 is loaded from shareware DOOM1.WAD v1.9
- **THEN** there are 467 vertices, 475 lines, 85 sectors and 138 things

### Requirement: Point-in-sector lookup
The game SHALL find the sector containing a point by walking the map's BSP nodes (as R_PointInSubsector
does). It SHALL also offer a variant that returns "outside the map" when an even-odd test against the
candidate sector's boundary lines fails.

#### Scenario: Player start
- **WHEN** looking up the player 1 start position in E1M1
- **THEN** a real sector is returned, not "outside the map"

### Requirement: Nearby-line queries
The level SHALL index its lines in a grid of 128-unit cells. It SHALL answer "lines that may touch this
box" and "lines that may cross this segment" without scanning every line, so that collision and
sight stay cheap with every monster awake.

#### Scenario: Simulation cost
- **WHEN** all 29 Ultra-Violence monsters are awake and chasing for one minute of game time
- **THEN** the simulation completes in under 6 seconds of real time (about 1 s measured in Release)

### Requirement: Spawn things by skill
Things SHALL be spawned only if their flags include the bit for the selected skill (skills 1–2: bit 1,
skill 3: bit 2, skills 4–5: bit 4). Multiplayer-only things (flag 16) SHALL NOT be spawned. The ambush
flag (8) SHALL be kept on monsters. Purely decorative things and gore SHALL NOT be spawned.

#### Scenario: Monster counts per skill
- **WHEN** E1M1 is started on skill 4 and on skill 3
- **THEN** there are 29 and 6 monsters respectively
