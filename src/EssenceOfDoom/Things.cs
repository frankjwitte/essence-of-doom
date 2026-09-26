namespace EssenceOfDoom;

public enum MonsterShape { Circle, Square, Diamond, Pentagon, BigSquare }

/// <summary>Per-monster numbers, taken from DOOM's info tables (radius, health, speed, pain chance)
/// with state timings collapsed into a few durations measured in tics (1/35 s).</summary>
public sealed class MonsterInfo
{
    public string Name = "";
    public double Radius = 20, Height = 56, Speed = 8;
    public int Health, PainChance;
    public int ChaseTics = 4;       // how often a chase decision is made (length of a run frame)
    public int PainTics = 6;
    public int AttackWindup = 10;   // tics from starting an attack to the shot/blow
    public int AttackTotal = 26;    // total tics the attack animation locks the monster
    public int HitscanPellets;      // 0 = no hitscan attack
    public int MeleeDie, MeleeMult; // melee damage = (1..MeleeDie) * MeleeMult; 0 = no melee
    public double MissileSpeed;     // 0 = no missile attack
    public int MissileMult;         // missile damage = (1..8) * MissileMult
    public int DropType;            // doomednum dropped on death, 0 = nothing
    public MonsterShape Shape;

    public bool HasMelee => MeleeDie > 0;
    public bool HasMissile => MissileSpeed > 0 || HitscanPellets > 0;
}

public enum PickupKind { Health, Armor, Ammo, Weapon, Key, Bonus }

public sealed class PickupInfo
{
    public PickupKind Kind;
    public string Message = "";
    public bool CountsAsItem;
}

public static class ThingTypes
{
    public const int PlayerStart = 1;
    public const int Barrel = 2035;

    public static readonly Dictionary<int, MonsterInfo> Monsters = new()
    {
        [3004] = new() { Name = "Zombieman", Health = 20, PainChance = 200, ChaseTics = 4, PainTics = 6,
            AttackWindup = 10, AttackTotal = 26, HitscanPellets = 1, DropType = 2007, Shape = MonsterShape.Circle },
        [9] = new() { Name = "Shotgun guy", Health = 30, PainChance = 170, ChaseTics = 3, PainTics = 6,
            AttackWindup = 10, AttackTotal = 30, HitscanPellets = 3, DropType = 2001, Shape = MonsterShape.Square },
        [3001] = new() { Name = "Imp", Health = 60, PainChance = 200, ChaseTics = 3, PainTics = 4,
            AttackWindup = 16, AttackTotal = 22, MeleeDie = 8, MeleeMult = 3, MissileSpeed = 10, MissileMult = 3,
            Shape = MonsterShape.Diamond },
        [3002] = new() { Name = "Demon", Radius = 30, Speed = 10, Health = 150, PainChance = 180, ChaseTics = 2,
            PainTics = 4, AttackWindup = 16, AttackTotal = 24, MeleeDie = 10, MeleeMult = 4, Shape = MonsterShape.Pentagon },
        [58] = new() { Name = "Spectre", Radius = 30, Speed = 10, Health = 150, PainChance = 180, ChaseTics = 2,
            PainTics = 4, AttackWindup = 16, AttackTotal = 24, MeleeDie = 10, MeleeMult = 4, Shape = MonsterShape.Pentagon },
        [3003] = new() { Name = "Baron", Radius = 24, Height = 64, Health = 1000, PainChance = 50, ChaseTics = 3,
            PainTics = 4, AttackWindup = 16, AttackTotal = 24, MeleeDie = 8, MeleeMult = 10, MissileSpeed = 15,
            MissileMult = 8, Shape = MonsterShape.BigSquare },
    };

    public static readonly Dictionary<int, PickupInfo> Pickups = new()
    {
        [2011] = new() { Kind = PickupKind.Health, Message = "Picked up a stimpack." },
        [2012] = new() { Kind = PickupKind.Health, Message = "Picked up a medikit." },
        [2014] = new() { Kind = PickupKind.Bonus, Message = "Picked up a health bonus.", CountsAsItem = true },
        [2015] = new() { Kind = PickupKind.Bonus, Message = "Picked up an armor bonus.", CountsAsItem = true },
        [2013] = new() { Kind = PickupKind.Bonus, Message = "Supercharge!", CountsAsItem = true },
        [2018] = new() { Kind = PickupKind.Armor, Message = "Picked up the armor." },
        [2019] = new() { Kind = PickupKind.Armor, Message = "Picked up the MegaArmor!" },
        [2007] = new() { Kind = PickupKind.Ammo, Message = "Picked up a clip." },
        [2048] = new() { Kind = PickupKind.Ammo, Message = "Picked up a box of bullets." },
        [2008] = new() { Kind = PickupKind.Ammo, Message = "Picked up 4 shotgun shells." },
        [2049] = new() { Kind = PickupKind.Ammo, Message = "Picked up a box of shotgun shells." },
        [2001] = new() { Kind = PickupKind.Weapon, Message = "You got the shotgun!" },
        [2002] = new() { Kind = PickupKind.Weapon, Message = "You got the chaingun!" },
        [5] = new() { Kind = PickupKind.Key, Message = "Picked up a blue keycard." },
        [6] = new() { Kind = PickupKind.Key, Message = "Picked up a yellow keycard." },
        [13] = new() { Kind = PickupKind.Key, Message = "Picked up a red keycard." },
        [40] = new() { Kind = PickupKind.Key, Message = "Picked up a blue skull key." },
        [39] = new() { Kind = PickupKind.Key, Message = "Picked up a yellow skull key." },
        [38] = new() { Kind = PickupKind.Key, Message = "Picked up a red skull key." },
    };

    /// <summary>Solid decorations (pillars, lamps, trees) that block movement. Radius per DOOM's info table.</summary>
    public static readonly Dictionary<int, double> Obstacles = new()
    {
        [35] = 16, [48] = 16, [2028] = 16, [30] = 16, [31] = 16, [32] = 16, [33] = 16, [36] = 16, [37] = 16,
        [41] = 16, [42] = 16, [43] = 16, [44] = 16, [45] = 16, [46] = 16, [47] = 16, [54] = 32, [55] = 16,
        [56] = 16, [57] = 16, [70] = 16, [85] = 16, [86] = 16,
    };

    public enum KeyColor { None, Blue, Yellow, Red }

    public static KeyColor KeyColorOf(int type) => type switch
    {
        5 or 40 => KeyColor.Blue,
        6 or 39 => KeyColor.Yellow,
        13 or 38 => KeyColor.Red,
        _ => KeyColor.None,
    };
}
