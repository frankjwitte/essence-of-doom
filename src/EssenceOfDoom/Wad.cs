using System.Text;

namespace EssenceOfDoom;

/// <summary>Minimal reader for the WAD container format: a header, a directory, and named lumps.</summary>
public sealed class Wad
{
    public sealed record Lump(string Name, int Offset, int Size);

    public string Path { get; }
    public List<Lump> Lumps { get; } = new();
    private readonly byte[] _data;

    public Wad(string path)
    {
        Path = path;
        _data = File.ReadAllBytes(path);
        string id = Encoding.ASCII.GetString(_data, 0, 4);
        if (id != "IWAD" && id != "PWAD")
            throw new InvalidDataException($"{path} is not a WAD file");

        int count = BitConverter.ToInt32(_data, 4);
        int dirOffset = BitConverter.ToInt32(_data, 8);
        for (int i = 0; i < count; i++)
        {
            int p = dirOffset + i * 16;
            int offset = BitConverter.ToInt32(_data, p);
            int size = BitConverter.ToInt32(_data, p + 4);
            string name = Encoding.ASCII.GetString(_data, p + 8, 8).TrimEnd('\0').ToUpperInvariant();
            Lumps.Add(new Lump(name, offset, size));
        }
    }

    public int IndexOf(string name) => Lumps.FindIndex(l => l.Name == name.ToUpperInvariant());

    public ReadOnlySpan<byte> Read(int lumpIndex)
    {
        var l = Lumps[lumpIndex];
        return _data.AsSpan(l.Offset, l.Size);
    }

    /// <summary>Map lumps follow the map marker in a fixed order; look up by name within that block.</summary>
    public ReadOnlySpan<byte> ReadMapLump(string map, string lumpName)
    {
        int marker = IndexOf(map);
        if (marker < 0) throw new KeyNotFoundException($"Map {map} not found in {Path}");
        for (int i = marker + 1; i < Math.Min(marker + 12, Lumps.Count); i++)
            if (Lumps[i].Name == lumpName) return Read(i);
        throw new KeyNotFoundException($"Lump {lumpName} not found for {map}");
    }

    /// <summary>Finds DOOM1.WAD: explicit path, DOOMWAD env var, current/exe directory, repo root, then Downloads.</summary>
    public static string? Locate(string? explicitPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(explicitPath)) candidates.Add(explicitPath);
        var env = Environment.GetEnvironmentVariable("DOOMWAD");
        if (!string.IsNullOrEmpty(env)) candidates.Add(env);

        var dirs = new List<string> { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        // Walk up from the executable so running from bin/Debug still finds a WAD at the repo root.
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && d != null; i++, d = d.Parent) dirs.Add(d.FullName);
        dirs.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));

        foreach (var dir in dirs)
            candidates.Add(System.IO.Path.Combine(dir, "DOOM1.WAD"));

        foreach (var c in candidates)
            if (File.Exists(c)) return c; // Windows file lookups are case-insensitive, so Doom1.WAD matches.
        return null;
    }
}
