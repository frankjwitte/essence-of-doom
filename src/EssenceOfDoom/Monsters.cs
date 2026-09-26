namespace EssenceOfDoom;

/// <summary>Monster behaviour, following the shape of DOOM's A_Look / A_Chase / P_NewChaseDir.
/// Movement is spread smoothly over the tics between chase decisions instead of DOOM's jumps.</summary>
public sealed partial class Game
{
    private void MonsterThink(Mobj m)
    {
        switch (m.State)
        {
            case AiState.Dead:
                return;

            case AiState.Dormant:
                if (--m.LookTimer > 0) return;
                m.LookTimer = 10; // A_Look runs on a 10-tic stand frame
                Look(m);
                return;

            case AiState.Pain:
                if (--m.StateTics <= 0) m.State = AiState.Chase;
                return;

            case AiState.Attack:
                int elapsed = m.Info!.AttackTotal - m.StateTics;
                if (!m.AttackFired && elapsed >= m.Info.AttackWindup)
                {
                    m.AttackFired = true;
                    PerformAttack(m);
                }
                else if (m.Target != null) FaceTarget(m);
                if (--m.StateTics <= 0)
                {
                    m.State = AiState.Chase;
                    m.ChaseTimer = 0;
                }
                return;

            case AiState.Chase:
                if (--m.ChaseTimer <= 0)
                {
                    m.ChaseTimer = m.Info!.ChaseTics;
                    Chase(m);
                }
                if (m.State == AiState.Chase) StepTowardMoveDir(m);
                return;
        }
    }

    private void WakeUp(Mobj m)
    {
        m.State = AiState.Chase;
        m.ChaseTimer = 0;
        m.WakeTick = Tick;
    }

    /// <summary>A_Look: wake on noise heard in our sector, or on seeing the player in front of us.</summary>
    private void Look(Mobj m)
    {
        m.Threshold = 0;
        var heard = Level.SectorAt(m.Pos).SoundTarget;
        if (heard != null && heard.Shootable && !heard.IsDead)
        {
            m.Target = heard;
            // Ambush ("deaf") monsters ignore noise until they can also see the source.
            if (!m.Ambush || CheckSight(m, heard))
            {
                WakeUp(m);
                return;
            }
        }
        if (LookForPlayer(m, allAround: false)) WakeUp(m);
    }

    private bool LookForPlayer(Mobj m, bool allAround)
    {
        var pm = Player.Mo;
        if (pm.IsDead) return false;
        if (!CheckSight(m, pm)) return false;
        if (!allAround)
        {
            double diff = Math.Abs(NormalizeAngle(AngleTo(m.Pos, pm.Pos) - m.Angle));
            if (diff > Math.PI / 2 && (pm.Pos - m.Pos).Length > 64) return false; // behind us and not close
        }
        m.Target = pm;
        return true;
    }

    /// <summary>A_Chase: one decision — attack, keep walking, or pick a new direction.</summary>
    private void Chase(Mobj m)
    {
        var info = m.Info!;
        if (m.ReactionTime > 0) m.ReactionTime--;
        if (m.Threshold > 0)
        {
            if (m.Target == null || m.Target.IsDead) m.Threshold = 0;
            else m.Threshold--;
        }

        if (m.Target == null || m.Target.IsDead || !m.Target.Shootable)
        {
            // Target gone: look for the player all around, otherwise go back to sleep.
            if (!LookForPlayer(m, allAround: true))
            {
                m.Target = null;
                m.State = AiState.Dormant;
                m.MoveDir = Dir.None;
                return;
            }
        }

        if (m.JustAttacked)
        {
            m.JustAttacked = false;
            NewChaseDir(m);
            return;
        }

        if (info.HasMelee && CheckMeleeRange(m))
        {
            StartAttack(m, melee: true);
            return;
        }

        // Only consider shooting at the end of a movement run (DOOM's movecount check), which gives
        // monsters their walk-walk-shoot rhythm.
        if (info.HasMissile && m.MoveCount <= 0 && CheckMissileRange(m))
        {
            StartAttack(m, melee: false);
            m.JustAttacked = true;
            return;
        }

        if (--m.MoveCount < 0 || m.MoveDir == Dir.None) NewChaseDir(m);
    }

