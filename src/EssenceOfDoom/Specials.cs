namespace EssenceOfDoom;

/// <summary>A sector whose floor or ceiling is moving: doors, lifts, lowering floors.</summary>
public abstract class Mover
{
    public Sector Sector = null!;
    /// <summary>Advance one tic. Returns false when finished.</summary>
    public abstract bool Tick(Game g);
}

public sealed class Door : Mover
{
    public double Top;
    public int Direction = 1;        // 1 opening, 0 waiting open, -1 closing
    public int WaitTics;
    public bool StayOpen;
    public const double Speed = 2;   // VDOORSPEED
    public const int Wait = 150;     // VDOORWAIT: ~4 seconds

    public override bool Tick(Game g)
    {
        var s = Sector;
        switch (Direction)
        {
            case 1:
                s.CeilingHeight = Math.Min(Top, s.CeilingHeight + Speed);
                if (s.CeilingHeight >= Top)
                {
                    if (StayOpen) return false;
                    Direction = 0;
                    WaitTics = Wait;
                }
                return true;
            case 0:
                if (--WaitTics <= 0) Direction = -1;
                return true;
            default:
                double next = s.CeilingHeight - Speed;
                if (next <= s.FloorHeight)
                {
                    s.CeilingHeight = s.FloorHeight;
                    return false;
                }
                if (g.SomethingWouldBeCrushed(s, s.FloorHeight, next))
                {
                    Direction = 1; // doors bounce back up off anything standing in them
                    return true;
                }
                s.CeilingHeight = next;
                return true;
        }
    }
}

public sealed class Lift : Mover
{
    public double Low, High;
    public int Direction = -1;       // -1 down, 0 waiting, 1 up
    public int WaitTics;
    public const double Speed = 4;   // PLATSPEED * 4
    public const int Wait = 105;     // 3 seconds at the bottom

    public override bool Tick(Game g)
    {
        var s = Sector;
        switch (Direction)
        {
            case -1:
                s.FloorHeight = Math.Max(Low, s.FloorHeight - Speed);
                if (s.FloorHeight <= Low) { Direction = 0; WaitTics = Wait; }
                return true;
            case 0:
                if (--WaitTics <= 0) Direction = 1;
                return true;
            default:
                double next = Math.Min(High, s.FloorHeight + Speed);
                if (g.SomethingWouldBeCrushed(s, next, s.CeilingHeight))
                {
                    Direction = -1;
                    return true;
                }
                s.FloorHeight = next;
                return s.FloorHeight < High;
        }
    }
}

public sealed class FloorMover : Mover
{
    public double Destination;
    public double Speed = 4;

    public override bool Tick(Game g)
    {
        var s = Sector;
        if (s.FloorHeight > Destination) s.FloorHeight = Math.Max(Destination, s.FloorHeight - Speed);
        else s.FloorHeight = Math.Min(Destination, s.FloorHeight + Speed);
        return s.FloorHeight != Destination;
    }
}

public sealed partial class Game
{
    public readonly List<Mover> Movers = new();
    private int _validCount;
    private readonly HashSet<int> _reportedSpecials = new();

    private void UpdateMovers()
    {
        foreach (var m in Movers.ToArray())
        {
            if (m.Tick(this)) continue;
            m.Sector.ActiveMover = null;
            Movers.Remove(m);
        }
    }

    private void StartMover(Mover m)
    {
        m.Sector.ActiveMover = m;
        Movers.Add(m);
    }

    /// <summary>Would a living thing touching this sector stop fitting if its floor/ceiling became these heights?</summary>
    public bool SomethingWouldBeCrushed(Sector s, double floor, double ceiling)
    {
        foreach (var mo in Mobjs)
        {
            if (!mo.Solid || mo.Removed || mo.Kind == MobjKind.Obstacle) continue;
            if (!Touches(mo, s)) continue;
            if (ceiling - Math.Max(floor, mo.Z) < mo.Height) return true;
        }
        return false;
    }

