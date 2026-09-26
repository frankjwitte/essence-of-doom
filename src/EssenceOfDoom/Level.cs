using System.Buffers.Binary;

namespace EssenceOfDoom;

public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double Dot(Vec2 o) => X * o.X + Y * o.Y;
    public static Vec2 FromAngle(double a) => new(Math.Cos(a), Math.Sin(a));
}

public sealed class Sector
{
    public int Index;
    public double FloorHeight, CeilingHeight;
    public int LightLevel, Special, Tag;
    public List<Line> Lines = new();

    // Runtime state.
    public object? ActiveMover;          // door/lift currently moving this sector
    public int SoundTraversed;           // noise propagation bookkeeping
    public Mobj? SoundTarget;            // who made the last noise heard here
    public bool SecretFound;
}

public sealed class Side
{
    public Sector Sector = null!;
}

public static class LineFlags
{
    public const int Blocking = 1, BlockMonsters = 2, TwoSided = 4, Secret = 32, SoundBlock = 64, DontDraw = 128;
}

public sealed class Line
{
    public int Index;
    public Vec2 V1, V2;
    public int Flags, Special, Tag;
    public Side? Front, Back;
    public Sector? FrontSector => Front?.Sector;
    public Sector? BackSector => Back?.Sector;
    public bool TwoSided => Back != null;
    public Vec2 Delta => V2 - V1;
    public bool SpecialUsedUp; // for once-only (W1/S1) triggers
    public bool SwitchOn;      // purely visual: switch has been pressed

    /// <summary>0 = front (right of V1→V2), 1 = back. Same convention as DOOM's P_PointOnLineSide.</summary>
    public int PointSide(Vec2 p)
    {
        var d = Delta;
        double left = d.Y * (p.X - V1.X);
        double right = (p.Y - V1.Y) * d.X;
        return right < left ? 0 : 1;
    }
}

public sealed class MapThing
{
    public double X, Y, AngleDeg;
    public int Type, Flags;
}

public sealed class Level
{
    public string Name = "";
    public List<Vec2> Vertices = new();
    public List<Line> Lines = new();
    public List<Side> Sides = new();
    public List<Sector> Sectors = new();
    public List<MapThing> Things = new();

    // BSP data, used only for point-in-sector lookups.
    private readonly List<(double x, double y, double dx, double dy, int right, int left)> _nodes = new();
    private readonly List<Sector> _subsectorSector = new();

    public double MinX, MinY, MaxX, MaxY;

