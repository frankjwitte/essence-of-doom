using EssenceOfDoom;

namespace EssenceOfDoom.Tests;

/// <summary>Plays the real E1M1 headlessly. Requires DOOM1.WAD somewhere Wad.Locate can find it.</summary>
public class E1M1Tests
{
    static readonly Wad TheWad = new(Wad.Locate(null) ?? throw new FileNotFoundException("DOOM1.WAD not found"));

    static Game NewGame(int skill = 4) => new(Level.Load(TheWad, "E1M1"), skill, seed: 42);

    static PlayerInput Idle(Game g) => new(default, g.Player.Mo.Angle, false, false, false, 0);

    static PlayerInput Walk(Vec2 dir) => new(dir, Math.Atan2(dir.Y, dir.X), false, false, false, 0);

    static void Run(Game g, int tics, Func<Game, PlayerInput>? input = null)
    {
        for (int i = 0; i < tics; i++) g.Update((input ?? Idle)(g));
    }

    static void Place(Game g, Vec2 pos, double angle)
    {
        var mo = g.Player.Mo;
        mo.Pos = mo.PrevPos = pos;
        mo.Angle = angle;
        mo.Z = g.FloorAt(mo);
    }

    static void RemoveMonsters(Game g) => g.Mobjs.RemoveAll(m => m.IsMonster);

    [Fact]
    public void LoadsE1M1Geometry()
    {
        var lv = Level.Load(TheWad, "E1M1");
        Assert.Equal(467, lv.Vertices.Count);
        Assert.Equal(475, lv.Lines.Count);
        Assert.Equal(85, lv.Sectors.Count);
        Assert.Equal(138, lv.Things.Count);
    }

    [Fact]
    public void SpawnsPlayerAndSkillFilteredMonsters()
    {
        var uv = NewGame(4);
        var hmp = NewGame(3);
        Assert.NotNull(uv.Level.SectorAtOrNull(uv.Player.Mo.Pos));
        Assert.Equal(29, uv.TotalKills);
        Assert.Equal(6, hmp.TotalKills);
        Assert.All(uv.Mobjs.Where(m => m.IsMonster), m => Assert.Equal(AiState.Dormant, m.State));
    }

    [Fact]
    public void PlayerNeverLeavesTheMapOrEntersWalls()
    {
        var g = NewGame();
        RemoveMonsters(g);
        var rng = new Random(7);
        var dir = new Vec2(0, 1);
        for (int t = 0; t < 35 * 60; t++)
        {
            if (t % 50 == 0) dir = Vec2.FromAngle(rng.NextDouble() * Math.PI * 2);
            g.Update(Walk(dir));
            var p = g.Player.Mo.Pos;
            Assert.NotNull(g.Level.SectorAtOrNull(p));
            foreach (var l in g.Level.Lines.Where(l => !l.TwoSided))
                Assert.True(Game.DistanceToSegment(p, l.V1, l.V2) > g.Player.Mo.Radius - 0.5,
                    $"tic {t}: player at {p} overlaps wall {l.Index}");
        }
    }

    [Fact]
    public void WalkingIntoAWallStopsAtTheWall()
    {
        var g = NewGame();
        RemoveMonsters(g);
        var start = g.Player.Mo.Pos;
        Run(g, 35 * 3, _ => Walk(new Vec2(0, -1)));
        Assert.True(g.Player.Mo.Pos.Y < start.Y);
        Assert.NotNull(g.Level.SectorAtOrNull(g.Player.Mo.Pos));
    }

    [Fact]
    public void UsingADoorOpensItAndItClosesAgain()
    {
        var g = NewGame();
        RemoveMonsters(g);
        var line = g.Level.Lines[151]; // DR door, vertical line at x=1536, front side to the west
        var door = line.BackSector!;
        Assert.Equal(door.FloorHeight, door.CeilingHeight);

        Place(g, new Vec2(1536 - 40, -2496), 0);
        g.Update(Idle(g) with { Use = true });
        Run(g, 40);
        Assert.True(door.CeilingHeight - door.FloorHeight >= 56, "door should be open enough to walk through");

        Run(g, 35 * 2, _ => Walk(new Vec2(1, 0)));
        Assert.True(g.Player.Mo.Pos.X > 1552 + 16, $"player should be past the door, at {g.Player.Mo.Pos}");

        Run(g, 35 * 10);
        Assert.Equal(door.FloorHeight, door.CeilingHeight);
    }

    [Fact]
    public void ClosedDoorBlocksSightAndMovement()
    {
        var g = NewGame();
        RemoveMonsters(g);
        var west = new Vec2(1536 - 40, -2496);
        var east = new Vec2(1552 + 60, -2496);
        Assert.False(g.CheckSightLine(west, 41, east, 0, 56));
        Place(g, west, 0);
        Run(g, 35 * 2, _ => Walk(new Vec2(1, 0)));
        Assert.True(g.Player.Mo.Pos.X < 1536);
    }

