namespace EssenceOfDoom;

public enum GameState { Playing, Dead, Exited }

/// <summary>One tic of player intent. Move is a world-space direction (length ≤ 1); Aim is a world angle in radians.</summary>
public record struct PlayerInput(Vec2 Move, double Aim, bool Fire, bool Use, bool Walk, int SelectWeapon);

/// <summary>The whole simulation. Runs at DOOM's 35 tics per second; knows nothing about rendering.</summary>
public sealed partial class Game
{
    public const int TicRate = 35;

    public readonly Level Level;
    public readonly int Skill;
    public readonly Random Rng;
    public readonly Player Player = new();
    public readonly List<Mobj> Mobjs = new();
    public readonly List<Projectile> Projectiles = new();
    public readonly List<Effect> Effects = new();
    public int Tick;
    public GameState State = GameState.Playing;
    public string Message = "";
    public int MessageTick = -1000;
    public int TotalKills, TotalItems, TotalSecrets;
    public int[] SectorNoiseTick;

    public Game(Level level, int skill = 4, int seed = 1)
    {
        Level = level;
        Skill = skill;
        Rng = new Random(seed);
        SectorNoiseTick = Enumerable.Repeat(-1000, level.Sectors.Count).ToArray();
        TotalSecrets = level.Sectors.Count(s => s.Special == 9);
        SpawnThings();
    }

    /// <summary>DOOM's P_Random returns 0..255; most formulas are written in terms of it.</summary>
    public int Random255() => Rng.Next(256);

    private void SpawnThings()
    {
        int skillBit = Skill <= 2 ? 1 : Skill == 3 ? 2 : 4;
        foreach (var t in Level.Things)
        {
            if (t.Type == ThingTypes.PlayerStart)
            {
                Player.Mo = new Mobj
                {
                    Kind = MobjKind.Player, Type = 1, Pos = new Vec2(t.X, t.Y), PrevPos = new Vec2(t.X, t.Y), Radius = 16, Height = 56,
                    Angle = t.AngleDeg * Math.PI / 180, Health = 100, Solid = true, Shootable = true,
                };
                Player.Mo.Z = Level.SectorAt(Player.Mo.Pos).FloorHeight;
                Mobjs.Add(Player.Mo);
                continue;
            }
            if ((t.Flags & 16) != 0 || (t.Flags & skillBit) == 0) continue; // multiplayer-only or other skill

            var pos = new Vec2(t.X, t.Y);
            var mo = SpawnMobj(t.Type, pos);
            if (mo == null) continue;
            mo.Angle = t.AngleDeg * Math.PI / 180;
            mo.Ambush = (t.Flags & 8) != 0;
            if (mo.IsMonster) TotalKills++;
            if (mo.Pickup?.CountsAsItem == true) TotalItems++;
        }
        if (Player.Mo == null) throw new InvalidDataException("Map has no player 1 start");
    }

    public Mobj? SpawnMobj(int type, Vec2 pos)
    {
        Mobj? mo = null;
        if (ThingTypes.Monsters.TryGetValue(type, out var mi))
        {
            mo = new Mobj
            {
                Kind = MobjKind.Monster, Info = mi, Radius = mi.Radius, Height = mi.Height, Health = mi.Health,
                Solid = true, Shootable = true, LookTimer = Rng.Next(1, 11), ReactionTime = Skill >= 5 ? 0 : 8,
            };
        }
        else if (ThingTypes.Pickups.TryGetValue(type, out var pi))
            mo = new Mobj { Kind = MobjKind.Pickup, Pickup = pi, Radius = 20, Height = 16 };
        else if (ThingTypes.Obstacles.TryGetValue(type, out var r))
            mo = new Mobj { Kind = MobjKind.Obstacle, Radius = r, Height = 16, Solid = true };
        else if (type == ThingTypes.Barrel)
            mo = new Mobj { Kind = MobjKind.Barrel, Radius = 10, Height = 42, Health = 20, Solid = true, Shootable = true };
        if (mo == null) return null; // decorations and gore: pure presentation, left out on purpose

        mo.Type = type;
        mo.Pos = mo.PrevPos = pos;
        mo.Z = Level.SectorAt(pos).FloorHeight;
        Mobjs.Add(mo);
        return mo;
    }

