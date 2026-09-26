# Design: Playable top-down E1M1

## Context
This is a small experiment, not a source port. The brief asks for readable, boring code, no engine
architecture, DOOM's data wherever practical, and DOOM's behaviour approximated where exactness
isn't worth it. Each decision below is judged against one question: does it help test whether the
essence of DOOM survives?

## Goals / Non-Goals
- Goals: a playable E1M1 built from the real WAD; DOOM-faithful gameplay consequences; the map visibly
  "waking up"; verification without a human playtester.
- Non-Goals: original graphics or sound, exact animation timing, menus, saves, multiplayer, demos,
  config screens, other IWADs, mods, 3D of any kind.

## Decisions

### Rendering library: Raylib-cs instead of SDL2
One NuGet package bundles the native library and gives immediate-mode lines, polygons, text and
texture upload. That is all this game needs. SDL2 would need more setup and hand-written drawing
for the same result.

### Fixed 35 Hz simulation with interpolated rendering
DOOM's timings (weapon periods, door waits, attack wind-ups, chase frames) are in tics, so simulating
at 35 Hz lets those numbers be used directly. The renderer runs at display rate and interpolates
`PrevPos → Pos`, so motion doesn't look steppy. `Game` has no raylib dependency, which is why it can be
tested headlessly.

### One `Game` partial class, plain `Mobj`
There is a single `Mobj` class with a `Kind` (player body, monster, pickup, obstacle, barrel). There is
no ECS or inheritance tree. `Game` is split by topic across files. The only class hierarchy is
`Mover` (Door, Lift, FloorMover), because those three genuinely share "tick until done".

### Scope taken from the data
Before implementing specials, E1M1's things and linedef specials were dumped. Only what the map uses
was implemented: specials 1, 11, 36, 88 (48 ignored), and zombiemen, shotgun guys and imps. Keyed
doors and key pickups were added anyway because the brief lists keys explicitly, even though E1M1 has none.
A few other shareware monster rows exist in the table as data only.

### Point-in-sector: BSP plus an even-odd check
Walking the WAD's own NODES is exact inside the map and cheap. The BSP also assigns points *outside*
the map to some subsector, so the renderer (and tests) confirm with an even-odd test against the
candidate sector's lines to detect the void.

### Collision: circles, DOOM's height rules, slide by projection
DOOM uses axis-aligned boxes. Circles against segments give smoother wall sliding at any angle and
the same passable widths (32 units for the player). The rules that matter for gameplay are kept exactly:
24-unit step-up, the thing must fit between floor and ceiling, closing doors block, monsters won't drop
off ledges over 24, and Blocking / BlockMonsters flags apply. Sliding moves as far as possible
(a 5-step binary search), then projects the rest onto the blocking line or around the blocking
thing, then falls back to one axis at a time. Things have infinite height against each other, as in vanilla DOOM.

### Sight: DOOM's slope-window algorithm
P_CheckSight's core idea is easy to port. A vertical window from the eye to the target's top and bottom
is narrowed by each two-sided line crossed, and the result does not depend on the order lines are
visited. That gives correct behaviour for ledges, windows and stairs without a 3D model.

### Hitscan: "nearest visible thing on the ray" instead of vertical autoaim
Top-down with mouse aim, horizontal aim is the player's job. DOOM's vertical autoaim becomes: along the
2D ray, hit the nearest shootable thing that the shooter can see; if there is none, puff on the first wall
whose opening doesn't contain the shot height. Shooting up stairs or over railings behaves as expected.

### Monster movement smoothed between decisions
DOOM monsters jump `speed` units once per chase frame. Here the same distance is spread over the frame's
tics, and decisions (attack, new direction, door) still happen once per frame. This doesn't change
gameplay, but it makes "watching them come through the geometry" readable.

### Noise is P_RecursiveSound, and it is drawn
The wake-up experience depends on this rule, so it is ported directly: flood through open lines, at most
one sound-blocking line. It is also made visible (a pink sector flash) because in this experiment sound
isn't available to tell the player what they just alerted.

### Default skill 4 (Ultra-Violence)
On Hurt Me Plenty, E1M1 has only 6 monsters, too few to test "several monsters elsewhere wake up".
Ultra-Violence has 29. `--skill 3` gives DOOM's default.

### Floor fills from a one-time raster
Sector polygons in DOOM are not stored explicitly, and triangulating them was more work than the
brief justifies. Instead a sector-index raster (4 units/pixel) is built once at load. Recolouring per
frame costs a pass over the pixels only when some sector's colour changed (doors, lifts, noise
flash). The fills give doors a visible state and let damaging floors and height read at a glance.

### Lines drawn by gameplay meaning
The automap convention (draw a line only where floor or ceiling changes) is extended by what the line means
for movement: walkable step, drop-only ledge, impassable railing or window, door, exit. The first playtest
screenshot showed Blocking-flagged window sills as faint steps, which misled; they are now barrier-weight lines.

### Block grid added after measuring
The first version scanned all 475 lines for every query. With all 29 monsters awake that cost 7 ms per
tic (14.5 s per simulated minute). A 128-unit grid, the same idea as DOOM's BLOCKMAP but built at load,
brought it to about 1 s per minute. It was added only after measuring the problem.

### Verification without a human playtester
- The tests play the real E1M1 through `Game.Update`, including a flood fill that proves the exit is
  reachable from the start under the actual collision rules.
- The `--shot`, `--at x,y`, `--fire` and `--overview` developer flags render the real window and save a screenshot.
- One run drove the real window with synthetic Win32 keyboard and mouse input to confirm the full input path.

## Risks / Trade-offs
- Full-map awareness removes ambushes. That is intended by the brief, but it may be the thing that
  stops it feeling like DOOM. See Open Questions.
- There is no vertical dimension on screen. Stairs, lifts and ledges are just lines.
- Circle collision differs slightly from DOOM's boxes at corners.
- Only E1M1's specials exist. Other maps will load, but unsupported specials do nothing (they are logged).
- Windows Smart App Control can block freshly built Debug test assemblies. Release builds run reliably.

## Open Questions
- Would a "discovered map only" or line-of-sight fog toggle bring back DOOM's tension? This is the obvious next
  experiment, and it was deliberately not started without asking.
- Is the pink noise flash a fair stand-in for sound, or does it give too much information?
