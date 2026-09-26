using System.Numerics;
using Raylib_cs;

namespace EssenceOfDoom;

/// <summary>Draws the level like an architectural drawing: pale floor fills, thin black walls,
/// and a handful of plain symbols for everything that moves.</summary>
public sealed class Renderer : IDisposable
{
    // Palette.
    static readonly Color Void = new(214, 214, 210, 255);
    static readonly Color Wall = new(20, 20, 20, 255);
    static readonly Color Step = new(125, 125, 125, 255);
    static readonly Color Ledge = new(70, 70, 70, 255);
    static readonly Color CeilingEdge = new(190, 190, 190, 255);
    static readonly Color DoorLine = new(150, 95, 35, 255);
    static readonly Color ExitColor = new(0, 150, 70, 255);
    static readonly Color MonsterAwake = new(210, 30, 30, 255);
    static readonly Color MonsterAttack = new(120, 0, 0, 255);
    static readonly Color MonsterDormant = new(95, 95, 95, 255);
    static readonly Color Ink = new(20, 20, 20, 255);

    const double FillScale = 4; // world units per fill-texture pixel

    readonly Game _g;
    readonly Level _lv;
    readonly int _texW, _texH;
    readonly int[] _pixelSector;          // sector index per fill pixel, -1 = outside the map
    readonly Color[] _pixels;
    readonly Color[] _sectorColor, _lastSectorColor;
    readonly bool[] _secretDoor;
    readonly ThingTypes.KeyColor[] _doorKey;
    readonly bool[] _isDoor;
    Texture2D _fill;
    readonly double _minFloor, _maxFloor;

    public double CamX, CamY, Zoom = 0.6;
    public bool Overview;
    public bool ShowHelp = true;
    int _sw, _sh;