    public void ShowMessage(string text)
    {
        Message = text;
        MessageTick = Tick;
    }

    public void Update(PlayerInput input)
    {
        if (State == GameState.Exited) return;
        Tick++;
        foreach (var mo in Mobjs) mo.PrevPos = mo.Pos;
        foreach (var p in Projectiles) p.PrevPos = p.Pos;

        UpdatePlayer(input);
        foreach (var mo in Mobjs.ToArray())
        {
            if (mo.IsMonster) MonsterThink(mo);
            else if (mo.Kind == MobjKind.Barrel && mo.IsDead && !mo.Removed && Tick - mo.DeathTick >= 15)
                ExplodeBarrel(mo);
        }
        UpdateProjectiles();
        if (Movers.Count > 0)
        {
            UpdateMovers();
            // Things standing on a moving floor ride along with it.
            foreach (var mo in Mobjs)
                if (!mo.Removed) mo.Z = FloorAt(mo);
        }
        Effects.RemoveAll(e => Tick - e.StartTick > e.Duration);
        if (Player.DamageFlash > 0) Player.DamageFlash--;
        if (Player.PickupFlash > 0) Player.PickupFlash--;
    }

    // ---------------------------------------------------------------- player

    private static readonly (int delay, int period)[] WeaponTiming =
    {
        (0, 0), (4, 17), (4, 14), (3, 37), (0, 4), // index by (int)Weapon: -, fist, pistol, shotgun, chaingun
    };

    private bool _attackHeld;

    private void UpdatePlayer(PlayerInput input)
    {
        var p = Player;
        var mo = p.Mo;

        if (State == GameState.Playing)
        {
            mo.Angle = input.Aim;
            double len = input.Move.Length;
            if (len > 0.001)
            {
                // DOOM's forwardmove * 2048 per tic: 25 (walk) or 50 (run) → ~0.78 / ~1.56 units/tic².
                double thrust = input.Walk ? 0.78 : 1.5625;
                p.Velocity += input.Move * (thrust / Math.Max(1, len));
            }
            if (input.SelectWeapon > 0) SelectWeapon((Weapon)input.SelectWeapon);
            if (input.Use) UseLines(mo);
            UpdateWeapon(input.Fire);
        }

        if (p.Velocity.Length > 0.01)
        {
            var before = mo.Pos;
            SlideMove(mo, p.Velocity);
            p.Velocity = mo.Pos - before; // whatever was blocked by walls is lost, sliding keeps the rest
        }
        p.Velocity *= 0.90625; // DOOM's ground friction
        if (p.Velocity.Length < 0.05) p.Velocity = default;

        mo.Z = FloorAt(mo);
        TouchPickups();
        PlayerSectorEffects();
    }

    private void SelectWeapon(Weapon w)
    {
        if (!Player.Weapons.Contains(w) || Player.Current == w) return;
        Player.Current = w;
        Player.PendingShotTics = -1;
    }

    private bool HasAmmo(Weapon w) => w switch
    {
        Weapon.Pistol or Weapon.Chaingun => Player.Bullets > 0,
        Weapon.Shotgun => Player.Shells > 0,
        _ => true,
    };

    private void UpdateWeapon(bool fire)
    {
        var p = Player;
        if (p.PendingShotTics > 0 && --p.PendingShotTics == 0) FireCurrentWeapon();
        if (p.FireCooldown > 0) { p.FireCooldown--; return; }

        if (!HasAmmo(p.Current))
        {
            // Out of ammo: switch to the best weapon that still has some, like DOOM does.
            p.Current = new[] { Weapon.Chaingun, Weapon.Shotgun, Weapon.Pistol, Weapon.Fist }
                .First(w => p.Weapons.Contains(w) && HasAmmo(w));
        }

        if (fire)
        {
            p.Refire = _attackHeld ? p.Refire + 1 : 0;
            _attackHeld = true;
            var (delay, period) = WeaponTiming[(int)p.Current];
            p.FireCooldown = period;
            p.PendingShotTics = delay;
            NoiseAlert(p.Mo); // every weapon, even the fist, wakes up whatever can hear it
            if (delay == 0) FireCurrentWeapon();
        }
        else
        {
            _attackHeld = false;
            p.Refire = 0;
        }
    }

