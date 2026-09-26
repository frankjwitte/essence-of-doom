namespace EssenceOfDoom;

public enum MobjKind { Player, Monster, Pickup, Obstacle, Barrel }

public enum AiState { Dormant, Chase, Attack, Pain, Dead }

/// <summary>A thing in the world: the player's body, a monster, a pickup, a barrel or a solid decoration.</summary>
public sealed class Mobj
{
    public MobjKind Kind;
    public int Type;               // doomednum
    public Vec2 Pos;
    public Vec2 PrevPos;           // position at the start of the tic, for smooth rendering
    public double Z;               // feet height
    public double Radius, Height;
    public double Angle;           // radians, 0 = east, counter-clockwise
    public int Health;
    public bool Solid, Shootable;
    public bool Removed;           // picked up / gone
    public bool Dropped;           // pickup dropped by a dead monster (gives less ammo)
    public MonsterInfo? Info;
    public PickupInfo? Pickup;

    // Monster AI state.
    public AiState State = AiState.Dormant;
    public Mobj? Target;
    public int StateTics;          // tics remaining in the current attack/pain state
    public int MoveDir = Dir.None;
    public int MoveCount;
    public int ChaseTimer;         // tics until the next chase decision
    public int ReactionTime = 8;
    public int Threshold;          // chase decisions left before the monster may switch targets
    public bool JustAttacked, JustHit, Ambush;
    public bool AttackFired;
    public bool AttackIsMelee;
    public int LookTimer;
    public int WakeTick = -1000;   // for the "woke up" visual pulse
    public int PainTick = -1000;
    public int DeathTick = -1000;

    public bool IsDead => Health <= 0;
    public bool IsMonster => Kind == MobjKind.Monster;
    public double MidZ => Z + Height / 2;
}

/// <summary>DOOM's eight movement directions; monsters only ever walk along these.</summary>
public static class Dir
{
    public const int East = 0, NorthEast = 1, North = 2, NorthWest = 3, West = 4, SouthWest = 5, South = 6, SouthEast = 7, None = 8;
    public static readonly double[] X = { 1, 0.7071, 0, -0.7071, -1, -0.7071, 0, 0.7071 };
    public static readonly double[] Y = { 0, 0.7071, 1, 0.7071, 0, -0.7071, -1, -0.7071 };
    public static int Opposite(int d) => d == None ? None : (d + 4) % 8;
}

public enum Weapon { Fist = 1, Pistol = 2, Shotgun = 3, Chaingun = 4 }

public sealed class Player
{
    public Mobj Mo = null!;
    public Vec2 Velocity;
    public int Armor, ArmorType;          // ArmorType 1 = green (absorbs 1/3), 2 = blue (absorbs 1/2)
    public int Bullets = 50, Shells;
    public readonly HashSet<Weapon> Weapons = new() { Weapon.Fist, Weapon.Pistol };
    public Weapon Current = Weapon.Pistol;
    public readonly HashSet<ThingTypes.KeyColor> Keys = new();
    public int FireCooldown;              // tics until the weapon can act again
    public int PendingShotTics = -1;      // tics until a started attack actually fires
    public int Refire;                    // consecutive shots, makes pistol/chaingun inaccurate
    public int DamageFlash, PickupFlash;
    public int Kills, Items, Secrets;
    public const int MaxBullets = 200, MaxShells = 50;
    public const double ViewHeight = 41;
}

public sealed class Projectile
{
    public Vec2 Pos;
    public Vec2 PrevPos;           // position at the start of the tic, for smooth rendering
    public double Z, VelZ;
    public Vec2 Vel;
    public double Radius = 6, Height = 8;
    public Mobj Source = null!;
    public int DamageMult = 3;
    public bool Dead;
}

public enum EffectKind { Tracer, Puff, Explosion, WakeRing, Blood }

public sealed class Effect
{
    public EffectKind Kind;
    public Vec2 A, B;
    public int StartTick, Duration;
    public bool FromMonster;
}