    [Fact]
    public void ShootingAZombieKillsItAndDropsAClip()
    {
        var g = NewGame();
        var zombie = g.Mobjs.First(m => m.Type == 3004);
        foreach (var other in g.Mobjs.Where(m => m.IsMonster && m != zombie).ToList()) g.Mobjs.Remove(other);
        var spot = FindVantagePoint(g, zombie, 140);
        Place(g, spot, Game.AngleTo(spot, zombie.Pos));

        for (int i = 0; i < 35 * 10 && !zombie.IsDead; i++)
            g.Update(new PlayerInput(default, Game.AngleTo(g.Player.Mo.Pos, zombie.Pos), true, false, false, 0));
        Assert.True(zombie.IsDead);
        Assert.Equal(1, g.Player.Kills);
        Assert.Contains(g.Mobjs, m => m.Type == 2007 && m.Dropped);
    }

    [Fact]
    public void GunfireWakesMonstersThatCanHearIt()
    {
        var g = NewGame();
        Run(g, 35);
        Assert.All(g.Mobjs.Where(m => m.IsMonster), m => Assert.Equal(AiState.Dormant, m.State));

        g.NoiseAlert(g.Player.Mo);
        Run(g, 35);
        int awake = g.Mobjs.Count(m => m.IsMonster && m.State != AiState.Dormant);
        Assert.True(awake > 0, "someone should have heard the shot");
        Assert.True(awake < g.TotalKills, "monsters behind closed doors should still be asleep");
    }

    [Fact]
    public void AwakeMonstersHuntAndHurtThePlayerWithoutLeavingTheMap()
    {
        var g = NewGame();
        var zombie = g.Mobjs.First(m => m.Type == 3004);
        Place(g, FindVantagePoint(g, zombie, 300), 0);
        g.NoiseAlert(g.Player.Mo);
        Run(g, 35 * 30);
        Assert.NotEqual(AiState.Dormant, zombie.State);
        Assert.True(g.Player.Mo.Health < 100, "the zombie should have shot the idle player");
        foreach (var m in g.Mobjs.Where(m => m.IsMonster))
            Assert.NotNull(g.Level.SectorAtOrNull(m.Pos));
    }

    [Fact]
    public void PlayerCanDie()
    {
        var g = NewGame();
        var zombie = g.Mobjs.First(m => m.Type == 3004);
        Place(g, FindVantagePoint(g, zombie, 200), 0);
        g.Player.Mo.Health = 15;
        g.NoiseAlert(g.Player.Mo);
        Run(g, 35 * 120);
        Assert.Equal(GameState.Dead, g.State);
        Assert.Equal(0, g.Player.Mo.Health);
    }

    [Fact]
    public void ExitSwitchCompletesTheLevel()
    {
        var g = NewGame();
        RemoveMonsters(g);
        var exit = g.Level.Lines[330]; // switch on a wall running north at x=2912, front side east
        Assert.Equal(11, exit.Special);
        Place(g, new Vec2(2912 + 30, -4768), Math.PI);
        g.Update(Idle(g) with { Use = true });
        Assert.Equal(GameState.Exited, g.State);
    }

    [Fact]
    public void NukageHurts()
    {
        var g = NewGame();
        RemoveMonsters(g);
        var nukage = g.Level.Sectors[13];
        Assert.Equal(7, nukage.Special);
        Place(g, FindPointInSector(g, nukage), 0);
        Run(g, 35 * 3);
        Assert.True(g.Player.Mo.Health < 100);
    }

    [Fact]
    public void WalkingOverAClipPicksItUp()
    {
        var g = NewGame();
        RemoveMonsters(g);
        var clip = g.Mobjs.First(m => m.Type == 2007);
        Place(g, clip.Pos + new Vec2(0.5, 0), 0);
        g.Update(Idle(g));
        Assert.Equal(60, g.Player.Bullets);
        Assert.DoesNotContain(clip, g.Mobjs);
    }

    [Fact]
    public void PickingUpTheShotgunGivesAndSelectsIt()
    {
        var g = NewGame();
        RemoveMonsters(g);
        var gun = g.Mobjs.First(m => m.Type == 2001);
        Place(g, gun.Pos, 0);
        g.Update(Idle(g));
        Assert.Equal(Weapon.Shotgun, g.Player.Current);
        Assert.Equal(8, g.Player.Shells);
    }

    [Fact]
    public void CrossingTheLiftLineLowersTheLift()
    {
        var g = NewGame();
        RemoveMonsters(g);
        var line = g.Level.Lines[195];
        var lift = g.Level.Sectors[70];
        double top = lift.FloorHeight;
        var mid = (line.V1 + line.V2) * 0.5;
        var n = new Vec2(-line.Delta.Y, line.Delta.X) * (1 / line.Delta.Length);
        var onLift = g.Level.SectorAt(mid + n * 20) == lift ? mid + n * 20 : mid - n * 20;
        var off = mid + (mid - onLift);
        Place(g, onLift, 0);
        var dir = off - onLift;
        Run(g, 20, _ => Walk(dir * (1 / dir.Length)));
        Run(g, 30);
        Assert.True(lift.FloorHeight < top, "lift should be going down");
    }