    public Renderer(Game g)
    {
        _g = g;
        _lv = g.Level;
        int n = _lv.Sectors.Count;
        _sectorColor = new Color[n];
        _lastSectorColor = new Color[n];
        _secretDoor = new bool[n];
        _doorKey = new ThingTypes.KeyColor[n];
        _isDoor = new bool[n];
        foreach (var l in _lv.Lines)
        {
            if (!Game.IsDoorSpecial(l.Special) || l.BackSector == null) continue;
            int s = l.BackSector.Index;
            _isDoor[s] = true;
            if ((l.Flags & LineFlags.Secret) != 0) _secretDoor[s] = true;
            var key = Game.DoorKey(l.Special);
            if (key != ThingTypes.KeyColor.None) _doorKey[s] = key;
        }
        _minFloor = _lv.Sectors.Min(s => s.FloorHeight);
        _maxFloor = _lv.Sectors.Max(s => s.FloorHeight);

        // Rasterise sector membership once. Floor colours change at runtime (doors, lifts, noise),
        // but which sector a pixel belongs to never does.
        _texW = (int)Math.Ceiling((_lv.MaxX - _lv.MinX) / FillScale);
        _texH = (int)Math.Ceiling((_lv.MaxY - _lv.MinY) / FillScale);
        _pixelSector = new int[_texW * _texH];
        _pixels = new Color[_texW * _texH];
        Parallel.For(0, _texH, py =>
        {
            for (int px = 0; px < _texW; px++)
            {
                var p = new Vec2(_lv.MinX + (px + 0.5) * FillScale, _lv.MaxY - (py + 0.5) * FillScale);
                _pixelSector[py * _texW + px] = _lv.SectorAtOrNull(p)?.Index ?? -1;
            }
        });
        var img = Raylib.GenImageColor(_texW, _texH, Void);
        _fill = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_fill, TextureFilter.Bilinear);
    }

    public void Dispose() => Raylib.UnloadTexture(_fill);

    // ---------------------------------------------------------------- camera

    Vector2 S(Vec2 w) => new((float)((w.X - CamX) * Zoom + _sw / 2.0), (float)(-(w.Y - CamY) * Zoom + _sh / 2.0));

    public Vec2 ScreenToWorld(Vector2 s) => new((s.X - _sw / 2.0) / Zoom + CamX, -(s.Y - _sh / 2.0) / Zoom + CamY);

    float Px(double worldSize, float min) => Math.Max(min, (float)(worldSize * Zoom));

    static Vec2 Lerp(Vec2 a, Vec2 b, double t) => a + (b - a) * t;

    public void UpdateCamera(double alpha)
    {
        _sw = Raylib.GetScreenWidth();
        _sh = Raylib.GetScreenHeight();
        if (Overview)
        {
            CamX = (_lv.MinX + _lv.MaxX) / 2;
            CamY = (_lv.MinY + _lv.MaxY) / 2;
            Zoom = Math.Min((_sw - 40) / (_lv.MaxX - _lv.MinX), (_sh - 100) / (_lv.MaxY - _lv.MinY));
        }
        else
        {
            var p = Lerp(_g.Player.Mo.PrevPos, _g.Player.Mo.Pos, alpha);
            CamX = p.X;
            CamY = p.Y;
        }
    }

    // ---------------------------------------------------------------- frame

    public void Draw(double alpha, Vector2 mouse)
    {
        Raylib.ClearBackground(Void);
        DrawFill();
        DrawLines();
        DrawEffects();
        DrawThings(alpha);
        DrawProjectiles(alpha);
        DrawPlayer(alpha);
        DrawCrosshair(mouse);
        DrawHud();
    }

    void DrawFill()
    {
        bool changed = false;
        for (int i = 0; i < _lv.Sectors.Count; i++)
        {
            _sectorColor[i] = SectorColor(_lv.Sectors[i]);
            if (!_sectorColor[i].Equals(_lastSectorColor[i])) changed = true;
        }
        if (changed)
        {
            Array.Copy(_sectorColor, _lastSectorColor, _sectorColor.Length);
            for (int i = 0; i < _pixels.Length; i++)
            {
                int s = _pixelSector[i];
                _pixels[i] = s < 0 ? Void : _sectorColor[s];
            }
            Raylib.UpdateTexture(_fill, _pixels);
        }
        var tl = S(new Vec2(_lv.MinX, _lv.MaxY));
        var br = S(new Vec2(_lv.MinX + _texW * FillScale, _lv.MaxY - _texH * FillScale));
        Raylib.DrawTexturePro(_fill, new Rectangle(0, 0, _texW, _texH),
            new Rectangle(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y), Vector2.Zero, 0, Color.White);
    }

    Color SectorColor(Sector s)
    {
        int i = s.Index;
        bool closed = s.CeilingHeight - s.FloorHeight <= 0;
        if (closed && _secretDoor[i]) return Void;                // secret doors read as solid wall
        if (closed && _isDoor[i])
            return _doorKey[i] == ThingTypes.KeyColor.None ? new Color(205, 180, 140, 255) : KeyTint(_doorKey[i]);

        double t = _maxFloor > _minFloor ? (s.FloorHeight - _minFloor) / (_maxFloor - _minFloor) : 1;
        double r, gg, b;
        if (s.Special is 4 or 5 or 7 or 16) { r = 196; gg = 228; b = 188; }    // damaging floor: pale green
        else { r = gg = b = 234 + 21 * t; }                                   // lower floors slightly darker

        int age = _g.Tick - _g.SectorNoiseTick[i];
        if (age < 20)
        {
            // Gunfire flood: sectors that just heard the player flash pink.
            double k = (1 - age / 20.0) * 0.4;
            r += (255 - r) * k; gg += (190 - gg) * k; b += (190 - b) * k;
        }
        return new Color((int)r, (int)gg, (int)b, 255);
    }

    static Color KeyTint(ThingTypes.KeyColor k) => k switch
    {
        ThingTypes.KeyColor.Blue => new Color(150, 170, 235, 255),
        ThingTypes.KeyColor.Yellow => new Color(235, 215, 110, 255),
        _ => new Color(235, 140, 140, 255),
    };

    static Color KeyColor(ThingTypes.KeyColor k) => k switch
    {
        ThingTypes.KeyColor.Blue => new Color(40, 70, 220, 255),
        ThingTypes.KeyColor.Yellow => new Color(200, 160, 0, 255),
        _ => new Color(210, 30, 30, 255),
    };

    void DrawLines()
    {
        float w = Math.Clamp((float)(Zoom * 3), 1.2f, 2.5f);
        foreach (var l in _lv.Lines)
        {
            if ((l.Flags & LineFlags.DontDraw) != 0) continue;
            Color c;
            float thick = w;
            if (Game.IsExitSpecial(l.Special)) { c = ExitColor; thick = w * 2.2f; }
            else if (Game.IsDoorSpecial(l.Special) && (l.Flags & LineFlags.Secret) == 0)
            {
                var key = Game.DoorKey(l.Special);
                c = key == ThingTypes.KeyColor.None ? DoorLine : KeyColor(key);
                thick = w * 1.6f;
            }
            else if (!l.TwoSided || (l.Flags & LineFlags.Secret) != 0) c = Wall;
            else if ((l.Flags & LineFlags.Blocking) != 0) { c = Ledge; thick = w * 1.1f; } // windows, railings: impassable
            else
            {
                var f = l.FrontSector!;
                var b = l.BackSector!;
                double df = Math.Abs(f.FloorHeight - b.FloorHeight);
                if (df > Game.MaxStepUp) { c = Ledge; thick = Math.Max(1, w * 0.9f); }     // can only drop down
                else if (df > 0) { c = Step; thick = Math.Max(1, w * 0.5f); }             // walkable step
                else if (f.CeilingHeight != b.CeilingHeight) { c = CeilingEdge; thick = 1; }
                else continue; // same floor, same ceiling: nothing to see, like the automap
            }
            Raylib.DrawLineEx(S(l.V1), S(l.V2), thick, c);

            if (Game.IsExitSpecial(l.Special))
            {
                var mid = S((l.V1 + l.V2) * 0.5);
                Raylib.DrawText("EXIT", (int)mid.X + 6, (int)mid.Y - 6, 14, ExitColor);
            }
        }
    }

    void DrawEffects()
    {
        foreach (var e in _g.Effects)
        {
            double age = (_g.Tick - e.StartTick) / (double)Math.Max(1, e.Duration);
            int a = (int)(255 * Math.Clamp(1 - age, 0, 1));
            switch (e.Kind)
            {
                case EffectKind.Tracer:
                    var tc = e.FromMonster ? new Color(220, 40, 40, a / 2) : new Color(30, 30, 30, a / 3);
                    Raylib.DrawLineEx(S(e.A), S(e.B), 1, tc);
                    break;
                case EffectKind.Puff:
                    Raylib.DrawCircleV(S(e.A), Px(4, 2), e.FromMonster ? new Color(230, 110, 30, a) : new Color(90, 90, 90, a));
                    break;
                case EffectKind.Blood:
                    Raylib.DrawCircleV(S(e.A), Px(4, 2), new Color(200, 0, 0, a));
                    break;
                case EffectKind.Explosion:
                    Raylib.DrawCircleV(S(e.A), Px(128 * Math.Min(1, age * 2.5), 4), new Color(255, 140, 0, a / 3));
                    Raylib.DrawCircleLinesV(S(e.A), Px(128 * Math.Min(1, age * 2.5), 4), new Color(230, 90, 0, a));
                    break;
            }
        }
    }

    void DrawThings(double alpha)
    {
        // Corpses and inanimate things first, living monsters on top.
        foreach (var mo in _g.Mobjs)
        {
            var c = S(Lerp(mo.PrevPos, mo.Pos, alpha));
            switch (mo.Kind)
            {
                case MobjKind.Pickup: DrawPickup(mo, c); break;
                case MobjKind.Obstacle:
                    Raylib.DrawCircleV(c, Px(mo.Radius * 0.7, 2), new Color(170, 170, 170, 255));
                    break;
                case MobjKind.Barrel:
                    bool blink = mo.IsDead && (_g.Tick / 2) % 2 == 0;
                    Raylib.DrawCircleLinesV(c, Px(mo.Radius, 3), blink ? new Color(255, 120, 0, 255) : new Color(60, 130, 60, 255));
                    Raylib.DrawCircleV(c, Px(3, 1.5f), new Color(60, 130, 60, 255));
                    break;
                case MobjKind.Monster when mo.IsDead:
                    float r = Px(mo.Radius * 0.5, 3);
                    var gray = new Color(150, 150, 150, 255);
                    Raylib.DrawLineEx(c - new Vector2(r, r), c + new Vector2(r, r), 1.5f, gray);
                    Raylib.DrawLineEx(c - new Vector2(r, -r), c + new Vector2(r, -r), 1.5f, gray);
                    break;
            }
        }
        foreach (var mo in _g.Mobjs)
            if (mo.IsMonster && !mo.IsDead) DrawMonster(mo, S(Lerp(mo.PrevPos, mo.Pos, alpha)));
    }

    void DrawMonster(Mobj m, Vector2 c)
    {
        float r = Px(m.Radius, 5);
        bool dormant = m.State == AiState.Dormant;
        bool pain = m.State == AiState.Pain;
        Color fill = m.State switch
        {
            AiState.Attack => MonsterAttack,
            AiState.Pain => new Color(255, 255, 255, 255),
            _ => MonsterAwake,
        };

        // Wake-up pulse: an expanding ring so you notice monsters coming alive across the map.
        int wakeAge = _g.Tick - m.WakeTick;
        if (wakeAge < 30)
        {
            float rr = r + wakeAge * 1.6f;
            Raylib.DrawCircleLinesV(c, rr, new Color(220, 30, 30, (int)(255 * (1 - wakeAge / 30.0))));
            Raylib.DrawCircleLinesV(c, rr + 1, new Color(220, 30, 30, (int)(255 * (1 - wakeAge / 30.0))));
        }

        // Attack wind-up: a faint line toward the target so the telegraph is readable.
        if (m.State == AiState.Attack && !m.AttackFired && m.Target != null)
            Raylib.DrawLineEx(c, S(m.Target.Pos), 1, new Color(200, 0, 0, 70));

        switch (m.Info!.Shape)
        {
            case MonsterShape.Circle:
                if (!dormant) Raylib.DrawCircleV(c, r, fill);
                Raylib.DrawCircleLinesV(c, r, dormant ? MonsterDormant : MonsterAttack);
                break;
            case MonsterShape.Square:
            case MonsterShape.BigSquare:
                var rect = new Rectangle(c.X - r * 0.85f, c.Y - r * 0.85f, r * 1.7f, r * 1.7f);
                if (!dormant) Raylib.DrawRectangleRec(rect, fill);
                Raylib.DrawRectangleLinesEx(rect, 1.5f, dormant ? MonsterDormant : MonsterAttack);
                break;
            case MonsterShape.Diamond:
                if (!dormant) Raylib.DrawPoly(c, 4, r * 1.15f, 0, fill);
                Raylib.DrawPolyLinesEx(c, 4, r * 1.15f, 0, 1.5f, dormant ? MonsterDormant : MonsterAttack);
                break;
            case MonsterShape.Pentagon:
                if (!dormant) Raylib.DrawPoly(c, 5, r, -90, fill);
                Raylib.DrawPolyLinesEx(c, 5, r, -90, 1.5f, dormant ? MonsterDormant : MonsterAttack);
                break;
        }
        if (pain) Raylib.DrawCircleLinesV(c, r + 2, MonsterAwake);

        // Facing tick.
        var dir = new Vector2((float)Math.Cos(m.Angle), -(float)Math.Sin(m.Angle));
        Raylib.DrawLineEx(c + dir * r, c + dir * (r + Px(8, 4)), 1.5f, dormant ? MonsterDormant : MonsterAttack);
    }

    void DrawPickup(Mobj it, Vector2 c)
    {
        float s = Px(7, 3);
        switch (it.Type)
        {
            case 2011:
            case 2012:
                float hs = it.Type == 2012 ? s * 1.4f : s;
                var red = new Color(210, 30, 30, 255);
                Raylib.DrawRectangleV(c - new Vector2(hs, hs / 3), new Vector2(hs * 2, hs * 2 / 3), red);
                Raylib.DrawRectangleV(c - new Vector2(hs / 3, hs), new Vector2(hs * 2 / 3, hs * 2), red);
                break;
            case 2014: Raylib.DrawCircleV(c, Px(3, 2), new Color(60, 90, 230, 255)); break;
            case 2015: Raylib.DrawCircleV(c, Px(3, 2), new Color(40, 150, 60, 255)); break;
            case 2013: Raylib.DrawCircleV(c, Px(10, 5), new Color(60, 90, 230, 255)); break;
            case 2018: Raylib.DrawRing(c, s * 0.8f, s * 1.3f, 0, 360, 24, new Color(40, 150, 60, 255)); break;
            case 2019: Raylib.DrawRing(c, s * 0.8f, s * 1.3f, 0, 360, 24, new Color(40, 70, 220, 255)); break;
            case 2007:
            case 2048:
                float bw = it.Type == 2048 ? 1.6f : 1f;
                Raylib.DrawRectangleV(c - new Vector2(s * 0.5f * bw, s * 0.5f), new Vector2(s * bw, s), new Color(90, 90, 90, 255));
                break;
            case 2008:
            case 2049:
                float sw = it.Type == 2049 ? 1.6f : 1f;
                Raylib.DrawRectangleV(c - new Vector2(s * 0.5f * sw, s * 0.5f), new Vector2(s * sw, s), new Color(170, 90, 20, 255));
                break;
            case 2001:
            case 2002:
                string label = it.Type == 2001 ? "SG" : "CG";
                Raylib.DrawRectangleV(c - new Vector2(s * 1.5f, s * 0.3f), new Vector2(s * 3, s * 0.6f), Ink);
                Raylib.DrawText(label, (int)(c.X + s * 1.7f), (int)(c.Y - 6), 12, Ink);
                break;
            default:
                var key = ThingTypes.KeyColorOf(it.Type);
                if (key != ThingTypes.KeyColor.None)
                {
                    Raylib.DrawPoly(c, 4, s * 1.3f, 0, KeyColor(key));
                    Raylib.DrawPolyLinesEx(c, 4, s * 1.3f, 0, 1, Ink);
                }
                break;
        }
    }

    void DrawProjectiles(double alpha)
    {
        foreach (var p in _g.Projectiles)
            Raylib.DrawCircleV(S(Lerp(p.PrevPos, p.Pos, alpha)), Px(p.Radius, 3), new Color(240, 100, 0, 255));
    }

    void DrawPlayer(double alpha)
    {
        var mo = _g.Player.Mo;
        var c = S(Lerp(mo.PrevPos, mo.Pos, alpha));
        float r = Px(mo.Radius, 7);
        if (mo.IsDead)
        {
            Raylib.DrawLineEx(c - new Vector2(r, r), c + new Vector2(r, r), 3, Ink);
            Raylib.DrawLineEx(c - new Vector2(r, -r), c + new Vector2(r, -r), 3, Ink);
            return;
        }
        Vector2 At(double ang, float len) => c + new Vector2((float)Math.Cos(ang), -(float)Math.Sin(ang)) * len;
        var tip = At(mo.Angle, r * 1.35f);
        var left = At(mo.Angle + 2.5, r);
        var right = At(mo.Angle - 2.5, r);
        // Raylib wants counter-clockwise order in screen space.
        Raylib.DrawTriangle(tip, left, right, new Color(20, 60, 200, 255));
        Raylib.DrawTriangleLines(tip, left, right, Ink);
    }

    void DrawCrosshair(Vector2 m)
    {
        var c = new Color(20, 20, 20, 160);
        Raylib.DrawLineEx(m - new Vector2(8, 0), m - new Vector2(3, 0), 1.5f, c);
        Raylib.DrawLineEx(m + new Vector2(3, 0), m + new Vector2(8, 0), 1.5f, c);
        Raylib.DrawLineEx(m - new Vector2(0, 8), m - new Vector2(0, 3), 1.5f, c);
        Raylib.DrawLineEx(m + new Vector2(0, 3), m + new Vector2(0, 8), 1.5f, c);
    }

    // ---------------------------------------------------------------- HUD

    void DrawHud()
    {
        var p = _g.Player;
        var mo = p.Mo;

        if (p.DamageFlash > 0)
        {
            int a = (int)Math.Min(160, p.DamageFlash * 9);
            int t = 14;
            var c = new Color(220, 0, 0, a);
            Raylib.DrawRectangle(0, 0, _sw, t, c);
            Raylib.DrawRectangle(0, _sh - t, _sw, t, c);
            Raylib.DrawRectangle(0, 0, t, _sh, c);
            Raylib.DrawRectangle(_sw - t, 0, t, _sh, c);
        }

        // Status bar.
        int barH = 44;
        Raylib.DrawRectangle(0, _sh - barH, _sw, barH, new Color(250, 250, 248, 235));
        Raylib.DrawLine(0, _sh - barH, _sw, _sh - barH, Ink);
        int y = _sh - barH + 10;
        int x = 16;
        void Field(string label, string value, Color vc)
        {
            Raylib.DrawText(label, x, y + 6, 12, new Color(110, 110, 110, 255));
            x += Raylib.MeasureText(label, 12) + 6;
            Raylib.DrawText(value, x, y, 24, vc);
            x += Raylib.MeasureText(value, 24) + 26;
        }
        Field("HEALTH", $"{mo.Health}%", mo.Health <= 25 ? MonsterAwake : Ink);
        Field("ARMOR", $"{p.Armor}%", Ink);
        string ammo = p.Current switch
        {
            Weapon.Pistol or Weapon.Chaingun => p.Bullets.ToString(),
            Weapon.Shotgun => p.Shells.ToString(),
            _ => "-",
        };
        Field(p.Current.ToString().ToUpper(), ammo, Ink);
        Field("BULL", p.Bullets.ToString(), new Color(90, 90, 90, 255));
        Field("SHEL", p.Shells.ToString(), new Color(90, 90, 90, 255));
        string arms = string.Join(" ", Enum.GetValues<Weapon>().Select(w => p.Weapons.Contains(w) ? ((int)w).ToString() : "·"));
        Field("ARMS", arms, Ink);
        foreach (var k in p.Keys)
        {
            Raylib.DrawPoly(new Vector2(x + 8, y + 12), 4, 9, 0, KeyColor(k));
            x += 24;
        }

        // Top-left: the experiment's vital signs.
        int awake = _g.Mobjs.Count(m => m.IsMonster && !m.IsDead && m.State != AiState.Dormant);
        int alive = _g.Mobjs.Count(m => m.IsMonster && !m.IsDead);
        int secs = _g.Tick / Game.TicRate;
        Raylib.DrawText($"{_lv.Name}  skill {_g.Skill}   {secs / 60}:{secs % 60:00}", 14, 12, 18, Ink);
        Raylib.DrawText($"awake {awake} / alive {alive}    kills {p.Kills}/{_g.TotalKills}   items {p.Items}/{_g.TotalItems}   secrets {p.Secrets}/{_g.TotalSecrets}",
            14, 34, 16, new Color(80, 80, 80, 255));

        if (_g.Tick - _g.MessageTick < 3 * Game.TicRate && _g.State == GameState.Playing)
            Centered(_g.Message, 64, 20, Ink);

        if (ShowHelp)
        {
            string help = "WASD move   mouse aim   LMB/Ctrl fire   E/Space use   1-4 weapon   Shift walk   wheel zoom   Tab overview   R restart   H hide";
            int w = Raylib.MeasureText(help, 14);
            Raylib.DrawText(help, _sw - w - 14, _sh - barH - 22, 14, new Color(110, 110, 110, 255));
        }

        if (_g.State == GameState.Dead)
        {
            Panel(new[] { "YOU DIED", "", "press R to try again" });
        }
        else if (_g.State == GameState.Exited)
        {
            static string Pct(int a, int b) => b == 0 ? "100%" : $"{a * 100 / b}%";
            Panel(new[]
            {
                $"{_lv.Name} COMPLETE", "",
                $"kills    {Pct(p.Kills, _g.TotalKills)}",
                $"items    {Pct(p.Items, _g.TotalItems)}",
                $"secrets  {Pct(p.Secrets, _g.TotalSecrets)}",
                $"time     {secs / 60}:{secs % 60:00}", "",
                "press R to play again",
            });
        }
    }

    void Centered(string text, int y, int size, Color c)
    {
        int w = Raylib.MeasureText(text, size);
        Raylib.DrawText(text, (_sw - w) / 2, y, size, c);
    }

    void Panel(string[] lines)
    {
        int lineH = 30;
        int h = lines.Length * lineH + 40;
        int w = 420;
        int x = (_sw - w) / 2, y = (_sh - h) / 2;
        Raylib.DrawRectangle(x, y, w, h, new Color(255, 255, 255, 240));
        Raylib.DrawRectangleLinesEx(new Rectangle(x, y, w, h), 2, Ink);
        for (int i = 0; i < lines.Length; i++)
            Centered(lines[i], y + 20 + i * lineH, i == 0 ? 28 : 22, i == 0 ? MonsterAwake : Ink);
    }
}
