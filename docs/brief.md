# Original brief

The brief this project was started from, verbatim.

---

Build a small experimental game called Essence of DOOM.
The question behind the experiment is:
How much can we remove from DOOM before it stops feeling like DOOM?
I want to preserve the gameplay structure while stripping away almost all of the presentation.
Core concept
Load an original DOOM WAD and play its maps as an extremely minimal top-down 2D game.
Think:

* white or very light background
* thin black map geometry
* extremely simple symbols/shapes for the player, monsters, doors, pickups and projectiles
* no textures
* no sprites
* no attempt to recreate DOOM's visual style
* no fake 3D

The map should look almost like an architectural/debug drawing.
Despite that visual simplicity, it should use the actual WAD data wherever practical:

* map geometry
* sectors
* linedefs
* things / monster placement
* player start
* doors
* keys
* pickups
* exit
* monster types

Start with DOOM1.WAD and E1M1.
The important part
This is not primarily a WAD viewer.
It must be playable.
The experiment is whether DOOM's level geometry, enemy placement, movement and simple enemy behaviour still produce something recognisably DOOM-like when almost everything else is removed.
The player should be able to:

* move around the actual level
* collide with walls
* open doors
* collect required keys
* pick up health/ammo where relevant
* shoot
* damage and kill monsters
* take damage
* die
* reach and activate the level exit

Monsters should at minimum:

* start at their original WAD positions
* have their original broad type distinctions
* be dormant until alerted where appropriate
* detect/hear/see the player using a reasonable approximation of DOOM behaviour
* pursue the player
* attack
* collide with level geometry
* die

Do not try to implement the complete DOOM engine.
Approximate behaviour where necessary. Preserve the gameplay consequence, not every historical implementation detail.
Visual idea
I want the player to have much more situational awareness than normal DOOM.
Initially render the entire discovered map, or even the entire map if that makes the experiment more interesting.
Represent entities minimally. For example:

* player: triangle showing facing direction
* monsters: small shapes, perhaps differentiated by shape/size
* projectiles: dots
* pickups: tiny symbols
* doors: visually distinguishable lines
* locked doors: indicate required key
* exit: clearly identifiable

When monsters become alerted, make that state visually apparent.
One of the experiences I want to see is:
I open or enter somewhere.
Several monsters elsewhere on the map wake up.
I can actually see them start moving through the geometry toward me.
That transition from a static, understandable map into an increasingly active system is important.
Technology
Use C# and .NET 8.
Choose a lightweight rendering/input library appropriate for a simple desktop 2D game. SDL2 is fine if practical, but use your judgement.
Keep the architecture simple.
Do not build an elaborate engine architecture, ECS, plugin system, editor, generic game framework, or abstraction hierarchy unless the current implementation genuinely requires it.
Readable boring code is preferred over clever code.
Development approach
First inspect the repository and determine what already exists.
Before changing anything, briefly state:

1. what is already there
2. what you intend to build
3. the smallest useful vertical slice

Then implement it.
The first meaningful milestone is:
Launch application → load DOOM1.WAD → load E1M1 → display actual map → spawn player and monsters → allow the player to move around with collision.
Once that works, continue toward a playable E1M1 rather than polishing infrastructure.
Run the application and tests yourself where possible.
Fix problems you encounter rather than merely documenting them.
Scope discipline
Do not add features merely because DOOM has them.
Every feature should answer:
Do we need this to test whether the essence of DOOM survives?
If not, leave it out.
Do not spend significant effort reproducing:

* original graphics
* original sound
* exact animation timing
* menus
* save games
* multiplayer
* demos
* configuration screens
* multiple IWAD compatibility
* mod support

Those can wait indefinitely, which is software-development terminology for "probably never."
The goal is a small playable experiment, not another DOOM source port.
When you reach a genuinely playable E1M1, stop and report:

* what works
* what was deliberately simplified
* what feels surprisingly DOOM-like
* what seems to be missing from the DOOM experience

Do not continue adding features after that point without asking.