    [Fact]
    public void ImpsThrowFireballsThatHurt()
    {
        var g = NewGame();
        var imp = g.Mobjs.First(m => m.Type == 3001);
        g.Mobjs.RemoveAll(m => m.IsMonster && m != imp);
        Place(g, FindVantagePoint(g, imp, 400), 0);
        g.NoiseAlert(g.Player.Mo);
        bool sawFireball = false;
        for (int i = 0; i < 35 * 30 && g.Player.Mo.Health == 100; i++)
        {
            g.Update(Idle(g));
            sawFireball |= g.Projectiles.Count > 0;
        }
        Assert.True(sawFireball);
        Assert.True(g.Player.Mo.Health < 100);
    }

    [Fact]
    public void ShootingABarrelExplodesAndHurtsNearbyThings()
    {
        var g = NewGame();
        RemoveMonsters(g);
        var barrel = g.Mobjs.First(m => m.Kind == MobjKind.Barrel);
        var spot = FindVantagePoint(g, barrel, 90);
        Place(g, spot, Game.AngleTo(spot, barrel.Pos));
        for (int i = 0; i < 35 * 5 && g.Mobjs.Contains(barrel); i++)
            g.Update(new PlayerInput(default, Game.AngleTo(g.Player.Mo.Pos, barrel.Pos), true, false, false, 0));
        Assert.DoesNotContain(barrel, g.Mobjs);
        Assert.True(g.Player.Mo.Health < 100, "standing 90 units away should hurt");
    }

    [Fact]
    public void SimulationIsCheapWithEverythingAwake()
    {
        var g = NewGame();
        foreach (var m in g.Mobjs.Where(m => m.IsMonster)) { m.Target = g.Player.Mo; m.State = AiState.Chase; }
        g.Player.Mo.Health = 100000;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Run(g, 35 * 60);
        Assert.True(sw.Elapsed.TotalSeconds < 6, $"one minute of play took {sw.Elapsed.TotalSeconds:F2}s to simulate");
    }

    [Fact]
    public void ExitIsReachableFromTheStartWithDoorsOpen()
    {
        var g = NewGame();
        g.Mobjs.RemoveAll(m => m.Kind != MobjKind.Obstacle && m.Kind != MobjKind.Barrel);
        foreach (var l in g.Level.Lines.Where(l => Game.IsDoorSpecial(l.Special) && l.BackSector != null))
        {
            var door = l.BackSector!;
            door.CeilingHeight = g.Level.Neighbours(door).Min(n => n.CeilingHeight) - 4;
        }

        var goal = new Vec2(2912 + 30, -4768);
        var probe = new Mobj { Kind = MobjKind.Player, Radius = 16, Height = 56 };
        var start = g.Player.Mo.Pos;
        var seen = new HashSet<(int, int)> { (0, 0) };
        var queue = new Queue<(int x, int y, double z)>();
        queue.Enqueue((0, 0, g.Player.Mo.Z));
        const double step = 12;
        bool reached = false;
        while (queue.Count > 0 && !reached)
        {
            var (x, y, z) = queue.Dequeue();
            var from = start + new Vec2(x * step, y * step);
            for (int d = 0; d < 8; d++)
            {
                int nx = x + Math.Sign(Dir.X[d]), ny = y + Math.Sign(Dir.Y[d]);
                if (seen.Contains((nx, ny))) continue;
                probe.Pos = from;
                probe.Z = z;
                var to = start + new Vec2(nx * step, ny * step);
                if (!g.TryMove(probe, to)) continue;
                seen.Add((nx, ny));
                queue.Enqueue((nx, ny, probe.Z));
                if ((to - goal).Length < 24) reached = true;
            }
        }
        Assert.True(reached, $"exit not reachable; explored {seen.Count} cells");
    }

    static Vec2 FindVantagePoint(Game g, Mobj target, double dist)
    {
        for (int a = 0; a < 72; a++)
        {
            var p = target.Pos + Vec2.FromAngle(a * Math.PI / 36) * dist;
            if (g.Level.SectorAtOrNull(p) == null) continue;
            var probe = new Mobj { Kind = MobjKind.Player, Pos = p, Radius = 16, Height = 56 };
            probe.Z = g.FloorAt(probe);
            if (!g.TryMove(probe, p, commit: false)) continue;
            if (g.CheckSight(probe, target)) return p;
        }
        throw new InvalidOperationException("no vantage point");
    }

    static Vec2 FindPointInSector(Game g, Sector s)
    {
        for (double x = g.Level.MinX; x < g.Level.MaxX; x += 8)
        for (double y = g.Level.MinY; y < g.Level.MaxY; y += 8)
        {
            var p = new Vec2(x, y);
            if (g.Level.SectorAtOrNull(p) != s) continue;
            if (s.Lines.All(l => Game.DistanceToSegment(p, l.V1, l.V2) > 20)) return p;
        }
        throw new InvalidOperationException("sector too small");
    }
}
