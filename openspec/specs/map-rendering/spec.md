# map-rendering Specification

## Purpose
Show the whole level as a plain architectural/debug drawing that gives the player far more situational
awareness than DOOM does. It should also make the moment a static map turns into an active system visible.

## Requirements

### Requirement: Whole map, no fog
The entire map SHALL always be drawn. No discovery or line-of-sight filtering is applied. The camera
SHALL follow the player, north up, with mouse-wheel zoom (0.12–3 px/unit, default 0.6). Tab SHALL toggle
an overview that fits the whole map on screen.

### Requirement: Floors as pale fills
Sectors SHALL be filled from a raster rendered once at load (4 map units per pixel, sector index per pixel)
and recoloured only when a sector's colour changes:
- outside the map: light grey (214, 214, 210)
- floors: near white, slightly darker the lower the floor (234–255 grey)
- damaging floors (specials 4, 5, 7, 16): pale green
- closed doors: tan, or the key's tint for keyed doors; closed secret doors: same as outside, so they read as solid wall

#### Scenario: A door opens
- **WHEN** a door's ceiling rises above its floor
- **THEN** its tan fill is replaced by the normal floor fill

### Requirement: Lines by gameplay meaning
Lines SHALL be drawn by what they mean for movement, like the automap:
- one-sided and secret-flagged lines: black walls
- two-sided lines flagged Blocking (windows, railings): dark grey, near wall weight
- floor difference over 24: dark grey (a ledge you can only drop from)
- floor difference up to 24: thin light grey (a walkable step)
- ceiling difference only: very light, 1 px; no difference: not drawn; "don't draw" flag: not drawn
- door lines: brown, or the key's colour; exit lines: thick green labelled EXIT

### Requirement: Minimal entity symbols
- Player: blue filled triangle pointing where they aim; a black × when dead.
- Monsters at their real radius (minimum 5 px): circle = zombieman, square = shotgun guy / baron,
  diamond = imp, pentagon = demon/spectre, each with a short facing tick.
- Corpses: small grey ×. Obstacles: grey dots. Barrels: green rings that flash orange before exploding.
- Pickups: red cross (health), tiny blue/green dots (bonuses), rings (armor), grey/brown blocks
  (bullets/shells), a bar with `SG`/`CG` (weapons), a diamond in the key colour (keys).
- Projectiles: orange dots. Hitscan: faint tracer lines for 5 tics, red for monsters and grey for the
  player. There are puffs at impacts and an expanding orange circle for explosions.

### Requirement: Alert state is obvious
Dormant monsters SHALL be drawn hollow and grey. Awake monsters SHALL be solid red, attacking monsters
dark red with a faint line to their target during wind-up, and monsters in pain white with a red ring.
A monster that just woke SHALL emit a red ring that expands for 30 tics.

#### Scenario: Watching the map wake up
- **WHEN** the player fires near a room of sleeping shotgun guys
- **THEN** they turn from hollow grey to red, pulse, and are seen moving through the geometry toward the player

### Requirement: Noise is visible
Sectors reached by a noise alert SHALL flash pink, fading over 20 tics. This shows how far gunfire carried.

### Requirement: HUD
A bottom bar SHALL show health (red at 25 or below), armor, the current weapon and its ammo, bullet and
shell counts, owned weapons and keys. The top-left SHALL show the map, skill and time, plus awake/alive
monster counts and kills, items and secrets against the totals. Pickup messages SHALL show for 3
seconds. A help line of controls SHALL show until H is pressed. Damage SHALL flash the screen edges red.

### Requirement: Smooth display of a 35 Hz simulation
Rendering SHALL run at display rate (vsync, up to 144 fps). Positions of things and projectiles SHALL
be interpolated between the previous and current tic.
