# Project Context

## Purpose
Essence of DOOM is an experiment. It asks how much of DOOM can be removed before it stops feeling
like DOOM. It loads the original `DOOM1.WAD` and plays E1M1 as a flat, top-down line drawing. The
gameplay structure is kept (geometry, placement, movement, monster behaviour) and almost all of
the presentation is stripped away. The original brief is in [`docs/brief.md`](../docs/brief.md).

Success means a genuinely playable E1M1 that tells us something about which parts of DOOM carry
its feel. It does not mean completeness or fidelity.

## Tech Stack
- C# / .NET 8 (`net8.0`), nullable enabled
- [Raylib-cs](https://github.com/ChrisDill/Raylib-cs) 8.1 for the window, input and 2D drawing
- xUnit for tests that play the real E1M1 headlessly

## Project Conventions

### Code Style
- Readable, boring code over clever code. Plain classes with public fields where that's simplest.
- No ECS, no plugin system, no engine abstraction layers. `Game` is one `partial class` split by
  topic (`Game.cs`, `Physics.cs`, `Monsters.cs`, `Specials.cs`).
- Comments explain *why* and which DOOM behaviour is being imitated (e.g. "like DOOM's P_TryMove").
- Units are DOOM map units. Angles are radians, with 0 = east and counter-clockwise positive.
  Time is measured in tics (35 per second).

### Architecture Patterns
- The simulation (`Game`) knows nothing about rendering or raylib. It runs at a fixed 35 Hz tic
  rate and takes one `PlayerInput` per tic.
- `Renderer` reads `Game` state and interpolates positions between tics for smooth display.
- Level data lives in `Level`. Runtime state that DOOM also keeps on map structures (sector
  heights, sound targets, used-up triggers) is mutated in place. Restarting reloads the level from the WAD.

### Testing Strategy
- Tests load the real E1M1 and drive `Game.Update` directly. They check behaviour (collision,
  doors, waking, attacks, death, exit), not implementation details.
- One flood-fill test proves the exit is reachable from the start under the actual collision rules.
- Run with `dotnet test -c Release`. Windows Smart App Control on the dev machine sometimes blocks
  freshly built *Debug* test assemblies.

### Git Workflow
- Local repository, `master` branch. WAD files are git-ignored and never committed.

## Domain Context
- WAD: DOOM's data container. A map is a group of lumps (THINGS, LINEDEFS, SIDEDEFS, VERTEXES,
  SEGS, SSECTORS, NODES, SECTORS, ...).
- Sector: a region with a floor height, a ceiling height and a special. Doors are sectors whose
  ceiling rises; lifts are sectors whose floor lowers.
- Linedef special: an action triggered by use, by walking over the line, or by shooting it.
- Tic: 1/35 s, DOOM's simulation step. Most of DOOM's timings are stated in tics.
- Skill: 1–5. Thing flags select which things appear on which skill.

## Important Constraints
- Scope discipline: every feature must answer "do we need this to test whether the essence of
  DOOM survives?" No menus, sound, original graphics, save games, multiplayer, demos, config
  screens, multi-IWAD or mod support.
- Preserve the gameplay consequence of DOOM's rules, not every historical implementation detail.
- Stop at a playable E1M1 and ask before adding more.

## External Dependencies
- `DOOM1.WAD` (shareware v1.9) supplied by the user. It is found via `--wad`, the `DOOMWAD` environment
  variable, the working/executable directories and their parents, or `~/Downloads`.