    private bool CheckMeleeRange(Mobj m)
    {
        var t = m.Target;
        if (t == null) return false;
        if ((t.Pos - m.Pos).Length >= 64 - 20 + t.Radius) return false;
        return CheckSight(m, t);
    }

    private bool CheckMissileRange(Mobj m)
    {
        var t = m.Target!;
        if (!CheckSight(m, t)) return false;
        if (m.JustHit)
        {
            m.JustHit = false; // just got hurt: always shoot back
            return true;
        }
        if (m.ReactionTime > 0) return false;
        double dist = (t.Pos - m.Pos).Length - 64;
        if (!m.Info!.HasMelee) dist -= 128; // no melee attack, so fire more
        dist = Math.Min(dist, 200);
        return Random255() >= dist;
    }

    private void StartAttack(Mobj m, bool melee)
    {
        m.State = AiState.Attack;
        m.StateTics = m.Info!.AttackTotal;
        m.AttackFired = false;
        m.AttackIsMelee = melee;
        FaceTarget(m);
    }

    private void FaceTarget(Mobj m)
    {
        if (m.Target != null) m.Angle = AngleTo(m.Pos, m.Target.Pos);
    }

    private void PerformAttack(Mobj m)
    {
        var info = m.Info!;
        var t = m.Target;
        if (t == null || t.IsDead) return;
        FaceTarget(m);

        if (info.HasMelee && CheckMeleeRange(m))
        {
            DamageMobj(t, m, m, (Random255() % info.MeleeDie + 1) * info.MeleeMult);
            return;
        }
        if (info.HitscanPellets > 0)
        {
            double spread = 22.4 * Math.PI / 180; // (P_Random()-P_Random())<<20
            for (int i = 0; i < info.HitscanPellets; i++)
            {
                double angle = m.Angle + (Random255() - Random255()) / 255.0 * spread;
                LineAttack(m, angle, 2048, (Random255() % 5 + 1) * 3);
            }
            return;
        }
        if (info.MissileSpeed > 0) SpawnMissile(m, t);
    }

    // ---------------------------------------------------------------- walking

    private void StepTowardMoveDir(Mobj m)
    {
        if (m.MoveDir == Dir.None) return;
        double step = m.Info!.Speed / m.Info.ChaseTics;
        var dest = m.Pos + new Vec2(Dir.X[m.MoveDir], Dir.Y[m.MoveDir]) * step;
        if (TryMove(m, dest))
        {
            m.Angle = Math.Atan2(Dir.Y[m.MoveDir], Dir.X[m.MoveDir]);
            return;
        }
        // Blocked. If a door is in the way, open it and wait here; otherwise rethink.
        if (TryOpenDoorsInWay(m)) return;
        m.MoveDir = Dir.None;
        if (m.Target != null) NewChaseDir(m);
    }

    private bool TryOpenDoorsInWay(Mobj m)
    {
        bool good = false;
        foreach (var l in _check.SpecialLines.ToArray())
            if (UseSpecialLine(m, l) || l.BackSector?.ActiveMover is Door) good = true;
        return good;
    }

    /// <summary>Could the monster take one full chase step in this direction?</summary>
    private bool TryWalk(Mobj m, int dir)
    {
        var dest = m.Pos + new Vec2(Dir.X[dir], Dir.Y[dir]) * m.Info!.Speed;
        if (!TryMove(m, dest, commit: false))
        {
            if (!TryOpenDoorsInWay(m)) return false;
        }
        m.MoveDir = dir;
        m.MoveCount = Random255() & 15;
        return true;
    }

