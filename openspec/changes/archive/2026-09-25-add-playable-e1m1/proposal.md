# Change: Add a playable top-down E1M1

## Why
The brief ([docs/brief.md](../../../../docs/brief.md)) asks whether DOOM's level geometry, enemy
placement, movement and simple enemy behaviour still feel like DOOM once almost all the presentation
is removed. Answering that needs a real, playable E1M1 built from the original data, not a WAD viewer.

## What Changes
- New .NET 8 console app `src/EssenceOfDoom` using Raylib-cs.
- **wad-loading**: locate DOOM1.WAD, parse E1M1's geometry and things, BSP point-in-sector, block grid.
- **player**: DOOM momentum movement, collision with step and height rules, use, four weapons,
  pickups, armor, damage, death, nukage and secrets.
- **monsters**: dormancy, sight and noise waking, 8-direction chase, DOOM attack rhythm, pain,
  infighting, drops and barrels.
- **level-specials**: doors (plus keyed doors), lift, lowering floor, exit, noise flood.
- **map-rendering**: whole-map architectural drawing, alert-state visuals, noise flash, HUD.
- New xUnit project `tests/EssenceOfDoom.Tests` that plays the real E1M1 headlessly.

## Impact
- Affected specs: all five capabilities are new (`wad-loading`, `player`, `monsters`,
  `level-specials`, `map-rendering`).
- Affected code: whole repository (initial implementation).
- Requires the user to supply `DOOM1.WAD`. It is git-ignored.
