namespace EssenceOfDoom;

/// <summary>Collision, movement, line of sight and hitscan. Brute force over all lines: E1M1 has 475 of them.</summary>
public sealed partial class Game
{
    public const double MaxStepUp = 24;

    public readonly struct Opening
    {
        public readonly double Top, Bottom, LowFloor;
        public Opening(double top, double bottom, double lowFloor) { Top = top; Bottom = bottom; LowFloor = lowFloor; }
        public double Range => Top - Bottom;
    }

    public static Opening LineOpening(Line l)
    {
        var f = l.FrontSector!;
        var b = l.BackSector!;
        return new Opening(Math.Min(f.CeilingHeight, b.CeilingHeight), Math.Max(f.FloorHeight, b.FloorHeight),
            Math.Min(f.FloorHeight, b.FloorHeight));
    }

    public static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double lenSq = ab.Dot(ab);
        double t = lenSq == 0 ? 0 : Math.Clamp((p - a).Dot(ab) / lenSq, 0, 1);
        return (p - (a + ab * t)).Length;
    }

    /// <summary>Segment/segment intersection; returns fraction along p1→p2, or -1 if they don't cross.</summary>
    public static double SegmentIntersect(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
    {
        var r = p2 - p1;
        var s = q2 - q1;
        double denom = r.X * s.Y - r.Y * s.X;
        if (Math.Abs(denom) < 1e-9) return -1;
        var qp = q1 - p1;
        double t = (qp.X * s.Y - qp.Y * s.X) / denom;
        double u = (qp.X * r.Y - qp.Y * r.X) / denom;
        return t >= 0 && t <= 1 && u >= 0 && u <= 1 ? t : -1;
    }

    private static bool OutsideBox(Line l, Vec2 p, double r) =>
        p.X + r < Math.Min(l.V1.X, l.V2.X) || p.X - r > Math.Max(l.V1.X, l.V2.X) ||
        p.Y + r < Math.Min(l.V1.Y, l.V2.Y) || p.Y - r > Math.Max(l.V1.Y, l.V2.Y);

    public sealed class PositionCheck
    {
        public double FloorZ, CeilingZ, DropoffZ;
        public Line? BlockingLine, FloorLine, CeilingLine;
        public Mobj? BlockingThing;
        public readonly List<Line> SpecialLines = new(); // blocking lines with specials, for monsters opening doors
    }

    /// <summary>Like DOOM's P_CheckPosition: can the thing stand here, and what floor/ceiling does it see?</summary>
    public bool CheckPosition(Mobj mo, Vec2 pos, PositionCheck c)
    {
        var sec = Level.SectorAt(pos);
        c.FloorZ = c.DropoffZ = sec.FloorHeight;
        c.CeilingZ = sec.CeilingHeight;
        c.BlockingLine = c.FloorLine = c.CeilingLine = null;
        c.BlockingThing = null;
        c.SpecialLines.Clear();

        foreach (var other in Mobjs)
        {
            if (other == mo || !other.Solid || other.Removed) continue;
            if ((other.Pos - pos).Length < other.Radius + mo.Radius)
            {
                c.BlockingThing = other;
                return false;
            }
        }

        bool ok = true;
        foreach (var l in Level.LinesNear(pos, mo.Radius))
        {
            if (OutsideBox(l, pos, mo.Radius)) continue;
            if (DistanceToSegment(pos, l.V1, l.V2) >= mo.Radius) continue;

            bool blocks = !l.TwoSided
                || (l.Flags & LineFlags.Blocking) != 0
                || (mo.IsMonster && (l.Flags & LineFlags.BlockMonsters) != 0);
            if (blocks)
            {
                c.BlockingLine ??= l;
                if (l.Special != 0) c.SpecialLines.Add(l);
                ok = false;
                continue;
            }
            var o = LineOpening(l);
            if (o.Top < c.CeilingZ) { c.CeilingZ = o.Top; c.CeilingLine = l; }
            if (o.Bottom > c.FloorZ) { c.FloorZ = o.Bottom; c.FloorLine = l; }
            if (o.LowFloor < c.DropoffZ) c.DropoffZ = o.LowFloor;
            if (l.Special != 0) c.SpecialLines.Add(l);
        }
        return ok;
    }

    private readonly PositionCheck _check = new();
    private Line? _blockLine;   // what stopped the last failed TryMove, for sliding
    private Mobj? _blockThing;

    /// <summary>DOOM's P_TryMove height rules on top of CheckPosition. Commits the move if allowed.</summary>
    public bool TryMove(Mobj mo, Vec2 pos, bool commit = true)
    {
        var c = _check;
        _blockLine = null;
        _blockThing = null;
        if (!CheckPosition(mo, pos, c))
        {
            _blockLine = c.BlockingLine;
            _blockThing = c.BlockingThing;
            return false;
        }
        _blockLine = c.CeilingLine ?? c.FloorLine;
        if (c.CeilingZ - c.FloorZ < mo.Height) return false;        // doesn't fit
        if (c.CeilingZ - mo.Z < mo.Height) return false;            // would have to duck (closing door)
        _blockLine = c.FloorLine;
        if (c.FloorZ - mo.Z > MaxStepUp) return false;              // step too tall
        if (mo.IsMonster && c.FloorZ - c.DropoffZ > MaxStepUp) return false; // monsters don't jump off ledges
        _blockLine = null;
        if (!commit) return true;

        var old = mo.Pos;
        mo.Pos = pos;
        mo.Z = c.FloorZ;
        CheckCrossedLines(mo, old, pos);
        return true;
    }

    /// <summary>Highest floor the thing's footprint touches, which is where it stands.</summary>
    public double FloorAt(Mobj mo)
    {
        double floor = Level.SectorAt(mo.Pos).FloorHeight;
        foreach (var l in Level.LinesNear(mo.Pos, mo.Radius))
        {
            if (!l.TwoSided || OutsideBox(l, mo.Pos, mo.Radius)) continue;
            if (DistanceToSegment(mo.Pos, l.V1, l.V2) >= mo.Radius) continue;
            floor = Math.Max(floor, LineOpening(l).Bottom);
        }
        return floor;
    }

    /// <summary>Player movement: move as far as possible, then slide along whatever blocked us.</summary>
    public void SlideMove(Mobj mo, Vec2 move)
    {
        // Split big moves so fast things can't tunnel through thin walls (DOOM uses MAXMOVE/2 the same way).
        int steps = (int)Math.Ceiling(move.Length / (mo.Radius / 2));
        var step = move * (1.0 / Math.Max(1, steps));
        for (int i = 0; i < steps; i++)
            if (!SlideStep(mo, step)) break;
    }

    private bool SlideStep(Mobj mo, Vec2 step)
    {
        if (TryMove(mo, mo.Pos + step)) return true;

        // Get as close as we can to the obstacle first, so we don't stop a few units short of walls.
        double lo = 0, hi = 1;
        for (int i = 0; i < 5; i++)
        {
            double mid = (lo + hi) / 2;
            if (TryMove(mo, mo.Pos + step * mid, commit: false)) lo = mid; else hi = mid;
        }
        if (lo > 0) TryMove(mo, mo.Pos + step * lo);
        var remaining = step * (1 - lo);

        // Slide along whatever blocked us: keep only the component parallel to it.
        TryMove(mo, mo.Pos + remaining, commit: false);
        Vec2? tangent = null, normal = null;
        if (_blockLine != null)
        {
            var d = _blockLine.Delta * (1.0 / _blockLine.Delta.Length);
            var n = new Vec2(-d.Y, d.X);
            if (n.Dot(mo.Pos - _blockLine.V1) < 0) n = n * -1;
            tangent = d; normal = n;
        }
        else if (_blockThing != null)
        {
            var n = mo.Pos - _blockThing.Pos;
            n = n * (1.0 / Math.Max(0.001, n.Length));
            tangent = new Vec2(-n.Y, n.X); normal = n;
        }
        if (tangent is Vec2 tg && normal is Vec2 nm)
        {
            // Nudge slightly away from the obstacle so rounding never leaves us overlapping it.
            if (TryMove(mo, mo.Pos + tg * remaining.Dot(tg) + nm * 0.01)) return true;
        }
        // Corners and things: fall back to trying each axis on its own.
        if (Math.Abs(remaining.X) > Math.Abs(remaining.Y))
            return TryMove(mo, mo.Pos + new Vec2(remaining.X, 0)) || TryMove(mo, mo.Pos + new Vec2(0, remaining.Y));
        return TryMove(mo, mo.Pos + new Vec2(0, remaining.Y)) || TryMove(mo, mo.Pos + new Vec2(remaining.X, 0));
    }

    // ---------------------------------------------------------------- sight

    /// <summary>DOOM's P_CheckSight idea: walk the 2D line between eyes and target, narrowing the visible
    /// vertical slope window through every two-sided line crossed. Closed doors and walls block.</summary>
    public bool CheckSight(Mobj from, Mobj to) =>
        CheckSightLine(from.Pos, from.Z + from.Height * 0.75, to.Pos, to.Z, to.Z + to.Height);

    public bool CheckSightLine(Vec2 a, double eyeZ, Vec2 b, double bottomZ, double topZ)
    {
        double dist = (b - a).Length;
        if (dist < 1) return true;
        double topSlope = (topZ - eyeZ) / dist;
        double bottomSlope = (bottomZ - eyeZ) / dist;

        double minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
        double minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
        foreach (var l in Level.LinesAlong(a, b))
        {
            if (Math.Max(l.V1.X, l.V2.X) < minX || Math.Min(l.V1.X, l.V2.X) > maxX ||
                Math.Max(l.V1.Y, l.V2.Y) < minY || Math.Min(l.V1.Y, l.V2.Y) > maxY) continue;
            double t = SegmentIntersect(a, b, l.V1, l.V2);
            if (t < 0) continue;
            if (!l.TwoSided) return false;
            var o = LineOpening(l);
            if (o.Range <= 0) return false;
            double d = Math.Max(t * dist, 0.001);
            if (l.FrontSector!.FloorHeight != l.BackSector!.FloorHeight)
                bottomSlope = Math.Max(bottomSlope, (o.Bottom - eyeZ) / d);
            if (l.FrontSector.CeilingHeight != l.BackSector.CeilingHeight)
                topSlope = Math.Min(topSlope, (o.Top - eyeZ) / d);
            if (topSlope <= bottomSlope) return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- attacks

    /// <summary>Hitscan. Hits the nearest shootable thing on the ray that the shooter can actually see
    /// (standing in for DOOM's vertical autoaim), otherwise puffs on the first wall.</summary>
    public void LineAttack(Mobj shooter, double angle, double range, int damage)
    {
        var start = shooter.Pos;
        var dir = Vec2.FromAngle(angle);
        double eyeZ = shooter.Z + shooter.Height / 2 + 8;

        Mobj? best = null;
        double bestDist = double.MaxValue;
        foreach (var t in Mobjs)
        {
            if (t == shooter || !t.Shootable || t.IsDead || t.Removed) continue;
            var rel = t.Pos - start;
            double along = rel.Dot(dir);
            if (along < 0) continue;
            double perp = Math.Abs(rel.X * dir.Y - rel.Y * dir.X);
            if (perp > t.Radius) continue;
            double entry = along - Math.Sqrt(t.Radius * t.Radius - perp * perp);
            if (entry > range || entry >= bestDist) continue;
            var hitPoint = start + dir * Math.Max(0, entry);
            if (!CheckSightLine(start, eyeZ, hitPoint, t.Z, t.Z + t.Height)) continue;
            best = t;
            bestDist = Math.Max(0, entry);
        }

        Vec2 end;
        if (best != null)
        {
            end = start + dir * bestDist;
            Effects.Add(new Effect { Kind = EffectKind.Blood, A = end, StartTick = Tick, Duration = 8 });
            DamageMobj(best, shooter, shooter, damage);
        }
        else
        {
            end = start + dir * WallHitDistance(start, dir, range, eyeZ);
            if (range > 64)
                Effects.Add(new Effect { Kind = EffectKind.Puff, A = end, StartTick = Tick, Duration = 8 });
        }
        if (range > 64)
            Effects.Add(new Effect { Kind = EffectKind.Tracer, A = start, B = end, StartTick = Tick, Duration = 5,
                FromMonster = shooter.IsMonster });
    }

    private double WallHitDistance(Vec2 start, Vec2 dir, double range, double z)
    {
        var end = start + dir * range;
        double nearest = range;
        foreach (var l in Level.LinesAlong(start, end))
        {
            double t = SegmentIntersect(start, end, l.V1, l.V2);
            if (t < 0 || t * range >= nearest) continue;
            if (l.TwoSided)
            {
                var o = LineOpening(l);
                if (o.Range > 0 && z > o.Bottom && z < o.Top) continue;
            }
            nearest = t * range;
        }
        return Math.Max(0, nearest - 2);
    }

    /// <summary>Barrel explosions: damage falls off linearly to zero at 128 units, blocked by walls.</summary>
    public void RadiusAttack(Mobj spot, Mobj? source, int damage)
    {
        foreach (var t in Mobjs.ToArray())
        {
            if (!t.Shootable || t.IsDead || t == spot) continue;
            double dist = Math.Max(0, (t.Pos - spot.Pos).Length - t.Radius);
            if (dist >= damage) continue;
            if (!CheckSight(t, spot)) continue;
            DamageMobj(t, spot, source, (int)(damage - dist));
        }
    }

    public static double AngleTo(Vec2 from, Vec2 to) => Math.Atan2(to.Y - from.Y, to.X - from.X);
}