    public static Level Load(Wad wad, string mapName)
    {
        var lv = new Level { Name = mapName.ToUpperInvariant() };

        var vx = wad.ReadMapLump(mapName, "VERTEXES");
        for (int i = 0; i + 4 <= vx.Length; i += 4)
            lv.Vertices.Add(new Vec2(S16(vx, i), S16(vx, i + 2)));

        var sec = wad.ReadMapLump(mapName, "SECTORS");
        for (int i = 0, n = 0; i + 26 <= sec.Length; i += 26, n++)
            lv.Sectors.Add(new Sector
            {
                Index = n,
                FloorHeight = S16(sec, i),
                CeilingHeight = S16(sec, i + 2),
                LightLevel = S16(sec, i + 20),
                Special = S16(sec, i + 22),
                Tag = S16(sec, i + 24),
            });

        var sd = wad.ReadMapLump(mapName, "SIDEDEFS");
        for (int i = 0; i + 30 <= sd.Length; i += 30)
            lv.Sides.Add(new Side { Sector = lv.Sectors[U16(sd, i + 28)] });

        var ld = wad.ReadMapLump(mapName, "LINEDEFS");
        for (int i = 0, n = 0; i + 14 <= ld.Length; i += 14, n++)
        {
            int front = U16(ld, i + 10), back = U16(ld, i + 12);
            var line = new Line
            {
                Index = n,
                V1 = lv.Vertices[U16(ld, i)],
                V2 = lv.Vertices[U16(ld, i + 2)],
                Flags = S16(ld, i + 4),
                Special = S16(ld, i + 6),
                Tag = S16(ld, i + 8),
                Front = front == 0xFFFF ? null : lv.Sides[front],
                Back = back == 0xFFFF ? null : lv.Sides[back],
            };
            lv.Lines.Add(line);
            line.FrontSector?.Lines.Add(line);
            if (line.BackSector != null && line.BackSector != line.FrontSector) line.BackSector.Lines.Add(line);
        }

        var th = wad.ReadMapLump(mapName, "THINGS");
        for (int i = 0; i + 10 <= th.Length; i += 10)
            lv.Things.Add(new MapThing
            {
                X = S16(th, i), Y = S16(th, i + 2), AngleDeg = S16(th, i + 4),
                Type = S16(th, i + 6), Flags = S16(th, i + 8),
            });

        // Subsector → sector goes through the subsector's first seg: seg → linedef → side → sector.
        var segs = wad.ReadMapLump(mapName, "SEGS");
        var ss = wad.ReadMapLump(mapName, "SSECTORS");
        for (int i = 0; i + 4 <= ss.Length; i += 4)
        {
            int firstSeg = U16(ss, i + 2);
            int p = firstSeg * 12;
            var line = lv.Lines[U16(segs, p + 6)];
            int dir = S16(segs, p + 8);
            lv._subsectorSector.Add(dir == 0 ? line.FrontSector! : line.BackSector!);
        }

        var nodes = wad.ReadMapLump(mapName, "NODES");
        for (int i = 0; i + 28 <= nodes.Length; i += 28)
            lv._nodes.Add((S16(nodes, i), S16(nodes, i + 2), S16(nodes, i + 4), S16(nodes, i + 6),
                U16(nodes, i + 24), U16(nodes, i + 26)));

        lv.MinX = lv.Vertices.Min(v => v.X); lv.MaxX = lv.Vertices.Max(v => v.X);
        lv.MinY = lv.Vertices.Min(v => v.Y); lv.MaxY = lv.Vertices.Max(v => v.Y);
        lv.BuildBlocks();
        return lv;
    }

    // ---------------------------------------------------------------- block grid
    // Same idea as DOOM's BLOCKMAP: 128-unit cells listing the lines that pass near them,
    // so collision and sight only look at nearby lines instead of all of them.

    private const double BlockSize = 128;
    private int _blocksW, _blocksH;
    private List<Line>[] _blocks = Array.Empty<List<Line>>();
    private int[] _lineStamp = Array.Empty<int>();
    private int _stamp;

    private void BuildBlocks()
    {
        _blocksW = (int)((MaxX - MinX) / BlockSize) + 1;
        _blocksH = (int)((MaxY - MinY) / BlockSize) + 1;
        _blocks = new List<Line>[_blocksW * _blocksH];
        for (int i = 0; i < _blocks.Length; i++) _blocks[i] = new List<Line>();
        _lineStamp = new int[Lines.Count];
        foreach (var l in Lines)
        {
            ForCellsOnSegment(l.V1, l.V2, cell => _blocks[cell].Add(l));
        }
    }

    private int CellX(double x) => Math.Clamp((int)((x - MinX) / BlockSize), 0, _blocksW - 1);
    private int CellY(double y) => Math.Clamp((int)((y - MinY) / BlockSize), 0, _blocksH - 1);