    private void FireCurrentWeapon()
    {
        var p = Player;
        var mo = p.Mo;
        p.PendingShotTics = -1;
        double spread = 5.6 * Math.PI / 180; // (P_Random()-P_Random())<<18 in DOOM
        switch (p.Current)
        {
            case Weapon.Fist:
            {
                int damage = (Random255() % 10 + 1) * 2;
                double angle = mo.Angle + (Random255() - Random255()) / 255.0 * spread;
                LineAttack(mo, angle, 64, damage);
                break;
            }
            case Weapon.Pistol:
            case Weapon.Chaingun:
            {
                if (p.Bullets <= 0) return;
                p.Bullets--;
                double angle = mo.Angle;
                if (p.Refire > 0) angle += (Random255() - Random255()) / 255.0 * spread;
                LineAttack(mo, angle, 2048, 5 * (Random255() % 3 + 1));
                break;
            }
            case Weapon.Shotgun:
            {
                if (p.Shells <= 0) return;
                p.Shells--;
                for (int i = 0; i < 7; i++)
                    LineAttack(mo, mo.Angle + (Random255() - Random255()) / 255.0 * spread, 2048, 5 * (Random255() % 3 + 1));
                break;
            }
        }
    }

    private void TouchPickups()
    {
        var mo = Player.Mo;
        if (mo.IsDead) return;
        foreach (var it in Mobjs)
        {
            if (it.Kind != MobjKind.Pickup || it.Removed) continue;
            if ((it.Pos - mo.Pos).Length >= it.Radius + mo.Radius) continue;
            double dz = it.Z - mo.Z;
            if (dz > mo.Height || dz < -8) continue;
            if (!GivePickup(it)) continue;
            it.Removed = true;
            if (it.Pickup!.CountsAsItem) Player.Items++;
            Player.PickupFlash = 6;
            ShowMessage(it.Pickup.Message);
        }
        Mobjs.RemoveAll(m => m.Removed && m.Kind == MobjKind.Pickup);
    }

    private bool GivePickup(Mobj it)
    {
        var p = Player;
        var mo = p.Mo;
        bool GiveAmmo(ref int ammo, int max, int amount)
        {
            if (ammo >= max) return false;
            ammo = Math.Min(max, ammo + (it.Dropped ? amount / 2 : amount));
            return true;
        }
        bool GiveWeapon(Weapon w, ref int ammo, int max, int amount)
        {
            bool gotAmmo = GiveAmmo(ref ammo, max, amount);
            if (p.Weapons.Contains(w)) return gotAmmo;
            p.Weapons.Add(w);
            p.Current = w;
            p.PendingShotTics = -1;
            return true;
        }

        switch (it.Type)
        {
            case 2011: if (mo.Health >= 100) return false; mo.Health = Math.Min(100, mo.Health + 10); return true;
            case 2012: if (mo.Health >= 100) return false; mo.Health = Math.Min(100, mo.Health + 25); return true;
            case 2014: mo.Health = Math.Min(200, mo.Health + 1); return true;
            case 2013: mo.Health = Math.Min(200, mo.Health + 100); return true;
            case 2015:
                p.Armor = Math.Min(200, p.Armor + 1);
                if (p.ArmorType == 0) p.ArmorType = 1;
                return true;
            case 2018: if (p.Armor >= 100) return false; p.Armor = 100; p.ArmorType = 1; return true;
            case 2019: if (p.Armor >= 200) return false; p.Armor = 200; p.ArmorType = 2; return true;
            case 2007: return GiveAmmo(ref p.Bullets, Player.MaxBullets, 10);
            case 2048: return GiveAmmo(ref p.Bullets, Player.MaxBullets, 50);
            case 2008: return GiveAmmo(ref p.Shells, Player.MaxShells, 4);
            case 2049: return GiveAmmo(ref p.Shells, Player.MaxShells, 20);
            case 2001: return GiveWeapon(Weapon.Shotgun, ref p.Shells, Player.MaxShells, 8);
            case 2002: return GiveWeapon(Weapon.Chaingun, ref p.Bullets, Player.MaxBullets, 20);
            default:
                var key = ThingTypes.KeyColorOf(it.Type);
                if (key == ThingTypes.KeyColor.None) return false;
                return p.Keys.Add(key);
        }
    }

