# Tasks

## 1. First milestone: load, draw, move
- [x] 1.1 Create `src/EssenceOfDoom` (net8.0) with Raylib-cs
- [x] 1.2 WAD directory reader and WAD locator
- [x] 1.3 Map lump parsing; BSP point-in-sector; even-odd "outside the map" test
- [x] 1.4 Dump E1M1's things and specials to decide scope (specials 1, 11, 36, 48, 88; zombieman, shotgun guy, imp; no keys)
- [x] 1.5 Spawn player and skill-filtered things
- [x] 1.6 Collision and sliding with DOOM's step and height rules
- [x] 1.7 Renderer: fills, lines, symbols, follow camera, overview

## 2. Toward playable E1M1
- [x] 2.1 Use lines; doors (plus keyed doors); lift; lowering floor; exit
- [x] 2.2 Weapons, hitscan, noise alert
- [x] 2.3 Monster look, sight, chase, attacks, pain, infighting, death and drops
- [x] 2.4 Imp fireballs; barrels and radius damage
- [x] 2.5 Pickups, armor, nukage, secrets, death and restart, intermission stats
- [x] 2.6 Alert visuals: hollow/solid states, wake ring, attack telegraph, noise flash
- [x] 2.7 HUD, messages, help line

## 3. Verification and fixes
- [x] 3.1 xUnit tests on the real E1M1 (19 tests)
- [x] 3.2 Flood-fill test: exit reachable from start under the real collision rules
- [x] 3.3 Fix test premises that were wrong about E1M1 (nobody near the start hears you; most UV shotgun guys are deaf)
- [x] 3.4 Performance: block grid for line queries (14.5 s → about 1 s per simulated minute, all monsters awake)
- [x] 3.5 Render Blocking-flagged windows and railings as barriers, not walkable steps
- [x] 3.6 Screenshot and synthetic-input runs of the real window; clamp the window to small monitors; quiet the raylib log
- [x] 3.7 Stop at playable E1M1 and report