    private bool Touches(Mobj mo, Sector s)
    {
        if (Level.SectorAt(mo.Pos) == s) return true;
        foreach (var l in s.Lines)
            if (DistanceToSegment(mo.Pos, l.V1, l.V2) < mo.Radius) return true;
        return false;
    }

    private double LowestNeighbourCeiling(Sector s)
    {
        double h = double.MaxValue;
        foreach (var n in Level.Neighbours(s)) h = Math.Min(h, n.CeilingHeight);
        return h == double.MaxValue ? s.CeilingHeight : h;
    }

    private double HighestNeighbourFloor(Sector s)
    {
        double h = -500;
        foreach (var n in Level.Neighbours(s)) h = Math.Max(h, n.FloorHeight);
        return h;
    }

    private double LowestNeighbourFloor(Sector s)
    {
        double h = s.FloorHeight;
        foreach (var n in Level.Neighbours(s)) h = Math.Min(h, n.FloorHeight);
        return h;
    }

    public static ThingTypes.KeyColor DoorKey(int special) => special switch
    {
        26 or 32 => ThingTypes.KeyColor.Blue,
        27 or 34 => ThingTypes.KeyColor.Yellow,
        28 or 33 => ThingTypes.KeyColor.Red,
        _ => ThingTypes.KeyColor.None,
    };

    public static bool IsDoorSpecial(int special) => special is 1 or 26 or 27 or 28 or 31 or 32 or 33 or 34;
    public static bool IsExitSpecial(int special) => special is 11 or 51 or 52;

    // ---------------------------------------------------------------- activation

    /// <summary>P_UseLines: poke 64 units straight ahead; the first special line hit gets activated.</summary>
    public void UseLines(Mobj user)
    {
        var start = user.Pos;
        var end = start + Vec2.FromAngle(user.Angle) * 64;
        var hits = new List<(double t, Line l)>();
        foreach (var l in Level.LinesAlong(start, end))
        {
            double t = SegmentIntersect(start, end, l.V1, l.V2);
            if (t >= 0) hits.Add((t, l));
        }
        foreach (var (_, l) in hits.OrderBy(h => h.t))
        {
            if (l.Special != 0)
            {
                if (l.PointSide(start) == 0) UseSpecialLine(user, l);
                return;
            }
            if (!l.TwoSided || LineOpening(l).Range <= 0) return; // wall in the way
        }
    }

    /// <summary>Returns true if something was activated.</summary>
    public bool UseSpecialLine(Mobj user, Line line)
    {
        bool isPlayer = user.Kind == MobjKind.Player;
        if (!isPlayer)
        {
            if ((line.Flags & LineFlags.Secret) != 0) return false;
            if (line.Special is not (1 or 32 or 33 or 34)) return false; // monsters only open plain doors
        }

        if (IsDoorSpecial(line.Special)) return VerticalDoor(user, line);

        if (line.Special is 11 or 51 && isPlayer)
        {
            line.SwitchOn = true;
            ExitLevel();
            return true;
        }

        if (_reportedSpecials.Add(line.Special))
            Console.WriteLine($"Unsupported use special {line.Special} on line {line.Index}");
        return false;
    }

    private bool VerticalDoor(Mobj user, Line line)
    {
        var key = DoorKey(line.Special);
        if (key != ThingTypes.KeyColor.None && user.Kind == MobjKind.Player && !Player.Keys.Contains(key))
        {
            ShowMessage($"You need a {key.ToString().ToLower()} key to open this door.");
            return false;
        }

        var sec = line.BackSector;
        if (sec == null) return false;
        bool repeatable = line.Special is 1 or 26 or 27 or 28;

        if (sec.ActiveMover is Door existing)
        {
            if (!repeatable) return false;
            if (existing.Direction == -1) existing.Direction = 1;                 // reopen while closing
            else if (user.Kind == MobjKind.Player) existing.Direction = -1;       // player can slam it shut
            return true;                                                          // (bad guys never close doors)
        }
        if (sec.ActiveMover != null) return false;

        double top = LowestNeighbourCeiling(sec) - 4;
        if (top <= sec.CeilingHeight) return false;
        StartMover(new Door { Sector = sec, Top = top, StayOpen = !repeatable });
        return true;
    }