    private void ForCellsOnSegment(Vec2 a, Vec2 b, Action<int> visit)
    {
        int x0 = CellX(Math.Min(a.X, b.X)), x1 = CellX(Math.Max(a.X, b.X));
        int y0 = CellY(Math.Min(a.Y, b.Y)), y1 = CellY(Math.Max(a.Y, b.Y));
        var d = b - a;
        for (int cy = y0; cy <= y1; cy++)
        for (int cx = x0; cx <= x1; cx++)
        {
            // Skip cells the segment's line passes entirely to one side of (all corners on the same side).
            double left = MinX + cx * BlockSize - 1, right = left + BlockSize + 2;
            double bottom = MinY + cy * BlockSize - 1, top = bottom + BlockSize + 2;
            int s = Side(a, d, left, bottom) + Side(a, d, right, bottom) + Side(a, d, left, top) + Side(a, d, right, top);
            if (s == 4 || s == -4) continue;
            visit(cy * _blocksW + cx);
        }
    }

    private static int Side(Vec2 a, Vec2 d, double px, double py) => Math.Sign(d.X * (py - a.Y) - d.Y * (px - a.X));

    /// <summary>Lines that might touch the given box.</summary>
    public List<Line> LinesInBox(double minX, double minY, double maxX, double maxY)
    {
        var result = new List<Line>();
        _stamp++;
        int x0 = CellX(minX), x1 = CellX(maxX), y0 = CellY(minY), y1 = CellY(maxY);
        for (int cy = y0; cy <= y1; cy++)
        for (int cx = x0; cx <= x1; cx++)
            foreach (var l in _blocks[cy * _blocksW + cx])
            {
                if (_lineStamp[l.Index] == _stamp) continue;
                _lineStamp[l.Index] = _stamp;
                result.Add(l);
            }
        return result;
    }

    public List<Line> LinesNear(Vec2 p, double radius) => LinesInBox(p.X - radius, p.Y - radius, p.X + radius, p.Y + radius);

    /// <summary>Lines that might cross the segment a→b.</summary>
    public List<Line> LinesAlong(Vec2 a, Vec2 b)
    {
        var result = new List<Line>();
        _stamp++;
        ForCellsOnSegment(a, b, cell =>
        {
            foreach (var l in _blocks[cell])
            {
                if (_lineStamp[l.Index] == _stamp) continue;
                _lineStamp[l.Index] = _stamp;
                result.Add(l);
            }
        });
        return result;
    }

    /// <summary>Walks the BSP tree like R_PointInSubsector. Always returns some sector, even outside the map.</summary>
    public Sector SectorAt(Vec2 p)
    {
        if (_nodes.Count == 0) return _subsectorSector[0];
        int n = _nodes.Count - 1;
        while (true)
        {
            var node = _nodes[n];
            double left = node.dy * (p.X - node.x);
            double right = (p.Y - node.y) * node.dx;
            int child = right < left ? node.right : node.left;
            if ((child & 0x8000) != 0) return _subsectorSector[child & 0x7FFF];
            n = child;
        }
    }

    /// <summary>True if the point is genuinely inside the given sector (even-odd test over its lines).</summary>
    public static bool PointInSectorPolygon(Sector s, Vec2 p)
    {
        bool inside = false;
        foreach (var l in s.Lines)
        {
            if (l.FrontSector == l.BackSector) continue; // internal line, both sides same sector
            var a = l.V1; var b = l.V2;
            if ((a.Y > p.Y) != (b.Y > p.Y))
            {
                double x = a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                if (x > p.X) inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>Returns the sector containing p, or null if p is in the void outside all sectors.</summary>
    public Sector? SectorAtOrNull(Vec2 p)
    {
        var s = SectorAt(p);
        return PointInSectorPolygon(s, p) ? s : null;
    }

    public IEnumerable<Sector> Neighbours(Sector s)
    {
        foreach (var l in s.Lines)
        {
            if (!l.TwoSided) continue;
            var other = l.FrontSector == s ? l.BackSector! : l.FrontSector!;
            if (other != s) yield return other;
        }
    }

    private static short S16(ReadOnlySpan<byte> b, int i) => BinaryPrimitives.ReadInt16LittleEndian(b.Slice(i, 2));
    private static int U16(ReadOnlySpan<byte> b, int i) => BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(i, 2));
}