    /// <summary>P_NewChaseDir: head roughly toward the target in 8 directions, never reversing unless forced.</summary>
    private void NewChaseDir(Mobj m)
    {
        var t = m.Target;
        if (t == null) { m.MoveDir = Dir.None; return; }

        int oldDir = m.MoveDir;
        int turnaround = Dir.Opposite(oldDir);
        double dx = t.Pos.X - m.Pos.X, dy = t.Pos.Y - m.Pos.Y;

        int d1 = dx > 10 ? Dir.East : dx < -10 ? Dir.West : Dir.None;
        int d2 = dy < -10 ? Dir.South : dy > 10 ? Dir.North : Dir.None;

        if (d1 != Dir.None && d2 != Dir.None)
        {
            int diag = (d1, d2) switch
            {
                (Dir.East, Dir.North) => Dir.NorthEast,
                (Dir.East, Dir.South) => Dir.SouthEast,
                (Dir.West, Dir.North) => Dir.NorthWest,
                _ => Dir.SouthWest,
            };
            if (diag != turnaround && TryWalk(m, diag)) return;
        }

        if (Random255() > 200 || Math.Abs(dy) > Math.Abs(dx)) (d1, d2) = (d2, d1);
        if (d1 == turnaround) d1 = Dir.None;
        if (d2 == turnaround) d2 = Dir.None;

        if (d1 != Dir.None && TryWalk(m, d1)) return;
        if (d2 != Dir.None && TryWalk(m, d2)) return;
        if (oldDir != Dir.None && TryWalk(m, oldDir)) return; // no direct path: keep going the old way

        if ((Random255() & 1) != 0)
        {
            for (int d = Dir.East; d <= Dir.SouthEast; d++)
                if (d != turnaround && TryWalk(m, d)) return;
        }
        else
        {
            for (int d = Dir.SouthEast; d >= Dir.East; d--)
                if (d != turnaround && TryWalk(m, d)) return;
        }
        if (turnaround != Dir.None && TryWalk(m, turnaround)) return;
        m.MoveDir = Dir.None; // can't move at all
    }

    // ---------------------------------------------------------------- projectiles

    private void SpawnMissile(Mobj source, Mobj target)
    {
        var info = source.Info!;
        double angle = AngleTo(source.Pos, target.Pos);
        double dist = Math.Max(1, (target.Pos - source.Pos).Length);
        var p = new Projectile
        {
            Pos = source.Pos,
            PrevPos = source.Pos,
            Z = source.Z + 32,
            Vel = Vec2.FromAngle(angle) * info.MissileSpeed,
            VelZ = (target.Z - source.Z) / (dist / info.MissileSpeed),
            Source = source,
            DamageMult = info.MissileMult,
        };
        Projectiles.Add(p);
        MoveProjectile(p, 0.5); // like P_CheckMissileSpawn: nudge forward, explode at once if inside a wall
    }

    private void UpdateProjectiles()
    {
        foreach (var p in Projectiles)
            if (!p.Dead) MoveProjectile(p, 1);
        Projectiles.RemoveAll(p => p.Dead);
    }

    private void MoveProjectile(Projectile p, double fraction)
    {
        int steps = (int)Math.Ceiling(p.Vel.Length * fraction / 4);
        var step = p.Vel * (fraction / Math.Max(1, steps));
        double stepZ = p.VelZ * fraction / Math.Max(1, steps);
        for (int i = 0; i < steps && !p.Dead; i++)
        {
            var next = p.Pos + step;
            p.Z += stepZ;
            if (ProjectileBlocked(p, next)) { ExplodeProjectile(p); return; }
            p.Pos = next;
        }
    }

    private bool ProjectileBlocked(Projectile p, Vec2 pos)
    {
        foreach (var t in Mobjs)
        {
            if (t == p.Source || t.Removed || (!t.Solid && !t.Shootable)) continue;
            if ((t.Pos - pos).Length >= t.Radius + p.Radius) continue;
            if (p.Z > t.Z + t.Height || p.Z + p.Height < t.Z) continue; // flies over/under
            if (t.Shootable)
            {
                // Imps don't hurt imps: same-species projectiles just splash.
                bool sameSpecies = t.IsMonster && p.Source.IsMonster && t.Type == p.Source.Type;
                if (!sameSpecies) DamageMobj(t, p.Source, p.Source, (Random255() % 8 + 1) * p.DamageMult);
            }
            return true;
        }

        var sec = Level.SectorAt(pos);
        if (p.Z < sec.FloorHeight || p.Z + p.Height > sec.CeilingHeight) return true;
        foreach (var l in Level.LinesNear(pos, p.Radius))
        {
            if (OutsideBox(l, pos, p.Radius)) continue;
            if (DistanceToSegment(pos, l.V1, l.V2) >= p.Radius) continue;
            if (!l.TwoSided) return true;
            var o = LineOpening(l);
            if (o.Bottom > p.Z || o.Top < p.Z + p.Height) return true;
        }
        return false;
    }

    private void ExplodeProjectile(Projectile p)
    {
        p.Dead = true;
        Effects.Add(new Effect { Kind = EffectKind.Puff, A = p.Pos, StartTick = Tick, Duration = 10, FromMonster = true });
    }

    public static double NormalizeAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }
}