    private void CheckCrossedLines(Mobj mo, Vec2 from, Vec2 to)
    {
        if (mo.Kind != MobjKind.Player && !mo.IsMonster) return;
        foreach (var l in Level.LinesAlong(from, to))
        {
            if (l.Special == 0 || l.SpecialUsedUp) continue;
            if (SegmentIntersect(from, to, l.V1, l.V2) < 0) continue;
            if (l.PointSide(from) == l.PointSide(to)) continue;
            CrossSpecialLine(l, mo);
        }
    }

    private void CrossSpecialLine(Line line, Mobj mo)
    {
        bool isPlayer = mo.Kind == MobjKind.Player;
        if (!isPlayer && line.Special is not (10 or 88)) return;
        switch (line.Special)
        {
            case 36: // W1 lower floor to 8 above highest neighbour, fast
                line.SpecialUsedUp = true;
                foreach (var s in TaggedSectors(line.Tag))
                {
                    double dest = HighestNeighbourFloor(s);
                    if (dest != s.FloorHeight) dest += 8;
                    StartMover(new FloorMover { Sector = s, Destination = dest, Speed = 4 });
                }
                break;
            case 10: // W1 lift
            case 88: // WR lift
                if (line.Special == 10) line.SpecialUsedUp = true;
                foreach (var s in TaggedSectors(line.Tag))
                    StartMover(new Lift { Sector = s, Low = LowestNeighbourFloor(s), High = s.FloorHeight });
                break;
            case 52: // W1 exit
                ExitLevel();
                break;
            default:
                // Door/switch specials are handled by UseLines; anything else we simply don't support.
                if (!IsDoorSpecial(line.Special) && !IsExitSpecial(line.Special) && line.Special != 48 &&
                    _reportedSpecials.Add(line.Special))
                    Console.WriteLine($"Unsupported walk special {line.Special} on line {line.Index}");
                break;
        }
    }

    private IEnumerable<Sector> TaggedSectors(int tag) =>
        Level.Sectors.Where(s => s.Tag == tag && tag != 0 && s.ActiveMover == null).ToList();

    private void ExitLevel()
    {
        if (State != GameState.Playing) return;
        State = GameState.Exited;
        ShowMessage("Level complete.");
    }

    // ---------------------------------------------------------------- noise

    /// <summary>P_NoiseAlert: gunfire floods outward through every open two-sided line. It passes through
    /// at most one sound-blocking line. Every sector reached remembers who made the noise.</summary>
    public void NoiseAlert(Mobj emitter)
    {
        _validCount++;
        RecursiveSound(Level.SectorAt(emitter.Pos), 0, emitter);
    }

    private readonly Dictionary<Sector, int> _soundValid = new();

    private void RecursiveSound(Sector sec, int soundBlocks, Mobj emitter)
    {
        if (_soundValid.TryGetValue(sec, out int v) && v == _validCount && sec.SoundTraversed <= soundBlocks + 1)
            return;
        _soundValid[sec] = _validCount;
        sec.SoundTraversed = soundBlocks + 1;
        sec.SoundTarget = emitter;
        SectorNoiseTick[sec.Index] = Tick;

        foreach (var l in sec.Lines)
        {
            if (!l.TwoSided) continue;
            if (LineOpening(l).Range <= 0) continue; // closed door
            var other = l.FrontSector == sec ? l.BackSector! : l.FrontSector!;
            if ((l.Flags & LineFlags.SoundBlock) != 0)
            {
                if (soundBlocks == 0) RecursiveSound(other, 1, emitter);
            }
            else RecursiveSound(other, soundBlocks, emitter);
        }
    }
}