    private void PlayerSectorEffects()
    {
        var mo = Player.Mo;
        var sec = Level.SectorAt(mo.Pos);
        if (sec.Special == 9)
        {
            Player.Secrets++;
            sec.Special = 0;
            sec.SecretFound = true;
        }
        if (mo.IsDead || mo.Z > sec.FloorHeight || (Tick & 31) != 0) return;
        int damage = sec.Special switch { 7 => 5, 5 => 10, 16 or 4 => 20, _ => 0 };
        if (damage > 0) DamageMobj(mo, null, null, damage);
    }

    // ---------------------------------------------------------------- damage

    public void DamageMobj(Mobj target, Mobj? inflictor, Mobj? source, int damage)
    {
        if (!target.Shootable || target.IsDead) return;

        if (target.Kind == MobjKind.Player)
        {
            var p = Player;
            if (Skill == 1) damage >>= 1;
            if (p.ArmorType > 0)
            {
                int saved = p.ArmorType == 1 ? damage / 3 : damage / 2;
                if (p.Armor <= saved) { saved = p.Armor; p.ArmorType = 0; }
                p.Armor -= saved;
                damage -= saved;
            }
            target.Health -= damage;
            p.DamageFlash = Math.Min(20, p.DamageFlash + damage / 2 + 4);
            if (inflictor != null)
            {
                // Knockback: a little push away from whatever hit us.
                var away = target.Pos - inflictor.Pos;
                if (away.Length > 0.1) p.Velocity += away * (damage * 0.08 / away.Length);
            }
            if (target.Health <= 0)
            {
                target.Health = 0;
                target.DeathTick = Tick;
                State = GameState.Dead;
                ShowMessage("You died.");
            }
            return;
        }

        target.Health -= damage;
        if (target.Health <= 0)
        {
            KillMobj(target, source);
            return;
        }

        if (!target.IsMonster) return;
        if (Random255() < target.Info!.PainChance)
        {
            target.JustHit = true;
            target.State = AiState.Pain;
            target.StateTics = target.Info.PainTics;
            target.PainTick = Tick;
        }
        target.ReactionTime = 0; // we're awake now
        if (target.Threshold == 0 && source != null && source != target)
        {
            target.Target = source; // this is where infighting comes from
            target.Threshold = 100;
            if (target.State == AiState.Dormant) WakeUp(target);
        }
    }

    private void KillMobj(Mobj target, Mobj? source)
    {
        target.Health = 0;
        target.DeathTick = Tick;
        target.Solid = false;
        target.Shootable = false;
        if (target.Kind == MobjKind.Barrel)
        {
            target.Target = source; // explosion is credited to whoever popped the barrel
            return;
        }
        target.State = AiState.Dead;
        target.MoveDir = Dir.None;
        Player.Kills++; // single player: every monster death counts, like DOOM
        if (target.Info!.DropType != 0)
        {
            var drop = SpawnMobj(target.Info.DropType, target.Pos);
            if (drop != null) drop.Dropped = true;
        }
    }

    private void ExplodeBarrel(Mobj barrel)
    {
        barrel.Removed = true;
        barrel.Shootable = false;
        Effects.Add(new Effect { Kind = EffectKind.Explosion, A = barrel.Pos, StartTick = Tick, Duration = 18 });
        RadiusAttack(barrel, barrel.Target, 128);
        Mobjs.Remove(barrel);
    }
}
